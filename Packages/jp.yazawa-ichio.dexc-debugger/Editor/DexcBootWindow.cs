using System;
using System.Linq;
using Dexc;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Dexc
{
	/// <summary>
	/// Picks a boot set, edits it and starts the game with it, without going through the debug boot scene. It is its own
	/// window because it is about getting the game running, while the Dexc Debugger window is about a game that already
	/// is. The selection it edits belongs to this window, so entering and leaving Play Mode does not throw it away.
	/// </summary>
	public sealed class DexcBootWindow : EditorWindow
	{
		[SerializeField]
		private string m_Tag = string.Empty;

		[NonSerialized]
		private DebugBootSelection m_Selection;

		[NonSerialized]
		private string m_Error;

		private const float ListWidth = 200f;

		private Vector2 m_ListScroll;
		private Vector2 m_DetailScroll;
		private GUIStyle m_Paragraph;
		private GUIStyle m_SetRow;

		[MenuItem("Window/Dexc/Dexc Boot")]
		private static void Open()
		{
			GetWindow<DexcBootWindow>().Show();
		}

		private void OnEnable()
		{
			titleContent = new GUIContent("Dexc Boot");
			minSize = new Vector2(380f, 220f);
		}

		private void OnGUI()
		{
			EnsureStyles();
			if (!DexcDebugger.IsEnabled)
			{
				GUILayout.Label("Dexc is disabled in this project.", m_Paragraph);
				return;
			}
			// One boot screen at a time: while the game runs, the running one owns the values.
			if (EditorApplication.isPlaying)
			{
				GUILayout.Label("The game is running. Stop it to choose a start configuration.", m_Paragraph);
				return;
			}

			using (new EditorGUILayout.HorizontalScope())
			{
				DrawSets();
				DrawDetail();
			}
		}

		private void DrawSets()
		{
			using (new EditorGUILayout.VerticalScope(GUILayout.Width(ListWidth)))
			{
				using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
				{
					GUILayout.Label("Start Configurations", EditorStyles.miniBoldLabel);
				}
				m_ListScroll = EditorGUILayout.BeginScrollView(m_ListScroll);
				var sets = DexcDefinitions.Boot.Sets.OrderBy(set => set.Attribute.Order).ThenBy(set => set.Attribute.Tag).ToList();
				if (sets.Count == 0)
					GUILayout.Label("No set declares [DebugBootMenu].", m_Paragraph);
				foreach (var set in sets)
				{
					var title = string.IsNullOrEmpty(set.Attribute.Title) ? set.Type.Name : set.Attribute.Title;
					var content = new GUIContent(title + "\n" + set.Attribute.Scene, set.Type.FullName);
					var height = m_SetRow.CalcHeight(content, ListWidth);
					if (GUILayout.Toggle(set.Attribute.Tag == m_Tag, content, m_SetRow, GUILayout.Height(height)))
						Select(set.Attribute.Tag);
				}
				EditorGUILayout.EndScrollView();
			}
		}

		private void DrawDetail()
		{
			using (new EditorGUILayout.VerticalScope())
			{
				if (!string.IsNullOrEmpty(m_Error))
				{
					EditorGUILayout.HelpBox(m_Error, MessageType.Error);
					return;
				}
				if (m_Selection == null)
				{
					GUILayout.Label("Select a start configuration.", m_Paragraph);
					return;
				}

				m_DetailScroll = EditorGUILayout.BeginScrollView(m_DetailScroll);
				foreach (var panel in m_Selection.Panels)
				{
					EditorGUILayout.LabelField(panel.Title, EditorStyles.boldLabel);
					DexcEditorWidgets.DrawPanel(panel, m_Paragraph);
					EditorGUILayout.Space();
				}
				EditorGUILayout.EndScrollView();

				var scene = m_Selection.ResolveScene();
				using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(scene)))
				{
					if (GUILayout.Button("Start", GUILayout.Height(28f)))
						Start();
				}
				if (string.IsNullOrEmpty(scene))
					EditorGUILayout.HelpBox("This set resolved no scene, so there is nowhere to start.", MessageType.Info);
				if (GUILayout.Button("Reset to declared values"))
				{
					m_Selection.Reset();
					Repaint();
				}
			}
		}

		private void Select(string tag)
		{
			if (m_Tag == tag && m_Selection != null)
				return;
			m_Tag = tag;
			m_Error = null;
			try
			{
				m_Selection = DebugBootSelection.Open(tag);
			}
			catch (Exception exception)
			{
				m_Selection = null;
				m_Error = exception.Message;
			}
		}

		/// <summary>Hands the values over and opens the destination, then enters Play Mode.</summary>
		private void Start()
		{
			try
			{
				// Asked before anything is handed over: cancelling leaves the values unsaved and the Editor out of Play Mode.
				if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
					return;
				var started = m_Selection.Start(scene => EditorSceneManager.OpenScene(ScenePath(scene), OpenSceneMode.Single));
				if (started)
					EditorApplication.isPlaying = true;
			}
			catch (Exception exception)
			{
				m_Error = exception.Message;
			}
		}

		/// <summary>Sets name a scene the way Build Settings does; find the asset that answers to that name.</summary>
		private static string ScenePath(string scene)
		{
			foreach (var candidate in EditorBuildSettings.scenes)
			{
				if (string.Equals(System.IO.Path.GetFileNameWithoutExtension(candidate.path), scene, StringComparison.Ordinal))
					return candidate.path;
				if (string.Equals(candidate.path, scene, StringComparison.Ordinal))
					return candidate.path;
			}
			throw new InvalidOperationException("Scene \"" + scene + "\" is not in Build Settings.");
		}

		private void EnsureStyles()
		{
			if (m_Paragraph != null)
				return;
			m_Paragraph = new GUIStyle(EditorStyles.wordWrappedLabel);
			// miniButton is one line tall whatever it is handed, so a two-line row draws over the row below it.
			m_SetRow = new GUIStyle(EditorStyles.miniButton)
			{
				fixedHeight = 0f,
				wordWrap = false,
				alignment = TextAnchor.MiddleLeft,
				padding = new RectOffset(8, 8, 6, 6),
			};
		}

#if UNITY_INCLUDE_TESTS
		internal void SelectForTests(string tag) => Select(tag);

		internal DebugBootSelection SelectionForTests => m_Selection;

		internal string ErrorForTests => m_Error;
#endif
	}
}
