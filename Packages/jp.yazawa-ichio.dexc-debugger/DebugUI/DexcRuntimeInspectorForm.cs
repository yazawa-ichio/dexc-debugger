using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Dexc
{
	internal static class DexcRuntimeInspectorForm
	{
		internal sealed class Field
		{
			internal DebugInspectorFieldDescriptor Descriptor;
			internal Type Type;
			internal InspectorValueSnapshot Read;
			internal DebugValue Value;
			internal DebugInspectorWriteValue Reference;
			internal string Raw = string.Empty;
			internal string Error;
			internal bool Dirty;

			/// <summary>Just written: the next refresh shows the value read back, not the text that was typed.</summary>
			internal bool Written;
			internal readonly Dictionary<string, string> Components = new Dictionary<string, string>();
			internal bool Writable =>
				IsCurrent(this)
				&& Read != null
				&& (Read.State == DebugInspectorSnapshotState.Value || Read.State == DebugInspectorSnapshotState.UnityReference)
				&& !Descriptor.IsCollection
				&& !DexcInspectorSnapshotText.IsCollection(Read)
				&& DexcInspectorRuntime.Registry.CanEdit(TargetComponent, Descriptor.Id, ExplicitEditing);
			internal bool IsReference => Read?.State == DebugInspectorSnapshotState.UnityReference;
			internal bool IsChoice => IsReference || Type == typeof(bool) || Type != null && Type.IsEnum;
			internal string Label => Descriptor.DisplayName;
			internal int Rows => Math.Max(1, Components.Count);
		}

		/// <summary>State of this class. Owned by <see cref="DexcUIRoot"/>; nothing here survives a root reset.</summary>
		internal sealed class State
		{
			internal object Session;
			internal DebugInspectorComponentHandle TargetComponent;
			internal float NextRead;
			internal bool Writing;
			internal IReadOnlyList<Field> Fields = Array.Empty<Field>();
			internal string Title = "Fields";
			internal bool ExplicitEditing;
			internal int Version;
			internal int Offset;
		}

		internal static State Data => DexcUIRoot.Current.InspectorForm;
		static object Session
		{
			get => Data.Session;
			set => Data.Session = value;
		}
		static DebugInspectorComponentHandle TargetComponent
		{
			get => Data.TargetComponent;
			set => Data.TargetComponent = value;
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
		internal static IReadOnlyList<Field> Fields
		{
			get => Data.Fields;
			private set => Data.Fields = value;
		}
		internal static string Title
		{
			get => Data.Title;
			private set => Data.Title = value;
		}
		internal static bool ExplicitEditing
		{
			get => Data.ExplicitEditing;
			private set => Data.ExplicitEditing = value;
		}
		internal static int Version
		{
			get => Data.Version;
			private set => Data.Version = value;
		}
		internal static int Offset
		{
			get => Data.Offset;
			private set => Data.Offset = value;
		}
		internal static bool HasUnregistered => Fields.Any(field => !field.Descriptor.IsRegistered);
		internal static bool CanApply =>
			!Writing
			&& Fields.Any(field => field.Writable && field.Dirty)
			&& Fields.All(field => !field.Writable || !field.Dirty || field.IsReference || field.Value != null);

		internal static bool IsCurrent(Field field) =>
			DexcRuntimeUIState.IsOpen
			&& ReferenceEquals(Session, DexcDebugger.CurrentSession)
			&& TargetComponent == DexcRuntimeUIState.InspectorComponent
			&& Fields.Contains(field)
			&& (DexcRuntimeUIState.Page == DexcRuntimeUIPage.InspectorFields || DexcRuntimeUIState.Page == DexcRuntimeUIPage.FormDetail);

		internal static void Reset()
		{
			Fields = Array.Empty<Field>();
			TargetComponent = default(DebugInspectorComponentHandle);
			Session = null;
			ExplicitEditing = false;
			Offset = 0;
			Version++;
		}

		internal static void Open(DebugInspectorComponentHandle component, string title = "Fields")
		{
			Reset();
			TargetComponent = component;
			Session = DexcDebugger.CurrentSession;
			Title = title;
			DexcRuntimeUIState.InspectorComponent = component;
			DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.InspectorFields);
			var result = new List<Field>();
			var offset = 0;
			while (true)
			{
				var page = component.GetFields(DexcRuntimeUIState.CreatePageRequest(offset, 100));
				result.AddRange(
					page.Fields.Select(descriptor => new Field
					{
						Descriptor = descriptor,
						Type = DexcRuntimeUIRenderer.ResolveType(descriptor.TypeName),
					})
				);
				if (!page.Page.HasMore || page.Fields.Count == 0)
				{
					break;
				}

				offset += page.Fields.Count;
			}

			Fields = result.OrderByDescending(field => field.Descriptor.IsRegistered).ToArray();
			Refresh();
		}

		internal static void EnsureOpen()
		{
			if (
				DexcRuntimeUIState.Page == DexcRuntimeUIPage.InspectorFields
				&& (TargetComponent != DexcRuntimeUIState.InspectorComponent || !ReferenceEquals(Session, DexcDebugger.CurrentSession))
			)
			{
				Open(DexcRuntimeUIState.InspectorComponent);
			}
		}

		internal static void Refresh()
		{
			if (!TargetComponent.IsValid || !ReferenceEquals(Session, DexcDebugger.CurrentSession))
			{
				return;
			}

			var values = TargetComponent.ReadFields(Fields.Select(field => field.Descriptor.Id).ToArray());
			if (values.Status == DebugOperationStatus.Stale)
			{
				DexcRuntimeUIState.ReturnFromStaleInspector("The selected inspector component is Missing.");
				Reset();
				return;
			}

			foreach (var field in Fields)
			{
				field.Read = values.Value.FirstOrDefault(value => value.Field == field.Descriptor.Id)?.Snapshot;
				// While the value stays the same, the text being typed stays too: "0." on the way to "0.05" reads as 0.
				if (field.Dirty || !field.Written && field.Value != null && field.Value.Equals(field.Read?.Value))
				{
					continue;
				}

				field.Written = false;

				field.Value = field.Read?.Value;
				field.Raw = SnapshotText(field.Read);
				field.Error =
					field.Read == null
					|| field.Read.State == DebugInspectorSnapshotState.ReadFailure
					|| field.Read.State == DebugInspectorSnapshotState.Missing
					|| field.Read.State == DebugInspectorSnapshotState.Unsupported
						? (field.Read?.State.ToString() ?? "Missing") + " " + field.Read?.Error
						: null;
				field.Reference =
					field.Read?.State == DebugInspectorSnapshotState.UnityReference
						? field.Read.Reference.IsValid
							? DebugInspectorWriteValue.FromReference(field.Read.Reference)
							: DebugInspectorWriteValue.NullReference
						: null;
				field.Components.Clear();
				if (
					!field.Descriptor.IsCollection
					&& !DexcInspectorSnapshotText.IsCollection(field.Read)
					&& field.Value != null
					&& field.Value.TryGetObject(out var members)
				)
				{
					foreach (var member in members)
					{
						field.Components[member.Name] = DexcInputText.Format(member.Value);
					}
				}
			}

			Version++;
			NextRead = Time.realtimeSinceStartup + .25f;
		}

		internal static void Tick()
		{
			if (DexcRuntimeUIState.Page != DexcRuntimeUIPage.InspectorFields)
			{
				return;
			}

			EnsureOpen();
			if (Time.realtimeSinceStartup >= NextRead)
			{
				Refresh();
			}
		}

		internal static string SnapshotText(InspectorValueSnapshot value) => DexcInspectorSnapshotText.Format(value);

		internal static string Text(Field field, string component = null) => component == null ? field.Raw : field.Components[component];

		internal static string Key(DebugInspectorFieldId id, string component = null) =>
			"inspector:" + id + (component == null ? string.Empty : ":" + component);

		/// <summary>
		/// The field page as rows. A renderer that scrolls freely passes <paramref name="windowed"/> false and gets every
		/// field; one that pages its content gets a window of fields with Previous and Next rows around it.
		/// </summary>
		internal static List<DexcUIRow> FieldRows(bool windowed)
		{
			EnsureOpen();
			var rows = new List<DexcUIRow>();
			if (HasUnregistered && !ExplicitEditing)
			{
				// Until editing is turned on the values are read as text; the band says so and holds the switch.
				rows.Add(
					new DexcUIRow
					{
						Kind = DexcUIRowKind.Label,
						Key = "inspector:read-only",
						Label = "Unregistered fields are read only. Values refresh while the page is open.",
					}
				);
				rows.Add(
					new DexcUIRow
					{
						Kind = DexcUIRowKind.Button,
						Key = "inspector:enable-editing",
						Label = "Enable editing…",
						Activated = RequestEditing,
					}
				);
			}
			if (windowed && Offset > 0)
				rows.Add(
					new DexcUIRow
					{
						Kind = DexcUIRowKind.Button,
						Key = "inspector:previous",
						Label = "Previous fields",
						Activated = Previous,
					}
				);
			foreach (var field in windowed ? VisibleFields() : Fields)
			{
				if (field.Components.Count > 0)
				{
					var first = true;
					foreach (var component in field.Components.Keys.ToArray())
					{
						rows.Add(FieldRow(field, component, first));
						first = false;
					}
				}
				else
					rows.Add(FieldRow(field, null, true));
			}
			if (windowed && HasNext)
				rows.Add(
					new DexcUIRow
					{
						Kind = DexcUIRowKind.Button,
						Key = "inspector:next",
						Label = "Next fields",
						Activated = Next,
					}
				);
			return rows;
		}

		static DexcUIRow FieldRow(Field field, string component, bool first)
		{
			// Edits are kept as drafts until Apply, so there is nothing to confirm; late edits from a page left behind are dropped.
			var version = DexcRuntimeUIState.SettingBindingVersion;
			return new DexcUIRow
			{
				Kind = field.IsChoice ? DexcUIRowKind.Choice : DexcUIRowKind.Input,
				Key = Key(field.Descriptor.Id, component),
				Label = first ? field.Label : string.Empty,
				Component = component,
				Text = () => Text(field, component),
				Error = () => field.Error,
				Enabled = () => field.Writable,
				Changed = text =>
				{
					if (version == DexcRuntimeUIState.SettingBindingVersion)
						Change(field, text, component);
				},
				Activated = () => Choose(field),
				Cycle = field.IsChoice && !field.IsReference && component == null ? direction => Cycle(field, direction) : (Func<int, bool>)null,
				Auxiliary = () => OpenText(field),
				Boolean = field.Type == typeof(bool),
				Note = first && !field.Writable ? "Read only" : null,
			};
		}

		internal static IReadOnlyList<Field> VisibleFields()
		{
			var result = new List<Field>();
			var rows = 0;
			foreach (var field in Fields.Skip(Offset))
			{
				if (rows + field.Rows > 16 && result.Count > 0)
				{
					break;
				}

				result.Add(field);
				rows += field.Rows;
			}

			return result;
		}

		internal static bool HasNext => Offset + VisibleFields().Count < Fields.Count;

		internal static void Next()
		{
			Offset += VisibleFields().Count;
			Version++;
		}

		internal static void Previous()
		{
			var rows = 0;
			while (Offset > 0 && rows + Fields[Offset - 1].Rows <= 16)
			{
				rows += Fields[--Offset].Rows;
			}

			Version++;
		}

		internal static void Change(Field field, string text, string component = null)
		{
			if (!IsCurrent(field) || !field.Writable || field.IsReference || Writing)
			{
				return;
			}

			if (Text(field, component) == text)
			{
				return;
			}

			if (component != null)
			{
				field.Components[component] = text;
				text = string.Join(", ", field.Components.Select(value => value.Key + "=" + value.Value));
			}

			field.Raw = text;
			field.Value = DexcInputText.ParseLike(field.Read.Value, text, field.Type);
			field.Error = field.Value == null ? "Invalid input. Use a decimal point (.) and the field's value type." : null;
			field.Dirty = field.Value == null || !field.Value.Equals(field.Read.Value);
			Version++;
		}

		internal static void Apply()
		{
			if (DexcRuntimeUIState.Page != DexcRuntimeUIPage.InspectorFields || !CanApply)
			{
				return;
			}

			Writing = true;
			try
			{
				foreach (var field in Fields.Where(field => field.Writable && field.Dirty).ToArray())
				{
					if (!IsCurrent(field))
					{
						break;
					}

					var result = TargetComponent.WriteField(
						field.Descriptor.Id,
						field.IsReference ? field.Reference : DebugInspectorWriteValue.From(field.Value),
						ExplicitEditing ? DebugInspectorWriteAccess.AllowUnregistered : DebugInspectorWriteAccess.RegisteredOnly
					);
					field.Dirty = result.Status != DebugOperationStatus.Succeeded;
					field.Written = !field.Dirty;
					field.Error = field.Dirty
						? result.Status + "\n" + string.Join("\n", result.Issues.Select(issue => issue.Message))
						: null;
				}

				Refresh();
			}
			finally
			{
				Writing = false;
			}
		}

		internal static void RequestEditing()
		{
			var owner = TargetComponent;
			DexcRuntimeFormDetail.OpenSelection(
				"Enable editing of unregistered fields?",
				(search, page) =>
					new DexcRuntimeFormDetail.ChoicePage(
						new[]
						{
							new DexcRuntimeFormDetail.Choice(
								"Enable editing",
								() =>
								{
									if (owner == TargetComponent && ReferenceEquals(Session, DexcDebugger.CurrentSession))
									{
										ExplicitEditing = true;
										Version++;
									}
								}
							),
						},
						false,
						"Unregistered fields are read only until you explicitly enable editing."
					),
				false
			);
		}

		internal static void Choose(Field field)
		{
			if (!IsCurrent(field) || !field.Writable)
			{
				return;
			}

			if (field.IsReference)
			{
				DexcRuntimeInspectorNavigation.ChooseReference(field);
				return;
			}

			if (field.Type == typeof(bool))
			{
				Change(field, field.Raw == "true" ? "false" : "true");
				return;
			}

			DexcRuntimeFormDetail.OpenSelection(
				field.Descriptor.DisplayName,
				(search, page) =>
				{
					var names = Enum.GetNames(field.Type)
						.Where(name => name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
						.ToArray();
					return new DexcRuntimeFormDetail.ChoicePage(
						names
							.Skip(page.Offset)
							.Take(page.Count)
							.Select(name => new DexcRuntimeFormDetail.Choice(name, () => Change(field, name)))
							.ToArray(),
						page.Offset + page.Count < names.Length
					);
				}
			);
		}

		/// <summary>Steps a boolean or enum field in place. The value stays a draft until Apply, as when it is chosen.</summary>
		internal static bool Cycle(Field field, int direction)
		{
			if (!IsCurrent(field) || !field.Writable || field.IsReference || !field.IsChoice)
			{
				return false;
			}

			var name =
				field.Type == typeof(bool)
					? field.Raw == "true" ? "false" : "true"
					: DexcUIRow.Step(Enum.GetNames(field.Type), field.Raw, direction);
			if (name == null)
			{
				return false;
			}

			Change(field, name);
			return true;
		}

		internal static void SelectReference(Field field, DebugInspectorTargetHandle target, string label)
		{
			if (!IsCurrent(field) || !field.Writable)
			{
				return;
			}

			field.Reference = target.IsValid ? DebugInspectorWriteValue.FromReference(target) : DebugInspectorWriteValue.NullReference;
			field.Raw = label;
			field.Dirty = field.Read.Reference != target;
			field.Error = null;
			Version++;
		}

		internal static void OpenText(Field field)
		{
			if (!IsCurrent(field))
			{
				return;
			}

			if (field.Writable && field.Type == typeof(string))
			{
				DexcRuntimeFormDetail.OpenTextEditor(field.Descriptor.DisplayName, field.Raw, text => Change(field, text));
			}
			else
			{
				DexcRuntimeFormDetail.Open(
					field.Descriptor.DisplayName
						+ "\n"
						+ (
							field.Descriptor.IsCollection || DexcInspectorSnapshotText.IsCollection(field.Read)
								? DexcInspectorSnapshotText.Format(field.Read, true)
								: field.Raw
						)
				);
			}
		}

		internal static void OpenWatchSelection()
		{
			var owner = TargetComponent;
			var fields = Fields.ToArray();
			DexcRuntimeFormDetail.OpenSelection(
				"Watch field",
				(search, page) =>
				{
					var matching = fields
						.Where(field => field.Descriptor.DisplayName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
						.ToArray();
					return new DexcRuntimeFormDetail.ChoicePage(
						matching
							.Skip(page.Offset)
							.Take(page.Count)
							.Select(field => new DexcRuntimeFormDetail.Choice(
								field.Descriptor.DisplayName,
								() =>
								{
									if (!IsCurrent(field) || owner != TargetComponent)
									{
										return;
									}

									var offset = 0;
									while (true)
									{
										var watches = DebugInspector.GetWatches(DexcRuntimeUIState.CreatePageRequest(offset, 100));
										if (watches.Entries.Any(watch => watch.Component == owner && watch.Field == field.Descriptor.Id))
										{
											DexcRuntimeUIState.Notice = "Already watching " + field.Descriptor.DisplayName;
											return;
										}

										if (!watches.Page.HasMore || watches.Entries.Count == 0)
										{
											break;
										}

										offset += watches.Entries.Count;
									}

									var result = owner.AddWatch(field.Descriptor.Id, TimeSpan.FromSeconds(.25));
									DexcRuntimeUIState.Notice =
										result.Status == DebugOperationStatus.Succeeded
											? "Watch added: " + field.Descriptor.DisplayName
											: result.Status.ToString();
								}
							))
							.ToArray(),
						page.Offset + page.Count < matching.Length
					);
				}
			);
		}
	}
}
