#define DEXC_TELEMETRY_LOG_VERBOSE
// Without it the IL processing would turn the span starts of this file into empty spans.
#define DEXC_TELEMETRY_DETAIL_DETAILED

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Dexc.Telemetry;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Field = DexcDebugger.Tests.Telemetry.ProtobufTestReader.Field;

namespace DexcDebugger.Tests.Telemetry
{
	/// <summary>Reads the frames through the plain field numbers of trace.proto, like the metrics tests.</summary>
	public sealed class TelemetryTracesTests
	{
		TelemetryRoot m_Root;
		MemoryStream m_Traces;
		MemoryStream m_Logs;
		TelemetryTracer m_Tracer;

		[SetUp]
		public void SetUp()
		{
			TelemetryRoot.Reset();
			m_Root = TelemetryRoot.Current;
			m_Traces = new MemoryStream(1 << 16);
			m_Logs = new MemoryStream(1 << 16);
			m_Tracer = m_Root.Traces.Tracer("Game");
		}

		[TearDown]
		public void TearDown()
		{
			TelemetryRoot.Reset();
		}

		void Start(bool ambient = false, TelemetryDetail detail = TelemetryDetail.Detailed)
		{
			var options = new TelemetryOptions { CollectUnityMetrics = false, CollectUnityLogs = false, AmbientContext = ambient, Detail = detail };
			m_Root.Start(
				new TelemetryFrameWriter(new MemoryStream()),
				new TelemetryFrameWriter(m_Logs),
				new TelemetryFrameWriter(m_Traces),
				options,
				new[] { new KeyValuePair<string, string>("service.name", "bench") },
				0
			);
		}

		static List<byte[]> Frames(MemoryStream stream)
		{
			var file = stream.ToArray();
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
			var resource = Nested(ProtobufTestReader.Read(frame), 1);
			return resource
				.Where(field => field.Number == 2)
				.SelectMany(scope => ProtobufTestReader.Read(scope.Bytes).Where(field => field.Number == 2))
				.Select(field => ProtobufTestReader.Read(field.Bytes))
				.ToList();
		}

		List<List<Field>> Spans()
		{
			m_Root.Flush();
			return Frames(m_Traces).SelectMany(Records).ToList();
		}

		static Field One(List<Field> fields, int number)
		{
			return fields.Single(field => field.Number == number);
		}

		static string Hex(Field field)
		{
			return BitConverter.ToString(field.Bytes);
		}

		[Test]
		public void ASpanCarriesItsIdsNameAndTimes()
		{
			Start();

			m_Tracer.Start("stage.load").Dispose();

			var span = Spans().Single();
			Assert.That(One(span, 1).Bytes.Length, Is.EqualTo(16), "trace_id");
			Assert.That(One(span, 2).Bytes.Length, Is.EqualTo(8), "span_id");
			Assert.That(One(span, 1).Bytes.Any(value => value != 0), Is.True);
			Assert.That(One(span, 2).Bytes.Any(value => value != 0), Is.True);
			Assert.That(span.Any(field => field.Number == 4), Is.False, "a root has no parent");
			Assert.That(One(span, 5).Text, Is.EqualTo("stage.load"));
			Assert.That(One(span, 6).Value, Is.EqualTo(1UL), "SPAN_KIND_INTERNAL");
			var now = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000000UL;
			Assert.That(One(span, 7).Value, Is.InRange(now - 60000000000UL, now + 60000000000UL));
			Assert.That(One(span, 8).Value, Is.GreaterThanOrEqualTo(One(span, 7).Value));
		}

		[Test]
		public void ATracerIsTheScope()
		{
			Start();

			m_Tracer.Start("x").Dispose();
			m_Root.Flush();

			var resource = Nested(ProtobufTestReader.Read(Frames(m_Traces).Single()), 1);
			Assert.That(One(Nested(resource, 1), 1).Bytes, Is.Not.Empty, "the resource comes first");
			Assert.That(One(Nested(Nested(resource, 2), 1), 1).Text, Is.EqualTo("Game"));
		}

		[Test]
		public void TheSameNameIsTheSameTracer()
		{
			Assert.That(m_Root.Traces.Tracer("Game"), Is.SameAs(m_Tracer));
			Assert.Throws<ArgumentException>(() => m_Root.Traces.Tracer(string.Empty));
		}

