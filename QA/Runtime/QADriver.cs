using UnityEngine;

namespace Dexc.QA
{
	/// <summary>Moves the root on once a frame, takes the screenshots at the end of frames, and keeps the counts when the application may not come back.</summary>
	internal sealed class QADriver : MonoBehaviour
	{
		internal QARoot Root;

		void Update()
		{
			Root.Pump();
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
