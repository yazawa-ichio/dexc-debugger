#if DEXC_DEBUGGER
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Dexc;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
#if UNITY_EDITOR
using UnityEditor;
#endif

using DebuggerRuntime = Dexc.DexcDebugger;

namespace DexcDebugger.Tests
{
	internal static class DexcUI06JapaneseValues
	{
		internal const string Path = "表示/日本語連続文字列確認";
		internal const string Description = "空白を含まない日本語の説明文がパネル幅で自然に折り返され最後まで表示されることを確認します";
		internal const string FieldLabel = "保存先シーン参照パス";
		internal const string Value = "シーン/配信地域/日本語ローカライズ確認用の非常に長い参照先";
		internal const string Validation = "選択した参照先は使用できません有効な参照先を選択してから実行してください";
		internal const string DescriptionTail = "最後まで表示されることを確認します";
		internal const string ValidationTail = "選択してから実行してください";

		internal static readonly string[] Canonical = { Path, Description, FieldLabel, Value, Validation };
	}

	[DebugCommandAttribute(
		DexcUI06JapaneseValues.Path,
		Description = DexcUI06JapaneseValues.Description,
		Access = DebugCommandAccess.ReadOnly
	)]
	public sealed class DexcUI06JapaneseCommand : DebugCommand
	{
		public static bool Reject;
		public static string ExecutedValue;
		public InputString 保存先シーン参照パス = new InputString(DexcUI06JapaneseValues.Value);

		protected override void Validate(DebugCommandValidation validation)
		{
			if (Reject)
				validation.Error(保存先シーン参照パス, "ui06-invalid-reference", DexcUI06JapaneseValues.Validation);
		}

		protected override void Execute()
		{
			ExecutedValue = 保存先シーン参照パス.Value;
		}
	}

	public sealed class DexcUI06Inspectable : MonoBehaviour
	{
		public int Health = 7;
	}

	public static class DexcUI06InspectorSource
	{
		public static DexcUI06Inspectable Target;

		[DebugInspectorSource("UI06/Registered")]
		private static IEnumerable<DebugSourceEntry<DexcUI06Inspectable>> Entries()
		{
			if (Target != null)
				yield return new DebugSourceEntry<DexcUI06Inspectable>(Target, "ui06-registered", "target");
		}
	}

	[TestFixture]
	public sealed class DexcUI06CrossRendererTests
	{
		const string PanelSettingsPath = "Packages/jp.yazawa-ichio.dexc-debugger/DebugUI/DexcRuntimeUIToolkitPanelSettings.asset";
		const string WorldShaderPath = "Packages/jp.yazawa-ichio.dexc-debugger/DebugUI/Shaders/WorldVisual.shader";
		const string NotoSansJPPresetPath = "Assets/DexcDebugger/Font/DexcRuntimeUISkin-NotoSansJP.asset";
		const string NotoSansJPPath = "Assets/DexcDebugger/Font/NotoSansJP-Regular.ttf";
		const string BIZUDPGothicPath = "Assets/Tests/Font/BIZ_UDPGothic/BIZUDPGothic-Regular.ttf";
		const string NotoSansPath = "Assets/Tests/Font/Noto_Sans/NotoSans-Regular.ttf";
		static QueuedProvider s_InputProvider;

		[SetUp]
		public void SetUp()
		{
			DexcUI06JapaneseCommand.Reject = false;
			DexcUI06JapaneseCommand.ExecutedValue = string.Empty;
			DexcUI06InspectorSource.Target = null;
			ResetRuntime();
		}

		[TearDown]
		public void TearDown()
		{
			DexcUI06JapaneseCommand.Reject = false;
			DexcUI06InspectorSource.Target = null;
			ResetRuntime();
		}

		[Test]
		public void JapaneseFixtureCanonicalStringsContainNoAsciiSpacesAndMatchProductionDescriptor()
		{
			Assert.That(DexcUI06JapaneseValues.Canonical, Has.All.Not.Contains(" "));
			Initialize(RendererKind.IMGUI, null, 390, 844);
			var item = JapaneseItem();
			Assert.That(item.Path, Is.EqualTo(DexcUI06JapaneseValues.Path));
			Assert.That(item.Description, Is.EqualTo(DexcUI06JapaneseValues.Description));
			Assert.That(DebugCommand.Registry.TryGetDescriptor(item.Id, out var descriptor), Is.True);
			var field = descriptor.Fields.Single(value => value.IsInput);
			Assert.That(field.Label, Is.EqualTo(DexcUI06JapaneseValues.FieldLabel));
			DexcRuntimeUIRenderer.SelectCommandForTests(item);
			Assert.That(
				DexcCommandRuntime.Drafts.GetInputValue(DexcRuntimeUIState.DraftId, field.Id),
				Is.EqualTo(DebugValue.From(DexcUI06JapaneseValues.Value))
			);
			DexcUI06JapaneseCommand.Reject = true;
			Assert.That(DebugCommand.Draft.Validate(DexcRuntimeUIState.DraftId).TryGetValidation(out var validation), Is.True);
			Assert.That(validation.Issues.Single().Message, Is.EqualTo(DexcUI06JapaneseValues.Validation));
		}

		[UnityTest]
		public IEnumerator CommandDraftValidationRequestResultAndHistoryMatchAcrossProductionProviderPaths()
		{
			var skin = CreateSkin();
			var snapshots = new List<CommandSnapshot>();
			try
			{
				foreach (RendererKind renderer in Enum.GetValues(typeof(RendererKind)))
				{
					Initialize(renderer, skin, 390, 844);
					SubmitProvider();
					Refresh(renderer, 390, 844);
					Assert.That(DexcRuntimeUIState.Page, Is.EqualTo(DexcRuntimeUIPage.CommandList), renderer.ToString());
					DexcRuntimeUIState.Search = DexcUI06JapaneseValues.Path;
					Refresh(renderer, 390, 844);
					SelectText(renderer, DexcUI06JapaneseValues.Path + "\n" + DexcUI06JapaneseValues.Description);
					SubmitProvider();
					Refresh(renderer, 390, 844);
					Assert.That(DexcRuntimeUIState.Page, Is.EqualTo(DexcRuntimeUIPage.CommandDetail), renderer.ToString());

					var field = DexcRuntimeUIState.Command.Fields.Single(value => value.IsInput);
					var draftValue = DexcCommandRuntime.Drafts.GetInputValue(DexcRuntimeUIState.DraftId, field.Id);
					DexcUI06JapaneseCommand.Reject = true;
					Refresh(renderer, 390, 844);
					yield return null;
					yield return null;
					Assert.That(DebugCommand.Draft.Validate(DexcRuntimeUIState.DraftId).TryGetValidation(out var invalid), Is.True);
					Assert.That(invalid.CanExecute, Is.False);
					Assert.That(invalid.Issues.Single().Message, Is.EqualTo(DexcUI06JapaneseValues.Validation));
					if (renderer != RendererKind.IMGUI)
					{
						Assert.That(RenderedInputValue(renderer), Is.EqualTo(DexcUI06JapaneseValues.Value));
						var text = string.Join("\n", VisibleTexts(renderer));
						Assert.That(text, Does.Contain(DexcUI06JapaneseValues.Description));
						Assert.That(text, Does.Contain(DexcUI06JapaneseValues.FieldLabel));
						// As in IMGUI, the whole message is written under the field, on the same page.
						Assert.That(text, Does.Contain(DexcUI06JapaneseValues.Validation));
						Assert.That(DexcRuntimeUIState.Page, Is.EqualTo(DexcRuntimeUIPage.CommandDetail));
					}

					DexcUI06JapaneseCommand.Reject = false;
					Refresh(renderer, 390, 844);
					SelectText(renderer, "Execute");
					SubmitProvider();
					Refresh(renderer, 390, 844);
					Assert.That(DexcRuntimeUIState.LastCommandResult, Is.Not.Null, renderer.ToString());
					Assert.That(
						DexcRuntimeUIState.LastCommandResult.Status,
						Is.EqualTo(CommandExecutionStatus.Succeeded),
						renderer.ToString()
					);
					var history = DebugCommand.Draft.GetHistory(DexcRuntimeUIState.CommandId, new DebugPageRequest(0, 8));
					Assert.That(history.Entries, Has.Count.EqualTo(1), renderer.ToString());
					snapshots.Add(
						new CommandSnapshot
						{
							Renderer = renderer.ToString(),
							Path = DexcRuntimeUIState.Command.Path,
							Draft = DexcRuntimeUIRenderer.ValueText(draftValue),
							Validation = DexcUI06JapaneseValues.Validation,
							Result = DexcRuntimeUIState.LastCommandResult.Status.ToString(),
							HistoryCount = history.Entries.Count,
						}
					);
					DexcUI06JapaneseCommand.Reject = false;
					yield return null;
				}
				Assert.That(snapshots.Select(item => item.Signature).Distinct().Count(), Is.EqualTo(1));
			}
			finally
			{
				if (skin != null)
					UnityEngine.Object.DestroyImmediate(skin);
			}
		}

		[UnityTest]
		public IEnumerator SettingInspectorWatchStaleAndDisplayOutcomesMatchAcrossRenderers()
		{
			var skin = CreateSkin();
			var signatures = new List<string>();
			try
			{
				foreach (RendererKind renderer in Enum.GetValues(typeof(RendererKind)))
				{
					Initialize(renderer, skin, 390, 844);
					var setting = DebugSetting
						.GetList(new DebugListQuery(string.Empty, "UI06/SubmitQuality", new DebugPageRequest(0, 8)))
						.Items.Single();
					Assert.That(
						setting.ApplyMode,
						Is.EqualTo(DebugSettingApplyMode.Submit),
						"this scenario explicitly exercises an enabled Apply action, not Immediate mode"
					);
					DebugSetting.Reset(setting.Id);
					DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.SettingList);
					DexcRuntimeUIState.Search = "UI06/SubmitQuality";
					Refresh(renderer, 390, 844);
					SelectText(renderer, DexcRuntimeSettingForm.ReadGroups("UI06/SubmitQuality").Single().Label);
					SubmitProvider();
					Refresh(renderer, 390, 844);
					Assert.That(DexcRuntimeUIState.Page, Is.EqualTo(DexcRuntimeUIPage.SettingDetail));
					yield return null;
					yield return null;
					EditSettingField(renderer, setting.Id, "9");
					Refresh(renderer, 390, 844);
					Assert.That(
						DexcRuntimeSettingForm.Fields.Single(field => field.Item.Id == setting.Id).Dirty,
						Is.True,
						renderer.ToString()
					);
					SelectText(renderer, "Apply");
					SubmitProvider();
					Refresh(renderer, 390, 844);
					Assert.That(DebugSetting.Read(setting.Id).TryGetValue(out var settingValue), Is.True);
					Assert.That(settingValue, Is.EqualTo(DebugValue.From(9)), renderer.ToString());
					Assert.That(
						DexcRuntimeSettingForm.Fields.Single(field => field.Item.Id == setting.Id).Dirty,
						Is.False,
						renderer.ToString()
					);

					var gameObject = new GameObject("UI06Inspectable-" + renderer);
					var inspectable = gameObject.AddComponent<DexcUI06Inspectable>();
					DexcUI06InspectorSource.Target = inspectable;
					var registered = DebugInspector.GetRegistered(
						new DebugListQuery("UI06/Registered", string.Empty, new DebugPageRequest(0, 8))
					);
					Assert.That(registered.Entries, Has.Count.EqualTo(1));
					DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.InspectorRegistered);
					DexcRuntimeInspectorNavigation.ChangeSearch("UI06/Registered");
					Refresh(renderer, 390, 844);
					SelectText(renderer, "ui06-registered");
					SubmitProvider();
					Refresh(renderer, 390, 844);
					Assert.That(DexcRuntimeUIState.Page, Is.EqualTo(DexcRuntimeUIPage.InspectorComponents));

					var components = DebugInspector.GetComponents(DexcRuntimeUIState.InspectorTarget);
					var componentIndex = components.Entries.ToList().FindIndex(item => item.TypeName.Contains(nameof(DexcUI06Inspectable)));
					Assert.That(componentIndex, Is.GreaterThanOrEqualTo(0));
					SelectText(renderer, components.Entries[componentIndex].DisplayName);
					SubmitProvider();
					Refresh(renderer, 390, 844);
					Assert.That(DexcRuntimeUIState.Page, Is.EqualTo(DexcRuntimeUIPage.InspectorFields));
					SelectText(renderer, "Watch");
					SubmitProvider();
					Refresh(renderer, 390, 844);
					Assert.That(DexcRuntimeUIState.Page, Is.EqualTo(DexcRuntimeUIPage.FormDetail));
					SelectText(renderer, "Health");
					SubmitProvider();
					Refresh(renderer, 390, 844);
					var watches = DebugInspector.GetWatches(new DebugPageRequest(0, 8));
					Assert.That(watches.Entries.Count, Is.GreaterThan(0), renderer.ToString());

					DexcRuntimeUIState.InspectorScene = default(DebugSceneHandle);
					DexcRuntimeUIState.InspectorParent = default(DebugObjectHandle);
					DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.InspectorHome);
					Refresh(renderer, 390, 844);
					DexcRuntimeUIState.SetSelectedIndex(1);
					SubmitProvider();
					Refresh(renderer, 390, 844);
					Assert.That(DexcRuntimeUIState.Page, Is.EqualTo(DexcRuntimeUIPage.InspectorScene), renderer + " scene entry");
					var scenePage = DebugInspector.GetScenes();
					var sceneIndex = scenePage.Items.ToList().FindIndex(item => item.Name == SceneManager.GetActiveScene().name);
					Assert.That(sceneIndex, Is.GreaterThanOrEqualTo(0));
					DexcRuntimeUIState.SetSelectedIndex(sceneIndex);
					SubmitProvider();
					Refresh(renderer, 390, 844);
					Assert.That(DexcRuntimeUIState.InspectorScene.IsValid, Is.True, renderer + " scene selection");
					var hierarchy = DebugInspector.GetHierarchyChildren(
						new DebugHierarchyChildrenQuery(
							DexcRuntimeUIState.InspectorScene,
							default(DebugObjectHandle),
							new DebugPageRequest(0, 64)
						)
					);
					var hierarchyIndex = hierarchy.Entries.ToList().FindIndex(item => item.Label == gameObject.name);
					Assert.That(hierarchyIndex, Is.GreaterThanOrEqualTo(0), renderer + " scene hierarchy");
					SelectText(renderer, gameObject.name);
					SubmitProvider();
					Refresh(renderer, 390, 844);
					Assert.That(DexcRuntimeUIState.Page, Is.EqualTo(DexcRuntimeUIPage.InspectorComponents), renderer + " scene target");

