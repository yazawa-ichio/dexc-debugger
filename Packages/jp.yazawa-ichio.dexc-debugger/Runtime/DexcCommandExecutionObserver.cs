using System;
using System.Threading.Tasks;

namespace Dexc
{
	internal static class DexcCommandExecutionObserver
	{
		internal static bool TryComplete(
			ref DebugCommandExecution execution,
			ref Task<CommandExecutionResult> task,
			out CommandExecutionResult result,
			out Exception error
		)
		{
			result = null;
			error = null;
			if (task == null || !task.IsCompleted)
			{
				return false;
			}
			var completed = task;
			var owned = execution;
			// Detach before Dispose invokes cancellation callbacks, which may re-enter the owner.
			execution = null;
			task = null;
			try
			{
				result = completed.GetAwaiter().GetResult();
			}
			catch (Exception exception)
			{
				error = exception;
			}
			var disposalError = Dispose(owned);
			if (disposalError != null)
			{
				error = error == null ? disposalError : new AggregateException(error, disposalError);
			}
			return true;
		}

		internal static Exception Release(ref DebugCommandExecution execution, ref Task<CommandExecutionResult> task)
		{
			var owned = execution;
			execution = null;
			task = null;
			return Dispose(owned);
		}

		internal static Exception Cancel(DebugCommandExecution execution)
		{
			try
			{
				execution?.Cancel();
				return null;
			}
			catch (Exception exception)
			{
				return exception;
			}
		}

		private static Exception Dispose(DebugCommandExecution execution)
		{
			try
			{
				execution?.Dispose();
				return null;
			}
			catch (Exception exception)
			{
				return exception;
			}
		}
	}
}
