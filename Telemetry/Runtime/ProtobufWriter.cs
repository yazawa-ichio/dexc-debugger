using System;
using System.Text;

namespace Dexc.Telemetry
{
	/// <summary>
	/// Writes the protobuf wire format into a buffer it owns, without allocating. Telemetry must never take the game down,
	/// so a write that does not fit is dropped and <see cref="HasOverflowed"/> stays set until <see cref="Reset"/>; the caller
	/// throws away such a message instead of checking every write. Main thread only.
	/// </summary>
	internal sealed class ProtobufWriter
	{
		internal const int WireVarint = 0;
		internal const int WireFixed64 = 1;
		internal const int WireLengthDelimited = 2;

		/// <summary>A reserved length is this many bytes, whatever the message turns out to be.</summary>
		const int ReservedLengthSize = 4;

		/// <summary>The largest length a reserved four byte varint can hold.</summary>
		internal const int MaximumCapacity = (1 << 28) - 1;

		readonly byte[] m_Buffer;
		readonly UTF8Encoding m_Encoding = new UTF8Encoding(false);
		int m_Position;

		internal ProtobufWriter(int capacity)
		{
			if (capacity <= 0 || capacity > MaximumCapacity)
			{
				throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "The capacity must be from 1 to " + MaximumCapacity + ".");
			}
			m_Buffer = new byte[capacity];
		}

		internal byte[] Buffer => m_Buffer;

		/// <summary>The number of bytes written so far. Also where the next write goes.</summary>
		internal int Position => m_Position;

		/// <summary>True when a write did not fit. What was written since the last <see cref="Reset"/> is not a whole message.</summary>
		internal bool HasOverflowed { get; private set; }

		internal void Reset()
		{
			m_Position = 0;
			HasOverflowed = false;
		}

		internal void WriteTag(int field, int wireType)
		{
			WriteVarint((ulong)(((uint)field << 3) | (uint)wireType));
		}

		internal void WriteVarint(ulong value)
		{
			if (!Reserve(VarintSize(value)))
			{
				return;
			}
			while (value >= 0x80)
			{
				m_Buffer[m_Position++] = (byte)(value | 0x80);
				value >>= 7;
			}
			m_Buffer[m_Position++] = (byte)value;
		}

		internal void WriteFixed64(ulong value)
		{
			if (!Reserve(8))
			{
				return;
			}
			PutFixed64(value);
		}

		internal void WriteDouble(double value)
		{
			WriteFixed64((ulong)BitConverter.DoubleToInt64Bits(value));
		}

		internal void WriteVarintField(int field, ulong value)
		{
			WriteTag(field, WireVarint);
			WriteVarint(value);
		}

		internal void WriteFixed64Field(int field, ulong value)
		{
			WriteTag(field, WireFixed64);
			WriteFixed64(value);
		}

		internal void WriteDoubleField(int field, double value)
		{
			WriteTag(field, WireFixed64);
			WriteDouble(value);
		}

		internal void WriteStringField(int field, string value)
		{
			WriteTag(field, WireLengthDelimited);
			var length = m_Encoding.GetByteCount(value);
			WriteVarint((ulong)length);
			if (!Reserve(length))
			{
				return;
			}
			m_Position += m_Encoding.GetBytes(value, 0, value.Length, m_Buffer, m_Position);
		}

		internal void WriteBytesField(int field, byte[] value, int count)
		{
			WriteTag(field, WireLengthDelimited);
			WriteVarint((ulong)count);
			WriteRaw(value, count);
		}

		/// <summary>Copies bytes that are already encoded, such as a part made once when a metric was registered.</summary>
		internal void WriteRaw(byte[] value, int count)
		{
			if (!Reserve(count))
			{
				return;
			}
			System.Buffer.BlockCopy(value, 0, m_Buffer, m_Position, count);
			m_Position += count;
		}

		internal void WritePackedFixed64Field(int field, ulong[] values, int count)
		{
			WriteTag(field, WireLengthDelimited);
			WriteVarint((ulong)count * 8);
			if (!Reserve(count * 8))
			{
				return;
			}
			for (var i = 0; i < count; i++)
			{
				PutFixed64(values[i]);
			}
		}

		internal void WritePackedDoubleField(int field, double[] values, int count)
		{
			WriteTag(field, WireLengthDelimited);
			WriteVarint((ulong)count * 8);
			if (!Reserve(count * 8))
			{
				return;
			}
			for (var i = 0; i < count; i++)
			{
				PutFixed64((ulong)BitConverter.DoubleToInt64Bits(values[i]));
			}
		}

		/// <summary>
		/// Starts a nested message. Its length is not known yet, so four bytes are kept for it and <see cref="EndMessage"/>
		/// fills them in as a padded varint, which every decoder reads the same as the shortest form.
		/// </summary>
		/// <returns>The mark to give to <see cref="EndMessage"/>.</returns>
		internal int BeginMessage(int field)
		{
			WriteTag(field, WireLengthDelimited);
			var mark = m_Position;
			Reserve(ReservedLengthSize);
			if (!HasOverflowed)
			{
				m_Position += ReservedLengthSize;
			}
			return mark;
		}

		internal void EndMessage(int mark)
		{
			if (HasOverflowed)
			{
				return;
			}
			var length = m_Position - mark - ReservedLengthSize;
			m_Buffer[mark] = (byte)(length | 0x80);
			m_Buffer[mark + 1] = (byte)((length >> 7) | 0x80);
			m_Buffer[mark + 2] = (byte)((length >> 14) | 0x80);
			m_Buffer[mark + 3] = (byte)((length >> 21) & 0x7F);
		}

		static int VarintSize(ulong value)
		{
			var size = 1;
			while (value >= 0x80)
			{
				value >>= 7;
				size++;
			}
			return size;
		}

		void PutFixed64(ulong value)
		{
			for (var i = 0; i < 8; i++)
			{
				m_Buffer[m_Position++] = (byte)value;
				value >>= 8;
			}
		}

		bool Reserve(int count)
		{
			if (HasOverflowed)
			{
				return false;
			}
			if (count > m_Buffer.Length - m_Position)
			{
				HasOverflowed = true;
				return false;
			}
			return true;
		}
	}
}
