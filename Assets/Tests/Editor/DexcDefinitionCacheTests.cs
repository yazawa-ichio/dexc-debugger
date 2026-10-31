using System.Linq;
using Dexc;
using NUnit.Framework;

namespace DexcDebugger.Tests
{
	/// <summary>
	/// Definitions come from attributes and the loaded assemblies, so they must be readable with no session and outside
	/// Play Mode. This is what lets the Editor window show commands and settings before the game runs.
	/// </summary>
	public sealed class DexcDefinitionCacheTests
	{
#if DEXC_DEBUGGER
		[Test]
		public void EveryFeatureIsListedWithoutASession()
		{
			Assert.That(Dexc.DexcDebugger.IsInitialized, Is.False, "the fixture runs with no session");

			Assert.That(DexcDefinitions.Commands.Definitions, Is.Not.Empty, "commands");
			Assert.That(DexcDefinitions.Settings.Sets, Is.Not.Empty, "setting sets");
			Assert.That(DexcDefinitions.Boot.Sets, Is.Not.Empty, "boot sets");
			Assert.That(DexcDefinitions.InspectorSources, Is.Not.Null, "inspector sources");
		}
#endif

		[Test]
		public void DeclaredIdentityIsCarried()
		{
			Assert.That(
				DexcDefinitions.Commands.Definitions.All(definition => !string.IsNullOrEmpty(definition.Attribute.Path)),
				Is.True,
				"every command carries its declared path"
			);
			Assert.That(
				DexcDefinitions.Settings.Sets.SelectMany(set => set.Properties).All(item => !string.IsNullOrEmpty(item.Attribute.Path)),
				Is.True,
				"every setting carries its declared path"
			);
			Assert.That(DexcDefinitions.Boot.Sets.All(set => set.Type != null), Is.True, "every boot set carries its type");
		}

		[Test]
		public void TheReflectionRunsOncePerAssemblyLoad()
		{
			var commands = DexcDefinitions.Commands;
			var settings = DexcDefinitions.Settings;

			Assert.That(DexcDefinitions.Commands, Is.SameAs(commands), "commands are collected once");
			Assert.That(DexcDefinitions.Settings, Is.SameAs(settings), "settings are collected once");
			Assert.That(DexcDefinitions.Boot.Sets, Is.SameAs(DexcDefinitions.Boot.Sets), "boot sets are collected once");
			Assert.That(
				DexcDefinitions.InspectorSources,
				Is.SameAs(DexcDefinitions.InspectorSources),
				"inspector sources are collected once"
			);
			Assert.That(DexcDefinitions.InputEditors, Is.SameAs(DexcDefinitions.InputEditors), "input editors are collected once");
		}

		[Test]
		public void DefinitionsCarryNoLiveInstance()
		{
			foreach (var definition in DexcDefinitions.Commands.Definitions)
			{
				foreach (var field in definition.Fields)
				{
					Assert.That(field.Field, Is.Not.Null, definition.Type.FullName + " keeps the declared field");
					if (field.IsInput)
						Assert.That(field.Schema, Is.Not.Null, definition.Type.FullName + " keeps the input schema");
				}
			}
			foreach (var set in DexcDefinitions.Settings.Sets)
			{
				foreach (var item in set.Properties)
				{
					Assert.That(item.Property, Is.Not.Null, set.Type.FullName + " keeps the declared property");
					Assert.That(item.ValueType, Is.Not.Null, set.Type.FullName + " keeps the declared value type");
				}
			}
		}
	}
}
