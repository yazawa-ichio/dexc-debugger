using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Dexc;
using NUnit.Framework;
using UnityEngine;

namespace DexcDebugger.Tests
{
	/// <summary>
	/// The library keeps its running state on a single root instance. Anything else that is static survives a Play Mode
	/// cycle when Domain Reload is disabled and makes the menu inherit the previous run's state.
	/// </summary>
	public sealed class DexcRootStateTests
	{
		/// <summary>The one mutable static the library is allowed to have for its running state.</summary>
		const string RootField = "Dexc.DexcRoot.s_Current";

		/// <summary>
		/// Definition caches. They hold the result of reading attributes from the loaded assemblies, which is the same for
		/// every run of the same build, so nothing from a previous Play Mode can leak through them. Their lifetime is the
		/// assembly load: a domain reload drops them with everything else. Add here only caches derived from attributes and
		/// assemblies alone, holding no value a user or a running game can change.
		/// </summary>
		static readonly HashSet<string> DefinitionCacheFields = new HashSet<string>(StringComparer.Ordinal)
		{
			"Dexc.DexcDefinitions.s_Commands",
			"Dexc.DexcDefinitions.s_Settings",
			"Dexc.DexcDefinitions.s_InspectorSources",
			"Dexc.DexcDefinitions.s_Boot",
			"Dexc.DexcDefinitions.s_InputEditors",
			"Dexc.DexcDefinitions.s_Types",
		};

		/// <summary>
		/// Types whose instances never change after construction, so a static readonly field of them cannot carry state
		/// between Play Mode sessions. Add to this list only for values that are constant for the life of the process.
		/// </summary>
		static readonly HashSet<string> ImmutableFieldTypes = new HashSet<string>(StringComparer.Ordinal)
		{
			"DebugInputSchema", // input schema descriptors built once per input type
			"Vector2", // layout reference resolutions
			"Type",
			"PropertyInfo", // reflection handles for the optional Input System
			"ReferenceEqualityComparer`1",
			"ReferenceComparer", // stateless comparers
			"DexcGuiOverlayDrawSink", // stateless IMGUI draw sink
			"DebugDisplaySubject", // the shared no-op subject
			"IReadOnlyList`1", // empty read-only collections
			"DebugValue", // the shared null value of the immutable value tree
		};

		static bool IsCompilerGenerated(Type type)
		{
			for (var current = type; current != null; current = current.DeclaringType)
			{
				if (current.Name.StartsWith("<", StringComparison.Ordinal) || current.IsDefined(typeof(CompilerGeneratedAttribute), false))
					return true;
			}
			return false;
		}

		static IEnumerable<Assembly> LibraryAssemblies()
		{
			yield return typeof(Dexc.DexcDebugger).Assembly;
			yield return typeof(DebugMenu).Assembly;
		}

		[Test]
		public void OnlyTheRootInstanceIsStoredInAStaticField()
		{
			var offenders = new List<string>();
			foreach (var assembly in LibraryAssemblies())
			{
				foreach (var type in assembly.GetTypes())
				{
					// Lambda caches, switch tables and string blobs the compiler emits hold no state of ours.
					if (IsCompilerGenerated(type))
						continue;
					foreach (
						var field in type.GetFields(
							BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly
						)
					)
					{
						if (field.IsLiteral)
							continue;
						// A static auto-property is judged by its own type, under the property's name.
						var fieldName = field.Name.EndsWith("k__BackingField", StringComparison.Ordinal)
							? field.Name.Substring(1, field.Name.IndexOf('>') - 1)
							: field.Name;
						if (fieldName.StartsWith("<", StringComparison.Ordinal))
							continue;
						var name = (type.FullName ?? type.Name) + "." + fieldName;
						if (name == RootField || DefinitionCacheFields.Contains(name))
							continue;
						if (field.IsInitOnly && ImmutableFieldTypes.Contains(field.FieldType.Name))
							continue;
						offenders.Add(name + " : " + field.FieldType.Name + (field.IsInitOnly ? " (readonly)" : string.Empty));
					}
				}
			}
			Assert.That(
				offenders,
				Is.Empty,
				"Static state must live on DexcRoot so it is dropped when Play Mode ends. Move these onto the root (or its "
					+ "DexcUIRoot slot), or add the type to ImmutableFieldTypes when the value is constant for the process, or add the field "
					+ "to DefinitionCacheFields when it only caches what attributes and assemblies decide:\n"
					+ string.Join("\n", offenders)
			);
		}

#if DEXC_DEBUGGER
		[Test]
		public void ResettingTheRootDropsSessionStorageRegistriesAndUiState()
		{
			DexcRoot.Reset();
			var storage = new DebugMemoryStorage();
			DebugStorage.Storage = storage;
			var panel = new DebugPanel("DexcRootStateTests").Toggle("Flag", () => false, _ => { });
			DebugPanels.Register(panel);
			var widget = new DebugDisplayWidget("DexcRootStateTests/HUD", () => "x");
			DebugDisplay.RegisterWidget(widget);
			Dexc.DexcDebugger.Initialize();
			DebugMenu.Open();
			DexcRuntimeUIState.Notice = "left over";
			DexcRuntimeUIState.SetSelectedIndex(3);
			Assert.That(Dexc.DexcDebugger.IsInitialized, Is.True);
			Assert.That(DebugPanels.All.Contains(panel), Is.True);

			DexcRoot.Reset();

			Assert.That(Dexc.DexcDebugger.IsInitialized, Is.False, "the session ends with the root");
			Assert.That(DexcRuntimeUIState.IsOpen, Is.False, "the menu is closed again");
			Assert.That(DexcRuntimeUIState.Notice, Is.Empty, "runtime menu state starts empty");
			Assert.That(DexcRuntimeUIState.SelectedIndex, Is.Zero);
			Assert.That(DebugPanels.All.Contains(panel), Is.False, "panel registrations do not survive");
			Assert.That(DebugStorage.Storage, Is.Not.SameAs(storage), "the storage override falls back to the default");
			Assert.That(DexcDisplayWidgets.Count, Is.Zero, "HUD widgets do not survive");
			DexcRoot.Reset();
		}

