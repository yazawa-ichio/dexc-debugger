#if DEXC_DEBUGGER
using System.Linq;
using Dexc;
using NUnit.Framework;
using UnityEngine;

namespace DexcDebugger.Tests
{
	/// <summary>
	/// The Boot window is what makes start configurations usable before the game runs: pick a set, edit it, start it.
	/// Its selection belongs to the window, so two windows never share values.
	/// </summary>
	public sealed class DexcBootWindowTests
	{
		private DexcBootWindow m_Window;
		private DebugMemoryStorage m_Storage;

		[SetUp]
		public void SetUp()
		{
			m_Storage = new DebugMemoryStorage();
			DebugStorage.Storage = m_Storage;
			m_Window = ScriptableObject.CreateInstance<DexcBootWindow>();
		}

		[TearDown]
		public void TearDown()
		{
			if (m_Window != null)
				Object.DestroyImmediate(m_Window);
			DebugStorage.ResetForTests();
		}

		[Test]
		public void SetsAreListedWithoutASession()
		{
			Assert.That(Dexc.DexcDebugger.IsInitialized, Is.False, "the fixture runs with no session");

			var sets = DexcDefinitions.Boot.Sets;

			Assert.That(sets, Is.Not.Empty, "sets are readable from the declarations alone");
			Assert.That(
				sets.All(set => !string.IsNullOrEmpty(set.Attribute.Tag) && !string.IsNullOrEmpty(set.Attribute.Scene)),
				Is.True,
				"every set names itself and its destination"
			);
		}

		[Test]
		public void SelectingASetOpensItsValuesAndResolvesItsScene()
		{
			m_Window.SelectForTests("root-state");

			Assert.That(m_Window.ErrorForTests, Is.Null);
			Assert.That(m_Window.SelectionForTests, Is.Not.Null);
			Assert.That(m_Window.SelectionForTests.ResolveScene(), Is.EqualTo("RootStateFixtureScene"));
			Assert.That(m_Window.SelectionForTests.Panels, Is.Not.Empty);
		}

		[Test]
		public void AnUnknownSetReportsTheProblemInsteadOfThrowing()
		{
			m_Window.SelectForTests("no-such-set");

			Assert.That(m_Window.SelectionForTests, Is.Null);
			Assert.That(m_Window.ErrorForTests, Does.Contain("no-such-set"));
		}

		[Test]
		public void TwoWindowsEditTheirOwnValues()
		{
			var other = ScriptableObject.CreateInstance<DexcBootWindow>();
			try
			{
				m_Window.SelectForTests("root-state");
				other.SelectForTests("root-state");

				Assert.That(other.SelectionForTests, Is.Not.SameAs(m_Window.SelectionForTests), "each window opens its own");

				((DebugToggleWidget)m_Window.SelectionForTests.Panels.Single().Widgets.Single()).Toggle();

				Assert.That(
					((DebugToggleWidget)other.SelectionForTests.Panels.Single().Widgets.Single()).Value,
					Is.False,
					"the other window is untouched"
				);
			}
			finally
			{
				Object.DestroyImmediate(other);
			}
		}
	}
}
#endif
