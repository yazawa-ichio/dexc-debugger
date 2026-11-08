using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Unity.Collections;
using Debug = UnityEngine.Debug;
using UnityEngine.Networking;

namespace Dexc.Telemetry
{
	/// <summary>
	/// Sends the telemetry files to an OTLP/HTTP endpoint by the Push rules of the specification. The files are the queue:
	/// how far each is sent is kept in a <c>.sent</c> file beside it, and a closed file sent whole goes with its <c>.sent</c>.
	/// The file work and the compression run on a worker; the main thread starts a request and looks, once a frame, whether
	/// it is done. One request at a time. Main thread, but for <see cref="Step"/>.
	/// </summary>
	internal sealed class TelemetryUploader : IDisposable
	{
		internal const string SentExtension = ".sent";
		const double FirstWaitSeconds = 5;
		const double LongestWaitSeconds = 30;
		const double GiveUpSeconds = 300;
		const double Multiplier = 1.5;
		const int GzipRoom = 64 * 1024;

		/// <summary>Whole frames of one file, their bodies joined and maybe compressed, found by the worker.</summary>
		sealed class Chunk
		{
			internal string Path;
			internal string Signal;
			internal long Offset;
			internal long Consumed;
			internal int Frames;
			internal int BodyBytes;
			internal byte[] Payload;
			internal int PayloadLength;
			internal bool Compressed;
		}

		readonly TelemetryUploadOptions m_Options;
		readonly TelemetryFileSet m_Files;
		readonly string m_Endpoint;
		readonly TelemetryCounter m_SentBytes;
		readonly TelemetryCounter m_Dropped;
		readonly TelemetryCounter m_Rejected;
		readonly TelemetryCounter m_Retries;
		readonly System.Random m_Random = new System.Random();
		readonly byte[] m_Header = new byte[TelemetryFrameWriter.HeaderSize];
		byte[] m_Body;
		byte[] m_Compressed;
		NativeArray<byte> m_Native;
		Task<Chunk> m_Preparing;
		UnityWebRequest m_Request;
		Chunk m_Chunk;
		string m_SettledPath;
		long m_SettledOffset;
		double m_NextRound;
		double m_RetryAt = -1;
		double m_RetryStarted = -1;
		double m_Wait = FirstWaitSeconds;
		int m_Limit;
		bool m_Kicked;

