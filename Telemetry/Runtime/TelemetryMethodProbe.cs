using System.ComponentModel;

namespace Dexc.Telemetry
{
	/// <summary>
	/// What a measured method holds from its start to its end. The IL processing calls it; code does not. When nothing is
	/// recorded the entry reads the root without making it and returns an empty probe. Small on purpose, as every measured
	/// call copies it: the owner and a token, by which the owner keeps the rest of the call.
	/// </summary>
	[EditorBrowsable(EditorBrowsableState.Never)]
	public readonly struct TelemetryMethodProbe
	{
		readonly TelemetryMethods m_Owner;
		readonly int m_Token;

		/// <param name="token">A call on the main thread: its slot and generation. On another thread: below zero.</param>
		internal TelemetryMethodProbe(TelemetryMethods owner, int token)
		{
			m_Owner = owner;
			m_Token = token;
		}

		/// <summary>A method with <see cref="TelemetryMethodAttribute"/>, recorded during a capture.</summary>
		public static TelemetryMethodProbe Enter(string scope, string name, TelemetryDetail detail, int every, int perFrame, bool anyThread)
		{
			var root = TelemetryRoot.Existing;
			return root == null ? default : root.Methods.Enter(scope, name, detail, every, perFrame, anyThread, false);
		}

		/// <summary>
		/// The start of an async method, from its first call to its completion. Its span is not a parent of the calls that
		/// follow: after the first await the caller goes on with other work.
		/// </summary>
		public static TelemetryMethodProbe EnterAsync(string scope, string name, TelemetryDetail detail, int every, int perFrame, bool anyThread)
		{
			var root = TelemetryRoot.Existing;
			return root == null ? default : root.Methods.Enter(scope, name, detail, every, perFrame, anyThread, true);
		}

		/// <summary>A method the profile chose, recorded while <see cref="DexcTelemetry.Watch(string)"/> covers it.</summary>
		public static TelemetryMethodProbe EnterWatched(string scope, string name, TelemetryDetail detail, int every, int perFrame, bool anyThread)
		{
			var root = TelemetryRoot.Existing;
			return root == null ? default : root.Methods.EnterWatched(scope, name, every, perFrame, anyThread, false);
		}

		/// <inheritdoc cref="EnterWatched"/>
		public static TelemetryMethodProbe EnterWatchedAsync(string scope, string name, TelemetryDetail detail, int every, int perFrame, bool anyThread)
		{
			var root = TelemetryRoot.Existing;
			return root == null ? default : root.Methods.EnterWatched(scope, name, every, perFrame, anyThread, true);
		}

		public void Exit()
		{
			m_Owner?.Exit(m_Token);
		}
	}
}
