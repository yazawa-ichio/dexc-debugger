using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.Scripting;

namespace Dexc
{
	[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
	[Conditional("DEXC_DEBUGGER")]
	public sealed class DebugCommandAttribute : PreserveAttribute
	{
		public DebugCommandAttribute(string path)
		{
			ValidatePath(path);
			Path = path;
			Description = string.Empty;
			ConfirmationMessage = string.Empty;
			Tags = Array.Empty<string>();
		}

		public string Path { get; }
		public string Description { get; set; }
		public string ConfirmationMessage { get; set; }
		public DebugCommandAccess Access { get; set; }
		public int Order { get; set; }
		public string[] Tags { get; set; }

		internal void ValidateDefinition()
		{
			if (Description == null || ConfirmationMessage == null || Tags == null)
				throw new ArgumentException("Command metadata must not be null.");
			foreach (var tag in Tags)
				if (string.IsNullOrWhiteSpace(tag))
					throw new ArgumentException("Command tags must not be empty.");
			if (!Enum.IsDefined(typeof(DebugCommandAccess), Access))
				throw new ArgumentOutOfRangeException(nameof(Access));
		}

		internal static void ValidatePath(string path)
		{
			if (
				string.IsNullOrWhiteSpace(path)
				|| path.Split('/').Any(segment => string.IsNullOrWhiteSpace(segment) || segment.Trim() != segment)
			)
				throw new ArgumentException("Command path must contain non-empty, trimmed segments.", nameof(path));
		}
	}

	public sealed class DebugInputChanges
	{
		private readonly IReadOnlyList<IDebugInput> m_Inputs;

		internal DebugInputChanges(IEnumerable<IDebugInput> inputs)
		{
			var copy = (inputs ?? throw new ArgumentNullException(nameof(inputs))).ToList();
			if (copy.Any(input => input == null))
				throw new ArgumentException("Changed inputs must not be null.", nameof(inputs));
			m_Inputs = new ReadOnlyCollection<IDebugInput>(copy);
		}

		public int Count
		{
			get { return m_Inputs.Count; }
		}

		public bool Contains(IDebugInput input)
		{
			return input != null && m_Inputs.Contains(input);
		}
	}

	public sealed class DebugCommandValidationIssue
	{
		internal DebugCommandValidationIssue(DebugValidationIssue issue, DebugCommandFieldId fieldId, bool unavailable)
		{
			Issue = issue ?? throw new ArgumentNullException(nameof(issue));
			FieldId = fieldId;
			HasField = fieldId.IsValid;
			IsUnavailable = unavailable;
		}

		public DebugValidationIssue Issue { get; }
		public DebugCommandFieldId FieldId { get; }
		public bool HasField { get; }
		public bool IsUnavailable { get; }
		public DebugValidationSeverity Severity
		{
			get { return Issue.Severity; }
		}
		public string Code
		{
			get { return Issue.Code; }
		}
		public string Message
		{
			get { return Issue.Message; }
		}
	}

	public sealed class DebugCommandValidationResult
	{
		internal DebugCommandValidationResult(IEnumerable<DebugCommandValidationIssue> issues)
		{
			Issues = new ReadOnlyCollection<DebugCommandValidationIssue>(
				(issues ?? Enumerable.Empty<DebugCommandValidationIssue>()).ToList()
			);
			CanExecute = !Issues.Any(issue => issue.Severity == DebugValidationSeverity.Error);
			HasWarnings = Issues.Any(issue => issue.Severity == DebugValidationSeverity.Warning);
		}

		public bool CanExecute { get; }
		public bool HasWarnings { get; }
		public IReadOnlyList<DebugCommandValidationIssue> Issues { get; }
	}

	public sealed class DebugCommandValidation
	{
		private readonly List<DebugCommandValidationIssue> m_Issues = new List<DebugCommandValidationIssue>();

		internal DebugCommandValidation(Func<IDebugInput, DebugCommandFieldId> fieldResolver)
		{
			m_FieldResolver = fieldResolver ?? throw new ArgumentNullException(nameof(fieldResolver));
		}

		private readonly Func<IDebugInput, DebugCommandFieldId> m_FieldResolver;
		internal IReadOnlyList<DebugCommandValidationIssue> Issues
		{
			get { return m_Issues; }
		}

		public void Error(string code, string message)
		{
			Add(null, code, message, false, DebugValidationSeverity.Error);
		}

		public void Warning(string code, string message)
		{
			Add(null, code, message, false, DebugValidationSeverity.Warning);
		}

		public void Unavailable(string code, string message)
		{
			Add(null, code, message, true, DebugValidationSeverity.Error);
		}

		public void Error(IDebugInput field, string code, string message)
		{
			Add(field, code, message, false, DebugValidationSeverity.Error);
		}

		public void Warning(IDebugInput field, string code, string message)
		{
			Add(field, code, message, false, DebugValidationSeverity.Warning);
		}

		public void Require(bool condition, string code, string message)
		{
			if (!condition)
				Error(code, message);
		}

		public void Require(bool condition, IDebugInput field, string code, string message)
		{
			if (!condition)
				Error(field, code, message);
		}

		private void Add(IDebugInput field, string code, string message, bool unavailable, DebugValidationSeverity severity)
		{
			var issue = new DebugValidationIssue(severity, code, message);
			var fieldId = default(DebugCommandFieldId);
			if (field != null)
			{
				fieldId = m_FieldResolver(field);
				if (!fieldId.IsValid)
					throw new InvalidOperationException("The validation field does not belong to the command.");
			}
			m_Issues.Add(new DebugCommandValidationIssue(issue, fieldId, unavailable));
		}
	}

	internal interface IDexcCommandRuntimeMarker { }

	public abstract class DebugCommand : IDexcCommandRuntimeMarker
	{
		public static class Registry
		{
			public static DebugCommandListPage GetList(DebugListQuery query)
			{
#if DEXC_DEBUGGER
				return DexcCommandRuntime.Registry.GetList(query);
#else
				return new DebugCommandListPage(DexcDisabledRuntime.CollectionSummary, DexcDisabledRuntime.EmptyPage(query.Page), null);
#endif
			}

			public static bool TryGetDescriptor(DebugCommandId commandId, out DebugCommandDescriptor descriptor)
			{
#if DEXC_DEBUGGER
				return DexcCommandRuntime.Registry.TryGetDescriptor(commandId, out descriptor);
#else
				descriptor = null;
				return false;
#endif
			}
		}

		public static class Draft
		{
			public static DebugCommandDraftOperationResult Create(DebugCommandId commandId)
			{
#if DEXC_DEBUGGER
				return DexcCommandRuntime.Drafts.Create(commandId);
#else
				return DexcDisabledRuntime.DisabledDraft(default(DebugCommandDraftId));
#endif
			}

			public static DebugCommandDraftOperationResult UpdateInput(
				DebugCommandDraftId draftId,
				DebugCommandFieldId fieldId,
				DebugValue value
			)
			{
#if DEXC_DEBUGGER
				return DexcCommandRuntime.Drafts.UpdateInput(draftId, fieldId, value);
#else
				return DexcDisabledRuntime.DisabledDraft(draftId);
#endif
			}

			public static DebugCommandDraftOperationResult SelectReference(
				DebugCommandDraftId draftId,
				DebugCommandFieldId fieldId,
				DebugReferenceHandle reference
			)
			{
#if DEXC_DEBUGGER
				return DexcCommandRuntime.Drafts.SelectReference(draftId, fieldId, reference);
#else
				return DexcDisabledRuntime.DisabledDraft(draftId);
#endif
			}

			public static DebugCommandDraftOperationResult ClearReference(DebugCommandDraftId draftId, DebugCommandFieldId fieldId)
			{
#if DEXC_DEBUGGER
				return DexcCommandRuntime.Drafts.ClearReference(draftId, fieldId);
#else
				return DexcDisabledRuntime.DisabledDraft(draftId);
#endif
			}

			public static DebugCommandReferenceListPage GetReferenceEntries(
				DebugCommandDraftId draftId,
				DebugCommandFieldId fieldId,
				DebugListQuery query
			)
			{
#if DEXC_DEBUGGER
				return DexcCommandRuntime.Drafts.GetReferenceEntries(draftId, fieldId, query);
#else
				return DexcDisabledRuntime.DisabledReferences(query.Page);
#endif
			}

			public static DebugCommandHistoryPage GetHistory(DebugCommandId commandId, DebugPageRequest page)
			{
#if DEXC_DEBUGGER
				return DexcCommandRuntime.History.GetHistory(commandId, page);
#else
				return new DebugCommandHistoryPage(DexcDisabledRuntime.EmptyPage(page), null);
#endif
			}

			public static DebugCommandDraftOperationResult Validate(DebugCommandDraftId draftId)
			{
#if DEXC_DEBUGGER
				return DexcCommandRuntime.Drafts.Validate(draftId);
#else
				return DexcDisabledRuntime.DisabledDraft(draftId);
#endif
			}

			public static DebugCommandRequestCreationResult CreateRequest(DebugCommandDraftId draftId)
			{
#if DEXC_DEBUGGER
				return DexcCommandRuntime.Drafts.CreateRequest(draftId);
#else
				return DexcDisabledRuntime.DisabledRequest(draftId);
#endif
			}

			public static void Discard(DebugCommandDraftId draftId)
			{
#if DEXC_DEBUGGER
				DexcCommandRuntime.Drafts.Discard(draftId);
#endif
			}

			public static DebugCommandDraftOperationResult CreateFromHistory(DebugHistoryId historyId)
			{
#if DEXC_DEBUGGER
				return DexcCommandRuntime.Drafts.CreateFromHistory(historyId);
#else
				return DexcDisabledRuntime.DisabledDraft(default(DebugCommandDraftId));
#endif
			}

			public static DebugCommandValidationResult Validate(DebugCommand command)
			{
#if DEXC_DEBUGGER
				return DexcCommandRuntime.ValidateTyped(command);
#else
				return DexcDisabledRuntime.DisabledTypedValidation(command);
#endif
			}

			public static DebugCommandValidationResult Validate<TOutput>(DebugCommand<TOutput> command)
			{
#if DEXC_DEBUGGER
				return DexcCommandRuntime.ValidateTyped(command);
#else
				return DexcDisabledRuntime.DisabledTypedValidation(command);
#endif
			}

			public static DebugCommandValidationResult Validate(AsyncDebugCommand command)
			{
#if DEXC_DEBUGGER
				return DexcCommandRuntime.ValidateTyped(command);
#else
				return DexcDisabledRuntime.DisabledTypedValidation(command);
#endif
			}

			public static DebugCommandValidationResult Validate<TOutput>(AsyncDebugCommand<TOutput> command)
			{
#if DEXC_DEBUGGER
				return DexcCommandRuntime.ValidateTyped(command);
#else
				return DexcDisabledRuntime.DisabledTypedValidation(command);
#endif
			}

			public static DebugCommandRequest CreateRequest(DebugCommand command)
			{
#if DEXC_DEBUGGER
				return DexcCommandRuntime.CreateTypedRequest(command);
#else
				throw new DebugCommandValidationException(DexcDisabledRuntime.DisabledTypedValidation(command));
#endif
			}

			public static DebugCommandRequest<TOutput> CreateRequest<TOutput>(DebugCommand<TOutput> command)
			{
#if DEXC_DEBUGGER
				return DexcCommandRuntime.CreateTypedRequest(command);
#else
				throw new DebugCommandValidationException(DexcDisabledRuntime.DisabledTypedValidation(command));
#endif
			}

			public static AsyncDebugCommandRequest CreateRequest(AsyncDebugCommand command)
			{
#if DEXC_DEBUGGER
				return (AsyncDebugCommandRequest)DexcCommandRuntime.CreateTypedRequest(command);
#else
				throw new DebugCommandValidationException(DexcDisabledRuntime.DisabledTypedValidation(command));
#endif
			}

			public static AsyncDebugCommandRequest<TOutput> CreateRequest<TOutput>(AsyncDebugCommand<TOutput> command)
			{
#if DEXC_DEBUGGER
				return DexcCommandRuntime.CreateTypedRequest(command);
#else
				throw new DebugCommandValidationException(DexcDisabledRuntime.DisabledTypedValidation(command));
#endif
			}

			[Conditional("DEXC_DEBUGGER")]
			public static void NotifyInputsChanged(DebugCommand command, params IDebugInput[] inputs)
			{
#if DEXC_DEBUGGER
				DexcCommandRuntime.Notify(command, inputs);
#endif
			}

			[Conditional("DEXC_DEBUGGER")]
			public static void NotifyInputsChanged<TOutput>(DebugCommand<TOutput> command, params IDebugInput[] inputs)
			{
#if DEXC_DEBUGGER
				DexcCommandRuntime.Notify(command, inputs);
#endif
			}

			[Conditional("DEXC_DEBUGGER")]
			public static void NotifyInputsChanged(AsyncDebugCommand command, params IDebugInput[] inputs)
			{
#if DEXC_DEBUGGER
				DexcCommandRuntime.Notify(command, inputs);
#endif
			}

			[Conditional("DEXC_DEBUGGER")]
			public static void NotifyInputsChanged<TOutput>(AsyncDebugCommand<TOutput> command, params IDebugInput[] inputs)
			{
#if DEXC_DEBUGGER
				DexcCommandRuntime.Notify(command, inputs);
#endif
			}
		}

		protected virtual void OnInputsChanged(DebugInputChanges changes) { }

		protected virtual void Validate(DebugCommandValidation validation) { }

		protected abstract void Execute();

		[System.Diagnostics.CodeAnalysis.DoesNotReturn]
		protected void Fail(string code, string message)
		{
			throw new DexcExpectedFailureException(code, message);
		}
	}

	public abstract class DebugCommand<TOutput> : IDexcCommandRuntimeMarker
	{
		protected virtual void OnInputsChanged(DebugInputChanges changes) { }

		protected virtual void Validate(DebugCommandValidation validation) { }

		[System.Diagnostics.CodeAnalysis.DoesNotReturn]
		protected void Fail(string code, string message)
		{
			throw new DexcExpectedFailureException(code, message);
		}

		protected abstract TOutput Execute();
	}

	public abstract class AsyncDebugCommand : IDexcCommandRuntimeMarker
	{
		protected virtual void OnInputsChanged(DebugInputChanges changes) { }

		protected virtual void Validate(DebugCommandValidation validation) { }

		[System.Diagnostics.CodeAnalysis.DoesNotReturn]
		protected void Fail(string code, string message)
		{
			throw new DexcExpectedFailureException(code, message);
		}

		protected virtual TimeSpan Timeout
		{
			get { return TimeSpan.FromSeconds(5); }
		}
		protected abstract ValueTask ExecuteAsync(CancellationToken cancellationToken);
	}

	public abstract class AsyncDebugCommand<TOutput> : IDexcCommandRuntimeMarker
	{
		protected virtual void OnInputsChanged(DebugInputChanges changes) { }

		protected virtual void Validate(DebugCommandValidation validation) { }

		[System.Diagnostics.CodeAnalysis.DoesNotReturn]
		protected void Fail(string code, string message)
		{
			throw new DexcExpectedFailureException(code, message);
		}

		protected virtual TimeSpan Timeout
		{
			get { return TimeSpan.FromSeconds(5); }
		}
		protected abstract ValueTask<TOutput> ExecuteAsync(CancellationToken cancellationToken);
	}

	public sealed class DebugCommandFieldDescriptor
	{
		internal DebugCommandFieldDescriptor(
			DebugCommandFieldId id,
			string name,
			string label,
			bool isInput,
			DebugInputSchema schema,
			Type inputType,
			DebugRangeAttribute range = null,
			bool isSlider = false,
			bool isMultiline = false
		)
		{
			Id = id;
			Name = name;
			Label = label;
			IsInput = isInput;
			Schema = schema;
			InputType = inputType;
			InputTypeName = inputType == null ? string.Empty : inputType.FullName;
			HasRange = range != null;
			RangeMinimum = range == null ? 0d : range.Minimum;
			RangeMaximum = range == null ? 0d : range.Maximum;
			IsSlider = isSlider;
			IsMultiline = isMultiline;
		}

		public DebugCommandFieldId Id { get; }
		public string Name { get; }
		public string Label { get; }
		public bool IsInput { get; }
		public DebugInputSchema Schema { get; }
		public Type InputType { get; }
		public string InputTypeName { get; }

		/// <summary>The field carries <see cref="DebugRangeAttribute"/>; values outside it fail validation.</summary>
		public bool HasRange { get; }
		public double RangeMinimum { get; }
		public double RangeMaximum { get; }

		/// <summary>The field carries <see cref="DebugSliderAttribute"/>.</summary>
		public bool IsSlider { get; }

		/// <summary>The field carries <see cref="DebugMultilineAttribute"/>.</summary>
		public bool IsMultiline { get; }

		/// <summary>Adds an error when <paramref name="value"/> is a number outside the field's range.</summary>
		internal void ValidateRange(DebugValue value, DebugValidation validation)
		{
			if (!HasRange || !TryGetNumber(value, out var number))
				return;
			var single = InputType == typeof(InputFloat);
			if (DebugRangeAttribute.IsBelow(number, RangeMinimum, single))
				validation.Error("constraint-minimum", "The value is below the range minimum.");
			else if (DebugRangeAttribute.IsAbove(number, RangeMaximum, single))
				validation.Error("constraint-maximum", "The value is above the range maximum.");
		}

		internal static bool TryGetNumber(DebugValue value, out double number)
		{
			number = 0d;
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
	}

	public sealed class DebugCommandListItem
	{
		internal DebugCommandListItem(DebugCommandDescriptor descriptor)
		{
			Id = descriptor.Id;
			Path = descriptor.Path;
			DisplayName = descriptor.Path.Split('/').Last();
			Description = descriptor.Description;
			Order = descriptor.Order;
			Tags = descriptor.Tags;
			IsAsync = descriptor.IsAsync;
			HasOutput = descriptor.HasOutput;
			HasConfirmation = !string.IsNullOrEmpty(descriptor.ConfirmationMessage);
			Access = descriptor.Access;
		}

		public DebugCommandId Id { get; }
		public string Path { get; }
		public string DisplayName { get; }
		public string Description { get; }
		public int Order { get; }
		public IReadOnlyList<string> Tags { get; }
		public bool IsAsync { get; }
		public bool HasOutput { get; }
		public bool HasConfirmation { get; }
		public DebugCommandAccess Access { get; }
	}

	public sealed class DebugCommandDescriptor
	{
		internal DebugCommandDescriptor(
			DebugCommandId id,
			DebugCollectionGeneration generation,
			Type type,
			DebugCommandAttribute attribute,
			DebugCommandAccess access,
			IEnumerable<DebugCommandFieldDescriptor> fields,
			bool isAsync,
			bool hasOutput,
			TimeSpan timeout
		)
		{
			Id = id;
			CollectionGeneration = generation;
			CommandType = type;
			Path = attribute.Path;
			Description = attribute.Description;
			ConfirmationMessage = attribute.ConfirmationMessage;
			Order = attribute.Order;
			Tags = new ReadOnlyCollection<string>(attribute.Tags.ToArray());
			Access = access;
			Fields = new ReadOnlyCollection<DebugCommandFieldDescriptor>(fields.ToList());
			IsAsync = isAsync;
			HasOutput = hasOutput;
			Timeout = timeout;
		}

		public DebugCommandId Id { get; }
		public DebugCollectionGeneration CollectionGeneration { get; }
		public string Path { get; }
		public string Description { get; }
		public string ConfirmationMessage { get; }
		public int Order { get; }
		public IReadOnlyList<string> Tags { get; }
		public DebugCommandAccess Access { get; }
		public IReadOnlyList<DebugCommandFieldDescriptor> Fields { get; }
		public bool IsAsync { get; }
		public bool HasOutput { get; }
		public TimeSpan Timeout { get; }
		internal Type CommandType { get; }
	}

	public sealed class DebugCommandListPage
	{
		internal DebugCommandListPage(DebugCollectionSummary summary, DebugPageInfo page, IEnumerable<DebugCommandListItem> items)
		{
			Summary = summary;
			Page = page;
			Items = new ReadOnlyCollection<DebugCommandListItem>((items ?? Enumerable.Empty<DebugCommandListItem>()).ToList());
		}

		public DebugCollectionSummary Summary { get; }
		public DebugPageInfo Page { get; }
		public IReadOnlyList<DebugCommandListItem> Items { get; }
	}

	public sealed class DebugCommandReferenceEntry
	{
		internal DebugCommandReferenceEntry(DebugReferenceHandle handle, string label, string path, bool alive, string typeName)
		{
			Handle = handle;
			Label = label;
			Path = path;
			Alive = alive;
			ValueTypeName = typeName;
		}

		public DebugReferenceHandle Handle { get; }
		public string Label { get; }
		public string Path { get; }
		public bool Alive { get; }
		public string ValueTypeName { get; }
	}

	public sealed class DebugCommandReferenceListPage
	{
		internal DebugCommandReferenceListPage(
			DebugOperationStatus status,
			DebugPageInfo page,
			IEnumerable<DebugCommandReferenceEntry> entries,
			DebugDiagnosticId diagnosticId
		)
		{
			Status = status;
			Page = page;
			Entries = new ReadOnlyCollection<DebugCommandReferenceEntry>(
				(entries ?? Enumerable.Empty<DebugCommandReferenceEntry>()).ToList()
			);
			DiagnosticId = diagnosticId;
		}

		public DebugOperationStatus Status { get; }
		public DebugPageInfo Page { get; }
		public IReadOnlyList<DebugCommandReferenceEntry> Entries { get; }
		public DebugDiagnosticId DiagnosticId { get; }
	}

	public enum DebugCommandDraftOperationStatus
	{
		Succeeded,
		Rejected,
		Stale,
		NotFound,
		DefinitionUnavailable,
		Disabled,
	}

	public readonly struct DebugCommandDraftOperationResult
	{
		internal DebugCommandDraftOperationResult(
			DebugCommandDraftOperationStatus status,
			DebugCommandDraftId draftId,
			DebugCommandValidationResult validation,
			DebugDiagnosticId diagnosticId
		)
		{
			Status = status;
			m_DraftId = draftId;
			Validation = validation;
			DiagnosticId = diagnosticId;
		}

		private readonly DebugCommandDraftId m_DraftId;
		public DebugCommandDraftOperationStatus Status { get; }
		public DebugDiagnosticId DiagnosticId { get; }
		public DebugCommandValidationResult Validation { get; }

		public bool TryGetDraftId(out DebugCommandDraftId draftId)
		{
			draftId = m_DraftId;
			return Status == DebugCommandDraftOperationStatus.Succeeded && draftId.IsValid;
		}

		public bool TryGetValidation(out DebugCommandValidationResult validation)
		{
			validation = Validation;
			return validation != null;
		}
	}

	public enum DebugCommandRequestCreationStatus
	{
		Created,
		ValidationFailed,
		Stale,
		DefinitionUnavailable,
		Disabled,
	}

	public readonly struct DebugCommandRequestCreationResult
	{
		internal DebugCommandRequestCreationResult(
			DebugCommandRequestCreationStatus status,
			DebugCommandRequest request,
			DebugCommandValidationResult validation,
			DebugDiagnosticId diagnosticId
		)
		{
			Status = status;
			m_Request = request;
			Validation = validation;
			DiagnosticId = diagnosticId;
		}

		private readonly DebugCommandRequest m_Request;
		public DebugCommandRequestCreationStatus Status { get; }
		public DebugCommandValidationResult Validation { get; }
		public DebugDiagnosticId DiagnosticId { get; }

		public bool TryGetRequest(out DebugCommandRequest request)
		{
			request = m_Request;
			return Status == DebugCommandRequestCreationStatus.Created && request != null;
		}
	}

	public class DebugCommandRequest
	{
		internal DebugCommandRequest(
			DexcCommandDescriptor descriptor,
			IReadOnlyDictionary<DebugCommandFieldId, DebugValue> values,
			long draftVersion,
			IReadOnlyDictionary<DebugCommandFieldId, DebugReferenceHandle> references = null
		)
		{
			Descriptor = descriptor;
			Values = values;
			References = references;
			DraftVersion = draftVersion;
			CreatedAtUtc = DateTimeOffset.UtcNow;
		}

		internal DexcCommandDescriptor Descriptor { get; }
		internal IReadOnlyDictionary<DebugCommandFieldId, DebugReferenceHandle> References { get; }
		public DebugCommandId CommandId
		{
			get { return Descriptor.Public.Id; }
		}
		public string Path
		{
			get { return Descriptor.Public.Path; }
		}
		public DebugSessionId SessionId
		{
			get { return Descriptor.SessionId; }
		}
		public DebugCollectionGeneration CollectionGeneration
		{
			get { return Descriptor.Public.CollectionGeneration; }
		}
		public DebugCommandAccess Access
		{
			get { return Descriptor.Public.Access; }
		}
		public IReadOnlyDictionary<DebugCommandFieldId, DebugValue> Values { get; }
		public long DraftVersion { get; }
		public DateTimeOffset CreatedAtUtc { get; }
	}

	public class AsyncDebugCommandRequest : DebugCommandRequest
	{
		internal AsyncDebugCommandRequest(
			DexcCommandDescriptor descriptor,
			IReadOnlyDictionary<DebugCommandFieldId, DebugValue> values,
			long draftVersion,
			IReadOnlyDictionary<DebugCommandFieldId, DebugReferenceHandle> references = null
		)
			: base(descriptor, values, draftVersion, references) { }
	}

	public sealed class DebugCommandRequest<TOutput> : DebugCommandRequest
	{
		internal DebugCommandRequest(
			DexcCommandDescriptor descriptor,
			IReadOnlyDictionary<DebugCommandFieldId, DebugValue> values,
			long draftVersion,
			IReadOnlyDictionary<DebugCommandFieldId, DebugReferenceHandle> references = null
		)
			: base(descriptor, values, draftVersion, references) { }
	}

	public sealed class AsyncDebugCommandRequest<TOutput> : AsyncDebugCommandRequest
	{
		internal AsyncDebugCommandRequest(
			DexcCommandDescriptor descriptor,
			IReadOnlyDictionary<DebugCommandFieldId, DebugValue> values,
			long draftVersion,
			IReadOnlyDictionary<DebugCommandFieldId, DebugReferenceHandle> references = null
		)
			: base(descriptor, values, draftVersion, references) { }
	}

	public enum CommandExecutionStatus
	{
		Succeeded,
		Failed,
		Cancelled,
		TimedOut,
	}

	public enum CommandFailureKind
	{
		None,
		Expected,
		Validation,
		Unavailable,
		MissingReference,
		UnhandledException,
		Internal,
	}

	public class CommandExecutionResult
	{
		internal CommandExecutionResult(
			string executionId,
			string path,
			CommandExecutionStatus status,
			CommandFailureKind failureKind,
			string code,
			string message,
			DebugDiagnosticId diagnosticId,
			DateTimeOffset started,
			TimeSpan duration,
			DebugValue output,
			string outputTypeName = ""
		)
		{
			ExecutionId = executionId;
			CommandPath = path;
			Status = status;
			FailureKind = failureKind;
			ErrorCode = code ?? string.Empty;
			Message = message ?? string.Empty;
			DiagnosticId = diagnosticId;
			StartedAtUtc = started;
			Duration = duration;
			Output = output;
			OutputTypeName = outputTypeName ?? string.Empty;
		}

		public string ExecutionId { get; }
		public string CommandPath { get; }
		public CommandExecutionStatus Status { get; }
		public CommandFailureKind FailureKind { get; }
		public string ErrorCode { get; }
		public string Message { get; }
		public DebugDiagnosticId DiagnosticId { get; }
		public DateTimeOffset StartedAtUtc { get; }
		public TimeSpan Duration { get; }
		public DebugValue Output { get; }
		public string OutputTypeName { get; }
	}

	public sealed class CommandExecutionResult<TOutput> : CommandExecutionResult
	{
		private readonly TOutput m_Output;
		private readonly bool m_HasOutput;

		internal CommandExecutionResult(CommandExecutionResult result, TOutput output, bool hasOutput)
			: base(
				result.ExecutionId,
				result.CommandPath,
				result.Status,
				result.FailureKind,
				result.ErrorCode,
				result.Message,
				result.DiagnosticId,
				result.StartedAtUtc,
				result.Duration,
				result.Output,
				result.OutputTypeName
			)
		{
			m_HasOutput = hasOutput && result.Status == CommandExecutionStatus.Succeeded;
			m_Output = m_HasOutput ? output : default(TOutput);
		}

		public bool TryGetOutput(out TOutput output)
		{
			output = m_Output;
			return m_HasOutput;
		}
	}

	public sealed class DebugCommandValidationException : InvalidOperationException
	{
		public DebugCommandValidationException(DebugCommandValidationResult validation)
			: base("The debug command is not executable.")
		{
			Validation = validation ?? throw new ArgumentNullException(nameof(validation));
		}

		public DebugCommandValidationResult Validation { get; }
	}

	public sealed class DebugCommandExecutionException : InvalidOperationException
	{
		internal DebugCommandExecutionException(CommandExecutionResult result)
			: base(result.Message)
		{
			Result = result;
		}

		public CommandExecutionResult Result { get; }
	}

	internal sealed class DexcExpectedFailureException : Exception
	{
		internal DexcExpectedFailureException(string code, string message)
		{
			if (string.IsNullOrWhiteSpace(code))
				throw new ArgumentException("Failure code must not be empty.", nameof(code));
			if (string.IsNullOrWhiteSpace(message))
				throw new ArgumentException("Failure message must not be empty.", nameof(message));
			Code = code;
			MessageText = message;
		}

		internal string Code { get; }
		internal string MessageText { get; }
	}

	internal sealed class DexcCommandField
	{
		internal FieldInfo Field;
		internal DebugCommandFieldDescriptor Public;
		internal bool IsInput;
		internal bool IsLabel;
		internal bool IsHeader;
	}

	internal sealed class DexcCommandDescriptor
	{
		internal DexcCommandDescriptor(
			DebugCommandDescriptor publicDescriptor,
			Type type,
			DebugSessionId sessionId,
			IReadOnlyList<DexcCommandField> fields
		)
		{
			Public = publicDescriptor;
			Type = type;
			SessionId = sessionId;
			Fields = fields;
		}

		internal DebugCommandDescriptor Public { get; }
		internal Type Type { get; }
		internal DebugSessionId SessionId { get; }
		internal IReadOnlyList<DexcCommandField> Fields { get; }

		internal DexcCommandField Find(DebugCommandFieldId id)
		{
			return Fields.FirstOrDefault(field => field.Public.Id == id);
		}
	}

	internal sealed class DexcCommandRegistry
	{
		private readonly Dictionary<DebugCommandId, DexcCommandDescriptor> m_ById = new Dictionary<DebugCommandId, DexcCommandDescriptor>();
		private readonly Dictionary<Type, DexcCommandDescriptor> m_ByType = new Dictionary<Type, DexcCommandDescriptor>();
		internal DebugCollectionSummary Summary { get; }

		private readonly DexcRuntimeSession m_State;

		internal DexcCommandRegistry(DexcRuntimeSession session)
		{
			m_State = session;
			// Definitions come from the assembly-lifetime cache; this session only assigns the ids and records what the
			// collection had to exclude.
			var definitions = DexcDefinitions.Commands;
			long next = 0;
			long nextField = 0;
			foreach (var definition in definitions.Definitions)
			{
				var fields = new List<DexcCommandField>();
				foreach (var field in definition.Fields)
				{
					fields.Add(
						new DexcCommandField
						{
							Field = field.Field,
							IsInput = field.IsInput,
							IsLabel = field.IsLabel,
							IsHeader = field.IsHeader,
							Public = new DebugCommandFieldDescriptor(
								DebugCommandFieldId.Create(session.SessionId, ++nextField),
								field.Field.Name,
								field.Field.Name,
								field.IsInput,
								field.Schema,
								field.Field.FieldType,
								field.Range,
								field.IsSlider,
								field.IsMultiline
							),
						}
					);
				}
				var commandId = DebugCommandId.Create(session.SessionId, ++next);
				var resolved =
					definition.Attribute.Access == DebugCommandAccess.Default ? session.Options.DefaultAccess : definition.Attribute.Access;
				var descriptor = new DebugCommandDescriptor(
					commandId,
					session.CollectionGeneration,
					definition.Type,
					definition.Attribute,
					resolved,
					fields.Select(field => field.Public),
					definition.IsAsync,
					definition.HasOutput,
					definition.Timeout
				);
				var entry = new DexcCommandDescriptor(descriptor, definition.Type, session.SessionId, fields);
				m_ById.Add(commandId, entry);
				m_ByType.Add(definition.Type, entry);
			}
			foreach (var diagnostic in definitions.Diagnostics)
			{
				session.Diagnostics.AddCollection(
					DebugDiagnosticSeverity.Error,
					DebugFeature.Command,
					"reflection-collection",
					diagnostic.Code,
					diagnostic.Message,
					diagnostic.Identity,
					session.CollectionGeneration,
					diagnostic.Exception
				);
			}
			Summary = new DebugCollectionSummary(
				DebugCollectionState.Ready,
				session.SessionId,
				session.CollectionGeneration,
				m_ById.Count,
				definitions.ExcludedCount,
				definitions.ErrorCount,
				0
			);
		}

		internal bool TryGet(DebugCommandId id, out DexcCommandDescriptor descriptor)
		{
			return m_ById.TryGetValue(id, out descriptor);
		}

		internal bool TryGet(Type type, out DexcCommandDescriptor descriptor)
		{
			return m_ByType.TryGetValue(type, out descriptor);
		}

		internal DebugCommandListPage GetList(DebugListQuery query)
		{
			DexcListQueryEvaluator.ValidatePage(query.Page, m_State.Options.MaxPageSize);
			var items = m_ById
				.Values.Select(item => new DebugCommandListItem(item.Public))
				.Where(item => DexcListQueryEvaluator.Matches(query, item.Path, item.DisplayName, item.Description, item.Tags))
				.OrderBy(item => item.Order)
				.ThenBy(item => item.Path, StringComparer.Ordinal)
				.ToList();
			var pageItems = items.Skip(query.Page.Offset).Take(query.Page.Count).ToList();
			var info = new DebugPageInfo(
				query.Page.Offset,
				pageItems.Count,
				query.Page.Offset + pageItems.Count < items.Count,
				true,
				items.Count,
				false
			);
			return new DebugCommandListPage(Summary, info, pageItems);
		}

		internal bool TryGetDescriptor(DebugCommandId id, out DebugCommandDescriptor descriptor)
		{
			if (m_ById.TryGetValue(id, out var value))
			{
				descriptor = value.Public;
				return true;
			}
			descriptor = null;
			return false;
		}
	}

	internal static class DexcCommandRuntime
	{
		// The state is the input. These take it explicitly so a caller that owns a state - the Editor window with its own -
		// can work through the same code without the library guessing which state was meant.
		internal static DexcCommandRegistry RegistryOf(DexcRuntimeSession state)
		{
			return state.Commands ?? (state.Commands = new DexcCommandRegistry(state));
		}

		internal static DexcCommandDraftStore DraftsOf(DexcRuntimeSession state)
		{
			return state.CommandDrafts ?? (state.CommandDrafts = new DexcCommandDraftStore(RegistryOf(state)));
		}

		internal static DexcCommandReferenceStore ReferencesOf(DexcRuntimeSession state)
		{
			return state.CommandReferences ?? (state.CommandReferences = new DexcCommandReferenceStore(state.SessionId));
		}

		internal static DexcCommandHistoryStore HistoryOf(DexcRuntimeSession state)
		{
			return state.CommandHistory ?? (state.CommandHistory = new DexcCommandHistoryStore(state));
		}

		// The public facade always means the running session.
		internal static DexcCommandRegistry Registry => RegistryOf(Get());
		internal static DexcCommandDraftStore Drafts => DraftsOf(Get());
		internal static DexcCommandReferenceStore References => ReferencesOf(Get());
		internal static DexcCommandHistoryStore History => HistoryOf(Get());

		private static DexcRuntimeSession Get()
		{
			var session = DexcDebugger.CurrentSession;
			if (session == null)
				throw new InvalidOperationException("DexcDebugger must be initialized.");
			return session;
		}

		internal static DebugCommandValidationResult ValidateTyped(object command)
		{
			if (command == null)
				throw new ArgumentNullException(nameof(command));
			return ValidateInstance(command, GetDescriptor(command.GetType()), true);
		}

		internal static DebugCommandRequest CreateTypedRequest(DebugCommand command)
		{
			return CreateTypedRequestObject(command);
		}

		internal static DebugCommandRequest<TOutput> CreateTypedRequest<TOutput>(DebugCommand<TOutput> command)
		{
			return (DebugCommandRequest<TOutput>)CreateTypedRequestObject(command);
		}

		internal static AsyncDebugCommandRequest CreateTypedRequest(AsyncDebugCommand command)
		{
			return (AsyncDebugCommandRequest)CreateTypedRequestObject(command);
		}

		internal static AsyncDebugCommandRequest<TOutput> CreateTypedRequest<TOutput>(AsyncDebugCommand<TOutput> command)
		{
			return (AsyncDebugCommandRequest<TOutput>)CreateTypedRequestObject(command);
		}

		private static DebugCommandRequest CreateTypedRequestObject(object command)
		{
			if (command == null)
				throw new ArgumentNullException(nameof(command));
			var descriptor = GetDescriptor(command.GetType());
			var validation = ValidateInstance(command, descriptor, true);
			if (!validation.CanExecute)
				throw new DebugCommandValidationException(validation);
			return CreateRequest(command, descriptor, 0);
		}

		internal static void Notify(object command, IDebugInput[] inputs)
		{
			if (command == null)
				throw new ArgumentNullException(nameof(command));
			if (inputs == null || inputs.Length == 0 || inputs.Any(input => input == null) || inputs.Distinct().Count() != inputs.Length)
				throw new ArgumentException("Inputs must be non-null, non-empty, and unique.", nameof(inputs));
			var descriptor = GetDescriptor(command.GetType());
			var fields = descriptor
				.Fields.Where(field => field.IsInput)
				.Select(field => field.Field.GetValue(command) as IDebugInput)
				.Where(input => input != null)
				.ToList();
			if (inputs.Any(input => !fields.Contains(input)))
				throw new ArgumentException("Input does not belong to the command.", nameof(inputs));
			var before = fields.Select(input => new DexcInputSnapshot(input)).ToArray();
			try
			{
				Notify(command, descriptor, inputs);
			}
			catch
			{
				foreach (var snapshot in before)
					snapshot.Restore();
				throw;
			}
		}

		private static DexcCommandDescriptor GetDescriptor(Type type)
		{
			if (!Registry.TryGet(type, out var descriptor))
				throw new InvalidOperationException("The command type is not registered.");
			return descriptor;
		}

		private static ConditionalWeakTable<object, Dictionary<FieldInfo, IDebugInput>> InputBaselines =>
			DexcRoot.Current.CommandInputBaselines;

		internal static DebugCommandValidationResult ValidateInstance(
			object command,
			DexcCommandDescriptor descriptor,
			bool checkReplacement = false
		)
		{
			var issues = new List<DebugCommandValidationIssue>();
			var currentInputs = descriptor
				.Fields.Where(field => field.IsInput)
				.Select(field => field.Field.GetValue(command) as IDebugInput)
				.ToList();
			var fieldMap = descriptor
				.Fields.Where(field => field.IsInput)
				.Zip(currentInputs, (field, input) => new { field, input })
				.Where(item => item.input != null)
				.ToDictionary(item => item.input, item => item.field.Public.Id, ReferenceEqualityComparer<IDebugInput>.Instance);
			var validation = new DebugCommandValidation(input =>
				fieldMap.TryGetValue(input, out var id) ? id : default(DebugCommandFieldId)
			);
			var baseline = checkReplacement ? InputBaselines.GetValue(command, _ => new Dictionary<FieldInfo, IDebugInput>()) : null;
			foreach (var field in descriptor.Fields.Where(field => field.IsInput))
			{
				var input = field.Field.GetValue(command) as IDebugInput;
				if (input == null)
				{
					validation.Error("input-replaced", "The command input instance was replaced with null.");
					break;
				}
				if (checkReplacement)
				{
					if (!baseline.TryGetValue(field.Field, out var original))
						baseline[field.Field] = input;
					else if (!ReferenceEquals(original, input))
					{
						validation.Error("input-replaced", "The command input instance was replaced after collection.");
						break;
					}
				}
				if (
					input is IDexcReferenceInput reference
					&& reference.State != InputReferenceState.Resolved
					&& !(reference.State == InputReferenceState.Unselected && reference.IsOptional)
				)
				{
					validation.Error(input, "reference-missing", "The selected reference is unavailable.");
					continue;
				}
				var value = input.GetValue();
				var inputValidation = new DebugValidation();
				input.Validate(value, inputValidation);
				field.Public.ValidateRange(value, inputValidation);
				foreach (var issue in inputValidation.Issues)
				{
					if (issue.Severity == DebugValidationSeverity.Error)
						validation.Error(input, issue.Code, issue.Message);
					else
						validation.Warning(input, issue.Code, issue.Message);
				}
			}
			try
			{
				InvokeValidate(command, validation);
			}
			catch (Exception exception)
			{
				validation.Error(
					"command-validation-failed",
					exception.InnerException == null ? exception.Message : exception.InnerException.Message
				);
			}
			return new DebugCommandValidationResult(validation.Issues);
		}

		internal static DebugCommandRequest CreateRequest(object command, DexcCommandDescriptor descriptor, long version)
		{
			var values = new Dictionary<DebugCommandFieldId, DebugValue>();
			foreach (var field in descriptor.Fields.Where(field => field.IsInput))
				values.Add(field.Public.Id, (field.Field.GetValue(command) as IDebugInput).GetValue());
			var references = new Dictionary<DebugCommandFieldId, DebugReferenceHandle>();
			References.PruneDestroyed();
			foreach (var field in descriptor.Fields.Where(field => field.IsInput))
			{
				if (field.Field.GetValue(command) is IDexcReferenceInput reference)
				{
					var selection = reference.CaptureSelection();
					references.Add(field.Public.Id, References.GetOrAdd(selection.Key, selection.Value));
				}
			}
			var immutable = new ReadOnlyDictionary<DebugCommandFieldId, DebugValue>(values);
			var handles = new ReadOnlyDictionary<DebugCommandFieldId, DebugReferenceHandle>(references);
			if (descriptor.Public.HasOutput)
			{
				var requestType = descriptor.Public.IsAsync ? typeof(AsyncDebugCommandRequest<>) : typeof(DebugCommandRequest<>);
				return (DebugCommandRequest)
					Activator.CreateInstance(
						requestType.MakeGenericType(descriptor.Type.BaseType.GetGenericArguments()[0]),
						BindingFlags.Instance | BindingFlags.NonPublic,
						null,
						new object[] { descriptor, immutable, version, handles },
						null
					);
			}
			return descriptor.Public.IsAsync
				? new AsyncDebugCommandRequest(descriptor, immutable, version, handles)
				: new DebugCommandRequest(descriptor, immutable, version, handles);
		}

		/// <summary>
		/// A command instance holding the request's values. A value that was valid when the request was made can be invalid
		/// by dispatch - a choice whose key has left its source - and cannot be applied; that is returned in
		/// <paramref name="invalid"/> as a validation failure, the instance left unfilled.
		/// </summary>
		internal static object RequestInstance(
			DebugCommandRequest request,
			DexcCommandDescriptor descriptor,
			out DebugCommandValidationResult invalid
		)
		{
			var instance = Activator.CreateInstance(descriptor.Type);
			invalid = ValidateSnapshot(instance, descriptor, request.Values);
			if (invalid == null)
				ApplySnapshot(instance, descriptor, request.Values, request.References);
			return instance;
		}

		private static DebugCommandValidationResult ValidateSnapshot(
			object instance,
			DexcCommandDescriptor descriptor,
			IReadOnlyDictionary<DebugCommandFieldId, DebugValue> values
		)
		{
			var inputs = descriptor
				.Fields.Where(field => field.IsInput)
				.Select(field => new { field, input = field.Field.GetValue(instance) as IDebugInput })
				.Where(item => item.input != null)
				.ToList();
			var validation = new DebugCommandValidation(input =>
				inputs.FirstOrDefault(item => ReferenceEquals(item.input, input))?.field.Public.Id ?? default(DebugCommandFieldId)
			);
			var failed = false;
			foreach (var item in inputs)
			{
				// A reference is restored through its own selection, which reports a missing target in validation.
				if (item.input is IDexcReferenceInput || !values.TryGetValue(item.field.Public.Id, out var value))
					continue;
				var check = new DebugValidation();
				item.input.Validate(value, check);
				foreach (var issue in check.Issues.Where(issue => issue.Severity == DebugValidationSeverity.Error))
				{
					validation.Error(item.input, issue.Code, issue.Message);
					failed = true;
				}
			}
			return failed ? new DebugCommandValidationResult(validation.Issues) : null;
		}

		internal static void ApplySnapshot(
			object instance,
			DexcCommandDescriptor descriptor,
			IReadOnlyDictionary<DebugCommandFieldId, DebugValue> values,
			IReadOnlyDictionary<DebugCommandFieldId, DebugReferenceHandle> references
		)
		{
			foreach (var field in descriptor.Fields.Where(field => field.IsInput))
			{
				var input = (IDebugInput)field.Field.GetValue(instance);
				if (!values.TryGetValue(field.Public.Id, out var value))
				{
					continue;
				}
				if (input is IDexcReferenceInput reference)
				{
					value.TryGetString(out var key);
					var selection = new DexcReferenceSelection(key, null, true);
					if (
						references != null
						&& references.TryGetValue(field.Public.Id, out var handle)
						&& References.TryResolve(handle, out var resolved)
						&& string.Equals(key, resolved.Key, StringComparison.Ordinal)
					)
					{
						selection = resolved;
					}
					reference.RestoreSelection(selection);
				}
				else
				{
					input.Apply(value);
				}
			}
		}

		private static void InvokeOnInputsChanged(object command, IEnumerable<IDebugInput> inputs)
		{
			var method = command
				.GetType()
				.BaseType.GetMethod(
					"OnInputsChanged",
					BindingFlags.Instance | BindingFlags.NonPublic,
					null,
					new[] { typeof(DebugInputChanges) },
					null
				);
			method.Invoke(command, new object[] { new DebugInputChanges(inputs) });
		}

		internal static void Notify(object command, DexcCommandDescriptor descriptor, IDebugInput[] inputs)
		{
			var fields = descriptor
				.Fields.Where(field => field.IsInput)
				.Select(field => field.Field.GetValue(command) as IDebugInput)
				.ToList();
			if (inputs == null || inputs.Length == 0 || inputs.Any(input => !fields.Contains(input)))
				throw new ArgumentException("Input does not belong to the command.", nameof(inputs));
			InvokeOnInputsChanged(command, inputs);
		}

		private static void InvokeValidate(object command, DebugCommandValidation validation)
		{
			var method = command
				.GetType()
				.BaseType.GetMethod(
					"Validate",
					BindingFlags.Instance | BindingFlags.NonPublic,
					null,
					new[] { typeof(DebugCommandValidation) },
					null
				);
			method.Invoke(command, new object[] { validation });
		}

		internal sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T>
			where T : class
		{
			internal static readonly ReferenceEqualityComparer<T> Instance = new ReferenceEqualityComparer<T>();

			public bool Equals(T x, T y)
			{
				return ReferenceEquals(x, y);
			}

			public int GetHashCode(T obj)
			{
				return RuntimeHelpers.GetHashCode(obj);
			}
		}
	}

	internal sealed class DexcCommandDraftStore
	{
		private sealed class Draft
		{
			internal DebugCommandDraftId Id;
			internal DexcCommandDescriptor Descriptor;
			internal object Instance;
			internal long Version;
		}

		private readonly DexcCommandRegistry m_Registry;
		private readonly Dictionary<DebugCommandDraftId, Draft> m_Drafts = new Dictionary<DebugCommandDraftId, Draft>();
		private long m_NextDraft;

		internal DexcCommandDraftStore(DexcCommandRegistry registry)
		{
			m_Registry = registry;
		}

		private DexcRuntimeSession Session
		{
			get { return DexcDebugger.CurrentSession; }
		}

		internal DebugCommandDraftOperationResult Create(DebugCommandId id)
		{
			if (!m_Registry.TryGet(id, out var descriptor))
				return Result(DebugCommandDraftOperationStatus.NotFound, default(DebugCommandDraftId), null, default(DebugDiagnosticId));
			try
			{
				var instance = Activator.CreateInstance(descriptor.Type);
				var draftId = DebugCommandDraftId.Create(Session.SessionId, ++m_NextDraft);
				var draft = new Draft
				{
					Id = draftId,
					Descriptor = descriptor,
					Instance = instance,
					Version = 0,
				};
				var validation = DexcCommandRuntime.ValidateInstance(instance, descriptor);
				m_Drafts.Add(draftId, draft);
				return Result(DebugCommandDraftOperationStatus.Succeeded, draftId, validation, default(DebugDiagnosticId));
			}
			catch (Exception exception)
			{
				var diagnostic = Session.Diagnostics.AddOperation(
					DebugDiagnosticSeverity.Error,
					DebugFeature.Command,
					"draft-create",
					"draft-create-failed",
					"The command draft could not be created.",
					id.ToString(),
					null,
					exception
				);
				return Result(
					DebugCommandDraftOperationStatus.DefinitionUnavailable,
					default(DebugCommandDraftId),
					null,
					diagnostic == null ? default(DebugDiagnosticId) : diagnostic.DiagnosticId
				);
			}
		}

		internal DebugCommandDraftOperationResult UpdateInput(DebugCommandDraftId id, DebugCommandFieldId fieldId, DebugValue value)
		{
			if (value == null)
				throw new ArgumentNullException(nameof(value));
			if (!m_Drafts.TryGetValue(id, out var draft))
				return Unknown(id);
			var field = draft.Descriptor.Find(fieldId);
			var input = GetDraftInput(draft, field);
			if (input == null)
				return Result(DebugCommandDraftOperationStatus.NotFound, id, null, default(DebugDiagnosticId));
			return ApplyChange(
				draft,
				input,
				() =>
				{
					var check = new DebugValidation();
					input.Validate(value, check);
					field.Public.ValidateRange(value, check);
					if (check.HasErrors)
						throw new InvalidOperationException(check.Issues[0].Message);
					input.Apply(value);
				}
			);
		}

		/// <summary>Moves on every input change; -1 for a draft that is gone.</summary>
		internal long GetVersion(DebugCommandDraftId id) => m_Drafts.TryGetValue(id, out var draft) ? draft.Version : -1;

		internal DebugValue GetInputValue(DebugCommandDraftId id, DebugCommandFieldId fieldId)
		{
			if (!m_Drafts.TryGetValue(id, out var draft))
				return null;
			var field = draft.Descriptor.Find(fieldId);
			var input = GetDraftInput(draft, field);
			return input == null ? null : input.GetValue();
		}

		internal IReadOnlyList<DexcChoiceCandidate> GetChoiceCandidates(DebugCommandDraftId id, DebugCommandFieldId fieldId)
		{
			if (!m_Drafts.TryGetValue(id, out var draft))
				return Array.Empty<DexcChoiceCandidate>();
			var input = GetDraftInput(draft, draft.Descriptor.Find(fieldId)) as IDexcChoiceInput;
			return input?.GetCandidates() ?? Array.Empty<DexcChoiceCandidate>();
		}

		internal string GetSelectionLabel(DebugCommandDraftId id, DebugCommandFieldId fieldId)
		{
			if (!m_Drafts.TryGetValue(id, out var draft))
				return null;
			var input = GetDraftInput(draft, draft.Descriptor.Find(fieldId));
			if (input == null || !input.GetValue().TryGetString(out var key))
				return null;
			try
			{
				if (input is IDexcChoiceInput choice)
					return choice.GetCandidates().FirstOrDefault(item => item.Key == key)?.Label ?? key;
				if (input is IDexcReferenceInput reference)
					return reference.GetCandidates().Where(item => item.Key == key).Select(item => item.Label).FirstOrDefault() ?? key;
			}
			catch
			{
				return key;
			} // Validation owns source failures; drawing must not lose the form.
			return null;
		}

		internal DebugCommandDraftOperationResult SelectReference(
			DebugCommandDraftId id,
			DebugCommandFieldId fieldId,
			DebugReferenceHandle handle
		)
		{
			if (!m_Drafts.TryGetValue(id, out var draft))
				return Unknown(id);
			var input = GetDraftInput(draft, draft.Descriptor.Find(fieldId));
			if (!(input is IDexcReferenceInput reference))
				return Result(DebugCommandDraftOperationStatus.NotFound, id, null, default(DebugDiagnosticId));
			if (!DexcCommandRuntime.References.TryResolve(handle, out var selection))
				return Result(DebugCommandDraftOperationStatus.Rejected, id, null, default(DebugDiagnosticId));
			return ApplyChange(draft, input, () => reference.RestoreSelection(selection));
		}

		internal DebugCommandDraftOperationResult ClearReference(DebugCommandDraftId id, DebugCommandFieldId fieldId)
		{
			if (!m_Drafts.TryGetValue(id, out var draft))
				return Unknown(id);
			var field = draft.Descriptor.Find(fieldId);
			var input = GetDraftInput(draft, field);
			if (input == null || !IsReference(input))
				return Result(DebugCommandDraftOperationStatus.NotFound, id, null, default(DebugDiagnosticId));
			return ApplyChange(draft, input, () => input.Apply(DebugValue.Null));
		}

		internal DebugCommandReferenceListPage GetReferenceEntries(
			DebugCommandDraftId id,
			DebugCommandFieldId fieldId,
			DebugListQuery query
		)
		{
			DexcListQueryEvaluator.ValidatePage(query.Page, Session.Options.MaxPageSize);
			if (!m_Drafts.TryGetValue(id, out var draft))
				return new DebugCommandReferenceListPage(
					id.IsValid ? DebugOperationStatus.Stale : DebugOperationStatus.NotFound,
					new DebugPageInfo(query.Page.Offset, 0, false, true, 0, false),
					null,
					default(DebugDiagnosticId)
				);
			var field = draft.Descriptor.Find(fieldId);
			var input = GetDraftInput(draft, field);
			if (input == null || !IsReference(input))
				return new DebugCommandReferenceListPage(
					DebugOperationStatus.NotFound,
					new DebugPageInfo(query.Page.Offset, 0, false, true, 0, false),
					null,
					default(DebugDiagnosticId)
				);
			try
			{
				var references = DexcCommandRuntime.References;
				references.PruneDestroyed();
				// Enumerate completely before issuing handles: a failed source must not leave partial registrations.
				var filtered = ((IDexcReferenceInput)input)
					.GetCandidates()
					.Where(entry =>
						entry.Value != null
						&& (
							query.SearchText.Length == 0
							|| entry.Label.IndexOf(query.SearchText, StringComparison.OrdinalIgnoreCase) >= 0
							|| entry.Path.IndexOf(query.SearchText, StringComparison.OrdinalIgnoreCase) >= 0
						)
					)
					.ToList();
				var entries = filtered
					.Skip(query.Page.Offset)
					.Take(query.Page.Count)
					.Select(entry => new DebugCommandReferenceEntry(
						references.GetOrAdd(entry.Key, entry.Value),
						entry.Label,
						entry.Path,
						true,
						entry.Value.GetType().FullName
					))
					.ToList();
				return new DebugCommandReferenceListPage(
					DebugOperationStatus.Succeeded,
					new DebugPageInfo(
						query.Page.Offset,
						entries.Count,
						query.Page.Offset + entries.Count < filtered.Count,
						true,
						filtered.Count,
						false
					),
					entries,
					default(DebugDiagnosticId)
				);
			}
			catch (Exception exception)
			{
				var diagnostic = Session.Diagnostics.AddOperation(
					DebugDiagnosticSeverity.Error,
					DebugFeature.Command,
					"reference-list",
					"reference-source-failed",
					"The command reference source failed.",
					fieldId.ToString(),
					null,
					exception
				);
				return new DebugCommandReferenceListPage(
					DebugOperationStatus.Failed,
					new DebugPageInfo(query.Page.Offset, 0, false, false, 0, false),
					null,
					diagnostic == null ? default(DebugDiagnosticId) : diagnostic.DiagnosticId
				);
			}
		}

		internal DebugCommandDraftOperationResult Validate(DebugCommandDraftId id)
		{
			if (!m_Drafts.TryGetValue(id, out var draft))
				return Unknown(id);
			return Result(
				DebugCommandDraftOperationStatus.Succeeded,
				id,
				DexcCommandRuntime.ValidateInstance(draft.Instance, draft.Descriptor),
				default(DebugDiagnosticId)
			);
		}

		internal DebugCommandRequestCreationResult CreateRequest(DebugCommandDraftId id)
		{
			if (!m_Drafts.TryGetValue(id, out var draft))
				return new DebugCommandRequestCreationResult(
					id.IsValid ? DebugCommandRequestCreationStatus.Stale : DebugCommandRequestCreationStatus.DefinitionUnavailable,
					null,
					null,
					default(DebugDiagnosticId)
				);
			var validation = DexcCommandRuntime.ValidateInstance(draft.Instance, draft.Descriptor);
			if (!validation.CanExecute)
				return new DebugCommandRequestCreationResult(
					DebugCommandRequestCreationStatus.ValidationFailed,
					null,
					validation,
					default(DebugDiagnosticId)
				);
			return new DebugCommandRequestCreationResult(
				DebugCommandRequestCreationStatus.Created,
				CreateDraftRequest(draft),
				validation,
				default(DebugDiagnosticId)
			);
		}

		internal void Discard(DebugCommandDraftId id)
		{
			m_Drafts.Remove(id);
		}

		internal void Clear() => m_Drafts.Clear();

		internal DebugCommandDraftOperationResult CreateFromHistory(DebugHistoryId historyId)
		{
			if (!DexcCommandRuntime.History.TryGetValues(historyId, out var commandId, out var values, out var references))
				return Result(DebugCommandDraftOperationStatus.Stale, default(DebugCommandDraftId), null, default(DebugDiagnosticId));
			if (!m_Registry.TryGet(commandId, out var descriptor))
				return Result(DebugCommandDraftOperationStatus.Stale, default(DebugCommandDraftId), null, default(DebugDiagnosticId));
			try
			{
				var instance = Activator.CreateInstance(descriptor.Type);
				DexcCommandRuntime.ApplySnapshot(instance, descriptor, values, references);
				var inputs = descriptor
					.Fields.Where(field => field.IsInput)
					.Select(field => (IDebugInput)field.Field.GetValue(instance))
					.ToArray();
				if (inputs.Length > 0)
				{
					DexcCommandRuntime.Notify(instance, descriptor, inputs);
				}
				var validation = DexcCommandRuntime.ValidateInstance(instance, descriptor);
				var id = DebugCommandDraftId.Create(Session.SessionId, ++m_NextDraft);
				m_Drafts.Add(
					id,
					new Draft
					{
						Id = id,
						Descriptor = descriptor,
						Instance = instance,
						Version = 0,
					}
				);
				return Result(DebugCommandDraftOperationStatus.Succeeded, id, validation, default(DebugDiagnosticId));
			}
			catch (Exception exception)
			{
				var diagnostic = Session.Diagnostics.AddOperation(
					DebugDiagnosticSeverity.Warning,
					DebugFeature.Command,
					"history-restore",
					"history-restore-failed",
					"The command history could not be restored.",
					historyId.ToString(),
					null,
					exception
				);
				return Result(
					DebugCommandDraftOperationStatus.Rejected,
					default(DebugCommandDraftId),
					null,
					diagnostic == null ? default(DebugDiagnosticId) : diagnostic.DiagnosticId
				);
			}
		}

		private DebugCommandRequest CreateDraftRequest(Draft draft) =>
			DexcCommandRuntime.CreateRequest(draft.Instance, draft.Descriptor, draft.Version);

		private static IDebugInput GetDraftInput(Draft draft, DexcCommandField field)
		{
			return field == null ? null : field.Field.GetValue(draft.Instance) as IDebugInput;
		}

		private DebugCommandDraftOperationResult ApplyChange(Draft draft, IDebugInput changed, Action apply)
		{
			var before = draft
				.Descriptor.Fields.Where(field => field.IsInput)
				.Select(field => new DexcInputSnapshot((IDebugInput)field.Field.GetValue(draft.Instance)))
				.ToArray();
			try
			{
				apply();
				DexcCommandRuntime.Notify(draft.Instance, draft.Descriptor, new[] { changed });
				draft.Version++;
				return Result(
					DebugCommandDraftOperationStatus.Succeeded,
					draft.Id,
					DexcCommandRuntime.ValidateInstance(draft.Instance, draft.Descriptor),
					default(DebugDiagnosticId)
				);
			}
			catch (Exception exception)
			{
				foreach (var snapshot in before)
					snapshot.Restore();
				var diagnostic = Session.Diagnostics.AddOperation(
					DebugDiagnosticSeverity.Warning,
					DebugFeature.Command,
					"draft-update",
					"draft-update-rejected",
					"The command draft update was rejected.",
					draft.Id.ToString(),
					null,
					exception
				);
				return Result(
					DebugCommandDraftOperationStatus.Rejected,
					draft.Id,
					DexcCommandRuntime.ValidateInstance(draft.Instance, draft.Descriptor),
					diagnostic == null ? default(DebugDiagnosticId) : diagnostic.DiagnosticId
				);
			}
		}

		private DebugCommandDraftOperationResult Unknown(DebugCommandDraftId id)
		{
			return Result(
				id.IsValid ? DebugCommandDraftOperationStatus.Stale : DebugCommandDraftOperationStatus.NotFound,
				id,
				null,
				default(DebugDiagnosticId)
			);
		}

		private static DebugCommandDraftOperationResult Result(
			DebugCommandDraftOperationStatus status,
			DebugCommandDraftId id,
			DebugCommandValidationResult validation,
			DebugDiagnosticId diagnostic
		)
		{
			return new DebugCommandDraftOperationResult(status, id, validation, diagnostic);
		}

		private static bool IsReference(IDebugInput input)
		{
			return input is IDexcReferenceInput;
		}
	}
}
