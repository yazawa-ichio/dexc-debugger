#define DEXC_TELEMETRY_LOG_WARNING

using System;
using System.IO;
using System.Linq;
using System.Threading;
using Dexc.Telemetry;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DexcDebugger.Tests.Telemetry
{
	/// <summary>The symbol above stands for a project that keeps warnings and up. A conditional call is decided in the file of the call.</summary>
	public sealed class TelemetryLogApiTests
	{
		string m_Directory;

		[SetUp]
		public void SetUp()
		{
			TelemetryRoot.Reset();
			m_Directory = Path.Combine(Path.GetTempPath(), "DexcTelemetryLogTests-" + Guid.NewGuid().ToString("N"));
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

		TelemetryOptions Options(bool unityLogs = false)
		{
			return new TelemetryOptions
			{
				Directory = m_Directory,
				CollectUnityMetrics = false,
				CollectUnityLogs = unityLogs,
				MinimumSeverity = TelemetrySeverity.Verbose,
			};
		}

		string LogFile()
		{
			return Directory.GetFiles(m_Directory, "logs-*").Single();
		}

		[Test]
		public void TheChosenSeverityKeepsItsOwnAndHigherCalls()
		{
			DexcTelemetry.Start(Options());

			DexcTelemetry.LogVerbose("verbose");
			DexcTelemetry.LogDebug("debug");
			DexcTelemetry.LogInfo("info");
			DexcTelemetry.LogWarning("warning");
			DexcTelemetry.LogError("error");
			DexcTelemetry.LogFatal("fatal");
			var path = LogFile();
			DexcTelemetry.Stop();

			var text = System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(path));
			Assert.That(text, Does.Contain("warning").And.Contain("error").And.Contain("fatal"));
			Assert.That(text, Does.Not.Contain("verbose").And.Not.Contain("info"));
		}

		[Test]
		public void WhatUnityLogsReachesTheFile()
		{
			DexcTelemetry.Start(Options(true));

			Debug.Log("main thread message");
			var path = LogFile();
			DexcTelemetry.Stop();

			Assert.That(System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(path)), Does.Contain("main thread message"));
		}

		[Test]
		public void WhatAnotherThreadLogsReachesTheFile()
		{
			DexcTelemetry.Start(Options(true));

			var thread = new Thread(() => Debug.Log("worker thread message"));
			thread.Start();
			thread.Join();
			var path = LogFile();
			DexcTelemetry.Stop();

			Assert.That(System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(path)), Does.Contain("worker thread message"));
		}

		[Test]
		public void TheCallbackEndsWithTheRun()
		{
			DexcTelemetry.Start(Options(true));
			var path = LogFile();
			DexcTelemetry.Stop();

			Debug.Log("after the stop");
			DexcTelemetry.Start(Options(false));
			DexcTelemetry.Stop();

			Assert.That(System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(path)), Does.Not.Contain("after the stop"));
		}

		[Test]
		public void UnityLogsCanBeLeftOut()
		{
			DexcTelemetry.Start(Options(false));

			Debug.Log("not collected");
			var path = LogFile();
			DexcTelemetry.Stop();

			Assert.That(new FileInfo(path).Length, Is.Zero);
		}
	}
}
