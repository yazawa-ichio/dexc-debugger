#define DEXC_TELEMETRY_LOG_WARNING

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Dexc.Telemetry;
using NUnit.Framework;
using UnityEngine;

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

		/// <summary>The text of the file, which may have taken its closed name since.</summary>
		static string Text(string path)
		{
			if (!File.Exists(path) && path.EndsWith(".open"))
			{
				path = path.Substring(0, path.Length - ".open".Length);
			}
			return Encoding.UTF8.GetString(File.ReadAllBytes(path));
		}

		[Test]
		public void TheChosenSeverityKeepsItsOwnAndHigherCalls()
		{
			DexcTelemetry.Start(Options());
			var logger = DexcTelemetry.Logger("Game");

			logger.Verbose("verbose");
			logger.Debug("debug");
			logger.Info("info");
			logger.Warning("warning");
			logger.Error("error");
			logger.Fatal("fatal");
			var path = LogFile();
			DexcTelemetry.Stop();

			var text = Text(path);
			Assert.That(text, Does.Contain("warning").And.Contain("error").And.Contain("fatal"));
			Assert.That(text, Does.Not.Contain("verbose").And.Not.Contain("info"));
		}

		[Test]
		public void TheArgumentsOfARemovedCallAreNotEvaluated()
		{
			var evaluated = false;
			Func<int> value = () =>
			{
				evaluated = true;
				return 1;
			};

			DexcTelemetry.Logger("Game").Info("{Value}", value());

			Assert.That(evaluated, Is.False);
		}

		[Test]
		public void TheLevelCanBeChangedThroughTheEntry()
		{
			var logger = DexcTelemetry.Logger("Game.Stage");

			DexcTelemetry.LogLevel = TelemetrySeverity.Error;
			Assert.That(DexcTelemetry.LogLevel, Is.EqualTo(TelemetrySeverity.Error));
			Assert.That(logger.IsEnabled(TelemetrySeverity.Warning), Is.False);

			DexcTelemetry.SetLogLevel("Game", TelemetrySeverity.Verbose);
			Assert.That(logger.IsEnabled(TelemetrySeverity.Verbose), Is.True);

			DexcTelemetry.ClearLogLevel("Game");
			Assert.That(logger.IsEnabled(TelemetrySeverity.Warning), Is.False);
		}

		[Test]
		public void TheMinimumSeverityBecomesTheLevelAtTheStart()
		{
			var options = Options();
			options.MinimumSeverity = TelemetrySeverity.Warning;

			DexcTelemetry.Start(options);

			Assert.That(DexcTelemetry.LogLevel, Is.EqualTo(TelemetrySeverity.Warning));
		}

		[Test]
		public void WhatUnityLogsReachesTheFile()
		{
			DexcTelemetry.Start(Options(true));

			Debug.Log("main thread message");
			var path = LogFile();
			DexcTelemetry.Stop();

			Assert.That(Text(path), Does.Contain("main thread message").And.Contain(UnityLogAdapter.Category));
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

			Assert.That(Text(path), Does.Contain("worker thread message"));
		}

		[Test]
		public void TheLevelOfUnityLogsIsThatOfItsCategory()
		{
			DexcTelemetry.SetLogLevel(UnityLogAdapter.Category, TelemetrySeverity.Fatal);
			DexcTelemetry.Start(Options(true));

			Debug.Log("silenced message");
			var path = LogFile();
			DexcTelemetry.Stop();

			Assert.That(Text(path), Does.Not.Contain("silenced message"));
		}

		[Test]
		public void TheAdapterEndsWithTheRun()
		{
			DexcTelemetry.Start(Options(true));
			var path = LogFile();
			DexcTelemetry.Stop();

			Debug.Log("after the stop");
			DexcTelemetry.Start(Options(false));
			DexcTelemetry.Stop();

			Assert.That(Text(path), Does.Not.Contain("after the stop"));
		}

		[Test]
		public void UnityLogsCanBeLeftOut()
		{
			DexcTelemetry.Start(Options(false));

			Debug.Log("not collected");
			var path = LogFile();
			DexcTelemetry.Stop();

			Assert.That(Text(path), Is.Empty);
		}

		[Test]
		public void AnAdapterIsAttachedAndDetachedWithTheRun()
		{
			var adapter = new OtherLogger();
			var options = Options();
			options.LogAdapters = new ITelemetryLogAdapter[] { adapter };

			DexcTelemetry.Start(options);
			Assert.That(adapter.Attached, Is.EqualTo(1));
			adapter.Write("from the other logger");
			var path = LogFile();
			DexcTelemetry.Stop();

			Assert.That(adapter.Detached, Is.EqualTo(1));
			Assert.That(Text(path), Does.Contain("from the other logger").And.Contain("Other.Library"));
		}

		[Test]
		public void ResettingTheRootDetachesTheAdapters()
		{
			var adapter = new OtherLogger();
			var options = Options();
			options.LogAdapters = new ITelemetryLogAdapter[] { adapter };
			DexcTelemetry.Start(options);

			TelemetryRoot.Reset();

			Assert.That(adapter.Detached, Is.EqualTo(1));
		}

		sealed class OtherLogger : ITelemetryLogAdapter
		{
			TelemetryLogger m_Logger;

			internal int Attached;
			internal int Detached;

			public void Attach()
			{
				Attached++;
				m_Logger = DexcTelemetry.Logger("Other.Library");
			}

			public void Detach()
			{
				Detached++;
			}

			internal void Write(string message)
			{
				m_Logger.Emit(TelemetrySeverity.Info, message);
			}
		}
	}
}
