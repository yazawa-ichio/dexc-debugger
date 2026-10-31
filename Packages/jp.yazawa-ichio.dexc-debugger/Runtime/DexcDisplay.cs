using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using UnityEngine;

namespace Dexc
{
	public enum DebugDisplayTimeScope
	{
		Frame = 0,
		Realtime = 1,
		GameTime = 2,
		Infinity = 3,
	}

	public enum DebugDisplaySpace
	{
		World = 0,
		Local = 1,
	}

	public enum DebugDisplayDrawStyle
	{
		Wire = 0,
		Solid = 1,
		WireAndSolid = 2,
	}

	public enum DebugDisplayPrimitiveKind
	{
		Value = 0,
		Label = 1,
		Line = 2,
		Ray = 3,
		PolyLine = 4,
		WireSphere = 5,
		WireCube = 6,
		WireDisc = 7,
		Relation = 8,
		Sphere = 9,
		Cube = 10,
		Disc = 11,
		Capsule = 12,
	}

	public readonly struct DebugDisplaySubjectId : IEquatable<DebugDisplaySubjectId>
	{
		private readonly DebugSessionId m_Session;
		private readonly long m_Value;

		internal DebugDisplaySubjectId(DebugSessionId session, long value)
		{
			m_Session = session;
			m_Value = value;
		}

		internal static DebugDisplaySubjectId Create(DebugSessionId session, long value)
		{
			return new DebugDisplaySubjectId(session, value);
		}

		public bool IsValid
		{
			get { return m_Session.IsValid && m_Value > 0; }
		}

		public bool Equals(DebugDisplaySubjectId other)
		{
			return m_Session == other.m_Session && m_Value == other.m_Value;
		}

		public override bool Equals(object obj)
		{
			return obj is DebugDisplaySubjectId && Equals((DebugDisplaySubjectId)obj);
		}

		public override int GetHashCode()
		{
			return HashCode.Combine(m_Session, m_Value);
		}

		public override string ToString()
		{
			return IsValid ? $"display-subject:{m_Value}" : "display-subject:invalid";
		}

		public static bool operator ==(DebugDisplaySubjectId left, DebugDisplaySubjectId right)
		{
			return left.Equals(right);
		}

		public static bool operator !=(DebugDisplaySubjectId left, DebugDisplaySubjectId right)
		{
			return !left.Equals(right);
		}
	}

	public sealed class DebugDisplayOwnerSnapshot
	{
		internal DebugDisplayOwnerSnapshot(
			DebugObjectHandle target,
			string typeName,
			string sceneName,
			string hierarchyPath,
			bool alive,
			Vector3 position,
			Quaternion rotation,
			Vector3 scale
		)
		{
			Target = target;
			TypeName = typeName ?? string.Empty;
			SceneName = sceneName ?? string.Empty;
			HierarchyPath = hierarchyPath ?? string.Empty;
			Alive = alive;
			Position = position;
			Rotation = rotation;
			Scale = scale;
		}

		public DebugObjectHandle Target { get; }
		public string TypeName { get; }
		public string SceneName { get; }
		public string HierarchyPath { get; }
		public bool Alive { get; }
		public Vector3 Position { get; }
		public Quaternion Rotation { get; }
		public Vector3 Scale { get; }
	}

	public sealed class DebugDisplayMetadataSnapshot
	{
		internal DebugDisplayMetadataSnapshot(string name, DebugValue value)
		{
			Name = name ?? string.Empty;
			Value = value ?? throw new ArgumentNullException(nameof(value));
		}

		public string Name { get; }
		public DebugValue Value { get; }
	}

	public sealed class DebugDisplayVisualSnapshot
	{
		internal DebugDisplayVisualSnapshot(
			DebugDisplayPrimitiveKind kind,
			string name,
			string path,
			Color color,
			DebugDisplayTimeScope timeScope,
			DebugDisplaySpace space,
			string text,
			DebugValue value,
			Vector3 start,
			Vector3 end,
			Vector3 center,
			Vector3 size,
			Vector3 normal,
			float radius,
			Quaternion rotation,
			IEnumerable<Vector3> points,
			DebugDisplaySubjectId source,
			DebugDisplaySubjectId target,
			DebugDisplayOwnerSnapshot targetOwner,
			DebugDisplayOwnerSnapshot basis,
			double expiresAt,
			bool hasExpiry,
			bool targetMissing,
			DebugDisplayDrawStyle drawStyle = DebugDisplayDrawStyle.Wire,
			Color fillColor = default(Color),
			float height = 0f
		)
		{
			DrawStyle = drawStyle;
			FillColor = fillColor;
			Height = height;
			Kind = kind;
			Name = name ?? string.Empty;
			Path = path ?? string.Empty;
			Color = color;
			TimeScope = timeScope;
			Space = space;
			Text = text ?? string.Empty;
			Value = value;
			Start = start;
			End = end;
			Center = center;
			Size = size;
			Normal = normal;
			Radius = radius;
			Rotation = rotation;
			Points = new ReadOnlyCollection<Vector3>((points ?? Enumerable.Empty<Vector3>()).ToList());
			Source = source;
			Target = target;
			TargetOwner = targetOwner;
			Basis = basis;
			ExpiresAt = expiresAt;
			HasExpiry = hasExpiry;
			TargetMissing = targetMissing;
		}

		public DebugDisplayDrawStyle DrawStyle { get; }
		public Color FillColor { get; }
		public float Height { get; }
		public DebugDisplayPrimitiveKind Kind { get; }
		public string Name { get; }
		public string Path { get; }
		public Color Color { get; }
		public DebugDisplayTimeScope TimeScope { get; }
		public DebugDisplaySpace Space { get; }
		public string Text { get; }
		public DebugValue Value { get; }
		public Vector3 Start { get; }
		public Vector3 End { get; }
		public Vector3 Center { get; }
		public Vector3 Size { get; }
		public Vector3 Normal { get; }
		public float Radius { get; }
		public Quaternion Rotation { get; }
		public IReadOnlyList<Vector3> Points { get; }
		public DebugDisplaySubjectId Source { get; }
		public DebugDisplaySubjectId Target { get; }
		public DebugDisplayOwnerSnapshot TargetOwner { get; }
		public DebugDisplayOwnerSnapshot Basis { get; }
		public double ExpiresAt { get; }
		public bool HasExpiry { get; }
		public bool TargetMissing { get; }

		internal DebugDisplayVisualSnapshot WithMissingTarget()
		{
			return new DebugDisplayVisualSnapshot(
				Kind,
				Name,
				Path,
				Color,
				TimeScope,
				Space,
				Text,
				Value,
				Start,
				End,
				Center,
				Size,
				Normal,
				Radius,
				Rotation,
				Points,
				Source,
				default(DebugDisplaySubjectId),
				null,
				Basis,
				ExpiresAt,
				HasExpiry,
				true,
				DrawStyle,
				FillColor,
				Height
			);
		}
	}

	public sealed class DebugDrawOptions
	{
		private DebugDrawOptions(
			string path,
			Color color,
			DebugDisplayTimeScope timeScope,
			float durationSeconds,
			DebugDisplayDrawStyle drawStyle = DebugDisplayDrawStyle.Wire,
			Color? fillColor = null
		)
		{
			if (string.IsNullOrWhiteSpace(path))
				throw new ArgumentException("Display path must not be empty.", nameof(path));
			if (!IsFinite(color.r) || !IsFinite(color.g) || !IsFinite(color.b) || !IsFinite(color.a))
				throw new ArgumentOutOfRangeException(nameof(color), "Display color must be finite.");
			if (!Enum.IsDefined(typeof(DebugDisplayTimeScope), timeScope))
				throw new ArgumentOutOfRangeException(nameof(timeScope));
			if (timeScope == DebugDisplayTimeScope.Frame && durationSeconds != 0f)
				throw new ArgumentException("Frame options must have zero duration.", nameof(durationSeconds));
			if (
				(timeScope == DebugDisplayTimeScope.Realtime || timeScope == DebugDisplayTimeScope.GameTime)
				&& (durationSeconds <= 0f || float.IsNaN(durationSeconds) || float.IsInfinity(durationSeconds))
			)
				throw new ArgumentOutOfRangeException(nameof(durationSeconds));
			if (timeScope == DebugDisplayTimeScope.Infinity && durationSeconds != 0f)
				throw new ArgumentException("Infinity options must have zero duration.", nameof(durationSeconds));
			if (!Enum.IsDefined(typeof(DebugDisplayDrawStyle), drawStyle))
				throw new ArgumentOutOfRangeException(nameof(drawStyle));
			var fill = fillColor ?? new Color(color.r, color.g, color.b, color.a * 0.25f);
			if (!IsFinite(fill.r) || !IsFinite(fill.g) || !IsFinite(fill.b) || !IsFinite(fill.a))
				throw new ArgumentOutOfRangeException(nameof(fillColor));
			DrawStyle = drawStyle;
			FillColor = fill;
			Path = path;
			Color = color;
			TimeScope = timeScope;
			DurationSeconds = durationSeconds;
		}

		public DebugDisplayDrawStyle DrawStyle { get; }
		public Color FillColor { get; }

		public DebugDrawOptions WithStyle(DebugDisplayDrawStyle style)
		{
			return new DebugDrawOptions(Path, Color, TimeScope, DurationSeconds, style, FillColor);
		}

		public DebugDrawOptions WithStyle(DebugDisplayDrawStyle style, Color fillColor)
		{
			return new DebugDrawOptions(Path, Color, TimeScope, DurationSeconds, style, fillColor);
		}

		public string Path { get; }
		public Color Color { get; }
		public DebugDisplayTimeScope TimeScope { get; }
		public float DurationSeconds { get; }

		public static DebugDrawOptions Frame(string path, Color color)
		{
			return new DebugDrawOptions(path, color, DebugDisplayTimeScope.Frame, 0f);
		}

		public static DebugDrawOptions Realtime(string path, Color color, float durationSeconds)
		{
			return new DebugDrawOptions(path, color, DebugDisplayTimeScope.Realtime, durationSeconds);
		}

		public static DebugDrawOptions GameTime(string path, Color color, float durationSeconds)
		{
			return new DebugDrawOptions(path, color, DebugDisplayTimeScope.GameTime, durationSeconds);
		}

		public static DebugDrawOptions Infinity(string path, Color color)
		{
			return new DebugDrawOptions(path, color, DebugDisplayTimeScope.Infinity, 0f);
		}

		internal static DebugDrawOptions Create(string path, Color color, DebugDisplayTimeScope scope, float durationSeconds)
		{
			return new DebugDrawOptions(path, color, scope, durationSeconds);
		}

		private static bool IsFinite(float value)
		{
			return !float.IsNaN(value) && !float.IsInfinity(value);
		}
	}

	public sealed class DebugDisplaySubjectSnapshot
	{
		internal DebugDisplaySubjectSnapshot(
			DebugDisplaySubjectId id,
			string category,
			string label,
			DebugDisplayOwnerSnapshot owner,
			IEnumerable<DebugDisplayMetadataSnapshot> metadata,
			IEnumerable<DebugDisplayVisualSnapshot> visuals
		)
		{
			Id = id;
			Category = category ?? string.Empty;
			Label = label ?? string.Empty;
			Owner = owner;
			Metadata = new ReadOnlyCollection<DebugDisplayMetadataSnapshot>(
				(metadata ?? Enumerable.Empty<DebugDisplayMetadataSnapshot>()).ToList()
			);
			Visuals = new ReadOnlyCollection<DebugDisplayVisualSnapshot>(
				(visuals ?? Enumerable.Empty<DebugDisplayVisualSnapshot>()).ToList()
			);
		}

		public DebugDisplaySubjectId Id { get; }
		public string Category { get; }
		public string Label { get; }
		public DebugDisplayOwnerSnapshot Owner { get; }
		public IReadOnlyList<DebugDisplayMetadataSnapshot> Metadata { get; }
		public IReadOnlyList<DebugDisplayVisualSnapshot> Visuals { get; }
	}

	public sealed class DebugDisplayFrameSnapshot
	{
		internal DebugDisplayFrameSnapshot(
			long frame,
			double timestamp,
			IEnumerable<DebugDisplaySubjectSnapshot> subjects,
			IEnumerable<DebugDisplayVisualSnapshot> globals,
			int droppedCount
		)
		{
			Frame = frame;
			Timestamp = timestamp;
			Subjects = new ReadOnlyCollection<DebugDisplaySubjectSnapshot>(
				(subjects ?? Enumerable.Empty<DebugDisplaySubjectSnapshot>()).ToList()
			);
			Globals = new ReadOnlyCollection<DebugDisplayVisualSnapshot>(
				(globals ?? Enumerable.Empty<DebugDisplayVisualSnapshot>()).ToList()
			);
			DroppedCount = droppedCount;
		}

		public long Frame { get; }
		public double Timestamp { get; }
		public IReadOnlyList<DebugDisplaySubjectSnapshot> Subjects { get; }
		public IReadOnlyList<DebugDisplayVisualSnapshot> Globals { get; }
		public int DroppedCount { get; }
	}

