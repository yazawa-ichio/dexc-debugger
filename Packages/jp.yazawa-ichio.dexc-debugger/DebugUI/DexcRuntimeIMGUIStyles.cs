using System;
using System.Collections.Generic;
using UnityEngine;

namespace Dexc
{
	internal sealed class DexcRuntimeIMGUIStyles : IDisposable
	{
		const int ScrollbarBarWidth = 12;
		const int ScrollbarGap = 4;

		readonly List<Texture2D> m_Textures = new List<Texture2D>();
		readonly Texture2D m_ScrollbarTrack;
		readonly Texture2D m_ScrollbarThumb;
		readonly Texture2D m_ScrollbarThumbHover;
		float m_ScrollbarGrab;

		DexcRuntimeIMGUIStyles(DexcRuntimeUISemanticPalette palette, Font font)
		{
			Palette = palette;
			var panel = CreateFill("Panel", palette.Panel);
			var header = CreateFill("Header", palette.Header);
			var row = CreateBordered("Row", palette.Row, palette.Header, 1);
			var hover = CreateBordered("Hover", palette.Selection, palette.SecondaryText, 1);
			var pressed = CreateBordered("Pressed", palette.Accent, palette.PrimaryText, 1);
			var selected = CreateBordered("Selected", palette.Selection, palette.PrimaryText, 1);
			var focused = CreateBordered("Focused", palette.Selection, palette.Focus, 2);
			var accent = CreateBordered("Accent", palette.Accent, palette.Focus, 1);
			// A disabled control is a flat fill: an edge would read as the focus outline.
			var disabled = CreateBordered("Disabled", palette.Disabled, palette.Disabled, 1);
			var chosen = CreateBordered("Chosen", palette.Selection, palette.Selection, 1);
			var error = CreateBordered("Error", palette.Error, palette.PrimaryText, 1);
			var warning = CreateBordered("Warning", palette.Row, palette.Warning, 1);

			Panel = Box(panel, palette.PrimaryText, 8);
			Header = Box(header, palette.PrimaryText, 4);
			Footer = Box(header, palette.PrimaryText, 4);
			Field = Box(row, palette.PrimaryText, 8);
			ErrorBox = Box(error, palette.PrimaryText, 8);
			WarningBox = Box(warning, palette.Warning, 8);
			RowButton = Button(row, hover, pressed, focused, disabled, palette.PrimaryText, font);
			SelectedRowButton = Button(selected, hover, pressed, focused, disabled, palette.PrimaryText, font);
			SelectedRowButton.fontStyle = FontStyle.Normal;
			PrimaryButton = Button(accent, hover, pressed, focused, disabled, palette.PrimaryText, font);
			PrimaryButton.fontStyle = FontStyle.Normal;
			PrimaryButton.alignment = TextAnchor.MiddleCenter;
			SecondaryButton = Button(row, hover, pressed, focused, disabled, palette.PrimaryText, font);
			SecondaryButton.alignment = TextAnchor.MiddleCenter;
			// The two halves of a switch such as Single | Split, chosen and not; each keeps its word on one line. The chosen half
			// is filled, not outlined: the outline is the focus.
			SelectedSecondaryButton = Button(chosen, hover, pressed, focused, disabled, palette.PrimaryText, font);
			SelectedSecondaryButton.fontStyle = FontStyle.Normal;
			SelectedSecondaryButton.alignment = TextAnchor.MiddleCenter;
			SelectedSecondaryButton.wordWrap = false;
			SwitchButton = new GUIStyle(SecondaryButton) { wordWrap = false };
			ErrorButton = Button(error, hover, pressed, focused, disabled, palette.PrimaryText, font);
			ErrorButton.alignment = TextAnchor.MiddleCenter;
			WarningButton = Button(warning, hover, pressed, focused, disabled, palette.Warning, font);
			WarningButton.alignment = TextAnchor.MiddleCenter;
			SliderTrack = new GUIStyle { padding = new RectOffset(6, 6, 0, 0) };
			SliderThumb = new GUIStyle { fixedWidth = 12, fixedHeight = 12 };
			SliderThumb.normal.background = accent;
			SliderThumb.hover.background = hover;
			SliderThumb.active.background = pressed;
			m_ScrollbarTrack = header;
			m_ScrollbarThumb = CreateFill("Scrollbar Thumb", Color.Lerp(palette.Header, palette.SecondaryText, 0.45f));
			m_ScrollbarThumbHover = CreateFill("Scrollbar Thumb Hover", Color.Lerp(palette.Header, palette.SecondaryText, 0.75f));
			DisabledButton = Button(disabled, disabled, disabled, disabled, disabled, palette.SecondaryText, font);
			DisabledButton.alignment = TextAnchor.MiddleCenter;
			TextField = new GUIStyle
			{
				font = font ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),
				fontSize = DexcRuntimeFormLayout.FontSize,
				wordWrap = false,
				clipping = TextClipping.Clip,
				alignment = TextAnchor.MiddleLeft,
				border = new RectOffset(2, 2, 2, 2),
				padding = new RectOffset(6, 6, 4, 4),
			};
			TextField.normal.background = row;
			TextField.normal.textColor = palette.PrimaryText;
			TextField.focused.background = focused;
			TextField.focused.textColor = palette.PrimaryText;
			MultilineTextField = new GUIStyle(TextField)
			{
				wordWrap = true,
				clipping = TextClipping.Clip,
				alignment = TextAnchor.UpperLeft,
			};
			PrimaryLabel = Label(palette.PrimaryText, font, false, FontStyle.Normal);
			PrimaryLabel.alignment = TextAnchor.MiddleLeft;
			FormLabel = new GUIStyle(PrimaryLabel)
			{
				wordWrap = true,
				clipping = TextClipping.Overflow,
				alignment = TextAnchor.UpperLeft,
			};
			ReadOnlyValue = new GUIStyle(TextField);
			ComponentLabel = new GUIStyle(PrimaryLabel) { alignment = TextAnchor.MiddleLeft };
			SecondaryLabel = Label(palette.SecondaryText, font, true, FontStyle.Normal);
			Breadcrumb = Label(palette.SecondaryText, font, true, FontStyle.Normal);
			Breadcrumb.fontSize = DexcRuntimeFormLayout.SmallFontSize;
			Breadcrumb.wordWrap = false;
			Breadcrumb.clipping = TextClipping.Clip;
			Section = Label(palette.PrimaryText, font, true, FontStyle.Bold);
			SuccessLabel = Label(palette.Success, font, true, FontStyle.Bold);
			WarningLabel = Label(palette.Warning, font, true, FontStyle.Normal);
			ErrorLabel = Label(palette.PrimaryText, font, true, FontStyle.Normal);

