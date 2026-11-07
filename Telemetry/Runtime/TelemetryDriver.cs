using UnityEngine;

namespace Dexc.Telemetry
{
	/// <summary>Gives the root its clock and tells it when the application may not come back.</summary>
	internal sealed class TelemetryDriver : MonoBehaviour
	{
		internal TelemetryRoot Root;

		void Update()
		{
			Root.OnFrame(Time.unscaledDeltaTime);
			Root.Tick(Time.realtimeSinceStartupAsDouble);
		}

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
