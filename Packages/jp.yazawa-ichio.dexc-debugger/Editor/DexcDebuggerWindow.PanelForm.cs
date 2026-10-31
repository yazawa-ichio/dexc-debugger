using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Dexc
{
	/// <summary>
	/// The Panels page: what the project registered through <see cref="DebugPanels"/>. Those come from running game code,
	/// so with nothing playing the list is empty and says so. Start configurations are a different thing and live in the
	/// Dexc Boot window.
	/// </summary>
	public sealed partial class DexcDebuggerWindow
	{
		private string m_SelectedPanelTitle = string.Empty;
		private Vector2 m_PanelDetailScroll;

		private DebugPanel SelectedPanel =>
			DebugPanels.All.FirstOrDefault(panel => panel.Title == m_SelectedPanelTitle) ?? DebugPanels.All.FirstOrDefault();

		private void DrawPanelListPane()
		{
			var panels = DebugPanels.All;
			if (panels.Count == 0)
			{
				GUILayout.Label("No panel is registered. Panels come from running game code.", m_ParagraphStyle);
				return;
			}

			m_LeftScroll = EditorGUILayout.BeginScrollView(m_LeftScroll);
			foreach (var panel in panels)
			{
				// A button, as the other lists use: a toggle stays true for the selected panel on every event, which would
				// switch the narrow layout back to the detail as soon as the list was shown.
				if (SelectionButton(panel.Title + "  ·  " + panel.Widgets.Count, ReferenceEquals(panel, SelectedPanel)))
				{
					m_SelectedPanelTitle = panel.Title;
					m_WorkspacePart = WorkspacePart.Detail;
				}
			}
			EditorGUILayout.EndScrollView();
		}

		private void DrawPanelDetailPane()
		{
			var panel = SelectedPanel;
			if (panel == null)
			{
				GUILayout.Label("Select a panel.", m_ParagraphStyle);
				return;
			}

			m_PanelDetailScroll = EditorGUILayout.BeginScrollView(m_PanelDetailScroll);
			EditorGUILayout.LabelField(panel.Title, EditorStyles.boldLabel);
			DexcEditorWidgets.DrawPanel(panel, m_ParagraphStyle);
			EditorGUILayout.EndScrollView();
		}
	}
}
