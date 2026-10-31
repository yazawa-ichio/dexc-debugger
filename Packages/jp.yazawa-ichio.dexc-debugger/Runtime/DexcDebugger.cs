using System;
using System.Diagnostics;

namespace Dexc
{
	/// <summary>
	/// Controls the explicit lifetime of a Dexc runtime session. Everything here, like the rest of the library, is called
	/// from the Unity main thread; the library does not check for or support other threads.
	/// </summary>
	public static class DexcDebugger
	{
		/// <summary>Session lifetime state. Owned by <see cref="DexcRoot"/>, so leaving Play Mode drops it.</summary>
		private static DexcRoot Root => DexcRoot.Current;

		internal static event Action SessionInvalidated
		{
			add { Root.SessionInvalidated += value; }
			remove { Root.SessionInvalidated -= value; }
		}

		/// <summary>
		/// Gets whether the package was compiled with the Dexc feature enabled. This property is side-effect free.
		/// </summary>
		public static bool IsEnabled
		{
			get
			{
#if DEXC_DEBUGGER
				return true;
#else
				return false;
#endif
			}
		}

		/// <summary>
		/// Gets whether an explicit Dexc runtime session is currently active. This property is side-effect free.
		/// </summary>
		public static bool IsInitialized
		{
			get
			{
#if DEXC_DEBUGGER
				return Root.Session != null;
#else
				return false;
#endif
			}
		}

		/// <summary>
		/// Starts a session using package defaults.
		/// </summary>
		/// <exception cref="InvalidOperationException">
		/// Thrown while another initialization is in progress.
		/// </exception>
		[Conditional("DEXC_DEBUGGER")]
		public static void Initialize()
		{
			InitializeCore(null);
		}

		/// <summary>
		/// Starts a session using options configured by the supplied callback. The callback's values are copied before return.
		/// </summary>
		/// <param name="configure">Callback used to configure the new session.</param>
		/// <exception cref="ArgumentNullException">
		/// Thrown when <paramref name="configure"/> is <see langword="null"/>.
		/// </exception>
		/// <exception cref="ArgumentException">
		/// Thrown when the callback supplies an invalid option value.
		/// </exception>
		/// <exception cref="InvalidOperationException">
		/// Thrown while another initialization is in progress, or when the callback initializes recursively.
		/// </exception>
		[Conditional("DEXC_DEBUGGER")]
		public static void Initialize(Action<DexcDebuggerOptions> configure)
		{
			if (configure == null)
			{
				throw new ArgumentNullException(nameof(configure));
			}
			InitializeCore(configure);
		}

		/// <summary>Ends the session of the current root without replacing the root itself.</summary>
		private static void ResetSession()
		{
			var root = Root;
			root.IsInitializing = false;
			InvalidateCurrentSession();
			root.NextGeneration = 0;
		}

		internal static DexcRuntimeSession CurrentSession => Root.Session;

		internal static bool IsCurrentSession(DexcRuntimeSession session)
		{
			return session != null && ReferenceEquals(session, Root.Session);
		}

#if UNITY_INCLUDE_TESTS
		internal static void ResetForTests()
		{
			ResetSession();
		}
#endif

		private static void InitializeCore(Action<DexcDebuggerOptions> configure)
		{
#if DEXC_DEBUGGER
			var root = Root;
			if (root.IsInitializing)
			{
				throw new InvalidOperationException("DexcDebugger.Initialize cannot be called recursively.");
			}
			root.IsInitializing = true;
			try
			{
				InvalidateCurrentSession();
				var options = new DexcDebuggerOptions();
				configure?.Invoke(options);

				var immutableOptions = options.ToImmutable();
				var generation = ++root.NextGeneration;
				var session = new DexcRuntimeSession(generation, immutableOptions);
				if (immutableOptions.RuntimeUISkin == null)
				{
					session.Diagnostics.AddOperation(
						DebugDiagnosticSeverity.Warning,
						DebugFeature.InputEditor,
						"runtime-ui",
						"runtime-ui-skin-missing",
						"RuntimeUI.Skin is not assigned. The code-only shell remains available and Skin-dependent visuals are disabled.",
						"RuntimeUI.Skin",
						null
					);
				}
				root.Session = session;
			}
			finally
			{
				root.IsInitializing = false;
			}
#endif
		}

		internal static void InvalidateCurrentSession()
		{
			var root = Root;
			var previous = root.Session;
			previous?.EndCommands();
			DexcInspectorRuntime.EndSession(previous);
			DexcSettingRuntime.EndSession(previous);
			DexcDisplayRuntime.EndSession(previous);
			try
			{
				root.SessionInvalidated?.Invoke();
			}
			catch (Exception exception)
			{
				UnityEngine.Debug.LogException(exception);
			}
			previous?.CommandDrafts?.Clear();
			previous?.CommandReferences?.Clear();
			root.Session = null;
		}
	}
}
