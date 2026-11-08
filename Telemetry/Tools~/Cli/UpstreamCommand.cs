using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;

namespace Dexc.Telemetry.Cli
{
	/// <summary>
	/// Keeps the package in step with OpenTelemetry, by hand, about once a month: see UPSTREAM.md. <c>check</c> compares
	/// the versions in upstream.json with the newest tags. <c>verify</c> compares the field numbers of OtlpFields.cs with
	/// the .proto files of a tag, and sends frames written by the encoder of the package to a Collector of a tag.
	/// </summary>
	internal static class UpstreamCommand
	{
		static readonly (string Key, string Repository, string Pattern)[] s_Projects =
		{
			("opentelemetry-proto", "opentelemetry-proto", @"^v\d+\.\d+\.\d+$"),
			("opentelemetry-specification", "opentelemetry-specification", @"^v\d+\.\d+\.\d+$"),
			("semantic-conventions", "semantic-conventions", @"^v\d+\.\d+\.\d+$"),
			("opentelemetry-dotnet", "opentelemetry-dotnet", @"^core-\d+\.\d+\.\d+$"),
			("opentelemetry-collector-contrib", "opentelemetry-collector-contrib", @"^v\d+\.\d+\.\d+$"),
		};

		const string CollectorConfig = @"receivers:
  otlp:
    protocols:
      http:
        endpoint: 0.0.0.0:4318
exporters:
  debug:
    verbosity: detailed
service:
  pipelines:
    metrics:
      receivers: [otlp]
      exporters: [debug]
    logs:
      receivers: [otlp]
      exporters: [debug]
    traces:
      receivers: [otlp]
      exporters: [debug]
";

		/// <summary>The folder of upstream.json, Telemetry/Tools~, found from this tool upward.</summary>
		static string Tools()
		{
			for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder != null; folder = folder.Parent)
			{
				if (File.Exists(Path.Combine(folder.FullName, "upstream.json")))
				{
					return folder.FullName;
				}
			}
			throw new ArgumentException("upstream.json was not found above " + AppContext.BaseDirectory);
		}

