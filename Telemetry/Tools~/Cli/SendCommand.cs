using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using Google.Protobuf;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;

namespace Dexc.Telemetry.Cli
{
	/// <summary>
	/// Sends files to an OTLP/HTTP endpoint by the Push rules of the specification: the bodies of following frames joined
	/// into one request up to a size, gzip at its fastest, the retries of OTLP with the intervals of the Collector, a request
	/// too large split in two.
	/// </summary>
	internal sealed class SendCommand
	{
		static readonly TimeSpan s_FirstWait = TimeSpan.FromSeconds(5);
		static readonly TimeSpan s_LongestWait = TimeSpan.FromSeconds(30);
		static readonly TimeSpan s_GiveUp = TimeSpan.FromSeconds(300);
		const double Multiplier = 1.5;

		readonly HttpClient m_Client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
		readonly Random m_Random = new Random();
		readonly string m_Endpoint;
		readonly int m_MaximumBytes;
		readonly bool m_Gzip;
		int m_Requests;
		int m_Frames;
		long m_Bytes;
		long m_Rejected;
		int m_Dropped;
		int m_Failed;

		SendCommand(string endpoint, int maximumBytes, bool gzip, IEnumerable<KeyValuePair<string, string>> headers)
		{
			m_Endpoint = endpoint.TrimEnd('/');
			m_MaximumBytes = maximumBytes;
			m_Gzip = gzip;
			foreach (var header in headers)
			{
				m_Client.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);
			}
		}

		internal static int Run(string path, string endpoint, int maximumBytes, bool gzip, bool open, IEnumerable<KeyValuePair<string, string>> headers)
		{
			var command = new SendCommand(endpoint, maximumBytes, gzip, headers);
			foreach (var file in TelemetryFiles.Find(path, open))
			{
				command.SendFile(file);
			}
			Console.WriteLine("sent " + command.m_Frames + " frames in " + command.m_Requests + " requests, " + command.m_Bytes + " bytes on the wire; rejected records " + command.m_Rejected + ", dropped frames " + command.m_Dropped + ", files given up " + command.m_Failed);
			return command.m_Dropped == 0 && command.m_Failed == 0 ? 0 : 1;
		}

		void SendFile(string file)
		{
			var signal = TelemetryFiles.SignalOf(file);
			var frames = TelemetryFiles.ReadFrames(file);
			var start = 0;
			while (start < frames.Count)
			{
				// Following frames up to the size, and at least one: a frame larger than the size goes alone.
				var count = 1;
				var size = frames[start].Length;
				while (start + count < frames.Count && size + frames[start + count].Length <= m_MaximumBytes)
				{
					size += frames[start + count].Length;
					count++;
				}
				if (!Send(signal, frames.GetRange(start, count)))
				{
					Console.WriteLine(Path.GetFileName(file) + ": gave up after " + s_GiveUp.TotalSeconds + "s of retries");
					m_Failed++;
					return;
				}
				start += count;
			}
			Console.WriteLine(Path.GetFileName(file) + ": " + frames.Count + " frames");
		}

