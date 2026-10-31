using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;

namespace Dexc
{
	/// <summary>Severity of a user-correctable validation issue.</summary>
	public enum DebugValidationSeverity
	{
		Error = 0,
		Warning = 1,
	}

	/// <summary>Severity of a definition or implementation diagnostic.</summary>
	public enum DebugDiagnosticSeverity
	{
		Warning = 0,
		Error = 1,
	}

	/// <summary>State of a feature's reflection collection.</summary>
	public enum DebugCollectionState
	{
		Disabled = 0,
		Ready = 1,
		Unavailable = 2,
	}

	/// <summary>Feature associated with a diagnostic.</summary>
	public enum DebugFeature
	{
		Command = 0,
		Setting = 1,
		Inspector = 2,
		Display = 3,
		InputEditor = 4,
	}

	[Flags]
	public enum DebugFeatureFilter
	{
		None = 0,
		Command = 1 << 0,
		Setting = 1 << 1,
		Inspector = 1 << 2,
		Display = 1 << 3,
		InputEditor = 1 << 4,
		All = Command | Setting | Inspector | Display | InputEditor,
	}

	[Flags]
	public enum DebugDiagnosticSourceFilter
	{
		None = 0,
		Collection = 1 << 0,
		Operation = 1 << 1,
		All = Collection | Operation,
	}

	/// <summary>Identity of one initialized runtime session.</summary>
	public readonly struct DebugSessionId : IEquatable<DebugSessionId>
	{
		private readonly Guid m_Value;

		internal DebugSessionId(Guid value)
		{
			m_Value = value;
		}

		internal static DebugSessionId Create()
		{
			return new DebugSessionId(Guid.NewGuid());
		}

		public bool IsValid
		{
			get { return m_Value != Guid.Empty; }
		}

		public bool Equals(DebugSessionId other)
		{
			return m_Value.Equals(other.m_Value);
		}

		public override bool Equals(object obj)
		{
			return obj is DebugSessionId && Equals((DebugSessionId)obj);
		}

		public override int GetHashCode()
		{
			return m_Value.GetHashCode();
		}

		public override string ToString()
		{
			return IsValid ? $"session:{m_Value:N}" : "session:invalid";
		}

		public static bool operator ==(DebugSessionId left, DebugSessionId right)
		{
			return left.Equals(right);
		}

		public static bool operator !=(DebugSessionId left, DebugSessionId right)
		{
			return !left.Equals(right);
		}
	}

	/// <summary>Collection generation scoped to one session.</summary>
	public readonly struct DebugCollectionGeneration : IEquatable<DebugCollectionGeneration>
	{
		private readonly DebugSessionId m_Session;
		private readonly long m_Value;

		internal DebugCollectionGeneration(DebugSessionId session, long value)
		{
			m_Session = session;
			m_Value = value;
		}

		internal static DebugCollectionGeneration Create(DebugSessionId session, long value)
		{
			return new DebugCollectionGeneration(session, value);
		}

		public bool IsValid
		{
			get { return m_Session.IsValid && m_Value > 0; }
		}

		public bool Equals(DebugCollectionGeneration other)
		{
			return m_Session == other.m_Session && m_Value == other.m_Value;
		}

		public override bool Equals(object obj)
		{
			return obj is DebugCollectionGeneration && Equals((DebugCollectionGeneration)obj);
		}

		public override int GetHashCode()
		{
			return HashCode.Combine(m_Session, m_Value);
		}

		public override string ToString()
		{
			return IsValid ? $"collection:{m_Value}" : "collection:invalid";
		}

		public static bool operator ==(DebugCollectionGeneration left, DebugCollectionGeneration right)
		{
			return left.Equals(right);
		}

		public static bool operator !=(DebugCollectionGeneration left, DebugCollectionGeneration right)
		{
			return !left.Equals(right);
		}
	}

	/// <summary>Offset and requested count for a bounded page.</summary>
	public readonly struct DebugPageRequest : IEquatable<DebugPageRequest>
	{
		public DebugPageRequest(int offset, int count)
		{
			if (offset < 0)
				throw new ArgumentOutOfRangeException(nameof(offset));
			if (count < 1)
				throw new ArgumentOutOfRangeException(nameof(count));
			Offset = offset;
			Count = count;
		}

		public int Offset { get; }
		public int Count { get; }

		public bool Equals(DebugPageRequest other)
		{
			return Offset == other.Offset && Count == other.Count;
		}

		public override bool Equals(object obj)
		{
			return obj is DebugPageRequest && Equals((DebugPageRequest)obj);
		}

		public override int GetHashCode()
		{
			return HashCode.Combine(Offset, Count);
		}

		public static bool operator ==(DebugPageRequest left, DebugPageRequest right)
		{
			return left.Equals(right);
		}

		public static bool operator !=(DebugPageRequest left, DebugPageRequest right)
		{
			return !left.Equals(right);
		}
	}

	/// <summary>Bounded page metadata returned by feature-specific list APIs.</summary>
	public readonly struct DebugPageInfo
	{
		public DebugPageInfo(int offset, int count, bool hasMore, bool isTotalCountKnown, int totalCount, bool isTruncated)
		{
			if (offset < 0)
				throw new ArgumentOutOfRangeException(nameof(offset));
			if (count < 0)
				throw new ArgumentOutOfRangeException(nameof(count));
			if (totalCount < 0)
				throw new ArgumentOutOfRangeException(nameof(totalCount));
			Offset = offset;
			Count = count;
			HasMore = hasMore;
			IsTotalCountKnown = isTotalCountKnown;
			TotalCount = totalCount;
			IsTruncated = isTruncated;
		}

		public int Offset { get; }
		public int Count { get; }
		public bool HasMore { get; }
		public bool IsTotalCountKnown { get; }
		public int TotalCount { get; }
		public bool IsTruncated { get; }
	}

