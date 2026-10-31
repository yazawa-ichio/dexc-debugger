using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dexc;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using DebuggerRuntime = Dexc.DexcDebugger;

namespace DexcDebugger.Tests
{
	public sealed class DexcInspectorTests
	{
		private GameObject m_GameObject;
		private InspectorFixtureComponent m_Component;

		[SetUp]
		public void SetUp()
		{
			DebuggerRuntime.Initialize();
			m_GameObject = new GameObject("DexcInspectorFixture");
			m_Component = m_GameObject.AddComponent<InspectorFixtureComponent>();
			InspectorSource.Target = m_Component;
		}

		[TearDown]
		public void TearDown()
		{
			InspectorSource.Target = null;
			if (m_GameObject != null)
				UnityEngine.Object.DestroyImmediate(m_GameObject);
			DebuggerRuntime.ResetForTests();
		}

#if DEXC_DEBUGGER
		/// <summary>
		/// A ScriptableObject is a legitimate thing to look at - build information, settings, master data - and it is not
		/// in any scene. It registers like anything else, and stands in for its own component.
		/// </summary>
		[Test]
		public void AnAssetRegistersAndShowsItsOwnFields()
		{
			var asset = ScriptableObject.CreateInstance<InspectorFixtureAsset>();
			asset.name = "InspectorFixtureAsset";
			try
			{
				DebugInspector.Register("Tests/Inspector/Asset", asset, "asset-label");

				var registered = DebugInspector.GetRegistered(
					new DebugListQuery("Tests/Inspector/Asset", string.Empty, new DebugPageRequest(0, 20))
				);
				var entry = registered.Entries.Single(item => item.Label == "asset-label");
				Assert.That(entry.Origin, Is.EqualTo(DebugInspectorEntryOrigin.Manual));
				Assert.That(entry.SceneName, Is.Empty, "an asset belongs to no scene");

				var components = DebugInspector.GetComponents(entry.Target);
				var component = components.Components.Single();
				Assert.That(component.TypeName, Is.EqualTo(typeof(InspectorFixtureAsset).FullName));

				var fields = component.Component.GetFields(new DebugPageRequest(0, 20));
				Assert.That(fields.Fields.Select(item => item.Name), Is.EquivalentTo(new[] { "Branch", "Commit" }));
				var branch = fields.Fields.Single(field => field.Name == "Branch");
				var read = component.Component.ReadFields(new[] { branch.Id });
				Assert.That(read.Status, Is.EqualTo(DebugOperationStatus.Succeeded));
				Assert.That(read.Value.Single().Snapshot.Value, Is.EqualTo(DebugValue.From("feature/boot-menu")));
			}
			finally
			{
				DebugInspector.Unregister("Tests/Inspector/Asset", asset);
				UnityEngine.Object.DestroyImmediate(asset);
			}
		}

		/// <summary>An empty path is a mistake in the call, not a condition of the run, so it stops there.</summary>
		[Test]
		public void RegisteringWithoutAPathThrows()
		{
			Assert.Throws<ArgumentException>(() => DebugInspector.Register(string.Empty, m_Component, "no-path"));
		}

		/// <summary>
		/// What is still refused says so where the caller is looking. A diagnostic alone is only found by someone who
		/// already suspects something went wrong.
		/// </summary>
		[Test]
		public void ARefusedRegistrationIsReportedToTheConsole()
		{
			var destroyed = new GameObject("DexcInspectorGone");
			UnityEngine.Object.DestroyImmediate(destroyed);
			UnityEngine.TestTools.LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Dexc did not register"));

			DebugInspector.Register("Tests/Inspector/Gone", destroyed, "gone-label");

			Assert.That(
				DebugInspector.GetRegistered(new DebugListQuery("Tests/Inspector/Gone", string.Empty, new DebugPageRequest(0, 20))).Entries,
				Is.Empty
			);
		}
#endif

#if DEXC_DEBUGGER
		/// <summary>A source is asked each time: what it stopped returning is no longer listed.</summary>
		[Test]
		public void ASourceListsOnlyWhatItReturnsNow()
		{
			var query = new DebugListQuery("Tests/Inspector/Source", string.Empty, new DebugPageRequest(0, 20));
			Assert.That(DebugInspector.GetRegistered(query).Entries, Has.Count.EqualTo(1));

			InspectorSource.Target = null;
			Assert.That(DebugInspector.GetRegistered(query).Entries, Is.Empty);
		}