	public static class DebugDisplay
	{
		public static DebugDisplaySubject CreateSubject(string category, UnityEngine.Object owner)
		{
#if DEXC_DEBUGGER
			return DexcDisplayRuntime.Registry?.CreateSubject(category, owner) ?? DebugDisplaySubject.NoOp;
#else
			return DebugDisplaySubject.NoOp;
#endif
		}

		public static bool TryGetCompletedFrame(out DebugDisplayFrameSnapshot frame)
		{
#if DEXC_DEBUGGER
			var registry = DexcDisplayRuntime.Registry;
			if (registry == null)
			{
				frame = null;
				return false;
			}
			return registry.TryGetCompletedFrame(out frame);
#else
			frame = null;
			return false;
#endif
		}

		[Conditional("DEXC_DEBUGGER")]
		public static void DrawValue(string path, DebugValue value)
		{
			DexcDisplayRuntime.Registry?.Draw(
				null,
				DebugDisplayPrimitiveKind.Value,
				path,
				DebugDrawOptions.Frame(path, Color.white),
				DexcDisplayVisualPayload.Value(value)
			);
		}

		[Conditional("DEXC_DEBUGGER")]
		public static void DrawValue(string path, float value)
		{
			DrawValue(path, DebugValue.From(value));
		}

		[Conditional("DEXC_DEBUGGER")]
		public static void DrawValue(string path, double value)
		{
			DrawValue(path, DebugValue.From(value));
		}

		[Conditional("DEXC_DEBUGGER")]
		public static void DrawValue(string path, int value)
		{
			DrawValue(path, DebugValue.From(value));
		}

		[Conditional("DEXC_DEBUGGER")]
		public static void DrawValue(string path, string value)
		{
			DrawValue(path, DebugValue.From(value));
		}

		[Conditional("DEXC_DEBUGGER")]
		/// <summary>Registers a HUD widget that is drawn every frame while the session is initialized.</summary>
		public static void RegisterWidget(DebugDisplayWidget widget)
		{
			DexcDisplayWidgets.Register(widget);
		}

		public static void UnregisterWidget(DebugDisplayWidget widget)
		{
			DexcDisplayWidgets.Unregister(widget);
		}

#if UNITY_INCLUDE_TESTS
		internal static void ResetWidgetsForTests()
		{
			DexcDisplayWidgets.Clear();
		}
#endif

		public static void DrawLabel(string path, string text)
		{
			DrawLabel(path, text, Color.white);
		}

		[Conditional("DEXC_DEBUGGER")]
		public static void DrawLabel(string path, string text, Color color)
		{
			DexcDisplayRuntime.Registry?.Draw(
				null,
				DebugDisplayPrimitiveKind.Label,
				path,
				DebugDrawOptions.Frame(path, color),
				DexcDisplayVisualPayload.Label(text)
			);
		}

		[Conditional("DEXC_DEBUGGER")]
		public static void DrawLine(string path, Vector3 start, Vector3 end)
		{
			DrawLine(path, start, end, Color.white);
		}

		[Conditional("DEXC_DEBUGGER")]
		public static void DrawLine(string path, Vector3 start, Vector3 end, Color color)
		{
			DexcDisplayRuntime.Registry?.Draw(
				null,
				DebugDisplayPrimitiveKind.Line,
				path,
				DebugDrawOptions.Frame(path, color),
				DexcDisplayVisualPayload.Line(start, end)
			);
		}

		[Conditional("DEXC_DEBUGGER")]
		public static void DrawRay(string path, Vector3 origin, Vector3 direction)
		{
			DrawRay(path, origin, direction, Color.white);
		}

		[Conditional("DEXC_DEBUGGER")]
		public static void DrawRay(string path, Vector3 origin, Vector3 direction, Color color)
		{
			DexcDisplayRuntime.Registry?.Draw(
				null,
				DebugDisplayPrimitiveKind.Ray,
				path,
				DebugDrawOptions.Frame(path, color),
				DexcDisplayVisualPayload.Line(origin, origin + direction)
			);
		}

		[Conditional("DEXC_DEBUGGER")]
		public static void DrawPolyLine(
			string path,
			IEnumerable<Vector3> points,
			Color color,
			DebugDisplaySpace space = DebugDisplaySpace.World,
			Transform basis = null
		)
		{
			DexcDisplayRuntime.Registry?.Draw(
				null,
				DebugDisplayPrimitiveKind.PolyLine,
				path,
				DebugDrawOptions.Frame(path, color),
				DexcDisplayVisualPayload.PolyLine(points, space, basis)
			);
		}

		[Conditional("DEXC_DEBUGGER")]
		public static void DrawPolyLine(
			string path,
			ReadOnlySpan<Vector3> points,
			Color color,
			DebugDisplaySpace space = DebugDisplaySpace.World,
			Transform basis = null
		)
		{
			DexcDisplayRuntime.Registry?.Draw(
				null,
				DebugDisplayPrimitiveKind.PolyLine,
				path,
				DebugDrawOptions.Frame(path, color),
				DexcDisplayVisualPayload.PolyLine(points, space, basis)
			);
		}

		[Conditional("DEXC_DEBUGGER")]
		public static void DrawPolyLine(
			string path,
			Vector3[] points,
			Color color,
			DebugDisplaySpace space = DebugDisplaySpace.World,
			Transform basis = null
		)
		{
			DexcDisplayRuntime.Registry?.Draw(
				null,
				DebugDisplayPrimitiveKind.PolyLine,
				path,
				DebugDrawOptions.Frame(path, color),
				DexcDisplayVisualPayload.PolyLine((ReadOnlySpan<Vector3>)points, space, basis)
			);
		}

		[Conditional("DEXC_DEBUGGER")]
		public static void DrawWireSphere(
			string path,
			Vector3 center,
			float radius,
			Color color,
			DebugDisplaySpace space = DebugDisplaySpace.World,
			Transform basis = null
		)
		{
			DexcDisplayRuntime.Registry?.Draw(
				null,
				DebugDisplayPrimitiveKind.WireSphere,
				path,
				DebugDrawOptions.Frame(path, color),
				DexcDisplayVisualPayload.Sphere(center, radius, space, basis)
			);
		}

		[Conditional("DEXC_DEBUGGER")]
		public static void DrawWireCube(
			string path,
			Vector3 center,
			Vector3 size,
			Quaternion rotation,
			Color color,
			DebugDisplaySpace space = DebugDisplaySpace.World,
			Transform basis = null
		)
		{
			DexcDisplayRuntime.Registry?.Draw(
				null,
				DebugDisplayPrimitiveKind.WireCube,
				path,
				DebugDrawOptions.Frame(path, color),
				DexcDisplayVisualPayload.Cube(center, size, rotation, space, basis)
			);
		}

		[Conditional("DEXC_DEBUGGER")]
		public static void DrawWireCube(
			string path,
			Vector3 center,
			Vector3 size,
			Color color,
			DebugDisplaySpace space = DebugDisplaySpace.World,
			Transform basis = null
		)
		{
			DrawWireCube(path, center, size, Quaternion.identity, color, space, basis);
		}

		[Conditional("DEXC_DEBUGGER")]
		public static void DrawWireDisc(
			string path,
			Vector3 center,
			Vector3 normal,
			float radius,
			Color color,
			DebugDisplaySpace space = DebugDisplaySpace.World,
			Transform basis = null
		)
		{
			DexcDisplayRuntime.Registry?.Draw(
				null,
				DebugDisplayPrimitiveKind.WireDisc,
				path,
				DebugDrawOptions.Frame(path, color),
				DexcDisplayVisualPayload.Disc(center, normal, radius, space, basis)
			);
		}

		[Conditional("DEXC_DEBUGGER")]
		public static void DrawRelation(string path, UnityEngine.Object target, Color color)
		{
			DexcDisplayRuntime.Registry?.Draw(
				null,
				DebugDisplayPrimitiveKind.Relation,
				path,
				DebugDrawOptions.Frame(path, color),
				DexcDisplayVisualPayload.Relation(target)
			);
		}

		[Conditional("DEXC_DEBUGGER")]
		public static void DrawRelation(string path, UnityEngine.Object target)
		{
			DrawRelation(path, target, Color.white);
		}

		[Conditional("DEXC_DEBUGGER")]
		public static void DrawRelation(string path, DebugDisplaySubject target, Color color)
		{
			DexcDisplayRuntime.Registry?.Draw(
				null,
				DebugDisplayPrimitiveKind.Relation,
				path,
				DebugDrawOptions.Frame(path, color),
				DexcDisplayVisualPayload.Relation(target)
			);
		}

		[Conditional("DEXC_DEBUGGER")]
		public static void DrawRelation(string path, DebugDisplaySubject target)
		{
			DrawRelation(path, target, Color.white);
		}

		[Conditional("DEXC_DEBUGGER")]
		public static void DrawLabelFor(string path, string text, float durationSeconds, DebugDisplayTimeScope scope)
		{
			DrawLabelFor(path, text, Color.white, durationSeconds, scope);
		}

		[Conditional("DEXC_DEBUGGER")]
		public static void DrawLabelFor(string path, string text, Color color, float durationSeconds, DebugDisplayTimeScope scope)
		{
			DexcDisplayRuntime.Registry?.Draw(
				null,
				DebugDisplayPrimitiveKind.Label,
				path,
				DexcDisplayRegistry.Options(path, color, scope, durationSeconds),
				DexcDisplayVisualPayload.Label(text)
			);
		}

		[Conditional("DEXC_DEBUGGER")]
		public static void DrawPolyLineFor(
			string path,
			IEnumerable<Vector3> points,
			Color color,
			float durationSeconds,
			DebugDisplayTimeScope scope,
			DebugDisplaySpace space = DebugDisplaySpace.World,
			Transform basis = null
		)
		{
			DexcDisplayRuntime.Registry?.Draw(
				null,
				DebugDisplayPrimitiveKind.PolyLine,
				path,
				DexcDisplayRegistry.Options(path, color, scope, durationSeconds),
				DexcDisplayVisualPayload.PolyLine(points, space, basis)
			);
		}

		[Conditional("DEXC_DEBUGGER")]
		public static void DrawPolyLineFor(
			string path,
			ReadOnlySpan<Vector3> points,
			Color color,
			float durationSeconds,
			DebugDisplayTimeScope scope,
			DebugDisplaySpace space = DebugDisplaySpace.World,
			Transform basis = null
		)
		{
			DexcDisplayRuntime.Registry?.Draw(
				null,
				DebugDisplayPrimitiveKind.PolyLine,
				path,
				DexcDisplayRegistry.Options(path, color, scope, durationSeconds),
				DexcDisplayVisualPayload.PolyLine(points, space, basis)
			);
		}

		[Conditional("DEXC_DEBUGGER")]
		public static void DrawPolyLineFor(
			string path,
			Vector3[] points,
			Color color,
			float durationSeconds,
			DebugDisplayTimeScope scope,
			DebugDisplaySpace space = DebugDisplaySpace.World,
			Transform basis = null
		)
		{
			DexcDisplayRuntime.Registry?.Draw(
				null,
				DebugDisplayPrimitiveKind.PolyLine,
				path,
				DexcDisplayRegistry.Options(path, color, scope, durationSeconds),
				DexcDisplayVisualPayload.PolyLine((ReadOnlySpan<Vector3>)points, space, basis)
			);
		}

		[Conditional("DEXC_DEBUGGER")]
		public static void DrawWireSphereFor(
			string path,
			Vector3 center,
			float radius,
			Color color,
			float durationSeconds,
			DebugDisplayTimeScope scope,
			DebugDisplaySpace space = DebugDisplaySpace.World,
			Transform basis = null
		)
		{
			DexcDisplayRuntime.Registry?.Draw(
				null,
				DebugDisplayPrimitiveKind.WireSphere,
				path,
				DexcDisplayRegistry.Options(path, color, scope, durationSeconds),
				DexcDisplayVisualPayload.Sphere(center, radius, space, basis)
			);
		}

		[Conditional("DEXC_DEBUGGER")]
		public static void DrawValueFor(string path, DebugValue value, float durationSeconds, DebugDisplayTimeScope scope)
		{
			DexcDisplayRuntime.Registry?.Draw(
				null,
				DebugDisplayPrimitiveKind.Value,
				path,
				DexcDisplayRegistry.Options(path, Color.white, scope, durationSeconds),
				DexcDisplayVisualPayload.Value(value)
			);
		}

		[Conditional("DEXC_DEBUGGER")]
		public static void DrawLineFor(
			string path,
			Vector3 start,
			Vector3 end,
			Color color,
			float durationSeconds,
			DebugDisplayTimeScope scope
		)
		{
			DexcDisplayRuntime.Registry?.Draw(
				null,
				DebugDisplayPrimitiveKind.Line,
				path,
				DexcDisplayRegistry.Options(path, color, scope, durationSeconds),
				DexcDisplayVisualPayload.Line(start, end)
			);
		}

