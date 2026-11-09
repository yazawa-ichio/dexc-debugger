using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dexc.QA;
using Dexc.Telemetry;
using NUnit.Framework;
using UnityEngine;

namespace DexcDebugger.Tests.QA
{
	public sealed class DexcQATests
	{
		sealed class Recorder : IDexcQAContributor, IDexcQASink
		{
			public readonly List<string> Calls = new List<string>();

			public void Contribute(DexcQAReport report)
			{
				Calls.Add("contribute " + report.Signature);
				report.Set("stage", "2");
				report.Attach("save.txt", new byte[] { 65 });
			}

			public void OnReportWritten(DexcQAReportInfo report)
			{
				Calls.Add("written " + report.Id + " " + File.Exists(Path.Combine(report.Folder, "report.json")));
			}
		}

		sealed class Throwing : IDexcQAContributor
		{
			public void Contribute(DexcQAReport report)
			{
				throw new InvalidOperationException("no save");
			}
		}

		sealed class Place : ITelemetryLocation
		{
			public bool TryGetLocation(out string map, out float x, out float y, out float z)
			{
				map = "yard";
				x = 4;
				y = 0;
				z = -2;
				return true;
			}
		}

		string m_Directory;

		[SetUp]
		public void SetUp()
		{
			QARoot.Reset();
			m_Directory = Path.Combine(Path.GetTempPath(), "DexcQA-" + Guid.NewGuid().ToString("N"));
		}

		[TearDown]
		public void TearDown()
		{
			QARoot.Reset();
			if (Directory.Exists(m_Directory))
			{
				Directory.Delete(m_Directory, true);
			}
		}

		void Start(int perSignature = 3)
		{
			DexcQA.Start(new DexcQAOptions { Directory = m_Directory, DetailedReportsPerSignature = perSignature });
		}

		string Report(string id)
		{
			return File.ReadAllText(Path.Combine(m_Directory, id, "report.json"));
		}

		string[] Folders()
		{
			return Directory.GetDirectories(m_Directory).Select(Path.GetFileName).ToArray();
		}

		[Test]
		public void AReportIsAFolderWithWhatItTakesToReproduce()
		{
			Start();
			QARoot.Current.OnLog("walked to the exit", null, LogType.Log);

			var id = DexcQA.Report("bot", "stage1.exit-blocked", "The exit does not open", "The guard is gone.");

			var report = Report(id);
			Assert.That(report, Does.Contain("\"kind\": \"bot\""));
			Assert.That(report, Does.Contain("\"signature\": \"stage1.exit-blocked\""));
			Assert.That(report, Does.Contain("\"detail\": \"The guard is gone.\""));
			Assert.That(report, Does.Contain("\"product\": "));
			Assert.That(report, Does.Contain("\"message\": \"walked to the exit\""));
			Assert.That(report, Does.Contain("\"screenshot\": null"), "nothing draws in a test");
			Assert.That(Directory.GetFiles(Path.Combine(m_Directory, id)).Select(Path.GetFileName), Is.EquivalentTo(new[] { "report.json" }));
		}

		[Test]
		public void TheSameDefectIsWrittenInFullAFewTimesAndCounted()
		{
			Start(perSignature: 2);

			var ids = Enumerable.Range(0, 5).Select(_ => DexcQA.Report("check", "same", "Same")).ToList();
			DexcQA.Check(true, "fine", "never reported");
			DexcQA.Flush();

			Assert.That(ids.Count(id => id != null), Is.EqualTo(2));
			Assert.That(Folders(), Has.Length.EqualTo(2));
			var counts = File.ReadAllText(Directory.GetFiles(m_Directory, "signatures-*.json").Single());
			Assert.That(counts, Does.Contain("\"signature\": \"same\""));
			Assert.That(counts, Does.Contain("\"count\": 5"));
			Assert.That(counts, Does.Not.Contain("fine"));
		}

