using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Dexc.Telemetry;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;
using Field = DexcDebugger.Tests.Telemetry.ProtobufTestReader.Field;

namespace DexcDebugger.Tests.Telemetry
{
	/// <summary>Reads the frames through the plain field numbers of logs.proto, like the metrics tests.</summary>
	public sealed class TelemetryLogsTests
	{
		MemoryStream m_Stream;
		TelemetryFrameWriter m_Frames;
		TelemetryMetrics m_Metrics;
		TelemetryLogs m_Logs;

		[SetUp]
		public void SetUp()
		{
			m_Stream = new MemoryStream(1 << 16);
			m_Frames = new TelemetryFrameWriter(m_Stream);
			m_Metrics = new TelemetryMetrics();
			m_Metrics.SetResource(new[] { new KeyValuePair<string, string>("service.name", "bench") });
			m_Logs = new TelemetryLogs(m_Metrics);
		}

		List<byte[]> Frames()
		{
			var file = m_Stream.ToArray();
			var frames = new List<byte[]>();
			var position = 0;
			while (position < file.Length)
			{
				var length = (file[position] << 24) | (file[position + 1] << 16) | (file[position + 2] << 8) | file[position + 3];
				var frame = new byte[length];
				Array.Copy(file, position + 4, frame, 0, length);
				frames.Add(frame);
				position += 4 + length;
			}
			return frames;
		}

		static List<Field> Nested(List<Field> fields, int number)
		{
			return ProtobufTestReader.Read(fields.Single(field => field.Number == number).Bytes);
		}

		static List<List<Field>> Records(byte[] frame)
		{
			var resourceLogs = Nested(ProtobufTestReader.Read(frame), 1);
			var scopeLogs = Nested(resourceLogs, 2);
			return scopeLogs.Where(field => field.Number == 2).Select(field => ProtobufTestReader.Read(field.Bytes)).ToList();
		}

		List<List<Field>> DrainAll()
		{
			m_Logs.Drain(m_Frames);
			m_Logs.Flush(m_Frames);
			return Frames().SelectMany(Records).ToList();
		}

		static string Body(List<Field> record)
		{
			return Nested(record, 5).Single(field => field.Number == 1).Text;
		}

		static Dictionary<string, string> Attributes(List<Field> record)
		{
			return record
				.Where(field => field.Number == 6)
				.Select(field => ProtobufTestReader.Read(field.Bytes))
				.ToDictionary(
					attribute => attribute.Single(item => item.Number == 1).Text,
					attribute => Nested(attribute, 2).Single(item => item.Number == 1).Text
				);
		}

		[Test]
		public void ARecordCarriesTimeSeverityAndBody()
		{
			m_Logs.Add(TelemetrySeverity.Warning, "stage あ cleared", null, false);

			var record = DrainAll().Single();

			var now = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000000UL;
			Assert.That(record.Single(field => field.Number == 1).Value, Is.InRange(now - 60000000000UL, now + 60000000000UL));
			Assert.That(record.Single(field => field.Number == 11).Value, Is.EqualTo(record.Single(field => field.Number == 1).Value));
			Assert.That(record.Single(field => field.Number == 2).Value, Is.EqualTo(13UL));
			Assert.That(record.Single(field => field.Number == 3).Text, Is.EqualTo("WARN"));
			Assert.That(Body(record), Is.EqualTo("stage あ cleared"));
		}

		[Test]
		public void AFrameCarriesTheResourceAndTheScope()
		{
			m_Logs.Add(TelemetrySeverity.Info, "x", null, false);
			DrainAll();

			var resourceLogs = Nested(ProtobufTestReader.Read(Frames().Single()), 1);

			var attribute = Nested(Nested(resourceLogs, 1), 1);
			Assert.That(attribute.Single(field => field.Number == 1).Text, Is.EqualTo("service.name"));
			Assert.That(Nested(Nested(resourceLogs, 2), 1).Single(field => field.Number == 1).Text, Is.EqualTo("Dexc.Telemetry"));
		}

