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
	///   Dexc.Autopilot.Dashboard [--port 7892] [--interval 1]
	///
	/// The workspace is DEXC_WORKSPACE, or the first one from here upwards. It writes where it listens and its token to
	/// .state/.dashboard/endpoint.json, for the tools of the agent, and prints the address to open.
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
				var workspace = Workspace.Find();
				var hub = new Hub(workspace.Directory);
				var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
				await using var server = new DashboardServer(hub, port, token, Path.Combine(AppContext.BaseDirectory, "wwwroot"));
				await server.Start();
				var endpoint = Endpoint(workspace.Directory);
				Directory.CreateDirectory(Path.GetDirectoryName(endpoint));
				File.WriteAllText(endpoint, new JsonObject { ["url"] = server.Url, ["token"] = token }.ToJsonString());
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

		/// <summary>Where a running dashboard says how to reach it.</summary>
		public static string Endpoint(string workspace) => Path.Combine(workspace, ".state", ".dashboard", "endpoint.json");

		private static string Option(string[] arguments, string name, string fallback)
		{
			var index = Array.IndexOf(arguments, "--" + name);
			return index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : fallback;
		}
	}
}
