using System;
using UnityEngine;

namespace Dexc
{
	// Logical UI units. Renderers apply their reference scale once, at the panel root.
	internal static class DexcRuntimeFormLayout
	{
		internal const float DefaultRowHeight = 28f;
		internal const float LabelGap = 5f;
		internal const float RowGap = 4f;
		internal const float ComponentWidth = 36f;
		internal const float ColumnGap = 6f;
		internal const float ErrorWidth = 28f;
		internal const float HorizontalPadding = 6f;
		internal const float VerticalPadding = 4f;

		/// <summary>Text sizes every renderer uses, so they read alike and grow together.</summary>
		internal const int FontSize = 15;
		internal const int TitleFontSize = 16;
		internal const int TrailingFontSize = 14;
		internal const int SmallFontSize = 13;
		internal const int NoteFontSize = 12;
		/// <summary>The ‹ › and other symbols drawn in a square button.</summary>
		internal const int SymbolFontSize = 17;

		/// <summary>The share of a row the label column takes when label and value sit side by side.</summary>
		internal const float LabelFraction = 0.4f;

		/// <summary>Below this row width the label goes above the value: a value narrower than this is too cramped to edit.</summary>
		internal const float StackWidth = 280f;

		/// <summary>A label that wraps to more lines than this goes above the value instead of growing the row.</summary>
		internal const int MaximumLabelLines = 2;

		/// <summary>Space above a section heading inside a page, separating it from the rows before it.</summary>
		internal const float SectionGap = 10f;

		internal static float RowHeight(float configured, float minimumHit, float lineHeight = 16f)
		{
			return Mathf.Max(configured, minimumHit, Mathf.Ceil(lineHeight) + 2f * VerticalPadding + 4f);
		}

		/// <summary>Side by side, the column of the menu's sections at the left.</summary>
		internal const float SplitSectionWidth = 216f;

		/// <summary>Side by side, the column of a list next to the page it opens.</summary>
		internal const float SplitListWidth = 288f;

		/// <summary>The panel header: the place in the menu over the page title, with square back and close buttons.</summary>
		/// <remarks>
		/// The two lines of text need room of their own, as a font with a tall line, such as Noto Sans JP, takes about 1.6
		/// times its size.
		/// </remarks>
		internal static float HeaderHeight(float hitHeight) =>
			Mathf.Max(hitHeight, Mathf.Ceil((SmallFontSize + TitleFontSize) * 1.6f)) + 12f;

		/// <summary>Where a field's label and value go in a row of <paramref name="rowWidth"/>.</summary>
		internal readonly struct Arrangement
		{
			internal Arrangement(bool stacked, float labelWidth, float valueX, float valueWidth)
			{
				Stacked = stacked;
				LabelWidth = labelWidth;
				ValueX = valueX;
				ValueWidth = valueWidth;
			}

			/// <summary>The label is on its own line above the value, both across the whole row.</summary>
			internal bool Stacked { get; }
			internal float LabelWidth { get; }
			internal float ValueX { get; }
			internal float ValueWidth { get; }
		}

		/// <summary>
		/// Decides between label and value side by side and the label above the value. <paramref name="labelLines"/> tells
		/// how many lines the label wraps to at a given width: renderers measure text their own way, the decision is shared.
		/// </summary>
		internal static Arrangement Arrange(float rowWidth, Func<float, int> labelLines)
		{
			rowWidth = Mathf.Max(0f, rowWidth);
			var labelWidth = Mathf.Floor(rowWidth * LabelFraction);
			if (rowWidth < StackWidth || labelLines != null && labelLines(labelWidth) > MaximumLabelLines)
				return new Arrangement(true, rowWidth, 0f, rowWidth);
			var valueX = labelWidth + ColumnGap;
			return new Arrangement(false, labelWidth, valueX, rowWidth - valueX);
		}

		/// <summary>
		/// Breaks a label wider than <paramref name="width"/> where a word starts inside it, such as before the S of
		/// SourcePath, so a long name wraps between its words instead of leaving a letter or two on the next line.
		/// <paramref name="lineWidth"/> measures one line the renderer's way. A word wider than the column is left whole
		/// for the renderer to wrap.
		/// </summary>
		internal static string BreakLabel(string text, float width, Func<string, float> lineWidth)
		{
			if (string.IsNullOrEmpty(text) || width <= 0f || lineWidth == null || lineWidth(text) <= width)
				return text;
			var builder = new System.Text.StringBuilder(text.Length + 4);
			var line = string.Empty;
			var start = 0;
			for (var index = 1; index <= text.Length; index++)
			{
				if (index < text.Length && !WordStarts(text, index))
					continue;
				var word = text.Substring(start, index - start);
				start = index;
				var joined = line + word;
				if (line.Length > 0 && lineWidth(joined.TrimEnd()) > width)
				{
					builder.Append(line.TrimEnd()).Append('\n');
					line = word.TrimStart();
				}
				else
				{
					line = joined;
				}
			}
			return builder.Append(line).ToString();
		}

		/// <summary>Breaks each line of a block of text between its words, as <see cref="BreakLabel"/> does a label.</summary>
		internal static string BreakLines(string text, float width, Func<string, float> lineWidth)
		{
			if (string.IsNullOrEmpty(text) || text.IndexOf('\n') < 0)
				return BreakLabel(text, width, lineWidth);
			var lines = text.Split('\n');
			for (var index = 0; index < lines.Length; index++)
				lines[index] = BreakLabel(lines[index], width, lineWidth);
			return string.Join("\n", lines);
		}

		private static bool WordStarts(string text, int index)
		{
			var previous = text[index - 1];
			var current = text[index];
			if (previous == '\n' || current == '\n')
				return false;
			return char.IsWhiteSpace(previous) && !char.IsWhiteSpace(current)
				|| previous == '_' || previous == '-' || previous == '/' || previous == '.'
				|| char.IsUpper(current) && (char.IsLower(previous) || char.IsDigit(previous))
				|| char.IsUpper(current) && char.IsUpper(previous) && index + 1 < text.Length && char.IsLower(text[index + 1]);
		}
	}
}
