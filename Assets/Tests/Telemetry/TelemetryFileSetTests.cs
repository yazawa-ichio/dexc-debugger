using System;
using System.IO;
using System.Linq;
using Dexc.Telemetry;
using NUnit.Framework;

namespace DexcDebugger.Tests.Telemetry
{
	public sealed class TelemetryFileSetTests
	{
		string m_Directory;

		[SetUp]
		public void SetUp()
		{
			m_Directory = Path.Combine(Path.GetTempPath(), "DexcTelemetryFiles-" + Guid.NewGuid().ToString("N"));
		}

		[TearDown]
		public void TearDown()
		{
			if (Directory.Exists(m_Directory))
			{
				Directory.Delete(m_Directory, true);
			}
		}

		string[] Files()
		{
			return Directory.GetFiles(m_Directory).Select(Path.GetFileName).OrderBy(name => name, StringComparer.Ordinal).ToArray();
		}

		void Old(string name, int bytes, int minutesAgo)
		{
			var path = Path.Combine(m_Directory, name);
			File.WriteAllBytes(path, new byte[bytes]);
			File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-minutesAgo));
		}

		[Test]
		public void TheFirstFileHasAPlainName()
		{
			using (var frames = new TelemetryFileSet(m_Directory, "run", 1000, 10000).Open("logs"))
			{
				Assert.That(Path.GetFileName(frames.Path), Is.EqualTo("logs-run.otlp.open"));
			}

			Assert.That(Files(), Is.EqualTo(new[] { "logs-run.otlp" }), "closing gives the closed name");
		}

		[Test]
		public void AFullFileContinuesInTheNextBetweenFrames()
		{
			using (var frames = new TelemetryFileSet(m_Directory, "run", 20, 10000).Open("logs"))
			{
				frames.WriteFrame(new byte[8], 8);
				Assert.That(Files(), Is.EqualTo(new[] { "logs-run.otlp.open" }), "still under the limit");
				frames.WriteFrame(new byte[8], 8);
				Assert.That(Path.GetFileName(frames.Path), Is.EqualTo("logs-run-1.otlp.open"));
				Assert.That(Files(), Does.Contain("logs-run.otlp"), "the full one is closed");
				frames.WriteFrame(new byte[20], 20);
				Assert.That(Path.GetFileName(frames.Path), Is.EqualTo("logs-run-2.otlp.open"));
			}

			Assert.That(new FileInfo(Path.Combine(m_Directory, "logs-run.otlp")).Length, Is.EqualTo(24), "two whole frames");
			Assert.That(new FileInfo(Path.Combine(m_Directory, "logs-run-1.otlp")).Length, Is.EqualTo(24));
		}

		[Test]
		public void OldFilesGoFirstWhenTheFolderIsOverItsLimit()
		{
			Directory.CreateDirectory(m_Directory);
			Old("logs-a.otlp", 600, 30);
			Old("metrics-b.otlp", 600, 20);
			Old("logs-c.otlp", 600, 10);

			using (new TelemetryFileSet(m_Directory, "run", 1000, 1300).Open("logs"))
			{
				Assert.That(Files(), Is.EqualTo(new[] { "logs-c.otlp", "logs-run.otlp.open", "metrics-b.otlp" }));
			}
		}

		[Test]
		public void OnlyTelemetryFilesAreDeleted()
		{
			Directory.CreateDirectory(m_Directory);
			Old("notes.txt", 5000, 60);
			Old("logs-a.otlp", 600, 30);

			using (new TelemetryFileSet(m_Directory, "run", 1000, 100).Open("logs"))
			{
				Assert.That(Files(), Is.EqualTo(new[] { "logs-run.otlp.open", "notes.txt" }));
			}
		}

		[Test]
		public void ProfilerFilesOfCapturesCountTowardTheLimit()
		{
			Directory.CreateDirectory(m_Directory);
			Old("capture-old-1.raw", 600, 30);
			Old("other.raw", 5000, 40);
			Old("logs-a.otlp", 600, 20);
			var files = new TelemetryFileSet(m_Directory, "run", 1000, 1300);

			var path = files.ProfilerPath(2);
			File.WriteAllBytes(path, new byte[700]);
			files.Trim();

			Assert.That(Path.GetFileName(path), Is.EqualTo("capture-run-2.raw"));
			Assert.That(Files(), Is.EqualTo(new[] { "capture-run-2.raw", "logs-a.otlp", "other.raw" }), "the one being written stays");

			files.Closed(path);
			File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-50));
			Old("logs-b.otlp", 300, 0);
			files.Trim();
			Assert.That(Files(), Is.EqualTo(new[] { "logs-a.otlp", "logs-b.otlp", "other.raw" }));
		}

		[Test]
		public void AFileLeftOpenByAnEndedRunIsClosedByTheNext()
		{
			Directory.CreateDirectory(m_Directory);
			Old("logs-crashed.otlp.open", 100, 5);
			var held = Path.Combine(m_Directory, "logs-other.otlp.open");
			using (new FileStream(held, FileMode.Create, FileAccess.Write, FileShare.Read | FileShare.Delete))
			{
				new TelemetryFileSet(m_Directory, "run", 1000, 10).Trim();

				Assert.That(Files(), Is.EqualTo(new[] { "logs-other.otlp.open" }), "the left one closed and went over the limit; the held one stays");
			}
		}

		[Test]
		public void TheFilesBeingWrittenAreKept()
		{
			var files = new TelemetryFileSet(m_Directory, "run", 10, 30);
			using (var logs = files.Open("logs"))
			using (var metrics = files.Open("metrics"))
			{
				metrics.WriteFrame(new byte[4], 4);
				metrics.Flush();
				for (var i = 0; i < 6; i++)
				{
					logs.WriteFrame(new byte[20], 20);
				}

				Assert.That(Files(), Does.Contain("metrics-run.otlp.open"));
				Assert.That(Files(), Does.Contain(Path.GetFileName(logs.Path)));
				Assert.That(Files().Length, Is.LessThan(6), "the older parts of this run went");
			}
		}
	}
}
