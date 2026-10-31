#if DEXC_DEBUGGER
using System.Linq;
using Dexc;
using NUnit.Framework;
using UnityEngine;

namespace DexcDebugger.Tests
{
	public enum BootFixtureMode
	{
		Fast,
		Normal,
		Slow,
	}

	[DebugBootGroup("Fixture Block", Order = 6)]
	public sealed class BootFixtureBlock : DebugBootBlock
	{
		[DebugBootItem("Host", Order = 0)]
		public string Host { get; private set; } = "localhost";
	}

	[DebugBootMenu("Boot Fixture", Order = 5, Tag = "fixture", Scene = "BootFixtureScene", Groups = new[] { typeof(BootFixtureBlock) })]
	public sealed class BootFixtureSet : DebugBootSet
	{
		public static int ChangedCount;
		public static string LastChanged;

		[DebugBootItem("Mode", Order = 0)]
		public BootFixtureMode Mode { get; private set; } = BootFixtureMode.Normal;

		[DebugBootItem("Enabled", Order = 1)]
		public bool Enabled { get; private set; } = true;

		[DebugBootItem("Count", Order = 2)]
		[DebugRange(0, 10)]
		public int Count { get; private set; } = 3;

		[DebugBootItem("Ratio", Order = 3, Step = 0.5)]
		[DebugRange(0, 2)]
		public float Ratio { get; private set; } = 1f;

		[DebugBootItem("Name", Order = 4, Persist = false)]
		public string Name { get; private set; } = "fixture";

		public static string StartedHost;
		public static string LoadedScene;

		protected override void OnStarting(DebugBootGroupsValues values)
		{
			StartedHost = values.Get<BootFixtureBlock>().Host;
		}

		protected override void OnChanged(string propertyName)
		{
			ChangedCount++;
			LastChanged = propertyName;
		}
	}

	[DebugPersistent]
	public sealed class PersistentFixtureSettings : DebugSettingSet<PersistentFixtureSettings>
	{
		[DebugSetting("Tests/Persistent/Speed")]
		[DebugRange(0, 100)]
		public float Speed { get; private set; } = 10f;

		[DebugSetting("Tests/Persistent/Label")]
		public string Label { get; private set; } = "default";

		[DebugSetting("Tests/Persistent/Offset")]
		public Vector3 Offset { get; private set; } = Vector3.one;
	}

	public sealed class PartialPersistentSettings : DebugSettingSet<PartialPersistentSettings>
	{
		[DebugSetting("Tests/Partial/Kept", Persist = true)]
		public int Kept { get; private set; } = 1;

		[DebugSetting("Tests/Partial/Volatile")]
		public int Volatile { get; private set; } = 1;
	}

	public sealed class DexcBootTests
	{
		DebugMemoryStorage m_Storage;

		[SetUp]
		public void SetUp()
		{
			m_Storage = new DebugMemoryStorage();
			DebugStorage.Storage = m_Storage;
			BootFixtureSet.ChangedCount = 0;
			BootFixtureSet.StartedHost = null;
			BootFixtureSet.LoadedScene = null;
		}

		[TearDown]
		public void TearDown()
		{
			DebugBootMenu.Close();
			DebugStorage.ResetForTests();
			Dexc.DexcDebugger.ResetForTests();
		}

		static DebugBootSelection Open() => DebugBootSelection.Open("fixture");

		static void OpenMenu() => DebugBootMenu.Open("fixture", scene => BootFixtureSet.LoadedScene = scene);

		[Test]
		public void RemoteBootReadListsEachItemWithItsValueAndWhatItTakes()
		{
			var operations = new DexcRemoteOperations();
			Assert.That(operations.BootRead()["status"], Is.EqualTo("not-found"), "no boot menu is open yet");

			OpenMenu();
			var read = operations.BootRead();
			Assert.That(read["status"], Is.EqualTo("succeeded"), "a boot menu needs no session");
			Assert.That(read["tag"], Is.EqualTo("fixture"));
			var groups = Members(read["groups"]);
			Assert.That(groups.Select(group => group["title"]), Is.EqualTo(new[] { "Boot Fixture", "Fixture Block" }));
			var items = Members(groups[0]["items"]);
			var mode = items.Single(item => (string)item["label"] == "Mode");
			Assert.That(mode["type"], Is.EqualTo("choice"));
			Assert.That(mode["value"], Is.EqualTo("Normal"));
			Assert.That(mode["options"], Is.EqualTo(new object[] { "Fast", "Normal", "Slow" }));
			var count = items.Single(item => (string)item["label"] == "Count");
			Assert.That(count["type"], Is.EqualTo("integer"));
			Assert.That(count["value"], Is.EqualTo(3L));
			Assert.That(count["maximum"], Is.EqualTo(10d));
		}