		[Test]
		public void AChildSharesTheTraceAndNamesItsParent()
		{
			Start();

			var parent = m_Tracer.Start("parent");
			m_Tracer.Start("child", parent.Context).Dispose();
			parent.Dispose();

			var spans = Spans();
			var child = spans[0];
			var root = spans[1];
			Assert.That(Hex(One(child, 1)), Is.EqualTo(Hex(One(root, 1))));
			Assert.That(Hex(One(child, 4)), Is.EqualTo(Hex(One(root, 2))));
			Assert.That(Hex(One(child, 2)), Is.Not.EqualTo(Hex(One(root, 2))));
		}

		[Test]
		public void WithoutAParentASpanStartsANewTrace()
		{
			Start();

			m_Tracer.Start("first").Dispose();
			m_Tracer.Start("second").Dispose();

			var spans = Spans();
			Assert.That(Hex(One(spans[0], 1)), Is.Not.EqualTo(Hex(One(spans[1], 1))));
		}

		[Test]
		public void AttributesKeepTheirTypesAndTheRestIsCounted()
		{
			Start();

			var span = m_Tracer.Start("x");
			span.Set("int", 3);
			span.Set("double", 1.5);
			span.Set("text", "boss");
			span.Set("bool", true);
			span.Set("e", 5);
			span.Set("f", 6);
			span.Set("g", 7);
			span.Set("h", 8);
			span.Set("one too many", 1);
			span.Set("two too many", 1);
			span.Dispose();

			var record = Spans().Single();
			var attributes = record.Where(field => field.Number == 9).Select(field => ProtobufTestReader.Read(field.Bytes)).ToList();
			Assert.That(attributes.Select(attribute => One(attribute, 1).Text), Is.EqualTo(new[] { "int", "double", "text", "bool", "e", "f", "g", "h" }));
			Assert.That(Nested(attributes[0], 2).Single().Value, Is.EqualTo(3UL));
			Assert.That(One(record, 10).Value, Is.EqualTo(2UL), "dropped_attributes_count");
		}

		[Test]
		public void AKeySetAgainTakesTheNewValue()
		{
			Start();

			var span = m_Tracer.Start("x");
			span.Set("stage", 1);
			span.Set("mode", "hard");
			span.Set("stage", 2);
			span.Dispose();

			var record = Spans().Single();
			var attributes = record.Where(field => field.Number == 9).Select(field => ProtobufTestReader.Read(field.Bytes)).ToList();
			Assert.That(attributes.Select(attribute => One(attribute, 1).Text), Is.EqualTo(new[] { "stage", "mode" }));
			Assert.That(Nested(attributes[0], 2).Single().Value, Is.EqualTo(2UL));
			Assert.That(record.Any(field => field.Number == 10), Is.False);
		}

		[Test]
		public void AnErrorSetsTheStatus()
		{
			Start();

			var span = m_Tracer.Start("x");
			span.SetError("broken");
			span.Dispose();
			m_Tracer.Start("fine").Dispose();

			var spans = Spans();
			var status = Nested(spans[0], 15);
			Assert.That(One(status, 2).Text, Is.EqualTo("broken"));
			Assert.That(One(status, 3).Value, Is.EqualTo(2UL), "STATUS_CODE_ERROR");
			Assert.That(spans[1].Any(field => field.Number == 15), Is.False, "the slot starts clean again");
		}

		[Test]
		public void BeforeTheStartASpanIsEmpty()
		{
			var span = m_Tracer.Start("x");

			Assert.That(span.Context.IsValid, Is.False);
			Assert.DoesNotThrow(() =>
			{
				span.Set("key", 1);
				span.SetError("x");
				span.Dispose();
			});
		}

		[Test]
		public void TheDefaultSpanDoesNothing()
		{
			var span = default(TelemetrySpan);

			Assert.DoesNotThrow(() =>
			{
				span.Set("key", 1);
				span.Dispose();
			});
		}

		[Test]
		public void AFinerDetailThanTheLevelIsNotRecorded()
		{
			Start(detail: TelemetryDetail.Standard);

			m_Tracer.Start("fine", detail: TelemetryDetail.Detailed).Dispose();
			m_Tracer.Start("coarse", detail: TelemetryDetail.Basic).Dispose();

			Assert.That(One(Spans().Single(), 5).Text, Is.EqualTo("coarse"));
		}

		[Test]
		public void EndingTwiceWritesOnce()
		{
			Start();

			var span = m_Tracer.Start("x");
			span.Dispose();
			span.Dispose();

			Assert.That(Spans().Count, Is.EqualTo(1));
		}