	/// <summary>Path and text filter shared by feature list APIs.</summary>
	public readonly struct DebugListQuery
	{
		public DebugListQuery(string pathPrefix, string searchText, DebugPageRequest page)
		{
			PathPrefix = pathPrefix ?? throw new ArgumentNullException(nameof(pathPrefix));
			SearchText = searchText ?? throw new ArgumentNullException(nameof(searchText));
			Page = page;
		}

		public string PathPrefix { get; }
		public string SearchText { get; }
		public DebugPageRequest Page { get; }
	}

	internal static class DexcListQueryEvaluator
	{
		internal static void ValidatePage(DebugPageRequest page, int maxPageSize)
		{
			if (page.Count > maxPageSize)
				throw new ArgumentOutOfRangeException(nameof(page), "The requested page exceeds the session MaxPageSize.");
		}

		internal static bool MatchesPathPrefix(string path, string prefix)
		{
			return string.IsNullOrEmpty(prefix)
				|| string.Equals(path, prefix, System.StringComparison.Ordinal)
				|| path.StartsWith(prefix + "/", System.StringComparison.Ordinal);
		}

		internal static bool MatchesText(string value, string searchText)
		{
			return string.IsNullOrEmpty(searchText)
				|| (value ?? string.Empty).IndexOf(searchText, System.StringComparison.OrdinalIgnoreCase) >= 0;
		}

		internal static bool Matches(DebugListQuery query, string path, string displayName, string description, IEnumerable<string> tags)
		{
			if (!MatchesPathPrefix(path, query.PathPrefix))
				return false;
			if (
				MatchesText(path, query.SearchText)
				|| MatchesText(displayName, query.SearchText)
				|| MatchesText(description, query.SearchText)
			)
				return true;
			return tags != null && tags.Any(tag => MatchesText(tag, query.SearchText));
		}
	}

	/// <summary>Diagnostic source and feature filter.</summary>
	public readonly struct DebugDiagnosticQuery
	{
		public DebugDiagnosticQuery(
			DebugDiagnosticSourceFilter sources,
			DebugFeatureFilter features,
			DebugDiagnosticSeverity? severity,
			DebugPageRequest page
		)
		{
			if ((sources & ~DebugDiagnosticSourceFilter.All) != 0)
				throw new ArgumentOutOfRangeException(nameof(sources));
			if ((features & ~DebugFeatureFilter.All) != 0)
				throw new ArgumentOutOfRangeException(nameof(features));
			if (severity.HasValue && !Enum.IsDefined(typeof(DebugDiagnosticSeverity), severity.Value))
				throw new ArgumentOutOfRangeException(nameof(severity));
			Sources = sources;
			Features = features;
			Severity = severity;
			Page = page;
		}

		public DebugDiagnosticSourceFilter Sources { get; }
		public DebugFeatureFilter Features { get; }
		public DebugDiagnosticSeverity? Severity { get; }
		public DebugPageRequest Page { get; }
	}

	/// <summary>Requests one bounded level of a scene hierarchy.</summary>
	public readonly struct DebugHierarchyChildrenQuery : IEquatable<DebugHierarchyChildrenQuery>
	{
		public DebugHierarchyChildrenQuery(DebugSceneHandle scene, DebugObjectHandle parent, DebugPageRequest page)
		{
			if (!scene.IsValid)
				throw new ArgumentException("Scene handle must be valid.", nameof(scene));
			Scene = scene;
			Parent = parent;
			Page = page;
		}

		public DebugSceneHandle Scene { get; }
		public DebugObjectHandle Parent { get; }
		public DebugPageRequest Page { get; }

		public bool Equals(DebugHierarchyChildrenQuery other)
		{
			return Scene == other.Scene && Parent == other.Parent && Page == other.Page;
		}

		public override bool Equals(object obj)
		{
			return obj is DebugHierarchyChildrenQuery && Equals((DebugHierarchyChildrenQuery)obj);
		}

		public override int GetHashCode()
		{
			return HashCode.Combine(Scene, Parent, Page);
		}

		public static bool operator ==(DebugHierarchyChildrenQuery left, DebugHierarchyChildrenQuery right)
		{
			return left.Equals(right);
		}

		public static bool operator !=(DebugHierarchyChildrenQuery left, DebugHierarchyChildrenQuery right)
		{
			return !left.Equals(right);
		}
	}

	/// <summary>Immutable validation information.</summary>
	public sealed class DebugValidationIssue
	{
		public DebugValidationIssue(DebugValidationSeverity severity, string code, string message)
		{
			if (!Enum.IsDefined(typeof(DebugValidationSeverity), severity))
				throw new ArgumentOutOfRangeException(nameof(severity));
			if (string.IsNullOrWhiteSpace(code))
				throw new ArgumentException("Code must not be empty.", nameof(code));
			if (string.IsNullOrWhiteSpace(message))
				throw new ArgumentException("Message must not be empty.", nameof(message));
			Severity = severity;
			Code = code;
			Message = message;
		}

		public DebugValidationSeverity Severity { get; }
		public string Code { get; }
		public string Message { get; }
	}

	/// <summary>Stable diagnostic identity scoped to a session and generation.</summary>
	public readonly struct DebugDiagnosticId : IEquatable<DebugDiagnosticId>
	{
		private readonly DebugSessionId m_Session;
		private readonly long m_Sequence;

		internal DebugDiagnosticId(DebugSessionId session, long sequence)
		{
			m_Session = session;
			m_Sequence = sequence;
		}

		internal static DebugDiagnosticId Create(DebugSessionId session, long sequence)
		{
			return new DebugDiagnosticId(session, sequence);
		}

		public bool IsValid
		{
			get { return m_Session.IsValid && m_Sequence > 0; }
		}

		public bool Equals(DebugDiagnosticId other)
		{
			return m_Session == other.m_Session && m_Sequence == other.m_Sequence;
		}

		public override bool Equals(object obj)
		{
			return obj is DebugDiagnosticId && Equals((DebugDiagnosticId)obj);
		}

		public override int GetHashCode()
		{
			return HashCode.Combine(m_Session, m_Sequence);
		}

		public override string ToString()
		{
			return IsValid ? $"diagnostic:{m_Sequence}" : "diagnostic:invalid";
		}

		public static bool operator ==(DebugDiagnosticId left, DebugDiagnosticId right)
		{
			return left.Equals(right);
		}

		public static bool operator !=(DebugDiagnosticId left, DebugDiagnosticId right)
		{
			return !left.Equals(right);
		}
	}

