#if DEXC_HAS_TELEMETRY
using System.Linq;
using Dexc;
using Dexc.Telemetry;
using NUnit.Framework;

namespace DexcDebugger.Tests.Editor
{
	/// <summary>The panel Dexc adds when the project has the Dexc Telemetry package, as this one does.</summary>
	public sealed class DexcTelemetryPanelTests
	{
		[Test]
		public void ThePanelShowsTheStateAndRefusesACaptureWhileStopped()
		{
			Assume.That(DexcTelemetry.IsRunning, Is.False);
			var panel = DexcTelemetryPanel.Create();
			var labels = panel.Widgets.OfType<DebugLabelWidget>().ToList();

			Assert.That(panel.Title, Is.EqualTo("Telemetry"));
			Assert.That(labels[0].Text, Does.StartWith("Stopped"));
			Assert.That(labels[1].Text, Is.EqualTo(DexcTelemetry.Folder));
			panel.Widgets.OfType<DebugButtonWidget>().Single(button => button.Label == "Capture").Invoke();
			Assert.That(labels.Last().Text, Does.StartWith("Not captured"));
			panel.Widgets.OfType<DebugButtonWidget>().Single(button => button.Label == "Seal").Invoke();
			Assert.That(labels.Last().Text, Does.StartWith("Not sealed"));
		}
	}
}
#endif
