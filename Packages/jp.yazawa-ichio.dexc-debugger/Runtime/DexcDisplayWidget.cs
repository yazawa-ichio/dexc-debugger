using System;
using System.Collections.Generic;
using UnityEngine;

namespace Dexc
{
	/// <summary>
	/// A HUD entry drawn every frame from a text provider. Register with <see cref="DebugDisplay.RegisterWidget"/>;
	/// visibility follows the same Display path filters as any other HUD label.
	/// </summary>
	public sealed class DebugDisplayWidget
	{
		readonly Func<string> m_Text;

		public DebugDisplayWidget(string path, Func<string> text, Color? color = null)
		{
			if (string.IsNullOrWhiteSpace(path))
				throw new ArgumentException("Path must not be empty.", nameof(path));
			Path = path;
			m_Text = text ?? throw new ArgumentNullException(nameof(text));
			Color = color ?? UnityEngine.Color.white;
		}

		public string Path { get; }
		public Color Color { get; set; }

		/// <summary>Set to false to keep the widget registered but hidden.</summary>
		public bool Enabled { get; set; } = true;

		internal string Read()
		{
			return m_Text() ?? string.Empty;
		}
	}

	internal static class DexcDisplayWidgets
	{
		static List<DebugDisplayWidget> Widgets => DexcRoot.Current.DisplayWidgets;
		static HashSet<DebugDisplayWidget> Reported => DexcRoot.Current.ReportedDisplayWidgets;

		internal static void Register(DebugDisplayWidget widget)
		{
			if (widget == null)
				throw new ArgumentNullException(nameof(widget));
			if (!Widgets.Contains(widget))
				Widgets.Add(widget);
		}

		internal static void Unregister(DebugDisplayWidget widget)
		{
			Widgets.Remove(widget);
			Reported.Remove(widget);
		}

		internal static int Count => Widgets.Count;

		internal static void Clear()
		{
			Widgets.Clear();
			Reported.Clear();
		}

		/// <summary>Draws every enabled widget as a one-frame HUD label. A failing provider is logged once and skipped.</summary>
		internal static void Draw()
		{
			if (Widgets.Count == 0 || !DexcDebugger.IsInitialized)
				return;
			foreach (var widget in Widgets.ToArray())
			{
				if (!widget.Enabled)
					continue;
				string text;
				try
				{
					text = widget.Read();
				}
				catch (Exception exception)
				{
					if (Reported.Add(widget))
						UnityEngine.Debug.LogWarning("Dexc display widget '" + widget.Path + "' failed: " + exception.Message);
					continue;
				}
				DebugDisplay.DrawLabel(widget.Path, text, widget.Color);
			}
		}
	}
}
