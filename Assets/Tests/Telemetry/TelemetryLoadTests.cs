using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dexc.Telemetry;
using NUnit.Framework;
using Field = DexcDebugger.Tests.Telemetry.ProtobufTestReader.Field;

namespace DexcDebugger.Tests.Telemetry
{
	/// <summary>Loads, read back through the field numbers of opentelemetry-proto written out here.</summary>
	public sealed class TelemetryLoadTests
	{
		TelemetryRoot m_Root;
		MemoryStream m_Metrics;
		MemoryStream m_Traces;

		[SetUp]
		public void SetUp()
		{
			TelemetryRoot.Reset();
			m_Root = TelemetryRoot.Current;
			m_Metrics = new MemoryStream(1 << 16);
			m_Traces = new MemoryStream(1 << 16);
		}

		[TearDown]
		public void TearDown()
		{
			TelemetryRoot.Reset();
		}

		void Start()
		{
			m_Root.Start(new TelemetryFrameWriter(m_Metrics), new TelemetryFrameWriter(new MemoryStream()), new TelemetryFrameWriter(m_Traces), new TelemetryOptions(), new KeyValuePair<string, string>[0], 0);
		}

		/// <summary>The messages in field 2 of field 2 of each frame: metrics of MetricsData, spans of TracesData.</summary>
		static List<List<Field>> Records(MemoryStream stream)
		{
			var records = new List<List<Field>>();
			var file = stream.ToArray();
			var position = 0;
			while (position < file.Length)
			{
				var length = (file[position] << 24) | (file[position + 1] << 16) | (file[position + 2] << 8) | file[position + 3];
				var message = new byte[length];
				Array.Copy(file, position + 4, message, 0, length);
				position += 4 + length;
				foreach (var resource in ProtobufTestReader.Read(message).Where(field => field.Number == 1))
				{
					foreach (var scope in ProtobufTestReader.Read(resource.Bytes).Where(field => field.Number == 2))
					{
						records.AddRange(ProtobufTestReader.Read(scope.Bytes).Where(field => field.Number == 2).Select(field => ProtobufTestReader.Read(field.Bytes)));
					}
				}
			}
			return records;
		}

		static string Text(List<Field> message, int number)
		{
			return message.Single(item => item.Number == number).Text;
		}

		/// <summary>The data point of a metric of this kind of data: 7 is a sum, 9 a histogram.</summary>
		static List<List<Field>> Points(List<List<Field>> metrics, string name, int kind)
		{
			return metrics.Where(metric => Text(metric, 1) == name)
				.Select(metric => ProtobufTestReader.Read(ProtobufTestReader.Read(metric.Single(item => item.Number == kind).Bytes).Single(item => item.Number == 1).Bytes))
				.ToList();
		}

		static string[] Attributes(List<Field> message, int field)
		{
			return message.Where(item => item.Number == field).Select(item =>
			{
				var pair = ProtobufTestReader.Read(item.Bytes);
				return Text(pair, 1) + "=" + Text(ProtobufTestReader.Read(pair.Single(part => part.Number == 2).Bytes), 1);
			}).ToArray();
		}

		[Test]
		public void ALoadIsASpanAndADurationAndItsFramesAreLeftOutOfTheFrameTime()
		{
			Start();

			var load = DexcTelemetry.BeginLoad("stage1", TelemetryLoadKind.Level);
			using (load.Phase("assets"))
			{
				m_Root.OnFrame(0.5);
			}
			m_Root.OnFrame(0.3);
			load.End();
			load.End();
			m_Root.OnFrame(0.016);
			m_Root.Flush();

			var metrics = Records(m_Metrics);
			Assert.That(Points(metrics, "unity.frame.time", 9).Single().Single(item => item.Number == 4).Value, Is.EqualTo(1UL), "only the frame after the load");
			Assert.That(Points(metrics, "dexc.load.frames", 7).Single().Single(item => item.Number == 6).Value, Is.EqualTo(2UL));
			var duration = Points(metrics, "dexc.load.duration", 9).Single();
			Assert.That(Attributes(duration, 9), Is.EqualTo(new[] { "dexc.load.kind=level", "dexc.load.name=stage1" }));
			Assert.That(duration.Single(item => item.Number == 4).Value, Is.EqualTo(1UL), "ended once");

			var spans = Records(m_Traces);
			var span = spans.Single(item => Text(item, 5) == "dexc.load");
			var phase = spans.Single(item => Text(item, 5) == "assets");
			Assert.That(Attributes(span, 9), Is.EqualTo(new[] { "dexc.load.name=stage1", "dexc.load.kind=level" }));
			Assert.That(phase.Single(item => item.Number == 4).Bytes, Is.EqualTo(span.Single(item => item.Number == 2).Bytes), "the phase is a child of the load");
		}

		[Test]
		public void ALoadOutsideARunLeavesTheFramesAlone()
		{
			var before = DexcTelemetry.BeginLoad("boot", TelemetryLoadKind.Cold);
			Start();
			before.End();
			var open = DexcTelemetry.BeginLoad("stage1");
			m_Root.Stop();

			// The stop closed the streams of the first run.
			m_Metrics = new MemoryStream(1 << 16);
			m_Traces = new MemoryStream(1 << 16);
			Start();
			open.End();
			m_Root.OnFrame(0.016);
			m_Root.Flush();

			var metrics = Records(m_Metrics);
			Assert.That(Points(metrics, "unity.frame.time", 9).Single().Single(item => item.Number == 4).Value, Is.EqualTo(1UL), "not a loading frame");
			Assert.That(Points(metrics, "dexc.load.duration", 9), Is.Empty);
		}
	}
}