		[Test]
		public void ExceptionsLoggedFromAnyThreadAreReportedOnTheMainThreadByWhatTheyHaveInCommon()
		{
			Start();
			QARoot.Current.OnLog("NullReferenceException: Object reference", "Game.Stage.Open () (at Assets/Stage.cs:10)\nGame.Bot.Step () (at Assets/Bot.cs:3)\n", LogType.Exception);
			QARoot.Current.OnLog("NullReferenceException: Object reference", "Game.Stage.Open () (at Assets/Stage.cs:12)\nGame.Bot.Step () (at Assets/Bot.cs:3)\n", LogType.Exception);
			QARoot.Current.OnLog("Failed to load stage 12", "UnityEngine.Debug:LogError (object)\nGame.Loader.Load () (at Assets/Loader.cs:40)\n", LogType.Error);
			Assert.That(Directory.Exists(m_Directory) ? Folders() : new string[0], Is.Empty, "not on the thread that logged");

			QARoot.Current.Pump();
			DexcQA.Flush();

			Assert.That(Folders(), Has.Length.EqualTo(3));
			var counts = File.ReadAllText(Directory.GetFiles(m_Directory, "signatures-*.json").Single());
			Assert.That(counts, Does.Contain("\"signature\": \"exception:NullReferenceException@Game.Stage.Open ();Game.Bot.Step ()\""));
			Assert.That(counts, Does.Contain("\"count\": 2"), "another line of the same code is the same defect");
			Assert.That(counts, Does.Contain("\"signature\": \"error:Failed to load stage #@Game.Loader.Load ()\""));
		}

		[Test]
		public void AnExceptionOfABotIsTakenByItsMessageNotByTheInterpreter()
		{
			const string Interpreted = "System.Reflection.MethodBase:Invoke (object,object[])\nILRuntime.CLR.Method.CLRMethod:Invoke ()\nDexc.Autopilot.AutopilotServer:Pump ()\nGame.AutopilotHost:Update ()\n";
			const string GameUnderBot = "Game.Door.Open () (at Assets/Door.cs:4)\nSystem.Reflection.MethodBase:Invoke (object,object[])\nGame.AutopilotHost:Update ()\n";

			Assert.That(QARoot.SignatureOf("InvalidOperationException: door 12 jammed", "UnityEngine.Debug:LogException (System.Exception)\n" + Interpreted, true),
				Is.EqualTo("exception:InvalidOperationException:InvalidOperationException: door # jammed"));
			Assert.That(QARoot.SignatureOf("NullReferenceException: x", GameUnderBot, true), Is.EqualTo("exception:NullReferenceException@Game.Door.Open ()"));
		}

		[Test]
		public void ContributorsAddToTheReportAndSinksHearOfItWritten()
		{
			var recorder = new Recorder();
			DexcQA.AddContributor(new Throwing());
			DexcQA.AddContributor(recorder);
			DexcQA.AddSink(recorder);
			Start();

			var id = DexcQA.Report("agent", "ui.overlap", "Buttons overlap");

			Assert.That(recorder.Calls, Is.EqualTo(new[] { "contribute ui.overlap", "written " + id + " True" }));
			var report = Report(id);
			Assert.That(report, Does.Contain("\"stage\": \"2\""));
			Assert.That(report, Does.Contain("\"save.txt\""));
			Assert.That(report, Does.Contain("no save"), "a contributor that throws is noted");
			Assert.That(File.ReadAllText(Path.Combine(m_Directory, id, "save.txt")), Is.EqualTo("A"));
		}

		[Test]
		public void WhatTelemetryKnowsOfTheGameGoesIn()
		{
			var telemetry = Path.Combine(m_Directory, "telemetry");
			DexcTelemetry.Start(new TelemetryOptions { Directory = telemetry, Location = new Place(), CollectUnityMetrics = false });
			try
			{
				DexcTelemetry.Annotate("stage", "1");
				Start();

				var report = Report(DexcQA.Report("check", "x", "x"));

				Assert.That(report, Does.Contain("\"stage\": \"1\""));
				Assert.That(report, Does.Contain("\"map\": \"yard\""));
				Assert.That(report, Does.Contain("\"z\": -2"));
			}
			finally
			{
				DexcTelemetry.Stop();
				DexcTelemetry.Annotate("stage", null);
			}
		}
	}
}
