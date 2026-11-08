// Keeps the measuring of TelemetryMethodAttribute, whose detail is Detailed by default.
#define DEXC_TELEMETRY_DETAIL_DETAILED

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Dexc.Telemetry;
using NUnit.Framework;
using UnityEngine.TestTools;
using Field = DexcDebugger.Tests.Telemetry.ProtobufTestReader.Field;

namespace DexcDebugger.Tests.Telemetry
{
	internal static class Measured
	{
		[TelemetryMethod]
		internal static int Outer(int value)
		{
			return Inner(value) + 1;
		}

		[TelemetryMethod]
		internal static int Inner(int value)
		{
			if (value > 0)
			{
				return value * 2;
			}
			return -1;
		}

		[TelemetryMethod]
		internal static void Throws()
		{
			throw new InvalidOperationException("measured");
		}

		[TelemetryMethod]
		internal static int Guarded(int value)
		{
			try
			{
				if (value < 0)
				{
					throw new InvalidOperationException("negative");
				}
				return value;
			}
			catch (InvalidOperationException)
			{
				return -1;
			}
		}

		[TelemetryMethod]
		internal static T Echo<T>(T value)
		{
			return value;
		}

		[TelemetryMethod(Every = 2)]
		internal static void Thinned()
		{
		}

		[TelemetryMethod(PerFrame = 2)]
		internal static void Capped()
		{
		}

		[TelemetryMethod(AnyThread = true)]
		internal static void Anywhere()
		{
		}

		[TelemetryMethod]
		internal static void MainOnly()
		{
		}

		[TelemetryMethod]
		internal static async Task<int> LaterAsync(int value)
		{
			await Task.Yield();
			return value + 1;
		}

		[TelemetryMethod]
		internal static async Task FailsAsync()
		{
			await Task.Yield();
			throw new InvalidOperationException("measured");
		}

		[TelemetryMethod]
		internal static async Task<int> AwayAsync()
		{
			await Task.Run(() => 1).ConfigureAwait(false);
			return 2;
		}
	}

	[TelemetryMethod]
	internal struct MeasuredStruct
	{
		internal int Value { get; set; }

		internal int Twice()
		{
			return Value * 2;
		}
	}

	/// <summary>The methods above are measured by the IL processing of this assembly.</summary>
	public sealed class TelemetryMethodTests
	{
		const string Prefix = "DexcDebugger.Tests.Telemetry.";

		TelemetryRoot m_Root;
		MemoryStream m_Traces;
		int m_Frame;

