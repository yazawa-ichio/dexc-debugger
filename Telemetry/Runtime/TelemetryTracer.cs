namespace Dexc.Telemetry
{
	/// <summary>Starts spans of one scope. Make it once with <see cref="DexcTelemetry.Tracer"/> and keep it. Main thread only.</summary>
	public sealed class TelemetryTracer
	{
		readonly TelemetryTraces m_Traces;

		internal TelemetryTracer(TelemetryTraces traces, string name)
		{
			m_Traces = traces;
			Name = name;
			var writer = new ProtobufWriter(TelemetryEnvelope.Overhead + name.Length * 3);
			var scope = writer.BeginMessage(OtlpFields.ScopeField);
			writer.WriteStringField(OtlpFields.ScopeName, name);
			writer.EndMessage(scope);
			Scope = new byte[writer.Position];
			System.Buffer.BlockCopy(writer.Buffer, 0, Scope, 0, writer.Position);
		}

		public string Name { get; }

		internal byte[] Scope { get; }

		/// <summary>
		/// Starts a span. Spans are for stretches such as a stage or a load, not for every frame. A span that is not recorded
		/// comes back empty: telemetry is not running, the detail is above the level, or too many spans are open.
		/// </summary>
		/// <param name="name">The kind of work. What varies goes in <see cref="TelemetrySpan.Set"/>.</param>
		/// <param name="parent">The parent. Without one the span starts a new trace, or follows the ambient context if that is on.</param>
		/// <param name="detail">How fine the span is.</param>
		public TelemetrySpan Start(string name, TelemetryContext parent = default, TelemetryDetail detail = TelemetryDetail.Standard)
		{
			return m_Traces.Start(this, name, parent, detail);
		}
	}
}