		[Test]
		public void AnOldHandleDoesNotTouchTheSpanThatReusedItsSlot()
		{
			Start();

			var old = m_Tracer.Start("old");
			old.Dispose();
			var current = m_Tracer.Start("current");

			old.Set("stale", 1);
			old.Dispose();
			current.Dispose();

			var spans = Spans();
			Assert.That(spans.Count, Is.EqualTo(2));
			Assert.That(spans[1].Any(field => field.Number == 9), Is.False);
		}

		[Test]
		public void AnEndOnAnotherThreadIsWrittenWithItsAttributes()
		{
			Start();
			var span = m_Tracer.Start("x");

			Task.Run(() =>
			{
				span.Set("stage", 3);
				span.Dispose();
			}).Wait();

			var record = Spans().Single();
			var attribute = ProtobufTestReader.Read(One(record, 9).Bytes);
			Assert.That(One(attribute, 1).Text, Is.EqualTo("stage"));
			Assert.That(One(record, 8).Value, Is.GreaterThanOrEqualTo(One(record, 7).Value));
		}

		[Test]
		public void EndingTwiceOnAnotherThreadWritesOnce()
		{
			Start();
			var span = m_Tracer.Start("x");

			Task.Run(() =>
			{
				span.Dispose();
				span.Dispose();
			}).Wait();
			span.Dispose();

			Assert.That(Spans().Count, Is.EqualTo(1));
		}

		[Test]
		public void TheSlotOfAnEndOnAnotherThreadComesBackOnTheNextFrame()
		{
			Start();
			var open = Enumerable.Range(0, 256).Select(_ => m_Tracer.Start("open")).ToList();

			Task.Run(() => open[0].Dispose()).Wait();
			Assert.That(m_Tracer.Start("before").Context.IsValid, Is.False, "the end is still queued");

			m_Root.OnFrame(0.016);

			Assert.That(m_Tracer.Start("after").Context.IsValid, Is.True);
		}

		[Test]
		public void TooManyOpenSpansAreDroppedAndCounted()
		{
			Start();

			var open = Enumerable.Range(0, 256).Select(_ => m_Tracer.Start("open")).ToList();
			Assert.That(open.All(span => span.Context.IsValid), Is.True);

			var extra = m_Tracer.Start("extra");

			Assert.That(extra.Context.IsValid, Is.False);
			open.ForEach(span => span.Dispose());
			Assert.That(m_Tracer.Start("again").Context.IsValid, Is.True, "the slots came back");
		}

		[Test]
		public void AFullBufferWritesAFrameAndKeepsEverySpan()
		{
			Start();
			var other = m_Root.Traces.Tracer("Other");
			var name = new string('x', 1000);

			for (var i = 0; i < 200; i++)
			{
				(i % 3 == 0 ? other : m_Tracer).Start(name).Dispose();
			}

			var spans = Spans();
			Assert.That(Frames(m_Traces).Count, Is.GreaterThan(1));
			Assert.That(spans.Count, Is.EqualTo(200));
		}

		[Test]
		public void AFailedWriteEndsTheOutputWithoutThrowing()
		{
			var options = new TelemetryOptions { CollectUnityMetrics = false, CollectUnityLogs = false };
			m_Root.Start(
				new TelemetryFrameWriter(new MemoryStream()),
				new TelemetryFrameWriter(new MemoryStream()),
				new TelemetryFrameWriter(new FailingStream()),
				options,
				new KeyValuePair<string, string>[0],
				0
			);
			LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("disk full"));
			var name = new string('x', 1000);

			Assert.DoesNotThrow(() =>
			{
				for (var i = 0; i < 200; i++)
				{
					m_Tracer.Start(name).Dispose();
				}
			});

			Assert.That(m_Root.IsRunning, Is.False);
		}

		[Test]
		public void ALogCanBeTiedToASpan()
		{
			Start();
			var span = m_Tracer.Start("x");

			var logger = m_Root.Logs.Logger("Game").WithContext(span.Context);
			logger.Info("inside");
			m_Root.Logs.Logger("Game").Info("outside");
			span.Dispose();
			m_Root.Flush();

			var written = Spans().Single();
			var records = Frames(m_Logs).SelectMany(Records).ToList();
			Assert.That(Hex(One(records[0], 9)), Is.EqualTo(Hex(One(written, 1))), "trace_id");
			Assert.That(Hex(One(records[0], 10)), Is.EqualTo(Hex(One(written, 2))), "span_id");
			Assert.That(records[1].Any(field => field.Number == 9), Is.False);
		}

