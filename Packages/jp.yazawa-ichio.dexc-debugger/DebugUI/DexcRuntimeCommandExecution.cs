using System;
using UnityEngine;

namespace Dexc
{
	internal static class DexcRuntimeCommandExecution
	{
		/// <summary>State of this class. Owned by <see cref="DexcUIRoot"/>; nothing here survives a root reset.</summary>
		internal sealed class State
		{
			internal DebugCommandRequest Pending;
			internal DebugCommandDraftId PendingDraft;
			internal int ConfirmationFrame = -1;
			internal int DispatchFrame = -1;
			internal bool Dispatching;
			internal DebugCommandDraftId ExecutionDraft;
		}

		internal static State Data => DexcUIRoot.Current.CommandExecution;
		private static DebugCommandRequest Pending
		{
			get => Data.Pending;
			set => Data.Pending = value;
		}
		private static DebugCommandDraftId PendingDraft
		{
			get => Data.PendingDraft;
			set => Data.PendingDraft = value;
		}
		private static int ConfirmationFrame
		{
			get => Data.ConfirmationFrame;
			set => Data.ConfirmationFrame = value;
		}
		private static int DispatchFrame
		{
			get => Data.DispatchFrame;
			set => Data.DispatchFrame = value;
		}
		private static bool Dispatching
		{
			get => Data.Dispatching;
			set => Data.Dispatching = value;
		}
		private static DebugCommandDraftId ExecutionDraft
		{
			get => Data.ExecutionDraft;
			set => Data.ExecutionDraft = value;
		}

		internal static bool IsConfirming => Pending != null;
		internal static string ConfirmationMessage => IsConfirming ? DexcRuntimeUIState.Command?.ConfirmationMessage : string.Empty;

		internal static bool CanExecute()
		{
			if (
				!DexcRuntimeUIState.IsOpen
				|| DexcRuntimeUIState.Page != DexcRuntimeUIPage.CommandDetail
				|| !DexcRuntimeUIState.HasDraft
				|| DexcRuntimeUIState.Command == null
				|| DexcRuntimeUIState.AsyncExecution != null
				|| DexcRuntimeUIState.CommandInvalidInput.Count != 0
				|| Dispatching
			)
			{
				return false;
			}
			var read = DebugCommand.Draft.Validate(DexcRuntimeUIState.DraftId);
			return read.Status == DebugCommandDraftOperationStatus.Succeeded
				&& read.TryGetValidation(out var validation)
				&& validation.CanExecute;
		}

		internal static void Request()
		{
			try
			{
				if (
					IsConfirming
					|| DispatchFrame == Time.frameCount
					|| Dispatching
					|| !DexcRuntimeUIState.IsOpen
					|| DexcRuntimeUIState.Page != DexcRuntimeUIPage.CommandDetail
					|| !DexcRuntimeUIState.HasDraft
					|| DexcRuntimeUIState.AsyncExecution != null
				)
				{
					return;
				}
				if (!CanExecute())
				{
					DexcRuntimeUIState.Notice = "Command validation failed.";
					return;
				}
				var creation = DebugCommand.Draft.CreateRequest(DexcRuntimeUIState.DraftId);
				if (!creation.TryGetRequest(out var request))
				{
					DexcRuntimeUIState.Notice = "Command request could not be created.";
					return;
				}
				if (!string.IsNullOrEmpty(DexcRuntimeUIState.Command.ConfirmationMessage))
				{
					Pending = request;
					PendingDraft = DexcRuntimeUIState.DraftId;
					ConfirmationFrame = Time.frameCount;
					DexcRuntimeUIState.SetSelectedIndex(0);
				}
				else
				{
					Dispatch(request);
				}
			}
			catch (Exception exception)
			{
				CancelConfirmation();
				DexcRuntimeUIState.Notice = exception.Message;
			}
		}

		internal static void Confirm()
		{
			if (!IsConfirming || ConfirmationFrame == Time.frameCount)
			{
				return;
			}
			var request = Pending;
			var draft = PendingDraft;
			// Consume the pending approval before validation or dispatch can re-enter the UI.
			CancelConfirmation();
			try
			{
				if (!CanExecute() || DexcRuntimeUIState.DraftId != draft || DexcRuntimeUIState.CommandId != request.CommandId)
				{
					return;
				}
				var current = DebugCommand.Draft.CreateRequest(draft);
				if (
					!current.TryGetRequest(out var validated)
					|| validated.DraftVersion != request.DraftVersion
					|| validated.SessionId != request.SessionId
					|| validated.CollectionGeneration != request.CollectionGeneration
				)
				{
					DexcRuntimeUIState.Notice = "Command changed. Review it and request execution again.";
					return;
				}
				Dispatch(request);
			}
			catch (Exception exception)
			{
				DexcRuntimeUIState.Notice = exception.Message;
			}
		}

