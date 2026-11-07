using System.IO;

namespace Dexc.Telemetry
{
	/// <summary>Frames of one signal in numbered files. A frame is never split: the next file begins between two frames.</summary>
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

		/// <summary>The file being written.</summary>
		internal string Path { get; private set; }

		private protected override void AfterFrame()
		{
			if (Stream.Length < m_Files.MaximumFileBytes)
			{
				return;
			}
			Stream.Dispose();
			m_Files.Closed(Path);
			Stream = m_Files.Create(m_Signal, ++m_Part, out var path);
			Path = path;
		}

		public override void Dispose()
		{
			base.Dispose();
			m_Files.Closed(Path);
		}
	}
}
