using Dexc.Telemetry.CodeGen;

namespace DexcDebugger.Tests.TelemetrySelector
{
	/// <summary>Chooses methods for the watch the way a project would, by code. The IL processing finds it by its interface.</summary>
	public sealed class TestSelector : ITelemetryMethodSelector
	{
		public bool Select(TelemetryMethodCandidate method)
		{
			return method.Namespace == "DexcDebugger.Tests.Telemetry.Selected" && method.Method.StartsWith("ByCode");
		}
	}
}