		[Conditional("DEXC_DEBUGGER")]
		public static void DrawRayFor(
			string path,
			Vector3 origin,
			Vector3 direction,
			Color color,
			float durationSeconds,
			DebugDisplayTimeScope scope
		)
		{
			DexcDisplayRuntime.Registry?.Draw(
				null,
				DebugDisplayPrimitiveKind.Ray,
				path,
				DexcDisplayRegistry.Options(path, color, scope, durationSeconds),
				DexcDisplayVisualPayload.Line(origin, origin + direction)
			);
		}

		[Conditional("DEXC_DEBUGGER")]
		public static void DrawWireCubeFor(
			string path,
			Vector3 center,
			Vector3 size,
			Quaternion rotation,
			Color color,
			float durationSeconds,
			DebugDisplayTimeScope scope,
			DebugDisplaySpace space = DebugDisplaySpace.World,
			Transform basis = null
		)
		{
			DexcDisplayRuntime.Registry?.Draw(
				null,
				DebugDisplayPrimitiveKind.WireCube,
				path,
				DexcDisplayRegistry.Options(path, color, scope, durationSeconds),
				DexcDisplayVisualPayload.Cube(center, size, rotation, space, basis)
			);
		}

		[Conditional("DEXC_DEBUGGER")]
		public static void DrawWireDiscFor(
			string path,
			Vector3 center,
			Vector3 normal,
			float radius,
			Color color,
			float durationSeconds,
			DebugDisplayTimeScope scope,
			DebugDisplaySpace space = DebugDisplaySpace.World,
			Transform basis = null
		)
		{
			DexcDisplayRuntime.Registry?.Draw(
				null,
				DebugDisplayPrimitiveKind.WireDisc,
				path,
				DexcDisplayRegistry.Options(path, color, scope, durationSeconds),
				DexcDisplayVisualPayload.Disc(center, normal, radius, space, basis)
			);
		}

		[Conditional("DEXC_DEBUGGER")]
		public static void DrawRelationFor(
			string path,
			UnityEngine.Object target,
			Color color,
			float durationSeconds,
			DebugDisplayTimeScope scope
		)
		{
			DexcDisplayRuntime.Registry?.Draw(
				null,
				DebugDisplayPrimitiveKind.Relation,
				path,
				DexcDisplayRegistry.Options(path, color, scope, durationSeconds),
				DexcDisplayVisualPayload.Relation(target)
			);
		}

		[Conditional("DEXC_DEBUGGER")]
		public static void DrawRelationFor(
			string path,
			DebugDisplaySubject target,
			Color color,
			float durationSeconds,
			DebugDisplayTimeScope scope
		)
		{
			DexcDisplayRuntime.Registry?.Draw(
				null,
				DebugDisplayPrimitiveKind.Relation,
				path,
				DexcDisplayRegistry.Options(path, color, scope, durationSeconds),
				DexcDisplayVisualPayload.Relation(target)
			);
		}

		[Conditional("DEXC_DEBUGGER")]
		public static void DrawValue(DebugDrawOptions options, DebugValue value)
		{
			DexcDisplayRuntime.Registry?.Draw(
				null,
				DebugDisplayPrimitiveKind.Value,
				options == null ? string.Empty : options.Path,
				options,
				DexcDisplayVisualPayload.Value(value)
			);
		}

		[Conditional("DEXC_DEBUGGER")]
		public static void DrawLabel(DebugDrawOptions options, string text)
		{
			DexcDisplayRuntime.Registry?.Draw(
				null,
				DebugDisplayPrimitiveKind.Label,
				options == null ? string.Empty : options.Path,
				options,
				DexcDisplayVisualPayload.Label(text)
			);
		}

		[Conditional("DEXC_DEBUGGER")]
		public static void DrawSphere(
			DebugDrawOptions options,
			Vector3 center,
			float radius,
			DebugDisplaySpace space = DebugDisplaySpace.World,
			Transform basis = null
		)
		{
			DexcDisplayRuntime.Registry?.Draw(
				null,
				DebugDisplayPrimitiveKind.Sphere,
				options == null ? string.Empty : options.Path,
				options,
				DexcDisplayVisualPayload.Sphere(center, radius, space, basis)
			);
		}

		[Conditional("DEXC_DEBUGGER")]
		public static void DrawSphere(
			string path,
			Vector3 center,
			float radius,
			Color color,
			DebugDisplayDrawStyle style = DebugDisplayDrawStyle.Wire,
			DebugDisplaySpace space = DebugDisplaySpace.World,
			Transform basis = null
		)
		{
			DrawSphere(DebugDrawOptions.Frame(path, color).WithStyle(style), center, radius, space, basis);
		}

		[Conditional("DEXC_DEBUGGER")]
		public static void DrawCube(
			DebugDrawOptions options,
			Vector3 center,
			Vector3 size,
			Quaternion rotation,
			DebugDisplaySpace space = DebugDisplaySpace.World,
			Transform basis = null
		)
		{
			DexcDisplayRuntime.Registry?.Draw(
				null,
				DebugDisplayPrimitiveKind.Cube,
				options == null ? string.Empty : options.Path,
				options,
				DexcDisplayVisualPayload.Cube(center, size, rotation, space, basis)
			);
		}

		[Conditional("DEXC_DEBUGGER")]
		public static void DrawCube(
			string path,
			Vector3 center,
			Vector3 size,
			Quaternion rotation,
			Color color,
			DebugDisplayDrawStyle style = DebugDisplayDrawStyle.Wire,
			DebugDisplaySpace space = DebugDisplaySpace.World,
			Transform basis = null
		)
		{
			DrawCube(DebugDrawOptions.Frame(path, color).WithStyle(style), center, size, rotation, space, basis);
		}

		[Conditional("DEXC_DEBUGGER")]
		public static void DrawDisc(
			DebugDrawOptions options,
			Vector3 center,
			Vector3 normal,
			float radius,
			DebugDisplaySpace space = DebugDisplaySpace.World,
			Transform basis = null
		)
		{
			DexcDisplayRuntime.Registry?.Draw(
				null,
				DebugDisplayPrimitiveKind.Disc,
				options == null ? string.Empty : options.Path,
				options,
				DexcDisplayVisualPayload.Disc(center, normal, radius, space, basis)
			);
		}

		[Conditional("DEXC_DEBUGGER")]
		public static void DrawDisc(
			string path,
			Vector3 center,
			Vector3 normal,
			float radius,
			Color color,
			DebugDisplayDrawStyle style = DebugDisplayDrawStyle.Wire,
			DebugDisplaySpace space = DebugDisplaySpace.World,
			Transform basis = null
		)
		{
			DrawDisc(DebugDrawOptions.Frame(path, color).WithStyle(style), center, normal, radius, space, basis);
		}

		[Conditional("DEXC_DEBUGGER")]
		public static void DrawCapsule(
			DebugDrawOptions options,
			Vector3 center,
			float radius,
			float height,
			Quaternion rotation,
			DebugDisplaySpace space = DebugDisplaySpace.World,
			Transform basis = null
		)
		{
			DexcDisplayRuntime.Registry?.Draw(
				null,
				DebugDisplayPrimitiveKind.Capsule,
				options == null ? string.Empty : options.Path,
				options,
				DexcDisplayVisualPayload.Capsule(center, radius, height, rotation, space, basis)
			);
		}

		[Conditional("DEXC_DEBUGGER")]
		public static void DrawCapsule(
			string path,
			Vector3 center,
			float radius,
			float height,
			Quaternion rotation,
			Color color,
			DebugDisplayDrawStyle style = DebugDisplayDrawStyle.Wire,
			DebugDisplaySpace space = DebugDisplaySpace.World,
			Transform basis = null
		)
		{
			DrawCapsule(DebugDrawOptions.Frame(path, color).WithStyle(style), center, radius, height, rotation, space, basis);
		}

		[Conditional("DEXC_DEBUGGER")]
		public static void DrawLine(DebugDrawOptions options, Vector3 start, Vector3 end)
		{
			DexcDisplayRuntime.Registry?.Draw(
				null,
				DebugDisplayPrimitiveKind.Line,
				options == null ? string.Empty : options.Path,
				options,
				DexcDisplayVisualPayload.Line(start, end)
			);
		}

		[Conditional("DEXC_DEBUGGER")]
		public static void DrawRay(DebugDrawOptions options, Vector3 origin, Vector3 direction)
		{
			DexcDisplayRuntime.Registry?.Draw(
				null,
				DebugDisplayPrimitiveKind.Ray,
				options == null ? string.Empty : options.Path,
				options,
				DexcDisplayVisualPayload.Line(origin, origin + direction)
			);
		}

		[Conditional("DEXC_DEBUGGER")]
		public static void DrawPolyLine(
			DebugDrawOptions options,
			IEnumerable<Vector3> points,
			DebugDisplaySpace space = DebugDisplaySpace.World,
			Transform basis = null
		)
		{
			DexcDisplayRuntime.Registry?.Draw(
				null,
				DebugDisplayPrimitiveKind.PolyLine,
				options == null ? string.Empty : options.Path,
				options,
				DexcDisplayVisualPayload.PolyLine(points, space, basis)
			);
		}

		[Conditional("DEXC_DEBUGGER")]
		public static void DrawPolyLine(
			DebugDrawOptions options,
			ReadOnlySpan<Vector3> points,
			DebugDisplaySpace space = DebugDisplaySpace.World,
			Transform basis = null
		)
		{
			DexcDisplayRuntime.Registry?.Draw(
				null,
				DebugDisplayPrimitiveKind.PolyLine,
				options == null ? string.Empty : options.Path,
				options,
				DexcDisplayVisualPayload.PolyLine(points, space, basis)
			);
		}

		[Conditional("DEXC_DEBUGGER")]
		public static void DrawPolyLine(
			DebugDrawOptions options,
			Vector3[] points,
			DebugDisplaySpace space = DebugDisplaySpace.World,
			Transform basis = null
		)
		{
			DexcDisplayRuntime.Registry?.Draw(
				null,
				DebugDisplayPrimitiveKind.PolyLine,
				options == null ? string.Empty : options.Path,
				options,
				DexcDisplayVisualPayload.PolyLine((ReadOnlySpan<Vector3>)points, space, basis)
			);
		}

		[Conditional("DEXC_DEBUGGER")]
		public static void DrawWireSphere(
			DebugDrawOptions options,
			Vector3 center,
			float radius,
			DebugDisplaySpace space = DebugDisplaySpace.World,
			Transform basis = null
		)
		{
			DexcDisplayRuntime.Registry?.Draw(
				null,
				DebugDisplayPrimitiveKind.WireSphere,
				options == null ? string.Empty : options.Path,
				options,
				DexcDisplayVisualPayload.Sphere(center, radius, space, basis)
			);
		}

		[Conditional("DEXC_DEBUGGER")]
		public static void DrawWireCube(
			DebugDrawOptions options,
			Vector3 center,
			Vector3 size,
			Quaternion rotation,
			DebugDisplaySpace space = DebugDisplaySpace.World,
			Transform basis = null
		)
		{
			DexcDisplayRuntime.Registry?.Draw(
				null,
				DebugDisplayPrimitiveKind.WireCube,
				options == null ? string.Empty : options.Path,
				options,
				DexcDisplayVisualPayload.Cube(center, size, rotation, space, basis)
			);
		}

		[Conditional("DEXC_DEBUGGER")]
		public static void DrawWireCube(
			DebugDrawOptions options,
			Vector3 center,
			Vector3 size,
			DebugDisplaySpace space = DebugDisplaySpace.World,
			Transform basis = null
		)
		{
			DrawWireCube(options, center, size, Quaternion.identity, space, basis);
		}

		[Conditional("DEXC_DEBUGGER")]
		public static void DrawWireDisc(
			DebugDrawOptions options,
			Vector3 center,
			Vector3 normal,
			float radius,
			DebugDisplaySpace space = DebugDisplaySpace.World,
			Transform basis = null
		)
		{
			DexcDisplayRuntime.Registry?.Draw(
				null,
				DebugDisplayPrimitiveKind.WireDisc,
				options == null ? string.Empty : options.Path,
				options,
				DexcDisplayVisualPayload.Disc(center, normal, radius, space, basis)
			);
		}

		/// <summary>
		/// Draws the outline of a circular sector: two edges from <paramref name="center"/> and the arc between them, such
		/// as a view cone seen from above. <paramref name="angle"/> is the full opening in degrees, split evenly around
		/// <paramref name="forward"/>. The sector lies in the plane through <paramref name="forward"/> that is
		/// perpendicular to <paramref name="normal"/>. It is recorded as one closed PolyLine, so filters, snapshots and
		/// remote tools see it as a path.
		/// </summary>
		[Conditional("DEXC_DEBUGGER")]
		public static void DrawWireSector(
			DebugDrawOptions options,
			Vector3 center,
			Vector3 normal,
			Vector3 forward,
			float angle,
			float radius,
			DebugDisplaySpace space = DebugDisplaySpace.World,
			Transform basis = null
		)
		{
			DrawPolyLine(options, SectorPoints(center, normal, forward, angle, radius), space, basis);
		}

