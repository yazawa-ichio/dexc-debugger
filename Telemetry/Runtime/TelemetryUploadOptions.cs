using System.Collections.Generic;

namespace Dexc.Telemetry
{
	/// <summary>How <see cref="DexcTelemetry.StartUpload"/> sends the files to an OTLP/HTTP endpoint. Every field but the endpoint has a default.</summary>
	public sealed class TelemetryUploadOptions
	{
		/// <summary>The base of the endpoint, such as <c>https://collector.example.com:4318</c>. The paths <c>/v1/metrics</c>, <c>/v1/logs</c> and <c>/v1/traces</c> follow it.</summary>
		public string Endpoint;

		/// <summary>Headers of every request, such as an API key.</summary>
		public IReadOnlyDictionary<string, string> Headers;

		public bool Metrics = true;

		public bool Traces = true;

		/// <summary>Off by default: the logs of Unity can hold any text. Ask the players before sending them.</summary>
		public bool Logs;

		/// <summary>The most bytes of frames joined into one request, before compression. A larger frame goes alone.</summary>
		public int MaximumRequestBytes = 1024 * 1024;

		/// <summary>gzip at its fastest. The logs shrank to 12% and the spans to 42% in a measure.</summary>
		public bool Gzip = true;

		/// <summary>Seconds between rounds of sending. A round goes on while there is more to send.</summary>
		public double IntervalSeconds = 60;

		/// <summary>How long one request may take.</summary>
		public int TimeoutSeconds = 30;
	}
}
