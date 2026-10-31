using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Dexc
{
	/// <summary>
	/// What Dexc looks like from outside the process, as a set of operations that answer with plain objects.
	///
	/// This is the part that survives a change of carrier. A transport that brings its own framing and serializer calls
	/// these directly; one that does not is handed the same answers through <see cref="DexcRemoteHost"/>. Nothing here
	/// knows how the answer travels.
	///
	/// The Editor window does not come through here. It calls the features straight, as it always has: removing every
	/// remote path would leave the window unchanged.
	/// </summary>
	internal sealed class DexcRemoteOperations
	{
		// What a connection accumulates lives on the instance the carrier holds, not in a static. A second one answers
		// about the same features without inheriting what the first saw.
		private string m_KnownSession = string.Empty;
		private long m_Epoch;

		// A diagnostic has no name of its own, so listing one hands out a token and this remembers what it stood for.
		// A caller cannot make one up, and everything handed out by an earlier run is dropped when the session changes.
		private readonly Dictionary<string, DebugDiagnosticId> m_Diagnostics = new Dictionary<string, DebugDiagnosticId>(
			StringComparer.Ordinal
		);

		internal const string ProtocolVersion = "1";

		/// <summary>Raised when the caller asked for something that cannot be answered as a result.</summary>
		internal sealed class RefusedException : Exception
		{
			internal RefusedException(string reason, string message)
				: base(message)
			{
				Reason = reason;
			}

			internal string Reason { get; }
		}

		internal IDictionary<string, object> Hello()
		{
			return new Dictionary<string, object> { ["protocolVersion"] = ProtocolVersion, ["session"] = SessionStatus() };
		}

		/// <summary>
		/// Starts a session when the game has not. Initialize carries [Conditional("DEXC_DEBUGGER")], so a caller compiled
		/// without that symbol - evaluated code, a tool assembly - loses the call entirely and silently. This operation
		/// lives where the symbol is, so an external tool can reach it.
		/// </summary>
		internal IDictionary<string, object> Initialize()
		{
			if (!DexcDebugger.IsEnabled)
				return Status("disabled", "Dexc is not compiled into this build.");
			if (!DexcDebugger.IsInitialized)
				DexcDebugger.Initialize();
			return Succeeded(new Dictionary<string, object>());
		}

		internal IDictionary<string, object> SessionStatus()
		{
			return new Dictionary<string, object>
			{
				["enabled"] = DexcDebugger.IsEnabled,
				["state"] = DexcDebugger.IsInitialized ? "active" : "not-initialized",
				["epoch"] = Epoch(),
			};
		}

		internal IDictionary<string, object> CommandList(string pathPrefix, string searchText, int offset, int count)
		{
			if (!TryRequireSession(out var unavailable))
				return unavailable;
			var page = Paged(() => DebugCommand.Registry.GetList(Query(pathPrefix, searchText, offset, count)));
			var items = new List<object>();
			foreach (var item in page.Items)
			{
				items.Add(
					new Dictionary<string, object>
					{
						["path"] = item.Path,
						["displayName"] = item.DisplayName,
						["description"] = item.Description,
						["order"] = (long)item.Order,
						["isAsync"] = item.IsAsync,
						["hasOutput"] = item.HasOutput,
						["hasConfirmation"] = item.HasConfirmation,
						["access"] = item.Access.ToString(),
					}
				);
			}
			return Succeeded(
				new Dictionary<string, object>
				{
					["summary"] = Summary(page.Summary),
					["page"] = Page(page.Page),
					["items"] = items,
				}
			);
		}

		internal IDictionary<string, object> CommandDescribe(string path)
		{
			if (!TryRequireSession(out var unavailable))
				return unavailable;
			RequirePath(path);
			if (!TryFindCommand(path, out var descriptor))
				return Status("not-found", "No command is at \"" + path + "\".");
			var fields = new List<object>();
			foreach (var field in descriptor.Fields)
			{
				var item = new Dictionary<string, object>
				{
					["name"] = field.Name,
					["label"] = field.Label ?? string.Empty,
					["isInput"] = field.IsInput,
					["inputTypeName"] = field.InputTypeName,
				};
				if (field.HasRange)
				{
					item["minimum"] = field.RangeMinimum;
					item["maximum"] = field.RangeMaximum;
				}
				fields.Add(item);
			}
			return Succeeded(
				new Dictionary<string, object>
				{
					["path"] = descriptor.Path,
					["isAsync"] = descriptor.IsAsync,
					["hasOutput"] = descriptor.HasOutput,
					["access"] = descriptor.Access.ToString(),
					["confirmationMessage"] = descriptor.ConfirmationMessage,
					["fields"] = fields,
				}
			);
		}

		/// <summary>
		/// Runs one command and answers with what it produced.
		///
		/// A command declared async is refused here. The carrier hands this to the main thread and waits for it to
		/// return, and an async command's completion is posted back to that same thread - so waiting would stop the very
		/// thread the command needs to finish on. A carrier that holds a connection open can wait properly; this one
		/// says so instead of hanging.
		/// </summary>
		internal IDictionary<string, object> CommandExecute(string path, IDictionary<string, object> inputs)
		{
			if (!TryRequireSession(out var unavailable))
				return unavailable;
			RequirePath(path);
			if (!TryFindCommand(path, out var descriptor))
				return Status("not-found", "No command is at \"" + path + "\".");
			if (descriptor.IsAsync)
				return Status(
					"unsupported",
					"This carrier cannot wait for an async command. Run it from the game or a connection that stays open."
				);

			var creation = DebugCommand.Draft.Create(descriptor.Id);
			if (!creation.TryGetDraftId(out var draft))
				return Status(DraftStatusText(creation.Status), "The command draft could not be created.");
			try
			{
				if (inputs != null)
				{
					foreach (var pair in inputs)
					{
						if (!TryFindField(descriptor, pair.Key, out var field))
							return Status("not-found", "No input is named \"" + pair.Key + "\".");
						if (!DexcRemoteValueReader.TryFromObject(pair.Value, out var value, out var error, field.Schema))
							throw new RefusedException(DexcRemoteReason.InvalidParams, error);
						var update = DebugCommand.Draft.UpdateInput(draft, field.Id, value);
						if (update.Status != DebugCommandDraftOperationStatus.Succeeded)
						{
							return Refused(
								DraftStatusText(update.Status),
								"The input \"" + pair.Key + "\" was not accepted.",
								descriptor,
								update.Validation
							);
						}
					}
				}
				var request = DebugCommand.Draft.CreateRequest(draft);
				if (!request.TryGetRequest(out var ready))
					return Refused(RequestStatusText(request.Status), "The command could not be prepared.", descriptor, request.Validation);
				return Executed(ready.Dispatch());
			}
			finally
			{
				DebugCommand.Draft.Discard(draft);
			}
		}

		private IDictionary<string, object> Executed(CommandExecutionResult result)
		{
			var answer = new Dictionary<string, object>
			{
				["status"] = result.Status == CommandExecutionStatus.Succeeded ? "succeeded" : "failed",
				["session"] = SessionStatus(),
				["path"] = result.CommandPath,
				["executionStatus"] = result.Status.ToString(),
				["failureKind"] = result.FailureKind.ToString(),
				["errorCode"] = result.ErrorCode,
				["message"] = result.Message,
				["durationMs"] = (long)result.Duration.TotalMilliseconds,
			};
			if (result.Output != null)
				answer["output"] = DexcRemoteValue.ToObject(result.Output);
			if (!string.IsNullOrEmpty(result.OutputTypeName))
				answer["outputTypeName"] = result.OutputTypeName;
			return answer;
		}

		/// <summary>
		/// A refusal with the command's own reasons attached. The status alone only says that it was turned down; a tool
		/// outside the process has no other way to read why.
		/// </summary>
		private IDictionary<string, object> Refused(
			string status,
			string message,
			DebugCommandDescriptor descriptor,
			DebugCommandValidationResult validation
		)
		{
			var answer = Status(status, message);
			var issues = new List<object>();
			if (validation != null)
			{
				foreach (var item in validation.Issues)
				{
					var issue = new Dictionary<string, object>
					{
						["severity"] = item.Issue.Severity.ToString(),
						["code"] = item.Issue.Code ?? string.Empty,
						["message"] = item.Issue.Message ?? string.Empty,
						["unavailable"] = item.IsUnavailable,
					};
					if (item.HasField)
						issue["field"] = FieldName(descriptor, item.FieldId);
					issues.Add(issue);
				}
			}
			answer["issues"] = issues;
			return answer;
		}

		private static string FieldName(DebugCommandDescriptor descriptor, DebugCommandFieldId id)
		{
			foreach (var field in descriptor.Fields)
			{
				if (field.Id.Equals(id))
					return field.Name;
			}
			return string.Empty;
		}

		private static bool TryFindField(DebugCommandDescriptor descriptor, string name, out DebugCommandFieldDescriptor field)
		{
			foreach (var item in descriptor.Fields)
			{
				if (!item.IsInput || !string.Equals(item.Name, name, StringComparison.Ordinal))
					continue;
				field = item;
				return true;
			}
			field = null;
			return false;
		}

		private static string RequestStatusText(DebugCommandRequestCreationStatus status)
		{
			switch (status)
			{
				case DebugCommandRequestCreationStatus.Created:
					return "succeeded";
				case DebugCommandRequestCreationStatus.ValidationFailed:
					return "rejected";
				case DebugCommandRequestCreationStatus.Stale:
					return "stale";
				case DebugCommandRequestCreationStatus.DefinitionUnavailable:
					return "definition-unavailable";
				default:
					return "disabled";
			}
		}

		private static string DraftStatusText(DebugCommandDraftOperationStatus status)
		{
			switch (status)
			{
				case DebugCommandDraftOperationStatus.Succeeded:
					return "succeeded";
				case DebugCommandDraftOperationStatus.Rejected:
					return "rejected";
				case DebugCommandDraftOperationStatus.Stale:
					return "stale";
				case DebugCommandDraftOperationStatus.NotFound:
					return "not-found";
				case DebugCommandDraftOperationStatus.DefinitionUnavailable:
					return "definition-unavailable";
				default:
					return "disabled";
			}
		}

		internal IDictionary<string, object> SettingList(string pathPrefix, string searchText, int offset, int count)
		{
			if (!TryRequireSession(out var unavailable))
				return unavailable;
			var page = Paged(() => DebugSetting.GetList(Query(pathPrefix, searchText, offset, count)));
			var items = new List<object>();
			foreach (var item in page.Items)
			{
				items.Add(
					new Dictionary<string, object>
					{
						["path"] = item.Path,
						["displayName"] = item.DisplayName,
						["valueType"] = item.ValueType,
						["scope"] = item.Scope.ToString(),
						["applyMode"] = item.ApplyMode.ToString(),
						["access"] = item.Access.ToString(),
					}
				);
			}
			return Succeeded(
				new Dictionary<string, object>
				{
					["summary"] = Summary(page.Summary),
					["page"] = Page(page.Page),
					["items"] = items,
				}
			);
		}

		internal IDictionary<string, object> SettingRead(string path)
		{
			if (!TryRequireSession(out var unavailable))
				return unavailable;
			RequirePath(path);
			if (!TryFindSetting(path, out var descriptor))
				return Status("not-found", "No setting is at \"" + path + "\".");
			var read = DebugSetting.Read(descriptor.Id);
			var answer = new Dictionary<string, object>
			{
				["status"] = StatusText(read.Status),
				["session"] = SessionStatus(),
				["path"] = descriptor.Path,
				["valueType"] = descriptor.ValueType,
			};
			if (read.TryGetValue(out var value))
				answer["value"] = DexcRemoteValue.ToObject(value);
			return answer;
		}

		/// <summary>The presets of every set that has a setting under the prefix. A preset is named by its declared ID.</summary>
		internal IDictionary<string, object> SettingPresets(string pathPrefix, int offset, int count)
		{
			if (!TryRequireSession(out var unavailable))
				return unavailable;
			var request = PageRequest(offset, count);
			var all = FindPresets(pathPrefix);
			var items = new List<object>();
			foreach (var preset in all.Skip(request.Offset).Take(request.Count))
			{
				items.Add(
					new Dictionary<string, object>
					{
						["id"] = preset.Key,
						["displayName"] = preset.DisplayName ?? string.Empty,
						["description"] = preset.Description ?? string.Empty,
					}
				);
			}
			return Succeeded(
				new Dictionary<string, object>
				{
					["page"] = Page(new DebugPageInfo(request.Offset, items.Count, request.Offset + items.Count < all.Count, true, all.Count, false)),
					["items"] = items,
				}
			);
		}

		/// <summary>What applying the preset would change, and why it would be refused. Nothing is changed.</summary>
		internal IDictionary<string, object> SettingPreviewPreset(string presetId)
		{
			if (!TryRequireSession(out var unavailable))
				return unavailable;
			if (string.IsNullOrEmpty(presetId))
				throw new RefusedException(DexcRemoteReason.InvalidParams, "This operation needs a preset id.");
			var preset = FindPresets(string.Empty).FirstOrDefault(item => string.Equals(item.Key, presetId, StringComparison.Ordinal));
			if (preset == null)
				return Status("not-found", "No preset has the id \"" + presetId + "\".");
			var preview = DebugSetting.PreviewPreset(preset.Id);
			var differences = new List<object>();
			foreach (var difference in preview.Value ?? Array.Empty<DebugSettingPresetDifference>())
			{
				differences.Add(
					new Dictionary<string, object>
					{
						["path"] = difference.Path,
						["current"] = DexcRemoteValue.ToObject(difference.CurrentValue),
						["preset"] = DexcRemoteValue.ToObject(difference.PresetValue),
					}
				);
			}
			var issues = new List<object>();
			foreach (var issue in preview.Issues)
			{
				issues.Add(
					new Dictionary<string, object>
					{
						["severity"] = issue.Severity.ToString(),
						["code"] = issue.Code ?? string.Empty,
						["message"] = issue.Message ?? string.Empty,
					}
				);
			}
			return new Dictionary<string, object>
			{
				["status"] = StatusText(preview.Status),
				["session"] = SessionStatus(),
				["id"] = preset.Key,
				["differences"] = differences,
				["issues"] = issues,
			};
		}

		/// <summary>
		/// Presets hang off a set, and a set is reached through its settings. The walk goes one item a page for the same
		/// reason the finds below do: the host does not know the session's page limit.
		/// </summary>
		private static List<DebugSettingPresetItem> FindPresets(string pathPrefix)
		{
			var sets = new List<DebugSettingSetId>();
			var offset = 0;
			while (true)
			{
				var request = PageRequest(offset, FindPageSize);
				var page = Paged(() => DebugSetting.GetList(new DebugListQuery(pathPrefix ?? string.Empty, string.Empty, request)));
				foreach (var item in page.Items)
				{
					if (!sets.Contains(item.SetId))
						sets.Add(item.SetId);
				}
				if (!page.Page.HasMore || page.Items.Count == 0)
					break;
				offset += page.Items.Count;
			}
			var presets = new List<DebugSettingPresetItem>();
			foreach (var set in sets)
			{
				offset = 0;
				while (true)
				{
					var request = PageRequest(offset, FindPageSize);
					var page = Paged(() => DebugSetting.GetPresets(set, request));
					presets.AddRange(page.Items);
					if (!page.Page.HasMore || page.Items.Count == 0)
						break;
					offset += page.Items.Count;
				}
			}
			return presets;
		}

		internal IDictionary<string, object> InspectorRegistered(string pathPrefix, string searchText, int offset, int count)
		{
			if (!TryRequireSession(out var unavailable))
				return unavailable;
			var page = Paged(() => DebugInspector.GetRegistered(Query(pathPrefix, searchText, offset, count)));
			var items = new List<object>();
			foreach (var entry in page.Entries)
			{
				items.Add(
					new Dictionary<string, object>
					{
						["entityId"] = Entity(entry.EntityId),
						["path"] = entry.Path ?? string.Empty,
						["label"] = entry.Label ?? string.Empty,
						["origin"] = entry.Origin.ToString(),
						["sceneName"] = entry.SceneName ?? string.Empty,
						["hierarchyPath"] = entry.HierarchyPath ?? string.Empty,
						["alive"] = entry.Alive,
					}
				);
			}
			return Succeeded(new Dictionary<string, object> { ["page"] = Page(page.Page), ["items"] = items });
		}

		internal IDictionary<string, object> InspectorScenes()
		{
			if (!TryRequireSession(out var unavailable))
				return unavailable;
			var items = new List<object>();
			foreach (var scene in DebugInspector.GetScenes().Scenes)
			{
				items.Add(
					new Dictionary<string, object>
					{
						// A scene made while the game runs has neither a name nor a path, so a token is what always names one.
						["scene"] = scene.Scene.ToString(),
						["name"] = scene.Name ?? string.Empty,
						["path"] = scene.Path ?? string.Empty,
						["isLoaded"] = scene.IsLoaded,
					}
				);
			}
			return Succeeded(new Dictionary<string, object> { ["items"] = items });
		}

		/// <summary>
		/// One level of a scene's hierarchy. Leaving the parent out asks for the roots. Walking a level is also what makes
		/// the objects on it nameable: an object the caller has seen listed is one it can then ask about by instance id.
		/// </summary>
		internal IDictionary<string, object> InspectorChildren(string scene, ulong parentEntityId, int offset, int count)
		{
			if (!TryRequireSession(out var unavailable))
				return unavailable;
			if (string.IsNullOrEmpty(scene))
				throw new RefusedException(DexcRemoteReason.InvalidParams, "This operation needs a scene.");
			if (!TryFindScene(scene, out var handle, out var sceneRefusal))
				return sceneRefusal;
			var parent = default(DebugObjectHandle);
			if (parentEntityId != 0UL && !DebugInspector.TryGetObject(parentEntityId, out parent))
				return Unknown(parentEntityId);
			var page = Paged(() =>
				DebugInspector.GetHierarchyChildren(new DebugHierarchyChildrenQuery(handle, parent, PageRequest(offset, count)))
			);
			var items = new List<object>();
			foreach (var entry in page.Entries)
			{
				items.Add(
					new Dictionary<string, object>
					{
						["entityId"] = Entity(entry.EntityId),
						["label"] = entry.Label ?? string.Empty,
						["path"] = entry.Path ?? string.Empty,
						["active"] = entry.Active,
						["hasChildren"] = entry.HasChildren,
					}
				);
			}
			return Succeeded(new Dictionary<string, object> { ["page"] = Page(page.Page), ["items"] = items });
		}

		internal IDictionary<string, object> InspectorComponents(ulong entityId)
		{
			if (!TryRequireSession(out var unavailable))
				return unavailable;
			if (!DebugInspector.TryGetTarget(entityId, out var target))
				return Unknown(entityId);
			var page = DebugInspector.GetComponents(target);
			var items = new List<object>();
			foreach (var entry in page.Components)
			{
				var item = new Dictionary<string, object>
				{
					["typeName"] = entry.TypeName ?? string.Empty,
					["displayName"] = entry.DisplayName ?? string.Empty,
					["hasRegisteredFields"] = entry.HasRegisteredFields,
					["alive"] = entry.Alive,
				};
				if (entry.DiagnosticId.IsValid)
					item["diagnosticId"] = Token(entry.DiagnosticId);
				items.Add(item);
			}
			return Succeeded(new Dictionary<string, object> { ["page"] = Page(page.Page), ["items"] = items });
		}

		internal IDictionary<string, object> InspectorFields(ulong entityId, string typeName, int offset, int count)
		{
			if (!TryRequireSession(out var unavailable))
				return unavailable;
			if (!TryFindComponent(entityId, typeName, out var component, out var refusal))
				return refusal;
			var page = Paged(() => component.GetFields(PageRequest(offset, count)));
			var items = new List<object>();
			foreach (var field in page.Fields)
			{
				items.Add(
					new Dictionary<string, object>
					{
						["name"] = field.Name ?? string.Empty,
						["displayName"] = field.DisplayName ?? string.Empty,
						["typeName"] = field.TypeName ?? string.Empty,
						["isRegistered"] = field.IsRegistered,
						["access"] = field.Access.ToString(),
						["canWrite"] = field.CanWrite,
						["isCollection"] = field.IsCollection,
					}
				);
			}
			return Succeeded(new Dictionary<string, object> { ["page"] = Page(page.Page), ["items"] = items });
		}

		/// <summary>
		/// Reads named fields. A name is turned into what the feature reads by walking the component's field pages, so
		/// <paramref name="count"/> is how many descriptors are looked at per page, not how many fields come back.
		/// </summary>
		internal IDictionary<string, object> InspectorRead(ulong entityId, string typeName, IReadOnlyList<string> fields, int count)
		{
			if (!TryRequireSession(out var unavailable))
				return unavailable;
			if (fields == null || fields.Count == 0)
				throw new RefusedException(DexcRemoteReason.InvalidParams, "This operation needs at least one field name.");
			if (!TryFindComponent(entityId, typeName, out var component, out var refusal))
				return refusal;

			var declared = Declared(component, count);
			var ids = new List<DebugInspectorFieldId>();
			var names = new List<string>();
			foreach (var name in fields)
			{
				DebugInspectorFieldDescriptor found = null;
				foreach (var descriptor in declared)
				{
					if (!string.Equals(descriptor.Name, name, StringComparison.Ordinal))
						continue;
					found = descriptor;
					break;
				}
				if (found == null)
					return Status("not-found", "The " + typeName + " has no field named \"" + name + "\".");
				ids.Add(found.Id);
				names.Add(found.Name);
			}

			var read = component.ReadFields(ids);
			var answer = new Dictionary<string, object>
			{
				["status"] = StatusText(read.Status),
				["session"] = SessionStatus(),
				["entityId"] = Entity(entityId),
				["typeName"] = typeName,
			};
			if (read.TryGetValue(out var values))
			{
				var items = new List<object>();
				foreach (var value in values)
				{
					var name = string.Empty;
					for (var i = 0; i < ids.Count; i++)
					{
						if (!ids[i].Equals(value.Field))
							continue;
						name = names[i];
						break;
					}
					items.Add(Snapshot(name, value.Snapshot));
				}
				answer["items"] = items;
			}
			return answer;
		}

		private static List<DebugInspectorFieldDescriptor> Declared(DebugInspectorComponentHandle component, int count)
		{
			var declared = new List<DebugInspectorFieldDescriptor>();
			var offset = 0;
			while (true)
			{
				var at = offset;
				var page = Paged(() => component.GetFields(PageRequest(at, count)));
				foreach (var field in page.Fields)
					declared.Add(field);
				if (!page.Page.HasMore || page.Page.Count <= 0)
					return declared;
				offset += page.Page.Count;
			}
		}

		private IDictionary<string, object> Snapshot(string name, InspectorValueSnapshot snapshot)
		{
			var answer = new Dictionary<string, object>
			{
				["name"] = name,
				["state"] = snapshot.State.ToString(),
				["typeName"] = snapshot.TypeName ?? string.Empty,
			};
			if (snapshot.Value != null)
				answer["value"] = DexcRemoteValue.ToObject(snapshot.Value);
			if (!string.IsNullOrEmpty(snapshot.ReferenceType))
				answer["referenceType"] = snapshot.ReferenceType;
			if (!string.IsNullOrEmpty(snapshot.ReferenceLabel))
				answer["label"] = snapshot.ReferenceLabel;
			if (!string.IsNullOrEmpty(snapshot.Error))
				answer["error"] = snapshot.Error;
			if (snapshot.DiagnosticId.IsValid)
				answer["diagnosticId"] = Token(snapshot.DiagnosticId);
			return answer;
		}

		private IDictionary<string, object> Unknown(ulong entityId)
		{
			return Status(
				"not-found",
				"Nothing in this run has the entity id "
					+ entityId.ToString(CultureInfo.InvariantCulture)
					+ ". An entity id comes from a listing and only holds for the run that gave it."
			);
		}

		/// <summary>
		/// An entity id leaves as a number, or as text once it passes what a double holds exactly. That is the same rule
		/// the value mapping uses and for the same reason: most serializers put a JSON number through a double, and an id
		/// that comes back with different digits names nothing. Measured ids sit far below the threshold.
		/// </summary>
		private static object Entity(ulong entityId)
		{
			return entityId > 9007199254740992UL ? (object)entityId.ToString(CultureInfo.InvariantCulture) : entityId;
		}

		/// <summary>
		/// A scene is named by the token a listing gave, or by its path, or by its name. The token always works; a scene made
		/// while the game runs has neither of the other two. A name two loaded scenes share is refused rather than one of them
		/// being picked quietly. Nothing is remembered for this: the token is re-derived from the live listing, so a scene
		/// that has gone simply stops matching.
		/// </summary>
		private bool TryFindScene(string scene, out DebugSceneHandle handle, out IDictionary<string, object> refusal)
		{
			handle = default(DebugSceneHandle);
			refusal = null;
			var matches = new List<DebugInspectorSceneEntry>();
			foreach (var entry in DebugInspector.GetScenes().Scenes)
			{
				if (
					string.Equals(entry.Scene.ToString(), scene, StringComparison.Ordinal)
					|| (entry.Path.Length != 0 && string.Equals(entry.Path, scene, StringComparison.Ordinal))
					|| (entry.Name.Length != 0 && string.Equals(entry.Name, scene, StringComparison.Ordinal))
				)
					matches.Add(entry);
			}
			if (matches.Count == 0)
			{
				refusal = Status("not-found", "No loaded scene is \"" + scene + "\".");
				return false;
			}
			if (matches.Count > 1)
			{
				refusal = Status("rejected", "More than one loaded scene is \"" + scene + "\". Name it by the token a listing gave.");
				return false;
			}
			handle = matches[0].Scene;
			return true;
		}

		private bool TryFindComponent(
			ulong entityId,
			string typeName,
			out DebugInspectorComponentHandle component,
			out IDictionary<string, object> refusal
		)
		{
			component = default(DebugInspectorComponentHandle);
			refusal = null;
			if (string.IsNullOrEmpty(typeName))
				throw new RefusedException(DexcRemoteReason.InvalidParams, "This operation needs a component type name.");
			if (!DebugInspector.TryGetTarget(entityId, out var target))
			{
				refusal = Unknown(entityId);
				return false;
			}
			// Either the full name or the short one, on the same rule as a scene: two matches are refused, not guessed between.
			var matches = new List<DebugInspectorComponentEntry>();
			foreach (var entry in DebugInspector.GetComponents(target).Components)
			{
				if (
					string.Equals(entry.TypeName, typeName, StringComparison.Ordinal)
					|| string.Equals(entry.DisplayName, typeName, StringComparison.Ordinal)
				)
					matches.Add(entry);
			}
			if (matches.Count == 0)
			{
				refusal = Status("not-found", "The object has no " + typeName + ".");
				return false;
			}
			if (matches.Count > 1)
			{
				refusal = Status("rejected", "The object has more than one " + typeName + ", and this cannot say which.");
				return false;
			}
			component = matches[0].Component;
			return true;
		}

		/// <summary>
		/// The one frame Display finished drawing. Display keeps no history, so this is what there is: asking twice in the
		/// same frame answers the same thing, and asking before anything has drawn answers that nothing has.
		/// </summary>
		internal IDictionary<string, object> DisplayFrame()
		{
			if (!TryRequireSession(out var unavailable))
				return unavailable;
			if (!DebugDisplay.TryGetCompletedFrame(out var frame))
				return Status("not-found", "No frame has finished drawing yet.");
			var subjects = new List<object>();
			foreach (var subject in frame.Subjects)
				subjects.Add(Subject(subject));
			var globals = new List<object>();
			foreach (var visual in frame.Globals)
				globals.Add(Visual(visual));
			return Succeeded(
				new Dictionary<string, object>
				{
					["frame"] = frame.Frame,
					["timestamp"] = frame.Timestamp,
					["droppedCount"] = (long)frame.DroppedCount,
					["subjects"] = subjects,
					["globals"] = globals,
				}
			);
		}

		private static IDictionary<string, object> Subject(DebugDisplaySubjectSnapshot subject)
		{
			var metadata = new List<object>();
			foreach (var item in subject.Metadata)
				metadata.Add(new Dictionary<string, object> { ["name"] = item.Name, ["value"] = DexcRemoteValue.ToObject(item.Value) });
			var visuals = new List<object>();
			foreach (var item in subject.Visuals)
				visuals.Add(Visual(item));
			var answer = new Dictionary<string, object>
			{
				["id"] = subject.Id.ToString(),
				["category"] = subject.Category,
				["label"] = subject.Label,
				["metadata"] = metadata,
				["visuals"] = visuals,
			};
			if (subject.Owner != null)
				answer["owner"] = Owner(subject.Owner);
			return answer;
		}

		/// <summary>
		/// Every geometric member goes out whatever the kind is. Which ones mean something follows from <c>kind</c>, and a
		/// reader that knows the kind reads the right ones; leaving the rest out instead would mean this layer deciding
		/// what each kind uses, and being wrong about one silently drops it.
		/// </summary>
		private static IDictionary<string, object> Visual(DebugDisplayVisualSnapshot visual)
		{
			var points = new List<object>();
			if (visual.Points != null)
			{
				foreach (var point in visual.Points)
					points.Add(Point(point));
			}
			var answer = new Dictionary<string, object>
			{
				["kind"] = visual.Kind.ToString(),
				["name"] = visual.Name,
				["path"] = visual.Path,
				["text"] = visual.Text,
				["color"] = Rgba(visual.Color),
				["fillColor"] = Rgba(visual.FillColor),
				["drawStyle"] = visual.DrawStyle.ToString(),
				["timeScope"] = visual.TimeScope.ToString(),
				["space"] = visual.Space.ToString(),
				["expiresAt"] = visual.ExpiresAt,
				["start"] = Point(visual.Start),
				["end"] = Point(visual.End),
				["center"] = Point(visual.Center),
				["size"] = Point(visual.Size),
				["normal"] = Point(visual.Normal),
				["rotation"] = Rotation(visual.Rotation),
				["radius"] = visual.Radius,
				["height"] = visual.Height,
				["points"] = points,
			};
			if (visual.Value != null)
				answer["value"] = DexcRemoteValue.ToObject(visual.Value);
			if (visual.Source.IsValid)
				answer["source"] = visual.Source.ToString();
			if (visual.Target.IsValid)
				answer["target"] = visual.Target.ToString();
			if (visual.TargetOwner != null)
				answer["targetOwner"] = Owner(visual.TargetOwner);
			if (visual.Basis != null)
				answer["basis"] = Owner(visual.Basis);
			return answer;
		}

		/// <summary>The owning object is described, not handed over: the handle it holds is not an external name.</summary>
		private static IDictionary<string, object> Owner(DebugDisplayOwnerSnapshot owner)
		{
			return new Dictionary<string, object>
			{
				["typeName"] = owner.TypeName,
				["sceneName"] = owner.SceneName,
				["hierarchyPath"] = owner.HierarchyPath,
				["alive"] = owner.Alive,
				["position"] = Point(owner.Position),
				["rotation"] = Rotation(owner.Rotation),
				["scale"] = Point(owner.Scale),
			};
		}

		private static IDictionary<string, object> Point(UnityEngine.Vector3 value)
		{
			return new Dictionary<string, object>
			{
				["x"] = value.x,
				["y"] = value.y,
				["z"] = value.z,
			};
		}

		private static IDictionary<string, object> Rotation(UnityEngine.Quaternion value)
		{
			return new Dictionary<string, object>
			{
				["x"] = value.x,
				["y"] = value.y,
				["z"] = value.z,
				["w"] = value.w,
			};
		}

		private static IDictionary<string, object> Rgba(UnityEngine.Color value)
		{
			return new Dictionary<string, object>
			{
				["r"] = value.r,
				["g"] = value.g,
				["b"] = value.b,
				["a"] = value.a,
			};
		}

		internal IDictionary<string, object> DiagnosticsList(string sources, string features, string severity, int offset, int count)
		{
			if (!TryRequireSession(out var unavailable))
				return unavailable;
			var query = DiagnosticQuery(sources, features, severity, offset, count);
			var page = Paged(() => DebugDiagnostics.GetSummaries(query));
			var items = new List<object>();
			foreach (var item in page.Summaries)
				items.Add(Summary(item));
			return Succeeded(new Dictionary<string, object> { ["page"] = Page(page.Page), ["items"] = items });
		}

		/// <summary>
		/// The exception behind a diagnostic, asked for by name. A failed operation answers with the token alone - the type,
		/// the message and the stack are not put on the wire unless someone asks for this one.
		/// </summary>
		internal IDictionary<string, object> DiagnosticsDetail(string diagnosticId)
		{
			if (!TryRequireSession(out var unavailable))
				return unavailable;
			if (string.IsNullOrEmpty(diagnosticId))
				throw new RefusedException(DexcRemoteReason.InvalidParams, "This operation needs a diagnostic id.");
			// Drops what an earlier run handed out before the token is looked up.
			Epoch();
			if (!m_Diagnostics.TryGetValue(diagnosticId, out var id))
			{
				return Status(
					"not-found",
					"Nothing was listed under \"" + diagnosticId + "\". A diagnostic id comes from a listing and cannot be composed."
				);
			}
			if (!DebugDiagnostics.TryGetDetail(id, out var detail))
				return Status("not-found", "The diagnostic \"" + diagnosticId + "\" is no longer held.");
			var inner = new List<object>();
			if (detail.InnerExceptions != null)
			{
				foreach (var item in detail.InnerExceptions)
				{
					inner.Add(
						new Dictionary<string, object>
						{
							["typeName"] = item.TypeName ?? string.Empty,
							["message"] = item.Message ?? string.Empty,
						}
					);
				}
			}
			return Succeeded(
				new Dictionary<string, object>
				{
					["summary"] = Summary(detail.Summary),
					["exceptionTypeName"] = detail.ExceptionTypeName,
					["exceptionMessage"] = detail.ExceptionMessage,
					["stackTrace"] = detail.StackTrace,
					["innerExceptions"] = inner,
				}
			);
		}

		private IDictionary<string, object> Summary(DebugDiagnosticSummary summary)
		{
			return new Dictionary<string, object>
			{
				["diagnosticId"] = Token(summary.DiagnosticId),
				["severity"] = summary.Severity.ToString(),
				["feature"] = summary.Feature.ToString(),
				["phase"] = summary.Phase ?? string.Empty,
				["code"] = summary.Code ?? string.Empty,
				["message"] = summary.Message ?? string.Empty,
				["identity"] = summary.Identity ?? string.Empty,
				["executionId"] = summary.ExecutionId ?? string.Empty,
				["occurredAtUtc"] = summary.OccurredAtUtc.ToString("o", CultureInfo.InvariantCulture),
			};
		}

		/// <summary>
		/// Names a diagnostic for a caller that cannot hold one. The run is settled first: reading the epoch is what drops
		/// what an earlier run handed out, and an answer reads it at the end - so leaving it until then would throw away
		/// the token just written down.
		/// </summary>
		private string Token(DebugDiagnosticId id)
		{
			Epoch();
			var token = id.ToString();
			m_Diagnostics[token] = id;
			return token;
		}

		private static DebugDiagnosticQuery DiagnosticQuery(string sources, string features, string severity, int offset, int count)
		{
			var source = Sources(sources);
			var feature = Features(features);
			DebugDiagnosticSeverity? level = null;
			if (!string.IsNullOrWhiteSpace(severity))
			{
				if (
					!Enum.TryParse<DebugDiagnosticSeverity>(severity, true, out var parsed)
					|| !Enum.IsDefined(typeof(DebugDiagnosticSeverity), parsed)
				)
					throw new RefusedException(DexcRemoteReason.InvalidParams, "\"" + severity + "\" is not a severity.");
				level = parsed;
			}
			try
			{
				return new DebugDiagnosticQuery(source, feature, level, new DebugPageRequest(offset, count));
			}
			catch (ArgumentOutOfRangeException exception)
			{
				throw new RefusedException(DexcRemoteReason.PageSizeExceeded, exception.Message);
			}
		}

		/// <summary>
		/// A filter is read before the query is built, because the query reports a filter it does not understand the same
		/// way it reports a page that is too large, and those are not the same failure.
		/// </summary>
		private static DebugDiagnosticSourceFilter Sources(string text)
		{
			if (string.IsNullOrWhiteSpace(text))
				return DebugDiagnosticSourceFilter.All;
			if (!Enum.TryParse<DebugDiagnosticSourceFilter>(text, true, out var parsed) || (parsed & ~DebugDiagnosticSourceFilter.All) != 0)
				throw new RefusedException(DexcRemoteReason.InvalidParams, "\"" + text + "\" is not a diagnostic source.");
			return parsed;
		}

		private static DebugFeatureFilter Features(string text)
		{
			if (string.IsNullOrWhiteSpace(text))
				return DebugFeatureFilter.All;
			if (!Enum.TryParse<DebugFeatureFilter>(text, true, out var parsed) || (parsed & ~DebugFeatureFilter.All) != 0)
				throw new RefusedException(DexcRemoteReason.InvalidParams, "\"" + text + "\" is not a feature.");
			return parsed;
		}

		/// <summary>
		/// A number that goes up when the session is replaced. The session's own id stays inside: a caller only needs to
		/// know that what it was holding is no longer about the same run.
		/// </summary>
		private long Epoch()
		{
			var current = DexcDebugger.IsInitialized ? CurrentSessionText() : string.Empty;
			if (!string.Equals(current, m_KnownSession, StringComparison.Ordinal))
			{
				m_KnownSession = current;
				m_Epoch++;
				m_Diagnostics.Clear();
			}
			return m_Epoch;
		}

		private static string CurrentSessionText()
		{
			return DebugCommand
				.Registry.GetList(new DebugListQuery(string.Empty, string.Empty, new DebugPageRequest(0, 1)))
				.Summary.SessionId.ToString();
		}

		/// <summary>
		/// Whether a session exists is the game's business, not a failure of the request, so the answer says so as a
		/// status rather than as an error.
		/// </summary>
		private bool TryRequireSession(out IDictionary<string, object> unavailable)
		{
			unavailable = null;
			if (!DexcDebugger.IsEnabled)
			{
				unavailable = Status("disabled", "Dexc is not compiled into this build.");
				return false;
			}
			if (!DexcDebugger.IsInitialized)
			{
				unavailable = Status("disabled", "No debug session is running.");
				return false;
			}
			return true;
		}

		/// <summary>
		/// The boot menu the game is waiting on: its set, and each item with its value and what it accepts. A boot menu
		/// can be open before the game starts a session, so this needs Dexc compiled in and nothing more.
		/// </summary>
		internal IDictionary<string, object> BootRead()
		{
			if (!TryRequireBootMenu(out var unavailable))
				return unavailable;
			var groups = new List<object>();
			foreach (var panel in DebugBootMenu.Panels)
			{
				var items = new List<object>();
				foreach (var widget in panel.Widgets)
					items.Add(BootItem(widget));
				groups.Add(new Dictionary<string, object> { ["title"] = panel.Title, ["items"] = items });
			}
			return Succeeded(
				new Dictionary<string, object>
				{
					["tag"] = DebugBootMenu.Data.Selection.Tag,
					["title"] = DebugBootMenu.Title,
					["groups"] = groups,
				}
			);
		}

		/// <summary>
		/// Sets the given items and starts the game, as pressing Start would. Items are named "group/item" as
		/// <see cref="BootRead"/> lists them. Every value is checked before any is written, so a request with one bad
		/// value changes nothing and leaves the menu waiting.
		/// </summary>
		internal IDictionary<string, object> BootStart(IDictionary<string, object> values)
		{
			if (!TryRequireBootMenu(out var unavailable))
				return unavailable;
			var assignments = new List<Action>();
			if (values != null)
			{
				foreach (var pair in values)
				{
					var widget = FindBootItem(pair.Key);
					if (widget == null)
					{
						return Status(
							"not-found",
							"No boot item is at \"" + pair.Key + "\". Name it as group/item, the way boot.read lists it."
						);
					}
					if (!TryBootAssignment(widget, pair.Value, out var assign, out var problem))
						return Status("rejected", "\"" + pair.Key + "\" " + problem);
					assignments.Add(assign);
				}
			}
			foreach (var assign in assignments)
				assign();
			DebugBootMenu.Start();
			return Succeeded(new Dictionary<string, object>());
		}

		private bool TryRequireBootMenu(out IDictionary<string, object> unavailable)
		{
			unavailable = null;
			if (!DexcDebugger.IsEnabled)
			{
				unavailable = Status("disabled", "Dexc is not compiled into this build.");
				return false;
			}
			if (!DebugBootMenu.IsOpen)
			{
				unavailable = Status("not-found", "No boot menu is open.");
				return false;
			}
			return true;
		}

		private static IDictionary<string, object> BootItem(DebugWidget widget)
		{
			var item = new Dictionary<string, object>
			{
				["label"] = widget.Label,
				["description"] = widget.Description ?? string.Empty,
			};
			switch (widget)
			{
				case DebugToggleWidget toggle:
					item["type"] = "bool";
					item["value"] = toggle.Value;
					break;
				case DebugChoiceWidget choice:
					item["type"] = "choice";
					item["value"] = choice.Selected;
					item["options"] = choice.Options.Cast<object>().ToList();
					break;
				case DebugNumberWidget number:
					item["type"] = number.Integer ? "integer" : "number";
					item["value"] = number.Integer ? (object)(long)number.Value : number.Value;
					// An open end is left out: JSON has no infinity.
					if (!double.IsInfinity(number.Minimum))
						item["minimum"] = number.Minimum;
					if (!double.IsInfinity(number.Maximum))
						item["maximum"] = number.Maximum;
					break;
				case DebugTextWidget text:
					item["type"] = "text";
					item["value"] = text.Value;
					break;
			}
			return item;
		}

		private static DebugWidget FindBootItem(string key)
		{
			var slash = key.IndexOf('/');
			if (slash < 0)
				return null;
			var group = key.Substring(0, slash);
			var label = key.Substring(slash + 1);
			var panel = DebugBootMenu.Panels.FirstOrDefault(candidate => candidate.Title == group);
			return panel?.Widgets.FirstOrDefault(widget => widget.Label == label);
		}

		/// <summary>Checks one value against its item and returns how to write it. Nothing is written here.</summary>
		private static bool TryBootAssignment(DebugWidget widget, object value, out Action assign, out string problem)
		{
			assign = null;
			problem = null;
			switch (widget)
			{
				case DebugToggleWidget toggle:
					if (!(value is bool flag))
					{
						problem = "takes true or false.";
						return false;
					}
					assign = () => toggle.Value = flag;
					return true;
				case DebugChoiceWidget choice:
					var index = value is string name ? ((IList<string>)choice.Options).IndexOf(name) : -1;
					if (index < 0)
					{
						problem = "takes one of: " + string.Join(", ", choice.Options) + ".";
						return false;
					}
					assign = () => choice.Index = index;
					return true;
				case DebugNumberWidget number:
					if (!TryNumber(value, out var amount) || amount < number.Minimum || amount > number.Maximum)
					{
						problem = "takes a number from " + number.Minimum.ToString(CultureInfo.InvariantCulture)
							+ " to " + number.Maximum.ToString(CultureInfo.InvariantCulture) + ".";
						return false;
					}
					if (number.Integer && Math.Floor(amount) != amount)
					{
						problem = "takes a whole number.";
						return false;
					}
					assign = () => number.Value = amount;
					return true;
				case DebugTextWidget text:
					if (!(value is string words))
					{
						problem = "takes text.";
						return false;
					}
					assign = () => text.Value = words;
					return true;
				default:
					problem = "cannot be set.";
					return false;
			}
		}

		private static bool TryNumber(object value, out double number)
		{
			switch (value)
			{
				case long signed:
					number = signed;
					return true;
				case ulong unsigned:
					number = unsigned;
					return true;
				case double floating:
					number = floating;
					return true;
				default:
					number = 0d;
					return false;
			}
		}

		private static void RequirePath(string path)
		{
			if (string.IsNullOrEmpty(path))
				throw new RefusedException(DexcRemoteReason.InvalidParams, "This operation needs a path.");
		}

		/// <summary>
		/// A page larger than the session allows is refused, not quietly cut down to size. The limit is checked both when
		/// the request is built and again when it is read, so both have to be caught.
		/// </summary>
		private static T Paged<T>(Func<T> read)
		{
			try
			{
				return read();
			}
			catch (ArgumentOutOfRangeException exception)
			{
				throw new RefusedException(DexcRemoteReason.PageSizeExceeded, exception.Message);
			}
		}

		private static DebugPageRequest PageRequest(int offset, int count)
		{
			try
			{
				return new DebugPageRequest(offset, count);
			}
			catch (ArgumentOutOfRangeException exception)
			{
				throw new RefusedException(DexcRemoteReason.PageSizeExceeded, exception.Message);
			}
		}

		private static DebugListQuery Query(string pathPrefix, string searchText, int offset, int count)
		{
			try
			{
				return new DebugListQuery(pathPrefix ?? string.Empty, searchText ?? string.Empty, new DebugPageRequest(offset, count));
			}
			catch (ArgumentOutOfRangeException exception)
			{
				throw new RefusedException(DexcRemoteReason.PageSizeExceeded, exception.Message);
			}
		}

		// The lists are prefix-filtered and sorted by Order, so the exact path can sit behind other matches. The host does
		// not know the session's page limit, and one item is the size every session accepts; the walk stops at the match.
		private const int FindPageSize = 1;

		private static bool TryFindCommand(string path, out DebugCommandDescriptor descriptor)
		{
			descriptor = null;
			var offset = 0;
			while (true)
			{
				var request = PageRequest(offset, FindPageSize);
				var page = Paged(() => DebugCommand.Registry.GetList(new DebugListQuery(path, string.Empty, request)));
				foreach (var item in page.Items)
				{
					if (string.Equals(item.Path, path, StringComparison.Ordinal))
						return DebugCommand.Registry.TryGetDescriptor(item.Id, out descriptor);
				}
				if (!page.Page.HasMore || page.Items.Count == 0)
					return false;
				offset += page.Items.Count;
			}
		}

		private static bool TryFindSetting(string path, out DebugSettingDescriptor descriptor)
		{
			descriptor = null;
			var offset = 0;
			while (true)
			{
				var request = PageRequest(offset, FindPageSize);
				var page = Paged(() => DebugSetting.GetList(new DebugListQuery(path, string.Empty, request)));
				foreach (var item in page.Items)
				{
					if (string.Equals(item.Path, path, StringComparison.Ordinal))
						return DebugSetting.TryGetDescriptor(item.Id, out descriptor);
				}
				if (!page.Page.HasMore || page.Items.Count == 0)
					return false;
				offset += page.Items.Count;
			}
		}

		private IDictionary<string, object> Succeeded(IDictionary<string, object> members)
		{
			var answer = new Dictionary<string, object> { ["status"] = "succeeded", ["session"] = SessionStatus() };
			foreach (var pair in members)
				answer[pair.Key] = pair.Value;
			return answer;
		}

		private IDictionary<string, object> Status(string status, string message)
		{
			return new Dictionary<string, object>
			{
				["status"] = status,
				["session"] = SessionStatus(),
				["message"] = message,
			};
		}

		private static string StatusText(DebugOperationStatus status)
		{
			switch (status)
			{
				case DebugOperationStatus.Succeeded:
					return "succeeded";
				case DebugOperationStatus.Rejected:
					return "rejected";
				case DebugOperationStatus.ReadOnly:
					return "read-only";
				case DebugOperationStatus.ExplicitAccessRequired:
					return "explicit-access-required";
				case DebugOperationStatus.Stale:
					return "stale";
				case DebugOperationStatus.NotFound:
					return "not-found";
				case DebugOperationStatus.DefinitionUnavailable:
					return "definition-unavailable";
				case DebugOperationStatus.Disabled:
					return "disabled";
				default:
					return "failed";
			}
		}

		private static IDictionary<string, object> Summary(DebugCollectionSummary summary)
		{
			return new Dictionary<string, object>
			{
				["state"] = summary.State.ToString(),
				["descriptorCount"] = (long)summary.DescriptorCount,
				["excludedDefinitionCount"] = (long)summary.ExcludedDefinitionCount,
				["errorCount"] = (long)summary.ErrorCount,
				["warningCount"] = (long)summary.WarningCount,
			};
		}

		private static IDictionary<string, object> Page(DebugPageInfo page)
		{
			return new Dictionary<string, object>
			{
				["offset"] = (long)page.Offset,
				["count"] = (long)page.Count,
				["hasMore"] = page.HasMore,
				["isTotalCountKnown"] = page.IsTotalCountKnown,
				["totalCount"] = (long)page.TotalCount,
				["isTruncated"] = page.IsTruncated,
			};
		}
	}
}
