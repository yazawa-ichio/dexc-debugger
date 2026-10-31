using System;
using System.Collections.Generic;

namespace Dexc
{
	internal static class DexcDisabledRuntime
	{
		internal static DebugCollectionSummary CollectionSummary
		{
			get
			{
				return new DebugCollectionSummary(
					DebugCollectionState.Disabled,
					default(DebugSessionId),
					default(DebugCollectionGeneration),
					0,
					0,
					0,
					0
				);
			}
		}

		internal static DebugPageInfo EmptyPage(DebugPageRequest request)
		{
			return new DebugPageInfo(request.Offset, 0, false, true, 0, false);
		}

		internal static DebugCommandDraftOperationResult DisabledDraft(DebugCommandDraftId id)
		{
			return new DebugCommandDraftOperationResult(DebugCommandDraftOperationStatus.Disabled, id, null, default(DebugDiagnosticId));
		}

		internal static DebugCommandRequestCreationResult DisabledRequest(DebugCommandDraftId id)
		{
			return new DebugCommandRequestCreationResult(
				DebugCommandRequestCreationStatus.Disabled,
				null,
				null,
				default(DebugDiagnosticId)
			);
		}

		internal static DebugCommandValidationResult DisabledTypedValidation(object command)
		{
			if (command == null)
				throw new ArgumentNullException(nameof(command));
			var issue = new DebugValidationIssue(DebugValidationSeverity.Error, "debugger-disabled", "Dexc Debugger is disabled.");
			return new DebugCommandValidationResult(new[] { new DebugCommandValidationIssue(issue, default(DebugCommandFieldId), true) });
		}

		internal static DebugCommandReferenceListPage DisabledReferences(DebugPageRequest request)
		{
			return new DebugCommandReferenceListPage(DebugOperationStatus.Disabled, EmptyPage(request), null, default(DebugDiagnosticId));
		}

		internal static DebugResult<DebugValue> DisabledSettingRead()
		{
			return new DebugResult<DebugValue>(DebugOperationStatus.Disabled, null, default(DebugDiagnosticId));
		}

		internal static DebugResult<DebugValue> DisabledSettingWrite()
		{
			return new DebugResult<DebugValue>(DebugOperationStatus.Disabled, null, null, default(DebugDiagnosticId));
		}

		internal static DebugResult<IReadOnlyList<DebugSettingPresetDifference>> DisabledPresetPreview()
		{
			return new DebugResult<IReadOnlyList<DebugSettingPresetDifference>>(
				DebugOperationStatus.Disabled,
				Array.Empty<DebugSettingPresetDifference>(),
				null,
				default(DebugDiagnosticId)
			);
		}

		internal static DebugResult DisabledPreset()
		{
			return new DebugResult(DebugOperationStatus.Disabled, null, default(DebugDiagnosticId));
		}

		internal static DebugResult<DebugInspectorSearchId> DisabledSearchStart()
		{
			return new DebugResult<DebugInspectorSearchId>(
				DebugOperationStatus.Disabled,
				default(DebugInspectorSearchId),
				default(DebugDiagnosticId)
			);
		}

		internal static DebugInspectorSearchPage DisabledSearchPage(DebugPageRequest request)
		{
			return new DebugInspectorSearchPage(
				DebugInspectorSearchState.Failed,
				EmptyPage(request),
				0,
				true,
				false,
				null,
				default(DebugDiagnosticId)
			);
		}

		internal static DebugResult<IReadOnlyList<DebugInspectorFieldValue>> DisabledInspectorRead()
		{
			return new DebugResult<IReadOnlyList<DebugInspectorFieldValue>>(
				DebugOperationStatus.Disabled,
				Array.Empty<DebugInspectorFieldValue>(),
				default(DebugDiagnosticId)
			);
		}

		internal static DebugResult<InspectorValueSnapshot> DisabledInspectorWrite()
		{
			return new DebugResult<InspectorValueSnapshot>(DebugOperationStatus.Disabled, null, null, default(DebugDiagnosticId));
		}

		internal static DebugResult<DebugInspectorWatchId> DisabledInspectorWatch()
		{
			return new DebugResult<DebugInspectorWatchId>(
				DebugOperationStatus.Disabled,
				default(DebugInspectorWatchId),
				null,
				default(DebugDiagnosticId)
			);
		}
	}
}
