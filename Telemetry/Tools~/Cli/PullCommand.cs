using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Dexc.Telemetry.Cli
{
	/// <summary>
	/// Takes the closed files of a Player by the Pull rules of the specification: the <c>.otlp</c> files and the Profiler
	/// files of captures, never one still open. The one that takes deletes, unless asked to keep them.
	/// </summary>
	internal static class PullCommand
	{
		const string ProfilerPrefix = "capture-";
		const string ProfilerExtension = ".raw";

		/// <summary>Whether a file of the folder is closed and belongs to telemetry.</summary>
		internal static bool IsTaken(string name)
		{
			return name.EndsWith(TelemetryFiles.Extension, StringComparison.OrdinalIgnoreCase)
				|| (name.StartsWith(ProfilerPrefix, StringComparison.OrdinalIgnoreCase) && name.EndsWith(ProfilerExtension, StringComparison.OrdinalIgnoreCase));
		}

		/// <summary>From a folder this machine can reach: a Windows Player or the Editor.</summary>
		internal static int FromFolder(string from, string to, bool keep)
		{
			Directory.CreateDirectory(to);
			var taken = 0;
			foreach (var path in Directory.GetFiles(from).Where(path => IsTaken(Path.GetFileName(path))))
			{
				var target = Path.Combine(to, Path.GetFileName(path));
				File.Copy(path, target, true);
				if (!keep)
				{
					File.Delete(path);
				}
				taken++;
			}
			Console.WriteLine("took " + taken + " files" + (keep ? ", kept" : ", deleted") + " in " + from);
			return 0;
		}

		/// <summary>From an Android device through adb. The folder is persistentDataPath, which adb can read.</summary>
		internal static int FromAdb(string package, string serial, string to, bool keep)
		{
			Directory.CreateDirectory(to);
			var folder = "/sdcard/Android/data/" + package + "/files/Telemetry";
			var listing = Adb(serial, "shell ls " + folder);
			if (listing.Code != 0)
			{
				Console.Error.WriteLine("adb could not list " + folder + ": " + listing.Output.Trim());
				return 1;
			}
			var names = listing.Output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Select(name => name.Trim()).Where(IsTaken).ToList();
			foreach (var name in names)
			{
				var pull = Adb(serial, "pull " + folder + "/" + name + " \"" + Path.Combine(to, name) + "\"");
				if (pull.Code != 0)
				{
					Console.Error.WriteLine("adb could not pull " + name + ": " + pull.Output.Trim());
					return 1;
				}
				if (!keep)
				{
					Adb(serial, "shell rm " + folder + "/" + name);
				}
			}
			Console.WriteLine("took " + names.Count + " files" + (keep ? ", kept" : ", deleted") + " in " + folder);
			return 0;
		}

		static (int Code, string Output) Adb(string serial, string arguments)
		{
			var start = new ProcessStartInfo("adb", (serial == null ? string.Empty : "-s " + serial + " ") + arguments)
			{
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
			};
			using (var process = Process.Start(start))
			{
				var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
				process.WaitForExit();
				return (process.ExitCode, output);
			}
		}
	}
}
