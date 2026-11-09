using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;

namespace Dexc.Autopilot.Compiler
{
	/// <summary>A defect: the reports of one signature, across builds and runs.</summary>
	internal sealed class QADefect
	{
		public const string Open = "open";
		public const string Fixing = "fixing";
		public const string Fixed = "fixed";
		public const string WontFix = "wontfix";

		public string Id { get; set; }
		public string Signature { get; set; }
		public string Kind { get; set; }
		public string Title { get; set; }
		public string State { get; set; } = Open;
		public string Created { get; set; }
		public string Updated { get; set; }

		/// <summary>When it was last marked fixed: an occurrence after it is a recurrence.</summary>
		public string FixedAt { get; set; }

		public SortedDictionary<string, QAOccurrence> Builds { get; set; } = new SortedDictionary<string, QAOccurrence>(StringComparer.OrdinalIgnoreCase);

		/// <summary>The reports written in full, as build/id, oldest first.</summary>
		public List<string> Reports { get; set; } = new List<string>();

		public List<QANote> Notes { get; set; } = new List<QANote>();

		public int Count => Builds.Values.Sum(build => build.Count);
	}

	/// <summary>How often a defect occurred in one build, by run: a run counts from zero, so runs are kept apart.</summary>
	internal sealed class QAOccurrence
	{
		public SortedDictionary<string, int> Runs { get; set; } = new SortedDictionary<string, int>(StringComparer.Ordinal);
		public string First { get; set; }
		public string Last { get; set; }

		public int Count => Runs.Values.Sum();
	}

	internal sealed class QANote
	{
		public string Time { get; set; }
		public string By { get; set; }
		public string Text { get; set; }
	}

	internal sealed class QAData
	{
		public int Next { get; set; } = 1;
		public List<QADefect> Defects { get; set; } = new List<QADefect>();
	}

	/// <summary>What a gathering found: defects seen for the first time, and fixed defects that came back.</summary>
	internal sealed class QAChanges
	{
		public List<QADefect> Created { get; } = new List<QADefect>();
		public List<QADefect> Recurred { get; } = new List<QADefect>();
		public int Reports { get; set; }

		public bool Any => Created.Count > 0 || Recurred.Count > 0 || Reports > 0;
	}

	/// <summary>
	/// The defects of a workspace, in QA/defects.json, and their reports, in QA/reports/name. Gathering reads what the
	/// Players of the workspace wrote in .state/name/qa: a report once it is whole, the counts as they stand. The tools and
	/// the dashboard both change the file, so every change holds QA/.lock.
	/// </summary>
	internal static class QAStore
	{
		private const int ReportsPerDefect = 10;
		private const int LockMilliseconds = 10000;

		private static readonly JsonSerializerOptions s_Json = new JsonSerializerOptions
		{
			WriteIndented = true,
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
			DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		};

		public static string Folder(Workspace workspace) => Path.Combine(workspace.Directory, "QA");

		public static string Reports(Workspace workspace, string build) => Path.Combine(Folder(workspace), "reports", build);

		public static QAData Read(Workspace workspace)
		{
			var path = Path.Combine(Folder(workspace), "defects.json");
			return File.Exists(path) ? JsonSerializer.Deserialize<QAData>(File.ReadAllText(path), s_Json) : new QAData();
		}

		/// <summary>Reads, changes and writes the defects, alone.</summary>
		public static T Change<T>(Workspace workspace, Func<QAData, T> change)
		{
			Directory.CreateDirectory(Folder(workspace));
			using var gate = Lock(workspace);
			var data = Read(workspace);
			var result = change(data);
			var path = Path.Combine(Folder(workspace), "defects.json");
			File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(data, s_Json));
			File.Move(path + ".tmp", path, true);
			return result;
		}

		/// <summary>Takes the whole reports of every build and brings the counts up to date.</summary>
		public static QAChanges Gather(Workspace workspace)
		{
			return Change(workspace, data =>
			{
				var changes = new QAChanges();
				foreach (var build in workspace.File.Builds.Keys)
					Gather(workspace, new WorkspacePlayer(workspace, build), data, changes);
				return changes;
			});
		}