		/// <summary>The children of an object that is gone are none: not the scene's roots shown under it.</summary>
		[Test]
		public void ADestroyedParentHasNoChildren()
		{
			var scene = DebugInspector.GetScenes().Scenes.Single(item => item.Name == m_GameObject.scene.name).Scene;
			var parent = DebugInspector
				.GetHierarchyChildren(new DebugHierarchyChildrenQuery(scene, default(DebugObjectHandle), new DebugPageRequest(0, 100)))
				.Entries.Single(item => item.Path == m_GameObject.name)
				.Target;
			UnityEngine.Object.DestroyImmediate(m_GameObject);

			var children = DebugInspector.GetHierarchyChildren(new DebugHierarchyChildrenQuery(scene, parent, new DebugPageRequest(0, 100)));
			Assert.That(children.Entries, Is.Empty);
		}

		/// <summary>A private field marked on a base class is the derived component's field too, as Unity's Inspector shows it.</summary>
		[Test]
		public void APrivateFieldOfABaseClassIsListed()
		{
			var derived = m_GameObject.AddComponent<InspectorDerivedFixtureComponent>();
			DebugInspector.Register("Tests/Inspector", m_Component, "base-label");
			var target = DebugInspector
				.GetRegistered(new DebugListQuery("Tests/Inspector", string.Empty, new DebugPageRequest(0, 20)))
				.Entries.Single(item => item.Origin == DebugInspectorEntryOrigin.Manual)
				.Target;
			var component = DebugInspector
				.GetComponents(target)
				.Components.Single(item => item.TypeName == typeof(InspectorDerivedFixtureComponent).FullName)
				.Component;
			var field = component.GetFields(new DebugPageRequest(0, 20)).Fields.Single(item => item.Name == "m_BaseValue");
			Assert.That(field.IsRegistered, Is.True);
			var read = component.ReadFields(new[] { field.Id });
			Assert.That(read.Value.Single().Snapshot.Value, Is.EqualTo(DebugValue.From(derived.BaseValue)));
		}

		/// <summary>Two components of one GameObject can have a field of the same name: each keeps its own history.</summary>
		[Test]
		public void AFieldHistoryBelongsToItsOwnComponent()
		{
			m_GameObject.AddComponent<InspectorFixtureComponent>();
			DebugInspector.Register("Tests/Inspector", m_Component, "history-label");
			var target = DebugInspector
				.GetRegistered(new DebugListQuery("Tests/Inspector", string.Empty, new DebugPageRequest(0, 20)))
				.Entries.Single(item => item.Origin == DebugInspectorEntryOrigin.Manual)
				.Target;
			var components = DebugInspector
				.GetComponents(target)
				.Components.Where(item => item.TypeName == typeof(InspectorFixtureComponent).FullName)
				.Select(item => item.Component)
				.ToArray();
			Assert.That(components, Has.Length.EqualTo(2));
			var fields = components
				.Select(component =>
					component
						.GetFields(new DebugPageRequest(0, 20))
						.Fields.Single(item => item.Name == nameof(InspectorFixtureComponent.RegisteredValue))
				)
				.ToArray();

			var write = components[0]
				.WriteField(fields[0].Id, DebugInspectorWriteValue.From(DebugValue.From(9)), DebugInspectorWriteAccess.RegisteredOnly);
			Assert.That(write.Status, Is.EqualTo(DebugOperationStatus.Succeeded));

			Assert.That(components[0].GetHistory(fields[0].Id, new DebugPageRequest(0, 20)).Entries, Has.Count.EqualTo(1));
			Assert.That(components[1].GetHistory(fields[1].Id, new DebugPageRequest(0, 20)).Entries, Is.Empty);
		}

