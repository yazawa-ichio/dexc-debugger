using System.Linq;

namespace Dexc
{
	internal static class DexcInspectorSnapshotText
	{
		internal static bool IsCollection(InspectorValueSnapshot snapshot)
		{
			return snapshot?.State == DebugInspectorSnapshotState.Value
				&& snapshot.Value != null
				&& snapshot.Value.TryGetObject(out var members)
				&& members.Count == 3
				&& members.Any(member => member.Name == "items")
				&& members.Any(member => member.Name == "count")
				&& members.Any(member => member.Name == "truncated");
		}

		internal static string Format(InspectorValueSnapshot snapshot, bool detail = false)
		{
			if (snapshot == null)
			{
				return "Missing";
			}

			if (!IsCollection(snapshot))
			{
				return snapshot.State == DebugInspectorSnapshotState.UnityReference ? snapshot.ReferenceLabel
					: snapshot.State == DebugInspectorSnapshotState.Value ? DexcInputText.Format(snapshot.Value)
					: snapshot.State + " " + snapshot.Error;
			}

			snapshot.Value.TryGetObject(out var members);
			var count = DexcInputText.Format(members.Single(member => member.Name == "count").Value);
			members.Single(member => member.Name == "items").Value.TryGetArray(out var items);
			members.Single(member => member.Name == "truncated").Value.TryGetBoolean(out var truncated);
			var summary = count + " items" + (truncated ? " (first 128)" : string.Empty);
			if (!detail)
			{
				return summary;
			}

			return summary
				+ "\n"
				+ string.Join(
					"\n",
					items.Select(item =>
					{
						item.TryGetObject(out var fields);
						var values = fields.ToDictionary(field => field.Name, field => field.Value);
						var state = DexcInputText.Format(values["state"]);
						var value = state == "Value" ? DexcInputText.Format(values["value"]) : DexcInputText.Format(values["label"]);
						return "[" + DexcInputText.Format(values["index"]) + "] " + state + " · " + value;
					})
				);
		}
	}
}
