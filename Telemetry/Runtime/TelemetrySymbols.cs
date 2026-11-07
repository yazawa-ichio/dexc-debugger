namespace Dexc.Telemetry
{
	/// <summary>
	/// The compile symbols that keep the recording calls of each <see cref="TelemetryDetail"/>. A project defines the one
	/// it wants, and that one keeps its own level and the coarser ones. With none of them every call is compiled away.
	/// </summary>
	internal static class TelemetrySymbols
	{
		internal const string DetailBasic = "DEXC_TELEMETRY_DETAIL_BASIC";
		internal const string DetailStandard = "DEXC_TELEMETRY_DETAIL_STANDARD";
		internal const string DetailDetailed = "DEXC_TELEMETRY_DETAIL_DETAILED";
	}
}