		[Test]
		public void ManualSourceAndSceneReachSharedComponentsAndBoundedFields()
		{
			DebugInspector.Register("Tests/Inspector", m_Component, "manual-label");
			var registered = DebugInspector.GetRegistered(new DebugListQuery("Tests/Inspector", string.Empty, new DebugPageRequest(0, 20)));
			Assert.That(
				registered.Entries.Any(item => item.Origin == DebugInspectorEntryOrigin.Manual && item.Label == "manual-label"),
				Is.True
			);
			Assert.That(registered.Entries.Any(item => item.Origin == DebugInspectorEntryOrigin.Source), Is.True);

			var scene = DebugInspector.GetScenes().Scenes.Single(item => item.Name == m_GameObject.scene.name);
			var hierarchy = DebugInspector.GetHierarchyChildren(
				new DebugHierarchyChildrenQuery(scene.Scene, default(DebugObjectHandle), new DebugPageRequest(0, 100))
			);
			Assert.That(hierarchy.Entries.Any(item => item.Path == m_GameObject.name), Is.True);
			var target = registered.Entries.First(item => item.Origin == DebugInspectorEntryOrigin.Manual).Target;
			var components = DebugInspector.GetComponents(target);
			var component = components.Components.Single(item => item.TypeName == typeof(InspectorFixtureComponent).FullName);
			var fields = component.Component.GetFields(new DebugPageRequest(0, 2));
			Assert.That(fields.Fields, Has.Count.EqualTo(2));
			Assert.That(fields.Page.HasMore, Is.True);
			var read = component.Component.ReadFields(fields.Fields.Select(item => item.Id).ToArray());
			Assert.That(read.Status, Is.EqualTo(DebugOperationStatus.Succeeded));
			Assert.That(read.Value, Has.Count.EqualTo(2));
		}

		[Test]
		public void InspectorQueriesUseSegmentBoundariesAndRejectOversizedPages()
		{
			DebuggerRuntime.ResetForTests();
			DebuggerRuntime.Initialize(options => options.Query.MaxPageSize = 2);
			DebugInspector.Register("Tests/Enemy/Unit", m_Component, "enemy-label");
			var bossObject = new GameObject("DexcInspectorBossFixture");
			try
			{
				var boss = bossObject.AddComponent<InspectorFixtureComponent>();
				DebugInspector.Register("Tests/EnemyBoss/Unit", boss, "boss-label");

				var prefix = DebugInspector.GetRegistered(new DebugListQuery("Tests/Enemy", string.Empty, new DebugPageRequest(0, 2)));
				Assert.That(prefix.Entries.Select(item => item.Path), Is.EqualTo(new[] { "Tests/Enemy/Unit" }));
				var search = DebugInspector.GetRegistered(new DebugListQuery(string.Empty, "Enemy/Unit", new DebugPageRequest(0, 2)));
				Assert.That(search.Entries.Select(item => item.Path), Is.EqualTo(new[] { "Tests/Enemy/Unit" }));

				var target = prefix.Entries.Single().Target;
				var component = DebugInspector
					.GetComponents(target)
					.Components.Single(item => item.TypeName == typeof(InspectorFixtureComponent).FullName)
					.Component;
				var scene = DebugInspector.GetScenes().Scenes.Single(item => item.Name == m_GameObject.scene.name);
				Assert.Throws<ArgumentOutOfRangeException>(() =>
					DebugInspector.GetRegistered(new DebugListQuery(string.Empty, string.Empty, new DebugPageRequest(0, 3)))
				);
				Assert.Throws<ArgumentOutOfRangeException>(() =>
					DebugInspector.GetHierarchyChildren(
						new DebugHierarchyChildrenQuery(scene.Scene, default(DebugObjectHandle), new DebugPageRequest(0, 3))
					)
				);
				Assert.Throws<ArgumentOutOfRangeException>(() =>
					DebugInspector.GetSearchResults(default(DebugInspectorSearchId), new DebugPageRequest(0, 3))
				);
				Assert.Throws<ArgumentOutOfRangeException>(() => DebugInspector.GetWatches(new DebugPageRequest(0, 3)));
				Assert.Throws<ArgumentOutOfRangeException>(() => component.GetFields(new DebugPageRequest(0, 3)));
				Assert.Throws<ArgumentOutOfRangeException>(() =>
					component.GetHistory(default(DebugInspectorFieldId), new DebugPageRequest(0, 3))
				);
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(bossObject);
			}
		}

