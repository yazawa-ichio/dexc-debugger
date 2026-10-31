using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Dexc;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using DebuggerRuntime = Dexc.DexcDebugger;

namespace DexcDebugger.Tests
{
	public sealed class DexcPerformanceAcceptanceTests
	{
		private GameObject m_HierarchyRoot;

		[SetUp]
		public void SetUp()
		{
			DebuggerRuntime.Initialize();
		}

		[TearDown]
		public void TearDown()
		{
			if (m_HierarchyRoot != null)
				UnityEngine.Object.DestroyImmediate(m_HierarchyRoot);
			DebuggerRuntime.ResetForTests();
		}

#if DEXC_DEBUGGER
		[UnityTest]
		public IEnumerator RepresentativeScenarioStaysBoundedAndWritesEvidence()
		{
			var evidence = new DexcPerformanceEvidence
			{
				UnityVersion = Application.unityVersion,
				CommandFixtureCount = DebugCommand
					.Registry.GetList(new DebugListQuery("I18/Performance/Command", string.Empty, new DebugPageRequest(0, 100)))
					.Items.Count,
				SettingFixtureCount = DebugSetting
					.GetList(new DebugListQuery(string.Empty, "I18/Performance/Setting", new DebugPageRequest(0, 100)))
					.Items.Count,
			};
			var options = DebuggerRuntime.CurrentSession.Options;
			evidence.HistoryCapacity = options.HistoryCapacityPerFeature;
			evidence.OperationDiagnosticCapacity = options.OperationCapacity;
			evidence.WatchCapacity = options.MaxWatchCount;
			evidence.WatchUpdatesPerFrame = options.MaxWatchUpdatesPerFrame;
			evidence.SubjectCapacity = options.MaxSubjectCount;
			evidence.MetadataCapacity = options.MaxMetadataPerSubject;
			evidence.VisualCapacityPerSubject = options.MaxVisualsPerSubject;
			evidence.VisualCapacityPerFrame = options.MaxVisualsPerFrame;
			evidence.PageSize = options.MaxPageSize;
			evidence.HierarchyChildren = options.MaxHierarchyChildren;
			evidence.SearchResults = options.MaxSearchResults;
			evidence.SearchWorkPerFrame = 64;

			var initTimer = Stopwatch.StartNew();
			DebuggerRuntime.Initialize();
			initTimer.Stop();
			evidence.ReinitializeMilliseconds = initTimer.Elapsed.TotalMilliseconds;

			for (var warmup = 0; warmup < 8; warmup++)
			{
				DebugCommand.Registry.GetList(new DebugListQuery(string.Empty, string.Empty, new DebugPageRequest(0, 100)));
				DebugSetting.GetList(new DebugListQuery(string.Empty, string.Empty, new DebugPageRequest(0, 100)));
			}
			GC.Collect();
			GC.WaitForPendingFinalizers();
			GC.Collect();
			var queryAllocatedBefore = GC.GetAllocatedBytesForCurrentThread();
			var queryTimer = Stopwatch.StartNew();
			for (var iteration = 0; iteration < 120; iteration++)
			{
				DebugCommand.Registry.GetList(new DebugListQuery(string.Empty, string.Empty, new DebugPageRequest(0, 100)));
				DebugSetting.GetList(new DebugListQuery(string.Empty, string.Empty, new DebugPageRequest(0, 100)));
			}
			queryTimer.Stop();
			evidence.QueryMilliseconds = queryTimer.Elapsed.TotalMilliseconds;
			evidence.QueryAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - queryAllocatedBefore;

			m_HierarchyRoot = new GameObject("DexcI18HierarchyRoot");
			for (var index = 0; index < 256; index++)
			{
				var child = new GameObject("DexcI18Node_" + index.ToString("D3"));
				child.transform.SetParent(m_HierarchyRoot.transform, false);
				child.AddComponent<DexcPerformanceComponent>();
			}

			var scene = DebugInspector.GetScenes().Scenes.Single(item => item.Name == SceneManager.GetActiveScene().name);
			var roots = DebugInspector.GetHierarchyChildren(
				new DebugHierarchyChildrenQuery(scene.Scene, default(DebugObjectHandle), new DebugPageRequest(0, options.MaxPageSize))
			);
			var root = roots.Entries.Single(item => item.Label == m_HierarchyRoot.name);
			var children = DebugInspector.GetHierarchyChildren(
				new DebugHierarchyChildrenQuery(scene.Scene, root.Target, new DebugPageRequest(0, options.MaxPageSize))
			);
			evidence.HierarchyNodeCount = children.Page.TotalCount;
			evidence.HierarchyPageCount = children.Entries.Count;
			Assert.That(children.Entries, Has.Count.EqualTo(options.MaxHierarchyChildren));

			var noMatch = DebugInspector.StartSearch(new DebugInspectorSearchQuery(scene.Scene, "DexcI18NoMatch", string.Empty, null));
			var lastInspected = 0;
			var searchFrames = 0;
			var maxInspectedPerFrame = 0;
			DebugInspectorSearchPage searchPage = null;
			while (searchFrames < 20)
			{
				yield return null;
				searchFrames++;
				searchPage = DebugInspector.GetSearchResults(noMatch.Value, new DebugPageRequest(0, 100));
				var inspectedThisFrame = searchPage.InspectedCount - lastInspected;
				maxInspectedPerFrame = Math.Max(maxInspectedPerFrame, inspectedThisFrame);
				lastInspected = searchPage.InspectedCount;
				if (searchPage.State != DebugInspectorSearchState.Searching)
					break;
			}
			evidence.SearchFrames = searchFrames;
			evidence.SearchInspectedCount = searchPage == null ? 0 : searchPage.InspectedCount;
			evidence.SearchMaxInspectedPerFrame = maxInspectedPerFrame;
			Assert.That(searchPage, Is.Not.Null);
			Assert.That(searchPage.State, Is.EqualTo(DebugInspectorSearchState.Completed));
			Assert.That(maxInspectedPerFrame, Is.LessThanOrEqualTo(evidence.SearchWorkPerFrame));
			Assert.That(searchPage.InspectedCount, Is.GreaterThanOrEqualTo(256));

			var componentSearch = DebugInspector.StartSearch(
				new DebugInspectorSearchQuery(scene.Scene, string.Empty, typeof(DexcPerformanceComponent).FullName, null)
			);
			DebugInspectorSearchPage componentPage = null;
			for (var frame = 0; frame < 20; frame++)
			{
				yield return null;
				componentPage = DebugInspector.GetSearchResults(componentSearch.Value, new DebugPageRequest(0, 100));
				if (componentPage.State != DebugInspectorSearchState.Searching)
					break;
			}
			Assert.That(componentPage, Is.Not.Null);
			Assert.That(componentPage.Entries, Is.Not.Empty);
			var components = DebugInspector.GetComponents(componentPage.Entries[0].Target);
			var component = components.Entries.Single(item => item.TypeName == typeof(DexcPerformanceComponent).FullName);
			var field = component
				.Component.GetFields(new DebugPageRequest(0, 20))
				.Entries.Single(item => item.Name == nameof(DexcPerformanceComponent.Value));
			for (var index = 0; index < 32; index++)
			{
				var watch = component.Component.AddWatch(field.Id, TimeSpan.Zero);
				Assert.That(watch.Status, Is.EqualTo(DebugOperationStatus.Succeeded));
			}
			evidence.WatchCount = DebugInspector.GetWatches(new DebugPageRequest(0, 100)).Entries.Count;
			Assert.That(evidence.WatchCount, Is.EqualTo(32));

			var subject = DebugDisplay.CreateSubject("I18/Performance", m_HierarchyRoot);
			var managedBefore = GC.GetTotalMemory(true);
			for (var cycle = 0; cycle < 8; cycle++)
			{
				for (var index = 0; index < 256; index++)
				{
					subject.DrawLine(DebugDrawOptions.Frame("I18/Performance/Visual/" + index, Color.white), Vector3.zero, Vector3.right);
				}
				yield return null;
				Assert.That(DebugDisplay.TryGetCompletedFrame(out var frame), Is.True);
				var subjectSnapshot = frame.Subjects.Single(item => item.Category == "I18/Performance");
				evidence.MaxVisualsObserved = Math.Max(evidence.MaxVisualsObserved, subjectSnapshot.Visuals.Count);
			}
			subject.Dispose();
			GC.Collect();
			GC.WaitForPendingFinalizers();
			GC.Collect();
			evidence.ManagedBytesAfterCycles = GC.GetTotalMemory(true) - managedBefore;
			Assert.That(evidence.MaxVisualsObserved, Is.EqualTo(256));
			Assert.That(evidence.ManagedBytesAfterCycles, Is.LessThan(16 * 1024 * 1024));

			DexcPerformanceHighPriorityEditor.Reset();
			DexcPerformanceLowPriorityEditor.Reset();
			var descriptor = new DebugInputEditorDescriptor(typeof(InputInt), DebugInputSchema.SignedInteger(), string.Empty, "Runtime");
			var providerRegistry = DexcDebugInputEditorRegistry.Create(
				new[] { typeof(DexcPerformanceLowPriorityEditor), typeof(DexcPerformanceHighPriorityEditor) }
			);
			Assert.That(providerRegistry.TrySelect(descriptor, out var provider, out var providerError), Is.True);
			for (var iteration = 0; iteration < 120; iteration++)
			{
				Assert.That(providerRegistry.TrySelect(descriptor, out var cachedProvider, out providerError), Is.True);
				Assert.That(cachedProvider, Is.SameAs(provider));
			}
			evidence.ProviderCanEditCalls = DexcPerformanceHighPriorityEditor.CanEditCount + DexcPerformanceLowPriorityEditor.CanEditCount;
			Assert.That(evidence.ProviderCanEditCalls, Is.EqualTo(2));

			var layout = DexcRuntimeUIRenderer
				.CalculateLayoutForTests(1280, 720, new Rect(0, 0, 1280, 720), DexcRuntimeUIPage.Home)
				.LogicalCoordinates();
			var safeArea = layout.Panel;
			var rowHeight = layout.RowHeight;
			evidence.PanelWidth = safeArea.width;
			evidence.PanelHeight = safeArea.height;
			evidence.ListRowHeight = rowHeight;
			Assert.That(safeArea.width, Is.InRange(260f, 480f));
			Assert.That(safeArea.height, Is.InRange(220f, 640f));
			Assert.That(rowHeight, Is.EqualTo(28f));

			Assert.That(evidence.CommandFixtureCount, Is.GreaterThanOrEqualTo(16));
			Assert.That(evidence.SettingFixtureCount, Is.GreaterThanOrEqualTo(16));
			Assert.That(evidence.ReinitializeMilliseconds, Is.LessThan(5000d));
			Assert.That(evidence.QueryMilliseconds, Is.LessThan(1000d));
			Assert.That(evidence.QueryAllocatedBytes, Is.LessThan(8 * 1024 * 1024));
			WriteEvidence(evidence);
		}

