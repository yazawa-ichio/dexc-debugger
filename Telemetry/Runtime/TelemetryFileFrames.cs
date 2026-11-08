using System.IO;

namespace Dexc.Telemetry
{
	/// <summary>
	/// Frames of one signal in numbered files. A frame is never split: the next file begins between two frames. The main
	/// thread only copies a frame into a borrowed buffer; <see cref="TelemetryFileWriting"/> writes it on its own thread and
	/// hands it to the operating system, so that it outlives a crash of the process.
	/// </summary>
	internal sealed class TelemetryFileFrames : TelemetryFrameWriter
	{
		readonly TelemetryFileSet m_Files;
		readonly string m_Signal;
		volatile string m_Path;
		int m_Part;

		internal TelemetryFileFrames(TelemetryFileSet files, string signal)
		{
			m_Files = files;
			m_Signal = signal;
			Stream = files.Create(signal, 0, out var path);
			m_Path = path;
		}

		/// <summary>The file being written, which ends with <c>.otlp.open</c>. It changes on the writing thread.</summary>
		internal string Path => m_Path;

		/// <summary>Copies the frame and leaves the writing to the thread. Dropped when too much waits already.</summary>
		internal override void WriteFrame(byte[] message, int count)
		{
			var length = HeaderSize + count;
			var buffer = m_Files.Writing.Rent(length);
			if (buffer == null)
			{
				return;
			}
			WriteHeader(buffer, count);
			System.Buffer.BlockCopy(message, 0, buffer, HeaderSize, count);
			m_Files.Writing.Write(this, buffer, length);
		}

		/// <summary>Nothing to do: each frame reaches the operating system as soon as the thread writes it.</summary>
		internal override void Flush()
		{
		}

		internal override void Rotate()
		{
			m_Files.Writing.Rotate(this);
		}

		internal override void FlushToDisk()
		{
			m_Files.Writing.FlushToDisk(this);
		}

		public override void Dispose()
		{
			m_Files.Writing.Close(this);
		}

		internal void WriteNow(byte[] buffer, int length)
		{
			Stream.Write(buffer, 0, length);
			Stream.Flush();
			if (Stream.Length >= m_Files.MaximumFileBytes)
			{
				RotateNow();
			}
		}

		/// <summary>Closes the file, which takes its closed name, and goes on in the next one.</summary>
		internal void RotateNow()
		{
			Stream.Dispose();
			m_Files.Closed(m_Path);
			Stream = m_Files.Create(m_Signal, ++m_Part, out var path);
			m_Path = path;
		}

		internal void FlushToDiskNow()
		{
			((FileStream)Stream).Flush(true);
		}

		internal void CloseNow()
		{
			Stream.Dispose();
			m_Files.Closed(m_Path);
		}
	}
}