		[Test]
		public void RegisteredAndUnregisteredWriteEnforceExplicitAccessAndKeepHistory()
		{
			DebugInspector.Register("Tests/Inspector", m_Component, "write-label");
			var target = DebugInspector
				.GetRegistered(new DebugListQuery("Tests/Inspector", string.Empty, new DebugPageRequest(0, 20)))
				.Entries.Single(item => item.Origin == DebugInspectorEntryOrigin.Manual)
				.Target;
			var component = DebugInspector
				.GetComponents(target)
				.Components.Single(item => item.TypeName == typeof(InspectorFixtureComponent).FullName)
				.Component;
			var fields = component.GetFields(new DebugPageRequest(0, 20)).Fields;
			var registered = fields.Single(item => item.Name == nameof(InspectorFixtureComponent.RegisteredValue));
			var unregistered = fields.Single(item => item.Name == nameof(InspectorFixtureComponent.UnregisteredValue));
			var locked = fields.Single(item => item.Name == nameof(InspectorFixtureComponent.LockedValue));

			Assert.That(
				component
					.WriteField(registered.Id, DebugInspectorWriteValue.From(DebugValue.From(9)), DebugInspectorWriteAccess.RegisteredOnly)
					.Status,
				Is.EqualTo(DebugOperationStatus.Succeeded)
			);
			Assert.That(m_Component.RegisteredValue, Is.EqualTo(9));
			Assert.That(
				component
					.WriteField(
						unregistered.Id,
						DebugInspectorWriteValue.From(DebugValue.From(8)),
						DebugInspectorWriteAccess.RegisteredOnly
					)
					.Status,
				Is.EqualTo(DebugOperationStatus.ExplicitAccessRequired)
			);
			Assert.That(
				component
					.WriteField(
						unregistered.Id,
						DebugInspectorWriteValue.From(DebugValue.From(8)),
						DebugInspectorWriteAccess.AllowUnregistered
					)
					.Status,
				Is.EqualTo(DebugOperationStatus.Succeeded)
			);
			Assert.That(
				component
					.WriteField(locked.Id, DebugInspectorWriteValue.From(DebugValue.From(8)), DebugInspectorWriteAccess.RegisteredOnly)
					.Status,
				Is.EqualTo(DebugOperationStatus.ReadOnly)
			);
			Assert.That(component.GetHistory(registered.Id, new DebugPageRequest(0, 20)).Entries, Has.Count.EqualTo(1));
		}

		[Test]
		public void InspectorIntegerNarrowingRejectsOverflow()
		{
			DebugInspector.Register("Tests/Inspector", m_Component, "narrowing-label");
			var target = DebugInspector
				.GetRegistered(new DebugListQuery("Tests/Inspector", string.Empty, new DebugPageRequest(0, 20)))
				.Entries.Single(item => item.Origin == DebugInspectorEntryOrigin.Manual)
				.Target;
			var component = DebugInspector
				.GetComponents(target)
				.Components.Single(item => item.TypeName == typeof(InspectorFixtureComponent).FullName)
				.Component;
			var field = component
				.GetFields(new DebugPageRequest(0, 20))
				.Fields.Single(item => item.Name == nameof(InspectorFixtureComponent.NarrowedValue));

			var result = component.WriteField(
				field.Id,
				DebugInspectorWriteValue.From(DebugValue.From((long)int.MaxValue + 1)),
				DebugInspectorWriteAccess.RegisteredOnly
			);

			Assert.That(result.Status, Is.EqualTo(DebugOperationStatus.Rejected));
			Assert.That(m_Component.NarrowedValue, Is.EqualTo(7));
			Assert.That(component.GetHistory(field.Id, new DebugPageRequest(0, 20)).Entries, Is.Empty);
		}

