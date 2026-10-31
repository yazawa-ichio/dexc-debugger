using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Dexc
{
	public sealed partial class DexcDebuggerWindow
	{
		private enum WorkspacePart
		{
			List,
			Detail,
			Panels,
		}

		private enum BottomPanel
		{
			Watch,
			History,
			Diagnostics,
			Snapshot,
		}

		private static readonly Page[] FeaturePages =
		{
			Page.Commands,
			Page.Settings,
			Page.Inspector,
			Page.History,
			Page.Display,
			Page.Panels,
			Page.Diagnostics,
		};
		private static readonly string[] FeatureNames =
		{
			"Command",
			"Setting",
			"Inspector",
			"History",
			"Display",
			"Panels",
			"Diagnostics",
		};
		private WorkspacePart m_WorkspacePart;
		private BottomPanel m_BottomPanel;
		private bool m_ShowFeatures;
		private Vector2 m_FeatureScroll;
		private string m_RestoreEditorFocus;
		private Vector2 m_CommandDetailScroll;
		private readonly Vector2[] m_BottomScroll = new Vector2[4];
		private int m_WatchOffset;
		private GUIStyle m_InputStyle,
			m_ActionStyle,
			m_SelectionStyle,
			m_ParagraphStyle,
			m_HeadingStyle;
		private readonly Dictionary<string, Rect> m_WorkspaceRects = new Dictionary<string, Rect>();
		private bool IsNarrowWorkspace => position.width < RequiredListWidth + RequiredDetailWidth + 8f;
		private float RequiredListWidth =>
			Mathf.Max(
				220f,
				m_ActionStyle == null
					? 0f
					: m_ActionStyle.CalcSize(new GUIContent("Registered")).x + m_ActionStyle.CalcSize(new GUIContent("Scene")).x + 16f
			);
		private float RequiredDetailWidth =>
			Mathf.Max(
				260f,
				m_ActionStyle == null
					? 0f
					: new[] { "Execute", "Cancel", "History" }.Sum(text => m_ActionStyle.CalcSize(new GUIContent(text)).x) + 44f
			);
		private float EditorRowHeight =>
			Mathf.Max(
				EditorGUIUtility.singleLineHeight + 8f,
				DexcDebugger.CurrentSession?.Options.RuntimeUISkin?.Windows.RowHeight ?? 28f,
				DexcDebugger.CurrentSession?.Options.RuntimeUISkin?.Windows.MinimumHitHeight ?? 28f
			);
#if UNITY_INCLUDE_TESTS
		internal IReadOnlyDictionary<string, Rect> WorkspaceRectsForTests => m_WorkspaceRects;

		internal void SelectWorkspacePartForTests(int part)
		{
			m_WorkspacePart = (WorkspacePart)part;
			Repaint();
		}

		internal void SelectBottomPanelForTests(int panel)
		{
			m_BottomPanel = (BottomPanel)panel;
			Repaint();
		}

		internal Vector2 CommandDetailScrollForTests
		{
			get => m_CommandDetailScroll;
			set => m_CommandDetailScroll = value;
		}
		internal bool SelectionLockedForTests
		{
			get => m_SelectionLocked;
			set => m_SelectionLocked = value;
		}

		internal void SelectFeatureForTests(string feature)
		{
			var index = Array.IndexOf(FeatureNames, feature);
			if (index >= 0)
			{
				SelectWorkspacePage(FeaturePages[index]);
			}
		}

		internal GUIStyle InputStyleForTests => m_InputStyle;
#endif

		private static Page FeaturePage(Page page) =>
			page == Page.CommandDetail ? Page.Commands
			: page == Page.SettingDetail ? Page.Settings
			: page;

		private void EnsureWorkspaceStyles()
		{
			if (m_InputStyle != null)
			{
				return;
			}

			m_InputStyle = new GUIStyle(EditorStyles.textField)
			{
				alignment = TextAnchor.MiddleLeft,
				padding = new RectOffset(6, 6, 4, 4),
				fixedHeight = 0,
			};
			m_ActionStyle = new GUIStyle(GUI.skin.button)
			{
				alignment = TextAnchor.MiddleCenter,
				padding = new RectOffset(8, 8, 4, 4),
				fixedHeight = 0,
			};
			m_SelectionStyle = new GUIStyle(m_ActionStyle) { alignment = TextAnchor.MiddleLeft, wordWrap = true };
			m_ParagraphStyle = new GUIStyle(EditorStyles.wordWrappedLabel) { alignment = TextAnchor.UpperLeft, wordWrap = true };
			m_HeadingStyle = new GUIStyle(m_ParagraphStyle) { fontStyle = FontStyle.Bold };
		}

		private void RememberRect(string name)
		{
			if (Event.current.type == EventType.Repaint)
			{
#if UNITY_INCLUDE_TESTS
				m_WorkspaceRects[name] = WindowRectForTests(GUILayoutUtility.GetLastRect());
#endif
			}
		}

		private bool TabButton(string label, bool selected)
		{
			var previous = GUI.backgroundColor;
			if (selected)
			{
				GUI.backgroundColor = new Color(0.55f, 0.75f, 1f);
			}

			var clicked = ActionButton(label);
			GUI.backgroundColor = previous;
			return clicked;
		}

		private bool ActionButton(string label, params GUILayoutOption[] options)
		{
			var clicked = GUILayout.Button(
				label,
				m_ActionStyle,
				options.Concat(new[] { GUILayout.Height(EditorRowHeight), GUILayout.MinWidth(0) }).ToArray()
			);
			RememberRect(label);
			return clicked;
		}

		private bool SelectionButton(string label, bool selected = false)
		{
			var previous = GUI.backgroundColor;
			if (selected)
			{
				GUI.backgroundColor = new Color(0.55f, 0.75f, 1f);
			}

			var clicked = GUILayout.Button(
				label,
				m_SelectionStyle,
				GUILayout.MinHeight(EditorRowHeight),
				GUILayout.MinWidth(0),
				GUILayout.ExpandWidth(true)
			);
			RememberRect(label);
			GUI.backgroundColor = previous;
			return clicked;
		}

		private void DrawToolbar()
		{
			using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
			{
				GUILayout.Label("Dexc", GUILayout.MinWidth(0));
				GUILayout.FlexibleSpace();
				m_SelectionLocked = GUILayout.Toggle(m_SelectionLocked, "Lock Selection", EditorStyles.toolbarButton, GUILayout.Width(105));
				if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(60)))
				{
					ClearReadCaches();
					m_SelectionDirty = true;
					if (DexcDebugger.IsInitialized)
					{
						ReadCommands();
						ReadSettings();
						ReadHistory();
					}
				}
			}

			var required = FeatureNames.Sum(name => m_ActionStyle.CalcSize(new GUIContent(name)).x + 8);
			if (position.width >= required)
			{
				using (new EditorGUILayout.HorizontalScope())
				{
					for (var index = 0; index < FeaturePages.Length; index++)
					{
						if (TabButton(FeatureNames[index], FeaturePage(m_Page) == FeaturePages[index]))
						{
							SelectWorkspacePage(FeaturePages[index]);
						}
					}
				}
			}
			else
			{
				if (ActionButton("Features · " + FeatureNames[Array.IndexOf(FeaturePages, FeaturePage(m_Page))]))
				{
					m_ShowFeatures = !m_ShowFeatures;
				}
			}
		}

		private void DrawMainWorkspace()
		{
			if (m_ShowFeatures)
			{
				m_FeatureScroll = EditorGUILayout.BeginScrollView(m_FeatureScroll);
				for (var index = 0; index < FeaturePages.Length; index++)
				{
					if (SelectionButton(FeatureNames[index], FeaturePage(m_Page) == FeaturePages[index]))
					{
						SelectWorkspacePage(FeaturePages[index]);
						m_ShowFeatures = false;
					}
				}

				EditorGUILayout.EndScrollView();
				return;
			}

			if (IsNarrowWorkspace)
			{
				using (new EditorGUILayout.HorizontalScope())
				{
					foreach (WorkspacePart part in Enum.GetValues(typeof(WorkspacePart)))
					{
						if (TabButton(part.ToString(), m_WorkspacePart == part))
						{
							m_WorkspacePart = part;
						}
					}
				}

				using (new EditorGUILayout.VerticalScope(GUILayout.MinWidth(0), GUILayout.ExpandHeight(true)))
				{
					if (m_WorkspacePart == WorkspacePart.List)
					{
						DrawWorkspaceListPane();
					}
					else if (m_WorkspacePart == WorkspacePart.Detail)
					{
						DrawWorkspaceDetailPane();
					}
					else
					{
						DrawBottomWorkspace();
					}
				}

				RememberRect("Workspace");
				return;
			}

			using (new EditorGUILayout.HorizontalScope())
			{
				using (
					new EditorGUILayout.VerticalScope(
						GUILayout.Width(Mathf.Max(RequiredListWidth, position.width - Mathf.Max(488f, RequiredDetailWidth)))
					)
				)
				{
					DrawWorkspaceListPane();
				}

				RememberRect("List pane");
				using (new EditorGUILayout.VerticalScope(GUILayout.MinWidth(0), GUILayout.ExpandWidth(true)))
				{
					DrawWorkspaceDetailPane();
				}

				RememberRect("Detail pane");
			}
		}

		private void DrawInspectorListPane()
		{
			DrawInspectorSourceToolbar();
			m_LeftScroll = EditorGUILayout.BeginScrollView(m_LeftScroll);
			if (m_InspectorSource == InspectorSource.Registered)
			{
				DrawRegisteredList();
			}
			else
			{
				DrawSceneList();
				DrawHierarchyPane();
			}

			EditorGUILayout.EndScrollView();
		}

		private void DrawBottomWorkspace()
		{
			using (
				new EditorGUILayout.VerticalScope(
					"box",
					IsNarrowWorkspace ? GUILayout.ExpandHeight(true) : GUILayout.Height(Mathf.Clamp(position.height * 0.28f, 120, 240))
				)
			)
			{
				var perRow = position.width < 340 ? 2 : 4;
				for (var first = 0; first < 4; first += perRow)
				{
					using (new EditorGUILayout.HorizontalScope())
					{
						for (var index = first; index < first + perRow; index++)
						{
							var panel = (BottomPanel)index;
							if (TabButton(panel == BottomPanel.Diagnostics ? "Issues" : panel.ToString(), m_BottomPanel == panel))
							{
								m_BottomPanel = panel;
							}
						}
					}
				}

				m_BottomScroll[(int)m_BottomPanel] = EditorGUILayout.BeginScrollView(m_BottomScroll[(int)m_BottomPanel]);
				if (m_BottomPanel == BottomPanel.Watch)
				{
					DrawWatchPanel();
				}
				else if (m_BottomPanel == BottomPanel.History)
				{
					DrawHistoryEntries(true);
				}
				else if (m_BottomPanel == BottomPanel.Diagnostics)
				{
					DrawDiagnostics();
				}
				else
				{
					DrawSnapshotSummary();
				}

				EditorGUILayout.EndScrollView();
			}

			RememberRect("Bottom panel");
		}

		private void DrawWatchPanel()
		{
			var page = DebugInspector.GetWatches(new DebugPageRequest(m_WatchOffset, BoundedPageSize(16)));
			if (page.Entries.Count == 0 && m_WatchOffset > 0)
			{
				m_WatchOffset = 0;
				return;
			}

			if (page.Entries.Count == 0)
			{
				GUILayout.Label("No watched values", m_ParagraphStyle);
				return;
			}

			foreach (var entry in page.Entries)
			{
				using (new EditorGUILayout.HorizontalScope())
				{
					GUILayout.Label(
						DexcInspectorRuntime.Registry.GetWatchLabel(entry.WatchId)
							+ "\n"
							+ entry.State
							+ " · "
							+ FormatSnapshot(entry.Snapshot),
						m_SelectionStyle,
						GUILayout.MinHeight(EditorRowHeight * 2),
						GUILayout.MinWidth(0),
						GUILayout.ExpandWidth(true)
					);
					using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying))
					{
						if (ActionButton("Remove", GUILayout.Width(76)))
						{
							DebugInspector.RemoveWatch(entry.WatchId);
						}
					}
				}
			}

			DrawPager(ref m_WatchOffset, page.Page, 16);
		}

		private void DrawPager(ref int offset, DebugPageInfo page, int size)
		{
			using (new EditorGUILayout.HorizontalScope())
			{
				using (new EditorGUI.DisabledScope(offset == 0))
				{
					if (ActionButton("Previous"))
					{
						offset = Math.Max(0, offset - BoundedPageSize(size));
					}
				}

				using (new EditorGUI.DisabledScope(!page.HasMore))
				{
					if (ActionButton("Next"))
					{
						offset += page.Count;
					}
				}
			}
		}

		private sealed class HistoryRow
		{
			internal string Key,
				Label,
				Detail;
			internal DateTimeOffset At;
			internal DebugCommandId Command;
			internal DebugHistoryId CommandHistory;
		}

		private string m_HistoryKind = "All";
		private string m_HistorySelection;
		private int m_HistoryOffset;
		private HistoryRow m_HistoryDetail;

		private List<HistoryRow> ReadHistoryRows()
		{
			var rows = new List<HistoryRow>();
			if (m_HistoryKind == "All" || m_HistoryKind == "Command")
			{
				foreach (var pair in DexcCommandRuntime.History.GetHistorySnapshot().Reverse())
				{
					var item = pair.Value;
					rows.Add(
						new HistoryRow
						{
							Key = "Command:" + item.Id,
							Label = item.CommandPath + " · " + item.Status,
							At = item.OccurredAtUtc,
							Command = pair.Key,
							CommandHistory = item.Id,
							Detail =
								item.CommandPath
								+ "\n"
								+ item.OccurredAtUtc.ToString("u")
								+ "\n"
								+ item.Status
								+ "\n"
								+ item.InputSummary
								+ "\n"
								+ item.OutputSummary,
						}
					);
				}
			}

			if (m_HistoryKind == "All" || m_HistoryKind == "Setting")
			{
				foreach (var item in DexcSettingRuntime.Registry.GetHistorySnapshot().Reverse())
				{
					rows.Add(
						new HistoryRow
						{
							Key = "Setting:" + item.Id,
							Label = item.Path + " · " + item.Operation,
							At = item.OccurredAtUtc,
							Detail =
								item.Path
								+ "\n"
								+ item.OccurredAtUtc.ToString("u")
								+ "\n"
								+ item.Operation
								+ "\nBefore: "
								+ FormatValue(item.Before)
								+ "\nAfter: "
								+ FormatValue(item.After),
						}
					);
				}
			}

			if (m_HistoryKind == "All" || m_HistoryKind == "Inspector")
			{
				foreach (var item in DexcInspectorRuntime.Registry.GetHistorySnapshot().Reverse())
				{
					rows.Add(
						new HistoryRow
						{
							Key = "Inspector:" + item.Id,
							Label = item.Label + " / " + item.FieldName + " · " + item.Status,
							At = item.OccurredAtUtc,
							Detail =
								item.Label
								+ " / "
								+ item.FieldName
								+ "\n"
								+ item.OccurredAtUtc.ToString("u")
								+ "\n"
								+ item.Status
								+ "\nBefore: "
								+ FormatSnapshot(item.Before)
								+ "\nAfter: "
								+ FormatSnapshot(item.After),
						}
					);
				}
			}

			return rows.OrderByDescending(row => row.At).ToList();
		}

		private void DrawHistoryEntries(bool bottom)
		{
			var filters = new[] { "All", "Command", "Setting", "Inspector" };
			var next = EditorGUILayout.Popup("Type", Array.IndexOf(filters, m_HistoryKind), filters);
			if (filters[next] != m_HistoryKind)
			{
				m_HistoryKind = filters[next];
				m_HistoryOffset = 0;
			}

			var rows = ReadHistoryRows();
			if (m_HistoryOffset >= rows.Count)
			{
				m_HistoryOffset = Math.Max(0, rows.Count - 16);
			}

			if (rows.Count == 0)
			{
				GUILayout.Label("No history", m_ParagraphStyle);
			}

			foreach (var row in rows.Skip(m_HistoryOffset).Take(16))
			{
				if (SelectionButton(row.Label, row.Key == m_HistorySelection))
				{
					m_HistorySelection = row.Key;
					m_HistoryDetail = row;
					if (!bottom)
					{
						m_WorkspacePart = WorkspacePart.Detail;
					}
				}
			}

			using (new EditorGUILayout.HorizontalScope())
			{
				using (new EditorGUI.DisabledScope(m_HistoryOffset == 0))
				{
					if (ActionButton("Previous"))
					{
						m_HistoryOffset = Math.Max(0, m_HistoryOffset - 16);
					}
				}

				using (new EditorGUI.DisabledScope(m_HistoryOffset + 16 >= rows.Count))
				{
					if (ActionButton("Next"))
					{
						m_HistoryOffset += 16;
					}
				}
			}

			if (bottom)
			{
				DrawHistoryContents();
			}
		}

		private void DrawHistoryListPane()
		{
			m_HistoryScroll = EditorGUILayout.BeginScrollView(m_HistoryScroll);
			DrawHistoryEntries(false);
			EditorGUILayout.EndScrollView();
		}

		private Vector2 m_HistoryDetailScroll;

		private void DrawHistoryDetailPane()
		{
			m_HistoryDetailScroll = EditorGUILayout.BeginScrollView(m_HistoryDetailScroll);
			DrawHistoryContents();
			EditorGUILayout.EndScrollView();
		}

		private void DrawHistoryContents()
		{
			if (m_HistoryDetail == null)
			{
				GUILayout.Label("Select a history entry.", m_ParagraphStyle);
				return;
			}

			GUILayout.Label(m_HistoryDetail.Detail, m_ParagraphStyle);
			if (m_HistoryDetail.CommandHistory.IsValid && ActionButton("Open inputs"))
			{
				if (Commands.TryGetDescriptor(m_HistoryDetail.Command, out var descriptor))
				{
					// Resolve from the history identity, independently of the current list page.
					m_CommandHistory = new DebugCommandHistoryPage(
						new DebugPageInfo(0, 1, false, true, 1, false),
						DexcCommandRuntime
							.History.GetHistorySnapshot()
							.Where(pair => pair.Value.Id == m_HistoryDetail.CommandHistory)
							.Select(pair => pair.Value)
					);
					ReuseCommandHistory(m_HistoryDetail.CommandHistory);
				}
			}
		}

		private void DrawDisplayListPane()
		{
			if (m_DisplayFrame == null)
			{
				GUILayout.Label("No completed Display frame", m_ParagraphStyle);
				return;
			}

			m_DisplayScroll = EditorGUILayout.BeginScrollView(m_DisplayScroll);
			foreach (var subject in m_DisplayFrame.Subjects)
			{
				if (SelectionButton(subject.Category + "/" + subject.Label, subject.Id == m_SelectedSubject))
				{
					m_SelectedSubject = subject.Id;
					m_WorkspacePart = WorkspacePart.Detail;
				}
			}

			EditorGUILayout.EndScrollView();
		}

		private void DrawDisplayDetailPane()
		{
			m_RightScroll = EditorGUILayout.BeginScrollView(m_RightScroll);
			var subject = m_DisplayFrame?.Subjects.FirstOrDefault(value => value.Id == m_SelectedSubject);
			if (subject != null)
			{
				DrawSubjectDetail(subject);
			}
			else
			{
				DrawSnapshotSummary();
			}

			EditorGUILayout.EndScrollView();
		}

		private void DrawSnapshotSummary()
		{
			if (m_DisplayFrame == null)
			{
				GUILayout.Label("No completed Display frame", m_ParagraphStyle);
				return;
			}

			GUILayout.Label("Frame " + m_DisplayFrame.Frame + " · Dropped " + m_DisplayFrame.DroppedCount, m_ParagraphStyle);
			foreach (var visual in m_DisplayFrame.Globals)
			{
				GUILayout.Label(visual.Kind + " · " + visual.Path, m_ParagraphStyle);
			}

			foreach (var subject in m_DisplayFrame.Subjects)
			{
				GUILayout.Label(subject.Label, m_HeadingStyle);
				DrawSubjectDetail(subject);
			}
		}
	}
}
