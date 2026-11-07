namespace Dexc.Telemetry
{
	/// <summary>
	/// A frame of records on its way out, for the signals that have a list of records: LogsData and TracesData have the same
	/// shape, a resource, then scopes that hold records. Records of one scope sit together, and a change of scope starts the
	/// next one, so the order of the records stays. Main thread only.
	/// </summary>
	internal sealed class TelemetryBatch
	{
		/// <summary>The frame buffer. When a record no longer fits, the frame is written and a new one begins.</summary>
		const int BufferSize = 64 * 1024;

		internal struct Mark
		{
			internal int Position;
			internal object Scope;
			internal int ScopeStart;
		}

		readonly TelemetryEnvelope m_Envelope;
		ProtobufWriter m_Writer;
		int m_ResourceStart;
		int m_ScopeStart;
		object m_Scope;
		int m_Records;

		internal TelemetryBatch(TelemetryEnvelope envelope)
		{
			m_Envelope = envelope;
		}

		internal ProtobufWriter Writer => m_Writer;

		internal bool HasOverflowed => m_Writer.HasOverflowed;

		/// <summary>Gets ready for a record of this scope.</summary>
		/// <returns>The mark to go back to when the record does not fit.</returns>
		internal Mark Enter(object scope, byte[] scopeBytes)
		{
			if (m_Records == 0)
			{
				Begin();
			}
			var mark = new Mark { Position = m_Writer.Position, Scope = m_Scope, ScopeStart = m_ScopeStart };
			if (m_Scope != scope)
			{
				if (m_Scope != null)
				{
					m_Writer.EndMessage(m_ScopeStart);
				}
				m_ScopeStart = m_Writer.BeginMessage(OtlpFields.BatchScope);
				m_Writer.WriteRaw(scopeBytes, scopeBytes.Length);
				m_Scope = scope;
			}
			return mark;
		}

		/// <summary>Takes back what came after the mark.</summary>
		internal void Rollback(in Mark mark)
		{
			m_Writer.Truncate(mark.Position);
			m_Scope = mark.Scope;
			m_ScopeStart = mark.ScopeStart;
		}

		/// <summary>Counts the record just written as part of the frame.</summary>
		internal void Commit()
		{
			m_Records++;
		}

		/// <summary>Writes the records so far as one frame. Without any there is no frame.</summary>
		internal void Flush(TelemetryFrameWriter frames)
		{
			if (m_Records == 0)
			{
				return;
			}
			m_Writer.EndMessage(m_ScopeStart);
			m_Writer.EndMessage(m_ResourceStart);
			m_Records = 0;
			frames.WriteFrame(m_Writer.Buffer, m_Writer.Position);
			frames.Flush();
		}

		void Begin()
		{
			m_Writer = m_Writer ?? new ProtobufWriter(BufferSize);
			m_Writer.Reset();
			m_Scope = null;
			m_ResourceStart = m_Writer.BeginMessage(OtlpFields.BatchResource);
			m_Writer.WriteRaw(m_Envelope.Resource, m_Envelope.Resource.Length);
		}
	}
}