		[Test]
		public void InspectorEnumUsesStringKeySnapshotAndRejectsNumericValue()
		{
			DebugInspector.Register("Tests/Inspector", m_Component, "enum-label");
			var target = DebugInspector
				.GetRegistered(new DebugListQuery("Tests/Inspector", string.Empty, new DebugPageRequest(0, 20)))
				.Entries.Single(item => item.Origin == DebugInspectorEntryOrigin.Manual)
				.Target;
			var component = DebugInspector
				.GetComponents(target)
				.Components.Single(item => item.TypeName == typeof(InspectorFixtureComponent).FullName)
				.Component;
			var field = component
				.GetFields(new DebugPageRequest(0, 20))
				.Fields.Single(item => item.Name == nameof(InspectorFixtureComponent.Mode));
			var initial = component.ReadFields(new[] { field.Id }).Value.Single().Snapshot;

			Assert.That(initial.State, Is.EqualTo(DebugInspectorSnapshotState.Value));
			Assert.That(initial.Value, Is.EqualTo(DebugValue.From("First")));
			Assert.That(
				component
					.WriteField(field.Id, DebugInspectorWriteValue.From(DebugValue.From(1)), DebugInspectorWriteAccess.RegisteredOnly)
					.Status,
				Is.EqualTo(DebugOperationStatus.Rejected)
			);
			Assert.That(
				component
					.WriteField(
						field.Id,
						DebugInspectorWriteValue.From(DebugValue.From("Second")),
						DebugInspectorWriteAccess.RegisteredOnly
					)
					.Status,
				Is.EqualTo(DebugOperationStatus.Succeeded)
			);
			Assert.That(m_Component.Mode, Is.EqualTo(InspectorFixtureComponent.InspectorMode.Second));
		}

		[Test]
		public void WatchHonorsIntervalAndTurnsMissingAfterTargetDestroy()
		{
			DebugInspector.Register("Tests/Inspector", m_Component, "watch-label");
			var target = DebugInspector
				.GetRegistered(new DebugListQuery("Tests/Inspector", string.Empty, new DebugPageRequest(0, 20)))
				.Entries.Single(item => item.Origin == DebugInspectorEntryOrigin.Manual)
				.Target;
			var component = DebugInspector
				.GetComponents(target)
				.Components.Single(item => item.TypeName == typeof(InspectorFixtureComponent).FullName)
				.Component;
			var field = component
				.GetFields(new DebugPageRequest(0, 20))
				.Fields.Single(item => item.Name == nameof(InspectorFixtureComponent.RegisteredValue));
			Assert.Throws<ArgumentOutOfRangeException>(() => component.AddWatch(field.Id, TimeSpan.FromMilliseconds(-1)));
			var watch = component.AddWatch(field.Id, TimeSpan.Zero);
			Assert.That(watch.Status, Is.EqualTo(DebugOperationStatus.Succeeded));
			m_Component.RegisteredValue = 42;
			DebuggerRuntime.CurrentSession.Inspector.Tick();
			Assert.That(
				DebugInspector.GetWatches(new DebugPageRequest(0, 20)).Entries.Single().Snapshot.Value,
				Is.EqualTo(DebugValue.From(42))
			);

			UnityEngine.Object.DestroyImmediate(m_GameObject);
			m_GameObject = null;
			DebuggerRuntime.CurrentSession.Inspector.Tick();
			var missing = DebugInspector.GetWatches(new DebugPageRequest(0, 20)).Entries.Single();
			Assert.That(missing.State, Is.EqualTo(DebugInspectorWatchState.Missing));
			DebugInspector.RemoveWatch(watch.Value);
			DebugInspector.RemoveWatch(watch.Value);
			Assert.That(DebugInspector.GetWatches(new DebugPageRequest(0, 20)).Entries, Is.Empty);
		}

