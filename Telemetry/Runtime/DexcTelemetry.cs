using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Dexc.Telemetry
{
	/// <summary>
	/// The entry to telemetry. Make the instruments once and record through them. What is recorded before
	/// <see cref="Start"/> is written by the first export after it. Main thread only.
	/// </summary>
	public static class DexcTelemetry
	{
		const string SdkName = "dexc-telemetry";

		/// <summary>True between <see cref="Start"/> and <see cref="Stop"/>, unless a write failed and ended the output.</summary>
		public static bool IsRunning => TelemetryRoot.Current.IsRunning;

		/// <summary>Opens a new file for this run and starts writing to it periodically.</summary>
		/// <returns>The path of the metrics file.</returns>
		/// <exception cref="InvalidOperationException">It is already running.</exception>
		public static string Start(TelemetryOptions options = null)
		{
			options = options ?? new TelemetryOptions();
			if (options.ExportIntervalSeconds <= 0)
			{
				throw new ArgumentOutOfRangeException(nameof(options), "The export interval must be above zero.");
			}
			var root = TelemetryRoot.Current;
			if (root.IsRunning)
			{
				throw new InvalidOperationException("Telemetry is already running.");
			}
			var directory = options.Directory ?? Path.Combine(Application.persistentDataPath, "Telemetry");
			Directory.CreateDirectory(directory);
			var run = Guid.NewGuid().ToString("N");
			var path = Path.Combine(directory, "metrics-" + run + ".otlp");
			var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
			root.Start(new TelemetryFrameWriter(stream), options, Resource(options, run), Time.realtimeSinceStartupAsDouble);
			return path;
		}

		/// <summary>Writes what is left and closes the file. Does nothing when not running.</summary>
		public static void Stop()
		{
			TelemetryRoot.Current.Stop();
		}

		/// <summary>Writes what is recorded now, without waiting for the interval.</summary>
		public static void Flush()
		{
			TelemetryRoot.Current.Flush();
		}

		/// <summary>The counter of this name, made on first use.</summary>
		/// <exception cref="ArgumentException">The name or unit is invalid, or the name is taken by another kind or unit.</exception>
		public static TelemetryCounter Counter(string name, string unit = "")
		{
			return TelemetryRoot.Current.Metrics.Counter(name, unit);
		}

		/// <summary>The gauge of this name, made on first use.</summary>
		/// <exception cref="ArgumentException">The name or unit is invalid, or the name is taken by another kind or unit.</exception>
		public static TelemetryGauge Gauge(string name, string unit = "")
		{
			return TelemetryRoot.Current.Metrics.Gauge(name, unit);
		}

		/// <summary>The histogram of this name, made on first use.</summary>
		/// <param name="name">The name of the metric.</param>
		/// <param name="unit">The unit of the values.</param>
		/// <param name="bounds">The upper bounds of the buckets, increasing. By default those of the OpenTelemetry SDK.</param>
		/// <exception cref="ArgumentException">An argument is invalid, or the name is taken with another kind, unit or bounds.</exception>
		public static TelemetryHistogram Histogram(string name, string unit = "", double[] bounds = null)
		{
			return TelemetryRoot.Current.Metrics.Histogram(name, unit, bounds);
		}

		static List<KeyValuePair<string, string>> Resource(TelemetryOptions options, string run)
		{
			var resource = new List<KeyValuePair<string, string>>
			{
				new KeyValuePair<string, string>("service.name", options.ServiceName ?? Application.productName),
				new KeyValuePair<string, string>("service.version", options.ServiceVersion ?? Application.version),
				new KeyValuePair<string, string>("service.instance.id", run),
				new KeyValuePair<string, string>("telemetry.sdk.name", SdkName),
			};
			if (options.ResourceAttributes != null)
			{
				resource.AddRange(options.ResourceAttributes);
			}
			return resource;
		}
	}
}
