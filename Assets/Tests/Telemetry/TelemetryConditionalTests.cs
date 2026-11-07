using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dexc.Telemetry;
using NUnit.Framework;

namespace DexcDebugger.Tests.Telemetry
{
	/// <summary>This file defines no detail symbol, like a project that wants none of the calls.</summary>
	public sealed class TelemetryConditionalTests
	{
		/// <summary>The value of the only counter in the next frame, or zero when there is no frame.</summary>
		internal static long ValueOf(TelemetryMetrics metrics)
		{
			var stream = new MemoryStream();
			metrics.Export(new TelemetryFrameWriter(stream));
			if (stream.Length == 0)
			{
				return 0;
			}
			var file = stream.ToArray();
			var message = new byte[file.Length - 4];
			Array.Copy(file, 4, message, 0, message.Length);
			var fields = ProtobufTestReader.Read(message);
			foreach (var number in new[] { 1, 2, 2, 7, 1 })
			{
				fields = ProtobufTestReader.Read(fields.Single(field => field.Number == number).Bytes);
			}
			return (long)fields.Single(field => field.Number == 6).Value;
		}

		[Test]
		public void WithoutASymbolEveryCallIsGone()
		{
			var metrics = new TelemetryMetrics();
			var counter = metrics.Counter("hits", string.Empty);
			var gauge = metrics.Gauge("memory", string.Empty);
			var histogram = metrics.Histogram("time", string.Empty, null);

			counter.AddBasic(1);
			counter.AddStandard(1);
			counter.AddDetailed(1);
			gauge.SetBasic(1);
			gauge.SetStandard(1);
			gauge.SetDetailed(1);
			histogram.RecordBasic(1);
			histogram.RecordStandard(1);
			histogram.RecordDetailed(1);

			var stream = new MemoryStream();
			metrics.Export(new TelemetryFrameWriter(stream));
			Assert.That(stream.Length, Is.Zero);
		}

		[Test]
		public void TheArgumentsOfARemovedCallAreNotEvaluated()
		{
			var evaluated = false;
			Func<long> value = () =>
			{
				evaluated = true;
				return 1;
			};

			new TelemetryMetrics().Counter("hits", string.Empty).AddBasic(value());

			Assert.That(evaluated, Is.False);
		}
	}
}