		[Test]
		public void SceneSearchIsIncrementalAndResultBounded()
		{
			DebuggerRuntime.ResetForTests();
			DebuggerRuntime.Initialize(options => options.Query.MaxSearchResults = 1);
			var second = new GameObject("DexcInspectorSearchMatch");
			second.AddComponent<InspectorFixtureComponent>();
			for (var index = 0; index < 80; index++)
				new GameObject("DexcInspectorSearchNoise_" + index).transform.SetParent(m_GameObject.transform);
			var scene = DebugInspector.GetScenes().Scenes.Single(item => item.Name == m_GameObject.scene.name);
			var start = DebugInspector.StartSearch(
				new DebugInspectorSearchQuery(scene.Scene, "DexcInspector", typeof(InspectorFixtureComponent).FullName, true)
			);
			Assert.That(start.Succeeded, Is.True);
			var initial = DebugInspector.GetSearchResults(start.Value, new DebugPageRequest(0, 20));
			Assert.That(initial.State, Is.EqualTo(DebugInspectorSearchState.Searching));
			DebuggerRuntime.CurrentSession.Inspector.Tick();
			var bounded = DebugInspector.GetSearchResults(start.Value, new DebugPageRequest(0, 20));
			Assert.That(bounded.InspectedCount, Is.LessThanOrEqualTo(64));
			for (var frame = 0; frame < 10 && bounded.State == DebugInspectorSearchState.Searching; frame++)
			{
				DebuggerRuntime.CurrentSession.Inspector.Tick();
				bounded = DebugInspector.GetSearchResults(start.Value, new DebugPageRequest(0, 20));
			}
			Assert.That(bounded.State, Is.EqualTo(DebugInspectorSearchState.Completed));
			Assert.That(bounded.Entries, Has.Count.EqualTo(1));
			Assert.That(bounded.IsTruncated, Is.True);
			UnityEngine.Object.DestroyImmediate(second);
		}

		[Test]
		public void MultipleSceneSearchesShareOneFrameWorkBudget()
		{
			DebuggerRuntime.ResetForTests();
			DebuggerRuntime.Initialize(options => options.Query.MaxSearchResults = 100);
			for (var index = 0; index < 80; index++)
				new GameObject("DexcInspectorAggregateNoise_" + index).transform.SetParent(m_GameObject.transform);
			var scene = DebugInspector.GetScenes().Scenes.Single(item => item.Name == m_GameObject.scene.name);
			var first = DebugInspector.StartSearch(new DebugInspectorSearchQuery(scene.Scene, "DexcInspector", string.Empty, true));
			var second = DebugInspector.StartSearch(new DebugInspectorSearchQuery(scene.Scene, "DexcInspector", string.Empty, true));
			DebuggerRuntime.CurrentSession.Inspector.Tick();
			var firstPage = DebugInspector.GetSearchResults(first.Value, new DebugPageRequest(0, 100));
			var secondPage = DebugInspector.GetSearchResults(second.Value, new DebugPageRequest(0, 100));
			Assert.That(firstPage.InspectedCount + secondPage.InspectedCount, Is.LessThanOrEqualTo(64));
		}

		[Test]
		public void ReadFieldsKeepsUnsupportedAndPartialReadFailurePerField()
		{
			DebugInspector.Register("Tests/Inspector", m_Component, "read-label");
			var target = DebugInspector
				.GetRegistered(new DebugListQuery("Tests/Inspector", string.Empty, new DebugPageRequest(0, 20)))
				.Entries.Single(item => item.Origin == DebugInspectorEntryOrigin.Manual)
				.Target;
			var component = DebugInspector
				.GetComponents(target)
				.Components.Single(item => item.TypeName == typeof(InspectorFixtureComponent).FullName)
				.Component;
			var fields = component.GetFields(new DebugPageRequest(0, 20)).Fields;
			var unsupported = fields.Single(item => item.Name == nameof(InspectorFixtureComponent.UnsupportedValue));
			var otherObject = new GameObject("DexcInspectorOther");
			var otherComponent = otherObject.AddComponent<InspectorFixtureComponent>();
			DebugInspector.Register("Tests/Inspector/Other", otherComponent, "other-label");
			var otherTarget = DebugInspector
				.GetRegistered(new DebugListQuery("Tests/Inspector/Other", string.Empty, new DebugPageRequest(0, 20)))
				.Entries.Single(item => item.Origin == DebugInspectorEntryOrigin.Manual)
				.Target;
			var otherHandle = DebugInspector
				.GetComponents(otherTarget)
				.Components.Single(item => item.TypeName == typeof(InspectorFixtureComponent).FullName)
				.Component;
			var otherField = otherHandle
				.GetFields(new DebugPageRequest(0, 20))
				.Fields.Single(item => item.Name == nameof(InspectorFixtureComponent.RegisteredValue));
			var good = fields.Single(item => item.Name == nameof(InspectorFixtureComponent.RegisteredValue));

			var read = component.ReadFields(new[] { good.Id, unsupported.Id, otherField.Id });
			Assert.That(
				read.Value.Single(item => item.Id == unsupported.Id).Snapshot.State,
				Is.EqualTo(DebugInspectorSnapshotState.Unsupported)
			);
			Assert.That(
				read.Value.Single(item => item.Id == otherField.Id).Snapshot.State,
				Is.EqualTo(DebugInspectorSnapshotState.ReadFailure)
			);
			UnityEngine.Object.DestroyImmediate(otherObject);
		}

