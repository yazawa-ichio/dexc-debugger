using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Google.Protobuf;
using Google.Protobuf.Collections;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Logs.V1;
using OpenTelemetry.Proto.Metrics.V1;
using OpenTelemetry.Proto.Trace.V1;

namespace Dexc.Telemetry.Cli
{
	/// <summary>Prints what the files hold, one line a record, for a person or an agent to read.</summary>
	internal static class DumpCommand
	{
		internal static int Run(string path, bool open)
		{
			foreach (var file in TelemetryFiles.Find(path, open))
			{
				var frames = TelemetryFiles.ReadFrames(file);
				Console.WriteLine("# " + Path.GetFileName(file) + ": " + frames.Count + " frames, " + new FileInfo(file).Length + " bytes");
				switch (TelemetryFiles.SignalOf(file))
				{
					case "metrics":
						Metrics(frames);
						break;
					case "logs":
						Logs(frames);
						break;
					case "traces":
						Traces(frames);
						break;
				}
			}
			return 0;
		}

		static void Metrics(List<byte[]> frames)
		{
			var first = true;
			foreach (var data in frames.Select(MetricsData.Parser.ParseFrom))
			{
				foreach (var resource in data.ResourceMetrics)
				{
					if (first)
					{
						Console.WriteLine("resource " + Attributes(resource.Resource.Attributes));
						first = false;
					}
					foreach (var scope in resource.ScopeMetrics)
					{
						foreach (var metric in scope.Metrics)
						{
							Console.WriteLine(Describe(metric));
						}
					}
				}
			}
		}

		static string Describe(Metric metric)
		{
			var unit = string.IsNullOrEmpty(metric.Unit) ? string.Empty : " " + metric.Unit;
			switch (metric.DataCase)
			{
				case Metric.DataOneofCase.Sum:
					var sum = metric.Sum.DataPoints.Single();
					return Time(sum.TimeUnixNano) + " sum       " + metric.Name + " = " + Number(sum.AsInt, sum.AsDouble, sum.ValueCase == NumberDataPoint.ValueOneofCase.AsInt) + unit;
				case Metric.DataOneofCase.Gauge:
					var gauge = metric.Gauge.DataPoints.Single();
					return Time(gauge.TimeUnixNano) + " gauge     " + metric.Name + " = " + Number(gauge.AsInt, gauge.AsDouble, gauge.ValueCase == NumberDataPoint.ValueOneofCase.AsInt) + unit;
				case Metric.DataOneofCase.Histogram:
					var point = metric.Histogram.DataPoints.Single();
					return Time(point.TimeUnixNano) + " histogram " + metric.Name + " count " + point.Count + ", sum " + point.Sum.ToString("G6") + ", min " + point.Min.ToString("G6") + ", max " + point.Max.ToString("G6") + unit;
				default:
					return metric.Name + " " + metric.DataCase;
			}
		}

		static void Logs(List<byte[]> frames)
		{
			foreach (var data in frames.Select(LogsData.Parser.ParseFrom))
			{
				foreach (var scope in data.ResourceLogs.SelectMany(resource => resource.ScopeLogs))
				{
					foreach (var record in scope.LogRecords)
					{
						var severity = string.IsNullOrEmpty(record.SeverityText) ? record.SeverityNumber.ToString() : record.SeverityText;
						var text = Time(record.TimeUnixNano) + " " + severity.PadRight(7) + " " + scope.Scope.Name;
						if (!string.IsNullOrEmpty(record.EventName))
						{
							text += " [" + record.EventName + "]";
						}
						text += " " + Value(record.Body);
						if (record.Attributes.Count > 0)
						{
							text += " " + Attributes(record.Attributes);
						}
						if (!record.SpanId.IsEmpty)
						{
							text += " span " + record.SpanId.ToBase16();
						}
						Console.WriteLine(text);
					}
				}
			}
		}

		static void Traces(List<byte[]> frames)
		{
			foreach (var data in frames.Select(TracesData.Parser.ParseFrom))
			{
				foreach (var scope in data.ResourceSpans.SelectMany(resource => resource.ScopeSpans))
				{
					foreach (var span in scope.Spans)
					{
						var milliseconds = (span.EndTimeUnixNano - span.StartTimeUnixNano) / 1e6;
						var text = Time(span.StartTimeUnixNano) + " " + milliseconds.ToString("F3").PadLeft(10) + "ms " + span.Name + " (" + scope.Scope.Name + ") id " + span.SpanId.ToBase16();
						if (!span.ParentSpanId.IsEmpty)
						{
							text += " parent " + span.ParentSpanId.ToBase16();
						}
						if (span.Attributes.Count > 0)
						{
							text += " " + Attributes(span.Attributes);
						}
						if (span.Status?.Code == Status.Types.StatusCode.Error)
						{
							text += " ERROR " + span.Status.Message;
						}
						Console.WriteLine(text);
					}
				}
			}
		}

		static string Time(ulong unixNanoseconds)
		{
			return TelemetryFiles.Time(unixNanoseconds).ToString("HH:mm:ss.fff");
		}

		static string Number(long integer, double floating, bool isInteger)
		{
			return isInteger ? integer.ToString() : floating.ToString("G6");
		}

		internal static string Attributes(RepeatedField<KeyValue> attributes)
		{
			return "{" + string.Join(", ", attributes.Select(attribute => attribute.Key + "=" + Value(attribute.Value))) + "}";
		}

		internal static string Value(AnyValue value)
		{
			if (value == null)
			{
				return string.Empty;
			}
			switch (value.ValueCase)
			{
				case AnyValue.ValueOneofCase.StringValue:
					return value.StringValue;
				case AnyValue.ValueOneofCase.IntValue:
					return value.IntValue.ToString();
				case AnyValue.ValueOneofCase.DoubleValue:
					return value.DoubleValue.ToString("G6");
				case AnyValue.ValueOneofCase.BoolValue:
					return value.BoolValue ? "true" : "false";
				default:
					return value.ValueCase.ToString();
			}
		}
	}

	internal static class ByteStringExtensions
	{
		internal static string ToBase16(this ByteString bytes)
		{
			return Convert.ToHexString(bytes.Span).ToLowerInvariant();
		}
	}
}