		[SetUp]
		public void SetUp()
		{
			TelemetryRoot.Reset();
			m_Root = TelemetryRoot.Current;
			m_Traces = new MemoryStream();
			var options = new TelemetryOptions { CollectUnityMetrics = false, CollectUnityLogs = false, Detail = TelemetryDetail.Basic };
			m_Root.Start(
				new TelemetryFrameWriter(new MemoryStream()),
				new TelemetryFrameWriter(new MemoryStream()),
				new TelemetryFrameWriter(m_Traces),
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

		void Frame()
		{
			m_Root.OnFrame(0.016, ++m_Frame);
		}

		/// <summary>Runs the work inside a capture of one frame, then writes everything.</summary>
		List<List<Field>> Captured(Action work, int frames = 1)
		{
			m_Root.Capture(frames, TelemetryDetail.Detailed, false);
			Frame();
			for (var i = 0; i < frames; i++)
			{
				work();
				Frame();
			}
			m_Root.Flush();
			return Spans();
		}

		List<List<Field>> Spans()
		{
			return ReadSpans(m_Traces);
		}

		/// <summary>The spans in the frames of the stream, in order.</summary>
		internal static List<List<Field>> ReadSpans(MemoryStream stream)
		{
			var file = stream.ToArray();
			var spans = new List<List<Field>>();
			var position = 0;
			while (position < file.Length)
			{
				var length = (file[position] << 24) | (file[position + 1] << 16) | (file[position + 2] << 8) | file[position + 3];
				var frame = new byte[length];
				Array.Copy(file, position + 4, frame, 0, length);
				position += 4 + length;
				var resource = ProtobufTestReader.Read(ProtobufTestReader.Read(frame).Single(field => field.Number == 1).Bytes);
				spans.AddRange(resource
					.Where(field => field.Number == 2)
					.SelectMany(scope => ProtobufTestReader.Read(scope.Bytes).Where(field => field.Number == 2))
					.Select(field => ProtobufTestReader.Read(field.Bytes)));
			}
			return spans;
		}

		internal static string Name(List<Field> span)
		{
			return span.Single(field => field.Number == 5).Text;
		}

		static List<List<Field>> Named(List<List<Field>> spans, string method)
		{
			return spans.Where(span => Name(span) == Prefix + method).ToList();
		}

		internal static byte[] Id(List<Field> span, int number)
		{
			return span.Single(field => field.Number == number).Bytes;
		}

		[Test]
		public void OutsideACaptureACallIsNotRecorded()
		{
			Assert.That(Measured.Outer(3), Is.EqualTo(7));
			m_Root.Flush();

			Assert.That(Spans(), Is.Empty);
		}

		[Test]
		public void ACallIsASpanUnderTheCallItRunsIn()
		{
			var result = 0;

			var spans = Captured(() => result = Measured.Outer(3));

			Assert.That(result, Is.EqualTo(7));
			var outer = Named(spans, "Measured.Outer").Single();
			var inner = Named(spans, "Measured.Inner").Single();
			var frame = spans.Single(span => Name(span) == "unity.frame");
			Assert.That(Id(inner, 4), Is.EqualTo(Id(outer, 2)), "the inner call is a child of the outer one");
			Assert.That(Id(outer, 4), Is.EqualTo(Id(frame, 2)), "the outer call is a child of the frame");
		}

		[Test]
		public void TheReturnsAndTheExceptionsOfTheMethodStayTheSame()
		{
			var results = new List<int>();

			var spans = Captured(() =>
			{
				results.Add(Measured.Inner(-5));
				results.Add(Measured.Guarded(4));
				results.Add(Measured.Guarded(-4));
				results.Add(Measured.Echo(9));
				results.Add(new MeasuredStruct { Value = 6 }.Twice());
				Assert.Throws<InvalidOperationException>(Measured.Throws);
				results.Add(Measured.Outer(1));
			});

			Assert.That(results, Is.EqualTo(new[] { -1, 4, -1, 9, 12, 3 }));
			Assert.That(Named(spans, "Measured.Guarded").Count, Is.EqualTo(2));
			Assert.That(Named(spans, "Measured.Throws").Count, Is.EqualTo(1), "the span ended with the exception");
			Assert.That(Named(spans, "MeasuredStruct.Twice").Count, Is.EqualTo(1));
			Assert.That(Named(spans, "MeasuredStruct.get_Value"), Is.Empty, "accessors are left out through the type");
			var outer = Named(spans, "Measured.Outer").Single();
			var frame = spans.Single(span => Name(span) == "unity.frame");
			Assert.That(Id(outer, 4), Is.EqualTo(Id(frame, 2)), "the throwing call left the stack as it was");
		}

		[Test]
		public void CallsAreThinnedAndCappedPerFrame()
		{
			var spans = Captured(() =>
			{
				for (var i = 0; i < 4; i++)
				{
					Measured.Thinned();
				}
				for (var i = 0; i < 5; i++)
				{
					Measured.Capped();
				}
			}, 2);

			Assert.That(Named(spans, "Measured.Thinned").Count, Is.EqualTo(4), "one in two of eight calls");
			Assert.That(Named(spans, "Measured.Capped").Count, Is.EqualTo(4), "two in each of two frames");
		}

		[Test]
		public void OnlyTheMethodsThatAskForItAreRecordedOnOtherThreads()
		{
			var spans = Captured(() =>
			{
				Task.Run(Measured.Anywhere).Wait();
				Task.Run(Measured.MainOnly).Wait();
			});

			var anywhere = Named(spans, "Measured.Anywhere").Single();
			Assert.That(Named(spans, "Measured.MainOnly"), Is.Empty);
			var thread = anywhere
				.Where(field => field.Number == 9)
				.Select(field => ProtobufTestReader.Read(field.Bytes))
				.Single(attribute => attribute.Single(field => field.Number == 1).Text == "thread.id");
			Assert.That(thread, Is.Not.Null);
		}

		static IEnumerator Wait(Task task)
		{
			while (!task.IsCompleted)
			{
				yield return null;
			}
		}

		[UnityTest]
		public IEnumerator AnAsyncCallRunsFromItsStartToItsCompletion()
		{
			m_Root.Capture(1, TelemetryDetail.Detailed, false);
			Frame();
			var later = Measured.LaterAsync(3);
			Measured.Outer(1);
			var fails = Measured.FailsAsync();
			var away = Measured.AwayAsync();
			yield return Wait(later);
			yield return Wait(fails);
			yield return Wait(away);
			Frame();
			m_Root.Flush();

			Assert.That(later.Result, Is.EqualTo(4));
			Assert.That(fails.Exception.InnerException, Is.TypeOf<InvalidOperationException>());
			Assert.That(away.Result, Is.EqualTo(2));
			var spans = Spans();
			var frame = spans.Single(span => Name(span) == "unity.frame");
			Assert.That(spans.Select(Name), Does.Contain(Prefix + "Measured.LaterAsync"), string.Join(", ", spans.Select(Name)));
			var measured = Named(spans, "Measured.LaterAsync").Single();
			Assert.That(Id(Named(spans, "Measured.Outer").Single(), 4), Is.EqualTo(Id(frame, 2)), "the call after the first await is not under the async one");
			Assert.That(Id(measured, 4), Is.EqualTo(Id(frame, 2)));
			Assert.That(measured.Single(field => field.Number == 8).Value, Is.GreaterThan(measured.Single(field => field.Number == 7).Value), "it ended after the await");
			Assert.That(Named(spans, "Measured.FailsAsync").Count, Is.EqualTo(1));
			Assert.That(Named(spans, "Measured.AwayAsync").Count, Is.EqualTo(1), "an end on another thread is written too");
		}
	}
}
