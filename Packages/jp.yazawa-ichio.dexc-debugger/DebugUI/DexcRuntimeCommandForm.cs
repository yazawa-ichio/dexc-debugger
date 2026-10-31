using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Dexc
{
	// Command operations are shared; renderers retain their own controls and focus bindings.
	internal static class DexcRuntimeCommandForm
	{
		internal static string IssueText(DebugCommandValidationIssue issue) => issue == null ? null : issue.Severity + ": " + issue.Message;

		internal static bool IsWarningText(string message) => message != null && message.StartsWith("Warning: ", StringComparison.Ordinal);

		/// <summary>State of this class. Owned by <see cref="DexcUIRoot"/>; nothing here survives a root reset.</summary>
		internal sealed class State
		{
			internal DebugSessionId ListSession;
			internal string ListSearch;
			internal int ListPageCount;
			internal int ListOffset;
			internal bool ResultPredatesEdits;
			internal string FieldWindowPath;
			internal int FieldWindowOffset;
			internal int ValidationFrame = -1;
			internal DebugCommandDraftId ValidationDraft;
			internal long ValidationVersion = -1;
			internal DebugCommandValidationResult Validation;

			/// <summary>The folder the list shows, as a path without a trailing slash; empty for the top.</summary>
			internal string Folder = string.Empty;

			/// <summary>The list shows the starred commands instead of a folder.</summary>
			internal bool InFavorites;
			internal List<DebugCommandListItem> AllItems;
			internal int AllItemsFrame = -1;
		}

		internal static State Data => DexcUIRoot.Current.CommandForm;
		static DebugSessionId ListSession
		{
			get => Data.ListSession;
			set => Data.ListSession = value;
		}
		static string ListSearch
		{
			get => Data.ListSearch;
			set => Data.ListSearch = value;
		}
		static int ListPageCount
		{
			get => Data.ListPageCount;
			set => Data.ListPageCount = value;
		}
		internal static int ListOffset
		{
			get => Data.ListOffset;
			private set => Data.ListOffset = value;
		}
		internal static bool ResultPredatesEdits
		{
			get => Data.ResultPredatesEdits;
			private set => Data.ResultPredatesEdits = value;
		}

		internal static DebugCommandListPage ReadList()
		{
			var session = DexcDebugger.CurrentSession.SessionId;
			if (session != ListSession || ListSearch != DexcRuntimeUIState.Search)
			{
				ListOffset = 0;
				ListSession = session;
				ListSearch = DexcRuntimeUIState.Search;
			}
			var request = DexcRuntimeUIState.CreatePageRequest(ListOffset, 20);
			ListPageCount = request.Count;
			return DebugCommand.Registry.GetList(new DebugListQuery(string.Empty, ListSearch, request));
		}

		internal static void MoveListPage(int direction)
		{
			ListOffset = Math.Max(0, ListOffset + Math.Sign(direction) * ListPageCount);
			DexcRuntimeUIState.SelectedIndex = 0;
			DexcRuntimeUIState.ScrollPosition = UnityEngine.Vector2.zero;
		}

		internal static bool CanEdit =>
			DexcRuntimeUIState.IsOpen
			&& DexcRuntimeUIState.HasDraft
			&& DexcRuntimeUIState.AsyncExecution == null
			&& !DexcRuntimeUIState.Confirming;

		internal static void ResetResult() => ResultPredatesEdits = false;

		internal static void MarkEdited()
		{
			if (CurrentResult != null)
				ResultPredatesEdits = true;
		}

		internal static CommandExecutionResult CurrentResult
		{
			get
			{
				var result = DexcRuntimeUIState.LastCommandResult;
				return result != null && result.CommandPath == DexcRuntimeUIState.Command?.Path ? result : null;
			}
		}

		internal static string ResultSummary
		{
			get
			{
				var result = CurrentResult;
				if (result == null)
					return string.Empty;
				return (ResultPredatesEdits ? "Result before edits" : "Result")
					+ " · "
					+ result.Status
					+ " · "
					+ result.Duration.TotalMilliseconds.ToString("F1", CultureInfo.InvariantCulture)
					+ " ms";
			}
		}

		internal static string ResultDetails
		{
			get
			{
				var result = CurrentResult;
				if (result == null)
					return string.Empty;
				return ResultSummary
					+ "\n"
					+ result.Message
					+ (result.Output == null ? string.Empty : "\n" + DexcRuntimeUIRenderer.ValueText(result.Output));
			}
		}

		internal static string ValueLabel(DebugCommandFieldDescriptor field)
		{
			var label = DexcCommandRuntime.Drafts.GetSelectionLabel(DexcRuntimeUIState.DraftId, field.Id);
			if (label != null)
				return label;
			var value = DexcCommandRuntime.Drafts.GetInputValue(DexcRuntimeUIState.DraftId, field.Id);
			return value == null || value.Kind == DebugValueKind.Null ? "Select…" : DexcRuntimeUIRenderer.ValueText(value);
		}

		internal static Dictionary<DebugCommandFieldId, DebugValue> CaptureValues() =>
			DexcRuntimeUIState
				.Command.Fields.Where(field => field.IsInput)
				.ToDictionary(field => field.Id, field => DexcCommandRuntime.Drafts.GetInputValue(DexcRuntimeUIState.DraftId, field.Id));

		internal static void InvalidateChangedValues(Dictionary<DebugCommandFieldId, DebugValue> before, DebugCommandFieldId edited)
		{
			foreach (var field in DexcRuntimeUIState.Command.Fields.Where(field => field.IsInput))
			{
				if (field.Id == edited || DexcRuntimeUIState.CommandInvalidInput.Contains(field.Id))
					continue;
				var after = DexcCommandRuntime.Drafts.GetInputValue(DexcRuntimeUIState.DraftId, field.Id);
				if (Equals(before[field.Id], after))
					continue;
				DexcRuntimeUIState.CommandInputText.Remove(field.Id);
				DexcRuntimeCommandFormComponents.Invalidate(field);
			}
		}

		internal static string RowValue(DebugCommandFieldDescriptor field, string component)
		{
			if (UsesSelection(field))
				return ValueLabel(field);
			var member = field.Schema?.Members.FirstOrDefault(item =>
				string.Equals(item.Name, component, StringComparison.OrdinalIgnoreCase)
			);
			if (member != null)
				return DexcRuntimeCommandFormComponents.Text(field, member.Name);
			return DexcRuntimeUIRenderer.GetCommandInputText(
				field,
				DexcRuntimeUIRenderer.ValueText(DexcCommandRuntime.Drafts.GetInputValue(DexcRuntimeUIState.DraftId, field.Id))
			);
		}

		internal static string Error(DebugCommandFieldDescriptor field, string component, string validationMessage)
		{
			if (!DexcRuntimeUIState.CommandInvalidInput.Contains(field.Id))
				return validationMessage;
			if (DexcRuntimeCommandFormComponents.Supports(field) && !string.IsNullOrEmpty(component))
			{
				var member = field.Schema.Members.First(item => string.Equals(item.Name, component, StringComparison.OrdinalIgnoreCase));
				if (DexcRuntimeCommandFormComponents.IsInvalid(field, member))
					return "Invalid " + member.Name + " value.";
				if (field.Schema.Members.Any(item => DexcRuntimeCommandFormComponents.IsInvalid(field, item)))
					return validationMessage;
			}
			return "Invalid input.";
		}

		internal static void OpenReference(DebugCommandFieldDescriptor field, string focus = null)
		{
			if (!CanEdit)
				return;
			var draft = DexcRuntimeUIState.DraftId;
			DexcRuntimeFormDetail.OpenSelection(
				field.Label,
				(search, page) =>
				{
					if (draft != DexcRuntimeUIState.DraftId)
						return new DexcRuntimeFormDetail.ChoicePage(null, false, "The input is no longer available.");
					var list = DebugCommand.Draft.GetReferenceEntries(draft, field.Id, new DebugListQuery(string.Empty, search, page));
					if (list.Status != DebugOperationStatus.Succeeded)
						return new DexcRuntimeFormDetail.ChoicePage(null, false, list.Status.ToString());
					var choices = new List<DexcRuntimeFormDetail.Choice>();
					if (page.Offset == 0)
						choices.Add(new DexcRuntimeFormDetail.Choice("None", () => SetReference(draft, field, null)));
					foreach (var entry in list.Entries)
					{
						var captured = entry;
						choices.Add(
							new DexcRuntimeFormDetail.Choice(
								entry.Label + (string.IsNullOrEmpty(entry.Path) ? string.Empty : "\n" + entry.Path),
								() => SetReference(draft, field, captured)
							)
						);
					}
					return new DexcRuntimeFormDetail.ChoicePage(choices, list.Page.HasMore);
				},
				focus: focus
			);
		}

		static void SetReference(DebugCommandDraftId draft, DebugCommandFieldDescriptor field, DebugCommandReferenceEntry entry)
		{
			if (!CanEdit || draft != DexcRuntimeUIState.DraftId)
				return;
			var before = DexcRuntimeUIState
				.Command.Fields.Where(item => item.IsInput)
				.ToDictionary(item => item.Id, item => DexcCommandRuntime.Drafts.GetInputValue(draft, item.Id));
			var result =
				entry == null
					? DebugCommand.Draft.ClearReference(draft, field.Id)
					: DebugCommand.Draft.SelectReference(draft, field.Id, entry.Handle);
			if (result.Status != DebugCommandDraftOperationStatus.Succeeded)
			{
				DexcRuntimeUIState.Notice = "Reference selection failed: " + result.Status;
				return;
			}
			MarkEdited();
			DexcRuntimeUIState.CommandInvalidInput.Remove(field.Id);
			foreach (var item in DexcRuntimeUIState.Command.Fields.Where(item => item.IsInput))
			{
				var after = DexcCommandRuntime.Drafts.GetInputValue(draft, item.Id);
				if (Equals(before[item.Id], after) || DexcRuntimeUIState.CommandInvalidInput.Contains(item.Id))
					continue;
				DexcRuntimeUIState.CommandInputText.Remove(item.Id);
				DexcRuntimeCommandFormComponents.Invalidate(item);
			}
		}

		internal static void OpenEnum(DebugCommandFieldDescriptor field, string focus = null)
		{
			var type = EnumType(field);
			if (!CanEdit || type == null)
				return;
			var draft = DexcRuntimeUIState.DraftId;
			DexcRuntimeFormDetail.OpenSelection(
				field.Label,
				(search, page) =>
				{
					var names = Enum.GetNames(type).Where(name => name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
					var choices = names
						.Skip(page.Offset)
						.Take(page.Count)
						.Select(name => new DexcRuntimeFormDetail.Choice(
							name,
							() =>
							{
								if (CanEdit && DexcRuntimeUIState.DraftId == draft)
									DexcRuntimeUIRenderer.UpdateCommandInputText(field, name);
							}
						))
						.ToArray();
					return new DexcRuntimeFormDetail.ChoicePage(choices, page.Offset + page.Count < names.Length);
				},
				focus: focus
			);
		}

		internal static void OpenChoice(DebugCommandFieldDescriptor field, string focus = null)
		{
			if (!CanEdit)
				return;
			var draft = DexcRuntimeUIState.DraftId;
			DexcRuntimeFormDetail.OpenSelection(
				field.Label,
				(search, page) =>
				{
					var candidates = DexcCommandRuntime
						.Drafts.GetChoiceCandidates(draft, field.Id)
						.Where(item =>
							item.Label.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
							|| item.Path.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
						)
						.ToArray();
					var choices = candidates
						.Skip(page.Offset)
						.Take(page.Count)
						.Select(item => new DexcRuntimeFormDetail.Choice(
							item.Label,
							() => SelectChoice(draft, field, DebugValue.From(item.Key))
						))
						.ToList();
					if (page.Offset == 0)
						choices.Insert(0, new DexcRuntimeFormDetail.Choice("None", () => SelectChoice(draft, field, DebugValue.Null)));
					return new DexcRuntimeFormDetail.ChoicePage(choices, page.Offset + page.Count < candidates.Length);
				},
				focus: focus
			);
		}

		static void SelectChoice(DebugCommandDraftId draft, DebugCommandFieldDescriptor field, DebugValue value)
		{
			if (!CanEdit || draft != DexcRuntimeUIState.DraftId)
				return;
			MarkEdited();
			DexcRuntimeUIRenderer.UpdateCommandInput(field, value);
			DexcRuntimeUIState.CommandInputText.Remove(field.Id);
		}

		internal static void Toggle(DebugCommandFieldDescriptor field)
		{
			if (!CanEdit)
				return;
			var current = DexcCommandRuntime.Drafts.GetInputValue(DexcRuntimeUIState.DraftId, field.Id);
			var value = current != null && current.TryGetBoolean(out var flag) && flag;
			DexcRuntimeUIRenderer.UpdateCommandInputText(field, (!value).ToString());
		}

		internal static void OpenFullText(DebugCommandFieldDescriptor field, string focus = null)
		{
			if (!CanEdit)
				return;
			var draft = DexcRuntimeUIState.DraftId;
			DexcRuntimeFormDetail.OpenTextEditor(
				field.Label,
				DexcRuntimeUIRenderer.GetCommandInputText(field, ValueLabel(field)),
				text =>
				{
					if (CanEdit && draft == DexcRuntimeUIState.DraftId)
						DexcRuntimeUIRenderer.UpdateCommandInputText(field, text);
				},
				focus
			);
		}

		/// <summary>The enum type of an enum field, or null.</summary>
		static Type EnumType(DebugCommandFieldDescriptor field)
		{
			if (field.InputType == null || !field.InputType.IsGenericType)
				return null;
			var type = field.InputType.GetGenericArguments().FirstOrDefault();
			return type != null && type.IsEnum ? type : null;
		}

		/// <summary>
		/// Steps a boolean or an enum field in place. Choices and references have candidates that have to be searched, so
		/// they only open their selection page.
		/// </summary>
		internal static Func<int, bool> Cycle(DebugCommandFieldDescriptor field)
		{
			if (field.Schema.ValueKind == DebugValueKind.Boolean)
				return direction =>
				{
					if (!CanEdit)
						return false;
					Toggle(field);
					return true;
				};
			var type = field.Schema.SemanticType == "enum" ? EnumType(field) : null;
			if (type == null)
				return null;
			var draft = DexcRuntimeUIState.DraftId;
			return direction =>
			{
				if (!CanEdit || draft != DexcRuntimeUIState.DraftId)
					return false;
				var name = DexcUIRow.Step(Enum.GetNames(type), ValueLabel(field), direction);
				if (name == null)
					return false;
				DexcRuntimeUIRenderer.UpdateCommandInputText(field, name);
				return true;
			};
		}

		internal static bool UsesSelection(DebugCommandFieldDescriptor field) =>
			field.Schema != null
			&& (
				field.Schema.SemanticType == "reference"
				|| field.Schema.SemanticType == "enum"
				|| field.Schema.SemanticType == "choice"
				|| field.Schema.ValueKind == DebugValueKind.Boolean
			);

		internal static void Select(DebugCommandFieldDescriptor field, string focus = null)
		{
			if (field.Schema.SemanticType == "reference")
				OpenReference(field, focus);
			else if (field.Schema.SemanticType == "enum")
				OpenEnum(field, focus);
			else if (field.Schema.SemanticType == "choice")
				OpenChoice(field, focus);
			else
				Toggle(field);
		}

		internal static void OpenHistory()
		{
			if (!CanEdit || DexcRuntimeUIState.Command == null)
				return;
			var command = DexcRuntimeUIState.CommandId;
			DexcRuntimeFormDetail.OpenSelection(
				"Execution history",
				(_, page) =>
				{
					var history = DebugCommand.Draft.GetHistory(command, page);
					var choices = history
						.Entries.Select(entry => new DexcRuntimeFormDetail.Choice(
							entry.OccurredAtUtc.ToLocalTime().ToString("MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
								+ " · "
								+ entry.Status
								+ "\nOpen inputs",
							() => RestoreHistory(command, entry)
						))
						.ToArray();
					return new DexcRuntimeFormDetail.ChoicePage(choices, history.Page.HasMore);
				},
				searchable: false
			);
		}

		internal static bool RestoreHistory(DebugCommandId command, DebugCommandHistoryEntry entry)
		{
			if (
				entry == null
				|| DexcRuntimeUIState.AsyncExecution != null
				|| !DebugCommand.Registry.TryGetDescriptor(command, out var descriptor)
				|| descriptor.Path != entry.CommandPath
			)
				return false;
			var result = DebugCommand.Draft.CreateFromHistory(entry.Id);
			if (!result.TryGetDraftId(out var draft))
			{
				DexcRuntimeUIState.Notice = "History inputs could not be opened: " + result.Status;
				return false;
			}
			DexcRuntimeUIState.DiscardDraft();
			DexcRuntimeUIState.Command = descriptor;
			DexcRuntimeUIState.CommandId = command;
			DexcRuntimeUIState.DraftId = draft;
			DexcRuntimeUIState.HasDraft = true;
			DexcRuntimeUIState.LastCommandResult = null;
			DexcRuntimeUIState.CommandInputText.Clear();
			DexcRuntimeUIState.CommandInvalidInput.Clear();
			ResetResult();
			DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.CommandDetail);
			return true;
		}

		internal static string FieldKey(DebugCommandFieldId id, string component = null) =>
			DexcRuntimeUIRenderer.CurrentCommandInputPrefix() + id + (component == null ? string.Empty : ":" + component);

		/// <summary>
		/// The draft's validation, kept while the frame and the draft version stay the same: every field row's error reads
		/// it, and validation runs the project's own Validate. Building the page drops it, because Validate also reads game
		/// state that can change without the draft changing.
		/// </summary>
		internal static DebugCommandValidationResult Validation()
		{
			var draft = DexcRuntimeUIState.DraftId;
			var version = DexcCommandRuntime.Drafts.GetVersion(draft);
			if (Data.ValidationFrame != UnityEngine.Time.frameCount || Data.ValidationDraft != draft || Data.ValidationVersion != version)
			{
				DebugCommand.Draft.Validate(draft).TryGetValidation(out var validation);
				Data.Validation = validation;
				Data.ValidationFrame = UnityEngine.Time.frameCount;
				Data.ValidationDraft = draft;
				Data.ValidationVersion = version;
			}
			return Data.Validation;
		}

		internal static DebugCommandValidationIssue Issue(DebugCommandFieldDescriptor field) =>
			Validation()?.Issues.FirstOrDefault(issue => issue.HasField && issue.FieldId == field.Id);

		/// <summary>
		/// The detail page as rows. <paramref name="maximumRows"/> is how many rows the renderer shows at once; 0 means it
		/// scrolls freely and gets every field. Otherwise the fields are paged on fixed page boundaries, so Previous and
		/// Next always land on the same pages.
		/// </summary>
		internal static List<DexcUIRow> DetailRows(int maximumRows = 0)
		{
			var rows = new List<DexcUIRow>();
			var command = DexcRuntimeUIState.Command;
			if (command == null)
				return rows;
			Data.ValidationFrame = -1;
			if (!string.IsNullOrEmpty(command.Description))
				rows.Add(
					new DexcUIRow
					{
						Kind = DexcUIRowKind.Label,
						Key = "command:description",
						Label = command.Description,
					}
				);
			if (DexcRuntimeUIState.Confirming)
				rows.Add(
					new DexcUIRow
					{
						Kind = DexcUIRowKind.Label,
						Key = "command:confirmation",
						Label = DexcRuntimeCommandExecution.ConfirmationMessage,
						Tone = DexcUIRowTone.Warning,
					}
				);
			if (DexcRuntimeUIState.AsyncExecution != null)
				rows.Add(
					new DexcUIRow
					{
						Kind = DexcUIRowKind.Label,
						Key = "command:running",
						Label = "Running · cancellation is cooperative",
						Tone = DexcUIRowTone.Warning,
					}
				);
			var fields = command.Fields.Select(FieldRows).ToList();
			if (!string.Equals(Data.FieldWindowPath, command.Path, StringComparison.Ordinal))
			{
				Data.FieldWindowPath = command.Path;
				Data.FieldWindowOffset = 0;
			}
			var start = 0;
			var end = fields.Count;
			var previous = -1;
			var next = -1;
			// A result adds two rows and paging two more; when everything fits there is no paging at all.
			var available = maximumRows - rows.Count;
			if (maximumRows > 0 && fields.Sum(field => field.Count) > available - 3)
				Window(fields, Math.Max(1, available - 5), out start, out end, out previous, out next);
			if (previous >= 0)
				rows.Add(NavigationRow("command:previous", "Previous fields", previous));
			for (var index = start; index < end; index++)
				rows.AddRange(fields[index]);
			if (next >= 0)
				rows.Add(NavigationRow("command:next", "Next fields", next));
			if (CurrentResult != null)
			{
				rows.Add(
					new DexcUIRow
					{
						Kind = DexcUIRowKind.Label,
						Key = "command:result",
						Text = () => ResultSummary,
					}
				);
				rows.Add(
					new DexcUIRow
					{
						Kind = DexcUIRowKind.Button,
						Key = "command:view-result",
						Label = "View result",
						Activated = () => DexcRuntimeFormDetail.Open(ResultDetails),
					}
				);
			}
			return rows;
		}

		static DexcUIRow NavigationRow(string key, string label, int offset)
		{
			return new DexcUIRow
			{
				Kind = DexcUIRowKind.Button,
				Key = key,
				Label = label,
				Activated = () => Data.FieldWindowOffset = offset,
			};
		}

		/// <summary>Cuts the fields into pages of at most <paramref name="budget"/> rows and picks the page holding the offset.</summary>
		static void Window(List<List<DexcUIRow>> fields, int budget, out int start, out int end, out int previous, out int next)
		{
			var pageStarts = new List<int>();
			var index = 0;
			while (index < fields.Count)
			{
				pageStarts.Add(index);
				var pageRows = 0;
				do
				{
					pageRows += fields[index].Count;
					index++;
				} while (index < fields.Count && pageRows + fields[index].Count <= budget);
			}

			var offset = Math.Max(0, Math.Min(Data.FieldWindowOffset, fields.Count - 1));
			var page = 0;
			for (var candidate = 1; candidate < pageStarts.Count && pageStarts[candidate] <= offset; candidate++)
				page = candidate;
			start = pageStarts[page];
			end = page + 1 < pageStarts.Count ? pageStarts[page + 1] : fields.Count;
			previous = page == 0 ? -1 : pageStarts[page - 1];
			next = page + 1 < pageStarts.Count ? end : -1;
			Data.FieldWindowOffset = start;
		}

		/// <summary>The rows of one field: a heading, a choice, one input per vector component, an input, or a read-only value.</summary>
		static List<DexcUIRow> FieldRows(DebugCommandFieldDescriptor field)
		{
			var rows = new List<DexcUIRow>();
			var key = FieldKey(field.Id);
			if (!field.IsInput)
			{
				rows.Add(
					new DexcUIRow
					{
						Kind = DexcUIRowKind.Header,
						Key = key,
						Label = field.Label,
					}
				);
				return rows;
			}

			var schema = field.Schema;
			var tone = Tone(Issue(field));
			if (schema == null)
				rows.Add(
					new DexcUIRow
					{
						Kind = DexcUIRowKind.Label,
						Key = key,
						Label = field.Label + ": unsupported",
						Error = () => IssueText(Issue(field)),
						Tone = tone,
					}
				);
			else if (UsesSelection(field))
				rows.Add(
					new DexcUIRow
					{
						Kind = DexcUIRowKind.Choice,
						Key = key,
						Label = field.Label,
						Text = () => RowValue(field, null),
						Error = () => IssueText(Issue(field)),
						Boolean = schema.ValueKind == DebugValueKind.Boolean,
						Activated = () => Select(field),
						Cycle = Cycle(field),
					}
				);
			else if (DexcRuntimeCommandFormComponents.Supports(field))
			{
				for (var index = 0; index < schema.Members.Count; index++)
					rows.Add(ComponentRow(field, schema.Members[index].Name, index == 0));
			}
			else if (schema.ValueKind == DebugValueKind.Array || schema.ValueKind == DebugValueKind.Object)
				rows.Add(
					new DexcUIRow
					{
						Kind = DexcUIRowKind.Label,
						Key = key,
						Text = () =>
							field.Label
							+ " · "
							+ schema.ValueKind
							+ "\n"
							+ DexcRuntimeUIRenderer.ValueText(
								DexcCommandRuntime.Drafts.GetInputValue(DexcRuntimeUIState.DraftId, field.Id)
							),
						Error = () => IssueText(Issue(field)),
						Tone = tone,
					}
				);
			else
			{
				rows.Add(ValueRow(field, tone));
				if (field.IsSlider)
					rows.Add(SliderRow(field));
			}

			foreach (var row in rows)
			{
				row.Group = key;
				row.Tag = field.Id;
				row.Enabled = () => CanEdit;
			}
			rows[0].Immediate = () => DexcRuntimeUIRenderer.TryDrawRuntimeInputEditor(field);
			return rows;
		}

		static DexcUIRowTone Tone(DebugCommandValidationIssue issue) =>
			issue == null ? DexcUIRowTone.Normal
			: issue.Severity == DebugValidationSeverity.Warning ? DexcUIRowTone.Warning
			: DexcUIRowTone.Error;

		static DexcUIRow ValueRow(DebugCommandFieldDescriptor field, DexcUIRowTone tone)
		{
			var draft = DexcRuntimeUIState.DraftId;
			return new DexcUIRow
			{
				Kind = DexcUIRowKind.Input,
				Key = FieldKey(field.Id),
				Label = field.Label,
				Text = () => RowValue(field, null),
				Error = () => Error(field, null, IssueText(Issue(field))),
				Tone = tone,
				Changed = text =>
				{
					// An edit for a draft that has been replaced, or that repeats the current text, changes nothing.
					if (draft != DexcRuntimeUIState.DraftId || !CanEdit || text == RowValue(field, null))
						return;
					DexcRuntimeUIRenderer.UpdateCommandInputText(field, text);
				},
				Auxiliary = field.Schema.ValueKind == DebugValueKind.String ? () => OpenFullText(field) : (Action)null,
				Multiline = field.IsMultiline,
			};
		}

		static DexcUIRow SliderRow(DebugCommandFieldDescriptor field)
		{
			var draft = DexcRuntimeUIState.DraftId;
			return new DexcUIRow
			{
				Kind = DexcUIRowKind.Slider,
				Key = FieldKey(field.Id) + ":slider",
				SliderValue = () => SliderValue(field),
				Slid = fraction =>
				{
					if (draft != DexcRuntimeUIState.DraftId || !CanEdit)
						return;
					DexcRuntimeUIRenderer.UpdateCommandInputText(field, SliderText(field, fraction));
				},
			};
		}

		internal static float SliderValue(DebugCommandFieldDescriptor field)
		{
			var value = DexcCommandRuntime.Drafts.GetInputValue(DexcRuntimeUIState.DraftId, field.Id);
			var range = field.RangeMaximum - field.RangeMinimum;
			if (!DebugCommandFieldDescriptor.TryGetNumber(value, out var number) || !(range > 0d))
				return 0f;
			return UnityEngine.Mathf.Clamp01((float)((number - field.RangeMinimum) / range));
		}

		/// <summary>The input text for a slider position: rounded to a whole number unless the input is floating point.</summary>
		internal static string SliderText(DebugCommandFieldDescriptor field, float fraction)
		{
			var floating = field.Schema.ValueKind == DebugValueKind.FloatingPoint;
			var number = field.RangeMinimum * (1d - UnityEngine.Mathf.Clamp01(fraction)) + field.RangeMaximum * UnityEngine.Mathf.Clamp01(fraction);
			return floating ? number.ToString("R", CultureInfo.InvariantCulture) : Math.Round(number).ToString("0", CultureInfo.InvariantCulture);
		}

		static DexcUIRow ComponentRow(DebugCommandFieldDescriptor field, string member, bool first)
		{
			var draft = DexcRuntimeUIState.DraftId;
			return new DexcUIRow
			{
				Kind = DexcUIRowKind.Input,
				Key = FieldKey(field.Id, member),
				Label = first ? field.Label : string.Empty,
				Component = member,
				Text = () => DexcRuntimeCommandFormComponents.Text(field, member),
				Error = () => Error(field, member, IssueText(Issue(field))),
				Changed = text =>
				{
					if (draft != DexcRuntimeUIState.DraftId || !CanEdit)
						return;
					DexcRuntimeCommandFormComponents.Change(field, member, text);
				},
			};
		}

		/// <summary>The command list as rows: search, one row per command on this page, and paging.</summary>
		internal static List<DexcUIRow> ListRows()
		{
			// The gamepad starts on the search field, the first row, and moves down into the entries.
			var search = DexcUIRow.Search("search:command", () => DexcRuntimeUIState.Search, text => DexcRuntimeUIState.Search = text);
			search.NavigationIndex = 0;
			var rows = new List<DexcUIRow> { search };
			// A search looks through every folder, so its results are listed flat with their full paths.
			if (!string.IsNullOrEmpty(DexcRuntimeUIState.Search))
				return SearchRows(rows);
			// The search and the folders share one page offset. Clearing the search returns to the folder's first page, and
			// forgetting the search lets the next one, even the same text, start at its first page too.
			if (!string.IsNullOrEmpty(ListSearch))
			{
				ListSearch = string.Empty;
				ListOffset = 0;
			}
			var entries = Data.InFavorites ? FavoriteEntries() : FolderEntries();
			var navigation = 1;
			foreach (var entry in entries.Skip(ListOffset).Take(PageEntries))
			{
				entry.NavigationIndex = navigation++;
				rows.Add(entry);
			}
			if (entries.Count == 0)
				rows.Add(
					new DexcUIRow
					{
						Kind = DexcUIRowKind.Label,
						Key = "command-list:empty",
						Label = Data.InFavorites ? "No favorites. Star a command on its page." : "No entries",
					}
				);
			if (ListOffset > 0)
				rows.Add(DexcUIRows.Paging("command-list:previous", "Previous", navigation++, () => MoveFolderPage(-1)));
			if (ListOffset + PageEntries < entries.Count)
				rows.Add(DexcUIRows.Paging("command-list:next", "Next", navigation, () => MoveFolderPage(1)));
			return rows;
		}

		/// <summary>Entries on one page of a folder, folders and the favorites entry included.</summary>
		const int PageEntries = 20;

		static List<DexcUIRow> SearchRows(List<DexcUIRow> rows)
		{
			var list = ReadList();
			DexcRuntimeUIState.SetSession(list.Summary.SessionId);
			var navigation = 1;
			foreach (var item in list.Items)
			{
				var target = item;
				rows.Add(
					new DexcUIRow
					{
						Kind = DexcUIRowKind.Button,
						Key = "command-list:" + item.Id,
						Label = item.Path + (string.IsNullOrEmpty(item.Description) ? string.Empty : "\n" + item.Description),
						NavigationIndex = navigation++,
						ListItem = true,
						Activated = () => DexcRuntimeUIRenderer.SelectCommand(target),
					}
				);
			}
			if (list.Items.Count == 0)
				rows.Add(
					new DexcUIRow
					{
						Kind = DexcUIRowKind.Label,
						Key = "command-list:empty",
						Label = "No entries",
					}
				);
			if (ListOffset > 0)
				rows.Add(DexcUIRows.Paging("command-list:previous", "Previous", navigation++, () => MoveListPage(-1)));
			if (list.Page.HasMore)
				rows.Add(DexcUIRows.Paging("command-list:next", "Next", navigation, () => MoveListPage(1)));
			return rows;
		}

		/// <summary>
		/// Every command, read once a frame. Folders are not a registry concept: they come from the slashes in the paths,
		/// so the whole list is needed to know which folders exist and how many commands each holds.
		/// </summary>
		internal static IReadOnlyList<DebugCommandListItem> AllItems()
		{
			var frame = UnityEngine.Time.frameCount;
			if (Data.AllItems != null && Data.AllItemsFrame == frame && ListSession == DexcDebugger.CurrentSession.SessionId)
				return Data.AllItems;
			var items = new List<DebugCommandListItem>();
			while (true)
			{
				var page = DebugCommand.Registry.GetList(
					new DebugListQuery(string.Empty, string.Empty, DexcRuntimeUIState.CreatePageRequest(items.Count, 100))
				);
				DexcRuntimeUIState.SetSession(page.Summary.SessionId);
				items.AddRange(page.Items);
				if (!page.Page.HasMore || page.Items.Count == 0)
					break;
			}
			ListSession = DexcDebugger.CurrentSession.SessionId;
			Data.AllItems = items;
			Data.AllItemsFrame = frame;
			return items;
		}

		/// <summary>The top shows the favorites entry first; then each folder with its count, then the commands here.</summary>
		static List<DexcUIRow> FolderEntries()
		{
			var folder = Data.Folder;
			var prefix = folder.Length == 0 ? string.Empty : folder + "/";
			var folders = new List<string>();
			var counts = new Dictionary<string, int>(StringComparer.Ordinal);
			var commands = new List<DebugCommandListItem>();
			foreach (var item in AllItems())
			{
				if (!item.Path.StartsWith(prefix, StringComparison.Ordinal))
					continue;
				var rest = item.Path.Substring(prefix.Length);
				var slash = rest.IndexOf('/');
				if (slash < 0)
				{
					commands.Add(item);
					continue;
				}
				var name = rest.Substring(0, slash);
				if (!counts.ContainsKey(name))
				{
					folders.Add(name);
					counts[name] = 0;
				}
				counts[name]++;
			}
			var entries = new List<DexcUIRow>();
			if (folder.Length == 0)
			{
				var starred = DexcRuntimeFavorites.All(DexcFavoriteKind.Command).Count;
				entries.Add(
					new DexcUIRow
					{
						Kind = DexcUIRowKind.Button,
						Key = "command-list:favorites",
						Label = "Favorites",
						Leading = "★",
						Trailing = () => starred.ToString(CultureInfo.InvariantCulture),
						Drill = true,
						ListItem = true,
						Activated = () => OpenFolder(string.Empty, true),
					}
				);
			}
			foreach (var name in folders)
			{
				var path = prefix + name;
				var count = counts[name];
				entries.Add(
					new DexcUIRow
					{
						Kind = DexcUIRowKind.Button,
						Key = "command-folder:" + path,
						Label = name,
						Trailing = () => count.ToString(CultureInfo.InvariantCulture),
						Drill = true,
						ListItem = true,
						Activated = () => OpenFolder(path, false),
					}
				);
			}
			foreach (var item in commands)
				entries.Add(CommandEntry(item, item.Path.Substring(prefix.Length), item.Description));
			return entries;
		}

		/// <summary>The starred commands that still exist, each with the folder it lives in.</summary>
		static List<DexcUIRow> FavoriteEntries()
		{
			var items = AllItems();
			var entries = new List<DexcUIRow>();
			foreach (var path in DexcRuntimeFavorites.All(DexcFavoriteKind.Command))
			{
				var item = items.FirstOrDefault(candidate => candidate.Path == path);
				if (item == null)
					continue;
				var slash = path.LastIndexOf('/');
				entries.Add(CommandEntry(item, item.DisplayName, slash < 0 ? null : path.Substring(0, slash)));
			}
			return entries;
		}

		static DexcUIRow CommandEntry(DebugCommandListItem item, string label, string detail)
		{
			return new DexcUIRow
			{
				Kind = DexcUIRowKind.Button,
				Key = "command-list:" + item.Id,
				Label = label,
				Leading = DexcRuntimeFavorites.Contains(DexcFavoriteKind.Command, item.Path) ? "★" : null,
				Detail = string.IsNullOrEmpty(detail) ? (Func<string>)null : () => detail,
				ListItem = true,
				Activated = () => DexcRuntimeUIRenderer.SelectCommand(item),
			};
		}

		static void OpenFolder(string folder, bool favorites)
		{
			Data.Folder = folder;
			Data.InFavorites = favorites;
			ListOffset = 0;
			DexcRuntimeUIState.SelectedIndex = 0;
			DexcRuntimeUIState.ScrollPosition = UnityEngine.Vector2.zero;
		}

		static void MoveFolderPage(int direction)
		{
			ListOffset = Math.Max(0, ListOffset + Math.Sign(direction) * PageEntries);
			DexcRuntimeUIState.SelectedIndex = 0;
			DexcRuntimeUIState.ScrollPosition = UnityEngine.Vector2.zero;
		}

		/// <summary>The list's place under "Command": the open folder's parts, or the favorites.</summary>
		internal static IEnumerable<string> FolderSegments =>
			!string.IsNullOrEmpty(DexcRuntimeUIState.Search) ? Enumerable.Empty<string>()
			: Data.InFavorites ? new[] { "Favorites" }
			: Data.Folder.Length == 0 ? Enumerable.Empty<string>()
			: Data.Folder.Split('/');

		internal static void OpenTop() => OpenFolder(string.Empty, false);

		/// <summary>Back on the list goes up one folder first. False at the top, where Back leaves the list.</summary>
		/// <summary>Whether Back leaves the list rather than going up a folder: at its top, or while it is searched.</summary>
		internal static bool AtTop => !string.IsNullOrEmpty(DexcRuntimeUIState.Search) || !Data.InFavorites && Data.Folder.Length == 0;

		internal static bool LeaveFolder()
		{
			if (AtTop)
				return false;
			var slash = Data.Folder.LastIndexOf('/');
			OpenFolder(Data.InFavorites || slash < 0 ? string.Empty : Data.Folder.Substring(0, slash), false);
			return true;
		}
	}
}
