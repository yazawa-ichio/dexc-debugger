#if DEXC_DEBUGGER
using System.Linq;
using Dexc;
using NUnit.Framework;

namespace DexcDebugger.Tests
{
	public sealed class DexcPanelTests
	{
		int m_Clicks;
		bool m_Flag;
		int m_Choice;
		double m_Number = 5d;
		string m_Text = "hello";

		[SetUp]
		public void SetUp()
		{
			DebugStorage.Storage = new DebugMemoryStorage();
			DebugPanels.ResetForTests();
			DebugDisplay.ResetWidgetsForTests();
			Dexc.DexcDebugger.ResetForTests();
			m_Clicks = 0;
			m_Flag = false;
			m_Choice = 0;
			m_Number = 5d;
			m_Text = "hello";
		}

		[TearDown]
		public void TearDown()
		{
			DebugMenu.Close();
			DebugPanels.ResetForTests();
			DebugDisplay.ResetWidgetsForTests();
			DebugStorage.ResetForTests();
			Dexc.DexcDebugger.ResetForTests();
		}

		DebugPanel RegisterPanel()
		{
			var panel = new DebugPanel("ZZ Panel") { Order = 100 }
				.Toggle("Flag", () => m_Flag, value => m_Flag = value)
				.Choice("Mode", new[] { "A", "B", "C" }, () => m_Choice, value => m_Choice = value)
				.Number("Count", () => m_Number, value => m_Number = value, 0, 10, 1, true)
				.Text("Name", () => m_Text, value => m_Text = value)
				.Label("static label")
				.Button("Click", () => m_Clicks++);
			DebugPanels.Register(panel);
			return panel;
		}

		[Test]
		public void RegisteredPanelsAreListedWithoutBootPanels()
		{
			var panel = RegisterPanel();
			var all = DebugPanels.All;
			Assert.That(all, Does.Contain(panel));
			Assert.That(all.Any(item => item.Title == "Boot Fixture"), Is.False, "boot panels belong to the boot screen");
			Assert.That(panel.Widgets.Count(widget => widget.IsInteractive), Is.EqualTo(5));
		}

		/// <summary>The panel is checked as rows, once, without any renderer: what each renderer draws comes from these.</summary>
		[Test]
		public void PanelRowsDrawWidgetsLikeSettingsAndApplyTypedTextOnConfirm()
		{
			var panel = RegisterPanel();
			Dexc.DexcDebugger.Initialize();
			DebugMenu.Open();
			DexcRuntimePanelForm.Open(panel);
			var rows = DexcRuntimePanelForm.DetailRows();
			DexcUIRow Row(string label) => rows.Single(row => row.Label == label);
			Assert.That(rows.Any(row => row.Kind == DexcUIRowKind.Header), Is.False, "the panel's title is in the header");
			Assert.That(DexcRuntimeUIRenderer.Breadcrumb(DexcRuntimeUIPage.PanelDetail).Last(), Is.EqualTo("ZZ Panel"));

			var flag = Row("Flag");
			Assert.That(flag.Kind, Is.EqualTo(DexcUIRowKind.Choice));
			Assert.That(flag.Boolean, Is.True);
			Assert.That(flag.Text(), Is.EqualTo("false"));
			flag.Activated();
			Assert.That(m_Flag, Is.True);
			Assert.That(flag.Text(), Is.EqualTo("true"));

			var mode = Row("Mode");
			Assert.That(mode.Kind, Is.EqualTo(DexcUIRowKind.Choice));
			Assert.That(mode.Boolean, Is.False);
			mode.Activated();
			Assert.That(mode.Text(), Is.EqualTo("B"), "pressing a choice moves to the next option");
			Assert.That(mode.Cycle(-1), Is.True);
			Assert.That(mode.Text(), Is.EqualTo("A"), "the row steps back in place as the gamepad's left does");
			Assert.That(flag.Cycle(1), Is.True);
			Assert.That(m_Flag, Is.False);
			flag.Activated();

			var count = Row("Count");
			Assert.That(count.Kind, Is.EqualTo(DexcUIRowKind.Input));
			count.Changed("7");
			Assert.That(count.Text(), Is.EqualTo("7"));
			Assert.That(m_Number, Is.EqualTo(5d), "typing keeps a draft");
			Assert.That(count.Confirmed("7"), Is.True);
			Assert.That(m_Number, Is.EqualTo(7d));
			count.Changed("x");
			Assert.That(count.Confirmed("x"), Is.False);
			Assert.That(m_Number, Is.EqualTo(7d));
			Assert.That(count.Text(), Is.EqualTo("7"), "a number that does not parse shows the value again");
			Assert.That(DexcRuntimeUIState.Notice, Does.Contain("Invalid number"));

			var label = rows.Single(row => row.Kind == DexcUIRowKind.Label);
			Assert.That(label.Text(), Is.EqualTo("static label"));
			Assert.That(label.NavigationIndex, Is.EqualTo(-1));
			var click = Row("Click");
			Assert.That(click.Kind, Is.EqualTo(DexcUIRowKind.Button));
			Assert.That(click.NavigationIndex, Is.EqualTo(4), "the row's place matches the gamepad's focusable list");
			click.Activated();
			Assert.That(m_Clicks, Is.EqualTo(1));
		}

