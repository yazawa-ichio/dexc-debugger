#if DEXC_DEBUGGER && UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dexc;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace DexcDebugger.Tests
{
	public sealed class DexcCommandFormTests
	{
		DexcRuntimeUISkin m_Skin;
		GameObject m_Events;
		GameObject m_Camera;
		internal static GameObject First;
		internal static GameObject Second;

		[SetUp]
		public void SetUp()
		{
			DexcRuntimeUIState.ResetForSession();
			Dexc.DexcDebugger.ResetForTests();
			DexcUIR03Command.Executions = 0;
			First = new GameObject("First");
			Second = new GameObject("Second");
			m_Skin = ScriptableObject.CreateInstance<DexcRuntimeUISkin>();
			m_Skin.SetWorldVisualShaderForEditor(
				AssetDatabase.LoadAssetAtPath<Shader>("Packages/jp.yazawa-ichio.dexc-debugger/DebugUI/Shaders/WorldVisual.shader")
			);
			m_Skin.SetUIToolkitPanelSettingsTemplateForEditor(
				AssetDatabase.LoadAssetAtPath<PanelSettings>(
					"Packages/jp.yazawa-ichio.dexc-debugger/DebugUI/DexcRuntimeUIToolkitPanelSettings.asset"
				)
			);
			Dexc.DexcDebugger.Initialize(options => options.RuntimeUI.Skin = m_Skin);
			DexcRuntimeUIState.SetSession(Dexc.DexcDebugger.CurrentSession.SessionId);
			DebugMenu.Open();
			Select();
			DexcRuntimeCommandForm.OpenReference(Field("Target"));
			DexcRuntimeFormDetail.Select(DexcRuntimeFormDetail.ReadChoices().Items[1]);
		}

		[TearDown]
		public void TearDown()
		{
			DebugMenu.Close();
			DexcRuntimeUIRendererSelection.SetUseUIToolkitForTests(false);
			DexcRuntimeUIToolkitRenderer.DestroyForTests();
#if DEXC_HAS_UGUI
			DexcRuntimeUIRendererSelection.SetUseUGUIForTests(false);
			DexcRuntimeUGUIRenderer.DestroyForTests();
#endif
			DexcRuntimeUIState.ResetForSession();
			Dexc.DexcDebugger.ResetForTests();
			Object.DestroyImmediate(m_Skin);
			Object.DestroyImmediate(First);
			Object.DestroyImmediate(Second);
			if (m_Events != null)
				Object.DestroyImmediate(m_Events);
			if (m_Camera != null)
				Object.DestroyImmediate(m_Camera);
		}

		static void Select(string path = "Tests/UIR03/Form")
		{
			DexcRuntimeUIRenderer.SelectCommandForTests(
				DebugCommand.Registry.GetList(new DebugListQuery(path, "", new DebugPageRequest(0, 1))).Items.Single()
			);
		}

		static DebugCommandFieldDescriptor Field(string name) => DexcRuntimeUIState.Command.Fields.Single(item => item.Label == name);

		static DebugValue Value(string name) => DexcCommandRuntime.Drafts.GetInputValue(DexcRuntimeUIState.DraftId, Field(name).Id);

		/// <summary>The page is checked as rows, once, without any renderer: what each renderer draws comes from these.</summary>
		[Test]
		public void DetailRowsDescribeTheCommandAndEditTheDraftWithoutARenderer()
		{
			var rows = DexcRuntimeCommandForm.DetailRows();
			DexcUIRow Row(string name) => rows.Single(row => row.Key == DexcRuntimeCommandForm.FieldKey(Field(name).Id));
			// The path is the header's place in the menu; the page starts with its content.
			Assert.That(rows.Any(row => row.Kind == DexcUIRowKind.Header && row.Label == DexcRuntimeUIState.Command.Path), Is.False);
			Assert.That(
				DexcRuntimeUIRenderer.Breadcrumb(DexcRuntimeUIPage.CommandDetail),
				Is.EqualTo(new[] { "Command" }.Concat(DexcRuntimeUIState.Command.Path.Split('/')))
			);

			var amount = Row("Amount");
			Assert.That(amount.Kind, Is.EqualTo(DexcUIRowKind.Input));
			Assert.That(amount.Text(), Is.EqualTo("7"));
			Assert.That(amount.Tag, Is.EqualTo(Field("Amount").Id));
			Assert.That(amount.IsEnabled, Is.True);
			Assert.That(Row("Target").Kind, Is.EqualTo(DexcUIRowKind.Choice));
			Assert.That(Row("Mode").Kind, Is.EqualTo(DexcUIRowKind.Choice));
			Assert.That(Row("Text").Auxiliary, Is.Not.Null, "a string can be opened in full");

			amount.Changed("12");
			Assert.That(Value("Amount"), Is.EqualTo(DebugValue.From(12)));
			Assert.That(Row("Mirror").Text(), Is.EqualTo("24"), "the command's own change notification reaches the rows");
			amount.Changed("x");
			Assert.That(amount.Text(), Is.EqualTo("x"), "invalid text is kept as typed");
			Assert.That(amount.ErrorText, Is.EqualTo("Invalid input."));
			Assert.That(Value("Amount"), Is.EqualTo(DebugValue.From(12)), "the draft keeps the last valid value");

			var first = DexcRuntimeCommandForm.DetailRows(8);
			Assert.That(first.Any(row => row.Key == "command:previous"), Is.False);
			first.Single(row => row.Key == "command:next").Activated();
			var second = DexcRuntimeCommandForm.DetailRows(8);
			Assert.That(second.Any(row => row.Key == "command:previous"), Is.True);
			Assert.That(
				second.Where(row => row.Tag != null).Select(row => row.Tag),
				Is.Not.EquivalentTo(first.Where(row => row.Tag != null).Select(row => row.Tag)),
				"Next moves to the following fields"
			);
		}

		[Test]
		public void CommandListPagesCoverAllDefinitionsAndSearchResetsPage()
		{
			DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.CommandList);
			DexcRuntimeUIState.Search = string.Empty;
			var ids = new System.Collections.Generic.HashSet<DebugCommandId>();
			var pages = 0;
			while (true)
			{
				var page = DexcRuntimeCommandForm.ReadList();
				Assert.That(page.Items.Count, Is.InRange(1, 20));
				foreach (var item in page.Items)
					Assert.That(ids.Add(item.Id), Is.True);
				pages++;
				Assert.That(pages, Is.LessThan(100));
				if (!page.Page.HasMore)
					break;
				DexcRuntimeCommandForm.MoveListPage(1);
			}
			Assert.That(pages, Is.GreaterThan(1));
			Assert.That(
				ids.Count,
				Is.EqualTo(DebugCommand.Registry.GetList(new DebugListQuery("", "", new DebugPageRequest(0, 1))).Summary.DescriptorCount)
			);
			DexcRuntimeUIState.Search = "Tests/UIR03/Form";
			Assert.That(DexcRuntimeCommandForm.ReadList().Items.Single().Path, Is.EqualTo("Tests/UIR03/Form"));
			Assert.That(DexcRuntimeCommandForm.ListOffset, Is.Zero);
		}

		[Test]
		public void ClearingASearchReturnsToTheFirstFolderPageAndHomeOpensTheOtherListUnfiltered()
		{
			DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.CommandList);
			DexcRuntimeUIState.Search = "Tests/";
			Assert.That(DexcRuntimeCommandForm.ReadList().Page.HasMore, Is.True, "the search needs a second page");
			DexcRuntimeCommandForm.MoveListPage(1);
			Assert.That(DexcRuntimeCommandForm.ListOffset, Is.GreaterThan(0));

			// The folders start at their first page again, not at the search's offset.
			DexcRuntimeUIState.Search = string.Empty;
			var rows = DexcRuntimeCommandForm.ListRows();
			Assert.That(DexcRuntimeCommandForm.ListOffset, Is.Zero);
			Assert.That(rows.Count(row => row.Kind == DexcUIRowKind.Button), Is.GreaterThan(0));

			// The two lists share the search text, so the other list opened from Home must not start filtered by it.
			DexcRuntimeUIState.Search = "Tests/";
			DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.Home);
			DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.SettingList);
			Assert.That(DexcRuntimeUIState.Search, Is.Empty);

			// Side by side, the section column moves straight from one list to the other.
			DexcRuntimeUIState.Search = "Tests/";
			DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.CommandList);
			Assert.That(DexcRuntimeUIState.Search, Is.Empty);

			// Back from a command keeps the search it was opened from.
			DexcRuntimeUIState.Search = "Tests/";
			DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.CommandDetail);
			DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.CommandList);
			Assert.That(DexcRuntimeUIState.Search, Is.EqualTo("Tests/"));
		}

		[Test]
		public void HistoryOpensNewDraftWithoutExecutionOrSnapshotMutation()
		{
			DexcRuntimeUIRenderer.UpdateCommandInputText(Field("Amount"), "99");
			DexcRuntimeCommandExecution.Request();
			Assert.That(DexcUIR03Command.Executions, Is.EqualTo(1));
			var original = DexcRuntimeUIState.DraftId;
			var history = DebugCommand.Draft.GetHistory(DexcRuntimeUIState.CommandId, new DebugPageRequest(0, 16)).Entries.Single();
			var summary = history.InputSummary;
			DexcRuntimeUIRenderer.UpdateCommandInputText(Field("Amount"), "123");
			Assert.That(DexcRuntimeCommandForm.ResultSummary, Does.StartWith("Result before edits"));
			DexcRuntimeCommandForm.OpenHistory();
			DexcRuntimeFormDetail.Select(DexcRuntimeFormDetail.ReadChoices().Items.Single());
			Assert.That(DexcUIR03Command.Executions, Is.EqualTo(1));
			Assert.That(DexcRuntimeUIState.DraftId, Is.Not.EqualTo(original));
			Assert.That(DebugCommand.Draft.Validate(original).Status, Is.EqualTo(DebugCommandDraftOperationStatus.Stale));
			Assert.That(Value("Amount"), Is.EqualTo(DebugValue.From(99)));
			DexcRuntimeUIRenderer.UpdateCommandInputText(Field("Amount"), "200");
			Assert.That(
				DebugCommand.Draft.GetHistory(DexcRuntimeUIState.CommandId, new DebugPageRequest(0, 16)).Entries.Single().InputSummary,
				Is.EqualTo(summary)
			);
			Assert.That(DexcRuntimeCommandForm.CurrentResult, Is.Null);
		}

		[Test]
		public void ReferenceSelectionUsesChosenCandidateAndPreservesOtherInvalidRaw()
		{
			DexcRuntimeUIRenderer.UpdateCommandInputText(Field("Amount"), "-");
			DexcRuntimeCommandForm.OpenReference(Field("Target"));
			var page = DexcRuntimeFormDetail.ReadChoices();
			Assert.That(page.Items.Count, Is.EqualTo(3));
			DexcRuntimeFormDetail.Select(page.Items.Single(item => item.Label.StartsWith("Second")));
			Assert.That(Value("Target"), Is.EqualTo(DebugValue.From("second")));
			Assert.That(DexcRuntimeUIState.CommandInputText[Field("Amount").Id], Is.EqualTo("-"));
			Assert.That(DexcUIR03Command.Executions, Is.Zero);
			DexcRuntimeCommandForm.OpenReference(Field("Target"));
			DexcRuntimeFormDetail.ChangeSearch("First");
			Assert.That(DexcRuntimeFormDetail.ReadChoices().Items.Count, Is.EqualTo(2));
			DexcRuntimeFormDetail.Select(DexcRuntimeFormDetail.ReadChoices().Items[0]);
			Assert.That(Value("Target").Kind, Is.EqualTo(DebugValueKind.Null));
		}

		[Test]
		public void ChoicePagingEnumAndStaleHelperEvents()
		{
			DexcRuntimeCommandForm.OpenChoice(Field("Choice"));
			var first = DexcRuntimeFormDetail.ReadChoices();
			Assert.That(first.HasMore, Is.True);
			Assert.That(first.Items.Count, Is.EqualTo(17));
			DexcRuntimeFormDetail.Next();
			var second = DexcRuntimeFormDetail.ReadChoices();
			Assert.That(second.Items.Count, Is.EqualTo(4));
			DexcRuntimeFormDetail.Select(second.Items.Last());
			Assert.That(Value("Choice"), Is.EqualTo(DebugValue.From("key19")));
			DexcRuntimeCommandForm.OpenEnum(Field("Mode"));
			DexcRuntimeFormDetail.Select(first.Items[1]);
			Assert.That(
				DexcRuntimeUIState.Page,
				Is.EqualTo(DexcRuntimeUIPage.FormDetail),
				"old helper callbacks must not close the current helper"
			);
			DexcRuntimeFormDetail.Select(DexcRuntimeFormDetail.ReadChoices().Items.Single(item => item.Label == "Second"));
			Assert.That(Value("Mode"), Is.EqualTo(DebugValue.From("Second")));
		}

		[Test]
		public void FullTextBackKeepsMultilineDraftAndRawErrorKeepsNoticeEmpty()
		{
			DexcRuntimeUIState.ScrollPosition = new Vector2(0, 77);
			DexcRuntimeCommandForm.OpenFullText(Field("Text"));
			DexcRuntimeFormDetail.ChangeText("First line\n" + new string('あ', 120));
			DexcRuntimeUIState.Back();
			Assert.That(Value("Text"), Is.EqualTo(DebugValue.From("First line\n" + new string('あ', 120))));
			Assert.That(DexcRuntimeUIState.ScrollPosition.y, Is.EqualTo(77));
			DexcRuntimeUIRenderer.UpdateCommandInputText(Field("Amount"), "1,000");
			Assert.That(DexcRuntimeUIState.CommandInvalidInput, Does.Contain(Field("Amount").Id));
			Assert.That(DexcRuntimeUIState.Notice, Is.Empty);
		}

		[UnityTest]
		public IEnumerator LateAsyncResultDoesNotAttachToNewDraftOfSameCommand()
		{
			DexcUIR03Async.Completion = new TaskCompletionSource<bool>();
			Select("Tests/UIR03/Async");
			var id = DexcRuntimeUIState.CommandId;
			DexcRuntimeCommandExecution.Request();
			Select("Tests/UIR03/Async");
			DexcUIR03Async.Completion.SetResult(true);
			for (var frame = 0; frame < 60 && DexcRuntimeUIState.AsyncExecution != null; frame++)
			{
				yield return null;
				DexcRuntimeCommandExecution.Poll();
			}
			Assert.That(DexcRuntimeUIState.AsyncExecution, Is.Null);
			Assert.That(DexcRuntimeUIState.LastCommandResult, Is.Null);
			Assert.That(DebugCommand.Draft.GetHistory(id, new DebugPageRequest(0, 16)).Entries.Count, Is.EqualTo(1));
		}

		[UnityTest]
		public IEnumerator UIToolkitKeepsInputsAndHistoryAlongsideResult() => NativeForm(true);

