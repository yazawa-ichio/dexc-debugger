#if DEXC_BRIDGE_HAS_TELEMETRY
using System;
using Dexc.Telemetry;
using UnityEngine;

namespace Dexc.Bridge
{
	/// <summary>
	/// The Telemetry panel of the Dexc menu: whether telemetry runs and where it writes, a capture of a few frames, the seal
	/// that closes the files for taking, and the sending of the files to a server started later, such as a dashboard or a
	/// collector on this machine. Starting telemetry stays with the game, which knows its options.
	/// </summary>
	internal static class TelemetryPanel
	{
		internal const string Title = "Telemetry";

		/// <summary>The dashboard of a workspace on this machine: OTLP over HTTP on its own port.</summary>
		internal const string DefaultEndpoint = "http://127.0.0.1:7892";

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
			var endpoint = DefaultEndpoint;
			var logs = false;
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
				.Label(() => DexcTelemetry.IsUploading ? "Sending to a server" : "Not sending")
				.Text("Server", () => endpoint, value => endpoint = value)
				.Toggle("Send logs too (any text the game logs)", () => logs, value => logs = value)
				.Button("Start sending", () => last = StartUpload(endpoint, logs))
				.Button("Stop sending", () =>
				{
					DexcTelemetry.StopUpload();
					last = "Stopped sending: the files stay and are sent from where they were at the next start";
				})
				.Label(() => last);
		}

		/// <summary>Sends the files of the run, those written so far included. While sending, the workspace does not take them.</summary>
		internal static string StartUpload(string endpoint, bool logs)
		{
			if (!DexcTelemetry.IsRunning)
			{
				return "Not sending: telemetry is stopped";
			}
			if (DexcTelemetry.IsUploading)
			{
				return "Already sending: stop it first to change the server";
			}
			try
			{
				DexcTelemetry.StartUpload(new TelemetryUploadOptions { Endpoint = endpoint, Logs = logs });
				return "Sending to " + endpoint;
			}
			catch (ArgumentException exception)
			{
				return "Not sending: " + exception.Message;
			}
		}
	}
}
#endif
