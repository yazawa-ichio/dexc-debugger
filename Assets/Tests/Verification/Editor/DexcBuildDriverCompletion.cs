using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DexcDebugger.Verification
{
	internal static class DexcBuildDriverCompletion
	{
		internal static string Restore(params Action[] steps)
		{
			var errors = new List<string>();
			foreach (var step in steps)
			{
				try
				{
					step();
				}
				catch (Exception exception)
				{
					errors.Add(exception.ToString());
				}
			}
			return string.Join("\n", errors);
		}

		internal static void WriteReportAndExit(string path, object record, bool succeeded)
		{
			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
				File.WriteAllText(path, JsonUtility.ToJson(record, true));
			}
			catch (Exception exception)
			{
				succeeded = false;
				Debug.LogException(exception);
			}
			EditorApplication.Exit(succeeded ? 0 : 1);
		}
	}
}
