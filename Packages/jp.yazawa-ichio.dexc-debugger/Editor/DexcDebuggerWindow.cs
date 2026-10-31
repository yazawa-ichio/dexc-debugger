using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dexc;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Dexc
{
	/// <summary>IMGUI editor surface for the shared Dexc read APIs and display snapshot.</summary>
	public sealed partial class DexcDebuggerWindow : EditorWindow
	{
		private enum Page
		{
			Inspector,
			Commands,
			CommandDetail,
			Settings,
			SettingDetail,
			History,
			Display,
			Panels,
			Diagnostics,
		}

		private enum InspectorSource
		{
			Registered,
			Scene,
		}

		private const int PageSize = 128;
		private const int FeaturePageSize = 32;
		private const int MaxSceneRows = 256;
		private const int MaxFieldRows = 128;
		private const int MaxDiagnosticRows = 64;

		// Open windows are owned by the Editor, not by a Play Mode session, so this set deliberately outlives DexcRoot.
		private object m_SubscribedRoot;

		private static int BoundedPageSize(int requested)
		{
			var session = DexcDebugger.CurrentSession;
			return session == null ? requested : Math.Min(requested, session.Options.MaxPageSize);
		}

		private static int BoundedHierarchyPageSize(int requested)
		{
			var session = DexcDebugger.CurrentSession;
			return session == null
				? requested
				: Math.Min(requested, Math.Min(session.Options.MaxPageSize, session.Options.MaxHierarchyChildren));
		}

		private Page m_Page;
		private InspectorSource m_InspectorSource;
		private Vector2 m_LeftScroll;
		private Vector2 m_MiddleScroll;
		private Vector2 m_RightScroll;
		private Vector2 m_DisplayScroll;
		private Vector2 m_DiagnosticScroll;
		private Vector2 m_CommandScroll;
		private readonly DexcEditorNumericInput m_CommandNumericInput = new DexcEditorNumericInput();
		private readonly DexcEditorNumericInput m_SettingNumericInput = new DexcEditorNumericInput();
		private readonly HashSet<DebugCommandFieldId> m_CommandRejectedInput = new HashSet<DebugCommandFieldId>();
		private readonly Dictionary<DebugCommandFieldId, string> m_CommandValidationErrors = new Dictionary<DebugCommandFieldId, string>();
#if UNITY_INCLUDE_TESTS
		internal DexcEditorNumericInput CommandNumericInputForTests => m_CommandNumericInput;
		internal DexcEditorNumericInput SettingNumericInputForTests => m_SettingNumericInput;
		internal DebugCommandDraftId CommandDraftForTests => m_SelectedDraft;
		internal DebugValue SettingEditedValueForTests => m_SelectedSettingValue;
		internal Rect SettingApplyRectForTests { get; private set; }
		private Vector2 m_RootMouseForTests;
		private Matrix4x4 m_RootMatrixForTests;

		internal Rect WindowRectForTests(Rect rect)
		{
			var localToRoot = m_RootMatrixForTests.inverse * GUI.matrix;
			var minimum = m_RootMouseForTests + (Vector2)localToRoot.MultiplyVector(rect.min - Event.current.mousePosition);
			var maximum = m_RootMouseForTests + (Vector2)localToRoot.MultiplyVector(rect.max - Event.current.mousePosition);
			return Rect.MinMaxRect(minimum.x, minimum.y, maximum.x, maximum.y);
		}

		internal void ShowSettingDetailForTests(DebugSettingId id)
		{
			if (SelectSetting(id))
			{
				m_Page = Page.SettingDetail;
				m_WorkspacePart = WorkspacePart.Detail;
			}
		}
#endif

		private Vector2 m_SettingScroll;
		private Vector2 m_HistoryScroll;
		private string m_InspectorSearch = string.Empty;
		private string m_DiagnosticSearch = string.Empty;
		private string m_CommandSearch = string.Empty;
		private string m_SettingSearch = string.Empty;
		private string m_CommandNotice = string.Empty;
		private bool m_CommandResultEdited;
		private DebugCommandDraftId m_ExecutingDraft;
		private string m_SettingNotice = string.Empty;
		private bool m_SelectionLocked;
		private bool m_SelectionDirty;

		// The same value the remote layer names an object by; see DebugInspector.EntityValue for why it is not an int.
		private ulong m_SyncedUnitySelection;
		private bool m_SessionWasInitialized;
		private DebugSceneHandle m_SelectedScene;
		private DebugObjectHandle m_SelectedParent;
		private DebugInspectorTargetHandle m_SelectedTarget;
		private DebugInspectorComponentHandle m_SelectedComponent;
		private DebugDisplaySubjectId m_SelectedSubject;
		private DebugInspectorSceneList m_Scenes;
		private DebugInspectorHierarchyPage m_Hierarchy;
		private DebugInspectorRegisteredPage m_Registered;
		private DebugInspectorComponentPage m_Components;
		private DebugInspectorFieldPage m_Fields;
		private DebugDisplayFrameSnapshot m_DisplayFrame;
		private DebugCommandListPage m_Commands;
		private DebugCommandDescriptor m_SelectedCommand;
		private DebugCommandDraftId m_SelectedDraft;
		private DebugCommandRequest m_PendingCommandRequest;

		/// <summary>
		/// What this window works against while nothing is playing. It is built from the same definitions as a session and
		/// carries the same stores, but this window owns it, so leaving and entering Play Mode does not throw the work
		/// away. While a session exists the window uses that instead, so what it shows is what the game sees.
		/// </summary>
		private DexcRuntimeSession m_EditorState;

		private DexcRuntimeSession State =>
			DexcDebugger.CurrentSession ?? (m_EditorState ?? (m_EditorState = DexcRuntimeSession.CreateDetached()));

		private DexcSettingRegistry Settings => DexcSettingRuntime.RegistryOf(State);
		private DexcCommandRegistry Commands => DexcCommandRuntime.RegistryOf(State);
		private DexcCommandDraftStore Drafts => DexcCommandRuntime.DraftsOf(State);
		private DexcCommandHistoryStore CommandHistoryStore => DexcCommandRuntime.HistoryOf(State);
		private DexcDiagnosticStore DiagnosticStore => State.Diagnostics;
		private DebugCommandDraftId m_PendingCommandDraft;
		private int m_CommandConfirmationFrame = -1;
		private int m_CommandDispatchFrame = -1;
		private Rect m_CommandPrimaryRect;
		private Rect m_CommandCancelRect;
		private readonly Dictionary<DebugCommandFieldId, DebugValue> m_CommandValues = new Dictionary<DebugCommandFieldId, DebugValue>();
		private readonly HashSet<string> m_CollapsedCommandCategories = new HashSet<string>();
		private CommandExecutionResult m_LastCommandExecution;
		private DebugCommandExecution m_CommandExecution;
		private Task<CommandExecutionResult> m_CommandExecutionTask;
		private DebugSettingListPage m_Settings;
		private DebugSettingDescriptor m_SelectedSetting;
		private DebugValue m_SelectedSettingValue;
		private DebugResult<DebugValue> m_LastSettingWrite;
		private DebugCommandHistoryPage m_CommandHistory;
		private DebugSettingHistoryPage m_SettingHistory;
		private DebugSettingPresetId m_SelectedPreset;
		private DebugResult<IReadOnlyList<DebugSettingPresetDifference>> m_PresetPreview;
		private DebugResult m_LastPresetResult;
		private readonly Dictionary<DebugInspectorFieldId, InspectorValueSnapshot> m_FieldValues =
			new Dictionary<DebugInspectorFieldId, InspectorValueSnapshot>();
		private readonly List<DebugDiagnosticId> m_DiagnosticIds = new List<DebugDiagnosticId>();

		[MenuItem("Window/Dexc/Dexc Debugger")]
		public static void Open()
		{
			var window = GetWindow<DexcDebuggerWindow>();
			window.titleContent = new GUIContent("Dexc Debugger");
			window.minSize = new Vector2(260f, 240f);
			window.Show();
		}

#if UNITY_INCLUDE_TESTS
		internal bool SelectedTargetIsValidForTests
		{
			get { return m_SelectedTarget.IsValid; }
		}
		internal bool SelectedSceneIsValidForTests
		{
			get { return m_SelectedScene.IsValid; }
		}
		internal DebugInspectorTargetHandle SelectedTargetForTests
		{
			get { return m_SelectedTarget; }
		}
		internal DebugDisplayFrameSnapshot CapturedDisplayFrameForTests
		{
			get { return m_DisplayFrame; }
		}
		internal IReadOnlyList<DebugCommandListItem> CommandItemsForTests
		{
			get { return m_Commands == null ? Array.Empty<DebugCommandListItem>() : m_Commands.Items; }
		}
		internal string SelectedCommandPathForTests
		{
			get { return m_SelectedCommand == null ? string.Empty : m_SelectedCommand.Path; }
		}
		internal string SelectedSettingPathForTests
		{
			get { return m_SelectedSetting == null ? string.Empty : m_SelectedSetting.Path; }
		}
		internal CommandExecutionResult LastCommandExecutionForTests
		{
			get { return m_LastCommandExecution; }
		}
		internal DebugCommandHistoryPage CommandHistoryForTests
		{
			get { return m_CommandHistory; }
		}
		internal DebugSettingHistoryPage SettingHistoryForTests
		{
			get { return m_SettingHistory; }
		}
		internal DebugResult<IReadOnlyList<DebugSettingPresetDifference>> PresetPreviewForTests
		{
			get { return m_PresetPreview; }
		}
		internal bool SelectedDraftIsValidForTests
		{
			get { return m_SelectedDraft.IsValid; }
		}

		internal DebugValue CommandValueForTests(DebugCommandFieldId id)
		{
			return m_CommandValues.TryGetValue(id, out var value) ? value : null;
		}

		internal bool CommandExecutionEnabledForTests
		{
			get { return CanExecuteCommand; }
		}
		internal string CommandNoticeForTests
		{
			get { return m_CommandNotice; }
		}

		internal void ProcessEditorUpdateForTests()
		{
			OnEditorUpdate();
		}

		internal void NotifyUnitySelectionChangedForTests()
		{
			OnUnitySelectionChanged();
		}

		internal void NotifySceneClosedForTests(Scene scene)
		{
			OnSceneClosed(scene);
		}

		internal void NotifyPlayModeStateForTests(PlayModeStateChange change)
		{
			OnPlayModeStateChanged(change);
		}

		internal void RememberDiagnosticForTests(DebugDiagnosticId id)
		{
			RememberDiagnostic(id);
		}

		internal bool TryGetDiagnosticDetailForTests(DebugDiagnosticId id, out DebugDiagnosticDetail detail)
		{
			detail = null;
			return m_DiagnosticIds.Contains(id) && DiagnosticStore.TryGetDetail(id, out detail);
		}

		internal void RefreshCommandPageForTests()
		{
			ReadCommands();
		}

		internal bool SelectCommandForTests(DebugCommandId id)
		{
			return SelectCommand(id);
		}

		internal bool UpdateCommandInputForTests(DebugCommandFieldId id, DebugValue value)
		{
			return UpdateCommandInput(id, value);
		}

		internal CommandExecutionResult ExecuteSelectedCommandForTests()
		{
			return ExecuteSelectedCommand();
		}

		internal bool CommandConfirmingForTests => m_PendingCommandRequest != null;
		internal Rect CommandPrimaryRectForTests => m_CommandPrimaryRect;
		internal Rect CommandCancelRectForTests => m_CommandCancelRect;
		internal DebugCommandDraftId SelectedDraftForTests => m_SelectedDraft;

		internal CommandExecutionResult ConfirmCommandForTests() => ConfirmSelectedCommand();

		internal void CancelConfirmationForTests() => CancelCommandConfirmation();

		internal void SelectHistoryPageForTests() => SelectWorkspacePage(Page.History);

		internal bool ReuseCommandHistoryForTests(DebugHistoryId id)
		{
			return ReuseCommandHistory(id);
		}

		internal void RefreshSettingPageForTests()
		{
			ReadSettings();
		}

		internal bool SelectSettingForTests(DebugSettingId id)
		{
			return SelectSetting(id);
		}

		internal DebugResult<DebugValue> WriteSelectedSettingForTests(DebugValue value)
		{
			return WriteSelectedSetting(value);
		}

		internal bool SelectPresetForTests(DebugSettingPresetId id)
		{
			return SelectPreset(id);
		}

		internal DebugResult ApplyPresetForTests()
		{
			return ApplySelectedPreset();
		}

		internal void RefreshHistoryForTests()
		{
			ReadHistory();
		}
#endif

		private void OnEnable()
		{
			EnsureSessionSubscription();
			Selection.selectionChanged += OnUnitySelectionChanged;
			EditorApplication.update += OnEditorUpdate;
			EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
			EditorSceneManager.sceneClosed += OnSceneClosed;
			SceneManager.sceneUnloaded += OnSceneClosed;
			EditorSceneManager.activeSceneChangedInEditMode += OnActiveSceneChanged;
			m_SelectionDirty = true;
		}

		private void OnDisable()
		{
			DexcDebugger.SessionInvalidated -= ClearSessionState;
			m_SubscribedRoot = null;
			Selection.selectionChanged -= OnUnitySelectionChanged;
			EditorApplication.update -= OnEditorUpdate;
			EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
			EditorSceneManager.sceneClosed -= OnSceneClosed;
			SceneManager.sceneUnloaded -= OnSceneClosed;
			EditorSceneManager.activeSceneChangedInEditMode -= OnActiveSceneChanged;
			ClearSessionState();
		}

		private void OnUnitySelectionChanged()
		{
			if (!m_SelectionLocked)
				m_SelectionDirty = true;
			Repaint();
		}

		private void OnPlayModeStateChanged(PlayModeStateChange change)
		{
			ClearSelectionState();
			ClearReadCaches();
			m_SelectionDirty = change == PlayModeStateChange.EnteredEditMode && Selection.activeGameObject != null && !m_SelectionLocked;
			Repaint();
		}

		private void OnSceneClosed(Scene scene)
		{
			ClearSelectionState();
			m_SelectionDirty = Selection.activeGameObject != null && !m_SelectionLocked;
			Repaint();
		}

		private void OnActiveSceneChanged(Scene oldScene, Scene newScene)
		{
			if (oldScene == newScene)
				return;
			ClearSelectionState();
			m_SelectionDirty = Selection.activeGameObject != null && !m_SelectionLocked;
			Repaint();
		}

		private void OnEditorUpdate()
		{
			EnsureSessionSubscription();
			if (!DexcDebugger.IsInitialized)
			{
				if (m_SessionWasInitialized)
					ClearSessionState();
				return;
			}

			if (!m_SessionWasInitialized)
			{
				m_SessionWasInitialized = true;
				m_SelectionDirty = true;
				ClearReadCaches();
			}

			if (m_SelectionDirty && !m_SelectionLocked)
			{
				m_SelectionDirty = false;
				SyncUnitySelection();
			}

			if (DebugDisplay.TryGetCompletedFrame(out var frame))
				m_DisplayFrame = frame;
			PollCommandExecution();
			Repaint();
		}

		private void OnGUI()
		{
#if UNITY_INCLUDE_TESTS
			m_RootMouseForTests = Event.current.mousePosition;
			m_RootMatrixForTests = GUI.matrix;
#endif
			EnsureWorkspaceStyles();
			m_CommandNumericInput.ErrorRequested = OpenEditorText;
			m_CommandNumericInput.RowHeight = EditorRowHeight;
			DrawToolbar();
			if (!DexcDebugger.IsEnabled)
			{
				EditorGUILayout.HelpBox("Dexc Debugger is disabled by the current scripting symbols.", MessageType.Info);
				return;
			}

			if (!DexcDebugger.IsInitialized)
			{
				EditorGUILayout.HelpBox(
					"No Dexc session is initialized. Initialize DexcDebugger explicitly from the project or test before reading runtime data.",
					MessageType.Info
				);
				return;
			}

			using (new EditorGUILayout.VerticalScope())
			{
				using (new EditorGUILayout.VerticalScope(GUILayout.ExpandHeight(true)))
				{
					DrawMainWorkspace();
				}

				if (!IsNarrowWorkspace)
				{
					DrawBottomWorkspace();
				}
			}
		}

		private void SelectWorkspacePage(Page page)
		{
			if (m_Page == page || FeaturePage(m_Page) == page)
			{
				return;
			}

			if (FeaturePage(m_Page) == Page.Settings)
			{
				DiscardSettingEdits();
			}

			m_Helper = null;
			CancelCommandConfirmation();
			m_Page = page;
			m_WorkspacePart = WorkspacePart.List;
			if (page == Page.Commands)
			{
				ReadCommands();
			}

			if (page == Page.Settings)
			{
				ReadSettings();
			}

			if (page == Page.History)
			{
				ReadHistory();
			}

			if (page == Page.Inspector)
			{
				m_SelectionDirty = true;
			}
		}

		private void DrawWorkspaceListPane()
		{
			if (m_Page == Page.Inspector)
			{
				DrawInspectorListPane();
			}
			else if (m_Page == Page.Display)
			{
				DrawDisplayListPane();
			}
			else if (m_Page == Page.Panels)
			{
				DrawPanelListPane();
			}
			else if (m_Page == Page.Diagnostics)
			{
				m_DiagnosticScroll = EditorGUILayout.BeginScrollView(m_DiagnosticScroll);
				DrawDiagnostics();
				EditorGUILayout.EndScrollView();
			}
			else if (m_Page == Page.Settings || m_Page == Page.SettingDetail)
			{
				DrawSettingListPane();
			}
			else if (m_Page == Page.History)
			{
				DrawHistoryListPane();
			}
			else
			{
				DrawCommandListPane();
			}
		}

		private void DrawCommandCategoryTree()
		{
			var categories = new HashSet<string>();
			foreach (var item in m_Commands.Items)
			{
				var parts = item.Path.Split('/');
				for (var index = 1; index < parts.Length; index++)
					categories.Add(string.Join("/", parts.Take(index).ToArray()));
			}
			foreach (var category in categories.Where(item => ParentPath(item) == null).OrderBy(item => item, StringComparer.Ordinal))
				DrawCommandCategoryNode(category, 0, categories);
			foreach (
				var item in m_Commands.Items.Where(item => ParentPath(item.Path) == null).OrderBy(item => item.Path, StringComparer.Ordinal)
			)
				DrawCommandItem(item, 0);
		}

		private void DrawCommandCategoryNode(string category, int depth, HashSet<string> categories)
		{
			var expanded = !m_CollapsedCommandCategories.Contains(category);
			using (new EditorGUILayout.HorizontalScope())
			{
				GUILayout.Space(depth * 12f);
				var next = EditorGUILayout.Foldout(expanded, category.Split('/').Last(), true);
				if (next != expanded)
				{
					if (next)
						m_CollapsedCommandCategories.Remove(category);
					else
						m_CollapsedCommandCategories.Add(category);
				}
			}
			if (!expanded)
				return;
			foreach (var child in categories.Where(item => ParentPath(item) == category).OrderBy(item => item, StringComparer.Ordinal))
				DrawCommandCategoryNode(child, depth + 1, categories);
			foreach (
				var item in m_Commands
					.Items.Where(item => CommandCategory(item.Path) == category)
					.OrderBy(item => item.Path, StringComparer.Ordinal)
			)
				DrawCommandItem(item, depth + 1);
		}

		private void DrawCommandItem(DebugCommandListItem item, int depth)
		{
			using (new EditorGUILayout.HorizontalScope())
			{
				GUILayout.Space((depth + 1) * 12f);
				if (
					SelectionButton(
						item.DisplayName,
						item.Id == (m_SelectedCommand == null ? default(DebugCommandId) : m_SelectedCommand.Id)
					)
				)
				{
					SelectCommand(item.Id);
					m_Page = Page.CommandDetail;
					m_WorkspacePart = WorkspacePart.Detail;
				}
			}
		}

		private void DrawWorkspaceDetailPane()
		{
			if (m_Helper != null)
			{
				DrawEditorHelper();
				return;
			}

			if (m_Page == Page.Inspector)
			{
				m_RightScroll = EditorGUILayout.BeginScrollView(m_RightScroll);
				DrawInspectorDetail();
				EditorGUILayout.EndScrollView();
			}
			else if (m_Page == Page.Display)
			{
				DrawDisplayDetailPane();
			}
			else if (m_Page == Page.Panels)
			{
				DrawPanelDetailPane();
			}
			else if (m_Page == Page.Diagnostics)
			{
				GUILayout.Label("Select a diagnostic in the list.", m_ParagraphStyle);
			}
			else if (m_Page == Page.Settings || m_Page == Page.SettingDetail)
			{
				DrawSettingDetail();
			}
			else if (m_Page == Page.History)
			{
				DrawHistoryDetailPane();
			}
			else
			{
				DrawCommandDetail();
			}

			if (m_RestoreEditorFocus != null && Event.current.type == EventType.Repaint)
			{
				GUI.FocusControl(m_RestoreEditorFocus);
				m_RestoreEditorFocus = null;
			}
		}

		private void DrawCommandDetail()
		{
			EditorGUILayout.LabelField("Command detail", EditorStyles.boldLabel);
			if (m_SelectedCommand == null)
			{
				EditorGUILayout.HelpBox("Select a command from the Commands page.", MessageType.Info);
				return;
			}

			m_CommandDetailScroll = EditorGUILayout.BeginScrollView(m_CommandDetailScroll);
			DrawCommandSummary(m_SelectedCommand);
			if (m_PendingCommandRequest != null)
			{
				GUILayout.Label(m_SelectedCommand.ConfirmationMessage, m_ParagraphStyle);
			}

			foreach (var field in m_SelectedCommand.Fields)
			{
				if (!field.IsInput)
				{
					GUILayout.Label(field.Label, m_HeadingStyle);
					continue;
				}

				using (new EditorGUI.DisabledScope(m_CommandExecution != null || m_PendingCommandRequest != null))
				{
					m_CommandNumericInput.OperationError = m_CommandValidationErrors.TryGetValue(field.Id, out var issue) ? issue : null;
					m_CommandValues.TryGetValue(field.Id, out var value);
					if (field.IsMultiline)
					{
						DrawCommandTextArea(field, value);
					}
					else if (DrawSchemaValue(field.Id.ToString(), field.Label, field.Schema, value, field.InputType, out var edited))
					{
						UpdateCommandInput(field.Id, edited);
					}

					if (field.IsSlider)
					{
						DrawCommandSlider(field, value);
					}
				}
			}

			if (m_LastCommandExecution != null)
			{
				DrawExecutionResult(m_LastCommandExecution);
			}

			EditorGUILayout.EndScrollView();
			if (m_PendingCommandRequest != null)
			{
				DrawCommandConfirmation();
				return;
			}

			using (new EditorGUILayout.HorizontalScope())
			{
				GUI.enabled = CanExecuteCommand;
				if (ActionButton(m_CommandExecution != null ? "Running" : "Execute"))
				{
					ExecuteSelectedCommand();
				}

				if (Event.current.type == EventType.Repaint)
				{
					m_CommandPrimaryRect = GUILayoutUtility.GetLastRect();
				}

				// Execute above narrowed GUI.enabled; Cancel is about a run already in flight, so it gets its own state.
				GUI.enabled = true;
				if (m_CommandExecution != null)
				{
					if (ActionButton("Cancel"))
					{
						CancelCommandExecution();
					}

					if (Event.current.type == EventType.Repaint)
					{
						m_CommandCancelRect = GUILayoutUtility.GetLastRect();
					}
				}

				GUI.enabled = true;
				if (ActionButton("History"))
				{
					m_BottomPanel = BottomPanel.History;
					m_WorkspacePart = WorkspacePart.Panels;
				}

				using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(m_CommandNotice)))
				{
					if (ActionButton(string.IsNullOrEmpty(m_CommandNotice) ? "" : "!", GUILayout.Width(28)))
					{
						OpenEditorText(m_CommandNotice);
					}
				}
			}
		}

		private void DrawCommandTextArea(DebugCommandFieldDescriptor field, DebugValue value)
		{
			GUILayout.Label(field.Label, m_HeadingStyle, GUILayout.MinWidth(0));
			string text = null;
			value?.TryGetString(out text);
			text = text ?? string.Empty;
			var next = EditorGUILayout.TextArea(
				text,
				new GUIStyle(m_InputStyle) { alignment = TextAnchor.UpperLeft, wordWrap = true },
				GUILayout.MinHeight(EditorRowHeight * 3f),
				GUILayout.MinWidth(0)
			);
			if (next != text)
			{
				UpdateCommandInput(field.Id, DebugValue.From(next));
			}
		}

		private void DrawCommandSlider(DebugCommandFieldDescriptor field, DebugValue value)
		{
			if (!DebugCommandFieldDescriptor.TryGetNumber(value, out var number))
			{
				return;
			}

			var current = (float)number;
			var next = GUILayout.HorizontalSlider(current, (float)field.RangeMinimum, (float)field.RangeMaximum);
			if (Mathf.Approximately(next, current))
			{
				return;
			}

			switch (field.Schema.ValueKind)
			{
				case DebugValueKind.FloatingPoint:
					UpdateCommandInput(field.Id, DebugValue.From((double)next));
					break;
				case DebugValueKind.UnsignedInteger:
					UpdateCommandInput(field.Id, DebugValue.From((ulong)Math.Round(Math.Max(0d, next))));
					break;
				default:
					UpdateCommandInput(field.Id, DebugValue.From((long)Math.Round(next)));
					break;
			}
		}

		private void DrawCommandConfirmation()
		{
			using (new EditorGUILayout.HorizontalScope())
			{
				using (new EditorGUI.DisabledScope(!CanExecuteCommand))
				{
					if (ActionButton("Confirm", GUILayout.Width(90f)))
					{
						ConfirmSelectedCommand();
					}

					if (Event.current.type == EventType.Repaint)
					{
						m_CommandPrimaryRect = GUILayoutUtility.GetLastRect();
					}
				}

				if (ActionButton("Cancel", GUILayout.Width(90f)))
				{
					CancelCommandConfirmation();
				}

				if (Event.current.type == EventType.Repaint)
				{
					m_CommandCancelRect = GUILayoutUtility.GetLastRect();
				}
			}

			if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
			{
				CancelCommandConfirmation();
				Event.current.Use();
			}
		}

		private void DrawCommandSummary(DebugCommandDescriptor descriptor)
		{
			GUILayout.Label(descriptor.Path, m_HeadingStyle, GUILayout.MinWidth(0));
			if (!string.IsNullOrEmpty(descriptor.Description))
			{
				GUILayout.Label(descriptor.Description, m_ParagraphStyle);
			}
		}

		private void DrawExecutionResult(CommandExecutionResult result)
		{
			using (new EditorGUILayout.VerticalScope("box"))
			{
				GUILayout.Label(
					m_CommandResultEdited || !m_CommandNumericInput.IsValid ? "Result · before edits" : "Result",
					m_HeadingStyle
				);
				GUILayout.Label(result.Status + " / " + result.FailureKind, m_ParagraphStyle);
				if (!string.IsNullOrEmpty(result.ErrorCode))
				{
					GUILayout.Label(result.ErrorCode, m_ParagraphStyle);
				}

				if (!string.IsNullOrEmpty(result.Message))
				{
					GUILayout.Label(result.Message, m_ParagraphStyle);
				}

				if (result.Output != null)
				{
					GUILayout.Label(FormatDetails(result.Output), m_ParagraphStyle);
				}
			}
		}

		private void DrawSceneList()
		{
			m_Scenes = DebugInspector.GetScenes();
			foreach (var scene in m_Scenes.Scenes)
			{
				if (GUILayout.Button(scene.Name, scene.Scene == m_SelectedScene ? EditorStyles.toolbarButton : EditorStyles.miniButton))
				{
					m_SelectedScene = scene.Scene;
					m_SelectedParent = default(DebugObjectHandle);
					m_HierarchyOffset = 0;
					m_EditorParents.Clear();
					CancelEditorSearch();
					m_Hierarchy = null;
				}
			}

			if (m_Scenes.Scenes.Count == 0)
			{
				EditorGUILayout.HelpBox("No loaded scenes.", MessageType.Info);
			}
		}

		private void DrawInspectorDetail()
		{
			EditorGUILayout.LabelField("Inspector", EditorStyles.boldLabel);
			if (!m_SelectedTarget.IsValid)
			{
				EditorGUILayout.HelpBox("Select an object from Registered or Scene.", MessageType.Info);
				return;
			}

			m_Components = DebugInspector.GetComponents(m_SelectedTarget);
			foreach (var component in m_Components.Components)
			{
				if (SelectionButton(component.DisplayName, component.Component == m_SelectedComponent))
				{
					m_SelectedComponent = component.Component;
					m_Fields = null;
					m_FieldValues.Clear();
				}
			}

			if (!m_SelectedComponent.IsValid)
			{
				return;
			}

			DrawEditorInspectorFields();
		}

		private void ReadFields()
		{
			if (m_Fields == null || !m_SelectedComponent.IsValid)
				return;
			var ids = m_Fields.Fields.Select(item => item.Id).ToArray();
			var result = m_SelectedComponent.ReadFields(ids);
			RememberDiagnostic(result.DiagnosticId);
			m_FieldValues.Clear();
			foreach (var item in result.Value)
			{
				m_FieldValues[item.Field] = item.Snapshot;
				RememberDiagnostic(item.Snapshot.DiagnosticId);
			}
		}

		private void DrawSubjectDetail(DebugDisplaySubjectSnapshot subject)
		{
			using (new EditorGUILayout.VerticalScope("box"))
			{
				if (subject.Owner != null)
				{
					GUILayout.Label(subject.Owner.HierarchyPath, m_ParagraphStyle);
					GUILayout.Label(subject.Owner.SceneName, m_ParagraphStyle);
				}

				foreach (var metadata in subject.Metadata)
				{
					GUILayout.Label(metadata.Name + "\n" + FormatValue(metadata.Value), m_ParagraphStyle);
				}

				foreach (var visual in subject.Visuals)
				{
					GUILayout.Label(visual.Kind + " · " + visual.Path, m_ParagraphStyle);
				}
			}
		}

		private void DrawDiagnostics()
		{
			GUILayout.Label("Diagnostics", m_HeadingStyle);
			GUILayout.Label("ID or code", m_ParagraphStyle);
			m_DiagnosticSearch = GUILayout.TextField(
				m_DiagnosticSearch,
				m_InputStyle,
				GUILayout.Height(EditorRowHeight),
				GUILayout.MinWidth(0)
			);
			var summaries = new List<DebugDiagnosticSummary>();
			var offset = 0;
			while (true)
			{
				var page = DiagnosticStore.GetSummaries(
					new DebugDiagnosticQuery(
						DebugDiagnosticSourceFilter.All,
						DebugFeatureFilter.All,
						null,
						new DebugPageRequest(offset, BoundedPageSize(FeaturePageSize))
					),
					State.Options.MaxPageSize
				);
				summaries.AddRange(page.Summaries);
				if (!page.Page.HasMore || page.Summaries.Count == 0)
				{
					break;
				}

				offset += page.Summaries.Count;
			}

			if (summaries.Count == 0 && m_DiagnosticIds.Count == 0)
			{
				EditorGUILayout.HelpBox("No diagnostic summary is available. Details remain session-scoped.", MessageType.Info);
				return;
			}

			foreach (var summary in summaries)
			{
				if (
					!string.IsNullOrEmpty(m_DiagnosticSearch)
					&& summary.Code.IndexOf(m_DiagnosticSearch, StringComparison.OrdinalIgnoreCase) < 0
					&& summary.Message.IndexOf(m_DiagnosticSearch, StringComparison.OrdinalIgnoreCase) < 0
					&& summary.DiagnosticId.ToString().IndexOf(m_DiagnosticSearch, StringComparison.OrdinalIgnoreCase) < 0
				)
				{
					continue;
				}

				RememberDiagnostic(summary.DiagnosticId);
				if (SelectionButton(summary.Severity + " · " + summary.Code))
				{
					var text =
						summary.Severity
						+ " · "
						+ summary.Code
						+ "\n\n"
						+ summary.Message
						+ "\n\n"
						+ summary.Feature
						+ " / "
						+ summary.Phase
						+ "\n"
						+ summary.Identity;
					if (
						DiagnosticStore.TryGetDetail(summary.DiagnosticId, out var detail)
						&& !string.IsNullOrEmpty(detail.ExceptionTypeName)
					)
					{
						text += "\n\n" + detail.ExceptionTypeName + ": " + detail.ExceptionMessage;
					}

					OpenEditorText(text);
					m_WorkspacePart = WorkspacePart.Detail;
				}
			}

			foreach (
				var id in m_DiagnosticIds.Where(item => summaries.All(summary => summary.DiagnosticId != item)).Take(MaxDiagnosticRows)
			)
			{
				GUILayout.Label(id + " · Stale or unavailable", m_ParagraphStyle);
			}
		}

		private void ReadCommands()
		{
			m_Commands = Commands.GetList(
				new DebugListQuery(string.Empty, m_CommandSearch, new DebugPageRequest(m_CommandOffset, BoundedPageSize(FeaturePageSize)))
			);
			if (m_SelectedCommand != null && !Commands.TryGetDescriptor(m_SelectedCommand.Id, out m_SelectedCommand))
			{
				CancelCommandConfirmation();
				m_SelectedCommand = null;
				DiscardCommandDraft();
				m_CommandValues.Clear();
				m_CommandNumericInput.Clear();
				m_CommandRejectedInput.Clear();
			}
		}

		private bool SelectCommand(DebugCommandId id)
		{
			DiscardSettingEdits();
			if (!Commands.TryGetDescriptor(id, out var descriptor))
			{
				return false;
			}

			var operation = Drafts.Create(id);
			if (!operation.TryGetDraftId(out var draft))
			{
				m_CommandNotice = "Command draft could not be created: " + operation.Status;
				return false;
			}

			DiscardCommandDraft();
			m_Page = Page.CommandDetail;
			m_WorkspacePart = WorkspacePart.Detail;
			m_SelectedCommand = descriptor;
			m_SelectedDraft = draft;
			m_CommandValues.Clear();
			m_CommandNumericInput.Clear();
			m_CommandRejectedInput.Clear();
			foreach (var field in descriptor.Fields.Where(item => item.IsInput))
			{
				m_CommandValues[field.Id] = DexcCommandRuntime.Drafts.GetInputValue(draft, field.Id);
			}

			m_LastCommandExecution = null;
			m_CommandNotice = string.Empty;
			return true;
		}

		private bool ReuseCommandHistory(DebugHistoryId id)
		{
			CancelCommandConfirmation();
			var entry = m_CommandHistory == null ? null : m_CommandHistory.Entries.FirstOrDefault(item => item.Id == id);
			if (entry == null)
			{
				return false;
			}

			var identity = DexcCommandRuntime.History.GetHistorySnapshot().FirstOrDefault(pair => pair.Value.Id == id).Key;
			if (!Commands.TryGetDescriptor(identity, out var descriptor))
			{
				m_CommandNotice = "History command descriptor is stale.";
				return false;
			}

			var operation = Drafts.CreateFromHistory(id);
			if (!operation.TryGetDraftId(out var draft))
			{
				m_CommandNotice = "History draft could not be restored: " + operation.Status;
				return false;
			}

			try
			{
				var requestCreation = Drafts.CreateRequest(draft);
				if (!requestCreation.TryGetRequest(out var restoredRequest))
				{
					m_CommandNotice = "History draft request could not be prepared: " + requestCreation.Status;
					return false;
				}

				DiscardCommandDraft();
				m_SelectedCommand = descriptor;
				m_SelectedDraft = draft;
				m_CommandValues.Clear();
				m_CommandNumericInput.Clear();
				m_CommandRejectedInput.Clear();
				foreach (var field in descriptor.Fields.Where(item => item.IsInput))
				{
					if (restoredRequest.Values.TryGetValue(field.Id, out var value))
					{
						m_CommandValues[field.Id] = value;
					}
				}

				m_LastCommandExecution = null;
				m_CommandNotice = "Draft restored from command history. Review the detail before executing.";
				m_Page = Page.CommandDetail;
				m_WorkspacePart = WorkspacePart.Detail;
				return true;
			}
			finally
			{
				if (m_SelectedDraft != draft)
				{
					Drafts.Discard(draft);
				}
			}
		}

		private bool UpdateCommandInput(DebugCommandFieldId id, DebugValue value)
		{
			if (!m_SelectedDraft.IsValid || value == null)
			{
				return false;
			}

			// Keep the editing candidate even when Core rejects it; another component must not resurrect an older value.
			CancelCommandConfirmation();
			m_CommandResultEdited = m_LastCommandExecution != null;
			m_CommandValidationErrors.Clear();
			m_CommandValues[id] = value;
			var result = Drafts.UpdateInput(m_SelectedDraft, id, value);
			if (result.Status != DebugCommandDraftOperationStatus.Succeeded)
			{
				m_CommandRejectedInput.Add(id);
				m_CommandNotice = "Input update failed: " + result.Status;
				m_CommandValidationErrors[id] = m_CommandNotice;
				return false;
			}

			m_CommandRejectedInput.Remove(id);
			RefreshEditorCommandValues(id);
			if (m_CommandRejectedInput.Count == 0)
			{
				m_CommandNotice = string.Empty;
			}

			return true;
		}

		private CommandExecutionResult ExecuteSelectedCommand()
		{
			if (m_SelectedCommand == null || !m_SelectedDraft.IsValid)
				return null;
			if (m_PendingCommandRequest != null || !CanExecuteCommand || m_CommandDispatchFrame == Time.frameCount)
				return null;
			try
			{
				if (!TryCreateCommandRequest(out var request))
					return null;
				if (!string.IsNullOrEmpty(m_SelectedCommand.ConfirmationMessage))
				{
					m_PendingCommandRequest = request;
					m_PendingCommandDraft = m_SelectedDraft;
					m_CommandConfirmationFrame = Time.frameCount;
					m_CommandNotice = string.Empty;
					if (Event.current != null)
						GUI.FocusControl(null);
					Repaint();
					return null;
				}
				return DispatchSelectedCommand(request);
			}
			catch (Exception exception)
			{
				CancelCommandConfirmation();
				m_CommandNotice = "Command execution failed: " + exception.Message;
				return null;
			}
		}

		private CommandExecutionResult ConfirmSelectedCommand()
		{
			if (m_PendingCommandRequest == null || m_CommandConfirmationFrame == Time.frameCount)
				return null;
			var request = m_PendingCommandRequest;
			var draft = m_PendingCommandDraft;
			CancelCommandConfirmation();
			try
			{
				if (!CanExecuteCommand || m_SelectedDraft != draft || m_SelectedCommand.Id != request.CommandId)
					return null;
				if (!TryCreateCommandRequest(out var current))
					return null;
				if (
					current.DraftVersion != request.DraftVersion
					|| current.SessionId != request.SessionId
					|| current.CollectionGeneration != request.CollectionGeneration
				)
				{
					m_CommandNotice = "Command changed. Review it and request execution again.";
					return null;
				}
				return DispatchSelectedCommand(request);
			}
			catch (Exception exception)
			{
				m_CommandNotice = "Command execution failed: " + exception.Message;
				return null;
			}
		}

		private bool TryCreateCommandRequest(out DebugCommandRequest request)
		{
			request = null;
			var validation = Drafts.Validate(m_SelectedDraft);
			m_CommandValidationErrors.Clear();
			if (validation.Validation != null)
			{
				foreach (var group in validation.Validation.Issues.Where(issue => issue.HasField).GroupBy(issue => issue.FieldId))
				{
					m_CommandValidationErrors[group.Key] = string.Join("\n", group.Select(issue => issue.Message));
				}
			}

			if (
				validation.Status != DebugCommandDraftOperationStatus.Succeeded
				|| validation.Validation == null
				|| !validation.Validation.CanExecute
			)
			{
				m_CommandNotice =
					"Command validation failed: "
					+ validation.Status
					+ (
						validation.Validation == null
							? ""
							: "\n" + string.Join("\n", validation.Validation.Issues.Select(issue => issue.Message))
					);
				return false;
			}

			var creation = Drafts.CreateRequest(m_SelectedDraft);
			if (creation.TryGetRequest(out request))
			{
				return true;
			}

			m_CommandNotice = "Command request could not be created: " + creation.Status;
			return false;
		}

		private CommandExecutionResult DispatchSelectedCommand(DebugCommandRequest request)
		{
			if (!CanExecuteCommand || m_CommandDispatchFrame == Time.frameCount)
			{
				return null;
			}

			m_CommandDispatchFrame = Time.frameCount;
			try
			{
				m_LastCommandExecution = null;
				m_CommandResultEdited = false;
				m_ExecutingDraft = m_SelectedDraft;
				if (request is AsyncDebugCommandRequest asyncRequest)
				{
					var session = DexcDebugger.CurrentSession;
					m_CommandExecution = StartCommandExecution(asyncRequest);
					m_CommandExecutionTask = m_CommandExecution.WaitAsync().AsTask();
					if (!ReferenceEquals(session, DexcDebugger.CurrentSession))
					{
						ClearSessionState();
						return null;
					}

					m_CommandNotice = "Command execution is running.";
					return null;
				}

				m_LastCommandExecution = DispatchCommandRequest(request);
				m_CommandNotice = string.Empty;
				ReadHistory();
				return m_LastCommandExecution;
			}
			catch
			{
				ReportCommandExecutionError(
					DexcCommandExecutionObserver.Release(ref m_CommandExecution, ref m_CommandExecutionTask),
					"dispose"
				);
				throw;
			}
		}

		private void CancelCommandConfirmation()
		{
			m_PendingCommandRequest = null;
			m_PendingCommandDraft = default(DebugCommandDraftId);
			m_CommandConfirmationFrame = -1;
			Repaint();
		}

		private bool CanExecuteCommand =>
			(m_Page == Page.Commands || m_Page == Page.CommandDetail)
			&& m_SelectedCommand != null
			&& m_SelectedDraft.IsValid
			&& m_CommandExecution == null
			&& m_CommandNumericInput.IsValid
			&& m_CommandRejectedInput.Count == 0;

		// Commands run outside Play Mode too. One that needs a running game fails on its own terms, and that failure is in
		// the result where the user can see it, which beats a blanket refusal.
		private CommandExecutionResult DispatchCommandRequest(DebugCommandRequest request)
		{
			return request.Dispatch();
		}

		private DebugCommandExecution StartCommandExecution(AsyncDebugCommandRequest request)
		{
			return request.StartExecution();
		}

		private void PollCommandExecution()
		{
			var session = State;
			if (
				!DexcCommandExecutionObserver.TryComplete(ref m_CommandExecution, ref m_CommandExecutionTask, out var result, out var error)
			)
			{
				return;
			}

			if (!ReferenceEquals(session, DexcDebugger.CurrentSession) || m_CommandExecution != null)
			{
				return;
			}

			if (m_ExecutingDraft == m_SelectedDraft)
			{
				m_LastCommandExecution = result;
				m_CommandResultEdited = false;
			}

			ReportCommandExecutionError(error, "completion");
			ReadHistory();
		}

		private void CancelCommandExecution()
		{
			ReportCommandExecutionError(DexcCommandExecutionObserver.Cancel(m_CommandExecution), "cancel");
		}

		private void ReportCommandExecutionError(Exception error, string phase)
		{
			if (error == null)
			{
				return;
			}
			m_CommandNotice = "Async command " + phase + " failed: " + error.Message;
			var diagnostic = DexcDebugger.CurrentSession?.Diagnostics.AddOperation(
				DebugDiagnosticSeverity.Error,
				DebugFeature.Command,
				phase,
				"command-" + phase + "-failed",
				m_CommandNotice,
				m_SelectedCommand?.Path ?? "Editor command execution",
				null,
				error
			);
			if (diagnostic != null)
			{
				RememberDiagnostic(diagnostic.DiagnosticId);
			}
		}

		private void ReadSettings()
		{
			m_Settings = Settings.GetList(
				new DebugListQuery(string.Empty, m_SettingSearch, new DebugPageRequest(0, BoundedPageSize(FeaturePageSize)))
			);
			if (m_SelectedSetting != null && !Settings.TryGetDescriptor(m_SelectedSetting.Id, out m_SelectedSetting))
			{
				m_SelectedSetting = null;
				m_SettingNumericInput.Clear();
				m_SelectedSettingValue = null;
			}
		}

		private bool SelectSetting(DebugSettingId id)
		{
			if (!Settings.TryGetDescriptor(id, out var descriptor))
			{
				return false;
			}

			CancelCommandConfirmation();
			m_SelectedSetting = descriptor;
			m_SelectedSettingValue = null;
			m_LastSettingWrite = null;
			m_SettingNotice = string.Empty;
			m_SelectedPreset = default(DebugSettingPresetId);
			m_PresetPreview = null;
			m_LastPresetResult = null;
			return OpenSettingClass(descriptor);
		}

		private bool SelectPreset(DebugSettingPresetId id)
		{
			if (!id.IsValid)
				return false;
			m_SelectedPreset = id;
			m_PresetPreview = Settings.PreviewPreset(id);
			m_SettingNotice = "Preset preview: " + m_PresetPreview.Status + " / " + m_PresetPreview.Value.Count + " differences.";
			return true;
		}

		private DebugResult ApplySelectedPreset()
		{
			if (!m_SelectedPreset.IsValid)
				return null;
			m_LastPresetResult = Settings.ApplyPreset(m_SelectedPreset);
			m_SettingNotice = "Preset apply: " + m_LastPresetResult.Status;
			if (m_LastPresetResult.Status == DebugOperationStatus.Succeeded)
			{
				ReadSelectedSetting();
				m_PresetPreview = Settings.PreviewPreset(m_SelectedPreset);
				ReadHistory();
			}
			return m_LastPresetResult;
		}

		private bool ReadSelectedSetting()
		{
			if (m_SelectedSetting == null)
				return false;
			m_SettingNumericInput.Clear();
			var result = Settings.Read(m_SelectedSetting.Id);
			if (!result.TryGetValue(out m_SelectedSettingValue))
			{
				m_SettingNotice = "Setting read returned: " + result.Status;
				return false;
			}
			return true;
		}

		private DebugResult<DebugValue> WriteSelectedSetting(DebugValue value)
		{
			if (m_SelectedSetting == null || value == null || !m_SettingNumericInput.IsValid)
			{
				return null;
			}

			m_LastSettingWrite = Settings.Write(m_SelectedSetting.Id, value);
			if (m_LastSettingWrite.Status == DebugOperationStatus.Succeeded)
			{
				m_SelectedSettingValue = value;
				var field = m_EditorSettingFields.FirstOrDefault(item => item.Descriptor.Id == m_SelectedSetting.Id);
				if (field != null)
				{
					field.Applied = value;
					field.Value = value;
					field.Dirty = false;
				}

				m_SettingNotice = string.Empty;
				ReadHistory();
			}
			else
			{
				m_SettingNotice = "Setting write returned: " + m_LastSettingWrite.Status;
			}

			return m_LastSettingWrite;
		}

		private void ReadHistory()
		{
			m_CommandHistory =
				m_SelectedCommand == null
					? null
					: CommandHistoryStore.GetHistory(m_SelectedCommand.Id, new DebugPageRequest(0, BoundedPageSize(MaxDiagnosticRows)));
			m_SettingHistory =
				m_SelectedSetting == null
					? null
					: Settings.GetHistory(m_SelectedSetting.Id, new DebugPageRequest(0, BoundedPageSize(MaxDiagnosticRows)));
		}

		private static DebugValue DefaultValue(DebugInputSchema schema)
		{
			if (schema == null || schema.IsNullable)
				return DebugValue.Null;
			switch (schema.ValueKind)
			{
				case DebugValueKind.Boolean:
					return DebugValue.From(false);
				case DebugValueKind.SignedInteger:
					return DebugValue.From(0L);
				case DebugValueKind.UnsignedInteger:
					return DebugValue.From(0UL);
				case DebugValueKind.FloatingPoint:
					return DebugValue.From(0d);
				case DebugValueKind.String:
					return DebugValue.From(string.Empty);
				case DebugValueKind.Array:
					return DebugValue.FromArray();
				case DebugValueKind.Object:
					return DebugValue.FromObject(
						schema.Members.Select(member => new DebugValueMember(member.Name, DefaultValue(member.Schema)))
					);
				default:
					return DebugValue.Null;
			}
		}

		private bool DrawSchemaValue(
			string key,
			string label,
			DebugInputSchema schema,
			DebugValue value,
			Type inputType,
			out DebugValue edited
		)
		{
			edited = value ?? DefaultValue(schema);
			if (schema == null)
			{
				return false;
			}

			var enumType = ResolveEnumType(inputType);
			var valueType = ResolveInputValueType(inputType);
			var draft = m_SelectedDraft;
			Action<DebugValue> selected = candidate =>
			{
				if (draft != m_SelectedDraft || m_CommandExecution != null || m_PendingCommandRequest != null)
				{
					return;
				}

				var field = m_SelectedCommand.Fields.FirstOrDefault(item => item.Id.ToString() == key);
				if (field != null)
				{
					UpdateCommandInput(field.Id, candidate);
				}
			};
			if (inputType != null && inputType.IsGenericType && inputType.GetGenericTypeDefinition() == typeof(InputChoice<>))
			{
				var field = m_SelectedCommand.Fields.First(item => item.Id.ToString() == key);
				DrawEditorSelectionValue(
					label,
					DexcCommandRuntime.Drafts.GetSelectionLabel(draft, field.Id) ?? "None",
					() => OpenCommandChoice(field),
					m_CommandNumericInput.OperationError
				);
				return false;
			}

			if (IsReferenceInput(inputType))
			{
				var field = m_SelectedCommand.Fields.First(item => item.Id.ToString() == key);
				DrawEditorSelectionValue(
					label,
					DexcCommandRuntime.Drafts.GetSelectionLabel(draft, field.Id) ?? "None",
					() => OpenCommandReference(field),
					m_CommandNumericInput.OperationError
				);
				return false;
			}

			if (schema.ValueKind == DebugValueKind.String && enumType != null)
			{
				return DrawEditorValue(key, label, enumType, edited, m_CommandNumericInput, out edited, selected);
			}

			if (schema.ValueKind == DebugValueKind.Object)
			{
				var objectValue = edited;
				var objectChanged = DrawObjectValue(key, label, schema, objectValue, valueType, out var objectEdited);
				edited = objectEdited;
				return objectChanged;
			}

			if (
				schema.ValueKind == DebugValueKind.SignedInteger
				|| schema.ValueKind == DebugValueKind.UnsignedInteger
				|| schema.ValueKind == DebugValueKind.FloatingPoint
			)
			{
				return m_CommandNumericInput.Draw(key, label, edited, schema.ValueKind, valueType, out edited);
			}

			return DrawEditorValue(key, label, valueType, edited, m_CommandNumericInput, out edited, selected);
		}

		private bool DrawObjectValue(
			string key,
			string label,
			DebugInputSchema schema,
			DebugValue value,
			Type valueType,
			out DebugValue edited
		)
		{
			var members = new List<DebugValueMember>();
			value.TryGetObject(out var existing);
			GUILayout.Label(label, m_HeadingStyle, GUILayout.MinWidth(0));
			var changed = false;
			var memberInputType =
				valueType == typeof(Vector2Int) || valueType == typeof(Vector3Int) ? typeof(InputInt)
				: valueType == typeof(Vector2) || valueType == typeof(Vector3) || valueType == typeof(Vector4) || valueType == typeof(Color)
					? typeof(InputFloat)
				: null;
			foreach (var member in schema.Members)
			{
				var existingMember = existing == null ? null : existing.FirstOrDefault(item => item.Name == member.Name);
				var current = existingMember == null ? DefaultValue(member.Schema) : existingMember.Value;
				var memberChanged = DrawSchemaValue(
					key + "/" + member.Name,
					member.Name,
					member.Schema,
					current,
					memberInputType,
					out var next
				);
				members.Add(new DebugValueMember(member.Name, memberChanged ? next : current));
				changed |= memberChanged;
			}

			edited = DebugValue.FromObject(members);
			return changed;
		}

		private static Type ResolveInputValueType(Type inputType)
		{
			for (var type = inputType; type != null; type = type.BaseType)
			{
				if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(DebugInput<>))
					return type.GetGenericArguments()[0];
			}
			return null;
		}

		private static Type ResolveEnumType(Type inputType)
		{
			if (inputType == null || !inputType.IsGenericType || inputType.GetGenericTypeDefinition() != typeof(InputEnum<>))
				return null;
			return inputType.GetGenericArguments()[0];
		}

		private static bool IsReferenceInput(Type inputType)
		{
			return inputType != null && inputType.IsGenericType && inputType.GetGenericTypeDefinition() == typeof(InputRef<>);
		}

		private static string ParentPath(string path)
		{
			if (string.IsNullOrEmpty(path))
				return null;
			var separator = path.LastIndexOf('/');
			return separator < 0 ? null : path.Substring(0, separator);
		}

		private static string CommandCategory(string path)
		{
			return ParentPath(path);
		}

		private static Type ResolveType(string name) => DexcDefinitions.ResolveType(name);

		private void SyncUnitySelection()
		{
			var gameObject = Selection.activeGameObject;
			if (gameObject != null && m_SelectedTarget.IsValid && m_SyncedUnitySelection == DebugInspector.EntityValue(gameObject))
			{
				return;
			}

			ClearSelectionState();
			if (gameObject == null || !gameObject.scene.IsValid())
			{
				return;
			}

			try
			{
				var scene = DebugInspector
					.GetScenes()
					.Scenes.FirstOrDefault(item => item.Path == gameObject.scene.path || item.Name == gameObject.scene.name);
				if (scene == null)
				{
					return;
				}

				var path = BuildHierarchyPath(gameObject.transform);
				var segments = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
				var parent = default(DebugObjectHandle);
				DebugInspectorHierarchyEntry match = null;
				var prefix = string.Empty;
				foreach (var segment in segments)
				{
					prefix = string.IsNullOrEmpty(prefix) ? segment : prefix + "/" + segment;
					var offset = 0;
					while (true)
					{
						var page = DebugInspector.GetHierarchyChildren(
							new DebugHierarchyChildrenQuery(
								scene.Scene,
								parent,
								new DebugPageRequest(offset, BoundedHierarchyPageSize(MaxSceneRows))
							)
						);
						match = page.Entries.FirstOrDefault(item => item.Path == prefix);
						if (match != null || !page.Page.HasMore || page.Entries.Count == 0)
						{
							break;
						}

						offset += page.Entries.Count;
					}

					if (match == null)
					{
						return;
					}

					parent = match.Target;
				}

				if (match != null)
				{
					SelectTarget(match.InspectorTarget, scene.Scene, match.Target);
					m_SyncedUnitySelection = DebugInspector.EntityValue(gameObject);
				}
			}
			catch (Exception exception)
			{
				RememberDiagnostic(default(DebugDiagnosticId));
				Debug.LogWarning("Dexc selection synchronization failed: " + exception.Message);
			}
		}

		private void SelectTarget(DebugInspectorTargetHandle target, DebugSceneHandle scene, DebugObjectHandle parent)
		{
			m_SyncedUnitySelection = 0UL;
			m_Helper = null;
			m_EditorInspectorFields.Clear();
			m_EditorInspectorOwner = default;
			m_SelectedTarget = target;
			m_SelectedScene = scene;
			m_SelectedComponent = default(DebugInspectorComponentHandle);
			m_Fields = null;
			m_FieldValues.Clear();
			m_WorkspacePart = WorkspacePart.Detail;
		}

		/// <summary>
		/// Re-arms the session notification after a root reset. The root that owns the event is replaced on every Play Mode
		/// transition, and this window outlives those, so a single subscription in OnEnable would be dropped after one cycle.
		/// </summary>
		private void EnsureSessionSubscription()
		{
			var root = DexcRoot.Current;
			if (ReferenceEquals(m_SubscribedRoot, root))
				return;
			m_SubscribedRoot = root;
			DexcDebugger.SessionInvalidated -= ClearSessionState;
			DexcDebugger.SessionInvalidated += ClearSessionState;
		}

		private void ClearSessionState()
		{
			m_SessionWasInitialized = false;
			var error = DexcCommandExecutionObserver.Release(ref m_CommandExecution, ref m_CommandExecutionTask);
			ReportCommandExecutionError(error, "dispose");
			ClearReadCaches();
			ClearSelectionState();
		}

		private void ClearSelectionState()
		{
			m_SyncedUnitySelection = 0UL;
			CancelEditorSearch();
			m_EditorParents.Clear();
			m_HierarchyOffset = 0;
			m_EditorInspectorFields.Clear();
			m_EditorInspectorOwner = default;
			m_ExplicitInspectorEditing = false;
			m_Helper = null;
			m_SelectedScene = default(DebugSceneHandle);
			m_SelectedParent = default(DebugObjectHandle);
			m_SelectedTarget = default(DebugInspectorTargetHandle);
			m_SelectedComponent = default(DebugInspectorComponentHandle);
			m_SelectedSubject = default(DebugDisplaySubjectId);
			m_Hierarchy = null;
			m_Components = null;
			m_Fields = null;
			m_FieldValues.Clear();
		}

		private void DiscardCommandDraft()
		{
			m_CommandValidationErrors.Clear();
			CancelCommandConfirmation();
			var draft = m_SelectedDraft;
			m_SelectedDraft = default(DebugCommandDraftId);
			if (draft.IsValid && DexcDebugger.IsInitialized)
			{
				Drafts.Discard(draft);
			}
		}

		private void ClearReadCaches()
		{
			m_Helper = null;
			CancelEditorSearch();
			m_CommandOffset = m_RegisteredOffset = m_HierarchyOffset = 0;
			m_EditorSettings.Clear();
			m_EditorSettingFields.Clear();
			DiscardCommandDraft();
			m_Scenes = null;
			m_Hierarchy = null;
			m_Registered = null;
			m_Components = null;
			m_Fields = null;
			m_DisplayFrame = null;
			m_Commands = null;
			m_SelectedCommand = null;
			m_CommandValues.Clear();
			m_CommandNumericInput.Clear();
			m_CommandRejectedInput.Clear();
			m_LastCommandExecution = null;
			m_Settings = null;
			m_SelectedSetting = null;
			m_EditorSettingFields.Clear();
			m_SettingNumericInput.Clear();
			m_SelectedSettingValue = null;
			m_LastSettingWrite = null;
			m_CommandHistory = null;
			m_SettingHistory = null;
			m_SelectedPreset = default(DebugSettingPresetId);
			m_PresetPreview = null;
			m_LastPresetResult = null;
			m_FieldValues.Clear();
			m_DiagnosticIds.Clear();
		}

		private void RememberDiagnostic(DebugDiagnosticId id)
		{
			if (id.IsValid && !m_DiagnosticIds.Contains(id))
				m_DiagnosticIds.Add(id);
		}

		private static string BuildHierarchyPath(Transform transform)
		{
			var parts = new List<string>();
			for (var current = transform; current != null; current = current.parent)
				parts.Add(current.name);
			parts.Reverse();
			return string.Join("/", parts.ToArray());
		}

		private static string FormatSnapshot(InspectorValueSnapshot snapshot)
		{
			if (snapshot == null)
				return string.Empty;
			if (DexcInspectorSnapshotText.IsCollection(snapshot))
				return DexcInspectorSnapshotText.Format(snapshot);
			if (snapshot.State != DebugInspectorSnapshotState.Value)
				return snapshot.State + (string.IsNullOrEmpty(snapshot.Error) ? string.Empty : ": " + snapshot.Error);
			return FormatValue(snapshot.Value);
		}

		private static string FormatValue(DebugValue value)
		{
			if (value == null)
			{
				return string.Empty;
			}

			if (value.Kind == DebugValueKind.Null)
			{
				return "null";
			}

			if (value.TryGetArray(out var array))
			{
				return "[" + array.Count + "]";
			}

			return DexcInputText.Format(value);
		}

		private static string FormatDetails(DebugValue value)
		{
			if (value == null)
			{
				return string.Empty;
			}

			if (value.TryGetArray(out var array))
			{
				return "["
					+ array.Count
					+ "]\n"
					+ string.Join("\n", array.Select((item, index) => "[" + index + "] " + FormatDetails(item)));
			}

			if (value.TryGetObject(out var members))
			{
				return string.Join("\n", members.Select(member => member.Name + ": " + FormatDetails(member.Value)));
			}

			return FormatValue(value);
		}
	}
}
