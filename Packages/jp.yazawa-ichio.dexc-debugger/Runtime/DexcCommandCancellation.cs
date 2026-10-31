using System;
using System.Threading;
using System.Threading.Tasks;

namespace Dexc
{
	internal enum DexcCommandCancellationCause
	{
		None,
		Cancelled,
		TimedOut,
	}

	/// <summary>
	/// The token a running Command receives, tied to the caller's token, the Command's timeout and the end of the session.
	/// Like the rest of the library it is used from the main thread only: the timeout is a <see cref="Task.Delay(TimeSpan)"/>
	/// whose continuation comes back there, and a caller who cancels their own token from another thread is on their own.
	/// </summary>
	internal sealed class DexcCommandCancellation
	{
		private readonly CancellationTokenSource m_Source;
		private readonly CancellationTokenSource m_TimeoutSource = new CancellationTokenSource();
		private readonly Action<Exception> m_OnCallbackError;
		private DexcCommandCancellationCause m_Cause;
		private Task m_CommandTask;

		internal DexcCommandCancellation(CancellationToken external, Action<Exception> onCallbackError)
		{
			m_Source = CancellationTokenSource.CreateLinkedTokenSource(external);
			m_OnCallbackError = onCallbackError;
			// Read once: the source is disposed on Finish, and the token is still compared against afterwards.
			Token = m_Source.Token;
		}

		internal CancellationToken Token { get; }

		internal bool IsFinished { get; private set; }

		/// <summary>Why the token was cancelled. A cancellation that arrived through the caller's token counts as a Cancel.</summary>
		internal DexcCommandCancellationCause Cause
		{
			get
			{
				if (m_Cause != DexcCommandCancellationCause.None)
					return m_Cause;
				return m_Source.IsCancellationRequested ? DexcCommandCancellationCause.Cancelled : DexcCommandCancellationCause.None;
			}
		}

		/// <summary>Remembers the Command's task so that a cancellation arriving after it completed is ignored.</summary>
		internal void Track(Task commandTask)
		{
			m_CommandTask = commandTask;
		}

		/// <summary>Requests a TimedOut cancellation once the timeout elapses, unless the Command finishes first.</summary>
		internal void StartTimeout(TimeSpan timeout)
		{
			if (timeout == Timeout.InfiniteTimeSpan)
				return;
			var ignored = TimeoutAsync(timeout);
		}

		private async Task TimeoutAsync(TimeSpan timeout)
		{
			try
			{
				await Task.Delay(timeout, m_TimeoutSource.Token);
			}
			catch (OperationCanceledException)
			{
				return;
			}
			Request(DexcCommandCancellationCause.TimedOut);
		}

		internal void Request(DexcCommandCancellationCause cause)
		{
			if (IsFinished || Cause != DexcCommandCancellationCause.None)
				return;
			// The Command already finished; its completion is just waiting for its turn on the main thread.
			if (m_CommandTask != null && m_CommandTask.IsCompleted)
				return;
			m_Cause = cause;
			try
			{
				m_Source.Cancel();
			}
			catch (Exception exception)
			{
				// A failing callback registered on the token must not end the Command's lifetime or release its gate.
				m_OnCallbackError(exception);
			}
		}

		internal void Finish()
		{
			if (IsFinished)
				return;
			IsFinished = true;
			// The task's Result is the Command's raw output; keeping it would keep that alive with the execution.
			m_CommandTask = null;
			m_TimeoutSource.Cancel();
			m_TimeoutSource.Dispose();
			// Disposing the linked source also unregisters it from the caller's token.
			m_Source.Dispose();
		}
	}
}
