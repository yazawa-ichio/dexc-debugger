namespace Dexc.Telemetry
{
	/// <summary>
	/// Where the player is, for the records that say where something happened: a hitch, a position at an interval. The game
	/// gives it in <see cref="TelemetryOptions.Location"/>; what a map is and which object stands for the player are its own
	/// to decide. Called on the main thread at most once a frame.
	/// </summary>
	public interface ITelemetryLocation
	{
		/// <summary>The map, such as a scene or a stage, and the position in it. False when there is no place to say.</summary>
		bool TryGetLocation(out string map, out float x, out float y, out float z);
	}
}
