using System;
using System.Collections.Generic;
using System.Linq;

namespace Dexc
{
	/// <summary>Shared state of the Runtime menu "Panels" page: the selected panel and the widget that has navigation focus.</summary>
	internal static class DexcRuntimePanelForm
	{
		/// <summary>State of this class. Owned by <see cref="DexcUIRoot"/>; nothing here survives a root reset.</summary>
		internal sealed class State
		{
			internal DebugPanel Current;
			internal int Version;

			/// <summary>Text typed into number and text widgets that has not been confirmed yet.</summary>
			internal readonly Dictionary<DebugWidget, string> Drafts = new Dictionary<DebugWidget, string>();
		}

		internal static State Data => DexcUIRoot.Current.PanelForm;
		internal static DebugPanel Current
		{
			get => Data.Current;
			private set => Data.Current = value;
		}
		internal static int Version
		{
			get => Data.Version;
			private set => Data.Version = value;
		}

		/// <summary>The boot screen shows boot panels; the debug menu shows what the project registered. They never mix.</summary>
		internal static IReadOnlyList<DebugPanel> Panels => DebugBootMenu.UsesRuntimeMenu ? DebugBootMenu.Panels : DebugPanels.All;

		/// <summary>Widgets of the current panel in display order.</summary>
		internal static IReadOnlyList<DebugWidget> Widgets => Current == null ? Array.Empty<DebugWidget>() : Current.Widgets;

		/// <summary>Widgets a user can focus, in display order. Navigation indices refer to this list.</summary>
		internal static IReadOnlyList<DebugWidget> Focusable => Widgets.Where(widget => widget.IsInteractive).ToList();

		internal static void Open(DebugPanel panel)
		{
			DebugBootMenu.NoteInteraction();
			Current = panel;
			Data.Drafts.Clear();
			Version++;
			DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.PanelDetail);
		}

		internal static void OpenAt(int index)
		{
			var panels = Panels;
			if (index >= 0 && index < panels.Count)
				Open(panels[index]);
		}

		internal static void Reset()
		{
			Current = null;
			Data.Drafts.Clear();
			Version++;
		}

		/// <summary>Returns the open boot set to its declared values and forgets the stored ones.</summary>
		internal static void ResetBootValues()
		{
			DebugBootMenu.Data.Selection?.Reset();
			Data.Drafts.Clear();
			Version++;
			DexcRuntimeUIState.Notice = "Reset to defaults.";
		}

		internal static DebugWidget FocusedWidget
		{
			get
			{
				var focusable = Focusable;
				if (focusable.Count == 0)
					return null;
				return focusable[DexcRuntimeUIState.ClampSelectedIndex(focusable.Count)];
			}
		}

		/// <summary>Left/right on the focused widget: toggles, cycles choices, steps numbers.</summary>
		internal static bool Adjust(int delta)
		{
			return Adjust(FocusedWidget, delta);
		}

		internal static bool Adjust(DebugWidget widget, int delta)
		{
			DebugBootMenu.NoteInteraction();
			if (widget == null || !widget.Enabled)
				return false;
			try
			{
				switch (widget)
				{
					case DebugToggleWidget toggle:
						toggle.Toggle();
						break;
					case DebugChoiceWidget choice:
						choice.Move(delta);
						break;
					case DebugNumberWidget number:
						number.Move(delta);
						break;
					default:
						return false;
				}
				Version++;
				return true;
			}
			catch (Exception exception)
			{
				DexcRuntimeUIState.Notice = exception.Message;
				return false;
			}
		}

		/// <summary>Submit on the focused widget: toggles, cycles choices, invokes buttons.</summary>
		internal static bool Activate()
		{
			return Activate(FocusedWidget);
		}

		internal static bool Activate(DebugWidget widget)
		{
			DebugBootMenu.NoteInteraction();
			if (widget == null || !widget.Enabled)
				return false;
			try
			{
				switch (widget)
				{
					case DebugToggleWidget toggle:
						toggle.Toggle();
						break;
					case DebugChoiceWidget choice:
						choice.Move(1);
						break;
					case DebugButtonWidget button:
						button.Invoke();
						break;
					default:
						return false;
				}
				Version++;
				return true;
			}
			catch (Exception exception)
			{
				DexcRuntimeUIState.Notice = exception.Message;
				return false;
			}
		}

		/// <summary>Text entry for number and text widgets. Numbers are applied only when the text parses.</summary>
		internal static bool SetText(DebugWidget widget, string text)
		{
			DebugBootMenu.NoteInteraction();
			if (widget == null || !widget.Enabled)
				return false;
			try
			{
				switch (widget)
				{
					case DebugNumberWidget number:
						if (!number.TrySetText(text))
							return false;
						break;
					case DebugTextWidget textWidget:
						textWidget.Value = text;
						break;
					default:
						return false;
				}
				Version++;
				return true;
			}
			catch (Exception exception)
			{
				DexcRuntimeUIState.Notice = exception.Message;
				return false;
			}
		}

