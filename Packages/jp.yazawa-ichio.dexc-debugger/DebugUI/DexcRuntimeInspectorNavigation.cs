using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Dexc
{
	internal static class DexcRuntimeInspectorNavigation
	{
		internal sealed class Row
		{
			internal Row(string key, string label, Action action)
			{
				Key = key;
				Label = label;
				Action = action;
			}

			internal string Key;
			internal string Label;
			internal Action Action;

			/// <summary>A second line under the label, such as when and what an entry was.</summary>
			internal string Detail;
		}

		internal sealed class Node
		{
			internal DebugObjectHandle Parent;
			internal DebugInspectorTargetHandle Target;
			internal string Label;
			internal int Offset;
		}

		/// <summary>State of this class. Owned by <see cref="DexcUIRoot"/>; nothing here survives a root reset.</summary>
		internal sealed class State
		{
			internal readonly Stack<Node> Parents = new Stack<Node>();
			internal DexcRuntimeUIPage Page;
			internal IReadOnlyList<Row> CurrentRows = Array.Empty<Row>();
			internal string Signature;
			internal float NextRead;
			internal string Search = string.Empty;
			internal bool SearchType;
			internal string NodeLabel = string.Empty;
			internal int NodeOffset;
			internal int Generation;
			internal readonly Dictionary<DexcRuntimeUIPage, int> Offsets = new Dictionary<DexcRuntimeUIPage, int>();
			internal readonly Dictionary<DexcRuntimeUIPage, string> Searches = new Dictionary<DexcRuntimeUIPage, string>();
			internal int Version;
			internal string Message;
		}

		internal static State Data => DexcUIRoot.Current.InspectorNavigation;
		static Stack<Node> Parents => Data.Parents;
		static DexcRuntimeUIPage Page
		{
			get => Data.Page;
			set => Data.Page = value;
		}
		static IReadOnlyList<Row> CurrentRows
		{
			get => Data.CurrentRows;
			set => Data.CurrentRows = value;
		}
		static string Signature
		{
			get => Data.Signature;
			set => Data.Signature = value;
		}
		static float NextRead
		{
			get => Data.NextRead;
			set => Data.NextRead = value;
		}
		internal static string Search
		{
			get => Data.Search;
			private set => Data.Search = value;
		}
		static bool SearchType
		{
			get => Data.SearchType;
			set => Data.SearchType = value;
		}
		static string NodeLabel
		{
			get => Data.NodeLabel;
			set => Data.NodeLabel = value;
		}
		static int NodeOffset
		{
			get => Data.NodeOffset;
			set => Data.NodeOffset = value;
		}
		static int Generation
		{
			get => Data.Generation;
			set => Data.Generation = value;
		}
		static Dictionary<DexcRuntimeUIPage, int> Offsets => Data.Offsets;
		static Dictionary<DexcRuntimeUIPage, string> Searches => Data.Searches;
		internal static int Version
		{
			get => Data.Version;
			private set => Data.Version = value;
		}
		internal static string Message
		{
			get => Data.Message;
			private set => Data.Message = value;
		}
		internal static bool IsList(DexcRuntimeUIPage page) =>
			page == DexcRuntimeUIPage.InspectorRegistered
			|| page == DexcRuntimeUIPage.InspectorScene
			|| page == DexcRuntimeUIPage.InspectorComponents
			|| page == DexcRuntimeUIPage.InspectorWatches
			|| page == DexcRuntimeUIPage.History
			|| page == DexcRuntimeUIPage.Diagnostic;

		internal static bool Searchable =>
			DexcRuntimeUIState.Page == DexcRuntimeUIPage.InspectorRegistered
			|| DexcRuntimeUIState.Page == DexcRuntimeUIPage.InspectorScene && DexcRuntimeUIState.InspectorScene.IsValid;

		internal static void Reset()
		{
			CancelSearch();
			Parents.Clear();
			DexcRuntimeUIState.InspectorScene = default(DebugSceneHandle);
			DexcRuntimeUIState.InspectorParent = default(DebugObjectHandle);
			Offsets.Clear();
			Searches.Clear();
			Search = string.Empty;
			NodeOffset = 0;
			NodeLabel = string.Empty;
			CurrentRows = Array.Empty<Row>();
			Signature = null;
			Generation++;
			Version++;
			NextRead = 0;
		}

		internal static void ChangedPage(DexcRuntimeUIPage page)
		{
			if (page == DexcRuntimeUIPage.FormDetail)
			{
				return;
			}

			if (Page == page)
			{
				return;
			}

			Offsets[Page] = NodeOffset;
			Searches[Page] = Search;
			CancelSearch();
			Page = page;
			NodeOffset = Offsets.TryGetValue(page, out var offset) ? offset : 0;
			Search = Searches.TryGetValue(page, out var search) ? search : string.Empty;
			CurrentRows = Array.Empty<Row>();
			Signature = null;
			Generation++;
			NextRead = 0;
		}

		static void CancelSearch()
		{
			if (DexcRuntimeUIState.InspectorSearch.IsValid && DexcDebugger.CurrentSession != null)
			{
				DebugInspector.CancelSearch(DexcRuntimeUIState.InspectorSearch);
			}

			DexcRuntimeUIState.InspectorSearch = default(DebugInspectorSearchId);
		}

		internal static void ChangeSearch(string value)
		{
			if (Search == value)
			{
				return;
			}

			Search = value ?? string.Empty;
			NodeOffset = 0;
			CancelSearch();
			Refresh();
		}

		internal static IReadOnlyList<Row> Rows()
		{
			if (Page != DexcRuntimeUIState.Page)
			{
				ChangedPage(DexcRuntimeUIState.Page);
			}

			if (Time.realtimeSinceStartup >= NextRead)
			{
				Refresh();
			}

			return CurrentRows;
		}

		internal static void Refresh()
		{
			if (!DexcRuntimeUIState.IsOpen || DexcDebugger.CurrentSession == null || !IsList(DexcRuntimeUIState.Page))
			{
				return;
			}

			var rows = new List<Row>();
			Message = string.Empty;
			try
			{
				var request = DexcRuntimeUIState.CreatePageRequest(
					NodeOffset,
					16,
					DexcRuntimeUIState.Page == DexcRuntimeUIPage.InspectorScene
				);
				var more = false;
				switch (DexcRuntimeUIState.Page)
				{
					case DexcRuntimeUIPage.Diagnostic:
						var diagnostics = DebugDiagnostics.GetSummaries(
							new DebugDiagnosticQuery(DebugDiagnosticSourceFilter.All, DebugFeatureFilter.All, null, request)
						);
						foreach (var summary in diagnostics.Summaries)
						{
							rows.Add(
								new Row(
									summary.DiagnosticId.ToString(),
									summary.Severity + " · " + summary.Code,
									() =>
									{
										var text =
											summary.Code
											+ "\n\n"
											+ summary.Message
											+ "\n\n"
											+ summary.Feature
											+ " / "
											+ summary.Phase
											+ "\n"
											+ summary.Identity;
										if (
											DebugDiagnostics.TryGetDetail(summary.DiagnosticId, out var detail)
											&& !string.IsNullOrEmpty(detail.ExceptionTypeName)
										)
										{
											text += "\n\n" + detail.ExceptionTypeName + ": " + detail.ExceptionMessage;
										}

										DexcRuntimeFormDetail.Open(text);
									}
								)
								{
									// The severity and code, then the message, then what it was about. The first line already names the
									// severity, so nothing sits at the right end and the lines below keep the row's whole width.
									Detail = summary.Message + (string.IsNullOrEmpty(summary.Identity) ? string.Empty : "\n" + summary.Identity),
								}
							);
						}

						more = diagnostics.Page.HasMore;
						break;
					case DexcRuntimeUIPage.InspectorRegistered:
						var registered = DebugInspector.GetRegistered(new DebugListQuery(string.Empty, Search, request));
						foreach (var item in registered.Entries)
						{
							rows.Add(
								new Row(
									item.Target.ToString(),
									item.Label,
									() => OpenTarget(item.Target, DexcRuntimeUIPage.InspectorRegistered)
								)
							);
						}

						more = registered.Page.HasMore;
						break;
					case DexcRuntimeUIPage.InspectorScene:
						if (!DexcRuntimeUIState.InspectorScene.IsValid)
						{
							var scenes = DebugInspector.GetScenes().Items;
							foreach (var scene in scenes.Skip(NodeOffset).Take(request.Count))
							{
								rows.Add(
									new Row(
										scene.Scene.ToString(),
										scene.Name,
										() =>
										{
											DexcRuntimeUIState.InspectorScene = scene.Scene;
											DexcRuntimeUIState.InspectorParent = default(DebugObjectHandle);
											Parents.Clear();
											NodeLabel = scene.Name;
											NodeOffset = 0;
											Refresh();
										}
									)
								);
							}

							more = NodeOffset + request.Count < scenes.Count;
							break;
						}

						rows.Add(
							new Row(
								"search-type",
								SearchType ? "Search: Component type" : "Search: Name",
								() =>
								{
									SearchType = !SearchType;
									CancelSearch();
									NodeOffset = 0;
									Refresh();
								}
							)
						);
						if (!string.IsNullOrEmpty(Search))
						{
							if (!DexcRuntimeUIState.InspectorSearch.IsValid)
							{
								DexcRuntimeUIState.InspectorSearch = DebugInspector
									.StartSearch(
										new DebugInspectorSearchQuery(
											DexcRuntimeUIState.InspectorScene,
											SearchType ? "" : Search,
											SearchType ? Search : "",
											null
										)
									)
									.Value;
							}

							var found = DebugInspector.GetSearchResults(DexcRuntimeUIState.InspectorSearch, request);
							foreach (var item in found.Entries)
							{
								rows.Add(
									new Row(
										item.Target.ToString(),
										item.Path + " · " + item.ComponentType,
										() => OpenTarget(item.Target, DexcRuntimeUIPage.InspectorScene)
									)
								);
							}

							Message = found.State + (found.IsTruncated ? " · Results limited" : string.Empty);
							more = found.Page.HasMore;
							rows.Add(new Row("cancel-search", "Clear search", () => ChangeSearch(string.Empty)));
							break;
						}

						// The object opened here is gone. It has no children to list, and listing none would look like a live
						// object without any, so the page says it is missing; Back still returns to its parent.
						if (
							DexcRuntimeUIState.InspectorParent.IsValid
							&& DexcRuntimeUIState.InspectorTarget.IsValid
							&& DebugInspector.GetComponents(DexcRuntimeUIState.InspectorTarget).Entries.Count == 0
						)
						{
							Message = NodeLabel + " · Missing";
							break;
						}

						Message = NodeLabel;
						if (DexcRuntimeUIState.InspectorParent.IsValid)
						{
							rows.Add(
								new Row(
									"components",
									"Components of " + NodeLabel,
									() => OpenTarget(DexcRuntimeUIState.InspectorTarget, DexcRuntimeUIPage.InspectorScene)
								)
							);
						}

						var hierarchy = DebugInspector.GetHierarchyChildren(
							new DebugHierarchyChildrenQuery(DexcRuntimeUIState.InspectorScene, DexcRuntimeUIState.InspectorParent, request)
						);
						foreach (var item in hierarchy.Entries)
						{
							rows.Add(
								new Row(
									item.Target.ToString(),
									(item.HasChildren ? "› " : string.Empty) + item.Label,
									() =>
									{
										if (!item.HasChildren)
										{
											OpenTarget(item.InspectorTarget, DexcRuntimeUIPage.InspectorScene);
											return;
										}

										Parents.Push(
											new Node
											{
												Parent = DexcRuntimeUIState.InspectorParent,
												Target = DexcRuntimeUIState.InspectorTarget,
												Label = NodeLabel,
												Offset = NodeOffset,
											}
										);
										DexcRuntimeUIState.InspectorParent = item.Target;
										DexcRuntimeUIState.InspectorTarget = item.InspectorTarget;
										NodeLabel = item.Path;
										NodeOffset = 0;
										Refresh();
									}
								)
							);
						}

						more = hierarchy.Page.HasMore;
						break;
					case DexcRuntimeUIPage.InspectorComponents:
						var components = DebugInspector.GetComponents(DexcRuntimeUIState.InspectorTarget).Entries;
						if (components.Count == 0)
						{
							DexcRuntimeUIState.ReturnFromStaleInspector("The selected inspector target is Missing.");
							break;
						}

						foreach (var item in components.Skip(NodeOffset).Take(request.Count))
						{
							rows.Add(
								new Row(
									item.Component.ToString(),
									item.DisplayName,
									() => DexcRuntimeInspectorForm.Open(item.Component, item.DisplayName)
								)
							);
						}

						more = NodeOffset + request.Count < components.Count;
						break;
					case DexcRuntimeUIPage.InspectorWatches:
						var watches = DebugInspector.GetWatches(request);
						foreach (var watch in watches.Entries)
						{
							rows.Add(new Row(watch.WatchId.ToString(), WatchLabel(watch), () => OpenWatch(watch)));
						}

						more = watches.Page.HasMore;
						break;
					case DexcRuntimeUIPage.History:
						rows.Add(
							new Row(
								"filter",
								"Type: " + DexcRuntimeHistory.Filter,
								() =>
									DexcRuntimeFormDetail.OpenSelection(
										"History type",
										(search, page) =>
											new DexcRuntimeFormDetail.ChoicePage(
												new[] { "All", "Command", "Setting", "Inspector" }
													.Select(kind => new DexcRuntimeFormDetail.Choice(
														kind,
														() =>
														{
															DexcRuntimeHistory.Filter = kind;
															NodeOffset = 0;
															Refresh();
														}
													))
													.ToArray(),
												false
											),
										false
									)
							)
						);
						var history = DexcRuntimeHistory.Read();
						foreach (var entry in history.Skip(NodeOffset).Take(request.Count))
						{
							rows.Add(
								new Row(entry.Key, entry.Label, () => DexcRuntimeHistory.Open(entry))
								{
									Detail = entry.At.ToLocalTime().ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) + " · " + entry.Kind,
								}
							);
						}

						more = NodeOffset + request.Count < history.Count;
						break;
				}

				if (NodeOffset > 0)
				{
					rows.Add(
						new Row(
							"previous",
							"Previous",
							() =>
							{
								NodeOffset = Math.Max(0, NodeOffset - request.Count);
								Refresh();
							}
						)
					);
				}

				if (more)
				{
					rows.Add(
						new Row(
							"next",
							"Next",
							() =>
							{
								NodeOffset += request.Count;
								Refresh();
							}
						)
					);
				}

				if (rows.Count == 0)
				{
					Message = "No items";
				}
			}
			catch (Exception exception)
			{
				Message = exception.Message;
			}

			var signature = string.Join("|", rows.Select(row => row.Key + "=" + row.Label)) + Message;
			if (Signature != signature)
			{
				Signature = signature;
				Version++;
			}

			var selected = DexcRuntimeUIState.SelectedIndex < CurrentRows.Count ? CurrentRows[DexcRuntimeUIState.SelectedIndex].Key : null;
			var selectedIndex = selected == null ? -1 : rows.FindIndex(row => row.Key == selected);
			if (selectedIndex >= 0 && !DexcRuntimeUIRendererSelection.UseUGUI && !DexcRuntimeUIRendererSelection.UseUIToolkit)
			{
				DexcRuntimeUIState.SetSelectedIndex(selectedIndex);
			}

			var generation = Generation;
			var session = DexcDebugger.CurrentSession;
			var pageOwner = DexcRuntimeUIState.Page;
			foreach (var row in rows)
			{
				var action = row.Action;
				row.Action = () =>
				{
					if (
						DexcRuntimeUIState.IsOpen
						&& ReferenceEquals(session, DexcDebugger.CurrentSession)
						&& generation == Generation
						&& pageOwner == DexcRuntimeUIState.Page
					)
					{
						action();
					}
				};
			}

			CurrentRows = rows;
			NextRead = Time.realtimeSinceStartup + .25f;
		}

		static void OpenTarget(DebugInspectorTargetHandle target, DexcRuntimeUIPage returnPage)
		{
			DexcRuntimeUIState.InspectorTarget = target;
			DexcRuntimeUIState.InspectorReturnPage = returnPage;
			DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.InspectorComponents);
		}

		internal static bool BackScene()
		{
			if (DexcRuntimeUIState.Page != DexcRuntimeUIPage.InspectorScene || !DexcRuntimeUIState.InspectorScene.IsValid)
			{
				return false;
			}

			if (!string.IsNullOrEmpty(Search))
			{
				ChangeSearch(string.Empty);
				return true;
			}

			if (Parents.Count > 0)
			{
				var node = Parents.Pop();
				DexcRuntimeUIState.InspectorParent = node.Parent;
				DexcRuntimeUIState.InspectorTarget = node.Target;
				NodeLabel = node.Label;
				NodeOffset = node.Offset;
			}
			else
			{
				DexcRuntimeUIState.InspectorScene = default(DebugSceneHandle);
				DexcRuntimeUIState.InspectorParent = default(DebugObjectHandle);
				NodeLabel = string.Empty;
				NodeOffset = 0;
			}

			Refresh();
			return true;
		}

		static string WatchLabel(DebugInspectorWatchEntry watch) =>
			DexcInspectorRuntime.Registry.GetWatchLabel(watch.WatchId)
			+ " · "
			+ watch.State
			+ " · "
			+ DexcRuntimeInspectorForm.SnapshotText(watch.Snapshot);

		static void OpenWatch(DebugInspectorWatchEntry watch)
		{
			DexcRuntimeFormDetail.OpenSelection(
				"Watch",
				(search, page) =>
					new DexcRuntimeFormDetail.ChoicePage(
						new[]
						{
							new DexcRuntimeFormDetail.Choice(
								"Remove watch",
								() =>
								{
									DebugInspector.RemoveWatch(watch.WatchId);
									Refresh();
								}
							),
						},
						false,
						WatchLabel(watch)
					),
				false
			);
		}

		internal static void ChooseReference(DexcRuntimeInspectorForm.Field field)
		{
			DexcRuntimeFormDetail.OpenSelection(
				field.Descriptor.DisplayName,
				(search, page) =>
					new DexcRuntimeFormDetail.ChoicePage(
						new[]
						{
							new DexcRuntimeFormDetail.Choice(
								"None",
								() => DexcRuntimeInspectorForm.SelectReference(field, default(DebugInspectorTargetHandle), "null")
							),
							new DexcRuntimeFormDetail.Choice("Registered", () => ReferenceRegistered(field)),
							new DexcRuntimeFormDetail.Choice("Scene", () => ReferenceScenes(field)),
						},
						false
					),
				false
			);
		}

		static void SelectReferenceTarget(DexcRuntimeInspectorForm.Field field, DebugInspectorTargetHandle target)
		{
			var candidates = DexcInspectorRuntime.Registry.GetAssignableReferences(target, field.Descriptor.TypeName);
			if (candidates.Count == 1)
			{
				DexcRuntimeInspectorForm.SelectReference(field, candidates[0].Key, candidates[0].Value);
				return;
			}

			DexcRuntimeFormDetail.OpenSelection(
				"Reference component",
				(search, page) =>
					new DexcRuntimeFormDetail.ChoicePage(
						candidates
							.Skip(page.Offset)
							.Take(page.Count)
							.Select(item => new DexcRuntimeFormDetail.Choice(
								item.Value,
								() => DexcRuntimeInspectorForm.SelectReference(field, item.Key, item.Value)
							))
							.ToArray(),
						page.Offset + page.Count < candidates.Count
					),
				false
			);
		}

		static void ReferenceRegistered(DexcRuntimeInspectorForm.Field field)
		{
			DexcRuntimeFormDetail.OpenSelection(
				"Registered references",
				(search, page) =>
				{
					var candidates = DebugInspector.GetRegistered(new DebugListQuery(string.Empty, search, page));
					return new DexcRuntimeFormDetail.ChoicePage(
						candidates
							.Entries.Where(item =>
								DexcInspectorRuntime.Registry.GetAssignableReferences(item.Target, field.Descriptor.TypeName).Count > 0
							)
							.Select(item => new DexcRuntimeFormDetail.Choice(item.Label, () => SelectReferenceTarget(field, item.Target)))
							.ToArray(),
						candidates.Page.HasMore
					);
				}
			);
		}

		static void ReferenceScenes(DexcRuntimeInspectorForm.Field field)
		{
			DexcRuntimeFormDetail.OpenSelection(
				"Reference scene",
				(search, page) =>
				{
					var scenes = DebugInspector
						.GetScenes()
						.Items.Where(item => item.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
						.ToArray();
					return new DexcRuntimeFormDetail.ChoicePage(
						scenes
							.Skip(page.Offset)
							.Take(page.Count)
							.Select(item => new DexcRuntimeFormDetail.Choice(
								item.Name,
								() => ReferenceHierarchy(field, item.Scene, default(DebugObjectHandle))
							))
							.ToArray(),
						page.Offset + page.Count < scenes.Length
					);
				}
			);
		}

		static void ReferenceHierarchy(DexcRuntimeInspectorForm.Field field, DebugSceneHandle scene, DebugObjectHandle parent)
		{
			DexcRuntimeFormDetail.OpenSelection(
				"Reference objects",
				(search, page) =>
				{
					var count = DexcRuntimeUIState.CreatePageRequest(0, Math.Max(1, page.Count / 2), true).Count;
					var children = DebugInspector.GetHierarchyChildren(
						new DebugHierarchyChildrenQuery(
							scene,
							parent,
							DexcRuntimeUIState.CreatePageRequest(page.Offset / page.Count * count, count, true)
						)
					);
					var choices = new List<DexcRuntimeFormDetail.Choice>();
					foreach (var item in children.Entries)
					{
						if (
							DexcInspectorRuntime.Registry.GetAssignableReferences(item.InspectorTarget, field.Descriptor.TypeName).Count > 0
						)
						{
							choices.Add(
								new DexcRuntimeFormDetail.Choice(
									"Select " + item.Label,
									() => SelectReferenceTarget(field, item.InspectorTarget)
								)
							);
						}

						if (item.HasChildren)
						{
							choices.Add(
								new DexcRuntimeFormDetail.Choice(
									"Children of " + item.Label,
									() => ReferenceHierarchy(field, scene, item.Target)
								)
							);
						}
					}

					return new DexcRuntimeFormDetail.ChoicePage(choices, children.Page.HasMore);
				},
				false
			);
		}

		/// <summary>The current inspector list page as rows: search where the page has one, its message, and its entries.</summary>
		internal static List<DexcUIRow> ListRows()
		{
			var rows = new List<DexcUIRow>();
			if (Searchable)
				rows.Add(DexcUIRow.Search("search:inspector", () => Search, ChangeSearch));
			var entries = Rows();
			if (!string.IsNullOrEmpty(Message))
				rows.Add(
					new DexcUIRow
					{
						Kind = DexcUIRowKind.Label,
						Key = "inspector-list:message",
						Label = Message,
					}
				);
			for (var index = 0; index < entries.Count; index++)
			{
				var entry = entries[index];
				rows.Add(
					new DexcUIRow
					{
						Kind = DexcUIRowKind.Button,
						Key = entry.Key,
						Label = entry.Label,
						Detail = entry.Detail == null ? (Func<string>)null : () => entry.Detail,
						NavigationIndex = index,
						ListItem = true,
						Activated = entry.Action,
					}
				);
			}
			return rows;
		}
	}
}
