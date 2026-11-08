using System.ComponentModel;

namespace Dexc.Telemetry
{
	/// <summary>
	/// What a measured method holds from its start to its end. The IL processing calls it; code does not. Outside a capture
	/// <see cref="Enter"/> reads the root without making it and returns an empty probe.
	/// </summary>
	[EditorBrowsable(EditorBrowsableState.Never)]
	public readonly struct TelemetryMethodProbe
	{
		readonly TelemetryMethods m_Owner;
		readonly int m_Index;
		readonly int m_Generation;
		readonly long m_StartTicks;
		readonly string m_Scope;
		readonly string m_Name;

		/// <summary>A call recorded on the main thread, as the span in this slot.</summary>
		internal TelemetryMethodProbe(TelemetryMethods owner, int index, int generation)
		{
			m_Owner = owner;
			m_Index = index;
			m_Generation = generation;
			m_StartTicks = 0;
			m_Scope = null;
			m_Name = null;
		}

		/// <summary>A call recorded on another thread, written when it ends.</summary>
		internal TelemetryMethodProbe(TelemetryMethods owner, string scope, string name, long startTicks)
		{
			m_Owner = owner;
			m_Index = -1;
			m_Generation = 0;
			m_StartTicks = startTicks;
			m_Scope = scope;
			m_Name = name;
		}

		public static TelemetryMethodProbe Enter(string scope, string name, TelemetryDetail detail, int every, int perFrame, bool anyThread)
		{
			var root = TelemetryRoot.Existing;
			return root == null ? default : root.Methods.Enter(scope, name, detail, every, perFrame, anyThread);
		}

		public void Exit()
		{
			if (m_Owner == null)
			{
				return;
			}
			if (m_Index >= 0)
			{
				m_Owner.Exit(m_Index, m_Generation);
			}
			else
			{
				m_Owner.ExitOtherThread(m_Scope, m_Name, m_StartTicks);
			}
		}
	}
}