					UnityEngine.Object.DestroyImmediate(gameObject);
					DexcUI06InspectorSource.Target = null;
					DexcRuntimeUIRenderer.RefreshInspectorDetailForTests();
					Assert.That(DexcRuntimeUIState.Notice, Does.Contain("no longer available"));

					DebugDisplay.DrawLabel(DebugDrawOptions.Frame("UI06", Color.white), "60");
					DebuggerRuntime.CurrentSession.Display.Tick();
					Assert.That(DexcRuntimeUIRenderer.ReadDisplayFrameForTests(), Is.Not.Null);
					DexcRuntimeUIRenderer.ResetRenderCountersForTests();
					var overlays = DexcRuntimeUIRenderer.RenderDisplayOverlayForTests();
					Assert.That(overlays, Is.GreaterThan(0), renderer.ToString());
					signatures.Add(settingValue + "|" + watches.Entries.Count + "|stale|" + overlays);
					foreach (var watch in watches.Entries)
						DebugInspector.RemoveWatch(watch.WatchId);
					yield return null;
				}
				Assert.That(
					signatures.Select(value => value.Split('|').Take(3).Aggregate((left, right) => left + "|" + right)).Distinct().Count(),
					Is.EqualTo(1)
				);
			}
			finally
			{
				if (skin != null)
					UnityEngine.Object.DestroyImmediate(skin);
			}
		}

		[UnityTest]
		public IEnumerator AsyncRunningAndCancelOutcomesMatchAcrossProductionProviderPaths()
		{
			var skin = CreateSkin();
			var signatures = new List<string>();
			try
			{
				foreach (RendererKind renderer in Enum.GetValues(typeof(RendererKind)))
				{
					Initialize(renderer, skin, 390, 844);
					DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.CommandList);
					DexcRuntimeUIState.Search = "UI02/Cancellable";
					Refresh(renderer, 390, 844);
					var command = DexcRuntimeCommandForm.ReadList().Items.Single();
					SelectText(renderer, command.Path + (string.IsNullOrEmpty(command.Description) ? "" : "\n" + command.Description));
					SubmitProvider();
					Refresh(renderer, 390, 844);
					Assert.That(DexcRuntimeUIState.Page, Is.EqualTo(DexcRuntimeUIPage.CommandDetail), renderer.ToString());
					var commandId = DexcRuntimeUIState.CommandId;
					SelectText(renderer, "Execute");
					SubmitProvider();
					Refresh(renderer, 390, 844);
					Assert.That(DexcRuntimeUIState.AsyncExecution, Is.Not.Null, renderer + " running");
					Assert.That(DexcRuntimeUIState.AsyncTask, Is.Not.Null, renderer + " running task");
					Assert.That(DexcRuntimeUIState.AsyncTask.IsCompleted, Is.False, renderer + " running task");
					SelectText(renderer, "▶ Cancel request");
					SubmitProvider();
					for (
						var frame = 0;
						frame < 60
							&& DexcRuntimeUIState.LastCommandResult == null
							&& DexcRuntimeUIState.AsyncTask != null
							&& !DexcRuntimeUIState.AsyncTask.IsCompleted;
						frame++
					)
						yield return null;
					Refresh(renderer, 390, 844);
					yield return null;
					Refresh(renderer, 390, 844);
					var cancelledStatus =
						DexcRuntimeUIState.LastCommandResult != null
							? DexcRuntimeUIState.LastCommandResult.Status
							: DexcRuntimeUIState.AsyncTask.GetAwaiter().GetResult().Status;
					Assert.That(cancelledStatus, Is.EqualTo(CommandExecutionStatus.Cancelled), renderer + " cancelled result");
					var history = DebugCommand.Draft.GetHistory(commandId, new DebugPageRequest(0, 8));
					Assert.That(history.Entries, Has.Count.EqualTo(1), renderer + " cancel history");
					Assert.That(history.Entries.Single().Status, Is.EqualTo(CommandExecutionStatus.Cancelled), renderer + " cancel result");
					signatures.Add(history.Entries.Single().Status.ToString());
				}
				Assert.That(signatures.Distinct().Count(), Is.EqualTo(1));
			}
			finally
			{
				if (skin != null)
					UnityEngine.Object.DestroyImmediate(skin);
			}
		}

		[UnityTest]
		public IEnumerator ExplicitFontsAreAppliedToEveryRendererAndNegativeNotoSansIsNotClaimedAsJapaneseCoverage()
		{
#if UNITY_EDITOR
			var fonts = new[]
			{
				new { Path = NotoSansJPPath, Expected = true },
				new { Path = BIZUDPGothicPath, Expected = true },
				new { Path = NotoSansPath, Expected = false },
			};
			foreach (var fixture in fonts)
			{
				var font = AssetDatabase.LoadAssetAtPath<Font>(fixture.Path);
				var skin = CreateSkin(font);
				try
				{
					foreach (RendererKind renderer in Enum.GetValues(typeof(RendererKind)))
					{
						Initialize(renderer, skin, 390, 844);
						DexcUI06JapaneseCommand.Reject = true;
						DexcRuntimeUIRenderer.SelectCommandForTests(JapaneseItem());
						Refresh(renderer, 390, 844);
						yield return null;
						yield return null;
						var resolved = ResolvedFont(renderer) ?? (renderer == RendererKind.IMGUI ? skin.Font : null);
						Assert.That(resolved, Is.SameAs(font), fixture.Path + "/" + renderer);
						Assert.That(SourceHasCanonicalGlyphs(resolved), Is.EqualTo(fixture.Expected), fixture.Path + "/" + renderer);
					}
				}
				finally
				{
					UnityEngine.Object.DestroyImmediate(skin);
				}
			}
#else
			yield break;
#endif
		}

		[UnityTest]
		public IEnumerator JapaneseContinuousTextVisualCaptureWritesThreeRenderersByThreeProfiles()
		{
			if (!Environment.GetCommandLineArgs().Any(item => item == "--dexc-ui06-japanese-capture"))
				yield break;
			var manifestPath = Environment.GetEnvironmentVariable("DEXC_UI06_JAPANESE_CAPTURE_MANIFEST");
			Assert.That(manifestPath, Is.Not.Null.And.Not.Empty);
			var outputDirectory = Path.GetDirectoryName(manifestPath);
			Directory.CreateDirectory(outputDirectory);
			var stamp = Environment.GetEnvironmentVariable("DEXC_VISUAL_CAPTURE_STAMP") ?? DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
			var entries = new List<JapaneseCaptureEntry>();
			var scenarios = CreateFontScenarios();
			try
			{
				foreach (var scenario in scenarios)
				{
					foreach (RendererKind renderer in Enum.GetValues(typeof(RendererKind)))
					{
						foreach (var profile in Profiles)
						{
#if UNITY_EDITOR
							var restore = SetRuntimeGameViewSize(profile.Width, profile.Height, scenario.Name + renderer + profile.Name);
#else
							Screen.SetResolution(profile.Width, profile.Height, FullScreenMode.Windowed);
							Action restore = () => { };
#endif
							GameObject cameraObject = null;
							try
							{
								for (
									var attempt = 0;
									(Screen.width != profile.Width || Screen.height != profile.Height) && attempt < 120;
									attempt++
								)
									yield return null;
								Initialize(renderer, scenario.Skin, profile.Width, profile.Height);
								cameraObject = new GameObject(
									"UI06JapaneseCaptureCamera-" + scenario.Name + "-" + renderer + "-" + profile.Name
								);
								var camera = cameraObject.AddComponent<Camera>();
								camera.clearFlags = CameraClearFlags.SolidColor;
								camera.backgroundColor = new Color(0.015f, 0.02f, 0.03f, 1f);
								camera.cullingMask = 0;
								camera.depth = 10000f;
								DexcUI06JapaneseCommand.Reject = true;
								DexcRuntimeUIRenderer.SelectCommandForTests(JapaneseItem());
								Refresh(renderer, profile.Width, profile.Height);
								if (renderer == RendererKind.UIToolkit)
									DexcRuntimeUIToolkitRenderer.ScrollValidationMessageIntoViewForTests();
								Texture2D texture = null;
								yield return CaptureRenderedFrame(renderer, profile.Width, profile.Height, captured => texture = captured);
								Assert.That(texture, Is.Not.Null, scenario.Name + "/" + renderer + "/" + profile.Name);
								AssertCanonicalTextIsBound(renderer, true);
								if (scenario.Font != null)
									Assert.That(ResolvedFont(renderer), Is.SameAs(scenario.Font), scenario.Name + "/" + renderer);
								try
								{
									var bytes = texture.EncodeToPNG();
									var artifact = Path.Combine(
										outputDirectory,
										"UI06-"
											+ scenario.Name
											+ "-"
											+ renderer
											+ "-"
											+ profile.Name
											+ "-JapaneseNoSpaces-"
											+ stamp
											+ ".png"
									);
									File.WriteAllBytes(artifact, bytes);
									var content = TextureRect(
										renderer,
										ContentRect(renderer, profile.Width, profile.Height),
										profile.Height
									);
									var descriptionText = TextRect(renderer, DexcUI06JapaneseValues.DescriptionTail);
									var validationText = TextRect(renderer, DexcUI06JapaneseValues.ValidationTail);
									var descriptionFits = TextFitsVertically(renderer, DexcUI06JapaneseValues.DescriptionTail);
									var validationFits = TextFitsVertically(renderer, DexcUI06JapaneseValues.ValidationTail);
									var description = TailInkRect(texture, descriptionText);
									var validation = TailInkRect(texture, validationText);
									var pathEvidence = renderer == RendererKind.UGUI ? UGUIPathEvidence(texture) : null;
									entries.Add(
										new JapaneseCaptureEntry
										{
											FontScenario = scenario.Name,
											Renderer = renderer.ToString(),
											Profile = profile.Name,
											ArtifactPath = Path.GetFullPath(artifact),
											Sha256 = Sha256(bytes),
											Width = texture.width,
											Height = texture.height,
											Content = new CaptureRect(content),
											DescriptionTextBounds = new CaptureRect(descriptionText),
											ValidationTextBounds = new CaptureRect(validationText),
											DescriptionTailBounds = new CaptureRect(description),
											ValidationTailBounds = new CaptureRect(validation),
											DescriptionTailInk = CountInk(texture, description),
											ValidationTailInk = CountInk(texture, validation),
											DescriptionTextFits = descriptionFits,
											ValidationTextFits = validationFits,
											HorizontalOverflow = HasHorizontalOverflow(renderer),
											ActualTextMatches = CanonicalTextIsBound(renderer, true),
											GlyphAvailability = HasCanonicalGlyphs(renderer, out var fontChain),
											FontChain = fontChain,
											FontAssetPath = scenario.AssetPath,
											FontGuid = scenario.Guid,
											FontSha256 = scenario.Sha256,
											FontFamily = scenario.Family,
											ExpectedGlyphAvailability = scenario.RequiresGlyphs,
											CommandPath = pathEvidence,
										}
									);
								}
								finally
								{
									UnityEngine.Object.DestroyImmediate(texture);
								}
							}
							finally
							{
								if (cameraObject != null)
									UnityEngine.Object.DestroyImmediate(cameraObject);
								restore();
							}
						}
					}
				}
				File.WriteAllText(
					manifestPath,
					JsonUtility.ToJson(
						new JapaneseCaptureManifest
						{
							Version = 4,
							Captures = entries.ToArray(),
							FontEvidence = CreateFontEvidence(),
						},
						true
					)
				);
				Assert.That(entries, Has.Count.EqualTo(36));
				Assert.That(
					entries,
					Has.All.Matches<JapaneseCaptureEntry>(item =>
						item.ActualTextMatches && item.DescriptionTextFits && item.ValidationTextFits && !item.HorizontalOverflow
					)
				);
				Assert.That(
					entries.Where(item => item.ExpectedGlyphAvailability),
					Has.All.Matches<JapaneseCaptureEntry>(item => item.GlyphAvailability)
				);
				Assert.That(
					entries.Where(item => item.Renderer == "UGUI"),
					Has.All.Matches<JapaneseCaptureEntry>(item =>
						item.CommandPath.TextFits && item.CommandPath.GlyphInk.All(ink => ink >= 4)
					)
				);
			}
			finally
			{
				foreach (var scenario in scenarios.Where(item => item.OwnsSkin))
					UnityEngine.Object.DestroyImmediate(scenario.Skin);
			}
		}

		[UnityTest]
		public IEnumerator EditorGameViewNormalAndLowResolutionUseTheSameProfileAndNormalizedLayout()
		{
#if UNITY_EDITOR
			if (!Environment.GetCommandLineArgs().Any(item => item == "--dexc-ui06-low-resolution-capture"))
				yield break;
			var manifestPath = Environment.GetEnvironmentVariable("DEXC_UI06_LOW_RESOLUTION_MANIFEST");
			Assert.That(manifestPath, Is.Not.Null.And.Not.Empty);
			var outputDirectory = Path.GetDirectoryName(manifestPath);
			Directory.CreateDirectory(outputDirectory);
			var stamp = Environment.GetEnvironmentVariable("DEXC_VISUAL_CAPTURE_STAMP") ?? DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
			var skin = CreateSkin();
			var entries = new List<LowResolutionCaptureEntry>();
			var currentProfile = DexcRuntimeUIReferenceResolver.ResolveCurrentProfile(844, 390);
			var fixedReference = DexcRuntimeUIReferenceResolver.ReferenceResolution(currentProfile);
			var referenceWidth = Mathf.RoundToInt(fixedReference.x);
			var referenceHeight = Mathf.RoundToInt(fixedReference.y);
			var scenarios = new[]
			{
				new LowResolutionScenario("Normal", referenceWidth * 2, referenceHeight * 2, false),
				new LowResolutionScenario("LowResolution", referenceWidth, referenceHeight, true),
			};
			try
			{
				foreach (RendererKind renderer in Enum.GetValues(typeof(RendererKind)))
				{
					foreach (var scenario in scenarios)
					{
						var configuration = SetRuntimeGameViewSize(
							scenario.Width,
							scenario.Height,
							"LowResolution-" + renderer + "-" + scenario.Name,
							scenario.LowResolution
						);
						GameObject cameraObject = null;
						try
						{
							for (
								var attempt = 0;
								(Screen.width != scenario.Width || Screen.height != scenario.Height) && attempt < 120;
								attempt++
							)
								yield return null;
							Assert.That(Screen.width, Is.EqualTo(scenario.Width), renderer + "/" + scenario.Name + " width");
							Assert.That(Screen.height, Is.EqualTo(scenario.Height), renderer + "/" + scenario.Name + " height");
							Assert.That(
								configuration.LowResolutionReadback,
								Is.EqualTo(scenario.LowResolution),
								renderer + "/" + scenario.Name + " Low Resolution readback"
							);
							InitializeActual(renderer, skin);
							cameraObject = new GameObject("UI06LowResolutionCamera-" + renderer + "-" + scenario.Name);
							var camera = cameraObject.AddComponent<Camera>();
							camera.clearFlags = CameraClearFlags.SolidColor;
							camera.backgroundColor = new Color(0.015f, 0.02f, 0.03f, 1f);
							camera.cullingMask = 0;
							camera.depth = 10000f;
							Texture2D texture = null;
							yield return CaptureActualFrame(renderer, captured => texture = captured);
							Assert.That(texture, Is.Not.Null);
							try
							{
								var layout = ActualLayout(renderer);
								var input = FixedHeaderInputRect(renderer);
								var bytes = texture.EncodeToPNG();
								var artifact = Path.Combine(
									outputDirectory,
									"UI06-" + renderer + "-" + scenario.Name + "-GameView-" + stamp + ".png"
								);
								File.WriteAllBytes(artifact, bytes);
								SelectProviderIndex(renderer, 0);
								SubmitProvider();
								RefreshActual(renderer);
								var operation = DexcRuntimeUIState.Page == DexcRuntimeUIPage.CommandList;
								entries.Add(
									new LowResolutionCaptureEntry
									{
										Renderer = renderer.ToString(),
										Scenario = scenario.Name,
										RequestedLowResolution = scenario.LowResolution,
										LowResolutionReadback = configuration.LowResolutionReadback,
										GuiPixelsPerPoint = renderer == RendererKind.IMGUI ? DexcRuntimeUIRenderer.GuiPixelsPerPoint : 1f,
										Width = texture.width,
										Height = texture.height,
										SafeArea = new CaptureRect(Screen.safeArea),
										Profile = layout.Profile,
										ReferenceResolution = layout.ReferenceResolution,
										Scale = layout.Scale,
										Panel = new CaptureRect(layout.Panel),
										Header = new CaptureRect(layout.Header),
										Content = new CaptureRect(layout.Content),
										Footer = new CaptureRect(layout.Footer),
										Input = new CaptureRect(input),
										NormalizedPanel = new CaptureRect(Normalize(layout.Panel, layout.Scale)),
										NormalizedHeader = new CaptureRect(Normalize(layout.Header, layout.Scale)),
										NormalizedContent = new CaptureRect(Normalize(layout.Content, layout.Scale)),
										NormalizedFooter = new CaptureRect(Normalize(layout.Footer, layout.Scale)),
										NormalizedInput = new CaptureRect(Normalize(input, layout.Scale)),
										LogicalMinimumHitHeight = layout.LogicalMinimumHitHeight,
										Operation = operation,
										HorizontalOverflow = HasHorizontalOverflow(renderer),
										ArtifactPath = Path.GetFullPath(artifact),
										Sha256 = Sha256(bytes),
									}
								);
							}
							finally
							{
								UnityEngine.Object.DestroyImmediate(texture);
							}
						}
						finally
						{
							if (cameraObject != null)
								UnityEngine.Object.DestroyImmediate(cameraObject);
							configuration.Restore();
						}
					}
				}
				foreach (RendererKind renderer in Enum.GetValues(typeof(RendererKind)))
				{
					var pair = entries.Where(item => item.Renderer == renderer.ToString()).ToArray();
					var matches =
						pair.Length == 2
						&& pair[0].Profile == pair[1].Profile
						&& pair[0].ReferenceResolution == pair[1].ReferenceResolution
						&& SameRect(pair[0].NormalizedPanel, pair[1].NormalizedPanel)
						&& SameRect(pair[0].NormalizedHeader, pair[1].NormalizedHeader)
						&& SameRect(pair[0].NormalizedContent, pair[1].NormalizedContent)
						&& SameRect(pair[0].NormalizedFooter, pair[1].NormalizedFooter)
						&& SameRect(pair[0].NormalizedInput, pair[1].NormalizedInput);
					foreach (var entry in pair)
						entry.NormalizedMatch = matches;
				}
				File.WriteAllText(
					manifestPath,
					JsonUtility.ToJson(new LowResolutionCaptureManifest { Version = 1, Captures = entries.ToArray() }, true)
				);
				Assert.That(entries, Has.Count.EqualTo(6));
				Assert.That(
					entries,
					Has.All.Matches<LowResolutionCaptureEntry>(item =>
						item.LowResolutionReadback == item.RequestedLowResolution
						&& item.NormalizedMatch
						&& item.Operation
						&& !item.HorizontalOverflow
					)
				);
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(skin);
			}
#else
			yield break;
#endif
		}

		private static readonly CaptureProfile[] Profiles =
		{
			new CaptureProfile("MobilePortrait", 390, 844),
			new CaptureProfile("MobileLandscape", 844, 390),
			new CaptureProfile("WindowsPlayer", 1366, 768),
		};

		[UnityTest]
		public IEnumerator FIX06SmallQueryLimitsKeepListsSelectionAndFormsUsable()
		{
			var skin = CreateSkin();
			try
			{
				foreach (var limit in new[] { 1, 2, 10, 24 })
				{
					foreach (RendererKind renderer in Enum.GetValues(typeof(RendererKind)))
					{
						Initialize(renderer, skin, 1100, 800, limit);
						var paths = new List<string>();
						var offset = 0;
						while (true)
						{
							var page = DebugCommand.Registry.GetList(
								new DebugListQuery(string.Empty, "UI06/Manual", DexcRuntimeUIState.CreatePageRequest(offset, 24))
							);
							Assert.That(page.Items.Count, Is.InRange(1, limit));
							paths.AddRange(page.Items.Select(item => item.Path));
							if (!page.Page.HasMore)
							{
								break;
							}

							offset += page.Page.Count;
							Assert.That(offset, Is.LessThan(15));
						}

						Assert.That(paths.Count, Is.EqualTo(15));
						Assert.That(paths.Distinct().Count(), Is.EqualTo(15));
						DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.CommandList);
						DexcRuntimeUIState.Search = "UI06/Manual";
						Refresh(renderer, 1100, 800);
						yield return null;
						yield return null;
						var count = Math.Min(limit, 15);
						Assert.That(
							DexcRuntimeUIRenderer.ProviderSelectionCount(),
							Is.EqualTo(count + (count < 15 ? 1 : 0) + (renderer == RendererKind.IMGUI ? 1 : 3)),
							renderer + " limit=" + limit
						);
						if (renderer == RendererKind.UGUI)
						{
							Assert.That(DexcRuntimeUGUIRenderer.VisibleRowCount, Is.EqualTo(count + 1 + (count < 15 ? 1 : 0)));
						}

						if (renderer == RendererKind.UIToolkit)
						{
							Assert.That(DexcRuntimeUIToolkitRenderer.VisibleRowCount, Is.EqualTo(count + 1 + (count < 15 ? 1 : 0)));
						}

						var selectedItem = DexcRuntimeCommandForm.ReadList().Items[count - 1];
						SelectText(
							renderer,
							selectedItem.Path + (string.IsNullOrEmpty(selectedItem.Description) ? "" : "\n" + selectedItem.Description)
						);
						DexcRuntimeUIRenderer.ActivateProviderSelection();
						Assert.That(DexcRuntimeUIState.Command.Path, Is.EqualTo(paths[count - 1]));
						Refresh(renderer, 1100, 800);
						yield return null;
						Assert.That(DexcRuntimeUIState.Page, Is.EqualTo(DexcRuntimeUIPage.CommandDetail));
						var many = DebugCommand
							.Registry.GetList(
								new DebugListQuery(string.Empty, "UI02/MoreThanPage", DexcRuntimeUIState.CreatePageRequest(0, 24))
							)
							.Items.Single();
						DexcRuntimeUIRenderer.SelectCommandForTests(many);
						Refresh(renderer, 1100, 800);
						yield return null;
						if (renderer == RendererKind.UGUI)
						{
							Assert.That(DexcRuntimeUGUIRenderer.VisibleRowCount, Is.InRange(2, 24));
						}

						if (renderer == RendererKind.UIToolkit)
						{
							Assert.That(DexcRuntimeUIToolkitRenderer.VisibleRowCount, Is.InRange(2, 24));
						}

						if (renderer == RendererKind.UGUI)
						{
							var next = Enumerable
								.Range(0, DexcRuntimeUGUIRenderer.SelectableCount)
								.Select(DexcRuntimeUGUIRenderer.SelectableForTests)
								.First(item =>
									item != null
									&& Convert
										.ToString(DexcUGUIApi.Get(item.transform.Find("Label")?.GetComponent(DexcUGUIApi.TextType), "text"))
										.StartsWith("Next fields", StringComparison.Ordinal)
								);
							DexcUGUIApi.InvokeButton(next);
							Assert.That(DexcRuntimeUGUIRenderer.VisibleRowCount, Is.LessThanOrEqualTo(24));
							Assert.That(
								Enumerable
									.Range(0, DexcRuntimeUGUIRenderer.VisibleRowCount)
									.Select(DexcRuntimeUGUIRenderer.RowTextForTests),
								Has.Some.Contains("Field26")
							);
						}

						if (renderer == RendererKind.UIToolkit)
						{
							var next = Enumerable
								.Range(0, DexcRuntimeUIToolkitRenderer.SelectableCount)
								.First(index =>
									DexcRuntimeUIToolkitRenderer
										.SelectableTextForTests(index)
										.StartsWith("Next fields", StringComparison.Ordinal)
								);
							DexcRuntimeUIToolkitRenderer.SelectForTests(next);
							DexcRuntimeUIToolkitRenderer.ActivateSelectedForTests();
							yield return null;
							Assert.That(DexcRuntimeUIToolkitRenderer.VisibleRowCount, Is.LessThanOrEqualTo(24));
							Assert.That(
								Enumerable
									.Range(0, DexcRuntimeUIToolkitRenderer.VisibleRowCount)
									.Select(DexcRuntimeUIToolkitRenderer.RowTextForTests),
								Has.Some.Contains("Field26")
							);
						}
					}
				}
			}
			finally
			{
				ResetRuntime();
				UnityEngine.Object.DestroyImmediate(skin);
			}
		}

		[UnityTest]
		public IEnumerator FIX06HierarchyUsesTheSmallerIndependentLimit()
		{
			var skin = CreateSkin();
			var scene = SceneManager.CreateScene("FIX06 bounded hierarchy");
			for (var index = 0; index < 3; index++)
				SceneManager.MoveGameObjectToScene(new GameObject("FIX06 root " + index), scene);
			try
			{
				foreach (var limits in new[] { new[] { 24, 1 }, new[] { 1, 24 }, new[] { 10, 2 } })
				foreach (RendererKind renderer in Enum.GetValues(typeof(RendererKind)))
				{
					Initialize(renderer, skin, 1100, 800, limits[0], limits[1]);
					var handle = DebugInspector.GetScenes().Scenes.Single(item => item.Name == scene.name).Scene;
					DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.InspectorScene);
					DexcRuntimeUIState.InspectorScene = handle;
					Refresh(renderer, 1100, 800);
					yield return null;
					yield return null;
					var count = Math.Min(limits[0], limits[1]);
					var rows = DexcRuntimeInspectorNavigation.Rows();
					Assert.That(rows.Count(row => row.Label.StartsWith("FIX06 root ", StringComparison.Ordinal)), Is.EqualTo(count));
					Assert.That(rows.Any(row => row.Key == "next"), Is.True);
					Assert.That(
						DexcRuntimeUIRenderer.ProviderSelectionCount(),
						Is.EqualTo(rows.Count + (renderer == RendererKind.IMGUI ? 0 : 3)),
						renderer.ToString()
					);
					if (renderer == RendererKind.UGUI)
						Assert.That(DexcRuntimeUGUIRenderer.VisibleRowCount, Is.EqualTo(rows.Count + 1));
					if (renderer == RendererKind.UIToolkit)
						Assert.That(DexcRuntimeUIToolkitRenderer.VisibleRowCount, Is.EqualTo(rows.Count + 1));
					var first = DebugInspector.GetHierarchyChildren(
						new DebugHierarchyChildrenQuery(
							handle,
							default(DebugObjectHandle),
							DexcRuntimeUIState.CreatePageRequest(0, 24, true)
						)
					);
					Assert.That(first.Entries.Count, Is.EqualTo(count));
					Assert.That(first.Page.HasMore, Is.True);
					var next = DebugInspector.GetHierarchyChildren(
						new DebugHierarchyChildrenQuery(
							handle,
							default(DebugObjectHandle),
							DexcRuntimeUIState.CreatePageRequest(first.Page.Count, 24, true)
						)
					);
					Assert.That(next.Entries.Select(item => item.Target).Intersect(first.Entries.Select(item => item.Target)), Is.Empty);
				}
			}
			finally
			{
				ResetRuntime();
				foreach (var root in scene.GetRootGameObjects())
					UnityEngine.Object.DestroyImmediate(root);
				SceneManager.UnloadSceneAsync(scene);
				UnityEngine.Object.DestroyImmediate(skin);
			}
		}

		private static void EditSettingField(RendererKind renderer, DebugSettingId id, string text)
		{
			if (renderer == RendererKind.IMGUI)
			{
				DexcRuntimeSettingForm.Change(DexcRuntimeSettingForm.Fields.Single(field => field.Item.Id == id), text);
			}
			else if (renderer == RendererKind.UIToolkit)
			{
				var input = DexcRuntimeUIToolkitRenderer.SettingInputForTests(id);
				Assert.That(input, Is.Not.Null);
				input.value = text;
			}
#if DEXC_HAS_UGUI
			else
			{
				var input = DexcRuntimeUGUIRenderer.SettingInputForTests(id);
				Assert.That(input, Is.Not.Null);
				DexcUGUIApi.Set(input, "text", text);
			}
#endif
		}

		private static int AfterSearch(int index) => index < 0 ? index : index + 1;

		private static void SelectText(RendererKind renderer, string text)
		{
			var index = -1;
			if (renderer == RendererKind.IMGUI)
			{
				// A list's search field is its first item, so its entries are numbered from one.
				if (DexcRuntimeUIState.Page == DexcRuntimeUIPage.CommandList)
					index = AfterSearch(
						DexcRuntimeCommandForm
							.ReadList()
							.Items.ToList()
							.FindIndex(item => text == item.Path + (string.IsNullOrEmpty(item.Description) ? "" : "\n" + item.Description))
					);
				else if (DexcRuntimeUIState.Page == DexcRuntimeUIPage.CommandDetail && (text == "Execute" || text == "▶ Cancel request"))
					index = DexcRuntimeUIRenderer.DetailRowCountForTests;
				else if (DexcRuntimeUIState.Page == DexcRuntimeUIPage.SettingList)
					index = AfterSearch(
						DexcRuntimeSettingForm
							.ReadGroups(DexcRuntimeUIState.Search)
							.Skip(DexcRuntimeSettingForm.ListOffset)
							.ToList()
							.FindIndex(group => group.Label == text)
					);
				else if (DexcRuntimeUIState.Page == DexcRuntimeUIPage.SettingDetail && text == "Apply")
					index = DexcRuntimeUIRenderer.DetailRowCountForTests;
				else if (DexcRuntimeUIState.Page == DexcRuntimeUIPage.InspectorFields && text == "Watch")
					index = DexcRuntimeUIRenderer.DetailRowCountForTests + 1;
				else if (DexcRuntimeUIState.Page == DexcRuntimeUIPage.FormDetail)
					index = DexcRuntimeFormDetail.ReadChoices().Items.ToList().FindIndex(item => item.Label == text);
				else
					index = DexcRuntimeInspectorNavigation.Rows().ToList().FindIndex(row => row.Label == text);
			}
			else
			{
				for (var candidate = 0; candidate < DexcRuntimeUIRenderer.ProviderSelectionCount(); candidate++)
				{
					var label = string.Empty;
					if (renderer == RendererKind.UIToolkit)
						label = DexcRuntimeUIToolkitRenderer.SelectableTextForTests(candidate);
#if DEXC_HAS_UGUI
					else
					{
						var selectable = DexcRuntimeUGUIRenderer.SelectableForTests(candidate);
						var nativeText = selectable == null ? null : selectable.GetComponentInChildren(DexcUGUIApi.TextType);
						if (nativeText != null)
							label = (string)DexcUGUIApi.Get(nativeText, "text");
					}
#endif
					if (label == text)
					{
						index = candidate;
						break;
					}
				}
			}
			Assert.That(index, Is.GreaterThanOrEqualTo(0), renderer + " selectable: " + text);
			DexcRuntimeUIState.SetSelectedIndex(index);
		}

		private static void Initialize(
			RendererKind renderer,
			DexcRuntimeUISkin skin,
			int width,
			int height,
			int maxPageSize = 100,
			int maxHierarchyChildren = 100
		)
		{
			ResetRuntime();
			DebuggerRuntime.Initialize(options =>
			{
				options.RuntimeUI.Skin = skin;
				options.Query.MaxPageSize = maxPageSize;
				options.Query.MaxHierarchyChildren = maxHierarchyChildren;
			});
			var session = DebugCommand
				.Registry.GetList(new DebugListQuery(string.Empty, string.Empty, new DebugPageRequest(0, 1)))
				.Summary.SessionId;
			DexcRuntimeUIState.SetSession(session);
			DexcRuntimeUIRendererSelection.SetUseUGUIForTests(renderer == RendererKind.UGUI);
			DexcRuntimeUIRendererSelection.SetUseUIToolkitForTests(renderer == RendererKind.UIToolkit);
			DexcRuntimeUIState.InputEditorProviderTypes = Array.Empty<Type>();
			DexcRuntimeBootstrap.InitializeForTests();
			s_InputProvider = new QueuedProvider();
			DebugMenu.SetInputProvider(s_InputProvider);
			DebugMenu.Open();
			Refresh(renderer, width, height);
		}

		private static void ResetRuntime()
		{
			if (DexcRuntimeUIState.AsyncExecution != null)
				DexcRuntimeUIState.AsyncExecution.Cancel();
			DebugMenu.Close();
			DexcRuntimeUIRendererSelection.SetUseUGUIForTests(false);
			DexcRuntimeUIRendererSelection.SetUseUIToolkitForTests(false);
			DexcRuntimeUIRenderer.ClearProfileOverrideForTests();
			DexcRuntimeUIToolkitRenderer.ClearProfileOverrideForTests();
			DexcRuntimeUIToolkitRenderer.DestroyForTests();
			DexcRuntimeUGUIRenderer.ClearProfileOverrideForTests();
			DexcRuntimeUIState.ResetForSession();
			DebuggerRuntime.ResetForTests();
			s_InputProvider = null;
		}

		private static void Refresh(RendererKind renderer, int width, int height)
		{
			var safe = new Rect(0f, 0f, width, height);
			if (renderer == RendererKind.IMGUI)
				DexcRuntimeUIRenderer.SetProfileOverrideForTests(DexcRuntimeUIReferenceResolver.LegacyProfileForTests(width, height));
			else if (renderer == RendererKind.UGUI)
			{
				DexcRuntimeUGUIRenderer.RenderProfileForTests(width, height, safe);
				Canvas.ForceUpdateCanvases();
			}
			else if (renderer == RendererKind.UIToolkit)
				DexcRuntimeUIToolkitRenderer.RenderProfileForTests(width, height, safe);
		}

		private static void InitializeActual(RendererKind renderer, DexcRuntimeUISkin skin)
		{
			Initialize(renderer, skin, Screen.width, Screen.height);
			if (renderer == RendererKind.IMGUI)
				DexcRuntimeUIRenderer.ClearProfileOverrideForTests();
			else if (renderer == RendererKind.UGUI)
				DexcRuntimeUGUIRenderer.ClearProfileOverrideForTests();
			else if (renderer == RendererKind.UIToolkit)
				DexcRuntimeUIToolkitRenderer.ClearProfileOverrideForTests();
			RefreshActual(renderer);
		}

		private static void RefreshActual(RendererKind renderer)
		{
			if (renderer == RendererKind.UGUI)
			{
				DexcRuntimeUGUIRenderer.RefreshForTests();
				DexcRuntimeUGUIRenderer.TickActive();
				Canvas.ForceUpdateCanvases();
			}
			else if (renderer == RendererKind.UIToolkit)
			{
				DexcRuntimeUIToolkitRenderer.RefreshForTests();
				DexcRuntimeUIToolkitRenderer.TickActive();
			}
		}

		private static IEnumerator CaptureActualFrame(RendererKind renderer, Action<Texture2D> captured)
		{
			yield return null;
			yield return new WaitForEndOfFrame();
			RefreshActual(renderer);
			yield return null;
			yield return new WaitForEndOfFrame();
			captured(ScreenCapture.CaptureScreenshotAsTexture());
		}

		private static ActualLayoutEvidence ActualLayout(RendererKind renderer)
		{
			if (renderer == RendererKind.UGUI)
			{
				var value = DexcRuntimeUGUIRenderer.CurrentLayoutForTests;
				return new ActualLayoutEvidence(
					value.Profile.ToString(),
					ResolutionText(value.ReferenceResolution),
					value.Scale,
					value.Panel,
					value.Header,
					value.Content,
					value.Footer,
					value.HitHeight
				);
			}
			if (renderer == RendererKind.UIToolkit)
			{
				var value = DexcRuntimeUIToolkitRenderer.CurrentLayoutForTests;
				return new ActualLayoutEvidence(
					value.Profile.ToString(),
					ResolutionText(value.ReferenceResolution),
					value.Scale,
					value.Panel,
					value.Header,
					value.Content,
					value.Footer,
					value.HitHeight
				);
			}
			var profile = DexcRuntimeUIReferenceResolver.ResolveCurrentProfile(Screen.width, Screen.height);
			var layout = DexcRuntimeUIRenderer.CalculateLayoutForProfileTests(
				Screen.width,
				Screen.height,
				Screen.safeArea,
				DexcRuntimeUIPage.Home,
				profile
			);
			return new ActualLayoutEvidence(
				layout.Profile.ToString(),
				ResolutionText(layout.ReferenceResolution),
				layout.Scale,
				BottomLeft(layout.Panel),
				BottomLeft(layout.Header),
				BottomLeft(layout.ContentViewport),
				BottomLeft(layout.Footer),
				layout.RowHeight
			);
		}

		private static Rect FixedHeaderInputRect(RendererKind renderer)
		{
			if (renderer == RendererKind.IMGUI)
				return DexcRuntimeUIRenderer.CloseScreenRectForTests;
			if (renderer == RendererKind.UGUI)
				return DexcRuntimeUGUIRenderer.CloseScreenRectForTests;
			return DexcRuntimeUIToolkitRenderer.CloseScreenRectForTests;
		}

		private static Rect BottomLeft(Rect topLeft)
		{
			return new Rect(topLeft.x, Screen.height - topLeft.yMax, topLeft.width, topLeft.height);
		}

		private static Rect Normalize(Rect value, float scale)
		{
			var divisor = scale > 0f ? scale : 1f;
			return new Rect(value.x / divisor, value.y / divisor, value.width / divisor, value.height / divisor);
		}

		private static string ResolutionText(Vector2 value)
		{
			return Mathf.RoundToInt(value.x) + "x" + Mathf.RoundToInt(value.y);
		}

		private static bool SameRect(CaptureRect left, CaptureRect right)
		{
			return Mathf.Abs(left.X - right.X) <= 0.05f
				&& Mathf.Abs(left.Y - right.Y) <= 0.05f
				&& Mathf.Abs(left.Width - right.Width) <= 0.05f
				&& Mathf.Abs(left.Height - right.Height) <= 0.05f;
		}

		private static IEnumerator CaptureRenderedFrame(RendererKind renderer, int width, int height, Action<Texture2D> captured)
		{
			yield return null;
			yield return new WaitForEndOfFrame();
			Refresh(renderer, width, height);
			if (renderer == RendererKind.UIToolkit)
				DexcRuntimeUIToolkitRenderer.ScrollValidationMessageIntoViewForTests();
			yield return null;
			yield return new WaitForEndOfFrame();
			captured(ScreenCapture.CaptureScreenshotAsTexture());
		}

		private static void SubmitProvider()
		{
			Assert.That(s_InputProvider, Is.Not.Null);
			var reads = s_InputProvider.ReadCount;
			s_InputProvider.Enqueue(new DebugMenuInputFrame(Vector2.zero, true, false));
			DexcRuntimeUIState.InputFrameNumber = -1;
			DexcRuntimeUIState.PollInput();
			Assert.That(s_InputProvider.ReadCount, Is.EqualTo(reads + 1));
			Assert.That(s_InputProvider.NonZeroFrameCount, Is.GreaterThan(0));
			DexcRuntimeUIState.InputActionFrame = -1;
			DexcRuntimeUIRenderer.ProcessProviderInputForTests();
		}

		private static void SelectProviderIndex(RendererKind renderer, int index)
		{
			DexcRuntimeUIState.SetSelectedIndex(index);
			if (renderer == RendererKind.UGUI)
				DexcRuntimeUGUIRenderer.SelectForTests(index);
			else if (renderer == RendererKind.UIToolkit)
				DexcRuntimeUIToolkitRenderer.SelectForTests(index);
		}

		private static DebugCommandListItem JapaneseItem()
		{
			return DebugCommand
				.Registry.GetList(new DebugListQuery(string.Empty, DexcUI06JapaneseValues.Path, new DebugPageRequest(0, 8)))
				.Items.Single();
		}

		[UnityTest]
		public IEnumerator ImmediateSettingCommitsOnProductionFieldConfirmationWithoutApply()
		{
			var skin = CreateSkin();
			var host = new GameObject("UI06F13FieldEventSystem", DexcUGUIApi.EventSystemType);
			var results = new List<string>();
			try
			{
				foreach (var renderer in new[] { RendererKind.UGUI, RendererKind.UIToolkit })
				{
					Initialize(renderer, skin, 390, 844);
					var item = DebugSetting
						.GetList(new DebugListQuery(string.Empty, "Tests/RuntimeUI/Quality", new DebugPageRequest(0, 8)))
						.Items.Single();
					Assert.That(item.ApplyMode, Is.EqualTo(DebugSettingApplyMode.Immediate));
					DebugSetting.Reset(item.Id);
					DexcRuntimeUIRenderer.SelectSettingForTests(item);
					Refresh(renderer, 390, 844);
					yield return null;
					yield return null;
					if (renderer == RendererKind.UGUI)
					{
						var field = Enumerable
							.Range(0, DexcRuntimeUGUIRenderer.SelectableCount)
							.Select(DexcRuntimeUGUIRenderer.SelectableForTests)
							.First(DexcUGUIApi.InputFieldType.IsInstanceOfType);
						DexcUGUIApi.SetSelected(field.gameObject);
						field.GetType().GetMethod("ActivateInputField", Type.EmptyTypes).Invoke(field, null);
						for (var frame = 0; frame < 5 && !Equals(DexcUGUIApi.Get(field, "isFocused"), true); frame++)
							yield return null;
						Assert.That(
							DexcUGUIApi.Get(field, "isFocused"),
							Is.EqualTo(true),
							"host EventSystem must activate the actual field before confirmation"
						);
						Debug.Log(
							"UI06 F13 UGUI activation: focus="
								+ DexcUGUIApi.Get(field, "isFocused")
								+ ", active="
								+ field.gameObject.activeInHierarchy
								+ ", version="
								+ DexcRuntimeUIState.SettingBindingVersion
						);
						var endEvent = DexcUGUIApi.Get(field, "onEndEdit");
						endEvent
							.GetType()
							.GetMethod("AddListener")
							.Invoke(
								endEvent,
								new object[]
								{
									new UnityEngine.Events.UnityAction<string>(text =>
										Debug.Log(
											"UI06 F13 UGUI actual EndEdit: text="
												+ text
												+ ", frame="
												+ Time.frameCount
												+ ", page="
												+ DexcRuntimeUIState.Page
												+ ", version="
												+ DexcRuntimeUIState.SettingBindingVersion
										)
									),
								}
							);
						DexcUGUIApi.Set(field, "text", "9");
						Debug.Log(
							"UI06 F13 UGUI after change: focus="
								+ DexcUGUIApi.Get(field, "isFocused")
								+ ", active="
								+ field.gameObject.activeInHierarchy
								+ ", version="
								+ DexcRuntimeUIState.SettingBindingVersion
						);
						field.GetType().GetMethod("DeactivateInputField", Type.EmptyTypes).Invoke(field, null);
					}
					else
					{
						var field = DexcRuntimeUIToolkitRenderer.FirstTextFieldForTests();
						field.Focus();
						field.value = "9";
						yield return null;
						field.Blur();
					}
					yield return null;
					Assert.That(DebugSetting.Read(item.Id).TryGetValue(out var value), Is.True);
					Debug.Log(
						"UI06 Immediate field confirmation: renderer="
							+ renderer
							+ ", mode="
							+ item.ApplyMode
							+ ", frame="
							+ Time.frameCount
							+ ", edit="
							+ DexcRuntimeSettingForm.Anchor?.Raw
							+ ", dirty="
							+ DexcRuntimeSettingForm.Anchor?.Dirty
							+ ", readback="
							+ DexcRuntimeUIRenderer.ValueText(value)
							+ ", ApplyInvoked=false"
					);
					if (!value.Equals(DebugValue.From(9)))
						results.Add(renderer.ToString());
				}
				Assert.That(results, Is.Empty, "Immediate input confirmation must write without a Submit-mode Apply action");
			}
			finally
			{
				ResetRuntime();
				UnityEngine.Object.DestroyImmediate(host);
				UnityEngine.Object.DestroyImmediate(skin);
			}
		}

		[UnityTest]
		public IEnumerator NumericSettingRawTextAndStructuresUseInvariantGrammar()
		{
			var skin = CreateSkin();
			var host = new GameObject("FIX03C FieldEventSystem", DexcUGUIApi.EventSystemType);
			var previous = System.Globalization.CultureInfo.CurrentCulture;
			try
			{
				foreach (var renderer in new[] { RendererKind.UGUI, RendererKind.UIToolkit })
				{
					Initialize(renderer, skin, 390, 844);
					foreach (var culture in new[] { "ja-JP", "en-US", "de-DE" })
					{
						System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo(culture);
						var item = SettingItem("ZZTests/FIX03C/Amount");
						DebugSetting.Reset(item.Id);
						DexcRuntimeUIRenderer.SelectSettingForTests(item);
						Refresh(renderer, 390, 844);
						yield return null;
						yield return null;
						var history = SettingHistory(item);
						foreach (var invalid in new[] { "", "-", "1e-", "2,5", "1e39" })
						{
							SetSettingField(renderer, SettingField(renderer), invalid);
							Assert.That(DexcRuntimeSettingForm.Anchor.Raw, Is.EqualTo(invalid));
							Assert.That(DexcRuntimeUIRenderer.CanApplySetting(), Is.False, invalid);
							ClickNumericSettingApply(renderer);
							DexcRuntimeUIRenderer.ApplySettingForRenderer();
							Assert.That(SettingHistory(item), Is.EqualTo(history));
						}
						SetSettingField(renderer, SettingField(renderer), "1.");
						Refresh(renderer, 390, 844);
						yield return null;
						yield return null;
						Assert.That(DexcRuntimeSettingForm.Anchor.Raw, Is.EqualTo("1."));
						ClickNumericSettingApply(renderer);
						Assert.That(DebugSetting.Read(item.Id).TryGetValue(out var amount), Is.True);
						Assert.That(amount, Is.EqualTo(DebugValue.From(1f)));
						Assert.That(
							DexcRuntimeSettingForm.Anchor.Raw,
							Is.EqualTo("1"),
							"Successful Apply clears the draft and reads back its value."
						);
						var vector = SettingItem("ZZTests/FIX03C/Vector");
						DexcRuntimeUIRenderer.SelectSettingForTests(vector);
						Refresh(renderer, 390, 844);
						yield return null;
						yield return null;
						SetSettingField(renderer, SettingField(renderer, "x"), "1,25");
						SetSettingField(renderer, SettingField(renderer, "y"), "2,5");
						Assert.That(DexcRuntimeUIRenderer.CanApplySetting(), Is.False);
						SetSettingField(renderer, SettingField(renderer, "x"), "1.25");
						SetSettingField(renderer, SettingField(renderer, "y"), "2.5");
						ClickNumericSettingApply(renderer);
						Assert.That(DebugSetting.Read(vector.Id).TryGetValue(out var value), Is.True);
						Assert.That(DexcRuntimeUIRenderer.ValueText(value), Is.EqualTo("x=1.25, y=2.5"));
						SetSettingField(renderer, SettingField(renderer, "x"), "3");
						SetSettingField(renderer, SettingField(renderer, "y"), "4");
						history = SettingHistory(vector);
						DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.SettingList);
						Assert.That(DexcRuntimeUIRenderer.CanApplySetting(), Is.False);
						DexcRuntimeUIRenderer.ApplySettingForRenderer();
						Assert.That(SettingHistory(vector), Is.EqualTo(history));
						DexcRuntimeUIRenderer.SelectSettingForTests(item);
					}
				}
			}
			finally
			{
				System.Globalization.CultureInfo.CurrentCulture = previous;
				ResetRuntime();
				UnityEngine.Object.DestroyImmediate(host);
				UnityEngine.Object.DestroyImmediate(skin);
			}
		}

		private static void ClickNumericSettingApply(RendererKind renderer)
		{
			if (renderer == RendererKind.UGUI)
			{
				var button = DexcRuntimeUGUIRenderer.FooterForTests.Find("FooterAction1").GetComponent(DexcUGUIApi.ButtonType);
				DexcUGUIApi.InvokeButton(button);
			}
			else
			{
				var button = DexcRuntimeUIToolkitRenderer.SafeAreaRootForTests.Q<Button>("FooterPrimary");
				using (
					var pointer = PointerUpEvent.GetPooled(
						new Event
						{
							type = EventType.MouseUp,
							button = 0,
							mousePosition = button.worldBound.center,
						}
					)
				)
				{
					pointer.target = button;
					button.SendEvent(pointer);
				}
			}
		}

		private static void EditFirstTextField(RendererKind renderer, string value)
		{
			if (renderer == RendererKind.IMGUI)
			{
				DexcRuntimeUIRenderer.SetSettingEditTextForRenderer(value);
				return;
			}
			if (renderer == RendererKind.UGUI)
			{
				var field = Enumerable
					.Range(0, DexcRuntimeUGUIRenderer.SelectableCount)
					.Select(DexcRuntimeUGUIRenderer.SelectableForTests)
					.First(DexcUGUIApi.InputFieldType.IsInstanceOfType);
				DexcUGUIApi.Set(field, "text", value);
				return;
			}
			DexcRuntimeUIToolkitRenderer.FirstTextFieldForTests().value = value;
		}

		[UnityTest]
		public IEnumerator SettingConfirmationRespectsValidationSubmitAndBindingLifetime()
		{
			var skin = CreateSkin();
			var host = new GameObject("UI06F13BindingEventSystem", DexcUGUIApi.EventSystemType);
			try
			{
				foreach (var renderer in new[] { RendererKind.UGUI, RendererKind.UIToolkit })
				{
					Initialize(renderer, skin, 390, 844);
					var item = SettingItem("UI06/ConstrainedQuality");
					DexcRuntimeUIRenderer.SelectSettingForTests(item);
					Refresh(renderer, 390, 844);
					yield return null;
					yield return null;
					var history = SettingHistory(item);
					yield return ConfirmSettingText(renderer, "invalid", () => AssertSettingValue(item, 1));
					AssertSettingValue(item, 1);
					Assert.That(DexcRuntimeSettingForm.Anchor.Dirty, Is.True);
					Assert.That(SettingHistory(item), Is.EqualTo(history));
					yield return null;
					yield return ConfirmSettingText(renderer, "6", () => AssertSettingValue(item, 1));
					AssertSettingValue(item, 6);
					Assert.That(DexcRuntimeSettingForm.Anchor.Dirty, Is.False);
					Assert.That(SettingHistory(item), Is.EqualTo(history + 1));
					yield return null;
					var field = SettingField(renderer);
					SetSettingFieldWithoutNotify(renderer, field, "7");
					EndSettingField(renderer, field);
					EndSettingField(renderer, field);
					AssertSettingValue(item, 6);
					Assert.That(SettingHistory(item), Is.EqualTo(history + 1), "without-notify and duplicate end must not write");
					Refresh(renderer, 390, 844);
					yield return null;
					yield return null;
					yield return ConfirmSettingText(renderer, "99");
					AssertSettingValue(item, 6);
					Assert.That(DexcRuntimeSettingForm.Anchor.Dirty, Is.True);
					Assert.That(DexcRuntimeSettingForm.Anchor.Error, Is.Not.Empty);
					Assert.That(SettingHistory(item), Is.EqualTo(history + 1), "Invalid range must not reach Write.");
					yield return null;
					field = SettingField(renderer);
					SetSettingField(renderer, field, "8");
					var submit = SettingItem("UI06/SubmitQuality");
					DexcRuntimeUIRenderer.SelectSettingForTests(submit);
					Refresh(renderer, 390, 844);
					EndSettingField(renderer, field);
					AssertSettingValue(item, 6);
					AssertSettingValue(submit, 1);
					yield return null;
					yield return null;
					yield return ConfirmSettingText(renderer, "4", () => AssertSettingValue(submit, 1));
					AssertSettingValue(submit, 1);
					Assert.That(DexcRuntimeSettingForm.Anchor.Dirty, Is.True);
					Refresh(renderer, 390, 844);
					// Apply, then Back, Close and the star.
					SelectProviderIndex(renderer, Mathf.Max(0, DexcRuntimeUIRenderer.ProviderSelectionCount() - 4));
					yield return null;
					if (renderer == RendererKind.UGUI)
						DexcRuntimeUGUIRenderer.TickActive();
					else
						DexcRuntimeUIToolkitRenderer.TickActive();
					Assert.That(DexcRuntimeUIState.InputState, Is.EqualTo(DebugMenuInputState.Navigation));
					SubmitProvider();
					AssertSettingValue(submit, 4);
					Assert.That(DexcRuntimeSettingForm.Anchor.Dirty, Is.False);
					var readOnly = SettingItem("UI06/ReadOnlyQuality");
					DexcRuntimeUIRenderer.SelectSettingForTests(readOnly);
					Refresh(renderer, 390, 844);
					yield return null;
					yield return null;
					yield return ConfirmSettingText(renderer, "9");
					AssertSettingValue(readOnly, 2);
					Assert.That(SettingHistory(readOnly), Is.Zero);
					Debug.Log(
						"UI06 F13 "
							+ renderer
							+ ": invalid/valid/constraint/readonly/Submit/duplicate/without-notify/pooled-other-setting assertions passed"
					);
				}
			}
			finally
			{
				ResetRuntime();
				UnityEngine.Object.DestroyImmediate(host);
				UnityEngine.Object.DestroyImmediate(skin);
			}
		}

		private static DebugSettingListItem SettingItem(string path) =>
			DebugSetting.GetList(new DebugListQuery(string.Empty, path, new DebugPageRequest(0, 8))).Items.Single();

		[Test]
		public void SettingBindingRejectsNavigationNewSessionAndExplicitUnbind()
		{
			var skin = CreateSkin();
			try
			{
				Initialize(RendererKind.UGUI, skin, 390, 844);
				var item = SettingItem("UI06/ConstrainedQuality");
				DexcRuntimeUIRenderer.SelectSettingForTests(item);
				var binding = new DexcRuntimeUIRenderer.SettingInputBinding(item);
				binding.Change("5");
				DebugMenu.Close();
				DebugMenu.Open();
				DexcRuntimeUIRenderer.SelectSettingForTests(item);
				Assert.That(binding.Confirm("5"), Is.False);
				AssertSettingValue(item, 1);
				binding = new DexcRuntimeUIRenderer.SettingInputBinding(item);
				binding.Change("6");
				binding.Invalidate();
				Assert.That(binding.Confirm("6"), Is.False);
				AssertSettingValue(item, 1);
				binding = new DexcRuntimeUIRenderer.SettingInputBinding(item);
				binding.Change("7");
				Initialize(RendererKind.UGUI, skin, 390, 844);
				var current = SettingItem("UI06/ConstrainedQuality");
				DexcRuntimeUIRenderer.SelectSettingForTests(current);
				Assert.That(binding.Confirm("7"), Is.False);
				AssertSettingValue(current, 1);
				Assert.That(SettingHistory(current), Is.Zero);
			}
			finally
			{
				ResetRuntime();
				UnityEngine.Object.DestroyImmediate(skin);
			}
		}

		private static int SettingHistory(DebugSettingListItem item) =>
			DebugSetting.GetHistory(item.Id, new DebugPageRequest(0, 20)).Entries.Count;

		private static void AssertSettingValue(DebugSettingListItem item, int expected)
		{
			Assert.That(DebugSetting.Read(item.Id).TryGetValue(out var actual), Is.True);
			Assert.That(actual, Is.EqualTo(DebugValue.From(expected)), item.Path);
		}

		private static object SettingField(RendererKind renderer, string component = null) =>
			renderer == RendererKind.UGUI
				? (object)DexcRuntimeUGUIRenderer.SettingInputForTests(DexcRuntimeUIState.SettingId, component)
				: DexcRuntimeUIToolkitRenderer.SettingInputForTests(DexcRuntimeUIState.SettingId, component);

		private static void SetSettingField(RendererKind renderer, object field, string value)
		{
			if (renderer == RendererKind.UGUI)
				DexcUGUIApi.Set((Component)field, "text", value);
			else
				((TextField)field).value = value;
		}

		private static void SetSettingFieldWithoutNotify(RendererKind renderer, object field, string value)
		{
			if (renderer == RendererKind.UGUI)
				field.GetType().GetMethod("SetTextWithoutNotify").Invoke(field, new object[] { value });
			else
				((TextField)field).SetValueWithoutNotify(value);
		}

		private static void EndSettingField(RendererKind renderer, object field)
		{
			if (renderer == RendererKind.UGUI)
				field.GetType().GetMethod("DeactivateInputField", Type.EmptyTypes).Invoke(field, null);
			else
				((TextField)field).Blur();
		}

		private static IEnumerator ConfirmSettingText(RendererKind renderer, string value, Action beforeConfirm = null)
		{
			var field = SettingField(renderer);
			if (renderer == RendererKind.UGUI)
			{
				DexcUGUIApi.SetSelected(((Component)field).gameObject);
				field.GetType().GetMethod("ActivateInputField", Type.EmptyTypes).Invoke(field, null);
				for (var frame = 0; frame < 5 && !Equals(DexcUGUIApi.Get(field, "isFocused"), true); frame++)
					yield return null;
			}
			else
				((TextField)field).Focus();
			yield return null;
			SetSettingField(renderer, field, value);
			beforeConfirm?.Invoke();
			yield return null;
			EndSettingField(renderer, field);
			yield return null;
		}

		private static void AssertCanonicalTextIsBound(RendererKind renderer, bool requireRenderedIMGUIText)
		{
			var evidence =
				"input="
				+ RenderedInputValue(renderer)
				+ " | text="
				+ (renderer == RendererKind.IMGUI ? string.Empty : string.Join(" | ", VisibleTexts(renderer)));
			Assert.That(
				CanonicalTextIsBound(renderer, requireRenderedIMGUIText),
				Is.True,
				renderer + " did not bind every canonical Japanese string to production UI state. Bound text: " + evidence
			);
		}

		private static bool CanonicalTextIsBound(RendererKind renderer, bool requireRenderedIMGUIText = false)
		{
			if (
				DexcRuntimeUIState.Command == null
				|| DexcRuntimeUIState.Command.Path != DexcUI06JapaneseValues.Path
				|| DexcRuntimeUIState.Command.Description != DexcUI06JapaneseValues.Description
			)
				return false;
			var field = DexcRuntimeUIState.Command.Fields.SingleOrDefault(item => item.IsInput);
			if (field == null || field.Label != DexcUI06JapaneseValues.FieldLabel)
				return false;
			if (
				!DebugCommand.Draft.Validate(DexcRuntimeUIState.DraftId).TryGetValidation(out var validation)
				|| validation.Issues.All(item => item.Message != DexcUI06JapaneseValues.Validation)
			)
				return false;
			if (
				DexcRuntimeUIRenderer.ValueText(DexcCommandRuntime.Drafts.GetInputValue(DexcRuntimeUIState.DraftId, field.Id))
				!= DexcUI06JapaneseValues.Value
			)
				return false;
			if (renderer == RendererKind.IMGUI)
				return !requireRenderedIMGUIText || RenderedInputValue(renderer) == DexcUI06JapaneseValues.Value;
			if (RenderedInputValue(renderer) != DexcUI06JapaneseValues.Value)
				return false;
			var text = string.Join("\n", VisibleTexts(renderer)).Replace("\r", string.Empty);
			var flattened = text.Replace("\n", string.Empty);
			return new[]
			{
				DexcUI06JapaneseValues.Path,
				DexcUI06JapaneseValues.Description,
				DexcUI06JapaneseValues.FieldLabel,
				DexcUI06JapaneseValues.Validation,
			}.All(value => flattened.Contains(value));
		}

		private static string RenderedInputValue(RendererKind renderer)
		{
			if (renderer == RendererKind.IMGUI)
				return DexcRuntimeUIRenderer.CommandInputTextForTests;
			if (renderer == RendererKind.UGUI)
			{
				var input = Enumerable
					.Range(0, DexcRuntimeUGUIRenderer.SelectableCount)
					.Select(DexcRuntimeUGUIRenderer.SelectableForTests)
					.First(DexcUGUIApi.InputFieldType.IsInstanceOfType);
				return Convert.ToString(DexcUGUIApi.Get(input, "text")) ?? string.Empty;
			}
			return DexcRuntimeUIToolkitRenderer.FirstTextFieldForTests().value ?? string.Empty;
		}

		private static IEnumerable<string> VisibleTexts(RendererKind renderer)
		{
			if (renderer == RendererKind.UGUI)
				return DexcRuntimeUGUIRenderer
					.RootForTests.GetComponentsInChildren(DexcUGUIApi.TextType, true)
					.Select(item => Convert.ToString(DexcUGUIApi.Get(item, "text")) ?? string.Empty);
			var root = DexcRuntimeUIToolkitRenderer.DocumentForTests.rootVisualElement;
			return root.Query<Label>()
				.ToList()
				.Select(item => item.text ?? string.Empty)
				.Concat(
					root.Query<TextField>().ToList().Select(item => (item.label ?? string.Empty) + "\n" + (item.value ?? string.Empty))
				);
		}

		private static Rect ContentRect(RendererKind renderer, int width, int height)
		{
			if (renderer == RendererKind.UGUI)
				return DexcRuntimeUGUIRenderer.ContentScreenRectForTests;
			if (renderer == RendererKind.UIToolkit)
				return DexcRuntimeUIToolkitRenderer.ContentScreenRectForTests;
			return DexcRuntimeUIRenderer
				.CalculateLayoutForTests(width, height, new Rect(0f, 0f, width, height), DexcRuntimeUIPage.CommandDetail)
				.ContentViewport;
		}

		private static Rect TextRect(RendererKind renderer, string text)
		{
			if (renderer == RendererKind.IMGUI)
			{
				var rect =
					text == DexcUI06JapaneseValues.ValidationTail
						? default(Rect) // IMGUI draws the message inline and records no rect for it.
						: DexcRuntimeUIRenderer.DescriptionScreenRectForTests;
				return rect;
			}
			if (renderer == RendererKind.UGUI)
			{
				var component = DexcRuntimeUGUIRenderer
					.RootForTests.GetComponentsInChildren(DexcUGUIApi.TextType, true)
					.FirstOrDefault(item =>
						(Convert.ToString(DexcUGUIApi.Get(item, "text")) ?? string.Empty).Replace("\n", string.Empty).Contains(text)
					);
				if (component != null)
					return ScreenRect(component.transform as RectTransform);
			}
			else if (renderer == RendererKind.UIToolkit)
			{
				if (text == DexcUI06JapaneseValues.ValidationTail)
					return DexcRuntimeUIToolkitRenderer.ValidationMessageScreenRectForTests;
				var label = DexcRuntimeUIToolkitRenderer
					.DocumentForTests.rootVisualElement.Query<Label>()
					.ToList()
					.FirstOrDefault(item => (item.text ?? string.Empty).Replace("\n", string.Empty).Contains(text));
				if (label != null && label.panel != null)
					return new Rect(
						label.worldBound.x,
						Screen.height - label.worldBound.yMax,
						label.worldBound.width,
						label.worldBound.height
					);
			}
			return default(Rect);
		}

		private static bool TextFitsVertically(RendererKind renderer, string text)
		{
			if (renderer != RendererKind.UGUI)
				return true;
			var component = DexcRuntimeUGUIRenderer
				.RootForTests.GetComponentsInChildren(DexcUGUIApi.TextType, true)
				.FirstOrDefault(item =>
					(Convert.ToString(DexcUGUIApi.Get(item, "text")) ?? string.Empty).Replace("\n", string.Empty).Contains(text)
				);
			var rect = component == null ? null : component.transform as RectTransform;
			if (rect == null)
				return false;
			var preferredHeight = Convert.ToSingle(DexcUGUIApi.Get(component, "preferredHeight"));
			return preferredHeight <= rect.rect.height + 0.01f;
		}

		private static Rect TailInkRect(Texture2D texture, Rect rect)
		{
			if (texture == null || rect.width <= 0f || rect.height <= 0f)
				return default(Rect);
			var xMin = Mathf.Clamp(Mathf.FloorToInt(rect.xMin) + 5, 0, texture.width - 1);
			var xMax = Mathf.Clamp(Mathf.CeilToInt(rect.xMax) - 5, xMin + 1, texture.width);
			var yMin = Mathf.Clamp(Mathf.FloorToInt(rect.yMin) + 5, 0, texture.height - 1);
			var yMax = Mathf.Clamp(Mathf.CeilToInt(rect.yMax) - 5, yMin + 1, texture.height);
			var pixels = texture.GetPixels32();
			var start = -1;
			var end = -1;
			var gap = 0;
			for (var y = yMin; y < yMax; y++)
			{
				var baseline = pixels[y * texture.width + xMax - 1];
				var ink = 0;
				for (var x = xMin; x < xMax - 2; x++)
				{
					var pixel = pixels[y * texture.width + x];
					if (Math.Abs(pixel.r - baseline.r) > 8 || Math.Abs(pixel.g - baseline.g) > 8 || Math.Abs(pixel.b - baseline.b) > 8)
						ink++;
				}
				if (ink >= 4)
				{
					if (start < 0)
						start = y;
					end = y;
					gap = 0;
				}
				else if (start >= 0 && ++gap > 3)
					break;
			}
			return start < 0
				? default(Rect)
				: new Rect(
					rect.x,
					Mathf.Max(rect.yMin, start - 2),
					rect.width,
					Mathf.Min(rect.yMax, end + 3) - Mathf.Max(rect.yMin, start - 2)
				);
		}

		private static CommandPathEvidence UGUIPathEvidence(Texture2D texture)
		{
			var label = DexcRuntimeUGUITests.CommandBreadcrumbText();
			var bound = Convert.ToString(DexcUGUIApi.Get(label, "text"));
			var generator = (TextGenerator)DexcUGUIApi.Get(label, "cachedTextGenerator");
			var glyphs = DexcRuntimeUGUITests.GeneratedGlyphBounds(label);
			var textBounds = DexcRuntimeUGUITests.TextScreenBounds(label);
			var rowBounds = DexcRuntimeUGUITests.TextScreenBounds(label.transform.parent);
			Assert.That(bound, Is.EqualTo("Home / Command\n" + DexcUI06JapaneseValues.Path));
			Assert.That(generator.lineCount, Is.EqualTo(2));
			Assert.That(
				glyphs.Length,
				Is.EqualTo(bound.Count(character => !char.IsWhiteSpace(character))),
				"F10: binding alone does not prove that the second line generated geometry"
			);
			var pathGlyphs = glyphs.Skip(glyphs.Length - DexcUI06JapaneseValues.Path.Length).ToArray();
			foreach (var glyph in pathGlyphs)
			{
				Assert.That(DexcRuntimeUGUITests.ContainsGlyph(rowBounds, glyph), Is.True);
				Assert.That(DexcRuntimeUGUITests.ContainsGlyph(DexcRuntimeUGUIRenderer.ContentScreenRectForTests, glyph), Is.True);
			}
			return new CommandPathEvidence
			{
				Source = "ProductionText.CanvasRendererMesh",
				BoundText = bound,
				Characters = DexcUI06JapaneseValues.Path,
				TextBounds = new CaptureRect(textBounds),
				RowBounds = new CaptureRect(rowBounds),
				TextFits = TextFitsVertically(RendererKind.UGUI, DexcUI06JapaneseValues.Path),
				PreferredHeight = Convert.ToSingle(DexcUGUIApi.Get(label, "preferredHeight")),
				ActualHeight = ((RectTransform)label.transform).rect.height,
				GeneratedLineCount = generator.lineCount,
				GeneratedGlyphCount = glyphs.Length,
				GlyphBounds = pathGlyphs.Select(glyph => new CaptureRect(glyph)).ToArray(),
				GlyphInk = pathGlyphs.Select(glyph => PathGlyphInk(texture, glyph, textBounds)).ToArray(),
			};
		}

		private static int PathGlyphInk(Texture2D texture, Rect glyph, Rect textBounds)
		{
			var pixels = texture.GetPixels32();
			var gutterX = Mathf.Clamp(Mathf.FloorToInt(textBounds.xMax) - 1, 0, texture.width - 1);
			var count = 0;
			for (var y = Mathf.Max(0, Mathf.FloorToInt(glyph.yMin)); y < Mathf.Min(texture.height, Mathf.CeilToInt(glyph.yMax)); y++)
			{
				var baseline = pixels[y * texture.width + gutterX];
				for (var x = Mathf.Max(0, Mathf.FloorToInt(glyph.xMin)); x < Mathf.Min(texture.width, Mathf.CeilToInt(glyph.xMax)); x++)
				{
					var pixel = pixels[y * texture.width + x];
					if (Math.Abs(pixel.r - baseline.r) > 8 || Math.Abs(pixel.g - baseline.g) > 8 || Math.Abs(pixel.b - baseline.b) > 8)
						count++;
				}
			}
			return count;
		}

		private static Rect TextureRect(RendererKind renderer, Rect rect, int textureHeight)
		{
			return renderer == RendererKind.IMGUI ? new Rect(rect.x, textureHeight - rect.yMax, rect.width, rect.height) : rect;
		}

		private static Rect ScreenRect(RectTransform rect)
		{
			var corners = new Vector3[4];
			rect.GetWorldCorners(corners);
			return new Rect(corners[0].x, corners[0].y, corners[2].x - corners[0].x, corners[2].y - corners[0].y);
		}

		private static int CountInk(Texture2D texture, Rect rect)
		{
			var xMin = Mathf.Clamp(Mathf.FloorToInt(rect.xMin), 0, texture.width - 1);
			var xMax = Mathf.Clamp(Mathf.CeilToInt(rect.xMax), xMin + 1, texture.width);
			var yMin = Mathf.Clamp(Mathf.FloorToInt(rect.yMin), 0, texture.height - 1);
			var yMax = Mathf.Clamp(Mathf.CeilToInt(rect.yMax), yMin + 1, texture.height);
			var pixels = texture.GetPixels32();
			var baseline = pixels[yMin * texture.width + xMin];
			var count = 0;
			for (var y = yMin; y < yMax; y++)
			for (var x = xMin; x < xMax; x++)
			{
				var pixel = pixels[y * texture.width + x];
				if (Math.Abs(pixel.r - baseline.r) > 4 || Math.Abs(pixel.g - baseline.g) > 4 || Math.Abs(pixel.b - baseline.b) > 4)
					count++;
			}
			return count;
		}

		private static bool HasHorizontalOverflow(RendererKind renderer)
		{
			return renderer == RendererKind.UGUI
				? DexcRuntimeUGUIRenderer.HasHorizontalOverflowForTests
				: renderer == RendererKind.UIToolkit && DexcRuntimeUIToolkitRenderer.HasHorizontalOverflowForTests;
		}

		private static bool HasCanonicalGlyphs(RendererKind renderer, out string chain)
		{
			var font = ResolvedFont(renderer);
			chain = font == null ? renderer + ": unresolved/default fallback chain" : renderer + ": " + font.name;
			return SourceHasCanonicalGlyphs(font);
		}

		private static bool SourceHasCanonicalGlyphs(Font font)
		{
			if (font == null || FontEngine.LoadFontFace(font) != FontEngineError.Success)
				return false;
			return DexcUI06JapaneseValues
				.Canonical.SelectMany(value => value)
				.Where(character => !char.IsWhiteSpace(character))
				.Distinct()
				.All(character => FontEngine.TryGetGlyphWithUnicodeValue(character, GlyphLoadFlags.LOAD_DEFAULT, out _));
		}

		private static Font ResolvedFont(RendererKind renderer)
		{
			if (renderer == RendererKind.IMGUI)
				return DexcRuntimeUIRenderer.FontForTests;
			if (renderer == RendererKind.UGUI)
			{
				var text = DexcRuntimeUGUIRenderer
					.RootForTests.GetComponentsInChildren(DexcUGUIApi.TextType, true)
					.FirstOrDefault(item =>
						(Convert.ToString(DexcUGUIApi.Get(item, "text")) ?? string.Empty).Contains(DexcUI06JapaneseValues.Description)
					);
				return text == null ? null : DexcUGUIApi.Get(text, "font") as Font;
			}
			var resolved = (object)DexcRuntimeUIToolkitRenderer.DocumentForTests.rootVisualElement.resolvedStyle;
			return resolved.GetType().GetProperty("unityFont", BindingFlags.Public | BindingFlags.Instance)?.GetValue(resolved, null)
				as Font;
		}

		private static DexcRuntimeUISkin CreateSkin(Font font = null)
		{
			var skin = ScriptableObject.CreateInstance<DexcRuntimeUISkin>();
#if UNITY_EDITOR
			skin.SetWorldVisualShaderForEditor(AssetDatabase.LoadAssetAtPath<Shader>(WorldShaderPath));
			skin.SetUIToolkitPanelSettingsTemplateForEditor(AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath));
			if (font != null)
			{
				var serialized = new SerializedObject(skin);
				serialized.FindProperty("m_Font").objectReferenceValue = font;
				serialized.ApplyModifiedPropertiesWithoutUndo();
			}