		/// <summary>The centre, the arc from one edge to the other, and the centre again.</summary>
		private static List<Vector3> SectorPoints(Vector3 center, Vector3 normal, Vector3 forward, float angle, float radius)
		{
			var axis = normal.normalized;
			var direction = Vector3.ProjectOnPlane(forward, axis).normalized;
			var opening = Mathf.Clamp(angle, 0f, 360f);
			// About one point every 7.5 degrees: smooth at view-cone sizes, well under the polyline point limit.
			var segments = Mathf.Max(2, Mathf.CeilToInt(opening / 7.5f));
			var points = new List<Vector3>(segments + 3) { center };
			for (var i = 0; i <= segments; i++)
			{
				var step = -opening * 0.5f + opening * i / segments;
				points.Add(center + Quaternion.AngleAxis(step, axis) * direction * radius);
			}
			points.Add(center);
			return points;
		}

		[Conditional("DEXC_DEBUGGER")]
		public static void DrawRelation(DebugDrawOptions options, UnityEngine.Object target)
		{
			DexcDisplayRuntime.Registry?.Draw(
				null,
				DebugDisplayPrimitiveKind.Relation,
				options == null ? string.Empty : options.Path,
				options,
				DexcDisplayVisualPayload.Relation(target)
			);
		}

		[Conditional("DEXC_DEBUGGER")]
		public static void DrawRelation(DebugDrawOptions options, DebugDisplaySubject target)
		{
			DexcDisplayRuntime.Registry?.Draw(
				null,
				DebugDisplayPrimitiveKind.Relation,
				options == null ? string.Empty : options.Path,
				options,
				DexcDisplayVisualPayload.Relation(target)
			);
		}

		[Conditional("DEXC_DEBUGGER")]
		public static void Clear(string path)
		{
			DexcDisplayRuntime.Registry?.Clear(null, path);
		}
	}

	public sealed class DebugDisplaySubject : IDisposable
	{
		private static readonly DebugDisplaySubject s_NoOp = new DebugDisplaySubject(null);
		private readonly DexcDisplaySubjectRecord m_Record;

		internal static DebugDisplaySubject NoOp
		{
			get { return s_NoOp; }
		}
		internal DexcDisplaySubjectRecord Record
		{
			get { return m_Record; }
		}

		internal DebugDisplaySubject(DexcDisplaySubjectRecord record)
		{
			m_Record = record;
		}

		public DebugDisplaySubjectId Id
		{
			get { return m_Record == null ? default(DebugDisplaySubjectId) : m_Record.Id; }
		}
		public string Category
		{
			get { return m_Record == null ? string.Empty : m_Record.Category; }
		}
		public string Label
		{
			get { return m_Record == null ? string.Empty : m_Record.Label; }
		}

		[Conditional("DEXC_DEBUGGER")]
		public void SetLabel(string label)
		{
			if (m_Record != null)
				m_Record.SetLabel(label);
		}

		[Conditional("DEXC_DEBUGGER")]
		public void SetMetadata(string name, bool value)
		{
			if (m_Record != null)
				m_Record.SetMetadata(name, value);
		}

		[Conditional("DEXC_DEBUGGER")]
		public void SetMetadata(string name, sbyte value)
		{
			if (m_Record != null)
				m_Record.SetMetadata(name, value);
		}

		[Conditional("DEXC_DEBUGGER")]
		public void SetMetadata(string name, short value)
		{
			if (m_Record != null)
				m_Record.SetMetadata(name, value);
		}

		[Conditional("DEXC_DEBUGGER")]
		public void SetMetadata(string name, int value)
		{
			if (m_Record != null)
				m_Record.SetMetadata(name, value);
		}

		[Conditional("DEXC_DEBUGGER")]
		public void SetMetadata(string name, long value)
		{
			if (m_Record != null)
				m_Record.SetMetadata(name, value);
		}

		[Conditional("DEXC_DEBUGGER")]
		public void SetMetadata(string name, byte value)
		{
			if (m_Record != null)
				m_Record.SetMetadata(name, value);
		}

		[Conditional("DEXC_DEBUGGER")]
		public void SetMetadata(string name, ushort value)
		{
			if (m_Record != null)
				m_Record.SetMetadata(name, value);
		}

		[Conditional("DEXC_DEBUGGER")]
		public void SetMetadata(string name, uint value)
		{
			if (m_Record != null)
				m_Record.SetMetadata(name, value);
		}

		[Conditional("DEXC_DEBUGGER")]
		public void SetMetadata(string name, ulong value)
		{
			if (m_Record != null)
				m_Record.SetMetadata(name, value);
		}

		[Conditional("DEXC_DEBUGGER")]
		public void SetMetadata(string name, float value)
		{
			if (m_Record != null)
				m_Record.SetMetadata(name, value);
		}

		[Conditional("DEXC_DEBUGGER")]
		public void SetMetadata(string name, double value)
		{
			if (m_Record != null)
				m_Record.SetMetadata(name, value);
		}

		[Conditional("DEXC_DEBUGGER")]
		public void SetMetadata(string name, string value)
		{
			if (m_Record != null)
				m_Record.SetMetadata(name, value);
		}

		[Conditional("DEXC_DEBUGGER")]
		public void SetMetadata(string name, Color value)
		{
			if (m_Record != null)
				m_Record.SetMetadata(name, value);
		}

		[Conditional("DEXC_DEBUGGER")]
		public void SetMetadata(string name, Vector2 value)
		{
			if (m_Record != null)
				m_Record.SetMetadata(name, value);
		}

		[Conditional("DEXC_DEBUGGER")]
		public void SetMetadata(string name, Vector3 value)
		{
			if (m_Record != null)
				m_Record.SetMetadata(name, value);
		}

		[Conditional("DEXC_DEBUGGER")]
		public void SetMetadata(string name, Vector4 value)
		{
			if (m_Record != null)
				m_Record.SetMetadata(name, value);
		}

		[Conditional("DEXC_DEBUGGER")]
		public void SetMetadata(string name, Vector2Int value)
		{
			if (m_Record != null)
				m_Record.SetMetadata(name, value);
		}

		[Conditional("DEXC_DEBUGGER")]
		public void SetMetadata(string name, Vector3Int value)
		{
			if (m_Record != null)
				m_Record.SetMetadata(name, value);
		}

		[Conditional("DEXC_DEBUGGER")]
		public void SetMetadata<TEnum>(string name, TEnum value)
			where TEnum : struct, Enum
		{
			if (m_Record != null)
				m_Record.SetMetadata(name, value);
		}

		[Conditional("DEXC_DEBUGGER")]
		public void SetMetadata(string name, DebugValue value)
		{
			if (m_Record != null)
				m_Record.SetMetadata(name, value);
		}

		[Conditional("DEXC_DEBUGGER")]
		public void RemoveMetadata(string name)
		{
			if (m_Record != null)
				m_Record.RemoveMetadata(name);
		}

		public void Dispose()
		{
			if (m_Record != null)
				m_Record.Dispose();
		}

		[Conditional("DEXC_DEBUGGER")]
		public void DrawValue(DebugDrawOptions options, DebugValue value)
		{
			if (m_Record != null)
				DexcDisplayRuntime.Registry?.Draw(
					m_Record,
					DebugDisplayPrimitiveKind.Value,
					options == null ? string.Empty : options.Path,
					options,
					DexcDisplayVisualPayload.Value(value)
				);
		}

		[Conditional("DEXC_DEBUGGER")]
		public void DrawValue(string path, DebugValue value)
		{
			if (m_Record != null)
				DexcDisplayRuntime.Registry?.Draw(
					m_Record,
					DebugDisplayPrimitiveKind.Value,
					path,
					DebugDrawOptions.Frame(path, Color.white),
					DexcDisplayVisualPayload.Value(value)
				);
		}

		[Conditional("DEXC_DEBUGGER")]
		public void DrawValueFor(string path, DebugValue value, float durationSeconds, DebugDisplayTimeScope scope)
		{
			if (m_Record != null)
				DexcDisplayRuntime.Registry?.Draw(
					m_Record,
					DebugDisplayPrimitiveKind.Value,
					path,
					DexcDisplayRegistry.Options(path, Color.white, scope, durationSeconds),
					DexcDisplayVisualPayload.Value(value)
				);
		}

		[Conditional("DEXC_DEBUGGER")]
		public void DrawLabel(DebugDrawOptions options, string text)
		{
			if (m_Record != null)
				DexcDisplayRuntime.Registry?.Draw(
					m_Record,
					DebugDisplayPrimitiveKind.Label,
					options == null ? string.Empty : options.Path,
					options,
					DexcDisplayVisualPayload.Label(text)
				);
		}

		[Conditional("DEXC_DEBUGGER")]
		public void DrawLabel(string path, string text, Color color)
		{
			if (m_Record != null)
				DexcDisplayRuntime.Registry?.Draw(
					m_Record,
					DebugDisplayPrimitiveKind.Label,
					path,
					DebugDrawOptions.Frame(path, color),
					DexcDisplayVisualPayload.Label(text)
				);
		}

		[Conditional("DEXC_DEBUGGER")]
		public void DrawLabel(string path, string text)
		{
			DrawLabel(path, text, Color.white);
		}

		[Conditional("DEXC_DEBUGGER")]
		public void DrawLabelFor(string path, string text, Color color, float durationSeconds, DebugDisplayTimeScope scope)
		{
			if (m_Record != null)
				DexcDisplayRuntime.Registry?.Draw(
					m_Record,
					DebugDisplayPrimitiveKind.Label,
					path,
					DexcDisplayRegistry.Options(path, color, scope, durationSeconds),
					DexcDisplayVisualPayload.Label(text)
				);
		}

		[Conditional("DEXC_DEBUGGER")]
		public void DrawSphere(
			DebugDrawOptions options,
			Vector3 center,
			float radius,
			DebugDisplaySpace space = DebugDisplaySpace.World,
			Transform basis = null
		)
		{
			if (m_Record == null)
				return;
			DexcDisplayRuntime.Registry?.Draw(
				m_Record,
				DebugDisplayPrimitiveKind.Sphere,
				options == null ? string.Empty : options.Path,
				options,
				DexcDisplayVisualPayload.Sphere(center, radius, space, basis)
			);
		}

		[Conditional("DEXC_DEBUGGER")]
		public void DrawSphere(
			string path,
			Vector3 center,
			float radius,
			Color color,
			DebugDisplayDrawStyle style = DebugDisplayDrawStyle.Wire,
			DebugDisplaySpace space = DebugDisplaySpace.World,
			Transform basis = null
		)
		{
			if (m_Record == null)
				return;
			DrawSphere(DebugDrawOptions.Frame(path, color).WithStyle(style), center, radius, space, basis);
		}

		[Conditional("DEXC_DEBUGGER")]
		public void DrawCube(
			DebugDrawOptions options,
			Vector3 center,
			Vector3 size,
			Quaternion rotation,
			DebugDisplaySpace space = DebugDisplaySpace.World,
			Transform basis = null
		)
		{
			if (m_Record == null)
				return;
			DexcDisplayRuntime.Registry?.Draw(
				m_Record,
				DebugDisplayPrimitiveKind.Cube,
				options == null ? string.Empty : options.Path,
				options,
				DexcDisplayVisualPayload.Cube(center, size, rotation, space, basis)
			);
		}

		[Conditional("DEXC_DEBUGGER")]
		public void DrawCube(
			string path,
			Vector3 center,
			Vector3 size,
			Quaternion rotation,
			Color color,
			DebugDisplayDrawStyle style = DebugDisplayDrawStyle.Wire,
			DebugDisplaySpace space = DebugDisplaySpace.World,
			Transform basis = null
		)
		{
			if (m_Record == null)
				return;
			DrawCube(DebugDrawOptions.Frame(path, color).WithStyle(style), center, size, rotation, space, basis);
		}

		[Conditional("DEXC_DEBUGGER")]
		public void DrawDisc(
			DebugDrawOptions options,
			Vector3 center,
			Vector3 normal,
			float radius,
			DebugDisplaySpace space = DebugDisplaySpace.World,
			Transform basis = null
		)
		{
			if (m_Record == null)
				return;
			DexcDisplayRuntime.Registry?.Draw(
				m_Record,
				DebugDisplayPrimitiveKind.Disc,
				options == null ? string.Empty : options.Path,
				options,
				DexcDisplayVisualPayload.Disc(center, normal, radius, space, basis)
			);
		}

		[Conditional("DEXC_DEBUGGER")]
		public void DrawDisc(
			string path,
			Vector3 center,
			Vector3 normal,
			float radius,
			Color color,
			DebugDisplayDrawStyle style = DebugDisplayDrawStyle.Wire,
			DebugDisplaySpace space = DebugDisplaySpace.World,
			Transform basis = null
		)
		{
			if (m_Record == null)
				return;
			DrawDisc(DebugDrawOptions.Frame(path, color).WithStyle(style), center, normal, radius, space, basis);
		}

		[Conditional("DEXC_DEBUGGER")]
		public void DrawCapsule(
			DebugDrawOptions options,
			Vector3 center,
			float radius,
			float height,
			Quaternion rotation,
			DebugDisplaySpace space = DebugDisplaySpace.World,
			Transform basis = null
		)
		{
			if (m_Record == null)
				return;
			DexcDisplayRuntime.Registry?.Draw(
				m_Record,
				DebugDisplayPrimitiveKind.Capsule,
				options == null ? string.Empty : options.Path,
				options,
				DexcDisplayVisualPayload.Capsule(center, radius, height, rotation, space, basis)
			);
		}