			// A field's row is a band a step lighter than the panel; the value controls inside keep the row colour.
			FieldRow = Box(header, palette.PrimaryText, 0);
			FieldRow.padding = new RectOffset(8, 6, 4, 4);
			FocusedFieldRow = Box(CreateBordered("Focused Row", palette.Selection, palette.Focus, 2), palette.PrimaryText, 0);
			FocusedFieldRow.padding = FieldRow.padding;
			var errorText = palette.FieldErrorText;
			ErrorTextField = new GUIStyle(TextField);
			ErrorTextField.normal.background = CreateBordered("Error Field", palette.Row, errorText, 1);
			ErrorTextField.focused.background = CreateBordered("Error Field Focused", palette.Selection, errorText, 2);
			CycleValue = new GUIStyle(TextField) { alignment = TextAnchor.MiddleCenter };
			CycleButton = new GUIStyle(SecondaryButton) { padding = new RectOffset(0, 0, 0, 0), fontSize = DexcRuntimeFormLayout.SymbolFontSize };
			Title = Label(palette.PrimaryText, font, false, FontStyle.Bold);
			Title.fontSize = DexcRuntimeFormLayout.TitleFontSize;
			Title.alignment = TextAnchor.MiddleLeft;
			Note = Label(palette.SecondaryText, font, false, FontStyle.Normal);
			Note.fontSize = DexcRuntimeFormLayout.NoteFontSize;
			FieldError = Label(errorText, font, true, FontStyle.Normal);
			FieldError.fontSize = DexcRuntimeFormLayout.SmallFontSize;
			Description = Label(palette.SecondaryText, font, true, FontStyle.Normal);
			Description.fontSize = DexcRuntimeFormLayout.SmallFontSize;
			Trailing = Label(palette.SecondaryText, font, false, FontStyle.Normal);
			Trailing.fontSize = DexcRuntimeFormLayout.TrailingFontSize;
			Trailing.alignment = TextAnchor.MiddleRight;
			ListMain = Label(palette.PrimaryText, font, true, FontStyle.Normal);
			ListDetail = Label(palette.SecondaryText, font, true, FontStyle.Normal);
			ListDetail.fontSize = DexcRuntimeFormLayout.SmallFontSize;
		}