#endif
			return skin;
		}

		private static FontScenario[] CreateFontScenarios()
		{
#if UNITY_EDITOR
			var preset = AssetDatabase.LoadAssetAtPath<DexcRuntimeUISkin>(NotoSansJPPresetPath);
			var notoSansJP = AssetDatabase.LoadAssetAtPath<Font>(NotoSansJPPath);
			var bizUDPGothic = AssetDatabase.LoadAssetAtPath<Font>(BIZUDPGothicPath);
			var notoSans = AssetDatabase.LoadAssetAtPath<Font>(NotoSansPath);
			Assert.That(preset, Is.Not.Null);
			Assert.That(notoSansJP, Is.Not.Null);
			Assert.That(bizUDPGothic, Is.Not.Null);
			Assert.That(notoSans, Is.Not.Null);
			return new[]
			{
				new FontScenario(
					"DefaultFallback",
					CreateSkin(),
					null,
					string.Empty,
					string.Empty,
					string.Empty,
					string.Empty,
					false,
					true
				),
				new FontScenario(
					"NotoSansJP",
					preset,
					notoSansJP,
					NotoSansJPPath,
					AssetDatabase.AssetPathToGUID(NotoSansJPPath),
					Sha256(File.ReadAllBytes(NotoSansJPPath)),
					"Noto Sans JP",
					true,
					false
				),
				new FontScenario(
					"BIZUDPGothic",
					CreateSkin(bizUDPGothic),
					bizUDPGothic,
					BIZUDPGothicPath,
					AssetDatabase.AssetPathToGUID(BIZUDPGothicPath),
					Sha256(File.ReadAllBytes(BIZUDPGothicPath)),
					"BIZ UDPGothic",
					true,
					true
				),
				new FontScenario(
					"NotoSansNegative",
					CreateSkin(notoSans),
					notoSans,
					NotoSansPath,
					AssetDatabase.AssetPathToGUID(NotoSansPath),
					Sha256(File.ReadAllBytes(NotoSansPath)),
					"Noto Sans",
					false,
					true
				),
			};
#else
			return new[]
			{
				new FontScenario(
					"DefaultFallback",
					CreateSkin(),
					null,
					string.Empty,
					string.Empty,
					string.Empty,
					string.Empty,
					false,
					true
				),
			};
#endif
		}

		private static FontEvidence[] CreateFontEvidence()
		{
#if UNITY_EDITOR
			return new[]
			{
				FontEvidence.Create("NotoSansJP", NotoSansJPPath, "Noto Sans JP", true),
				FontEvidence.Create("BIZUDPGothic", BIZUDPGothicPath, "BIZ UDPGothic", true),
				FontEvidence.Create("NotoSansNegative", NotoSansPath, "Noto Sans", false),
			};
#else
			return Array.Empty<FontEvidence>();
#endif
		}

