using System;
using System.IO;

namespace Dexc.Telemetry
{
	/// <summary>
	/// Appends messages to a stream as frames: a four byte big-endian length, then the message. This is the framing of the
	/// OpenTelemetry Collector file exporter's proto format. A frame carries no signal type, so one stream holds one signal.
	/// A reader that finds a frame cut short by a crash drops it and keeps the frames before it. Main thread only.
	/// </summary>
	internal sealed class TelemetryFrameWriter : IDisposable
	{
		const int HeaderSize = 4;

		readonly Stream m_Stream;
		readonly byte[] m_Header = new byte[HeaderSize];

		/// <param name="stream">The stream to append to. This writer owns it and disposes it.</param>
		internal TelemetryFrameWriter(Stream stream)
		{
			m_Stream = stream ?? throw new ArgumentNullException(nameof(stream));
		}

		internal void WriteFrame(byte[] message, int count)
		{
			m_Header[0] = (byte)(count >> 24);
			m_Header[1] = (byte)(count >> 16);
			m_Header[2] = (byte)(count >> 8);
			m_Header[3] = (byte)count;
			m_Stream.Write(m_Header, 0, HeaderSize);
			m_Stream.Write(message, 0, count);
		}

		internal void Flush()
		{
			m_Stream.Flush();
		}

		public void Dispose()
		{
			m_Stream.Dispose();
		}
	}
}
