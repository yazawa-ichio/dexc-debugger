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
	/// Runs <see cref="TelemetryWeaver"/> on every assembly that uses <c>Dexc.Telemetry</c>. Unity runs it while compiling,
	/// for the Editor and for each Player build, so what it reports is a compile error.
	/// </summary>
	internal sealed class TelemetryPostProcessor : ILPostProcessor
	{
		const string RuntimeAssembly = "Dexc.Telemetry";

		public override ILPostProcessor GetInstance()
		{
			return this;
		}

		public override bool WillProcess(ICompiledAssembly compiledAssembly)
		{
			return compiledAssembly.Name != RuntimeAssembly
				&& compiledAssembly.References.Any(reference => Path.GetFileNameWithoutExtension(reference) == RuntimeAssembly);
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
						TelemetryWeaver.Check(assembly.MainModule, diagnostics);
						var defines = new HashSet<string>(compiledAssembly.Defines);
						var changed = TelemetryWeaver.StripSpans(assembly.MainModule, defines, ReadSource);
						changed |= TelemetryWeaver.InstrumentMethods(assembly.MainModule, defines, ReadSource, diagnostics);
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