#if UNITY_EDITOR
		private static Action SetRuntimeGameViewSize(int width, int height, string profile)
		{
			return SetRuntimeGameViewSizeCore(width, height, profile, null).Restore;
		}

		private static GameViewConfiguration SetRuntimeGameViewSize(int width, int height, string profile, bool lowResolution)
		{
			return SetRuntimeGameViewSizeCore(width, height, profile, lowResolution);
		}

		private static GameViewConfiguration SetRuntimeGameViewSizeCore(int width, int height, string profile, bool? lowResolution)
		{
			var editorAssembly = typeof(Editor).Assembly;
			var gameViewType = editorAssembly.GetType("UnityEditor.GameView", true);
			var gameViewSizesType = editorAssembly.GetType("UnityEditor.GameViewSizes", true);
			var gameViewSizeType = editorAssembly.GetType("UnityEditor.GameViewSize", true);
			var gameViewSizeTypeEnum = editorAssembly.GetType("UnityEditor.GameViewSizeType", true);
			var singletonType = typeof(ScriptableSingleton<>).MakeGenericType(gameViewSizesType);
			var singleton = singletonType.GetProperty("instance", BindingFlags.Public | BindingFlags.Static).GetValue(null, null);
			var groupType = singleton
				.GetType()
				.GetProperty("currentGroupType", BindingFlags.Public | BindingFlags.Instance)
				.GetValue(singleton, null);
			var group = singleton
				.GetType()
				.GetMethod("GetGroup", BindingFlags.Public | BindingFlags.Instance)
				.Invoke(singleton, new[] { groupType });
			var fixedResolution = Enum.Parse(gameViewSizeTypeEnum, "FixedResolution");
			var constructor = gameViewSizeType.GetConstructor(
				BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance,
				null,
				new[] { gameViewSizeTypeEnum, typeof(int), typeof(int), typeof(string) },
				null
			);
			var size = constructor.Invoke(new object[] { fixedResolution, width, height, "Dexc UI06 " + profile });
			group.GetType().GetMethod("AddCustomSize", BindingFlags.Public | BindingFlags.Instance).Invoke(group, new[] { size });
			var index =
				(int)group.GetType().GetMethod("GetTotalCount", BindingFlags.Public | BindingFlags.Instance).Invoke(group, null) - 1;
			var gameView = EditorWindow.GetWindow(gameViewType);
			var selected = gameViewType.GetProperty(
				"selectedSizeIndex",
				BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance
			);
			var previous = (int)selected.GetValue(gameView, null);
			var lowResolutionProperty = gameViewType.GetProperty(
				"lowResolutionForAspectRatios",
				BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance
			);
			var lowResolutionField = gameViewType.GetField(
				"m_LowResolutionForAspectRatios",
				BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance
			);
			Func<bool> readLowResolution = () =>
				lowResolutionProperty != null
					? (bool)lowResolutionProperty.GetValue(gameView, null)
					: lowResolutionField != null && (bool)lowResolutionField.GetValue(gameView);
			var previousLowResolution = readLowResolution();
			selected.SetValue(gameView, index, null);
			if (lowResolution.HasValue)
			{
				if (lowResolutionProperty != null)
					lowResolutionProperty.SetValue(gameView, lowResolution.Value, null);
				else if (lowResolutionField != null)
					lowResolutionField.SetValue(gameView, lowResolution.Value);
				else
					throw new MissingMemberException(gameViewType.FullName, "lowResolutionForAspectRatios");
			}
			gameView.Repaint();
			EditorApplication.QueuePlayerLoopUpdate();
			return new GameViewConfiguration(
				readLowResolution,
				() =>
				{
					selected.SetValue(gameView, previous, null);
					if (lowResolutionProperty != null)
						lowResolutionProperty.SetValue(gameView, previousLowResolution, null);
					else if (lowResolutionField != null)
						lowResolutionField.SetValue(gameView, previousLowResolution);
					group
						.GetType()
						.GetMethod("RemoveCustomSize", BindingFlags.Public | BindingFlags.Instance)
						.Invoke(group, new object[] { index });
					gameView.Repaint();
				}
			);
		}
