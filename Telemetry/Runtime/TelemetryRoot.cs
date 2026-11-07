using UnityEngine;

namespace Dexc.Telemetry
{
	/// <summary>
	/// The root of the running telemetry and the only mutable static variable in this package. Every other piece of state
	/// hangs off this instance, so replacing it resets the package completely. It stands apart from Dexc's own root because
	/// this package works without Dexc and stays in a build Dexc was removed from.
	/// </summary>
	internal sealed class TelemetryRoot
	{
		static TelemetryRoot s_Current;

		internal static TelemetryRoot Current => s_Current ?? (s_Current = new TelemetryRoot());

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		static void ResetForPlayMode()
		{
			Reset();
		}

		/// <summary>Installs a fresh root.</summary>
		internal static void Reset()
		{
			s_Current = new TelemetryRoot();
		}
	}
}
