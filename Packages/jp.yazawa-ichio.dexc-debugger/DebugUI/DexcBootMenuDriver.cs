#if DEXC_DEBUGGER
using UnityEngine;

namespace Dexc
{
	/// <summary>Hidden driver that polls input and draws the boot menu while it is open.</summary>
	internal sealed class DexcBootMenuDriver : MonoBehaviour
	{
		/// <summary>State of this class. Owned by <see cref="DexcUIRoot"/>; nothing here survives a root reset.</summary>
		internal sealed class State
		{
			internal DexcBootMenuDriver Instance;
		}

		internal static State Data => DexcUIRoot.Current.BootMenuDriver;
		static DexcBootMenuDriver Instance
		{
			get => Data.Instance;
			set => Data.Instance = value;
		}

		internal static void Ensure()
		{
			if (Instance != null)
				return;
			var gameObject = new GameObject("DexcBootMenu");
			// No hide flags: Play Mode owns the lifetime, so leaving it destroys this with everything else.
			DontDestroyOnLoad(gameObject);
			Instance = gameObject.AddComponent<DexcBootMenuDriver>();
		}

		internal static void Destroy()
		{
			if (Instance == null)
				return;
			var instance = Instance;
			Instance = null;
			if (Application.isPlaying)
				Object.Destroy(instance.gameObject);
			else
				Object.DestroyImmediate(instance.gameObject);
		}

		void Update()
		{
			if (!DebugBootMenu.IsOpen)
				return;
			if (!DebugBootMenu.UsesRuntimeMenu)
				DexcBootMenuInput.Poll();
			if (
				DexcBootMenuState.AutoStartAt > 0f
				&& !DexcBootMenuState.AutoStartCancelled
				&& Time.realtimeSinceStartup >= DexcBootMenuState.AutoStartAt
			)
				DebugBootMenu.Start();
		}

		void OnGUI()
		{
			if (!DebugBootMenu.IsOpen || DebugBootMenu.UsesRuntimeMenu)
				return;
			DexcBootMenuRenderer.Draw();
		}
	}
}
#endif
