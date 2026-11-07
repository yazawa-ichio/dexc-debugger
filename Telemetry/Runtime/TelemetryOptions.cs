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

		/// <summary>The finest level recorded. It can only silence what the compile symbols kept.</summary>
		public TelemetryDetail Detail = TelemetryDetail.Detailed;

		/// <summary>Whether to collect the frame time, memory and GC of Unity, and the cost of this package.</summary>
		public bool CollectUnityMetrics = true;

		/// <summary>More attributes of the OpenTelemetry resource, such as the build or the device.</summary>
		public IReadOnlyDictionary<string, string> ResourceAttributes;
	}
}
