using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Dexc.Telemetry.Cli
{
	/// <summary>
	/// The files the package writes: frames of a four byte big-endian length and the message, one signal per file, the
	/// signal first in the name. A file still being written ends with <c>.otlp.open</c>; its last frame may be cut short,
	/// and a frame cut short is left out.
	/// </summary>
	internal static class TelemetryFiles
	{
		internal const string Extension = ".otlp";
		internal const string OpenExtension = ".otlp.open";
		internal static readonly string[] Signals = { "metrics", "logs", "traces" };

		/// <summary>The signal of a file by its name, or null for a file that is not telemetry.</summary>
		internal static string SignalOf(string path)
		{
			var name = Path.GetFileName(path);
			if (!name.EndsWith(Extension, StringComparison.OrdinalIgnoreCase) && !name.EndsWith(OpenExtension, StringComparison.OrdinalIgnoreCase))
			{
				return null;
			}
			return Signals.FirstOrDefault(signal => name.StartsWith(signal + "-", StringComparison.Ordinal));
		}

		/// <summary>The telemetry files of a file or a folder, oldest first. The open ones only when asked for.</summary>
		internal static List<string> Find(string path, bool open)
		{
			var paths = Directory.Exists(path) ? Directory.GetFiles(path) : new[] { path };
			return paths
				.Where(file => SignalOf(file) != null && (open || !file.EndsWith(OpenExtension, StringComparison.OrdinalIgnoreCase)))
				.OrderBy(File.GetLastWriteTimeUtc)
				.ToList();
		}

		internal static List<byte[]> ReadFrames(string path)
		{
			var data = File.ReadAllBytes(path);
			var frames = new List<byte[]>();
			var position = 0;
			while (position + 4 <= data.Length)
			{
				var length = (data[position] << 24) | (data[position + 1] << 16) | (data[position + 2] << 8) | data[position + 3];
				if (length < 0 || position + 4 + length > data.Length)
				{
					break;
				}
				frames.Add(data.AsSpan(position + 4, length).ToArray());
				position += 4 + length;
			}
			return frames;
		}

		internal static void WriteFrames(string path, IEnumerable<byte[]> frames)
		{
			using (var stream = File.Create(path))
			{
				foreach (var frame in frames)
				{
					stream.WriteByte((byte)(frame.Length >> 24));
					stream.WriteByte((byte)(frame.Length >> 16));
					stream.WriteByte((byte)(frame.Length >> 8));
					stream.WriteByte((byte)frame.Length);
					stream.Write(frame, 0, frame.Length);
				}
			}
		}

		internal static DateTime Time(ulong unixNanoseconds)
		{
			return DateTime.UnixEpoch.AddTicks((long)(unixNanoseconds / 100));
		}
	}
}