		[TestCase(LogType.Log, 9)]
		[TestCase(LogType.Warning, 13)]
		[TestCase(LogType.Error, 17)]
		[TestCase(LogType.Assert, 17)]
		[TestCase(LogType.Exception, 17)]
		public void UnityLogTypesMapToSeverities(LogType type, int severity)
		{
			m_Logs.OnUnityLog("message", string.Empty, type);

			Assert.That(DrainAll().Single().Single(field => field.Number == 2).Value, Is.EqualTo((ulong)severity));
		}

		[Test]
		public void AnExceptionBecomesTheSemanticConventionAttributes()
		{
			m_Logs.OnUnityLog("NullReferenceException: Object is gone: really", "Game.Update () (at Game.cs:3)", LogType.Exception);

			var record = DrainAll().Single();

			var attributes = Attributes(record);
			Assert.That(attributes["exception.type"], Is.EqualTo("NullReferenceException"));
			Assert.That(attributes["exception.message"], Is.EqualTo("Object is gone: really"));
			Assert.That(attributes["exception.stacktrace"], Is.EqualTo("Game.Update () (at Game.cs:3)"));
			Assert.That(Body(record), Does.StartWith("NullReferenceException"));
		}

		[Test]
		public void AnExceptionWithoutAMessageStillHasItsType()
		{
			m_Logs.OnUnityLog("Something", string.Empty, LogType.Exception);

			var attributes = Attributes(DrainAll().Single());

			Assert.That(attributes["exception.type"], Is.EqualTo("Something"));
			Assert.That(attributes.ContainsKey("exception.message"), Is.False);
			Assert.That(attributes.ContainsKey("exception.stacktrace"), Is.False);
		}

		[Test]
		public void AStackIsKeptFromWarningsUp()
		{
			m_Logs.OnUnityLog("info", "stack", LogType.Log);
			m_Logs.OnUnityLog("warning", "stack", LogType.Warning);

			var records = DrainAll();

			Assert.That(Attributes(records[0]), Is.Empty);
			Assert.That(Attributes(records[1])["code.stacktrace"], Is.EqualTo("stack"));
		}

		[Test]
		public void TheMinimumSeveritySilencesLowerRecords()
		{
			m_Logs.Minimum = TelemetrySeverity.Warning;

			m_Logs.Add(TelemetrySeverity.Info, "low", null, false);
			m_Logs.Add(TelemetrySeverity.Warning, "high", null, false);

			Assert.That(Body(DrainAll().Single()), Is.EqualTo("high"));
		}

		[Test]
		public void RecordsWaitForTheFlush()
		{
			m_Logs.Add(TelemetrySeverity.Info, "x", null, false);

			m_Logs.Drain(m_Frames);

			Assert.That(m_Stream.Length, Is.Zero);
		}

		[Test]
		public void AnErrorGoesOutAtOnceWithWhatCameBefore()
		{
			m_Logs.Add(TelemetrySeverity.Info, "before", null, false);
			m_Logs.Add(TelemetrySeverity.Error, "broken", null, false);

			m_Logs.Drain(m_Frames);

			Assert.That(Records(Frames().Single()).Count, Is.EqualTo(2));
		}

		[Test]
		public void NothingQueuedWritesNoFrame()
		{
			m_Logs.Drain(m_Frames);
			m_Logs.Flush(m_Frames);

			Assert.That(m_Stream.Length, Is.Zero);
		}

		[Test]
		public void AFullBufferWritesAFrameAndKeepsEveryRecord()
		{
			var body = new string('x', 4000);
			var count = 0;
			for (var batch = 0; batch < 4; batch++)
			{
				for (var i = 0; i < 20; i++)
				{
					m_Logs.Add(TelemetrySeverity.Info, body + count++, null, false);
				}
				m_Logs.Drain(m_Frames);
			}

			var records = DrainAll();

			Assert.That(Frames().Count, Is.GreaterThan(1));
			Assert.That(records.Count, Is.EqualTo(count));
			Assert.That(Body(records.Last()), Does.EndWith((count - 1).ToString()));
		}

