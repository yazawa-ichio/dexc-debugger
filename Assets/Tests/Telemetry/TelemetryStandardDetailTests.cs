#define DEXC_TELEMETRY_DETAIL_STANDARD

using System.IO;
using Dexc.Telemetry;
using NUnit.Framework;

namespace DexcDebugger.Tests.Telemetry
{
	/// <summary>The symbol above stands for a project that chose Standard. A conditional call is decided in the file of the call.</summary>
	public sealed class TelemetryStandardDetailTests
	{
		[Test]
		public void TheChosenDetailKeepsItsOwnAndCoarserCalls()
		{
			var metrics = new TelemetryMetrics();
			metrics.SetResource(new System.Collections.Generic.KeyValuePair<string, string>[0]);
			var counter = metrics.Counter("hits", string.Empty);

			counter.AddBasic(1);
			counter.AddStandard(10);
			counter.AddDetailed(100);

			Assert.That(TelemetryConditionalTests.ValueOf(metrics), Is.EqualTo(11L));
		}
	}
}
