#if DEXC_DEBUGGER
using System.Linq;
using Dexc;
using NUnit.Framework;
using UnityEngine;

namespace DexcDebugger.Tests
{
	[DebugBootGroup("Start Fixture Server", Order = 0)]
	public sealed class BootStartServerBlock : DebugBootBlock
	{
		[DebugBootItem("Host", Order = 0)]
		public string Host { get; private set; } = "localhost";
	}

	[DebugBootGroup("Start Fixture Stage", Order = 1)]
	public sealed class BootStartBattleBlock : DebugBootBlock
	{
		[DebugBootItem("Stage", Order = 0)]
		[DebugRange(1, 9)]
		public int Stage { get; private set; } = 1;
	}

	/// <summary>The everyday entry: a server and nothing else.</summary>
	[DebugBootMenu(
		"Start Fixture Normal",
		Order = 80,
		Tag = "start-normal",
		Scene = "StartFixtureScene",
		Groups = new[] { typeof(BootStartServerBlock) }
	)]
	public sealed class BootStartNormalSet : DebugBootSet
	{
		public static string Host;
		public static int Stage;
		public static string Loaded;
		public static bool DebuggerStarted;

		protected override void OnStarting(DebugBootGroupsValues values)
		{
			Host = values.Get<BootStartServerBlock>().Host;
			Stage = 1;
		}

		protected override void OnLoadSceneAfter(DebugBootGroupsValues values)
		{
			DebuggerStarted = true;
		}
	}

	/// <summary>The battle check: the same server block plus what to fight.</summary>
	[DebugBootMenu(
		"Start Fixture Battle",
		Order = 81,
		Tag = "start-battle",
		Scene = "StartFixtureScene",
		Groups = new[] { typeof(BootStartServerBlock), typeof(BootStartBattleBlock) }
	)]
	public sealed class BootStartBattleSet : DebugBootSet
	{
		protected override void OnStarting(DebugBootGroupsValues values)
		{
			BootStartNormalSet.Host = values.Get<BootStartServerBlock>().Host;
			BootStartNormalSet.Stage = values.Get<BootStartBattleBlock>().Stage;
		}
	}

	/// <summary>
	/// Starting is what a boot set is for, and it happens in two places: the boot scene while the game runs, and the Boot
	/// window while it does not. Both have to hand the values over in the same order.
	/// </summary>
	public sealed class DexcBootStartTests
	{
		private DebugMemoryStorage m_Storage;

		[SetUp]
		public void SetUp()
		{
			m_Storage = new DebugMemoryStorage();
			DebugStorage.Storage = m_Storage;
			BootStartNormalSet.Host = null;
			BootStartNormalSet.Stage = 0;
			BootStartNormalSet.Loaded = null;
			BootStartNormalSet.DebuggerStarted = false;
		}

		[TearDown]
		public void TearDown()
		{
			DebugStorage.ResetForTests();
		}

		[Test]
		public void TwoEntriesShareTheServerBlockAndItsStoredValue()
		{
			var normal = DebugBootSelection.Open("start-normal");
			((DebugTextWidget)normal.Panels.Single(panel => panel.Title == "Start Fixture Server").Widgets.Single()).Value =
				"shared.example";

			var battle = DebugBootSelection.Open("start-battle");

			Assert.That(
				((DebugTextWidget)battle.Panels.Single(panel => panel.Title == "Start Fixture Server").Widgets.Single()).Value,
				Is.EqualTo("shared.example"),
				"the block is stored under the block, so both entries see the same server"
			);
			Assert.That(
				battle.Panels.Select(panel => panel.Title).ToArray(),
				Is.EqualTo(new[] { "Start Fixture Battle", "Start Fixture Server", "Start Fixture Stage" }),
				"the battle entry adds its own block on top of the shared one"
			);
			Assert.That(
				normal.Panels.Select(panel => panel.Title).ToArray(),
				Is.EqualTo(new[] { "Start Fixture Normal", "Start Fixture Server" }),
				"the normal entry lists only the shared block"
			);
		}

		[Test]
		public void TheBattleEntryHandsOverBothBlocks()
		{
			var battle = DebugBootSelection.Open("start-battle");
			((DebugTextWidget)battle.Panels.Single(panel => panel.Title == "Start Fixture Server").Widgets.Single()).Value =
				"battle.example";
			((DebugNumberWidget)battle.Panels.Single(panel => panel.Title == "Start Fixture Stage").Widgets.Single()).Value = 7;
			string loaded = null;

			Assert.That(battle.Start(scene => loaded = scene), Is.True);

			Assert.That(loaded, Is.EqualTo("StartFixtureScene"));
			Assert.That(BootStartNormalSet.Host, Is.EqualTo("battle.example"));
			Assert.That(BootStartNormalSet.Stage, Is.EqualTo(7));
		}

		/// <summary>
		/// The hooks run around the load: OnStarting before it is asked for, OnLoadSceneAfter once it has been.
		/// </summary>
		[Test]
		public void BothHooksRunAroundTheLoadAndInThatOrder()
		{
			var order = new System.Collections.Generic.List<string>();
			var normal = DebugBootSelection.Open("start-normal");

			normal.Start(scene => order.Add("load"));

			Assert.That(BootStartNormalSet.Host, Is.Not.Null, "OnStarting ran");
			Assert.That(BootStartNormalSet.DebuggerStarted, Is.True, "OnLoadSceneAfter ran");
			Assert.That(order, Is.EqualTo(new[] { "load" }));
		}

		[Test]
		public void APersistedValueSurvivesTheStartAndIsThereForTheNextOne()
		{
			var first = DebugBootSelection.Open("start-normal");
			((DebugTextWidget)first.Panels.Single(panel => panel.Title == "Start Fixture Server").Widgets.Single()).Value =
				"persisted.example";
			first.Start(_ => { });

			// What the Editor writes reaches the run through storage, which is what survives entering Play Mode.
			var afterReload = DebugBootSelection.Open("start-normal");

			Assert.That(
				((DebugTextWidget)afterReload.Panels.Single(panel => panel.Title == "Start Fixture Server").Widgets.Single()).Value,
				Is.EqualTo("persisted.example")
			);
		}
	}
}
#endif