		[Test]
		public void StorageFactoryOutlivesARootResetAndMakesAFreshStorageAfterFlushing()
		{
			DexcRoot.Reset();
			var made = new List<CountingStorage>();
			DebugStorage.Factory = () =>
			{
				var storage = new CountingStorage();
				made.Add(storage);
				return storage;
			};
			try
			{
				DebugStorage.Save("key", "value");
				DexcRoot.Reset();

				Assert.That(made[0].Flushes, Is.EqualTo(1), "a write still buffered reaches the storage before the root goes");
				Assert.That(DebugStorage.Factory, Is.Not.Null, "the way to make the storage is kept");
				Assert.That(DebugStorage.Storage, Is.SameAs(made[1]), "the new root makes its own storage from it");
			}
			finally
			{
				DebugStorage.Factory = null;
				DexcRoot.Reset();
			}
			Assert.That(DebugStorage.Storage, Is.TypeOf<DebugPlayerPrefsStorage>(), "without a factory it is PlayerPrefs again");
		}

		sealed class CountingStorage : IDebugStorage
		{
			internal int Flushes;

			public bool TryLoad(string key, out string value)
			{
				value = null;
				return false;
			}

			public void Save(string key, string value) { }

			public void Delete(string key) { }

			public void Flush()
			{
				Flushes++;
			}
		}
#endif

#if DEXC_DEBUGGER
		/// <summary>
		/// Boot values belong to the screen that opened them. Nothing static keeps them, so a second screen starts from what
		/// was declared and stored rather than from what the first one was left holding.
		/// </summary>
		[Test]
		public void BootValuesBelongToTheSelectionThatOpenedThem()
		{
			DexcRoot.Reset();
			DebugStorage.Storage = new DebugMemoryStorage();

			var first = DebugBootSelection.Open("root-state");
			var marked = (DebugToggleWidget)first.Panels.Single().Widgets.Single();
			marked.Toggle();
			Assert.That(marked.Value, Is.True);

			var second = DebugBootSelection.Open("root-state");

			Assert.That(second, Is.Not.SameAs(first), "each screen opens its own");
			Assert.That(
				((DebugToggleWidget)second.Panels.Single().Widgets.Single()).Value,
				Is.False,
				"Persist = false starts from the declared value"
			);
			DexcRoot.Reset();
		}
#endif

		/// <summary>Nothing is built until a feature is used, so a project that never calls Dexc pays nothing.</summary>
		[Test]
		public void NothingIsCreatedUntilAFeatureIsUsed()
		{
			DexcRoot.Reset();
			Assert.That(Resources.FindObjectsOfTypeAll<DexcRuntimeUIDriver>(), Is.Empty, "no menu driver before the menu opens");
			Assert.That(Resources.FindObjectsOfTypeAll<DexcDisplayDriver>(), Is.Empty, "no display driver before Display is used");
			Assert.That(
				Resources.FindObjectsOfTypeAll<DexcInspectorDriver>(),
				Is.Empty,
				"no inspector driver before the Inspector is used"
			);
			Dexc.DexcDebugger.Initialize();
			Assert.That(
				Resources.FindObjectsOfTypeAll<DexcRuntimeUIDriver>(),
				Is.Empty,
				"Initialize alone builds nothing; the features build what they need"
			);
			DexcRoot.Reset();
		}

		/// <summary>Edit Mode never gets runtime objects, whatever the feature APIs are asked to do.</summary>
		[Test]
		public void FeatureUseOutsidePlayModeCreatesNoObjects()
		{
			DexcRoot.Reset();
			DebugStorage.Storage = new DebugMemoryStorage();
			Dexc.DexcDebugger.Initialize();
			DebugDisplay.DrawLabel("Tests/RootState/Label", "text");
			DebugMenu.Open();
			Assert.That(Resources.FindObjectsOfTypeAll<DexcDisplayDriver>(), Is.Empty, "Display draws nothing outside Play Mode");
			Assert.That(Resources.FindObjectsOfTypeAll<DexcRuntimeUIDriver>(), Is.Empty, "the menu builds nothing outside Play Mode");
			DebugMenu.Close();
			DexcRoot.Reset();
		}

		[DebugBootMenu("Root State Fixture", Order = 90, Tag = "root-state", Scene = "RootStateFixtureScene")]
		public sealed class RootStateBootSet : DebugBootSet
		{
			[DebugBootItem("Marked", Persist = false)]
			public bool Marked { get; private set; }

			internal void Mark()
			{
				Marked = true;
			}

			internal bool IsMarked => Marked;
		}
	}
}
