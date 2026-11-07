using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dexc.Telemetry;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace DexcDebugger.Tests.Telemetry
{
	public sealed class TelemetrySourcesTests
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

		/// <summary>The metrics of every frame written so far, by name.</summary>
		Dictionary<string, List<ProtobufTestReader.Field>> Written()
		{
			var metrics = new Dictionary<string, List<ProtobufTestReader.Field>>();
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
				foreach (var field in scopeMetrics.Where(field => field.Number == 2))
				{
					var metric = ProtobufTestReader.Read(field.Bytes);
					metrics[metric.Single(item => item.Number == 1).Text] = metric;
				}
			}
			return metrics;
		}

		static List<ProtobufTestReader.Field> Point(List<ProtobufTestReader.Field> metric, int kind)
		{
			var data = ProtobufTestReader.Read(metric.Single(item => item.Number == kind).Bytes);
			return ProtobufTestReader.Read(data.Single(item => item.Number == 1).Bytes);
		}

		[Test]
		public void FramesBecomeAHistogramOfMilliseconds()
		{
			Start(true);

			m_Root.OnFrame(0.016);
			m_Root.OnFrame(0.032);
			m_Root.Flush();

			var point = Point(Written()["unity.frame.time"], 9);
			Assert.That(point.Single(item => item.Number == 4).Value, Is.EqualTo(2UL));
			Assert.That(point.Single(item => item.Number == 5).Double, Is.EqualTo(48.0).Within(0.001));
		}

		[Test]
		public void AnExportBeforeTheFirstFrameReportsNoMemoryOfZero()
		{
			Start(true);

			m_Root.Flush();

			Assert.That(Written().ContainsKey("unity.memory.used"), Is.False);
		}

		[UnityTest]
		public IEnumerator AnExportReadsTheMemoryCounters()
		{
			Start(true);

			for (var frame = 0; frame < 5; frame++)
			{
				yield return null;
			}
			m_Root.Flush();

			var written = Written();
			foreach (var name in new[] { "unity.memory.used", "unity.memory.reserved", "unity.gc.used", "unity.gc.reserved" })
			{
				Assert.That(written.ContainsKey(name), Is.True, name);
				Assert.That(Point(written[name], 5).Single(item => item.Number == 4).Double, Is.GreaterThan(0.0), name);
			}
		}

		[Test]
		public void CollectionsAreCountedSinceTheStart()
		{
			Start(true);

			GC.Collect();
			m_Root.Flush();

			var point = Point(Written()["unity.gc.collections"], 7);
			Assert.That(point.Single(item => item.Number == 6).Value, Is.GreaterThanOrEqualTo(1UL));
		}

		[Test]
		public void AnExportReportsItsOwnCostInTheNextOne()
		{
			Start(true);
			m_Root.OnFrame(0.016);

			m_Root.Flush();
			Assert.That(Written().ContainsKey("dexc.telemetry.export.time"), Is.False);
			m_Root.Flush();

			var written = Written();
			Assert.That(Point(written["dexc.telemetry.export.time"], 9).Single(item => item.Number == 4).Value, Is.EqualTo(1UL));
			Assert.That(Point(written["dexc.telemetry.export.size"], 7).Single(item => item.Number == 6).Value, Is.GreaterThan(0UL));
		}

		[Test]
		public void TheSourcesCanBeLeftOut()
		{
			Start(false);

			m_Root.OnFrame(0.016);
			m_Root.Flush();

			Assert.That(m_Stream.Length, Is.Zero);
		}

		[Test]
		public void AFrameDoesNotAllocate()
		{
			Start(true);
			TestDelegate frame = () => m_Root.OnFrame(0.016);
			frame();

			AllocationCheck.AssertNone(frame);
		}

		[Test]
		public void AnExportWithTheSourcesDoesNotAllocate()
		{
			Start(true);
			TestDelegate export = () =>
			{
				m_Stream.Position = 0;
				m_Root.OnFrame(0.016);
				m_Root.Flush();
			};
			export();
			export();

			AllocationCheck.AssertNone(export);
		}

		[Test]
		public void StoppingReleasesTheRecorders()
		{
			Start(true);

			Assert.DoesNotThrow(() => m_Root.Stop());
			Assert.That(m_Root.IsRunning, Is.False);
			Assert.DoesNotThrow(() => m_Root.OnFrame(0.016), "a frame after the stop is ignored");
		}
	}
}
