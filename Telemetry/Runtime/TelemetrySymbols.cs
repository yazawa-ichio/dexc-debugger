namespace Dexc.Telemetry
{
	/// <summary>
	/// The compile symbols that keep the recording calls of each <see cref="TelemetryDetail"/> and the log calls of each
	/// <see cref="TelemetrySeverity"/>. A project defines the one it wants of each kind. That one keeps its own level and the
	/// coarser or more severe ones. With none of them every call is compiled away.
	/// </summary>
	internal static class TelemetrySymbols
	{
		internal const string DetailBasic = "DEXC_TELEMETRY_DETAIL_BASIC";
		internal const string DetailStandard = "DEXC_TELEMETRY_DETAIL_STANDARD";
		internal const string DetailDetailed = "DEXC_TELEMETRY_DETAIL_DETAILED";

		internal const string LogVerbose = "DEXC_TELEMETRY_LOG_VERBOSE";
		internal const string LogDebug = "DEXC_TELEMETRY_LOG_DEBUG";
		internal const string LogInfo = "DEXC_TELEMETRY_LOG_INFO";
		internal const string LogWarning = "DEXC_TELEMETRY_LOG_WARNING";
		internal const string LogError = "DEXC_TELEMETRY_LOG_ERROR";
		internal const string LogFatal = "DEXC_TELEMETRY_LOG_FATAL";
	}
}
