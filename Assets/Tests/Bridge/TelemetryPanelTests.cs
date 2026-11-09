using System.Linq;
using Dexc;
using Dexc.Bridge;
using Dexc.Telemetry;
using NUnit.Framework;

namespace DexcDebugger.Tests.Bridge
{
	/// <summary>The panel the bridge adds when the project has Dexc Telemetry, as this one does.</summary>
	public sealed class TelemetryPanelTests
	{
		[Test]
		public void ThePanelShowsTheStateAndRefusesACaptureWhileStopped()
		{
			Assume.That(DexcTelemetry.IsRunning, Is.False);
			var panel = TelemetryPanel.Create();
			var labels = panel.Widgets.OfType<DebugLabelWidget>().ToList();

			Assert.That(panel.Title, Is.EqualTo("Telemetry"));
			Assert.That(labels[0].Text, Does.StartWith("Stopped"));
			Assert.That(labels[1].Text, Is.EqualTo(DexcTelemetry.Folder));
			panel.Widgets.OfType<DebugButtonWidget>().Single(button => button.Label == "Capture").Invoke();
			Assert.That(labels.Last().Text, Does.StartWith("Not captured"));
			panel.Widgets.OfType<DebugButtonWidget>().Single(button => button.Label == "Seal").Invoke();
			Assert.That(labels.Last().Text, Does.StartWith("Not sealed"));
		}

		[Test]
		public void SendingStartsLaterFromThePanelOnceTelemetryRuns()
		{
			Assume.That(DexcTelemetry.IsRunning, Is.False);
			Assert.That(TelemetryPanel.StartUpload("http://127.0.0.1:1", false), Does.StartWith("Not sending: telemetry is stopped"));
			var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "DexcBridgeUpload-" + System.Guid.NewGuid().ToString("N"));
			DexcTelemetry.Start(new TelemetryOptions { Directory = directory, CollectUnityMetrics = false });
			try
			{
				var panel = TelemetryPanel.Create();

				Assert.That(TelemetryPanel.StartUpload("", false), Does.StartWith("Not sending: "), "no server");
				Assert.That(TelemetryPanel.StartUpload("http://127.0.0.1:1", false), Is.EqualTo("Sending to http://127.0.0.1:1"));
				Assert.That(DexcTelemetry.IsUploading, Is.True);
				Assert.That(TelemetryPanel.StartUpload("http://127.0.0.1:2", false), Does.StartWith("Already sending"));
				panel.Widgets.OfType<DebugButtonWidget>().Single(button => button.Label == "Stop sending").Invoke();
				Assert.That(DexcTelemetry.IsUploading, Is.False);
			}
			finally
			{
				DexcTelemetry.Stop();
				if (System.IO.Directory.Exists(directory))
				{
					System.IO.Directory.Delete(directory, true);
				}
			}
		}
	}
}
