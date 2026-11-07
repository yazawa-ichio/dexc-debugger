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

		/// <summary>Opens new files for this run, one per signal, and starts writing to them periodically.</summary>
		/// <returns>The path of the metrics file. The logs are beside it, named the same with <c>logs</c> for <c>metrics</c>.</returns>
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
			var run = Guid.NewGuid().ToString("N");
			var files = new TelemetryFileSet(directory, run, options.MaximumFileBytes, options.MaximumDirectoryBytes);
			var metrics = files.Open("metrics");
			var logs = files.Open("logs");
			var traces = files.Open("traces");
			root.Start(metrics, logs, traces, options, Resource(options, run), Time.realtimeSinceStartupAsDouble);
			return metrics.Path;
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

		/// <summary>The tracer of this name, made on first use.</summary>
		/// <exception cref="ArgumentException">The name is empty.</exception>
		public static TelemetryTracer Tracer(string name)
		{
			return TelemetryRoot.Current.Traces.Tracer(name);
		}

		/// <summary>The current span, when <see cref="TelemetryOptions.AmbientContext"/> is on. Any thread.</summary>
		public static TelemetryContext CurrentContext => TelemetryRoot.Current.Traces.Current;

		/// <summary>The logger of a category, made on first use. Any thread.</summary>
		/// <exception cref="ArgumentException">The category is empty.</exception>
		public static TelemetryLogger Logger(string category)
		{
			return TelemetryRoot.Current.Logs.Logger(category);
		}

		/// <summary>
		/// The level of the categories without one of their own. It works on the calls the compile symbols kept, so a build
		/// that keeps Verbose can start at Info and collect Verbose later.
		/// </summary>
		public static TelemetrySeverity LogLevel
		{
			get => TelemetryRoot.Current.Logs.DefaultLevel;
			set => TelemetryRoot.Current.Logs.DefaultLevel = value;
		}

		/// <summary>Sets the level of a category and those under it: <c>Game</c> also covers <c>Game.Stage</c>. The longest match wins.</summary>
		public static void SetLogLevel(string category, TelemetrySeverity level)
		{
			TelemetryRoot.Current.Logs.SetLevel(category, level);
		}

		/// <summary>Removes what <see cref="SetLogLevel"/> set for this category.</summary>
		public static void ClearLogLevel(string category)
		{
			TelemetryRoot.Current.Logs.ClearLevel(category);
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