		internal static void CancelConfirmation()
		{
			Pending = null;
			PendingDraft = default(DebugCommandDraftId);
			ConfirmationFrame = -1;
		}

		internal static void Reset()
		{
			ReleaseExecution();
			CancelConfirmation();
			if (!Dispatching)
			{
				DispatchFrame = -1;
			}
		}

		internal static void Poll()
		{
			var session = DexcDebugger.CurrentSession;
			if (
				!DexcCommandExecutionObserver.TryComplete(
					ref DexcRuntimeUIState.Data.AsyncExecution,
					ref DexcRuntimeUIState.Data.AsyncTask,
					out var result,
					out var error
				)
			)
			{
				return;
			}
			if (DexcRuntimeUIState.AsyncExecution != null)
			{
				return;
			}
			DexcDebugger.SessionInvalidated -= OnSessionInvalidated;
			if (session == null || !ReferenceEquals(session, DexcDebugger.CurrentSession))
			{
				return;
			}
			if (DexcRuntimeUIState.HasDraft && DexcRuntimeUIState.DraftId == ExecutionDraft)
				DexcRuntimeUIState.LastCommandResult = result;
			ExecutionDraft = default;
			ReportError(error, "completion");
		}

		internal static void CancelExecution()
		{
			ReportError(DexcCommandExecutionObserver.Cancel(DexcRuntimeUIState.AsyncExecution), "cancel");
		}

		internal static void ReleaseExecution()
		{
			ExecutionDraft = default;
			DexcDebugger.SessionInvalidated -= OnSessionInvalidated;
			ReportError(
				DexcCommandExecutionObserver.Release(ref DexcRuntimeUIState.Data.AsyncExecution, ref DexcRuntimeUIState.Data.AsyncTask),
				"dispose"
			);
		}

		private static void OnSessionInvalidated()
		{
			ReleaseExecution();
			DexcRuntimeUIState.LastCommandResult = null;
			CancelConfirmation();
		}

		private static void ReportError(Exception error, string phase)
		{
			if (error == null)
			{
				return;
			}
			DexcRuntimeUIState.Notice = "Async command " + phase + " failed: " + error.Message;
			DexcDebugger.CurrentSession?.Diagnostics.AddOperation(
				DebugDiagnosticSeverity.Error,
				DebugFeature.Command,
				phase,
				"command-" + phase + "-failed",
				DexcRuntimeUIState.Notice,
				"Runtime UI",
				null,
				error
			);
		}

		private static void Dispatch(DebugCommandRequest request)
		{
			if (Dispatching || DispatchFrame == Time.frameCount || !CanExecute())
			{
				return;
			}
			Dispatching = true;
			DispatchFrame = Time.frameCount;
			try
			{
				DexcRuntimeUIState.LastCommandResult = null;
				DexcRuntimeCommandForm.ResetResult();
				ExecutionDraft = DexcRuntimeUIState.DraftId;
				if (request is AsyncDebugCommandRequest asyncRequest)
				{
					var session = DexcDebugger.CurrentSession;
					DexcDebugger.SessionInvalidated -= OnSessionInvalidated;
					DexcDebugger.SessionInvalidated += OnSessionInvalidated;
					DexcRuntimeUIState.AsyncExecution = asyncRequest.StartExecution();
					DexcRuntimeUIState.AsyncTask = DexcRuntimeUIState.AsyncExecution.WaitAsync().AsTask();
					if (!ReferenceEquals(session, DexcDebugger.CurrentSession))
					{
						ReleaseExecution();
					}
				}
				else
				{
					var draft = ExecutionDraft;
					var result = request.Dispatch();
					if (DexcRuntimeUIState.HasDraft && DexcRuntimeUIState.DraftId == draft)
						DexcRuntimeUIState.LastCommandResult = result;
				}
			}
			catch
			{
				ReleaseExecution();
				throw;
			}
			finally
			{
				Dispatching = false;
			}
		}
	}
}
