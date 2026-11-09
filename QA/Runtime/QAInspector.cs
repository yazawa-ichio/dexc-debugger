using System;
using System.Collections.Generic;

namespace Dexc.QA
{
	/// <summary>
	/// What the Watch of the Dexc Inspector shows when a report is written: the fields the person or the agent chose to
	/// keep an eye on, with their values at that moment. Read through the public Inspector API only, as an adapter on top
	/// of Dexc, so that it can move to a package of its own. Values are copies: the handles of the Inspector live only in
	/// this run.
	/// </summary>
	internal static class QAInspector
	{
		const int WatchLimit = 50;
		const int PageSize = 20;

		/// <returns>The watched fields, or null when Dexc is not running.</returns>
		internal static List<object> Watches()
		{
			if (!DexcDebugger.IsInitialized)
			{
				return null;
			}
			var labels = RegisteredLabels();
			var watches = new List<object>();
			var offset = 0;
			while (watches.Count < WatchLimit)
			{
				var page = DebugInspector.GetWatches(new DebugPageRequest(offset, PageSize));
				foreach (var entry in page.Entries)
				{
					if (watches.Count >= WatchLimit)
					{
						break;
					}
					watches.Add(Watch(entry, labels));
				}
				if (!page.Page.HasMore || page.Entries.Count == 0)
				{
					break;
				}
				offset += page.Entries.Count;
			}
			return watches;
		}

		static List<KeyValuePair<string, object>> Watch(DebugInspectorWatchEntry entry, Dictionary<DebugInspectorTargetHandle, string> labels)
		{
			string component = null;
			string field = null;
			string error = null;
			try
			{
				component = ComponentName(entry);
				field = FieldName(entry);
			}
			catch (Exception exception)
			{
				// A target gone since: the snapshot says so too. Noted, not logged, as a log of an error would be a report.
				error = exception.Message;
			}
			labels.TryGetValue(entry.Target, out var target);
			var snapshot = entry.Snapshot;
			return new List<KeyValuePair<string, object>>
			{
				Member("target", target),
				Member("component", component),
				Member("field", field),
				Member("state", entry.State.ToString()),
				Member("snapshot", snapshot == null ? null : new List<KeyValuePair<string, object>>
				{
					Member("state", snapshot.State.ToString()),
					Member("value", snapshot.State == DebugInspectorSnapshotState.Value ? Value(snapshot.Value) : null),
					Member("type", string.IsNullOrEmpty(snapshot.TypeName) ? null : snapshot.TypeName),
					Member("reference", string.IsNullOrEmpty(snapshot.ReferenceLabel) ? null : snapshot.ReferenceLabel),
					Member("error", string.IsNullOrEmpty(snapshot.Error) ? null : snapshot.Error),
				}),
				Member("error", error),
			};
		}

		/// <summary>The registered targets by their handle, as "label (path)": the objects the game gave the Inspector.</summary>
		static Dictionary<DebugInspectorTargetHandle, string> RegisteredLabels()
		{
			var labels = new Dictionary<DebugInspectorTargetHandle, string>();
			var offset = 0;
			while (true)
			{
				var page = DebugInspector.GetRegistered(new DebugListQuery(string.Empty, string.Empty, new DebugPageRequest(offset, PageSize)));
				foreach (var entry in page.Entries)
				{
					labels[entry.Target] = entry.Label + " (" + entry.Path + ")";
				}
				if (!page.Page.HasMore || page.Entries.Count == 0)
				{
					return labels;
				}
				offset += page.Entries.Count;
			}
		}

		static string ComponentName(DebugInspectorWatchEntry entry)
		{
			foreach (var component in DebugInspector.GetComponents(entry.Target).Entries)
			{
				if (component.Component.Equals(entry.Component))
				{
					return string.IsNullOrEmpty(component.DisplayName) ? component.TypeName : component.DisplayName;
				}
			}
			return null;
		}

		static string FieldName(DebugInspectorWatchEntry entry)
		{
			var offset = 0;
			while (true)
			{
				var page = entry.Component.GetFields(new DebugPageRequest(offset, PageSize));
				foreach (var field in page.Entries)
				{
					if (field.Id.Equals(entry.Field))
					{
						return field.DisplayName;
					}
				}
				if (!page.Page.HasMore || page.Entries.Count == 0)
				{
					return null;
				}
				offset += page.Entries.Count;
			}
		}

		/// <summary>A DebugValue as plain values the report writes.</summary>
		static object Value(DebugValue value)
		{
			if (value == null)
			{
				return null;
			}
			switch (value.Kind)
			{
				case DebugValueKind.Boolean:
					value.TryGetBoolean(out var flag);
					return flag;
				case DebugValueKind.SignedInteger:
					value.TryGetSignedInteger(out var signed);
					return signed;
				case DebugValueKind.UnsignedInteger:
					value.TryGetUnsignedInteger(out var unsigned);
					return unsigned;
				case DebugValueKind.FloatingPoint:
					value.TryGetFloatingPoint(out var number);
					return number;
				case DebugValueKind.String:
					value.TryGetString(out var text);
					return text;
				case DebugValueKind.Array:
					value.TryGetArray(out var items);
					var list = new List<object>();
					foreach (var item in items)
					{
						list.Add(Value(item));
					}
					return list;
				case DebugValueKind.Object:
					value.TryGetObject(out var members);
					var result = new List<KeyValuePair<string, object>>();
					foreach (var member in members)
					{
						result.Add(Member(member.Name, Value(member.Value)));
					}
					return result;
				default:
					return null;
			}
		}

		static KeyValuePair<string, object> Member(string key, object value)
		{
			return new KeyValuePair<string, object>(key, value);
		}
	}
}
