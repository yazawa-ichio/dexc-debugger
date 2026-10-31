using System;
using System.Collections.Generic;
using System.Linq;
using Dexc;
using NUnit.Framework;
using DebuggerRuntime = Dexc.DexcDebugger;

namespace DexcDebugger.Tests
{
	public sealed class DexcCommonContractsTests
	{
		[SetUp]
		public void SetUp()
		{
			DebuggerRuntime.ResetForTests();
		}

		[TearDown]
		public void TearDown()
		{
			DebuggerRuntime.ResetForTests();
		}

#if DEXC_DEBUGGER
		[Test]
		public void PageRequestRejectsUnboundedInputAndListQueryCopiesText()
		{
			Assert.Throws<ArgumentOutOfRangeException>(() => new DebugPageRequest(-1, 1));
			Assert.Throws<ArgumentOutOfRangeException>(() => new DebugPageRequest(0, 0));
			var query = new DebugListQuery("Enemy", "damage", new DebugPageRequest(0, 5));
			Assert.That(query.PathPrefix, Is.EqualTo("Enemy"));
			Assert.That(query.SearchText, Is.EqualTo("damage"));
			Assert.Throws<ArgumentNullException>(() => new DebugListQuery(null, string.Empty, new DebugPageRequest(0, 1)));
		}

		[Test]
		public void HierarchyChildrenQueryRequiresSceneAndAllowsInvalidParentForRoot()
		{
			DebuggerRuntime.Initialize();
			var session = DebuggerRuntime.CurrentSession;
			var scene = DebugSceneHandle.Create(session.SessionId, 1);
			var rootQuery = new DebugHierarchyChildrenQuery(scene, default(DebugObjectHandle), new DebugPageRequest(0, 3));
			var sameQuery = new DebugHierarchyChildrenQuery(scene, default(DebugObjectHandle), new DebugPageRequest(0, 3));
			Assert.That(rootQuery.Parent.IsValid, Is.False);
			Assert.That(rootQuery, Is.EqualTo(sameQuery));
			Assert.Throws<ArgumentException>(() =>
				new DebugHierarchyChildrenQuery(default(DebugSceneHandle), default(DebugObjectHandle), new DebugPageRequest(0, 1))
			);
		}

		[Test]
		public void ValidationIssueRejectsEmptyContractData()
		{
			var issue = new DebugValidationIssue(DebugValidationSeverity.Warning, "item-disabled", "Item is disabled.");
			Assert.That(issue.Code, Is.EqualTo("item-disabled"));
			Assert.Throws<ArgumentException>(() => new DebugValidationIssue(DebugValidationSeverity.Error, "", "message"));
			Assert.Throws<ArgumentException>(() => new DebugValidationIssue(DebugValidationSeverity.Error, "code", ""));
		}

		[Test]
		public void FeatureHandlesAreTypedAndDefaultIsInvalid()
		{
			DebuggerRuntime.Initialize();
			var session = DebuggerRuntime.CurrentSession;
			Assert.That(default(DebugCommandId).IsValid, Is.False);
			var first = DebugCommandId.Create(session.SessionId, 1);
			var same = DebugCommandId.Create(session.SessionId, 1);
			var other = DebugCommandId.Create(session.SessionId, 2);
			Assert.That(first.IsValid, Is.True);
			Assert.That(first, Is.EqualTo(same));
			Assert.That(first, Is.Not.EqualTo(other));
			Assert.That(first.ToString(), Does.StartWith("command:"));
		}

		[Test]
		public void OperationDiagnosticsAreBoundedButCollectionDiagnosticsSurvive()
		{
			DebuggerRuntime.Initialize(options => options.Diagnostic.OperationCapacity = 1);
			var session = DebuggerRuntime.CurrentSession;
			var collection = session.Diagnostics.AddCollection(
				DebugDiagnosticSeverity.Error,
				DebugFeature.Command,
				"collect",
				"bad-definition",
				"Definition failed.",
				"Enemy/Run",
				session.CollectionGeneration
			);
			var operation1 = session.Diagnostics.AddOperation(
				DebugDiagnosticSeverity.Error,
				DebugFeature.Command,
				"execute",
				"operation-1",
				"First operation.",
				"Enemy/Run",
				"exec-1"
			);
			var operation2 = session.Diagnostics.AddOperation(
				DebugDiagnosticSeverity.Error,
				DebugFeature.Command,
				"execute",
				"operation-2",
				"Second operation.",
				"Enemy/Run",
				"exec-2"
			);

			Assert.That(operation1, Is.Not.Null);
			Assert.That(operation2, Is.Not.Null);
			Assert.That(session.Diagnostics.TryGetDetail(collection.DiagnosticId, out _), Is.True);
			Assert.That(session.Diagnostics.TryGetDetail(operation1.DiagnosticId, out _), Is.False);
			Assert.That(session.Diagnostics.TryGetDetail(operation2.DiagnosticId, out _), Is.True);
		}

