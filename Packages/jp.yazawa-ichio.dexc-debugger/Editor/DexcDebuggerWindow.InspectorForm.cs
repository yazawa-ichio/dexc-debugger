using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Dexc
{
	public sealed partial class DexcDebuggerWindow
	{
		private sealed class EditorInspectorField
		{
			internal DebugInspectorFieldDescriptor Descriptor;
			internal Type Type;
			internal InspectorValueSnapshot Read;
			internal DebugValue Value;
			internal DebugInspectorWriteValue Reference;
			internal string ReferenceLabel,
				Error;
			internal bool Dirty;
			internal readonly DexcEditorNumericInput Numeric = new DexcEditorNumericInput();
		}

		private readonly List<EditorInspectorField> m_EditorInspectorFields = new List<EditorInspectorField>();
		private DebugInspectorComponentHandle m_EditorInspectorOwner;
		private bool m_ExplicitInspectorEditing;
		private double m_NextInspectorRead;

#if UNITY_INCLUDE_TESTS
		internal void SelectInspectorComponentForTests(DebugInspectorComponentHandle component)
		{
			m_SelectedComponent = component;
			OpenEditorInspector();
		}

		internal int InspectorFieldCountForTests => m_EditorInspectorFields.Count;

		internal DexcEditorNumericInput InspectorNumericForTests(DebugInspectorFieldId id) =>
			m_EditorInspectorFields.First(field => field.Descriptor.Id == id).Numeric;

		internal void AddWatchForTests(DebugInspectorFieldId field)
		{
			AddEditorWatch(field);
		}
#endif

		private void OpenEditorInspector()
		{
			m_EditorInspectorFields.Clear();
			m_ExplicitInspectorEditing = false;
			m_Helper = null;
			m_EditorInspectorOwner = m_SelectedComponent;
			if (!m_SelectedComponent.IsValid)
			{
				return;
			}

			var offset = 0;
			while (true)
			{
				var page = m_SelectedComponent.GetFields(new DebugPageRequest(offset, BoundedPageSize(MaxFieldRows)));
				foreach (var descriptor in page.Fields)
				{
					var field = new EditorInspectorField { Descriptor = descriptor, Type = ResolveType(descriptor.TypeName) };
					field.Numeric.ErrorRequested = OpenEditorText;
					m_EditorInspectorFields.Add(field);
				}

				if (!page.Page.HasMore || page.Fields.Count == 0)
				{
					break;
				}

				offset += page.Fields.Count;
			}

			RefreshEditorInspector();
		}

		private void RefreshEditorInspector()
		{
			if (!m_EditorInspectorOwner.IsValid || m_EditorInspectorOwner != m_SelectedComponent)
			{
				return;
			}

			foreach (var field in m_EditorInspectorFields)
			{
				var result = m_SelectedComponent.ReadFields(new[] { field.Descriptor.Id });
				field.Read = result.Value.FirstOrDefault()?.Snapshot;
				if (field.Read != null)
				{
					m_FieldValues[field.Descriptor.Id] = field.Read;
				}

				if (!field.Dirty)
				{
					var next = field.Read?.Value;
					if (!Equals(field.Value, next))
					{
						field.Numeric.Clear();
					}

					field.Value = next;
					field.ReferenceLabel = field.Read?.ReferenceLabel;
					field.Reference =
						field.Read != null && field.Read.Reference.IsValid
							? DebugInspectorWriteValue.FromReference(field.Read.Reference)
							: DebugInspectorWriteValue.NullReference;
				}
			}

			m_NextInspectorRead = EditorApplication.timeSinceStartup + 0.25;
		}

		private bool CanEditInspector(EditorInspectorField field) =>
			EditorApplication.isPlaying
			&& !field.Descriptor.IsCollection
			&& field.Read != null
			&& (field.Read.State == DebugInspectorSnapshotState.Value || field.Read.State == DebugInspectorSnapshotState.UnityReference)
			&& DexcInspectorRuntime.Registry.CanEdit(m_SelectedComponent, field.Descriptor.Id, m_ExplicitInspectorEditing);

		private void DrawEditorInspectorFields()
		{
			if (m_EditorInspectorOwner != m_SelectedComponent)
			{
				OpenEditorInspector();
			}

			if (EditorApplication.timeSinceStartup >= m_NextInspectorRead)
			{
				RefreshEditorInspector();
			}

			foreach (var field in m_EditorInspectorFields)
			{
				var writable = CanEditInspector(field);
				field.Numeric.OperationError = field.Error;
				var label = field.Descriptor.DisplayName + (writable ? "" : " · Read only");
				if (
					field.Read == null
					|| field.Read.State != DebugInspectorSnapshotState.Value
						&& field.Read.State != DebugInspectorSnapshotState.UnityReference
				)
				{
					GUILayout.Label(label, m_ParagraphStyle);
					GUILayout.Label(
						field.Read == null ? "Missing" : field.Read.State.ToString(),
						m_InputStyle,
						GUILayout.Height(EditorRowHeight),
						GUILayout.MinWidth(0)
					);
					continue;
				}

				if (field.Descriptor.IsCollection || DexcInspectorSnapshotText.IsCollection(field.Read))
				{
					GUILayout.Label(label, m_ParagraphStyle);
					using (new EditorGUILayout.HorizontalScope())
					{
						GUILayout.Label(
							DexcInspectorSnapshotText.Format(field.Read),
							m_InputStyle,
							GUILayout.Height(EditorRowHeight),
							GUILayout.MinWidth(0)
						);
						if (ActionButton("…", GUILayout.Width(28)))
						{
							OpenEditorText(field.Descriptor.DisplayName + "\n" + DexcInspectorSnapshotText.Format(field.Read, true));
						}
					}
					GUILayout.Space(4);
				}
				else if (field.Type != null && typeof(UnityEngine.Object).IsAssignableFrom(field.Type))
				{
					GUILayout.Label(label, m_ParagraphStyle);
					GUILayout.Space(5);
					using (new EditorGUILayout.HorizontalScope())
					{
						using (new EditorGUI.DisabledScope(!writable))
						{
							if (
								GUILayout.Button(
									field.ReferenceLabel ?? "None",
									m_InputStyle,
									GUILayout.Height(EditorRowHeight),
									GUILayout.MinWidth(0)
								)
							)
							{
								OpenInspectorReference(field);
							}
						}

						using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(field.Error)))
						{
							if (ActionButton(string.IsNullOrEmpty(field.Error) ? "" : "!", GUILayout.Width(28)))
							{
								OpenEditorText(field.Error);
							}
						}
					}

					GUILayout.Space(4);
				}
				else
				{
					using (new EditorGUI.DisabledScope(!writable))
					{
						if (
							DrawEditorValue(
								"inspector/" + field.Descriptor.Id,
								label,
								field.Type,
								field.Value,
								field.Numeric,
								out var next,
								selected =>
								{
									if (!m_EditorInspectorFields.Contains(field) || !CanEditInspector(field))
									{
										return;
									}

									field.Value = selected;
									field.Dirty = !Equals(selected, field.Read?.Value);
									field.Error = null;
								}
							)
						)
						{
							field.Value = next;
							field.Dirty = !Equals(next, field.Read?.Value);
							field.Error = null;
						}
					}

					if (!field.Numeric.IsValid)
					{
						field.Dirty = true;
					}
				}
			}

			using (new EditorGUILayout.HorizontalScope())
			{
				using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying))
				{
					if (ActionButton("Watch"))
					{
						m_Helper = new EditorHelper
						{
							Title = "Add Watch",
							Session = DexcDebugger.CurrentSession,
							Choices = m_EditorInspectorFields
								.Select(field => new KeyValuePair<string, Action>(
									field.Descriptor.DisplayName,
									() =>
									{
										AddEditorWatch(field.Descriptor.Id);
										m_Helper = null;
									}
								))
								.ToList(),
						};
					}

					using (
						new EditorGUI.DisabledScope(
							!m_EditorInspectorFields.Any(field => field.Dirty && CanEditInspector(field))
								|| m_EditorInspectorFields.Any(field => !field.Numeric.IsValid)
						)
					)
					{
						if (ActionButton("Apply"))
						{
							ApplyEditorInspector();
						}
					}
				}
			}

			if (!m_ExplicitInspectorEditing && m_EditorInspectorFields.Any(field => !field.Descriptor.IsRegistered))
			{
				using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying))
				{
					if (ActionButton("Enable editing"))
					{
						m_Helper = new EditorHelper
						{
							Title = "Enable editing of unregistered fields?",
							Session = DexcDebugger.CurrentSession,
							Choices = new List<KeyValuePair<string, Action>>
							{
								new KeyValuePair<string, Action>(
									"Confirm",
									() =>
									{
										m_ExplicitInspectorEditing = true;
										m_Helper = null;
									}
								),
							},
						};
					}
				}
			}
		}

		private void ApplyEditorInspector()
		{
			foreach (var field in m_EditorInspectorFields.Where(field => field.Dirty && CanEditInspector(field) && field.Numeric.IsValid))
			{
				var value =
					field.Type != null && typeof(UnityEngine.Object).IsAssignableFrom(field.Type) ? field.Reference
					: field.Value != null ? DebugInspectorWriteValue.From(field.Value)
					: null;
				if (value == null)
				{
					continue;
				}

				var result = m_SelectedComponent.WriteField(
					field.Descriptor.Id,
					value,
					m_ExplicitInspectorEditing ? DebugInspectorWriteAccess.AllowUnregistered : DebugInspectorWriteAccess.RegisteredOnly
				);
				field.Error = result.Status == DebugOperationStatus.Succeeded ? null : result.Status.ToString();
				if (result.Status == DebugOperationStatus.Succeeded)
				{
					field.Dirty = false;
				}
			}

			RefreshEditorInspector();
		}

		private void AddEditorWatch(DebugInspectorFieldId field)
		{
			if (!EditorApplication.isPlaying || !m_SelectedComponent.IsValid)
			{
				return;
			}

			var offset = 0;
			while (true)
			{
				var page = DebugInspector.GetWatches(new DebugPageRequest(offset, BoundedPageSize(16)));
				if (page.Entries.Any(entry => entry.Component == m_SelectedComponent && entry.Field == field))
				{
					return;
				}

				if (!page.Page.HasMore || page.Entries.Count == 0)
				{
					break;
				}

				offset += page.Entries.Count;
			}

			m_SelectedComponent.AddWatch(field, TimeSpan.FromSeconds(0.25));
			m_BottomPanel = BottomPanel.Watch;
		}

		private void OpenInspectorReference(EditorInspectorField field)
		{
			var owner = m_SelectedComponent;
			var choices = new List<KeyValuePair<string, Action>>
			{
				new KeyValuePair<string, Action>(
					"None",
					() =>
					{
						if (owner != m_SelectedComponent || !m_EditorInspectorFields.Contains(field) || !CanEditInspector(field))
						{
							return;
						}

						field.Reference = DebugInspectorWriteValue.NullReference;
						field.ReferenceLabel = "None";
						field.Dirty = true;
						m_Helper = null;
					}
				),
			};
			choices.Add(
				new KeyValuePair<string, Action>(
					"Scene",
					() =>
					{
						m_Helper = new EditorHelper
						{
							Title = "Scene",
							Session = DexcDebugger.CurrentSession,
							Choices = DebugInspector
								.GetScenes()
								.Scenes.Select(scene => new KeyValuePair<string, Action>(
									scene.Name,
									() => OpenInspectorReferenceHierarchy(field, owner, scene.Scene, default, 0, null)
								))
								.ToList(),
						};
					}
				)
			);
			var offset = 0;
			while (true)
			{
				var page = DebugInspector.GetRegistered(new DebugListQuery("", "", new DebugPageRequest(offset, BoundedPageSize(16))));
				foreach (var target in page.Entries)
				{
					foreach (
						var candidate in DexcInspectorRuntime.Registry.GetAssignableReferences(target.Target, field.Descriptor.TypeName)
					)
					{
						choices.Add(
							new KeyValuePair<string, Action>(
								target.Label + " / " + candidate.Value,
								() =>
								{
									if (
										owner != m_SelectedComponent
										|| !m_EditorInspectorFields.Contains(field)
										|| !CanEditInspector(field)
									)
									{
										return;
									}

									field.Reference = DebugInspectorWriteValue.FromReference(candidate.Key);
									field.ReferenceLabel = target.Label + " / " + candidate.Value;
									field.Dirty = true;
									m_Helper = null;
								}
							)
						);
					}
				}

				if (!page.Page.HasMore || page.Entries.Count == 0)
				{
					break;
				}

				offset += page.Entries.Count;
			}

			m_Helper = new EditorHelper
			{
				Title = field.Descriptor.DisplayName,
				Session = DexcDebugger.CurrentSession,
				Choices = choices,
			};
		}

		private void OpenCommandReference(DebugCommandFieldDescriptor field)
		{
			var draft = m_SelectedDraft;
			var choices = new List<KeyValuePair<string, Action>>
			{
				new KeyValuePair<string, Action>(
					"None",
					() =>
					{
						if (
							draft != m_SelectedDraft
							|| !EditorApplication.isPlaying
							|| m_CommandExecution != null
							|| m_PendingCommandRequest != null
						)
						{
							return;
						}

						var result = Drafts.ClearReference(draft, field.Id);
						if (result.Status == DebugCommandDraftOperationStatus.Succeeded)
						{
							m_CommandValidationErrors.Clear();
						}

						m_CommandNotice = result.Status == DebugCommandDraftOperationStatus.Succeeded ? "" : result.Status.ToString();
						m_CommandResultEdited = m_LastCommandExecution != null;
						RefreshEditorCommandValues();
						m_Helper = null;
					}
				),
			};
			var offset = 0;
			while (true)
			{
				var page = Drafts.GetReferenceEntries(
					draft,
					field.Id,
					new DebugListQuery("", "", new DebugPageRequest(offset, BoundedPageSize(16)))
				);
				foreach (var entry in page.Entries)
				{
					choices.Add(
						new KeyValuePair<string, Action>(
							entry.Label + "\n" + entry.Path,
							() =>
							{
								if (
									draft != m_SelectedDraft
									|| !EditorApplication.isPlaying
									|| m_CommandExecution != null
									|| m_PendingCommandRequest != null
								)
								{
									return;
								}

								var result = Drafts.SelectReference(draft, field.Id, entry.Handle);
								if (result.Status == DebugCommandDraftOperationStatus.Succeeded)
								{
									m_CommandValidationErrors.Clear();
								}

								m_CommandResultEdited = m_LastCommandExecution != null;
								m_CommandNotice =
									result.Status == DebugCommandDraftOperationStatus.Succeeded ? "" : result.Status.ToString();
								RefreshEditorCommandValues();
								m_Helper = null;
							}
						)
					);
				}

				if (!page.Page.HasMore || page.Entries.Count == 0)
				{
					break;
				}

				offset += page.Entries.Count;
			}

			m_Helper = new EditorHelper
			{
				Title = field.Label,
				Session = DexcDebugger.CurrentSession,
				Choices = choices,
			};
		}

		private void OpenInspectorReferenceHierarchy(
			EditorInspectorField field,
			DebugInspectorComponentHandle owner,
			DebugSceneHandle scene,
			DebugObjectHandle parent,
			int offset,
			Action back
		)
		{
			if (owner != m_SelectedComponent || !m_EditorInspectorFields.Contains(field) || !CanEditInspector(field))
			{
				return;
			}

			var page = DebugInspector.GetHierarchyChildren(
				new DebugHierarchyChildrenQuery(scene, parent, new DebugPageRequest(offset, BoundedHierarchyPageSize(16)))
			);
			var choices = new List<KeyValuePair<string, Action>>();
			Action current = () => OpenInspectorReferenceHierarchy(field, owner, scene, parent, offset, back);
			if (back != null)
			{
				choices.Add(new KeyValuePair<string, Action>("Parent", back));
			}

			foreach (var entry in page.Entries)
			{
				choices.Add(
					new KeyValuePair<string, Action>(
						entry.Path,
						() =>
						{
							var candidates = DexcInspectorRuntime.Registry.GetAssignableReferences(
								entry.InspectorTarget,
								field.Descriptor.TypeName
							);
							m_Helper = new EditorHelper
							{
								Title = entry.Path,
								Session = DexcDebugger.CurrentSession,
								Choices = candidates
									.Select(candidate => new KeyValuePair<string, Action>(
										candidate.Value,
										() =>
										{
											if (
												owner != m_SelectedComponent
												|| !m_EditorInspectorFields.Contains(field)
												|| !CanEditInspector(field)
											)
											{
												return;
											}

											field.Reference = DebugInspectorWriteValue.FromReference(candidate.Key);
											field.ReferenceLabel = entry.Path + " / " + candidate.Value;
											field.Dirty = true;
											field.Error = null;
											m_Helper = null;
										}
									))
									.ToList(),
							};
							m_Helper.Choices.Add(
								new KeyValuePair<string, Action>(
									"Browse children",
									() => OpenInspectorReferenceHierarchy(field, owner, scene, entry.Target, 0, current)
								)
							);
							m_Helper.Choices.Add(new KeyValuePair<string, Action>("Parent", current));
						}
					)
				);
			}

			if (offset > 0)
			{
				choices.Add(
					new KeyValuePair<string, Action>(
						"Previous",
						() =>
							OpenInspectorReferenceHierarchy(
								field,
								owner,
								scene,
								parent,
								Math.Max(0, offset - BoundedHierarchyPageSize(16)),
								back
							)
					)
				);
			}

			if (page.Page.HasMore)
			{
				choices.Add(
					new KeyValuePair<string, Action>(
						"Next",
						() => OpenInspectorReferenceHierarchy(field, owner, scene, parent, offset + page.Entries.Count, back)
					)
				);
			}

			m_Helper = new EditorHelper
			{
				Title = "Scene objects",
				Session = DexcDebugger.CurrentSession,
				Choices = choices,
			};
		}

		private void OpenCommandChoice(DebugCommandFieldDescriptor field)
		{
			var draft = m_SelectedDraft;
			Action<DebugValue> select = value =>
			{
				if (
					draft != m_SelectedDraft
					|| !EditorApplication.isPlaying
					|| m_CommandExecution != null
					|| m_PendingCommandRequest != null
				)
				{
					return;
				}

				UpdateCommandInput(field.Id, value);
				m_Helper = null;
			};
			var choices = new List<KeyValuePair<string, Action>>
			{
				new KeyValuePair<string, Action>("None", () => select(DebugValue.Null)),
			};
			choices.AddRange(
				DexcCommandRuntime
					.Drafts.GetChoiceCandidates(draft, field.Id)
					.Select(candidate => new KeyValuePair<string, Action>(candidate.Label, () => select(DebugValue.From(candidate.Key))))
			);
			m_Helper = new EditorHelper
			{
				Title = field.Label,
				Session = DexcDebugger.CurrentSession,
				Choices = choices,
			};
		}
	}
}
