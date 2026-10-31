using System;
using Dexc;
using UnityEditor;
using UnityEngine;

namespace Dexc
{
	/// <summary>
	/// Draws a <see cref="DebugPanel"/> with EditorGUI. Panels come from two places that have nothing else in common - the
	/// project's registered panels and the boot screen - so the drawing lives here rather than in either window.
	/// </summary>
	internal static class DexcEditorWidgets
	{
		internal static void DrawPanel(DebugPanel panel, GUIStyle paragraph)
		{
			if (panel == null)
				return;
			foreach (var widget in panel.Widgets)
				DrawWidget(widget, paragraph);
		}

		internal static void DrawWidget(DebugWidget widget, GUIStyle paragraph)
		{
			// A widget can say it is unavailable right now; the row stays visible so its value is still readable.
			using (new EditorGUI.DisabledScope(!widget.Enabled))
			{
				switch (widget)
				{
					case DebugLabelWidget label:
						EditorGUILayout.LabelField(label.Label, label.Text);
						break;
					case DebugToggleWidget toggle:
						Edit(() => EditorGUILayout.Toggle(toggle.Label, toggle.Value), value => toggle.Value = value);
						break;
					case DebugChoiceWidget choice:
						Edit(() => EditorGUILayout.Popup(choice.Label, choice.Index, ToArray(choice.Options)), value => choice.Index = value);
						break;
					case DebugNumberWidget number:
						DrawNumber(number);
						break;
					case DebugTextWidget text:
						Edit(() => EditorGUILayout.TextField(text.Label, text.Value), value => text.Value = value);
						break;
					case DebugButtonWidget button:
						if (GUILayout.Button(button.Label))
							Write(button.Invoke);
						break;
				}
			}

			if (!string.IsNullOrEmpty(widget.Description))
				EditorGUILayout.LabelField(" ", widget.Description, paragraph);
		}

		private static void DrawNumber(DebugNumberWidget number)
		{
			var bounded = !double.IsInfinity(number.Minimum) && !double.IsInfinity(number.Maximum);
			if (number.Integer)
			{
				// The int slider only for a range an int holds: a uint or long item's own range does not fit, and casting it
				// would write a different value back.
				if (bounded && number.Minimum >= int.MinValue && number.Maximum <= int.MaxValue)
					Edit(
						() => EditorGUILayout.IntSlider(number.Label, (int)number.Value, (int)number.Minimum, (int)number.Maximum),
						value => number.Value = value
					);
				// Past the long range, as a large ulong is, a long field would show a different number; the value is a double.
				else if (number.Minimum >= long.MinValue && number.Maximum <= long.MaxValue)
					Edit(() => EditorGUILayout.LongField(number.Label, (long)number.Value), value => number.Value = value);
				else
					Edit(() => EditorGUILayout.DoubleField(number.Label, number.Value), value => number.Value = value);
				return;
			}
			if (bounded)
			{
				Edit(
					() => EditorGUILayout.Slider(number.Label, (float)number.Value, (float)number.Minimum, (float)number.Maximum),
					value => number.Value = value
				);
				return;
			}
			Edit(() => EditorGUILayout.DoubleField(number.Label, number.Value), value => number.Value = value);
		}

		/// <summary>
		/// Draws a control and writes its value back only when the user changed it: a widget's setter is project code that
		/// saves and notifies, and the window repaints every frame.
		/// </summary>
		private static void Edit<T>(Func<T> draw, Action<T> write)
		{
			EditorGUI.BeginChangeCheck();
			var value = default(T);
			var drawn = false;
			try
			{
				value = draw();
				drawn = true;
			}
			catch (Exception exception)
			{
				Debug.LogException(exception);
			}
			if (EditorGUI.EndChangeCheck() && drawn)
				Write(() => write(value));
		}

		/// <summary>
		/// A widget writes through to whatever declared it, which is project code. A throw there is the project's problem
		/// and must not take the window down with it.
		/// </summary>
		private static void Write(Action action)
		{
			try
			{
				action();
			}
			catch (Exception exception)
			{
				Debug.LogException(exception);
			}
		}

		private static string[] ToArray(System.Collections.Generic.IReadOnlyList<string> options)
		{
			var array = new string[options.Count];
			for (var index = 0; index < options.Count; index++)
				array[index] = options[index];
			return array;
		}
	}
}