	/// <summary>Immutable diagnostic summary safe for UI and adapters.</summary>
	public sealed class DebugDiagnosticSummary
	{
		public DebugDiagnosticSummary(
			DebugDiagnosticId diagnosticId,
			DebugDiagnosticSeverity severity,
			DebugFeature feature,
			string phase,
			string code,
			string message,
			string identity,
			DebugCollectionGeneration collectionGeneration,
			string executionId,
			DateTimeOffset occurredAtUtc
		)
		{
			if (!diagnosticId.IsValid)
				throw new ArgumentException("Diagnostic ID must be valid.", nameof(diagnosticId));
			if (!Enum.IsDefined(typeof(DebugDiagnosticSeverity), severity))
				throw new ArgumentOutOfRangeException(nameof(severity));
			if (!Enum.IsDefined(typeof(DebugFeature), feature))
				throw new ArgumentOutOfRangeException(nameof(feature));
			if (string.IsNullOrWhiteSpace(phase))
				throw new ArgumentException("Phase must not be empty.", nameof(phase));
			if (string.IsNullOrWhiteSpace(code))
				throw new ArgumentException("Code must not be empty.", nameof(code));
			if (string.IsNullOrWhiteSpace(message))
				throw new ArgumentException("Message must not be empty.", nameof(message));
			if (string.IsNullOrWhiteSpace(identity))
				throw new ArgumentException("Identity must not be empty.", nameof(identity));
			DiagnosticId = diagnosticId;
			Severity = severity;
			Feature = feature;
			Phase = phase;
			Code = code;
			Message = message;
			Identity = identity;
			CollectionGeneration = collectionGeneration;
			ExecutionId = executionId;
			OccurredAtUtc = occurredAtUtc;
		}

		public DebugDiagnosticId DiagnosticId { get; }
		public DebugDiagnosticSeverity Severity { get; }
		public DebugFeature Feature { get; }
		public string Phase { get; }
		public string Code { get; }
		public string Message { get; }
		public string Identity { get; }
		public DebugCollectionGeneration CollectionGeneration { get; }
		public string ExecutionId { get; }
		public DateTimeOffset OccurredAtUtc { get; }
	}

	/// <summary>Exception detail without retaining exception objects.</summary>
	public sealed class DebugDiagnosticExceptionInfo
	{
		public DebugDiagnosticExceptionInfo(string typeName, string message)
		{
			if (string.IsNullOrWhiteSpace(typeName))
				throw new ArgumentException("Type name must not be empty.", nameof(typeName));
			TypeName = typeName;
			Message = message ?? string.Empty;
		}

		public string TypeName { get; }
		public string Message { get; }
	}

	/// <summary>Immutable diagnostic detail associated with a summary.</summary>
	public sealed class DebugDiagnosticDetail
	{
		public DebugDiagnosticDetail(
			DebugDiagnosticSummary summary,
			string exceptionTypeName,
			string exceptionMessage,
			string stackTrace,
			IReadOnlyList<DebugDiagnosticExceptionInfo> innerExceptions
		)
		{
			Summary = summary ?? throw new ArgumentNullException(nameof(summary));
			ExceptionTypeName = exceptionTypeName ?? string.Empty;
			ExceptionMessage = exceptionMessage ?? string.Empty;
			StackTrace = stackTrace ?? string.Empty;
			InnerExceptions = new ReadOnlyCollection<DebugDiagnosticExceptionInfo>(
				(innerExceptions ?? Array.Empty<DebugDiagnosticExceptionInfo>()).ToArray()
			);
		}

		public DebugDiagnosticSummary Summary { get; }
		public DebugDiagnosticId DiagnosticId
		{
			get { return Summary.DiagnosticId; }
		}
		public string ExceptionTypeName { get; }
		public string ExceptionMessage { get; }
		public string StackTrace { get; }
		public IReadOnlyList<DebugDiagnosticExceptionInfo> InnerExceptions { get; }
	}

	/// <summary>One bounded page of diagnostic summaries.</summary>
	public sealed class DebugDiagnosticSummaryPage
	{
		internal DebugDiagnosticSummaryPage(DebugPageInfo page, IEnumerable<DebugDiagnosticSummary> summaries)
		{
			Page = page;
			Summaries = new ReadOnlyCollection<DebugDiagnosticSummary>((summaries ?? Enumerable.Empty<DebugDiagnosticSummary>()).ToList());
		}

		public DebugPageInfo Page { get; }
		public IReadOnlyList<DebugDiagnosticSummary> Summaries { get; }
	}

	/// <summary>Immutable collection summary shared by feature list APIs.</summary>
	public sealed class DebugCollectionSummary
	{
		public DebugCollectionSummary(
			DebugCollectionState state,
			DebugSessionId sessionId,
			DebugCollectionGeneration collectionGeneration,
			int descriptorCount,
			int excludedDefinitionCount,
			int errorCount,
			int warningCount
		)
		{
			if (!Enum.IsDefined(typeof(DebugCollectionState), state))
				throw new ArgumentOutOfRangeException(nameof(state));
			if (descriptorCount < 0)
				throw new ArgumentOutOfRangeException(nameof(descriptorCount));
			if (excludedDefinitionCount < 0)
				throw new ArgumentOutOfRangeException(nameof(excludedDefinitionCount));
			if (errorCount < 0)
				throw new ArgumentOutOfRangeException(nameof(errorCount));
			if (warningCount < 0)
				throw new ArgumentOutOfRangeException(nameof(warningCount));
			State = state;
			SessionId = sessionId;
			CollectionGeneration = collectionGeneration;
			DescriptorCount = descriptorCount;
			ExcludedDefinitionCount = excludedDefinitionCount;
			ErrorCount = errorCount;
			WarningCount = warningCount;
		}

