using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Dexc.Telemetry.CodeGen;
using Dexc.Telemetry.Editor;
using Mono.Cecil;
using NUnit.Framework;

namespace DexcDebugger.Tests.Telemetry.CodeGen
{
	public sealed class TelemetryProfilesFreshnessTests
	{
		string m_Folder;

		[SetUp]
		public void SetUp()
		{
			m_Folder = Path.Combine(Path.GetTempPath(), "DexcTelemetryProfiles-" + Guid.NewGuid().ToString("N"));
		}

		[TearDown]
		public void TearDown()
		{
			if (Directory.Exists(m_Folder))
			{
				Directory.Delete(m_Folder, true);
			}
		}

		[Test]
		public void TheTestsWereProcessedWithTheFilesAsTheyAre()
		{
			var current = TelemetryProfilesFreshness.CurrentHash(Directory.GetCurrentDirectory());
			var tests = AppDomain.CurrentDomain.GetAssemblies().Single(assembly => assembly.GetName().Name == "DexcDebugger.Tests.Telemetry");

			var metadata = tests.GetCustomAttributes<AssemblyMetadataAttribute>().Single(attribute => attribute.Key == TelemetryWeaver.ProfilesMetadata);

			Assert.That(metadata.Value, Is.EqualTo(current), "it defines DEXC_TELEMETRY_WATCH, so it keeps the hash");
			Assert.That(TelemetryProfilesBuildCheck.HashOf(tests.Location), Is.EqualTo(current), "the file says the same");
			Assert.That(TelemetryProfilesFreshness.LoadedHashes(), Is.All.EqualTo(current));
		}

		[Test]
		public void AMarkedModuleCarriesTheHash()
		{
			// Written to a file, not loaded: a loaded assembly with another hash would make the Editor compile again.
			Directory.CreateDirectory(m_Folder);
			var path = Path.Combine(m_Folder, "Marked.dll");
			using (var assembly = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("Marked", new Version(1, 0)), "Marked", ModuleKind.Dll))
			{
				TelemetryWeaver.MarkProfiles(assembly.MainModule, "abc");
				assembly.Write(path);
			}

			Assert.That(TelemetryProfilesBuildCheck.HashOf(path), Is.EqualTo("abc"));
		}

		[Test]
		public void ChoosingAPresetKeepsTheRestOfTheFileOfTheUser()
		{
			var path = Path.Combine(m_Folder, TelemetryProfiles.UserFile);
			Directory.CreateDirectory(Path.GetDirectoryName(path));
			File.WriteAllText(path, "{ \"presets\": [ { \"name\": \"Mine\", \"namespaces\": [\"Game\"], \"every\": 2 } ] }");

			TelemetryProfilesSettings.SetUserActive(m_Folder, "Mine");

			var profiles = TelemetryProfiles.Load(m_Folder);
			Assert.That(profiles.Active.Name, Is.EqualTo("Mine"));
			Assert.That(profiles.Active.Namespaces, Is.EqualTo(new[] { "Game" }));
			Assert.That(profiles.Active.Every, Is.EqualTo(2));
			TelemetryProfilesSettings.SetUserActive(m_Folder, null);
			Assert.That(TelemetryProfiles.Load(m_Folder).ActiveName, Is.Null);
		}

		[Test]
		public void WrittenJsonReadsBackTheSame()
		{
			var value = new Dictionary<string, object>
			{
				["text"] = "a \"quoted\"\n\\ line",
				["number"] = 2.5,
				["flag"] = true,
				["none"] = null,
				["list"] = new List<object> { "x", 1.0, new Dictionary<string, object>() },
				["empty"] = new List<object>(),
			};

			var back = (Dictionary<string, object>)MiniJson.Parse(MiniJson.Write(value));

			Assert.That(back["text"], Is.EqualTo(value["text"]));
			Assert.That(back["number"], Is.EqualTo(2.5));
			Assert.That(back["flag"], Is.EqualTo(true));
			Assert.That(back["none"], Is.Null);
			Assert.That(((List<object>)back["list"]).Count, Is.EqualTo(3));
			Assert.That(((List<object>)back["empty"]), Is.Empty);
		}
	}
}
