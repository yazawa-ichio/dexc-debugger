namespace Dexc.Telemetry
{
	/// <summary>
	/// What the loggers of one category share: the encoded scope and the level. A level change reaches every logger of the
	/// category through this one field, so a call only compares two numbers.
	/// </summary>
	internal sealed class TelemetryLogCategory
	{
		volatile int m_Level;

		internal TelemetryLogCategory(string name, TelemetrySeverity level)
		{
			Name = name;
			m_Level = (int)level;
			var writer = new ProtobufWriter(TelemetryEnvelope.Overhead + name.Length * 3);
			var scope = writer.BeginMessage(OtlpFields.ScopeField);
			writer.WriteStringField(OtlpFields.ScopeName, name);
			writer.EndMessage(scope);
			Scope = new byte[writer.Position];
			System.Buffer.BlockCopy(writer.Buffer, 0, Scope, 0, writer.Position);
		}

		internal string Name { get; }

		/// <summary>The InstrumentationScope of the records, named after the category as the official SDK does.</summary>
		internal byte[] Scope { get; }

		internal int Level => m_Level;

		internal void SetLevel(TelemetrySeverity level)
		{
			m_Level = (int)level;
		}
	}
}