		public DebugCollectionState State { get; }
		public DebugSessionId SessionId { get; }
		public DebugCollectionGeneration CollectionGeneration { get; }
		public int DescriptorCount { get; }
		public int ExcludedDefinitionCount { get; }
		public int ErrorCount { get; }
		public int WarningCount { get; }
	}

	// Feature identifiers are deliberately separate structs. They cannot be compared
	// or converted across feature boundaries, and every value is scoped to a session.
	public readonly struct DebugCommandId : IEquatable<DebugCommandId>
	{
		private readonly DebugSessionId m_Session;
		private readonly long m_Value;

		internal DebugCommandId(DebugSessionId session, long value)
		{
			m_Session = session;
			m_Value = value;
		}

		internal static DebugCommandId Create(DebugSessionId session, long value)
		{
			return new DebugCommandId(session, value);
		}

		public bool IsValid
		{
			get { return m_Session.IsValid && m_Value > 0; }
		}

		public bool Equals(DebugCommandId other)
		{
			return m_Session == other.m_Session && m_Value == other.m_Value;
		}

		public override bool Equals(object obj)
		{
			return obj is DebugCommandId && Equals((DebugCommandId)obj);
		}

		public override int GetHashCode()
		{
			return HashCode.Combine(m_Session, m_Value);
		}

		public override string ToString()
		{
			return IsValid ? $"command:{m_Value}" : "command:invalid";
		}

		public static bool operator ==(DebugCommandId left, DebugCommandId right)
		{
			return left.Equals(right);
		}

		public static bool operator !=(DebugCommandId left, DebugCommandId right)
		{
			return !left.Equals(right);
		}
	}

	public readonly struct DebugCommandFieldId : IEquatable<DebugCommandFieldId>
	{
		private readonly DebugSessionId m_Session;
		private readonly long m_Value;

		internal DebugCommandFieldId(DebugSessionId session, long value)
		{
			m_Session = session;
			m_Value = value;
		}

		internal static DebugCommandFieldId Create(DebugSessionId session, long value)
		{
			return new DebugCommandFieldId(session, value);
		}

		public bool IsValid
		{
			get { return m_Session.IsValid && m_Value > 0; }
		}

		public bool Equals(DebugCommandFieldId other)
		{
			return m_Session == other.m_Session && m_Value == other.m_Value;
		}

		public override bool Equals(object obj)
		{
			return obj is DebugCommandFieldId && Equals((DebugCommandFieldId)obj);
		}

		public override int GetHashCode()
		{
			return HashCode.Combine(m_Session, m_Value);
		}

		public override string ToString()
		{
			return IsValid ? $"command-field:{m_Value}" : "command-field:invalid";
		}

		public static bool operator ==(DebugCommandFieldId left, DebugCommandFieldId right)
		{
			return left.Equals(right);
		}

		public static bool operator !=(DebugCommandFieldId left, DebugCommandFieldId right)
		{
			return !left.Equals(right);
		}
	}

	public readonly struct DebugCommandDraftId : IEquatable<DebugCommandDraftId>
	{
		private readonly DebugSessionId m_Session;
		private readonly long m_Value;

		internal DebugCommandDraftId(DebugSessionId session, long value)
		{
			m_Session = session;
			m_Value = value;
		}

		internal static DebugCommandDraftId Create(DebugSessionId session, long value)
		{
			return new DebugCommandDraftId(session, value);
		}

		public bool IsValid
		{
			get { return m_Session.IsValid && m_Value > 0; }
		}

		public bool Equals(DebugCommandDraftId other)
		{
			return m_Session == other.m_Session && m_Value == other.m_Value;
		}

		public override bool Equals(object obj)
		{
			return obj is DebugCommandDraftId && Equals((DebugCommandDraftId)obj);
		}

		public override int GetHashCode()
		{
			return HashCode.Combine(m_Session, m_Value);
		}

		public override string ToString()
		{
			return IsValid ? $"command-draft:{m_Value}" : "command-draft:invalid";
		}

		public static bool operator ==(DebugCommandDraftId left, DebugCommandDraftId right)
		{
			return left.Equals(right);
		}

		public static bool operator !=(DebugCommandDraftId left, DebugCommandDraftId right)
		{
			return !left.Equals(right);
		}
	}

	public readonly struct DebugReferenceHandle : IEquatable<DebugReferenceHandle>
	{
		private readonly DebugSessionId m_Session;
		private readonly long m_Value;

		internal DebugReferenceHandle(DebugSessionId session, long value)
		{
			m_Session = session;
			m_Value = value;
		}

		internal static DebugReferenceHandle Create(DebugSessionId session, long value)
		{
			return new DebugReferenceHandle(session, value);
		}

		public bool IsValid
		{
			get { return m_Session.IsValid && m_Value > 0; }
		}

		public bool Equals(DebugReferenceHandle other)
		{
			return m_Session == other.m_Session && m_Value == other.m_Value;
		}

		public override bool Equals(object obj)
		{
			return obj is DebugReferenceHandle && Equals((DebugReferenceHandle)obj);
		}

		public override int GetHashCode()
		{
			return HashCode.Combine(m_Session, m_Value);
		}

		public override string ToString()
		{
			return IsValid ? $"reference:{m_Value}" : "reference:invalid";
		}

		public static bool operator ==(DebugReferenceHandle left, DebugReferenceHandle right)
		{
			return left.Equals(right);
		}

		public static bool operator !=(DebugReferenceHandle left, DebugReferenceHandle right)
		{
			return !left.Equals(right);
		}
	}

