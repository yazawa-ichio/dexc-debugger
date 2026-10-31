using System;
using System.Collections.Generic;

namespace Dexc
{
	internal sealed class DexcRuntimeSession
	{
		/// <summary>
		/// A state that is not a session: the Editor window holds one so it can use the features while nothing is playing.
		/// It is built the same way and carries the same stores, but it is never installed on the root and no game code
		/// can reach it. Its owner decides how long it lives.
		/// </summary>
		internal static DexcRuntimeSession CreateDetached()
		{
			return new DexcRuntimeSession(0, new DexcDebuggerOptions().ToImmutable()) { IsDetached = true };
		}

		internal DexcRuntimeSession(long generation, DexcImmutableOptions options)
		{
			SessionId = DebugSessionId.Create();
			Generation = generation;
			CollectionGeneration = DebugCollectionGeneration.Create(SessionId, generation);
			Options = options;
			Diagnostics = new DexcDiagnosticStore(SessionId, options.OperationCapacity);
		}

		internal DebugSessionId SessionId { get; }

		internal long Generation { get; }

		internal DebugCollectionGeneration CollectionGeneration { get; }

		internal DexcImmutableOptions Options { get; }

		internal DexcDiagnosticStore Diagnostics { get; }

		private readonly HashSet<DexcCommandCancellation> m_RunningCommands = new HashSet<DexcCommandCancellation>();
		private bool m_IsEnding;

		/// <summary>True for a state that is owned by someone other than the root.</summary>
		internal bool IsDetached { get; private set; }

		// "Current" asks whether this state is still the one its owner is using. A detached state answers for itself; the
		// root never holds it, so comparing against the root's session would reject every write made through it.
		internal bool IsCurrent => !m_IsEnding && (IsDetached || DexcDebugger.IsCurrentSession(this));

		internal void TrackCommand(DexcCommandCancellation cancellation)
		{
			if (m_IsEnding)
			{
				cancellation.Request(DexcCommandCancellationCause.Cancelled);
				return;
			}
			m_RunningCommands.Add(cancellation);
		}

		internal void UntrackCommand(DexcCommandCancellation cancellation) => m_RunningCommands.Remove(cancellation);

		internal void EndCommands()
		{
			m_IsEnding = true;
			var pending = new List<DexcCommandCancellation>(m_RunningCommands);
			m_RunningCommands.Clear();
			foreach (var cancellation in pending)
			{
				cancellation.Request(DexcCommandCancellationCause.Cancelled);
			}
		}

		internal DexcCommandRegistry Commands { get; set; }

		internal DexcCommandDraftStore CommandDrafts { get; set; }

		internal DexcCommandReferenceStore CommandReferences { get; set; }

		internal DexcCommandAccessGate CommandAccessGate { get; set; }

		internal DexcCommandHistoryStore CommandHistory { get; set; }

		internal DexcSettingRegistry Settings { get; set; }

		internal DexcInspectorRegistry Inspector { get; set; }

		internal DexcDisplayRegistry Display { get; set; }
	}
}
