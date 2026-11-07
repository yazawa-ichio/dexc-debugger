using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Dexc.Telemetry
{
	/// <summary>
	/// The files of one run in the telemetry folder. It keeps the folder under its limit by deleting the oldest files, of
	/// this run or earlier ones: what led up to a problem is at the end. Main thread only.
	/// </summary>
	internal sealed class TelemetryFileSet
	{
		internal const string Extension = ".otlp";

		readonly string m_Directory;
		readonly string m_Run;
		readonly long m_MaximumFileBytes;
		readonly long m_MaximumDirectoryBytes;
		readonly HashSet<string> m_Open = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		internal TelemetryFileSet(string directory, string run, long maximumFileBytes, long maximumDirectoryBytes)
		{
			m_Directory = directory;
			m_Run = run;
			m_MaximumFileBytes = maximumFileBytes;
			m_MaximumDirectoryBytes = maximumDirectoryBytes;
			Directory.CreateDirectory(directory);
		}

		internal long MaximumFileBytes => m_MaximumFileBytes;

		/// <summary>Opens the first file of a signal.</summary>
		internal TelemetryFileFrames Open(string signal)
		{
			return new TelemetryFileFrames(this, signal);
		}

		/// <summary>Creates the file with this number. The first has no number, so a run that stays small has plain names.</summary>
		internal FileStream Create(string signal, int part, out string path)
		{
			var name = signal + "-" + m_Run + (part == 0 ? string.Empty : "-" + part) + Extension;
			path = Path.Combine(m_Directory, name);
			var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read | FileShare.Delete);
			m_Open.Add(path);
			Trim();
			return stream;
		}

		internal void Closed(string path)
		{
			m_Open.Remove(path);
		}

		/// <summary>Deletes the oldest closed files until the folder is under its limit.</summary>
		internal void Trim()
		{
			var files = new DirectoryInfo(m_Directory).GetFiles("*" + Extension).OrderBy(file => file.LastWriteTimeUtc).ToList();
			var total = files.Sum(file => file.Length);
			foreach (var file in files)
			{
				if (total <= m_MaximumDirectoryBytes)
				{
					return;
				}
				if (m_Open.Contains(file.FullName))
				{
					continue;
				}
				try
				{
					file.Delete();
					total -= file.Length;
				}
				catch (IOException exception)
				{
					// A file another Player still has open. The next trim tries again.
					Debug.LogWarning(exception.Message);
				}
			}
		}
	}
}