		private static void WriteEvidence(DexcPerformanceEvidence evidence)
		{
			var path = Environment.GetEnvironmentVariable("DEXC_I18_PERFORMANCE_RESULT");
			if (string.IsNullOrEmpty(path))
				return;
			var directory = Path.GetDirectoryName(Path.GetFullPath(path));
			if (!string.IsNullOrEmpty(directory))
				Directory.CreateDirectory(directory);
			File.WriteAllText(path, JsonUtility.ToJson(evidence, true));
		}
#endif
	}

	[Serializable]
	public sealed class DexcPerformanceEvidence
	{
		public string UnityVersion;
		public int CommandFixtureCount;
		public int SettingFixtureCount;
		public double ReinitializeMilliseconds;
		public double QueryMilliseconds;
		public long QueryAllocatedBytes;
		public int HistoryCapacity;
		public int OperationDiagnosticCapacity;
		public long OperationDiagnosticBytes;
		public int WatchCapacity;
		public int WatchUpdatesPerFrame;
		public int SubjectCapacity;
		public int MetadataCapacity;
		public int VisualCapacityPerSubject;
		public int VisualCapacityPerFrame;
		public long SnapshotBytes;
		public int PageSize;
		public int HierarchyChildren;
		public int SearchResults;
		public int SearchWorkPerFrame;
		public int HierarchyNodeCount;
		public int HierarchyPageCount;
		public int SearchFrames;
		public int SearchInspectedCount;
		public int SearchMaxInspectedPerFrame;
		public int WatchCount;
		public int MaxVisualsObserved;
		public long ManagedBytesAfterCycles;
		public int ProviderCanEditCalls;
		public float PanelWidth;
		public float PanelHeight;
		public float ListRowHeight;
	}

