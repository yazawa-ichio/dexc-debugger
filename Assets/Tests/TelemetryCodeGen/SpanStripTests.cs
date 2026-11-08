using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dexc.Telemetry;
using Dexc.Telemetry.CodeGen;
using Mono.Cecil;
using Mono.Cecil.Cil;
using NUnit.Framework;

namespace DexcDebugger.Tests.Telemetry.CodeGen
{
	/// <summary>Builds a method that starts a span, strips it, then loads and runs it to see that the IL still holds.</summary>
	public sealed class SpanStripTests
	{
		const string File = "C:/fixture/Calls.cs";

		AssemblyDefinition m_Assembly;
		ModuleDefinition m_Module;
		MethodDefinition m_Method;
		TelemetryRoot m_Root;

		[SetUp]
		public void SetUp()
		{
			TelemetryRoot.Reset();
			m_Root = TelemetryRoot.Current;
			var options = new TelemetryOptions { CollectUnityMetrics = false, CollectUnityLogs = false, Detail = TelemetryDetail.Detailed };
			m_Root.Start(
				new TelemetryFrameWriter(new MemoryStream()),
				new TelemetryFrameWriter(new MemoryStream()),
				new TelemetryFrameWriter(new MemoryStream()),
				options,
				new KeyValuePair<string, string>[0],
				0
			);
			var name = "Fixture" + Guid.NewGuid().ToString("N");
			m_Assembly = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition(name, new Version(1, 0)), name, ModuleKind.Dll);
			m_Module = m_Assembly.MainModule;
			var type = new TypeDefinition("Fixture", "Calls", TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Abstract | TypeAttributes.Sealed, m_Module.TypeSystem.Object);
			m_Module.Types.Add(type);
			m_Method = new MethodDefinition("Run", MethodAttributes.Public | MethodAttributes.Static, m_Module.ImportReference(typeof(TelemetrySpan)));
			m_Method.Parameters.Add(new ParameterDefinition("tracer", ParameterAttributes.None, m_Module.ImportReference(typeof(TelemetryTracer))));
			type.Methods.Add(m_Method);
		}

		[TearDown]
		public void TearDown()
		{
			m_Assembly.Dispose();
			TelemetryRoot.Reset();
		}

		/// <summary>return tracer.Start("x", default, detail);</summary>
		void EmitStart(Action<ILProcessor> detail)
		{
			var il = m_Method.Body.GetILProcessor();
			var parent = new VariableDefinition(m_Module.ImportReference(typeof(TelemetryContext)));
			m_Method.Body.Variables.Add(parent);
			var first = Instruction.Create(OpCodes.Ldarg_0);
			il.Append(first);
			il.Emit(OpCodes.Ldstr, "x");
			il.Emit(OpCodes.Ldloca, parent);
			il.Emit(OpCodes.Initobj, m_Module.ImportReference(typeof(TelemetryContext)));
			il.Emit(OpCodes.Ldloc, parent);
			detail(il);
			il.Emit(OpCodes.Callvirt, m_Module.ImportReference(typeof(TelemetryTracer).GetMethod(nameof(TelemetryTracer.Start))));
			il.Emit(OpCodes.Ret);
			m_Method.DebugInformation.SequencePoints.Add(new SequencePoint(first, new Document(File)) { StartLine = 10, EndLine = 10, StartColumn = 1, EndColumn = 2 });
		}

		bool Strip(string[] defines, string source = null)
		{
			return TelemetryWeaver.StripSpans(m_Module, defines, path => path == File && source != null ? SourceSymbols.Read(source) : null);
		}

		/// <summary>Writes the module, loads it and runs the method with a tracer of the running telemetry.</summary>
		TelemetrySpan Run()
		{
			var stream = new MemoryStream();
			m_Assembly.Write(stream);
			var loaded = System.Reflection.Assembly.Load(stream.ToArray());
			var span = (TelemetrySpan)loaded.GetType("Fixture.Calls").GetMethod("Run").Invoke(null, new object[] { m_Root.Traces.Tracer("Game") });
			span.Dispose();
			return span;
		}

		bool CallsStart()
		{
			return m_Method.Body.Instructions.Any(instruction => instruction.Operand is MethodReference called && called.Name == "Start");
		}

		[Test]
		public void AFinerStartBecomesAnEmptySpan()
		{
			EmitStart(il => il.Emit(OpCodes.Ldc_I4_3));

			Assert.That(Strip(new[] { TelemetryWeaver.DetailStandard }), Is.True);

			Assert.That(CallsStart(), Is.False);
			Assert.That(Run().Context.IsValid, Is.False);
		}

		[Test]
		public void AStartTheSymbolsKeepStays()
		{
			EmitStart(il => il.Emit(OpCodes.Ldc_I4_2));

			Assert.That(Strip(new[] { TelemetryWeaver.DetailStandard }), Is.False);

			Assert.That(Run().Context.IsValid, Is.True);
		}

		[Test]
		public void WithoutASymbolEveryStartGoes()
		{
			EmitStart(il => il.Emit(OpCodes.Ldc_I4_1));

			Assert.That(Strip(new string[0]), Is.True);

			Assert.That(Run().Context.IsValid, Is.False);
		}

		[Test]
		public void TheFileOfTheCallCanDefineTheSymbol()
		{
			EmitStart(il => il.Emit(OpCodes.Ldc_I4_3));

			Assert.That(Strip(new string[0], "// a header\n#define DEXC_TELEMETRY_DETAIL_DETAILED\nusing System;\n"), Is.False);
		}

		[Test]
		public void TheFileOfTheCallCanUndefineTheSymbol()
		{
			EmitStart(il => il.Emit(OpCodes.Ldc_I4_2));

			Assert.That(Strip(new[] { TelemetryWeaver.DetailStandard }, "#undef DEXC_TELEMETRY_DETAIL_STANDARD\n#define DEXC_TELEMETRY_DETAIL_BASIC\n"), Is.True);
		}

		[Test]
		public void AFileWithConditionsAtTheTopKeepsEverything()
		{
			EmitStart(il => il.Emit(OpCodes.Ldc_I4_3));

			Assert.That(Strip(new string[0], "#if UNITY_EDITOR\n#define DEXC_TELEMETRY_DETAIL_DETAILED\n#endif\n"), Is.False);
		}

		[Test]
		public void ADetailThatIsNotAConstantStays()
		{
			m_Method.Parameters.Add(new ParameterDefinition("detail", ParameterAttributes.None, m_Module.ImportReference(typeof(TelemetryDetail))));
			EmitStart(il => il.Emit(OpCodes.Ldarg_1));

			Assert.That(Strip(new string[0]), Is.False);
		}

		[TestCase("#define A\n#define B // why\nusing System;\n#define C", new[] { "A", "B" })]
		[TestCase("\uFEFF/* a\n block */\n#define A\n", new[] { "A" })]
		[TestCase("#nullable enable\n#pragma warning disable 1\n#define A\n", new[] { "A" })]
		[TestCase("using System;\n#define A\n", new string[0])]
		public void TheSymbolsAreReadUpToTheFirstToken(string source, string[] defined)
		{
			var symbols = SourceSymbols.Read(source);

			Assert.That(symbols.IsKnown, Is.True);
			foreach (var symbol in new[] { "A", "B", "C" })
			{
				Assert.That(symbols.Holds(symbol, false), Is.EqualTo(defined.Contains(symbol)), symbol);
			}
		}
	}
}