	public readonly struct DebugSettingId : IEquatable<DebugSettingId>
	{
		private readonly DebugSessionId m_Session;
		private readonly long m_Value;

		internal DebugSettingId(DebugSessionId session, long value)
		{
			m_Session = session;
			m_Value = value;
		}

		internal static DebugSettingId Create(DebugSessionId session, long value)
		{
			return new DebugSettingId(session, value);
		}

		public bool IsValid
		{
			get { return m_Session.IsValid && m_Value > 0; }
		}

		public bool Equals(DebugSettingId other)
		{
			return m_Session == other.m_Session && m_Value == other.m_Value;
		}

		public override bool Equals(object obj)
		{
			return obj is DebugSettingId && Equals((DebugSettingId)obj);
		}

		public override int GetHashCode()
		{
			return HashCode.Combine(m_Session, m_Value);
		}

		public override string ToString()
		{
			return IsValid ? $"setting:{m_Value}" : "setting:invalid";
		}

		public static bool operator ==(DebugSettingId left, DebugSettingId right)
		{
			return left.Equals(right);
		}

		public static bool operator !=(DebugSettingId left, DebugSettingId right)
		{
			return !left.Equals(right);
		}
	}

	public readonly struct DebugSettingSetId : IEquatable<DebugSettingSetId>
	{
		private readonly DebugSessionId m_Session;
		private readonly long m_Value;

		internal DebugSettingSetId(DebugSessionId session, long value)
		{
			m_Session = session;
			m_Value = value;
		}

		internal static DebugSettingSetId Create(DebugSessionId session, long value)
		{
			return new DebugSettingSetId(session, value);
		}

		public bool IsValid
		{
			get { return m_Session.IsValid && m_Value > 0; }
		}

		public bool Equals(DebugSettingSetId other)
		{
			return m_Session == other.m_Session && m_Value == other.m_Value;
		}

		public override bool Equals(object obj)
		{
			return obj is DebugSettingSetId && Equals((DebugSettingSetId)obj);
		}

		public override int GetHashCode()
		{
			return HashCode.Combine(m_Session, m_Value);
		}

		public override string ToString()
		{
			return IsValid ? $"setting-set:{m_Value}" : "setting-set:invalid";
		}

		public static bool operator ==(DebugSettingSetId left, DebugSettingSetId right)
		{
			return left.Equals(right);
		}

		public static bool operator !=(DebugSettingSetId left, DebugSettingSetId right)
		{
			return !left.Equals(right);
		}
	}

	public readonly struct DebugSettingPresetId : IEquatable<DebugSettingPresetId>
	{
		private readonly DebugSessionId m_Session;
		private readonly long m_Value;

		internal DebugSettingPresetId(DebugSessionId session, long value)
		{
			m_Session = session;
			m_Value = value;
		}

		internal static DebugSettingPresetId Create(DebugSessionId session, long value)
		{
			return new DebugSettingPresetId(session, value);
		}

		public bool IsValid
		{
			get { return m_Session.IsValid && m_Value > 0; }
		}

		public bool Equals(DebugSettingPresetId other)
		{
			return m_Session == other.m_Session && m_Value == other.m_Value;
		}

		public override bool Equals(object obj)
		{
			return obj is DebugSettingPresetId && Equals((DebugSettingPresetId)obj);
		}

		public override int GetHashCode()
		{
			return HashCode.Combine(m_Session, m_Value);
		}

		public override string ToString()
		{
			return IsValid ? $"setting-preset:{m_Value}" : "setting-preset:invalid";
		}

		public static bool operator ==(DebugSettingPresetId left, DebugSettingPresetId right)
		{
			return left.Equals(right);
		}

		public static bool operator !=(DebugSettingPresetId left, DebugSettingPresetId right)
		{
			return !left.Equals(right);
		}
	}

	public readonly struct DebugInspectorTargetHandle : IEquatable<DebugInspectorTargetHandle>
	{
		private readonly DebugSessionId m_Session;
		private readonly long m_Value;

		internal DebugInspectorTargetHandle(DebugSessionId session, long value)
		{
			m_Session = session;
			m_Value = value;
		}

		internal static DebugInspectorTargetHandle Create(DebugSessionId session, long value)
		{
			return new DebugInspectorTargetHandle(session, value);
		}

		public bool IsValid
		{
			get { return m_Session.IsValid && m_Value > 0; }
		}

		public bool Equals(DebugInspectorTargetHandle other)
		{
			return m_Session == other.m_Session && m_Value == other.m_Value;
		}

		public override bool Equals(object obj)
		{
			return obj is DebugInspectorTargetHandle && Equals((DebugInspectorTargetHandle)obj);
		}

		public override int GetHashCode()
		{
			return HashCode.Combine(m_Session, m_Value);
		}

		public override string ToString()
		{
			return IsValid ? $"inspector-target:{m_Value}" : "inspector-target:invalid";
		}

		public static bool operator ==(DebugInspectorTargetHandle left, DebugInspectorTargetHandle right)
		{
			return left.Equals(right);
		}

		public static bool operator !=(DebugInspectorTargetHandle left, DebugInspectorTargetHandle right)
		{
			return !left.Equals(right);
		}
	}

