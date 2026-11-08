using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Dexc.Autopilot.Compiler
{
	/// <summary>
	/// One long-lived compiler for snippets and bots, and the small client that talks to it.
	///
	///   serve [--watch] [--vm ilruntime|pipeline]    run the compiler; --watch loads the bot on every save
	///   eval "snippet" [--vm ...] [--keep]           compile a snippet and run it in the Player
	///   bot [--vm ...] [--load] [--out file]         compile the bot, and load it into the Player
	///   status | stop
	///   ws ...                                       builds side by side; see WorkspaceCommand
	///
	/// A client starts the server if nothing answers.
	/// </summary>
	internal static class Program
	{
		private const string DefaultBot = "Assets/Samples/StealthStageDemo/Bot~/StealthBot";
		private const int DefaultPort = 7891;
		private const int ConnectMilliseconds = 200;

		private static int Main(string[] arguments)
		{
			Console.OutputEncoding = new UTF8Encoding(false);
			if (arguments.Length == 0)
			{
				Console.Error.WriteLine("usage: serve | eval <snippet> | bot | status | stop | ws");
				return 2;
			}

			var options = Options.Parse(arguments);
			var port = Port();
			switch (arguments[0])
			{
				case "serve":
					new Server(Root(), port, options.Value("vm", "ilruntime"), options.Has("watch") ? Path.Combine(Root(), options.Value("dir", DefaultBot)) : null).Run();
					return 0;
				case "eval":
					return Ask(port, Request(
						"op", "eval",
						"snippet", arguments[1],
						"vm", options.Value("vm", "ilruntime"),
						"keep", options.Has("keep") ? "yes" : null,
						"send", options.Has("compile-only") ? null : "yes"
					));
				case "bot":
					return Ask(port, Request(
						"op", "bot",
						"dirs", Path.Combine(Root(), options.Value("dir", DefaultBot)),
						"vm", options.Value("vm", "ilruntime"),
						"load", options.Has("load") ? "yes" : null,
						"out", options.Value("out", null)
					));
				case "status":
				case "stop":
					return Ask(port, Request("op", arguments[0]), arguments[0] == "status");
				case "ws":
					return WorkspaceCommand.Run(arguments, options, port);
				default:
					Console.Error.WriteLine("unknown command: " + arguments[0]);
					return 2;
			}
		}

		private static int Port()
		{
			var text = Environment.GetEnvironmentVariable("DEXC_COMPILER_PORT");
			return string.IsNullOrEmpty(text) ? DefaultPort : int.Parse(text);
		}

		/// <summary>The Unity project this tool sits in.</summary>
		private static string Root()
		{
			var directory = new DirectoryInfo(AppContext.BaseDirectory);
			while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "ProjectSettings")))
				directory = directory.Parent;
			return directory?.FullName ?? throw new InvalidOperationException("Not inside a Unity project.");
		}

		/// <summary>Written by hand so a client call does not pay to load and compile a JSON library.</summary>
		internal static string Request(params string[] pairs)
		{
			var builder = new StringBuilder("{");
			for (var index = 0; index + 1 < pairs.Length; index += 2)
			{
				if (pairs[index + 1] == null)
					continue;
				if (builder.Length > 1)
					builder.Append(',');
				AppendString(builder, pairs[index]);
				builder.Append(':');
				AppendString(builder, pairs[index + 1]);
			}

			return builder.Append('}').ToString();
		}

		private static void AppendString(StringBuilder builder, string text)
		{
			builder.Append('"');
			foreach (var character in text)
			{
				if (character == '"' || character == '\\')
					builder.Append('\\').Append(character);
				else if (character < ' ')
					builder.Append("\\u").Append(((int)character).ToString("x4"));
				else
					builder.Append(character);
			}

			builder.Append('"');
		}

		private static int Ask(int port, string request, bool startServer = true)
		{
			using var client = Connect(port, startServer);
			if (client == null)
			{
				Console.Error.WriteLine("The compiler is not running.");
				return 1;
			}

			var answer = Exchange(client, request);
			Console.WriteLine(answer);
			return answer != null && answer.Contains("\"ok\":true") ? 0 : 1;
		}

		/// <summary>One request to the server, starting it if it is not up.</summary>
		internal static string Exchange(int port, string request)
		{
			using var client = Connect(port, true);
			if (client == null)
				throw new InvalidOperationException("The compiler did not start.");
			return Exchange(client, request);
		}

		private static string Exchange(TcpClient client, string request)
		{
			using var stream = client.GetStream();
			using var writer = new StreamWriter(stream, new UTF8Encoding(false));
			using var reader = new StreamReader(stream, new UTF8Encoding(false));
			writer.WriteLine(request);
			writer.Flush();
			return reader.ReadLine();
		}

		private static TcpClient Connect(int port, bool startServer)
		{
			var client = TryConnect(port);
			if (client != null || !startServer)
				return client;
			Process.Start(new ProcessStartInfo(Environment.ProcessPath, "serve") { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden });
			for (var attempt = 0; attempt < 100 && client == null; attempt++)
			{
				Thread.Sleep(100);
				client = TryConnect(port);
			}

			return client;
		}

		private static TcpClient TryConnect(int port)
		{
			// Windows keeps retrying a closed port for about two seconds, and nothing on loopback takes that long to answer.
			var client = new TcpClient { NoDelay = true };
			try
			{
				if (client.ConnectAsync(IPAddress.Loopback, port).Wait(ConnectMilliseconds))
					return client;
			}
			catch (AggregateException)
			{
			}

			client.Dispose();
			return null;
		}

		internal sealed class Options
		{
			private readonly Dictionary<string, string> m_Values = new Dictionary<string, string>();

			public static Options Parse(string[] arguments)
			{
				var options = new Options();
				for (var index = 1; index < arguments.Length; index++)
				{
					if (!arguments[index].StartsWith("--", StringComparison.Ordinal))
						continue;
					var hasValue = index + 1 < arguments.Length && !arguments[index + 1].StartsWith("--", StringComparison.Ordinal);
					options.m_Values[arguments[index].Substring(2)] = hasValue ? arguments[++index] : string.Empty;
				}

				return options;
			}

			public bool Has(string name) => m_Values.ContainsKey(name);

			public void Set(string name, string value = "") => m_Values[name] = value;

			public string Value(string name, string fallback) => m_Values.TryGetValue(name, out var value) ? value : fallback;
		}
	}
}