		[Test]
		public void RemoteBootStartSetsTheItemsThenStarts()
		{
			OpenMenu();
			var answer = new DexcRemoteOperations().BootStart(
				new System.Collections.Generic.Dictionary<string, object>
				{
					["Boot Fixture/Mode"] = "Fast",
					["Boot Fixture/Count"] = 5L,
					["Fixture Block/Host"] = "staging",
				}
			);

			Assert.That(answer["status"], Is.EqualTo("succeeded"));
			Assert.That(DebugBootMenu.IsOpen, Is.False);
			Assert.That(BootFixtureSet.LoadedScene, Is.EqualTo("BootFixtureScene"));
			Assert.That(BootFixtureSet.StartedHost, Is.EqualTo("staging"));
			var key = DexcBootValue.KeyFor(typeof(BootFixtureSet), typeof(BootFixtureSet).GetProperty("Mode"));
			Assert.That(m_Storage.TryLoad(key, out var mode), Is.True);
			Assert.That(mode, Is.EqualTo("Fast"), "a value set from outside is saved like one set in the menu");
		}

		static System.Collections.Generic.List<System.Collections.Generic.IDictionary<string, object>> Members(object list)
		{
			return ((System.Collections.Generic.List<object>)list).Cast<System.Collections.Generic.IDictionary<string, object>>().ToList();
		}

		[Test]
		public void RemoteBootStartWithABadValueChangesNothingAndKeepsWaiting()
		{
			OpenMenu();
			var operations = new DexcRemoteOperations();
			var answer = operations.BootStart(
				new System.Collections.Generic.Dictionary<string, object>
				{
					["Boot Fixture/Mode"] = "Fast",
					["Boot Fixture/Count"] = 50L,
				}
			);
			Assert.That(answer["status"], Is.EqualTo("rejected"));
			Assert.That(DebugBootMenu.IsOpen, Is.True);
			var mode = DebugBootMenu.Panels[0].Widgets.OfType<DebugChoiceWidget>().Single();
			Assert.That(mode.Selected, Is.EqualTo("Normal"), "the good value before the bad one was not written");

			answer = operations.BootStart(new System.Collections.Generic.Dictionary<string, object> { ["Boot Fixture/Speed"] = 1L });
			Assert.That(answer["status"], Is.EqualTo("not-found"));
			Assert.That(DebugBootMenu.IsOpen, Is.True);
		}

		[Test]
		public void TheSetAndItsBlocksAreListedInOrder()
		{
			var selection = Open();
			CollectionAssert.AreEqual(new[] { "Boot Fixture", "Fixture Block" }, selection.Panels.Select(panel => panel.Title).ToArray());
			var group = selection.Panels[0];
			CollectionAssert.AreEqual(
				new[] { "Mode", "Enabled", "Count", "Ratio", "Name" },
				group.Widgets.Select(widget => widget.Label).ToArray()
			);
			Assert.IsInstanceOf<DebugChoiceWidget>(group.Widgets[0]);
			Assert.IsInstanceOf<DebugToggleWidget>(group.Widgets[1]);
			Assert.IsInstanceOf<DebugNumberWidget>(group.Widgets[2]);
			Assert.IsInstanceOf<DebugNumberWidget>(group.Widgets[3]);
			Assert.IsInstanceOf<DebugTextWidget>(group.Widgets[4]);
			Assert.IsInstanceOf<DebugTextWidget>(selection.Panels[1].Widgets.Single());
		}

		[Test]
		public void AnUnknownTagStops()
		{
			Assert.Throws<System.InvalidOperationException>(() => DebugBootSelection.Open("no-such-set"));
			Assert.Throws<System.ArgumentException>(() => DebugBootSelection.Open(string.Empty));
		}

