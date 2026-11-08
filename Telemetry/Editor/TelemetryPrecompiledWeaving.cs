using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dexc.Telemetry.CodeGen;
using Mono.Cecil;
using Mono.Cecil.Cil;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Compilation;
using UnityEngine;

namespace Dexc.Telemetry.Editor
{
	/// <summary>
	/// Puts the measuring of the watch into the prebuilt DLLs of a Player build, such as those in Plugins, which the IL
	/// processing of the compiler never sees. It runs once the scripts of the build are compiled and rewrites the copies in
	/// the build, so the files of the project stay as they are, and the Editor runs them unmeasured.
	/// </summary>
	internal sealed class TelemetryPrecompiledWeaving : IPostBuildPlayerScriptDLLs
	{
		public int callbackOrder => 10;

		public void OnPostBuildPlayerScriptDLLs(BuildReport report)
		{
			var target = NamedBuildTarget.FromBuildTargetGroup(report.summary.platformGroup);
			if (!PlayerSettings.GetScriptingDefineSymbols(target).Split(';').Contains(TelemetryWeaver.WatchSymbol))
			{
				return;
			}
			TelemetryProfiles profiles;
			try
			{
				profiles = TelemetryProfiles.Load(Directory.GetCurrentDirectory());
			}
			catch (Exception exception) when (exception is FormatException || exception is IOException)
			{
				throw new BuildFailedException("Dexc Telemetry: the profile files could not be read: " + exception.Message);
			}
			var preset = profiles.Active;
			var selectors = TelemetryWeaver.FindSelectors();
			var prebuilt = new HashSet<string>(
				CompilationPipeline.GetPrecompiledAssemblyPaths(CompilationPipeline.PrecompiledAssemblySources.UserAssembly).Select(Path.GetFileName),
				StringComparer.OrdinalIgnoreCase
			);
			var libraries = report.GetFiles().Where(file => file.role == "ManagedLibrary").Select(file => file.path).ToList();
			var folders = libraries.Select(Path.GetDirectoryName).Distinct().ToList();
			foreach (var path in libraries.Where(path => prebuilt.Contains(Path.GetFileName(path))))
			{
				var name = Path.GetFileNameWithoutExtension(path);
				if (name == TelemetryWeaver.RuntimeAssembly || (preset?.MayChoose(name) != true && selectors.Count == 0))
				{
					continue;
				}
				var diagnostics = new List<TelemetryDiagnostic>();
				if (Weave(path, folders, preset, selectors, diagnostics))
				{
					Debug.Log("Dexc Telemetry: measured the watch in the prebuilt " + Path.GetFileName(path));
				}
				foreach (var diagnostic in diagnostics)
				{
					Debug.LogWarning("Dexc Telemetry: " + diagnostic.Message);
				}
			}
		}

		/// <summary>Rewrites the DLL in place, and its symbols when it has them. False when nothing was chosen in it.</summary>
		internal static bool Weave(string path, IEnumerable<string> folders, TelemetryProfile preset, IReadOnlyList<ITelemetryMethodSelector> selectors, List<TelemetryDiagnostic> diagnostics)
		{
			var symbols = Path.ChangeExtension(path, ".pdb");
			var hasSymbols = File.Exists(symbols);
			using (var resolver = new DefaultAssemblyResolver())
			{
				foreach (var folder in folders)
				{
					resolver.AddSearchDirectory(folder);
				}
				var parameters = new ReaderParameters
				{
					AssemblyResolver = resolver,
					ReadingMode = ReadingMode.Immediate,
					ReadSymbols = hasSymbols,
					SymbolStream = hasSymbols ? new MemoryStream(File.ReadAllBytes(symbols)) : null,
					SymbolReaderProvider = hasSymbols ? new DefaultSymbolReaderProvider(false) : null,
				};
				using (var assembly = AssemblyDefinition.ReadAssembly(new MemoryStream(File.ReadAllBytes(path)), parameters))
				{
					if (!TelemetryWeaver.InstrumentWatched(assembly.MainModule, preset, selectors, diagnostics))
					{
						return false;
					}
					var written = assembly.MainModule.HasSymbols;
					using (var output = File.Create(path))
					using (var pdb = written ? File.Create(symbols) : null)
					{
						assembly.Write(output, new WriterParameters { WriteSymbols = written, SymbolStream = pdb, SymbolWriterProvider = written ? new PortablePdbWriterProvider() : null });
					}
					return true;
				}
			}
		}
	}
}
