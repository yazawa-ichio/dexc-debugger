#if DEXC_DEBUGGER
using Dexc;
using NUnit.Framework;

namespace DexcDebugger.Tests
{
	public sealed class DexcRuntimeFormLayoutTests
	{
		[Test]
		public void LabelSitsBesideTheValueUntilTheRowIsNarrowOrTheLabelWrapsTooMuch()
		{
			var wide = DexcRuntimeFormLayout.Arrange(360f, _ => 1);
			Assert.That(wide.Stacked, Is.False);
			Assert.That(wide.LabelWidth, Is.EqualTo(144f));
			Assert.That(wide.ValueX, Is.EqualTo(144f + DexcRuntimeFormLayout.ColumnGap));
			Assert.That(wide.ValueX + wide.ValueWidth, Is.EqualTo(360f));

			Assert.That(DexcRuntimeFormLayout.Arrange(360f, _ => DexcRuntimeFormLayout.MaximumLabelLines).Stacked, Is.False);
			var wrapped = DexcRuntimeFormLayout.Arrange(360f, _ => DexcRuntimeFormLayout.MaximumLabelLines + 1);
			Assert.That(wrapped.Stacked, Is.True);
			Assert.That(wrapped.LabelWidth, Is.EqualTo(360f));
			Assert.That(wrapped.ValueWidth, Is.EqualTo(360f));

			var lines = 0;
			var narrow = DexcRuntimeFormLayout.Arrange(DexcRuntimeFormLayout.StackWidth - 1f, width =>
			{
				lines++;
				return 1;
			});
			Assert.That(narrow.Stacked, Is.True);
			Assert.That(lines, Is.Zero, "a narrow row stacks without measuring the label");
		}

		[Test]
		public void LongLabelsBreakBetweenTheirWords()
		{
			System.Func<string, float> width = text => text.Length * 10f;
			Assert.That(DexcRuntimeFormLayout.BreakLabel("Count", 170f, width), Is.EqualTo("Count"));
			Assert.That(
				DexcRuntimeFormLayout.BreakLabel("DestinationSceneSourcePath", 170f, width),
				Is.EqualTo("DestinationScene\nSourcePath")
			);
			Assert.That(DexcRuntimeFormLayout.BreakLabel("ComponentReference", 120f, width), Is.EqualTo("Component\nReference"));
			Assert.That(DexcRuntimeFormLayout.BreakLabel("Max hit points", 100f, width), Is.EqualTo("Max hit\npoints"));
			Assert.That(DexcRuntimeFormLayout.BreakLabel("UIScale", 30f, width), Is.EqualTo("UI\nScale"), "an acronym ends before the next word");
		}