		[Test]
		public void StartHandsTheValuesOverAndGoesToTheDeclaredScene()
		{
			var selection = Open();
			((DebugTextWidget)selection.Panels[1].Widgets.Single()).Value = "example.test";
			string loaded = null;

			Assert.IsTrue(selection.Start(scene => loaded = scene));

			Assert.AreEqual("BootFixtureScene", loaded);
			Assert.AreEqual("example.test", BootFixtureSet.StartedHost, "the set reads its blocks through the hook argument");
		}

		[Test]
		public void WidgetsWriteThroughAndPersist()
		{
			var group = Open().Panels[0];
			var mode = (DebugChoiceWidget)group.Widgets[0];
			mode.Move(1);
			Assert.AreEqual(BootFixtureMode.Slow.ToString(), mode.Selected);
			mode.Move(1);
			Assert.AreEqual(BootFixtureMode.Fast.ToString(), mode.Selected, "choice wraps");
			var enabled = (DebugToggleWidget)group.Widgets[1];
			enabled.Toggle();
			Assert.IsFalse(enabled.Value);
			var count = (DebugNumberWidget)group.Widgets[2];
			count.Move(20);
			Assert.AreEqual(10, count.Value, "clamped to range");
			var ratio = (DebugNumberWidget)group.Widgets[3];
			ratio.Move(-1);
			Assert.AreEqual(0.5, ratio.Value, 0.0001);
			((DebugTextWidget)group.Widgets[4]).Value = "renamed";
			Assert.AreEqual(6, BootFixtureSet.ChangedCount);
			Assert.AreEqual("Name", BootFixtureSet.LastChanged);
			Assert.IsTrue(m_Storage.TryLoad("Boot/DexcDebugger.Tests.BootFixtureSet/Mode", out var stored));
			Assert.AreEqual("Fast", stored);
			Assert.IsFalse(m_Storage.TryLoad("Boot/DexcDebugger.Tests.BootFixtureSet/Name", out _), "Persist = false");
		}

		[Test]
		public void PersistedValuesComeBackWhenTheSetIsOpenedAgain()
		{
			var first = Open().Panels[0];
			((DebugChoiceWidget)first.Widgets[0]).Index = 2;
			((DebugNumberWidget)first.Widgets[2]).Value = 7;

			var reopened = Open();
			var group = reopened.Panels[0];
			Assert.AreEqual(BootFixtureMode.Slow.ToString(), ((DebugChoiceWidget)group.Widgets[0]).Selected);
			Assert.AreEqual(7, ((DebugNumberWidget)group.Widgets[2]).Value);

			reopened.Reset();
			Assert.AreEqual(BootFixtureMode.Normal.ToString(), ((DebugChoiceWidget)group.Widgets[0]).Selected);
			Assert.AreEqual(3, ((DebugNumberWidget)group.Widgets[2]).Value);
			Assert.AreEqual(0, m_Storage.Count);
		}

		[Test]
		public void EachSelectionKeepsItsOwnValues()
		{
			var first = Open();
			((DebugTextWidget)first.Panels[0].Widgets[4]).Value = "first";
			var second = Open();

			Assert.AreEqual("fixture", ((DebugTextWidget)second.Panels[0].Widgets[4]).Value, "Persist = false starts declared");
			Assert.AreEqual("first", ((DebugTextWidget)first.Panels[0].Widgets[4]).Value, "the other selection is untouched");
		}

		[Test]
		public void MenuStateNavigatesAndStarts()
		{
			OpenMenu();
			Assert.IsTrue(DebugBootMenu.IsOpen);
			Assert.AreEqual(6, DexcBootMenuState.Rows.Count, "five set items plus the block item");
			var mode = (DebugChoiceWidget)DexcBootMenuState.Rows[0].Widget;
			DexcBootMenuState.Focus = 0;
			DexcBootMenuState.Adjust(1);
			Assert.AreEqual(BootFixtureMode.Slow.ToString(), mode.Selected);
			DexcBootMenuState.Move(1);
			DexcBootMenuState.Activate();
			Assert.IsFalse(((DebugToggleWidget)DexcBootMenuState.Rows[1].Widget).Value);
			DexcBootMenuState.Focus = DexcBootMenuState.FocusableCount - 1;
			Assert.AreEqual(DexcBootMenuState.FooterStart, DexcBootMenuState.FocusedFooter);
			DexcBootMenuState.Activate();
			Assert.IsFalse(DebugBootMenu.IsOpen);
			Assert.AreEqual("BootFixtureScene", BootFixtureSet.LoadedScene, "starting goes where the set declared");
		}

