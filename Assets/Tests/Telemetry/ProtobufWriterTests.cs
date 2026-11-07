using System;
using System.Collections.Generic;
using System.Linq;
using Dexc.Telemetry;
using NUnit.Framework;

namespace DexcDebugger.Tests.Telemetry
{
	public sealed class ProtobufWriterTests
	{
		static byte[] Written(ProtobufWriter writer)
		{
			return writer.Buffer.Take(writer.Position).ToArray();
		}

		[TestCase(0UL, new byte[] { 0x00 })]
		[TestCase(1UL, new byte[] { 0x01 })]
		[TestCase(127UL, new byte[] { 0x7F })]
		[TestCase(128UL, new byte[] { 0x80, 0x01 })]
		[TestCase(300UL, new byte[] { 0xAC, 0x02 })]
		[TestCase(ulong.MaxValue, new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x01 })]
		public void VarintMatchesTheWireFormat(ulong value, byte[] expected)
		{
			var writer = new ProtobufWriter(16);
			writer.WriteVarint(value);
			Assert.That(Written(writer), Is.EqualTo(expected));
		}

		[Test]
		public void TagsCarryTheFieldNumberAndWireType()
		{
			var writer = new ProtobufWriter(16);
			writer.WriteTag(1, ProtobufWriter.WireVarint);
			writer.WriteTag(2, ProtobufWriter.WireLengthDelimited);
			writer.WriteTag(16, ProtobufWriter.WireFixed64);
			Assert.That(Written(writer), Is.EqualTo(new byte[] { 0x08, 0x12, 0x81, 0x01 }));
		}

		[Test]
		public void Fixed64IsLittleEndian()
		{
			var writer = new ProtobufWriter(16);
			writer.WriteFixed64(0x0102030405060708UL);
			Assert.That(Written(writer), Is.EqualTo(new byte[] { 0x08, 0x07, 0x06, 0x05, 0x04, 0x03, 0x02, 0x01 }));
		}

		[Test]
		public void DoubleIsItsIeeeBitsInLittleEndian()
		{
			var writer = new ProtobufWriter(16);
			writer.WriteDouble(1.0);
			Assert.That(Written(writer), Is.EqualTo(new byte[] { 0, 0, 0, 0, 0, 0, 0xF0, 0x3F }));
		}

		[Test]
		public void StringsAreUtf8WithTheirByteLength()
		{
			var writer = new ProtobufWriter(16);
			writer.WriteStringField(1, "aあ");
			Assert.That(Written(writer), Is.EqualTo(new byte[] { 0x0A, 0x04, 0x61, 0xE3, 0x81, 0x82 }));
		}

		[Test]
		public void EmptyStringKeepsItsField()
		{
			var writer = new ProtobufWriter(16);
			writer.WriteStringField(1, string.Empty);
			Assert.That(Written(writer), Is.EqualTo(new byte[] { 0x0A, 0x00 }));
		}

		[Test]
		public void PackedFixed64IsOneFieldOfConsecutiveValues()
		{
			var writer = new ProtobufWriter(32);
			writer.WritePackedFixed64Field(6, new ulong[] { 1, 2, 99 }, 2);
			Assert.That(
				Written(writer),
				Is.EqualTo(new byte[] { 0x32, 0x10, 1, 0, 0, 0, 0, 0, 0, 0, 2, 0, 0, 0, 0, 0, 0, 0 })
			);
		}

		[Test]
		public void PackedDoubleIsOneFieldOfConsecutiveValues()
		{
			var writer = new ProtobufWriter(32);
			writer.WritePackedDoubleField(7, new[] { 1.0 }, 1);
			Assert.That(Written(writer), Is.EqualTo(new byte[] { 0x3A, 0x08, 0, 0, 0, 0, 0, 0, 0xF0, 0x3F }));
		}

		[Test]
		public void BytesAndRawCopyOnlyTheGivenCount()
		{
			var writer = new ProtobufWriter(16);
			writer.WriteBytesField(1, new byte[] { 7, 8, 9 }, 2);
			writer.WriteRaw(new byte[] { 5, 6 }, 1);
			Assert.That(Written(writer), Is.EqualTo(new byte[] { 0x0A, 0x02, 7, 8, 5 }));
		}

		[Test]
		public void ReservedLengthIsAPaddedVarintOfTheMessageSize()
		{
			var writer = new ProtobufWriter(16);
			var mark = writer.BeginMessage(1);
			writer.WriteVarintField(1, 150);
			writer.EndMessage(mark);
			Assert.That(Written(writer), Is.EqualTo(new byte[] { 0x0A, 0x83, 0x80, 0x80, 0x00, 0x08, 0x96, 0x01 }));
		}

