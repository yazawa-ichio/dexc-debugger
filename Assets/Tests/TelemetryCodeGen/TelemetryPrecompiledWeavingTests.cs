using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dexc.Telemetry;
using Dexc.Telemetry.CodeGen;
using Dexc.Telemetry.Editor;
using Mono.Cecil;
using Mono.Cecil.Cil;
using NUnit.Framework;

namespace DexcDebugger.Tests.Telemetry.CodeGen
{
	public sealed class TelemetryPrecompiledWeavingTests
	{
		string m_Folder;

		[SetUp]
		public void SetUp()
		{
			m_Folder = Path.Combine(Path.GetTempPath(), "DexcTelemetryPrebuilt-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(m_Folder);
			TelemetryRoot.Reset();
		}

		[TearDown]
		public void TearDown()
		{
			TelemetryRoot.Reset();
			Directory.Delete(m_Folder, true);
		}

		/// <summary>A prebuilt DLL that knows nothing of telemetry: Pre.Squares.Square(value) returns value * value.</summary>
		string Prebuilt(string name)
		{
			var path = Path.Combine(m_Folder, name + ".dll");
			using (var assembly = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition(name, new Version(1, 0)), name, ModuleKind.Dll))
			{
				var module = assembly.MainModule;
				var type = new TypeDefinition("Pre", "Squares", TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Abstract | TypeAttributes.Sealed, module.TypeSystem.Object);
				module.Types.Add(type);
				var method = new MethodDefinition("Square", MethodAttributes.Public | MethodAttributes.Static, module.TypeSystem.Int32);
				method.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None, module.TypeSystem.Int32));
				type.Methods.Add(method);
				var il = method.Body.GetILProcessor();
				il.Emit(OpCodes.Ldarg_0);
				il.Emit(OpCodes.Ldarg_0);
				il.Emit(OpCodes.Mul);
				il.Emit(OpCodes.Ret);
				assembly.Write(path);
			}
			return path;
		}

		static TelemetryProfile Preset(string space)
		{
			return TelemetryProfiles.Read("{ \"active\": \"P\", \"presets\": [ { \"name\": \"P\", \"namespaces\": [\"" + space + "\"] } ] }", null).Active;
		}

		[Test]
		public void APrebuiltDllGetsTheMeasuringAndStillWorks()
		{
			var name = "Prebuilt" + Guid.NewGuid().ToString("N");
			var path = Prebuilt(name);

			Assert.That(TelemetryPrecompiledWeaving.Weave(path, new[] { m_Folder }, Preset("Pre"), new ITelemetryMethodSelector[0], new List<TelemetryDiagnostic>()), Is.True);

			using (var woven = AssemblyDefinition.ReadAssembly(new MemoryStream(File.ReadAllBytes(path))))
			{
				Assert.That(woven.MainModule.AssemblyReferences.Select(reference => reference.Name), Does.Contain("Dexc.Telemetry"));
				var square = woven.MainModule.GetType("Pre.Squares").Methods.Single();
				Assert.That(square.Body.Instructions.Any(instruction => instruction.Operand is MethodReference called && called.Name == "EnterWatched"), Is.True);
			}
			var root = TelemetryRoot.Current;
			root.Start(
				new TelemetryFrameWriter(new MemoryStream()),
				new TelemetryFrameWriter(new MemoryStream()),
				new TelemetryFrameWriter(new MemoryStream()),
				new TelemetryOptions { CollectUnityMetrics = false, CollectUnityLogs = false },
				new KeyValuePair<string, string>[0],
				0
			);
			DexcTelemetry.Watch("Pre.Squares.Square");
			var loaded = System.Reflection.Assembly.Load(File.ReadAllBytes(path));
			Assert.That(loaded.GetType("Pre.Squares").GetMethod("Square").Invoke(null, new object[] { 7 }), Is.EqualTo(49));
		}

		[Test]
		public void ADllThePresetDoesNotChooseStaysAsItIs()
		{
			var path = Prebuilt("Untouched" + Guid.NewGuid().ToString("N"));
			var before = File.ReadAllBytes(path);

			Assert.That(TelemetryPrecompiledWeaving.Weave(path, new[] { m_Folder }, Preset("Other"), new ITelemetryMethodSelector[0], new List<TelemetryDiagnostic>()), Is.False);

			Assert.That(File.ReadAllBytes(path), Is.EqualTo(before));
		}
	}
}