	public sealed class DexcPerformanceComponent : MonoBehaviour
	{
		[DebugField]
		public int Value;
	}

	[DebugInputEditor(1, "Runtime")]
	public sealed class DexcPerformanceLowPriorityEditor : DebugInputEditor
	{
		public static int CanEditCount;

		public static void Reset()
		{
			CanEditCount = 0;
		}

		public override bool CanEdit(DebugInputEditorDescriptor descriptor)
		{
			CanEditCount++;
			return true;
		}

		public override void OnGUI(DebugInputEditorContext context, DebugInputEdit edit) { }
	}

	[DebugInputEditor(5, "Runtime")]
	public sealed class DexcPerformanceHighPriorityEditor : DebugInputEditor
	{
		public static int CanEditCount;

		public static void Reset()
		{
			CanEditCount = 0;
		}

		public override bool CanEdit(DebugInputEditorDescriptor descriptor)
		{
			CanEditCount++;
			return true;
		}

		public override void OnGUI(DebugInputEditorContext context, DebugInputEdit edit) { }
	}

	[DebugCommand("I18/Performance/Command/00", Access = DebugCommandAccess.ReadOnly)]
	public sealed class DexcPerformanceCommand00 : DebugCommand
	{
		public InputInt Value = new InputInt(0);

		protected override void Execute() { }
	}