		[Conditional("DEXC_DEBUGGER")]
		public void DrawCapsule(
			string path,
			Vector3 center,
			float radius,
			float height,
			Quaternion rotation,
			Color color,
			DebugDisplayDrawStyle style = DebugDisplayDrawStyle.Wire,
			DebugDisplaySpace space = DebugDisplaySpace.World,
			Transform basis = null
		)
		{
			if (m_Record == null)
				return;
			DrawCapsule(DebugDrawOptions.Frame(path, color).WithStyle(style), center, radius, height, rotation, space, basis);
		}

		[Conditional("DEXC_DEBUGGER")]
		public void DrawLine(DebugDrawOptions options, Vector3 start, Vector3 end)
		{
			if (m_Record != null)
				DexcDisplayRuntime.Registry?.Draw(
					m_Record,
					DebugDisplayPrimitiveKind.Line,
					options == null ? string.Empty : options.Path,
					options,
					DexcDisplayVisualPayload.Line(start, end)
				);
		}

		[Conditional("DEXC_DEBUGGER")]
		public void DrawLine(string path, Vector3 start, Vector3 end, Color color)
		{
			if (m_Record != null)
				DexcDisplayRuntime.Registry?.Draw(
					m_Record,
					DebugDisplayPrimitiveKind.Line,
					path,
					DebugDrawOptions.Frame(path, color),
					DexcDisplayVisualPayload.Line(start, end)
				);
		}

		[Conditional("DEXC_DEBUGGER")]
		public void DrawLineFor(string path, Vector3 start, Vector3 end, Color color, float durationSeconds, DebugDisplayTimeScope scope)
		{
			if (m_Record != null)
				DexcDisplayRuntime.Registry?.Draw(
					m_Record,
					DebugDisplayPrimitiveKind.Line,
					path,
					DexcDisplayRegistry.Options(path, color, scope, durationSeconds),
					DexcDisplayVisualPayload.Line(start, end)
				);
		}

		[Conditional("DEXC_DEBUGGER")]
		public void DrawRay(DebugDrawOptions options, Vector3 origin, Vector3 direction)
		{
			if (m_Record != null)
				DexcDisplayRuntime.Registry?.Draw(
					m_Record,
					DebugDisplayPrimitiveKind.Ray,
					options == null ? string.Empty : options.Path,
					options,
					DexcDisplayVisualPayload.Line(origin, origin + direction)
				);
		}

		[Conditional("DEXC_DEBUGGER")]
		public void DrawRay(string path, Vector3 origin, Vector3 direction, Color color)
		{
			if (m_Record != null)
				DexcDisplayRuntime.Registry?.Draw(
					m_Record,
					DebugDisplayPrimitiveKind.Ray,
					path,
					DebugDrawOptions.Frame(path, color),
					DexcDisplayVisualPayload.Line(origin, origin + direction)
				);
		}

		[Conditional("DEXC_DEBUGGER")]
		public void DrawRay(string path, Vector3 origin, Vector3 direction)
		{
			DrawRay(path, origin, direction, Color.white);
		}

		[Conditional("DEXC_DEBUGGER")]
		public void DrawRayFor(
			string path,
			Vector3 origin,
			Vector3 direction,
			Color color,
			float durationSeconds,
			DebugDisplayTimeScope scope
		)
		{
			if (m_Record != null)
				DexcDisplayRuntime.Registry?.Draw(
					m_Record,
					DebugDisplayPrimitiveKind.Ray,
					path,
					DexcDisplayRegistry.Options(path, color, scope, durationSeconds),
					DexcDisplayVisualPayload.Line(origin, origin + direction)
				);
		}

		[Conditional("DEXC_DEBUGGER")]
		public void DrawPolyLine(
			DebugDrawOptions options,
			IEnumerable<Vector3> points,
			DebugDisplaySpace space = DebugDisplaySpace.World,
			Transform basis = null
		)
		{
			if (m_Record != null)
				DexcDisplayRuntime.Registry?.Draw(
					m_Record,
					DebugDisplayPrimitiveKind.PolyLine,
					options == null ? string.Empty : options.Path,
					options,
					DexcDisplayVisualPayload.PolyLine(points, space, basis)
				);
		}

		[Conditional("DEXC_DEBUGGER")]
		public void DrawPolyLine(
			DebugDrawOptions options,
			ReadOnlySpan<Vector3> points,
			DebugDisplaySpace space = DebugDisplaySpace.World,
			Transform basis = null
		)
		{
			if (m_Record != null)
				DexcDisplayRuntime.Registry?.Draw(
					m_Record,
					DebugDisplayPrimitiveKind.PolyLine,
					options == null ? string.Empty : options.Path,
					options,
					DexcDisplayVisualPayload.PolyLine(points, space, basis)
				);
		}

		[Conditional("DEXC_DEBUGGER")]
		public void DrawPolyLine(
			DebugDrawOptions options,
			Vector3[] points,
			DebugDisplaySpace space = DebugDisplaySpace.World,
			Transform basis = null
		)
		{
			if (m_Record != null)
				DexcDisplayRuntime.Registry?.Draw(
					m_Record,
					DebugDisplayPrimitiveKind.PolyLine,
					options == null ? string.Empty : options.Path,
					options,
					DexcDisplayVisualPayload.PolyLine((ReadOnlySpan<Vector3>)points, space, basis)
				);
		}

		[Conditional("DEXC_DEBUGGER")]
		public void DrawPolyLine(
			string path,
			IEnumerable<Vector3> points,
			Color color,
			DebugDisplaySpace space = DebugDisplaySpace.World,
			Transform basis = null
		)
		{
			if (m_Record != null)
				DexcDisplayRuntime.Registry?.Draw(
					m_Record,
					DebugDisplayPrimitiveKind.PolyLine,
					path,
					DebugDrawOptions.Frame(path, color),
					DexcDisplayVisualPayload.PolyLine(points, space, basis)
				);
		}

		[Conditional("DEXC_DEBUGGER")]
		public void DrawPolyLine(
			string path,
			ReadOnlySpan<Vector3> points,
			Color color,
			DebugDisplaySpace space = DebugDisplaySpace.World,
			Transform basis = null
		)
		{
			if (m_Record != null)
				DexcDisplayRuntime.Registry?.Draw(
					m_Record,
					DebugDisplayPrimitiveKind.PolyLine,
					path,
					DebugDrawOptions.Frame(path, color),
					DexcDisplayVisualPayload.PolyLine(points, space, basis)
				);
		}

		[Conditional("DEXC_DEBUGGER")]
		public void DrawPolyLine(
			string path,
			Vector3[] points,
			Color color,
			DebugDisplaySpace space = DebugDisplaySpace.World,
			Transform basis = null
		)
		{
			if (m_Record != null)
				DexcDisplayRuntime.Registry?.Draw(
					m_Record,
					DebugDisplayPrimitiveKind.PolyLine,
					path,
					DebugDrawOptions.Frame(path, color),
					DexcDisplayVisualPayload.PolyLine((ReadOnlySpan<Vector3>)points, space, basis)
				);
		}

		[Conditional("DEXC_DEBUGGER")]
		public void DrawPolyLineFor(
			string path,
			IEnumerable<Vector3> points,
			Color color,
			float durationSeconds,
			DebugDisplayTimeScope scope,
			DebugDisplaySpace space = DebugDisplaySpace.World,
			Transform basis = null
		)
		{
			if (m_Record != null)
				DexcDisplayRuntime.Registry?.Draw(
					m_Record,
					DebugDisplayPrimitiveKind.PolyLine,
					path,
					DexcDisplayRegistry.Options(path, color, scope, durationSeconds),
					DexcDisplayVisualPayload.PolyLine(points, space, basis)
				);
		}

		[Conditional("DEXC_DEBUGGER")]
		public void DrawPolyLineFor(
			string path,
			ReadOnlySpan<Vector3> points,
			Color color,
			float durationSeconds,
			DebugDisplayTimeScope scope,
			DebugDisplaySpace space = DebugDisplaySpace.World,
			Transform basis = null
		)
		{
			if (m_Record != null)
				DexcDisplayRuntime.Registry?.Draw(
					m_Record,
					DebugDisplayPrimitiveKind.PolyLine,
					path,
					DexcDisplayRegistry.Options(path, color, scope, durationSeconds),
					DexcDisplayVisualPayload.PolyLine(points, space, basis)
				);
		}

		[Conditional("DEXC_DEBUGGER")]
		public void DrawPolyLineFor(
			string path,
			Vector3[] points,
			Color color,
			float durationSeconds,
			DebugDisplayTimeScope scope,
			DebugDisplaySpace space = DebugDisplaySpace.World,
			Transform basis = null
		)
		{
			if (m_Record != null)
				DexcDisplayRuntime.Registry?.Draw(
					m_Record,
					DebugDisplayPrimitiveKind.PolyLine,
					path,
					DexcDisplayRegistry.Options(path, color, scope, durationSeconds),
					DexcDisplayVisualPayload.PolyLine((ReadOnlySpan<Vector3>)points, space, basis)
				);
		}

		[Conditional("DEXC_DEBUGGER")]
		public void DrawWireSphere(
			DebugDrawOptions options,
			Vector3 center,
			float radius,
			DebugDisplaySpace space = DebugDisplaySpace.World,
			Transform basis = null
		)
		{
			if (m_Record != null)
				DexcDisplayRuntime.Registry?.Draw(
					m_Record,
					DebugDisplayPrimitiveKind.WireSphere,
					options == null ? string.Empty : options.Path,
					options,
					DexcDisplayVisualPayload.Sphere(center, radius, space, basis)
				);
		}

		[Conditional("DEXC_DEBUGGER")]
		public void DrawWireSphere(
			string path,
			Vector3 center,
			float radius,
			Color color,
			DebugDisplaySpace space = DebugDisplaySpace.World,
			Transform basis = null
		)
		{
			if (m_Record != null)
				DexcDisplayRuntime.Registry?.Draw(
					m_Record,
					DebugDisplayPrimitiveKind.WireSphere,
					path,
					DebugDrawOptions.Frame(path, color),
					DexcDisplayVisualPayload.Sphere(center, radius, space, basis)
				);
		}

		[Conditional("DEXC_DEBUGGER")]
		public void DrawWireSphereFor(
			string path,
			Vector3 center,
			float radius,
			Color color,
			float durationSeconds,
			DebugDisplayTimeScope scope,
			DebugDisplaySpace space = DebugDisplaySpace.World,
			Transform basis = null
		)
		{
			if (m_Record != null)
				DexcDisplayRuntime.Registry?.Draw(
					m_Record,
					DebugDisplayPrimitiveKind.WireSphere,
					path,
					DexcDisplayRegistry.Options(path, color, scope, durationSeconds),
					DexcDisplayVisualPayload.Sphere(center, radius, space, basis)
				);
		}

		[Conditional("DEXC_DEBUGGER")]
		public void DrawWireCube(
			DebugDrawOptions options,
			Vector3 center,
			Vector3 size,
			Quaternion rotation,
			DebugDisplaySpace space = DebugDisplaySpace.World,
			Transform basis = null
		)
		{
			if (m_Record != null)
				DexcDisplayRuntime.Registry?.Draw(
					m_Record,
					DebugDisplayPrimitiveKind.WireCube,
					options == null ? string.Empty : options.Path,
					options,
					DexcDisplayVisualPayload.Cube(center, size, rotation, space, basis)
				);
		}

		[Conditional("DEXC_DEBUGGER")]
		public void DrawWireCube(
			DebugDrawOptions options,
			Vector3 center,
			Vector3 size,
			DebugDisplaySpace space = DebugDisplaySpace.World,
			Transform basis = null
		)
		{
			DrawWireCube(options, center, size, Quaternion.identity, space, basis);
		}

		[Conditional("DEXC_DEBUGGER")]
		public void DrawWireCube(
			string path,
			Vector3 center,
			Vector3 size,
			Quaternion rotation,
			Color color,
			DebugDisplaySpace space = DebugDisplaySpace.World,
			Transform basis = null
		)
		{
			if (m_Record != null)
				DexcDisplayRuntime.Registry?.Draw(
					m_Record,
					DebugDisplayPrimitiveKind.WireCube,
					path,
					DebugDrawOptions.Frame(path, color),
					DexcDisplayVisualPayload.Cube(center, size, rotation, space, basis)
				);
		}

		[Conditional("DEXC_DEBUGGER")]
		public void DrawWireCubeFor(
			string path,
			Vector3 center,
			Vector3 size,
			Quaternion rotation,
			Color color,
			float durationSeconds,
			DebugDisplayTimeScope scope,
			DebugDisplaySpace space = DebugDisplaySpace.World,
			Transform basis = null
		)
		{
			if (m_Record != null)
				DexcDisplayRuntime.Registry?.Draw(
					m_Record,
					DebugDisplayPrimitiveKind.WireCube,
					path,
					DexcDisplayRegistry.Options(path, color, scope, durationSeconds),
					DexcDisplayVisualPayload.Cube(center, size, rotation, space, basis)
				);
		}

