using UnityEngine;

namespace Dexc.Telemetry
{
	/// <summary>Tells the root when the application may not come back. The frames come from <see cref="TelemetryPlayerLoop"/>.</summary>
	internal sealed class TelemetryDriver : MonoBehaviour
	{
		internal TelemetryRoot Root;

		void OnApplicationPause(bool paused)
		{
			if (paused)
			{
				Root.Flush();
			}
		}

		void OnApplicationQuit()
		{
			Root.Stop();
		}
	}
}
