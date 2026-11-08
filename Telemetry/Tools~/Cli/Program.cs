using System;
using System.Collections.Generic;
using System.Linq;

namespace Dexc.Telemetry.Cli
{
	/// <summary>Reads, takes and sends the files Dexc Telemetry writes. See the CLI section of docs/spec/telemetry.md.</summary>
	internal static class Program
	{
		const string Usage = @"dexc-telemetry <command>

  dump <file|folder> [--open]                 print what the files hold
  send <file|folder> [--endpoint <url>] [--max-bytes <n>] [--no-gzip] [--header <name=value>]... [--open]
                                              send by OTLP/HTTP, by default to http://127.0.0.1:4318, 1048576 bytes a request
  pull --from <folder> --to <folder> [--keep] take the closed files of a folder
  pull --adb <package> [--serial <id>] --to <folder> [--keep]
                                              take the closed files of an Android Player
  captures <folder>                           list the captures
  cut <folder> --capture <n> --to <folder>    write capture n as files of its own

--open also reads the files still being written, up to their last whole frame.";

		static int Main(string[] arguments)
		{
			if (arguments.Length == 0)
			{
				Console.WriteLine(Usage);
				return 2;
			}
			var options = new Options(arguments.Skip(1).ToArray());
			try
			{
				switch (arguments[0])
				{
					case "dump":
						return DumpCommand.Run(options.Path(), options.Flag("--open"));
					case "send":
						return SendCommand.Run(
							options.Path(),
							options.Value("--endpoint") ?? "http://127.0.0.1:4318",
							int.Parse(options.Value("--max-bytes") ?? "1048576"),
							!options.Flag("--no-gzip"),
							options.Flag("--open"),
							options.Values("--header").Select(Header)
						);
					case "pull":
						var to = options.Required("--to");
						return options.Value("--adb") is string package
							? PullCommand.FromAdb(package, options.Value("--serial"), to, options.Flag("--keep"))
							: PullCommand.FromFolder(options.Required("--from"), to, options.Flag("--keep"));
					case "captures":
						return CaptureCommands.List(options.Path());
					case "cut":
						return CaptureCommands.Cut(options.Path(), int.Parse(options.Required("--capture")), options.Required("--to"));
					default:
						Console.Error.WriteLine("unknown command " + arguments[0] + "\n\n" + Usage);
						return 2;
				}
			}
			catch (ArgumentException exception)
			{
				Console.Error.WriteLine(exception.Message + "\n\n" + Usage);
				return 2;
			}
		}

		static KeyValuePair<string, string> Header(string text)
		{
			var equals = text.IndexOf('=');
			if (equals <= 0)
			{
				throw new ArgumentException("A header is name=value: " + text);
			}
			return new KeyValuePair<string, string>(text.Substring(0, equals), text.Substring(equals + 1));
		}

		/// <summary>The arguments after the command: one path, flags, and options with a value.</summary>
		sealed class Options
		{
			readonly List<string> m_Positional = new List<string>();
			readonly List<KeyValuePair<string, string>> m_Named = new List<KeyValuePair<string, string>>();
			readonly HashSet<string> m_Flags = new HashSet<string>();

			internal Options(string[] arguments)
			{
				for (var i = 0; i < arguments.Length; i++)
				{
					var argument = arguments[i];
					if (!argument.StartsWith("--"))
					{
						m_Positional.Add(argument);
					}
					else if (argument == "--open" || argument == "--keep" || argument == "--no-gzip")
					{
						m_Flags.Add(argument);
					}
					else if (i + 1 < arguments.Length)
					{
						m_Named.Add(new KeyValuePair<string, string>(argument, arguments[++i]));
					}
					else
					{
						throw new ArgumentException(argument + " needs a value");
					}
				}
			}

			internal string Path()
			{
				return m_Positional.Count == 1 ? m_Positional[0] : throw new ArgumentException("Give one file or folder.");
			}

			internal bool Flag(string name)
			{
				return m_Flags.Contains(name);
			}

			internal string Value(string name)
			{
				return m_Named.LastOrDefault(pair => pair.Key == name).Value;
			}

			internal IEnumerable<string> Values(string name)
			{
				return m_Named.Where(pair => pair.Key == name).Select(pair => pair.Value);
			}

			internal string Required(string name)
			{
				return Value(name) ?? throw new ArgumentException(name + " is needed");
			}
		}
	}
}