		[Test]
		public void PersistedValueRoundTrips()
		{
			var values = new[]
			{
				DebugValue.From(true),
				DebugValue.From(-42L),
				DebugValue.From(42UL),
				DebugValue.From(1.25),
				DebugValue.From("a:b;c{d}"),
				DebugValue.From(string.Empty),
				DebugValue.FromObject(new DebugValueMember("x", DebugValue.From(1f)), new DebugValueMember("y", DebugValue.From(-2f))),
				DebugValue.FromArray(DebugValue.From(1), DebugValue.From("two")),
			};
			foreach (var value in values)
			{
				Assert.IsTrue(
					DexcPersistedValue.TryDeserialize(DexcPersistedValue.Serialize(value), out var restored),
					value.Kind.ToString()
				);
				Assert.AreEqual(value, restored, value.Kind.ToString());
			}
		}

		[Test]
		public void PersistentSettingsSaveAndRestore()
		{
			Dexc.DexcDebugger.Initialize();
			var speed = FindSetting("Tests/Persistent/Speed");
			var label = FindSetting("Tests/Persistent/Label");
			var offset = FindSetting("Tests/Persistent/Offset");
			var kept = FindSetting("Tests/Partial/Kept");
			var volatileItem = FindSetting("Tests/Partial/Volatile");
			Assert.AreEqual(DebugOperationStatus.Succeeded, DebugSetting.Write(speed.Id, DebugValue.From(55f)).Status);
			Assert.AreEqual(DebugOperationStatus.Succeeded, DebugSetting.Write(label.Id, DebugValue.From("saved")).Status);
			Assert.AreEqual(
				DebugOperationStatus.Succeeded,
				DebugSetting
					.Write(
						offset.Id,
						DebugValue.FromObject(
							new DebugValueMember("x", DebugValue.From(4f)),
							new DebugValueMember("y", DebugValue.From(5f)),
							new DebugValueMember("z", DebugValue.From(6f))
						)
					)
					.Status
			);
			Assert.AreEqual(DebugOperationStatus.Succeeded, DebugSetting.Write(kept.Id, DebugValue.From(9)).Status);
			Assert.AreEqual(DebugOperationStatus.Succeeded, DebugSetting.Write(volatileItem.Id, DebugValue.From(9)).Status);
			Assert.AreEqual(DebugOperationStatus.Rejected, DebugSetting.Write(speed.Id, DebugValue.From(500f)).Status);
			Assert.IsTrue(m_Storage.TryLoad("Setting/Tests/Persistent/Speed", out _));
			Assert.IsFalse(m_Storage.TryLoad("Setting/Tests/Partial/Volatile", out _));

			Dexc.DexcDebugger.Initialize();
			Assert.AreEqual(55f, PersistentFixtureSettings.Current.Speed);
			Assert.AreEqual("saved", PersistentFixtureSettings.Current.Label);
			Assert.AreEqual(new Vector3(4f, 5f, 6f), PersistentFixtureSettings.Current.Offset);
			Assert.AreEqual(9, PartialPersistentSettings.Current.Kept);
			Assert.AreEqual(1, PartialPersistentSettings.Current.Volatile);
			var restoredSpeed = FindSetting("Tests/Persistent/Speed");
			Assert.IsTrue(DebugSetting.Read(restoredSpeed.Id).TryGetValue(out var read));
			Assert.IsTrue(read.TryGetFloatingPoint(out var number));
			Assert.AreEqual(55d, number, 0.0001d);

			Assert.IsTrue(DebugSetting.TryRead<float>(restoredSpeed.Id, out var typedSpeed));
			Assert.AreEqual(55f, typedSpeed);
			Assert.AreEqual(DebugOperationStatus.Succeeded, DebugSetting.Write(restoredSpeed.Id, 42f).Status);
			Assert.AreEqual(42f, PersistentFixtureSettings.Current.Speed);
			Assert.IsFalse(DebugSetting.TryRead<int>(FindSetting("Tests/Persistent/Label").Id, out _), "type mismatch is not converted");

			m_Storage.Save("Setting/Tests/Persistent/Speed", DexcPersistedValue.Serialize(DebugValue.From(999f)));
			Dexc.DexcDebugger.Initialize();
			Assert.AreEqual(10f, PersistentFixtureSettings.Current.Speed, "out-of-range persisted value is ignored");
		}