	[DebugCommand("I18/Performance/Command/01", Access = DebugCommandAccess.ReadOnly)]
	public sealed class DexcPerformanceCommand01 : DebugCommand
	{
		public InputInt Value = new InputInt(1);

		protected override void Execute() { }
	}

	[DebugCommand("I18/Performance/Command/02", Access = DebugCommandAccess.ReadOnly)]
	public sealed class DexcPerformanceCommand02 : DebugCommand
	{
		public InputInt Value = new InputInt(2);

		protected override void Execute() { }
	}

	[DebugCommand("I18/Performance/Command/03", Access = DebugCommandAccess.ReadOnly)]
	public sealed class DexcPerformanceCommand03 : DebugCommand
	{
		public InputInt Value = new InputInt(3);

		protected override void Execute() { }
	}

	[DebugCommand("I18/Performance/Command/04", Access = DebugCommandAccess.ReadOnly)]
	public sealed class DexcPerformanceCommand04 : DebugCommand
	{
		public InputInt Value = new InputInt(4);

		protected override void Execute() { }
	}

	[DebugCommand("I18/Performance/Command/05", Access = DebugCommandAccess.ReadOnly)]
	public sealed class DexcPerformanceCommand05 : DebugCommand
	{
		public InputInt Value = new InputInt(5);

		protected override void Execute() { }
	}

	[DebugCommand("I18/Performance/Command/06", Access = DebugCommandAccess.ReadOnly)]
	public sealed class DexcPerformanceCommand06 : DebugCommand
	{
		public InputInt Value = new InputInt(6);

		protected override void Execute() { }
	}

	[DebugCommand("I18/Performance/Command/07", Access = DebugCommandAccess.ReadOnly)]
	public sealed class DexcPerformanceCommand07 : DebugCommand
	{
		public InputInt Value = new InputInt(7);

		protected override void Execute() { }
	}

	[DebugCommand("I18/Performance/Command/08", Access = DebugCommandAccess.ReadOnly)]
	public sealed class DexcPerformanceCommand08 : DebugCommand
	{
		public InputInt Value = new InputInt(8);

		protected override void Execute() { }
	}

	[DebugCommand("I18/Performance/Command/09", Access = DebugCommandAccess.ReadOnly)]
	public sealed class DexcPerformanceCommand09 : DebugCommand
	{
		public InputInt Value = new InputInt(9);

		protected override void Execute() { }
	}

	[DebugCommand("I18/Performance/Command/10", Access = DebugCommandAccess.ReadOnly)]
	public sealed class DexcPerformanceCommand10 : DebugCommand
	{
		public InputInt Value = new InputInt(10);

		protected override void Execute() { }
	}

	[DebugCommand("I18/Performance/Command/11", Access = DebugCommandAccess.ReadOnly)]
	public sealed class DexcPerformanceCommand11 : DebugCommand
	{
		public InputInt Value = new InputInt(11);

		protected override void Execute() { }
	}

	[DebugCommand("I18/Performance/Command/12", Access = DebugCommandAccess.ReadOnly)]
	public sealed class DexcPerformanceCommand12 : DebugCommand
	{
		public InputInt Value = new InputInt(12);

