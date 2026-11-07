using System;

namespace Dexc.Telemetry
{
	/// <summary>
	/// A stretch of work, open until <see cref="Dispose"/>. It is a handle, so it allocates nothing and can be kept across an
	/// await. A span that was not recorded does nothing. Main thread only: the end of a span must come back to it.
	/// </summary>
	public readonly struct TelemetrySpan : IDisposable
	{
		readonly TelemetryTraces m_Traces;
		readonly int m_Index;
		readonly int m_Generation;

		internal TelemetrySpan(TelemetryTraces traces, int index, int generation, TelemetryContext context)
		{
			m_Traces = traces;
			m_Index = index;
			m_Generation = generation;
			Context = context;
		}

		/// <summary>Where this span sits. Invalid when it is not recorded.</summary>
		public TelemetryContext Context { get; }

		/// <summary>Sets an attribute. A span keeps four. Those beyond are counted as dropped.</summary>
		public void Set(string key, TelemetryValue value)
		{
			m_Traces?.SetAttribute(m_Index, m_Generation, key, value);
		}

		/// <summary>Marks the span as failed.</summary>
		public void SetError(string message)
		{
			m_Traces?.SetError(m_Index, m_Generation, message);
		}

		/// <summary>Ends the span. Ending it twice does nothing.</summary>
		public void Dispose()
		{
			m_Traces?.End(m_Index, m_Generation);
		}
	}
}