		/// <summary>
		/// The file store keeps writes in memory until it is flushed. A setting written from the menu has to reach the file
		/// by itself: nothing else in a session flushes it, and the next run reads the file, not this instance.
		/// </summary>
		[Test]
		public void APersistedSettingReachesTheFileWithoutAnExplicitFlush()
		{
			var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dexc-persist-" + System.Guid.NewGuid().ToString("N") + ".txt");
			try
			{
				DebugStorage.Storage = new DebugFileStorage(path);
				Dexc.DexcDebugger.Initialize();
				var speed = FindSetting("Tests/Persistent/Speed");

				Assert.AreEqual(DebugOperationStatus.Succeeded, DebugSetting.Write(speed.Id, DebugValue.From(33f)).Status);

				Assert.IsTrue(System.IO.File.Exists(path), "the write reached the disk");
				var nextRun = new DebugFileStorage(path);
				Assert.IsTrue(nextRun.TryLoad("Setting/Tests/Persistent/Speed", out var saved), "a fresh store reads the value back");
				Assert.AreEqual(DexcPersistedValue.Serialize(DebugValue.From(33f)), saved);
			}
			finally
			{
				if (System.IO.File.Exists(path))
					System.IO.File.Delete(path);
			}
		}

		/// <summary>NaN passes through a clamp, and an infinity cannot be shown: a number field takes neither.</summary>
		[Test]
		public void ANumberFieldTakesNoNaNOrInfinity()
		{
			var stored = 1d;
			var number = new DebugNumberWidget("Ratio", () => stored, value => stored = value, 0d, 2d);
			foreach (var text in new[] { "NaN", "Infinity", "-Infinity", "1e400" })
				Assert.That(number.TrySetText(text), Is.False, text);
			number.Value = double.NaN;
			Assert.That(stored, Is.EqualTo(1d));
			Assert.That(number.TrySetText("1.5"), Is.True);
			Assert.That(stored, Is.EqualTo(1.5d));
		}

		/// <summary>A file that cannot be read for a moment is read again later, not written over with only the new keys.</summary>
		[Test]
		public void AFailedReadDoesNotLetTheNextFlushDropTheOtherValues()
		{
			var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dexc-persist-" + System.Guid.NewGuid().ToString("N") + ".txt");
			try
			{
				System.IO.File.WriteAllText(path, "kept\tvalue\n");
				var storage = new DebugFileStorage(path);
				using (new System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.None))
				{
					Assert.Throws<System.IO.IOException>(() => storage.TryLoad("kept", out _));
					// A value saved meanwhile is kept until the file can be read and written again.
					storage.Save("added", "new");
					Assert.IsTrue(storage.TryLoad("added", out var held));
					Assert.AreEqual("new", held);
					Assert.Throws<System.IO.IOException>(storage.Flush);
				}

				storage.Flush();

				var nextRun = new DebugFileStorage(path);
				Assert.IsTrue(nextRun.TryLoad("kept", out var kept));
				Assert.AreEqual("value", kept);
				Assert.IsTrue(nextRun.TryLoad("added", out var added));
				Assert.AreEqual("new", added);
			}
			finally
			{
				if (System.IO.File.Exists(path))
					System.IO.File.Delete(path);
			}
		}

		static DebugSettingListItem FindSetting(string path)
		{
			var page = DebugSetting.GetList(new DebugListQuery(string.Empty, string.Empty, new DebugPageRequest(0, 100)));
			var item = page.Items.FirstOrDefault(candidate => candidate.Path == path);
			Assert.IsNotNull(item, path);
			return item;
		}
	}
}
#endif
