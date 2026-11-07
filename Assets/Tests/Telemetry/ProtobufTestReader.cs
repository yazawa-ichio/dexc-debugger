using System;
using System.Collections.Generic;
using System.Text;

namespace DexcDebugger.Tests.Telemetry
{
	/// <summary>
	/// A small protobuf decoder written from the wire format's definition and sharing no code with the writer, so a mistake
	/// in the writer cannot hide in both. It rejects what a real decoder would reject: a truncated field, an over-long
	/// varint and an unknown wire type.
	/// </summary>
	internal sealed class ProtobufTestReader
	{
		internal readonly struct Field
		{
			internal readonly int Number;
			internal readonly int WireType;
			internal readonly ulong Value;
			internal readonly byte[] Bytes;

			internal Field(int number, int wireType, ulong value, byte[] bytes)
			{
				Number = number;
				WireType = wireType;
				Value = value;
				Bytes = bytes;
			}

			internal double Double => BitConverter.Int64BitsToDouble((long)Value);
			internal string Text => Encoding.UTF8.GetString(Bytes);
		}

		readonly byte[] m_Buffer;
		readonly int m_End;
		int m_Position;

		ProtobufTestReader(byte[] buffer, int count)
		{
			m_Buffer = buffer;
			m_End = count;
		}

		internal static List<Field> Read(byte[] buffer, int count)
		{
			var reader = new ProtobufTestReader(buffer, count);
			var fields = new List<Field>();
			while (reader.m_Position < reader.m_End)
			{
				fields.Add(reader.ReadField());
			}
			return fields;
		}

		internal static List<Field> Read(byte[] buffer)
		{
			return Read(buffer, buffer.Length);
		}

		Field ReadField()
		{
			var tag = ReadVarint();
			var number = (int)(tag >> 3);
			var wireType = (int)(tag & 7);
			if (number == 0)
			{
				throw new FormatException("Field number 0 is invalid.");
			}
			switch (wireType)
			{
				case 0:
					return new Field(number, wireType, ReadVarint(), null);
				case 1:
					return new Field(number, wireType, ReadFixed64(), null);
				case 2:
					var length = ReadVarint();
					if (length > (ulong)(m_End - m_Position))
					{
						throw new FormatException("A length-delimited field runs past the end.");
					}
					var bytes = new byte[(int)length];
					Array.Copy(m_Buffer, m_Position, bytes, 0, bytes.Length);
					m_Position += bytes.Length;
					return new Field(number, wireType, 0, bytes);
				default:
					throw new FormatException("Wire type " + wireType + " is not used by OTLP here.");
			}
		}

		ulong ReadVarint()
		{
			ulong value = 0;
			for (var shift = 0; shift < 70; shift += 7)
			{
				if (m_Position >= m_End)
				{
					throw new FormatException("A varint runs past the end.");
				}
				var current = m_Buffer[m_Position++];
				value |= (ulong)(current & 0x7F) << shift;
				if ((current & 0x80) == 0)
				{
					return value;
				}
			}
			throw new FormatException("A varint is longer than ten bytes.");
		}

		ulong ReadFixed64()
		{
			if (m_End - m_Position < 8)
			{
				throw new FormatException("A fixed64 runs past the end.");
			}
			ulong value = 0;
			for (var i = 0; i < 8; i++)
			{
				value |= (ulong)m_Buffer[m_Position++] << (i * 8);
			}
			return value;
		}
	}
}
