using System;
using System.Collections.Generic;

namespace Dexc
{
	internal enum DexcUIRowTone
	{
		Normal,
		Warning,
		Error,
	}

	internal enum DexcUIRowKind
	{
		Header,
		Label,
		Button,
		Input,
		Choice,
		Slider,
	}

	/// <summary>
	/// One row of a Runtime menu page, described once and drawn by every renderer. A page builds its rows from its form;
	/// the renderer turns each row into its own controls and, when the form changes, re-reads the row's getters in place
	/// instead of rebuilding - which is what keeps a text field's focus and caret while the user types.
	/// </summary>
	internal sealed class DexcUIRow
	{
		internal DexcUIRowKind Kind;

		/// <summary>Stays the same for the same field across rebuilds. IMGUI uses it as the control name.</summary>
		internal string Key;

		/// <summary>The caption: a header or button text, or the field label on the first row of a field.</summary>
		internal string Label = string.Empty;

		/// <summary>The member a structured value's row edits, such as "x" of a Vector3.</summary>
		internal string Component;

		/// <summary>The current text of an input or choice, or of a label whose text follows the form.</summary>
		internal Func<string> Text;

		internal Func<string> Error;

		internal Func<bool> Enabled;

		internal Func<float> SliderValue;

		/// <summary>The user edited the text. It is not written yet.</summary>
		internal Action<string> Changed;

		/// <summary>The user finished editing. Returns whether anything was written.</summary>
		internal Func<string, bool> Confirmed;

		/// <summary>A button or choice was pressed. The renderer rebuilds the page afterwards.</summary>
		internal Action Activated;

		internal Action<float> Slid;

		/// <summary>The "…" action beside an input, such as opening a longer text in the detail editor.</summary>
		internal Action Auxiliary;

		/// <summary>
		/// Steps a choice to its previous (-1) or next (+1) value in place, without opening the selection page. Returns
		/// whether anything changed. Null for a choice whose candidates have to be searched, such as a reference.
		/// </summary>
		internal Func<int, bool> Cycle;

		/// <summary>A short tag beside the label, such as "Immediate" or "Read only".</summary>
		internal string Note;

		/// <summary>A list entry's second line, such as a command's description or a favorite's location.</summary>
		internal Func<string> Detail;

		/// <summary>Text at the right end of a list entry, such as a count or a result.</summary>
		internal Func<string> Trailing;

		internal DexcUIRowTone TrailingTone;

		/// <summary>A mark before a list entry's label, such as the favorite star.</summary>
		internal string Leading;

		/// <summary>Activating the entry goes one level deeper: a folder or a page. Drawn as a chevron at the right end.</summary>
		internal bool Drill;

		/// <summary>A line of a table, such as a collection's items, drawn on a band every other line so rows can be followed.</summary>
		internal bool Striped;

		/// <summary>The renderer stopped using this row's controls, so late events from them must be ignored.</summary>
		internal Action Unbound;

		/// <summary>How the row reads: a warning or an error colours it. Decided when the page is built.</summary>
		internal DexcUIRowTone Tone;

		/// <summary>Rows that belong to one field share a group, such as the components of a vector.</summary>
		internal string Group;

		/// <summary>What the row stands for, for tests that look a field up by its id.</summary>
		internal object Tag;

		/// <summary>
		/// An IMGUI drawing that replaces the field's rows when it returns true: a project's own input editor. The retained
		/// renderers cannot run OnGUI code and draw the rows instead.
		/// </summary>
		internal Func<bool> Immediate;

		/// <summary>A choice between two values, drawn as a check box.</summary>
		internal bool Boolean;

		/// <summary>
		/// The row's place among the page's focusable rows when the page's gamepad navigation counts rows, or -1. IMGUI
		/// marks the focused row with it; the retained renderers follow their own selectables.
		/// </summary>
		internal int NavigationIndex = -1;

		/// <summary>The row is an entry of the page's list: IMGUI keeps it in view when the gamepad selects it.</summary>
		internal bool ListItem;

		/// <summary>An input that takes several lines, such as the detail page's text editor.</summary>
		internal bool Multiline;

		/// <summary>Confirming this input changes what the page lists, so the renderer rebuilds the page afterwards.</summary>
		internal bool Rebuilds;

		internal bool IsEnabled => Enabled == null || Enabled();

		internal string ErrorText => Error == null ? null : Error();

		internal string DetailText => Detail == null ? null : Detail();

		internal string TrailingText => Trailing == null ? null : Trailing();

		/// <summary>Steps through a list of names from the current one, wrapping at both ends. Null when the list is empty.</summary>
		internal static string Step(IReadOnlyList<string> names, string current, int direction)
		{
			if (names.Count == 0 || direction == 0)
				return null;
			var index = -1;
			for (var candidate = 0; candidate < names.Count; candidate++)
				if (string.Equals(names[candidate], current, StringComparison.Ordinal))
					index = candidate;
			if (index < 0)
				return names[direction > 0 ? 0 : names.Count - 1];
			return names[((index + Math.Sign(direction)) % names.Count + names.Count) % names.Count];
		}

		/// <summary>
		/// A search field. Typing keeps a draft, so a renderer that rebuilds its rows every frame keeps what was typed;
		/// confirming applies the search and the page is rebuilt with the new list.
		/// </summary>
		internal static DexcUIRow Search(string key, Func<string> current, Action<string> apply)
		{
			var drafts = DexcRuntimeUIState.StructuredInputText;
			return new DexcUIRow
			{
				Kind = DexcUIRowKind.Input,
				Key = key,
				Label = "Search",
				Text = () => drafts.TryGetValue(key, out var draft) ? draft : current() ?? string.Empty,
				Changed = text => drafts[key] = text,
				Confirmed = text =>
				{
					drafts.Remove(key);
					apply(text);
					return true;
				},
				Rebuilds = true,
			};
		}
	}
}
