#define DEXC_TELEMETRY_LOG_VERBOSE

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Dexc.Telemetry;
using NUnit.Framework;
using UnityEngine;
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
		TelemetryLogger m_Logger;

		[SetUp]
		public void SetUp()
		{
			m_Stream = new MemoryStream(1 << 16);
			m_Frames = new TelemetryFrameWriter(m_Stream);
			m_Metrics = new TelemetryMetrics();
			m_Metrics.SetResource(new[] { new KeyValuePair<string, string>("service.name", "bench") });
			m_Logs = new TelemetryLogs(m_Metrics);
			m_Logs.DefaultLevel = TelemetrySeverity.Verbose;
			m_Logger = m_Logs.Logger("Game");
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

		static List<List<Field>> Scopes(byte[] frame)
		{
			var resourceLogs = Nested(ProtobufTestReader.Read(frame), 1);
			return resourceLogs.Where(field => field.Number == 2).Select(field => ProtobufTestReader.Read(field.Bytes)).ToList();
		}

		static List<List<Field>> Records(byte[] frame)
		{
			return Scopes(frame)
				.SelectMany(scope => scope.Where(field => field.Number == 2).Select(field => ProtobufTestReader.Read(field.Bytes)))
				.ToList();
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

		static Dictionary<string, List<Field>> Values(List<Field> record)
		{
			return record
				.Where(field => field.Number == 6)
				.Select(field => ProtobufTestReader.Read(field.Bytes))
				.ToDictionary(attribute => attribute.Single(item => item.Number == 1).Text, attribute => Nested(attribute, 2));
		}

		static Dictionary<string, string> Attributes(List<Field> record)
		{
			return Values(record).ToDictionary(pair => pair.Key, pair => pair.Value.Single(item => item.Number == 1).Text);
		}

		ulong Dropped()
		{
			m_Stream.SetLength(0);
			m_Metrics.Export(m_Frames);
			var frames = Frames();
			if (frames.Count == 0)
			{
				return 0;
			}
			var metric = ProtobufTestReader.Read(Nested(Nested(ProtobufTestReader.Read(frames.Single()), 1), 2).Single(field => field.Number == 2).Bytes);
			Assert.That(metric.Single(field => field.Number == 1).Text, Is.EqualTo("dexc.telemetry.logs.dropped"));
			return Nested(Nested(metric, 7), 1).Single(field => field.Number == 6).Value;
		}

		[Test]
		public void ARecordCarriesTimeSeverityAndBody()
		{
			m_Logger.Warning("stage あ cleared");

			var record = DrainAll().Single();

			var now = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000000UL;
			Assert.That(record.Single(field => field.Number == 1).Value, Is.InRange(now - 60000000000UL, now + 60000000000UL));
			Assert.That(record.Single(field => field.Number == 11).Value, Is.EqualTo(record.Single(field => field.Number == 1).Value));
			Assert.That(record.Single(field => field.Number == 2).Value, Is.EqualTo(13UL));
			Assert.That(record.Single(field => field.Number == 3).Text, Is.EqualTo("WARN"));
			Assert.That(Body(record), Is.EqualTo("stage あ cleared"));
		}

		[Test]
		public void TheCategoryIsTheScopeName()
		{
			m_Logger.Info("x");
			DrainAll();

			var resourceLogs = Nested(ProtobufTestReader.Read(Frames().Single()), 1);

			var attribute = Nested(Nested(resourceLogs, 1), 1);
			Assert.That(attribute.Single(field => field.Number == 1).Text, Is.EqualTo("service.name"));
			Assert.That(Nested(Nested(resourceLogs, 2), 1).Single(field => field.Number == 1).Text, Is.EqualTo("Game"));
		}

		[Test]
		public void CategoriesKeepTheOrderOfTheRecords()
		{
			var other = m_Logs.Logger("Audio");
			m_Logger.Info("first");
			m_Logger.Info("second");
			other.Info("third");
			m_Logger.Info("fourth");
			DrainAll();

			var scopes = Scopes(Frames().Single());

			Assert.That(scopes.Select(scope => Nested(scope, 1).Single(field => field.Number == 1).Text), Is.EqualTo(new[] { "Game", "Audio", "Game" }));
			Assert.That(Records(Frames().Single()).Select(Body), Is.EqualTo(new[] { "first", "second", "third", "fourth" }));
		}

		[Test]
		public void TheBodyIsTheTemplateAndTheValuesAreTypedAttributes()
		{
			m_Logger.Info("stage {Stage} took {Seconds:F2} by {Who}, clear {Clear}", 3, 1.5, "あ", true);

			var record = DrainAll().Single();

			Assert.That(Body(record), Is.EqualTo("stage {Stage} took {Seconds:F2} by {Who}, clear {Clear}"));
			var values = Values(record);
			Assert.That(values["Stage"].Single().Number, Is.EqualTo(3), "int_value");
			Assert.That(values["Stage"].Single().Value, Is.EqualTo(3UL));
			Assert.That(values["Seconds"].Single().Number, Is.EqualTo(4), "double_value");
			Assert.That(values["Seconds"].Single().Double, Is.EqualTo(1.5));
			Assert.That(values["Who"].Single().Number, Is.EqualTo(1), "string_value");
			Assert.That(values["Who"].Single().Text, Is.EqualTo("あ"));
			Assert.That(values["Clear"].Single().Number, Is.EqualTo(2), "bool_value");
			Assert.That(values["Clear"].Single().Value, Is.EqualTo(1UL));
		}

		[Test]
		public void ANegativeIntegerKeepsItsValue()
		{
			m_Logger.Info("{Delta}", -5L);

			Assert.That((long)Values(DrainAll().Single())["Delta"].Single().Value, Is.EqualTo(-5L));
		}

		[Test]
		public void TheFormattedMessageCanBeTheBody()
		{
			m_Logs.IncludeFormattedMessage = true;

			m_Logger.Info("stage {Stage} took {Seconds:F2}, {{raw}} {Missing}", 3, 1.5);

			var record = DrainAll().Single();
			Assert.That(Body(record), Is.EqualTo("stage 3 took 1.50, {raw} {Missing}"));
			Assert.That(Values(record).ContainsKey("Stage"), Is.True, "the values stay attributes");
		}

		[Test]
		public void BracesThatAreNotHolesAreLeftAlone()
		{
			m_Logger.Info("{{not}} a {Hole} and {unclosed", 1);

			var values = Values(DrainAll().Single());

			Assert.That(values.Keys, Is.EqualTo(new[] { "Hole" }));
		}

		[Test]
		public void AValueWithoutAHoleIsLeftOut()
		{
			m_Logger.Info("{First}", 1, 2);

			var values = Values(DrainAll().Single());

			Assert.That(values.Keys, Is.EqualTo(new[] { "First" }));
		}

		[Test]
		public void AnObjectValueBecomesText()
		{
			m_Logger.Info("{Kind}", TelemetryValue.Of(TelemetrySeverity.Fatal));

			Assert.That(Attributes(DrainAll().Single())["Kind"], Is.EqualTo("Fatal"));
		}

		[Test]
		public void AToStringThatThrowsDoesNotStopTheOutput()
		{
			m_Logger.Info("{Bad}", TelemetryValue.Of(new Throwing()));

			Assert.That(Attributes(DrainAll().Single())["Bad"], Does.Contain("InvalidOperationException"));
		}

		[Test]
		public void ANullValueIsAnEmptyString()
		{
			m_Logger.Info("{Name}", (string)null);

			Assert.That(Attributes(DrainAll().Single())["Name"], Is.Empty);
		}

		[Test]
		public void LoggerAttributesGoOnEveryRecordOfThatLogger()
		{
			var stage = m_Logger.With("stage", 3).With("mode", "hard");

			stage.Info("{Value}", 1);
			m_Logger.Info("plain");

			var records = DrainAll();
			var values = Values(records[0]);
			Assert.That(values.Keys, Is.EqualTo(new[] { "stage", "mode", "Value" }));
			Assert.That(values["stage"].Single().Value, Is.EqualTo(3UL));
			Assert.That(values["mode"].Single().Text, Is.EqualTo("hard"));
			Assert.That(Values(records[1]), Is.Empty);
		}

		[Test]
		public void ALoggerWithAttributesKeepsItsCategoryAndLevel()
		{
			var stage = m_Logger.With("stage", 3);

			m_Logs.SetLevel("Game", TelemetrySeverity.Error);

			Assert.That(stage.Category, Is.EqualTo("Game"));
			Assert.That(stage.IsEnabled(TelemetrySeverity.Info), Is.False);
			Assert.That(stage.IsEnabled(TelemetrySeverity.Error), Is.True);
		}

		[Test]
		public void AnEventCarriesItsName()
		{
			var cleared = m_Logger.With("mode", "hard").WithEvent("game.stage.cleared");

			cleared.Info("stage {Stage}", 3);
			m_Logger.Info("plain");

			var records = DrainAll();
			Assert.That(records[0].Single(field => field.Number == 12).Text, Is.EqualTo("game.stage.cleared"));
			Assert.That(Values(records[0]).Keys, Is.EqualTo(new[] { "mode", "Stage" }));
			Assert.That(records[1].Any(field => field.Number == 12), Is.False);
		}

		[Test]
		public void AnEventKeepsItsNameThroughAttributes()
		{
			var cleared = m_Logger.WithEvent("game.stage.cleared").With("mode", "hard");

			Assert.That(cleared.EventName, Is.EqualTo("game.stage.cleared"));
			Assert.That(cleared.Category, Is.EqualTo("Game"));
		}

		[Test]
		public void AnEventNeedsAName()
		{
			Assert.Throws<ArgumentException>(() => m_Logger.WithEvent(string.Empty));
			Assert.Throws<ArgumentException>(() => m_Logger.WithEvent(null));
		}

		[Test]
		public void AnAttributeNeedsAKey()
		{
			Assert.Throws<ArgumentException>(() => m_Logger.With(string.Empty, 1));
		}

		[Test]
		public void TheSameCategoryIsTheSameLogger()
		{
			Assert.That(m_Logs.Logger("Game"), Is.SameAs(m_Logger));
			Assert.Throws<ArgumentException>(() => m_Logs.Logger(string.Empty));
		}

		[Test]
		public void TheDefaultLevelSilencesLowerRecords()
		{
			m_Logs.DefaultLevel = TelemetrySeverity.Warning;

			m_Logger.Info("low");
			m_Logger.Warning("high");

			Assert.That(Body(DrainAll().Single()), Is.EqualTo("high"));
		}

		[Test]
		public void TheLevelCanBeRaisedAndLoweredLater()
		{
			m_Logs.DefaultLevel = TelemetrySeverity.Info;
			m_Logger.Verbose("before");

			m_Logs.DefaultLevel = TelemetrySeverity.Verbose;
			m_Logger.Verbose("collected");

			m_Logs.DefaultLevel = TelemetrySeverity.Info;
			m_Logger.Verbose("after");

			Assert.That(Body(DrainAll().Single()), Is.EqualTo("collected"));
		}

		[Test]
		public void ACategoryLevelCoversTheCategoriesUnderIt()
		{
			m_Logs.DefaultLevel = TelemetrySeverity.Warning;
			var child = m_Logs.Logger("Game.Stage");
			var other = m_Logs.Logger("GameOver");

			m_Logs.SetLevel("Game", TelemetrySeverity.Verbose);

			Assert.That(child.IsEnabled(TelemetrySeverity.Verbose), Is.True);
			Assert.That(m_Logger.IsEnabled(TelemetrySeverity.Verbose), Is.True);
			Assert.That(other.IsEnabled(TelemetrySeverity.Verbose), Is.False, "a name that only starts the same is another category");
		}

		[Test]
		public void TheLongestCategoryRuleWins()
		{
			m_Logs.SetLevel("Game", TelemetrySeverity.Error);
			m_Logs.SetLevel("Game.Stage", TelemetrySeverity.Debug);

			Assert.That(m_Logs.Logger("Game.Stage.Boss").IsEnabled(TelemetrySeverity.Debug), Is.True);
			Assert.That(m_Logger.IsEnabled(TelemetrySeverity.Debug), Is.False);
		}

		[Test]
		public void ACategoryRuleAppliesToALoggerMadeLater()
		{
			m_Logs.SetLevel("Later", TelemetrySeverity.Fatal);

			Assert.That(m_Logs.Logger("Later").IsEnabled(TelemetrySeverity.Error), Is.False);
		}

		[Test]
		public void ClearingACategoryRuleGoesBackToTheDefault()
		{
			m_Logs.SetLevel("Game", TelemetrySeverity.Fatal);

			m_Logs.ClearLevel("Game");

			Assert.That(m_Logger.IsEnabled(TelemetrySeverity.Verbose), Is.True);
		}

		[Test]
		public void AnExceptionObjectBecomesTheSemanticConventionAttributes()
		{
			Exception thrown;
			try
			{
				throw new InvalidOperationException("broken");
			}
			catch (Exception exception)
			{
				thrown = exception;
			}

			m_Logger.Error(thrown, "stage {Stage} failed", 3);

			var values = Values(DrainAll().Single());
			Assert.That(values["exception.type"].Single().Text, Is.EqualTo("InvalidOperationException"));
			Assert.That(values["exception.message"].Single().Text, Is.EqualTo("broken"));
			Assert.That(values["exception.stacktrace"].Single().Text, Does.Contain("AnExceptionObjectBecomes"));
			Assert.That(values["Stage"].Single().Value, Is.EqualTo(3UL));
		}

		[Test]
		public void AnAdapterMessageIsPlainTextNotATemplate()
		{
			m_Logger.Emit(TelemetrySeverity.Info, "json {\"a\": 1} and {Name}");

			var record = DrainAll().Single();

			Assert.That(Body(record), Is.EqualTo("json {\"a\": 1} and {Name}"));
			Assert.That(Values(record), Is.Empty);
		}

		[Test]
		public void AnAdapterMessageIsNotFormattedEither()
		{
			m_Logs.IncludeFormattedMessage = true;

			m_Logger.Emit(TelemetrySeverity.Info, "{{kept}}");

			Assert.That(Body(DrainAll().Single()), Is.EqualTo("{{kept}}"));
		}

		[TestCase(LogType.Log, 9)]
		[TestCase(LogType.Warning, 13)]
		[TestCase(LogType.Error, 17)]
		[TestCase(LogType.Assert, 17)]
		[TestCase(LogType.Exception, 17)]
		public void UnityLogTypesMapToSeverities(LogType type, int severity)
		{
			UnityLogAdapter.Forward(m_Logger, "message", string.Empty, type);

			Assert.That(DrainAll().Single().Single(field => field.Number == 2).Value, Is.EqualTo((ulong)severity));
		}

		[Test]
		public void AUnityExceptionBecomesTheSemanticConventionAttributes()
		{
			UnityLogAdapter.Forward(m_Logger, "NullReferenceException: Object is gone: really", "Game.Update () (at Game.cs:3)", LogType.Exception);

			var record = DrainAll().Single();

			var attributes = Attributes(record);
			Assert.That(attributes["exception.type"], Is.EqualTo("NullReferenceException"));
			Assert.That(attributes["exception.message"], Is.EqualTo("Object is gone: really"));
			Assert.That(attributes["exception.stacktrace"], Is.EqualTo("Game.Update () (at Game.cs:3)"));
			Assert.That(Body(record), Does.StartWith("NullReferenceException"));
		}

		[Test]
		public void AUnityExceptionWithoutAMessageStillHasItsType()
		{
			UnityLogAdapter.Forward(m_Logger, "Something", string.Empty, LogType.Exception);

			var attributes = Attributes(DrainAll().Single());

			Assert.That(attributes["exception.type"], Is.EqualTo("Something"));
			Assert.That(attributes.ContainsKey("exception.message"), Is.False);
			Assert.That(attributes.ContainsKey("exception.stacktrace"), Is.False);
		}

		[Test]
		public void AStackIsKeptFromWarningsUp()
		{
			UnityLogAdapter.Forward(m_Logger, "info", "stack", LogType.Log);
			UnityLogAdapter.Forward(m_Logger, "warning", "stack", LogType.Warning);

			var records = DrainAll();

			Assert.That(Attributes(records[0]), Is.Empty);
			Assert.That(Attributes(records[1])["code.stacktrace"], Is.EqualTo("stack"));
		}

		[Test]
		public void RecordsWaitForTheFlush()
		{
			m_Logger.Info("x");

			m_Logs.Drain(m_Frames);

			Assert.That(m_Stream.Length, Is.Zero);
		}

		[Test]
		public void AnErrorGoesOutAtOnceWithWhatCameBefore()
		{
			m_Logger.Info("before");
			m_Logger.Error("broken");

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
			var other = m_Logs.Logger("Other");
			var count = 0;
			for (var batch = 0; batch < 4; batch++)
			{
				for (var i = 0; i < 20; i++)
				{
					(i % 3 == 0 ? other : m_Logger).Emit(TelemetrySeverity.Info, body + count++);
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
			m_Logger.Emit(TelemetrySeverity.Info, body);

			Assert.That(Body(DrainAll().Single()), Is.EqualTo(new string('x', 4095)));
		}

		[Test]
		public void TheLargestRecordFitsAFrame()
		{
			var text = new string('あ', 10000);
			m_Logger.Info(text + "{A}{B}{C}{D}", text, text, text, text);
			UnityLogAdapter.Forward(m_Logger, "Type: " + text, text, LogType.Exception);

			var records = DrainAll();

			Assert.That(records.Count, Is.EqualTo(2));
			Assert.That(Attributes(records[0])["A"].Length, Is.EqualTo(1024));
			Assert.That(Attributes(records[1])["exception.stacktrace"].Length, Is.EqualTo(4096));
		}

		[Test]
		public void ARecordLargerThanAFrameIsDroppedAndCounted()
		{
			var huge = m_Logger;
			for (var i = 0; i < 30; i++)
			{
				huge = huge.With("key" + i, new string('あ', 1000));
			}

			huge.Info("too large");
			m_Logger.Info("next");

			Assert.That(Body(DrainAll().Single()), Is.EqualTo("next"));
			Assert.That(Dropped(), Is.EqualTo(1UL));
		}

		[Test]
		public void ANullBodyIsAnEmptyOne()
		{
			m_Logger.Emit(TelemetrySeverity.Info, null);

			Assert.That(Body(DrainAll().Single()), Is.Empty);
		}

		[Test]
		public void AFullQueueDropsAndCounts()
		{
			for (var i = 0; i < 1024 + 7; i++)
			{
				m_Logger.Info("x");
			}

			Assert.That(DrainAll().Count, Is.EqualTo(1024));
			Assert.That(Dropped(), Is.EqualTo(7UL));
		}

		[Test]
		public void RecordsFromOtherThreadsArrive()
		{
			const int PerThread = 200;
			var threads = Enumerable.Range(0, 4).Select(index => new Thread(() =>
			{
				var logger = m_Logs.Logger("Thread" + index);
				for (var i = 0; i < PerThread; i++)
				{
					logger.Info("{Index}", i);
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
					m_Logger.Info("x");
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

			Assert.That((ulong)written + Dropped(), Is.EqualTo((ulong)Total));
		}

		[Test]
		public void LoggersThatStaySilentMakeNoBuffers()
		{
			var logs = new TelemetryLogs(m_Metrics);
			logs.DefaultLevel = TelemetrySeverity.Warning;

			logs.Logger("Quiet").Info("below the level");
			logs.Drain(m_Frames);
			logs.Flush(m_Frames);

			const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
			Assert.That(typeof(TelemetryLogs).GetField("m_Incoming", Flags).GetValue(logs), Is.Null);
			var batch = (TelemetryBatch)typeof(TelemetryLogs).GetField("m_Batch", Flags).GetValue(logs);
			Assert.That(batch.Writer, Is.Null);
		}

		[Test]
		public void ACallBelowTheLevelDoesNotAllocate()
		{
			m_Logs.DefaultLevel = TelemetrySeverity.Info;
			const string name = "boss";
			TestDelegate log = () =>
			{
				m_Logger.Verbose("plain");
				m_Logger.Verbose("stage {Stage} took {Seconds} by {Who}, clear {Clear}", 3, 1.5, name, true);
			};
			log();

			AllocationCheck.AssertNone(log);
		}

		[Test]
		public void ARecordWithPrimitiveValuesDoesNotAllocate()
		{
			var stage = m_Logger.With("mode", "hard");
			const string name = "boss";
			TestDelegate log = () =>
			{
				m_Stream.Position = 0;
				stage.Info("stage {Stage} took {Seconds} by {Who}, clear {Clear}", 3, 1.5, name, true);
				m_Logs.Drain(m_Frames);
				m_Logs.Flush(m_Frames);
			};
			log();

			AllocationCheck.AssertNone(log);
		}

		[Test]
		public void AUnityExceptionDoesNotAllocateHere()
		{
			const string body = "NullReferenceException: Object is gone";
			const string stack = "Game.Update ()";
			TestDelegate log = () =>
			{
				m_Stream.Position = 0;
				UnityLogAdapter.Forward(m_Logger, body, stack, LogType.Exception);
				m_Logs.Drain(m_Frames);
			};
			log();

			AllocationCheck.AssertNone(log);
		}

		sealed class Throwing
		{
			public override string ToString()
			{
				throw new InvalidOperationException("no text");
			}
		}
	}
}
