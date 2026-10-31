using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Dexc
{
	internal sealed class DexcEditorNumericInput
	{
		private readonly Dictionary<string, string> m_Text = new Dictionary<string, string>();
		private readonly HashSet<string> m_Invalid = new HashSet<string>();

#if UNITY_INCLUDE_TESTS
		internal string TextForTests(string key) => m_Text.TryGetValue(key, out var text) ? text : null;
#endif

		internal bool IsValid => m_Invalid.Count == 0;
#if UNITY_INCLUDE_TESTS
		internal Action<string, Rect> ControlDrawnForTests;
#endif
		internal Action<string> ErrorRequested;
		internal float RowHeight = 28f;
		private GUIStyle m_Style,
			m_ComponentStyle;
		internal string OperationError;

		internal bool IsPrefixValid(string key) =>
			!m_Invalid.Any(value => value == key || value.StartsWith(key + "/", StringComparison.Ordinal));

		internal void ClearPrefix(string key)
		{
			foreach (
				var item in m_Text.Keys.Where(value => value == key || value.StartsWith(key + "/", StringComparison.Ordinal)).ToArray()
			)
			{
				m_Text.Remove(item);
				m_Invalid.Remove(item);
			}
		}

		internal void Clear()
		{
			m_Text.Clear();
			m_Invalid.Clear();
		}

		internal bool Draw(string key, string label, DebugValue value, DebugValueKind kind, Type type, out DebugValue edited)
		{
			edited = value;
			if (!m_Text.TryGetValue(key, out var text))
				text = DexcInputText.Format(value);
			string next;
			if (m_ComponentStyle == null)
				m_ComponentStyle = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleLeft };
			if (m_Style == null)
				m_Style = new GUIStyle(EditorStyles.textField)
				{
					alignment = TextAnchor.MiddleLeft,
					padding = new RectOffset(6, 6, 4, 4),
					fixedHeight = 0,
				};
			var component = label != null && label.Length == 1 && "xyzwrgbauvwXYZWRGBAUVW".Contains(label);
			if (!string.IsNullOrEmpty(label) && !component)
			{
				GUILayout.Label(label, EditorStyles.wordWrappedLabel, GUILayout.MinWidth(0));
				GUILayout.Space(5);
			}
			using (new EditorGUILayout.HorizontalScope())
			{
				if (component)
					GUILayout.Label(label.ToUpperInvariant(), m_ComponentStyle, GUILayout.Width(36), GUILayout.Height(RowHeight));
				GUI.SetNextControlName("DexcEditorNumeric:" + key);
				next = GUILayout.TextField(text, m_Style, GUILayout.MinWidth(40f), GUILayout.Height(RowHeight));
#if UNITY_INCLUDE_TESTS
				if (Event.current.type == EventType.Repaint)
					ControlDrawnForTests?.Invoke(key, GUILayoutUtility.GetLastRect());
#endif
				var enabled = GUI.enabled;
				var hasError = m_Invalid.Contains(key) || !string.IsNullOrEmpty(OperationError);
				GUI.enabled = hasError || !enabled;
				if (
					GUILayout.Button(
						hasError ? "!"
							: !enabled ? "…"
							: "",
						GUILayout.Width(28),
						GUILayout.Height(RowHeight)
					)
				)
					ErrorRequested?.Invoke(hasError ? OperationError ?? "Invalid number or value outside the supported range." : text);
				GUI.enabled = enabled;
			}
			var changed = !string.Equals(next, text, StringComparison.Ordinal);
			if (changed)
			{
				m_Text[key] = next;
				var parsed = Parse(type, kind, next);
				if (parsed == null)
					m_Invalid.Add(key);
				else
				{
					m_Invalid.Remove(key);
					edited = parsed;
				}
			}
			GUILayout.Space(4);
			return changed && !m_Invalid.Contains(key);
		}

		internal void SetValue(string key, DebugValue value)
		{
			m_Text[key] = DexcInputText.Format(value);
			m_Invalid.Remove(key);
		}

		internal static bool IsInteger(Type type) =>
			type == typeof(sbyte)
			|| type == typeof(byte)
			|| type == typeof(short)
			|| type == typeof(ushort)
			|| type == typeof(int)
			|| type == typeof(uint)
			|| type == typeof(long)
			|| type == typeof(ulong);

		internal static DebugValue Parse(Type type, DebugValueKind kind, string text)
		{
			type = type == null ? null : Nullable.GetUnderlyingType(type) ?? type;
			var value = DexcInputText.ParseScalar(kind, text);
			return DexcInputText.FitsType(value, type) ? value : null;
		}
	}
}
