#if DEXC_DEBUGGER
using System;
using System.Linq;
using System.Reflection;
using Dexc;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DexcDebugger.Tests
{
	public sealed class DexcDisplayFilterTests
	{
		GameObject m_Object;
		DexcPlayerRenderPass m_Pass;

		[SetUp]
		public void SetUp()
		{
			Dexc.DexcDebugger.ResetForTests();
			Dexc.DexcDebugger.Initialize();
			m_Object = new GameObject("Filter fixture");
			m_Object.AddComponent<Camera>();
			m_Pass = new DexcPlayerRenderPass();
		}

		[TearDown]
		public void TearDown()
		{
			m_Pass.ReleaseResources(true);
			Object.DestroyImmediate(m_Object);
			Dexc.DexcDebugger.ResetForTests();
		}

		static DebugSettingId Setting(string path) =>
			DebugSetting.GetList(new DebugListQuery(path, "", new DebugPageRequest(0, 100))).Items.Single(item => item.Path == path).Id;

		static void Set(string path, bool value) =>
			Assert.That(DebugSetting.Write(Setting(path), DebugValue.From(value)).Status, Is.EqualTo(DebugOperationStatus.Succeeded));

		DebugDisplayFrameSnapshot Frame()
		{
			DebugDisplay.DrawLabel("Enemy/Label", "Global");
			DebugDisplay.DrawLine("Enemy/Path", Vector3.zero, Vector3.right, Color.green);
			DebugDisplay.DrawLine("EnemyBoss/Path", Vector3.zero, Vector3.up, Color.red);
			DebugDisplay.CreateSubject("Enemy", m_Object).DrawLabel("Enemy/Label", "Subject");
			Dexc.DexcDebugger.CurrentSession.Display.Tick();
			Assert.That(DebugDisplay.TryGetCompletedFrame(out var frame), Is.True);
			return frame;
		}

		[TestCase("HUD", false)]
		[TestCase("Overlay", true)]
		public void TextLayersAreIndependentAndKeepSnapshot(string layer, bool subject)
		{
			var frame = Frame();
			Set("Display/" + layer, false);
			var filter = DexcDisplayVisibility.Capture();
			Assert.That(filter.IsVisible(frame.Globals.First(), false), Is.EqualTo(subject));
			Assert.That(filter.IsVisible(frame.Subjects.Single().Visuals.Single(), true), Is.EqualTo(!subject));
			DebugMenu.Close();
			Assert.That(DebugDisplay.TryGetCompletedFrame(out var after), Is.True);
			Assert.That(after, Is.SameAs(frame));
			Assert.That(DexcDisplayVisibility.Capture().Layer(layer), Is.False);
			Assert.That(DebugSetting.Reset(Setting("Display/" + layer)).Status, Is.EqualTo(DebugOperationStatus.Succeeded));
			Assert.That(DexcDisplayVisibility.Capture().Layer(layer), Is.True);
		}

		[Test]
		public void PathFiltersUseSegmentsAndWorldOffClearsActualMesh()
		{
			var frame = Frame();
			Set("Display/Enemy", false);
			var filter = DexcDisplayVisibility.Capture();
			Assert.That(
				frame.Globals.Where(visual => filter.IsVisible(visual, false)).Select(visual => visual.Path),
				Is.EqualTo(new[] { "EnemyBoss/Path" })
			);
			var build = typeof(DexcPlayerRenderPass).GetMethod("BuildMesh", BindingFlags.Instance | BindingFlags.NonPublic);
			build.Invoke(m_Pass, new object[] { frame, m_Object.GetComponent<Camera>() });
			var mesh = (Mesh)
				typeof(DexcPlayerRenderPass).GetField("m_Mesh", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(m_Pass);
			Assert.That(mesh.vertexCount, Is.GreaterThan(0));
			Set("Display/World", false);
			build.Invoke(m_Pass, new object[] { frame, m_Object.GetComponent<Camera>() });
			Assert.That(mesh.vertexCount, Is.Zero);
			Assert.That(frame.Globals.Count, Is.EqualTo(3));
		}

		[Test]
		public void NonBooleanAndFailedReadsDoNotHideVisualsAndNewSessionResetsLayers()
		{
			var frame = Frame();
			DexcDisplayFilterSettings.Current.FailRead = true;
			Assert.That(DexcDisplayVisibility.Capture().IsVisible(frame.Globals.First(), false), Is.True);
			DexcDisplayFilterSettings.Current.FailRead = false;
			Set("Display/HUD", false);
			Dexc.DexcDebugger.ResetForTests();
			Dexc.DexcDebugger.Initialize();
			Assert.That(DexcDisplayVisibility.Capture().Layer("HUD"), Is.True);
		}
	}

	public sealed class DexcDisplayFilterSettings : DebugSettingSet<DexcDisplayFilterSettings>
	{
		public bool FailRead;

		[DebugSetting("Display/Enemy")]
		public bool Enemy { get; private set; } = true;

		[DebugSetting("Display/Enemy/Label")]
		public bool Label
		{
			get
			{
				if (FailRead)
					throw new InvalidOperationException("filter getter failed");
				return true;
			}
			private set { }
		}

		[DebugSetting("Display/EnemyBoss")]
		public int NonBoolean { get; private set; }
	}
}
#endif
