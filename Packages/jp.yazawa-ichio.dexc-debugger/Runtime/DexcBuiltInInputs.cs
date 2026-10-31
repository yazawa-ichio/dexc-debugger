using System;
using System.Linq;
using UnityEngine;

namespace Dexc
{
	/// <summary>Boolean input.</summary>
	public sealed class InputBool : DebugInput<bool>
	{
		private static readonly DebugInputSchema s_Schema = DebugInputSchema.Boolean();

		public InputBool()
			: this(false) { }

		public InputBool(bool initialValue)
			: base(initialValue, s_Schema) { }

		protected override DebugValue WriteValue(bool value)
		{
			return DebugValue.From(value);
		}

		protected override DebugInputReadResult<bool> ReadValue(DebugValue value)
		{
			return value.TryGetBoolean(out var result)
				? DebugInputReadResult<bool>.Success(result)
				: DebugInputReadResult<bool>.Failure("Boolean value is required.");
		}
	}

	/// <summary>32-bit signed integer input.</summary>
	public sealed class InputInt : DebugInput<int>
	{
		private static readonly DebugInputSchema s_Schema = DebugInputSchema.SignedInteger();

		public InputInt()
			: this(0) { }

		public InputInt(int initialValue)
			: base(initialValue, s_Schema) { }

		protected override DebugValue WriteValue(int value)
		{
			return DebugValue.From(value);
		}

		protected override DebugInputReadResult<int> ReadValue(DebugValue value)
		{
			if (!value.TryGetSignedInteger(out var result) || result < int.MinValue || result > int.MaxValue)
			{
				return DebugInputReadResult<int>.Failure("Signed integer is outside the Int32 range.");
			}

			return DebugInputReadResult<int>.Success((int)result);
		}
	}

	/// <summary>64-bit signed integer input.</summary>
	public sealed class InputLong : DebugInput<long>
	{
		private static readonly DebugInputSchema s_Schema = DebugInputSchema.SignedInteger();

		public InputLong()
			: this(0L) { }

		public InputLong(long initialValue)
			: base(initialValue, s_Schema) { }

		protected override DebugValue WriteValue(long value)
		{
			return DebugValue.From(value);
		}

		protected override DebugInputReadResult<long> ReadValue(DebugValue value)
		{
			return value.TryGetSignedInteger(out var result)
				? DebugInputReadResult<long>.Success(result)
				: DebugInputReadResult<long>.Failure("Signed integer is required.");
		}
	}

	/// <summary>Single-precision floating-point input.</summary>
	public sealed class InputFloat : DebugInput<float>
	{
		private static readonly DebugInputSchema s_Schema = DebugInputSchema.FloatingPoint();

		public InputFloat()
			: this(0f) { }

		public InputFloat(float initialValue)
			: base(initialValue, s_Schema) { }

		protected override DebugValue WriteValue(float value)
		{
			return DebugValue.From(value);
		}

		protected override DebugInputReadResult<float> ReadValue(DebugValue value)
		{
			if (!value.TryGetFloatingPoint(out var result) || result < float.MinValue || result > float.MaxValue)
			{
				return DebugInputReadResult<float>.Failure("Finite floating-point value is outside the Single range.");
			}

			return DebugInputReadResult<float>.Success((float)result);
		}
	}

	/// <summary>Double-precision floating-point input.</summary>
	public sealed class InputDouble : DebugInput<double>
	{
		private static readonly DebugInputSchema s_Schema = DebugInputSchema.FloatingPoint();

		public InputDouble()
			: this(0d) { }

		public InputDouble(double initialValue)
			: base(initialValue, s_Schema) { }

		protected override DebugValue WriteValue(double value)
		{
			return DebugValue.From(value);
		}

		protected override DebugInputReadResult<double> ReadValue(DebugValue value)
		{
			return value.TryGetFloatingPoint(out var result)
				? DebugInputReadResult<double>.Success(result)
				: DebugInputReadResult<double>.Failure("Finite floating-point value is required.");
		}
	}

	/// <summary>Non-null string input.</summary>
	public sealed class InputString : DebugInput<string>
	{
		private static readonly DebugInputSchema s_Schema = DebugInputSchema.String();

		public InputString()
			: this(string.Empty) { }

		public InputString(string initialValue)
			: base(initialValue ?? string.Empty, s_Schema) { }

		public new string Value
		{
			get { return base.Value; }
			set { base.Value = value ?? string.Empty; }
		}

		protected override DebugValue WriteValue(string value)
		{
			return DebugValue.From(value ?? string.Empty);
		}

		protected override DebugInputReadResult<string> ReadValue(DebugValue value)
		{
			if (!value.TryGetString(out var result))
			{
				return DebugInputReadResult<string>.Failure("String value is required; Null is not accepted.");
			}

			return DebugInputReadResult<string>.Success(result);
		}
	}