		[Conditional("DEXC_DEBUGGER")]
		public void DrawWireDisc(
			DebugDrawOptions options,
			Vector3 center,
			Vector3 normal,
			float radius,
			DebugDisplaySpace space = DebugDisplaySpace.World,
			Transform basis = null
		)
		{
			if (m_Record != null)
				DexcDisplayRuntime.Registry?.Draw(
					m_Record,
					DebugDisplayPrimitiveKind.WireDisc,
					options == null ? string.Empty : options.Path,
					options,
					DexcDisplayVisualPayload.Disc(center, normal, radius, space, basis)
				);
		}

		[Conditional("DEXC_DEBUGGER")]
		public void DrawWireDisc(
			string path,
			Vector3 center,
			Vector3 normal,
			float radius,
			Color color,
			DebugDisplaySpace space = DebugDisplaySpace.World,
			Transform basis = null
		)
		{
			if (m_Record != null)
				DexcDisplayRuntime.Registry?.Draw(
					m_Record,
					DebugDisplayPrimitiveKind.WireDisc,
					path,
					DebugDrawOptions.Frame(path, color),
					DexcDisplayVisualPayload.Disc(center, normal, radius, space, basis)
				);
		}

		[Conditional("DEXC_DEBUGGER")]
		public void DrawWireDiscFor(
			string path,
			Vector3 center,
			Vector3 normal,
			float radius,
			Color color,
			float durationSeconds,
			DebugDisplayTimeScope scope,
			DebugDisplaySpace space = DebugDisplaySpace.World,
			Transform basis = null
		)
		{
			if (m_Record != null)
				DexcDisplayRuntime.Registry?.Draw(
					m_Record,
					DebugDisplayPrimitiveKind.WireDisc,
					path,
					DexcDisplayRegistry.Options(path, color, scope, durationSeconds),
					DexcDisplayVisualPayload.Disc(center, normal, radius, space, basis)
				);
		}

		[Conditional("DEXC_DEBUGGER")]
		public void DrawRelation(DebugDrawOptions options, UnityEngine.Object target)
		{
			if (m_Record != null)
				DexcDisplayRuntime.Registry?.Draw(
					m_Record,
					DebugDisplayPrimitiveKind.Relation,
					options == null ? string.Empty : options.Path,
					options,
					DexcDisplayVisualPayload.Relation(target)
				);
		}

		[Conditional("DEXC_DEBUGGER")]
		public void DrawRelation(string path, UnityEngine.Object target, Color color)
		{
			if (m_Record != null)
				DexcDisplayRuntime.Registry?.Draw(
					m_Record,
					DebugDisplayPrimitiveKind.Relation,
					path,
					DebugDrawOptions.Frame(path, color),
					DexcDisplayVisualPayload.Relation(target)
				);
		}

		[Conditional("DEXC_DEBUGGER")]
		public void DrawRelationFor(string path, UnityEngine.Object target, Color color, float durationSeconds, DebugDisplayTimeScope scope)
		{
			if (m_Record != null)
				DexcDisplayRuntime.Registry?.Draw(
					m_Record,
					DebugDisplayPrimitiveKind.Relation,
					path,
					DexcDisplayRegistry.Options(path, color, scope, durationSeconds),
					DexcDisplayVisualPayload.Relation(target)
				);
		}

		[Conditional("DEXC_DEBUGGER")]
		public void DrawRelation(DebugDrawOptions options, DebugDisplaySubject target)
		{
			if (m_Record != null)
				DexcDisplayRuntime.Registry?.Draw(
					m_Record,
					DebugDisplayPrimitiveKind.Relation,
					options == null ? string.Empty : options.Path,
					options,
					DexcDisplayVisualPayload.Relation(target)
				);
		}

		[Conditional("DEXC_DEBUGGER")]
		public void DrawRelation(string path, DebugDisplaySubject target, Color color)
		{
			if (m_Record != null)
				DexcDisplayRuntime.Registry?.Draw(
					m_Record,
					DebugDisplayPrimitiveKind.Relation,
					path,
					DebugDrawOptions.Frame(path, color),
					DexcDisplayVisualPayload.Relation(target)
				);
		}

		[Conditional("DEXC_DEBUGGER")]
		public void DrawRelationFor(
			string path,
			DebugDisplaySubject target,
			Color color,
			float durationSeconds,
			DebugDisplayTimeScope scope
		)
		{
			if (m_Record != null)
				DexcDisplayRuntime.Registry?.Draw(
					m_Record,
					DebugDisplayPrimitiveKind.Relation,
					path,
					DexcDisplayRegistry.Options(path, color, scope, durationSeconds),
					DexcDisplayVisualPayload.Relation(target)
				);
		}

		[Conditional("DEXC_DEBUGGER")]
		public void Clear(string name)
		{
			if (m_Record != null)
				DexcDisplayRuntime.Registry?.Clear(m_Record, name);
		}
	}

	internal abstract class DexcDisplayMetadataSlot
	{
		protected DexcDisplayMetadataSlot(string name)
		{
			Name = name;
		}

		internal string Name { get; }
		internal abstract DebugValueKind Kind { get; }
		internal abstract DebugValue CaptureValue();
	}

	internal sealed class DexcDisplayMetadataSlot<T> : DexcDisplayMetadataSlot
	{
		private readonly Func<T, DebugValue> m_Capture;

		internal DexcDisplayMetadataSlot(string name, T value, DebugValueKind kind, Func<T, DebugValue> capture)
			: base(name)
		{
			Value = value;
			KindValue = kind;
			m_Capture = capture;
		}

		internal T Value;
		internal DebugValueKind KindValue { get; }
		internal override DebugValueKind Kind
		{
			get { return KindValue; }
		}

		internal override DebugValue CaptureValue()
		{
			return m_Capture(Value);
		}
	}

	internal sealed class DexcDisplaySubjectRecord
	{
		private readonly DexcDisplayRegistry m_Registry;
		private readonly WeakReference m_Owner;
		private readonly DebugObjectHandle m_OwnerHandle;
		private readonly string m_OwnerTypeName;
		private readonly Dictionary<string, DexcDisplayMetadataSlot> m_Metadata = new Dictionary<string, DexcDisplayMetadataSlot>(
			StringComparer.Ordinal
		);
		private bool m_Disposed;

		internal DexcDisplaySubjectRecord(
			DexcDisplayRegistry registry,
			DebugDisplaySubjectId id,
			string category,
			UnityEngine.Object owner,
			DebugObjectHandle ownerHandle
		)
		{
			m_Registry = registry;
			Id = id;
			Category = category;
			Label = string.IsNullOrEmpty(owner.name) ? owner.GetType().Name : owner.name;
			m_Owner = new WeakReference(owner);
			m_OwnerHandle = ownerHandle;
			m_OwnerTypeName = owner.GetType().FullName;
		}

		internal DebugDisplaySubjectId Id { get; }
		internal string Category { get; }
		internal string Label { get; private set; }
		internal bool IsDisposed
		{
			get { return m_Disposed; }
		}
		internal UnityEngine.Object Owner
		{
			get { return m_Owner.Target as UnityEngine.Object; }
		}

		internal void SetLabel(string label)
		{
			m_Registry.EnsureActive(this);
			if (string.IsNullOrWhiteSpace(label))
				throw new ArgumentException("Display Subject label must not be empty.", nameof(label));
			Label = label;
		}

		internal void SetMetadata(string name, bool value)
		{
			Set(name, value, DebugValueKind.Boolean, current => DebugValue.From(current));
		}

		internal void SetMetadata(string name, sbyte value)
		{
			Set(name, value, DebugValueKind.SignedInteger, current => DebugValue.From(current));
		}

		internal void SetMetadata(string name, short value)
		{
			Set(name, value, DebugValueKind.SignedInteger, current => DebugValue.From(current));
		}

		internal void SetMetadata(string name, int value)
		{
			Set(name, value, DebugValueKind.SignedInteger, current => DebugValue.From(current));
		}

		internal void SetMetadata(string name, long value)
		{
			Set(name, value, DebugValueKind.SignedInteger, current => DebugValue.From(current));
		}

		internal void SetMetadata(string name, byte value)
		{
			Set(name, value, DebugValueKind.UnsignedInteger, current => DebugValue.From(current));
		}

		internal void SetMetadata(string name, ushort value)
		{
			Set(name, value, DebugValueKind.UnsignedInteger, current => DebugValue.From(current));
		}

		internal void SetMetadata(string name, uint value)
		{
			Set(name, value, DebugValueKind.UnsignedInteger, current => DebugValue.From(current));
		}

		internal void SetMetadata(string name, ulong value)
		{
			Set(name, value, DebugValueKind.UnsignedInteger, current => DebugValue.From(current));
		}

		internal void SetMetadata(string name, float value)
		{
			Set(name, value, DebugValueKind.FloatingPoint, current => DebugValue.From(current));
		}

		internal void SetMetadata(string name, double value)
		{
			Set(name, value, DebugValueKind.FloatingPoint, current => DebugValue.From(current));
		}

		internal void SetMetadata(string name, string value)
		{
			if (value == null)
				throw new ArgumentNullException(nameof(value));
			Set(name, value, DebugValueKind.String, current => DebugValue.From(current));
		}

		internal void SetMetadata(string name, Color value)
		{
			Set(name, value, DebugValueKind.Object, CaptureColor);
		}

		internal void SetMetadata(string name, Vector2 value)
		{
			Set(name, value, DebugValueKind.Object, CaptureVector2);
		}

		internal void SetMetadata(string name, Vector3 value)
		{
			Set(name, value, DebugValueKind.Object, CaptureVector3);
		}

		internal void SetMetadata(string name, Vector4 value)
		{
			Set(name, value, DebugValueKind.Object, CaptureVector4);
		}

		internal void SetMetadata(string name, Vector2Int value)
		{
			Set(name, value, DebugValueKind.Object, CaptureVector2Int);
		}

		internal void SetMetadata(string name, Vector3Int value)
		{
			Set(name, value, DebugValueKind.Object, CaptureVector3Int);
		}

		internal void SetMetadata<TEnum>(string name, TEnum value)
			where TEnum : struct, Enum
		{
			Set(name, value, DebugValueKind.SignedInteger, current => DebugValue.From(Convert.ToInt64(current)));
		}

		internal void SetMetadata(string name, DebugValue value)
		{
			if (value == null)
				throw new ArgumentNullException(nameof(value));
			Set(name, value, value.Kind, current => current);
		}

		internal void RemoveMetadata(string name)
		{
			m_Registry.EnsureActive(this);
			if (string.IsNullOrWhiteSpace(name))
				throw new ArgumentException("Metadata name must not be empty.", nameof(name));
			m_Metadata.Remove(name);
		}

		internal DebugDisplaySubjectSnapshot CaptureSnapshot(IEnumerable<DebugDisplayVisualSnapshot> visuals = null)
		{
			var owner = Owner;
			if (owner == null)
				return null;
			var gameObject = owner as GameObject ?? ((Component)owner).gameObject;
			var metadata = m_Metadata.Values.Select(item => new DebugDisplayMetadataSnapshot(item.Name, item.CaptureValue()));
			return new DebugDisplaySubjectSnapshot(
				Id,
				Category,
				Label,
				new DebugDisplayOwnerSnapshot(
					m_OwnerHandle,
					m_OwnerTypeName,
					gameObject.scene.name,
					HierarchyPath(gameObject),
					true,
					gameObject.transform.position,
					gameObject.transform.rotation,
					gameObject.transform.lossyScale
				),
				metadata,
				visuals
			);
		}

		internal void Dispose()
		{
			if (m_Disposed)
				return;
			m_Disposed = true;
			m_Metadata.Clear();
			m_Registry.Remove(this);
		}

		private void Set<T>(string name, T value, DebugValueKind kind, Func<T, DebugValue> capture)
		{
			m_Registry.EnsureActive(this);
			if (string.IsNullOrWhiteSpace(name))
				throw new ArgumentException("Metadata name must not be empty.", nameof(name));
			DexcDisplayMetadataSlot existing;
			if (m_Metadata.TryGetValue(name, out existing))
			{
				var typed = existing as DexcDisplayMetadataSlot<T>;
				if (typed == null || existing.Kind != kind)
				{
					m_Registry.AddDiagnostic("display-metadata-type-change", name, "Metadata type cannot change after registration.");
					return;
				}

				typed.Value = value;
				return;
			}

			if (m_Metadata.Count >= m_Registry.Session.Options.MaxMetadataPerSubject)
			{
				m_Registry.AddDiagnostic("display-metadata-capacity", name, "The Subject metadata capacity was reached.");
				return;
			}

			m_Metadata.Add(name, new DexcDisplayMetadataSlot<T>(name, value, kind, capture));
		}

		private static DebugValue CaptureColor(Color value)
		{
			return DebugValue.FromObject(
				new DebugValueMember("r", DebugValue.From(value.r)),
				new DebugValueMember("g", DebugValue.From(value.g)),
				new DebugValueMember("b", DebugValue.From(value.b)),
				new DebugValueMember("a", DebugValue.From(value.a))
			);
		}

