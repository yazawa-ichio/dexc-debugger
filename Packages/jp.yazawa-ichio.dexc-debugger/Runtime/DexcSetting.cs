using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Scripting;

namespace Dexc
{
	public enum DebugSettingScope
	{
		Global,
		Scene,
		Session,
	}

	public enum DebugSettingApplyMode
	{
		Immediate,
		Submit,
	}

	public enum DebugSettingAccess
	{
		ReadOnly,
		ReadWrite,
	}

	[AttributeUsage(AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
	[Conditional("DEXC_DEBUGGER")]
	public sealed class DebugSettingAttribute : PreserveAttribute
	{
		public DebugSettingAttribute(string path)
		{
			if (string.IsNullOrWhiteSpace(path))
				throw new ArgumentException("Setting path must not be empty.", nameof(path));
			Path = path;
			Scope = DebugSettingScope.Session;
			ApplyMode = DebugSettingApplyMode.Immediate;
			Access = DebugSettingAccess.ReadWrite;
		}

		public string Path { get; }
		public DebugSettingScope Scope { get; set; }
		public DebugSettingApplyMode ApplyMode { get; set; }
		public DebugSettingAccess Access { get; set; }

		/// <summary>When true the value is stored through <see cref="DebugStorage"/> on every successful write and restored when the registry collects.</summary>
		public bool Persist { get; set; }
	}

	/// <summary>Marks every setting of a <see cref="DebugSettingSet{TSelf}"/> as persistent (see <see cref="DebugSettingAttribute.Persist"/>).</summary>
	[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
	[Conditional("DEXC_DEBUGGER")]
	public sealed class DebugPersistentAttribute : PreserveAttribute
	{
		public DebugPersistentAttribute() { }
	}

	/// <summary>Bounds a numeric Setting property, or a numeric input field of a Command.</summary>
	[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
	[Conditional("DEXC_DEBUGGER")]
	public sealed class DebugRangeAttribute : PreserveAttribute
	{
		public DebugRangeAttribute(double minimum, double maximum)
		{
			if (double.IsNaN(minimum) || double.IsNaN(maximum) || minimum > maximum)
				throw new ArgumentException("Range bounds are invalid.");
			Minimum = minimum;
			Maximum = maximum;
		}

		public double Minimum { get; }
		public double Maximum { get; }

		/// <summary>
		/// Whether <paramref name="value"/> is below the range. A float is compared with the bound as a float holds it:
		/// 0.1f is 0.10000000149 as a double, so it would otherwise fall outside a range that ends at 0.1.
		/// </summary>
		internal static bool IsBelow(double value, double minimum, bool single) =>
			single ? (float)value < (float)minimum : value < minimum;

		/// <summary>Whether <paramref name="value"/> is above the range, compared as <see cref="IsBelow"/> compares.</summary>
		internal static bool IsAbove(double value, double maximum, bool single) =>
			single ? (float)value > (float)maximum : value > maximum;
	}

	/// <summary>Shows a slider for a ranged numeric Setting property or Command input field.</summary>
	[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
	[Conditional("DEXC_DEBUGGER")]
	public sealed class DebugSliderAttribute : PreserveAttribute
	{
		public DebugSliderAttribute() { }
	}

	/// <summary>Edits a string Command input field in a box of several lines.</summary>
	[AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
	[Conditional("DEXC_DEBUGGER")]
	public sealed class DebugMultilineAttribute : PreserveAttribute
	{
		public DebugMultilineAttribute() { }
	}

	[AttributeUsage(AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
	[Conditional("DEXC_DEBUGGER")]
	public sealed class DebugPresetAttribute : PreserveAttribute
	{
		public DebugPresetAttribute(string id, string displayName = null, string description = null)
		{
			if (string.IsNullOrWhiteSpace(id))
				throw new ArgumentException("Preset ID must not be empty.", nameof(id));
			Id = id;
			DisplayName = displayName ?? id;
			Description = description ?? string.Empty;
		}

		public string Id { get; }
		public string DisplayName { get; }
		public string Description { get; }
	}

	public sealed class DebugSettingChanges
	{
		private readonly IReadOnlyList<DebugSettingId> m_Ids;

		internal DebugSettingChanges(IEnumerable<DebugSettingId> ids)
		{
			m_Ids = new ReadOnlyCollection<DebugSettingId>((ids ?? Enumerable.Empty<DebugSettingId>()).ToList());
		}

		public int Count
		{
			get { return m_Ids.Count; }
		}

		public bool Contains(DebugSettingId id)
		{
			return m_Ids.Contains(id);
		}
	}

	/// <summary>
	/// How the registry reaches a set's change hook. A direct call keeps the hook alive under managed stripping, where a
	/// protected virtual that is only ever found by reflection is removed.
	/// </summary>
	internal interface IDexcSettingSet
	{
		void NotifyChanged(DebugSettingChanges changes);
	}

	[Preserve]
	public abstract class DebugSettingSet<TSelf> : IDexcSettingSet
		where TSelf : DebugSettingSet<TSelf>
	{
		public static TSelf Current
		{
			get
			{
				// Held by the root instead of a static, so a new Play Mode session starts from persisted values again.
				var sets = DexcRoot.Current.SettingSets;
				if (!sets.TryGetValue(typeof(TSelf), out var entry))
					sets[typeof(TSelf)] = entry = new DexcRoot.SettingSetEntry();
				var session = DexcDebugger.CurrentSession;
				if (session != null && entry.Instance != null && entry.SessionId != session.SessionId)
					entry.Instance = null;
				if (entry.Instance == null)
				{
					entry.Instance = (TSelf)Activator.CreateInstance(typeof(TSelf));
					DexcSettingPersistence.Restore((TSelf)entry.Instance);
				}
				if (session != null)
					entry.SessionId = session.SessionId;
				return (TSelf)entry.Instance;
			}
		}

		protected virtual void OnChanged(DebugSettingChanges changes) { }

		void IDexcSettingSet.NotifyChanged(DebugSettingChanges changes)
		{
			OnChanged(changes);
		}

		internal static void ResetCurrent()
		{
			DexcRoot.Current.SettingSets.Remove(typeof(TSelf));
		}
	}

	public sealed class DebugSettingListItem
	{
		internal DebugSettingListItem(DebugSettingDescriptor descriptor)
		{
			Id = descriptor.Id;
			SetId = descriptor.SetId;
			Path = descriptor.Path;
			DisplayName = descriptor.Path.Split('/').Last();
			ValueType = descriptor.ValueType;
			Scope = descriptor.Scope;
			ApplyMode = descriptor.ApplyMode;
			Access = descriptor.Access;
		}

		public DebugSettingId Id { get; }
		public DebugSettingSetId SetId { get; }
		public string Path { get; }
		public string DisplayName { get; }
		public string ValueType { get; }
		public DebugSettingScope Scope { get; }
		public DebugSettingApplyMode ApplyMode { get; }
		public DebugSettingAccess Access { get; }
	}

	public sealed class DebugSettingDescriptor
	{
		internal DebugSettingDescriptor(
			DebugSettingId id,
			DebugSettingSetId setId,
			string path,
			Type type,
			DebugValue defaultValue,
			DebugSettingAttribute attribute,
			DebugRangeAttribute range,
			bool slider
		)
		{
			Id = id;
			SetId = setId;
			Path = path;
			ValueType = type.FullName;
			DefaultValue = defaultValue;
			Scope = attribute.Scope;
			ApplyMode = attribute.ApplyMode;
			Access = attribute.Access;
			HasRange = range != null;
			RangeMinimum = range == null ? 0 : range.Minimum;
			RangeMaximum = range == null ? 0 : range.Maximum;
			IsSlider = slider;
		}

		public DebugSettingId Id { get; }
		public DebugSettingSetId SetId { get; }
		public string Path { get; }
		public string ValueType { get; }
		public DebugValue DefaultValue { get; }
		public DebugSettingScope Scope { get; }
		public DebugSettingApplyMode ApplyMode { get; }
		public DebugSettingAccess Access { get; }
		public bool HasRange { get; }
		public double RangeMinimum { get; }
		public double RangeMaximum { get; }
		public bool IsSlider { get; }
	}

	public sealed class DebugSettingListPage
	{
		internal DebugSettingListPage(DebugCollectionSummary summary, DebugPageInfo page, IEnumerable<DebugSettingListItem> items)
		{
			Summary = summary;
			Page = page;
			Items = new ReadOnlyCollection<DebugSettingListItem>((items ?? Enumerable.Empty<DebugSettingListItem>()).ToList());
		}

		public DebugCollectionSummary Summary { get; }
		public DebugPageInfo Page { get; }
		public IReadOnlyList<DebugSettingListItem> Items { get; }
	}

	public sealed class DebugSettingPresetItem
	{
		internal DebugSettingPresetItem(DebugSettingPresetId id, string key, DebugSettingSetId setId, string name, string description)
		{
			Id = id;
			Key = key;
			SetId = setId;
			DisplayName = name;
			Description = description;
		}

		public DebugSettingPresetId Id { get; }

		/// <summary>The ID declared with <see cref="DebugPresetAttribute"/>. Unique across sets and the same in every session.</summary>
		public string Key { get; }
		public DebugSettingSetId SetId { get; }
		public string DisplayName { get; }
		public string Description { get; }
	}

	public sealed class DebugSettingPresetPage
	{
		internal DebugSettingPresetPage(DebugPageInfo page, IEnumerable<DebugSettingPresetItem> items)
		{
			Page = page;
			Items = new ReadOnlyCollection<DebugSettingPresetItem>((items ?? Enumerable.Empty<DebugSettingPresetItem>()).ToList());
		}

		public DebugPageInfo Page { get; }
		public IReadOnlyList<DebugSettingPresetItem> Items { get; }
	}

	public sealed class DebugSettingPresetDifference
	{
		internal DebugSettingPresetDifference(DebugSettingId id, string path, DebugValue current, DebugValue preset)
		{
			Id = id;
			Path = path;
			CurrentValue = current;
			PresetValue = preset;
		}

		public DebugSettingId Id { get; }
		public string Path { get; }
		public DebugValue CurrentValue { get; }
		public DebugValue PresetValue { get; }
	}

	public sealed class DebugSettingHistoryEntry
	{
		internal DebugSettingHistoryEntry(
			DebugHistoryId id,
			DateTimeOffset at,
			string path,
			DebugValue before,
			DebugValue after,
			string operation
		)
		{
			Id = id;
			OccurredAtUtc = at;
			Path = path;
			Before = before;
			After = after;
			Operation = operation;
		}

		public DebugHistoryId Id { get; }
		public DateTimeOffset OccurredAtUtc { get; }
		public string Path { get; }
		public DebugValue Before { get; }
		public DebugValue After { get; }
		public string Operation { get; }
	}

	public sealed class DebugSettingHistoryPage
	{
		internal DebugSettingHistoryPage(DebugPageInfo page, IEnumerable<DebugSettingHistoryEntry> entries)
		{
			Page = page;
			Entries = new ReadOnlyCollection<DebugSettingHistoryEntry>((entries ?? Enumerable.Empty<DebugSettingHistoryEntry>()).ToList());
		}

		public DebugPageInfo Page { get; }
		public IReadOnlyList<DebugSettingHistoryEntry> Entries { get; }
	}

	public static class DebugSetting
	{
		public static DebugSettingListPage GetList(DebugListQuery query)
		{
#if DEXC_DEBUGGER
			return DexcSettingRuntime.Registry.GetList(query);
#else
			return new DebugSettingListPage(DexcDisabledRuntime.CollectionSummary, DexcDisabledRuntime.EmptyPage(query.Page), null);
#endif
		}

		public static bool TryGetDescriptor(DebugSettingId id, out DebugSettingDescriptor descriptor)
		{
#if DEXC_DEBUGGER
			return DexcSettingRuntime.Registry.TryGetDescriptor(id, out descriptor);
#else
			descriptor = null;
			return false;
#endif
		}

		public static DebugResult<DebugValue> Read(DebugSettingId id)
		{
#if DEXC_DEBUGGER
			return DexcSettingRuntime.Registry.Read(id);
#else
			return DexcDisabledRuntime.DisabledSettingRead();
#endif
		}

		public static DebugResult<DebugValue> Write(DebugSettingId id, DebugValue value)
		{
#if DEXC_DEBUGGER
			return DexcSettingRuntime.Registry.Write(id, value);
#else
			return DexcDisabledRuntime.DisabledSettingWrite();
#endif
		}

		/// <summary>Reads a setting and converts it to <typeparamref name="T"/> (bool, numeric, string, enum, Color, Vector types).</summary>
		public static bool TryRead<T>(DebugSettingId id, out T value)
		{
			value = default(T);
#if DEXC_DEBUGGER
			if (!Read(id).TryGetValue(out var raw))
				return false;
			try
			{
				value = (T)DexcSettingRegistry.FromValueForRestore(raw, typeof(T));
				return true;
			}
			catch (Exception)
			{
				return false;
			}
#else
			return false;
#endif
		}

		/// <summary>Writes a typed value; the same validation as <see cref="Write(DebugSettingId, DebugValue)"/> applies.</summary>
		public static DebugResult<DebugValue> Write<T>(DebugSettingId id, T value)
		{
#if DEXC_DEBUGGER
			return Write(id, DexcSettingRegistry.ToValueForWrite(value, typeof(T)));
#else
			return DexcDisabledRuntime.DisabledSettingWrite();
#endif
		}

		public static DebugResult<DebugValue> Reset(DebugSettingId id)
		{
#if DEXC_DEBUGGER
			return DexcSettingRuntime.Registry.Reset(id);
#else
			return DexcDisabledRuntime.DisabledSettingWrite();
#endif
		}

		public static DebugSettingPresetPage GetPresets(DebugSettingSetId setId, DebugPageRequest page)
		{
#if DEXC_DEBUGGER
			return DexcSettingRuntime.Registry.GetPresets(setId, page);
#else
			return new DebugSettingPresetPage(DexcDisabledRuntime.EmptyPage(page), null);
#endif
		}

		public static DebugResult<IReadOnlyList<DebugSettingPresetDifference>> PreviewPreset(DebugSettingPresetId id)
		{
#if DEXC_DEBUGGER
			return DexcSettingRuntime.Registry.PreviewPreset(id);
#else
			return DexcDisabledRuntime.DisabledPresetPreview();
#endif
		}

		public static DebugResult ApplyPreset(DebugSettingPresetId id)
		{
#if DEXC_DEBUGGER
			return DexcSettingRuntime.Registry.ApplyPreset(id);
#else
			return DexcDisabledRuntime.DisabledPreset();
#endif
		}

		public static DebugSettingHistoryPage GetHistory(DebugSettingId id, DebugPageRequest page)
		{
#if DEXC_DEBUGGER
			return DexcSettingRuntime.Registry.GetHistory(id, page);
#else
			return new DebugSettingHistoryPage(DexcDisabledRuntime.EmptyPage(page), null);
#endif
		}
	}

	internal sealed class DexcSettingProperty
	{
		internal bool Persist;
		internal PropertyInfo Property;
		internal DebugSettingDescriptor Public;
		internal object Current;
		internal object Defaults;
		internal DebugRangeAttribute Range;
		internal Type ValueType;
		internal DexcSettingSetDescriptor Set;
	}

	internal sealed class DexcSettingPreset
	{
		internal DebugSettingPresetItem Public;
		internal Dictionary<DebugSettingId, DebugValue> Values;
		internal DexcSettingSetDescriptor Set;
	}

	internal sealed class DexcSettingSetDescriptor
	{
		internal DebugSettingSetId Id;
		internal Type Type;
		internal object Current;
		internal object Defaults;
		internal List<DexcSettingProperty> Properties = new List<DexcSettingProperty>();
		internal List<DexcSettingPreset> Presets = new List<DexcSettingPreset>();
	}

	internal sealed class DexcSettingRegistry
	{
		private readonly DexcRuntimeSession m_Session;
		private readonly Dictionary<DebugSettingId, DexcSettingProperty> m_ById = new Dictionary<DebugSettingId, DexcSettingProperty>();
		private readonly Dictionary<DebugSettingSetId, DexcSettingSetDescriptor> m_BySet =
			new Dictionary<DebugSettingSetId, DexcSettingSetDescriptor>();
		private readonly Dictionary<DebugSettingPresetId, DexcSettingPreset> m_ByPreset =
			new Dictionary<DebugSettingPresetId, DexcSettingPreset>();
		private readonly LinkedList<DebugSettingHistoryEntry> m_History = new LinkedList<DebugSettingHistoryEntry>();
		private long m_NextSetting;
		private long m_NextSet;
		private long m_NextPreset;
		private long m_NextHistory;
		internal DebugCollectionSummary Summary { get; }

		internal IReadOnlyDictionary<string, bool> ReadBooleanPrefix(string prefix)
		{
			var values = new Dictionary<string, bool>(StringComparer.Ordinal);
			foreach (var item in m_ById.Values)
			{
				if (item.ValueType != typeof(bool) || !item.Public.Path.StartsWith(prefix, StringComparison.Ordinal))
				{
					continue;
				}

				var read = Read(item.Public.Id);
				if (read.TryGetValue(out var value) && value.TryGetBoolean(out var visible))
				{
					values.Add(item.Public.Path, visible);
				}
			}

			return values;
		}

		internal DexcSettingRegistry(DexcRuntimeSession session)
		{
			// Definitions come from the assembly-lifetime cache; this session resolves the live instances, assigns the ids
			// and records what the collection had to exclude.
			m_Session = session;
			var definitions = DexcDefinitions.Settings;
			var excluded = definitions.ExcludedCount;
			var errors = definitions.ErrorCount;
			foreach (var diagnostic in definitions.Diagnostics)
				AddCollectionError(diagnostic.Identity, diagnostic.Exception, diagnostic.Code, diagnostic.Message);
			foreach (var definition in definitions.Sets)
			{
				object current;
				object defaults;
				try
				{
					var currentProperty = definition.Type.GetProperty(
						"Current",
						BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy
					);
					current = currentProperty.GetValue(null, null);
					defaults = Activator.CreateInstance(definition.Type);
				}
				catch (Exception exception)
				{
					excluded++;
					errors++;
					AddCollectionError(definition.Type.FullName, exception);
					continue;
				}
				var set = new DexcSettingSetDescriptor
				{
					Id = DebugSettingSetId.Create(session.SessionId, ++m_NextSet),
					Type = definition.Type,
					Current = current,
					Defaults = defaults,
				};
				var idByProperty = new Dictionary<PropertyInfo, DebugSettingId>();
				foreach (var property in definition.Properties)
				{
					var id = DebugSettingId.Create(session.SessionId, ++m_NextSetting);
					idByProperty.Add(property.Property, id);
					var item = new DexcSettingProperty
					{
						Property = property.Property,
						Public = new DebugSettingDescriptor(
							id,
							set.Id,
							property.Attribute.Path,
							property.ValueType,
							property.DefaultValue,
							property.Attribute,
							property.Range,
							property.HasSlider
						),
						Current = current,
						Defaults = defaults,
						Range = property.Range,
						ValueType = property.ValueType,
						Set = set,
						Persist = property.Persist,
					};
					set.Properties.Add(item);
					m_ById.Add(id, item);
				}
				foreach (var preset in definition.Presets)
				{
					var item = new DexcSettingPreset
					{
						Public = new DebugSettingPresetItem(
							DebugSettingPresetId.Create(session.SessionId, ++m_NextPreset),
							preset.Attribute.Id,
							set.Id,
							preset.Attribute.DisplayName,
							preset.Attribute.Description
						),
						Values = preset
							.Values.Where(pair => idByProperty.ContainsKey(pair.Key))
							.ToDictionary(pair => idByProperty[pair.Key], pair => pair.Value),
						Set = set,
					};
					set.Presets.Add(item);
					m_ByPreset.Add(item.Public.Id, item);
				}
				m_BySet.Add(set.Id, set);
			}
			Summary = new DebugCollectionSummary(
				DebugCollectionState.Ready,
				session.SessionId,
				session.CollectionGeneration,
				m_ById.Count,
				excluded,
				errors,
				0
			);
			RestorePersisted();
			SceneManager.sceneUnloaded += OnSceneUnloaded;
		}

		internal static string PersistKey(string path)
		{
			return "Setting/" + path;
		}

		private void RestorePersisted()
		{
			var changed = new Dictionary<DexcSettingSetDescriptor, List<DebugSettingId>>();
			foreach (var item in m_ById.Values)
			{
				if (!item.Persist || item.Public.Access == DebugSettingAccess.ReadOnly)
					continue;
				if (
					!DebugStorage.TryLoad(PersistKey(item.Public.Path), out var text)
					|| !DexcPersistedValue.TryDeserialize(text, out var value)
				)
					continue;
				var issues = new List<DebugValidationIssue>();
				Validate(value, item, issues);
				if (issues.Count != 0)
					continue;
				try
				{
					item.Property.SetValue(item.Current, FromValue(value, item.ValueType), null);
					if (!changed.TryGetValue(item.Set, out var ids))
					{
						ids = new List<DebugSettingId>();
						changed.Add(item.Set, ids);
					}
					ids.Add(item.Public.Id);
				}
				catch (Exception exception)
				{
					AddCollectionError(
						item.Public.Path,
						exception,
						"setting-restore-failed",
						"A persisted setting value could not be restored."
					);
				}
			}
			foreach (var pair in changed)
			{
				try
				{
					InvokeChanged(pair.Key, pair.Value);
				}
				catch (Exception exception)
				{
					AddCollectionError(
						pair.Key.Type.FullName,
						exception,
						"setting-restore-change-failed",
						"A persisted setting restore callback failed."
					);
				}
			}
		}

		/// <summary>Hands a persisted value to storage. Returns whether anything was handed over, so the caller knows to flush.</summary>
		private static bool SavePersisted(DexcSettingProperty item, DebugValue value)
		{
			if (!item.Persist)
				return false;
			DebugStorage.Save(PersistKey(item.Public.Path), DexcPersistedValue.Serialize(value));
			return true;
		}

		internal DebugSettingListPage GetList(DebugListQuery query)
		{
			DexcListQueryEvaluator.ValidatePage(query.Page, m_Session.Options.MaxPageSize);
			var all = m_ById
				.Values.Select(item => new DebugSettingListItem(item.Public))
				.Where(item => DexcListQueryEvaluator.Matches(query, item.Path, item.DisplayName, string.Empty, null))
				.OrderBy(item => item.Path, StringComparer.Ordinal)
				.ToList();
			var values = all.Skip(query.Page.Offset).Take(query.Page.Count).ToList();
			return new DebugSettingListPage(
				Summary,
				new DebugPageInfo(query.Page.Offset, values.Count, query.Page.Offset + values.Count < all.Count, true, all.Count, false),
				values
			);
		}

		internal bool TryGetDescriptor(DebugSettingId id, out DebugSettingDescriptor descriptor)
		{
			if (m_ById.TryGetValue(id, out var item))
			{
				descriptor = item.Public;
				return true;
			}
			descriptor = null;
			return false;
		}

		internal DebugResult<DebugValue> Read(DebugSettingId id)
		{
			if (!m_ById.TryGetValue(id, out var item))
				return new DebugResult<DebugValue>(
					id.IsValid ? DebugOperationStatus.Stale : DebugOperationStatus.NotFound,
					null,
					default(DebugDiagnosticId)
				);
			try
			{
				return new DebugResult<DebugValue>(
					DebugOperationStatus.Succeeded,
					ToValue(item.Property.GetValue(item.Current, null), item.ValueType),
					default(DebugDiagnosticId)
				);
			}
			catch (Exception exception)
			{
				var diagnostic = m_Session.Diagnostics.AddOperation(
					DebugDiagnosticSeverity.Error,
					DebugFeature.Setting,
					"read",
					"setting-read-failed",
					"The setting getter failed.",
					item.Public.Path,
					null,
					exception
				);
				return new DebugResult<DebugValue>(
					DebugOperationStatus.Failed,
					null,
					diagnostic == null ? default(DebugDiagnosticId) : diagnostic.DiagnosticId
				);
			}
		}

		internal DebugResult<DebugValue> Write(DebugSettingId id, DebugValue value)
		{
			return WriteCore(id, value, "Write");
		}

		internal DebugResult<DebugValue> Reset(DebugSettingId id)
		{
			if (!m_ById.TryGetValue(id, out var item))
				return new DebugResult<DebugValue>(
					id.IsValid ? DebugOperationStatus.Stale : DebugOperationStatus.NotFound,
					null,
					null,
					default(DebugDiagnosticId)
				);
			return WriteCore(id, item.Public.DefaultValue, "Reset");
		}

		internal DebugSettingPresetPage GetPresets(DebugSettingSetId id, DebugPageRequest page)
		{
			DexcListQueryEvaluator.ValidatePage(page, m_Session.Options.MaxPageSize);
			if (!m_BySet.TryGetValue(id, out var set))
				return new DebugSettingPresetPage(new DebugPageInfo(page.Offset, 0, false, true, 0, false), null);
			var values = set.Presets.Skip(page.Offset).Take(page.Count).Select(item => item.Public).ToList();
			return new DebugSettingPresetPage(
				new DebugPageInfo(
					page.Offset,
					values.Count,
					page.Offset + values.Count < set.Presets.Count,
					true,
					set.Presets.Count,
					false
				),
				values
			);
		}

		internal DebugResult<IReadOnlyList<DebugSettingPresetDifference>> PreviewPreset(DebugSettingPresetId id)
		{
			if (!m_ByPreset.TryGetValue(id, out var preset))
				return new DebugResult<IReadOnlyList<DebugSettingPresetDifference>>(
					id.IsValid ? DebugOperationStatus.Stale : DebugOperationStatus.NotFound,
					Array.Empty<DebugSettingPresetDifference>(),
					null,
					default(DebugDiagnosticId)
				);
			var differences = new List<DebugSettingPresetDifference>();
			var issues = new List<DebugValidationIssue>();
			foreach (var pair in preset.Values)
			{
				var item = m_ById[pair.Key];
				var current = ToValue(item.Property.GetValue(item.Current, null), item.ValueType);
				if (item.Public.Access == DebugSettingAccess.ReadOnly)
					issues.Add(
						new DebugValidationIssue(
							DebugValidationSeverity.Error,
							"setting-read-only",
							"The preset contains a read-only setting."
						)
					);
				Validate(pair.Value, item, issues);
				if (!current.Equals(pair.Value))
					differences.Add(new DebugSettingPresetDifference(item.Public.Id, item.Public.Path, current, pair.Value));
			}
			return new DebugResult<IReadOnlyList<DebugSettingPresetDifference>>(
				issues.Count == 0 ? DebugOperationStatus.Succeeded : DebugOperationStatus.Rejected,
				differences,
				issues,
				default(DebugDiagnosticId)
			);
		}

		internal DebugResult ApplyPreset(DebugSettingPresetId id)
		{
			if (!m_ByPreset.TryGetValue(id, out var preset))
				return new DebugResult(
					id.IsValid ? DebugOperationStatus.Stale : DebugOperationStatus.NotFound,
					null,
					default(DebugDiagnosticId)
				);
			var preview = PreviewPreset(id);
			if (preview.Status != DebugOperationStatus.Succeeded)
				return new DebugResult(DebugOperationStatus.Rejected, preview.Issues, preview.DiagnosticId);
			var changed = new List<DebugSettingId>();
			var persisted = false;
			try
			{
				foreach (var pair in preset.Values)
				{
					var item = m_ById[pair.Key];
					var before = ToValue(item.Property.GetValue(item.Current, null), item.ValueType);
					item.Property.SetValue(item.Current, FromValue(pair.Value, item.ValueType), null);
					persisted |= SavePersisted(item, pair.Value);
					changed.Add(pair.Key);
					AddHistory(item, "Preset", before, pair.Value);
				}
				// Once for the whole preset: it is one action, and the file store rewrites the whole file on each flush.
				if (persisted)
					DebugStorage.Flush();
				InvokeChanged(preset.Set, changed);
				return new DebugResult(DebugOperationStatus.Succeeded, null, default(DebugDiagnosticId));
			}
			catch (Exception exception)
			{
				var diagnostic = m_Session.Diagnostics.AddOperation(
					DebugDiagnosticSeverity.Error,
					DebugFeature.Setting,
					"preset-apply",
					"setting-preset-failed",
					"The setting preset apply failed.",
					id.ToString(),
					null,
					exception
				);
				return new DebugResult(
					DebugOperationStatus.Failed,
					null,
					diagnostic == null ? default(DebugDiagnosticId) : diagnostic.DiagnosticId
				);
			}
		}

		internal IReadOnlyList<DebugSettingHistoryEntry> GetHistorySnapshot() => m_History.ToArray();

		internal DebugSettingHistoryPage GetHistory(DebugSettingId id, DebugPageRequest page)
		{
			DexcListQueryEvaluator.ValidatePage(page, m_Session.Options.MaxPageSize);
			var path = m_ById.TryGetValue(id, out var item) ? item.Public.Path : string.Empty;
			var values = m_History.Where(entry => entry.Path == path).Reverse().ToList();
			var selected = values.Skip(page.Offset).Take(page.Count).ToList();
			return new DebugSettingHistoryPage(
				new DebugPageInfo(page.Offset, selected.Count, page.Offset + selected.Count < values.Count, true, values.Count, false),
				selected
			);
		}

		private DebugResult<DebugValue> WriteCore(DebugSettingId id, DebugValue value, string operation)
		{
			if (value == null)
				throw new ArgumentNullException(nameof(value));
			if (!m_ById.TryGetValue(id, out var item))
				return new DebugResult<DebugValue>(
					id.IsValid ? DebugOperationStatus.Stale : DebugOperationStatus.NotFound,
					null,
					null,
					default(DebugDiagnosticId)
				);
			if (item.Public.Access == DebugSettingAccess.ReadOnly)
				return new DebugResult<DebugValue>(DebugOperationStatus.ReadOnly, null, null, default(DebugDiagnosticId));
			var issues = new List<DebugValidationIssue>();
			Validate(value, item, issues);
			if (issues.Count != 0)
				return new DebugResult<DebugValue>(DebugOperationStatus.Rejected, null, issues, default(DebugDiagnosticId));
			try
			{
				var before = ToValue(item.Property.GetValue(item.Current, null), item.ValueType);
				item.Property.SetValue(item.Current, FromValue(value, item.ValueType), null);
				// A successful write is the commit point the README promises. Storage may only keep the value in memory until
				// it is flushed - the file store does - and nothing else in a session flushes it, so this is where it lands.
				if (SavePersisted(item, value))
					DebugStorage.Flush();
				InvokeChanged(item.Set, new[] { id });
				AddHistory(item, operation, before, value);
				return new DebugResult<DebugValue>(DebugOperationStatus.Succeeded, value, null, default(DebugDiagnosticId));
			}
			catch (Exception exception)
			{
				var diagnostic = m_Session.Diagnostics.AddOperation(
					DebugDiagnosticSeverity.Error,
					DebugFeature.Setting,
					"write",
					"setting-write-failed",
					"The setting setter or change callback failed.",
					item.Public.Path,
					null,
					exception
				);
				return new DebugResult<DebugValue>(
					DebugOperationStatus.Failed,
					null,
					null,
					diagnostic == null ? default(DebugDiagnosticId) : diagnostic.DiagnosticId
				);
			}
		}

		internal static void ValidateForRestore(DebugValue value, DexcSettingProperty item, List<DebugValidationIssue> issues)
		{
			Validate(value, item, issues);
		}

		internal static DebugValue ToValueForWrite(object value, Type type)
		{
			return ToValue(value, type);
		}

		internal static object FromValueForRestore(DebugValue value, Type type)
		{
			return FromValue(value, type);
		}

		private static void Validate(DebugValue value, DexcSettingProperty item, List<DebugValidationIssue> issues)
		{
			try
			{
				FromValue(value, item.ValueType);
			}
			catch (Exception exception)
			{
				issues.Add(new DebugValidationIssue(DebugValidationSeverity.Error, "setting-type-mismatch", exception.Message));
				return;
			}
			if (item.Range != null)
			{
				var number = ToDouble(value);
				var single = item.ValueType == typeof(float);
				if (
					!number.HasValue
					|| DebugRangeAttribute.IsBelow(number.Value, item.Range.Minimum, single)
					|| DebugRangeAttribute.IsAbove(number.Value, item.Range.Maximum, single)
				)
					issues.Add(
						new DebugValidationIssue(
							DebugValidationSeverity.Error,
							"setting-range",
							"The setting value is outside the configured range."
						)
					);
			}
		}

		private void AddHistory(DexcSettingProperty item, string operation, DebugValue before, DebugValue after)
		{
			var id = DebugHistoryId.Create(m_Session.SessionId, ++m_NextHistory);
			m_History.AddLast(new DebugSettingHistoryEntry(id, DateTimeOffset.UtcNow, item.Public.Path, before, after, operation));
			while (m_History.Count > m_Session.Options.HistoryCapacityPerFeature)
				m_History.RemoveFirst();
		}

		internal void ResetScope(DebugSettingScope scope)
		{
			ResetScopes(new[] { scope });
		}

		internal void EndSession()
		{
			ResetScopes(new[] { DebugSettingScope.Global, DebugSettingScope.Scene, DebugSettingScope.Session });
			SceneManager.sceneUnloaded -= OnSceneUnloaded;
			foreach (var set in m_BySet.Values)
				DexcRoot.Current.SettingSets.Remove(set.Type);
		}

		private void ResetScopes(IEnumerable<DebugSettingScope> scopes)
		{
			var requested = new HashSet<DebugSettingScope>(scopes);
			var changed = new Dictionary<DexcSettingSetDescriptor, List<DebugSettingId>>();
			foreach (var item in m_ById.Values)
			{
				if (!requested.Contains(item.Public.Scope))
					continue;
				try
				{
					var before = ToValue(item.Property.GetValue(item.Current, null), item.ValueType);
					if (before.Equals(item.Public.DefaultValue))
						continue;
					item.Property.SetValue(item.Current, item.Property.GetValue(item.Defaults, null), null);
					AddHistory(item, "ScopeReset", before, item.Public.DefaultValue);
					if (!changed.TryGetValue(item.Set, out var ids))
					{
						ids = new List<DebugSettingId>();
						changed.Add(item.Set, ids);
					}
					ids.Add(item.Public.Id);
				}
				catch (Exception exception)
				{
					AddCollectionError(item.Public.Path, exception, "setting-scope-reset", "A setting scope reset failed.");
				}
			}
			foreach (var pair in changed)
			{
				try
				{
					InvokeChanged(pair.Key, pair.Value);
				}
				catch (Exception exception)
				{
					AddCollectionError(
						pair.Key.Type.FullName,
						exception,
						"setting-scope-change-failed",
						"A setting scope reset callback failed."
					);
				}
			}
		}

		private void OnSceneUnloaded(Scene scene)
		{
			if (m_Session.IsCurrent)
				ResetScope(DebugSettingScope.Scene);
		}

		private void AddCollectionError(string identity, Exception exception)
		{
			AddCollectionError(identity, exception, "invalid-setting-definition", "A setting definition was excluded.");
		}

		private void AddCollectionError(string identity, Exception exception, string code, string message)
		{
			m_Session.Diagnostics.AddCollection(
				DebugDiagnosticSeverity.Error,
				DebugFeature.Setting,
				"reflection-collection",
				code,
				message,
				identity,
				m_Session.CollectionGeneration,
				exception
			);
		}

		private static void InvokeChanged(DexcSettingSetDescriptor set, IEnumerable<DebugSettingId> ids)
		{
			((IDexcSettingSet)set.Current).NotifyChanged(new DebugSettingChanges(ids));
		}

		internal static bool IsSettingSet(Type type)
		{
			return type.IsClass
				&& !type.IsAbstract
				&& type.BaseType != null
				&& type.BaseType.IsGenericType
				&& type.BaseType.GetGenericTypeDefinition() == typeof(DebugSettingSet<>);
		}

		internal static void ValidateSetType(Type type)
		{
			if (type.BaseType.GetGenericArguments()[0] != type)
				throw new InvalidOperationException("Setting set generic argument must match the concrete type.");
			var constructor = type.GetConstructor(BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null);
			if (constructor == null)
				throw new InvalidOperationException("Setting set must have a public parameterless constructor.");
		}

		internal static void ValidateSettingProperty(PropertyInfo property, DebugSettingAttribute attribute)
		{
			if (
				property.GetGetMethod(false) == null
				|| property.GetSetMethod(true) == null
				|| property.GetSetMethod(true).IsPublic
				|| property.GetIndexParameters().Length != 0
				|| !IsSupported(property.PropertyType)
			)
				throw new InvalidOperationException("Setting property is not supported.");
			ValidatePath(attribute.Path);
			if (
				!Enum.IsDefined(typeof(DebugSettingScope), attribute.Scope)
				|| !Enum.IsDefined(typeof(DebugSettingApplyMode), attribute.ApplyMode)
				|| !Enum.IsDefined(typeof(DebugSettingAccess), attribute.Access)
			)
				throw new InvalidOperationException("Setting metadata is invalid.");
		}

		internal static void ValidateConstraint(Type type, DebugRangeAttribute range, bool slider)
		{
			if (range != null && !IsNumeric(type))
				throw new InvalidOperationException("DebugRange requires a numeric setting type.");
			if (slider && (!IsNumeric(type) || range == null))
				throw new InvalidOperationException("DebugSlider requires a numeric setting with a range.");
		}

		private static void ValidatePath(string path)
		{
			if (
				string.IsNullOrWhiteSpace(path)
				|| path.Split('/').Any(segment => string.IsNullOrWhiteSpace(segment) || segment.Trim() != segment)
			)
				throw new InvalidOperationException("Setting path must contain non-empty, trimmed segments.");
		}

		private static bool IsNumeric(Type type)
		{
			return type == typeof(sbyte)
				|| type == typeof(byte)
				|| type == typeof(short)
				|| type == typeof(ushort)
				|| type == typeof(int)
				|| type == typeof(uint)
				|| type == typeof(long)
				|| type == typeof(ulong)
				|| type == typeof(float)
				|| type == typeof(double);
		}

		internal static bool ReferencesRuntime(Assembly assembly)
		{
			try
			{
				return assembly == typeof(DexcRuntimeAssemblyMarker).Assembly
					|| assembly
						.GetReferencedAssemblies()
						.Any(reference => reference.Name == typeof(DexcRuntimeAssemblyMarker).Assembly.GetName().Name);
			}
			catch
			{
				return false;
			}
		}

		private static bool IsSupported(Type type)
		{
			return type == typeof(bool)
				|| IsNumeric(type)
				|| type == typeof(string)
				|| type.IsEnum
				|| type == typeof(Color)
				|| type == typeof(Vector2)
				|| type == typeof(Vector3)
				|| type == typeof(Vector4)
				|| type == typeof(Vector2Int)
				|| type == typeof(Vector3Int);
		}

		internal static DebugValue ToValue(object value, Type type)
		{
			if (type == typeof(bool))
				return DebugValue.From((bool)value);
			if (type == typeof(string))
				return DebugValue.From((string)value);
			if (type == typeof(sbyte))
				return DebugValue.From((sbyte)value);
			if (type == typeof(byte))
				return DebugValue.From((byte)value);
			if (type == typeof(short))
				return DebugValue.From((short)value);
			if (type == typeof(ushort))
				return DebugValue.From((ushort)value);
			if (type == typeof(int))
				return DebugValue.From((int)value);
			if (type == typeof(uint))
				return DebugValue.From((uint)value);
			if (type == typeof(long))
				return DebugValue.From((long)value);
			if (type == typeof(ulong))
				return DebugValue.From((ulong)value);
			if (type == typeof(float))
				return DebugValue.From((float)value);
			if (type == typeof(double))
				return DebugValue.From((double)value);
			if (type.IsEnum)
			{
				var name = Enum.GetName(type, value);
				if (name == null)
					throw new InvalidOperationException("Enum value is not defined.");
				return DebugValue.From(name);
			}
			if (type == typeof(Color))
			{
				var c = (Color)value;
				return DebugValue.FromObject(
					new DebugValueMember("r", DebugValue.From(c.r)),
					new DebugValueMember("g", DebugValue.From(c.g)),
					new DebugValueMember("b", DebugValue.From(c.b)),
					new DebugValueMember("a", DebugValue.From(c.a))
				);
			}
			if (type == typeof(Vector2))
			{
				var v = (Vector2)value;
				return DebugValue.FromObject(
					new DebugValueMember("x", DebugValue.From(v.x)),
					new DebugValueMember("y", DebugValue.From(v.y))
				);
			}
			if (type == typeof(Vector3))
			{
				var v = (Vector3)value;
				return DebugValue.FromObject(
					new DebugValueMember("x", DebugValue.From(v.x)),
					new DebugValueMember("y", DebugValue.From(v.y)),
					new DebugValueMember("z", DebugValue.From(v.z))
				);
			}
			if (type == typeof(Vector4))
			{
				var v = (Vector4)value;
				return DebugValue.FromObject(
					new DebugValueMember("x", DebugValue.From(v.x)),
					new DebugValueMember("y", DebugValue.From(v.y)),
					new DebugValueMember("z", DebugValue.From(v.z)),
					new DebugValueMember("w", DebugValue.From(v.w))
				);
			}
			if (type == typeof(Vector2Int))
			{
				var v = (Vector2Int)value;
				return DebugValue.FromObject(
					new DebugValueMember("x", DebugValue.From(v.x)),
					new DebugValueMember("y", DebugValue.From(v.y))
				);
			}
			if (type == typeof(Vector3Int))
			{
				var v = (Vector3Int)value;
				return DebugValue.FromObject(
					new DebugValueMember("x", DebugValue.From(v.x)),
					new DebugValueMember("y", DebugValue.From(v.y)),
					new DebugValueMember("z", DebugValue.From(v.z))
				);
			}
			throw new InvalidOperationException("Unsupported setting type.");
		}

		private static object FromValue(DebugValue value, Type type)
		{
			if (type == typeof(bool) && value.TryGetBoolean(out var boolean))
				return boolean;
			if (type == typeof(string) && value.TryGetString(out var text))
				return text;
			if (type == typeof(sbyte))
				return ConvertSigned(value, sbyte.MinValue, sbyte.MaxValue, number => (sbyte)number);
			if (type == typeof(short))
				return ConvertSigned(value, short.MinValue, short.MaxValue, number => (short)number);
			if (type == typeof(int))
				return ConvertSigned(value, int.MinValue, int.MaxValue, number => (int)number);
			if (type == typeof(long))
				return ConvertSigned(value, long.MinValue, long.MaxValue, number => number);
			if (type == typeof(byte))
				return ConvertUnsigned(value, byte.MaxValue, number => (byte)number);
			if (type == typeof(ushort))
				return ConvertUnsigned(value, ushort.MaxValue, number => (ushort)number);
			if (type == typeof(uint))
				return ConvertUnsigned(value, uint.MaxValue, number => (uint)number);
			if (type == typeof(ulong))
				return ConvertUnsigned(value, ulong.MaxValue, number => number);
			if ((type == typeof(float) || type == typeof(double)) && value.TryGetFloatingPoint(out var floating))
				return type == typeof(float) ? (object)ToSingle(floating) : floating;
			if (type.IsEnum)
			{
				if (!value.TryGetString(out var enumKey) || string.IsNullOrEmpty(enumKey))
					throw new InvalidOperationException("Enum value must be a String key.");
				if (!Enum.IsDefined(type, enumKey))
					throw new InvalidOperationException("Enum key is not declared.");
				return Enum.Parse(type, enumKey, false);
			}
			IReadOnlyList<DebugValueMember> members;
			if (!value.TryGetObject(out members))
				throw new InvalidOperationException("Expected object value.");
			if (type == typeof(Color))
				return new Color(
					FloatMember(members, "r"),
					FloatMember(members, "g"),
					FloatMember(members, "b"),
					FloatMember(members, "a")
				);
			if (type == typeof(Vector2))
				return new Vector2(FloatMember(members, "x"), FloatMember(members, "y"));
			if (type == typeof(Vector3))
				return new Vector3(FloatMember(members, "x"), FloatMember(members, "y"), FloatMember(members, "z"));
			if (type == typeof(Vector4))
				return new Vector4(
					FloatMember(members, "x"),
					FloatMember(members, "y"),
					FloatMember(members, "z"),
					FloatMember(members, "w")
				);
			if (type == typeof(Vector2Int))
				return new Vector2Int(IntegerMember(members, "x"), IntegerMember(members, "y"));
			if (type == typeof(Vector3Int))
				return new Vector3Int(IntegerMember(members, "x"), IntegerMember(members, "y"), IntegerMember(members, "z"));
			throw new InvalidOperationException("Unsupported setting type.");
		}

		private static object ConvertSigned(DebugValue value, long minimum, long maximum, Func<long, object> convert)
		{
			if (!value.TryGetSignedInteger(out var number))
				throw new InvalidOperationException("Expected signed integer.");
			if (number < minimum || number > maximum)
				throw new InvalidOperationException("Integer is outside the target type range.");
			return convert(number);
		}

		private static object ConvertUnsigned(DebugValue value, ulong maximum, Func<ulong, object> convert)
		{
			if (!value.TryGetUnsignedInteger(out var number))
				throw new InvalidOperationException("Expected unsigned integer.");
			if (number > maximum)
				throw new InvalidOperationException("Unsigned integer is outside the target type range.");
			return convert(number);
		}

		private static int IntegerMember(IReadOnlyList<DebugValueMember> members, string name)
		{
			var item = members.SingleOrDefault(member => member.Name == name);
			if (item == null || !item.Value.TryGetSignedInteger(out var number))
			{
				throw new InvalidOperationException("Expected signed integer member: " + name);
			}
			if (number < int.MinValue || number > int.MaxValue)
			{
				throw new InvalidOperationException("Integer member is outside the Int32 range: " + name);
			}
			return (int)number;
		}

		private static double Member(IReadOnlyList<DebugValueMember> members, string name)
		{
			var item = members.SingleOrDefault(member => member.Name == name);
			if (item == null || !item.Value.TryGetFloatingPoint(out var number))
				throw new InvalidOperationException("Missing numeric member.");
			return number;
		}

		private static float FloatMember(IReadOnlyList<DebugValueMember> members, string name) => ToSingle(Member(members, name));

		/// <summary>
		/// A double past the float range would be stored as Infinity, which cannot be read back out as a value, so the
		/// setting could neither be read nor reset afterwards.
		/// </summary>
		private static float ToSingle(double number)
		{
			if (number < float.MinValue || number > float.MaxValue)
				throw new InvalidOperationException("Value is outside the float range.");
			return (float)number;
		}

		private static double? ToDouble(DebugValue value)
		{
			if (value.TryGetFloatingPoint(out var floating))
				return floating;
			if (value.TryGetSignedInteger(out var signed))
				return signed;
			if (value.TryGetUnsignedInteger(out var unsigned))
				return unsigned;
			return null;
		}
	}

	/// <summary>Restores persisted values for one setting set instance without a registry (used when Current is created).</summary>
	internal static class DexcSettingPersistence
	{
		internal static void Restore(object instance)
		{
#if DEXC_DEBUGGER
			var type = instance.GetType();
			var setPersistent = type.GetCustomAttributes(typeof(DebugPersistentAttribute), false).Length != 0;
			foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
			{
				var attribute = property
					.GetCustomAttributes(typeof(DebugSettingAttribute), false)
					.OfType<DebugSettingAttribute>()
					.SingleOrDefault();
				if (attribute == null || !(attribute.Persist || setPersistent) || attribute.Access == DebugSettingAccess.ReadOnly)
					continue;
				if (
					property.GetSetMethod(true) == null
					|| !DebugStorage.TryLoad(DexcSettingRegistry.PersistKey(attribute.Path), out var text)
					|| !DexcPersistedValue.TryDeserialize(text, out var value)
				)
					continue;
				try
				{
					var range = property
						.GetCustomAttributes(typeof(DebugRangeAttribute), false)
						.OfType<DebugRangeAttribute>()
						.SingleOrDefault();
					var item = new DexcSettingProperty { Range = range, ValueType = property.PropertyType };
					var issues = new List<DebugValidationIssue>();
					DexcSettingRegistry.ValidateForRestore(value, item, issues);
					if (issues.Count != 0)
						continue;
					property.SetValue(instance, DexcSettingRegistry.FromValueForRestore(value, property.PropertyType), null);
				}
				catch (Exception exception)
				{
					UnityEngine.Debug.LogWarning("Dexc persisted setting restore failed for " + attribute.Path + ": " + exception.Message);
				}
			}
#endif
		}
	}

	internal static class DexcSettingRuntime
	{
		// The state is the input, for the same reason as the command side.
		internal static DexcSettingRegistry RegistryOf(DexcRuntimeSession state)
		{
			return state.Settings ?? (state.Settings = new DexcSettingRegistry(state));
		}

		// The public facade always means the running session.
		internal static DexcSettingRegistry Registry => RegistryOf(Get());

		internal static void EndSession(DexcRuntimeSession session)
		{
			if (session != null && session.Settings != null)
				session.Settings.EndSession();
		}

		private static DexcRuntimeSession Get()
		{
			var session = DexcDebugger.CurrentSession;
			if (session == null)
				throw new InvalidOperationException("DexcDebugger must be initialized.");
			return session;
		}
	}
}
