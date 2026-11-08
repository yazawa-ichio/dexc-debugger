// Without it the IL processing would turn the span starts of this file into empty spans.
#define DEXC_TELEMETRY_DETAIL_DETAILED

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dexc.Telemetry;
using NUnit.Framework;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;
using Field = DexcDebugger.Tests.Telemetry.ProtobufTestReader.Field;

namespace DexcDebugger.Tests.Telemetry
{
	/// <summary>Drives the frame boundaries by hand: frame <c>n</c> is the boundary before frame <c>n</c>.</summary>
	public sealed class TelemetryCaptureTests
	{
		TelemetryRoot m_Root;
		MemoryStream m_Metrics;
		MemoryStream m_Traces;
		TelemetryCounter m_Hits;

		[SetUp]
		public void SetUp()
		{
			TelemetryRoot.Reset();
			m_Root = TelemetryRoot.Current;
			m_Metrics = new MemoryStream();
			m_Traces = new MemoryStream();
			m_Hits = m_Root.Metrics.Counter("hits", string.Empty);
		}

		[TearDown]
		public void TearDown()
		{
			TelemetryRoot.Reset();
		}

		void Start(TelemetryDetail detail = TelemetryDetail.Basic, bool ambient = false)
		{
			var options = new TelemetryOptions
			{
				CollectUnityMetrics = false,
				CollectUnityLogs = false,
				Detail = detail,
				AmbientContext = ambient,
				ExportIntervalSeconds = 60,
			};
			m_Root.Start(
				new TelemetryFrameWriter(m_Metrics),
				new TelemetryFrameWriter(new MemoryStream()),
				new TelemetryFrameWriter(m_Traces),
				options,
				new[] { new KeyValuePair<string, string>("service.name", "bench") },
				0
			);
		}

		/// <summary>A frame of the game that records once, then the boundary after it.</summary>
		void Frame(int next)
		{
			m_Hits.Add(1, TelemetryDetail.Basic);
			m_Root.OnFrame(0.016, next);
			m_Root.Tick(next * 0.016);
		}