		/// <summary>Sends the frames as one request until it is taken or refused. False only when the retries ran out.</summary>
		bool Send(string signal, List<byte[]> frames)
		{
			var body = new byte[frames.Sum(frame => frame.Length)];
			var offset = 0;
			foreach (var frame in frames)
			{
				Buffer.BlockCopy(frame, 0, body, offset, frame.Length);
				offset += frame.Length;
			}
			var payload = m_Gzip ? Gzip(body) : body;
			var started = DateTime.UtcNow;
			var wait = s_FirstWait;
			while (true)
			{
				HttpResponseMessage response = null;
				try
				{
					var content = new ByteArrayContent(payload);
					content.Headers.ContentType = new MediaTypeHeaderValue("application/x-protobuf");
					if (m_Gzip)
					{
						content.Headers.ContentEncoding.Add("gzip");
					}
					response = m_Client.PostAsync(m_Endpoint + "/v1/" + signal, content).GetAwaiter().GetResult();
				}
				catch (HttpRequestException)
				{
					response = null;
				}
				catch (OperationCanceledException)
				{
					// The timeout of the client.
					response = null;
				}
				var status = response == null ? 0 : (int)response.StatusCode;
				if (status == 200)
				{
					m_Requests++;
					m_Frames += frames.Count;
					m_Bytes += payload.Length;
					m_Rejected += Rejected(signal, response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult());
					return true;
				}
				var tooLarge = status == 413 || (status == 400 && Message(response).Contains("too large"));
				if (tooLarge && frames.Count > 1)
				{
					var half = frames.Count / 2;
					return Send(signal, frames.GetRange(0, half)) && Send(signal, frames.GetRange(half, frames.Count - half));
				}
				if (status != 0 && status != 429 && status != 502 && status != 503 && status != 504)
				{
					Console.WriteLine("refused with " + status + ": " + frames.Count + " frames dropped");
					m_Dropped += frames.Count;
					return true;
				}
				var delay = RetryAfter(response) ?? Jitter(wait);
				if (DateTime.UtcNow - started + delay > s_GiveUp)
				{
					return false;
				}
				Console.WriteLine((status == 0 ? "no answer" : "status " + status) + ", again in " + delay.TotalSeconds.ToString("F1") + "s");
				Thread.Sleep(delay);
				wait = TimeSpan.FromTicks(Math.Min((long)(wait.Ticks * Multiplier), s_LongestWait.Ticks));
			}
		}

		/// <summary>
		/// The message of the Status a refusal carries. Collector 0.164.0 refuses a body over its limit with 400 and
		/// "http: request body too large", where OTLP asks for 413, so the message is read too.
		/// </summary>
		static string Message(HttpResponseMessage response)
		{
			try
			{
				var input = new CodedInputStream(response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult());
				uint tag;
				while ((tag = input.ReadTag()) != 0)
				{
					if (tag == 18)
					{
						return input.ReadString();
					}
					input.SkipLastField();
				}
			}
			catch (InvalidProtocolBufferException)
			{
			}
			return string.Empty;
		}

		/// <summary>Half to one and a half times the wait, so that many senders do not come back together.</summary>
		TimeSpan Jitter(TimeSpan wait)
		{
			return TimeSpan.FromTicks((long)(wait.Ticks * (0.5 + m_Random.NextDouble())));
		}

		static TimeSpan? RetryAfter(HttpResponseMessage response)
		{
			var retry = response?.Headers.RetryAfter;
			if (retry == null)
			{
				return null;
			}
			if (retry.Delta.HasValue)
			{
				return retry.Delta.Value;
			}
			if (retry.Date.HasValue)
			{
				var delay = retry.Date.Value - DateTimeOffset.UtcNow;
				return delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
			}
			return null;
		}

		/// <summary>The records a partial success refused. The request is not sent again.</summary>
		static long Rejected(string signal, byte[] body)
		{
			if (body.Length == 0)
			{
				return 0;
			}
			switch (signal)
			{
				case "metrics":
					return ExportMetricsServiceResponse.Parser.ParseFrom(body).PartialSuccess?.RejectedDataPoints ?? 0;
				case "logs":
					return ExportLogsServiceResponse.Parser.ParseFrom(body).PartialSuccess?.RejectedLogRecords ?? 0;
				default:
					return ExportTraceServiceResponse.Parser.ParseFrom(body).PartialSuccess?.RejectedSpans ?? 0;
			}
		}

		static byte[] Gzip(byte[] body)
		{
			var output = new MemoryStream();
			using (var gzip = new GZipStream(output, CompressionLevel.Fastest, true))
			{
				gzip.Write(body, 0, body.Length);
			}
			return output.ToArray();
		}
	}
}
