using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Dexc
{
	/// <summary>
	/// The root of the running debug menu and the only mutable static variable in the library. Every other piece of state
	/// (session, storage, registries, UI state, renderers) hangs off this instance, so replacing it resets the library
	/// completely. A new root is created when Play Mode starts (also with Domain Reload disabled) and when the Editor
	/// leaves Play Mode, which is what keeps repeated Play Mode entries from inheriting the previous run's state.
	/// </summary>
	internal sealed class DexcRoot : IDisposable
	{
		static DexcRoot s_Current;

		internal static DexcRoot Current => s_Current ?? (s_Current = new DexcRoot());

		// Session lifetime (DexcDebugger).
		internal DexcRuntimeSession Session;
		internal long NextGeneration;
		internal bool IsInitializing;
		internal Action SessionInvalidated;

		// Registries usable before Initialize.
		internal IDebugStorage Storage;

		/// <summary>How the game asked for its storage to be made. Configuration, not running state: the next root keeps it.</summary>
		internal Func<IDebugStorage> StorageFactory;
		internal readonly Dictionary<Type, object> BootSets = new Dictionary<Type, object>();
		internal readonly Dictionary<Type, SettingSetEntry> SettingSets = new Dictionary<Type, SettingSetEntry>();
		internal readonly Dictionary<Type, bool> LoadedBootTypes = new Dictionary<Type, bool>();
		internal readonly List<DebugPanel> RegisteredPanels = new List<DebugPanel>();
		internal DebugPanel HomePanel;
		internal readonly List<DebugDisplayWidget> DisplayWidgets = new List<DebugDisplayWidget>();
		internal readonly HashSet<DebugDisplayWidget> ReportedDisplayWidgets = new HashSet<DebugDisplayWidget>();
		internal readonly ConditionalWeakTable<object, Dictionary<FieldInfo, IDebugInput>> CommandInputBaselines =
			new ConditionalWeakTable<object, Dictionary<FieldInfo, IDebugInput>>();

#if DEXC_HAS_URP
		// World Visual rendering, drawn through the URP render pass.
		DexcPlayerRenderer.State m_PlayerRenderer;
		internal DexcPlayerRenderer.State PlayerRenderer => m_PlayerRenderer ?? (m_PlayerRenderer = new DexcPlayerRenderer.State());
#endif

		// Drivers of the Runtime features. Created on first use of the feature, never at startup.
		internal DexcDisplayDriver DisplayDriver;
		internal DexcInspectorDriver InspectorDriver;

		// Display HUD and Overlay layers, drawn by DexcDisplayDriver.
		internal DebugDisplayFrameSnapshot DisplayFrame;

		/// <summary>
		/// The open menu's panel in GUI coordinates, or null while no menu is shown. Supplied by the DebugUI assembly so the
		/// Display text can move out from under the panel.
		/// </summary>
		internal Func<Rect?> MenuPanel;

		/// <summary>State of the DebugUI assembly (its <c>DexcUIRoot</c>), kept here so it dies with this root.</summary>
		internal object UI;

		internal sealed class SettingSetEntry
		{
			internal object Instance;
			internal DebugSessionId SessionId;
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		static void ResetForPlayMode()
		{
			Reset();
		}

#if UNITY_EDITOR
		[UnityEditor.InitializeOnLoadMethod]
		static void HookEditor()
		{
			UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
			Reset();
		}

		static void OnPlayModeStateChanged(UnityEditor.PlayModeStateChange change)
		{
			// Runtime objects die with Play Mode; drop the state that referenced them instead of carrying it to the next run.
			// Wait until Edit Mode: the scene still runs a last LateUpdate and its OnDestroy calls after ExitingPlayMode,
			// and a game that unregisters from Dexc there must still find its session.
			if (change == UnityEditor.PlayModeStateChange.EnteredEditMode)
				Reset();
		}
#endif

		/// <summary>Disposes the current root and installs a fresh one.</summary>
		internal static void Reset()
		{
			var previous = s_Current;
			try
			{
				// The next root makes its storage afresh and may read the same place, so nothing written may stay buffered.
				previous?.Storage?.Flush();
			}
			catch (Exception exception)
			{
				Debug.LogException(exception);
			}
			try
			{
				previous?.Dispose();
			}
			catch (Exception exception)
			{
				Debug.LogException(exception);
			}
			s_Current = new DexcRoot { StorageFactory = previous?.StorageFactory };
		}

		/// <summary>Destroys an object the library created, using the call Unity allows in the current mode.</summary>
		internal static void DestroyObject(UnityEngine.Object target)
		{
			if (target == null)
				return;
			if (Application.isPlaying)
				UnityEngine.Object.Destroy(target);
			else
				UnityEngine.Object.DestroyImmediate(target);
		}

		public void Dispose()
		{
			// Still reachable through Current while disposing, so the static facades see the state being torn down.
			// The session goes first: its invalidation handlers run against the UI state they belong to.
			try
			{
				DexcDebugger.InvalidateCurrentSession();
#if DEXC_DEBUGGER
				DexcDisplayDriver.Destroy();
				DexcInspectorDriver.Destroy();
#endif
			}
			finally
			{
				SessionInvalidated = null;
				try
				{
					(UI as IDisposable)?.Dispose();
				}
				finally
				{
					UI = null;
				}
			}
		}
	}
}