		[TestCase(0)]
		[TestCase(127)]
		[TestCase(128)]
		[TestCase(16384)]
		[TestCase(70000)]
		public void ReservedLengthDecodesForAnySize(int size)
		{
			var writer = new ProtobufWriter(size + 16);
			var mark = writer.BeginMessage(3);
			writer.WriteRaw(new byte[size], size);
			writer.EndMessage(mark);

			var fields = ProtobufTestReader.Read(writer.Buffer, writer.Position);

			Assert.That(fields.Count, Is.EqualTo(1));
			Assert.That(fields[0].Number, Is.EqualTo(3));
			Assert.That(fields[0].Bytes.Length, Is.EqualTo(size));
		}

		[Test]
		public void NestedMessagesDecodeLevelByLevel()
		{
			var writer = new ProtobufWriter(128);
			var outer = writer.BeginMessage(1);
			var inner = writer.BeginMessage(2);
			writer.WriteStringField(1, "bench");
			writer.WriteDoubleField(4, 16.5);
			writer.EndMessage(inner);
			writer.WriteFixed64Field(3, 42);
			writer.EndMessage(outer);

			var top = ProtobufTestReader.Read(writer.Buffer, writer.Position);
			var middle = ProtobufTestReader.Read(top.Single().Bytes);
			var bottom = ProtobufTestReader.Read(middle[0].Bytes);

			Assert.That(middle[1].Number, Is.EqualTo(3));
			Assert.That(middle[1].Value, Is.EqualTo(42UL));
			Assert.That(bottom[0].Text, Is.EqualTo("bench"));
			Assert.That(bottom[1].Double, Is.EqualTo(16.5));
		}

		[Test]
		public void AWriteThatDoesNotFitIsDroppedAndFlagged()
		{
			var writer = new ProtobufWriter(4);
			writer.WriteVarint(1);
			Assert.That(writer.HasOverflowed, Is.False);

			writer.WriteFixed64(1);

			Assert.That(writer.HasOverflowed, Is.True);
			Assert.That(writer.Position, Is.EqualTo(1), "nothing of the failed write stays");
		}

		[Test]
		public void AfterAnOverflowEveryWriteIsDroppedEvenIfItWouldFit()
		{
			var writer = new ProtobufWriter(4);
			writer.WriteFixed64(1);

			writer.WriteVarint(1);

			Assert.That(writer.Position, Is.Zero, "a message with a hole in it must not look complete");
		}

		[Test]
		public void AnOverflowInsideAMessageDoesNotThrow()
		{
			var writer = new ProtobufWriter(8);
			var mark = writer.BeginMessage(1);
			writer.WriteFixed64Field(1, 1);
			writer.EndMessage(mark);
			Assert.That(writer.HasOverflowed, Is.True);
		}

		[Test]
		public void AMessageWithoutRoomForItsLengthDoesNotThrow()
		{
			var writer = new ProtobufWriter(2);
			var mark = writer.BeginMessage(1);
			writer.EndMessage(mark);
			Assert.That(writer.HasOverflowed, Is.True);
		}

		[Test]
		public void ResetClearsThePositionAndTheOverflow()
		{
			var writer = new ProtobufWriter(4);
			writer.WriteFixed64(1);

			writer.Reset();
			writer.WriteVarint(5);

			Assert.That(writer.HasOverflowed, Is.False);
			Assert.That(Written(writer), Is.EqualTo(new byte[] { 5 }));
		}

		[TestCase(0)]
		[TestCase(-1)]
		[TestCase(ProtobufWriter.MaximumCapacity + 1)]
		public void ACapacityOutOfRangeIsRefused(int capacity)
		{
			Assert.Throws<ArgumentOutOfRangeException>(() => new ProtobufWriter(capacity));
		}

		[Test]
		public void WritingDoesNotAllocate()
		{
			var writer = new ProtobufWriter(4096);
			var counts = new ulong[] { 1, 2, 3 };
			var bounds = new[] { 1.0, 2.0 };
			var raw = new byte[] { 1, 2, 3 };
			const string text = "frame.time";
			var cases = new Dictionary<string, Action>
			{
				["message"] = () => writer.EndMessage(writer.BeginMessage(1)),
				["varint"] = () => writer.WriteVarintField(2, 300),
				["fixed64"] = () => writer.WriteFixed64Field(3, 1),
				["double"] = () => writer.WriteDoubleField(4, 16.5),
				["string"] = () => writer.WriteStringField(1, text),
				["bytes"] = () => writer.WriteBytesField(1, raw, raw.Length),
				["packed fixed64"] = () => writer.WritePackedFixed64Field(6, counts, counts.Length),
				["packed double"] = () => writer.WritePackedDoubleField(7, bounds, bounds.Length),
			};

			foreach (var pair in cases)
			{
				TestDelegate write = () =>
				{
					writer.Reset();
					pair.Value();
				};
				// The first call compiles the code, so only a later one says what writing itself costs.
				write();
				AllocationCheck.AssertNone(write, pair.Key);
			}
		}

		[Test]
		public void TheAllocationCheckSeesAnAllocation()
		{
			TestDelegate allocate = () => GC.KeepAlive(new byte[16]);
			allocate();
			AllocationCheck.AssertSome(allocate);
		}
	}
}
