#if DEXC_DEBUGGER && !DISABLE_DEXC_DEBUGGER && DEXC_REMOTE_PLAYER
using Dexc;
using UnityEngine;
using UnityEngine.Scripting;
using DebuggerRuntime = Dexc.DexcDebugger;

namespace DexcDebugger.Verification
{
	[DebugCommandAttribute("I19/Remote/Echo")]
	public sealed class DexcRemotePlayerCommand : DebugCommand<string>
	{
		public InputString Text = new InputString("from the player");

		protected override string Execute() => Text.Value;
	}

	[Preserve]
	public sealed class DexcRemotePlayerSettings : DebugSettingSet<DexcRemotePlayerSettings>
	{
		[DebugSetting("I19/Remote/Volume")]
		public int Volume { get; private set; } = 5;
	}

	/// <summary>
	/// Registers itself with the Inspector, but only once a session exists.
	///
	/// Nothing here starts one. The session is expected to arrive from outside, so registration has to wait for it:
	/// the facade refuses to register before there is a session to register into.
	/// </summary>
	public sealed class DexcRemotePlayerComponent : MonoBehaviour
	{
		[DebugField(Access = DebugFieldAccess.ReadOnly)]
		public int Charge = 19;

		private bool m_Registered;

		private void Update()
		{
			if (m_Registered || !DebuggerRuntime.IsInitialized)
				return;
			m_Registered = true;
			DebugInspector.Register("I19/Remote/Target", this, "remote-target");
			Debug.Log("Dexc remote Player fixture registered its Inspector target.");
		}
	}

	/// <summary>
	/// Puts a running Player on the other end of the same client that drives the Editor.
	///
	/// This one checks nothing itself and never quits. Everything about it is asked from outside over the Unity CLI,
	/// which is the point: the remote layer is only proven on a device if a tool outside the process can reach it
	/// there, and a fixture that tested itself would prove the opposite.
	///
	/// Nothing here starts the Pipeline server either - the package boots its own driver from the build-baked config,
	/// which the build driver turns on. All this puts in the scene is something for the operations to find.
	///
	/// It also does not call Initialize. A session is expected to arrive through dexc_initialize, which is the whole
	/// reason that command exists - Initialize carries [Conditional("DEXC_DEBUGGER")] and so vanishes from any caller
	/// compiled without the symbol.
	/// </summary>
	public static class DexcRemotePlayerFixture
	{
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		private static void Start()
		{
			var host = new GameObject("DexcRemotePlayerHost");
			Object.DontDestroyOnLoad(host);
			host.AddComponent<DexcRemotePlayerComponent>();
			Debug.Log("Dexc remote Player fixture is up.");
		}
	}
}
#endif