	/// <summary>Enum input serialized as the declared enum member name.</summary>
	public sealed class InputEnum<T> : DebugInput<T>
		where T : struct, Enum
	{
		private static readonly DebugInputSchema s_Schema = DebugInputSchema.String(semanticType: "enum");

		public InputEnum(T initialValue)
			: base(initialValue, s_Schema)
		{
			if (Enum.GetName(typeof(T), initialValue) == null)
				throw new ArgumentException("Initial enum value is not declared.", nameof(initialValue));
		}

		protected override DebugValue WriteValue(T value)
		{
			var name = Enum.GetName(typeof(T), value);
			if (name == null)
				throw new InvalidOperationException("Enum value is not declared.");
			return DebugValue.From(name);
		}

		protected override DebugInputReadResult<T> ReadValue(DebugValue value)
		{
			if (!value.TryGetString(out var key))
				return DebugInputReadResult<T>.Failure("Enum value must be a String key.");
			foreach (var name in Enum.GetNames(typeof(T)))
			{
				if (string.Equals(name, key, StringComparison.Ordinal))
				{
					return DebugInputReadResult<T>.Success((T)Enum.Parse(typeof(T), name, false));
				}
			}

			return DebugInputReadResult<T>.Failure("Enum key is not declared.");
		}
	}

	/// <summary>Color input represented by named floating-point components.</summary>
	public sealed class InputColor : DebugInput<Color>
	{
		private static readonly DebugInputSchema s_Schema = DebugInputSchema.Object(
			new DebugInputMemberSchema("r", DebugInputSchema.FloatingPoint()),
			new DebugInputMemberSchema("g", DebugInputSchema.FloatingPoint()),
			new DebugInputMemberSchema("b", DebugInputSchema.FloatingPoint()),
			new DebugInputMemberSchema("a", DebugInputSchema.FloatingPoint())
		);

		public InputColor()
			: this(Color.clear) { }

		public InputColor(Color initialValue)
			: base(initialValue, s_Schema) { }

		protected override DebugValue WriteValue(Color value)
		{
			return DebugValue.FromObject(
				new DebugValueMember("r", DebugValue.From(value.r)),
				new DebugValueMember("g", DebugValue.From(value.g)),
				new DebugValueMember("b", DebugValue.From(value.b)),
				new DebugValueMember("a", DebugValue.From(value.a))
			);
		}

		protected override DebugInputReadResult<Color> ReadValue(DebugValue value)
		{
			if (!DexcBuiltInInputReaders.TryReadFloats(value, new[] { "r", "g", "b", "a" }, out var components))
			{
				return DebugInputReadResult<Color>.Failure("Color requires finite floating-point r, g, b, and a components.");
			}

			return DebugInputReadResult<Color>.Success(new Color(components[0], components[1], components[2], components[3]));
		}
	}

	/// <summary>Two-component floating-point vector input.</summary>
	public sealed class InputVector2 : DebugInput<Vector2>
	{
		private static readonly DebugInputSchema s_Schema = DexcBuiltInInputSchemas.FloatingObject("x", "y");

		public InputVector2()
			: this(Vector2.zero) { }

		public InputVector2(Vector2 initialValue)
			: base(initialValue, s_Schema) { }

		protected override DebugValue WriteValue(Vector2 value)
		{
			return DexcBuiltInInputWriters.FloatingObject(new[] { "x", "y" }, new[] { value.x, value.y });
		}

		protected override DebugInputReadResult<Vector2> ReadValue(DebugValue value)
		{
			if (!DexcBuiltInInputReaders.TryReadFloats(value, new[] { "x", "y" }, out var components))
				return DebugInputReadResult<Vector2>.Failure("Vector2 requires finite floating-point x and y components.");
			return DebugInputReadResult<Vector2>.Success(new Vector2(components[0], components[1]));
		}
	}

	/// <summary>Three-component floating-point vector input.</summary>
	public sealed class InputVector3 : DebugInput<Vector3>
	{
		private static readonly DebugInputSchema s_Schema = DexcBuiltInInputSchemas.FloatingObject("x", "y", "z");

		public InputVector3()
			: this(Vector3.zero) { }

		public InputVector3(Vector3 initialValue)
			: base(initialValue, s_Schema) { }

		protected override DebugValue WriteValue(Vector3 value)
		{
			return DexcBuiltInInputWriters.FloatingObject(new[] { "x", "y", "z" }, new[] { value.x, value.y, value.z });
		}

		protected override DebugInputReadResult<Vector3> ReadValue(DebugValue value)
		{
			if (!DexcBuiltInInputReaders.TryReadFloats(value, new[] { "x", "y", "z" }, out var components))
				return DebugInputReadResult<Vector3>.Failure("Vector3 requires finite floating-point x, y, and z components.");
			return DebugInputReadResult<Vector3>.Success(new Vector3(components[0], components[1], components[2]));
		}
	}

	/// <summary>Four-component floating-point vector input.</summary>
	public sealed class InputVector4 : DebugInput<Vector4>
	{
		private static readonly DebugInputSchema s_Schema = DexcBuiltInInputSchemas.FloatingObject("x", "y", "z", "w");

		public InputVector4()
			: this(Vector4.zero) { }