		[Test]
		public async Task SceneUnloadRemovesRegisteredTarget()
		{
			var scene = SceneManager.CreateScene("DexcInspectorUnload_" + Guid.NewGuid().ToString("N"));
			var sceneObject = new GameObject("DexcInspectorUnloadTarget");
			SceneManager.MoveGameObjectToScene(sceneObject, scene);
			var sceneComponent = sceneObject.AddComponent<InspectorFixtureComponent>();
			DebugInspector.Register("Tests/Inspector/Unload", sceneComponent, "unload-label");
			Assert.That(
				DebugInspector
					.GetRegistered(new DebugListQuery("Tests/Inspector/Unload", string.Empty, new DebugPageRequest(0, 20)))
					.Entries,
				Has.Count.EqualTo(1)
			);
			var unload = SceneManager.UnloadSceneAsync(scene);
			while (unload != null && !unload.isDone)
				await Task.Yield();
			Assert.That(
				DebugInspector
					.GetRegistered(new DebugListQuery("Tests/Inspector/Unload", string.Empty, new DebugPageRequest(0, 20)))
					.Entries,
				Is.Empty
			);
		}

		[Test]
		public void WatchBudgetAndReinitializeKeepSessionBoundaries()
		{
			DebuggerRuntime.ResetForTests();
			DebuggerRuntime.Initialize(options => options.Inspector.MaxWatchUpdatesPerFrame = 1);
			DebugInspector.Register("Tests/Inspector/Budget", m_Component, "budget-label");
			var target = DebugInspector
				.GetRegistered(new DebugListQuery("Tests/Inspector/Budget", string.Empty, new DebugPageRequest(0, 20)))
				.Entries.Single(item => item.Origin == DebugInspectorEntryOrigin.Manual)
				.Target;
			var component = DebugInspector
				.GetComponents(target)
				.Components.Single(item => item.TypeName == typeof(InspectorFixtureComponent).FullName)
				.Component;
			var fields = component.GetFields(new DebugPageRequest(0, 20)).Fields;
			var first = fields.Single(item => item.Name == nameof(InspectorFixtureComponent.RegisteredValue));
			var second = fields.Single(item => item.Name == nameof(InspectorFixtureComponent.SecondRegisteredValue));
			var firstWatch = component.AddWatch(first.Id, TimeSpan.Zero);
			var secondWatch = component.AddWatch(second.Id, TimeSpan.Zero);
			m_Component.RegisteredValue = 40;
			m_Component.SecondRegisteredValue = 60;
			DebuggerRuntime.CurrentSession.Inspector.Tick();
			var watches = DebugInspector.GetWatches(new DebugPageRequest(0, 20)).Entries;
			Assert.That(
				watches.Count(item =>
					item.Snapshot.Value != null && item.Snapshot.Value.Equals(DebugValue.From(40))
					|| item.Snapshot.Value != null && item.Snapshot.Value.Equals(DebugValue.From(60))
				),
				Is.EqualTo(1)
			);

			DebuggerRuntime.Initialize();
			Assert.That(component.ReadFields(new[] { first.Id }).Status, Is.EqualTo(DebugOperationStatus.Stale));
			Assert.That(DebugInspector.GetWatches(new DebugPageRequest(0, 20)).Entries, Is.Empty);
			Assert.That(firstWatch.Value.IsValid, Is.True);
			Assert.That(secondWatch.Value.IsValid, Is.True);
		}

