using System.Collections.Generic;

namespace Dexc.Telemetry
{
	/// <summary>How <see cref="DexcTelemetry.Start"/> runs. Every field has a default.</summary>
	public sealed class TelemetryOptions
	{
		/// <summary>Where the files go. By default the Telemetry folder in <c>Application.persistentDataPath</c>.</summary>
		public string Directory;

		/// <summary>The <c>service.name</c> of the data. By default <c>Application.productName</c>.</summary>
		public string ServiceName;

		/// <summary>The <c>service.version</c> of the data. By default <c>Application.version</c>.</summary>
		public string ServiceVersion;

		/// <summary>Seconds between writes of the metrics. Memory does not depend on this: metrics are totals of the interval.</summary>
		public double ExportIntervalSeconds = 60;

		/// <summary>
		/// The longest a frame of logs or spans waits before it is written. A full frame (64KB) or an error goes out at once.
		/// Shorter loses less in a crash and adds the resource to more frames.
		/// </summary>
		public double RecordFlushSeconds = 5;

		/// <summary>The finest level recorded. It can only silence what the compile symbols kept.</summary>
		public TelemetryDetail Detail = TelemetryDetail.Detailed;

		/// <summary>Whether to collect the frame time, memory and GC of Unity, and the cost of this package.</summary>
		public bool CollectUnityMetrics = true;

		/// <summary>Whether to collect what is logged through Unity, with a <see cref="UnityLogAdapter"/>.</summary>
		public bool CollectUnityLogs = true;

		/// <summary>Other loggers to bring in. They are attached when telemetry starts and detached when it stops.</summary>
		public IReadOnlyList<ITelemetryLogAdapter> LogAdapters;

		/// <summary>The level of the categories without one of their own. <see cref="DexcTelemetry.LogLevel"/> changes it later.</summary>
		public TelemetrySeverity MinimumSeverity = TelemetrySeverity.Info;

		/// <summary>
		/// Whether the body of a record is the text with the values filled in. By default it is the template, as in the
		/// official SDK, and the values are attributes either way. Making the text allocates.
		/// </summary>
		public bool IncludeFormattedMessage;

		/// <summary>
		/// Whether spans and logs follow the current span through an AsyncLocal, across awaits, without passing a context.
		/// Off by default: in IL2CPP setting an AsyncLocal costs about a microsecond and 500 bytes for each span.
		/// </summary>
		public bool AmbientContext;

		/// <summary>The size at which a file ends and the next one begins.</summary>
		public long MaximumFileBytes = 8L * 1024 * 1024;

		/// <summary>The limit of the folder. Above it the oldest files are deleted, of this run or earlier ones.</summary>
		public long MaximumDirectoryBytes = 64L * 1024 * 1024;

		/// <summary>More attributes of the OpenTelemetry resource, such as the build or the device.</summary>
		public IReadOnlyDictionary<string, string> ResourceAttributes;
	}
}
