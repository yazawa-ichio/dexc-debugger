using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Dexc
{
	internal enum DexcOverlayLabelStyle
	{
		Box,
		Default,
	}

	internal interface IDexcOverlayDrawSink
	{
		void Label(Rect rect, string text, DexcOverlayLabelStyle style);
	}

	internal sealed class DexcGuiOverlayDrawSink : IDexcOverlayDrawSink
	{
		internal static readonly DexcGuiOverlayDrawSink Instance = new DexcGuiOverlayDrawSink();

		public void Label(Rect rect, string text, DexcOverlayLabelStyle style)
		{
			GUI.Label(rect, text, style == DexcOverlayLabelStyle.Box ? GUI.skin.GetStyle("box") : GUI.skin.GetStyle("label"));
		}
	}

	/// <summary>
	/// Draws the text layers of Display: HUD (global labels and values) and Overlay (subject labels and values). The
	/// third layer, World, is drawn by <see cref="DexcPlayerRenderer"/>. All three live in the Runtime assembly, so
	/// Display never needs the DebugUI menu and nothing in Runtime has to reach across the assembly boundary.
	/// </summary>
	internal static class DexcDisplayOverlay
	{
		/// <summary>Draw depth of the text layers. Higher than the menu's, so the menu stays in front (lower depth draws on top).</summary>
		internal const int GuiDepth = 1;

		internal static DebugDisplayFrameSnapshot Frame
		{
			get => DexcRoot.Current.DisplayFrame;
			set => DexcRoot.Current.DisplayFrame = value;
		}

		internal static void Draw()
		{
			ReadFrame();
			Render(DexcGuiOverlayDrawSink.Instance);
		}

		private static void ReadFrame()
		{
			if (!DexcDebugger.IsInitialized)
			{
				Frame = null;
				return;
			}

			DebugDisplay.TryGetCompletedFrame(out DexcRoot.Current.DisplayFrame);
		}

		private static void Render(IDexcOverlayDrawSink sink)
		{
			var frame = Frame;
			if (frame == null)
				return;
			var visibility = DexcDisplayVisibility.Capture();
			var labels = new List<Label>();
			if (visibility.Layer("HUD"))
			{
				labels.Add(
					new Label(
						new Rect(12f, 12f, 260f, 30f),
						"FPS " + (1f / Mathf.Max(Time.unscaledDeltaTime, 0.0001f)).ToString("F1") + "  Frame " + frame.Frame,
						DexcOverlayLabelStyle.Box
					)
				);
			}

			var y = 46f;
			foreach (var visual in frame.Globals.Where(visual => IsDisplayText(visual) && visibility.IsVisible(visual, false)).Take(8))
			{
				labels.Add(
					new Label(
						new Rect(12f, y, 360f, 22f),
						visual.Kind == DebugDisplayPrimitiveKind.Label ? visual.Text : DexcInputText.Format(visual.Value),
						DexcOverlayLabelStyle.Default
					)
				);
				y += 22f;
			}

			foreach (var subject in frame.Subjects)
			{
				foreach (var visual in subject.Visuals.Where(visual => IsDisplayText(visual) && visibility.IsVisible(visual, true)).Take(8))
				{
					labels.Add(
						new Label(
							new Rect(12f, y, 360f, 22f),
							subject.Label
								+ " · "
								+ (visual.Kind == DebugDisplayPrimitiveKind.Label ? visual.Text : DexcInputText.Format(visual.Value)),
							DexcOverlayLabelStyle.Default
						)
					);
					y += 22f;
				}
			}

			var mirror = MovesAside(labels, DexcRoot.Current.MenuPanel?.Invoke(), Screen.width);
			foreach (var label in labels)
				sink.Label(mirror ? Mirror(label.Rect, Screen.width) : label.Rect, label.Text, label.Style);
		}

		/// <summary>
		/// The menu draws in front of the text, so where its panel covers the text the text goes to the other side of the
		/// screen. It stays where it is when the other side is covered too.
		/// </summary>
		internal static bool MovesAside(IReadOnlyList<Label> labels, Rect? panel, float screenWidth)
		{
			if (!panel.HasValue)
				return false;
			return labels.Any(label => label.Rect.Overlaps(panel.Value))
				&& !labels.Any(label => Mirror(label.Rect, screenWidth).Overlaps(panel.Value));
		}

		private static Rect Mirror(Rect rect, float screenWidth) => new Rect(screenWidth - rect.xMax, rect.y, rect.width, rect.height);

		internal readonly struct Label
		{
			internal Label(Rect rect, string text, DexcOverlayLabelStyle style)
			{
				Rect = rect;
				Text = text;
				Style = style;
			}

			internal Rect Rect { get; }
			internal string Text { get; }
			internal DexcOverlayLabelStyle Style { get; }
		}

		private static bool IsDisplayText(DebugDisplayVisualSnapshot visual) =>
			visual.Kind == DebugDisplayPrimitiveKind.Label || visual.Kind == DebugDisplayPrimitiveKind.Value;

#if UNITY_INCLUDE_TESTS
		internal static DebugDisplayFrameSnapshot ReadFrameForTests()
		{
			ReadFrame();
			return Frame;
		}

		internal static int RenderForTests()
		{
			var sink = new TestOverlayDrawSink();
			Render(sink);
			return sink.LabelCount;
		}

		private sealed class TestOverlayDrawSink : IDexcOverlayDrawSink
		{
			internal int LabelCount;

			public void Label(Rect rect, string text, DexcOverlayLabelStyle style)
			{
				LabelCount++;
			}
		}
#endif
	}
}