	public readonly struct DebugInspectorComponentHandle : IEquatable<DebugInspectorComponentHandle>
	{
		private readonly DebugSessionId m_Session;
		private readonly long m_Value;

		internal DebugInspectorComponentHandle(DebugSessionId session, long value)
		{
			m_Session = session;
			m_Value = value;
		}

		internal static DebugInspectorComponentHandle Create(DebugSessionId session, long value)
		{
			return new DebugInspectorComponentHandle(session, value);
		}

		public bool IsValid
		{
			get { return m_Session.IsValid && m_Value > 0; }
		}

		public bool Equals(DebugInspectorComponentHandle other)
		{
			return m_Session == other.m_Session && m_Value == other.m_Value;
		}

		public override bool Equals(object obj)
		{
			return obj is DebugInspectorComponentHandle && Equals((DebugInspectorComponentHandle)obj);
		}

		public override int GetHashCode()
		{
			return HashCode.Combine(m_Session, m_Value);
		}

		public override string ToString()
		{
			return IsValid ? $"inspector-component:{m_Value}" : "inspector-component:invalid";
		}

		public static bool operator ==(DebugInspectorComponentHandle left, DebugInspectorComponentHandle right)
		{
			return left.Equals(right);
		}

		public static bool operator !=(DebugInspectorComponentHandle left, DebugInspectorComponentHandle right)
		{
			return !left.Equals(right);
		}

		public DebugInspectorFieldPage GetFields(DebugPageRequest page)
		{
			return DexcInspectorRuntime.Registry.GetFields(this, page);
		}

		public DebugResult<IReadOnlyList<DebugInspectorFieldValue>> ReadFields(IReadOnlyList<DebugInspectorFieldId> fields)
		{
			return DexcInspectorRuntime.Registry.ReadFields(this, fields);
		}

		public DebugResult<InspectorValueSnapshot> WriteField(
			DebugInspectorFieldId field,
			DebugInspectorWriteValue value,
			DebugInspectorWriteAccess access
		)
		{
			return DexcInspectorRuntime.Registry.WriteField(this, field, value, access);
		}

		public DebugResult<DebugInspectorWatchId> AddWatch(DebugInspectorFieldId field, TimeSpan interval)
		{
			return DexcInspectorRuntime.Registry.AddWatch(this, field, interval);
		}

		public DebugInspectorHistoryPage GetHistory(DebugInspectorFieldId field, DebugPageRequest page)
		{
			return DexcInspectorRuntime.Registry.GetHistory(this, field, page);
		}
	}

	public readonly struct DebugInspectorFieldId : IEquatable<DebugInspectorFieldId>
	{
		private readonly DebugSessionId m_Session;
		private readonly long m_Value;

		internal DebugInspectorFieldId(DebugSessionId session, long value)
		{
			m_Session = session;
			m_Value = value;
		}

		internal static DebugInspectorFieldId Create(DebugSessionId session, long value)
		{
			return new DebugInspectorFieldId(session, value);
		}

		public bool IsValid
		{
			get { return m_Session.IsValid && m_Value > 0; }
		}

		public bool Equals(DebugInspectorFieldId other)
		{
			return m_Session == other.m_Session && m_Value == other.m_Value;
		}

		public override bool Equals(object obj)
		{
			return obj is DebugInspectorFieldId && Equals((DebugInspectorFieldId)obj);
		}

		public override int GetHashCode()
		{
			return HashCode.Combine(m_Session, m_Value);
		}

		public override string ToString()
		{
			return IsValid ? $"inspector-field:{m_Value}" : "inspector-field:invalid";
		}

		public static bool operator ==(DebugInspectorFieldId left, DebugInspectorFieldId right)
		{
			return left.Equals(right);
		}

		public static bool operator !=(DebugInspectorFieldId left, DebugInspectorFieldId right)
		{
			return !left.Equals(right);
		}
	}

	public readonly struct DebugInspectorSearchId : IEquatable<DebugInspectorSearchId>
	{
		private readonly DebugSessionId m_Session;
		private readonly long m_Value;

		internal DebugInspectorSearchId(DebugSessionId session, long value)
		{
			m_Session = session;
			m_Value = value;
		}

		internal static DebugInspectorSearchId Create(DebugSessionId session, long value)
		{
			return new DebugInspectorSearchId(session, value);
		}

		public bool IsValid
		{
			get { return m_Session.IsValid && m_Value > 0; }
		}

		public bool Equals(DebugInspectorSearchId other)
		{
			return m_Session == other.m_Session && m_Value == other.m_Value;
		}

		public override bool Equals(object obj)
		{
			return obj is DebugInspectorSearchId && Equals((DebugInspectorSearchId)obj);
		}

		public override int GetHashCode()
		{
			return HashCode.Combine(m_Session, m_Value);
		}

		public override string ToString()
		{
			return IsValid ? $"inspector-search:{m_Value}" : "inspector-search:invalid";
		}

		public static bool operator ==(DebugInspectorSearchId left, DebugInspectorSearchId right)
		{
			return left.Equals(right);
		}

		public static bool operator !=(DebugInspectorSearchId left, DebugInspectorSearchId right)
		{
			return !left.Equals(right);
		}
	}

	public readonly struct DebugInspectorWatchId : IEquatable<DebugInspectorWatchId>
	{
		private readonly DebugSessionId m_Session;
		private readonly long m_Value;

		internal DebugInspectorWatchId(DebugSessionId session, long value)
		{
			m_Session = session;
			m_Value = value;
		}

		internal static DebugInspectorWatchId Create(DebugSessionId session, long value)
		{
			return new DebugInspectorWatchId(session, value);
		}

		public bool IsValid
		{
			get { return m_Session.IsValid && m_Value > 0; }
		}

		public bool Equals(DebugInspectorWatchId other)
		{
			return m_Session == other.m_Session && m_Value == other.m_Value;
		}

		public override bool Equals(object obj)
		{
			return obj is DebugInspectorWatchId && Equals((DebugInspectorWatchId)obj);
		}

		public override int GetHashCode()
		{
			return HashCode.Combine(m_Session, m_Value);
		}

		public override string ToString()
		{
			return IsValid ? $"inspector-watch:{m_Value}" : "inspector-watch:invalid";
		}

		public static bool operator ==(DebugInspectorWatchId left, DebugInspectorWatchId right)
		{
			return left.Equals(right);
		}

		public static bool operator !=(DebugInspectorWatchId left, DebugInspectorWatchId right)
		{
			return !left.Equals(right);
		}
	}