		/// <summary>Display text of a widget's value ("On"/"Off", the selected option, the number, or the text).</summary>
		internal static string ValueText(DebugWidget widget)
		{
			switch (widget)
			{
				case DebugToggleWidget toggle:
					return toggle.Value ? "On" : "Off";
				case DebugChoiceWidget choice:
					return choice.Selected;
				case DebugNumberWidget number:
					return number.Text;
				case DebugTextWidget text:
					return text.Value;
				case DebugLabelWidget label:
					return label.Text;
				default:
					return string.Empty;
			}
		}

		internal static string Key(DebugWidget widget) => "panel:" + widget.GetHashCode();

		/// <summary>
		/// The panel as rows. Toggles and choices are drawn like a setting's choice, and number and text widgets like a
		/// setting's input: typing keeps a draft and Enter or leaving the field applies it.
		/// </summary>
		internal static List<DexcUIRow> DetailRows()
		{
			var rows = new List<DexcUIRow>();
			if (Current == null)
				return rows;
			var navigation = 0;
			foreach (var widget in Current.Widgets)
			{
				var row = WidgetRow(widget);
				if (widget.IsInteractive)
					row.NavigationIndex = navigation++;
				row.Label = MarkChanged(widget, row.Label);
				rows.Add(row);
				if (!string.IsNullOrEmpty(widget.Description))
				{
					var description = widget.Description;
					rows.Add(
						new DexcUIRow
						{
							Kind = DexcUIRowKind.Label,
							Key = Key(widget) + ":description",
							Label = description,
						}
					);
				}
			}
			return rows;
		}

		/// <summary>
		/// A boot item set away from what its block declares ends its label with an asterisk, so a run's odd settings stand
		/// out without the row changing size.
		/// </summary>
		internal static string MarkChanged(DebugWidget widget, string label) =>
			widget.IsChanged != null && widget.IsChanged() ? label + " *" : label;

		static DexcUIRow WidgetRow(DebugWidget widget)
		{
			var row = new DexcUIRow
			{
				Key = Key(widget),
				Label = widget.Label,
				Enabled = () => widget.Enabled,
			};
			switch (widget)
			{
				case DebugLabelWidget label:
					row.Kind = DexcUIRowKind.Label;
					row.Label = string.Empty;
					row.Text = () => label.Text;
					break;
				case DebugButtonWidget _:
					row.Kind = DexcUIRowKind.Button;
					row.Activated = () => Activate(widget);
					break;
				case DebugToggleWidget toggle:
					row.Kind = DexcUIRowKind.Choice;
					row.Boolean = true;
					row.Text = () => toggle.Value ? "true" : "false";
					row.Activated = () => Activate(widget);
					row.Cycle = direction => Adjust(widget, direction);
					break;
				case DebugChoiceWidget choice:
					row.Kind = DexcUIRowKind.Choice;
					row.Text = () => choice.Selected;
					row.Activated = () => Activate(widget);
					row.Cycle = direction => Adjust(widget, direction);
					break;
				default:
					row.Kind = DexcUIRowKind.Input;
					row.Text = () => Data.Drafts.TryGetValue(widget, out var draft) ? draft : ValueText(widget);
					row.Changed = text => Data.Drafts[widget] = text;
					row.Confirmed = text => Commit(widget, text);
					break;
			}
			return row;
		}

		/// <summary>Applies a typed draft. A number that does not parse is reported and the field shows the value again.</summary>
		static bool Commit(DebugWidget widget, string text)
		{
			if (!Data.Drafts.Remove(widget))
				return false;
			if (SetText(widget, text))
				return true;
			if (widget is DebugNumberWidget)
				DexcRuntimeUIState.Notice = "Invalid number: " + text;
			return false;
		}

		/// <summary>The panel list as rows.</summary>
		internal static List<DexcUIRow> ListRows()
		{
			var rows = new List<DexcUIRow>();
			var panels = Panels;
			if (panels.Count == 0 && !DebugBootMenu.UsesRuntimeMenu)
				rows.Add(
					new DexcUIRow
					{
						Kind = DexcUIRowKind.Label,
						Key = "panel-list:empty",
						Label = "No panels. Register a DebugPanel with DebugPanels.Register or add a [DebugBootMenu] class.",
					}
				);
			for (var index = 0; index < panels.Count; index++)
			{
				var target = index;
				rows.Add(
					new DexcUIRow
					{
						Kind = DexcUIRowKind.Button,
						Key = "panel-list:" + panels[index].GetHashCode(),
						Label = panels[index].Title,
						NavigationIndex = index,
						ListItem = true,
						Activated = () => OpenAt(target),
					}
				);
			}
			return rows;
		}
	}
}
