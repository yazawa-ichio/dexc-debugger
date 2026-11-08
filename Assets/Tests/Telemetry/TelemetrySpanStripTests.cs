#define DEXC_TELEMETRY_DETAIL_STANDARD

using System.Collections.Generic;
using System.IO;
using Dexc.Telemetry;
using NUnit.Framework;

namespace DexcDebugger.Tests.Telemetry
{
	/// <summary>
	/// The symbol above stands for a project that chose Standard. The IL processing of this very assembly empties the
	/// finer span starts of this file, as ConditionalAttribute removes the finer metric calls.
	/// </summary>
	public sealed class TelemetrySpanStripTests
	{
		TelemetryRoot m_Root;

		[SetUp]
		public void SetUp()
		{
			TelemetryRoot.Reset();
			m_Root = TelemetryRoot.Current;
			var options = new TelemetryOptions { CollectUnityMetrics = false, CollectUnityLogs = false, Detail = TelemetryDetail.Detailed };
			m_Root.Start(
				new TelemetryFrameWriter(new MemoryStream()),
				new TelemetryFrameWriter(new MemoryStream()),
				new TelemetryFrameWriter(new MemoryStream()),
				options,
				new KeyValuePair<string, string>[0],
				0
			);
		}

		[TearDown]
		public void TearDown()
		{
			TelemetryRoot.Reset();
		}

		[Test]
		public void AStartTheSymbolKeepsIsRecorded()
		{
			var tracer = DexcTelemetry.Tracer("Game");

			using (var span = tracer.Start("kept", detail: TelemetryDetail.Standard))
			{
				Assert.That(span.Context.IsValid, Is.True);
			}
		}

		[Test]
		public void AFinerStartIsGoneWithItsArguments()
		{
			var tracer = DexcTelemetry.Tracer("Game");
			var evaluated = 0;

			using (var span = tracer.Start(Name(ref evaluated), detail: TelemetryDetail.Detailed))
			{
				span.Set("ignored", 1);
				Assert.That(span.Context.IsValid, Is.False, "the run time detail would have kept it");
			}

			Assert.That(evaluated, Is.Zero, "the name was not computed");
		}

		static string Name(ref int evaluated)
		{
			evaluated++;
			return "gone";
		}
	}
}
