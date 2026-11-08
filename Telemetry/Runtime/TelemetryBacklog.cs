namespace Dexc.Telemetry
{
	/// <summary>What happens to a frame when the frames waiting for the writing thread are at the cap.</summary>
	public enum TelemetryBacklog
	{
		/// <summary>The frame is dropped and counted. The game never waits for storage. For a release build.</summary>
		Drop = 0,

		/// <summary>
		/// The main thread waits for room, up to <see cref="TelemetryOptions.BacklogWaitSeconds"/>, and drops the frame only
		/// after it. Nothing is lost while storage keeps up at all, but a stalled storage stalls the game. For a build that
		/// verifies, where gaps in the data cost more than a slow frame.
		/// </summary>
		Wait = 1,
	}
}