#if DEXC_HAS_UGUI
		[UnityTest]
		public IEnumerator UGUIKeepsInputsAndHistoryAlongsideResult() => NativeForm(false);
#endif

		IEnumerator NativeForm(bool toolkit)
		{
			if (toolkit)
				DexcRuntimeUIRendererSelection.SetUseUIToolkitForTests(true);
#if DEXC_HAS_UGUI
			else
			{
				DexcRuntimeUIRendererSelection.SetUseUGUIForTests(true);
				m_Events = new GameObject("UIR03 events", DexcUGUIApi.EventSystemType);
			}
#endif
			yield return Render(toolkit);
			DexcRuntimeCommandExecution.Request();
			yield return Render(toolkit);
			Assert.That(DexcRuntimeCommandForm.CurrentResult, Is.Not.Null);
			if (toolkit)
			{
				var root = DexcRuntimeUIToolkitRenderer.SafeAreaRootForTests;
				Assert.That(root.Query<TextField>().ToList().Any(input => input.label == "Amount"), Is.True);
				Assert.That(root.Q<Button>("History").resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
				Assert.That(root.Q<Button>("FooterSecondary").style.display.value, Is.EqualTo(DisplayStyle.None));
				Assert.That(
					Enumerable.Range(0, DexcRuntimeUIToolkitRenderer.VisibleRowCount).Select(DexcRuntimeUIToolkitRenderer.RowTextForTests),
					Does.Contain("View result")
				);
				root.Query<TextField>().ToList().First(input => input.label == "Amount").value = "42";
				Assert.That(root.Query<TextField>().ToList().First(input => input.label == "Mirror").value, Is.EqualTo("84"));
				Assert.That(
					Enumerable
						.Range(0, DexcRuntimeUIToolkitRenderer.VisibleRowCount)
						.Select(DexcRuntimeUIToolkitRenderer.RowTextForTests)
						.Any(text => text.StartsWith("Result before edits")),
					Is.True
				);
			}
#if DEXC_HAS_UGUI
			else
			{
				Assert.That(
					DexcRuntimeUGUIRenderer
						.RootForTests.GetComponentsInChildren(DexcUGUIApi.InputFieldType)
						.Any(input => ((Behaviour)input).enabled),
					Is.True
				);
				Assert.That(
					Enumerable.Range(0, DexcRuntimeUGUIRenderer.VisibleRowCount).Select(DexcRuntimeUGUIRenderer.RowTextForTests),
					Does.Contain("Amount").And.Contain("View result")
				);
				var inputs = DexcRuntimeUGUIRenderer.RootForTests.GetComponentsInChildren(DexcUGUIApi.InputFieldType);
				var amount = inputs.First(input =>
					(string)DexcUGUIApi.Get(input.transform.parent.Find("Label").GetComponent(DexcUGUIApi.TextType), "text") == "Amount"
				);
				var mirror = inputs.First(input =>
					(string)DexcUGUIApi.Get(input.transform.parent.Find("Label").GetComponent(DexcUGUIApi.TextType), "text") == "Mirror"
				);
				DexcUGUIApi.Set(amount, "text", "42");
				Assert.That((string)DexcUGUIApi.Get(mirror, "text"), Is.EqualTo("84"));
				Assert.That(
					Enumerable
						.Range(0, DexcRuntimeUGUIRenderer.VisibleRowCount)
						.Select(DexcRuntimeUGUIRenderer.RowTextForTests)
						.Any(text => text.StartsWith("Result before edits")),
					Is.True
				);
			}
#endif
			DexcRuntimeCommandForm.OpenHistory();
			yield return Render(toolkit);
			DexcRuntimeFormDetail.Select(DexcRuntimeFormDetail.ReadChoices().Items.Single());
			yield return Render(toolkit);
			Assert.That(DexcUIR03Command.Executions, Is.EqualTo(1));
			Assert.That(DexcRuntimeCommandForm.CurrentResult, Is.Null);
		}

		static IEnumerator Render(bool toolkit)
		{
			if (toolkit)
				DexcRuntimeUIToolkitRenderer.RenderProfileForTests(390, 844, new Rect(0, 0, 390, 844));
#if DEXC_HAS_UGUI
			else
				DexcRuntimeUGUIRenderer.RenderProfileForTests(390, 844, new Rect(0, 0, 390, 844));
#endif
			Canvas.ForceUpdateCanvases();
			yield return null;
			yield return null;
		}

		[UnityTest]
		public IEnumerator SideBySideCaptures()
		{
			if (Application.isBatchMode)
				Assert.Ignore("Requires the automatic --graphics runner.");
			foreach (var renderer in new[] { "IMGUI", "UIToolkit", "UGUI" })
			{
#if !DEXC_HAS_UGUI
				if (renderer == "UGUI")
					continue;
#endif
				TearDown();
				DexcRuntimeBootstrap.InitializeForTests();
				SetUp();
				DexcRuntimeUIState.Back();
				DexcRuntimeUIState.LayoutOverride = DexcRuntimeUILayoutMode.Split;
				Select();
				m_Camera = new GameObject("Split capture camera");
				var camera = m_Camera.AddComponent<Camera>();
				camera.clearFlags = CameraClearFlags.SolidColor;
				camera.backgroundColor = Color.black;
				camera.cullingMask = 0;
				DexcRuntimeUIRendererSelection.SetUseUIToolkitForTests(renderer == "UIToolkit");
#if DEXC_HAS_UGUI
				DexcRuntimeUIRendererSelection.SetUseUGUIForTests(renderer == "UGUI");
#endif
				var safe = new Rect(0, 0, Screen.width, Screen.height);
				Assert.That(
					DexcRuntimeUIReferenceResolver.Calculate(Screen.width, Screen.height, safe, DexcRuntimeUIProfileKind.Windows).Split,
					Is.True,
					"the capture screen must be wide enough"
				);
				if (renderer == "UIToolkit")
					DexcRuntimeUIToolkitRenderer.RenderProfileForTests(Screen.width, Screen.height, safe, DexcRuntimeUIProfileKind.Windows);
#if DEXC_HAS_UGUI
				else if (renderer == "UGUI")
					DexcRuntimeUGUIRenderer.RenderProfileForTests(Screen.width, Screen.height, safe, DexcRuntimeUIProfileKind.Windows);
#endif
				else
					DexcRuntimeUIRenderer.SetProfileOverrideForTests(DexcRuntimeUIProfileKind.Windows);
				for (var frame = 0; frame < 8; frame++)
					yield return null;
				yield return new WaitForEndOfFrame();
				var texture = ScreenCapture.CaptureScreenshotAsTexture();
				try
				{
					Directory.CreateDirectory("Logs/UnityTests/Split");
					File.WriteAllBytes("Logs/UnityTests/Split/" + renderer + "-detail.png", texture.EncodeToPNG());
				}
				finally
				{
					Object.DestroyImmediate(texture);
				}
			}
		}

		[UnityTest]
		public IEnumerator CommandFlowCaptures()
		{
			if (Application.isBatchMode)
				Assert.Ignore("Requires the automatic --graphics runner.");
			foreach (var renderer in new[] { "IMGUI", "UIToolkit", "UGUI" })
			{
#if !DEXC_HAS_UGUI
				if (renderer == "UGUI")
					continue;
#endif
				TearDown();
				DexcRuntimeBootstrap.InitializeForTests();
				SetUp();
				m_Camera = new GameObject("UIR03 capture camera");
				var camera = m_Camera.AddComponent<Camera>();
				camera.clearFlags = CameraClearFlags.SolidColor;
				camera.backgroundColor = Color.black;
				camera.cullingMask = 0;
				DexcRuntimeUIRendererSelection.SetUseUIToolkitForTests(renderer == "UIToolkit");
#if DEXC_HAS_UGUI
				DexcRuntimeUIRendererSelection.SetUseUGUIForTests(renderer == "UGUI");
#endif
				foreach (var state in new[] { "edit", "result", "history", "selection" })
				{
					if (state == "result")
						DexcRuntimeCommandExecution.Request();
					if (state == "history")
						DexcRuntimeCommandForm.OpenHistory();
					if (state == "selection")
					{
						DexcRuntimeUIState.Back();
						DexcRuntimeCommandForm.OpenReference(Field("Target"));
					}
					var safe = new Rect(0, 0, Screen.width, Screen.height);
					if (renderer == "UIToolkit")
						DexcRuntimeUIToolkitRenderer.RenderProfileForTests(
							Screen.width,
							Screen.height,
							safe,
							DexcRuntimeUIProfileKind.Windows
						);
#if DEXC_HAS_UGUI
					else if (renderer == "UGUI")
						DexcRuntimeUGUIRenderer.RenderProfileForTests(Screen.width, Screen.height, safe, DexcRuntimeUIProfileKind.Windows);
#endif
					else
						DexcRuntimeUIRenderer.SetProfileOverrideForTests(DexcRuntimeUIProfileKind.Windows);
					for (var frame = 0; frame < 8; frame++)
						yield return null;
					yield return new WaitForEndOfFrame();
					Assert.That(DexcRuntimeUIState.IsOpen && DexcRuntimeUIState.HasDraft, Is.True);
					if (state == "result")
						Assert.That(DexcRuntimeCommandForm.CurrentResult, Is.Not.Null);
					var texture = ScreenCapture.CaptureScreenshotAsTexture();
					try
					{
						var panel = DexcRuntimeUIReferenceResolver
							.Calculate(Screen.width, Screen.height, safe, DexcRuntimeUIProfileKind.Windows)
							.Panel;
						var pixels = texture.GetPixels(
							Mathf.CeilToInt(panel.xMin),
							Mathf.CeilToInt(panel.yMin),
							Mathf.FloorToInt(panel.width) - 1,
							Mathf.FloorToInt(panel.height) - 1
						);
						Assert.That(pixels.Count(pixel => pixel.r > .5f && pixel.g > .5f && pixel.b > .5f), Is.GreaterThan(50));
						Directory.CreateDirectory("Logs/UnityTests/UIR03");
						File.WriteAllBytes("Logs/UnityTests/UIR03/" + renderer + "-" + state + ".png", texture.EncodeToPNG());
					}
					finally
					{
						Object.DestroyImmediate(texture);
					}
				}
			}
		}
	}

	public enum DexcUIR03Mode
	{
		First,
		Second,
	}

	[DebugCommandAttribute("Tests/UIR03/Form", Access = DebugCommandAccess.ReadOnly)]
	public sealed class DexcUIR03Command : DebugCommand
	{
		internal static int Executions;
		public InputInt Amount = new InputInt(7);
		public InputInt Mirror = new InputInt(14);
		public InputString Text = new InputString("Sample");
		public InputRef<GameObject> Target = InputRef.Create(() =>
			new[]
			{
				new InputChoiceEntry<GameObject>("first", DexcCommandFormTests.First, "First", "Scene/First"),
				new InputChoiceEntry<GameObject>("second", DexcCommandFormTests.Second, "Second", "Scene/Second"),
			}
		);
		public InputEnum<DexcUIR03Mode> Mode = new InputEnum<DexcUIR03Mode>(DexcUIR03Mode.First);
		public InputChoice<int> Choice = new InputChoice<int>(
			Enumerable.Range(0, 20).Select(index => new InputChoiceEntry<int>("key" + index, index, "Choice " + index))
		);

		protected override void Execute()
		{
			Executions++;
		}

		protected override void OnInputsChanged(DebugInputChanges changes)
		{
			Mirror.Value = Amount.Value * 2;
		}
	}

	[DebugCommandAttribute("Tests/UIR03/Async", Access = DebugCommandAccess.ReadOnly)]
	public sealed class DexcUIR03Async : AsyncDebugCommand
	{
		internal static TaskCompletionSource<bool> Completion;
		public InputInt Amount = new InputInt(7);

		protected override async ValueTask ExecuteAsync(CancellationToken cancellationToken)
		{
			await Completion.Task;
		}
	}
}
#endif
