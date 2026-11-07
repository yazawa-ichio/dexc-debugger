using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dexc.Telemetry;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;
using Field = DexcDebugger.Tests.Telemetry.ProtobufTestReader.Field;

namespace DexcDebugger.Tests.Telemetry
{
	/// <summary>
	/// Reads the frames back through the field numbers of opentelemetry-proto, written out here as plain numbers on purpose:
	/// a wrong constant in the package must not be able to agree with itself.
	/// </summary>
	public sealed class TelemetryMetricsTests
	{
		MemoryStream m_Stream;
		TelemetryMetrics m_Metrics;
		TelemetryFrameWriter m_Frames;

		[SetUp]
		public void SetUp()
		{
			m_Stream = new MemoryStream(1 << 16);
			m_Frames = new TelemetryFrameWriter(m_Stream);
			m_Metrics = new TelemetryMetrics();
			m_Metrics.SetResource(new[] { new KeyValuePair<string, string>("service.name", "bench") });
		}

		static List<Field> With(List<Field> fields, int number)
		{
			return fields.Where(field => field.Number == number).ToList();
		}

		static Field One(List<Field> fields, int number)
		{
			return With(fields, number).Single();
		}

		static List<Field> Nested(List<Field> fields, int number)
		{
			return ProtobufTestReader.Read(One(fields, number).Bytes);
		}

		/// <summary>Takes the one frame written so far and returns its ResourceMetrics.</summary>
		List<Field> ExportOneFrame()
		{
			m_Metrics.Export(m_Frames);
			var file = m_Stream.ToArray();
			var length = (file[0] << 24) | (file[1] << 16) | (file[2] << 8) | file[3];
			Assert.That(file.Length, Is.EqualTo(length + 4), "one frame");
			m_Stream.SetLength(0);
			var message = new byte[length];
			Array.Copy(file, 4, message, 0, length);
			return Nested(ProtobufTestReader.Read(message), 1);
		}

		static List<List<Field>> Metrics(List<Field> resourceMetrics)
		{
			var scopeMetrics = Nested(resourceMetrics, 2);
			return With(scopeMetrics, 2).Select(field => ProtobufTestReader.Read(field.Bytes)).ToList();
		}

		[Test]
		public void AFrameCarriesTheResourceAndTheScope()
		{
			m_Metrics.Counter("hits", string.Empty).Add(1, TelemetryDetail.Basic);

			var resourceMetrics = ExportOneFrame();

			var attribute = Nested(Nested(resourceMetrics, 1), 1);
			Assert.That(One(attribute, 1).Text, Is.EqualTo("service.name"));
			Assert.That(One(Nested(attribute, 2), 1).Text, Is.EqualTo("bench"));
			Assert.That(One(Nested(Nested(resourceMetrics, 2), 1), 1).Text, Is.EqualTo("Dexc.Telemetry"));
		}

		[Test]
		public void ACounterIsAMonotonicDeltaSum()
		{
			var counter = m_Metrics.Counter("hits", "{hit}");
			counter.Add(2, TelemetryDetail.Basic);
			counter.Add(3, TelemetryDetail.Basic);

			var metric = Metrics(ExportOneFrame()).Single();

			Assert.That(One(metric, 1).Text, Is.EqualTo("hits"));
			Assert.That(One(metric, 3).Text, Is.EqualTo("{hit}"));
			var sum = Nested(metric, 7);
			Assert.That(One(sum, 2).Value, Is.EqualTo(1UL), "AGGREGATION_TEMPORALITY_DELTA");
			Assert.That(One(sum, 3).Value, Is.EqualTo(1UL), "is_monotonic");
			var point = Nested(sum, 1);
			Assert.That(One(point, 6).Value, Is.EqualTo(5UL), "as_int");
			Assert.That(One(point, 6).WireType, Is.EqualTo(1), "as_int is sfixed64");
		}

		[Test]
		public void AGaugeReportsItsLastValue()
		{
			var gauge = m_Metrics.Gauge("memory", "By");
			gauge.Set(1.5, TelemetryDetail.Basic);
			gauge.Set(2.5, TelemetryDetail.Basic);

			var metric = Metrics(ExportOneFrame()).Single();

			var point = Nested(Nested(metric, 5), 1);
			Assert.That(One(point, 4).Double, Is.EqualTo(2.5), "as_double");
		}

