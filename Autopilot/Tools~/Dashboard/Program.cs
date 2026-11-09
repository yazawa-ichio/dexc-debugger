using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Dexc.Autopilot.Compiler;

namespace Dexc.Autopilot.Dashboard
{
	/// <summary>
	/// The dashboard of a workspace: the builds that ws start started, their machines, and the pages of the project.
	///
	///   Dexc.Autopilot.Dashboard [--port 7892] [--interval 1] [--human-timeout 60] [--forward http://127.0.0.1:4318]
	///
	/// The workspace is DEXC_WORKSPACE, or the first one from here upwards. It writes where it listens and its token to
	/// .state/.dashboard/endpoint.json, for ws agent, and prints the address to open. A question of a bot goes to the
	/// agent when nobody answered it in --human-timeout seconds. It takes OTLP/HTTP at /v1/, and --forward sends what came
	/// on as it came, to a collector or a backend such as grafana/otel-lgtm.
	/// </summary>
	internal static class Program
	{
		private const int DefaultPort = 7892;

		private static async Task<int> Main(string[] arguments)
		{
			Console.OutputEncoding = new UTF8Encoding(false);
			try
			{
				var port = int.Parse(Option(arguments, "port", DefaultPort.ToString(CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture);
				var interval = TimeSpan.FromSeconds(double.Parse(Option(arguments, "interval", "1"), CultureInfo.InvariantCulture));
				var humanTimeout = TimeSpan.FromSeconds(double.Parse(Option(arguments, "human-timeout", "60"), CultureInfo.InvariantCulture));
				var workspace = Workspace.Find();
				if (IsRunning(workspace))
					throw new InvalidOperationException("A dashboard already runs for this workspace.");

				var hub = new Hub(workspace.Directory, humanTimeout);
				var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
				var forward = Option(arguments, "forward", null);
				await using var server = new DashboardServer(hub, port, token, Path.Combine(AppContext.BaseDirectory, "wwwroot"), forward);
				await server.Start();
				var endpoint = workspace.DashboardEndpoint;
				Directory.CreateDirectory(Path.GetDirectoryName(endpoint));
				File.WriteAllText(endpoint, new JsonObject { ["url"] = server.Url, ["token"] = token, ["pid"] = Environment.ProcessId }.ToJsonString());
				// Closing the window ends the process without Ctrl+C; a kill cannot be heard, which the pid covers.
				AppDomain.CurrentDomain.ProcessExit += (_, _) => File.Delete(endpoint);
				Console.WriteLine("Dashboard of " + workspace.Directory);
				Console.WriteLine(server.Url + "?token=" + token);
				using var stop = new CancellationTokenSource();
				Console.CancelKeyPress += (_, press) =>
				{
					press.Cancel = true;
					stop.Cancel();
				};
				await hub.Run(interval, stop.Token);
				File.Delete(endpoint);
				return 0;
			}
			catch (InvalidOperationException exception)
			{
				Console.Error.WriteLine(exception.Message);
				return 1;
			}
		}

		/// <summary>Whether a dashboard runs for the workspace. A killed one left its file, which goes now.</summary>
		private static bool IsRunning(Workspace workspace)
		{
			try
			{
				workspace.RunningDashboard();
				return true;
			}
			catch (InvalidOperationException)
			{
				return false;
			}
		}

		private static string Option(string[] arguments, string name, string fallback)
		{
			var index = Array.IndexOf(arguments, "--" + name);
			return index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : fallback;
		}
	}
}
