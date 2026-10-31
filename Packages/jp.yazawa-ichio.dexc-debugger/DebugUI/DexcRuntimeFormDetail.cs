using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Dexc
{
	// A same-panel fallback keeps the originating form's raw values and selection alive.
	internal static class DexcRuntimeFormDetail
	{
		/// <summary>State of this class. Owned by <see cref="DexcUIRoot"/>; nothing here survives a root reset.</summary>
		internal sealed class State
		{
			internal DexcRuntimeUIPage ReturnPage;
			internal int ReturnIndex;
			internal Vector2 ReturnScroll;
			internal Vector2 ReturnListScroll;
			internal string ReturnFocus;
			internal Func<string, DebugPageRequest, ChoicePage> ChoiceReader;
			internal Action<string> EditText;
			internal Action ConfirmText;
			internal Action ApplyAction;
			internal Func<bool> CanApplyCheck;
			internal int PageCount;
			internal int ChoiceGeneration;
			internal string Title = string.Empty;
			internal string Search = string.Empty;
			internal string Text = string.Empty;
			internal int Offset;
			internal bool Searchable;
			internal string PendingFocus;
			internal string Message = string.Empty;
		}

		internal static State Data => DexcUIRoot.Current.FormDetail;
		static DexcRuntimeUIPage ReturnPage
		{
			get => Data.ReturnPage;
			set => Data.ReturnPage = value;
		}

		/// <summary>The page the detail returns to, which is also where it sits in the menu.</summary>
		internal static DexcRuntimeUIPage ReturnTo => ReturnPage;
		static int ReturnIndex
		{
			get => Data.ReturnIndex;
			set => Data.ReturnIndex = value;
		}
		static Vector2 ReturnScroll
		{
			get => Data.ReturnScroll;
			set => Data.ReturnScroll = value;
		}
		static Vector2 ReturnListScroll
		{
			get => Data.ReturnListScroll;
			set => Data.ReturnListScroll = value;
		}
		static string ReturnFocus
		{
			get => Data.ReturnFocus;
			set => Data.ReturnFocus = value;
		}
		static Func<string, DebugPageRequest, ChoicePage> ChoiceReader
		{
			get => Data.ChoiceReader;
			set => Data.ChoiceReader = value;
		}
		static Action<string> EditText
		{
			get => Data.EditText;
			set => Data.EditText = value;
		}
		static Action ConfirmText
		{
			get => Data.ConfirmText;
			set => Data.ConfirmText = value;
		}
		static Action ApplyAction
		{
			get => Data.ApplyAction;
			set => Data.ApplyAction = value;
		}
		static Func<bool> CanApplyCheck
		{
			get => Data.CanApplyCheck;
			set => Data.CanApplyCheck = value;
		}
		static int PageCount
		{
			get => Data.PageCount;
			set => Data.PageCount = value;
		}
		static int ChoiceGeneration
		{
			get => Data.ChoiceGeneration;
			set => Data.ChoiceGeneration = value;
		}
		internal static string Title
		{
			get => Data.Title;
			private set => Data.Title = value;
		}
		internal static string Search
		{
			get => Data.Search;
			private set => Data.Search = value;
		}
		internal static string Text
		{
			get => Data.Text;
			private set => Data.Text = value;
		}
		internal static int Offset
		{
			get => Data.Offset;
			private set => Data.Offset = value;
		}
		internal static bool Searchable
		{
			get => Data.Searchable;
			private set => Data.Searchable = value;
		}
		internal static string PendingFocus
		{
			get => Data.PendingFocus;
			set => Data.PendingFocus = value;
		}
		internal static string Message
		{
			get => Data.Message;
			private set => Data.Message = value;
		}

		internal sealed class Choice
		{
			internal Choice(string label, Action selected)
			{
				Label = label;
				Selected = selected;
				Generation = ChoiceGeneration;
			}

			internal string Label { get; }
			internal Action Selected { get; }
			internal int Generation { get; }
		}

		internal sealed class ChoicePage
		{
			internal ChoicePage(IReadOnlyList<Choice> items, bool hasMore, string message = "")
			{
				Items = items ?? Array.Empty<Choice>();
				HasMore = hasMore;
				Message = message;
			}

			internal IReadOnlyList<Choice> Items { get; }
			internal bool HasMore { get; }
			internal string Message { get; }
		}

		internal static bool HasApply => ApplyAction != null;
		internal static bool CanApply => HasApply && (CanApplyCheck == null || CanApplyCheck());
		internal static bool IsSelection => ChoiceReader != null;
		internal static bool IsTextEditor => EditText != null;

		internal static void Reset()
		{
			ChoiceGeneration++;
			Message = string.Empty;
			Title = string.Empty;
			Search = string.Empty;
			Text = string.Empty;
			Offset = 0;
			Searchable = false;
			ChoiceReader = null;
			EditText = null;
			ConfirmText = null;
			ApplyAction = null;
			CanApplyCheck = null;
			PendingFocus = null;
			ReturnFocus = null;
			ReturnPage = DexcRuntimeUIPage.Home;
			ReturnIndex = 0;
			ReturnScroll = Vector2.zero;
			ReturnListScroll = Vector2.zero;
		}

		internal static void Open(string message, string focus = null)
		{
			if (DexcRuntimeUIState.Page == DexcRuntimeUIPage.FormDetail)
				return;
			Reset();
			ReturnFocus = focus;
			PendingFocus = null;
			ReturnPage = DexcRuntimeUIState.Page;
			ReturnIndex = DexcRuntimeUIState.SelectedIndex;
			ReturnScroll = DexcRuntimeUIState.ScrollPosition;
			ReturnListScroll = DexcRuntimeUIState.ListScrollPosition;
			Message = message ?? string.Empty;
			DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.FormDetail);
		}

		internal static void OpenSelection(
			string title,
			Func<string, DebugPageRequest, ChoicePage> readChoices,
			bool searchable = true,
			string focus = null,
			Action apply = null,
			Func<bool> canApply = null
		)
		{
			if (DexcRuntimeUIState.Page == DexcRuntimeUIPage.FormDetail)
				return;
			Open(string.Empty, focus);
			Title = title;
			Searchable = searchable;
			ChoiceReader = readChoices;
			ApplyAction = apply;
			CanApplyCheck = canApply;
		}

		internal static void OpenTextEditor(string title, string text, Action<string> edit, string focus = null, Action confirmed = null)
		{
			if (DexcRuntimeUIState.Page == DexcRuntimeUIPage.FormDetail)
				return;
			Open(string.Empty, focus);
			Title = title;
			Text = text ?? string.Empty;
			EditText = edit;
			ConfirmText = confirmed;
		}

		internal static void ChangeText(string text)
		{
			Text = text ?? string.Empty;
			EditText?.Invoke(Text);
		}

		internal static void ChangeSearch(string search)
		{
			Search = search ?? string.Empty;
			Offset = 0;
		}

		internal static ChoicePage ReadChoices()
		{
			if (ChoiceReader == null)
				return new ChoicePage(null, false);
			var request = DexcRuntimeUIState.CreatePageRequest(Offset, 16);
			PageCount = request.Count;
			try
			{
				return ChoiceReader(Search, request);
			}
			catch (Exception exception)
			{
				return new ChoicePage(null, false, exception.Message);
			}
		}

		internal static void Previous() => Offset = Mathf.Max(0, Offset - PageCount);

		internal static void Next() => Offset += PageCount;

		internal static void Select(Choice choice)
		{
			if (DexcRuntimeUIState.Page != DexcRuntimeUIPage.FormDetail || choice == null || choice.Generation != ChoiceGeneration)
				return;
			if (!HasApply)
				Close();
			choice.Selected?.Invoke();
		}

		internal static void Apply()
		{
			if (!CanApply || DexcRuntimeUIState.Page != DexcRuntimeUIPage.FormDetail)
				return;
			var apply = ApplyAction;
			Close();
			apply();
		}

		internal static void Close()
		{
			if (DexcRuntimeUIState.Page != DexcRuntimeUIPage.FormDetail)
				return;
			var confirm = ConfirmText;
			DexcRuntimeUIState.Navigate(ReturnPage);
			DexcRuntimeUIState.SelectedIndex = ReturnIndex;
			DexcRuntimeUIState.ScrollPosition = ReturnScroll;
			DexcRuntimeUIState.ListScrollPosition = ReturnListScroll;
			var focus = ReturnFocus;
			Reset();
			PendingFocus = focus;
			confirm?.Invoke();
		}

		/// <summary>One item of a collection as the Inspector writes it: "[index] text".</summary>
		const string ItemLine = @"^\[(\d+)\] (.*)$";

		/// <summary>The detail page as rows: a text editor, a message, or a searchable, paged list of choices.</summary>
		internal static List<DexcUIRow> Rows()
		{
			// The title is the last part of the header's place in the menu, so the page itself starts with its content.
			var rows = new List<DexcUIRow>();
			if (IsTextEditor)
			{
				rows.Add(
					new DexcUIRow
					{
						Kind = DexcUIRowKind.Input,
						Key = "detail:text",
						Multiline = true,
						Text = () => Text,
						Changed = ChangeText,
					}
				);
				return rows;
			}
			if (!IsSelection)
			{
				// The lines before the first item are the summary, such as the field's name and the count.
				var lines = Message.Split('\n');
				var matches = lines.Select(line => System.Text.RegularExpressions.Regex.Match(line, ItemLine)).ToList();
				var summary = matches.FindIndex(match => match.Success);
				var items = summary < 1 ? new List<System.Text.RegularExpressions.Match>() : matches.Skip(summary).ToList();
				if (items.Count == 0 || items.Any(match => !match.Success))
				{
					rows.Add(
						new DexcUIRow
						{
							Kind = DexcUIRowKind.Label,
							Key = "detail:message",
							Label = Message,
						}
					);
					return rows;
				}
				// A collection: its summary, then one line per item with the index in a right-aligned column.
				rows.Add(
					new DexcUIRow
					{
						Kind = DexcUIRowKind.Label,
						Key = "detail:message",
						Label = string.Join("\n", lines.Take(summary)),
					}
				);
				var width = items.Max(match => match.Groups[1].Value.Length);
				for (var index = 0; index < items.Count; index++)
				{
					var match = items[index];
					rows.Add(
						new DexcUIRow
						{
							Kind = DexcUIRowKind.Label,
							Key = "detail:item:" + index,
							// Figure spaces are as wide as a digit, so the indices line up on their right edge.
							Label = match.Groups[1].Value.PadLeft(width, '\u2007') + "    " + match.Groups[2].Value,
							Striped = index % 2 == 0,
						}
					);
				}
				return rows;
			}
			if (Searchable)
				rows.Add(DexcUIRow.Search("search:choice", () => Search, ChangeSearch));
			var page = ReadChoices();
			if (!string.IsNullOrEmpty(page.Message))
				rows.Add(
					new DexcUIRow
					{
						Kind = DexcUIRowKind.Label,
						Key = "detail:page-message",
						Label = page.Message,
					}
				);
			if (page.Items.Count == 0 && string.IsNullOrEmpty(page.Message))
				rows.Add(
					new DexcUIRow
					{
						Kind = DexcUIRowKind.Label,
						Key = "detail:empty",
						Label = "No entries",
					}
				);
			var navigation = 0;
			foreach (var item in page.Items)
			{
				var target = item;
				rows.Add(
					new DexcUIRow
					{
						Kind = DexcUIRowKind.Button,
						Key = "detail:choice:" + navigation,
						Label = item.Label,
						NavigationIndex = navigation++,
						ListItem = true,
						Activated = () => Select(target),
					}
				);
			}
			if (Offset > 0)
				rows.Add(DexcUIRows.Paging("detail:previous", "Previous", navigation++, Previous));
			if (page.HasMore)
				rows.Add(DexcUIRows.Paging("detail:next", "Next", navigation, Next));
			return rows;
		}
	}
}
