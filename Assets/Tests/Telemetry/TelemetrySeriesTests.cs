using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dexc.Telemetry;
using NUnit.Framework;
using Field = DexcDebugger.Tests.Telemetry.ProtobufTestReader.Field;

namespace DexcDebugger.Tests.Telemetry
{
	/// <summary>
	/// Series and annotations, read back through the field numbers of opentelemetry-proto written out here: attributes are
	/// field 7 of NumberDataPoint and field 9 of HistogramDataPoint.
	/// </summary>
	public sealed class TelemetrySeriesTests
	{
		TelemetryRoot m_Root;
		MemoryStream m_Stream;

		[SetUp]
		public void SetUp()
		{
			TelemetryRoot.Reset();
			m_Root = TelemetryRoot.Current;
			m_Stream = new MemoryStream(1 << 16);
		}

		[TearDown]
		public void TearDown()
		{
			TelemetryRoot.Reset();
		}

		void Start(bool collect)
		{
			var options = new TelemetryOptions { CollectUnityMetrics = collect };
			m_Root.Start(new TelemetryFrameWriter(m_Stream), new TelemetryFrameWriter(new MemoryStream()), new TelemetryFrameWriter(new MemoryStream()), options, new KeyValuePair<string, string>[0], 0);
		}

		/// <summary>Every metric of every frame written so far, in order.</summary>
		List<List<Field>> Written()
		{
			var metrics = new List<List<Field>>();
			var file = m_Stream.ToArray();
			var position = 0;
			while (position < file.Length)
			{
				var length = (file[position] << 24) | (file[position + 1] << 16) | (file[position + 2] << 8) | file[position + 3];
				var message = new byte[length];
				Array.Copy(file, position + 4, message, 0, length);
				position += 4 + length;
				var resourceMetrics = ProtobufTestReader.Read(ProtobufTestReader.Read(message).Single().Bytes);
				var scopeMetrics = ProtobufTestReader.Read(resourceMetrics.Single(field => field.Number == 2).Bytes);
				metrics.AddRange(scopeMetrics.Where(field => field.Number == 2).Select(field => ProtobufTestReader.Read(field.Bytes)));
			}
			return metrics;
		}

		static string Name(List<Field> metric)
		{
			return metric.Single(item => item.Number == 1).Text;
		}

		static List<Field> Point(List<Field> metric, int kind)
		{
			var data = ProtobufTestReader.Read(metric.Single(item => item.Number == kind).Bytes);
			return ProtobufTestReader.Read(data.Single(item => item.Number == 1).Bytes);
		}

		/// <summary>The string attributes of a data point, as "key=value".</summary>
		static string[] Attributes(List<Field> point, int field)
		{
			return point.Where(item => item.Number == field).Select(item =>
			{
				var pair = ProtobufTestReader.Read(item.Bytes);
				var value = ProtobufTestReader.Read(pair.Single(part => part.Number == 2).Bytes).Single(part => part.Number == 1).Text;
				return pair.Single(part => part.Number == 1).Text + "=" + value;
			}).ToArray();
		}

		[Test]
		public void ASeriesIsTheSameMetricWithItsAttributesOnTheDataPoint()
		{
			Start(false);
			var histogram = DexcTelemetry.Histogram("load", "ms", new double[] { 10, 100 });
			var counter = DexcTelemetry.Counter("hits");

			histogram.Record(5, TelemetryDetail.Basic);
			histogram.Series("stage", "2").Record(50, TelemetryDetail.Basic);
			counter.Series("stage", "2").Add(3, TelemetryDetail.Basic);
			m_Root.Flush();

			var metrics = Written();
			var loads = metrics.Where(metric => Name(metric) == "load").Select(metric => Point(metric, 9)).ToList();
			Assert.That(loads, Has.Count.EqualTo(2));
			Assert.That(Attributes(loads[0], 9), Is.Empty, "the metric itself");
			Assert.That(Attributes(loads[1], 9), Is.EqualTo(new[] { "stage=2" }));
			Assert.That(loads[1].Single(item => item.Number == 5).Double, Is.EqualTo(50.0));
			var hits = Point(metrics.Single(metric => Name(metric) == "hits"), 7);
			Assert.That(Attributes(hits, 7), Is.EqualTo(new[] { "stage=2" }), "only the series had data");
		}

		[Test]
		public void KeysInAnyOrderAreOneSeriesAndTheValuesPastTheLimitAreOther()
		{
			var histogram = DexcTelemetry.Histogram("frame", "ms");

			Assert.That(histogram.Series("mode", "a", "stage", "1"), Is.SameAs(histogram.Series("stage", "1", "mode", "a")));
			for (var i = 2; i < 40; i++)
			{
				histogram.Series("mode", "a", "stage", i.ToString());
			}
			var other = histogram.Series("mode", "b", "stage", "99");

			Assert.That(other.Attributes.Select(pair => pair.Key + "=" + pair.Value), Is.EqualTo(new[] { "mode=other", "stage=other" }));
			Assert.That(histogram.Series("mode", "c", "stage", "100"), Is.SameAs(other));
			Assert.That(() => other.Series("x", "1"), Throws.InvalidOperationException);
			Assert.That(() => histogram.Series("stage", "1", "stage", "2"), Throws.ArgumentException);
		}

		[Test]
		public void AnnotationsSplitTheFramesOfUnity()
		{
			Start(true);

			DexcTelemetry.Annotate("stage", "1");
			m_Root.OnFrame(0.016);
			DexcTelemetry.Annotate("stage", "2");
			m_Root.OnFrame(0.032);
			m_Root.OnFrame(0.032);
			DexcTelemetry.Annotate("stage", null);
			m_Root.OnFrame(0.010);
			m_Root.Flush();

			var frames = Written().Where(metric => Name(metric) == "unity.frame.time").Select(metric => Point(metric, 9))
				.ToDictionary(point => string.Join(",", Attributes(point, 9)), point => point.Single(item => item.Number == 4).Value);
			Assert.That(frames, Is.EqualTo(new Dictionary<string, ulong> { [""] = 1, ["stage=1"] = 1, ["stage=2"] = 2 }));
		}

		[Test]
		public void TwoAnnotationsAtMostInKeyOrder()
		{
			var annotations = new TelemetryAnnotations();

			annotations.Set("stage", "1");
			annotations.Set("mode", "hard");
			Assert.That(annotations.Pairs().Select(pair => pair.Key), Is.EqualTo(new[] { "mode", "stage" }));
			Assert.That(() => annotations.Set("difficulty", "2"), Throws.InvalidOperationException);
			var version = annotations.Version;
			annotations.Set("mode", "hard");
			Assert.That(annotations.Version, Is.EqualTo(version), "the same value changes nothing");

			annotations.Set("mode", null);
			Assert.That(annotations.Pairs().Select(pair => pair.Key + "=" + pair.Value), Is.EqualTo(new[] { "stage=1" }));
		}

		[Test]
		public void AnAnnotatedFrameDoesNotAllocate()
		{
			Start(true);
			DexcTelemetry.Annotate("stage", "1");
			m_Root.OnFrame(0.016);

			AllocationCheck.AssertNone(() => m_Root.OnFrame(0.016));
		}
	}
}
