using System;
using System.Collections.Generic;
using System.Linq;

namespace Dexc
{
	internal static class DexcRuntimeHistory
	{
		internal sealed class Entry
		{
			internal string Key;
			internal string Kind;
			internal string Label;
			internal string Detail;
			internal DateTimeOffset At;
			internal Action Edit;
		}

		/// <summary>State of this class. Owned by <see cref="DexcUIRoot"/>; nothing here survives a root reset.</summary>
		internal sealed class State
		{
			internal string Filter = "All";
			internal int Offset;
		}

		internal static State Data => DexcUIRoot.Current.History;
		internal static string Filter
		{
			get => Data.Filter;
			set => Data.Filter = value;
		}
		internal static int Offset
		{
			get => Data.Offset;
			set => Data.Offset = value;
		}

		internal static void Reset()
		{
			Filter = "All";
			Offset = 0;
		}

		internal static IReadOnlyList<Entry> Read()
		{
			// Stores own the bounded, immutable history. UI never reconstructs it from current objects.
			var entries = new List<Entry>();
			if (Filter == "All" || Filter == "Command")
			{
				foreach (var pair in DexcCommandRuntime.History.GetHistorySnapshot().Reverse())
				{
					var item = pair.Value;
					entries.Add(
						new Entry
						{
							Key = "Command:" + item.Id,
							Kind = "Command",
							Label = item.CommandPath + " · " + item.Status,
							At = item.OccurredAtUtc,
							Detail =
								item.CommandPath
								+ "\n"
								+ item.Status
								+ "\n"
								+ item.OccurredAtUtc.ToString("u")
								+ "\n"
								+ item.InputSummary
								+ "\n"
								+ item.OutputSummary
								+ "\n"
								+ item.ErrorCode,
							Edit = () => DexcRuntimeCommandForm.RestoreHistory(pair.Key, item),
						}
					);
				}
			}

			if (Filter == "All" || Filter == "Setting")
			{
				foreach (var item in DexcSettingRuntime.Registry.GetHistorySnapshot().Reverse())
				{
					entries.Add(
						new Entry
						{
							Key = "Setting:" + item.Id,
							Kind = "Setting",
							Label = item.Path + " · " + item.Operation,
							At = item.OccurredAtUtc,
							Detail =
								item.Path
								+ "\n"
								+ item.Operation
								+ "\n"
								+ item.OccurredAtUtc.ToString("u")
								+ "\nBefore: "
								+ DexcInputText.Format(item.Before)
								+ "\nAfter: "
								+ DexcInputText.Format(item.After),
						}
					);
				}
			}

			if (Filter == "All" || Filter == "Inspector")
			{
				foreach (var item in DexcInspectorRuntime.Registry.GetHistorySnapshot().Reverse())
				{
					entries.Add(
						new Entry
						{
							Key = "Inspector:" + item.Id,
							Kind = "Inspector",
							Label = item.Label + " / " + item.FieldName + " · " + item.Status,
							At = item.OccurredAtUtc,
							Detail =
								item.Label
								+ "\n"
								+ item.ComponentType
								+ " / "
								+ item.FieldName
								+ "\n"
								+ item.Status
								+ "\n"
								+ item.OccurredAtUtc.ToString("u")
								+ "\nBefore: "
								+ DexcRuntimeInspectorForm.SnapshotText(item.Before)
								+ "\nAfter: "
								+ DexcRuntimeInspectorForm.SnapshotText(item.After),
						}
					);
				}
			}

			return entries.OrderByDescending(item => item.At).ToArray();
		}

		internal static void Open(Entry item)
		{
			if (item.Edit == null)
			{
				DexcRuntimeFormDetail.Open(item.Detail);
				return;
			}

			DexcRuntimeFormDetail.OpenSelection(
				"Command history",
				(search, page) =>
					new DexcRuntimeFormDetail.ChoicePage(
						new[] { new DexcRuntimeFormDetail.Choice("Open inputs", item.Edit) },
						false,
						item.Detail
					),
				false
			);
		}
	}
}