		[Test]
		public void WideScreensOfferSideBySideAndTheHeaderSwitchDecidesIt()
		{
			var screen = new UnityEngine.Rect(0f, 0f, 1366f, 768f);
			try
			{
				DexcRuntimeUIState.LayoutOverride = null;
				var single = DexcRuntimeUIReferenceResolver.Calculate(1366, 768, screen, DexcRuntimeUIProfileKind.Windows);
				Assert.That(single.CanSplit, Is.True, "1366 wide fits the 960 wide panel");
				Assert.That(single.Split, Is.False, "a skin without a choice keeps one page");
				Assert.That(single.Panel.width, Is.EqualTo(360f));

				DexcRuntimeUIState.LayoutOverride = DexcRuntimeUILayoutMode.Split;
				var split = DexcRuntimeUIReferenceResolver.Calculate(1366, 768, screen, DexcRuntimeUIProfileKind.Windows);
				Assert.That(split.Split, Is.True);
				Assert.That(split.Panel.width, Is.EqualTo(960f));
				Assert.That(split.Panel.height, Is.EqualTo(560f));

				var phone = DexcRuntimeUIReferenceResolver.Calculate(390, 844, new UnityEngine.Rect(0f, 0f, 390f, 844f), DexcRuntimeUIProfileKind.AndroidPortrait);
				Assert.That(phone.CanSplit, Is.False);
				Assert.That(phone.Split, Is.False, "a narrow screen stays one page whatever the switch says");

				var tall = DexcRuntimeUIReferenceResolver.Calculate(1440, 3200, new UnityEngine.Rect(0f, 0f, 1440f, 3200f), DexcRuntimeUIProfileKind.Windows);
				Assert.That(tall.CanSplit, Is.False, "a portrait screen offers no side by side even when it is wide enough");
				Assert.That(tall.Split, Is.False);

				DexcRuntimeUIRenderer.SplitColumns(DexcRuntimeUIPage.CommandDetail, 960f, out var sections, out var list);
				Assert.That(sections, Is.EqualTo(DexcRuntimeFormLayout.SplitSectionWidth));
				Assert.That(list, Is.EqualTo(DexcRuntimeFormLayout.SplitListWidth), "a page opened from a list keeps the list beside it");
				DexcRuntimeUIRenderer.SplitColumns(DexcRuntimeUIPage.CommandList, 960f, out sections, out list);
				Assert.That(list, Is.Zero, "the list itself takes the list's place");
				DexcRuntimeUIRenderer.SplitColumns(DexcRuntimeUIPage.History, 960f, out sections, out list);
				Assert.That(list, Is.Zero);
			}
			finally
			{
				DexcRuntimeUIState.LayoutOverride = null;
			}
		}

		[Test]
		public void DisplayTextMovesToTheOtherSideWhenThePanelCoversIt()
		{
			var hud = new[]
			{
				new DexcDisplayOverlay.Label(new UnityEngine.Rect(12f, 12f, 260f, 30f), "FPS", DexcOverlayLabelStyle.Box),
			};
			var landscape = DexcRuntimeUIReferenceResolver.Calculate(844, 390, new UnityEngine.Rect(0f, 0f, 844f, 390f), DexcRuntimeUIProfileKind.AndroidLandscape);
			var landscapePanel = new UnityEngine.Rect(landscape.Panel.x, 390f - landscape.Panel.yMax, landscape.Panel.width, landscape.Panel.height);
			Assert.That(landscapePanel.x, Is.EqualTo(12f), "the default landscape panel sits on the left");
			Assert.That(DexcDisplayOverlay.MovesAside(hud, landscapePanel, 844f), Is.True);

			var windows = DexcRuntimeUIReferenceResolver.Calculate(1366, 768, new UnityEngine.Rect(0f, 0f, 1366f, 768f), DexcRuntimeUIProfileKind.Windows);
			var windowsPanel = new UnityEngine.Rect(windows.Panel.x, 768f - windows.Panel.yMax, windows.Panel.width, windows.Panel.height);
			Assert.That(DexcDisplayOverlay.MovesAside(hud, windowsPanel, 1366f), Is.False, "the HUD sits above the centred panel");
			Assert.That(DexcDisplayOverlay.MovesAside(hud, null, 844f), Is.False, "nothing moves while the menu is closed");
			Assert.That(
				DexcDisplayOverlay.MovesAside(hud, new UnityEngine.Rect(0f, 0f, 844f, 390f), 844f),
				Is.False,
				"a panel across the whole width leaves the text where it is"
			);
		}

		[Test]
		public void StepWrapsAtBothEndsAndStartsFromAnEndWhenTheCurrentValueIsUnknown()
		{
			var names = new[] { "A", "B", "C" };
			Assert.That(DexcUIRow.Step(names, "A", 1), Is.EqualTo("B"));
			Assert.That(DexcUIRow.Step(names, "C", 1), Is.EqualTo("A"));
			Assert.That(DexcUIRow.Step(names, "A", -1), Is.EqualTo("C"));
			Assert.That(DexcUIRow.Step(names, "Select…", 1), Is.EqualTo("A"));
			Assert.That(DexcUIRow.Step(names, "Select…", -1), Is.EqualTo("C"));
			Assert.That(DexcUIRow.Step(new string[0], "A", 1), Is.Null);
		}
	}
}
#endif