#endif

		private static string Sha256(byte[] bytes)
		{
			using (var sha = SHA256.Create())
				return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
		}

		private enum RendererKind
		{
			IMGUI,
			UGUI,
			UIToolkit,
		}

		private sealed class QueuedProvider : DebugMenuInputProvider
		{
			readonly Queue<DebugMenuInputFrame> m_Frames = new Queue<DebugMenuInputFrame>();
			internal int ReadCount { get; private set; }
			internal int NonZeroFrameCount { get; private set; }

			internal void Enqueue(DebugMenuInputFrame frame)
			{
				m_Frames.Enqueue(frame);
			}

			public override DebugMenuInputFrame ReadInput()
			{
				ReadCount++;
				var frame = m_Frames.Count == 0 ? new DebugMenuInputFrame(Vector2.zero, false, false) : m_Frames.Dequeue();
				if (frame.Navigation != Vector2.zero || frame.SubmitPressed || frame.CancelPressed)
					NonZeroFrameCount++;
				return frame;
			}
		}

		private sealed class CommandSnapshot
		{
			internal string Renderer;
			internal string Path;
			internal string Draft;
			internal string Validation;
			internal string Result;
			internal int HistoryCount;
			internal string Signature => Path + "|" + Draft + "|" + Validation + "|" + Result + "|" + HistoryCount;
		}

		private readonly struct CaptureProfile
		{
			internal CaptureProfile(string name, int width, int height)
			{
				Name = name;
				Width = width;
				Height = height;
			}

			internal string Name { get; }
			internal int Width { get; }
			internal int Height { get; }
		}

		private readonly struct LowResolutionScenario
		{
			internal LowResolutionScenario(string name, int width, int height, bool lowResolution)
			{
				Name = name;
				Width = width;
				Height = height;
				LowResolution = lowResolution;
			}

			internal string Name { get; }
			internal int Width { get; }
			internal int Height { get; }
			internal bool LowResolution { get; }
		}

		private readonly struct ActualLayoutEvidence
		{
			internal ActualLayoutEvidence(
				string profile,
				string referenceResolution,
				float scale,
				Rect panel,
				Rect header,
				Rect content,
				Rect footer,
				float logicalMinimumHitHeight
			)
			{
				Profile = profile;
				ReferenceResolution = referenceResolution;
				Scale = scale;
				Panel = panel;
				Header = header;
				Content = content;
				Footer = footer;
				LogicalMinimumHitHeight = logicalMinimumHitHeight;
			}

			internal string Profile { get; }
			internal string ReferenceResolution { get; }
			internal float Scale { get; }
			internal Rect Panel { get; }
			internal Rect Header { get; }
			internal Rect Content { get; }
			internal Rect Footer { get; }
			internal float LogicalMinimumHitHeight { get; }
		}