		internal DexcRuntimeUISemanticPalette Palette { get; }
		internal GUIStyle Panel { get; }
		internal GUIStyle Header { get; }
		internal GUIStyle Footer { get; }
		internal GUIStyle Field { get; }
		internal GUIStyle ErrorBox { get; }
		internal GUIStyle WarningBox { get; }
		internal GUIStyle RowButton { get; }
		internal GUIStyle SelectedRowButton { get; }
		internal GUIStyle PrimaryButton { get; }
		internal GUIStyle SecondaryButton { get; }
		internal GUIStyle SelectedSecondaryButton { get; }
		internal GUIStyle SwitchButton { get; }
		internal GUIStyle ErrorButton { get; }
		internal GUIStyle SliderTrack { get; }
		internal GUIStyle SliderThumb { get; }
		internal GUIStyle WarningButton { get; }
		internal GUIStyle DisabledButton { get; }
		internal GUIStyle TextField { get; }

		internal GUIStyle MultilineTextField { get; }
		internal GUIStyle PrimaryLabel { get; }
		internal GUIStyle FormLabel { get; }
		internal GUIStyle ReadOnlyValue { get; }
		internal GUIStyle ComponentLabel { get; }
		internal GUIStyle SecondaryLabel { get; }
		internal GUIStyle Breadcrumb { get; }
		internal GUIStyle Section { get; }
		internal GUIStyle SuccessLabel { get; }
		internal GUIStyle WarningLabel { get; }
		internal GUIStyle ErrorLabel { get; }
		internal GUIStyle FieldRow { get; }
		internal GUIStyle FocusedFieldRow { get; }
		internal GUIStyle ErrorTextField { get; }

		/// <summary>The value between a choice's ‹ and › buttons.</summary>
		internal GUIStyle CycleValue { get; }
		internal GUIStyle CycleButton { get; }

		/// <summary>The page title in the header, under the place in the menu.</summary>
		internal GUIStyle Title { get; }

		/// <summary>The small tag under a field's label, such as "Immediate".</summary>
		internal GUIStyle Note { get; }

		/// <summary>An input error, written under the value it is about.</summary>
		internal GUIStyle FieldError { get; }
		internal GUIStyle Description { get; }

		/// <summary>A list entry's count or result at its right end.</summary>
		internal GUIStyle Trailing { get; }
		internal GUIStyle ListMain { get; }
		internal GUIStyle ListDetail { get; }

		internal static DexcRuntimeIMGUIStyles Create(DexcRuntimeUISkinSnapshot skin)
		{
			var palette = DexcRuntimeUISemanticPalette.FromSnapshot(skin);
			return new DexcRuntimeIMGUIStyles(palette, skin == null ? null : skin.Font);
		}

		/// <summary>Width the scrollbar of <see cref="BeginScrollView"/> takes at the right of the view, gap included.</summary>
		internal float ScrollbarWidth => ScrollbarBarWidth + ScrollbarGap;

		/// <summary>
		/// A vertical scroll view whose scrollbar <see cref="EndScrollView"/> draws in the palette. IMGUI's own scrollbar
		/// takes its arrows from the built-in skin, so it is hidden and the view only clamps and wheels the position.
		/// The content must leave <see cref="ScrollbarWidth"/> free at the right.
		/// </summary>
		internal Vector2 BeginScrollView(Vector2 position, float width, float height)
		{
			return GUILayout.BeginScrollView(
				position,
				false,
				false,
				GUIStyle.none,
				GUIStyle.none,
				GUIStyle.none,
				GUILayout.Width(width),
				GUILayout.Height(height)
			);
		}

		/// <summary>
		/// Ends <see cref="BeginScrollView"/> right after the content's layout group, and draws and drives the scrollbar.
		/// Returns the position after a drag or a click on the track.
		/// </summary>
		internal Vector2 EndScrollView(Vector2 position)
		{
			var layout = Event.current.type == EventType.Layout;
			var contentHeight = layout ? 0f : GUILayoutUtility.GetLastRect().yMax;
			GUILayout.EndScrollView();
			var id = GUIUtility.GetControlID(FocusType.Passive);
			if (layout)
				return position;
			var view = GUILayoutUtility.GetLastRect();
			var track = new Rect(view.xMax - ScrollbarBarWidth, view.y, ScrollbarBarWidth, view.height);
			var inner = new Rect(track.x + 2f, track.y + 2f, track.width - 4f, track.height - 4f);
			var range = Mathf.Max(0f, contentHeight - view.height);
			var thumbHeight = range > 0f ? Mathf.Max(ScrollbarBarWidth * 2f, inner.height * view.height / contentHeight) : inner.height;
			var travel = Mathf.Max(0f, inner.height - thumbHeight);
			var thumb = new Rect(inner.x, inner.y + (range > 0f ? travel * Mathf.Clamp01(position.y / range) : 0f), inner.width, thumbHeight);
			var current = Event.current;
			switch (current.GetTypeForControl(id))
			{
				case EventType.MouseDown:
					if (current.button != 0 || range <= 0f || !track.Contains(current.mousePosition))
						break;
					GUIUtility.hotControl = id;
					m_ScrollbarGrab = thumb.Contains(current.mousePosition) ? current.mousePosition.y - thumb.y : thumbHeight * 0.5f;
					position.y = ScrollFor(current.mousePosition.y, inner, travel, range);
					current.Use();
					break;
				case EventType.MouseDrag:
					if (GUIUtility.hotControl != id)
						break;
					position.y = ScrollFor(current.mousePosition.y, inner, travel, range);
					current.Use();
					break;
				case EventType.MouseUp:
					if (GUIUtility.hotControl != id)
						break;
					GUIUtility.hotControl = 0;
					current.Use();
					break;
				case EventType.Repaint:
					GUI.DrawTexture(track, m_ScrollbarTrack);
					if (range <= 0f)
						break;
					var active = GUIUtility.hotControl == id || track.Contains(current.mousePosition);
					GUI.DrawTexture(thumb, active ? m_ScrollbarThumbHover : m_ScrollbarThumb);
					break;
			}
			return position;
		}