	public readonly struct DebugSceneHandle : IEquatable<DebugSceneHandle>
	{
		private readonly DebugSessionId m_Session;
		private readonly long m_Value;

		internal DebugSceneHandle(DebugSessionId session, long value)
		{
			m_Session = session;
			m_Value = value;
		}

		internal static DebugSceneHandle Create(DebugSessionId session, long value)
		{
			return new DebugSceneHandle(session, value);
		}

		public bool IsValid
		{
			get { return m_Session.IsValid && m_Value > 0; }
		}

		public bool Equals(DebugSceneHandle other)
		{
			return m_Session == other.m_Session && m_Value == other.m_Value;
		}

		public override bool Equals(object obj)
		{
			return obj is DebugSceneHandle && Equals((DebugSceneHandle)obj);
		}

		public override int GetHashCode()
		{
			return HashCode.Combine(m_Session, m_Value);
		}

		public override string ToString()
		{
			return IsValid ? $"scene:{m_Value}" : "scene:invalid";
		}

		public static bool operator ==(DebugSceneHandle left, DebugSceneHandle right)
		{
			return left.Equals(right);
		}

		public static bool operator !=(DebugSceneHandle left, DebugSceneHandle right)
		{
			return !left.Equals(right);
		}
	}

	public readonly struct DebugObjectHandle : IEquatable<DebugObjectHandle>
	{
		private readonly DebugSessionId m_Session;
		private readonly long m_Value;

		internal DebugObjectHandle(DebugSessionId session, long value)
		{
			m_Session = session;
			m_Value = value;
		}

		internal static DebugObjectHandle Create(DebugSessionId session, long value)
		{
			return new DebugObjectHandle(session, value);
		}

		public bool IsValid
		{
			get { return m_Session.IsValid && m_Value > 0; }
		}

		public bool Equals(DebugObjectHandle other)
		{
			return m_Session == other.m_Session && m_Value == other.m_Value;
		}

		public override bool Equals(object obj)
		{
			return obj is DebugObjectHandle && Equals((DebugObjectHandle)obj);
		}

		public override int GetHashCode()
		{
			return HashCode.Combine(m_Session, m_Value);
		}

		public override string ToString()
		{
			return IsValid ? $"object:{m_Value}" : "object:invalid";
		}

		public static bool operator ==(DebugObjectHandle left, DebugObjectHandle right)
		{
			return left.Equals(right);
		}

		public static bool operator !=(DebugObjectHandle left, DebugObjectHandle right)
		{
			return !left.Equals(right);
		}
	}

	/// <summary>Identifies one history entry of any feature within a session.</summary>
	public readonly struct DebugHistoryId : IEquatable<DebugHistoryId>
	{
		private readonly DebugSessionId m_Session;
		private readonly long m_Value;

		internal DebugHistoryId(DebugSessionId session, long value)
		{
			m_Session = session;
			m_Value = value;
		}

		internal static DebugHistoryId Create(DebugSessionId session, long value)
		{
			return new DebugHistoryId(session, value);
		}

		public bool IsValid
		{
			get { return m_Session.IsValid && m_Value > 0; }
		}

		public bool Equals(DebugHistoryId other)
		{
			return m_Session == other.m_Session && m_Value == other.m_Value;
		}

		public override bool Equals(object obj)
		{
			return obj is DebugHistoryId && Equals((DebugHistoryId)obj);
		}

		public override int GetHashCode()
		{
			return HashCode.Combine(m_Session, m_Value);
		}

		public override string ToString()
		{
			return IsValid ? "history:" + m_Value : "history:invalid";
		}

		public static bool operator ==(DebugHistoryId left, DebugHistoryId right)
		{
			return left.Equals(right);
		}

		public static bool operator !=(DebugHistoryId left, DebugHistoryId right)
		{
			return !left.Equals(right);
		}
	}

	/// <summary>Outcome of a Setting or Inspector operation.</summary>
	public enum DebugOperationStatus
	{
		Succeeded,
		Rejected,
		ReadOnly,
		ExplicitAccessRequired,
		Stale,
		NotFound,
		DefinitionUnavailable,
		Failed,
		Disabled,
	}

	/// <summary>Result of an operation without a payload: status, validation issues and the diagnostic recorded on failure.</summary>
	public class DebugResult
	{
		private static readonly IReadOnlyList<DebugValidationIssue> s_NoIssues = new DebugValidationIssue[0];

		internal DebugResult(DebugOperationStatus status, IEnumerable<DebugValidationIssue> issues, DebugDiagnosticId diagnostic)
		{
			Status = status;
			Issues = issues == null ? s_NoIssues : new ReadOnlyCollection<DebugValidationIssue>(issues.ToList());
			DiagnosticId = diagnostic;
		}

		internal DebugResult(DebugOperationStatus status, DebugDiagnosticId diagnostic)
			: this(status, null, diagnostic) { }

		public DebugOperationStatus Status { get; }

		public bool Succeeded
		{
			get { return Status == DebugOperationStatus.Succeeded; }
		}

		public IReadOnlyList<DebugValidationIssue> Issues { get; }

		public DebugDiagnosticId DiagnosticId { get; }
	}

	/// <summary>Result of an operation that returns a payload when it succeeds.</summary>
	public sealed class DebugResult<T> : DebugResult
	{
		internal DebugResult(DebugOperationStatus status, T value, IEnumerable<DebugValidationIssue> issues, DebugDiagnosticId diagnostic)
			: base(status, issues, diagnostic)
		{
			Value = value;
		}

		internal DebugResult(DebugOperationStatus status, T value, DebugDiagnosticId diagnostic)
			: this(status, value, null, diagnostic) { }

		/// <summary>Payload. Meaningful when <see cref="DebugResult.Succeeded"/> is true; may be empty or default otherwise.</summary>
		public T Value { get; }

		public bool TryGetValue(out T value)
		{
			value = Value;
			return Succeeded && value != null;
		}
	}

	internal sealed class DexcDiagnosticStore
	{
		private readonly DebugSessionId m_SessionId;
		private readonly int m_OperationCapacity;
		private readonly List<DebugDiagnosticDetail> m_Collection = new List<DebugDiagnosticDetail>();
		private readonly LinkedList<DebugDiagnosticDetail> m_Operation = new LinkedList<DebugDiagnosticDetail>();
		private readonly Dictionary<DebugDiagnosticId, long> m_Sequence = new Dictionary<DebugDiagnosticId, long>();
		private long m_NextSequence;

