using System;
using System.Collections.Generic;
using System.IO;
using Dexc.Telemetry;
using NUnit.Framework;

namespace DexcDebugger.Tests.Telemetry
{
	public sealed class TelemetryFrameWriterTests
	{
		/// <summary>Reads frames as a tool would: a frame cut short ends the file, and the frames before it stay.</summary>
		static List<byte[]> ReadFrames(byte[] file)
		{
			var frames = new List<byte[]>();
			var position = 0;
			while (file.Length - position >= 4)
			{
				var length = (file[position] << 24) | (file[position + 1] << 16) | (file[position + 2] << 8) | file[position + 3];
				position += 4;
				if (file.Length - position < length)
				{
					break;
				}
				var frame = new byte[length];
				Array.Copy(file, position, frame, 0, length);
				position += length;
				frames.Add(frame);
			}
			return frames;
		}

		[Test]
		public void AFrameIsABigEndianLengthAndTheMessage()
		{
			var stream = new MemoryStream();
			var writer = new TelemetryFrameWriter(stream);

			writer.WriteFrame(new byte[] { 1, 2, 3, 99 }, 3);

			Assert.That(stream.ToArray(), Is.EqualTo(new byte[] { 0, 0, 0, 3, 1, 2, 3 }));
		}

		[Test]
		public void TheLengthUsesAllFourBytes()
		{
			var stream = new MemoryStream();
			var writer = new TelemetryFrameWriter(stream);

			writer.WriteFrame(new byte[0x010203], 0x010203);

			var bytes = stream.ToArray();
			Assert.That(new[] { bytes[0], bytes[1], bytes[2], bytes[3] }, Is.EqualTo(new byte[] { 0, 1, 2, 3 }));
		}

		[Test]
		public void FramesFollowOneAnother()
		{
			var stream = new MemoryStream();
			var writer = new TelemetryFrameWriter(stream);

			writer.WriteFrame(new byte[] { 1 }, 1);
			writer.WriteFrame(new byte[0], 0);
			writer.WriteFrame(new byte[] { 2, 3 }, 2);

			var frames = ReadFrames(stream.ToArray());
			Assert.That(frames.Count, Is.EqualTo(3));
			Assert.That(frames[0], Is.EqualTo(new byte[] { 1 }));
			Assert.That(frames[1], Is.Empty);
			Assert.That(frames[2], Is.EqualTo(new byte[] { 2, 3 }));
		}

		[Test]
		public void AFrameCutShortLeavesTheEarlierFramesReadable()
		{
			var stream = new MemoryStream();
			var writer = new TelemetryFrameWriter(stream);
			writer.WriteFrame(new byte[] { 1 }, 1);
			writer.WriteFrame(new byte[] { 2, 3, 4 }, 3);

			var whole = stream.ToArray();
			var cut = new byte[whole.Length - 1];
			Array.Copy(whole, cut, cut.Length);

			Assert.That(ReadFrames(cut).Count, Is.EqualTo(1));
		}

		[Test]
		public void TheStreamIsRequired()
		{
			Assert.Throws<ArgumentNullException>(() => new TelemetryFrameWriter(null));
		}

		[Test]
		public void DisposingClosesTheStream()
		{
			var stream = new MemoryStream();

			new TelemetryFrameWriter(stream).Dispose();

			Assert.That(stream.CanWrite, Is.False);
		}
	}
}