		[Test]
		public void AHistogramReportsItsBucketsAndSummary()
		{
			var histogram = m_Metrics.Histogram("frame.time", "ms", new[] { 10.0, 20.0 });
			foreach (var value in new[] { 5.0, 10.0, 15.0, 25.0 })
			{
				histogram.Record(value, TelemetryDetail.Basic);
			}

			var metric = Metrics(ExportOneFrame()).Single();

			var data = Nested(metric, 9);
			Assert.That(One(data, 2).Value, Is.EqualTo(1UL), "AGGREGATION_TEMPORALITY_DELTA");
			var point = Nested(data, 1);
			Assert.That(One(point, 4).Value, Is.EqualTo(4UL), "count");
			Assert.That(One(point, 5).Double, Is.EqualTo(55.0), "sum");
			Assert.That(One(point, 11).Double, Is.EqualTo(5.0), "min");
			Assert.That(One(point, 12).Double, Is.EqualTo(25.0), "max");
			var buckets = One(point, 6).Bytes;
			Assert.That(
				Enumerable.Range(0, 3).Select(i => BitConverter.ToUInt64(buckets, i * 8)),
				Is.EqualTo(new ulong[] { 2, 1, 1 }),
				"a bucket includes its upper bound"
			);
			var bounds = One(point, 7).Bytes;
			Assert.That(Enumerable.Range(0, 2).Select(i => BitConverter.ToDouble(bounds, i * 8)), Is.EqualTo(new[] { 10.0, 20.0 }));
		}

		[Test]
		public void TimesAreUnixNanosecondsAndIntervalsFollowOneAnother()
		{
			var counter = m_Metrics.Counter("hits", string.Empty);
			counter.Add(1, TelemetryDetail.Basic);
			var first = Nested(Nested(Metrics(ExportOneFrame()).Single(), 7), 1);
			counter.Add(1, TelemetryDetail.Basic);
			var second = Nested(Nested(Metrics(ExportOneFrame()).Single(), 7), 1);

			var now = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000000UL;
			Assert.That(One(first, 3).Value, Is.InRange(now - 60000000000UL, now + 60000000000UL));
			Assert.That(One(first, 2).Value, Is.LessThanOrEqualTo(One(first, 3).Value));
			Assert.That(One(second, 2).Value, Is.EqualTo(One(first, 3).Value), "the next interval starts where the last one ended");
		}

		[Test]
		public void AnExportStartsANewInterval()
		{
			var counter = m_Metrics.Counter("hits", string.Empty);
			var histogram = m_Metrics.Histogram("time", string.Empty, new[] { 10.0 });
			counter.Add(5, TelemetryDetail.Basic);
			histogram.Record(100, TelemetryDetail.Basic);
			ExportOneFrame();
			counter.Add(1, TelemetryDetail.Basic);
			histogram.Record(1, TelemetryDetail.Basic);

			var metrics = Metrics(ExportOneFrame());

			Assert.That(One(Nested(Nested(metrics[0], 7), 1), 6).Value, Is.EqualTo(1UL));
			var point = Nested(Nested(metrics[1], 9), 1);
			Assert.That(One(point, 4).Value, Is.EqualTo(1UL));
			Assert.That(One(point, 5).Double, Is.EqualTo(1.0));
			Assert.That(One(point, 11).Double, Is.EqualTo(1.0), "the minimum starts over");
			Assert.That(One(point, 12).Double, Is.EqualTo(1.0), "the maximum starts over");
		}

		[Test]
		public void OnlyMetricsWithDataAreWritten()
		{
			m_Metrics.Counter("idle", string.Empty);
			m_Metrics.Gauge("idle.gauge", string.Empty);
			m_Metrics.Histogram("idle.histogram", string.Empty, null);
			m_Metrics.Counter("hits", string.Empty).Add(1, TelemetryDetail.Basic);

			var metric = Metrics(ExportOneFrame()).Single();

			Assert.That(One(metric, 1).Text, Is.EqualTo("hits"));
		}

		[Test]
		public void AnIntervalWithoutDataWritesNoFrame()
		{
			m_Metrics.Counter("idle", string.Empty);

			m_Metrics.Export(m_Frames);

			Assert.That(m_Stream.Length, Is.Zero);
		}

		[Test]
		public void AnInstrumentRegisteredAfterAnExportFitsTheNextFrame()
		{
			m_Metrics.Counter("first", string.Empty).Add(1, TelemetryDetail.Basic);
			ExportOneFrame();
			m_Metrics.Histogram("second", string.Empty, Enumerable.Range(1, 200).Select(i => (double)i).ToArray()).Record(1, TelemetryDetail.Basic);

			Assert.That(Metrics(ExportOneFrame()).Count, Is.EqualTo(1));
		}

		[Test]
		public void TheDetailLevelSilencesFinerCalls()
		{
			var counter = m_Metrics.Counter("hits", string.Empty);
			m_Metrics.Detail = TelemetryDetail.Standard;

			counter.Add(1, TelemetryDetail.Basic);
			counter.Add(10, TelemetryDetail.Standard);
			counter.Add(100, TelemetryDetail.Detailed);

			Assert.That(One(Nested(Nested(Metrics(ExportOneFrame()).Single(), 7), 1), 6).Value, Is.EqualTo(11UL));
		}

