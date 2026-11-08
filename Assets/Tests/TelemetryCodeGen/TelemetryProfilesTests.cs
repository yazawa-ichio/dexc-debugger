using System;
using Dexc.Telemetry.CodeGen;
using Mono.Cecil;
using NUnit.Framework;

namespace DexcDebugger.Tests.Telemetry.CodeGen
{
	public sealed class TelemetryProfilesTests
	{
		const string Project = @"{
			""active"": ""Game"",
			""presets"": [
				{ ""name"": ""Game"", ""assemblies"": [""Game.*""], ""namespaces"": [""Game.Stage""], ""every"": 4, ""perFrame"": 8, ""anyThread"": true },
				{ ""name"": ""Hot"", ""attributes"": [""Game.HotAttribute""] }
			]
		}";

		[Test]
		public void TheProjectChoosesThePreset()
		{
			var profiles = TelemetryProfiles.Read(Project, null);

			var active = profiles.Active;
			Assert.That(active.Name, Is.EqualTo("Game"));
			Assert.That(active.Every, Is.EqualTo(4));
			Assert.That(active.PerFrame, Is.EqualTo(8));
			Assert.That(active.AnyThread, Is.True);
		}

		[Test]
		public void TheUserReplacesAPresetAndChoosesAnother()
		{
			var profiles = TelemetryProfiles.Read(Project, @"{ ""active"": ""Hot"", ""presets"": [ { ""name"": ""Hot"", ""namespaces"": [""Mine""] } ] }");

			Assert.That(profiles.Presets.Count, Is.EqualTo(2));
			Assert.That(profiles.Active.Name, Is.EqualTo("Hot"));
			Assert.That(profiles.Active.Namespaces, Is.EqualTo(new[] { "Mine" }));
			Assert.That(profiles.Active.Attributes, Is.Empty, "the preset of the user replaces the whole preset");
		}

		[Test]
		public void TheHashFollowsBothFiles()
		{
			var first = TelemetryProfiles.Read(Project, null).Hash;

			Assert.That(TelemetryProfiles.Read(Project, null).Hash, Is.EqualTo(first));
			Assert.That(TelemetryProfiles.Read(Project, @"{ ""active"": ""Hot"" }").Hash, Is.Not.EqualTo(first));
		}

		[Test]
		public void WithoutFilesNothingIsChosen()
		{
			Assert.That(TelemetryProfiles.Read(null, null).Active, Is.Null);
		}

		[TestCase(@"{ ""presets"": [ { ""assemblies"": [] } ] }")]
		[TestCase(@"{ ""presets"": [ { ""name"": ""A"", ""every"": 0 } ] }")]
		[TestCase(@"{ ""presets"": [ { ""name"": ""A"", ""namespaces"": [1] } ] }")]
		[TestCase(@"{ ""presets"": [ ")]
		public void AWrongFileIsRefused(string text)
		{
			Assert.Throws<FormatException>(() => TelemetryProfiles.Read(text, null));
		}

		[Test]
		public void EveryListThatIsGivenMustMatch()
		{
			using (var module = ModuleDefinition.CreateModule("Game.Core", ModuleKind.Dll))
			{
				var stage = Method(module, "Game.Stage.Spawn", "Spawner");
				var deeper = Method(module, "Game.Stage.Boss", "Boss");
				var other = Method(module, "Game.Ui", "Menu");
				var preset = TelemetryProfiles.Read(Project, null).Active;

				Assert.That(preset.Chooses("Game.Core", stage), Is.True);
				Assert.That(preset.Chooses("Game.Core", deeper), Is.True, "namespaces under it count");
				Assert.That(preset.Chooses("Game.Core", other), Is.False);
				Assert.That(preset.Chooses("Tools", stage), Is.False);
				Assert.That(preset.MayChoose("Tools"), Is.False);
			}
		}

		static MethodDefinition Method(ModuleDefinition module, string space, string type)
		{
			var definition = new TypeDefinition(space, type, TypeAttributes.Public | TypeAttributes.Class, module.TypeSystem.Object);
			module.Types.Add(definition);
			var method = new MethodDefinition("Run", MethodAttributes.Public | MethodAttributes.Static, module.TypeSystem.Void);
			definition.Methods.Add(method);
			return method;
		}
	}
}
