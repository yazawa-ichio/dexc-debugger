using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dexc.Telemetry;
using NUnit.Framework;

namespace DexcDebugger.Tests.Telemetry
{
	public sealed class DexcTelemetryTests
	{
		string m_Directory;

		[SetUp]
		public void SetUp()
		{
			TelemetryRoot.Reset();
			m_Directory = Path.Combine(Path.GetTempPath(), "DexcTelemetryTests-" + Guid.NewGuid().ToString("N"));
		}

		[TearDown]
		public void TearDown()
		{
			TelemetryRoot.Reset();
			if (Directory.Exists(m_Directory))
			{
				Directory.Delete(m_Directory, true);
			}
		}

		TelemetryOptions Options(double interval = 60)
		{
			return new TelemetryOptions
			{
				Directory = m_Directory,
				ServiceName = "bench",
				ServiceVersion = "1.2",
				ExportIntervalSeconds = interval,
				ResourceAttributes = new Dictionary<string, string> { ["device.model"] = "pixel" },
			};
		}

		[Test]
		public void AStartedRunWritesItsMetricsToItsOwnFile()
		{
			var path = DexcTelemetry.Start(Options());
			DexcTelemetry.Counter("hits").Add(3, TelemetryDetail.Basic);

			DexcTelemetry.Stop();

			Assert.That(DexcTelemetry.IsRunning, Is.False);
			Assert.That(Path.GetDirectoryName(path), Is.EqualTo(m_Directory));
			Assert.That(new FileInfo(path).Length, Is.GreaterThan(4));
			Assert.That(ReadResource(path), Is.SupersetOf(new[] { "service.name=bench", "service.version=1.2", "device.model=pixel", "telemetry.sdk.name=dexc-telemetry" }));
		}

		[Test]
		public void TwoRunsDoNotShareAFile()
		{
			var first = DexcTelemetry.Start(Options());
			DexcTelemetry.Stop();
			var second = DexcTelemetry.Start(Options());
			DexcTelemetry.Stop();

			Assert.That(second, Is.Not.EqualTo(first));
		}

		[Test]
		public void WhatWasRecordedBeforeTheStartIsWrittenAfterIt()
		{
			DexcTelemetry.Counter("early").Add(1, TelemetryDetail.Basic);

			var path = DexcTelemetry.Start(Options());
			DexcTelemetry.Stop();

			Assert.That(new FileInfo(path).Length, Is.GreaterThan(4));
		}

		[Test]
		public void FlushWritesWithoutStopping()
		{
			var path = DexcTelemetry.Start(Options());
			DexcTelemetry.Counter("hits").Add(1, TelemetryDetail.Basic);

			DexcTelemetry.Flush();

			Assert.That(DexcTelemetry.IsRunning, Is.True);
			using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
			{
				Assert.That(stream.Length, Is.GreaterThan(4));
			}
		}

		[Test]
		public void TheIntervalDecidesWhenATickWrites()
		{
			var stream = new MemoryStream();
			var root = TelemetryRoot.Current;
			var counter = root.Metrics.Counter("hits", string.Empty);
			root.Start(new TelemetryFrameWriter(stream), new TelemetryFrameWriter(new MemoryStream()), Options(10), new KeyValuePair<string, string>[0], 100);

			counter.Add(1, TelemetryDetail.Basic);
			root.Tick(109.9);
			Assert.That(stream.Length, Is.Zero);

			root.Tick(110);
			var written = stream.Length;
			Assert.That(written, Is.GreaterThan(0));

			counter.Add(1, TelemetryDetail.Basic);
			root.Tick(115);
			Assert.That(stream.Length, Is.EqualTo(written), "the interval counts from the last write");
		}

		[Test]
		public void AFailedWriteEndsTheOutputWithoutThrowing()
		{
			var root = TelemetryRoot.Current;
			root.Metrics.Counter("hits", string.Empty).Add(1, TelemetryDetail.Basic);
			root.Start(new TelemetryFrameWriter(new FailingStream()), new TelemetryFrameWriter(new MemoryStream()), Options(), new KeyValuePair<string, string>[0], 0);
			UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Exception, new System.Text.RegularExpressions.Regex("disk full"));

			Assert.DoesNotThrow(() => root.Flush());

			Assert.That(root.IsRunning, Is.False);
		}

		[Test]
		public void StartingTwiceIsRefused()
		{
			DexcTelemetry.Start(Options());

			Assert.Throws<InvalidOperationException>(() => DexcTelemetry.Start(Options()));
		}

		[TestCase(0)]
		[TestCase(-1)]
		public void AnIntervalMustBeAboveZero(double interval)
		{
			Assert.Throws<ArgumentOutOfRangeException>(() => DexcTelemetry.Start(Options(interval)));
		}

		[Test]
		public void StoppingWhenNotRunningDoesNothing()
		{
			Assert.DoesNotThrow(DexcTelemetry.Stop);
			Assert.DoesNotThrow(DexcTelemetry.Flush);
		}

		[Test]
		public void ResettingTheRootClosesTheFile()
		{
			var path = DexcTelemetry.Start(Options());

			TelemetryRoot.Reset();

			Assert.DoesNotThrow(() => File.Delete(path));
			Assert.That(DexcTelemetry.IsRunning, Is.False);
		}

		static IEnumerable<string> ReadResource(string path)
		{
			var file = File.ReadAllBytes(path);
			var message = new byte[file.Length - 4];
			Array.Copy(file, 4, message, 0, message.Length);
			var resourceMetrics = ProtobufTestReader.Read(ProtobufTestReader.Read(message).Single().Bytes);
			var resource = ProtobufTestReader.Read(resourceMetrics.Single(field => field.Number == 1).Bytes);
			foreach (var field in resource)
			{
				var attribute = ProtobufTestReader.Read(field.Bytes);
				var value = ProtobufTestReader.Read(attribute.Single(item => item.Number == 2).Bytes).Single();
				yield return attribute.Single(item => item.Number == 1).Text + "=" + value.Text;
			}
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
