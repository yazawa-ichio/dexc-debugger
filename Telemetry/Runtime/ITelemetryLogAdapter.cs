namespace Dexc.Telemetry
{
	/// <summary>
	/// Brings the records of another logger into telemetry. Give it to <see cref="TelemetryOptions.LogAdapters"/>. It gets
	/// its loggers from <see cref="DexcTelemetry.Logger"/> and hands records over with <see cref="TelemetryLogger.Emit(TelemetrySeverity, string, string, bool)"/>,
	/// which it may call on any thread.
	/// </summary>
	public interface ITelemetryLogAdapter
	{
		/// <summary>Starts listening. Called on the main thread when telemetry starts.</summary>
		void Attach();

		/// <summary>Stops listening. Called on the main thread when telemetry stops.</summary>
		void Detach();
	}
}