		public static void SetState(Workspace workspace, string id, string state, string by, string note)
		{
			if (state != QADefect.Open && state != QADefect.Fixing && state != QADefect.Fixed && state != QADefect.WontFix)
				throw new InvalidOperationException("A state is open, fixing, fixed or wontfix: " + state);
			Change(workspace, data =>
			{
				var defect = data.Defects.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase))
					?? throw new InvalidOperationException("No such defect: " + id);
				var now = Now();
				defect.Notes.Add(new QANote { Time = now, By = by, Text = defect.State + " -> " + state + (string.IsNullOrEmpty(note) ? string.Empty : ": " + note) });
				defect.State = state;
				defect.Updated = now;
				if (state == QADefect.Fixed)
					defect.FixedAt = now;
				return true;
			});
		}

		public static void AddNote(Workspace workspace, string id, string by, string text)
		{
			Change(workspace, data =>
			{
				var defect = data.Defects.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase))
					?? throw new InvalidOperationException("No such defect: " + id);
				defect.Notes.Add(new QANote { Time = Now(), By = by, Text = text });
				defect.Updated = Now();
				return true;
			});
		}

		public static string Now() => DateTime.UtcNow.ToString("O");

		/// <summary>The seed a report of the defect ran on, by the resource the workspace gave the Player, or null.</summary>
		public static string SeedOf(Workspace workspace, QADefect defect)
		{
			foreach (var report in defect.Reports)
			{
				var path = Path.Combine(Folder(workspace), "reports", report.Replace('/', Path.DirectorySeparatorChar), "report.json");
				if (!File.Exists(path))
					continue;
				var seed = JsonNode.Parse(File.ReadAllText(path))?["resource"]?["dexc.seed"]?.GetValue<string>();
				if (!string.IsNullOrEmpty(seed))
					return seed;
			}

			return null;
		}

		/// <summary>Whether the defect occurred in the build after the time.</summary>
		public static bool OccurredSince(QAData data, string id, string build, string time)
		{
			var defect = data.Defects.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
			return defect != null && defect.Builds.TryGetValue(build, out var occurrence) && occurrence.Last != null && string.CompareOrdinal(occurrence.Last, time) > 0;
		}

		/// <summary>
		/// Notes the budgets a build went over, as defects of kind perf, one per budget and series. Counted once per check
		/// that found it over, so the count says how long it stayed over. A fixed one that goes over again is open again.
		/// </summary>
		public static QAChanges RecordBudgets(Workspace workspace, IReadOnlyList<(string Build, string Signature, string Title)> overs)
		{
			if (overs.Count == 0)
				return new QAChanges();
			return Change(workspace, data =>
			{
				var changes = new QAChanges();
				var now = Now();
				foreach (var over in overs)
				{
					var defect = data.Defects.FirstOrDefault(known => known.Signature == over.Signature);
					if (defect == null)
					{
						defect = new QADefect { Id = "D" + data.Next.ToString("D4"), Signature = over.Signature, Kind = "perf", Title = over.Title, Created = now };
						data.Next++;
						data.Defects.Add(defect);
						changes.Created.Add(defect);
					}

					if (!defect.Builds.TryGetValue(over.Build, out var occurrence))
					{
						occurrence = new QAOccurrence { First = now };
						defect.Builds[over.Build] = occurrence;
					}

					occurrence.Runs.TryGetValue("budget", out var checks);
					occurrence.Runs["budget"] = checks + 1;
					occurrence.Last = now;
					defect.Title = over.Title;
					defect.Updated = now;
					if (defect.State == QADefect.Fixed && defect.FixedAt != null && string.CompareOrdinal(now, defect.FixedAt) > 0)
					{
						defect.State = QADefect.Open;
						defect.Notes.Add(new QANote { Time = now, By = "dashboard", Text = "fixed -> open: over the budget again in " + over.Build });
						changes.Recurred.Add(defect);
					}
				}

				return changes;
			});
		}

		private static void Gather(Workspace workspace, WorkspacePlayer player, QAData data, QAChanges changes)
		{
			var source = player.QAFolder;
			if (!Directory.Exists(source))
				return;
			var target = Reports(workspace, player.Name);
			Directory.CreateDirectory(target);
			var running = player.IsRunning;
			foreach (var folder in Directory.GetDirectories(source))
			{
				var report = Path.Combine(folder, "report.json");
				if (!File.Exists(report))
					continue;
				// A screenshot comes at the end of the frame: a running Player may still be writing it.
				if (running && JsonNode.Parse(File.ReadAllText(report))?["screenshot"]?.GetValue<string>() == "pending")
					continue;
				var destination = Path.Combine(target, Path.GetFileName(folder));
				if (Directory.Exists(destination))
					Directory.Delete(destination, true);
				Directory.Move(folder, destination);
				changes.Reports++;
			}

			foreach (var file in Directory.GetFiles(source, "signatures-*.json"))
			{
				var counts = JsonNode.Parse(File.ReadAllText(file));
				File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);
				var run = counts?["run"]?.GetValue<string>() ?? Path.GetFileNameWithoutExtension(file);
				foreach (var item in counts?["signatures"]?.AsArray() ?? new JsonArray())
					Count(data, changes, player.Name, run, item, target);
			}
		}

		private static void Count(QAData data, QAChanges changes, string build, string run, JsonNode item, string reports)
		{
			var signature = item["signature"].GetValue<string>();
			var defect = data.Defects.FirstOrDefault(known => known.Signature == signature);
			var now = Now();
			if (defect == null)
			{
				defect = new QADefect
				{
					Id = "D" + data.Next.ToString("D4"),
					Signature = signature,
					Kind = item["kind"]?.GetValue<string>(),
					Title = item["title"]?.GetValue<string>(),
					Created = now,
					Updated = now,
				};
				data.Next++;
				data.Defects.Add(defect);
				changes.Created.Add(defect);
			}

			if (!defect.Builds.TryGetValue(build, out var occurrence))
			{
				occurrence = new QAOccurrence();
				defect.Builds[build] = occurrence;
			}

			var count = item["count"].GetValue<int>();
			var last = item["last"]?.GetValue<string>();
			var first = item["first"]?.GetValue<string>();
			var grew = !occurrence.Runs.TryGetValue(run, out var before) || before != count;
			occurrence.Runs[run] = count;
			if (first != null && (occurrence.First == null || string.CompareOrdinal(first, occurrence.First) < 0))
				occurrence.First = first;
			if (last != null && (occurrence.Last == null || string.CompareOrdinal(last, occurrence.Last) > 0))
				occurrence.Last = last;
			if (grew)
				defect.Updated = now;
			foreach (var id in item["reports"]?.AsArray() ?? new JsonArray())
			{
				var name = build + "/" + id.GetValue<string>();
				if (defect.Reports.Count < ReportsPerDefect && !defect.Reports.Contains(name) && Directory.Exists(Path.Combine(reports, id.GetValue<string>())))
					defect.Reports.Add(name);
			}

			// Marked fixed, and seen again after that: it is not.
			if (defect.State == QADefect.Fixed && last != null && defect.FixedAt != null && string.CompareOrdinal(last, defect.FixedAt) > 0)
			{
				defect.State = QADefect.Open;
				defect.Notes.Add(new QANote { Time = now, By = "dashboard", Text = "fixed -> open: occurred again in " + build });
				changes.Recurred.Add(defect);
			}
		}

		/// <summary>A lock file, so that ws and the dashboard do not write over each other.</summary>
		private static FileStream Lock(Workspace workspace)
		{
			var path = Path.Combine(Folder(workspace), ".lock");
			var waited = 0;
			while (true)
			{
				try
				{
					return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
				}
				catch (IOException) when (waited < LockMilliseconds)
				{
					Thread.Sleep(50);
					waited += 50;
				}
			}
		}
	}
}
