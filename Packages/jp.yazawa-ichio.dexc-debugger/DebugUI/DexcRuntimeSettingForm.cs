using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace Dexc
{
	// UI drafts are per property. The backend intentionally has no class transaction.
	internal static class DexcRuntimeSettingForm
	{
		internal sealed class Group
		{
			internal DebugSettingSetId Id;
			internal string Label;
			internal DebugSettingListItem[] Items;
		}

		internal sealed class Field
		{
			internal DebugSettingListItem Item;
			internal DebugSettingDescriptor Descriptor;
			internal Type Type;
			internal DebugResult<DebugValue> Read;
			internal DebugValue Applied;
			internal DebugValue Value;
			internal string Raw;
			internal string Error;
			internal bool Dirty;

			/// <summary>Just written: the next refresh shows the value read back, not the text that was typed.</summary>
			internal bool Written;
			internal int Revision;
			internal int ConfirmedRevision;
			internal readonly Dictionary<string, string> Components = new Dictionary<string, string>();
			internal bool Writable => Item.Access == DebugSettingAccess.ReadWrite && Descriptor != null;
			internal bool IsChoice => Type == typeof(bool) || Type != null && Type.IsEnum;
			internal bool IsSlider => Descriptor != null && Descriptor.IsSlider && Descriptor.HasRange;
			internal bool CanSlide =>
				IsSlider
				&& Descriptor.RangeMaximum > Descriptor.RangeMinimum
				&& !double.IsInfinity(Descriptor.RangeMinimum)
				&& !double.IsInfinity(Descriptor.RangeMaximum);
			internal string Label => Item.DisplayName;
			internal string Note =>
				!Writable ? "Read only"
				: Item.ApplyMode == DebugSettingApplyMode.Immediate ? "Immediate"
				: null;
			internal int RowCount => Math.Max(1, Components.Count) + (IsSlider ? 1 : 0);
		}

		/// <summary>State of this class. Owned by <see cref="DexcUIRoot"/>; nothing here survives a root reset.</summary>
		internal sealed class State
		{
			internal object Session;
			internal List<Group> Groups = new List<Group>();
			internal List<DebugSettingPresetItem> Presets = new List<DebugSettingPresetItem>();
			internal float NextRead;
			internal bool Writing;
			internal bool PresetSelected;
			internal DebugSettingPresetItem SelectedPreset;
			internal Group Current;
			internal IReadOnlyList<Field> Fields = Array.Empty<Field>();
			internal string MatchingPreset = "Undetermined";
			internal int Version;
			internal int WindowOffset;
			internal int ListOffset;
			internal string ListSearch;

			/// <summary>The folder the list shows, as a path without a trailing slash; empty for the top.</summary>
			internal string Folder = string.Empty;

			/// <summary>The list shows the starred groups instead of a folder.</summary>
			internal bool InFavorites;
		}

		internal static State Data => DexcUIRoot.Current.SettingForm;
		static object Session
		{
			get => Data.Session;
			set => Data.Session = value;
		}
		static List<Group> Groups
		{
			get => Data.Groups;
			set => Data.Groups = value;
		}
		static List<DebugSettingPresetItem> Presets
		{
			get => Data.Presets;
			set => Data.Presets = value;
		}
		static float NextRead
		{
			get => Data.NextRead;
			set => Data.NextRead = value;
		}
		static bool Writing
		{
			get => Data.Writing;
			set => Data.Writing = value;
		}
		static bool PresetSelected
		{
			get => Data.PresetSelected;
			set => Data.PresetSelected = value;
		}
		static DebugSettingPresetItem SelectedPreset
		{
			get => Data.SelectedPreset;
			set => Data.SelectedPreset = value;
		}
		internal static Group Current
		{
			get => Data.Current;
			private set => Data.Current = value;
		}
		internal static IReadOnlyList<Field> Fields
		{
			get => Data.Fields;
			private set => Data.Fields = value;
		}
		internal static string MatchingPreset
		{
			get => Data.MatchingPreset;
			private set => Data.MatchingPreset = value;
		}
		internal static int Version
		{
			get => Data.Version;
			private set => Data.Version = value;
		}
		internal static int WindowOffset
		{
			get => Data.WindowOffset;
			set => Data.WindowOffset = value;
		}
		internal static int ListOffset
		{
			get => Data.ListOffset;
			set => Data.ListOffset = value;
		}
		static string ListSearch
		{
			get => Data.ListSearch;
			set => Data.ListSearch = value;
		}

		internal static IReadOnlyList<Field> VisibleFields()
		{
			var result = new List<Field>();
			var rows = 0;
			foreach (var field in Fields.Skip(WindowOffset))
			{
				if (rows + field.RowCount > 16 && result.Count > 0)
					break;
				result.Add(field);
				rows += field.RowCount;
			}
			return result;
		}

		internal static bool HasNextFields => WindowOffset + VisibleFields().Count < Fields.Count;

		internal static void NextFields() => WindowOffset += VisibleFields().Count;

		internal static void PreviousFields()
		{
			var rows = 0;
			while (WindowOffset > 0 && rows + Fields[WindowOffset - 1].RowCount <= 16)
				rows += Fields[--WindowOffset].RowCount;
		}

		internal static bool IsCurrent(Field field) =>
			DexcRuntimeUIState.IsOpen
			&& ReferenceEquals(Session, DexcDebugger.CurrentSession)
			&& (DexcRuntimeUIState.Page == DexcRuntimeUIPage.SettingDetail || DexcRuntimeUIState.Page == DexcRuntimeUIPage.FormDetail)
			&& Fields.Contains(field);

		internal static bool CanApply =>
			Current != null
			&& !Writing
			&& Fields.All(field => !field.Writable || field.Value != null)
			&& Fields.Any(field => field.Writable && field.Dirty && field.Item.ApplyMode == DebugSettingApplyMode.Submit);

		internal static void ClearSession()
		{
			Reset();
			Session = null;
			Groups.Clear();
			Presets.Clear();
			ListOffset = 0;
		}

		internal static void Reset()
		{
			Version++;
			DexcRuntimeUIState.Setting = null;
			DexcRuntimeUIState.SettingId = default(DebugSettingId);
			Current = null;
			Fields = Array.Empty<Field>();
			WindowOffset = 0;
			PresetSelected = false;
			SelectedPreset = null;
			MatchingPreset = "Undetermined";
		}

		internal static IReadOnlyList<Group> ReadGroups(string search = "")
		{
			if (!ReferenceEquals(Session, DexcDebugger.CurrentSession))
			{
				Reset();
				Session = DexcDebugger.CurrentSession;
				Groups.Clear();
			}
			if (Groups.Count == 0)
			{
				var items = new List<DebugSettingListItem>();
				var offset = 0;
				while (true)
				{
					var page = DebugSetting.GetList(
						new DebugListQuery(string.Empty, string.Empty, DexcRuntimeUIState.CreatePageRequest(offset, 100))
					);
					DexcRuntimeUIState.SetSession(page.Summary.SessionId);
					items.AddRange(page.Items);
					if (!page.Page.HasMore || page.Items.Count == 0)
						break;
					offset += page.Items.Count;
				}
				Groups = items
					.GroupBy(item => item.SetId)
					.Select(group =>
					{
						var all = group.OrderBy(item => item.Path, StringComparer.Ordinal).ToArray();
						var common = all[0].Path.Split('/').Take(all[0].Path.Split('/').Length - 1).ToArray();
						foreach (var item in all.Skip(1))
						{
							var parent = item.Path.Split('/');
							common = common.TakeWhile((part, index) => index < parent.Length - 1 && part == parent[index]).ToArray();
						}
						return new Group
						{
							Id = group.Key,
							Items = all,
							Label = common.Length > 0 ? string.Join("/", common) : string.Join(" / ", all.Select(item => item.DisplayName)),
						};
					})
					.ToList();
				// Two classes can share a parent, as the built-in Display settings and a game's Display/Enemy do. A favorite is
				// saved by the label, so each of them is named by its first setting below that parent: paths are unique, so
				// no two such names are the same, and the label stays short however many settings the class has.
				foreach (var same in Groups.GroupBy(group => group.Label, StringComparer.Ordinal).Where(same => same.Count() > 1))
				{
					foreach (var group in same)
					{
						var path = group.Items[0].Path;
						var first = (path.StartsWith(same.Key + "/", StringComparison.Ordinal) ? path.Substring(same.Key.Length + 1) : path)
							.Replace("/", " · ");
						var more = group.Items.Length > 1 ? " +" + (group.Items.Length - 1) : string.Empty;
						group.Label += " (" + first + more + ")";
					}
				}
				Groups = Groups.OrderBy(group => group.Label, StringComparer.Ordinal).ToList();
			}
			if (ListSearch != search)
			{
				ListSearch = search;
				ListOffset = 0;
			}
			return Groups
				.Where(group =>
					string.IsNullOrEmpty(search)
					|| group.Label.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
					|| group.Items.Any(item => item.Path.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
				)
				.ToArray();
		}

		internal static void Open(DebugSettingListItem item)
		{
			var group = ReadGroups().FirstOrDefault(value => value.Id == item.SetId);
			if (group == null)
				return;
			Reset();
			Current = group;
			DexcRuntimeUIState.Setting = item;
			DexcRuntimeUIState.SettingId = item.Id;
			DexcRuntimeUIState.Notice = string.Empty;
			Fields = group
				.Items.Select(value =>
				{
					DebugSetting.TryGetDescriptor(value.Id, out var descriptor);
					return new Field
					{
						Item = value,
						Descriptor = descriptor,
						Type = DexcRuntimeUIRenderer.ResolveType(value.ValueType),
					};
				})
				.ToArray();
			Presets.Clear();
			var offset = 0;
			while (true)
			{
				var page = DebugSetting.GetPresets(group.Id, DexcRuntimeUIState.CreatePageRequest(offset, 100));
				Presets.AddRange(page.Items);
				if (!page.Page.HasMore || page.Items.Count == 0)
					break;
				offset += page.Items.Count;
			}
			DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.SettingDetail);
			Refresh(true);
		}

		internal static void Refresh(bool discard = false)
		{
			if (Current == null || !ReferenceEquals(Session, DexcDebugger.CurrentSession))
				return;
			foreach (var field in Fields)
			{
				field.Read = DebugSetting.Read(field.Item.Id);
				field.Read.TryGetValue(out field.Applied);
				if (discard || !field.Dirty)
				{
					// While the value stays the same, the text being typed stays too: "0." on the way to "0.05" reads as 0.
					if (!discard && !field.Written && field.Value != null && field.Value.Equals(field.Applied))
						continue;
					field.Written = false;
					field.Value = field.Applied;
					field.Raw = DexcInputText.Format(field.Value);
					field.Dirty = false;
					field.Error = field.Applied == null ? field.Read.Status.ToString() : null;
					field.Components.Clear();
					if (field.Value != null && field.Value.TryGetObject(out var members))
					{
						foreach (var member in members)
							field.Components[member.Name] = DexcInputText.Format(member.Value);
					}
				}
			}
			Version++;
			MatchPreset();
			NextRead = Time.realtimeSinceStartup + 0.25f;
		}

		internal static void Tick()
		{
			if (DexcRuntimeUIState.Page == DexcRuntimeUIPage.SettingDetail && Time.realtimeSinceStartup >= NextRead)
				Refresh();
		}

		internal static string Text(Field field, string component = null) =>
			string.IsNullOrEmpty(component) ? field.Raw : field.Components[component];

		internal static void Change(Field field, string text, string component = null)
		{
			if (!IsCurrent(field) || !field.Writable || Writing)
				return;
			if (Text(field, component) == text)
				return;
			if (!string.IsNullOrEmpty(component))
			{
				field.Components[component] = text;
				text = string.Join(", ", field.Components.Select(value => value.Key + "=" + value.Value));
			}
			field.Raw = text;
			field.Value = DexcInputText.ParseLike(field.Descriptor.DefaultValue, text, field.Type);
			if (
				field.Value != null
				&& field.Descriptor.HasRange
				&& Number(field.Value, out var number)
				&& (number < field.Descriptor.RangeMinimum || number > field.Descriptor.RangeMaximum)
			)
				field.Value = null;
			if (component == null && field.Value != null && field.Value.TryGetObject(out var members))
			{
				foreach (var member in members)
					field.Components[member.Name] = DexcInputText.Format(member.Value);
			}
			field.Error = field.Value == null ? "Invalid input. Use a decimal point (.) and a value within the field range." : null;
			field.Dirty = field.Value == null || !field.Value.Equals(field.Applied);
			field.Revision++;
			Version++;
		}

		internal static bool Confirm(Field field)
		{
			if (!IsCurrent(field) || field.ConfirmedRevision == field.Revision)
				return false;
			field.ConfirmedRevision = field.Revision;
			if (!field.Writable || !field.Dirty || field.Value == null || field.Item.ApplyMode != DebugSettingApplyMode.Immediate)
				return false;
			Write(field);
			return true;
		}

		internal static void Apply()
		{
			if (!DexcRuntimeUIState.IsOpen || DexcRuntimeUIState.Page != DexcRuntimeUIPage.SettingDetail || !CanApply)
				return;
			foreach (
				var field in Fields
					.Where(value => value.Writable && value.Dirty && value.Item.ApplyMode == DebugSettingApplyMode.Submit)
					.ToArray()
			)
				Write(field);
		}

		static void Write(Field field, bool reset = false)
		{
			if (!IsCurrent(field) || !field.Writable || Writing)
				return;
			Writing = true;
			try
			{
				var result = reset ? DebugSetting.Reset(field.Item.Id) : DebugSetting.Write(field.Item.Id, field.Value);
				if (!IsCurrent(field))
					return;
				field.Dirty = result.Status != DebugOperationStatus.Succeeded;
				field.Written = !field.Dirty;
				field.Error = field.Dirty ? Failure(result.Status, result.Issues) : null;
				Refresh();
			}
			finally
			{
				Writing = false;
			}
		}

		static string Failure(DebugOperationStatus status, IReadOnlyList<DebugValidationIssue> issues) =>
			status + (issues.Count == 0 ? string.Empty : "\n" + string.Join("\n", issues.Select(issue => issue.Message)));

		internal static void Choose(Field field)
		{
			if (!IsCurrent(field) || !field.Writable)
				return;
			if (field.Type == typeof(bool))
			{
				Change(field, field.Raw == "true" ? "false" : "true");
				Confirm(field);
				return;
			}
			DexcRuntimeFormDetail.OpenSelection(
				field.Item.DisplayName,
				(search, page) =>
				{
					var names = Enum.GetNames(field.Type)
						.Where(name => name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
						.ToArray();
					return new DexcRuntimeFormDetail.ChoicePage(
						names
							.Skip(page.Offset)
							.Take(page.Count)
							.Select(name => new DexcRuntimeFormDetail.Choice(
								name,
								() =>
								{
									Change(field, name);
									Confirm(field);
								}
							))
							.ToArray(),
						page.Offset + page.Count < names.Length
					);
				}
			);
		}

		/// <summary>Steps a boolean or enum field in place and writes it as choosing it on the selection page would.</summary>
		internal static bool Cycle(Field field, int direction)
		{
			if (!IsCurrent(field) || !field.Writable || !field.IsChoice)
				return false;
			var name =
				field.Type == typeof(bool)
					? field.Raw == "true" ? "false" : "true"
					: DexcUIRow.Step(Enum.GetNames(field.Type), field.Raw, direction);
			if (name == null)
				return false;
			Change(field, name);
			Confirm(field);
			return true;
		}

		internal static void OpenText(Field field)
		{
			if (!IsCurrent(field))
				return;
			if (!field.Writable)
				DexcRuntimeFormDetail.Open(field.Raw);
			else
				DexcRuntimeFormDetail.OpenTextEditor(
					field.Item.DisplayName,
					field.Raw,
					text => Change(field, text),
					confirmed: () => Confirm(field)
				);
		}

		internal static float SliderValue(Field field)
		{
			if (!field.CanSlide || !Number(field.Value ?? field.Applied, out var number))
				return 0f;
			var min = field.Descriptor.RangeMinimum;
			var max = field.Descriptor.RangeMaximum;
			var range = max - min;
			var ratio = double.IsInfinity(range) ? (number / 2d - min / 2d) / (max / 2d - min / 2d) : (number - min) / range;
			return Mathf.Clamp01((float)ratio);
		}

		internal static void ChangeSlider(Field field, float fraction)
		{
			if (!field.CanSlide || float.IsNaN(fraction) || float.IsInfinity(fraction))
				return;
			fraction = Mathf.Clamp01(fraction);
			var min = field.Descriptor.RangeMinimum;
			var max = field.Descriptor.RangeMaximum;
			var number = min * (1d - fraction) + max * fraction;
			if (field.Descriptor.DefaultValue.Kind != DebugValueKind.FloatingPoint)
				number = Math.Round(number);
			Change(
				field,
				number.ToString(
					field.Descriptor.DefaultValue.Kind == DebugValueKind.FloatingPoint ? "R" : "0",
					CultureInfo.InvariantCulture
				)
			);
			Confirm(field);
		}

		static bool Number(DebugValue value, out double number)
		{
			number = 0;
			if (value == null)
				return false;
			if (value.TryGetFloatingPoint(out number))
				return true;
			if (value.TryGetSignedInteger(out var signed))
			{
				number = signed;
				return true;
			}
			if (value.TryGetUnsignedInteger(out var unsigned))
			{
				number = unsigned;
				return true;
			}
			return false;
		}

		internal static void OpenPresets()
		{
			if (Current == null)
				return;
			Refresh(true);
			PresetSelected = false;
			SelectedPreset = null;
			DexcRuntimeFormDetail.OpenSelection(
				"Presets",
				(_, page) =>
				{
					var choices = new List<DexcRuntimeFormDetail.Choice>();
					choices.Add(
						new DexcRuntimeFormDetail.Choice(
							(PresetSelected && SelectedPreset == null ? "✓ " : "") + "Initial values",
							() =>
							{
								PresetSelected = true;
								SelectedPreset = null;
							}
						)
					);
					choices.AddRange(
						Presets.Select(preset => new DexcRuntimeFormDetail.Choice(
							(SelectedPreset == preset ? "✓ " : "") + preset.DisplayName,
							() =>
							{
								PresetSelected = true;
								SelectedPreset = preset;
							}
						))
					);
					return new DexcRuntimeFormDetail.ChoicePage(
						choices.Skip(page.Offset).Take(page.Count).ToArray(),
						page.Offset + page.Count < choices.Count
					);
				},
				false,
				apply: ApplySelectedPreset,
				canApply: () => PresetSelected && Fields.Any(field => field.Writable)
			);
		}

		static void ApplySelectedPreset()
		{
			if (Current == null || !PresetSelected)
				return;
			if (SelectedPreset == null)
			{
				foreach (var field in Fields.Where(field => field.Writable).ToArray())
					Write(field, true);
			}
			else
				ApplyPreset(SelectedPreset);
		}

		internal static void ApplyPreset(DebugSettingPresetItem preset)
		{
			if (Current == null || preset.SetId != Current.Id)
				return;
			var result = DebugSetting.ApplyPreset(preset.Id);
			Refresh(true);
			if (result.Status != DebugOperationStatus.Succeeded)
				DexcRuntimeFormDetail.Open(Failure(result.Status, result.Issues));
		}

		static void MatchPreset()
		{
			MatchingPreset = "Undetermined";
			if (Fields.Any(field => field.Applied == null))
				return;
			if (Fields.Where(field => field.Writable).All(field => field.Applied.Equals(field.Descriptor.DefaultValue)))
			{
				MatchingPreset = "Initial values";
				return;
			}
			MatchingPreset = "Custom";
			foreach (var preset in Presets.OrderBy(value => value.Id.ToString(), StringComparer.Ordinal))
			{
				var preview = DebugSetting.PreviewPreset(preset.Id);
				if (preview.Status == DebugOperationStatus.Failed)
					MatchingPreset = "Undetermined";
				if (preview.Status == DebugOperationStatus.Succeeded && preview.Value.Count == 0)
				{
					MatchingPreset = preset.DisplayName;
					return;
				}
			}
		}

		/// <summary>The field of the selected setting; Fields owns the drafts.</summary>
		internal static Field Anchor => Fields.FirstOrDefault(field => field.Item.Id == DexcRuntimeUIState.SettingId);

		internal static string Key(DebugSettingId id, string component = null) =>
			"setting:" + id + (component == null ? string.Empty : ":" + component);

		/// <summary>
		/// The detail page as rows. A renderer that scrolls freely passes <paramref name="windowed"/> false and gets every
		/// field; one that pages its content gets a window of fields with Previous and Next rows around it.
		/// </summary>
		internal static List<DexcUIRow> DetailRows(bool windowed)
		{
			var rows = new List<DexcUIRow>();
			if (Current == null)
				return rows;
			rows.Add(
				new DexcUIRow
				{
					Kind = DexcUIRowKind.Label,
					Key = "setting:preset",
					Text = () => MatchingPreset + Pending(),
				}
			);
			if (windowed && WindowOffset > 0)
				rows.Add(
					new DexcUIRow
					{
						Kind = DexcUIRowKind.Button,
						Key = "setting:previous",
						Label = "Previous fields",
						Activated = PreviousFields,
					}
				);
			foreach (var field in windowed ? VisibleFields() : Fields)
			{
				if (field.Components.Count > 0)
				{
					var first = true;
					foreach (var component in field.Components.Keys.ToArray())
					{
						rows.Add(ValueRow(field, component, first));
						first = false;
					}
				}
				else
					rows.Add(ValueRow(field, null, true));
				if (field.IsSlider)
					rows.Add(SliderRow(field));
			}
			if (windowed && HasNextFields)
				rows.Add(
					new DexcUIRow
					{
						Kind = DexcUIRowKind.Button,
						Key = "setting:next",
						Label = "Next fields",
						Activated = NextFields,
					}
				);
			return rows;
		}

		/// <summary>What waits for Apply and what is wrong, after the preset the values match.</summary>
		static string Pending()
		{
			var pending = Fields.Count(field => field.Writable && field.Dirty && field.Item.ApplyMode == DebugSettingApplyMode.Submit);
			var errors = Fields.Count(field => !string.IsNullOrEmpty(field.Error));
			return (pending > 0 ? " · " + pending + " pending" : string.Empty) + (errors > 0 ? " · " + errors + " error" + (errors == 1 ? string.Empty : "s") : string.Empty);
		}

		static DexcUIRow ValueRow(Field field, string component, bool first)
		{
			// The binding drops edits that arrive after the page, the session or this row's controls have moved on.
			var binding = new DexcRuntimeUIRenderer.SettingInputBinding(field.Item, component);
			return new DexcUIRow
			{
				Kind = field.IsChoice ? DexcUIRowKind.Choice : DexcUIRowKind.Input,
				Key = Key(field.Item.Id, component),
				Label = first ? field.Label : string.Empty,
				Component = component,
				Text = () => Text(field, component),
				Error = () => field.Error,
				Enabled = () => field.Writable && field.Applied != null,
				Changed = binding.Change,
				Confirmed = binding.Confirm,
				Unbound = binding.Invalidate,
				Activated = () => Choose(field),
				Cycle = field.IsChoice && component == null ? direction => Cycle(field, direction) : (Func<int, bool>)null,
				Auxiliary = field.Type == typeof(string) ? () => OpenText(field) : (Action)null,
				Boolean = field.Type == typeof(bool),
				Note = first ? field.Note : null,
			};
		}

		static DexcUIRow SliderRow(Field field)
		{
			var version = DexcRuntimeUIState.SettingBindingVersion;
			return new DexcUIRow
			{
				Kind = DexcUIRowKind.Slider,
				Key = Key(field.Item.Id) + ":slider",
				SliderValue = () => SliderValue(field),
				Enabled = () => field.Writable && field.CanSlide,
				Slid = value =>
				{
					if (version == DexcRuntimeUIState.SettingBindingVersion)
						ChangeSlider(field, value);
				},
			};
		}

		/// <summary>
		/// The setting list as rows. A search lists the matching groups flat with their full labels; otherwise the list shows
		/// one folder of the groups' labels, with the favorites entry at the top, and paging.
		/// </summary>
		internal static List<DexcUIRow> ListRows()
		{
			// The gamepad starts on the search field, the first row, and moves down into the entries.
			var search = DexcUIRow.Search("search:setting", () => DexcRuntimeUIState.Search, text => DexcRuntimeUIState.Search = text);
			search.NavigationIndex = 0;
			var rows = new List<DexcUIRow> { search };
			var searching = !string.IsNullOrEmpty(DexcRuntimeUIState.Search);
			var groups = ReadGroups(DexcRuntimeUIState.Search);
			var entries =
				searching ? groups.Select(group => GroupEntry(group, group.Label, null)).ToList()
				: Data.InFavorites ? FavoriteEntries(groups)
				: FolderEntries(groups);
			var navigation = 1;
			foreach (var entry in entries.Skip(ListOffset).Take(20))
			{
				entry.NavigationIndex = navigation++;
				rows.Add(entry);
			}
			if (Data.InFavorites && entries.Count == 0)
				rows.Add(
					new DexcUIRow
					{
						Kind = DexcUIRowKind.Label,
						Key = "setting-list:empty",
						Label = "No favorites. Star a setting group on its page.",
					}
				);
			if (ListOffset > 0)
				rows.Add(
					DexcUIRows.Paging("setting-list:previous", "Previous", navigation++, () => ListOffset = Math.Max(0, ListOffset - 20))
				);
			if (ListOffset + 20 < entries.Count)
				rows.Add(DexcUIRows.Paging("setting-list:next", "Next", navigation, () => ListOffset += 20));
			return rows;
		}

		static DexcUIRow GroupEntry(Group group, string label, string detail)
		{
			var values = group.Items.Length + (group.Items.Length == 1 ? " value" : " values");
			return new DexcUIRow
			{
				Kind = DexcUIRowKind.Button,
				Key = "setting-list:" + group.Id,
				Label = label,
				Leading = DexcRuntimeFavorites.Contains(DexcFavoriteKind.Setting, group.Label) ? "★" : null,
				Detail = () => string.IsNullOrEmpty(detail) ? values : detail + " · " + values,
				ListItem = true,
				Activated = () => Open(group.Items[0]),
			};
		}

		/// <summary>The top shows the favorites entry first; then each folder with its count, then the groups here.</summary>
		static List<DexcUIRow> FolderEntries(IReadOnlyList<Group> groups)
		{
			var prefix = Data.Folder.Length == 0 ? string.Empty : Data.Folder + "/";
			var folders = new List<string>();
			var counts = new Dictionary<string, int>(StringComparer.Ordinal);
			var here = new List<Group>();
			foreach (var group in groups)
			{
				if (!group.Label.StartsWith(prefix, StringComparison.Ordinal))
					continue;
				var rest = group.Label.Substring(prefix.Length);
				var slash = rest.IndexOf('/');
				if (slash < 0)
				{
					here.Add(group);
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
			if (prefix.Length == 0)
			{
				var starred = DexcRuntimeFavorites.All(DexcFavoriteKind.Setting).Count;
				entries.Add(
					new DexcUIRow
					{
						Kind = DexcUIRowKind.Button,
						Key = "setting-list:favorites",
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
						Key = "setting-folder:" + path,
						Label = name,
						Trailing = () => count.ToString(CultureInfo.InvariantCulture),
						Drill = true,
						ListItem = true,
						Activated = () => OpenFolder(path, false),
					}
				);
			}
			foreach (var group in here)
				entries.Add(GroupEntry(group, group.Label.Substring(prefix.Length), null));
			return entries;
		}

		/// <summary>The starred groups that still exist, each with the folder it lives in.</summary>
		static List<DexcUIRow> FavoriteEntries(IReadOnlyList<Group> groups)
		{
			var entries = new List<DexcUIRow>();
			foreach (var label in DexcRuntimeFavorites.All(DexcFavoriteKind.Setting))
			{
				var group = groups.FirstOrDefault(candidate => candidate.Label == label);
				if (group == null)
					continue;
				var slash = label.LastIndexOf('/');
				entries.Add(GroupEntry(group, slash < 0 ? label : label.Substring(slash + 1), slash < 0 ? null : label.Substring(0, slash)));
			}
			return entries;
		}

		static void OpenFolder(string folder, bool favorites)
		{
			Data.Folder = folder;
			Data.InFavorites = favorites;
			ListOffset = 0;
			DexcRuntimeUIState.SelectedIndex = 0;
			DexcRuntimeUIState.ScrollPosition = Vector2.zero;
		}

		/// <summary>The list's place under "Setting": the open folder's parts, or the favorites.</summary>
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