#if UNITY_EDITOR
		private sealed class GameViewConfiguration
		{
			readonly Func<bool> m_ReadLowResolution;
			readonly Action m_Restore;

			internal GameViewConfiguration(Func<bool> readLowResolution, Action restore)
			{
				m_ReadLowResolution = readLowResolution;
				m_Restore = restore;
			}

			internal bool LowResolutionReadback => m_ReadLowResolution();

			internal void Restore()
			{
				m_Restore();
			}
		}
#endif

		private sealed class FontScenario
		{
			internal FontScenario(
				string name,
				DexcRuntimeUISkin skin,
				Font font,
				string assetPath,
				string guid,
				string sha256,
				string family,
				bool requiresGlyphs,
				bool ownsSkin
			)
			{
				Name = name;
				Skin = skin;
				Font = font;
				AssetPath = assetPath;
				Guid = guid;
				Sha256 = sha256;
				Family = family;
				RequiresGlyphs = requiresGlyphs;
				OwnsSkin = ownsSkin;
			}

			internal string Name { get; }
			internal DexcRuntimeUISkin Skin { get; }
			internal Font Font { get; }
			internal string AssetPath { get; }
			internal string Guid { get; }
			internal string Sha256 { get; }
			internal string Family { get; }
			internal bool RequiresGlyphs { get; }
			internal bool OwnsSkin { get; }
		}

		[Serializable]
		private sealed class JapaneseCaptureManifest
		{
			public int Version;
			public JapaneseCaptureEntry[] Captures;
			public FontEvidence[] FontEvidence;
		}

		[Serializable]
		private sealed class LowResolutionCaptureManifest
		{
			public int Version;
			public LowResolutionCaptureEntry[] Captures;
		}

		[Serializable]
		private sealed class LowResolutionCaptureEntry
		{
			public string Renderer;
			public string Scenario;
			public bool RequestedLowResolution;
			public bool LowResolutionReadback;
			public float GuiPixelsPerPoint;
			public int Width;
			public int Height;
			public CaptureRect SafeArea;
			public string Profile;
			public string ReferenceResolution;
			public float Scale;
			public CaptureRect Panel;
			public CaptureRect Header;
			public CaptureRect Content;
			public CaptureRect Footer;
			public CaptureRect Input;
			public CaptureRect NormalizedPanel;
			public CaptureRect NormalizedHeader;
			public CaptureRect NormalizedContent;
			public CaptureRect NormalizedFooter;
			public CaptureRect NormalizedInput;
			public float LogicalMinimumHitHeight;
			public bool NormalizedMatch;
			public bool Operation;
			public bool HorizontalOverflow;
			public string ArtifactPath;
			public string Sha256;
		}

		[Serializable]
		private sealed class FontEvidence
		{
			public string Scenario;
			public string AssetPath;
			public string Guid;
			public string Sha256;
			public string Family;
			public bool ExpectedCanonicalGlyphs;

#if UNITY_EDITOR
			internal static FontEvidence Create(string scenario, string path, string family, bool expected)
			{
				return new FontEvidence
				{
					Scenario = scenario,
					AssetPath = path,
					Guid = AssetDatabase.AssetPathToGUID(path),
					Sha256 = DexcUI06CrossRendererTests.Sha256(File.ReadAllBytes(path)),
					Family = family,
					ExpectedCanonicalGlyphs = expected,
				};
			}
#endif
		}

		[Serializable]
		private sealed class JapaneseCaptureEntry
		{
			public string FontScenario;
			public string Renderer;
			public string Profile;
			public string ArtifactPath;
			public string Sha256;
			public int Width;
			public int Height;
			public CaptureRect Content;
			public CaptureRect DescriptionTextBounds;
			public CaptureRect ValidationTextBounds;
			public CaptureRect DescriptionTailBounds;
			public CaptureRect ValidationTailBounds;
			public int DescriptionTailInk;
			public int ValidationTailInk;
			public bool DescriptionTextFits;
			public bool ValidationTextFits;
			public bool HorizontalOverflow;
			public bool ActualTextMatches;
			public bool GlyphAvailability;
			public string FontChain;
			public string FontAssetPath;
			public string FontGuid;
			public string FontSha256;
			public string FontFamily;
			public bool ExpectedGlyphAvailability;
			public CommandPathEvidence CommandPath;
		}

		[Serializable]
		private sealed class CommandPathEvidence
		{
			public string Source;
			public string BoundText;
			public string Characters;
			public CaptureRect TextBounds;
			public CaptureRect RowBounds;
			public bool TextFits;
			public float PreferredHeight;
			public float ActualHeight;
			public int GeneratedLineCount;
			public int GeneratedGlyphCount;
			public CaptureRect[] GlyphBounds;
			public int[] GlyphInk;
		}

		public sealed class FIX03NumericSettings : DebugSettingSet<FIX03NumericSettings>
		{
			[DebugSetting("ZZTests/FIX03C/Amount", ApplyMode = DebugSettingApplyMode.Submit)]
			public float Amount { get; private set; } = 2.5f;

			[DebugSetting("ZZTests/FIX03C/Vector", ApplyMode = DebugSettingApplyMode.Submit)]
			public Vector2 Direction { get; private set; } = Vector2.zero;
		}

		public sealed class UI06SubmitSettings : DebugSettingSet<UI06SubmitSettings>
		{
			[DebugSetting("UI06/SubmitQuality", ApplyMode = DebugSettingApplyMode.Submit)]
			public int Quality { get; private set; } = 1;

			[DebugSetting("UI06/ConstrainedQuality"), DebugRange(0d, 10d)]
			public int ConstrainedQuality { get; private set; } = 1;

			[DebugSetting("UI06/ReadOnlyQuality", Access = DebugSettingAccess.ReadOnly)]
			public int ReadOnlyQuality { get; private set; } = 2;
		}

		[Serializable]
		private struct CaptureRect
		{
			internal CaptureRect(Rect rect)
			{
				X = rect.x;
				Y = rect.y;
				Width = rect.width;
				Height = rect.height;
			}

			public float X;
			public float Y;
			public float Width;
			public float Height;
		}
	}
}
#endif
