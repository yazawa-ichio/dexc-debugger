#if DEXC_DEBUGGER
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Dexc;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace DexcDebugger.Tests
{
	public sealed class DexcEditorWorkspaceTests
	{
		private DexcDebuggerWindow m_Window;
		private readonly Dictionary<string, Rect> m_Controls = new Dictionary<string, Rect>();
		private string m_Focus;
		private string m_ObservedFocus;
		private GameObject m_Target;

		[UnitySetUp]
		public IEnumerator Setup()
		{
			yield return new EnterPlayMode();
			m_Controls.Clear();
			m_Focus = null;
			Dexc.DexcDebugger.ResetForTests();
			Dexc.DexcDebugger.Initialize();
			m_Window = ScriptableObject.CreateInstance<DexcDebuggerWindow>();
			m_Window.minSize = new Vector2(260, 240);
			m_Window.position = new Rect(80, 80, 1100, 740);
			m_Window.ShowUtility();
			m_Window.position = new Rect(80, 80, 1100, 740);
			m_Window.ProcessEditorUpdateForTests();
			m_Window.CommandNumericInputForTests.ControlDrawnForTests = Observe;
			m_Window.SettingNumericInputForTests.ControlDrawnForTests = Observe;
			yield return Paint();
		}

		[UnityTearDown]
		public IEnumerator Teardown()
		{
			if (m_Window != null)
			{
				m_Window.Close();
			}

			if (m_Target != null)
			{
				Object.DestroyImmediate(m_Target);
			}

			Selection.activeGameObject = null;
			Dexc.DexcDebugger.ResetForTests();
			yield return new ExitPlayMode();
		}

		[UnityTest]
		public IEnumerator SettingClassEditsApplyTogetherAndInvalidRowDoesNotResize()
		{
			var fields = Settings();
			m_Window.ShowSettingDetailForTests(fields[0].Id);
			yield return Paint();
			yield return Paint();
			Assert.That(m_Window.SettingFieldCountForTests, Is.EqualTo(3));
			foreach (var field in fields)
			{
				m_Window.SettingFieldNumericForTests(field.Id).ControlDrawnForTests = Observe;
			}

			yield return Paint();
			var firstRect = m_Controls["setting"];
			yield return Edit("setting", "-");
			Assert.That(m_Window.SettingNumericInputForTests.IsValid, Is.False);
			Assert.That(m_Controls["setting"], Is.EqualTo(firstRect));
			Assert.That(m_Window.InputStyleForTests.alignment, Is.EqualTo(TextAnchor.MiddleLeft));
			yield return Edit("setting/" + fields[1].Id, "8");
			Click(m_Window.SettingApplyRectForTests);
			Assert.That(Read(fields[1].Id), Is.EqualTo(DebugValue.From(2)));
			yield return Edit("setting", "7");
			Click(m_Window.SettingApplyRectForTests);
			yield return Paint();
			Assert.That(Read(fields[0].Id), Is.EqualTo(DebugValue.From(7)));
			Assert.That(Read(fields[1].Id), Is.EqualTo(DebugValue.From(8)));
			yield return Edit("setting", "9");
			m_Window.SelectBottomPanelForTests(0);
			m_Window.SelectWorkspacePartForTests(2);
			m_Window.position = new Rect(80, 80, 320, 600);
			yield return Paint();
			m_Window.SelectWorkspacePartForTests(1);
			yield return Paint();
			Assert.That(m_Window.SettingFieldValueForTests(fields[0].Id), Is.EqualTo(DebugValue.From(9)));
			m_Window.SelectFeatureForTests("Command");
			Assert.That(m_Window.SettingFieldValueForTests(fields[0].Id), Is.EqualTo(DebugValue.From(7)));
		}

		[UnityTest]
		public IEnumerator NarrowPanelsAndResizeKeepCommandDraftAndOwnScroll()
		{
			var command = DebugCommand
				.Registry.GetList(new DebugListQuery("Tests/ZZFIX03B", "", new DebugPageRequest(0, 8)))
				.Items.Single();
			DebugCommand.Registry.TryGetDescriptor(command.Id, out var descriptor);
			var input = descriptor.Fields.Single(field => field.Name == "Unsigned");
			m_Window.SelectCommandForTests(command.Id);
			m_Window.UpdateCommandInputForTests(input.Id, DebugValue.From(ulong.MaxValue));
			var draft = m_Window.CommandDraftForTests;
			foreach (var width in new[] { 1100, 760, 320, 260, 1100 })
			{
				m_Window.position = new Rect(80, 80, width, 740);
				m_Window.SelectWorkspacePartForTests(1);
				yield return Paint();
				yield return Paint();
				Assert.That(m_Window.CommandDraftForTests, Is.EqualTo(draft));
				Assert.That(m_Window.CommandValueForTests(input.Id), Is.EqualTo(DebugValue.From(ulong.MaxValue)));
				var rect = m_Controls[input.Id.ToString()];
				Assert.That(rect.width, Is.GreaterThan(40));
				Assert.That(rect.xMin, Is.GreaterThanOrEqualTo(0));
				Assert.That(rect.xMax, Is.LessThanOrEqualTo(width + 1));
				if (width < 700)
				{
					m_Window.SelectWorkspacePartForTests(2);
					for (var panel = 0; panel < 4; panel++)
					{
						m_Window.SelectBottomPanelForTests(panel);
						yield return Paint();
					}

					Assert.That(m_Window.CommandDraftForTests, Is.EqualTo(draft));
				}
			}
		}

		[UnityTest]
		public IEnumerator LowHeightCommandReachesLastFieldAndPreservesScrollAcrossPanels()
		{
			var command = DebugCommand
				.Registry.GetList(new DebugListQuery("Tests/ZZFIX03B", "", new DebugPageRequest(0, 8)))
				.Items.Single();
			DebugCommand.Registry.TryGetDescriptor(command.Id, out var descriptor);
			m_Window.SelectCommandForTests(command.Id);
			m_Window.position = new Rect(80, 80, 260, 260);
			m_Window.SelectWorkspacePartForTests(1);
			yield return Paint();
			m_Window.CommandDetailScrollForTests = new Vector2(0, 100000);
			yield return Paint();
			yield return Paint();
			var scroll = m_Window.CommandDetailScrollForTests;
			Assert.That(scroll.y, Is.InRange(1, 99999));
			var last = descriptor.Fields.Last(field => field.IsInput);
			var rect = m_Controls[last.Id.ToString()];
			Assert.That(rect.yMin, Is.GreaterThanOrEqualTo(0));
			Assert.That(rect.yMax, Is.LessThanOrEqualTo(m_Window.CommandPrimaryRectForTests.yMin));
			m_Window.SelectWorkspacePartForTests(2);
			yield return Paint();
			m_Window.SelectWorkspacePartForTests(1);
			yield return Paint();
			Assert.That(m_Window.CommandDetailScrollForTests, Is.EqualTo(scroll));
			Assert.That(m_Controls[last.Id.ToString()], Is.EqualTo(rect));
		}

		[UnityTest]
		public IEnumerator QueryPagesKeepEveryCommandAndResolveSelectionBeyondFirstPage()
		{
			Dexc.DexcDebugger.ResetForTests();
			Dexc.DexcDebugger.Initialize(options =>
			{
				options.Query.MaxPageSize = 2;
				options.Query.MaxHierarchyChildren = 2;
			});
			m_Window.ProcessEditorUpdateForTests();
			m_Window.RefreshCommandPageForTests();
			var visited = new HashSet<DebugCommandId>();
			do
			{
				Assert.That(m_Window.CommandItemsForTests.Count, Is.LessThanOrEqualTo(2));
				foreach (var item in m_Window.CommandItemsForTests)
				{
					Assert.That(visited.Add(item.Id), Is.True);
				}
			} while (m_Window.NextCommandPageForTests());
			var total = DebugCommand.Registry.GetList(new DebugListQuery("", "", new DebugPageRequest(0, 2))).Page.TotalCount;
			Assert.That(visited.Count, Is.EqualTo(total));
			m_Target = new GameObject("UIR06 parent");
			GameObject last = null;
			for (var index = 0; index < 7; index++)
			{
				last = new GameObject("UIR06 child " + index);
				last.transform.SetParent(m_Target.transform);
			}

			Selection.activeGameObject = last;
			m_Window.NotifyUnitySelectionChangedForTests();
			m_Window.ProcessEditorUpdateForTests();
			Assert.That(m_Window.SelectedTargetIsValidForTests, Is.True);
			yield return Paint();
		}

		[UnityTest]
		public IEnumerator CommandConfirmationRetainsInputsAndHistoryRestoresWithoutExecuting()
		{
			EditorConfirmedCommand.s_Count = 0;
			EditorConfirmedCommand.s_Reject = false;
			EditorConfirmedCommand.s_Reenter = null;
			var command = DebugCommand
				.Registry.GetList(new DebugListQuery("Tests/FIX02E/Sync", "", new DebugPageRequest(0, 8)))
				.Items.Single();
			DebugCommand.Registry.TryGetDescriptor(command.Id, out var descriptor);
			var input = descriptor.Fields.Single(field => field.IsInput);
			m_Window.SelectCommandForTests(command.Id);
			m_Window.position = new Rect(80, 80, 260, 400);
			yield return Paint();
			yield return Paint();
			yield return Edit(input.Id.ToString(), "27");
			Click(m_Window.WorkspaceRectsForTests["Execute"]);
			Assert.That(m_Window.CommandConfirmingForTests, Is.True);
			m_Controls.Clear();
			yield return Paint();
			Assert.That(m_Controls.ContainsKey(input.Id.ToString()), Is.True, "Confirmation must retain the disabled form.");
			Assert.That(EditorConfirmedCommand.s_Count, Is.Zero);
			Click(m_Window.WorkspaceRectsForTests["Confirm"]);
			yield return Paint();
			Assert.That(EditorConfirmedCommand.s_Count, Is.EqualTo(1));
			Assert.That(EditorConfirmedCommand.s_Value, Is.EqualTo(27));
			m_Window.RefreshHistoryForTests();
			var history = DebugCommand.Draft.GetHistory(command.Id, new DebugPageRequest(0, 8)).Entries.Single();
			var draft = m_Window.CommandDraftForTests;
			Assert.That(m_Window.ReuseCommandHistoryForTests(history.Id), Is.True);
			Assert.That(m_Window.CommandDraftForTests, Is.Not.EqualTo(draft));
			Assert.That(m_Window.CommandValueForTests(input.Id), Is.EqualTo(DebugValue.From(27)));
			Assert.That(EditorConfirmedCommand.s_Count, Is.EqualTo(1));
		}

		[UnityTest]
		public IEnumerator InspectorNativeInputsApplyTogetherAndImmediateSettingCommitsOnBlur()
		{
			m_Window.position = new Rect(80, 80, 320, 600);
			m_Target = new GameObject("UIR06 editable object");
			var source = m_Target.AddComponent<EditorWorkspaceInspectable>();
			DebugInspector.Register("ZZUIR06/Editable", m_Target, "Editable object");
			m_Window.SelectFeatureForTests("Inspector");
			Selection.activeGameObject = m_Target;
			m_Window.NotifyUnitySelectionChangedForTests();
			m_Window.ProcessEditorUpdateForTests();
			yield return Paint();
			var component = DebugInspector
				.GetComponents(m_Window.SelectedTargetForTests)
				.Components.Single(item => item.DisplayName.Contains(nameof(EditorWorkspaceInspectable)))
				.Component;
			m_Window.SelectInspectorComponentForTests(component);
			m_Window.NotifyUnitySelectionChangedForTests();
			m_Window.ProcessEditorUpdateForTests();
			Assert.That(
				m_Window.InspectorFieldCountForTests,
				Is.GreaterThanOrEqualTo(2),
				"Repeated notification for the same Unity selection preserves the component."
			);
			var fields = component
				.GetFields(new DebugPageRequest(0, 16))
				.Fields.Where(field => field.Name == "Health" || field.Name == "Speed")
				.ToArray();
			foreach (var field in fields)
			{
				Assert.That(
					component.ReadFields(new[] { field.Id }).Value.Single().Snapshot.State,
					Is.EqualTo(DebugInspectorSnapshotState.Value)
				);
				m_Window.InspectorNumericForTests(field.Id).ControlDrawnForTests = Observe;
			}

			yield return Paint();
			yield return Paint();
			foreach (var field in fields)
			{
				if (!Application.isBatchMode)
				{
					Directory.CreateDirectory("Logs/UnityTests/UIR06");
					yield return Capture("Logs/UnityTests/UIR06", "Inspector-before-" + field.Name);
				}

				Assert.That(
					m_Window.InspectorFieldCountForTests,
					Is.GreaterThanOrEqualTo(2),
					"Inspector form remains selected: " + Selection.activeGameObject + " / " + m_Window.SelectedTargetForTests
				);
				Assert.That(m_Window.InspectorNumericForTests(field.Id).ControlDrawnForTests, Is.Not.Null, "Field model survives repaint.");
				yield return Edit("inspector/" + field.Id, field.Name == "Health" ? "35" : "2.5");
			}

			Assert.That(source.Health, Is.EqualTo(7));
			Assert.That(source.Speed, Is.EqualTo(1));
			Click(m_Window.WorkspaceRectsForTests["Apply"]);
			yield return Paint();
			Assert.That(source.Health, Is.EqualTo(35));
			Assert.That(source.Speed, Is.EqualTo(2.5f));
			var settings = Settings();
			var immediate = settings.Single(field => field.Path.EndsWith("/Speed", StringComparison.Ordinal));
			m_Window.ShowSettingDetailForTests(immediate.Id);
			foreach (var setting in settings)
			{
				m_Window.SettingFieldNumericForTests(setting.Id).ControlDrawnForTests = Observe;
			}

			yield return Paint();
			yield return Paint();
			yield return Edit("setting", "3.5");
			Assert.That(Read(immediate.Id), Is.EqualTo(DebugValue.From(1f)), "Typing does not apply Immediate numbers.");
			yield return Edit("setting/" + settings[0].Id, "4");
			Assert.That(Read(immediate.Id), Is.EqualTo(DebugValue.From(3.5f)), "Leaving the field confirms it once.");
		}

		[UnityTest]
		public IEnumerator PresetFailureRemainsVisibleAndEnumSelectionUsesTheFormDraft()
		{
			var items = DebugSetting.GetList(new DebugListQuery("ZZUIR06/Preset", "", new DebugPageRequest(0, 8))).Items;
			m_Window.ShowSettingDetailForTests(items[0].Id);
			typeof(DexcDebuggerWindow)
				.GetMethod("OpenEditorPresets", BindingFlags.Instance | BindingFlags.NonPublic)
				.Invoke(m_Window, null);
			yield return Paint();
			yield return Paint();
			Click(m_Window.WorkspaceRectsForTests["Fast"]);
			yield return Paint();
			Click(m_Window.WorkspaceRectsForTests["Apply"]);
			yield return Paint();
			var helper = typeof(DexcDebuggerWindow).GetField("m_Helper", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(m_Window);
			Assert.That(helper, Is.Not.Null, "Rejected preset remains in the bounded error page.");
			var message = (string)helper.GetType().GetField("Text", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(helper);
			Assert.That(message, Does.StartWith("Preset apply:"));
			Assert.That(EditorWorkspacePresetSettings.Current.Amount, Is.EqualTo(1));
			Click(m_Window.WorkspaceRectsForTests["Back"]);
			m_Window.ShowSettingDetailForTests(items.Single(item => item.Path.EndsWith("/Mode", StringComparison.Ordinal)).Id);
			yield return Paint();
			yield return Paint();
			Click(m_Controls["setting"]);
			yield return Paint();
			Click(m_Window.WorkspaceRectsForTests["Second"]);
			yield return Paint();
			Assert.That(EditorWorkspacePresetSettings.Current.Mode, Is.EqualTo(EditorWorkspaceMode.Second));
			Assert.That(m_Window.SettingFieldCountForTests, Is.EqualTo(3));
			yield return Paint();
			Assert.That(m_ObservedFocus, Is.EqualTo("DexcEditorNumeric:setting"));
		}

		[UnityTest]
		public IEnumerator CorrectedReferenceSelectionAndClearRemoveStaleValidation()
		{
			m_Target = new GameObject("UIR07 reference");
			EditorWorkspaceReferenceCommand.s_Target = m_Target;
			EditorWorkspaceReferenceCommand.s_RequireNone = false;
			EditorWorkspaceReferenceCommand.s_Executions = 0;
			var command = DebugCommand
				.Registry.GetList(new DebugListQuery("Tests/ZZUIR07/Reference", "", new DebugPageRequest(0, 8)))
				.Items.Single();
			DebugCommand.Registry.TryGetDescriptor(command.Id, out var descriptor);
			var field = descriptor.Fields.Single(item => item.IsInput);
			m_Window.SelectCommandForTests(command.Id);
			yield return Paint();
			var errors = (IDictionary)
				typeof(DexcDebuggerWindow)
					.GetField("m_CommandValidationErrors", BindingFlags.Instance | BindingFlags.NonPublic)
					.GetValue(m_Window);
			foreach (var choice in new[] { "Only target\nUIR07/Target", "None" })
			{
				Click(m_Window.WorkspaceRectsForTests["Execute"]);
				yield return Paint();
				Assert.That(errors.Count, Is.EqualTo(1));
				typeof(DexcDebuggerWindow)
					.GetMethod("OpenCommandReference", BindingFlags.Instance | BindingFlags.NonPublic)
					.Invoke(m_Window, new object[] { field });
				yield return Paint();
				Click(m_Window.WorkspaceRectsForTests[choice]);
				yield return Paint();
				Assert.That(errors.Count, Is.Zero);
				var beforeFrame = Time.frameCount;
				var deadline = EditorApplication.timeSinceStartup + 5;
				while (Time.frameCount == beforeFrame && EditorApplication.timeSinceStartup < deadline)
				{
					yield return null;
				}

				Assert.That(Time.frameCount, Is.GreaterThan(beforeFrame), "Separate executions require separate Unity frames.");
				Click(m_Window.WorkspaceRectsForTests["Execute"]);
				yield return Paint();
				Assert.That(
					errors.Count,
					Is.EqualTo(choice == "None" ? 1 : 0),
					"An unselected reference remains unavailable for execution."
				);
				if (choice == "None")
				{
					Assert.That(errors.Values.Cast<string>().Single(), Does.Contain("selected reference is unavailable"));
				}

				Assert.That(EditorWorkspaceReferenceCommand.s_Executions, Is.EqualTo(1), choice);
				EditorWorkspaceReferenceCommand.s_RequireNone = true;
			}

			Assert.That(EditorWorkspaceReferenceCommand.s_Executions, Is.EqualTo(1));
		}

		[UnityTest]
		public IEnumerator CaptureEditorWorkspaceAtWideAndNarrowSizes()
		{
			if (Application.isBatchMode)
			{
				Assert.Ignore("Capture runs with the explicit --graphics runner.");
			}

			var setting = Settings()[0];
			typeof(DexcDebuggerWindow)
				.GetField("m_SettingSearch", BindingFlags.Instance | BindingFlags.NonPublic)
				.SetValue(m_Window, "ZZUIR06/Gameplay");
			var directory = Path.Combine(Application.dataPath, "../Logs/UnityTests/UIR06");
			Directory.CreateDirectory(directory);
			foreach (var width in new[] { 1100, 320, 260 })
			{
				m_Window.position = new Rect(80, 80, width, 640);
				m_Window.ShowSettingDetailForTests(setting.Id);
				m_Window.SelectWorkspacePartForTests(1);
				yield return Paint();
				yield return Paint();
				yield return Capture(directory, "Editor-Setting-" + width);
				m_Window.SelectWorkspacePartForTests(2);
				m_Window.SelectBottomPanelForTests(2);
				yield return Paint();
				yield return Capture(directory, "Editor-Panels-" + width);
				var command = DebugCommand
					.Registry.GetList(new DebugListQuery("Tests/ZZFIX03B", "", new DebugPageRequest(0, 8)))
					.Items.Single();
				m_Window.SelectCommandForTests(command.Id);
				yield return Paint();
				yield return Paint();
				yield return Capture(directory, "Editor-Command-" + width);
			}
		}

		[UnityTest]
		public IEnumerator CollectionDetailUsesNativeButtonAndBoundedScroll()
		{
			if (Application.isBatchMode)
				Assert.Ignore("Capture runs with the explicit --graphics runner.");
			m_Target = new GameObject("Collection detail");
			m_Target.AddComponent<EditorCollectionInspectable>();
			Selection.activeGameObject = m_Target;
			m_Window.NotifyUnitySelectionChangedForTests();
			m_Window.ProcessEditorUpdateForTests();
			var component = DebugInspector
				.GetComponents(m_Window.SelectedTargetForTests)
				.Components.Single(item => item.TypeName == typeof(EditorCollectionInspectable).FullName)
				.Component;
			var directory = Path.GetFullPath("Logs/UnityTests/UIR05");
			Directory.CreateDirectory(directory);
			foreach (var width in new[] { 1100, 320 })
			{
				m_Window.position = new Rect(80, 80, width, 640);
				m_Window.SelectInspectorComponentForTests(component);
				m_Window.SelectWorkspacePartForTests(1);
				yield return Paint();
				yield return Paint();
				Assert.That(component.GetFields(new DebugPageRequest(0, 20)).Fields.Single().CanWrite, Is.False);
				yield return Capture(directory, "Editor-Collection-" + width);
				Click(m_Window.WorkspaceRectsForTests["…"]);
				yield return Paint();
				var helper = typeof(DexcDebuggerWindow)
					.GetField("m_Helper", BindingFlags.Instance | BindingFlags.NonPublic)
					.GetValue(m_Window);
				Assert.That(helper, Is.Not.Null);
				var text = (string)helper.GetType().GetField("Text", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(helper);
				Assert.That(text, Does.Contain("[127] Value · 127"));
				Assert.That(text, Does.Not.Contain("[128]"));
				var scroll = helper.GetType().GetField("Scroll", BindingFlags.Instance | BindingFlags.NonPublic);
				scroll.SetValue(helper, new Vector2(0, 10000));
				yield return Paint();
				yield return Paint();
				Assert.That(((Vector2)scroll.GetValue(helper)).y, Is.GreaterThan(0).And.LessThan(10000));
				yield return Capture(directory, "Editor-Collection-End-" + width);
				Click(m_Window.WorkspaceRectsForTests["Back"]);
				yield return Paint();
			}
		}

		private IEnumerator Capture(string directory, string name)
		{
			var probe = new VisualElement { pickingMode = PickingMode.Ignore };
			probe.style.position = Position.Absolute;
			probe.style.left = 0;
			probe.style.top = 0;
			probe.style.width = 8;
			probe.style.height = 8;
			probe.style.backgroundColor = Color.magenta;
			m_Window.rootVisualElement.Add(probe);
			m_Window.Focus();
			yield return Paint();
			yield return Paint();
			var marked = CaptureFrame();
			File.WriteAllBytes(Path.Combine(directory, name + "-probe.png"), marked.EncodeToPNG());
			try
			{
				var side = Mathf.RoundToInt(8 * EditorGUIUtility.pixelsPerPoint);
				var markerPixels = marked.GetPixels(0, marked.height - side, side, side);
				// Play Mode tint changes intensity. Identity is the marker's location and magenta chroma.
				Assert.That(
					markerPixels.Count(pixel => pixel.r > 0.5f && pixel.b > 0.5f && pixel.g < 0.1f && Mathf.Abs(pixel.r - pixel.b) < 0.04f),
					Is.GreaterThanOrEqualTo(side * side * 0.7f),
					"Capture must contain this window's marker at its known physical position."
				);
			}
			finally
			{
				Object.DestroyImmediate(marked);
				probe.RemoveFromHierarchy();
			}

			yield return Paint();
			var texture = CaptureFrame();
			try
			{
				Assert.That(
					texture.GetPixels32().Distinct().Count(),
					Is.GreaterThan(30),
					"The window must contain rendered controls and text."
				);
				File.WriteAllBytes(Path.Combine(directory, name + ".png"), texture.EncodeToPNG());
			}
			finally
			{
				Object.DestroyImmediate(texture);
			}
		}

		private Texture2D CaptureFrame()
		{
			var scale = EditorGUIUtility.pixelsPerPoint;
			var width = Mathf.RoundToInt(m_Window.position.width * scale);
			var height = Mathf.RoundToInt(m_Window.position.height * scale);
			Assert.That(width, Is.GreaterThan(0));
			Assert.That(height, Is.GreaterThan(0));
			var render = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
			var upright = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
			var previous = RenderTexture.active;
			Texture2D texture = null;
			try
			{
				// Unity 6.3 CaptureEditorWindow passes logical dimensions to GrabPixels.
				// Request the full physical client surface instead, including at 150% DPI.
				var parent = typeof(EditorWindow).GetField("m_Parent", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(m_Window);
				var viewType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GUIView");
				var grab = viewType.GetMethod("GrabPixels", BindingFlags.Instance | BindingFlags.NonPublic);
				Assert.That(grab, Is.Not.Null);
				grab.Invoke(parent, new object[] { render, new Rect(0, 0, width, height) });
				var orientation = EditorGUIUtility.Load("SceneView/BlitSceneViewCapture.mat") as Material;
				Assert.That(orientation, Is.Not.Null);
				Graphics.Blit(render, upright, orientation);
				RenderTexture.active = upright;
				texture = new Texture2D(width, height, TextureFormat.RGB24, false);
				texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
				texture.Apply();
				return texture;
			}
			catch
			{
				if (texture != null)
				{
					Object.DestroyImmediate(texture);
				}

				throw;
			}
			finally
			{
				RenderTexture.active = previous;
				RenderTexture.ReleaseTemporary(render);
				RenderTexture.ReleaseTemporary(upright);
			}
		}

		[UnityTest]
		public IEnumerator WatchSurvivesSelectionLockAndDestroyedTarget()
		{
			m_Target = new GameObject("UIR06 watched object");
			m_Target.AddComponent<EditorWorkspaceInspectable>();
			Selection.activeGameObject = m_Target;
			m_Window.NotifyUnitySelectionChangedForTests();
			m_Window.ProcessEditorUpdateForTests();
			var target = m_Window.SelectedTargetForTests;
			var component = DebugInspector
				.GetComponents(target)
				.Components.Single(item => item.DisplayName.Contains(nameof(EditorWorkspaceInspectable)))
				.Component;
			m_Window.SelectInspectorComponentForTests(component);
			var field = component.GetFields(new DebugPageRequest(0, 20)).Fields.Single(item => item.Name == "Health");
			m_Window.AddWatchForTests(field.Id);
			m_Window.AddWatchForTests(field.Id);
			Assert.That(DebugInspector.GetWatches(new DebugPageRequest(0, 20)).Entries.Count, Is.EqualTo(1));
			m_Window.SelectionLockedForTests = true;
			Selection.activeGameObject = null;
			m_Window.NotifyUnitySelectionChangedForTests();
			m_Window.ProcessEditorUpdateForTests();
			Assert.That(m_Window.SelectedTargetForTests, Is.EqualTo(target));
			m_Window.SelectionLockedForTests = false;
			m_Window.NotifyUnitySelectionChangedForTests();
			m_Window.ProcessEditorUpdateForTests();
			Assert.That(m_Window.SelectedTargetIsValidForTests, Is.False);
			Object.DestroyImmediate(m_Target);
			yield return new WaitForSecondsRealtime(0.4f);
			var watch = DebugInspector.GetWatches(new DebugPageRequest(0, 20)).Entries.Single();
			Assert.That(watch.State, Is.EqualTo(DebugInspectorWatchState.Missing));
			m_Window.SelectBottomPanelForTests(0);
			yield return Paint();
		}

		private static DebugSettingListItem[] Settings() =>
			DebugSetting.GetList(new DebugListQuery("ZZUIR06/Gameplay", "", new DebugPageRequest(0, 16))).Items.ToArray();

		private static DebugValue Read(DebugSettingId id)
		{
			DebugSetting.Read(id).TryGetValue(out var value);
			return value;
		}

		private IEnumerator Paint()
		{
			m_Window.Repaint();
			yield return null;
		}

		private void Observe(string key, Rect rect)
		{
			m_ObservedFocus = GUI.GetNameOfFocusedControl();
			m_Controls[key] = m_Window.WindowRectForTests(rect);
			if (m_Focus == key)
			{
				GUI.FocusControl("DexcEditorNumeric:" + key);
				m_Focus = null;
			}
		}

		private IEnumerator Edit(string key, string text)
		{
			m_Focus = key;
			yield return Paint();
			yield return Paint();
			Assert.That(
				m_Focus,
				Is.Null,
				"Requested input="
					+ key
					+ "; observed="
					+ string.Join(",", m_Controls.Keys)
					+ "; selected="
					+ m_Window.SelectedTargetForTests
			);
			Click(m_Controls[key]);
			m_Window.SendEvent(Event.KeyboardEvent("^a"));
			m_Window.SendEvent(Event.KeyboardEvent("backspace"));
			foreach (var character in text)
			{
				m_Window.SendEvent(new Event { type = EventType.KeyDown, character = character });
			}

			yield return Paint();
		}

		private void Click(Rect rect)
		{
			m_Window.SendEvent(
				new Event
				{
					type = EventType.MouseDown,
					button = 0,
					mousePosition = rect.center,
				}
			);
			m_Window.SendEvent(
				new Event
				{
					type = EventType.MouseUp,
					button = 0,
					mousePosition = rect.center,
				}
			);
		}
	}

	[DebugCommandAttribute("Tests/ZZUIR07/Reference", Access = DebugCommandAccess.ReadOnly)]
	public sealed class EditorWorkspaceReferenceCommand : DebugCommand
	{
		internal static GameObject s_Target;
		internal static bool s_RequireNone;
		internal static int s_Executions;
		public InputRef<GameObject> Target = InputRef.Create(() =>
			new[] { new InputChoiceEntry<GameObject>("only", s_Target, "Only target", "UIR07/Target") }
		);

		protected override void Validate(DebugCommandValidation validation)
		{
			if (Target.TryGetValue(out _) == s_RequireNone)
			{
				validation.Error(Target, "target-state", "Select the required reference state.");
			}
		}

		protected override void Execute()
		{
			s_Executions++;
		}
	}

	public sealed class EditorWorkspaceSettings : DebugSettingSet<EditorWorkspaceSettings>
	{
		[DebugSetting("ZZUIR06/Gameplay/Amount", ApplyMode = DebugSettingApplyMode.Submit)]
		public int Amount { get; private set; } = 1;

		[DebugSetting("ZZUIR06/Gameplay/Health", ApplyMode = DebugSettingApplyMode.Submit)]
		public int Health { get; private set; } = 2;

		[DebugSetting("ZZUIR06/Gameplay/Speed", ApplyMode = DebugSettingApplyMode.Immediate)]
		public float Speed { get; private set; } = 1;
	}

	public sealed class EditorWorkspacePresetSettings : DebugSettingSet<EditorWorkspacePresetSettings>
	{
		[DebugSetting("ZZUIR06/Preset/Amount", ApplyMode = DebugSettingApplyMode.Submit)]
		public int Amount { get; private set; } = 1;

		[DebugSetting("ZZUIR06/Preset/Read only", Access = DebugSettingAccess.ReadOnly)]
		public int ReadOnly { get; private set; } = 2;

		[DebugSetting("ZZUIR06/Preset/Mode")]
		public EditorWorkspaceMode Mode { get; private set; }

		[DebugPreset("uir06-fast", "Fast")]
		public static EditorWorkspacePresetSettings Fast => new EditorWorkspacePresetSettings { Amount = 4 };
	}

	public enum EditorWorkspaceMode
	{
		First,
		Second,
	}

	public sealed class EditorCollectionInspectable : MonoBehaviour
	{
		[DebugField]
		public int[] Values = Enumerable.Range(0, 129).ToArray();
	}

	public sealed class EditorWorkspaceInspectable : MonoBehaviour
	{
		[DebugField]
		public int Health = 7;

		[DebugField]
		public float Speed = 1;

		[DebugField]
		public int[] Samples = { 10, 20, 30 };
	}
}
#endif
