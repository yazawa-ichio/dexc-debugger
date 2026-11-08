using System;
using System.Collections.Generic;
using Dexc.Telemetry;
using Dexc.Telemetry.CodeGen;
using Mono.Cecil;
using Mono.Cecil.Cil;
using NUnit.Framework;

namespace DexcDebugger.Tests.Telemetry.CodeGen
{
	/// <summary>
	/// The IL is built here with Cecil. A wrong template in compiled test code would stop the build, which is the point of
	/// the check.
	/// </summary>
	public sealed class TemplateCheckTests
	{
		ModuleDefinition m_Module;
		MethodDefinition m_Method;
		ILProcessor m_IL;

		[SetUp]
		public void SetUp()
		{
			m_Module = ModuleDefinition.CreateModule("Fixture", ModuleKind.Dll);
			var type = new TypeDefinition("Fixture", "Calls", TypeAttributes.Public | TypeAttributes.Class, m_Module.TypeSystem.Object);
			m_Module.Types.Add(type);
			m_Method = new MethodDefinition("Log", MethodAttributes.Public | MethodAttributes.Static, m_Module.TypeSystem.Void);
			m_Method.Parameters.Add(new ParameterDefinition("logger", ParameterAttributes.None, m_Module.ImportReference(typeof(TelemetryLogger))));
			m_Method.Parameters.Add(new ParameterDefinition("flag", ParameterAttributes.None, m_Module.TypeSystem.Boolean));
			type.Methods.Add(m_Method);
			m_IL = m_Method.Body.GetILProcessor();
		}

		[TearDown]
		public void TearDown()
		{
			m_Module.Dispose();
		}

		MethodReference Logger(string name, params Type[] parameters)
		{
			return m_Module.ImportReference(typeof(TelemetryLogger).GetMethod(name, parameters));
		}

		void Value(int value)
		{
			m_IL.Emit(OpCodes.Ldc_I4, value);
			m_IL.Emit(OpCodes.Call, m_Module.ImportReference(typeof(TelemetryValue).GetMethod("op_Implicit", new[] { typeof(int) })));
		}

		List<TelemetryDiagnostic> Check()
		{
			m_IL.Emit(OpCodes.Ret);
			var diagnostics = new List<TelemetryDiagnostic>();
			TelemetryWeaver.Check(m_Module, diagnostics);
			return diagnostics;
		}

		[Test]
		public void AHoleWithoutAValueIsAnError()
		{
			m_IL.Emit(OpCodes.Ldarg_0);
			m_IL.Emit(OpCodes.Ldstr, "stage {Stage} took {Seconds}");
			Value(3);
			m_IL.Emit(OpCodes.Callvirt, Logger("Info", typeof(string), typeof(TelemetryValue)));

			var diagnostics = Check();

			Assert.That(diagnostics.Count, Is.EqualTo(1));
			Assert.That(diagnostics[0].IsError, Is.True);
			Assert.That(diagnostics[0].Message, Does.Contain("2 hole(s) for 1 value(s)"));
		}

		[Test]
		public void AValueWithoutAHoleIsAnError()
		{
			m_IL.Emit(OpCodes.Ldarg_0);
			m_IL.Emit(OpCodes.Ldstr, "stage {Stage}");
			Value(3);
			Value(4);
			m_IL.Emit(OpCodes.Callvirt, Logger("Warning", typeof(string), typeof(TelemetryValue), typeof(TelemetryValue)));

			Assert.That(Check().Count, Is.EqualTo(1));
		}

		[Test]
		public void MatchingHolesPass()
		{
			m_IL.Emit(OpCodes.Ldarg_0);
			m_IL.Emit(OpCodes.Ldstr, "{{raw}} stage {Stage} took {Seconds:F2}");
			Value(3);
			Value(4);
			m_IL.Emit(OpCodes.Callvirt, Logger("Info", typeof(string), typeof(TelemetryValue), typeof(TelemetryValue)));
			m_IL.Emit(OpCodes.Ldarg_0);
			m_IL.Emit(OpCodes.Ldstr, "no holes");
			m_IL.Emit(OpCodes.Callvirt, Logger("Debug", typeof(string)));

			Assert.That(Check(), Is.Empty);
		}

		[Test]
		public void TheTemplateAfterAnExceptionIsChecked()
		{
			m_IL.Emit(OpCodes.Ldarg_0);
			m_IL.Emit(OpCodes.Ldnull);
			m_IL.Emit(OpCodes.Ldstr, "stage {Stage} failed");
			m_IL.Emit(OpCodes.Callvirt, Logger("Error", typeof(Exception), typeof(string)));

			Assert.That(Check().Count, Is.EqualTo(1));
		}

		[Test]
		public void ATemplateThatIsNotAConstantIsNotChecked()
		{
			m_IL.Emit(OpCodes.Ldarg_0);
			m_IL.Emit(OpCodes.Ldstr, "stage ");
			m_IL.Emit(OpCodes.Ldstr, "{Stage}");
			m_IL.Emit(OpCodes.Call, m_Module.ImportReference(typeof(string).GetMethod("Concat", new[] { typeof(string), typeof(string) })));
			m_IL.Emit(OpCodes.Callvirt, Logger("Info", typeof(string)));

			Assert.That(Check(), Is.Empty);
		}

		[Test]
		public void ABranchInTheArgumentsIsNotChecked()
		{
			var second = Instruction.Create(OpCodes.Ldstr, "b {X}");
			var call = Instruction.Create(OpCodes.Callvirt, Logger("Info", typeof(string)));
			m_IL.Emit(OpCodes.Ldarg_0);
			m_IL.Emit(OpCodes.Ldarg_1);
			m_IL.Emit(OpCodes.Brfalse, second);
			m_IL.Emit(OpCodes.Ldstr, "a {X}");
			m_IL.Emit(OpCodes.Br, call);
			m_IL.Append(second);
			m_IL.Append(call);

			Assert.That(Check(), Is.Empty);
		}

		[Test]
		public void TheValuesMayBeComputed()
		{
			m_IL.Emit(OpCodes.Ldarg_0);
			m_IL.Emit(OpCodes.Ldstr, "sum {Sum}");
			m_IL.Emit(OpCodes.Ldc_I4_1);
			m_IL.Emit(OpCodes.Ldc_I4_2);
			m_IL.Emit(OpCodes.Add);
			m_IL.Emit(OpCodes.Dup);
			m_IL.Emit(OpCodes.Pop);
			m_IL.Emit(OpCodes.Call, m_Module.ImportReference(typeof(TelemetryValue).GetMethod("op_Implicit", new[] { typeof(int) })));
			Value(5);
			m_IL.Emit(OpCodes.Callvirt, Logger("Info", typeof(string), typeof(TelemetryValue), typeof(TelemetryValue)));

			Assert.That(Check().Count, Is.EqualTo(1), "one hole for two values");
		}

		[TestCase("plain")]
		[TestCase("{A}")]
		[TestCase("{A} and {B:F2} and {C,5}")]
		[TestCase("{{A}} {B}")]
		[TestCase("}} {A} {{")]
		[TestCase("{A} {unclosed")]
		[TestCase("{}")]
		[TestCase("{A}}")]
		public void TheHolesAreCountedLikeTheRuntime(string template)
		{
			var position = 0;
			var runtime = 0;
			while (TelemetryLogTemplate.NextHole(template, ref position, out _))
			{
				runtime++;
			}

			Assert.That(TemplateHoles.Count(template), Is.EqualTo(runtime));
		}
	}
}
