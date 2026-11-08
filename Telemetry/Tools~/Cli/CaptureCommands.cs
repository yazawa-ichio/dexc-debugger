using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Google.Protobuf;
using OpenTelemetry.Proto.Logs.V1;
using OpenTelemetry.Proto.Metrics.V1;
using OpenTelemetry.Proto.Trace.V1;

namespace Dexc.Telemetry.Cli
{
	/// <summary>
	/// The captures in a folder, found by their <c>dexc.capture</c> spans, and the cutting of one into files of its own in
	/// the same format: the spans of its trace or within its time, the metric points of its frames, the logs within it.
	/// </summary>
	internal static class CaptureCommands
	{
		sealed class Capture
		{
			internal string Run;
			internal Span Span;
		}

		static List<Capture> Find(string folder)
		{
			var captures = new List<Capture>();
			foreach (var file in TelemetryFiles.Find(folder, true).Where(file => TelemetryFiles.SignalOf(file) == "traces"))
			{
				foreach (var data in TelemetryFiles.ReadFrames(file).Select(TracesData.Parser.ParseFrom))
				{
					foreach (var resource in data.ResourceSpans)
					{
						var run = resource.Resource.Attributes.FirstOrDefault(attribute => attribute.Key == "service.instance.id")?.Value.StringValue;
						foreach (var span in resource.ScopeSpans.SelectMany(scope => scope.Spans).Where(span => span.Name == "dexc.capture"))
						{
							captures.Add(new Capture { Run = run, Span = span });
						}
					}
				}
			}
			return captures.OrderBy(capture => capture.Span.StartTimeUnixNano).ToList();
		}

		internal static int List(string folder)
		{
			var captures = Find(folder);
			for (var i = 0; i < captures.Count; i++)
			{
				var span = captures[i].Span;
				Console.WriteLine(
					(i + 1) + ": run " + captures[i].Run + ", " + TelemetryFiles.Time(span.StartTimeUnixNano).ToString("yyyy-MM-dd HH:mm:ss.fff") + ", "
					+ ((span.EndTimeUnixNano - span.StartTimeUnixNano) / 1e6).ToString("F1") + "ms " + DumpCommand.Attributes(span.Attributes)
				);
			}
			if (captures.Count == 0)
			{
				Console.WriteLine("no captures in " + folder);
			}
			return 0;
		}

		/// <param name="number">The capture as <see cref="List"/> numbers it, from one.</param>
		internal static int Cut(string folder, int number, string to)
		{
			var captures = Find(folder);
			if (number < 1 || number > captures.Count)
			{
				Console.Error.WriteLine("there are " + captures.Count + " captures in " + folder);
				return 1;
			}
			var capture = captures[number - 1];
			var start = capture.Span.StartTimeUnixNano;
			var end = capture.Span.EndTimeUnixNano;
			Directory.CreateDirectory(to);
			foreach (var file in TelemetryFiles.Find(folder, true).Where(file => Path.GetFileName(file).Contains(capture.Run)))
			{
				var signal = TelemetryFiles.SignalOf(file);
				var kept = new List<byte[]>();
				foreach (var frame in TelemetryFiles.ReadFrames(file))
				{
					var cut = signal == "traces" ? CutTraces(frame, capture.Span.TraceId, start, end)
						: signal == "logs" ? CutLogs(frame, start, end)
						: CutMetrics(frame, start, end);
					if (cut != null)
					{
						kept.Add(cut.ToByteArray());
					}
				}
				var name = signal + "-" + capture.Run + "-capture" + number + TelemetryFiles.Extension;
				TelemetryFiles.WriteFrames(Path.Combine(to, name), kept);
				Console.WriteLine(name + ": " + kept.Count + " frames");
			}
			return 0;
		}

		/// <summary>The spans of the trace of the capture, and the others that started within it.</summary>
		static IMessage CutTraces(byte[] frame, ByteString trace, ulong start, ulong end)
		{
			var data = TracesData.Parser.ParseFrom(frame);
			foreach (var scope in data.ResourceSpans.SelectMany(resource => resource.ScopeSpans))
			{
				var spans = scope.Spans.Where(span => span.TraceId == trace || (span.StartTimeUnixNano >= start && span.StartTimeUnixNano <= end)).ToList();
				scope.Spans.Clear();
				scope.Spans.AddRange(spans);
			}
			foreach (var resource in data.ResourceSpans)
			{
				Prune(resource.ScopeSpans, scope => scope.Spans.Count == 0);
			}
			Prune(data.ResourceSpans, resource => resource.ScopeSpans.Count == 0);
			return data.ResourceSpans.Count == 0 ? null : data;
		}

		static IMessage CutLogs(byte[] frame, ulong start, ulong end)
		{
			var data = LogsData.Parser.ParseFrom(frame);
			foreach (var scope in data.ResourceLogs.SelectMany(resource => resource.ScopeLogs))
			{
				var records = scope.LogRecords.Where(record => record.TimeUnixNano >= start && record.TimeUnixNano <= end).ToList();
				scope.LogRecords.Clear();
				scope.LogRecords.AddRange(records);
			}
			foreach (var resource in data.ResourceLogs)
			{
				Prune(resource.ScopeLogs, scope => scope.LogRecords.Count == 0);
			}
			Prune(data.ResourceLogs, resource => resource.ScopeLogs.Count == 0);
			return data.ResourceLogs.Count == 0 ? null : data;
		}

		/// <summary>
		/// A frame of metrics whose interval overlaps the capture: during it the metrics are written each frame, and the
		/// interval before it ends where it begins.
		/// </summary>
		static IMessage CutMetrics(byte[] frame, ulong start, ulong end)
		{
			var data = MetricsData.Parser.ParseFrom(frame);
			var times = data.ResourceMetrics.SelectMany(resource => resource.ScopeMetrics).SelectMany(scope => scope.Metrics).Select(Interval).Where(interval => interval.End != 0).ToList();
			if (times.Count == 0)
			{
				return null;
			}
			var from = times.Min(interval => interval.Start);
			var to = times.Max(interval => interval.End);
			return to > start && from < end ? data : null;
		}

		static (ulong Start, ulong End) Interval(Metric metric)
		{
			switch (metric.DataCase)
			{
				case Metric.DataOneofCase.Sum:
					return (metric.Sum.DataPoints[0].StartTimeUnixNano, metric.Sum.DataPoints[0].TimeUnixNano);
				case Metric.DataOneofCase.Gauge:
					return (metric.Gauge.DataPoints[0].StartTimeUnixNano, metric.Gauge.DataPoints[0].TimeUnixNano);
				case Metric.DataOneofCase.Histogram:
					return (metric.Histogram.DataPoints[0].StartTimeUnixNano, metric.Histogram.DataPoints[0].TimeUnixNano);
				default:
					return (0, 0);
			}
		}

		static void Prune<T>(Google.Protobuf.Collections.RepeatedField<T> items, Func<T, bool> empty)
		{
			var kept = items.Where(item => !empty(item)).ToList();
			items.Clear();
			items.AddRange(kept);
		}
	}
}
