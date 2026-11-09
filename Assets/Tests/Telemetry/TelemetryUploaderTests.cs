using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Dexc.Telemetry;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace DexcDebugger.Tests.Telemetry
{
	/// <summary>The Push rules, against a server on this machine that answers as each test says.</summary>
	public sealed class TelemetryUploaderTests
	{
		sealed class Received
		{
			internal string Path;
			internal byte[] Body;
			internal string Encoding;
			internal string Key;
		}

		sealed class Answer
		{
			internal int Status = 200;
			internal byte[] Body = Array.Empty<byte>();
			internal string RetryAfter;
		}

		static readonly TimeSpan s_Wait = TimeSpan.FromSeconds(5);

		string m_Directory;
		HttpListener m_Listener;
		Thread m_Serving;
		string m_Endpoint;
		readonly List<Received> m_Received = new List<Received>();
		Func<int, Answer> m_Answer;

		[SetUp]
		public void SetUp()
		{
			m_Directory = Path.Combine(Path.GetTempPath(), "DexcTelemetryUpload-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(m_Directory);
			m_Received.Clear();
			m_Answer = _ => new Answer();
			var probe = new TcpListener(IPAddress.Loopback, 0);
			probe.Start();
			var port = ((IPEndPoint)probe.LocalEndpoint).Port;
			probe.Stop();
			m_Endpoint = "http://127.0.0.1:" + port + "/";
			m_Listener = new HttpListener();
			m_Listener.Prefixes.Add(m_Endpoint);
			m_Listener.Start();
			m_Serving = new Thread(Serve) { IsBackground = true };
			m_Serving.Start();
		}

		[TearDown]
		public void TearDown()
		{
			m_Listener.Close();
			m_Serving.Join(s_Wait);
			if (Directory.Exists(m_Directory))
			{
				Directory.Delete(m_Directory, true);
			}
		}

		void Serve()
		{
			while (true)
			{
				HttpListenerContext context;
				try
				{
					context = m_Listener.GetContext();
				}
				catch (Exception)
				{
					return;
				}
				var body = new MemoryStream();
				context.Request.InputStream.CopyTo(body);
				var bytes = body.ToArray();
				var encoding = context.Request.Headers["Content-Encoding"];
				if (encoding == "gzip")
				{
					var plain = new MemoryStream();
					using (var gzip = new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress))
					{
						gzip.CopyTo(plain);
					}
					bytes = plain.ToArray();
				}
				int index;
				lock (m_Received)
				{
					m_Received.Add(new Received { Path = context.Request.Url.AbsolutePath, Body = bytes, Encoding = encoding, Key = context.Request.Headers["X-Key"] });
					index = m_Received.Count - 1;
				}
				var answer = m_Answer(index);
				context.Response.StatusCode = answer.Status;
				context.Response.ContentType = "application/x-protobuf";
				if (answer.RetryAfter != null)
				{
					context.Response.AddHeader("Retry-After", answer.RetryAfter);
				}
				context.Response.OutputStream.Write(answer.Body, 0, answer.Body.Length);
				context.Response.Close();
			}
		}

		List<Received> Requests()
		{
			lock (m_Received)
			{
				return m_Received.ToList();
			}
		}

		TelemetryUploadOptions Options(int maximumRequestBytes = 1024 * 1024)
		{
			return new TelemetryUploadOptions
			{
				Endpoint = m_Endpoint,
				Headers = new Dictionary<string, string> { ["X-Key"] = "test" },
				MaximumRequestBytes = maximumRequestBytes,
			};
		}

		/// <summary>A closed file of frames whose bodies are their number repeated, as many bytes as given.</summary>
		void Frames(string name, params int[] sizes)
		{
			using (var stream = File.Create(Path.Combine(m_Directory, name)))
			{
				for (var i = 0; i < sizes.Length; i++)
				{
					var size = sizes[i];
					stream.Write(new[] { (byte)(size >> 24), (byte)(size >> 16), (byte)(size >> 8), (byte)size }, 0, 4);
					stream.Write(Enumerable.Repeat((byte)(i + 1), size).ToArray(), 0, size);
				}
			}
		}

		static byte[] Bodies(params (int Number, int Size)[] frames)
		{
			return frames.SelectMany(frame => Enumerable.Repeat((byte)frame.Number, frame.Size)).ToArray();
		}

		string[] Files()
		{
			return Directory.GetFiles(m_Directory).Select(Path.GetFileName).OrderBy(name => name, StringComparer.Ordinal).ToArray();
		}

		/// <summary>Frames go by, a tenth of a second each, until the condition holds.</summary>
		static IEnumerator Until(TelemetryUploader uploader, Func<bool> condition, Func<double> now)
		{
			var watch = Stopwatch.StartNew();
			while (!condition())
			{
				if (watch.Elapsed > TimeSpan.FromSeconds(15))
				{
					Assert.Fail("Timed out.");
				}
				uploader.Update(now());
				yield return null;
			}
		}

		[UnityTest]
		public IEnumerator ClosedFilesGoWholeFromWhereTheyWereLeftAndAreDeleted()
		{
			Frames("metrics-a.otlp", 10, 20);
			File.WriteAllText(Path.Combine(m_Directory, "metrics-a.otlp.sent"), "14");
			Frames("traces-b.otlp", 5);
			Frames("logs-c.otlp", 7);
			var files = new TelemetryFileSet(m_Directory, "run", 100000, 1000000);
			var time = 0d;
			var uploader = new TelemetryUploader(Options(), files, new TelemetryMetrics(), time);
			try
			{
				yield return Until(uploader, () => !uploader.IsBusy && Requests().Count == 2 && !File.Exists(Path.Combine(m_Directory, "traces-b.otlp")), () => time += 0.1);
			}
			finally
			{
				uploader.Dispose();
				files.Writing.Stop(s_Wait);
			}

			var requests = Requests();
			Assert.That(requests.Select(request => request.Path), Is.EquivalentTo(new[] { "/v1/metrics", "/v1/traces" }));
			Assert.That(requests.Single(request => request.Path == "/v1/metrics").Body, Is.EqualTo(Bodies((2, 20))), "the first frame was sent before");
			Assert.That(requests.Single(request => request.Path == "/v1/traces").Body, Is.EqualTo(Bodies((1, 5))));
			Assert.That(requests.Select(request => request.Encoding), Is.All.EqualTo("gzip"));
			Assert.That(requests.Select(request => request.Key), Is.All.EqualTo("test"));
			Assert.That(Files(), Is.EqualTo(new[] { "logs-c.otlp" }), "logs stay unless asked for, the rest go with their .sent");
		}

		[UnityTest]
		public IEnumerator AnOpenFileIsSentUpToItsLastWholeFrameAndDeletedOnceClosed()
		{
			var files = new TelemetryFileSet(m_Directory, "run", 100000, 1000000);
			var time = 0d;
			var uploader = new TelemetryUploader(Options(), files, new TelemetryMetrics(), time);
			var frames = files.Open("metrics");
			try
			{
				frames.WriteFrame(Bodies((3, 9)), 9);
				Assert.That(files.Writing.Wait(s_Wait), Is.True);
				yield return Until(uploader, () => !uploader.IsBusy && Requests().Count == 1, () => time += 0.1);
				Assert.That(File.ReadAllText(Path.Combine(m_Directory, "metrics-run.otlp.sent")), Is.EqualTo("13"));

				frames.Dispose();
				frames = null;
				Assert.That(files.Writing.Wait(s_Wait), Is.True);
				uploader.Kick();
				yield return Until(uploader, () => !uploader.IsBusy && Files().Length == 0, () => time += 0.1);
			}
			finally
			{
				frames?.Dispose();
				uploader.Dispose();
				files.Writing.Stop(s_Wait);
			}

			Assert.That(Requests().Single().Body, Is.EqualTo(Bodies((3, 9))), "nothing twice");
		}

		[UnityTest]
		public IEnumerator TheEndOfAnOpenFileWaitsForTheNextRoundUnlessItFillsARequest()
		{
			var files = new TelemetryFileSet(m_Directory, "run", 100000, 1000000);
			var time = 0d;
			var uploader = new TelemetryUploader(Options(20), files, new TelemetryMetrics(), time);
			var frames = files.Open("traces");
			try
			{
				frames.WriteFrame(Bodies((1, 15)), 15);
				frames.WriteFrame(Bodies((2, 15)), 15);
				frames.WriteFrame(Bodies((3, 15)), 15);
				Assert.That(files.Writing.Wait(s_Wait), Is.True);
				yield return Until(uploader, () => !uploader.IsBusy && Requests().Count == 2, () => time += 0.1);
				var stop = time + 3;
				yield return Until(uploader, () => time >= stop, () => time += 0.1);
				Assert.That(Requests().Count, Is.EqualTo(2), "the last frame does not fill a request: it waits");

				time += 60;
				yield return Until(uploader, () => !uploader.IsBusy && Requests().Count == 3, () => time += 0.1);
			}
			finally
			{
				frames?.Dispose();
				uploader.Dispose();
				files.Writing.Stop(s_Wait);
			}

			Assert.That(Requests().Select(request => request.Body), Is.EqualTo(new[] { Bodies((1, 15)), Bodies((2, 15)), Bodies((3, 15)) }));
		}

		[UnityTest]
		public IEnumerator EachOpenFileSendsItsEndOnceARound()
		{
			var files = new TelemetryFileSet(m_Directory, "run", 100000, 1000000);
			var time = 0d;
			var uploader = new TelemetryUploader(Options(), files, new TelemetryMetrics(), time);
			var metrics = files.Open("metrics");
			var traces = files.Open("traces");
			try
			{
				metrics.WriteFrame(Bodies((1, 5)), 5);
				traces.WriteFrame(Bodies((2, 5)), 5);
				Assert.That(files.Writing.Wait(s_Wait), Is.True);
				yield return Until(uploader, () => !uploader.IsBusy && Requests().Count == 2, () => time += 0.1);
				metrics.WriteFrame(Bodies((3, 5)), 5);
				Assert.That(files.Writing.Wait(s_Wait), Is.True);
				var stop = time + 3;
				yield return Until(uploader, () => time >= stop, () => time += 0.1);
			}
			finally
			{
				metrics.Dispose();
				traces.Dispose();
				uploader.Dispose();
				files.Writing.Stop(s_Wait);
			}

			Assert.That(Requests().Select(request => request.Path), Is.EquivalentTo(new[] { "/v1/metrics", "/v1/traces" }), "one round sends both, and the next frame waits for the next round");
		}

		[UnityTest]
		public IEnumerator UnavailableIsSentAgainAfterRetryAfter()
		{
			Frames("metrics-a.otlp", 10);
			m_Answer = index => index == 0 ? new Answer { Status = 503, RetryAfter = "0" } : new Answer();
			var files = new TelemetryFileSet(m_Directory, "run", 100000, 1000000);
			var time = 0d;
			var uploader = new TelemetryUploader(Options(), files, new TelemetryMetrics(), time);
			try
			{
				yield return Until(uploader, () => !uploader.IsBusy && Files().Length == 0, () => time += 0.1);
			}
			finally
			{
				uploader.Dispose();
				files.Writing.Stop(s_Wait);
			}

			Assert.That(Requests().Select(request => request.Body), Is.EqualTo(new[] { Bodies((1, 10)), Bodies((1, 10)) }));
		}

		[UnityTest]
		public IEnumerator TooLargeIsSplitInHalvesAndARefusalIsDropped()
		{
			Frames("metrics-a.otlp", 100, 100, 100, 100);
			Frames("traces-b.otlp", 10);
			m_Answer = index => index == 0 ? new Answer { Status = 413 } : index == 3 ? new Answer { Status = 400 } : new Answer();
			var files = new TelemetryFileSet(m_Directory, "run", 100000, 1000000);
			var time = 0d;
			var uploader = new TelemetryUploader(Options(1000), files, new TelemetryMetrics(), time);
			try
			{
				yield return Until(uploader, () => !uploader.IsBusy && Files().Length == 0, () => time += 0.1);
			}
			finally
			{
				uploader.Dispose();
				files.Writing.Stop(s_Wait);
			}

			Assert.That(Requests().Select(request => request.Body.Length), Is.EqualTo(new[] { 400, 200, 200, 10 }), "a 400 is not sent again");
		}

		[Test]
		public void TheAnswersAreRead()
		{
			// ExportMetricsServiceResponse { partial_success { rejected_data_points: 3, error_message: "x" } }
			Assert.That(TelemetryUploader.PartialSuccessRejected(new byte[] { 0x0A, 0x05, 0x08, 0x03, 0x12, 0x01, (byte)'x' }), Is.EqualTo(3));
			Assert.That(TelemetryUploader.PartialSuccessRejected(Array.Empty<byte>()), Is.Zero);
			// google.rpc.Status { code: 3, message: "http: request body too large" }
			var message = System.Text.Encoding.UTF8.GetBytes("http: request body too large");
			var status = new byte[] { 0x08, 0x03, 0x12, (byte)message.Length }.Concat(message).ToArray();
			Assert.That(TelemetryUploader.StatusMessage(status), Is.EqualTo("http: request body too large"));
		}
	}
}
