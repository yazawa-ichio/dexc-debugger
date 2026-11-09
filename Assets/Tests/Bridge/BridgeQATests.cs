using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dexc;
using Dexc.Bridge;
using Dexc.QA;
using NUnit.Framework;
using UnityEngine;

namespace DexcDebugger.Tests.Bridge
{
	/// <summary>What the bridge puts of Dexc into the reports of Dexc QA.</summary>
	public sealed class BridgeQATests
	{
		string m_Directory;

		[SetUp]
		public void SetUp()
		{
			QARoot.Reset();
			m_Directory = Path.Combine(Path.GetTempPath(), "DexcBridge-" + Guid.NewGuid().ToString("N"));
			// A reset QA has no contributors; a run gets this one from the bridge as it starts.
			DexcQA.AddContributor(new QAInspectorSection());
			DexcQA.Start(new DexcQAOptions { Directory = m_Directory });
		}

		[TearDown]
		public void TearDown()
		{
			QARoot.Reset();
			Dexc.DexcDebugger.ResetForTests();
			if (Directory.Exists(m_Directory))
			{
				Directory.Delete(m_Directory, true);
			}
		}

		string Report(string id)
		{
			return File.ReadAllText(Path.Combine(m_Directory, id, "report.json"));
		}

		[Test]
		public void TheFieldsWatchedInTheInspectorGoInWithTheirValues()
		{
			Dexc.DexcDebugger.ResetForTests();
			Dexc.DexcDebugger.Initialize();
			var host = new GameObject("Shelf");
			try
			{
				// A component of the runtime tests: one of this Editor-only assembly could not be added to an object.
				var shelf = host.AddComponent<DexcCollectionFixture>();
				shelf.Values = new List<int> { 5, 6 };
				DebugInspector.Register("Tests/Shelf", shelf, "Shelf");
				var target = DebugInspector.GetRegistered(new DebugListQuery("Tests/Shelf", "", new DebugPageRequest(0, 20))).Entries.Single().Target;
				var component = DebugInspector.GetComponents(target).Components.Single(item => item.TypeName == typeof(DexcCollectionFixture).FullName).Component;
				var field = component.GetFields(new DebugPageRequest(0, 20)).Fields.Single(item => item.Name == "Values");
				Assert.That(component.AddWatch(field.Id, TimeSpan.Zero).Status, Is.EqualTo(DebugOperationStatus.Succeeded));

				var report = Report(DexcQA.Report("check", "shelf", "The shelf is wrong"));

				Assert.That(report, Does.Contain("\"target\": \"Shelf (Tests/Shelf)\""));
				Assert.That(report, Does.Contain("\"field\": \"" + field.DisplayName + "\""));
				Assert.That(report, Does.Contain("\"count\": 2"), "the value as the Inspector reads it");
				Assert.That(report, Does.Contain("\"diagnostics\": ["));
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(host);
			}
		}

		[Test]
		public void WithoutDexcRunningTheSectionsAreNull()
		{
			Dexc.DexcDebugger.ResetForTests();

			var report = Report(DexcQA.Report("check", "x", "x"));

			Assert.That(report, Does.Contain("\"inspector\": null"));
			Assert.That(report, Does.Contain("\"diagnostics\": null"));
		}
	}
}
