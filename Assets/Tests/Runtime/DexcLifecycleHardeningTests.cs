using System;
using System.Collections;
using Dexc;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using DebuggerRuntime = Dexc.DexcDebugger;

namespace DexcDebugger.Tests
{
#if DEXC_DEBUGGER
	[TestFixture]
	public sealed class DexcLifecycleHardeningTests
	{
		private GameObject m_Owner;

		[SetUp]
		public void SetUp()
		{
			DebuggerRuntime.ResetForTests();
			DebuggerRuntime.Initialize();
			m_Owner = new GameObject("DexcLifecycleHardeningOwner");
		}

		[TearDown]
		public void TearDown()
		{
			if (m_Owner != null)
				UnityEngine.Object.DestroyImmediate(m_Owner);
			DebuggerRuntime.ResetForTests();
		}

		[Test]
		public void SubsystemResetDropsCompletedDisplayFrameAndInvalidatesOldSubject()
		{
			var subject = DebugDisplay.CreateSubject("Lifecycle", m_Owner);
			subject.DrawLine(DebugDrawOptions.Frame("Lifecycle/Line", Color.white), Vector3.zero, Vector3.right);
			DebuggerRuntime.CurrentSession.Display.Tick();
			Assert.That(DebugDisplay.TryGetCompletedFrame(out var oldFrame), Is.True);

			DebuggerRuntime.ResetForTests();
			DebuggerRuntime.Initialize();

			Assert.That(oldFrame, Is.Not.Null);
			Assert.That(DebugDisplay.TryGetCompletedFrame(out _), Is.False);
			Assert.Throws<InvalidOperationException>(() => subject.SetLabel("stale"));
			Assert.That(DebuggerRuntime.IsInitialized, Is.True);
		}

		[UnityTest]
		public IEnumerator SceneUnloadDropsSubjectsOwnedByTheUnloadedScene()
		{
			var fixtureScene = SceneManager.CreateScene("DexcLifecycleScene_" + Guid.NewGuid().ToString("N"));
			var sceneOwner = new GameObject("DexcLifecycleSceneOwner");
			SceneManager.MoveGameObjectToScene(sceneOwner, fixtureScene);
			var subject = DebugDisplay.CreateSubject("Lifecycle/Scene", sceneOwner);
			subject.DrawLine(DebugDrawOptions.Frame("Lifecycle/Scene/Line", Color.white), Vector3.zero, Vector3.right);
			DebuggerRuntime.CurrentSession.Display.Tick();
			Assert.That(DebugDisplay.TryGetCompletedFrame(out _), Is.True);

			var unload = SceneManager.UnloadSceneAsync(fixtureScene);
			while (unload != null && !unload.isDone)
				yield return null;
			DebuggerRuntime.CurrentSession.Display.Tick();

			Assert.That(DebugDisplay.TryGetCompletedFrame(out var frame), Is.True);
			Assert.That(frame.Subjects, Is.Empty);
			Assert.Throws<InvalidOperationException>(() => subject.SetLabel("stale"));
		}
	}
#endif
}
