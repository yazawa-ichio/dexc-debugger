using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Unity.CompilationPipeline.Common.Diagnostics;
using Unity.CompilationPipeline.Common.ILPostProcessing;

namespace Dexc.Telemetry.CodeGen
{
	/// <summary>
	/// Runs <see cref="TelemetryWeaver"/> on every assembly that uses <c>Dexc.Telemetry</c>, and with
	/// <c>DEXC_TELEMETRY_WATCH</c> also on those the active preset or a selector may choose from. Unity runs it while
	/// compiling, for the Editor and for each Player build, so what it reports is a compile error. It runs in a process of
	/// its own whose folder is the project.
	/// </summary>
	internal sealed class TelemetryPostProcessor : ILPostProcessor
	{
		readonly object m_Lock = new object();
		TelemetryProfiles m_Profiles;
		string m_ProfilesError;
		DateTime m_ProjectTime;
		DateTime m_UserTime;
		IReadOnlyList<ITelemetryMethodSelector> m_Selectors;

		public override ILPostProcessor GetInstance()
		{
			return this;
		}

		public override bool WillProcess(ICompiledAssembly compiledAssembly)
		{
			var name = compiledAssembly.Name;
			if (name == TelemetryWeaver.RuntimeAssembly || name.EndsWith(".CodeGen", StringComparison.Ordinal))
			{
				return false;
			}
			if (compiledAssembly.References.Any(reference => Path.GetFileNameWithoutExtension(reference) == TelemetryWeaver.RuntimeAssembly))
			{
				return true;
			}
			if (!compiledAssembly.Defines.Contains(TelemetryWeaver.WatchSymbol))
			{
				return false;
			}
			var profiles = Profiles(out var error);
			return error != null || profiles.Active?.MayChoose(name) == true || Selectors().Count > 0;
		}

		public override ILPostProcessResult Process(ICompiledAssembly compiledAssembly)
		{
			if (!WillProcess(compiledAssembly))
			{
				return null;
			}
			var diagnostics = new List<TelemetryDiagnostic>();
			InMemoryAssembly written = null;
			try
			{
				using (var resolver = new DefaultAssemblyResolver())
				{
					foreach (var directory in compiledAssembly.References.Select(Path.GetDirectoryName).Distinct())
					{
						resolver.AddSearchDirectory(directory);
					}
					var parameters = new ReaderParameters
					{
						AssemblyResolver = resolver,
						SymbolStream = new MemoryStream(compiledAssembly.InMemoryAssembly.PdbData),
						SymbolReaderProvider = new PortablePdbReaderProvider(),
						ReadingMode = ReadingMode.Immediate,
					};
					using (var assembly = AssemblyDefinition.ReadAssembly(new MemoryStream(compiledAssembly.InMemoryAssembly.PeData), parameters))
					{
						var module = assembly.MainModule;
						TelemetryWeaver.Check(module, diagnostics);
						var defines = new HashSet<string>(compiledAssembly.Defines);
						var changed = TelemetryWeaver.StripSpans(module, defines, ReadSource);
						changed |= TelemetryWeaver.InstrumentMethods(module, defines, ReadSource, diagnostics);
						if (defines.Contains(TelemetryWeaver.WatchSymbol))
						{
							var profiles = Profiles(out var error);
							if (error != null)
							{
								diagnostics.Add(TelemetryDiagnostic.Error(null, 0, 0, error));
							}
							else
							{
								TelemetryWeaver.InstrumentWatched(module, profiles.Active, Selectors(), diagnostics);
								TelemetryWeaver.MarkProfiles(module, profiles.Hash);
								changed = true;
							}
						}
						if (changed)
						{
							var pe = new MemoryStream();
							var pdb = new MemoryStream();
							assembly.Write(pe, new WriterParameters { SymbolWriterProvider = new PortablePdbWriterProvider(), SymbolStream = pdb, WriteSymbols = true });
							written = new InMemoryAssembly(pe.ToArray(), pdb.ToArray());
						}
					}
				}
			}
			catch (Exception exception)
			{
				diagnostics.Add(TelemetryDiagnostic.Error(null, 0, 0, "Dexc.Telemetry could not process " + compiledAssembly.Name + ": " + exception));
			}
			if (written == null && diagnostics.Count == 0)
			{
				return null;
			}
			return new ILPostProcessResult(written ?? compiledAssembly.InMemoryAssembly, diagnostics.Select(ToMessage).ToList());
		}

		/// <summary>The presets, read again when a file changed. Assemblies may be processed at the same time.</summary>
		TelemetryProfiles Profiles(out string error)
		{
			lock (m_Lock)
			{
				var folder = Directory.GetCurrentDirectory();
				var projectTime = WriteTime(Path.Combine(folder, TelemetryProfiles.ProjectFile));
				var userTime = WriteTime(Path.Combine(folder, TelemetryProfiles.UserFile));
				if ((m_Profiles == null && m_ProfilesError == null) || projectTime != m_ProjectTime || userTime != m_UserTime)
				{
					m_ProjectTime = projectTime;
					m_UserTime = userTime;
					try
					{
						m_Profiles = TelemetryProfiles.Load(folder);
						m_ProfilesError = null;
					}
					catch (Exception exception) when (exception is FormatException || exception is IOException)
					{
						m_Profiles = null;
						m_ProfilesError = "The telemetry profiles could not be read: " + exception.Message;
					}
				}
				error = m_ProfilesError;
				return m_Profiles;
			}
		}

		static DateTime WriteTime(string path)
		{
			return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
		}

		/// <summary>The selectors of the project: the code generation assemblies of Unity are all loaded in this process.</summary>
		IReadOnlyList<ITelemetryMethodSelector> Selectors()
		{
			lock (m_Lock)
			{
				m_Selectors = m_Selectors ?? TelemetryWeaver.FindSelectors();
				return m_Selectors;
			}
		}

		static SourceSymbols ReadSource(string path)
		{
			try
			{
				return File.Exists(path) ? SourceSymbols.Read(File.ReadAllText(path)) : null;
			}
			catch (IOException)
			{
				return null;
			}
		}

		static DiagnosticMessage ToMessage(TelemetryDiagnostic diagnostic)
		{
			return new DiagnosticMessage
			{
				DiagnosticType = diagnostic.IsError ? DiagnosticType.Error : DiagnosticType.Warning,
				File = diagnostic.File,
				Line = diagnostic.Line,
				Column = diagnostic.Column,
				MessageData = diagnostic.Message,
			};
		}
	}
}
