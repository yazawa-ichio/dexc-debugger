using System;

namespace Dexc.Telemetry
{
	/// <summary>
	/// Where a span sits in a trace. Pass it to <see cref="TelemetryTracer.Start"/> to make a child, also across an await,
	/// and to <see cref="TelemetryLogger.WithContext"/> to tie logs to the span. The default value is no span.
	/// </summary>
	public readonly struct TelemetryContext : IEquatable<TelemetryContext>
	{
		internal readonly ulong TraceHigh;
		internal readonly ulong TraceLow;
		internal readonly ulong SpanId;

		internal TelemetryContext(ulong traceHigh, ulong traceLow, ulong spanId)
		{
			TraceHigh = traceHigh;
			TraceLow = traceLow;
			SpanId = spanId;
		}

		public bool IsValid => SpanId != 0;

		public bool Equals(TelemetryContext other)
		{
			return TraceHigh == other.TraceHigh && TraceLow == other.TraceLow && SpanId == other.SpanId;
		}

		public override bool Equals(object obj)
		{
			return obj is TelemetryContext other && Equals(other);
		}

		public override int GetHashCode()
		{
			return SpanId.GetHashCode();
		}
	}
}