		private static DebugValue CaptureVector2(Vector2 value)
		{
			return DebugValue.FromObject(
				new DebugValueMember("x", DebugValue.From(value.x)),
				new DebugValueMember("y", DebugValue.From(value.y))
			);
		}

		private static DebugValue CaptureVector3(Vector3 value)
		{
			return DebugValue.FromObject(
				new DebugValueMember("x", DebugValue.From(value.x)),
				new DebugValueMember("y", DebugValue.From(value.y)),
				new DebugValueMember("z", DebugValue.From(value.z))
			);
		}

		private static DebugValue CaptureVector4(Vector4 value)
		{
			return DebugValue.FromObject(
				new DebugValueMember("x", DebugValue.From(value.x)),
				new DebugValueMember("y", DebugValue.From(value.y)),
				new DebugValueMember("z", DebugValue.From(value.z)),
				new DebugValueMember("w", DebugValue.From(value.w))
			);
		}

		private static DebugValue CaptureVector2Int(Vector2Int value)
		{
			return DebugValue.FromObject(
				new DebugValueMember("x", DebugValue.From(value.x)),
				new DebugValueMember("y", DebugValue.From(value.y))
			);
		}

		private static DebugValue CaptureVector3Int(Vector3Int value)
		{
			return DebugValue.FromObject(
				new DebugValueMember("x", DebugValue.From(value.x)),
				new DebugValueMember("y", DebugValue.From(value.y)),
				new DebugValueMember("z", DebugValue.From(value.z))
			);
		}

		private static string HierarchyPath(GameObject gameObject)
		{
			var parts = new List<string>();
			for (var current = gameObject.transform; current != null; current = current.parent)
				parts.Add(current.name);
			parts.Reverse();
			return string.Join("/", parts.ToArray());
		}
	}

	internal sealed class DexcDisplayVisualPayload
	{
		internal string Text;
		internal DebugValue Data;
		internal Vector3 Start;
		internal Vector3 End;
		internal Vector3 Center;
		internal Vector3 Size;
		internal Vector3 Normal;
		internal float Radius;
		internal float Height;
		internal Quaternion Rotation;
		internal List<Vector3> Points;
		internal DebugDisplaySpace Space;
		internal Transform Basis;
		internal UnityEngine.Object TargetObject;
		internal DebugDisplaySubject TargetSubject;

		internal static DexcDisplayVisualPayload Value(DebugValue value)
		{
			return new DexcDisplayVisualPayload { Data = value };
		}

		internal static DexcDisplayVisualPayload Label(string text)
		{
			return new DexcDisplayVisualPayload { Text = text };
		}

		internal static DexcDisplayVisualPayload Line(Vector3 start, Vector3 end)
		{
			return new DexcDisplayVisualPayload { Start = start, End = end };
		}

		internal static DexcDisplayVisualPayload PolyLine(IEnumerable<Vector3> points, DebugDisplaySpace space, Transform basis)
		{
			return new DexcDisplayVisualPayload
			{
				Points = points == null ? null : points.ToList(),
				Space = space,
				Basis = basis,
			};
		}

		internal static DexcDisplayVisualPayload PolyLine(ReadOnlySpan<Vector3> points, DebugDisplaySpace space, Transform basis)
		{
			var list = new List<Vector3>(points.Length);
			foreach (var point in points)
				list.Add(point);
			return new DexcDisplayVisualPayload
			{
				Points = list,
				Space = space,
				Basis = basis,
			};
		}

		internal static DexcDisplayVisualPayload Sphere(Vector3 center, float radius, DebugDisplaySpace space, Transform basis)
		{
			return new DexcDisplayVisualPayload
			{
				Center = center,
				Radius = radius,
				Space = space,
				Basis = basis,
			};
		}

		internal static DexcDisplayVisualPayload Cube(
			Vector3 center,
			Vector3 size,
			Quaternion rotation,
			DebugDisplaySpace space,
			Transform basis
		)
		{
			return new DexcDisplayVisualPayload
			{
				Center = center,
				Size = size,
				Rotation = rotation,
				Space = space,
				Basis = basis,
			};
		}

		internal static DexcDisplayVisualPayload Disc(
			Vector3 center,
			Vector3 normal,
			float radius,
			DebugDisplaySpace space,
			Transform basis
		)
		{
			return new DexcDisplayVisualPayload
			{
				Center = center,
				Normal = normal,
				Radius = radius,
				Space = space,
				Basis = basis,
			};
		}

		internal static DexcDisplayVisualPayload Capsule(
			Vector3 center,
			float radius,
			float height,
			Quaternion rotation,
			DebugDisplaySpace space,
			Transform basis
		)
		{
			return new DexcDisplayVisualPayload
			{
				Center = center,
				Radius = radius,
				Height = height,
				Rotation = rotation,
				Space = space,
				Basis = basis,
			};
		}

		internal static DexcDisplayVisualPayload Relation(UnityEngine.Object target)
		{
			return new DexcDisplayVisualPayload { TargetObject = target };
		}

		internal static DexcDisplayVisualPayload Relation(DebugDisplaySubject target)
		{
			return new DexcDisplayVisualPayload { TargetSubject = target };
		}
	}

	internal sealed class DexcDisplayVisualRecord
	{
		internal DexcDisplayVisualRecord(
			string key,
			DexcDisplaySubjectRecord subject,
			DebugDisplayPrimitiveKind kind,
			DebugDisplayVisualSnapshot snapshot,
			bool hasExpiry,
			double expiresAt
		)
		{
			Key = key;
			Subject = subject;
			Kind = kind;
			Snapshot = snapshot;
			HasExpiry = hasExpiry;
			ExpiresAt = expiresAt;
		}

		internal string Key { get; }
		internal DexcDisplaySubjectRecord Subject { get; }
		internal DebugDisplayPrimitiveKind Kind { get; }
		internal DebugDisplayVisualSnapshot Snapshot { get; }
		internal bool HasExpiry { get; }
		internal double ExpiresAt { get; }
	}

	internal sealed class DexcDisplayRegistry
	{
		internal readonly DexcRuntimeSession Session;
		private readonly Dictionary<DebugDisplaySubjectId, DexcDisplaySubjectRecord> m_Subjects =
			new Dictionary<DebugDisplaySubjectId, DexcDisplaySubjectRecord>();
		private readonly Dictionary<string, DexcDisplayVisualRecord> m_PersistentVisuals = new Dictionary<string, DexcDisplayVisualRecord>(
			StringComparer.Ordinal
		);
		private readonly Dictionary<string, DexcDisplayVisualRecord> m_FrameVisuals = new Dictionary<string, DexcDisplayVisualRecord>(
			StringComparer.Ordinal
		);
		private DebugDisplayFrameSnapshot m_CompletedFrame;
		private long m_NextSubject;
		private long m_NextObject;
		private long m_Frame;
		private int m_DroppedThisFrame;
		private int m_GlobalVisualsAcceptedThisFrame;
		private bool m_Ended;

		internal DexcDisplayRegistry(DexcRuntimeSession session)
		{
			Session = session;
		}

		internal DebugDisplaySubject CreateSubject(string category, UnityEngine.Object owner)
		{
			if (string.IsNullOrWhiteSpace(category))
				throw new ArgumentException("Display Subject category must not be empty.", nameof(category));
			if (owner == null)
				throw new ArgumentNullException(nameof(owner));
			if (!(owner is GameObject) && !(owner is Component))
				throw new ArgumentException("Display Subject owner must be a GameObject or Component.", nameof(owner));
			RemoveDestroyedSubjects();
			if (
				m_Ended
				|| !Session.IsCurrent
				|| Session.Options.MaxSubjectCount == 0
				|| m_Subjects.Count >= Session.Options.MaxSubjectCount
			)
			{
				AddDiagnostic("display-subject-capacity", category, "The Display Subject capacity was reached.");
				return DebugDisplaySubject.NoOp;
			}

			var gameObject = owner as GameObject ?? ((Component)owner).gameObject;
			var id = DebugDisplaySubjectId.Create(Session.SessionId, ++m_NextSubject);
			var handle = DebugObjectHandle.Create(Session.SessionId, ++m_NextObject);
			var record = new DexcDisplaySubjectRecord(this, id, category, owner, handle);
			m_Subjects.Add(id, record);
			return new DebugDisplaySubject(record);
		}

		internal void EnsureActive(DexcDisplaySubjectRecord record)
		{
			if (record == null || record.IsDisposed || m_Ended || !Session.IsCurrent)
				throw new InvalidOperationException("The Display Subject is stale.");
		}

		internal void Remove(DexcDisplaySubjectRecord record)
		{
			if (record == null)
				return;
			MarkRelationsMissing(record);
			m_Subjects.Remove(record.Id);
			foreach (var key in m_PersistentVisuals.Values.Where(item => item.Subject == record).Select(item => item.Key).ToList())
				m_PersistentVisuals.Remove(key);
			foreach (var key in m_FrameVisuals.Values.Where(item => item.Subject == record).Select(item => item.Key).ToList())
				m_FrameVisuals.Remove(key);
		}

		internal void AddDiagnostic(string code, string identity, string message)
		{
			Session.Diagnostics.AddOperation(
				DebugDiagnosticSeverity.Error,
				DebugFeature.Display,
				"operation",
				code,
				message,
				identity,
				null
			);
		}

		internal static DebugDrawOptions Options(string path, Color color, DebugDisplayTimeScope scope, float durationSeconds)
		{
			return DebugDrawOptions.Create(path, color, scope, durationSeconds);
		}

		internal void Draw(
			DexcDisplaySubjectRecord subject,
			DebugDisplayPrimitiveKind kind,
			string path,
			DebugDrawOptions options,
			DexcDisplayVisualPayload payload
		)
		{
			if (subject != null)
				EnsureActive(subject);
			if (options == null)
				throw new ArgumentNullException(nameof(options));
			if (payload == null)
				throw new ArgumentNullException(nameof(payload));
			if (string.IsNullOrWhiteSpace(path))
				throw new ArgumentException("Display path must not be empty.", nameof(path));
			if (
				payload.TargetSubject != null
				&& (payload.TargetSubject.Record == null || payload.TargetSubject.Record.IsDisposed || !Session.IsCurrent)
			)
			{
				Drop("display-relation-target-missing", path, "The relation target Subject is stale.");
				return;
			}
			if (!Validate(kind, payload, out var validationMessage))
			{
				Drop("display-invalid-geometry", path, validationMessage);
				return;
			}

			var key = (subject == null ? "global:" : subject.Id.ToString() + ":") + path;
			DexcDisplayVisualRecord existing;
			if (!m_FrameVisuals.TryGetValue(key, out existing))
				m_PersistentVisuals.TryGetValue(key, out existing);
			if (existing != null && existing.Kind != kind)
			{
				Drop("display-primitive-kind-change", path, "A display identity cannot change primitive kind.");
				return;
			}
			if (existing == null)
			{
				if (subject != null && CountSubjectVisuals(subject) >= Session.Options.MaxVisualsPerSubject)
				{
					Drop("display-subject-visual-capacity", path, "The Subject visual capacity was reached.");
					return;
				}
				if (subject == null && m_GlobalVisualsAcceptedThisFrame >= Session.Options.MaxVisualsPerFrame)
				{
					Drop("display-frame-visual-capacity", path, "The global frame visual capacity was reached.");
					return;
				}
				if (subject == null)
					m_GlobalVisualsAcceptedThisFrame++;
			}

			var snapshot = CreateSnapshot(subject, kind, path, options, payload);
			var expiry = Expiry(options, out var hasExpiry);
			var record = new DexcDisplayVisualRecord(key, subject, kind, snapshot, hasExpiry, expiry);
			m_FrameVisuals.Remove(key);
			m_PersistentVisuals.Remove(key);
			if (options.TimeScope == DebugDisplayTimeScope.Frame)
				m_FrameVisuals.Add(key, record);
			else
				m_PersistentVisuals.Add(key, record);
		}

		internal void Clear(DexcDisplaySubjectRecord subject, string path)
		{
			if (subject != null)
				EnsureActive(subject);
			if (string.IsNullOrWhiteSpace(path))
				throw new ArgumentException("Display path must not be empty.", nameof(path));
			var key = (subject == null ? "global:" : subject.Id.ToString() + ":") + path;
			m_FrameVisuals.Remove(key);
			m_PersistentVisuals.Remove(key);
		}

