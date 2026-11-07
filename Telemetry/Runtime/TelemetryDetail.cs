namespace Dexc.Telemetry
{
	/// <summary>
	/// How fine a metric or a span is. OpenTelemetry has no such level for these signals, so this one is ours and has no
	/// relation to <see cref="TelemetrySeverity"/>.
	/// </summary>
	public enum TelemetryDetail
	{
		/// <summary>What a shipped game can afford all the time: frame time, memory, scene changes.</summary>
		Basic = 1,

		/// <summary>Totals per system and the spans of stages and other long stretches.</summary>
		Standard = 2,

		/// <summary>Values of every frame and spans of single methods.</summary>
		Detailed = 3,
	}
}