		[Test]
		public void ZeroOperationCapacityDoesNotRetainOperationDiagnostics()
		{
			DebuggerRuntime.Initialize(options => options.Diagnostic.OperationCapacity = 0);
			var operation = DebuggerRuntime.CurrentSession.Diagnostics.AddOperation(
				DebugDiagnosticSeverity.Warning,
				DebugFeature.Setting,
				"write",
				"ignored",
				"Not retained.",
				"Audio/Volume",
				"exec"
			);
			Assert.That(operation, Is.Null);
		}

		[Test]
		public void DiagnosticSummariesFilterBySourceFeatureSeverityAndPageNewestFirst()
		{
			DebuggerRuntime.Initialize(options => options.Query.MaxPageSize = 2);
			var session = DebuggerRuntime.CurrentSession;
			var collection = session.Diagnostics.AddCollection(
				DebugDiagnosticSeverity.Warning,
				DebugFeature.Command,
				"collect",
				"collection-warning",
				"Collection warning.",
				"Tests/Command",
				session.CollectionGeneration
			);
			var firstOperation = session.Diagnostics.AddOperation(
				DebugDiagnosticSeverity.Error,
				DebugFeature.Setting,
				"write",
				"setting-error",
				"Setting error.",
				"Tests/Setting",
				"setting-exec"
			);
			var secondOperation = session.Diagnostics.AddOperation(
				DebugDiagnosticSeverity.Error,
				DebugFeature.Inspector,
				"read",
				"inspector-error",
				"Inspector error.",
				"Tests/Inspector",
				"inspector-exec"
			);

			var page = DebugDiagnostics.GetSummaries(
				new DebugDiagnosticQuery(DebugDiagnosticSourceFilter.All, DebugFeatureFilter.All, null, new DebugPageRequest(0, 2))
			);
			Assert.That(
				page.Summaries.Select(item => item.DiagnosticId),
				Is.EqualTo(new[] { secondOperation.DiagnosticId, firstOperation.DiagnosticId })
			);
			Assert.That(page.Page.TotalCount, Is.EqualTo(4));
			Assert.That(page.Page.HasMore, Is.True);

			var setting = DebugDiagnostics.GetSummaries(
				new DebugDiagnosticQuery(
					DebugDiagnosticSourceFilter.Operation,
					DebugFeatureFilter.Setting,
					DebugDiagnosticSeverity.Error,
					new DebugPageRequest(0, 1)
				)
			);
			Assert.That(setting.Summaries.Select(item => item.DiagnosticId), Is.EqualTo(new[] { firstOperation.DiagnosticId }));
			var collectionPage = DebugDiagnostics.GetSummaries(
				new DebugDiagnosticQuery(
					DebugDiagnosticSourceFilter.Collection,
					DebugFeatureFilter.Command,
					DebugDiagnosticSeverity.Warning,
					new DebugPageRequest(0, 1)
				)
			);
			Assert.That(collectionPage.Summaries.Select(item => item.DiagnosticId), Is.EqualTo(new[] { collection.DiagnosticId }));
			Assert.Throws<ArgumentOutOfRangeException>(() =>
				DebugDiagnostics.GetSummaries(
					new DebugDiagnosticQuery(DebugDiagnosticSourceFilter.All, DebugFeatureFilter.All, null, new DebugPageRequest(0, 3))
				)
			);
		}

		[Test]
		public void DiagnosticDetailDoesNotExposeExceptionObject()
		{
			DebuggerRuntime.Initialize();
			var exception = new InvalidOperationException("outer", new ArgumentException("inner"));
			var detail = DebuggerRuntime.CurrentSession.Diagnostics.AddCollection(
				DebugDiagnosticSeverity.Error,
				DebugFeature.Inspector,
				"collect",
				"exception",
				"Collection failed.",
				"Scene/Enemy",
				DebuggerRuntime.CurrentSession.CollectionGeneration,
				exception
			);
			Assert.That(detail.ExceptionTypeName, Does.Contain(nameof(InvalidOperationException)));
			Assert.That(detail.InnerExceptions, Has.Count.EqualTo(1));
			Assert.That(detail.InnerExceptions[0].TypeName, Does.Contain(nameof(ArgumentException)));
		}
#else
		[Test]
		public void DisabledAssemblyHasNoSession()
		{
			Assert.That(DebuggerRuntime.IsEnabled, Is.False);
			Assert.That(DebuggerRuntime.IsInitialized, Is.False);
		}
#endif
	}
}
