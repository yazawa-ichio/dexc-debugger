using System;
using System.Collections;
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
	public enum DebugInspectorEntryOrigin
	{
		Manual,
		Source,
		Scene,
	}

	public enum DebugFieldAccess
	{
		Default,
		ReadOnly,
		Locked,
	}

	public enum DebugInspectorSnapshotState
	{
		Value,
		UnityReference,
		Unsupported,
		ReadFailure,
		Missing,
	}

	public enum DebugInspectorWriteAccess
	{
		RegisteredOnly,
		AllowUnregistered,
	}

	public enum DebugInspectorWatchState
	{
		Active,
		Missing,
		Unsupported,
		ReadFailure,
	}

	public enum DebugInspectorSearchState
	{
		Searching,
		Completed,
		Cancelled,
		Failed,
	}

	[AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
	[Conditional("DEXC_DEBUGGER")]
	public sealed class DebugFieldAttribute : PreserveAttribute
	{
		public DebugFieldAttribute()
		{
			Access = DebugFieldAccess.Default;
			DisplayName = string.Empty;
		}

		public DebugFieldAccess Access { get; set; }
		public string DisplayName { get; set; }
	}

	[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
	[Conditional("DEXC_DEBUGGER")]
	public sealed class DebugInspectorSourceAttribute : PreserveAttribute
	{
		public DebugInspectorSourceAttribute(string path)
		{
			if (string.IsNullOrWhiteSpace(path))
				throw new ArgumentException("Inspector source path must not be empty.", nameof(path));
			Path = path;
			Key = string.Empty;
		}

		public string Path { get; }
		public string Key { get; set; }
	}

	public sealed class DebugSourceEntry<T> : IDexcInspectorSourceEntry
		where T : UnityEngine.Object
	{
		public DebugSourceEntry(T target, string label, string path = null)
		{
			Target = target;
			Label = label ?? string.Empty;
			Path = path ?? string.Empty;
		}

		public T Target { get; }
		public string Label { get; }
		public string Path { get; }
		UnityEngine.Object IDexcInspectorSourceEntry.Target
		{
			get { return Target; }
		}
		string IDexcInspectorSourceEntry.Label
		{
			get { return Label; }
		}
		string IDexcInspectorSourceEntry.Path
		{
			get { return Path; }
		}
	}

	public sealed class DebugInspectorRegisteredEntry
	{
		internal DebugInspectorRegisteredEntry(
			DebugInspectorTargetHandle target,
			ulong entityId,
			string path,
			string label,
			DebugInspectorEntryOrigin origin,
			DebugSceneHandle scene,
			string sceneName,
			string hierarchyPath,
			bool alive
		)
		{
			Target = target;
			EntityId = entityId;
			Path = path;
			Label = label;
			Origin = origin;
			Scene = scene;
			SceneName = sceneName;
			HierarchyPath = hierarchyPath;
			Alive = alive;
		}

		public DebugInspectorTargetHandle Target { get; }

		/// <summary>
		/// The raw value behind <see cref="UnityEngine.Object.GetEntityId"/>. A handle is only meaningful inside the process,
		/// so this is what a caller outside it names the object by; it is unique within one run and not across runs.
		///
		/// It is a number here rather than Unity's own <c>EntityId</c> because that type changed width between 6000.3 and
		/// 6000.6. What Dexc hands out does not follow that.
		/// </summary>
		public ulong EntityId { get; }
		public string Path { get; }
		public string Label { get; }
		public DebugInspectorEntryOrigin Origin { get; }
		public DebugSceneHandle Scene { get; }
		public string SceneName { get; }
		public string HierarchyPath { get; }
		public bool Alive { get; }
	}

	public sealed class DebugInspectorRegisteredPage
	{
		internal DebugInspectorRegisteredPage(DebugPageInfo page, IEnumerable<DebugInspectorRegisteredEntry> entries)
		{
			Page = page;
			Entries = new ReadOnlyCollection<DebugInspectorRegisteredEntry>(
				(entries ?? Enumerable.Empty<DebugInspectorRegisteredEntry>()).ToList()
			);
		}

		public DebugPageInfo Page { get; }
		public IReadOnlyList<DebugInspectorRegisteredEntry> Entries { get; }
	}

	public sealed class DebugInspectorSceneEntry
	{
		internal DebugInspectorSceneEntry(DebugSceneHandle scene, string name, string path, bool isLoaded)
		{
			Scene = scene;
			Name = name;
			Path = path;
			IsLoaded = isLoaded;
		}

		public DebugSceneHandle Scene { get; }
		public string Name { get; }
		public string Path { get; }
		public bool IsLoaded { get; }
	}

	public sealed class DebugInspectorSceneList
	{
		internal DebugInspectorSceneList(IEnumerable<DebugInspectorSceneEntry> scenes)
		{
			Scenes = new ReadOnlyCollection<DebugInspectorSceneEntry>((scenes ?? Enumerable.Empty<DebugInspectorSceneEntry>()).ToList());
		}

		public IReadOnlyList<DebugInspectorSceneEntry> Scenes { get; }
		public IReadOnlyList<DebugInspectorSceneEntry> Items
		{
			get { return Scenes; }
		}
	}

	public sealed class DebugInspectorHierarchyEntry
	{
		internal DebugInspectorHierarchyEntry(
			DebugObjectHandle target,
			DebugInspectorTargetHandle inspectorTarget,
			ulong entityId,
			DebugSceneHandle scene,
			string label,
			string path,
			bool active,
			bool hasChildren
		)
		{
			Target = target;
			InspectorTarget = inspectorTarget;
			EntityId = entityId;
			Scene = scene;
			Label = label;
			Path = path;
			Active = active;
			HasChildren = hasChildren;
		}

		public DebugObjectHandle Target { get; }
		public DebugInspectorTargetHandle InspectorTarget { get; }

		/// <summary>See <see cref="DebugInspectorRegisteredEntry.EntityId"/>.</summary>
		public ulong EntityId { get; }
		public DebugSceneHandle Scene { get; }
		public string Label { get; }
		public string Path { get; }
		public bool Active { get; }
		public bool HasChildren { get; }
	}

	public sealed class DebugInspectorHierarchyPage
	{
		internal DebugInspectorHierarchyPage(DebugPageInfo page, IEnumerable<DebugInspectorHierarchyEntry> entries)
		{
			Page = page;
			Entries = new ReadOnlyCollection<DebugInspectorHierarchyEntry>(
				(entries ?? Enumerable.Empty<DebugInspectorHierarchyEntry>()).ToList()
			);
		}

		public DebugPageInfo Page { get; }
		public IReadOnlyList<DebugInspectorHierarchyEntry> Entries { get; }
	}

	public sealed class DebugInspectorSearchQuery
	{
		public DebugInspectorSearchQuery(DebugSceneHandle scene, string name, string componentType, bool? active)
		{
			if (!scene.IsValid)
				throw new ArgumentException("Scene handle must be valid.", nameof(scene));
			Scene = scene;
			Name = name ?? string.Empty;
			ComponentType = componentType ?? string.Empty;
			Active = active;
		}

		public DebugSceneHandle Scene { get; }
		public string Name { get; }
		public string ComponentType { get; }
		public bool? Active { get; }
	}

	public sealed class DebugInspectorSearchEntry
	{
		internal DebugInspectorSearchEntry(
			DebugInspectorTargetHandle target,
			DebugSceneHandle scene,
			string path,
			string name,
			string componentType
		)
		{
			Target = target;
			Scene = scene;
			Path = path;
			Name = name;
			ComponentType = componentType;
		}

		public DebugInspectorTargetHandle Target { get; }
		public DebugSceneHandle Scene { get; }
		public string Path { get; }
		public string Name { get; }
		public string ComponentType { get; }
	}

	public sealed class DebugInspectorSearchPage
	{
		internal DebugInspectorSearchPage(
			DebugInspectorSearchState state,
			DebugPageInfo page,
			int inspectedCount,
			bool totalKnown,
			bool truncated,
			IEnumerable<DebugInspectorSearchEntry> entries,
			DebugDiagnosticId diagnostic
		)
		{
			State = state;
			Page = page;
			InspectedCount = inspectedCount;
			IsTotalCountKnown = totalKnown;
			IsTruncated = truncated;
			Entries = new ReadOnlyCollection<DebugInspectorSearchEntry>(
				(entries ?? Enumerable.Empty<DebugInspectorSearchEntry>()).ToList()
			);
			DiagnosticId = diagnostic;
		}

		public DebugInspectorSearchState State { get; }
		public DebugPageInfo Page { get; }
		public int InspectedCount { get; }
		public bool IsTotalCountKnown { get; }
		public bool IsTruncated { get; }
		public IReadOnlyList<DebugInspectorSearchEntry> Entries { get; }
		public DebugDiagnosticId DiagnosticId { get; }
	}

	public sealed class DebugInspectorComponentEntry
	{
		internal DebugInspectorComponentEntry(
			DebugInspectorComponentHandle component,
			string typeName,
			string displayName,
			bool hasRegisteredFields,
			bool alive,
			DebugDiagnosticId diagnostic
		)
		{
			Component = component;
			TypeName = typeName;
			DisplayName = displayName;
			HasRegisteredFields = hasRegisteredFields;
			Alive = alive;
			DiagnosticId = diagnostic;
		}

		public DebugInspectorComponentHandle Component { get; }
		public DebugInspectorComponentHandle Handle
		{
			get { return Component; }
		}
		public string TypeName { get; }
		public string DisplayName { get; }
		public bool HasRegisteredFields { get; }
		public bool Alive { get; }
		public DebugDiagnosticId DiagnosticId { get; }
	}

	public sealed class DebugInspectorComponentPage
	{
		internal DebugInspectorComponentPage(DebugPageInfo page, IEnumerable<DebugInspectorComponentEntry> components)
		{
			Page = page;
			Components = new ReadOnlyCollection<DebugInspectorComponentEntry>(
				(components ?? Enumerable.Empty<DebugInspectorComponentEntry>()).ToList()
			);
		}

		public DebugPageInfo Page { get; }
		public IReadOnlyList<DebugInspectorComponentEntry> Components { get; }
		public IReadOnlyList<DebugInspectorComponentEntry> Entries
		{
			get { return Components; }
		}
	}

	public sealed class DebugInspectorFieldDescriptor
	{
		internal DebugInspectorFieldDescriptor(
			DebugInspectorFieldId id,
			string name,
			string displayName,
			string typeName,
			bool registered,
			DebugFieldAccess access,
			bool canWrite,
			bool isCollection
		)
		{
			Id = id;
			Name = name;
			DisplayName = displayName;
			TypeName = typeName;
			IsRegistered = registered;
			Access = access;
			CanWrite = canWrite;
			IsCollection = isCollection;
		}

		public DebugInspectorFieldId Id { get; }
		public string Name { get; }
		public string DisplayName { get; }
		public string TypeName { get; }
		public bool IsRegistered { get; }
		public DebugFieldAccess Access { get; }
		public bool CanWrite { get; }
		public bool IsCollection { get; }
	}

	public sealed class DebugInspectorFieldPage
	{
		internal DebugInspectorFieldPage(DebugPageInfo page, IEnumerable<DebugInspectorFieldDescriptor> fields)
		{
			Page = page;
			Fields = new ReadOnlyCollection<DebugInspectorFieldDescriptor>(
				(fields ?? Enumerable.Empty<DebugInspectorFieldDescriptor>()).ToList()
			);
		}

		public DebugPageInfo Page { get; }
		public IReadOnlyList<DebugInspectorFieldDescriptor> Fields { get; }
		public IReadOnlyList<DebugInspectorFieldDescriptor> Entries
		{
			get { return Fields; }
		}
	}

	public sealed class InspectorValueSnapshot
	{
		internal InspectorValueSnapshot(
			DebugInspectorSnapshotState state,
			DebugValue value,
			DebugInspectorTargetHandle reference,
			string referenceType,
			string referenceLabel,
			string typeName,
			DebugDiagnosticId diagnostic,
			string error
		)
		{
			State = state;
			Value = value;
			Reference = reference;
			ReferenceType = referenceType ?? string.Empty;
			ReferenceLabel = referenceLabel ?? string.Empty;
			TypeName = typeName ?? string.Empty;
			DiagnosticId = diagnostic;
			Error = error ?? string.Empty;
		}

		public DebugInspectorSnapshotState State { get; }
		public DebugValue Value { get; }
		public DebugInspectorTargetHandle Reference { get; }
		public string ReferenceType { get; }
		public string ReferenceLabel { get; }
		public string TypeName { get; }
		public DebugDiagnosticId DiagnosticId { get; }
		public string Error { get; }
	}

	public sealed class DebugInspectorFieldValue
	{
		internal DebugInspectorFieldValue(DebugInspectorFieldId id, InspectorValueSnapshot snapshot)
		{
			Field = id;
			Snapshot = snapshot;
		}

		public DebugInspectorFieldId Field { get; }
		public DebugInspectorFieldId Id
		{
			get { return Field; }
		}
		public InspectorValueSnapshot Snapshot { get; }
	}

	public sealed class DebugInspectorWriteValue
	{
		private DebugInspectorWriteValue(DebugValue value, DebugInspectorTargetHandle reference, bool nullReference)
		{
			Value = value;
			Reference = reference;
			IsNullReference = nullReference;
		}

		public DebugValue Value { get; }
		public DebugInspectorTargetHandle Reference { get; }
		public bool IsReference
		{
			get { return Reference.IsValid || IsNullReference; }
		}
		public bool IsNullReference { get; }

		public static DebugInspectorWriteValue From(DebugValue value)
		{
			return new DebugInspectorWriteValue(
				value ?? throw new ArgumentNullException(nameof(value)),
				default(DebugInspectorTargetHandle),
				false
			);
		}

		public static DebugInspectorWriteValue FromReference(DebugInspectorTargetHandle target)
		{
			if (!target.IsValid)
				throw new ArgumentException("Reference handle must be valid.", nameof(target));
			return new DebugInspectorWriteValue(null, target, false);
		}

		public static DebugInspectorWriteValue NullReference
		{
			get { return new DebugInspectorWriteValue(null, default(DebugInspectorTargetHandle), true); }
		}
	}

	public sealed class DebugInspectorWatchEntry
	{
		internal DebugInspectorWatchEntry(
			DebugInspectorWatchId id,
			DebugInspectorTargetHandle target,
			DebugInspectorComponentHandle component,
			DebugInspectorFieldId field,
			TimeSpan interval,
			DebugInspectorWatchState state,
			InspectorValueSnapshot snapshot
		)
		{
			WatchId = id;
			Target = target;
			Component = component;
			Field = field;
			Interval = interval;
			State = state;
			Snapshot = snapshot;
		}

		public DebugInspectorWatchId WatchId { get; }
		public DebugInspectorTargetHandle Target { get; }
		public DebugInspectorComponentHandle Component { get; }
		public DebugInspectorFieldId Field { get; }
		public TimeSpan Interval { get; }
		public DebugInspectorWatchState State { get; }
		public InspectorValueSnapshot Snapshot { get; }
	}

	public sealed class DebugInspectorWatchPage
	{
		internal DebugInspectorWatchPage(DebugPageInfo page, IEnumerable<DebugInspectorWatchEntry> watches)
		{
			Page = page;
			Watches = new ReadOnlyCollection<DebugInspectorWatchEntry>((watches ?? Enumerable.Empty<DebugInspectorWatchEntry>()).ToList());
		}

		public DebugPageInfo Page { get; }
		public IReadOnlyList<DebugInspectorWatchEntry> Watches { get; }
		public IReadOnlyList<DebugInspectorWatchEntry> Entries
		{
			get { return Watches; }
		}
	}

	public sealed class DebugInspectorHistoryEntry
	{
		internal DebugInspectorHistoryEntry(
			DebugHistoryId id,
			DebugInspectorTargetHandle target,
			string label,
			string component,
			string field,
			InspectorValueSnapshot before,
			InspectorValueSnapshot after,
			DebugOperationStatus status,
			DateTimeOffset occurredAtUtc
		)
		{
			Id = id;
			Target = target;
			Label = label;
			ComponentType = component;
			FieldName = field;
			Before = before;
			After = after;
			Status = status;
			OccurredAtUtc = occurredAtUtc;
		}

		public DebugHistoryId Id { get; }
		public DebugInspectorTargetHandle Target { get; }
		public string Label { get; }
		public string ComponentType { get; }
		public string FieldName { get; }
		public InspectorValueSnapshot Before { get; }
		public InspectorValueSnapshot After { get; }
		public DebugOperationStatus Status { get; }
		public DateTimeOffset OccurredAtUtc { get; }

		/// <summary>
		/// The component written. The target is the GameObject, and two components on it can have a field of the same name,
		/// or be two of the same type, so neither the target nor the type name tells their histories apart.
		/// </summary>
		internal DebugInspectorComponentHandle Component { get; set; }
	}

	public sealed class DebugInspectorHistoryPage
	{
		internal DebugInspectorHistoryPage(DebugPageInfo page, IEnumerable<DebugInspectorHistoryEntry> entries)
		{
			Page = page;
			Entries = new ReadOnlyCollection<DebugInspectorHistoryEntry>(
				(entries ?? Enumerable.Empty<DebugInspectorHistoryEntry>()).ToList()
			);
		}

		public DebugPageInfo Page { get; }
		public IReadOnlyList<DebugInspectorHistoryEntry> Entries { get; }
	}

	public static class DebugInspector
	{
		[Conditional("DEXC_DEBUGGER")]
		public static void Register(string path, UnityEngine.Object target, string label)
		{
			// Without a session there is nothing to list the target in; registering then does nothing.
			if (DexcDebugger.CurrentSession != null)
				DexcInspectorRuntime.Registry.Register(path, target, label);
		}

		[Conditional("DEXC_DEBUGGER")]
		public static void Unregister(string path, UnityEngine.Object target)
		{
			// Games unregister from OnDestroy, which also runs while Play Mode tears the scene down. Unregistering needs
			// no polling, so it must not create the driver then; with no registry yet there is nothing to remove.
			DexcInspectorRuntime.ExistingRegistry?.Unregister(path, target);
		}

		public static DebugInspectorRegisteredPage GetRegistered(DebugListQuery query)
		{
#if DEXC_DEBUGGER
			return DexcInspectorRuntime.Registry.GetRegistered(query);
#else
			return new DebugInspectorRegisteredPage(DexcDisabledRuntime.EmptyPage(query.Page), null);
#endif
		}

		public static DebugInspectorSceneList GetScenes()
		{
#if DEXC_DEBUGGER
			return DexcInspectorRuntime.Registry.GetScenes();
#else
			return new DebugInspectorSceneList(null);
#endif
		}

		/// <summary>
		/// Reads the value that names an object outside the process.
		///
		/// GetInstanceID went obsolete in Unity 6000.6, and so did every conversion between EntityId and int, because Unity
		/// says an EntityId will not fit an int for much longer. GetEntityId exists as far back as 6000.3, which is this
		/// package's floor, and is obsolete in neither - so the call is the same everywhere and only reading the number out
		/// of it differs. This is the one place that difference lives.
		/// </summary>
		internal static ulong EntityValue(UnityEngine.Object target)
		{
#if UNITY_6000_6_OR_NEWER
			return EntityId.ToULong(target.GetEntityId());
#else
			// ToULong arrived with the 64-bit EntityId; before it the value is 32 bits and int is how you reach it. Verified
			// absent on 6000.3.20f1 and present on 6000.6.1f1 - the boundary between the two is assumed, not measured, so a
			// project on 6000.4 or 6000.5 is where this would first be wrong.
			return unchecked((ulong)(uint)(int)target.GetEntityId());
#endif
		}

		/// <summary>
		/// Finds the object an entity id stands for, so a caller holding only the id - one outside the process, which
		/// cannot hold a handle - can ask about it. The id comes from a listing and is only good for the run that gave it.
		/// </summary>
		public static bool TryGetTarget(ulong entityId, out DebugInspectorTargetHandle target)
		{
#if DEXC_DEBUGGER
			if (DexcInspectorRuntime.Registry.TryFindByEntity(entityId, out var item))
			{
				target = item.Handle;
				return true;
			}
#endif
			target = default(DebugInspectorTargetHandle);
			return false;
		}

		/// <summary>The same object as <see cref="TryGetTarget"/>, as the handle a hierarchy query takes for its parent.</summary>
		public static bool TryGetObject(ulong entityId, out DebugObjectHandle target)
		{
#if DEXC_DEBUGGER
			if (DexcInspectorRuntime.Registry.TryFindByEntity(entityId, out var item))
			{
				target = item.ObjectHandle;
				return true;
			}
#endif
			target = default(DebugObjectHandle);
			return false;
		}

		public static DebugInspectorHierarchyPage GetHierarchyChildren(DebugHierarchyChildrenQuery query)
		{
#if DEXC_DEBUGGER
			return DexcInspectorRuntime.Registry.GetHierarchyChildren(query);
#else
			return new DebugInspectorHierarchyPage(DexcDisabledRuntime.EmptyPage(query.Page), null);
#endif
		}

		public static DebugResult<DebugInspectorSearchId> StartSearch(DebugInspectorSearchQuery query)
		{
#if DEXC_DEBUGGER
			return DexcInspectorRuntime.Registry.StartSearch(query);
#else
			return DexcDisabledRuntime.DisabledSearchStart();
#endif
		}

		public static DebugInspectorSearchPage GetSearchResults(DebugInspectorSearchId id, DebugPageRequest page)
		{
#if DEXC_DEBUGGER
			return DexcInspectorRuntime.Registry.GetSearchResults(id, page);
#else
			return DexcDisabledRuntime.DisabledSearchPage(page);
#endif
		}

		[Conditional("DEXC_DEBUGGER")]
		public static void CancelSearch(DebugInspectorSearchId id)
		{
			DexcInspectorRuntime.Registry.CancelSearch(id);
		}

		public static DebugInspectorComponentPage GetComponents(DebugInspectorTargetHandle target)
		{
#if DEXC_DEBUGGER
			return DexcInspectorRuntime.Registry.GetComponents(target);
#else
			return new DebugInspectorComponentPage(DexcDisabledRuntime.EmptyPage(new DebugPageRequest(0, 1)), null);
#endif
		}

		public static DebugInspectorWatchPage GetWatches(DebugPageRequest page)
		{
#if DEXC_DEBUGGER
			return DexcInspectorRuntime.Registry.GetWatches(page);
#else
			return new DebugInspectorWatchPage(DexcDisabledRuntime.EmptyPage(page), null);
#endif
		}

		[Conditional("DEXC_DEBUGGER")]
		public static void RemoveWatch(DebugInspectorWatchId id)
		{
			DexcInspectorRuntime.Registry.RemoveWatch(id);
		}
	}

	public static class DebugInspectorComponentHandleExtensions
	{
		public static DebugInspectorFieldPage GetFields(this DebugInspectorComponentHandle handle, DebugPageRequest page)
		{
#if DEXC_DEBUGGER
			return DexcInspectorRuntime.Registry.GetFields(handle, page);
#else
			return new DebugInspectorFieldPage(DexcDisabledRuntime.EmptyPage(page), null);
#endif
		}

		public static DebugResult<IReadOnlyList<DebugInspectorFieldValue>> ReadFields(
			this DebugInspectorComponentHandle handle,
			IReadOnlyList<DebugInspectorFieldId> fields
		)
		{
#if DEXC_DEBUGGER
			return DexcInspectorRuntime.Registry.ReadFields(handle, fields);
#else
			return DexcDisabledRuntime.DisabledInspectorRead();
#endif
		}

		public static DebugResult<InspectorValueSnapshot> WriteField(
			this DebugInspectorComponentHandle handle,
			DebugInspectorFieldId field,
			DebugInspectorWriteValue value,
			DebugInspectorWriteAccess access
		)
		{
#if DEXC_DEBUGGER
			return DexcInspectorRuntime.Registry.WriteField(handle, field, value, access);
#else
			return DexcDisabledRuntime.DisabledInspectorWrite();
#endif
		}

		public static DebugResult<DebugInspectorWatchId> AddWatch(
			this DebugInspectorComponentHandle handle,
			DebugInspectorFieldId field,
			TimeSpan interval
		)
		{
#if DEXC_DEBUGGER
			return DexcInspectorRuntime.Registry.AddWatch(handle, field, interval);
#else
			return DexcDisabledRuntime.DisabledInspectorWatch();
#endif
		}

		public static DebugInspectorHistoryPage GetHistory(
			this DebugInspectorComponentHandle handle,
			DebugInspectorFieldId field,
			DebugPageRequest page
		)
		{
#if DEXC_DEBUGGER
			return DexcInspectorRuntime.Registry.GetHistory(handle, field, page);
#else
			return new DebugInspectorHistoryPage(DexcDisabledRuntime.EmptyPage(page), null);
#endif
		}
	}

	internal interface IDexcInspectorSourceEntry
	{
		UnityEngine.Object Target { get; }
		string Label { get; }
		string Path { get; }
	}

	internal sealed class DexcInspectorTarget
	{
		internal DebugInspectorTargetHandle Handle;
		internal DebugObjectHandle ObjectHandle;
		internal ulong EntityId;
		internal UnityEngine.Object Object;
		internal string Label;
		internal string Path;
		internal DebugInspectorEntryOrigin Origin;
		internal DebugSceneHandle Scene;
		internal string SceneName;
		internal string HierarchyPath;
		internal readonly List<DexcInspectorComponent> Components = new List<DexcInspectorComponent>();
		internal bool Alive
		{
			get { return Object != null; }
		}
	}

	internal sealed class DexcInspectorComponent
	{
		internal DebugInspectorComponentHandle Handle;
		internal DexcInspectorTarget Target;

		/// <summary>What the fields are read from: a Component of the target, or the target itself when it has none.</summary>
		internal UnityEngine.Object Component;
		internal readonly List<DexcInspectorField> Fields = new List<DexcInspectorField>();
	}

	internal sealed class DexcInspectorField
	{
		internal DebugInspectorFieldId Id;
		internal DexcInspectorComponent Component;
		internal FieldInfo Field;
		internal DebugInspectorFieldDescriptor Public;
		internal bool Registered;
		internal DebugFieldAccess Access;
		internal bool CanWrite
		{
			get
			{
				return !Field.IsStatic
					&& !Field.IsInitOnly
					&& !Field.IsLiteral
					&& Access != DebugFieldAccess.ReadOnly
					&& Access != DebugFieldAccess.Locked;
			}
		}
	}

	internal sealed class DexcInspectorWatch
	{
		internal DebugInspectorWatchId Id;
		internal DexcInspectorField Field;
		internal TimeSpan Interval;
		internal float LastRead;
		internal DebugInspectorWatchState State;
		internal InspectorValueSnapshot Snapshot;
	}

	internal sealed class DexcInspectorSearch
	{
		internal DebugInspectorSearchId Id;
		internal DebugInspectorSearchQuery Query;
		internal readonly List<DebugInspectorSearchEntry> Results = new List<DebugInspectorSearchEntry>();
		internal readonly Stack<GameObject> Pending = new Stack<GameObject>();
		internal DebugInspectorSearchState State;
		internal int Inspected;
		internal bool IsTruncated;
		internal DebugDiagnosticId Diagnostic;
	}

	internal sealed class DexcInspectorRegistry
	{
		private readonly DexcRuntimeSession m_Session;
		private readonly Dictionary<ulong, DexcInspectorTarget> m_ByEntity = new Dictionary<ulong, DexcInspectorTarget>();
		private readonly Dictionary<DebugInspectorTargetHandle, DexcInspectorTarget> m_Targets =
			new Dictionary<DebugInspectorTargetHandle, DexcInspectorTarget>();
		private readonly Dictionary<DebugInspectorComponentHandle, DexcInspectorComponent> m_Components =
			new Dictionary<DebugInspectorComponentHandle, DexcInspectorComponent>();
		private readonly Dictionary<DebugInspectorFieldId, DexcInspectorField> m_Fields =
			new Dictionary<DebugInspectorFieldId, DexcInspectorField>();
		private readonly Dictionary<DebugInspectorWatchId, DexcInspectorWatch> m_Watches =
			new Dictionary<DebugInspectorWatchId, DexcInspectorWatch>();
		private readonly Dictionary<DebugInspectorSearchId, DexcInspectorSearch> m_Searches =
			new Dictionary<DebugInspectorSearchId, DexcInspectorSearch>();
		private readonly Dictionary<string, DebugSceneHandle> m_SceneHandles = new Dictionary<string, DebugSceneHandle>(
			StringComparer.Ordinal
		);
		private readonly LinkedList<DebugInspectorHistoryEntry> m_History = new LinkedList<DebugInspectorHistoryEntry>();
		private readonly Dictionary<Type, List<FieldInfo>> m_FieldCache = new Dictionary<Type, List<FieldInfo>>();
		private long m_NextTarget,
			m_NextComponent,
			m_NextField,
			m_NextWatch,
			m_NextSearch,
			m_NextHistory,
			m_NextScene;
		private const int SearchWorkPerFrame = 64;

		internal DexcInspectorRegistry(DexcRuntimeSession session)
		{
			m_Session = session;
			SceneManager.sceneUnloaded += OnSceneUnloaded;
		}

		internal void EndSession()
		{
			SceneManager.sceneUnloaded -= OnSceneUnloaded;
			m_Watches.Clear();
			m_Searches.Clear();
			m_Fields.Clear();
			m_Components.Clear();
			m_Targets.Clear();
			m_ByEntity.Clear();
			m_History.Clear();
			m_SceneHandles.Clear();
		}

		/// <summary>
		/// Takes any live object: a GameObject, a Component - which registers the GameObject it is on - or an asset such
		/// as a ScriptableObject, whose own fields are then what there is to look at. An empty path is a mistake in the
		/// call itself and throws; an object that is gone is a condition of the run and is reported instead.
		/// </summary>
		internal void Register(string path, UnityEngine.Object target, string label)
		{
			if (string.IsNullOrWhiteSpace(path))
				throw new ArgumentException("Inspector registration path must not be empty.", nameof(path));
			if (target == null)
			{
				Reject(path, "the object is null or has been destroyed");
				return;
			}
			var owner = Owner(target);
			var item = GetOrCreateTarget(owner, label ?? owner.name, DebugInspectorEntryOrigin.Manual);
			item.Path = path;
			item.Label = label ?? owner.name;
			item.Origin = DebugInspectorEntryOrigin.Manual;
		}

		/// <summary>
		/// Says so where the caller will see it. Registration cannot report back - the public entry point is compiled out
		/// of a release build and so has no return value - and a diagnostic is only found by someone already looking.
		/// </summary>
		private void Reject(string path, string reason)
		{
			AddDiagnostic("register-invalid", path, "The inspector registration was invalid.");
			UnityEngine.Debug.LogError("Dexc did not register \"" + path + "\" in the Inspector: " + reason + ".");
		}

		/// <summary>A Component is shown through the GameObject it is on; anything else stands for itself.</summary>
		private static UnityEngine.Object Owner(UnityEngine.Object target)
		{
			var component = target as Component;
			return component == null ? target : component.gameObject;
		}

		internal void Unregister(string path, UnityEngine.Object target)
		{
			if (target == null)
				return;
			var owner = Owner(target);
			DexcInspectorTarget item;
			if (
				m_ByEntity.TryGetValue(DebugInspector.EntityValue(owner), out item)
				&& string.Equals(item.Path, path, StringComparison.Ordinal)
			)
			{
				item.Path = string.Empty;
				item.Origin = DebugInspectorEntryOrigin.Scene;
			}
		}

		internal DebugInspectorRegisteredPage GetRegistered(DebugListQuery query)
		{
			DexcListQueryEvaluator.ValidatePage(query.Page, m_Session.Options.MaxPageSize);
			Maintain();
			var entries = new List<DebugInspectorRegisteredEntry>();
			// Registered by hand here; a source is asked again below, so what it no longer returns is no longer listed.
			foreach (
				var target in m_Targets.Values.Where(item =>
					item.Alive && !string.IsNullOrEmpty(item.Path) && item.Origin == DebugInspectorEntryOrigin.Manual
				)
			)
			{
				if (!DexcListQueryEvaluator.Matches(query, target.Path, target.Label, string.Empty, null))
					continue;
				entries.Add(PublicEntry(target));
			}
			foreach (var source in CollectSources())
			{
				if (source.Target == null || !DexcListQueryEvaluator.Matches(query, source.Path, source.Label, string.Empty, null))
					continue;
				var target = GetOrCreateTarget(
					source.Target as Component == null ? source.Target : ((Component)source.Target).gameObject,
					source.Label,
					DebugInspectorEntryOrigin.Source
				);
				entries.Add(
					new DebugInspectorRegisteredEntry(
						target.Handle,
						target.EntityId,
						source.Path,
						source.Label,
						DebugInspectorEntryOrigin.Source,
						target.Scene,
						target.SceneName,
						target.HierarchyPath,
						target.Alive
					)
				);
			}
			entries = entries
				.GroupBy(item => item.Path + "\n" + item.Target.ToString(), StringComparer.Ordinal)
				.Select(group => group.First())
				.OrderBy(item => item.Path, StringComparer.Ordinal)
				.ThenBy(item => item.Label, StringComparer.Ordinal)
				.ToList();
			return new DebugInspectorRegisteredPage(
				Page(query.Page, entries.Count, entries),
				entries.Skip(query.Page.Offset).Take(query.Page.Count)
			);
		}

		internal DebugInspectorSceneList GetScenes()
		{
			var result = new List<DebugInspectorSceneEntry>();
			for (var i = 0; i < SceneManager.sceneCount; i++)
			{
				var scene = SceneManager.GetSceneAt(i);
				if (!scene.isLoaded)
					continue;
				result.Add(new DebugInspectorSceneEntry(SceneHandle(scene), scene.name, scene.path, true));
			}
			return new DebugInspectorSceneList(result);
		}

		/// <summary>
		/// Finds what an instance id stands for. The index this reads is the one registration and hierarchy walking already
		/// fill, so nothing new is kept: an object a caller has seen listed is an object it can name.
		/// </summary>
		internal bool TryFindByEntity(ulong entityId, out DexcInspectorTarget target)
		{
			Maintain();
			if (m_ByEntity.TryGetValue(entityId, out target) && target.Alive)
				return true;
			target = null;
			return false;
		}

		internal DebugInspectorHierarchyPage GetHierarchyChildren(DebugHierarchyChildrenQuery query)
		{
			DexcListQueryEvaluator.ValidatePage(query.Page, m_Session.Options.MaxPageSize);
			Maintain();
			var scene = FindScene(query.Scene);
			if (!scene.IsValid())
				return new DebugInspectorHierarchyPage(new DebugPageInfo(query.Page.Offset, 0, false, true, 0, false), null);
			var parent = query.Parent.IsValid ? FindObject(query.Parent) : null;
			// A parent that is gone has no children. Listing the scene's roots instead would show them under it.
			if (query.Parent.IsValid && parent == null)
				return new DebugInspectorHierarchyPage(new DebugPageInfo(query.Page.Offset, 0, false, true, 0, false), null);
			var roots =
				parent == null
					? scene.GetRootGameObjects()
					: Enumerable.Range(0, parent.transform.childCount).Select(i => parent.transform.GetChild(i).gameObject).ToArray();
			var all = roots.Select(item => HierarchyEntry(item, query.Scene)).ToList();
			var values = all.Skip(query.Page.Offset).Take(Math.Min(query.Page.Count, m_Session.Options.MaxHierarchyChildren)).ToList();
			return new DebugInspectorHierarchyPage(
				new DebugPageInfo(
					query.Page.Offset,
					values.Count,
					query.Page.Offset + values.Count < all.Count,
					true,
					all.Count,
					values.Count < all.Count - query.Page.Offset
				),
				values
			);
		}

		internal DebugResult<DebugInspectorSearchId> StartSearch(DebugInspectorSearchQuery query)
		{
			if (query == null)
				throw new ArgumentNullException(nameof(query));
			var search = new DexcInspectorSearch
			{
				Id = DebugInspectorSearchId.Create(m_Session.SessionId, ++m_NextSearch),
				Query = query,
				State = DebugInspectorSearchState.Searching,
			};
			m_Searches.Add(search.Id, search);
			InitializeSearch(search);
			return new DebugResult<DebugInspectorSearchId>(
				search.State == DebugInspectorSearchState.Failed ? DebugOperationStatus.Failed : DebugOperationStatus.Succeeded,
				search.Id,
				search.Diagnostic
			);
		}

		internal DebugInspectorSearchPage GetSearchResults(DebugInspectorSearchId id, DebugPageRequest page)
		{
			DexcListQueryEvaluator.ValidatePage(page, m_Session.Options.MaxPageSize);
			DexcInspectorSearch search;
			if (!m_Searches.TryGetValue(id, out search))
				return new DebugInspectorSearchPage(
					DebugInspectorSearchState.Failed,
					new DebugPageInfo(page.Offset, 0, false, true, 0, false),
					0,
					true,
					false,
					null,
					default(DebugDiagnosticId)
				);
			var available = search.Results.Count - Math.Min(page.Offset, search.Results.Count);
			return new DebugInspectorSearchPage(
				search.State,
				new DebugPageInfo(
					page.Offset,
					Math.Min(page.Count, available),
					page.Offset + page.Count < search.Results.Count,
					search.State != DebugInspectorSearchState.Searching,
					search.Results.Count,
					search.IsTruncated
				),
				search.Inspected,
				search.State != DebugInspectorSearchState.Searching,
				search.IsTruncated,
				search.Results.Skip(page.Offset).Take(page.Count),
				search.Diagnostic
			);
		}

		internal void CancelSearch(DebugInspectorSearchId id)
		{
			DexcInspectorSearch search;
			if (m_Searches.TryGetValue(id, out search) && search.State == DebugInspectorSearchState.Searching)
				search.State = DebugInspectorSearchState.Cancelled;
		}

		internal DebugInspectorComponentPage GetComponents(DebugInspectorTargetHandle id)
		{
			Maintain();
			DexcInspectorTarget target;
			if (!m_Targets.TryGetValue(id, out target) || !target.Alive)
				return new DebugInspectorComponentPage(new DebugPageInfo(0, 0, false, true, 0, false), null);
			if (target.Components.Count == 0)
				BuildComponents(target);
			var values = target
				.Components.Select(item => new DebugInspectorComponentEntry(
					item.Handle,
					item.Component.GetType().FullName,
					item.Component.GetType().Name,
					item.Fields.Any(field => field.Registered),
					item.Component != null,
					default(DebugDiagnosticId)
				))
				.ToList();
			return new DebugInspectorComponentPage(new DebugPageInfo(0, values.Count, false, true, values.Count, false), values);
		}

		internal DebugInspectorFieldPage GetFields(DebugInspectorComponentHandle id, DebugPageRequest page)
		{
			DexcListQueryEvaluator.ValidatePage(page, m_Session.Options.MaxPageSize);
			DexcInspectorComponent component;
			if (!m_Components.TryGetValue(id, out component) || component.Component == null)
				return new DebugInspectorFieldPage(new DebugPageInfo(page.Offset, 0, false, true, 0, false), null);
			var values = component.Fields.Skip(page.Offset).Take(page.Count).Select(item => item.Public).ToList();
			return new DebugInspectorFieldPage(
				new DebugPageInfo(
					page.Offset,
					values.Count,
					page.Offset + values.Count < component.Fields.Count,
					true,
					component.Fields.Count,
					values.Count < component.Fields.Count - page.Offset
				),
				values
			);
		}

		internal DebugResult<IReadOnlyList<DebugInspectorFieldValue>> ReadFields(
			DebugInspectorComponentHandle id,
			IReadOnlyList<DebugInspectorFieldId> ids
		)
		{
			DexcInspectorComponent component;
			if (!m_Components.TryGetValue(id, out component) || component.Component == null)
				return new DebugResult<IReadOnlyList<DebugInspectorFieldValue>>(
					DebugOperationStatus.Stale,
					Array.Empty<DebugInspectorFieldValue>(),
					default(DebugDiagnosticId)
				);
			var values = new List<DebugInspectorFieldValue>();
			foreach (var fieldId in ids ?? Array.Empty<DebugInspectorFieldId>())
			{
				DexcInspectorField field;
				if (!m_Fields.TryGetValue(fieldId, out field) || field.Component != component)
				{
					var diagnostic = AddDiagnostic(
						"inspector-field-not-found",
						fieldId.ToString(),
						"The requested inspector field could not be read."
					);
					values.Add(
						new DebugInspectorFieldValue(
							fieldId,
							new InspectorValueSnapshot(
								DebugInspectorSnapshotState.ReadFailure,
								null,
								default(DebugInspectorTargetHandle),
								string.Empty,
								string.Empty,
								string.Empty,
								diagnostic,
								"The field does not belong to this component."
							)
						)
					);
					continue;
				}
				values.Add(new DebugInspectorFieldValue(field.Id, Read(field)));
			}
			return new DebugResult<IReadOnlyList<DebugInspectorFieldValue>>(
				DebugOperationStatus.Succeeded,
				values,
				default(DebugDiagnosticId)
			);
		}

		internal bool CanEdit(DebugInspectorComponentHandle component, DebugInspectorFieldId id, bool allowUnregistered)
		{
			DexcInspectorField field;
			return m_Fields.TryGetValue(id, out field)
				&& field.Component.Handle == component
				&& field.Component.Component != null
				&& !field.Public.IsCollection
				&& !(field.Field.GetValue(field.Component.Component) is ICollection)
				&& (
					field.Registered
						? field.CanWrite
						: allowUnregistered && !field.Field.IsStatic && !field.Field.IsInitOnly && !field.Field.IsLiteral
				);
		}

		internal IReadOnlyList<KeyValuePair<DebugInspectorTargetHandle, string>> GetAssignableReferences(
			DebugInspectorTargetHandle handle,
			string typeName
		)
		{
			DexcInspectorTarget target;
			if (!m_Targets.TryGetValue(handle, out target) || !target.Alive)
				return Array.Empty<KeyValuePair<DebugInspectorTargetHandle, string>>();
			var candidates = new List<UnityEngine.Object> { target.Object };
			if (target.Object is GameObject gameObject)
				candidates.AddRange(gameObject.GetComponents<Component>().Where(component => component != null));
			var result = new List<KeyValuePair<DebugInspectorTargetHandle, string>>();
			foreach (var candidate in candidates)
			{
				var matches = false;
				for (var type = candidate.GetType(); type != null; type = type.BaseType)
					if (type.FullName == typeName)
					{
						matches = true;
						break;
					}
				if (!matches)
					continue;
				var reference = GetOrCreateTarget(candidate, candidate.name, DebugInspectorEntryOrigin.Scene);
				result.Add(
					new KeyValuePair<DebugInspectorTargetHandle, string>(
						reference.Handle,
						candidate.name + " / " + candidate.GetType().Name + " [" + (result.Count + 1) + "]"
					)
				);
			}
			return result;
		}

		internal DebugResult<InspectorValueSnapshot> WriteField(
			DebugInspectorComponentHandle componentId,
			DebugInspectorFieldId fieldId,
			DebugInspectorWriteValue value,
			DebugInspectorWriteAccess access
		)
		{
			if (value == null)
				throw new ArgumentNullException(nameof(value));
			DexcInspectorField field;
			DexcInspectorComponent component;
			if (
				!m_Fields.TryGetValue(fieldId, out field)
				|| !m_Components.TryGetValue(componentId, out component)
				|| field.Component != component
			)
				return WriteResult(DebugOperationStatus.Stale, null, null, null, default(DebugDiagnosticId));
			if (component.Component == null || field.Component.Target.Object == null)
				return WriteResult(DebugOperationStatus.Stale, null, null, null, default(DebugDiagnosticId));
			if (!Enum.IsDefined(typeof(DebugInspectorWriteAccess), access))
				throw new ArgumentOutOfRangeException(nameof(access));
			if (field.Public.IsCollection || field.Field.GetValue(component.Component) is ICollection)
				return WriteResult(DebugOperationStatus.ReadOnly, null, null, null, default(DebugDiagnosticId));
			if (!field.Registered && access != DebugInspectorWriteAccess.AllowUnregistered)
				return WriteResult(DebugOperationStatus.ExplicitAccessRequired, null, null, null, default(DebugDiagnosticId));
			if (
				(field.Registered && !field.CanWrite)
				|| (!field.Registered && (field.Field.IsStatic || field.Field.IsInitOnly || field.Field.IsLiteral))
			)
				return WriteResult(
					field.Access == DebugFieldAccess.Locked || field.Access == DebugFieldAccess.ReadOnly
						? DebugOperationStatus.ReadOnly
						: DebugOperationStatus.Rejected,
					null,
					null,
					null,
					default(DebugDiagnosticId)
				);
			var before = Read(field);
			var issues = new List<DebugValidationIssue>();
			object converted;
			try
			{
				converted = FromWriteValue(value, field.Field.FieldType);
			}
			catch (Exception exception)
			{
				issues.Add(new DebugValidationIssue(DebugValidationSeverity.Error, "inspector-type-mismatch", exception.Message));
				return WriteResult(DebugOperationStatus.Rejected, before, null, issues, default(DebugDiagnosticId));
			}
			try
			{
				field.Field.SetValue(field.Component.Component, converted);
				var after = Read(field);
				AddHistory(field, before, after, DebugOperationStatus.Succeeded);
				return WriteResult(DebugOperationStatus.Succeeded, before, after, null, default(DebugDiagnosticId));
			}
			catch (Exception exception)
			{
				var diagnostic = AddDiagnostic("inspector-write-failed", field.Field.Name, "The inspector field write failed.", exception);
				return WriteResult(DebugOperationStatus.Failed, before, null, null, diagnostic);
			}
		}

		internal DebugResult<DebugInspectorWatchId> AddWatch(
			DebugInspectorComponentHandle componentId,
			DebugInspectorFieldId fieldId,
			TimeSpan interval
		)
		{
			if (interval < TimeSpan.Zero)
				throw new ArgumentOutOfRangeException(nameof(interval));
			if (m_Watches.Count >= m_Session.Options.MaxWatchCount)
				return new DebugResult<DebugInspectorWatchId>(
					DebugOperationStatus.Rejected,
					default(DebugInspectorWatchId),
					new[]
					{
						new DebugValidationIssue(DebugValidationSeverity.Error, "watch-capacity", "The watch capacity has been reached."),
					},
					default(DebugDiagnosticId)
				);
			DexcInspectorField field;
			DexcInspectorComponent component;
			if (
				!m_Fields.TryGetValue(fieldId, out field)
				|| !m_Components.TryGetValue(componentId, out component)
				|| field.Component != component
			)
				return new DebugResult<DebugInspectorWatchId>(
					DebugOperationStatus.Stale,
					default(DebugInspectorWatchId),
					null,
					default(DebugDiagnosticId)
				);
			var watch = new DexcInspectorWatch
			{
				Id = DebugInspectorWatchId.Create(m_Session.SessionId, ++m_NextWatch),
				Field = field,
				Interval = interval,
				LastRead = Time.unscaledTime,
				State = DebugInspectorWatchState.Active,
				Snapshot = Read(field),
			};
			watch.State = StateFor(watch.Snapshot);
			m_Watches.Add(watch.Id, watch);
			return new DebugResult<DebugInspectorWatchId>(DebugOperationStatus.Succeeded, watch.Id, null, default(DebugDiagnosticId));
		}

		internal DebugInspectorWatchPage GetWatches(DebugPageRequest page)
		{
			DexcListQueryEvaluator.ValidatePage(page, m_Session.Options.MaxPageSize);
			Maintain();
			var values = m_Watches
				.Values.OrderBy(item => item.Id.ToString(), StringComparer.Ordinal)
				.Select(item => new DebugInspectorWatchEntry(
					item.Id,
					item.Field.Component.Target.Handle,
					item.Field.Component.Handle,
					item.Field.Id,
					item.Interval,
					item.State,
					item.Snapshot
				))
				.ToList();
			var selected = values.Skip(page.Offset).Take(page.Count).ToList();
			return new DebugInspectorWatchPage(
				new DebugPageInfo(page.Offset, selected.Count, page.Offset + selected.Count < values.Count, true, values.Count, false),
				selected
			);
		}

		internal string GetWatchLabel(DebugInspectorWatchId id)
		{
			DexcInspectorWatch watch;
			return m_Watches.TryGetValue(id, out watch)
				? watch.Field.Component.Target.Label + " / " + watch.Field.Field.DeclaringType.Name + " / " + watch.Field.Public.DisplayName
				: "Missing";
		}

		internal void RemoveWatch(DebugInspectorWatchId id)
		{
			m_Watches.Remove(id);
		}

		internal IReadOnlyList<DebugInspectorHistoryEntry> GetHistorySnapshot() => m_History.ToArray();

		internal DebugInspectorHistoryPage GetHistory(
			DebugInspectorComponentHandle componentId,
			DebugInspectorFieldId fieldId,
			DebugPageRequest page
		)
		{
			DexcListQueryEvaluator.ValidatePage(page, m_Session.Options.MaxPageSize);
			var values = m_History
				.Where(item => item.Target.IsValid && item.FieldName == FieldName(fieldId) && item.Component == componentId)
				.Reverse()
				.ToList();
			var selected = values.Skip(page.Offset).Take(page.Count).ToList();
			return new DebugInspectorHistoryPage(
				new DebugPageInfo(page.Offset, selected.Count, page.Offset + selected.Count < values.Count, true, values.Count, false),
				selected
			);
		}

		internal void Tick()
		{
			if (!m_Session.IsCurrent)
				return;
			Maintain();
			var searchBudget = SearchWorkPerFrame;
			foreach (var search in m_Searches.Values.Where(item => item.State == DebugInspectorSearchState.Searching).ToList())
			{
				if (searchBudget <= 0)
					break;
				searchBudget -= ProcessSearch(search, searchBudget);
			}
			var now = Time.unscaledTime;
			var budget = m_Session.Options.MaxWatchUpdatesPerFrame;
			foreach (var watch in m_Watches.Values.ToList())
			{
				if (budget <= 0)
					break;
				if (watch.Interval > TimeSpan.Zero && now - watch.LastRead < (float)watch.Interval.TotalSeconds)
					continue;
				watch.LastRead = now;
				watch.Snapshot = Read(watch.Field);
				watch.State = StateFor(watch.Snapshot);
				budget--;
			}
		}

		private void BuildComponents(DexcInspectorTarget target)
		{
			var gameObject = target.Object as GameObject;
			if (gameObject == null)
			{
				// An asset has no components. Its own fields are what there is to look at, so it stands in for one.
				BuildComponent(target, target.Object);
				return;
			}
			foreach (var unityComponent in gameObject.GetComponents<Component>())
			{
				if (unityComponent == null)
					continue;
				BuildComponent(target, unityComponent);
			}
		}

		private void BuildComponent(DexcInspectorTarget target, UnityEngine.Object owner)
		{
			var item = new DexcInspectorComponent
			{
				Handle = DebugInspectorComponentHandle.Create(m_Session.SessionId, ++m_NextComponent),
				Target = target,
				Component = owner,
			};
			target.Components.Add(item);
			m_Components.Add(item.Handle, item);
			foreach (var field in GetFieldInfos(owner.GetType()))
			{
				var attribute = field
					.GetCustomAttributes(typeof(DebugFieldAttribute), true)
					.OfType<DebugFieldAttribute>()
					.SingleOrDefault();
				var registered = attribute != null;
				if (!registered && !field.IsPublic)
					continue;
				var access = attribute == null ? DebugFieldAccess.ReadOnly : attribute.Access;
				var id = DebugInspectorFieldId.Create(m_Session.SessionId, ++m_NextField);
				var descriptor = new DebugInspectorFieldDescriptor(
					id,
					field.Name,
					string.IsNullOrWhiteSpace(attribute == null ? string.Empty : attribute.DisplayName)
						? field.Name
						: attribute.DisplayName,
					field.FieldType.FullName,
					registered,
					access,
					registered
						&& access == DebugFieldAccess.Default
						&& !typeof(ICollection).IsAssignableFrom(field.FieldType)
						&& !field.IsStatic
						&& !field.IsInitOnly
						&& !field.IsLiteral,
					typeof(ICollection).IsAssignableFrom(field.FieldType)
				);
				var descriptorItem = new DexcInspectorField
				{
					Id = id,
					Component = item,
					Field = field,
					Public = descriptor,
					Registered = registered,
					Access = access,
				};
				item.Fields.Add(descriptorItem);
				m_Fields.Add(id, descriptorItem);
			}
		}

		private DexcInspectorTarget GetOrCreateTarget(UnityEngine.Object target, string label, DebugInspectorEntryOrigin origin)
		{
			var gameObject = target as GameObject ?? (target as Component)?.gameObject;
			var entityId = DebugInspector.EntityValue(target);
			if (m_ByEntity.TryGetValue(entityId, out var existing) && existing.Alive && existing.Object == target)
				return existing;
			var item = new DexcInspectorTarget
			{
				Handle = DebugInspectorTargetHandle.Create(m_Session.SessionId, ++m_NextTarget),
				ObjectHandle = DebugObjectHandle.Create(m_Session.SessionId, ++m_NextTarget),
				// Kept rather than asked for later: a destroyed object still answers GetEntityId, but a null one does not.
				EntityId = entityId,
				Object = target,
				Label = label,
				Origin = origin,
				Scene = gameObject == null ? default(DebugSceneHandle) : SceneHandle(gameObject.scene),
				SceneName = gameObject == null ? string.Empty : gameObject.scene.name,
				HierarchyPath = gameObject == null ? string.Empty : HierarchyPath(gameObject),
			};
			m_ByEntity[entityId] = item;
			m_Targets.Add(item.Handle, item);
			return item;
		}

		private DebugInspectorRegisteredEntry PublicEntry(DexcInspectorTarget item)
		{
			return new DebugInspectorRegisteredEntry(
				item.Handle,
				item.EntityId,
				item.Path,
				item.Label,
				item.Origin,
				item.Scene,
				item.SceneName,
				item.HierarchyPath,
				item.Alive
			);
		}

		private DebugInspectorHierarchyEntry HierarchyEntry(GameObject item, DebugSceneHandle scene)
		{
			var target = GetOrCreateTarget(item, item.name, DebugInspectorEntryOrigin.Scene);
			return new DebugInspectorHierarchyEntry(
				target.ObjectHandle,
				target.Handle,
				target.EntityId,
				scene,
				item.name,
				HierarchyPath(item),
				item.activeSelf,
				item.transform.childCount != 0
			);
		}

		private void InitializeSearch(DexcInspectorSearch search)
		{
			var scene = FindScene(search.Query.Scene);
			if (!scene.IsValid())
			{
				search.State = DebugInspectorSearchState.Failed;
				return;
			}
			var roots = scene.GetRootGameObjects();
			for (var index = roots.Length - 1; index >= 0; index--)
				search.Pending.Push(roots[index]);
			if (m_Session.Options.MaxSearchResults == 0)
			{
				search.IsTruncated = true;
				search.State = DebugInspectorSearchState.Completed;
			}
		}

		private int ProcessSearch(DexcInspectorSearch search, int workBudget)
		{
			var inspected = 0;
			while (search.Pending.Count != 0 && inspected < workBudget && search.State == DebugInspectorSearchState.Searching)
			{
				var gameObject = search.Pending.Pop();
				if (gameObject == null)
					continue;
				inspected++;
				search.Inspected++;
				var nameMatch =
					search.Query.Name.Length == 0 || gameObject.name.IndexOf(search.Query.Name, StringComparison.OrdinalIgnoreCase) >= 0;
				var activeMatch = !search.Query.Active.HasValue || gameObject.activeSelf == search.Query.Active.Value;
				foreach (var component in gameObject.GetComponents<Component>())
				{
					if (component == null)
						continue;
					var typeMatch =
						search.Query.ComponentType.Length == 0
						|| component.GetType().FullName.IndexOf(search.Query.ComponentType, StringComparison.OrdinalIgnoreCase) >= 0;
					if (!nameMatch || !activeMatch || !typeMatch)
						continue;
					if (search.Results.Count >= m_Session.Options.MaxSearchResults)
					{
						search.IsTruncated = true;
						search.State = DebugInspectorSearchState.Completed;
						search.Pending.Clear();
						break;
					}
					var target = GetOrCreateTarget(gameObject, gameObject.name, DebugInspectorEntryOrigin.Scene);
					search.Results.Add(
						new DebugInspectorSearchEntry(
							target.Handle,
							search.Query.Scene,
							HierarchyPath(gameObject),
							gameObject.name,
							component.GetType().FullName
						)
					);
				}
				if (search.State != DebugInspectorSearchState.Searching)
					continue;
				for (var childIndex = gameObject.transform.childCount - 1; childIndex >= 0; childIndex--)
					search.Pending.Push(gameObject.transform.GetChild(childIndex).gameObject);
			}
			if (search.State == DebugInspectorSearchState.Searching && search.Pending.Count == 0)
				search.State = DebugInspectorSearchState.Completed;
			return inspected;
		}

		private IEnumerable<IDexcInspectorSourceEntry> CollectSources()
		{
			// Which members are sources is a definition and does not change while the assemblies stay loaded; what they
			// return does, so every search reads them again.
			foreach (var definition in DexcDefinitions.InspectorSources)
			{
				object value;
				try
				{
					value = definition.Read();
				}
				catch (Exception exception)
				{
					AddDiagnostic("source-failed", definition.Identity, "Inspector source evaluation failed.", exception);
					continue;
				}
				var entries = value as IEnumerable;
				if (entries == null)
					continue;
				foreach (var entry in entries)
				{
					var source = entry as IDexcInspectorSourceEntry;
					if (source != null)
					{
						var path = string.IsNullOrEmpty(source.Path)
							? definition.Attribute.Path
							: definition.Attribute.Path + "/" + source.Path;
						yield return new SourceProjection(source.Target, source.Label, path);
					}
				}
			}
		}

		private sealed class SourceProjection : IDexcInspectorSourceEntry
		{
			internal SourceProjection(UnityEngine.Object target, string label, string path)
			{
				Target = target;
				Label = label;
				Path = path;
			}

			public UnityEngine.Object Target { get; }
			public string Label { get; }
			public string Path { get; }
		}

		private InspectorValueSnapshot Read(DexcInspectorField field)
		{
			if (field.Component.Target.Object == null || field.Component.Component == null)
				return new InspectorValueSnapshot(
					DebugInspectorSnapshotState.Missing,
					null,
					default(DebugInspectorTargetHandle),
					string.Empty,
					field.Component.Target.Label,
					field.Field.FieldType.FullName,
					default(DebugDiagnosticId),
					string.Empty
				);
			try
			{
				var raw = field.Field.GetValue(field.Component.Component);
				return ToSnapshot(raw, field.Field.FieldType);
			}
			catch (Exception exception)
			{
				var identity = field.Field.Name;
				var typeName = field.Field.FieldType.FullName;
				var diagnostic = AddDiagnostic("inspector-read-failed", identity, "The inspector field read failed.", exception);
				return new InspectorValueSnapshot(
					DebugInspectorSnapshotState.ReadFailure,
					null,
					default(DebugInspectorTargetHandle),
					string.Empty,
					field.Component.Target.Label,
					typeName,
					diagnostic,
					exception.Message
				);
			}
		}

		private InspectorValueSnapshot ToSnapshot(object raw, Type type)
		{
			if (raw is ICollection || typeof(ICollection).IsAssignableFrom(type))
				return new InspectorValueSnapshot(
					DebugInspectorSnapshotState.Value,
					raw == null ? DebugValue.Null : CollectionValue((ICollection)raw),
					default(DebugInspectorTargetHandle),
					string.Empty,
					string.Empty,
					type.FullName,
					default(DebugDiagnosticId),
					string.Empty
				);
			if (typeof(UnityEngine.Object).IsAssignableFrom(type))
			{
				var reference = raw as UnityEngine.Object;
				if (reference == null)
					return new InspectorValueSnapshot(
						DebugInspectorSnapshotState.UnityReference,
						null,
						default(DebugInspectorTargetHandle),
						type.FullName,
						"null",
						type.FullName,
						default(DebugDiagnosticId),
						string.Empty
					);
				var target = GetOrCreateTarget(reference, reference.name, DebugInspectorEntryOrigin.Scene);
				return new InspectorValueSnapshot(
					DebugInspectorSnapshotState.UnityReference,
					null,
					target.Handle,
					reference.GetType().FullName,
					reference.name,
					type.FullName,
					default(DebugDiagnosticId),
					string.Empty
				);
			}
			try
			{
				return new InspectorValueSnapshot(
					DebugInspectorSnapshotState.Value,
					ToValue(raw, type),
					default(DebugInspectorTargetHandle),
					string.Empty,
					string.Empty,
					type.FullName,
					default(DebugDiagnosticId),
					string.Empty
				);
			}
			catch
			{
				return new InspectorValueSnapshot(
					DebugInspectorSnapshotState.Unsupported,
					null,
					default(DebugInspectorTargetHandle),
					string.Empty,
					string.Empty,
					type.FullName,
					default(DebugDiagnosticId),
					string.Empty
				);
			}
		}

		private object FromWriteValue(DebugInspectorWriteValue value, Type type)
		{
			if (typeof(UnityEngine.Object).IsAssignableFrom(type))
			{
				if (value.IsNullReference)
					return null;
				if (!value.Reference.IsValid)
					throw new InvalidOperationException("A Unity reference is required.");
				DexcInspectorTarget target;
				if (!m_Targets.TryGetValue(value.Reference, out target) || !target.Alive)
					throw new InvalidOperationException("The Unity reference is stale.");
				if (!type.IsInstanceOfType(target.Object))
					throw new InvalidOperationException("The Unity reference type is incompatible.");
				return target.Object;
			}
			if (value.Value == null)
				throw new InvalidOperationException("A scalar value is required.");
			return FromValue(value.Value, type);
		}

		private void AddHistory(
			DexcInspectorField field,
			InspectorValueSnapshot before,
			InspectorValueSnapshot after,
			DebugOperationStatus status
		)
		{
			var entry = new DebugInspectorHistoryEntry(
				DebugHistoryId.Create(m_Session.SessionId, ++m_NextHistory),
				field.Component.Target.Handle,
				field.Component.Target.Label,
				field.Component.Component.GetType().FullName,
				field.Field.Name,
				before,
				after,
				status,
				DateTimeOffset.UtcNow
			)
			{
				Component = field.Component.Handle,
			};
			m_History.AddLast(entry);
			while (m_History.Count > m_Session.Options.HistoryCapacityPerFeature)
				m_History.RemoveFirst();
		}

		private void Maintain()
		{
			foreach (var target in m_Targets.Values.Where(item => !item.Alive).ToList())
			{
				foreach (var watch in m_Watches.Values.Where(item => item.Field.Component.Target == target))
				{
					watch.State = DebugInspectorWatchState.Missing;
					watch.Snapshot = new InspectorValueSnapshot(
						DebugInspectorSnapshotState.Missing,
						null,
						default(DebugInspectorTargetHandle),
						string.Empty,
						target.Label,
						string.Empty,
						default(DebugDiagnosticId),
						string.Empty
					);
				}
			}
		}

		private void OnSceneUnloaded(Scene scene)
		{
			foreach (var target in m_Targets.Values.Where(item => item.SceneName == scene.name).ToList())
			{
				foreach (var watch in m_Watches.Values.Where(item => item.Field.Component.Target == target))
				{
					watch.State = DebugInspectorWatchState.Missing;
					watch.Snapshot = new InspectorValueSnapshot(
						DebugInspectorSnapshotState.Missing,
						null,
						default(DebugInspectorTargetHandle),
						string.Empty,
						target.Label,
						string.Empty,
						default(DebugDiagnosticId),
						string.Empty
					);
				}
			}
		}

		private DebugSceneHandle SceneHandle(Scene scene)
		{
			if (!scene.IsValid())
				return default(DebugSceneHandle);
			var key = scene.path + "|" + scene.name;
			DebugSceneHandle handle;
			if (m_SceneHandles.TryGetValue(key, out handle))
				return handle;
			handle = DebugSceneHandle.Create(m_Session.SessionId, ++m_NextScene);
			m_SceneHandles.Add(key, handle);
			return handle;
		}

		private Scene FindScene(DebugSceneHandle handle)
		{
			for (var i = 0; i < SceneManager.sceneCount; i++)
			{
				var scene = SceneManager.GetSceneAt(i);
				if (SceneHandle(scene) == handle)
					return scene;
			}
			return default(Scene);
		}

		private GameObject FindObject(DebugObjectHandle handle)
		{
			var item = m_Targets.Values.FirstOrDefault(candidate => candidate.ObjectHandle == handle);
			return item == null ? null : item.Object as GameObject;
		}

		private string FieldName(DebugInspectorFieldId id)
		{
			DexcInspectorField field;
			return m_Fields.TryGetValue(id, out field) ? field.Field.Name : string.Empty;
		}

		private static string HierarchyPath(GameObject gameObject)
		{
			var parts = new List<string>();
			for (var current = gameObject.transform; current != null; current = current.parent)
				parts.Add(current.name);
			parts.Reverse();
			return string.Join("/", parts.ToArray());
		}

		private static DebugInspectorWatchState StateFor(InspectorValueSnapshot snapshot)
		{
			switch (snapshot.State)
			{
				case DebugInspectorSnapshotState.Missing:
					return DebugInspectorWatchState.Missing;
				case DebugInspectorSnapshotState.Unsupported:
					return DebugInspectorWatchState.Unsupported;
				case DebugInspectorSnapshotState.ReadFailure:
					return DebugInspectorWatchState.ReadFailure;
				default:
					return DebugInspectorWatchState.Active;
			}
		}

		private static DebugPageInfo Page(DebugPageRequest request, int total, IList<DebugInspectorRegisteredEntry> values)
		{
			return new DebugPageInfo(
				request.Offset,
				values.Skip(request.Offset).Take(request.Count).Count(),
				request.Offset + request.Count < total,
				true,
				total,
				false
			);
		}

		private DebugResult<InspectorValueSnapshot> WriteResult(
			DebugOperationStatus status,
			InspectorValueSnapshot before,
			InspectorValueSnapshot after,
			IEnumerable<DebugValidationIssue> issues,
			DebugDiagnosticId diagnostic
		)
		{
			return new DebugResult<InspectorValueSnapshot>(status, after, issues, diagnostic);
		}

		private DebugDiagnosticId AddDiagnostic(string code, string identity, string message, Exception exception = null)
		{
			var detail = m_Session.Diagnostics.AddOperation(
				DebugDiagnosticSeverity.Error,
				DebugFeature.Inspector,
				"operation",
				code,
				message,
				identity,
				null,
				exception
			);
			return detail == null ? default(DebugDiagnosticId) : detail.DiagnosticId;
		}

		private List<FieldInfo> GetFieldInfos(Type type)
		{
			List<FieldInfo> fields;
			if (m_FieldCache.TryGetValue(type, out fields))
				return fields;
			var all = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).ToList();
			// Asking the type leaves out the private fields its base classes declare, so a [DebugField] on a base class would
			// not be listed. Only those marked ones are added: a base class's other private fields are its own internals,
			// whoever wrote it.
			for (var declaring = type.BaseType; declaring != null; declaring = declaring.BaseType)
			{
				all.AddRange(
					declaring
						.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
						.Where(field => field.IsPrivate && field.IsDefined(typeof(DebugFieldAttribute), false))
				);
			}
			fields = all.OrderBy(field => field.MetadataToken).ToList();
			m_FieldCache.Add(type, fields);
			return fields;
		}

		private static DebugValue CollectionValue(ICollection collection)
		{
			var count = collection.Count;
			var items = new List<DebugValue>();
			var iterator = collection.GetEnumerator();
			try
			{
				while (items.Count < 128 && iterator.MoveNext())
				{
					var raw = iterator.Current;
					var state = DebugInspectorSnapshotState.Value;
					var value = DebugValue.Null;
					var label = string.Empty;
					if (raw is UnityEngine.Object reference)
					{
						state = DebugInspectorSnapshotState.UnityReference;
						label = reference == null ? "null" : reference.name;
					}
					else if (raw != null)
					{
						try
						{
							value = ToValue(raw, raw.GetType());
						}
						catch (InvalidOperationException)
						{
							state = DebugInspectorSnapshotState.Unsupported;
							label = raw.GetType().FullName;
						}
						catch (ArgumentException)
						{
							state = DebugInspectorSnapshotState.Unsupported;
							label = raw.GetType().FullName;
						}
					}

					items.Add(
						DebugValue.FromObject(
							new DebugValueMember("index", DebugValue.From(items.Count)),
							new DebugValueMember("state", DebugValue.From(state.ToString())),
							new DebugValueMember("value", value),
							new DebugValueMember("label", DebugValue.From(label))
						)
					);
				}
			}
			finally
			{
				(iterator as IDisposable)?.Dispose();
			}

			return DebugValue.FromObject(
				new DebugValueMember("count", DebugValue.From(count)),
				new DebugValueMember("items", DebugValue.FromArray(items)),
				new DebugValueMember("truncated", DebugValue.From(count > items.Count))
			);
		}

		private static DebugValue ToValue(object value, Type type)
		{
			if (type == typeof(bool))
				return DebugValue.From((bool)value);
			if (type == typeof(string))
				return DebugValue.From((string)value);
			if (type == typeof(int))
				return DebugValue.From((int)value);
			if (type == typeof(float))
				return DebugValue.From((float)value);
			if (type == typeof(double))
				return DebugValue.From((double)value);
			if (type == typeof(long))
				return DebugValue.From((long)value);
			if (type == typeof(uint))
				return DebugValue.From((uint)value);
			if (type == typeof(ulong))
				return DebugValue.From((ulong)value);
			if (type == typeof(short))
				return DebugValue.From((short)value);
			if (type == typeof(ushort))
				return DebugValue.From((ushort)value);
			if (type == typeof(byte))
				return DebugValue.From((byte)value);
			if (type == typeof(sbyte))
				return DebugValue.From((sbyte)value);
			if (type.IsEnum)
			{
				var name = Enum.GetName(type, value);
				if (name == null)
					throw new InvalidOperationException("Enum value is not defined.");
				return DebugValue.From(name);
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
			throw new InvalidOperationException("Unsupported inspector field type.");
		}

		private static object FromValue(DebugValue value, Type type)
		{
			if (type == typeof(bool) && value.TryGetBoolean(out var boolean))
				return boolean;
			if (type == typeof(string) && value.TryGetString(out var text))
				return text;
			if (type == typeof(float) && value.TryGetFloatingPoint(out var floating))
				return (float)floating;
			if (type == typeof(double) && value.TryGetFloatingPoint(out var doubleValue))
				return doubleValue;
			if (type == typeof(long) && value.TryGetSignedInteger(out var longValue))
				return longValue;
			if (type == typeof(ulong) && value.TryGetUnsignedInteger(out var ulongValue))
				return ulongValue;
			if (type == typeof(sbyte))
				return NarrowSigned(value, sbyte.MinValue, sbyte.MaxValue, "SByte");
			if (type == typeof(short))
				return NarrowSigned(value, short.MinValue, short.MaxValue, "Int16");
			if (type == typeof(int))
				return NarrowSigned(value, int.MinValue, int.MaxValue, "Int32");
			if (type == typeof(byte))
				return NarrowUnsigned(value, byte.MaxValue, "Byte");
			if (type == typeof(ushort))
				return NarrowUnsigned(value, ushort.MaxValue, "UInt16");
			if (type == typeof(uint))
				return NarrowUnsigned(value, uint.MaxValue, "UInt32");
			if (type.IsEnum)
			{
				if (!value.TryGetString(out var enumKey) || string.IsNullOrEmpty(enumKey))
					throw new InvalidOperationException("Enum value must be a String key.");
				if (!Enum.IsDefined(type, enumKey))
					throw new InvalidOperationException("Enum key is not declared.");
				return Enum.Parse(type, enumKey, false);
			}
			IReadOnlyList<DebugValueMember> members;
			if (value.TryGetObject(out members))
			{
				var map = members.ToDictionary(item => item.Name, item => item.Value, StringComparer.Ordinal);
				Func<string, float> number = name =>
				{
					double n;
					if (!map[name].TryGetFloatingPoint(out n))
						throw new InvalidOperationException("Expected floating point member.");
					return (float)n;
				};
				if (type == typeof(Vector2))
					return new Vector2(number("x"), number("y"));
				if (type == typeof(Vector3))
					return new Vector3(number("x"), number("y"), number("z"));
				if (type == typeof(Vector4))
					return new Vector4(number("x"), number("y"), number("z"), number("w"));
				if (type == typeof(Color))
					return new Color(number("r"), number("g"), number("b"), number("a"));
			}
			throw new InvalidOperationException("Inspector value type does not exactly match the field type.");
		}

		private static object NarrowSigned(DebugValue value, long minimum, long maximum, string typeName)
		{
			if (!value.TryGetSignedInteger(out var number) || number < minimum || number > maximum)
				throw new InvalidOperationException(typeName + " value is outside the target type range.");
			if (typeName == "SByte")
				return (sbyte)number;
			if (typeName == "Int16")
				return (short)number;
			return (int)number;
		}

		private static object NarrowUnsigned(DebugValue value, ulong maximum, string typeName)
		{
			if (!value.TryGetUnsignedInteger(out var number) || number > maximum)
				throw new InvalidOperationException(typeName + " value is outside the target type range.");
			if (typeName == "Byte")
				return (byte)number;
			if (typeName == "UInt16")
				return (ushort)number;
			return (uint)number;
		}
	}

	internal static class DexcInspectorRuntime
	{
		internal static DexcInspectorRegistry Registry
		{
			get
			{
				var session = Get();
				// Every Inspector API funnels through here, so this is where watch polling starts.
				DexcInspectorDriver.Ensure();
				return session.Inspector ?? (session.Inspector = new DexcInspectorRegistry(session));
			}
		}

		/// <summary>The session's registry if a session exists and the Inspector has been used, without starting watch polling.</summary>
		internal static DexcInspectorRegistry ExistingRegistry => DexcDebugger.CurrentSession?.Inspector;

		internal static void EndSession(DexcRuntimeSession session)
		{
			if (session != null && session.Inspector != null)
				session.Inspector.EndSession();
		}

		internal static void Tick()
		{
			var session = DexcDebugger.CurrentSession;
			if (session != null && session.Inspector != null)
				session.Inspector.Tick();
		}

		private static DexcRuntimeSession Get()
		{
			var session = DexcDebugger.CurrentSession;
			if (session == null)
				throw new InvalidOperationException("DexcDebugger must be initialized.");
			return session;
		}
	}

	internal sealed class DexcInspectorDriver : MonoBehaviour
	{
		private void Update()
		{
			DexcInspectorRuntime.Tick();
		}

		internal static void Destroy()
		{
			var root = DexcRoot.Current;
			var driver = root.InspectorDriver;
			root.InspectorDriver = null;
			DexcRoot.DestroyObject(driver == null ? null : driver.gameObject);
		}

		/// <summary>Creates the driver the first time the Inspector is used. Never while the Editor is not playing.</summary>
		internal static void Ensure()
		{
#if DEXC_DEBUGGER
			var root = DexcRoot.Current;
			if (root.InspectorDriver != null || !Application.isPlaying)
				return;
			var gameObject = new GameObject("DexcInspectorDriver");
			// No hide flags: Play Mode owns the lifetime, so leaving it destroys this with everything else.
			DontDestroyOnLoad(gameObject);
			root.InspectorDriver = gameObject.AddComponent<DexcInspectorDriver>();
#endif
		}
	}
}
