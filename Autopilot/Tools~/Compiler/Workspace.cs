using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Dexc.Autopilot.Compiler
{
	/// <summary>One build a workspace looks at. Where it came from is not the workspace's business: it starts from a Player.</summary>
	internal sealed class WorkspaceBuild
	{
		public const string Managed = "managed";
		public const string Dlls = "dlls";
		public const string Csproj = "csproj";

		public string Player { get; set; }
		public string References { get; set; } = Managed;
		public List<string> Assemblies { get; set; }
		public string Project { get; set; }
		public string Editor { get; set; }

		/// <summary>The commit the build was made from, if the one who added it said. It goes into the telemetry of the build.</summary>
		public string Commit { get; set; }
	}

	internal sealed class WorkspaceFile
	{
		public List<string> Usings { get; set; } = new List<string>();
		public List<string> Publicize { get; set; } = new List<string>();
		public SortedDictionary<string, WorkspaceBuild> Builds { get; set; } = new SortedDictionary<string, WorkspaceBuild>(StringComparer.OrdinalIgnoreCase);

		/// <summary>The performance the game promises itself. Going over one is a defect, found by the dashboard.</summary>
		public List<WorkspaceBudget> Budgets { get; set; } = new List<WorkspaceBudget>();
	}

	/// <summary>
	/// A limit on a distribution: a statistic of a histogram, over a window, for the series whose attributes include
	/// <see cref="Where"/>. Such as the p95 of unity.frame.time at stage 1 under 33.4 ms, or loads of a level under 5 s.
	/// </summary>
	internal sealed class WorkspaceBudget
	{
		public string Metric { get; set; }

		/// <summary>mean, p50, p95, p99, max, slow or frozen: the last two are shares of frames, from 0 to 1.</summary>
		public string Stat { get; set; } = "p95";

		public double Max { get; set; }

		public Dictionary<string, string> Where { get; set; } = new Dictionary<string, string>();

		/// <summary>The values a series needs in the window before it is judged: a few frames say nothing.</summary>
		public int Minimum { get; set; } = 100;

		public double WindowSeconds { get; set; } = 300;
	}

	/// <summary>
	/// Builds looked at side by side. A folder holds workspace.json, the bot all builds share in Common, and in Builds/name
	/// what one build needs of its own. A file there replaces the one at the same path in Common.
	/// </summary>
	internal sealed class Workspace
	{
		public const string FileName = "workspace.json";

		private static readonly JsonSerializerOptions s_Json = new JsonSerializerOptions
		{
			WriteIndented = true,
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
			DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		};

		private Workspace(string directory, WorkspaceFile file)
		{
			Directory = directory;
			File = file;
		}

		public string Directory { get; }
		public WorkspaceFile File { get; }
		public string Common => Path.Combine(Directory, "Common");

		public static Workspace Create(string directory, IEnumerable<string> usings, IEnumerable<string> publicize)
		{
			var full = Path.GetFullPath(directory);
			if (System.IO.File.Exists(Path.Combine(full, FileName)))
				throw new InvalidOperationException("A workspace is already there: " + full);
			var workspace = new Workspace(full, new WorkspaceFile { Usings = usings.ToList(), Publicize = publicize.ToList() });
			System.IO.Directory.CreateDirectory(workspace.Common);
			workspace.Save();
			return workspace;
		}

		/// <summary>DEXC_WORKSPACE, or the first workspace from here upwards.</summary>
		public static Workspace Find()
		{
			var named = Environment.GetEnvironmentVariable("DEXC_WORKSPACE");
			var directory = new DirectoryInfo(string.IsNullOrEmpty(named) ? System.IO.Directory.GetCurrentDirectory() : named);
			while (directory != null && !System.IO.File.Exists(Path.Combine(directory.FullName, FileName)))
				directory = directory.Parent;
			if (directory == null)
				throw new InvalidOperationException("No " + FileName + " here or above. Set DEXC_WORKSPACE, or run ws init.");
			return Open(directory.FullName);
		}

		/// <summary>The workspace in this folder, read as it is now.</summary>
		public static Workspace Open(string directory)
		{
			var text = System.IO.File.ReadAllText(Path.Combine(directory, FileName));
			return new Workspace(directory, JsonSerializer.Deserialize<WorkspaceFile>(text, s_Json));
		}

		public void Save() => System.IO.File.WriteAllText(Path.Combine(Directory, FileName), JsonSerializer.Serialize(File, s_Json));

		public string OwnBot(string build) => Path.Combine(Directory, "Builds", build);

		public string State(string build) => Path.Combine(Directory, ".state", build);

		/// <summary>What the dashboard keeps: where it listens, and the journal of the questions and the instructions.</summary>
		public string Dashboard => Path.Combine(Directory, ".state", ".dashboard");

		/// <summary>Where a running dashboard says how to reach it, as {"url", "token", "pid"}.</summary>
		public string DashboardEndpoint => Path.Combine(Dashboard, "endpoint.json");

		/// <summary>
		/// How to reach the running dashboard. A dashboard that was killed leaves its file behind: the process it names is
		/// gone, so the file is taken away and the dashboard said not to run.
		/// </summary>
		/// <exception cref="InvalidOperationException">No dashboard runs for this workspace.</exception>
		public JsonNode RunningDashboard()
		{
			const string NotRunning = "The dashboard is not running. Start Dexc.Autopilot.Dashboard in the workspace.";
			if (!System.IO.File.Exists(DashboardEndpoint))
				throw new InvalidOperationException(NotRunning);
			var endpoint = JsonNode.Parse(System.IO.File.ReadAllText(DashboardEndpoint));
			var pid = endpoint?["pid"]?.GetValue<int>() ?? 0;
			if (!IsAlive(pid))
			{
				System.IO.File.Delete(DashboardEndpoint);
				throw new InvalidOperationException(NotRunning + " The last one ended without cleaning up.");
			}

			return endpoint;
		}

		private static bool IsAlive(int pid)
		{
			try
			{
				using var process = Process.GetProcessById(pid);
				return !process.HasExited;
			}
			catch (ArgumentException)
			{
				return false;
			}
		}

		/// <summary>Where the telemetry files of a build are gathered: Telemetry/name.</summary>
		public string Telemetry(string build) => Path.Combine(Directory, "Telemetry", build);
	}

	/// <summary>Makes what a bot compiles against. Later sources replace earlier ones of the same file name.</summary>
	internal static class WorkspaceReferences
	{
		private const string BackupFolder = "_BackUpThisFolder_ButDontShipItWithYourGame";

		public static void Generate(Workspace workspace, string name)
		{
			var build = workspace.File.Builds[name];
			var state = workspace.State(name);
			var sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			var managed = FindManaged(build.Player);
			if (build.References == WorkspaceBuild.Dlls && build.Editor == null)
				throw new InvalidOperationException("--dlls needs --editor: assemblies from before a build refer to a netstandard the Player does not have.");
			if (managed != null)
			{
				AddFolder(sources, managed);
				if (build.Editor != null)
					sources["netstandard.dll"] = Path.Combine(ClassLibrary(build.Editor), "Facades", "netstandard.dll");
			}
			else if (build.References != WorkspaceBuild.Managed && build.Editor != null)
				AddEditor(sources, build.Editor);
			else
				throw new InvalidOperationException(
					"No Managed folder beside " + build.Player + ". Keep *" + BackupFolder + "/Managed with the Player, or give --dlls or --csproj with --editor."
				);

			foreach (var folder in build.Assemblies ?? new List<string>())
				AddFolder(sources, folder);
			if (build.Project != null)
				AddProject(sources, build.Project, Path.Combine(state, "csproj"));

			var references = Path.Combine(state, "refs");
			if (Directory.Exists(references))
				Directory.Delete(references, true);
			Directory.CreateDirectory(references);
			foreach (var source in sources.Values)
				File.Copy(source, Path.Combine(references, Path.GetFileName(source)));
			Publicize(workspace.File.Publicize, sources, references);

			// Last, and only now: its stamp is what tells the compiler the whole set is new.
			var lines = Directory.GetFiles(references, "*.dll").OrderBy(path => path, StringComparer.Ordinal).Select(path => "-r:\"" + path + "\"");
			File.WriteAllLines(Path.Combine(state, "refs.rsp"), lines);
		}

		/// <summary>IL2CPP keeps the stripped assemblies in its backup folder, a Mono Player beside its data.</summary>
		private static string FindManaged(string player)
		{
			var folder = Path.GetDirectoryName(player);
			var name = Path.GetFileNameWithoutExtension(player);
			foreach (var candidate in new[] { Path.Combine(folder, name + BackupFolder, "Managed"), Path.Combine(folder, name + "_Data", "Managed") })
			{
				if (Directory.Exists(candidate) && Directory.GetFiles(candidate, "*.dll").Length > 0)
					return candidate;
			}

			return null;
		}

		private static void AddFolder(Dictionary<string, string> sources, string folder)
		{
			if (!Directory.Exists(folder))
				throw new InvalidOperationException("No such folder: " + folder);
			foreach (var path in Directory.GetFiles(folder, "*.dll"))
				sources[Path.GetFileName(path)] = path;
		}

		/// <summary>The engine and class library, for assemblies from before a build that left none of its own.</summary>
		private static void AddEditor(Dictionary<string, string> sources, string editor)
		{
			AddFolder(sources, Path.Combine(editor, "Data", "PlaybackEngines", "windowsstandalonesupport", "Variations", "il2cpp", "Managed"));
			foreach (var path in new[] { "mscorlib.dll", "System.dll", "System.Core.dll", Path.Combine("Facades", "netstandard.dll") })
				sources[Path.GetFileName(path)] = Path.Combine(ClassLibrary(editor), path);
		}

		private static string ClassLibrary(string editor) => Path.Combine(editor, "Data", "MonoBleedingEdge", "lib", "mono", "unityaot-win32");

		private static void AddProject(Dictionary<string, string> sources, string project, string output)
		{
			var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };
			foreach (var argument in new[] { "build", project, "-c", "Debug", "-o", output, "-nologo", "-v", "q" })
				start.ArgumentList.Add(argument);
			using (var process = Process.Start(start))
			{
				var errors = process.StandardError.ReadToEndAsync();
				var text = process.StandardOutput.ReadToEnd();
				process.WaitForExit();
				if (process.ExitCode != 0)
					throw new InvalidOperationException("dotnet build failed: " + project + Environment.NewLine + text + errors.Result);
			}

			var directory = Path.GetDirectoryName(project);
			foreach (var hint in XDocument.Load(project).Descendants().Where(element => element.Name.LocalName == "HintPath"))
			{
				var path = Path.GetFullPath(Path.Combine(directory, hint.Value.Trim()));
				if (File.Exists(path) && path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
					sources[Path.GetFileName(path)] = path;
			}

			AddFolder(sources, output);
		}

		private static void Publicize(IEnumerable<string> assemblies, Dictionary<string, string> sources, string references)
		{
			var tool = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Publicize", "bin", "Release", "net10.0", "Publicize.exe"));
			foreach (var assembly in assemblies)
			{
				if (!sources.TryGetValue(assembly + ".dll", out var source))
					throw new InvalidOperationException("Not among the references: " + assembly);
				var start = new ProcessStartInfo(tool) { RedirectStandardError = true, RedirectStandardOutput = true };
				start.ArgumentList.Add(references);
				start.ArgumentList.Add(source);
				using var process = Process.Start(start);
				var errors = process.StandardError.ReadToEndAsync();
				process.StandardOutput.ReadToEnd();
				process.WaitForExit();
				if (process.ExitCode != 0)
					throw new InvalidOperationException("Publicize failed: " + source + Environment.NewLine + errors.Result);
			}
		}
	}

	/// <summary>The Players of a workspace. Each runs on a port and token of its own, kept in its state folder.</summary>
	internal sealed class WorkspacePlayer
	{
		private const int StartSeconds = 30;
		private const int CloseSeconds = 10;

		private readonly Workspace m_Workspace;

		public WorkspacePlayer(Workspace workspace, string name)
		{
			m_Workspace = workspace;
			Name = name;
		}

		public string Name { get; }
		private string State => m_Workspace.State(Name);
		public string ResponseFile => Path.Combine(State, "refs.rsp");
		public string TokenFile => Path.Combine(State, "token");
		private string PortFile => Path.Combine(State, "port");
		private string ProcessFile => Path.Combine(State, "player.pid");
		public string Port => File.Exists(PortFile) ? File.ReadAllText(PortFile).Trim() : null;

		/// <summary>The folder the Player writes its defects in, its own as the telemetry is.</summary>
		public string QAFolder => Path.Combine(State, "qa");

		/// <summary>The folder the Player writes its telemetry in, its own even when builds share a product name.</summary>
		public string TelemetryFolder => Path.Combine(State, "telemetry");

		public bool IsRunning => FindProcess() != null;

		public JsonObject Status()
		{
			if (!IsRunning)
				return null;
			return Send("status", null);
		}

		/// <summary>One request to the running Player.</summary>
		public JsonObject Send(string operation, byte[] payload) => new PlayerLink(TokenFile, int.Parse(Port)).Send(operation, payload);

		/// <param name="seed">Put into the telemetry of the run as dexc.seed, for the bot or the game to read the same way.</param>
		public void Start(bool headless, string seed)
		{
			if (IsRunning)
				return;
			Directory.CreateDirectory(State);
			var port = FreePort();
			var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
			File.WriteAllText(TokenFile, token);
			File.WriteAllText(PortFile, port.ToString());
			// Through the shell, so the Player inherits no handles: a caller reading this output would wait for the Player to exit.
			var start = new ProcessStartInfo(m_Workspace.File.Builds[Name].Player) { UseShellExecute = true };
			var display = headless ? new[] { "-batchmode" } : new[] { "-screen-fullscreen", "0", "-screen-width", "1024", "-screen-height", "640" };
			foreach (var argument in display.Concat(new[] { "-logFile", Path.Combine(State, "player.log"), "-dexc-autopilot-token", token, "-dexc-autopilot-port", port.ToString() }))
				start.ArgumentList.Add(argument);
			// Read by Dexc Telemetry when the game or an eval starts it. A Player without it ignores them.
			foreach (var argument in TelemetryArguments(seed))
				start.ArgumentList.Add(argument);
			// Dexc QA starts on its own with it, so that defects are found from the first frame.
			start.ArgumentList.Add("-dexc-qa-directory");
			start.ArgumentList.Add(QAFolder);
			using (var process = Process.Start(start))
				File.WriteAllText(ProcessFile, process.Id.ToString());
			var clock = Stopwatch.StartNew();
			while (true)
			{
				if (!IsRunning)
					throw new InvalidOperationException(Name + " exited. Log: " + Path.Combine(State, "player.log"));
				try
				{
					Status();
					return;
				}
				catch (Exception exception) when (exception is IOException || exception is SocketException)
				{
					if (clock.Elapsed.TotalSeconds > StartSeconds)
						throw new InvalidOperationException(Name + " did not answer. Log: " + Path.Combine(State, "player.log"));
					Thread.Sleep(200);
				}
			}
		}

		public void Stop()
		{
			using var process = FindProcess();
			if (process != null)
			{
				// A headless Player has no window to close.
				if (!process.CloseMainWindow() || !process.WaitForExit(CloseSeconds * 1000))
				{
					process.Kill();
					process.WaitForExit();
				}
			}

			File.Delete(ProcessFile);
			File.Delete(TokenFile);
			File.Delete(PortFile);
		}

		private IEnumerable<string> TelemetryArguments(string seed)
		{
			yield return "-dexc-telemetry-directory";
			yield return TelemetryFolder;
			var resource = new List<string> { "dexc.build=" + Name };
			var commit = m_Workspace.File.Builds[Name].Commit;
			if (!string.IsNullOrEmpty(commit))
				resource.Add("vcs.ref.head.revision=" + commit);
			if (!string.IsNullOrEmpty(seed))
				resource.Add("dexc.seed=" + seed);
			foreach (var pair in resource)
			{
				yield return "-dexc-telemetry-resource";
				yield return pair;
			}
		}

		/// <summary>
		/// Moves the closed telemetry files of this Player into the workspace: the .otlp files and the Profiler files of
		/// captures, never one still being written. Seal the Player first to have everything up to now.
		/// </summary>
		public int TakeTelemetry(string target)
		{
			if (!Directory.Exists(TelemetryFolder))
				return 0;
			Directory.CreateDirectory(target);
			var taken = 0;
			foreach (var path in Directory.GetFiles(TelemetryFolder))
			{
				var name = Path.GetFileName(path);
				var closed = name.EndsWith(".otlp", StringComparison.OrdinalIgnoreCase)
					|| (name.StartsWith("capture-", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".raw", StringComparison.OrdinalIgnoreCase));
				var destination = Path.Combine(target, name);
				if (!closed || File.Exists(destination))
					continue;
				File.Move(path, destination);
				taken++;
			}

			return taken;
		}

		/// <summary>By id and by name: Windows hands the id of a Player that has gone to the next process.</summary>
		private Process FindProcess()
		{
			if (!File.Exists(ProcessFile))
				return null;
			try
			{
				var process = Process.GetProcessById(int.Parse(File.ReadAllText(ProcessFile).Trim()));
				if (!process.HasExited && process.ProcessName == Path.GetFileNameWithoutExtension(m_Workspace.File.Builds[Name].Player))
					return process;
				process.Dispose();
				return null;
			}
			catch (ArgumentException)
			{
				return null;
			}
		}

		private static int FreePort()
		{
			var listener = new TcpListener(IPAddress.Loopback, 0);
			listener.Start();
			var port = ((IPEndPoint)listener.LocalEndpoint).Port;
			listener.Stop();
			return port;
		}
	}

	/// <summary>
	///   ws init dir [--using a,b] [--publicize a,b]
	///   ws add name player [--dlls a,b] [--csproj file] [--editor dir] [--commit sha]
	///   ws remove name
	///   ws refs | start [--headless] [--seed n] | stop | status | watch      [--build a,b]
	///   ws telemetry start [--watch a,b] [--upload] | capture frames [--profiler] | pull | stop   [--build a,b]
	///   ws bot [--load] [--vm ...]                                 [--build a,b]
	///   ws eval "snippet" [--vm ...] [--keep]                      [--build a,b]
	///   ws agent wait [--timeout 90] | answer build id answer | say text
	///   ws qa pull | list [--state open] | show id | set id state [--note text]
	///   ws qa verify id --build name [--seconds 300] [--seed n] [--headless] [--keep]
	/// Every build answers on a line of its own, whatever the others did.
	/// </summary>
	internal static class WorkspaceCommand
	{
		private const int WatchDelayMilliseconds = 150;
		private const string BotName = "Bot";

		public static int Run(string[] arguments, Program.Options options, int compilerPort)
		{
			try
			{
				var verb = arguments.Length > 1 ? arguments[1] : string.Empty;
				switch (verb)
				{
					case "init":
						Workspace.Create(arguments[2], List(options, "using"), List(options, "publicize"));
						return 0;
					case "add":
						return Add(arguments, options);
					case "remove":
						return Remove(arguments[2]);
					case "agent":
						return Agent(Workspace.Find(), arguments, options);
				}

				var workspace = Workspace.Find();
				var players = Selected(workspace, options);
				switch (verb)
				{
					case "refs":
						foreach (var player in players)
							WorkspaceReferences.Generate(workspace, player.Name);
						return 0;
					case "start":
						Warm(workspace, players, compilerPort);
						return ForEach(players, player =>
						{
							player.Start(options.Has("headless"), options.Value("seed", null));
							return "port " + player.Port;
						});
					case "telemetry":
						return Telemetry(workspace, players, arguments, options, compilerPort);
					case "qa":
						return QA(workspace, arguments, options, compilerPort);
					case "stop":
						return ForEach(players, player =>
						{
							player.Stop();
							return "stopped";
						});
					case "status":
						return ForEach(players, player => player.Status()?.ToJsonString() ?? "stopped");
					case "bot":
						return ForEach(players, player => Bot(workspace, player, options, compilerPort));
					case "eval":
						return ForEach(Running(players), player => Eval(workspace, player, arguments[2], options, compilerPort));
					case "watch":
						return Watch(workspace, players, options, compilerPort);
					default:
						Console.Error.WriteLine("usage: ws init | add | remove | refs | start | stop | status | bot | eval | telemetry | qa | watch | agent");
						return 2;
				}
			}
			catch (InvalidOperationException exception)
			{
				Console.Error.WriteLine(exception.Message);
				return 1;
			}
		}

		/// <summary>
		/// The defects the Players found, through Dexc QA. pull takes the whole reports into QA/reports and brings the
		/// defects in QA/defects.json up to date; list, show and set read and change them. No eval: the Players write their
		/// reports and counts to their own folders, which ws start gave them.
		/// </summary>
		private static int QA(Workspace workspace, string[] arguments, Program.Options options, int compilerPort)
		{
			var verb = arguments.Length > 2 ? arguments[2] : string.Empty;
			switch (verb)
			{
				case "pull":
					var changes = QAStore.Gather(workspace);
					Console.WriteLine("took " + changes.Reports + " reports into " + Path.Combine(QAStore.Folder(workspace), "reports"));
					foreach (var defect in changes.Created)
						Console.WriteLine("new    " + Line(defect));
					foreach (var defect in changes.Recurred)
						Console.WriteLine("again  " + Line(defect));
					return 0;
				case "list":
					var state = options.Value("state", null);
					foreach (var defect in QAStore.Read(workspace).Defects.Where(defect => state == null || defect.State == state))
						Console.WriteLine(Line(defect));
					return 0;
				case "show":
					if (arguments.Length < 4)
						throw new InvalidOperationException("ws qa show <id>");
					var found = QAStore.Read(workspace).Defects.FirstOrDefault(defect => string.Equals(defect.Id, arguments[3], StringComparison.OrdinalIgnoreCase))
						?? throw new InvalidOperationException("No such defect: " + arguments[3]);
					var json = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
					var shown = JsonSerializer.SerializeToNode(found, json).AsObject();
					shown["reportFolders"] = new JsonArray(found.Reports.Select(report => (JsonNode)Path.Combine(QAStore.Folder(workspace), "reports", report.Replace('/', Path.DirectorySeparatorChar))).ToArray());
					Console.WriteLine(shown.ToJsonString(json));
					return 0;
				case "set":
					if (arguments.Length < 5)
						throw new InvalidOperationException("ws qa set <id> <open|fixing|fixed|wontfix> [--note text]");
					QAStore.SetState(workspace, arguments[3], arguments[4], "agent", options.Value("note", null));
					return 0;
				case "verify":
					return Verify(workspace, arguments, options, compilerPort);
				default:
					throw new InvalidOperationException("ws qa pull | list [--state open] | show <id> | set <id> <state> [--note text] | verify <id> --build <name>");
			}
		}

		/// <summary>Seconds between gatherings while a verification runs.</summary>
		private const int VerifyGatherSeconds = 5;

		/// <summary>
		/// Runs a build again on the seed a report of the defect recorded, with the bot, for a while, and says whether the
		/// defect came back. It does not say the defect is fixed: a run that did not hit it may not have gone where it
		/// happens. What ran, and for how long, goes into the notes of the defect.
		/// </summary>
		private static int Verify(Workspace workspace, string[] arguments, Program.Options options, int compilerPort)
		{
			if (arguments.Length < 4 || !options.Has("build"))
				throw new InvalidOperationException("ws qa verify <id> --build <name> [--seconds 300] [--seed n] [--headless] [--keep]");
			var id = arguments[3];
			var build = options.Value("build", null);
			if (!workspace.File.Builds.ContainsKey(build))
				throw new InvalidOperationException("No such build: " + build);
			var defect = QAStore.Read(workspace).Defects.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase))
				?? throw new InvalidOperationException("No such defect: " + id);
			var seed = options.Value("seed", null) ?? QAStore.SeedOf(workspace, defect);
			var seconds = double.Parse(options.Value("seconds", "300"), System.Globalization.CultureInfo.InvariantCulture);
			var player = new WorkspacePlayer(workspace, build);
			var players = new List<WorkspacePlayer> { player };
			player.Stop();
			Warm(workspace, players, compilerPort);
			var started = QAStore.Now();
			player.Start(options.Has("headless"), seed);
			var load = new Program.Options();
			load.Set("load");
			load.Set("vm", options.Value("vm", "ilruntime"));
			Console.WriteLine(build + "  " + Bot(workspace, player, load, compilerPort));
			var clock = Stopwatch.StartNew();
			var occurred = false;
			while (clock.Elapsed.TotalSeconds < seconds && player.IsRunning && !occurred)
			{
				Thread.Sleep(TimeSpan.FromSeconds(Math.Min(VerifyGatherSeconds, Math.Max(0.1, seconds - clock.Elapsed.TotalSeconds))));
				QAStore.Gather(workspace);
				occurred = QAStore.OccurredSince(QAStore.Read(workspace), id, build, started);
			}

			var exited = !player.IsRunning;
			QAStore.Gather(workspace);
			occurred = QAStore.OccurredSince(QAStore.Read(workspace), id, build, started);
			var result = occurred ? "occurred again" : exited ? "did not occur, but the Player exited" : "did not occur";
			var note = "verify on " + build + " with seed " + (seed ?? "none") + " for " + clock.Elapsed.TotalSeconds.ToString("0") + " s: " + result;
			QAStore.AddNote(workspace, id, "agent", note);
			if (!options.Has("keep"))
				player.Stop();
			Console.WriteLine(note);
			return occurred || exited ? 1 : 0;
		}

		private static string Line(QADefect defect) =>
			defect.Id + "  " + defect.State.PadRight(7) + "  " + defect.Count.ToString().PadLeft(5) + "x  " + string.Join(",", defect.Builds.Keys).PadRight(12) + "  " + defect.Title + "  [" + defect.Signature + "]";

		/// <summary>Seconds between the metrics a Player writes and sends with ws telemetry start --upload.</summary>
		private const int UploadSeconds = 5;

		/// <summary>Exit code of ws agent wait when nothing came in time.</summary>
		public const int NothingCame = 3;

		/// <summary>
		/// The agent's side of the dashboard. wait takes the next question or instruction for the agent, as one line of JSON,
		/// and exits with 3 when nothing came; run it again to go on waiting. answer answers a question of a bot as the
		/// agent, and say tells the people what the agent is doing.
		/// </summary>
		private static int Agent(Workspace workspace, string[] arguments, Program.Options options)
		{
			var endpoint = workspace.RunningDashboard();
			var verb = arguments.Length > 2 ? arguments[2] : string.Empty;
			var seconds = double.Parse(options.Value("timeout", "90"), System.Globalization.CultureInfo.InvariantCulture);
			using var client = new System.Net.Http.HttpClient { BaseAddress = new Uri(endpoint["url"].GetValue<string>()), Timeout = TimeSpan.FromSeconds(seconds + 30) };
			client.DefaultRequestHeaders.Add("X-Dexc-Token", endpoint["token"].GetValue<string>());
			System.Net.Http.HttpResponseMessage Post(string path, JsonObject body) =>
				client.PostAsync(path, new System.Net.Http.StringContent(body?.ToJsonString() ?? "{}", System.Text.Encoding.UTF8, "application/json")).GetAwaiter().GetResult();
			string Read(System.Net.Http.HttpResponseMessage response) => response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
			try
			{
				switch (verb)
				{
					case "wait":
						var next = Post("api/agent/next?timeout=" + seconds.ToString(System.Globalization.CultureInfo.InvariantCulture), null);
						if (next.StatusCode == HttpStatusCode.NoContent)
						{
							Console.Error.WriteLine("Nothing for the agent in " + seconds + " seconds.");
							return NothingCame;
						}

						Console.WriteLine(Read(next));
						return next.IsSuccessStatusCode ? 0 : 1;
					case "answer":
						if (arguments.Length < 6)
							throw new InvalidOperationException("ws agent answer <build> <id> <answer>");
						var answered = Post("api/decisions/answer", new JsonObject { ["build"] = arguments[3], ["id"] = arguments[4], ["answer"] = arguments[5], ["by"] = "agent" });
						Console.WriteLine(Read(answered));
						return answered.IsSuccessStatusCode ? 0 : 1;
					case "say":
						if (arguments.Length < 4)
							throw new InvalidOperationException("ws agent say <text>");
						var said = Post("api/agent/say", new JsonObject { ["text"] = arguments[3] });
						return said.IsSuccessStatusCode ? 0 : 1;
					default:
						throw new InvalidOperationException("ws agent wait [--timeout 90] | answer <build> <id> <answer> | say <text>");
				}
			}
			catch (System.Net.Http.HttpRequestException exception)
			{
				throw new InvalidOperationException("The dashboard did not answer: " + exception.Message);
			}
		}

		private static IEnumerable<string> List(Program.Options options, string name)
		{
			var value = options.Value(name, string.Empty);
			return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		}

		private static int Add(string[] arguments, Program.Options options)
		{
			var workspace = Workspace.Find();
			var name = arguments[2];
			var build = new WorkspaceBuild { Player = FindExecutable(Path.GetFullPath(arguments[3])) };
			var folders = List(options, "dlls").Select(Path.GetFullPath).ToList();
			if (folders.Count > 0)
			{
				build.References = WorkspaceBuild.Dlls;
				build.Assemblies = folders;
			}

			if (options.Has("csproj"))
			{
				build.References = WorkspaceBuild.Csproj;
				build.Project = Path.GetFullPath(options.Value("csproj", null));
			}

			if (options.Has("editor"))
				build.Editor = Path.GetFullPath(options.Value("editor", null));
			if (options.Has("commit"))
				build.Commit = options.Value("commit", null);
			workspace.File.Builds[name] = build;
			Directory.CreateDirectory(workspace.OwnBot(name));
			WorkspaceReferences.Generate(workspace, name);
			workspace.Save();
			return 0;
		}

		private static int Remove(string name)
		{
			var workspace = Workspace.Find();
			new WorkspacePlayer(workspace, name).Stop();
			workspace.File.Builds.Remove(name);
			workspace.Save();
			return 0;
		}

		private static string FindExecutable(string path)
		{
			if (File.Exists(path))
				return path;
			if (!Directory.Exists(path))
				throw new InvalidOperationException("No such Player: " + path);
			var players = Directory.GetFiles(path, "*.exe").Where(file => !Path.GetFileName(file).StartsWith("UnityCrashHandler", StringComparison.Ordinal)).ToList();
			if (players.Count != 1)
				throw new InvalidOperationException("Name the Player's executable: " + path);
			return players[0];
		}

		private static List<WorkspacePlayer> Selected(Workspace workspace, Program.Options options)
		{
			var names = List(options, "build").ToList();
			foreach (var name in names)
			{
				if (!workspace.File.Builds.ContainsKey(name))
					throw new InvalidOperationException("No such build: " + name);
			}

			var chosen = names.Count > 0 ? names : workspace.File.Builds.Keys.ToList();
			return chosen.Select(name => new WorkspacePlayer(workspace, name)).ToList();
		}

		private static List<WorkspacePlayer> Running(List<WorkspacePlayer> players)
		{
			var running = players.Where(player => player.IsRunning).ToList();
			if (running.Count == 0)
				throw new InvalidOperationException("No Player is running. Run ws start.");
			return running;
		}

		/// <summary>All at once, and the lines in build order. One that fails says so on its line and leaves the rest alone.</summary>
		private static int ForEach(List<WorkspacePlayer> players, Func<WorkspacePlayer, string> action)
		{
			var failed = false;
			var tasks = players.Select(player => Task.Run(() =>
			{
				try
				{
					return action(player);
				}
				catch (Exception exception)
				{
					failed = true;
					return "failed: " + exception.Message;
				}
			})).ToList();
			var width = players.Count == 0 ? 0 : players.Max(player => player.Name.Length);
			for (var index = 0; index < players.Count; index++)
			{
				var line = tasks[index].Result;
				failed |= line != null && line.Contains("\"ok\":false");
				Console.WriteLine(players[index].Name.PadRight(width) + "  " + line);
			}

			return failed ? 1 : 0;
		}

		/// <summary>
		/// Loads each build's references into the compiler. Before the Players, not after: a compile that first has to read
		/// them takes a minute with the Players running flat out beside it.
		/// </summary>
		private static void Warm(Workspace workspace, List<WorkspacePlayer> players, int compilerPort)
		{
			foreach (var player in players.Where(player => !player.IsRunning))
				Program.Exchange(compilerPort, Program.Request("op", "eval", "snippet", "0", "rsp", player.ResponseFile, "usings", string.Join(";", workspace.File.Usings)));
		}

		private static string Eval(Workspace workspace, WorkspacePlayer player, string snippet, Program.Options options, int compilerPort) =>
			Program.Exchange(compilerPort, Program.Request(
				"op", "eval",
				"snippet", snippet,
				"vm", options.Value("vm", "ilruntime"),
				"keep", options.Has("keep") ? "yes" : null,
				"send", "yes",
				"rsp", player.ResponseFile,
				"usings", string.Join(";", workspace.File.Usings),
				"port", player.Port,
				"token", player.TokenFile
			));

		/// <summary>
		/// Dexc Telemetry in every build, through eval, so that the game needs no code for it: start it, capture frames, and
		/// gather the files into Telemetry/name. Each Player writes in its own folder, which ws start gave it.
		/// </summary>
		private static int Telemetry(Workspace workspace, List<WorkspacePlayer> players, string[] arguments, Program.Options options, int compilerPort)
		{
			const string Api = "Dexc.Telemetry.DexcTelemetry";
			var verb = arguments.Length > 2 ? arguments[2] : string.Empty;
			switch (verb)
			{
				case "start":
					var watch = string.Concat(List(options, "watch").Select(pattern => Api + ".Watch(\"" + pattern.Replace("\"", string.Empty) + "\");"));
					var start = "if (!" + Api + ".IsRunning) " + Api + ".Start(); ";
					var upload = string.Empty;
					if (options.Has("upload"))
					{
						// To the dashboard, every few seconds, for it to draw as the Players go. The logs too: they carry the hitches.
						var url = workspace.RunningDashboard()["url"].GetValue<string>().Replace("\"", string.Empty);
						start = "if (!" + Api + ".IsRunning) " + Api + ".Start(new Dexc.Telemetry.TelemetryOptions { ExportIntervalSeconds = " + UploadSeconds + ", PositionSeconds = 1 }); ";
						upload = "if (!" + Api + ".IsUploading) " + Api + ".StartUpload(new Dexc.Telemetry.TelemetryUploadOptions { Endpoint = \"" + url + "\", IntervalSeconds = " + UploadSeconds + ", Logs = true }); ";
					}

					return ForEach(Running(players), player => Eval(workspace, player, start + watch + upload + "return " + Api + ".IsRunning;", options, compilerPort));
				case "capture":
					if (arguments.Length < 4 || !int.TryParse(arguments[3], out var frames) || frames <= 0)
						throw new InvalidOperationException("ws telemetry capture <frames>");
					var profiler = options.Has("profiler") ? "true" : "false";
					return ForEach(Running(players), player => Eval(workspace, player, "return " + Api + ".Capture(" + frames + ", Dexc.Telemetry.TelemetryDetail.Detailed, " + profiler + ");", options, compilerPort));
				case "pull":
					return ForEach(players, player => Gather(workspace, player, Api + ".Seal(); return true;", options, compilerPort));
				case "stop":
					return ForEach(players, player => Gather(workspace, player, Api + ".Stop(); return true;", options, compilerPort));
				default:
					throw new InvalidOperationException("ws telemetry start | capture <frames> | pull | stop");
			}
		}

		/// <summary>Has a running Player close its files, then takes them. A Player that stopped has its closed files taken as they are.</summary>
		private static string Gather(Workspace workspace, WorkspacePlayer player, string snippet, Program.Options options, int compilerPort)
		{
			var answer = player.IsRunning ? Eval(workspace, player, snippet, options, compilerPort) : "stopped";
			var taken = player.TakeTelemetry(workspace.Telemetry(player.Name));
			return "took " + taken + " files into " + workspace.Telemetry(player.Name) + " (" + answer + ")";
		}

		private static string Bot(Workspace workspace, WorkspacePlayer player, Program.Options options, int compilerPort)
		{
			var load = options.Has("load");
			if (load && !player.IsRunning)
				return "stopped";
			return Program.Exchange(compilerPort, Program.Request(
				"op", "bot",
				"dirs", workspace.Common + ";" + workspace.OwnBot(player.Name),
				"name", BotName,
				"vm", options.Value("vm", "ilruntime"),
				"load", load ? "yes" : null,
				"out", load ? null : Path.Combine(workspace.State(player.Name), BotName + ".dll"),
				"rsp", player.ResponseFile,
				"port", player.Port,
				"token", player.TokenFile
			));
		}

		/// <summary>A save in Common goes to every build, a save in Builds/name to that one.</summary>
		private static int Watch(Workspace workspace, List<WorkspacePlayer> players, Program.Options options, int compilerPort)
		{
			var gate = new object();
			var changed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var load = new Program.Options();
			load.Set("load");
			load.Set("vm", options.Value("vm", "ilruntime"));
			var ownBots = Path.Combine(workspace.Directory, "Builds") + Path.DirectorySeparatorChar;
			using var timer = new Timer(_ =>
			{
				List<string> paths;
				lock (gate)
				{
					paths = changed.ToList();
					changed.Clear();
				}

				var shared = paths.Any(path => !path.StartsWith(ownBots, StringComparison.OrdinalIgnoreCase));
				var names = paths.Where(path => path.StartsWith(ownBots, StringComparison.OrdinalIgnoreCase)).Select(path => path.Substring(ownBots.Length).Split(Path.DirectorySeparatorChar)[0]);
				var affected = players.Where(player => player.IsRunning && (shared || names.Contains(player.Name, StringComparer.OrdinalIgnoreCase))).ToList();
				ForEach(affected, player => Bot(workspace, player, load, compilerPort));
			});
			using var watcher = new FileSystemWatcher(workspace.Directory, "*.cs") { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName };
			var state = Path.Combine(workspace.Directory, ".state") + Path.DirectorySeparatorChar;
			void Changed(string path)
			{
				if (path.StartsWith(state, StringComparison.OrdinalIgnoreCase))
					return;
				lock (gate)
					changed.Add(path);
				timer.Change(WatchDelayMilliseconds, Timeout.Infinite);
			}

			watcher.Changed += (_, change) => Changed(change.FullPath);
			watcher.Created += (_, change) => Changed(change.FullPath);
			watcher.Deleted += (_, change) => Changed(change.FullPath);
			watcher.Renamed += (_, change) => Changed(change.FullPath);
			watcher.EnableRaisingEvents = true;
			Console.WriteLine("Watching " + workspace.Directory);
			Thread.Sleep(Timeout.Infinite);
			return 0;
		}
	}
}