		public InputVector4(Vector4 initialValue)
			: base(initialValue, s_Schema) { }

		protected override DebugValue WriteValue(Vector4 value)
		{
			return DexcBuiltInInputWriters.FloatingObject(new[] { "x", "y", "z", "w" }, new[] { value.x, value.y, value.z, value.w });
		}

		protected override DebugInputReadResult<Vector4> ReadValue(DebugValue value)
		{
			if (!DexcBuiltInInputReaders.TryReadFloats(value, new[] { "x", "y", "z", "w" }, out var components))
				return DebugInputReadResult<Vector4>.Failure("Vector4 requires finite floating-point components.");
			return DebugInputReadResult<Vector4>.Success(new Vector4(components[0], components[1], components[2], components[3]));
		}
	}

	/// <summary>Two-component signed integer vector input.</summary>
	public sealed class InputVector2Int : DebugInput<Vector2Int>
	{
		private static readonly DebugInputSchema s_Schema = DexcBuiltInInputSchemas.SignedObject("x", "y");

		public InputVector2Int()
			: this(Vector2Int.zero) { }

		public InputVector2Int(Vector2Int initialValue)
			: base(initialValue, s_Schema) { }

		protected override DebugValue WriteValue(Vector2Int value)
		{
			return DexcBuiltInInputWriters.SignedObject(new[] { "x", "y" }, new[] { value.x, value.y });
		}

		protected override DebugInputReadResult<Vector2Int> ReadValue(DebugValue value)
		{
			if (!DexcBuiltInInputReaders.TryReadInts(value, new[] { "x", "y" }, out var components))
				return DebugInputReadResult<Vector2Int>.Failure("Vector2Int requires signed integer x and y components.");
			return DebugInputReadResult<Vector2Int>.Success(new Vector2Int(components[0], components[1]));
		}
	}

	/// <summary>Three-component signed integer vector input.</summary>
	public sealed class InputVector3Int : DebugInput<Vector3Int>
	{
		private static readonly DebugInputSchema s_Schema = DexcBuiltInInputSchemas.SignedObject("x", "y", "z");

		public InputVector3Int()
			: this(Vector3Int.zero) { }

		public InputVector3Int(Vector3Int initialValue)
			: base(initialValue, s_Schema) { }

		protected override DebugValue WriteValue(Vector3Int value)
		{
			return DexcBuiltInInputWriters.SignedObject(new[] { "x", "y", "z" }, new[] { value.x, value.y, value.z });
		}

		protected override DebugInputReadResult<Vector3Int> ReadValue(DebugValue value)
		{
			if (!DexcBuiltInInputReaders.TryReadInts(value, new[] { "x", "y", "z" }, out var components))
				return DebugInputReadResult<Vector3Int>.Failure("Vector3Int requires signed integer components.");
			return DebugInputReadResult<Vector3Int>.Success(new Vector3Int(components[0], components[1], components[2]));
		}
	}

	internal static class DexcBuiltInInputSchemas
	{
		internal static DebugInputSchema FloatingObject(params string[] names)
		{
			return DebugInputSchema.Object(names.Select(name => new DebugInputMemberSchema(name, DebugInputSchema.FloatingPoint())));
		}

		internal static DebugInputSchema SignedObject(params string[] names)
		{
			return DebugInputSchema.Object(names.Select(name => new DebugInputMemberSchema(name, DebugInputSchema.SignedInteger())));
		}
	}

	internal static class DexcBuiltInInputWriters
	{
		internal static DebugValue FloatingObject(string[] names, float[] values)
		{
			return DebugValue.FromObject(names.Select((name, index) => new DebugValueMember(name, DebugValue.From(values[index]))));
		}

		internal static DebugValue SignedObject(string[] names, int[] values)
		{
			return DebugValue.FromObject(names.Select((name, index) => new DebugValueMember(name, DebugValue.From(values[index]))));
		}
	}

	internal static class DexcBuiltInInputReaders
	{
		internal static bool TryReadFloats(DebugValue value, string[] names, out float[] result)
		{
			result = new float[names.Length];
			if (!value.TryGetObject(out var members) || members.Count != names.Length)
				return false;
			for (var index = 0; index < names.Length; index++)
			{
				if (
					!string.Equals(members[index].Name, names[index], StringComparison.Ordinal)
					|| !members[index].Value.TryGetFloatingPoint(out var number)
					|| number < float.MinValue
					|| number > float.MaxValue
				)
					return false;
				result[index] = (float)number;
			}
			return true;
		}

		internal static bool TryReadInts(DebugValue value, string[] names, out int[] result)
		{
			result = new int[names.Length];
			if (!value.TryGetObject(out var members) || members.Count != names.Length)
				return false;
			for (var index = 0; index < names.Length; index++)
			{
				if (
					!string.Equals(members[index].Name, names[index], StringComparison.Ordinal)
					|| !members[index].Value.TryGetSignedInteger(out var number)
					|| number < int.MinValue
					|| number > int.MaxValue
				)
					return false;
				result[index] = (int)number;
			}
			return true;
		}
	}
}
