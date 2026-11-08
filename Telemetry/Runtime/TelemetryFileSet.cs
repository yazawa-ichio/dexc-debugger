using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Dexc.Telemetry
{
	/// <summary>
	/// The files of one run in the telemetry folder. A file being written ends with <c>.otlp.open</c> and is renamed to
	/// <c>.otlp</c> when it closes, so that whoever takes files only takes whole ones. It keeps the folder under its limit
	/// by deleting the oldest closed files, of this run or earlier ones: what led up to a problem is at the end. Main
	/// thread only.
	/// </summary>
	internal sealed class TelemetryFileSet
	{
		internal const string Extension = ".otlp";
		internal const string OpenExtension = ".otlp.open";
		const string ProfilerPrefix = "capture-";
		const string ProfilerExtension = ".raw";

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
			CloseLeftovers();
		}

		internal string Folder => m_Directory;

		internal long MaximumFileBytes => m_MaximumFileBytes;

		/// <summary>Opens the first file of a signal.</summary>
		internal TelemetryFileFrames Open(string signal)
		{
			return new TelemetryFileFrames(this, signal);
		}

		/// <summary>Creates the file with this number. The first has no number, so a run that stays small has plain names.</summary>
		internal FileStream Create(string signal, int part, out string path)
		{
			var name = signal + "-" + m_Run + (part == 0 ? string.Empty : "-" + part) + OpenExtension;
			path = Path.Combine(m_Directory, name);
			var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read | FileShare.Delete);
			m_Open.Add(path);
			Trim();
			return stream;
		}

		/// <summary>The path of the Unity Profiler file of a capture. It is kept as open until <see cref="Closed"/>.</summary>
		internal string ProfilerPath(int capture)
		{
			var path = Path.Combine(m_Directory, ProfilerPrefix + m_Run + "-" + capture + ProfilerExtension);
			m_Open.Add(path);
			return path;
		}

		/// <summary>Takes a file off the open ones. A telemetry file gets its closed name.</summary>
		/// <returns>The path the file has now.</returns>
		internal string Closed(string path)
		{
			m_Open.Remove(path);
			return path.EndsWith(OpenExtension, StringComparison.OrdinalIgnoreCase) ? Rename(path) : path;
		}

		internal bool IsOpen(string path)
		{
			return m_Open.Contains(path);
		}

		/// <summary>
		/// Deletes the oldest closed files until the folder is under its limit. Unity Profiler files of captures count. A file
		/// still being written, by this Player or another, is never deleted.
		/// </summary>
		internal void Trim()
		{
			var files = new DirectoryInfo(m_Directory).GetFiles().Where(file => IsOwn(file.Name)).OrderBy(file => file.LastWriteTimeUtc).ToList();
			var total = files.Sum(file => file.Length);
			foreach (var file in files)
			{
				if (total <= m_MaximumDirectoryBytes)
				{
					return;
				}
				if (m_Open.Contains(file.FullName) || file.Name.EndsWith(OpenExtension, StringComparison.OrdinalIgnoreCase))
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

		/// <summary>Whether a file of this folder belongs to telemetry: one of its files, open or closed, or a Profiler file of a capture.</summary>
		internal static bool IsOwn(string name)
		{
			return name.EndsWith(Extension, StringComparison.OrdinalIgnoreCase)
				|| name.EndsWith(OpenExtension, StringComparison.OrdinalIgnoreCase)
				|| (name.StartsWith(ProfilerPrefix, StringComparison.OrdinalIgnoreCase) && name.EndsWith(ProfilerExtension, StringComparison.OrdinalIgnoreCase));
		}

		/// <summary>
		/// Gives its closed name to each open file that no process holds: one left by a run that ended without closing it.
		/// Another Player writing in the same folder holds its files, so they cannot be opened alone and are left.
		/// </summary>
		void CloseLeftovers()
		{
			foreach (var path in Directory.GetFiles(m_Directory, "*" + OpenExtension))
			{
				try
				{
					using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
					{
					}
				}
				catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
				{
					continue;
				}
				Rename(path);
			}
		}

		static string Rename(string path)
		{
			var closed = path.Substring(0, path.Length - OpenExtension.Length) + Extension;
			try
			{
				File.Move(path, closed);
				return closed;
			}
			catch (IOException exception)
			{
				Debug.LogWarning(exception.Message);
				return path;
			}
		}
	}
}