		[TestCase(0)]
		[TestCase(-5)]
		public void ACounterIgnoresWhatWouldNotGrowIt(long value)
		{
			m_Metrics.Counter("hits", string.Empty).Add(value, TelemetryDetail.Basic);

			m_Metrics.Export(m_Frames);

			Assert.That(m_Stream.Length, Is.Zero);
		}

		[Test]
		public void AHistogramIgnoresNaN()
		{
			m_Metrics.Histogram("time", string.Empty, null).Record(double.NaN, TelemetryDetail.Basic);

			m_Metrics.Export(m_Frames);

			Assert.That(m_Stream.Length, Is.Zero);
		}

		[Test]
		public void TheSameNameReturnsTheSameInstrument()
		{
			Assert.That(m_Metrics.Counter("hits", "s"), Is.SameAs(m_Metrics.Counter("hits", "s")));
			var bounds = new[] { 1.0 };
			Assert.That(m_Metrics.Histogram("time", string.Empty, bounds), Is.SameAs(m_Metrics.Histogram("time", string.Empty, bounds)));
		}

		[Test]
		public void TheBoundsAreCopied()
		{
			var bounds = new[] { 1.0, 2.0 };
			var histogram = m_Metrics.Histogram("time", string.Empty, bounds);

			bounds[1] = 100;
			histogram.Record(50, TelemetryDetail.Basic);

			var point = Nested(Nested(Metrics(ExportOneFrame()).Single(), 9), 1);
			Assert.That(BitConverter.ToUInt64(One(point, 6).Bytes, 16), Is.EqualTo(1UL), "50 is above the bounds as they were given");
		}

		[Test]
		public void ANameTakenByAnotherKindOrShapeIsRefused()
		{
			m_Metrics.Counter("hits", "s");
			m_Metrics.Histogram("time", string.Empty, new[] { 1.0 });

			Assert.Throws<ArgumentException>(() => m_Metrics.Gauge("hits", "s"));
			Assert.Throws<ArgumentException>(() => m_Metrics.Counter("hits", "ms"));
			Assert.Throws<ArgumentException>(() => m_Metrics.Histogram("time", string.Empty, new[] { 2.0 }));
		}

		[TestCase("")]
		[TestCase("1hits")]
		[TestCase("frame time")]
		[TestCase("時間")]
		public void AnInvalidNameIsRefused(string name)
		{
			Assert.Throws<ArgumentException>(() => m_Metrics.Counter(name, string.Empty));
		}

		[Test]
		public void ANameMayUseTheSeparators()
		{
			Assert.DoesNotThrow(() => m_Metrics.Counter("game.frame_time-ms/total2", string.Empty));
		}

		[Test]
		public void AnInvalidUnitIsRefused()
		{
			Assert.Throws<ArgumentException>(() => m_Metrics.Counter("hits", "秒"));
			Assert.Throws<ArgumentException>(() => m_Metrics.Counter("hits", new string('s', 64)));
			Assert.Throws<ArgumentException>(() => m_Metrics.Counter("hits", null));
		}

		[TestCase(new double[0])]
		[TestCase(new[] { 2.0, 1.0 })]
		[TestCase(new[] { 1.0, 1.0 })]
		[TestCase(new[] { double.NaN })]
		[TestCase(new[] { double.PositiveInfinity })]
		public void InvalidBoundsAreRefused(double[] bounds)
		{
			Assert.Throws<ArgumentException>(() => m_Metrics.Histogram("time", string.Empty, bounds));
		}

		[Test]
		public void RecordingDoesNotAllocate()
		{
			var counter = m_Metrics.Counter("hits", string.Empty);
			var gauge = m_Metrics.Gauge("memory", string.Empty);
			var histogram = m_Metrics.Histogram("time", string.Empty, null);
			TestDelegate record = () =>
			{
				counter.Add(1, TelemetryDetail.Basic);
				gauge.Set(1, TelemetryDetail.Basic);
				histogram.Record(16.6, TelemetryDetail.Basic);
			};
			record();

			Assert.That(record, Is.Not.AllocatingGCMemory());
		}

		[Test]
		public void ExportingDoesNotAllocate()
		{
			var counter = m_Metrics.Counter("hits", string.Empty);
			var histogram = m_Metrics.Histogram("time", string.Empty, null);
			TestDelegate export = () =>
			{
				m_Stream.Position = 0;
				counter.Add(1, TelemetryDetail.Basic);
				histogram.Record(16.6, TelemetryDetail.Basic);
				m_Metrics.Export(m_Frames);
			};
			// The first export makes the buffer, so only the second goes the way every later one does.
			export();
			export();

			Assert.That(export, Is.Not.AllocatingGCMemory());
		}
	}
}