		internal TelemetryUploader(TelemetryUploadOptions options, TelemetryFileSet files, TelemetryMetrics metrics, double now)
		{
			m_Options = options;
			m_Files = files;
			m_Endpoint = options.Endpoint.TrimEnd('/') + "/v1/";
			m_SentBytes = metrics.Counter("dexc.telemetry.upload.size", "By");
			m_Dropped = metrics.Counter("dexc.telemetry.upload.dropped", "{frame}");
			m_Rejected = metrics.Counter("dexc.telemetry.upload.rejected", "{record}");
			m_Retries = metrics.Counter("dexc.telemetry.upload.retries", "{request}");
			m_Body = new byte[options.MaximumRequestBytes];
			m_Compressed = new byte[options.MaximumRequestBytes + GzipRoom];
			m_Native = new NativeArray<byte>(m_Compressed.Length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
			m_Limit = options.MaximumRequestBytes;
			m_NextRound = now;
		}

		/// <summary>Whether a request is out or being made ready.</summary>
		internal bool IsBusy => m_Request != null || m_Preparing != null;

		/// <summary>Sends at the next frame instead of waiting for the interval, as when files were closed.</summary>
		internal void Kick()
		{
			m_Kicked = true;
		}

		/// <summary>
		/// The application may not come back: when nothing is under way, reads the next frames here and sends them without
		/// waiting for the answer. What does not get through is sent again later, from the .sent.
		/// </summary>
		internal void Pause()
		{
			if (m_Request != null || m_Preparing != null || m_RetryAt >= 0)
			{
				return;
			}
			try
			{
				m_Chunk = Step(TakeSettled(), m_Limit);
			}
			catch (IOException exception)
			{
				Debug.LogWarning("Dexc Telemetry: " + exception.Message);
				return;
			}
			if (m_Chunk != null)
			{
				Send();
			}
		}

		/// <summary>Moves the sending on. Once a frame, cheap when there is nothing to look at.</summary>
		internal void Update(double now)
		{
			if (m_Request != null)
			{
				if (m_Request.isDone)
				{
					Answer(now);
				}
				return;
			}
			if (m_Preparing != null)
			{
				if (!m_Preparing.IsCompleted)
				{
					return;
				}
				var preparing = m_Preparing;
				m_Preparing = null;
				if (preparing.IsFaulted)
				{
					// A file another program holds, or a full disk: the next round tries again.
					Debug.LogWarning("Dexc Telemetry: " + preparing.Exception.GetBaseException().Message);
					m_NextRound = now + m_Options.IntervalSeconds;
					return;
				}
				m_Chunk = preparing.Result;
				if (m_Chunk == null)
				{
					m_NextRound = now + m_Options.IntervalSeconds;
					return;
				}
				Send();
				return;
			}
			if (m_RetryAt >= 0)
			{
				if (now >= m_RetryAt)
				{
					m_RetryAt = -1;
					Send();
				}
				return;
			}
			if (now >= m_NextRound || m_Kicked)
			{
				m_Kicked = false;
				Prepare();
			}
		}

		void Prepare()
		{
			var settled = TakeSettled();
			var limit = m_Limit;
#if UNITY_WEBGL && !UNITY_EDITOR
			// No threads.
			try
			{
				m_Preparing = Task.FromResult(Step(settled, limit));
			}
			catch (IOException exception)
			{
				m_Preparing = Task.FromException<Chunk>(exception);
			}
#else
			m_Preparing = Task.Run(() => Step(settled, limit));
#endif
		}

		(string Path, long Offset)? TakeSettled()
		{
			if (m_SettledPath == null)
			{
				return null;
			}
			var settled = (m_SettledPath, m_SettledOffset);
			m_SettledPath = null;
			return settled;
		}

		void Send()
		{
			var chunk = m_Chunk;
			if (m_Native.Length < chunk.PayloadLength)
			{
				// A single frame larger than the request size, sent as it is.
				m_Native.Dispose();
				m_Native = new NativeArray<byte>(chunk.PayloadLength, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
			}
			NativeArray<byte>.Copy(chunk.Payload, 0, m_Native, 0, chunk.PayloadLength);
			var request = new UnityWebRequest(m_Endpoint + chunk.Signal, UnityWebRequest.kHttpVerbPOST)
			{
				// The handler reads the persistent buffer without owning it; the request is gone before the buffer is.
				uploadHandler = new UploadHandlerRaw(m_Native.GetSubArray(0, chunk.PayloadLength), false) { contentType = "application/x-protobuf" },
				downloadHandler = new DownloadHandlerBuffer(),
				timeout = m_Options.TimeoutSeconds,
				disposeUploadHandlerOnDispose = true,
				disposeDownloadHandlerOnDispose = true,
			};
			if (chunk.Compressed)
			{
				request.SetRequestHeader("Content-Encoding", "gzip");
			}
			if (m_Options.Headers != null)
			{
				foreach (var header in m_Options.Headers)
				{
					request.SetRequestHeader(header.Key, header.Value);
				}
			}
			m_Request = request;
			request.SendWebRequest();
		}

		/// <summary>The rules of OTLP: 429, 502, 503 and 504 and a lost connection are sent again, the rest is not.</summary>
		void Answer(double now)
		{
			var request = m_Request;
			m_Request = null;
			var status = request.result == UnityWebRequest.Result.ConnectionError ? 0 : (int)request.responseCode;
			var body = request.downloadHandler?.data;
			var retryAfter = request.GetResponseHeader("Retry-After");
			request.Dispose();
			var chunk = m_Chunk;
			if (status >= 200 && status < 300)
			{
				m_SentBytes.Add(chunk.PayloadLength, TelemetryDetail.Basic);
				m_Rejected.Add(PartialSuccessRejected(body), TelemetryDetail.Basic);
				Settled(chunk);
				return;
			}
			if (status == 413 || (status == 400 && StatusMessage(body).Contains("too large")))
			{
				if (chunk.Frames > 1)
				{
					// Half the frames, from the same place: the .sent has not moved.
					m_Chunk = null;
					m_Limit = Math.Max(1, chunk.BodyBytes / 2);
					Prepare();
					return;
				}
				m_Dropped.Add(chunk.Frames, TelemetryDetail.Basic);
				Settled(chunk);
				return;
			}
			if (status != 0 && status != 429 && status != 502 && status != 503 && status != 504)
			{
				m_Dropped.Add(chunk.Frames, TelemetryDetail.Basic);
				Settled(chunk);
				return;
			}
			if (m_RetryStarted < 0)
			{
				m_RetryStarted = now;
			}
			if (now - m_RetryStarted >= GiveUpSeconds)
			{
				// The file stays as it is: the next round sends it again.
				Reset();
				m_NextRound = now + m_Options.IntervalSeconds;
				return;
			}
			m_Retries.Add(1, TelemetryDetail.Basic);
			m_RetryAt = now + (RetryAfter(retryAfter) ?? m_Wait * (0.5 + m_Random.NextDouble()));
			m_Wait = Math.Min(m_Wait * Multiplier, LongestWaitSeconds);
		}

		/// <summary>The chunk is done with, sent or dropped: the worker writes it down first, then goes on at once.</summary>
		void Settled(Chunk chunk)
		{
			m_SettledPath = chunk.Path;
			m_SettledOffset = chunk.Offset + chunk.Consumed;
			Reset();
			Prepare();
		}

		void Reset()
		{
			m_Chunk = null;
			m_Limit = m_Options.MaximumRequestBytes;
			m_Wait = FirstWaitSeconds;
			m_RetryStarted = -1;
		}

		/// <summary>
		/// On the worker: writes down the chunk the main thread settled, then finds the next whole frames, closed files first,
		/// oldest first. Closed files of any run, and the open files of this run up to their last whole frame. The open files
		/// of another Player are its own to send.
		/// </summary>
		Chunk Step((string Path, long Offset)? settled, int limit)
		{
			if (settled.HasValue)
			{
				Settle(settled.Value.Path, settled.Value.Offset);
			}
			var folder = m_Files.Folder;
			var files = new DirectoryInfo(folder).GetFiles()
				.Select(file => (Info: file, Path: System.IO.Path.Combine(folder, file.Name), Open: file.Name.EndsWith(TelemetryFileSet.OpenExtension, StringComparison.OrdinalIgnoreCase)))
				.Where(file => IsSent(file.Info.Name) && (!file.Open || m_Files.IsOpen(file.Path)))
				.OrderBy(file => file.Open)
				.ThenBy(file => file.Info.LastWriteTimeUtc)
				.ToList();
			foreach (var file in files)
			{
				var offset = SentOffset(file.Path);
				if (!file.Open && offset >= file.Info.Length)
				{
					Settle(file.Path, offset);
					continue;
				}
				var chunk = Read(file.Path, offset, limit);
				if (chunk != null)
				{
					return chunk;
				}
			}
			DeleteOrphans(folder);
			return null;
		}

		bool IsSent(string name)
		{
			switch (SignalOf(name))
			{
				case "metrics":
					return m_Options.Metrics;
				case "traces":
					return m_Options.Traces;
				case "logs":
					return m_Options.Logs;
				default:
					return false;
			}
		}

		static string SignalOf(string name)
		{
			if (!name.EndsWith(TelemetryFileSet.Extension, StringComparison.OrdinalIgnoreCase) && !name.EndsWith(TelemetryFileSet.OpenExtension, StringComparison.OrdinalIgnoreCase))
			{
				return null;
			}
			var dash = name.IndexOf('-');
			return dash < 0 ? null : name.Substring(0, dash);
		}

		/// <summary>The name a file has once closed. Its .sent follows that name, so that it holds across the rename.</summary>
		static string ClosedPath(string path)
		{
			return path.EndsWith(TelemetryFileSet.OpenExtension, StringComparison.OrdinalIgnoreCase)
				? path.Substring(0, path.Length - TelemetryFileSet.OpenExtension.Length) + TelemetryFileSet.Extension
				: path;
		}

		static long SentOffset(string path)
		{
			var sent = ClosedPath(path) + SentExtension;
			return File.Exists(sent) && long.TryParse(File.ReadAllText(sent), NumberStyles.Integer, CultureInfo.InvariantCulture, out var offset) ? offset : 0;
		}

		/// <summary>Keeps how far a file is sent. A closed file sent whole goes, with its .sent.</summary>
		static void Settle(string path, long offset)
		{
			var closed = ClosedPath(path);
			var sent = closed + SentExtension;
			if (File.Exists(closed) && new FileInfo(closed).Length <= offset)
			{
				File.Delete(closed);
				File.Delete(sent);
				return;
			}
			File.WriteAllText(sent, offset.ToString(CultureInfo.InvariantCulture));
		}

		/// <summary>The .sent of files that are gone, as when the limit of the folder took them.</summary>
		static void DeleteOrphans(string folder)
		{
			foreach (var sent in Directory.GetFiles(folder, "*" + TelemetryFileSet.Extension + SentExtension))
			{
				var closed = sent.Substring(0, sent.Length - SentExtension.Length);
				var open = closed.Substring(0, closed.Length - TelemetryFileSet.Extension.Length) + TelemetryFileSet.OpenExtension;
				if (!File.Exists(closed) && !File.Exists(open))
				{
					File.Delete(sent);
				}
			}
		}

		/// <summary>The whole frames from the offset, at least one and up to the limit, their bodies joined.</summary>
		Chunk Read(string path, long offset, int limit)
		{
			FileStream stream;
			try
			{
				stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
			}
			catch (FileNotFoundException)
			{
				// Closed and renamed meanwhile, or deleted for the limit of the folder.
				path = ClosedPath(path);
				if (!File.Exists(path))
				{
					return null;
				}
				stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
			}
			using (stream)
			{
				var length = stream.Length;
				var position = offset;
				var body = 0;
				var frames = 0;
				stream.Position = offset;
				while (position + m_Header.Length <= length && stream.Read(m_Header, 0, m_Header.Length) == m_Header.Length)
				{
					var size = (m_Header[0] << 24) | (m_Header[1] << 16) | (m_Header[2] << 8) | m_Header[3];
					if (size < 0 || position + m_Header.Length + size > length || (frames > 0 && body + size > limit))
					{
						// Not whole yet, or the next request's.
						break;
					}
					if (body + size > m_Body.Length)
					{
						Array.Resize(ref m_Body, body + size);
					}
					if (stream.Read(m_Body, body, size) != size)
					{
						break;
					}
					body += size;
					position += m_Header.Length + size;
					frames++;
				}
				if (frames == 0)
				{
					return null;
				}
				var chunk = new Chunk
				{
					Path = path,
					Signal = SignalOf(System.IO.Path.GetFileName(path)),
					Offset = offset,
					Consumed = position - offset,
					Frames = frames,
					BodyBytes = body,
				};
				Compress(chunk);
				return chunk;
			}
		}

		void Compress(Chunk chunk)
		{
			if (m_Options.Gzip)
			{
				try
				{
					var output = new MemoryStream(m_Compressed, 0, m_Compressed.Length, true, false);
					using (var gzip = new GZipStream(output, CompressionLevel.Fastest, true))
					{
						gzip.Write(m_Body, 0, chunk.BodyBytes);
					}
					chunk.Payload = m_Compressed;
					chunk.PayloadLength = (int)output.Position;
					chunk.Compressed = true;
					return;
				}
				catch (NotSupportedException)
				{
					// Did not fit, as a frame larger than the request size may not: it goes as it is.
				}
			}
			chunk.Payload = m_Body;
			chunk.PayloadLength = chunk.BodyBytes;
			chunk.Compressed = false;
		}

		static double? RetryAfter(string header)
		{
			if (string.IsNullOrEmpty(header))
			{
				return null;
			}
			if (double.TryParse(header, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
			{
				return Math.Max(0, seconds);
			}
			if (DateTime.TryParse(header, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var date))
			{
				return Math.Max(0, (date - DateTime.UtcNow).TotalSeconds);
			}
			return null;
		}

		/// <summary>
		/// The records a partial success refused. In every signal's Export*ServiceResponse it is the first field, a varint,
		/// of the message in field 1.
		/// </summary>
		internal static long PartialSuccessRejected(byte[] response)
		{
			if (response == null || !FindBytes(response, 1, out var start, out var length))
			{
				return 0;
			}
			var position = start;
			var end = start + length;
			while (position < end)
			{
				var tag = Varint(response, ref position);
				if (tag >> 3 == 1 && (tag & 7) == 0)
				{
					return (long)Varint(response, ref position);
				}
				if (!Skip(response, ref position, (int)(tag & 7)))
				{
					return 0;
				}
			}
			return 0;
		}

		/// <summary>The message of the google.rpc.Status of a refusal, field 2.</summary>
		internal static string StatusMessage(byte[] status)
		{
			return status != null && FindBytes(status, 2, out var start, out var length)
				? System.Text.Encoding.UTF8.GetString(status, start, length)
				: string.Empty;
		}

		static bool FindBytes(byte[] message, int number, out int start, out int length)
		{
			var position = 0;
			while (position < message.Length)
			{
				var tag = Varint(message, ref position);
				if ((int)(tag >> 3) == number && (tag & 7) == 2)
				{
					var size = Varint(message, ref position);
					if (size > (ulong)(message.Length - position))
					{
						break;
					}
					start = position;
					length = (int)size;
					return true;
				}
				if (!Skip(message, ref position, (int)(tag & 7)))
				{
					break;
				}
			}
			start = 0;
			length = 0;
			return false;
		}

		static ulong Varint(byte[] data, ref int position)
		{
			ulong value = 0;
			for (var shift = 0; shift < 64 && position < data.Length; shift += 7)
			{
				var current = data[position++];
				value |= (ulong)(current & 0x7F) << shift;
				if ((current & 0x80) == 0)
				{
					break;
				}
			}
			return value;
		}

		static bool Skip(byte[] data, ref int position, int wire)
		{
			ulong size;
			switch (wire)
			{
				case 0:
					Varint(data, ref position);
					return true;
				case 1:
					size = 8;
					break;
				case 2:
					size = Varint(data, ref position);
					break;
				case 5:
					size = 4;
					break;
				default:
					return false;
			}
			if (size > (ulong)(data.Length - position))
			{
				return false;
			}
			position += (int)size;
			return true;
		}

		/// <summary>Leaves the request: what it carried is sent again from the .sent at the next start.</summary>
		public void Dispose()
		{
			if (m_Request != null)
			{
				m_Request.Abort();
				m_Request.Dispose();
				m_Request = null;
			}
			try
			{
				m_Preparing?.Wait(TimeSpan.FromSeconds(5));
			}
			catch (AggregateException)
			{
				// The files stay as they are.
			}
			m_Preparing = null;
			var settled = TakeSettled();
			if (settled.HasValue)
			{
				try
				{
					Settle(settled.Value.Path, settled.Value.Offset);
				}
				catch (IOException)
				{
					// Sent again at the next start.
				}
			}
			if (m_Native.IsCreated)
			{
				m_Native.Dispose();
			}
		}
	}
}
