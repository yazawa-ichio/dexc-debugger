#if DEXC_HAS_TELEMETRY
using Dexc.Telemetry;
using UnityEngine;

namespace Dexc
{
	/// <summary>
	/// The Telemetry panel, when the project has the Dexc Telemetry package: whether it runs and where it writes, a capture
	/// of a few frames, and the seal that closes the files for taking. Starting telemetry stays with the game, which knows
	/// its options. Without the package nothing of this is compiled.
	/// </summary>
	internal static class DexcTelemetryPanel
	{
		internal const string Title = "Telemetry";

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		static void Register()
		{
			DebugPanels.Register(Create());
		}

		/// <summary>The panel. What it remembers between presses lives in it, and goes with it.</summary>
		internal static DebugPanel Create()
		{
			var frames = 120d;
			var profiler = false;
			var last = string.Empty;
			return new DebugPanel(Title)
				.Label(() => !DexcTelemetry.IsRunning ? "Stopped. The game starts it." : DexcTelemetry.IsCapturing ? "Running, capturing" : "Running")
				.Label(() => DexcTelemetry.Folder)
				.Number("Capture frames", () => frames, value => frames = value, 1, 100000, 1, true)
				.Toggle("Unity Profiler (Development Build)", () => profiler, value => profiler = value)
				.Button("Capture", () => last = DexcTelemetry.Capture((int)frames, TelemetryDetail.Detailed, profiler)
					? "Capturing " + (int)frames + " frames"
					: "Not captured: telemetry is stopped, or a capture is on")
				.Button("Seal", () =>
				{
					DexcTelemetry.Seal();
					last = DexcTelemetry.IsRunning ? "Sealed: the files so far are closed" : "Not sealed: telemetry is stopped";
				})
				.Label(() => last);
		}
	}
}
#endif