		internal DexcDiagnosticStore(DebugSessionId sessionId, int operationCapacity)
		{
			m_SessionId = sessionId;
			m_OperationCapacity = operationCapacity;
		}

		internal DebugDiagnosticDetail AddCollection(
			DebugDiagnosticSeverity severity,
			DebugFeature feature,
			string phase,
			string code,
			string message,
			string identity,
			DebugCollectionGeneration generation,
			Exception exception = null
		)
		{
			var detail = Create(severity, feature, phase, code, message, identity, generation, null, exception);
			m_Collection.Add(detail);
			return detail;
		}

		internal DebugDiagnosticDetail AddOperation(
			DebugDiagnosticSeverity severity,
			DebugFeature feature,
			string phase,
			string code,
			string message,
			string identity,
			string executionId,
			Exception exception = null
		)
		{
			if (m_OperationCapacity == 0)
				return null;
			var detail = Create(
				severity,
				feature,
				phase,
				code,
				message,
				identity,
				default(DebugCollectionGeneration),
				executionId,
				exception
			);
			while (m_Operation.Count >= m_OperationCapacity)
			{
				m_Sequence.Remove(m_Operation.First.Value.DiagnosticId);
				m_Operation.RemoveFirst();
			}
			m_Operation.AddLast(detail);
			return detail;
		}

		internal bool TryGetDetail(DebugDiagnosticId id, out DebugDiagnosticDetail detail)
		{
			detail =
				m_Collection.FirstOrDefault(item => item.DiagnosticId == id) ?? m_Operation.FirstOrDefault(item => item.DiagnosticId == id);
			return detail != null;
		}

		internal IReadOnlyList<DebugDiagnosticDetail> GetCollectionDetails()
		{
			return new ReadOnlyCollection<DebugDiagnosticDetail>(m_Collection.ToList());
		}

		internal DebugDiagnosticSummaryPage GetSummaries(DebugDiagnosticQuery query, int maxPageSize)
		{
			if (query.Page.Count > maxPageSize)
				throw new ArgumentOutOfRangeException(nameof(query), "The requested diagnostic page exceeds the session MaxPageSize.");
			var details = new List<DebugDiagnosticDetail>();
			if ((query.Sources & DebugDiagnosticSourceFilter.Collection) != 0)
				details.AddRange(m_Collection);
			if ((query.Sources & DebugDiagnosticSourceFilter.Operation) != 0)
				details.AddRange(m_Operation);
			var filtered = details
				.Where(item =>
					FeatureMatches(query.Features, item.Summary.Feature)
					&& (!query.Severity.HasValue || item.Summary.Severity == query.Severity.Value)
				)
				.OrderByDescending(item => m_Sequence[item.DiagnosticId])
				.ToList();
			var selected = filtered.Skip(query.Page.Offset).Take(query.Page.Count).Select(item => item.Summary).ToList();
			return new DebugDiagnosticSummaryPage(
				new DebugPageInfo(
					query.Page.Offset,
					selected.Count,
					query.Page.Offset + selected.Count < filtered.Count,
					true,
					filtered.Count,
					false
				),
				selected
			);
		}

		private static bool FeatureMatches(DebugFeatureFilter features, DebugFeature feature)
		{
			var flag = (DebugFeatureFilter)(1 << (int)feature);
			return (features & flag) != 0;
		}

		private DebugDiagnosticDetail Create(
			DebugDiagnosticSeverity severity,
			DebugFeature feature,
			string phase,
			string code,
			string message,
			string identity,
			DebugCollectionGeneration generation,
			string executionId,
			Exception exception
		)
		{
			var id = DebugDiagnosticId.Create(m_SessionId, ++m_NextSequence);
			m_Sequence.Add(id, m_NextSequence);
			var summary = new DebugDiagnosticSummary(
				id,
				severity,
				feature,
				phase,
				code,
				message,
				identity,
				generation,
				executionId,
				DateTimeOffset.UtcNow
			);
			var inner = new List<DebugDiagnosticExceptionInfo>();
			for (var current = exception == null ? null : exception.InnerException; current != null; current = current.InnerException)
				inner.Add(new DebugDiagnosticExceptionInfo(current.GetType().FullName, current.Message));
			return new DebugDiagnosticDetail(
				summary,
				exception == null ? string.Empty : exception.GetType().FullName,
				exception == null ? string.Empty : exception.Message,
				exception == null ? string.Empty : exception.StackTrace,
				inner
			);
		}
	}

	/// <summary>Reads diagnostics retained by the current runtime session.</summary>
	public static class DebugDiagnostics
	{
		/// <summary>Gets a filtered, newest-first page of retained diagnostic summaries.</summary>
		public static DebugDiagnosticSummaryPage GetSummaries(DebugDiagnosticQuery query)
		{
#if DEXC_DEBUGGER
			var session = DexcDebugger.CurrentSession;
			return session == null
				? new DebugDiagnosticSummaryPage(new DebugPageInfo(query.Page.Offset, 0, false, true, 0, false), null)
				: session.Diagnostics.GetSummaries(query, session.Options.MaxPageSize);
#else
			return new DebugDiagnosticSummaryPage(new DebugPageInfo(query.Page.Offset, 0, false, true, 0, false), null);
#endif
		}

		/// <summary>Gets one retained diagnostic detail, or false for an unknown or stale ID.</summary>
		public static bool TryGetDetail(DebugDiagnosticId diagnosticId, out DebugDiagnosticDetail detail)
		{
			var session = DexcDebugger.CurrentSession;
			if (session == null)
			{
				detail = null;
				return false;
			}
			return session.Diagnostics.TryGetDetail(diagnosticId, out detail);
		}
	}
}