		internal void Tick()
		{
			RemoveDestroyedSubjects();
			var nowRealtime = (double)Time.unscaledTime;
			var nowGameTime = (double)Time.time;
			foreach (
				var record in m_PersistentVisuals
					.Values.Where(item =>
						item.HasExpiry
						&& (
							(item.Snapshot.TimeScope == DebugDisplayTimeScope.Realtime && nowRealtime >= item.ExpiresAt)
							|| (item.Snapshot.TimeScope == DebugDisplayTimeScope.GameTime && nowGameTime >= item.ExpiresAt)
						)
					)
					.ToList()
			)
				m_PersistentVisuals.Remove(record.Key);
			var active = new Dictionary<string, DexcDisplayVisualRecord>(m_PersistentVisuals, StringComparer.Ordinal);
			foreach (var item in m_FrameVisuals)
				active[item.Key] = item.Value;
			var subjectSnapshots = new List<DebugDisplaySubjectSnapshot>();
			var snapshotDropped = m_DroppedThisFrame;
			foreach (var subject in m_Subjects.Values.ToList())
			{
				var visuals = active.Values.Where(item => item.Subject == subject).Select(item => item.Snapshot).ToList();
				var snapshot = subject.CaptureSnapshot(visuals);
				if (snapshot != null)
					subjectSnapshots.Add(snapshot);
			}
			var globals = active.Values.Where(item => item.Subject == null).Select(item => item.Snapshot).ToList();
			m_CompletedFrame = new DebugDisplayFrameSnapshot(++m_Frame, nowRealtime, subjectSnapshots, globals, snapshotDropped);
			m_FrameVisuals.Clear();
			m_DroppedThisFrame = 0;
			m_GlobalVisualsAcceptedThisFrame = 0;
		}

		internal bool TryGetCompletedFrame(out DebugDisplayFrameSnapshot frame)
		{
			frame = m_CompletedFrame;
			return frame != null;
		}

		internal IReadOnlyList<DebugDisplaySubjectSnapshot> CaptureSubjects()
		{
			var active = new Dictionary<string, DexcDisplayVisualRecord>(m_PersistentVisuals, StringComparer.Ordinal);
			foreach (var item in m_FrameVisuals)
				active[item.Key] = item.Value;
			return new ReadOnlyCollection<DebugDisplaySubjectSnapshot>(
				m_Subjects
					.Values.Select(item =>
						item.CaptureSnapshot(active.Values.Where(visual => visual.Subject == item).Select(visual => visual.Snapshot))
					)
					.Where(item => item != null)
					.ToList()
			);
		}

		internal void EndSession()
		{
			if (m_Ended)
				return;
			m_Ended = true;
			m_Subjects.Clear();
			m_PersistentVisuals.Clear();
			m_FrameVisuals.Clear();
			m_CompletedFrame = null;
		}

		private void RemoveDestroyedSubjects()
		{
			foreach (var record in m_Subjects.Values.ToList())
				if (record.Owner == null)
					record.Dispose();
		}

		private int CountSubjectVisuals(DexcDisplaySubjectRecord subject)
		{
			return m_PersistentVisuals
				.Values.Concat(m_FrameVisuals.Values)
				.Where(item => item.Subject == subject)
				.Select(item => item.Key)
				.Distinct(StringComparer.Ordinal)
				.Count();
		}

		private bool Validate(DebugDisplayPrimitiveKind kind, DexcDisplayVisualPayload payload, out string message)
		{
			message = string.Empty;
			if (!Enum.IsDefined(typeof(DebugDisplaySpace), payload.Space))
			{
				message = "Coordinate space is invalid.";
				return false;
			}
			if (payload.Space == DebugDisplaySpace.Local && payload.Basis == null)
			{
				message = "Local display geometry requires a basis Transform.";
				return false;
			}
			if (payload.Space == DebugDisplaySpace.World && payload.Basis != null)
			{
				message = "World display geometry must not specify a basis Transform.";
				return false;
			}
			switch (kind)
			{
				case DebugDisplayPrimitiveKind.Value:
					if (payload.Data == null)
						message = "Display value must not be null.";
					break;
				case DebugDisplayPrimitiveKind.Label:
					if (payload.Text == null)
						message = "Display label must not be null.";
					break;
				case DebugDisplayPrimitiveKind.Line:
				case DebugDisplayPrimitiveKind.Ray:
					if (!Finite(payload.Start) || !Finite(payload.End))
						message = "Line geometry must be finite.";
					break;
				case DebugDisplayPrimitiveKind.PolyLine:
					if (
						payload.Points == null
						|| payload.Points.Count < 2
						|| payload.Points.Count > Session.Options.MaxPolylinePointCount
						|| payload.Points.Any(point => !Finite(point))
					)
						message = "Polyline geometry is invalid or exceeds its point limit.";
					break;
				case DebugDisplayPrimitiveKind.Sphere:
				case DebugDisplayPrimitiveKind.WireSphere:
					if (!Finite(payload.Center) || !Finite(payload.Radius) || payload.Radius < 0f)
						message = "Wire sphere geometry is invalid.";
					break;
				case DebugDisplayPrimitiveKind.Cube:
				case DebugDisplayPrimitiveKind.WireCube:
					if (
						!Finite(payload.Center)
						|| !Finite(payload.Size)
						|| payload.Size.x < 0f
						|| payload.Size.y < 0f
						|| payload.Size.z < 0f
						|| !Finite(payload.Rotation)
					)
						message = "Wire cube geometry is invalid.";
					break;
				case DebugDisplayPrimitiveKind.Disc:
				case DebugDisplayPrimitiveKind.WireDisc:
					if (
						!Finite(payload.Center)
						|| !Finite(payload.Normal)
						|| payload.Normal.sqrMagnitude <= 0f
						|| !Finite(payload.Radius)
						|| payload.Radius < 0f
					)
						message = "Wire disc geometry is invalid.";
					break;
				case DebugDisplayPrimitiveKind.Capsule:
					if (
						!Finite(payload.Center)
						|| !Finite(payload.Radius)
						|| payload.Radius < 0f
						|| !Finite(payload.Height)
						|| payload.Height < 0f
						|| !Finite(payload.Rotation)
						|| (payload.Rotation.x == 0f && payload.Rotation.y == 0f && payload.Rotation.z == 0f && payload.Rotation.w == 0f)
					)
					{
						message = "Capsule geometry is invalid.";
					}
					break;
				case DebugDisplayPrimitiveKind.Relation:
					if (payload.TargetSubject == null && payload.TargetObject == null)
						message = "Relation target must not be null.";
					break;
				default:
					message = "Display primitive kind is invalid.";
					break;
			}
			return string.IsNullOrEmpty(message);
		}

		private DebugDisplayVisualSnapshot CreateSnapshot(
			DexcDisplaySubjectRecord subject,
			DebugDisplayPrimitiveKind kind,
			string path,
			DebugDrawOptions options,
			DexcDisplayVisualPayload payload
		)
		{
			var target = payload.TargetSubject == null ? default(DebugDisplaySubjectId) : payload.TargetSubject.Id;
			var targetOwner = payload.TargetSubject == null ? CreateOwnerSnapshot(payload.TargetObject) : null;
			if (payload.TargetSubject == null && payload.TargetObject != null)
			{
				var targetRecord = FindSubject(payload.TargetObject);
				if (targetRecord != null)
				{
					target = targetRecord.Id;
					targetOwner = null;
				}
			}
			var rotation = payload.Rotation;
			if (kind == DebugDisplayPrimitiveKind.Capsule)
			{
				var length = Math.Sqrt(
					(double)rotation.x * rotation.x
						+ (double)rotation.y * rotation.y
						+ (double)rotation.z * rotation.z
						+ (double)rotation.w * rotation.w
				);
				rotation = new Quaternion(
					(float)(rotation.x / length),
					(float)(rotation.y / length),
					(float)(rotation.z / length),
					(float)(rotation.w / length)
				);
			}
			return new DebugDisplayVisualSnapshot(
				kind,
				path,
				path,
				options.Color,
				options.TimeScope,
				payload.Space,
				payload.Text,
				payload.Data,
				payload.Start,
				payload.End,
				payload.Center,
				payload.Size,
				payload.Normal,
				payload.Radius,
				rotation,
				payload.Points,
				subject == null ? default(DebugDisplaySubjectId) : subject.Id,
				target,
				targetOwner,
				CreateOwnerSnapshot(payload.Basis),
				Expiry(options, out var hasExpiry),
				hasExpiry,
				false,
				kind >= DebugDisplayPrimitiveKind.Sphere ? options.DrawStyle : DebugDisplayDrawStyle.Wire,
				options.FillColor,
				payload.Height
			);
		}

		private double Expiry(DebugDrawOptions options, out bool hasExpiry)
		{
			hasExpiry = options.TimeScope == DebugDisplayTimeScope.Realtime || options.TimeScope == DebugDisplayTimeScope.GameTime;
			if (!hasExpiry)
				return 0d;
			return (options.TimeScope == DebugDisplayTimeScope.Realtime ? (double)Time.unscaledTime : (double)Time.time)
				+ options.DurationSeconds;
		}

		private DexcDisplaySubjectRecord FindSubject(UnityEngine.Object owner)
		{
			return m_Subjects.Values.FirstOrDefault(item => item.Owner == owner);
		}

		private DebugDisplayOwnerSnapshot CreateOwnerSnapshot(UnityEngine.Object owner)
		{
			if (owner == null)
				return null;
			var gameObject = owner as GameObject ?? (owner as Component == null ? null : ((Component)owner).gameObject);
			if (gameObject == null)
				return null;
			var handle = DebugObjectHandle.Create(Session.SessionId, ++m_NextObject);
			var parts = new List<string>();
			for (var current = gameObject.transform; current != null; current = current.parent)
				parts.Add(current.name);
			parts.Reverse();
			return new DebugDisplayOwnerSnapshot(
				handle,
				owner.GetType().FullName,
				gameObject.scene.name,
				string.Join("/", parts.ToArray()),
				true,
				gameObject.transform.position,
				gameObject.transform.rotation,
				gameObject.transform.lossyScale
			);
		}

		private void Drop(string code, string identity, string message)
		{
			m_DroppedThisFrame++;
			AddDiagnostic(code, identity, message);
		}

		private void MarkRelationsMissing(DexcDisplaySubjectRecord target)
		{
			foreach (var pair in m_PersistentVisuals.Where(item => item.Value.Snapshot.Target == target.Id).ToList())
			{
				var old = pair.Value;
				m_PersistentVisuals[pair.Key] = new DexcDisplayVisualRecord(
					old.Key,
					old.Subject,
					old.Kind,
					old.Snapshot.WithMissingTarget(),
					old.HasExpiry,
					old.ExpiresAt
				);
			}
			foreach (var pair in m_FrameVisuals.Where(item => item.Value.Snapshot.Target == target.Id).ToList())
			{
				var old = pair.Value;
				m_FrameVisuals[pair.Key] = new DexcDisplayVisualRecord(
					old.Key,
					old.Subject,
					old.Kind,
					old.Snapshot.WithMissingTarget(),
					old.HasExpiry,
					old.ExpiresAt
				);
			}
		}

		private static bool Finite(Vector3 value)
		{
			return Finite(value.x) && Finite(value.y) && Finite(value.z);
		}

		private static bool Finite(Quaternion value)
		{
			return Finite(value.x) && Finite(value.y) && Finite(value.z) && Finite(value.w);
		}

		private static bool Finite(float value)
		{
			return !float.IsNaN(value) && !float.IsInfinity(value);
		}
	}

	internal static class DexcDisplayRuntime
	{
		/// <summary>
		/// The session's registry, or null while no session exists: drawing without a session does nothing, so a game can
		/// call Display wherever its state lives without checking for Dexc first.
		/// </summary>
		internal static DexcDisplayRegistry Registry
		{
			get
			{
				var session = DexcDebugger.CurrentSession;
				if (session == null)
					return null;
				// Every Display API funnels through here, so this is where Display starts drawing itself.
				DexcDisplayDriver.Ensure();
				DexcPlayerRenderer.EnsureInitialized();
				return session.Display ?? (session.Display = new DexcDisplayRegistry(session));
			}
		}

		internal static void EndSession(DexcRuntimeSession session)
		{
			if (session != null && session.Display != null)
				session.Display.EndSession();
		}

		internal static void Tick()
		{
			var session = DexcDebugger.CurrentSession;
			if (session == null)
				return;
			DexcDisplayWidgets.Draw();
			if (session.Display != null)
				session.Display.Tick();
		}

	}

	internal sealed class DexcDisplayDriver : MonoBehaviour
	{
		private void LateUpdate()
		{
			DexcDisplayRuntime.Tick();
		}

		internal static void Destroy()
		{
			var root = DexcRoot.Current;
			var driver = root.DisplayDriver;
			root.DisplayDriver = null;
			DexcRoot.DestroyObject(driver == null ? null : driver.gameObject);
		}

		private void OnGUI()
		{
			// The menu draws from its own OnGUI, and the order between MonoBehaviours is not defined; depth keeps Display behind it.
			GUI.depth = DexcDisplayOverlay.GuiDepth;
			DexcDisplayOverlay.Draw();
		}

		/// <summary>Creates the driver the first time Display is used. Never while the Editor is not playing.</summary>
		internal static void Ensure()
		{
#if DEXC_DEBUGGER
			var root = DexcRoot.Current;
			if (root.DisplayDriver != null || !Application.isPlaying)
				return;
			var gameObject = new GameObject("DexcDisplayDriver");
			// No hide flags: Play Mode owns the lifetime, so leaving it destroys this with everything else.
			UnityEngine.Object.DontDestroyOnLoad(gameObject);
			root.DisplayDriver = gameObject.AddComponent<DexcDisplayDriver>();
#endif
		}
	}
}