		static Dictionary<string, string> Pinned()
		{
			return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(Tools(), "upstream.json")));
		}

		internal static int Check()
		{
			var pinned = Pinned();
			var behind = 0;
			foreach (var (key, repository, pattern) in s_Projects)
			{
				var latest = Latest(repository, pattern);
				var mine = pinned[key];
				var newer = latest != null && Normalize(latest) != Normalize(mine);
				behind += newer ? 1 : 0;
				Console.WriteLine(key + ": " + mine + (newer ? " -> " + latest + "  https://github.com/open-telemetry/" + repository + "/releases/tag/" + latest : " (newest)"));
			}
			return behind == 0 ? 0 : 3;
		}

		static string Normalize(string tag)
		{
			return tag.TrimStart('v');
		}

		static string Latest(string repository, string pattern)
		{
			var (code, output) = Run("git", "ls-remote --tags https://github.com/open-telemetry/" + repository + ".git");
			if (code != 0)
			{
				Console.Error.WriteLine("git ls-remote failed for " + repository + ": " + output.Trim());
				return null;
			}
			return output.Split('\n')
				.Select(line => line.Trim())
				.Where(line => line.Contains("refs/tags/") && !line.EndsWith("^{}"))
				.Select(line => line.Substring(line.IndexOf("refs/tags/", StringComparison.Ordinal) + "refs/tags/".Length))
				.Where(tag => Regex.IsMatch(tag, pattern))
				.OrderBy(tag => Version.Parse(Regex.Match(tag, @"\d+\.\d+\.\d+").Value))
				.LastOrDefault();
		}

		internal static int Verify(string protoTag, string collectorTag)
		{
			var pinned = Pinned();
			protoTag = protoTag ?? pinned["opentelemetry-proto"];
			collectorTag = collectorTag ?? pinned["opentelemetry-collector-contrib"];
			var fields = VerifyFields(protoTag);
			var collector = VerifyCollector(collectorTag);
			Console.WriteLine("fields of " + protoTag + ": " + (fields ? "match" : "DIFFER") + "; Collector " + collectorTag + ": " + (collector ? "takes the frames" : "FAILED"));
			return fields && collector ? 0 : 1;
		}

		/// <summary>Each constant of OtlpFields.cs against the field it stands for in the .proto files of the tag.</summary>
		static bool VerifyFields(string tag)
		{
			var folder = Path.Combine(Path.GetTempPath(), "dexc-telemetry-proto-" + tag);
			if (!Directory.Exists(folder))
			{
				var (code, output) = Run("git", "clone -q --depth 1 --branch " + tag + " https://github.com/open-telemetry/opentelemetry-proto.git \"" + folder + "\"");
				if (code != 0)
				{
					Console.Error.WriteLine("could not fetch opentelemetry-proto " + tag + ": " + output.Trim());
					return false;
				}
			}
			var tools = Tools();
			var constants = Regex.Matches(File.ReadAllText(Path.Combine(tools, "..", "Runtime", "OtlpFields.cs")), @"internal const int (\w+) = (\d+);")
				.ToDictionary(match => match.Groups[1].Value, match => int.Parse(match.Groups[2].Value));
			var map = JsonSerializer.Deserialize<Dictionary<string, string[]>>(File.ReadAllText(Path.Combine(tools, "otlp-fields.json")));
			var numbers = new Dictionary<string, Dictionary<string, int>>();
			var good = true;
			foreach (var constant in constants)
			{
				if (!map.TryGetValue(constant.Key, out var places))
				{
					Console.WriteLine("OtlpFields." + constant.Key + " is not in otlp-fields.json");
					good = false;
					continue;
				}
				foreach (var place in places)
				{
					var parts = place.Split(' ');
					if (!numbers.TryGetValue(parts[0], out var file))
					{
						file = Numbers(Path.Combine(folder, "opentelemetry", "proto", parts[0]));
						numbers.Add(parts[0], file);
					}
					if (!file.TryGetValue(parts[1], out var number))
					{
						Console.WriteLine("OtlpFields." + constant.Key + ": " + place + " is not in " + tag);
						good = false;
					}
					else if (number != constant.Value)
					{
						Console.WriteLine("OtlpFields." + constant.Key + " = " + constant.Value + ", but " + place + " = " + number + " in " + tag);
						good = false;
					}
				}
			}
			foreach (var stale in map.Keys.Except(constants.Keys))
			{
				Console.WriteLine("otlp-fields.json has " + stale + ", which OtlpFields.cs no longer has");
				good = false;
			}
			foreach (var copy in Directory.GetFiles(Path.Combine(tools, "proto", "opentelemetry"), "*.proto", SearchOption.AllDirectories))
			{
				var relative = Path.GetRelativePath(Path.Combine(tools, "proto"), copy);
				var fresh = Path.Combine(folder, relative);
				if (!File.Exists(fresh) || File.ReadAllText(fresh) != File.ReadAllText(copy))
				{
					Console.WriteLine("proto/" + relative.Replace('\\', '/') + " differs from " + tag + ": copy it over when moving to it");
				}
			}
			return good;
		}

		/// <summary>The field numbers of a .proto file, as Message.field, the message being the innermost one.</summary>
		static Dictionary<string, int> Numbers(string path)
		{
			var numbers = new Dictionary<string, int>();
			var messages = new Stack<string>();
			var blocks = new Stack<bool>();
			foreach (var raw in File.ReadAllLines(path))
			{
				var line = Regex.Replace(raw, "//.*$", string.Empty).Trim();
				var message = Regex.Match(line, @"^message\s+(\w+)\s*\{");
				if (message.Success)
				{
					messages.Push(message.Groups[1].Value);
					blocks.Push(true);
					continue;
				}
				if (Regex.IsMatch(line, @"^(oneof|enum)\s+\w+\s*\{"))
				{
					blocks.Push(false);
					continue;
				}
				if (line.StartsWith("}"))
				{
					if (blocks.Count > 0 && blocks.Pop())
					{
						messages.Pop();
					}
					continue;
				}
				var field = Regex.Match(line, @"^(?:repeated\s+|optional\s+)?[\w.]+(?:<[^>]*>)?\s+(\w+)\s*=\s*(\d+)\s*[;\[]");
				if (field.Success && messages.Count > 0 && !(blocks.Count > 0 && !blocks.Peek() && IsEnumLine(line)))
				{
					numbers[messages.Peek() + "." + field.Groups[1].Value] = int.Parse(field.Groups[2].Value);
				}
			}
			return numbers;
		}

		/// <summary>An enum value has no type before its name: NAME = 1;</summary>
		static bool IsEnumLine(string line)
		{
			return Regex.IsMatch(line, @"^\w+\s*=");
		}

		/// <summary>Writes the fixtures with the encoder of the package and sends them to a Collector of the tag in Docker.</summary>
		static bool VerifyCollector(string tag)
		{
			var tools = Tools();
			var work = Path.Combine(Path.GetTempPath(), "dexc-telemetry-upstream-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(work);
			var (built, buildOutput) = Run("dotnet", "run -c Release --project \"" + Path.Combine(tools, "Fixtures") + "\" -- \"" + Path.Combine(work, "frames") + "\"");
			if (built != 0)
			{
				Console.Error.WriteLine("the fixtures could not be written: " + buildOutput.Trim());
				return false;
			}
			var config = Path.Combine(work, "config.yaml");
			File.WriteAllText(config, CollectorConfig);
			var name = "dexc-telemetry-upstream";
			Run("docker", "rm -f " + name);
			var (started, startOutput) = Run("docker", "run -d --rm --name " + name + " -p 4318:4318 -v \"" + config + ":/etc/otelcol-contrib/config.yaml\" otel/opentelemetry-collector-contrib:" + Normalize(tag));
			if (started != 0)
			{
				Console.Error.WriteLine("the Collector did not start: " + startOutput.Trim());
				return false;
			}
			try
			{
				var ready = false;
				for (var i = 0; i < 60 && !ready; i++)
				{
					Thread.Sleep(1000);
					ready = Run("docker", "logs " + name).Output.Contains("Everything is ready");
				}
				if (!ready)
				{
					Console.Error.WriteLine("the Collector did not get ready");
					return false;
				}
				var sent = SendCommand.Run(Path.Combine(work, "frames"), "http://127.0.0.1:4318", 1024 * 1024, true, false, Array.Empty<KeyValuePair<string, string>>());
				Thread.Sleep(2000);
				var logs = Run("docker", "logs " + name).Output;
				var spans = Regex.Matches(logs, @"Span #\d+").Count;
				var records = Regex.Matches(logs, @"LogRecord #\d+").Count;
				var metrics = Regex.Matches(logs, @"Metric #\d+").Count;
				Console.WriteLine("the Collector read " + spans + " spans, " + records + " log records, " + metrics + " metrics");
				return sent == 0 && spans == 2 && records == 4 && metrics == 3;
			}
			finally
			{
				Run("docker", "stop " + name);
				Directory.Delete(work, true);
			}
		}

		static (int Code, string Output) Run(string file, string arguments)
		{
			var start = new ProcessStartInfo(file, arguments)
			{
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
			};
			using (var process = Process.Start(start))
			{
				var error = process.StandardError.ReadToEndAsync();
				var output = process.StandardOutput.ReadToEnd();
				process.WaitForExit();
				return (process.ExitCode, output + error.Result);
			}
		}
	}
}
