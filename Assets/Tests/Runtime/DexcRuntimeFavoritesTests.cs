#if DEXC_DEBUGGER
using System.Linq;
using Dexc;
using NUnit.Framework;

namespace DexcDebugger.Tests
{
	public sealed class DexcRuntimeFavoritesTests
	{
		DebugMemoryStorage m_Storage;

		[SetUp]
		public void SetUp()
		{
			DexcRuntimeUIState.ResetForSession();
			Dexc.DexcDebugger.ResetForTests();
			m_Storage = new DebugMemoryStorage();
			DebugStorage.Storage = m_Storage;
			// The starred paths are read once per Play Mode; each test starts from its own empty storage instead.
			DexcUIRoot.Current.Favorites.Commands = null;
			DexcUIRoot.Current.Favorites.Settings = null;
			Dexc.DexcDebugger.Initialize();
			DexcRuntimeUIState.SetSession(Dexc.DexcDebugger.CurrentSession.SessionId);
			DebugMenu.Open();
		}

		[TearDown]
		public void TearDown()
		{
			DebugMenu.Close();
			DexcRuntimeUIState.ResetForSession();
			Dexc.DexcDebugger.ResetForTests();
		}

		static DexcUIRow Entry(string label) =>
			DexcRuntimeCommandForm.ListRows().Single(row => row.ListItem && row.Label == label);

		[Test]
		public void CommandListOpensFoldersFromPathsAndBackGoesUpOneFolderAtATime()
		{
			DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.CommandList);
			var top = DexcRuntimeCommandForm.ListRows().Where(row => row.ListItem).ToList();
			Assert.That(top[0].Label, Is.EqualTo("Favorites"), "the favorites come first at the top");
			Assert.That(top.Select(row => row.Label), Has.Member("Tests"));
			Assert.That(Entry("Tests").Drill, Is.True);
			Assert.That(int.Parse(Entry("Tests").TrailingText), Is.GreaterThan(1), "a folder shows how many commands it holds");

			Entry("Tests").Activated();
			Entry("UIR03").Activated();
			Assert.That(DexcRuntimeUIRenderer.Breadcrumb(DexcRuntimeUIPage.CommandList), Is.EqualTo(new[] { "Command", "Tests", "UIR03" }));
			Assert.That(Entry("Form").Drill, Is.False, "a command opens its page, not a folder");

			DexcRuntimeUIState.Back();
			Assert.That(DexcRuntimeUIState.Page, Is.EqualTo(DexcRuntimeUIPage.CommandList));
			Assert.That(DexcRuntimeUIRenderer.Breadcrumb(DexcRuntimeUIPage.CommandList), Is.EqualTo(new[] { "Command", "Tests" }));
			DexcRuntimeUIState.Back();
			DexcRuntimeUIState.Back();
			Assert.That(DexcRuntimeUIState.Page, Is.EqualTo(DexcRuntimeUIPage.Home), "at the top Back leaves the list");

			DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.CommandList);
			DexcRuntimeUIState.Search = "UIR03/Form";
			var found = DexcRuntimeCommandForm.ListRows().Where(row => row.ListItem).ToList();
			Assert.That(found.Select(row => row.Label), Has.Some.StartsWith("Tests/UIR03/Form"), "a search lists full paths flat");
		}

		[Test]
		public void TheGamepadReachesTheStarAfterTheFooter()
		{
			// The IMGUI numbering is the one checked, so the menu opens again with it.
			DebugMenu.Close();
			DexcRuntimeUIRendererSelection.SetUseUIToolkitForTests(false);
#if DEXC_HAS_UGUI
			DexcRuntimeUIRendererSelection.SetUseUGUIForTests(false);
#endif
			DebugMenu.Open();
			DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.CommandList);
			Entry("Tests").Activated();
			Entry("UIR03").Activated();
			Entry("Form").Activated();
			var count = DexcRuntimeUIRenderer.ProviderSelectionCount();
			Assert.That(count, Is.GreaterThan(DexcRuntimeUIRenderer.DetailRowCountForTests + 1), "rows, the footer, then the star");
			DexcRuntimeUIState.SetSelectedIndex(count - 1);
			DexcRuntimeUIRenderer.ActivateProviderSelection();
			Assert.That(
				DexcRuntimeFavorites.Contains(DexcFavoriteKind.Command, "Tests/UIR03/Form"),
				Is.True,
				$"count {count}, selected {DexcRuntimeUIState.SelectedIndex}, notice {DexcRuntimeUIState.Notice}"
#if DEXC_HAS_UGUI
					+ $", UGUI {DexcRuntimeUGUIRenderer.IsActive}"
#endif
			);
			Assert.That(DexcRuntimeUIState.Page, Is.EqualTo(DexcRuntimeUIPage.CommandDetail), "starring stays on the page");
		}

		[Test]
		public void ACollectionsItemsAreLinesOfATableWithTheIndexOnTheRight()
		{
			DexcRuntimeFormDetail.Open(string.Join("\n", "Values", "12 items", "[0] Value · 0", "[1] Value · 1", "[10] Value · 10"));
			var rows = DexcRuntimeFormDetail.Rows();
			Assert.That(rows[0].Label, Is.EqualTo("Values\n12 items"), "the lines before the first item are its summary");
			var items = rows.Skip(1).ToList();
			Assert.That(items.Select(row => row.Striped), Is.EqualTo(new[] { true, false, true }), "every other line sits on a band");
			Assert.That(items[0].Label, Is.EqualTo(" 0    Value · 0"), "a figure space pads the index to the widest one");
			Assert.That(items[2].Label, Is.EqualTo("10    Value · 10"));

			DexcRuntimeUIState.Back();
			DexcRuntimeFormDetail.Open("plain message");
			Assert.That(DexcRuntimeFormDetail.Rows().Single().Label, Is.EqualTo("plain message"), "other text stays one block");
		}

		[Test]
		public void StarredCommandsAndSettingGroupsAreListedAndStoredByPath()
		{
			DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.CommandList);
			Entry("Tests").Activated();
			Entry("UIR03").Activated();
			Entry("Form").Activated();
			Assert.That(DexcRuntimeUIState.Page, Is.EqualTo(DexcRuntimeUIPage.CommandDetail));
			Assert.That(DexcRuntimeFavorites.Star(DexcRuntimeUIPage.CommandDetail), Is.EqualTo("☆"));
			DexcRuntimeFavorites.ToggleCurrent(DexcRuntimeUIPage.CommandDetail);
			Assert.That(DexcRuntimeFavorites.Star(DexcRuntimeUIPage.CommandDetail), Is.EqualTo("★"));
			Assert.That(m_Storage.TryLoad("UI/Favorites/Command", out var stored), Is.True);
			Assert.That(stored, Is.EqualTo("Tests/UIR03/Form"), "kept by path, which lasts across sessions");

			DexcRuntimeUIState.Back();
			Assert.That(Entry("Form").Leading, Is.EqualTo("★"));
			DexcRuntimeUIState.Back();
			DexcRuntimeUIState.Back();
			Assert.That(Entry("Favorites").TrailingText, Is.EqualTo("1"));
			Entry("Favorites").Activated();
			var favorite = Entry("Form");
			Assert.That(favorite.DetailText, Is.EqualTo("Tests/UIR03"), "a favorite shows the folder it lives in");
			favorite.Activated();
			Assert.That(DexcRuntimeUIState.Command.Path, Is.EqualTo("Tests/UIR03/Form"));

			// A path that is gone is skipped where favorites are listed, but stays stored for a build that brings it back.
			DexcRuntimeFavorites.Toggle(DexcFavoriteKind.Command, "Gone/Command");
			DexcRuntimeUIState.Back();
			Assert.That(DexcRuntimeCommandForm.ListRows().Count(row => row.ListItem), Is.EqualTo(1));
			Assert.That(DexcRuntimeFavorites.All(DexcFavoriteKind.Command), Has.Member("Gone/Command"));

			var group = DexcRuntimeSettingForm.ReadGroups().First();
			DexcRuntimeSettingForm.Open(group.Items[0]);
			DexcRuntimeFavorites.ToggleCurrent(DexcRuntimeUIPage.SettingDetail);
			Assert.That(DexcRuntimeFavorites.Contains(DexcFavoriteKind.Setting, group.Label), Is.True);
			Assert.That(m_Storage.TryLoad("UI/Favorites/Setting", out var settings), Is.True);
			Assert.That(settings, Is.EqualTo(group.Label));
		}
	}
}
#endif
