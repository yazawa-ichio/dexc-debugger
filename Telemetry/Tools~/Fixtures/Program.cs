using System;
using System.Collections.Generic;
using System.IO;
using Dexc.Telemetry;

namespace Dexc.Telemetry.Fixtures
{
	/// <summary>
	/// Writes one file of each signal into the folder given, with every kind of field the package writes: the three kinds
	/// of metrics, logs with values, an event, an exception and a span, and spans with a parent, attributes and an error.
	/// </summary>
	internal static class Program
	{
		static int Main(string[] arguments)
		{
			if (arguments.Length != 1)
			{
				Console.Error.WriteLine("dexc-telemetry-fixtures <folder>");
				return 2;
			}
			var files = new TelemetryFileSet(arguments[0], "fixture", 8L * 1024 * 1024, 64L * 1024 * 1024);
			var metricsFrames = files.Open("metrics");
			var logFrames = files.Open("logs");
			var traceFrames = files.Open("traces");
			var metrics = new TelemetryMetrics();
			metrics.SetResource(new[]
			{
				new KeyValuePair<string, string>("service.name", "dexc-telemetry-fixture"),
				new KeyValuePair<string, string>("service.version", "1.0.0"),
				new KeyValuePair<string, string>("service.instance.id", "fixture"),
				new KeyValuePair<string, string>("telemetry.sdk.name", "dexc-telemetry"),
			});
			var traces = new TelemetryTraces(metrics);
			traces.WriteFailed = exception => throw exception;
			traces.Begin(traceFrames, false);
			var logs = new TelemetryLogs(metrics, traces);

			metrics.Counter("fixture.hits", "{hit}").AddBasic(3);
			metrics.Gauge("fixture.memory", "By").SetBasic(1024);
			var histogram = metrics.Histogram("fixture.frame.time", "ms", new double[] { 8, 16.7, 33.4 });
			histogram.RecordBasic(5);
			histogram.RecordBasic(20);

			var tracer = traces.Tracer("Fixture");
			var logger = logs.Logger("Fixture").With("mode", "hard");
			using (var parent = tracer.Start("fixture.parent", default, TelemetryDetail.Basic))
			{
				parent.Set("stage", 3);
				var child = tracer.Start("fixture.child", parent.Context, TelemetryDetail.Basic);
				child.Set("ratio", 0.5);
				child.Set("boss", true);
				child.SetError("broken");
				child.Dispose();
				logger.Info("stage {Stage} took {Seconds:F2}", 3, 1.5);
				logger.WithEvent("fixture.cleared").Info("cleared");
				logger.WithContext(parent.Context).Warning("inside the parent");
				logger.Error(new InvalidOperationException("broken"), "stage {Stage} failed", 3);
			}

			logs.Drain(logFrames);
			logs.Flush(logFrames);
			traces.Flush();
			metrics.Export(metricsFrames);
			metricsFrames.Dispose();
			logFrames.Dispose();
			traceFrames.Dispose();
			foreach (var file in Directory.GetFiles(arguments[0], "*-fixture.otlp"))
			{
				Console.WriteLine(file);
			}
			return 0;
		}
	}
}
