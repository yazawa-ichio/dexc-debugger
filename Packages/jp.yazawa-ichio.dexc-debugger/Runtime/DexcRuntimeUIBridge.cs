using System;

namespace Dexc
{
	internal static class DexcRuntimeUIBridge
	{
		internal static bool TryGetSession(out DebugSessionId sessionId)
		{
			var session = DexcDebugger.CurrentSession;
			if (session == null)
			{
				sessionId = default(DebugSessionId);
				return false;
			}

			sessionId = session.SessionId;
			return true;
		}

		internal static void AddDiagnostic(string code, string identity, string message, Exception exception = null)
		{
			var session = DexcDebugger.CurrentSession;
			if (session != null)
				session.Diagnostics.AddOperation(
					DebugDiagnosticSeverity.Warning,
					DebugFeature.InputEditor,
					"runtime-ui",
					code,
					message,
					identity ?? string.Empty,
					null,
					exception
				);
		}
	}
}
