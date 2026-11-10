using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Unity.CompilationPipeline.Common.Diagnostics;
using Unity.CompilationPipeline.Common.ILPostProcessing;

namespace UniTaskStateMachinePool.CodeGen
{
	/// <summary>
	/// Runs <see cref="StateMachinePoolRewriter"/> on every assembly of a Player that uses UniTask, while Unity compiles
	/// it. The Editor's own compilation is left alone: there the state machines are classes, which need no box, and the
	/// rewrite has nothing to do. A build that defines <see cref="OffDefine"/> is left alone as well, which is how the
	/// same Player is built without the rewrite to compare against.
	/// </summary>
	internal sealed class StateMachinePoolPostProcessor : ILPostProcessor
	{
		const string OffDefine = "UNITASK_STATE_MACHINE_POOL_OFF";

		public override ILPostProcessor GetInstance()
		{
			return this;
		}

		public override bool WillProcess(ICompiledAssembly compiledAssembly)
		{
			if (compiledAssembly.Defines.Contains("UNITY_EDITOR") || compiledAssembly.Defines.Contains(OffDefine) || compiledAssembly.Name.EndsWith(".CodeGen", StringComparison.Ordinal))
			{
				return false;
			}
			return compiledAssembly.Name == "UniTask" || compiledAssembly.References.Any(reference => Path.GetFileNameWithoutExtension(reference) == "UniTask");
		}

		public override ILPostProcessResult Process(ICompiledAssembly compiledAssembly)
		{
			if (!WillProcess(compiledAssembly))
			{
				return null;
			}
			var diagnostics = new List<DiagnosticMessage>();
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
						var result = StateMachinePoolRewriter.Rewrite(assembly.MainModule);
						if (result.Machines == 0 && result.Starts == 0)
						{
							return null;
						}
						var pe = new MemoryStream();
						var pdb = new MemoryStream();
						assembly.Write(pe, new WriterParameters { SymbolWriterProvider = new PortablePdbWriterProvider(), SymbolStream = pdb, WriteSymbols = true });
						return new ILPostProcessResult(new InMemoryAssembly(pe.ToArray(), pdb.ToArray()), diagnostics);
					}
				}
			}
			catch (Exception exception)
			{
				diagnostics.Add(new DiagnosticMessage { DiagnosticType = DiagnosticType.Error, MessageData = "UniTask state machine pool could not process " + compiledAssembly.Name + ": " + exception });
				return new ILPostProcessResult(compiledAssembly.InMemoryAssembly, diagnostics);
			}
		}
	}
}
