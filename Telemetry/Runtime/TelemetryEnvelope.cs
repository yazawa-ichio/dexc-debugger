using System;
using System.Collections.Generic;

namespace Dexc.Telemetry
{
	/// <summary>
	/// The encoded Resource and InstrumentationScope. They have the same field numbers in ResourceMetrics and ResourceLogs
	/// and in their scope messages, so every signal copies the same bytes.
	/// </summary>
	internal sealed class TelemetryEnvelope
	{
		internal const string ScopeName = "Dexc.Telemetry";

		/// <summary>Room for the tags and reserved lengths around a message.</summary>
		internal const int Overhead = 32;

		internal TelemetryEnvelope()
		{
			var writer = new ProtobufWriter(Overhead + ScopeName.Length);
			var scope = writer.BeginMessage(OtlpFields.ScopeField);
			writer.WriteStringField(OtlpFields.ScopeName, ScopeName);
			writer.EndMessage(scope);
			Scope = Copy(writer);
			Resource = new byte[0];
		}

		internal byte[] Resource { get; private set; }

		internal byte[] Scope { get; }

		/// <summary>Sets what every frame says about where the data comes from.</summary>
		internal void SetResource(IReadOnlyList<KeyValuePair<string, string>> attributes)
		{
			var size = Overhead;
			foreach (var pair in attributes)
			{
				size += Overhead + (pair.Key.Length + pair.Value.Length) * 3;
			}
			var writer = new ProtobufWriter(size);
			var resource = writer.BeginMessage(OtlpFields.ResourceField);
			foreach (var pair in attributes)
			{
				WriteAttribute(writer, OtlpFields.ResourceAttributes, pair.Key, pair.Value, 0, pair.Value.Length);
			}
			writer.EndMessage(resource);
			Resource = Copy(writer);
		}

		/// <summary>Writes a string attribute whose value is a part of <paramref name="value"/>.</summary>
		internal static void WriteAttribute(ProtobufWriter writer, int field, string key, string value, int start, int count)
		{
			var attribute = writer.BeginMessage(field);
			writer.WriteStringField(OtlpFields.KeyValueKey, key);
			var any = writer.BeginMessage(OtlpFields.KeyValueValue);
			writer.WriteStringField(OtlpFields.AnyValueString, value, start, count);
			writer.EndMessage(any);
			writer.EndMessage(attribute);
		}

		static byte[] Copy(ProtobufWriter writer)
		{
			var bytes = new byte[writer.Position];
			Buffer.BlockCopy(writer.Buffer, 0, bytes, 0, bytes.Length);
			return bytes;
		}
	}
}
