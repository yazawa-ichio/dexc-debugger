using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dexc.Telemetry;
using NUnit.Framework;
using Field = DexcDebugger.Tests.Telemetry.ProtobufTestReader.Field;

namespace DexcDebugger.Tests.Telemetry
{
	/// <summary>Hitches and positions, read back through the field numbers of opentelemetry-proto written out here.</summary>
	public sealed class TelemetryFrameEventsTests
	{
		sealed class FixedLocation : ITelemetryLocation
		{
			public bool TryGetLocation(out string map, out float x, out float y, out float z)
			{
				map = "yard";
				x = 1;
				y = 2;
				z = 3;
				return true;
			}
		}

		TelemetryRoot m_Root;
		MemoryStream m_Metrics;
		MemoryStream m_Logs;

		[SetUp]
		public void SetUp()
		{
			TelemetryRoot.Reset();
			m_Root = TelemetryRoot.Current;
			m_Metrics = new MemoryStream(1 << 16);
			m_Logs = new MemoryStream(1 << 16);
		}

		[TearDown]
		public void TearDown()
		{
			TelemetryRoot.Reset();
		}

		void Start(TelemetryOptions options)
		{
			options.Location = new FixedLocation();
			m_Root.Start(new TelemetryFrameWriter(m_Metrics), new TelemetryFrameWriter(m_Logs), new TelemetryFrameWriter(new MemoryStream()), options, new KeyValuePair<string, string>[0], 0);
		}

		/// <summary>The messages in field 2 of field 2 of each frame: metrics of MetricsData, records of LogsData.</summary>
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

		/// <summary>The records of an event, field 12 of LogRecord.</summary>
		List<List<Field>> Events(string name)
		{
			return Records(m_Logs).Where(record => record.Any(item => item.Number == 12 && item.Text == name)).ToList();
		}

		/// <summary>Attributes, field 6 of LogRecord, as text: a string as it is, a bool, an int or a double printed.</summary>
		static Dictionary<string, string> Attributes(List<Field> record)
		{
			return record.Where(item => item.Number == 6).Select(item => ProtobufTestReader.Read(item.Bytes)).ToDictionary(
				pair => pair.Single(part => part.Number == 1).Text,
				pair =>
				{
					var value = ProtobufTestReader.Read(pair.Single(part => part.Number == 2).Bytes).Single();
					return value.Number == 1 ? value.Text : value.Number == 2 ? (value.Value != 0).ToString() : value.Number == 3 ? value.Value.ToString() : value.Double.ToString("0.###");
				});
		}

		/// <summary>The value of a counter, field 6 of the data point of field 7 of Metric, by its attributes.</summary>
		Dictionary<string, ulong> Counter(string name)
		{
			return Records(m_Metrics).Where(metric => metric.Single(item => item.Number == 1).Text == name)
				.Select(metric => ProtobufTestReader.Read(ProtobufTestReader.Read(metric.Single(item => item.Number == 7).Bytes).Single(item => item.Number == 1).Bytes))
				.ToDictionary(
					point => string.Join(",", point.Where(item => item.Number == 7).Select(item => ProtobufTestReader.Read(item.Bytes)).Select(pair =>
						pair.Single(part => part.Number == 1).Text + "=" + ProtobufTestReader.Read(pair.Single(part => part.Number == 2).Bytes).Single().Text)),
					point => point.Single(item => item.Number == 6).Value);
		}

		[Test]
		public void AHitchIsCountedAndWrittenWithWhatTheGameDidAndWhere()
		{
			Start(new TelemetryOptions { HitchMilliseconds = 50 });
			DexcTelemetry.Annotate("stage", "1");

			m_Root.OnFrame(0.016);
			m_Root.OnFrame(0.2);
			m_Root.Flush();

			var hitch = Attributes(Events("dexc.hitch").Single());
			Assert.That(hitch["unity.frame.time"], Is.EqualTo("200"));
			Assert.That(hitch["dexc.loading"], Is.EqualTo("False"));
			Assert.That(hitch["stage"], Is.EqualTo("1"));
			Assert.That(hitch["dexc.map"], Is.EqualTo("yard"));
			Assert.That(hitch["dexc.position.z"], Is.EqualTo("3"));
			Assert.That(Counter("dexc.hitches"), Is.EqualTo(new Dictionary<string, ulong> { ["stage=1"] = 1 }));
		}

		[Test]
		public void HitchEventsStopAtTheLimitButAllAreCounted()
		{
			Start(new TelemetryOptions { HitchMilliseconds = 50, HitchEventsPerExport = 2 });

			for (var i = 0; i < 5; i++)
			{
				m_Root.OnFrame(0.1);
			}
			m_Root.Flush();

			Assert.That(Events("dexc.hitch"), Has.Count.EqualTo(2));
			Assert.That(Counter("dexc.hitches"), Is.EqualTo(new Dictionary<string, ulong> { [""] = 5 }));
		}

		[Test]
		public void PositionsComeAtTheInterval()
		{
			Start(new TelemetryOptions { HitchMilliseconds = 0, PositionSeconds = 1 });

			for (var i = 0; i < 5; i++)
			{
				m_Root.OnFrame(0.5);
			}
			m_Root.Flush();

			var positions = Events("dexc.position");
			Assert.That(positions, Has.Count.EqualTo(2));
			Assert.That(Attributes(positions[0])["dexc.position.x"], Is.EqualTo("1"));
			Assert.That(Events("dexc.hitch"), Is.Empty, "hitches are off");
		}
	}
}
