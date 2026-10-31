using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Dexc
{
	public enum DebugCommandExecutionState
	{
		Running,
		Cancelling,
		TimingOut,
		Succeeded,
		Failed,
		Cancelled,
		TimedOut,
	}

	public sealed class DebugCommandHistoryEntry
	{
		internal DebugCommandHistoryEntry(
			DebugHistoryId id,
			DateTimeOffset occurredAtUtc,
			CommandExecutionStatus status,
			string path,
			string inputSummary,
			string outputSummary,
			string errorCode
		)
		{
			Id = id;
			OccurredAtUtc = occurredAtUtc;
			Status = status;
			CommandPath = path;
			InputSummary = inputSummary ?? string.Empty;
			OutputSummary = outputSummary ?? string.Empty;
			ErrorCode = errorCode ?? string.Empty;
		}

		public DebugHistoryId Id { get; }
		public DateTimeOffset OccurredAtUtc { get; }
		public CommandExecutionStatus Status { get; }
		public string CommandPath { get; }
		public string InputSummary { get; }
		public string OutputSummary { get; }
		public string ErrorCode { get; }
	}

	public sealed class DebugCommandHistoryPage
	{
		internal DebugCommandHistoryPage(DebugPageInfo page, IEnumerable<DebugCommandHistoryEntry> entries)
		{
			Page = page;
			Entries = new ReadOnlyCollection<DebugCommandHistoryEntry>((entries ?? Enumerable.Empty<DebugCommandHistoryEntry>()).ToList());
		}

		public DebugPageInfo Page { get; }
		public IReadOnlyList<DebugCommandHistoryEntry> Entries { get; }
	}

	public abstract class DebugCommandExecution : IDisposable
	{
		protected DebugCommandExecution(string id, CancellationTokenSource cancellation, Action onCancel = null)
		{
			Id = id;
			m_Cancellation = cancellation;
			m_OnCancel = onCancel;
		}

		private readonly CancellationTokenSource m_Cancellation;

		internal DebugCommandExecution(string id, Action onCancel)
		{
			Id = id;
			m_OnCancel = onCancel;
		}

		private readonly Action m_OnCancel;
		private bool m_CancelRequested;
		public string Id { get; }
		public abstract DebugCommandExecutionState State { get; }

		/// <summary>Idempotent: a second call, including the one from <see cref="Dispose"/>, does nothing.</summary>
		public void Cancel()
		{
			if (m_CancelRequested)
				return;
			m_CancelRequested = true;
			if (m_OnCancel != null)
				m_OnCancel();
			m_Cancellation?.Cancel();
		}

		public abstract ValueTask<CommandExecutionResult> WaitAsync();

		public void Dispose()
		{
			try
			{
				Cancel();
			}
			finally
			{
				m_Cancellation?.Dispose();
			}
		}
	}

	public sealed class DebugCommandExecution<TOutput> : DebugCommandExecution
	{
		private readonly Task<CommandExecutionResult<TOutput>> m_Task;
		private readonly Func<DebugCommandExecutionState> m_State;

		internal DebugCommandExecution(
			string id,
			Task<CommandExecutionResult<TOutput>> task,
			Func<DebugCommandExecutionState> state,
			Action onCancel
		)
			: base(id, onCancel)
		{
			m_Task = task;
			m_State = state;
		}

		public override DebugCommandExecutionState State
		{
			get { return m_State(); }
		}

		public override async ValueTask<CommandExecutionResult> WaitAsync()
		{
			return await m_Task;
		}

		public async ValueTask<CommandExecutionResult<TOutput>> WaitTypedAsync()
		{
			return await m_Task;
		}
	}

	/// <summary>Who holds the session's Command lock: any number of ReadOnly Commands, or one Lock Command.</summary>
	internal sealed class DexcCommandAccessGate
	{
		private int m_Readers;
		private bool m_Writer;

		internal bool TryEnter(DebugCommandAccess access)
		{
			if (access == DebugCommandAccess.ReadOnly)
			{
				if (m_Writer)
					return false;
				m_Readers++;
				return true;
			}
			if (m_Writer || m_Readers != 0)
				return false;
			m_Writer = true;
			return true;
		}

		internal void Exit(DebugCommandAccess access)
		{
			if (access == DebugCommandAccess.ReadOnly)
				m_Readers--;
			else
				m_Writer = false;
		}
	}

	internal sealed class DexcCommandHistoryStore
	{
		private sealed class Item
		{
			internal DebugCommandHistoryEntry Public;
			internal DebugCommandId CommandId;
			internal IReadOnlyDictionary<DebugCommandFieldId, DebugValue> Values;
			internal IReadOnlyDictionary<DebugCommandFieldId, DebugReferenceHandle> References;
		}

		private readonly DexcRuntimeSession m_Session;
		private readonly LinkedList<Item> m_Items = new LinkedList<Item>();
		private long m_Next;

		internal DexcCommandHistoryStore(DexcRuntimeSession session)
		{
			m_Session = session;
		}

		internal void Add(DebugCommandRequest request, CommandExecutionResult result)
		{
			var id = DebugHistoryId.Create(m_Session.SessionId, ++m_Next);
			var item = new Item
			{
				CommandId = request.CommandId,
				Values = request.Values,
				References = request.References,
				Public = new DebugCommandHistoryEntry(
					id,
					DateTimeOffset.UtcNow,
					result.Status,
					request.Path,
					request.Values.Count + " input(s)",
					DexcCommandOutputProjection.Summary(result),
					result.ErrorCode
				),
			};
			m_Items.AddLast(item);
			while (m_Items.Count > m_Session.Options.HistoryCapacityPerFeature)
				m_Items.RemoveFirst();
		}

		internal IReadOnlyList<KeyValuePair<DebugCommandId, DebugCommandHistoryEntry>> GetHistorySnapshot() =>
			m_Items.Select(item => new KeyValuePair<DebugCommandId, DebugCommandHistoryEntry>(item.CommandId, item.Public)).ToArray();

		internal DebugCommandHistoryPage GetHistory(DebugCommandId commandId, DebugPageRequest page)
		{
			DexcListQueryEvaluator.ValidatePage(page, m_Session.Options.MaxPageSize);
			var values = m_Items.Where(item => item.CommandId == commandId).Reverse().ToList();
			var selected = values.Skip(page.Offset).Take(page.Count).Select(item => item.Public).ToList();
			return new DebugCommandHistoryPage(
				new DebugPageInfo(page.Offset, selected.Count, page.Offset + selected.Count < values.Count, true, values.Count, false),
				selected
			);
		}

		private bool TryGet(DebugHistoryId historyId, out Item item)
		{
			item = m_Items.FirstOrDefault(candidate => candidate.Public.Id == historyId);
			return item != null;
		}

		internal bool TryGetValues(
			DebugHistoryId historyId,
			out DebugCommandId commandId,
			out IReadOnlyDictionary<DebugCommandFieldId, DebugValue> values,
			out IReadOnlyDictionary<DebugCommandFieldId, DebugReferenceHandle> references
		)
		{
			if (TryGet(historyId, out var item))
			{
				commandId = item.CommandId;
				values = item.Values;
				references = item.References;
				return true;
			}
			commandId = default(DebugCommandId);
			values = null;
			references = null;
			return false;
		}
	}

	public static class DebugCommandExecutionExtensions
	{
		public static CommandExecutionResult Dispatch(this DebugCommandRequest request)
		{
			return DexcCommandDispatcher.DispatchSync(request);
		}

		public static CommandExecutionResult<TOutput> Dispatch<TOutput>(this DebugCommandRequest<TOutput> request)
		{
			return DexcCommandDispatcher.DispatchSync<TOutput>(request);
		}

		public static void DispatchOrThrow(this DebugCommandRequest request)
		{
			ThrowIfFailed(request.Dispatch());
		}

		public static TOutput DispatchOrThrow<TOutput>(this DebugCommandRequest<TOutput> request)
		{
			var result = request.Dispatch();
			ThrowIfFailed(result);
			if (!result.TryGetOutput(out var output))
				throw new DebugCommandExecutionException(result);
			return output;
		}

		public static ValueTask<CommandExecutionResult> DispatchAsync(
			this AsyncDebugCommandRequest request,
			CancellationToken cancellationToken = default(CancellationToken)
		)
		{
			return DexcCommandDispatcher.DispatchAsync(request, cancellationToken);
		}

		public static ValueTask<CommandExecutionResult<TOutput>> DispatchAsync<TOutput>(
			this AsyncDebugCommandRequest<TOutput> request,
			CancellationToken cancellationToken = default(CancellationToken)
		)
		{
			return DexcCommandDispatcher.DispatchAsync<TOutput>(request, cancellationToken);
		}

		public static async ValueTask DispatchOrThrowAsync(
			this AsyncDebugCommandRequest request,
			CancellationToken cancellationToken = default(CancellationToken)
		)
		{
			ThrowIfFailed(await request.DispatchAsync(cancellationToken));
		}

		public static async ValueTask<TOutput> DispatchOrThrowAsync<TOutput>(
			this AsyncDebugCommandRequest<TOutput> request,
			CancellationToken cancellationToken = default(CancellationToken)
		)
		{
			var result = await request.DispatchAsync(cancellationToken);
			ThrowIfFailed(result);
			if (!result.TryGetOutput(out var output))
				throw new DebugCommandExecutionException(result);
			return output;
		}

		public static DebugCommandExecution StartExecution(
			this AsyncDebugCommandRequest request,
			CancellationToken cancellationToken = default(CancellationToken)
		)
		{
			return DexcCommandDispatcher.Start(request, cancellationToken);
		}

		public static DebugCommandExecution<TOutput> StartExecution<TOutput>(
			this AsyncDebugCommandRequest<TOutput> request,
			CancellationToken cancellationToken = default(CancellationToken)
		)
		{
			return DexcCommandDispatcher.Start<TOutput>(request, cancellationToken);
		}

		private static void ThrowIfFailed(CommandExecutionResult result)
		{
			if (result.Status != CommandExecutionStatus.Succeeded)
				throw new DebugCommandExecutionException(result);
		}
	}

	internal static class DexcCommandDispatcher
	{
		private sealed class ExecutionContext
		{
			internal string Id;
			internal bool GateEntered;
			internal DebugCommandAccess GateAccess;
			internal DexcCommandCancellation Cancellation;
			internal DebugCommandRequest Request;
			internal DexcRuntimeSession Session;
			internal object Output;
			internal bool CaptureTypedOutput;
			internal bool Invoked;
			private DebugCommandExecutionState? m_Terminal;

			/// <summary>Until the result is in, the state follows the token: a cancelled token means Cancelling or TimingOut.</summary>
			internal DebugCommandExecutionState State
			{
				get
				{
					if (m_Terminal.HasValue)
						return m_Terminal.Value;
					switch (Cancellation.Cause)
					{
						case DexcCommandCancellationCause.TimedOut:
							return DebugCommandExecutionState.TimingOut;
						case DexcCommandCancellationCause.Cancelled:
							return DebugCommandExecutionState.Cancelling;
						default:
							return DebugCommandExecutionState.Running;
					}
				}
			}

			internal void SetTerminalState(DebugCommandExecutionState state)
			{
				m_Terminal = state;
			}
		}

		internal static CommandExecutionResult DispatchSync(DebugCommandRequest request)
		{
			return DispatchSync(request, out _);
		}

		internal static CommandExecutionResult<TOutput> DispatchSync<TOutput>(DebugCommandRequest<TOutput> request)
		{
			var result = DispatchSync(request, out var output);
			var hasOutput = result.Status == CommandExecutionStatus.Succeeded;
			return new CommandExecutionResult<TOutput>(result, hasOutput ? (TOutput)output : default(TOutput), hasOutput);
		}

		private static CommandExecutionResult DispatchSync(DebugCommandRequest request, out object output)
		{
			output = null;
			var session = ValidateRequest(request, out var descriptor, out var failure);
			if (failure != null)
				return failure;
			// An async request is a DebugCommandRequest too, so this entry point compiles for one. Running it here would fail
			// inside and read as the command's own exception, though the command never ran.
			if (request is AsyncDebugCommandRequest)
			{
				return Failed(
					request,
					CommandExecutionStatus.Failed,
					CommandFailureKind.Internal,
					"request-async",
					"An async command is dispatched with DispatchAsync."
				);
			}
			if (!session.CommandAccessGateOrCreate().TryEnter(descriptor.Public.Access))
			{
				return Failed(
					request,
					CommandExecutionStatus.Failed,
					CommandFailureKind.Unavailable,
					"command-locked",
					"The command is already locked."
				);
			}
			var started = DateTimeOffset.UtcNow;
			CommandExecutionResult result;
			try
			{
				var instance = DexcCommandRuntime.RequestInstance(request, descriptor, out var invalid);
				var validation = invalid ?? DexcCommandRuntime.ValidateInstance(instance, descriptor);
				if (!validation.CanExecute)
					return ValidationFailure(request, validation);
				output = InvokeSync(instance, descriptor);
				result = Success(session, request, started, output);
			}
			catch (Exception exception)
			{
				result = ExceptionResult(session, request, started, exception);
			}
			finally
			{
				session.CommandAccessGateOrCreate().Exit(descriptor.Public.Access);
			}
			session.CommandHistoryOrCreate().Add(request, result);
			return result;
		}

		internal static ValueTask<CommandExecutionResult> DispatchAsync(
			AsyncDebugCommandRequest request,
			CancellationToken cancellationToken
		)
		{
			var execution = Start(request, cancellationToken);
			return execution.WaitAsync();
		}

		internal static async ValueTask<CommandExecutionResult<TOutput>> DispatchAsync<TOutput>(
			AsyncDebugCommandRequest<TOutput> request,
			CancellationToken cancellationToken
		)
		{
			var execution = Start(request, cancellationToken);
			return await execution.WaitTypedAsync();
		}

		internal static DebugCommandExecution Start(AsyncDebugCommandRequest request, CancellationToken cancellationToken)
		{
			var context = CreateContext(request, cancellationToken, false);
			return new DexcUntypedExecution(context, RunAsync(context));
		}

		internal static DebugCommandExecution<TOutput> Start<TOutput>(
			AsyncDebugCommandRequest<TOutput> request,
			CancellationToken cancellationToken
		)
		{
			var context = CreateContext(request, cancellationToken, true);
			return new DebugCommandExecution<TOutput>(
				context.Id,
				RunTypedAsync<TOutput>(context),
				() => context.State,
				() => context.Cancellation.Request(DexcCommandCancellationCause.Cancelled)
			);
		}

		private static ExecutionContext CreateContext(AsyncDebugCommandRequest request, CancellationToken token, bool captureOutput)
		{
			var context = new ExecutionContext
			{
				Id = Guid.NewGuid().ToString("N"),
				Request = request,
				Session = DexcDebugger.CurrentSession,
				CaptureTypedOutput = captureOutput,
			};
			context.Cancellation = new DexcCommandCancellation(token, error => AddCancellationDiagnostic(context, error));
			context.Session?.TrackCommand(context.Cancellation);
			return context;
		}

		// Runs synchronously up to the Command's first real await, so a caller of StartExecution gets the preflight
		// (validation, the lock, the first cancellation check) before the call returns.
		private static async Task<CommandExecutionResult> RunAsync(ExecutionContext context)
		{
			var session = context.Session;
			var started = DateTimeOffset.UtcNow;
			try
			{
				var current = ValidateRequest(context.Request, out var descriptor, out var failure);
				if (failure == null && !ReferenceEquals(current, session))
				{
					failure = Failed(
						context.Request,
						CommandExecutionStatus.Failed,
						CommandFailureKind.Unavailable,
						"stale-request",
						"The request belongs to an inactive debug session."
					);
				}
				if (failure == null && context.Cancellation.Cause != DexcCommandCancellationCause.None)
				{
					failure = CancellationResult(context);
				}
				if (failure == null && !session.CommandAccessGateOrCreate().TryEnter(descriptor.Public.Access))
				{
					failure = Failed(
						context.Request,
						CommandExecutionStatus.Failed,
						CommandFailureKind.Unavailable,
						"command-locked",
						"The command is already locked."
					);
				}
				Task<object> commandTask = null;
				if (failure == null)
				{
					context.GateEntered = true;
					context.GateAccess = descriptor.Public.Access;
					try
					{
						var instance = DexcCommandRuntime.RequestInstance(context.Request, descriptor, out var invalid);
						var validation = invalid ?? DexcCommandRuntime.ValidateInstance(instance, descriptor);
						if (!validation.CanExecute)
						{
							failure = ValidationFailure(context.Request, validation);
						}
						else if (context.Cancellation.Cause != DexcCommandCancellationCause.None)
						{
							failure = CancellationResult(context);
						}
						else
						{
							context.Invoked = true;
							commandTask = InvokeAsync(instance, descriptor, context.Cancellation.Token);
						}
					}
					catch (Exception exception)
					{
						failure = AsyncExceptionResult(session, context, started, exception);
					}
				}
				if (commandTask != null)
				{
					return await AwaitCommandAsync(session, context, descriptor, commandTask, started);
				}
				context.Cancellation.Finish();
				context.SetTerminalState(TerminalState(failure));
				if (context.Invoked)
					AddHistoryIfCurrent(session, context, failure);
				return failure;
			}
			finally
			{
				context.Cancellation.Finish();
				ReleaseGate(session, context);
				session?.UntrackCommand(context.Cancellation);
			}
		}

		private static async Task<CommandExecutionResult<TOutput>> RunTypedAsync<TOutput>(ExecutionContext context)
		{
			var baseResult = await RunAsync(context);
			var result = new CommandExecutionResult<TOutput>(
				baseResult,
				baseResult.Status == CommandExecutionStatus.Succeeded ? (TOutput)context.Output : default(TOutput),
				baseResult.Status == CommandExecutionStatus.Succeeded
			);
			context.Output = null;
			return result;
		}

		private static async Task<CommandExecutionResult> AwaitCommandAsync(
			DexcRuntimeSession session,
			ExecutionContext context,
			DexcCommandDescriptor descriptor,
			Task<object> commandTask,
			DateTimeOffset started
		)
		{
			object output = null;
			Exception error = null;
			try
			{
				context.Cancellation.Track(commandTask);
				context.Cancellation.StartTimeout(descriptor.Public.Timeout);
				output = await commandTask;
			}
			catch (Exception exception)
			{
				error = exception;
			}
			context.Cancellation.Finish();
			CommandExecutionResult result;
			if (error != null)
			{
				result = AsyncExceptionResult(session, context, started, error);
			}
			else if (context.Cancellation.Cause != DexcCommandCancellationCause.None)
			{
				result = CancellationResult(context);
			}
			else
			{
				result = Success(session, context.Request, started, output);
			}
			context.Output = result.Status == CommandExecutionStatus.Succeeded && context.CaptureTypedOutput ? output : null;
			context.SetTerminalState(TerminalState(result));
			AddHistoryIfCurrent(session, context, result);
			return result;
		}

		private static CommandExecutionResult CancellationResult(ExecutionContext context)
		{
			var timeout = context.Cancellation.Cause == DexcCommandCancellationCause.TimedOut;
			return Failed(
				context.Request,
				timeout ? CommandExecutionStatus.TimedOut : CommandExecutionStatus.Cancelled,
				CommandFailureKind.None,
				timeout ? "command-timeout" : "command-cancelled",
				timeout ? "The command exceeded its timeout." : "The command was cancelled."
			);
		}

		private static CommandExecutionResult AsyncExceptionResult(
			DexcRuntimeSession session,
			ExecutionContext context,
			DateTimeOffset started,
			Exception exception
		)
		{
			var actual = exception is TargetInvocationException && exception.InnerException != null ? exception.InnerException : exception;
			if (
				actual is OperationCanceledException cancelled
				&& cancelled.CancellationToken == context.Cancellation.Token
				&& context.Cancellation.Cause != DexcCommandCancellationCause.None
			)
			{
				return CancellationResult(context);
			}
			return ExceptionResult(session, context.Request, started, actual);
		}

		private static DebugCommandExecutionState TerminalState(CommandExecutionResult result)
		{
			switch (result.Status)
			{
				case CommandExecutionStatus.Succeeded:
					return DebugCommandExecutionState.Succeeded;
				case CommandExecutionStatus.Cancelled:
					return DebugCommandExecutionState.Cancelled;
				case CommandExecutionStatus.TimedOut:
					return DebugCommandExecutionState.TimedOut;
				default:
					return DebugCommandExecutionState.Failed;
			}
		}

		private static void ReleaseGate(DexcRuntimeSession session, ExecutionContext context)
		{
			if (session == null || !context.GateEntered)
				return;
			session.CommandAccessGateOrCreate().Exit(context.GateAccess);
			context.GateEntered = false;
		}

		private static void AddHistoryIfCurrent(DexcRuntimeSession session, ExecutionContext context, CommandExecutionResult result)
		{
			if (!session.IsCurrent)
				return;
			session.CommandHistoryOrCreate().Add(context.Request, result);
		}

		private static CommandExecutionResult ValidationFailure(DebugCommandRequest request, DebugCommandValidationResult validation)
		{
			var issue = validation.Issues.FirstOrDefault(item => item.Severity == DebugValidationSeverity.Error);
			return Failed(
				request,
				CommandExecutionStatus.Failed,
				validation.Issues.Any(item => item.Code == "reference-missing") ? CommandFailureKind.MissingReference
					: validation.Issues.Any(item => item.IsUnavailable) ? CommandFailureKind.Unavailable
					: CommandFailureKind.Validation,
				issue == null ? "validation-failed" : issue.Code,
				issue == null ? "The command is not executable." : issue.Message
			);
		}

		private static void AddCancellationDiagnostic(ExecutionContext context, Exception error)
		{
			if (context.Session == null || !context.Session.IsCurrent)
			{
				return;
			}
			context.Session.Diagnostics.AddOperation(
				DebugDiagnosticSeverity.Error,
				DebugFeature.Command,
				"cancel",
				"command-cancellation-callback-failed",
				"A Command cancellation callback failed.",
				context.Request?.Path ?? "Command execution",
				context.Id,
				error
			);
		}

		private static Task<object> InvokeAsync(object instance, DexcCommandDescriptor descriptor, CancellationToken token)
		{
			var method = descriptor.Type.BaseType.GetMethod(
				"ExecuteAsync",
				BindingFlags.Instance | BindingFlags.NonPublic,
				null,
				new[] { typeof(CancellationToken) },
				null
			);
			var returned = method.Invoke(instance, new object[] { token });
			var asTask = returned.GetType().GetMethod("AsTask");
			var task = (Task)asTask.Invoke(returned, null);
			return AwaitTaskResult(task);
		}

		private static async Task<object> AwaitTaskResult(Task task)
		{
			await task;
			var result = task.GetType().GetProperty("Result");
			return result == null ? null : result.GetValue(task, null);
		}

		private static object InvokeSync(object instance, DexcCommandDescriptor descriptor)
		{
			return descriptor
				.Type.BaseType.GetMethod("Execute", BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null)
				.Invoke(instance, null);
		}

		private static DexcRuntimeSession ValidateRequest(
			DebugCommandRequest request,
			out DexcCommandDescriptor descriptor,
			out CommandExecutionResult failure
		)
		{
			descriptor = null;
			failure = null;
			var session = DexcDebugger.CurrentSession;
			if (request == null)
			{
				failure = Failed(
					null,
					CommandExecutionStatus.Failed,
					CommandFailureKind.Internal,
					"request-null",
					"The request must not be null."
				);
				return session;
			}
			if (
				session == null
				|| !session.IsCurrent
				|| request.SessionId != session.SessionId
				|| !DexcCommandRuntime.Registry.TryGet(request.CommandId, out descriptor)
			)
			{
				failure = Failed(
					request,
					CommandExecutionStatus.Failed,
					CommandFailureKind.Unavailable,
					"stale-request",
					"The request belongs to an inactive debug session."
				);
				return session;
			}
			return session;
		}

		private static CommandExecutionResult Success(
			DexcRuntimeSession session,
			DebugCommandRequest request,
			DateTimeOffset started,
			object output
		)
		{
			var typeName = OutputTypeName(request);
			DebugValue projection;
			try
			{
				projection = request.Descriptor.Public.HasOutput ? DexcCommandOutputProjection.Project(output) : null;
			}
			catch (Exception exception)
			{
				const string code = "command-output-projection-failed";
				var message = "Output projection failed for " + typeName + ".";
				var diagnostic = session.Diagnostics.AddOperation(
					DebugDiagnosticSeverity.Error,
					DebugFeature.Command,
					"output-projection",
					code,
					message,
					request.Path,
					null,
					exception
				);
				return new CommandExecutionResult(
					Guid.NewGuid().ToString("N"),
					request.Path,
					CommandExecutionStatus.Failed,
					CommandFailureKind.Internal,
					code,
					message,
					diagnostic == null ? default(DebugDiagnosticId) : diagnostic.DiagnosticId,
					started,
					DateTimeOffset.UtcNow - started,
					null,
					typeName
				);
			}
			return new CommandExecutionResult(
				Guid.NewGuid().ToString("N"),
				request.Path,
				CommandExecutionStatus.Succeeded,
				CommandFailureKind.None,
				string.Empty,
				string.Empty,
				default(DebugDiagnosticId),
				started,
				DateTimeOffset.UtcNow - started,
				projection,
				typeName
			);
		}

		private static string OutputTypeName(DebugCommandRequest request)
		{
			if (request == null || !request.Descriptor.Public.HasOutput)
			{
				return string.Empty;
			}
			var type = request.Descriptor.Type.BaseType.GetGenericArguments()[0];
			return type.FullName ?? type.Name;
		}

		private static CommandExecutionResult Failed(
			DebugCommandRequest request,
			CommandExecutionStatus status,
			CommandFailureKind kind,
			string code,
			string message
		)
		{
			return new CommandExecutionResult(
				Guid.NewGuid().ToString("N"),
				request == null ? string.Empty : request.Path,
				status,
				kind,
				code,
				message,
				default(DebugDiagnosticId),
				DateTimeOffset.UtcNow,
				TimeSpan.Zero,
				null,
				OutputTypeName(request)
			);
		}

		private static CommandExecutionResult ExceptionResult(
			DexcRuntimeSession session,
			DebugCommandRequest request,
			DateTimeOffset started,
			Exception exception
		)
		{
			var actual = exception is TargetInvocationException && exception.InnerException != null ? exception.InnerException : exception;
			if (actual is DexcExpectedFailureException expected)
				return new CommandExecutionResult(
					Guid.NewGuid().ToString("N"),
					request.Path,
					CommandExecutionStatus.Failed,
					CommandFailureKind.Expected,
					expected.Code,
					expected.MessageText,
					default(DebugDiagnosticId),
					started,
					DateTimeOffset.UtcNow - started,
					null,
					OutputTypeName(request)
				);
			var diagnostic =
				session == null
					? null
					: session.Diagnostics.AddOperation(
						DebugDiagnosticSeverity.Error,
						DebugFeature.Command,
						"dispatch",
						"command-exception",
						"The command threw an unhandled exception.",
						request.Path,
						null,
						actual
					);
			return new CommandExecutionResult(
				Guid.NewGuid().ToString("N"),
				request.Path,
				CommandExecutionStatus.Failed,
				CommandFailureKind.UnhandledException,
				"command-exception",
				actual.Message,
				diagnostic == null ? default(DebugDiagnosticId) : diagnostic.DiagnosticId,
				started,
				DateTimeOffset.UtcNow - started,
				null,
				OutputTypeName(request)
			);
		}

		private sealed class DexcUntypedExecution : DebugCommandExecution
		{
			private readonly Task<CommandExecutionResult> m_Task;
			private readonly ExecutionContext m_Context;

			internal DexcUntypedExecution(ExecutionContext context, Task<CommandExecutionResult> task)
				: base(context.Id, () => context.Cancellation.Request(DexcCommandCancellationCause.Cancelled))
			{
				m_Context = context;
				m_Task = task;
			}

			public override DebugCommandExecutionState State
			{
				get { return m_Context.State; }
			}

			public override async ValueTask<CommandExecutionResult> WaitAsync()
			{
				return await m_Task;
			}
		}
	}

	internal static class DexcSessionCommandExtensions
	{
		internal static DexcCommandAccessGate CommandAccessGateOrCreate(this DexcRuntimeSession session)
		{
			return session.CommandAccessGate ?? (session.CommandAccessGate = new DexcCommandAccessGate());
		}

		internal static DexcCommandHistoryStore CommandHistoryOrCreate(this DexcRuntimeSession session)
		{
			return session.CommandHistory ?? (session.CommandHistory = new DexcCommandHistoryStore(session));
		}
	}
}