		/// <summary>
		/// A value the game cannot hand over - here a collection whose Count throws - is a read failure. The watch keeps
		/// that as its state and points at the diagnostic instead of dropping the field.
		/// </summary>
		[Test]
		public void WatchReadFailureKeepsDiagnosticState()
		{
			m_GameObject.AddComponent<InspectorReadFailureComponent>();
			DebugInspector.Register("Tests/Inspector/ReadFailure", m_Component, "failure-label");
			var target = DebugInspector
				.GetRegistered(new DebugListQuery("Tests/Inspector/ReadFailure", string.Empty, new DebugPageRequest(0, 20)))
				.Entries.Single(item => item.Origin == DebugInspectorEntryOrigin.Manual)
				.Target;
			var component = DebugInspector
				.GetComponents(target)
				.Components.Single(item => item.TypeName == typeof(InspectorReadFailureComponent).FullName)
				.Component;
			var field = component
				.GetFields(new DebugPageRequest(0, 20))
				.Fields.Single(item => item.Name == nameof(InspectorReadFailureComponent.Failing));
			var watch = component.AddWatch(field.Id, TimeSpan.Zero);
			Assert.That(watch.Status, Is.EqualTo(DebugOperationStatus.Succeeded));
			DebuggerRuntime.CurrentSession.Inspector.Tick();

			var entry = DebugInspector.GetWatches(new DebugPageRequest(0, 20)).Entries.Single();
			Assert.That(entry.State, Is.EqualTo(DebugInspectorWatchState.ReadFailure));
			Assert.That(entry.Snapshot.DiagnosticId.IsValid, Is.True);
		}
#endif
	}

	public class InspectorBaseFixtureComponent : MonoBehaviour
	{
		[DebugField]
		private int m_BaseValue = 5;

		internal int BaseValue => m_BaseValue;
	}

	public sealed class InspectorDerivedFixtureComponent : InspectorBaseFixtureComponent { }

	public sealed class InspectorFixtureComponent : MonoBehaviour
	{
		public enum InspectorMode
		{
			First,
			Second,
		}

		[DebugField]
		public int RegisteredValue = 3;

		[DebugField]
		public int SecondRegisteredValue = 6;

		public int UnregisteredValue = 4;

		[DebugField(Access = DebugFieldAccess.ReadOnly)]
		public int LockedValue = 5;

		[DebugField]
		public int NarrowedValue = 7;

		[DebugField]
		public InspectorMode Mode = InspectorMode.First;

		public UnsupportedInspectorValue UnsupportedValue = new UnsupportedInspectorValue();
	}

	/// <summary>An asset, not a scene object: it has no components, so its own fields are all there is.</summary>
	public sealed class InspectorFixtureAsset : ScriptableObject
	{
		[DebugField]
		public string Branch = "feature/boot-menu";

		public string Commit = "66df92d";
	}

	public sealed class UnsupportedInspectorValue
	{
		public int Value;
	}

	public sealed class InspectorReadFailureComponent : MonoBehaviour
	{
		[DebugField]
		public ThrowingCollection Failing = new ThrowingCollection();
	}

	/// <summary>A collection the game refuses to count: the shape of a read that fails inside user code.</summary>
	public sealed class ThrowingCollection : System.Collections.ICollection
	{
		public int Count => throw new InvalidOperationException("The collection cannot be counted.");

		public bool IsSynchronized => false;

		public object SyncRoot => this;

		public void CopyTo(Array array, int index) { }

		public System.Collections.IEnumerator GetEnumerator() => Array.Empty<object>().GetEnumerator();
	}

	public static class InspectorSource
	{
		public static InspectorFixtureComponent Target;

		[DebugInspectorSource("Tests/Inspector/Source")]
		private static IEnumerable<DebugSourceEntry<InspectorFixtureComponent>> GetEntries()
		{
			if (Target == null)
				yield break;
			yield return new DebugSourceEntry<InspectorFixtureComponent>(Target, "source-label", "entry");
		}
	}
}
