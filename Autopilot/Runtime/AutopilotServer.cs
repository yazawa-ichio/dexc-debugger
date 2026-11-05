using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace Dexc.Autopilot
{
	/// <summary>
	/// How a tool reaches the autopilot: one line in, one line of JSON out, over loopback.
	///
	/// A line is "token operation [base64]". The server only opens when the Player is started with -dexc-autopilot-token,
	/// so a build run the normal way listens on nothing. What it accepts is code, so the token is not optional.
	/// Requests wait for the main thread, where everything they touch lives.
	/// </summary>
	public sealed class AutopilotServer : IDisposable
	{
		private const int DefaultPort = 7890;
		private const float KeepSeconds = 120f;
		private const string EvalMethod = "Eval";
		private const string EvalType = "DexcEvalSnippet";

		/// <summary>An interpreter left alive for a snippet that goes on after it returns.</summary>
		private struct Kept
		{
			public IAutopilotModule Module;
			public float Expires;
		}

		private sealed class Request
		{
			public string Operation;
			public byte[] Payload;
			public string Reply;
			public readonly ManualResetEventSlim Done = new ManualResetEventSlim();
		}

		private readonly ConcurrentQueue<Request> m_Requests = new ConcurrentQueue<Request>();
		private readonly TcpListener m_Listener;
		private readonly string m_Token;
		private readonly IAutopilotHost m_Host;
		private readonly List<Kept> m_Kept = new List<Kept>();

		private AutopilotServer(IAutopilotHost host, string token, int port)
		{
			m_Host = host;
			m_Token = token;
			m_Listener = new TcpListener(IPAddress.Loopback, port);
			m_Listener.Start();
			new Thread(Accept) { IsBackground = true, Name = "AutopilotServer" }.Start();
			UnityEngine.Debug.Log("Autopilot server on port " + port);
		}

		public static AutopilotServer TryStart(IAutopilotHost host)
		{
			var token = ReadArgument("-dexc-autopilot-token");
			if (string.IsNullOrEmpty(token))
				return null;
			var port = ReadArgument("-dexc-autopilot-port");
			return new AutopilotServer(host, token, port != null ? int.Parse(port, CultureInfo.InvariantCulture) : DefaultPort);
		}

		public void Pump()
		{
			for (var index = m_Kept.Count - 1; index >= 0; index--)
			{
				if (Time.realtimeSinceStartup < m_Kept[index].Expires)
					continue;
				m_Kept[index].Module.Dispose();
				m_Kept.RemoveAt(index);
			}

			while (m_Requests.TryDequeue(out var request))
			{
				try
				{
					request.Reply = Write(Run(request));
				}
				catch (Exception exception)
				{
					request.Reply = Write(new Dictionary<string, object> { ["ok"] = false, ["error"] = exception.ToString() });
				}

				request.Done.Set();
			}
		}

		public void Dispose() => m_Listener.Stop();

		/// <summary>An operation is "status", or a verb and a VM: "load:ilruntime", "eval:pipeline", "eval-keep:ilruntime".</summary>
		private IDictionary<string, object> Run(Request request)
		{
			if (request.Operation == "status")
				return m_Host.Status();
			var parts = request.Operation.Split(':');
			if (parts.Length != 2 || m_Host.FindBackend(parts[1]) == null)
				throw new ArgumentException("Unknown operation: " + request.Operation);
			switch (parts[0])
			{
				case "load":
					return m_Host.Load(request.Payload, parts[1]);
				case "eval":
					return Eval(request.Payload, parts[1], false);
				case "eval-keep":
					return Eval(request.Payload, parts[1], true);
				default:
					throw new ArgumentException("Unknown operation: " + request.Operation);
			}
		}

		/// <summary>
		/// Each snippet gets a VM of its own, so a snippet that breaks takes nothing else with it and looking at the game
		/// never disturbs a running bot.
		/// </summary>
		/// <param name="keep">Leaves the VM alive for a while, for a snippet that goes on after it returns, such as an await.</param>
		private IDictionary<string, object> Eval(byte[] assembly, string backend, bool keep)
		{
			var result = new Dictionary<string, object>();
			var clock = Stopwatch.StartNew();
			IAutopilotModule module = null;
			try
			{
				module = m_Host.FindBackend(backend).Load(assembly);
				result["loadMs"] = clock.ElapsedMilliseconds;
				if (module.UnboundMembers.Count > 0)
				{
					// Refused rather than run: it would fail at whatever line reaches the missing member.
					result["ok"] = false;
					result["error"] = "Not available in this build: " + string.Join(", ", module.UnboundMembers);
					module.Dispose();
					return result;
				}

				clock.Restart();
				var value = module.Invoke(EvalType, EvalMethod);
				result["runMs"] = clock.ElapsedMilliseconds;
				result["ok"] = true;
				result["type"] = value?.GetType().FullName;
				result["value"] = Format(value);
			}
			catch (Exception exception)
			{
				result["ok"] = false;
				result["error"] = exception.GetType().Name + ": " + exception.Message;
				var trace = exception.ToString();
				result["trace"] = trace.Length > 1200 ? trace.Substring(0, 1200) : trace;
			}

			if (keep && module != null)
				m_Kept.Add(new Kept { Module = module, Expires = Time.realtimeSinceStartup + KeepSeconds });
			else
				module?.Dispose();
			return result;
		}

		private void Accept()
		{
			while (true)
			{
				TcpClient client;
				try
				{
					client = m_Listener.AcceptTcpClient();
				}
				catch (Exception)
				{
					return;
				}

				// A slow tool must not hold up the next one, and a pool thread costs nothing while idle.
				ThreadPool.QueueUserWorkItem(_ => Serve(client));
			}
		}

		private void Serve(TcpClient client)
		{
			using (client)
			using (var stream = client.GetStream())
			using (var reader = new StreamReader(stream, new UTF8Encoding(false)))
			using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
			{
				try
				{
					var parts = (reader.ReadLine() ?? string.Empty).Split(' ');
					if (parts.Length < 2 || parts[0] != m_Token)
						return;
					var request = new Request
					{
						Operation = parts[1],
						Payload = parts.Length > 2 ? Convert.FromBase64String(parts[2]) : null,
					};
					m_Requests.Enqueue(request);
					request.Done.Wait();
					writer.WriteLine(request.Reply);
					writer.Flush();
				}
				catch (IOException)
				{
					// The tool went away. There is nobody to tell.
				}
			}
		}

		private static string ReadArgument(string name)
		{
			var arguments = Environment.GetCommandLineArgs();
			for (var index = 0; index + 1 < arguments.Length; index++)
			{
				if (arguments[index] == name)
					return arguments[index + 1];
			}

			return null;
		}

		/// <summary>Text, because a value may hold a live object that must not cross the wire.</summary>
		private static string Format(object value)
		{
			if (value == null)
				return "null";
			if (value is string text)
				return text;
			if (!(value is IEnumerable items))
				return value.ToString();
			var builder = new StringBuilder("[");
			var first = true;
			foreach (var item in items)
			{
				if (!first)
					builder.Append(", ");
				first = false;
				builder.Append(item is IEnumerable && !(item is string) ? "[...]" : Format(item));
			}

			return builder.Append(']').ToString();
		}

		private static string Write(object value)
		{
			var builder = new StringBuilder();
			Write(builder, value);
			return builder.ToString();
		}

		private static void Write(StringBuilder builder, object value)
		{
			switch (value)
			{
				case null:
					builder.Append("null");
					break;
				case bool flag:
					builder.Append(flag ? "true" : "false");
					break;
				case string text:
					WriteString(builder, text);
					break;
				case IDictionary map:
					builder.Append('{');
					var firstKey = true;
					foreach (DictionaryEntry entry in map)
					{
						if (!firstKey)
							builder.Append(',');
						firstKey = false;
						WriteString(builder, entry.Key.ToString());
						builder.Append(':');
						Write(builder, entry.Value);
					}

					builder.Append('}');
					break;
				case IEnumerable list:
					builder.Append('[');
					var firstItem = true;
					foreach (var item in list)
					{
						if (!firstItem)
							builder.Append(',');
						firstItem = false;
						Write(builder, item);
					}

					builder.Append(']');
					break;
				case IFormattable number:
					builder.Append(number.ToString(null, CultureInfo.InvariantCulture));
					break;
				default:
					WriteString(builder, value.ToString());
					break;
			}
		}

		private static void WriteString(StringBuilder builder, string text)
		{
			builder.Append('"');
			foreach (var character in text)
			{
				if (character == '"' || character == '\\')
					builder.Append('\\').Append(character);
				else if (character < ' ')
					builder.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
				else
					builder.Append(character);
			}

			builder.Append('"');
		}
	}
}