		protected override void Execute() { }
	}

	[DebugCommand("I18/Performance/Command/13", Access = DebugCommandAccess.ReadOnly)]
	public sealed class DexcPerformanceCommand13 : DebugCommand
	{
		public InputInt Value = new InputInt(13);

		protected override void Execute() { }
	}

	[DebugCommand("I18/Performance/Command/14", Access = DebugCommandAccess.ReadOnly)]
	public sealed class DexcPerformanceCommand14 : DebugCommand
	{
		public InputInt Value = new InputInt(14);

		protected override void Execute() { }
	}

	[DebugCommand("I18/Performance/Command/15", Access = DebugCommandAccess.ReadOnly)]
	public sealed class DexcPerformanceCommand15 : DebugCommand
	{
		public InputInt Value = new InputInt(15);

		protected override void Execute() { }
	}

	public sealed class DexcPerformanceSetting00 : DebugSettingSet<DexcPerformanceSetting00>
	{
		[DebugSetting("I18/Performance/Setting/00")]
		public int Value { get; private set; }
	}

	public sealed class DexcPerformanceSetting01 : DebugSettingSet<DexcPerformanceSetting01>
	{
		[DebugSetting("I18/Performance/Setting/01")]
		public int Value { get; private set; }
	}

	public sealed class DexcPerformanceSetting02 : DebugSettingSet<DexcPerformanceSetting02>
	{
		[DebugSetting("I18/Performance/Setting/02")]
		public int Value { get; private set; }
	}

	public sealed class DexcPerformanceSetting03 : DebugSettingSet<DexcPerformanceSetting03>
	{
		[DebugSetting("I18/Performance/Setting/03")]
		public int Value { get; private set; }
	}

	public sealed class DexcPerformanceSetting04 : DebugSettingSet<DexcPerformanceSetting04>
	{
		[DebugSetting("I18/Performance/Setting/04")]
		public int Value { get; private set; }
	}

	public sealed class DexcPerformanceSetting05 : DebugSettingSet<DexcPerformanceSetting05>
	{
		[DebugSetting("I18/Performance/Setting/05")]
		public int Value { get; private set; }
	}

	public sealed class DexcPerformanceSetting06 : DebugSettingSet<DexcPerformanceSetting06>
	{
		[DebugSetting("I18/Performance/Setting/06")]
		public int Value { get; private set; }
	}

	public sealed class DexcPerformanceSetting07 : DebugSettingSet<DexcPerformanceSetting07>
	{
		[DebugSetting("I18/Performance/Setting/07")]
		public int Value { get; private set; }
	}

	public sealed class DexcPerformanceSetting08 : DebugSettingSet<DexcPerformanceSetting08>
	{
		[DebugSetting("I18/Performance/Setting/08")]
		public int Value { get; private set; }
	}

	public sealed class DexcPerformanceSetting09 : DebugSettingSet<DexcPerformanceSetting09>
	{
		[DebugSetting("I18/Performance/Setting/09")]
		public int Value { get; private set; }
	}

	public sealed class DexcPerformanceSetting10 : DebugSettingSet<DexcPerformanceSetting10>
	{
		[DebugSetting("I18/Performance/Setting/10")]
		public int Value { get; private set; }
	}

	public sealed class DexcPerformanceSetting11 : DebugSettingSet<DexcPerformanceSetting11>
	{
		[DebugSetting("I18/Performance/Setting/11")]
		public int Value { get; private set; }
	}

	public sealed class DexcPerformanceSetting12 : DebugSettingSet<DexcPerformanceSetting12>
	{
		[DebugSetting("I18/Performance/Setting/12")]
		public int Value { get; private set; }
	}

	public sealed class DexcPerformanceSetting13 : DebugSettingSet<DexcPerformanceSetting13>
	{
		[DebugSetting("I18/Performance/Setting/13")]
		public int Value { get; private set; }
	}

	public sealed class DexcPerformanceSetting14 : DebugSettingSet<DexcPerformanceSetting14>
	{
		[DebugSetting("I18/Performance/Setting/14")]
		public int Value { get; private set; }
	}

	public sealed class DexcPerformanceSetting15 : DebugSettingSet<DexcPerformanceSetting15>
	{
		[DebugSetting("I18/Performance/Setting/15")]
		public int Value { get; private set; }
	}
}
