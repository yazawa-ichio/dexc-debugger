using System;
using System.Diagnostics;

namespace Dexc.Telemetry
{
	/// <summary>
	/// The time of every record: the wall clock when the root was made, carried on by the high resolution counter. In a
	/// Windows IL2CPP Player <c>DateTime.UtcNow</c> moved in steps of about a millisecond, which a span of a method cannot
	/// use. Any thread.
	/// </summary>
	internal sealed class TelemetryClock
	{
		readonly long m_StartTicks;
		readonly long m_StartTimestamp;
		readonly double m_TicksPerTimestamp;

		internal TelemetryClock()
		{
			m_StartTicks = DateTime.UtcNow.Ticks;
			m_StartTimestamp = Stopwatch.GetTimestamp();
			m_TicksPerTimestamp = (double)TimeSpan.TicksPerSecond / Stopwatch.Frequency;
		}

		/// <summary>Now, in the ticks of <see cref="DateTime"/>, UTC.</summary>
		internal long UtcTicks => m_StartTicks + (long)((Stopwatch.GetTimestamp() - m_StartTimestamp) * m_TicksPerTimestamp);
	}
}
