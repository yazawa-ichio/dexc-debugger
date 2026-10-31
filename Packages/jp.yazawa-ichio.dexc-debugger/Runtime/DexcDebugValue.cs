using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Dexc
{
	/// <summary>Value tree kinds shared by Input, UI, history, and adapters.</summary>
	public enum DebugValueKind
	{
		Null = 0,
		Boolean = 1,
		SignedInteger = 2,
		UnsignedInteger = 3,
		FloatingPoint = 4,
		String = 5,
		Array = 6,
		Object = 7,
	}

	/// <summary>One immutable, ordered member of a DebugValue object.</summary>
	public sealed class DebugValueMember
	{
		public DebugValueMember(string name, DebugValue value)
		{
			if (string.IsNullOrEmpty(name))
			{
				throw new ArgumentException("Member name must not be empty.", nameof(name));
			}

			Name = name;
			Value = value ?? throw new ArgumentNullException(nameof(value));
		}

		public string Name { get; }

		public DebugValue Value { get; }
	}

	/// <summary>Immutable value tree used at the Debug input boundary.</summary>
	public sealed class DebugValue : IEquatable<DebugValue>
	{
		public const int MaxDepth = 16;
		public const int MaxNodeCount = 4096;
		public const int MaxArrayLength = 1024;

		private readonly bool m_Boolean;
		private readonly long m_SignedInteger;
		private readonly ulong m_UnsignedInteger;
		private readonly double m_FloatingPoint;
		private readonly string m_String;
		private readonly IReadOnlyList<DebugValue> m_Array;
		private readonly IReadOnlyList<DebugValueMember> m_Object;

		private DebugValue(DebugValueKind kind)
		{
			Kind = kind;
		}

		private DebugValue(bool value)
		{
			Kind = DebugValueKind.Boolean;
			m_Boolean = value;
		}

		private DebugValue(long value)
		{
			Kind = DebugValueKind.SignedInteger;
			m_SignedInteger = value;
		}

		private DebugValue(ulong value)
		{
			Kind = DebugValueKind.UnsignedInteger;
			m_UnsignedInteger = value;
		}

		private DebugValue(double value)
		{
			if (double.IsNaN(value) || double.IsInfinity(value))
			{
				throw new ArgumentOutOfRangeException(nameof(value), "FloatingPoint values must be finite.");
			}

			Kind = DebugValueKind.FloatingPoint;
			m_FloatingPoint = value;
		}

		private DebugValue(string value)
		{
			Kind = DebugValueKind.String;
			m_String = value;
		}

		private DebugValue(IReadOnlyList<DebugValue> values)
		{
			Kind = DebugValueKind.Array;
			m_Array = values;
		}

		private DebugValue(IReadOnlyList<DebugValueMember> members)
		{
			Kind = DebugValueKind.Object;
			m_Object = members;
		}

		public static DebugValue Null { get; } = new DebugValue(DebugValueKind.Null);

		public DebugValueKind Kind { get; }

		public static DebugValue From(bool value)
		{
			return new DebugValue(value);
		}

		public static DebugValue From(sbyte value)
		{
			return new DebugValue((long)value);
		}

		public static DebugValue From(short value)
		{
			return new DebugValue((long)value);
		}

		public static DebugValue From(int value)
		{
			return new DebugValue((long)value);
		}

		public static DebugValue From(long value)
		{
			return new DebugValue(value);
		}

		public static DebugValue From(byte value)
		{
			return new DebugValue((ulong)value);
		}

		public static DebugValue From(ushort value)
		{
			return new DebugValue((ulong)value);
		}

		public static DebugValue From(uint value)
		{
			return new DebugValue((ulong)value);
		}

		public static DebugValue From(ulong value)
		{
			return new DebugValue(value);
		}

		public static DebugValue From(float value)
		{
			return new DebugValue((double)value);
		}

		public static DebugValue From(double value)
		{
			return new DebugValue(value);
		}

		public static DebugValue From(string value)
		{
			if (value == null)
			{
				throw new ArgumentNullException(nameof(value));
			}

			return new DebugValue(value);
		}

		public static DebugValue FromArray(IEnumerable<DebugValue> values)
		{
			if (values == null)
			{
				throw new ArgumentNullException(nameof(values));
			}

			var copy = new List<DebugValue>();
			foreach (var value in values)
			{
				if (value == null)
				{
					throw new ArgumentNullException(nameof(values), "Array elements must not be null.");
				}

				copy.Add(value);
			}

			if (copy.Count > MaxArrayLength)
			{
				throw new ArgumentException("Array length exceeds the DebugValue limit.", nameof(values));
			}

			ValidateTree(copy, null);
			return new DebugValue(new ReadOnlyCollection<DebugValue>(copy));
		}

		public static DebugValue FromArray(params DebugValue[] values)
		{
			return FromArray((IEnumerable<DebugValue>)values);
		}

		public static DebugValue FromObject(IEnumerable<DebugValueMember> members)
		{
			if (members == null)
			{
				throw new ArgumentNullException(nameof(members));
			}

			var copy = new List<DebugValueMember>();
			var names = new HashSet<string>(StringComparer.Ordinal);
			foreach (var member in members)
			{
				if (member == null)
				{
					throw new ArgumentNullException(nameof(members), "Object members must not be null.");
				}

				if (!names.Add(member.Name))
				{
					throw new ArgumentException("Object member names must be unique.", nameof(members));
				}

				copy.Add(member);
			}

			ValidateTree(null, copy);
			return new DebugValue(new ReadOnlyCollection<DebugValueMember>(copy));
		}

		public static DebugValue FromObject(params DebugValueMember[] members)
		{
			return FromObject((IEnumerable<DebugValueMember>)members);
		}

		public bool TryGetBoolean(out bool value)
		{
			value = m_Boolean;
			return Kind == DebugValueKind.Boolean;
		}

		public bool TryGetSignedInteger(out long value)
		{
			value = m_SignedInteger;
			return Kind == DebugValueKind.SignedInteger;
		}

		public bool TryGetUnsignedInteger(out ulong value)
		{
			value = m_UnsignedInteger;
			return Kind == DebugValueKind.UnsignedInteger;
		}

		public bool TryGetFloatingPoint(out double value)
		{
			value = m_FloatingPoint;
			return Kind == DebugValueKind.FloatingPoint;
		}

		public bool TryGetString(out string value)
		{
			value = m_String;
			return Kind == DebugValueKind.String;
		}

		public bool TryGetArray(out IReadOnlyList<DebugValue> values)
		{
			values = m_Array;
			return Kind == DebugValueKind.Array;
		}

		public bool TryGetObject(out IReadOnlyList<DebugValueMember> members)
		{
			members = m_Object;
			return Kind == DebugValueKind.Object;
		}

		public bool Equals(DebugValue other)
		{
			if (ReferenceEquals(other, null) || Kind != other.Kind)
			{
				return false;
			}

			switch (Kind)
			{
				case DebugValueKind.Null:
					return true;
				case DebugValueKind.Boolean:
					return m_Boolean == other.m_Boolean;
				case DebugValueKind.SignedInteger:
					return m_SignedInteger == other.m_SignedInteger;
				case DebugValueKind.UnsignedInteger:
					return m_UnsignedInteger == other.m_UnsignedInteger;
				case DebugValueKind.FloatingPoint:
					return m_FloatingPoint.Equals(other.m_FloatingPoint);
				case DebugValueKind.String:
					return string.Equals(m_String, other.m_String, StringComparison.Ordinal);
				case DebugValueKind.Array:
					return AreArraysEqual(m_Array, other.m_Array);
				case DebugValueKind.Object:
					return AreObjectsEqual(m_Object, other.m_Object);
				default:
					return false;
			}
		}

		public override bool Equals(object obj)
		{
			return Equals(obj as DebugValue);
		}

		public override int GetHashCode()
		{
			var hash = new HashCode();
			hash.Add(Kind);
			switch (Kind)
			{
				case DebugValueKind.Boolean:
					hash.Add(m_Boolean);
					break;
				case DebugValueKind.SignedInteger:
					hash.Add(m_SignedInteger);
					break;
				case DebugValueKind.UnsignedInteger:
					hash.Add(m_UnsignedInteger);
					break;
				case DebugValueKind.FloatingPoint:
					hash.Add(m_FloatingPoint);
					break;
				case DebugValueKind.String:
					hash.Add(m_String, StringComparer.Ordinal);
					break;
				case DebugValueKind.Array:
					foreach (var value in m_Array)
					{
						hash.Add(value);
					}
					break;
				case DebugValueKind.Object:
					foreach (var member in m_Object)
					{
						hash.Add(member.Name, StringComparer.Ordinal);
						hash.Add(member.Value);
					}
					break;
			}

			return hash.ToHashCode();
		}

		public static bool operator ==(DebugValue left, DebugValue right)
		{
			return ReferenceEquals(left, right) || (!ReferenceEquals(left, null) && left.Equals(right));
		}

		public static bool operator !=(DebugValue left, DebugValue right)
		{
			return !(left == right);
		}

		private static bool AreArraysEqual(IReadOnlyList<DebugValue> left, IReadOnlyList<DebugValue> right)
		{
			if (left.Count != right.Count)
			{
				return false;
			}

			for (var index = 0; index < left.Count; index++)
			{
				if (!left[index].Equals(right[index]))
				{
					return false;
				}
			}

			return true;
		}

		private static bool AreObjectsEqual(IReadOnlyList<DebugValueMember> left, IReadOnlyList<DebugValueMember> right)
		{
			if (left.Count != right.Count)
			{
				return false;
			}

			for (var index = 0; index < left.Count; index++)
			{
				if (
					!string.Equals(left[index].Name, right[index].Name, StringComparison.Ordinal)
					|| !left[index].Value.Equals(right[index].Value)
				)
				{
					return false;
				}
			}

			return true;
		}

		private static void ValidateTree(IReadOnlyList<DebugValue> values, IReadOnlyList<DebugValueMember> members)
		{
			var nodeCount = 1;
			if (values != null)
			{
				foreach (var value in values)
				{
					ValidateTree(value, 2, ref nodeCount);
				}
			}

			if (members != null)
			{
				foreach (var member in members)
				{
					ValidateTree(member.Value, 2, ref nodeCount);
				}
			}
		}

		private static void ValidateTree(DebugValue value, int depth, ref int nodeCount)
		{
			if (value == null)
			{
				throw new ArgumentNullException(nameof(value));
			}

			if (depth > MaxDepth)
			{
				throw new ArgumentException("DebugValue depth exceeds the configured limit.");
			}

			nodeCount++;
			if (nodeCount > MaxNodeCount)
			{
				throw new ArgumentException("DebugValue node count exceeds the configured limit.");
			}

			if (value.Kind == DebugValueKind.Array)
			{
				if (value.m_Array.Count > MaxArrayLength)
				{
					throw new ArgumentException("DebugValue array length exceeds the configured limit.");
				}

				foreach (var child in value.m_Array)
				{
					ValidateTree(child, depth + 1, ref nodeCount);
				}
			}
			else if (value.Kind == DebugValueKind.Object)
			{
				foreach (var member in value.m_Object)
				{
					ValidateTree(member.Value, depth + 1, ref nodeCount);
				}
			}
		}
	}
}
