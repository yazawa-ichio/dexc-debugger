namespace Dexc.Telemetry
{
	/// <summary>
	/// How serious a log record is. The values are the OpenTelemetry SeverityNumber of each band, so they go into a record
	/// as they are. Verbose is OpenTelemetry's TRACE, renamed so that it does not read as the Traces signal.
	/// </summary>
	public enum TelemetrySeverity
	{
		Verbose = 1,
		Debug = 5,
		Info = 9,
		Warning = 13,
		Error = 17,
		Fatal = 21,
	}
}