		static int CountFrames(MemoryStream stream)
		{
			return Frames(stream).Count;
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

		List<List<Field>> Spans()
		{
			return Frames(m_Traces)
				.Select(frame => ProtobufTestReader.Read(ProtobufTestReader.Read(frame).Single(field => field.Number == 1).Bytes))
				.SelectMany(resource => resource.Where(field => field.Number == 2))
				.SelectMany(scope => ProtobufTestReader.Read(scope.Bytes).Where(field => field.Number == 2))
				.Select(field => ProtobufTestReader.Read(field.Bytes))
				.ToList();
		}

		static Field One(List<Field> fields, int number)
		{
			return fields.Single(field => field.Number == number);
		}

		static List<Field> Attribute(List<Field> span, string key)
		{
			return span
				.Where(field => field.Number == 9)
				.Select(field => ProtobufTestReader.Read(field.Bytes))
				.Single(attribute => One(attribute, 1).Text == key);
		}

		static ulong IntegerOf(List<Field> attribute)
		{
			return ProtobufTestReader.Read(One(attribute, 2).Bytes).Single().Value;
		}

		[Test]
		public void ACaptureWritesEveryFrameAndThenGoesBackToTheInterval()
		{
			Start();

			Assert.That(m_Root.Capture(3, TelemetryDetail.Detailed, false), Is.True);
			Frame(10);
			Assert.That(CountFrames(m_Metrics), Is.EqualTo(1), "what came before goes out at the start");

			for (var i = 0; i < 3; i++)
			{
				Frame(11 + i);
				Assert.That(CountFrames(m_Metrics), Is.EqualTo(2 + i));
			}
			Assert.That(m_Root.IsCapturing, Is.False);

			Frame(14);
			m_Root.Tick(59);
			Assert.That(CountFrames(m_Metrics), Is.EqualTo(4), "the interval started again at the end");
			m_Root.Tick(13 * 0.016 + 60);
			Assert.That(CountFrames(m_Metrics), Is.EqualTo(5));
		}

		[Test]
		public void TheDetailRisesForTheCaptureAndComesBack()
		{
			Start(TelemetryDetail.Basic);

			m_Root.Capture(1, TelemetryDetail.Detailed, false);
			Assert.That(m_Root.Metrics.Detail, Is.EqualTo(TelemetryDetail.Basic), "not before the next frame");
			Frame(1);
			Assert.That(m_Root.Metrics.Detail, Is.EqualTo(TelemetryDetail.Detailed));
			Frame(2);

			Assert.That(m_Root.Metrics.Detail, Is.EqualTo(TelemetryDetail.Basic));
		}

		[Test]
		public void ACaptureDoesNotLowerTheDetail()
		{
			Start(TelemetryDetail.Standard);

			m_Root.Capture(1, TelemetryDetail.Basic, false);
			Frame(1);

			Assert.That(m_Root.Metrics.Detail, Is.EqualTo(TelemetryDetail.Standard));
		}

		[Test]
		public void ACaptureIsASpanWithASpanPerFrame()
		{
			Start();

			m_Root.Capture(2, TelemetryDetail.Detailed, false);
			Frame(10);
			Frame(11);
			Frame(12);
			m_Root.Flush();

			var spans = Spans();
			var capture = spans.Single(span => One(span, 5).Text == "dexc.capture");
			var frames = spans.Where(span => One(span, 5).Text == "unity.frame").ToList();
			Assert.That(IntegerOf(Attribute(capture, "dexc.capture.frames")), Is.EqualTo(2UL));
			Assert.That(frames.Select(frame => IntegerOf(Attribute(frame, "unity.frame.number"))), Is.EqualTo(new[] { 10UL, 11UL }));
			Assert.That(frames.All(frame => One(frame, 4).Bytes.SequenceEqual(One(capture, 2).Bytes)), Is.True, "the frames are children of the capture");
		}

		[Test]
		public void ACaptureNeedsARunAndOneAtATime()
		{
			Assert.That(m_Root.Capture(1, TelemetryDetail.Detailed, false), Is.False);
			Start();

			Assert.That(m_Root.Capture(1, TelemetryDetail.Detailed, false), Is.True);
			Assert.That(m_Root.Capture(1, TelemetryDetail.Detailed, false), Is.False);
			Assert.Throws<ArgumentOutOfRangeException>(() => m_Root.Capture(0, TelemetryDetail.Detailed, false));
		}

		[Test]
		public void StoppingACaptureEndsItAtTheNextFrame()
		{
			Start();
			m_Root.Capture(100, TelemetryDetail.Detailed, false);
			Frame(1);

			m_Root.StopCapture();
			Assert.That(m_Root.IsCapturing, Is.True);
			Frame(2);

			Assert.That(m_Root.IsCapturing, Is.False);
			Assert.That(m_Root.Metrics.Detail, Is.EqualTo(TelemetryDetail.Basic));
		}

		[Test]
		public void StoppingACaptureThatHasNotBegunDropsIt()
		{
			Start();
			m_Root.Capture(5, TelemetryDetail.Detailed, false);

			m_Root.StopCapture();

			Assert.That(m_Root.IsCapturing, Is.False);
			Frame(1);
			Assert.That(m_Root.Metrics.Detail, Is.EqualTo(TelemetryDetail.Basic));
		}

		[Test]
		public void StoppingTelemetryEndsTheCaptureAndWritesIt()
		{
			Start();
			m_Root.Capture(100, TelemetryDetail.Detailed, false);
			Frame(1);

			m_Root.Stop();

			Assert.That(m_Root.IsCapturing, Is.False);
			Assert.That(m_Root.Metrics.Detail, Is.EqualTo(TelemetryDetail.Basic));
			Assert.That(Spans().Any(span => One(span, 5).Text == "dexc.capture"), Is.True);
		}

		[Test]
		public void TheSpansOfACaptureStayOutOfTheAmbientContext()
		{
			Start(ambient: true);
			m_Root.Capture(2, TelemetryDetail.Detailed, false);
			Frame(1);

			Assert.That(m_Root.Traces.Current.IsValid, Is.False);
			m_Root.Traces.Tracer("Game").Start("game").Dispose();
			Frame(2);
			Frame(3);

			var game = Spans().Single(span => One(span, 5).Text == "game");
			Assert.That(game.Any(field => field.Number == 4), Is.False, "the game span has no parent");
		}

		[Test]
		public void TheFrameRunsOnceAtTheEndOfInitialization()
		{
			PlayerLoopSystem.UpdateFunction update = () => { };

			var loop = TelemetryPlayerLoop.With(TelemetryPlayerLoop.With(PlayerLoop.GetDefaultPlayerLoop(), update), update);

			var initialization = loop.subSystemList.Single(phase => phase.type == typeof(Initialization));
			Assert.That(initialization.subSystemList.Last().type, Is.EqualTo(typeof(TelemetryPlayerLoop.Frame)));
			Assert.That(loop.subSystemList.Sum(phase => (phase.subSystemList ?? new PlayerLoopSystem[0]).Count(system => system.type == typeof(TelemetryPlayerLoop.Frame))), Is.EqualTo(1));
			var without = TelemetryPlayerLoop.Without(loop);
			Assert.That(without.subSystemList.Single(phase => phase.type == typeof(Initialization)).subSystemList.Any(system => system.type == typeof(TelemetryPlayerLoop.Frame)), Is.False);
		}
	}
}
