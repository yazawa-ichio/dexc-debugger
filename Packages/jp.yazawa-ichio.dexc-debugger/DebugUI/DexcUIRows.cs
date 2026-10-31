using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Dexc
{
	/// <summary>Pages built from rows that have no form of their own.</summary>
	internal static class DexcUIRows
	{
		/// <summary>State of the Home page. Owned by <see cref="DexcUIRoot"/>; nothing here survives a root reset.</summary>
		internal sealed class State
		{
			internal float NextCount;
			internal object Session;
			internal int Commands;
			internal int Settings;
			internal int Watches;
			internal int Errors;
			internal int Warnings;
		}

		static State Data => DexcUIRoot.Current.Home;

		/// <summary>How many recent command runs Home lists.</summary>
		const int RecentCount = 3;

		/// <summary>
		/// Home: the six features with what each holds, a notice when definitions failed to collect, then the starred
		/// commands and setting groups and the latest command runs. The gamepad walks them in that order, top to bottom.
		/// </summary>
		internal static List<DexcUIRow> HomeRows()
		{
			var rows = new List<DexcUIRow>();
			if (DexcDebugger.CurrentSession == null)
			{
				rows.Add(
					new DexcUIRow
					{
						Kind = DexcUIRowKind.Label,
						Key = "home:uninitialized",
						Label = "DexcDebugger is not initialized.",
					}
				);
				return rows;
			}
			var counts = Counts();
			rows.Add(HomeRow("Command", DexcRuntimeUIPage.CommandList, 0, count: counts.Commands));
			rows.Add(HomeRow("Setting", DexcRuntimeUIPage.SettingList, 1, count: counts.Settings));
			rows.Add(HomeRow("Inspector", DexcRuntimeUIPage.InspectorHome, 2, count: counts.Watches, unit: " watched"));
			rows.Add(HomeRow("History", DexcRuntimeUIPage.History, 3));
			var diagnostics = HomeRow("Diagnostics", DexcRuntimeUIPage.Diagnostic, 4, count: counts.Errors);
			if (counts.Errors > 0)
				diagnostics.TrailingTone = DexcUIRowTone.Warning;
			rows.Add(diagnostics);
			rows.Add(HomeRow("Panels", DexcRuntimeUIPage.PanelList, 5, count: DexcRuntimePanelForm.Panels.Count));
			var navigation = 6;
			var warning = DexcRuntimeUIRenderer.CollectionWarning();
			if (!string.IsNullOrEmpty(warning))
			{
				// The notice opens Diagnostics, where each failure is listed.
				rows.Add(
					new DexcUIRow
					{
						Kind = DexcUIRowKind.Button,
						Key = "home:warning",
						Label = warning,
						Tone = DexcUIRowTone.Warning,
						Trailing = () => counts.Warnings > 0 ? counts.Warnings + " warnings" : null,
						Drill = true,
						ListItem = true,
						NavigationIndex = navigation++,
						Activated = () => DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.Diagnostic),
					}
				);
			}
			var favorites = FavoriteRows();
			if (favorites.Count > 0)
				rows.Add(Section("home:favorites", "FAVORITES"));
			foreach (var row in favorites)
			{
				row.NavigationIndex = navigation++;
				rows.Add(row);
			}
			var recent = RecentRows();
			if (recent.Count > 0)
				rows.Add(Section("home:recent", "RECENT"));
			foreach (var row in recent)
			{
				row.NavigationIndex = navigation++;
				rows.Add(row);
			}
			return rows;
		}

		static DexcUIRow Section(string key, string label) => new DexcUIRow { Kind = DexcUIRowKind.Header, Key = key, Label = label };

		/// <summary>
		/// The counts on Home, read at most once a second: counting reads whole registries, and IMGUI builds Home every
		/// frame. Inspector targets are not counted, because finding them runs project code.
		/// </summary>
		static State Counts()
		{
			var data = Data;
			var now = UnityEngine.Time.realtimeSinceStartup;
			if (ReferenceEquals(data.Session, DexcDebugger.CurrentSession) && now < data.NextCount)
				return data;
			data.Session = DexcDebugger.CurrentSession;
			data.NextCount = now + 1f;
			var one = DexcRuntimeUIState.CreatePageRequest(0, 1);
			data.Commands = DebugCommand.Registry.GetList(new DebugListQuery(string.Empty, string.Empty, one)).Page.TotalCount;
			data.Settings = DexcRuntimeSettingForm.ReadGroups().Count;
			data.Watches = DebugInspector.GetWatches(one).Page.TotalCount;
			data.Errors = DiagnosticCount(DebugDiagnosticSeverity.Error, one);
			data.Warnings = DiagnosticCount(DebugDiagnosticSeverity.Warning, one);
			return data;
		}

		static int DiagnosticCount(DebugDiagnosticSeverity severity, DebugPageRequest page) =>
			DebugDiagnostics
				.GetSummaries(new DebugDiagnosticQuery(DebugDiagnosticSourceFilter.All, DebugFeatureFilter.All, severity, page))
				.Page.TotalCount;

		/// <summary>The starred commands, then the starred setting groups, each with what it is and where it lives.</summary>
		static List<DexcUIRow> FavoriteRows()
		{
			var rows = new List<DexcUIRow>();
			var commands = DexcRuntimeFavorites.All(DexcFavoriteKind.Command);
			if (commands.Count > 0)
			{
				var items = DexcRuntimeCommandForm.AllItems();
				foreach (var path in commands)
				{
					var item = items.FirstOrDefault(candidate => candidate.Path == path);
					if (item != null)
						rows.Add(FavoriteRow("home:favorite-command:" + path, item.DisplayName, "Command", path, () => DexcRuntimeUIRenderer.SelectCommand(item)));
				}
			}
			var settings = DexcRuntimeFavorites.All(DexcFavoriteKind.Setting);
			if (settings.Count > 0)
			{
				var groups = DexcRuntimeSettingForm.ReadGroups();
				foreach (var label in settings)
				{
					var group = groups.FirstOrDefault(candidate => candidate.Label == label);
					if (group == null)
						continue;
					var slash = label.LastIndexOf('/');
					rows.Add(
						FavoriteRow(
							"home:favorite-setting:" + label,
							slash < 0 ? label : label.Substring(slash + 1),
							"Setting",
							label,
							() => DexcRuntimeSettingForm.Open(group.Items[0])
						)
					);
				}
			}
			return rows;
		}

		static DexcUIRow FavoriteRow(string key, string label, string kind, string path, Action open)
		{
			var slash = path.LastIndexOf('/');
			var where = kind + (slash < 0 ? string.Empty : " · " + path.Substring(0, slash));
			return new DexcUIRow
			{
				Kind = DexcUIRowKind.Button,
				Key = key,
				Label = label,
				Leading = "★",
				Detail = () => where,
				ListItem = true,
				Activated = open,
			};
		}

		/// <summary>The latest command runs, newest first. Opening one brings its inputs back, as History does.</summary>
		static List<DexcUIRow> RecentRows()
		{
			var rows = new List<DexcUIRow>();
			var history = DexcCommandRuntime.History.GetHistorySnapshot();
			for (var index = history.Count - 1; index >= 0 && rows.Count < RecentCount; index--)
			{
				var command = history[index].Key;
				var entry = history[index].Value;
				var slash = entry.CommandPath.LastIndexOf('/');
				var time = entry.OccurredAtUtc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
				var where = slash < 0 ? time : entry.CommandPath.Substring(0, slash) + " · " + time;
				var succeeded = entry.Status == CommandExecutionStatus.Succeeded;
				rows.Add(
					new DexcUIRow
					{
						Kind = DexcUIRowKind.Button,
						Key = "home:recent:" + entry.Id,
						Label = slash < 0 ? entry.CommandPath : entry.CommandPath.Substring(slash + 1),
						Detail = () => where,
						Trailing = () => succeeded ? "OK" : entry.Status.ToString(),
						TrailingTone = succeeded ? DexcUIRowTone.Normal : DexcUIRowTone.Warning,
						ListItem = true,
						Activated = () => DexcRuntimeCommandForm.RestoreHistory(command, entry),
					}
				);
			}
			return rows;
		}

		/// <summary>The Inspector's first page: which kind of target list to open.</summary>
		internal static List<DexcUIRow> InspectorHomeRows()
		{
			return new List<DexcUIRow>
			{
				HomeRow("Registered", DexcRuntimeUIPage.InspectorRegistered, 0, "inspector-home:"),
				HomeRow("Scene", DexcRuntimeUIPage.InspectorScene, 1, "inspector-home:"),
				HomeRow("Watch", DexcRuntimeUIPage.InspectorWatches, 2, "inspector-home:"),
			};
		}

		static DexcUIRow HomeRow(
			string label,
			DexcRuntimeUIPage page,
			int navigation,
			string keyPrefix = "home:",
			int count = -1,
			string unit = null
		)
		{
			return new DexcUIRow
			{
				Kind = DexcUIRowKind.Button,
				Key = keyPrefix + page,
				Label = label,
				NavigationIndex = navigation,
				ListItem = true,
				Drill = true,
				Trailing = count < 0 ? (Func<string>)null : () => count.ToString(CultureInfo.InvariantCulture) + unit,
				Activated = () => DexcRuntimeUIState.Navigate(page),
			};
		}

		/// <summary>A Previous or Next row of a paged list. It counts in the gamepad's list after the entries.</summary>
		internal static DexcUIRow Paging(string key, string label, int navigation, System.Action move)
		{
			return new DexcUIRow
			{
				Kind = DexcUIRowKind.Button,
				Key = key,
				Label = label,
				NavigationIndex = navigation,
				Activated = move,
			};
		}
	}
}
