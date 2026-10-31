using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Dexc
{
	public sealed partial class DexcDebuggerWindow
	{
		private sealed class EditorSettingField
		{
			internal DebugSettingDescriptor Descriptor;
			internal Type Type;
			internal DebugValue Applied,
				Value;
			internal DexcEditorNumericInput Numeric;
			internal string Error,
				Key;
			internal bool Dirty;
			internal bool Writable => Descriptor.Access == DebugSettingAccess.ReadWrite;
		}

		private readonly List<EditorSettingField> m_EditorSettingFields = new List<EditorSettingField>();
		private List<DebugSettingListItem> m_EditorSettings = new List<DebugSettingListItem>();
		private Vector2 m_SettingDetailScroll;
		private string m_PreviousSettingFocus;
		private bool m_RevealSelectedSetting;
		private double m_NextSettingRead;

		private sealed class EditorHelper
		{
			internal string Title,
				Text;
			internal Vector2 Scroll;
			internal List<KeyValuePair<string, Action>> Choices;
			internal object Session;
			internal Action<string> ApplyText;
			internal string ReturnFocus;
			internal bool RequiresPlayMode;
		}

		private EditorHelper m_Helper;
#if UNITY_INCLUDE_TESTS
		internal int SettingFieldCountForTests => m_EditorSettingFields.Count;

		internal DebugValue SettingFieldValueForTests(DebugSettingId id) =>
			m_EditorSettingFields.FirstOrDefault(field => field.Descriptor.Id == id)?.Value;

		internal DexcEditorNumericInput SettingFieldNumericForTests(DebugSettingId id) =>
			m_EditorSettingFields.FirstOrDefault(field => field.Descriptor.Id == id)?.Numeric;
#endif

		private void ReadEditorSettings()
		{
			m_EditorSettings.Clear();
			var offset = 0;
			while (true)
			{
				var page = Settings.GetList(
					new DebugListQuery(string.Empty, string.Empty, new DebugPageRequest(offset, BoundedPageSize(FeaturePageSize)))
				);
				m_EditorSettings.AddRange(page.Items);
				if (!page.Page.HasMore || page.Items.Count == 0)
				{
					break;
				}

				offset += page.Items.Count;
			}
		}

		private static string SettingGroupName(IEnumerable<DebugSettingListItem> items)
		{
			var paths = items.Select(item => item.Path.Split('/')).ToArray();
			var prefix = paths[0].Take(paths[0].Length - 1).ToArray();
			foreach (var path in paths.Skip(1))
			{
				prefix = prefix.TakeWhile((value, index) => index < path.Length - 1 && path[index] == value).ToArray();
			}

			return prefix.Length > 0 ? string.Join("/", prefix) : string.Join(" / ", items.Select(item => item.DisplayName));
		}

		private bool OpenSettingClass(DebugSettingDescriptor selected)
		{
			m_Helper = null;
			m_EditorSettingFields.Clear();
			m_SettingNumericInput.Clear();
			ReadEditorSettings();
			foreach (
				var item in m_EditorSettings.Where(item => item.SetId == selected.SetId).OrderBy(item => item.Path, StringComparer.Ordinal)
			)
			{
				if (!Settings.TryGetDescriptor(item.Id, out var descriptor))
				{
					continue;
				}

				var field = new EditorSettingField
				{
					Descriptor = descriptor,
					Type = ResolveType(descriptor.ValueType),
					Key = item.Id == selected.Id ? "setting" : "setting/" + item.Id,
					Numeric = item.Id == selected.Id ? m_SettingNumericInput : new DexcEditorNumericInput(),
				};
				field.Numeric.ErrorRequested = OpenEditorText;
				ReadEditorSetting(field, true);
				m_EditorSettingFields.Add(field);
			}

			m_SettingDetailScroll = Vector2.zero;
			m_RevealSelectedSetting = true;
			m_PreviousSettingFocus = null;
			SyncEditorSettingAnchor();
			return m_SelectedSettingValue != null;
		}

		private void ReadEditorSetting(EditorSettingField field, bool discard)
		{
			var read = Settings.Read(field.Descriptor.Id);
			read.TryGetValue(out field.Applied);
			if (discard || !field.Dirty)
			{
				var changed = !Equals(field.Value, field.Applied);
				field.Value = field.Applied;
				if (field.Applied == null)
				{
					field.Error = read.Status.ToString();
				}
				else if (discard || changed)
				{
					field.Error = null;
				}

				field.Dirty = false;
				if (discard || changed)
				{
					field.Numeric.Clear();
				}
			}
		}

		private void SyncEditorSettingAnchor()
		{
			var field = m_EditorSettingFields.FirstOrDefault(value => value.Descriptor.Id == m_SelectedSetting?.Id);
			if (field == null)
			{
				return;
			}

			m_SelectedSettingValue = field.Value;
		}

		private void DiscardSettingEdits()
		{
			if (DexcDebugger.IsInitialized)
			{
				foreach (var field in m_EditorSettingFields)
				{
					ReadEditorSetting(field, true);
				}
			}

			SyncEditorSettingAnchor();
			m_PreviousSettingFocus = null;
		}

		private void DrawSettingListPane()
		{
			if (m_EditorSettings.Count == 0)
			{
				ReadEditorSettings();
			}

			GUILayout.Label("Setting", m_HeadingStyle);
			m_SettingSearch = GUILayout.TextField(m_SettingSearch, m_InputStyle, GUILayout.Height(EditorRowHeight));
			m_SettingScroll = EditorGUILayout.BeginScrollView(m_SettingScroll);
			foreach (var group in m_EditorSettings.GroupBy(item => item.SetId))
			{
				if (
					!string.IsNullOrEmpty(m_SettingSearch)
					&& !group.Any(item => item.Path.IndexOf(m_SettingSearch, StringComparison.OrdinalIgnoreCase) >= 0)
				)
				{
					continue;
				}

				if (SelectionButton(SettingGroupName(group), m_SelectedSetting != null && m_SelectedSetting.SetId == group.Key))
				{
					if (m_SelectedSetting == null || m_SelectedSetting.SetId != group.Key)
					{
						SelectSetting(group.First().Id);
					}

					m_Page = Page.SettingDetail;
					m_WorkspacePart = WorkspacePart.Detail;
				}
			}

			EditorGUILayout.EndScrollView();
		}

		private void DrawSettingDetail()
		{
			if (m_SelectedSetting == null)
			{
				GUILayout.Label("Select a Setting class.", m_ParagraphStyle);
				return;
			}

			if (EditorApplication.timeSinceStartup >= m_NextSettingRead)
			{
				foreach (var field in m_EditorSettingFields)
				{
					ReadEditorSetting(field, false);
				}

				m_NextSettingRead = EditorApplication.timeSinceStartup + 0.25;
			}

			GUILayout.Label(SettingGroupName(m_EditorSettings.Where(item => item.SetId == m_SelectedSetting.SetId)), m_HeadingStyle);
			var enter =
				Event.current.type == EventType.KeyDown
				&& (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter);
			m_SettingDetailScroll = EditorGUILayout.BeginScrollView(m_SettingDetailScroll);
			foreach (var field in m_EditorSettingFields)
			{
				GUILayout.Space(0);
				if (m_RevealSelectedSetting && field.Descriptor.Id == m_SelectedSetting.Id && Event.current.type == EventType.Repaint)
				{
					m_SettingDetailScroll.y = GUILayoutUtility.GetLastRect().y;
					m_RevealSelectedSetting = false;
					Repaint();
				}

				using (new EditorGUI.DisabledScope(!field.Writable))
				{
					field.Numeric.OperationError = field.Error;
					var label =
						field.Descriptor.Path.Split('/').Last()
						+ (
							!field.Writable ? " · Read only"
							: field.Descriptor.ApplyMode == DebugSettingApplyMode.Immediate ? " · Immediate"
							: ""
						);
					if (
						DrawEditorValue(
							field.Key,
							label,
							field.Type,
							field.Value,
							field.Numeric,
							out var next,
							selected =>
							{
								if (!m_EditorSettingFields.Contains(field) || !field.Writable)
								{
									return;
								}

								field.Value = selected;
								field.Dirty = !Equals(selected, field.Applied);
								field.Error = null;
								ConfirmEditorSetting(field);
							}
						)
					)
					{
						field.Value = next;
						field.Dirty = !Equals(next, field.Applied);
						field.Error = null;
						if (field.Type == typeof(bool) || field.Type != null && field.Type.IsEnum)
						{
							ConfirmEditorSetting(field);
						}
					}

					if (!field.Numeric.IsValid)
					{
						field.Dirty = true;
					}

					if (field.Descriptor.IsSlider && field.Descriptor.HasRange)
					{
						DrawEditorSlider(field);
					}
				}
			}

			EditorGUILayout.EndScrollView();
			var focus = GUI.GetNameOfFocusedControl();
			if (enter || !string.Equals(focus, m_PreviousSettingFocus, StringComparison.Ordinal))
			{
				var commit = enter ? focus : m_PreviousSettingFocus;
				var field = m_EditorSettingFields
					.OrderByDescending(value => value.Key.Length)
					.FirstOrDefault(value =>
						commit == "DexcEditorNumeric:" + value.Key
						|| commit != null && commit.StartsWith("DexcEditorNumeric:" + value.Key + "/", StringComparison.Ordinal)
					);
				if (field != null)
				{
					ConfirmEditorSetting(field);
				}
			}

			m_PreviousSettingFocus = focus;
			using (new EditorGUILayout.HorizontalScope())
			{
				if (
					GUILayout.Button(
						"Preset · " + MatchingEditorPreset(),
						m_InputStyle,
						GUILayout.Height(EditorRowHeight),
						GUILayout.MinWidth(0)
					)
				)
				{
					OpenEditorPresets();
				}

				using (
					new EditorGUI.DisabledScope(
						!m_EditorSettingFields.All(field => !field.Writable || field.Numeric.IsValid)
							|| !m_EditorSettingFields.Any(field =>
								field.Writable && field.Dirty && field.Descriptor.ApplyMode == DebugSettingApplyMode.Submit
							)
					)
				)
				{
					if (ActionButton("Apply", GUILayout.Width(90)))
					{
						foreach (
							var field in m_EditorSettingFields.Where(field =>
								field.Writable && field.Dirty && field.Descriptor.ApplyMode == DebugSettingApplyMode.Submit
							)
						)
						{
							WriteEditorSetting(field);
						}
					}
				}

				if (Event.current.type == EventType.Repaint)
				{
#if UNITY_INCLUDE_TESTS
					SettingApplyRectForTests = WindowRectForTests(GUILayoutUtility.GetLastRect());
#endif
				}
			}

			SyncEditorSettingAnchor();
		}

		private void ConfirmEditorSetting(EditorSettingField field)
		{
			if (field.Descriptor.ApplyMode == DebugSettingApplyMode.Immediate)
			{
				WriteEditorSetting(field);
			}
		}

		private void WriteEditorSetting(EditorSettingField field)
		{
			if (!field.Writable || !field.Dirty || field.Value == null || !field.Numeric.IsValid)
			{
				return;
			}

			m_LastSettingWrite = Settings.Write(field.Descriptor.Id, field.Value);
			if (m_LastSettingWrite.Status == DebugOperationStatus.Succeeded)
			{
				field.Applied = field.Value;
				field.Dirty = false;
				field.Error = null;
				ReadHistory();
			}
			else
			{
				field.Error = "Setting write: " + m_LastSettingWrite.Status;
			}

			SyncEditorSettingAnchor();
		}

		private bool DrawEditorValue(
			string key,
			string label,
			Type type,
			DebugValue value,
			DexcEditorNumericInput numeric,
			out DebugValue edited,
			Action<DebugValue> selected = null
		)
		{
			edited = value;
			numeric.RowHeight = EditorRowHeight;
			if (
				value != null
				&& (
					value.Kind == DebugValueKind.SignedInteger
					|| value.Kind == DebugValueKind.UnsignedInteger
					|| value.Kind == DebugValueKind.FloatingPoint
				)
			)
			{
				return numeric.Draw(key, label, value, value.Kind, type, out edited);
			}

			GUILayout.Label(label, m_ParagraphStyle, GUILayout.MinWidth(0));
			GUILayout.Space(5);
			if (value != null && value.TryGetObject(out var members))
			{
				var values = new List<DebugValueMember>();
				var changed = false;
				foreach (var member in members)
				{
					var memberType = type == typeof(Vector2Int) || type == typeof(Vector3Int) ? typeof(int) : typeof(float);
					var updated = numeric.Draw(
						key + "/" + member.Name,
						member.Name,
						member.Value,
						member.Value.Kind,
						memberType,
						out var next
					);
					values.Add(new DebugValueMember(member.Name, updated ? next : member.Value));
					changed |= updated;
				}

				edited = DebugValue.FromObject(values);
				return changed;
			}

			var modified = false;
			using (new EditorGUILayout.HorizontalScope())
			{
				GUI.SetNextControlName("DexcEditorNumeric:" + key);
				EditorGUI.BeginChangeCheck();
				if (type == typeof(bool) && value != null && value.TryGetBoolean(out var boolean))
				{
					edited = DebugValue.From(
						GUILayout.Toggle(
							boolean,
							boolean ? "✓ On" : "□ Off",
							m_SelectionStyle,
							GUILayout.Height(EditorRowHeight),
							GUILayout.MinWidth(0)
						)
					);
				}
				else if (type == typeof(string) && value != null && value.TryGetString(out var text))
				{
					edited = DebugValue.From(
						GUILayout.TextField(text, m_InputStyle, GUILayout.Height(EditorRowHeight), GUILayout.MinWidth(40))
					);
				}
				else if (type != null && type.IsEnum)
				{
					var names = Enum.GetNames(type);
					if (
						GUILayout.Button(
							DexcInputText.Format(value),
							m_InputStyle,
							GUILayout.Height(EditorRowHeight),
							GUILayout.MinWidth(0)
						)
					)
					{
						m_Helper = new EditorHelper
						{
							Title = label,
							Session = DexcDebugger.CurrentSession,
							ReturnFocus = "DexcEditorNumeric:" + key,
							Choices = names
								.Select(name => new KeyValuePair<string, Action>(
									name,
									() =>
									{
										selected?.Invoke(DebugValue.From(name));
										m_RestoreEditorFocus = "DexcEditorNumeric:" + key;
										m_Helper = null;
									}
								))
								.ToList(),
						};
					}
				}
				else
				{
					GUILayout.Label(
						value == null ? "Unavailable" : FormatValue(value),
						m_InputStyle,
						GUILayout.Height(EditorRowHeight),
						GUILayout.MinWidth(0)
					);
				}

				if (Event.current.type == EventType.Repaint)
				{
#if UNITY_INCLUDE_TESTS
					numeric.ControlDrawnForTests?.Invoke(key, GUILayoutUtility.GetLastRect());
#endif
				}

				modified = EditorGUI.EndChangeCheck() && !Equals(edited, value);
				var wasEnabled = GUI.enabled;
				GUI.enabled = value != null;
				if (ActionButton(string.IsNullOrEmpty(numeric.OperationError) ? "…" : "!", GUILayout.Width(28)))
				{
					if (wasEnabled && selected != null && type == typeof(string) && string.IsNullOrEmpty(numeric.OperationError))
					{
						m_Helper = new EditorHelper
						{
							Title = label,
							Session = DexcDebugger.CurrentSession,
							Text = DexcInputText.Format(value),
							ApplyText = text => selected(DebugValue.From(text)),
							ReturnFocus = "DexcEditorNumeric:" + key,
						};
					}
					else
					{
						OpenEditorText(numeric.OperationError ?? FormatDetails(value));
					}
				}

				GUI.enabled = wasEnabled;
			}

			GUILayout.Space(4);
			return modified;
		}

		private void DrawEditorSlider(EditorSettingField field)
		{
			var minimum = field.Descriptor.RangeMinimum;
			var maximum = field.Descriptor.RangeMaximum;
			if (field.Type == typeof(int))
			{
				minimum = Math.Max(int.MinValue, minimum);
				maximum = Math.Min(int.MaxValue, maximum);
			}
			else if (field.Type == typeof(float))
			{
				minimum = Math.Max(-float.MaxValue, minimum);
				maximum = Math.Min(float.MaxValue, maximum);
			}

			if (!(maximum > minimum) || double.IsInfinity(minimum) || double.IsInfinity(maximum))
			{
				return;
			}

			var value = field.Value;
			var number =
				value != null && value.TryGetSignedInteger(out var integer) ? integer
				: value != null && value.TryGetUnsignedInteger(out var unsigned) ? unsigned
				: value != null && value.TryGetFloatingPoint(out var floating) ? floating
				: 0;
			EditorGUI.BeginChangeCheck();
			var ratio = GUILayout.HorizontalSlider(
				Mathf.Clamp01((float)((number - minimum) / (maximum - minimum))),
				0,
				1,
				GUILayout.Height(EditorRowHeight)
			);
			if (Event.current.type == EventType.Repaint)
			{
#if UNITY_INCLUDE_TESTS
				field.Numeric.ControlDrawnForTests?.Invoke(field.Key + "/slider", GUILayoutUtility.GetLastRect());
#endif
			}

			if (EditorGUI.EndChangeCheck())
			{
				var numberValue = minimum * (1d - ratio) + maximum * ratio;
				// Every integer type, not only int: each is written as its own kind, so a short or a uint is not sent as a
				// floating point number its setting rejects.
				var integerType = DexcEditorNumericInput.IsInteger(field.Type);
				var kind =
					!integerType ? DebugValueKind.FloatingPoint
					: field.Type == typeof(byte) || field.Type == typeof(ushort) || field.Type == typeof(uint) || field.Type == typeof(ulong)
						? DebugValueKind.UnsignedInteger
					: DebugValueKind.SignedInteger;
				var parsed = DexcEditorNumericInput.Parse(
					field.Type,
					kind,
					(integerType ? Math.Round(numberValue) : numberValue).ToString(integerType ? "0" : "R", CultureInfo.InvariantCulture)
				);
				if (parsed == null)
				{
					return;
				}

				field.Value = parsed;
				field.Numeric.SetValue(field.Key, field.Value);
				field.Dirty = !Equals(field.Value, field.Applied);
				ConfirmEditorSetting(field);
			}
		}

		private void DrawEditorSelectionValue(string label, string value, Action open, string error)
		{
			GUILayout.Label(label, m_ParagraphStyle, GUILayout.MinWidth(0));
			GUILayout.Space(5);
			using (new EditorGUILayout.HorizontalScope())
			{
				if (GUILayout.Button(value, m_InputStyle, GUILayout.Height(EditorRowHeight), GUILayout.MinWidth(0)))
				{
					open();
				}

				var enabled = GUI.enabled;
				GUI.enabled = !string.IsNullOrEmpty(error);
				if (ActionButton(string.IsNullOrEmpty(error) ? "" : "!", GUILayout.Width(28)))
				{
					OpenEditorText(error);
				}

				GUI.enabled = enabled;
			}

			GUILayout.Space(4);
		}

		private List<DebugSettingPresetItem> ReadEditorPresets()
		{
			var items = new List<DebugSettingPresetItem>();
			if (m_SelectedSetting == null)
			{
				return items;
			}

			var offset = 0;
			while (true)
			{
				var page = Settings.GetPresets(m_SelectedSetting.SetId, new DebugPageRequest(offset, BoundedPageSize(FeaturePageSize)));
				items.AddRange(page.Items);
				if (!page.Page.HasMore || page.Items.Count == 0)
				{
					break;
				}

				offset += page.Items.Count;
			}

			return items;
		}

		private string MatchingEditorPreset()
		{
			var current = m_EditorSettingFields.ToDictionary(
				field => field.Descriptor.Id,
				field =>
				{
					Settings.Read(field.Descriptor.Id).TryGetValue(out var value);
					return value;
				}
			);
			if (current.Values.Any(value => value == null))
			{
				return "Undetermined";
			}

			if (m_EditorSettingFields.All(field => Equals(current[field.Descriptor.Id], field.Descriptor.DefaultValue)))
			{
				return "Initial values";
			}

			foreach (var preset in ReadEditorPresets())
			{
				var preview = Settings.PreviewPreset(preset.Id);
				if (
					preview.Status == DebugOperationStatus.Succeeded
					&& preview.Value.All(difference => Equals(difference.CurrentValue, difference.PresetValue))
				)
				{
					return preset.DisplayName;
				}
			}

			return "Custom";
		}

		private void OpenEditorPresets()
		{
			DiscardSettingEdits();
			var choices = new List<KeyValuePair<string, Action>>();
			choices.Add(new KeyValuePair<string, Action>("Initial values", () => ConfirmEditorPreset(null)));
			foreach (var preset in ReadEditorPresets())
			{
				choices.Add(new KeyValuePair<string, Action>(preset.DisplayName, () => ConfirmEditorPreset(preset)));
			}

			m_Helper = new EditorHelper
			{
				Title = "Presets",
				Choices = choices,
				Session = DexcDebugger.CurrentSession,
			};
		}

		private void ConfirmEditorPreset(DebugSettingPresetItem preset)
		{
			m_Helper = new EditorHelper
			{
				Title = preset?.DisplayName ?? "Initial values",
				RequiresPlayMode = true,
				Session = DexcDebugger.CurrentSession,
				Choices = new List<KeyValuePair<string, Action>>
				{
					new KeyValuePair<string, Action>(
						"Apply",
						() =>
						{
							if (!EditorApplication.isPlaying)
							{
								return;
							}

							if (preset == null)
							{
								var failures = new List<string>();
								foreach (var field in m_EditorSettingFields.Where(field => field.Writable))
								{
									var result = Settings.Reset(field.Descriptor.Id);
									if (result.Status != DebugOperationStatus.Succeeded)
									{
										failures.Add(field.Descriptor.Path + " · " + result.Status);
									}
								}

								m_SettingNotice = string.Join("\n", failures);
							}
							else
							{
								m_LastPresetResult = Settings.ApplyPreset(preset.Id);
								m_SettingNotice =
									m_LastPresetResult.Status == DebugOperationStatus.Succeeded
										? ""
										: "Preset apply: " + m_LastPresetResult.Status;
							}

							DiscardSettingEdits();
							m_Helper = null;
							if (!string.IsNullOrEmpty(m_SettingNotice))
							{
								OpenEditorText(m_SettingNotice);
							}

							ReadHistory();
						}
					),
				},
			};
		}

		private void OpenEditorText(string text) =>
			m_Helper = new EditorHelper
			{
				Title = "Details",
				Text = text,
				Session = DexcDebugger.CurrentSession,
			};

		private void DrawEditorHelper()
		{
			var helper = m_Helper;
			if (!ReferenceEquals(helper.Session, DexcDebugger.CurrentSession))
			{
				m_Helper = null;
				return;
			}

			GUILayout.Label(helper.Title, m_HeadingStyle);
			helper.Scroll = EditorGUILayout.BeginScrollView(helper.Scroll);
			if (helper.Choices != null)
			{
				foreach (var choice in helper.Choices)
				{
					using (new EditorGUI.DisabledScope(helper.RequiresPlayMode && !EditorApplication.isPlaying))
					{
						if (
							SelectionButton(choice.Key)
							&& ReferenceEquals(m_Helper, helper)
							&& ReferenceEquals(helper.Session, DexcDebugger.CurrentSession)
						)
						{
							choice.Value();
						}
					}
				}
			}

			if (helper.Text != null)
			{
				if (helper.ApplyText == null)
				{
					GUILayout.Label(helper.Text, m_ParagraphStyle, GUILayout.MinWidth(0));
				}
				else
				{
					helper.Text = EditorGUILayout.TextArea(
						helper.Text,
						new GUIStyle(m_InputStyle) { alignment = TextAnchor.UpperLeft, wordWrap = true },
						GUILayout.MinHeight(120),
						GUILayout.MinWidth(0)
					);
				}
			}

			EditorGUILayout.EndScrollView();
			if (helper.ApplyText != null && ActionButton("Use input"))
			{
				helper.ApplyText(helper.Text);
				m_Helper = null;
				m_RestoreEditorFocus = helper.ReturnFocus;
			}

			if (ActionButton("Back"))
			{
				m_Helper = null;
				m_RestoreEditorFocus = helper.ReturnFocus;
			}
		}
	}
}
