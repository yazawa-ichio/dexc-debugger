using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dexc.Autopilot.Compiler;

namespace Dexc.Autopilot.Dashboard
{
	/// <summary>
	/// How a map of the game lies on an image, written by the project in Dashboard/maps/name.json: which two world axes
	/// run across and down the image, and the world coordinates at its edges. The name is what ITelemetryLocation calls the
	/// map. Without an image the view draws a grid over the same bounds.
	/// </summary>
	internal sealed class MapLayout
	{
		public string Image { get; set; }
		public string Horizontal { get; set; } = "x";
		public string Vertical { get; set; } = "z";
		public double Left { get; set; }
		public double Right { get; set; }
		public double Top { get; set; }
		public double Bottom { get; set; }

		/// <summary>Where a world position falls on the image, from 0 to 1 across and down. Outside the bounds it falls outside 0 to 1.</summary>
		public (double U, double V) Project(double x, double y, double z)
		{
			double Axis(string name) => name == "x" ? x : name == "y" ? y : z;
			return ((Axis(Horizontal) - Left) / (Right - Left), (Axis(Vertical) - Top) / (Bottom - Top));
		}
	}

	/// <summary>The maps of the project, and what happened where on them: the positions, the hitches, the defects.</summary>
	internal static class Maps
	{
		private static readonly JsonSerializerOptions s_Json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

		public static string Folder(string workspace) => Path.Combine(workspace, DashboardServer.ProjectFolder, "maps");

		public static IReadOnlyList<string> Names(string workspace)
		{
			var folder = Folder(workspace);
			return Directory.Exists(folder)
				? Directory.GetFiles(folder, "*.json").Select(Path.GetFileNameWithoutExtension).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList()
				: Array.Empty<string>();
		}

		public static MapLayout Layout(string workspace, string map)
		{
			var path = Path.Combine(Folder(workspace), map + ".json");
			return File.Exists(path) && Path.GetFileName(map) == map ? JsonSerializer.Deserialize<MapLayout>(File.ReadAllText(path), s_Json) : null;
		}

		/// <summary>Everything that happened on a map, projected onto its image: u and v from 0 to 1, with the build.</summary>
		public static JsonObject Points(Hub hub, string map)
		{
			var layout = Layout(hub.Directory, map);
			if (layout == null)
				return null;
			var positions = new JsonArray();
			var hitches = new JsonArray();
			foreach (var build in hub.Perf.Builds())
			{
				foreach (var record in hub.Perf.Positions(build))
				{
					if (Place(layout, map, record["attributes"]) is JsonObject place)
						positions.Add(With(place, build, null));
				}

				foreach (var record in hub.Perf.Hitches(build))
				{
					if (Place(layout, map, record["attributes"]) is JsonObject place)
					{
						place["ms"] = record["attributes"]?["unity.frame.time"]?.DeepClone();
						hitches.Add(With(place, build, record["time"]));
					}
				}
			}

			var defects = new JsonArray();
			var workspace = hub.Workspace();
			foreach (var defect in QAStore.Read(workspace).Defects)
			{
				foreach (var report in defect.Reports)
				{
					var path = Path.Combine(QAStore.Folder(workspace), "reports", report.Replace('/', Path.DirectorySeparatorChar), "report.json");
					if (!File.Exists(path))
						continue;
					var location = JsonNode.Parse(File.ReadAllText(path))?["location"];
					if (location == null || location["map"]?.GetValue<string>() != map)
						continue;
					var (u, v) = layout.Project(location["x"].GetValue<double>(), location["y"].GetValue<double>(), location["z"].GetValue<double>());
					defects.Add(new JsonObject { ["u"] = u, ["v"] = v, ["id"] = defect.Id, ["title"] = defect.Title, ["state"] = defect.State, ["build"] = report.Split('/')[0] });
				}
			}

			return new JsonObject
			{
				["map"] = map,
				["image"] = layout.Image == null ? null : "/project/maps/" + layout.Image,
				["layout"] = JsonSerializer.SerializeToNode(layout, s_Json),
				["positions"] = positions,
				["hitches"] = hitches,
				["defects"] = defects,
			};
		}

		/// <summary>The place of a record with the attributes of ITelemetryLocation, when it is on this map.</summary>
		private static JsonObject Place(MapLayout layout, string map, JsonNode attributes)
		{
			if (attributes?["dexc.map"]?.ToString() != map)
				return null;
			double Read(string key) => attributes[key] is JsonValue value && value.TryGetValue<double>(out var number) ? number : 0;
			var (u, v) = layout.Project(Read("dexc.position.x"), Read("dexc.position.y"), Read("dexc.position.z"));
			return new JsonObject { ["u"] = u, ["v"] = v };
		}

		private static JsonObject With(JsonObject place, string build, JsonNode time)
		{
			place["build"] = build;
			if (time != null)
				place["time"] = time.DeepClone();
			return place;
		}
	}
}
