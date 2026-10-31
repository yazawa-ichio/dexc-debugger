using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Dexc
{
	public sealed partial class DexcDebuggerWindow
	{
		private int m_CommandOffset,
			m_RegisteredOffset,
			m_HierarchyOffset;
		private readonly Stack<DebugObjectHandle> m_EditorParents = new Stack<DebugObjectHandle>();
		private DebugInspectorSearchId m_EditorSearch;
		private bool m_EditorSearchType;

#if UNITY_INCLUDE_TESTS
		internal bool NextCommandPageForTests()
		{
			if (m_Commands == null || !m_Commands.Page.HasMore)
			{
				return false;
			}

			m_CommandOffset += m_Commands.Items.Count;
			ReadCommands();
			return true;
		}
#endif

		private void DrawCommandListPane()
		{
			GUILayout.Label("Command", m_HeadingStyle);
			var search = GUILayout.TextField(m_CommandSearch, m_InputStyle, GUILayout.Height(EditorRowHeight));
			if (search != m_CommandSearch)
			{
				m_CommandSearch = search;
				m_CommandOffset = 0;
				ReadCommands();
			}

			if (m_Commands == null)
			{
				ReadCommands();
			}

			m_CommandScroll = EditorGUILayout.BeginScrollView(m_CommandScroll);
			if (m_Commands != null)
			{
				DrawCommandCategoryTree();
			}

			EditorGUILayout.EndScrollView();
			if (m_Commands != null)
			{
				var before = m_CommandOffset;
				DrawPager(ref m_CommandOffset, m_Commands.Page, FeaturePageSize);
				if (before != m_CommandOffset)
				{
					ReadCommands();
				}
			}
		}

		private void CancelEditorSearch()
		{
			if (m_EditorSearch.IsValid && DexcDebugger.IsInitialized)
			{
				DebugInspector.CancelSearch(m_EditorSearch);
			}

			m_EditorSearch = default;
		}

		private void DrawInspectorSourceToolbar()
		{
			using (new EditorGUILayout.HorizontalScope())
			{
				foreach (InspectorSource source in Enum.GetValues(typeof(InspectorSource)))
				{
					if (ActionButton(source.ToString()) && m_InspectorSource != source)
					{
						m_InspectorSource = source;
						CancelEditorSearch();
						m_RegisteredOffset = m_HierarchyOffset = 0;
					}
				}
			}

			var search = GUILayout.TextField(m_InspectorSearch, m_InputStyle, GUILayout.Height(EditorRowHeight));
			if (search != m_InspectorSearch)
			{
				m_InspectorSearch = search;
				m_RegisteredOffset = m_HierarchyOffset = 0;
				CancelEditorSearch();
			}

			if (m_InspectorSource == InspectorSource.Scene && ActionButton(m_EditorSearchType ? "Search: Component type" : "Search: Name"))
			{
				m_EditorSearchType = !m_EditorSearchType;
				m_HierarchyOffset = 0;
				CancelEditorSearch();
			}
		}

		private void DrawRegisteredList()
		{
			m_Registered = DebugInspector.GetRegistered(
				new DebugListQuery("", m_InspectorSearch, new DebugPageRequest(m_RegisteredOffset, BoundedPageSize(PageSize)))
			);
			if (m_Registered.Entries.Count == 0 && m_RegisteredOffset > 0)
			{
				m_RegisteredOffset = 0;
				return;
			}

			foreach (var entry in m_Registered.Entries)
			{
				if (
					SelectionButton(
						entry.Label + (string.IsNullOrEmpty(entry.Path) ? "" : "\n" + entry.Path),
						entry.Target == m_SelectedTarget
					)
				)
				{
					SelectTarget(entry.Target, entry.Scene, default);
				}
			}

			if (m_Registered.Entries.Count == 0)
			{
				GUILayout.Label("No registered objects", m_ParagraphStyle);
			}

			DrawPager(ref m_RegisteredOffset, m_Registered.Page, PageSize);
		}

		private void DrawHierarchyPane()
		{
			if (!m_SelectedScene.IsValid)
			{
				return;
			}

			if (!string.IsNullOrEmpty(m_InspectorSearch))
			{
				if (!m_EditorSearch.IsValid)
				{
					m_EditorSearch = DebugInspector
						.StartSearch(
							new DebugInspectorSearchQuery(
								m_SelectedScene,
								m_EditorSearchType ? "" : m_InspectorSearch,
								m_EditorSearchType ? m_InspectorSearch : "",
								null
							)
						)
						.Value;
				}

				var results = DebugInspector.GetSearchResults(
					m_EditorSearch,
					new DebugPageRequest(m_HierarchyOffset, BoundedPageSize(MaxSceneRows))
				);
				GUILayout.Label(results.State + (results.IsTruncated ? " · Results limited" : ""), m_ParagraphStyle);
				foreach (var entry in results.Entries)
				{
					if (SelectionButton(entry.Path + "\n" + entry.ComponentType, entry.Target == m_SelectedTarget))
					{
						SelectTarget(entry.Target, entry.Scene, default);
					}
				}

				DrawPager(ref m_HierarchyOffset, results.Page, MaxSceneRows);
				return;
			}

			if (m_SelectedParent.IsValid && ActionButton("Parent"))
			{
				m_SelectedParent = m_EditorParents.Count > 0 ? m_EditorParents.Pop() : default;
				m_HierarchyOffset = 0;
			}

			m_Hierarchy = DebugInspector.GetHierarchyChildren(
				new DebugHierarchyChildrenQuery(
					m_SelectedScene,
					m_SelectedParent,
					new DebugPageRequest(m_HierarchyOffset, BoundedHierarchyPageSize(MaxSceneRows))
				)
			);
			if (m_Hierarchy.Entries.Count == 0 && m_HierarchyOffset > 0)
			{
				m_HierarchyOffset = 0;
				return;
			}

			foreach (var entry in m_Hierarchy.Entries)
			{
				using (new EditorGUILayout.HorizontalScope())
				{
					if (SelectionButton(entry.Label, entry.InspectorTarget == m_SelectedTarget))
					{
						SelectTarget(entry.InspectorTarget, entry.Scene, entry.Target);
					}

					if (entry.HasChildren && ActionButton("›", GUILayout.Width(28)))
					{
						m_EditorParents.Push(m_SelectedParent);
						m_SelectedParent = entry.Target;
						m_HierarchyOffset = 0;
					}
				}
			}

			var before = m_HierarchyOffset;
			DrawPager(ref m_HierarchyOffset, m_Hierarchy.Page, BoundedHierarchyPageSize(MaxSceneRows));
			if (before != m_HierarchyOffset)
			{
				m_MiddleScroll = Vector2.zero;
			}
		}

		private void RefreshEditorCommandValues(DebugCommandFieldId edited = default)
		{
			if (m_SelectedCommand == null || !m_SelectedDraft.IsValid)
			{
				return;
			}

			foreach (var field in m_SelectedCommand.Fields.Where(field => field.IsInput && field.Id != edited))
			{
				if (m_CommandRejectedInput.Contains(field.Id) || !m_CommandNumericInput.IsPrefixValid(field.Id.ToString()))
				{
					continue;
				}

				var next = DexcCommandRuntime.Drafts.GetInputValue(m_SelectedDraft, field.Id);
				if (m_CommandValues.TryGetValue(field.Id, out var previous) && Equals(previous, next))
				{
					continue;
				}

				m_CommandValues[field.Id] = next;
				m_CommandNumericInput.ClearPrefix(field.Id.ToString());
			}
		}
	}
}