		[Test]
		public void TheContextIsNotAmbientUnlessAskedFor()
		{
			Start();

			var parent = m_Tracer.Start("parent");
			var child = m_Tracer.Start("child");

			Assert.That(m_Root.Traces.Current.IsValid, Is.False);
			child.Dispose();
			parent.Dispose();
			Assert.That(Spans()[0].Any(field => field.Number == 4), Is.False);
		}

		[Test]
		public void TheAmbientContextNestsAndComesBack()
		{
			Start(true);

			var parent = m_Tracer.Start("parent");
			Assert.That(m_Root.Traces.Current, Is.EqualTo(parent.Context));
			var child = m_Tracer.Start("child");
			Assert.That(m_Root.Traces.Current, Is.EqualTo(child.Context));
			child.Dispose();
			Assert.That(m_Root.Traces.Current, Is.EqualTo(parent.Context));
			parent.Dispose();
			Assert.That(m_Root.Traces.Current.IsValid, Is.False);

			var spans = Spans();
			Assert.That(Hex(One(spans[0], 4)), Is.EqualTo(Hex(One(spans[1], 2))));
		}

		[Test]
		public void AnExplicitParentWinsOverTheAmbientContext()
		{
			Start(true);

			var first = m_Tracer.Start("first");
			var ambient = m_Tracer.Start("second");
			var child = m_Tracer.Start("child", first.Context);
			child.Dispose();
			ambient.Dispose();
			first.Dispose();

			var spans = Spans();
			Assert.That(Hex(One(spans[0], 4)), Is.EqualTo(Hex(One(spans[2], 2))));
		}

		[Test]
		public void ALogFollowsTheAmbientContext()
		{
			Start(true);

			var span = m_Tracer.Start("x");
			m_Root.Logs.Logger("Game").Info("inside");
			span.Dispose();
			m_Root.Flush();

			Assert.That(Hex(One(Frames(m_Logs).SelectMany(Records).Single(), 10)), Is.EqualTo(Hex(One(Spans().Single(), 2))));
		}

		static IEnumerator Wait(Task task)
		{
			var frames = 0;
			while (!task.IsCompleted)
			{
				Assert.That(++frames, Is.LessThan(2000), "the task did not finish");
				yield return null;
			}
			task.GetAwaiter().GetResult();
		}

		[UnityTest]
		public IEnumerator AnExplicitContextCrossesATaskAwait()
		{
			Start();

			async Task Run()
			{
				var parent = m_Tracer.Start("parent");
				await Task.Yield();
				m_Tracer.Start("child", parent.Context).Dispose();
				parent.Dispose();
			}

			yield return Wait(Run());
			var spans = Spans();
			Assert.That(Hex(One(spans[0], 4)), Is.EqualTo(Hex(One(spans[1], 2))));
		}

		[UnityTest]
		public IEnumerator TheAmbientContextCrossesATaskAwait()
		{
			Start(true);
			var seen = default(TelemetryContext);
			var expected = default(TelemetryContext);

			async Task Run()
			{
				using (var span = m_Tracer.Start("parent"))
				{
					expected = span.Context;
					await Task.Yield();
					seen = m_Root.Traces.Current;
				}
			}

			yield return Wait(Run());
			Assert.That(expected.IsValid, Is.True);
			Assert.That(seen, Is.EqualTo(expected));
		}

		[UnityTest]
		public IEnumerator TheAmbientContextCrossesAnAwaitableHop()
		{
			Start(true);
			var seen = default(TelemetryContext);
			var expected = default(TelemetryContext);

			async Awaitable Run()
			{
				using (var span = m_Tracer.Start("parent"))
				{
					expected = span.Context;
					await Awaitable.BackgroundThreadAsync();
					await Awaitable.MainThreadAsync();
					seen = m_Root.Traces.Current;
				}
			}

			var awaitable = Run();
			var frames = 0;
			while (!awaitable.GetAwaiter().IsCompleted)
			{
				Assert.That(++frames, Is.LessThan(2000), "the awaitable did not finish");
				yield return null;
			}
			Assert.That(seen, Is.EqualTo(expected));
		}

		[Test]
		public void ASpanDoesNotAllocate()
		{
			Start();
			TestDelegate work = () =>
			{
				m_Traces.Position = 0;
				var span = m_Tracer.Start("x");
				span.Set("stage", 3);
				span.Dispose();
				m_Root.Traces.Flush();
			};
			work();
			work();

			AllocationCheck.AssertNone(work);
		}

		sealed class FailingStream : MemoryStream
		{
			public override void Write(byte[] buffer, int offset, int count)
			{
				throw new IOException("disk full");
			}
		}
	}
}