		[Test]
		public void LongTextIsCutWithoutBreakingASurrogatePair()
		{
			var body = new string('x', 4095) + "😀" + new string('y', 100);
			m_Logs.Add(TelemetrySeverity.Info, body, null, false);

			var written = Body(DrainAll().Single());

			Assert.That(written, Is.EqualTo(new string('x', 4095)));
		}

		[Test]
		public void TheLargestRecordFitsAFrame()
		{
			var text = new string('あ', 10000);
			m_Logs.OnUnityLog("Type: " + text, text, LogType.Exception);

			var record = DrainAll().Single();

			Assert.That(Attributes(record)["exception.stacktrace"].Length, Is.EqualTo(4096));
		}

		[Test]
		public void ANullBodyIsAnEmptyOne()
		{
			m_Logs.Add(TelemetrySeverity.Info, null, null, false);

			Assert.That(Body(DrainAll().Single()), Is.Empty);
		}

		[Test]
		public void AFullQueueDropsAndCounts()
		{
			for (var i = 0; i < 1024 + 7; i++)
			{
				m_Logs.Add(TelemetrySeverity.Info, "x", null, false);
			}

			var records = DrainAll();

			Assert.That(records.Count, Is.EqualTo(1024));
			m_Stream.SetLength(0);
			m_Metrics.Export(m_Frames);
			var metric = ProtobufTestReader.Read(Nested(Nested(ProtobufTestReader.Read(Frames().Single()), 1), 2).Single(field => field.Number == 2).Bytes);
			Assert.That(metric.Single(field => field.Number == 1).Text, Is.EqualTo("dexc.telemetry.logs.dropped"));
			Assert.That(Nested(Nested(metric, 7), 1).Single(field => field.Number == 6).Value, Is.EqualTo(7UL));
		}

		[Test]
		public void RecordsFromOtherThreadsArrive()
		{
			const int PerThread = 200;
			var threads = Enumerable.Range(0, 4).Select(index => new Thread(() =>
			{
				for (var i = 0; i < PerThread; i++)
				{
					m_Logs.OnUnityLog("thread " + index, string.Empty, LogType.Log);
				}
			})).ToList();

			threads.ForEach(thread => thread.Start());
			threads.ForEach(thread => thread.Join());

			Assert.That(DrainAll().Count, Is.EqualTo(PerThread * threads.Count));
		}

		[Test]
		public void DrainingWhileThreadsLogLosesNothing()
		{
			const int Total = 20000;
			var thread = new Thread(() =>
			{
				for (var i = 0; i < Total; i++)
				{
					m_Logs.Add(TelemetrySeverity.Info, "x", null, false);
					if (i % 512 == 0)
					{
						Thread.Sleep(1);
					}
				}
			});
			thread.Start();
			while (thread.IsAlive)
			{
				m_Logs.Drain(m_Frames);
			}
			thread.Join();

			var written = DrainAll().Count;
			m_Stream.SetLength(0);
			m_Metrics.Export(m_Frames);
			var dropped = Frames().Count == 0
				? 0
				: (int)Nested(Nested(ProtobufTestReader.Read(Nested(Nested(ProtobufTestReader.Read(Frames().Single()), 1), 2).Single(field => field.Number == 2).Bytes), 7), 1).Single(field => field.Number == 6).Value;

			Assert.That(written + dropped, Is.EqualTo(Total));
		}

		[Test]
		public void EncodingDoesNotAllocate()
		{
			const string body = "NullReferenceException: Object is gone";
			const string stack = "Game.Update ()";
			TestDelegate log = () =>
			{
				m_Stream.Position = 0;
				m_Logs.OnUnityLog(body, stack, LogType.Exception);
				m_Logs.Add(TelemetrySeverity.Info, body, null, false);
				m_Logs.Drain(m_Frames);
			};
			log();

			Assert.That(log, Is.Not.AllocatingGCMemory());
		}
	}
}