		float ScrollFor(float mouseY, Rect inner, float travel, float range)
		{
			if (travel <= 0f)
				return 0f;
			return Mathf.Clamp01((mouseY - m_ScrollbarGrab - inner.y) / travel) * range;
		}

		public void Dispose()
		{
			foreach (var texture in m_Textures)
			{
				if (texture == null)
					continue;
				if (Application.isPlaying)
					UnityEngine.Object.Destroy(texture);
				else
					UnityEngine.Object.DestroyImmediate(texture);
			}
			m_Textures.Clear();
		}

		Texture2D CreateFill(string name, Color color)
		{
			var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
			{
				name = "Dexc Runtime IMGUI " + name,
				hideFlags = HideFlags.HideAndDontSave,
				filterMode = FilterMode.Point,
				wrapMode = TextureWrapMode.Clamp,
			};
			texture.SetPixel(0, 0, color);
			texture.Apply(false, true);
			m_Textures.Add(texture);
			return texture;
		}

		Texture2D CreateBordered(string name, Color fill, Color border, int thickness)
		{
			const int size = 5;
			var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
			{
				name = "Dexc Runtime IMGUI " + name,
				hideFlags = HideFlags.HideAndDontSave,
				filterMode = FilterMode.Point,
				wrapMode = TextureWrapMode.Clamp,
			};
			var pixels = new Color[size * size];
			for (var y = 0; y < size; y++)
			{
				for (var x = 0; x < size; x++)
				{
					pixels[y * size + x] = x < thickness || y < thickness || x >= size - thickness || y >= size - thickness ? border : fill;
				}
			}
			texture.SetPixels(pixels);
			texture.Apply(false, true);
			m_Textures.Add(texture);
			return texture;
		}

		static GUIStyle Box(Texture2D background, Color text, int padding)
		{
			var style = new GUIStyle
			{
				border = new RectOffset(2, 2, 2, 2),
				padding = new RectOffset(padding, padding, padding, padding),
				wordWrap = true,
			};
			style.normal.background = background;
			style.normal.textColor = text;
			return style;
		}

		static GUIStyle Button(
			Texture2D normal,
			Texture2D hover,
			Texture2D active,
			Texture2D focus,
			Texture2D disabled,
			Color text,
			Font font
		)
		{
			var style = new GUIStyle
			{
				font = font ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),
				fontSize = DexcRuntimeFormLayout.FontSize,
				border = new RectOffset(2, 2, 2, 2),
				padding = new RectOffset(8, 8, 4, 4),
				wordWrap = true,
				alignment = TextAnchor.MiddleLeft,
			};
			SetState(style.normal, normal, text);
			SetState(style.hover, hover, text);
			SetState(style.active, active, text);
			SetState(style.focused, focus, text);
			SetState(style.onNormal, focus, text);
			SetState(style.onHover, hover, text);
			SetState(style.onActive, active, text);
			SetState(style.onFocused, focus, text);
			if (disabled != null)
			{
				style.onNormal.background = disabled;
			}

			return style;
		}

		static GUIStyle Label(Color text, Font font, bool wordWrap, FontStyle fontStyle)
		{
			var style = new GUIStyle
			{
				font = font ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),
				fontSize = DexcRuntimeFormLayout.FontSize,
				fontStyle = fontStyle,
				wordWrap = wordWrap,
				clipping = wordWrap ? TextClipping.Overflow : TextClipping.Clip,
				alignment = TextAnchor.UpperLeft,
			};
			style.normal.textColor = text;
			return style;
		}

		static void SetState(GUIStyleState state, Texture2D background, Color text)
		{
			state.background = background;
			state.textColor = text;
		}
	}
}
