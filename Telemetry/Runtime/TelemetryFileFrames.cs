using System.IO;

namespace Dexc.Telemetry
{
	/// <summary>
	/// Frames of one signal in numbered files. A frame is never split: the next file begins between two frames. Each frame
	/// is handed to the operating system once written, so it outlives a crash of the process.
	/// </summary>
	internal sealed class TelemetryFileFrames : TelemetryFrameWriter
	{
		readonly TelemetryFileSet m_Files;
		readonly string m_Signal;
		int m_Part;

		internal TelemetryFileFrames(TelemetryFileSet files, string signal)
		{
			m_Files = files;
			m_Signal = signal;
			Stream = files.Create(signal, 0, out var path);
			Path = path;
		}

		/// <summary>The file being written, which ends with <c>.otlp.open</c>.</summary>
		internal string Path { get; private set; }

		private protected override void AfterFrame()
		{
			Stream.Flush();
			if (Stream.Length >= m_Files.MaximumFileBytes)
			{
				Rotate();
			}
		}

		/// <summary>Closes the file, which takes its closed name, and goes on in the next one.</summary>
		internal override void Rotate()
		{
			Stream.Dispose();
			m_Files.Closed(Path);
			Stream = m_Files.Create(m_Signal, ++m_Part, out var path);
			Path = path;
		}

		internal override void FlushToDisk()
		{
			((FileStream)Stream).Flush(true);
		}

		public override void Dispose()
		{
			base.Dispose();
			m_Files.Closed(Path);
		}
	}
}