		[Test]
		public void RuntimeMenuPanelsPageNavigatesAndOperatesWidgets()
		{
			var panel = RegisterPanel();
			Dexc.DexcDebugger.Initialize();
			DebugMenu.Open();
			DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.PanelList);
			var panels = DebugPanels.All;
			Assert.That(DexcRuntimeUIRenderer.ProviderSelectionCount(), Is.EqualTo(panels.Count));
			DexcRuntimeUIState.SetSelectedIndex(panels.ToList().IndexOf(panel));
			DexcRuntimeUIRenderer.ActivateProviderSelection();
			Assert.That(DexcRuntimeUIState.Page, Is.EqualTo(DexcRuntimeUIPage.PanelDetail));
			Assert.That(DexcRuntimePanelForm.Current, Is.SameAs(panel));
			Assert.That(DexcRuntimeUIRenderer.ProviderSelectionCount(), Is.EqualTo(5));

			DexcRuntimeUIState.SetSelectedIndex(0);
			DexcRuntimePanelForm.Adjust(1);
			Assert.That(m_Flag, Is.True);
			DexcRuntimeUIState.SetSelectedIndex(1);
			DexcRuntimePanelForm.Adjust(1);
			Assert.That(m_Choice, Is.EqualTo(1));
			DexcRuntimePanelForm.Adjust(-2);
			Assert.That(m_Choice, Is.EqualTo(2), "choice wraps");
			DexcRuntimeUIState.SetSelectedIndex(2);
			DexcRuntimePanelForm.Adjust(1);
			Assert.That(m_Number, Is.EqualTo(6d));
			Assert.That(DexcRuntimePanelForm.SetText(DexcRuntimePanelForm.Focusable[2], "42"), Is.True);
			Assert.That(m_Number, Is.EqualTo(10d), "clamped");
			Assert.That(DexcRuntimePanelForm.SetText(DexcRuntimePanelForm.Focusable[2], "x"), Is.False);
			Assert.That(DexcRuntimePanelForm.SetText(DexcRuntimePanelForm.Focusable[3], "renamed"), Is.True);
			Assert.That(m_Text, Is.EqualTo("renamed"));
			DexcRuntimeUIState.SetSelectedIndex(4);
			DexcRuntimeUIRenderer.ActivateProviderSelection();
			Assert.That(m_Clicks, Is.EqualTo(1));

			DexcRuntimeUIState.Back();
			Assert.That(DexcRuntimeUIState.Page, Is.EqualTo(DexcRuntimeUIPage.PanelList));
			Assert.That(DexcRuntimePanelForm.Current, Is.Null);
			DexcRuntimeUIState.Back();
			Assert.That(DexcRuntimeUIState.Page, Is.EqualTo(DexcRuntimeUIPage.Home));
			// The six features come first; a collection notice, favorites or recent runs may follow them.
			Assert.That(DexcRuntimeUIRenderer.ProviderSelectionCount(), Is.GreaterThanOrEqualTo(6), "Home lists Panels");
			Assert.That(DexcUIRows.HomeRows().Single(row => row.NavigationIndex == 5).Label, Is.EqualTo("Panels"));
		}

		[Test]
		public void DisplayWidgetsDrawEachFrameUntilUnregistered()
		{
			Dexc.DexcDebugger.Initialize();
			var frames = 0;
			var widget = new DebugDisplayWidget("ZZ/HUD/Fps", () => "60 (" + frames++ + ")");
			DebugDisplay.RegisterWidget(widget);
			DexcDisplayRuntime.Tick();
			Assert.That(DebugDisplay.TryGetCompletedFrame(out var frame), Is.True);
			Assert.That(frame.Globals.Any(visual => visual.Path == "ZZ/HUD/Fps" && visual.Text == "60 (0)"), Is.True);
			DexcDisplayRuntime.Tick();
			Assert.That(DebugDisplay.TryGetCompletedFrame(out frame), Is.True);
			Assert.That(frame.Globals.Any(visual => visual.Path == "ZZ/HUD/Fps" && visual.Text == "60 (1)"), Is.True);
			DebugDisplay.UnregisterWidget(widget);
			DexcDisplayRuntime.Tick();
			Assert.That(DebugDisplay.TryGetCompletedFrame(out frame), Is.True);
			Assert.That(frame.Globals.Any(visual => visual.Path == "ZZ/HUD/Fps"), Is.False);
		}

		[Test]
		public void DisplayWidgetFailureIsReportedOnceAndDoesNotStopOtherWidgets()
		{
			Dexc.DexcDebugger.Initialize();
			DebugDisplay.RegisterWidget(new DebugDisplayWidget("ZZ/HUD/Broken", () => throw new System.InvalidOperationException("boom")));
			DebugDisplay.RegisterWidget(new DebugDisplayWidget("ZZ/HUD/Ok", () => "fine"));
			UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Warning, new System.Text.RegularExpressions.Regex("ZZ/HUD/Broken"));
			DexcDisplayRuntime.Tick();
			DexcDisplayRuntime.Tick();
			Assert.That(DebugDisplay.TryGetCompletedFrame(out var frame), Is.True);
			Assert.That(frame.Globals.Any(visual => visual.Path == "ZZ/HUD/Ok"), Is.True);
		}
	}
}
#endif
