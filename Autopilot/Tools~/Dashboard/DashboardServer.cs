using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;

namespace Dexc.Autopilot.Dashboard
{
	/// <summary>
	/// The HTTP side: the API over the hub, the pages that come with the dashboard, and the project's own pages in
	/// Dashboard/ of the workspace. Loopback only. A request whose Host is not this machine is refused, so a web page
	/// cannot reach it by renaming itself, and every request that changes something needs the token of this run.
	/// </summary>
	internal sealed class DashboardServer : IAsyncDisposable
	{
		public const string TokenHeader = "X-Dexc-Token";
		public const string ProjectFolder = "Dashboard";

		private readonly WebApplication m_App;

		public DashboardServer(Hub hub, int port, string token, string pages)
		{
			Hub = hub;
			Token = token;
			var builder = WebApplication.CreateSlimBuilder();
			builder.Logging.ClearProviders();
			builder.WebHost.UseKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, port));
			m_App = builder.Build();
			m_App.Use(Guard);
			MapApi(m_App);
			var project = Path.Combine(hub.Directory, ProjectFolder);
			Directory.CreateDirectory(project);
			Serve(m_App, pages, PathString.Empty);
			Serve(m_App, project, "/project");
		}

		public Hub Hub { get; }

		public string Token { get; }

		public Task Start() => m_App.StartAsync();

		/// <summary>The address it listens on, with the port the system gave when it was asked for any.</summary>
		public string Url => m_App.Urls.First().Replace("[::1]", "127.0.0.1").TrimEnd('/') + "/";

		public async ValueTask DisposeAsync()
		{
			await m_App.StopAsync();
			await m_App.DisposeAsync();
		}

		private Task Guard(HttpContext context, Func<Task> next)
		{
			var host = context.Request.Host.Host;
			if (host != "127.0.0.1" && host != "localhost")
			{
				context.Response.StatusCode = StatusCodes.Status421MisdirectedRequest;
				return Task.CompletedTask;
			}

			var reads = HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method);
			if (!reads && context.Request.Headers[TokenHeader] != Token)
			{
				context.Response.StatusCode = StatusCodes.Status403Forbidden;
				return Task.CompletedTask;
			}

			return next();
		}

		private void MapApi(WebApplication app)
		{
			app.MapGet("/api/builds", () => Json(new JsonArray(Hub.Builds.Select(build => (JsonNode)Hub.Summary(build)).ToArray())));
			app.MapGet("/api/status", (string build) => Each(build, Hub.Full));
			app.MapGet("/api/graph", (string build) => Each(build, view => view.Graph?.DeepClone()));
			app.MapGet("/api/history", (string build) => Each(build, Hub.History));
			app.MapGet("/api/pages", () =>
			{
				var folder = Path.Combine(Hub.Directory, ProjectFolder);
				var pages = Directory.Exists(folder)
					? Directory.GetFiles(folder, "*.html").Select(Path.GetFileName).OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
					: Enumerable.Empty<string>();
				return Json(new JsonArray(pages.Select(name => (JsonNode)name).ToArray()));
			});
			app.MapGet("/api/events", Events);
			app.MapGet("/api/decisions", () => Json(Hub.Relay.Decisions()));
			app.MapGet("/api/journal", () => Json(Hub.Relay.Journal()));
			app.MapPost("/api/decisions/answer", async (HttpContext context) =>
			{
				var body = await Body(context);
				var by = Text(body, "by") ?? "human";
				if (by != "human" && by != "agent")
					return Results.BadRequest("by is human or agent");
				try
				{
					var reply = Hub.Relay.Answer(Text(body, "build"), Text(body, "id"), Text(body, "answer"), by);
					return reply["ok"]?.GetValue<bool>() == true ? Json(reply) : Results.Text(reply.ToJsonString(), "application/json", statusCode: StatusCodes.Status409Conflict);
				}
				catch (Exception exception) when (exception is System.IO.IOException || exception is System.Net.Sockets.SocketException)
				{
					return Results.Text(new JsonObject { ["ok"] = false, ["error"] = exception.Message }.ToJsonString(), "application/json", statusCode: StatusCodes.Status502BadGateway);
				}
			});
			app.MapPost("/api/instructions", async (HttpContext context) =>
			{
				var body = await Body(context);
				var text = Text(body, "text");
				return string.IsNullOrWhiteSpace(text) ? Results.BadRequest("text is empty") : Json(Hub.Relay.Instruct(text, body?["builds"] as JsonArray));
			});
			app.MapPost("/api/agent/say", async (HttpContext context) =>
			{
				var text = Text(await Body(context), "text");
				if (string.IsNullOrWhiteSpace(text))
					return Results.BadRequest("text is empty");
				Hub.Relay.Say(text);
				return Results.NoContent();
			});
			// A POST: taking an event delivers it, and a page elsewhere must not take one away from the agent.
			app.MapPost("/api/agent/next", async (HttpContext context, double? timeout) =>
			{
				var wait = TimeSpan.FromSeconds(Math.Clamp(timeout ?? AgentWaitSeconds, 0, MaximumAgentWaitSeconds));
				try
				{
					var next = await Hub.Relay.Next(wait, context.RequestAborted);
					return next == null ? Results.NoContent() : Json(next);
				}
				catch (OperationCanceledException)
				{
					return Results.NoContent();
				}
			});
		}

		/// <summary>Under the two minutes a shell command of an agent may take by default.</summary>
		public const double AgentWaitSeconds = 90;
		private const double MaximumAgentWaitSeconds = 300;

		private static async Task<JsonObject> Body(HttpContext context)
		{
			try
			{
				return await JsonNode.ParseAsync(context.Request.Body) as JsonObject;
			}
			catch (System.Text.Json.JsonException)
			{
				return null;
			}
		}

		private static string Text(JsonObject body, string name) => body?[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

		/// <summary>One build when named, or every build by name.</summary>
		private IResult Each(string name, Func<BuildView, JsonNode> select)
		{
			if (!string.IsNullOrEmpty(name))
			{
				var build = Hub.Build(name);
				return build == null ? Results.NotFound() : Json(select(build));
			}

			var all = new JsonObject();
			foreach (var build in Hub.Builds)
				all[build.Name] = select(build);
			return Json(all);
		}

		private async Task Events(HttpContext context)
		{
			context.Response.Headers.CacheControl = "no-store";
			context.Response.ContentType = "text/event-stream";
			var reader = Hub.Listen();
			try
			{
				foreach (var build in Hub.Builds)
				{
					await context.Response.WriteAsync("event: build\ndata: " + Hub.Full(build).ToJsonString() + "\n\n");
					if (build.Graph != null)
						await context.Response.WriteAsync("event: graph\ndata: " + new JsonObject { ["name"] = build.Name, ["graph"] = build.Graph.DeepClone() }.ToJsonString() + "\n\n");
				}

				await context.Response.Body.FlushAsync();
				await foreach (var text in reader.ReadAllAsync(context.RequestAborted))
				{
					await context.Response.WriteAsync(text);
					await context.Response.Body.FlushAsync();
				}
			}
			catch (OperationCanceledException)
			{
				// The page went away.
			}
			finally
			{
				Hub.Forget(reader);
			}
		}

		private static IResult Json(JsonNode node) => Results.Text(node?.ToJsonString() ?? "null", "application/json; charset=utf-8");

		/// <summary>Files as they are on disk now: an agent that edits a page sees it at the next reload.</summary>
		private static void Serve(WebApplication app, string folder, PathString path)
		{
			var files = new PhysicalFileProvider(Path.GetFullPath(folder));
			app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files, RequestPath = path });
			app.UseStaticFiles(new StaticFileOptions
			{
				FileProvider = files,
				RequestPath = path,
				OnPrepareResponse = response => response.Context.Response.Headers.CacheControl = "no-store",
			});
		}
	}
}
