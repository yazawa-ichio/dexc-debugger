using System;

namespace Dexc
{
	/// <summary>Closed set of schema constraint kinds.</summary>
	public enum DebugInputConstraintKind
	{
		Minimum = 0,
		Maximum = 1,
		MinimumLength = 2,
		MaximumLength = 3,
		SelectionRequired = 4,
	}

	/// <summary>Immutable, data-only constraint attached to an input schema.</summary>
	public sealed class DebugInputConstraint
	{
		private DebugInputConstraint(DebugInputConstraintKind kind, DebugValue value, int length)
		{
			Kind = kind;
			Value = value;
			Length = length;
		}

		public DebugInputConstraintKind Kind { get; }

		public DebugValue Value { get; }

		public int Length { get; }

		public static DebugInputConstraint Minimum(DebugValue value)
		{
			ValidateNumericValue(value, nameof(value));
			return new DebugInputConstraint(DebugInputConstraintKind.Minimum, value, 0);
		}

		public static DebugInputConstraint Maximum(DebugValue value)
		{
			ValidateNumericValue(value, nameof(value));
			return new DebugInputConstraint(DebugInputConstraintKind.Maximum, value, 0);
		}

		public static DebugInputConstraint MinimumLength(int value)
		{
			ValidateLength(value, nameof(value));
			return new DebugInputConstraint(DebugInputConstraintKind.MinimumLength, DebugValue.Null, value);
		}

		public static DebugInputConstraint MaximumLength(int value)
		{
			ValidateLength(value, nameof(value));
			return new DebugInputConstraint(DebugInputConstraintKind.MaximumLength, DebugValue.Null, value);
		}

		public static DebugInputConstraint SelectionRequired()
		{
			return new DebugInputConstraint(DebugInputConstraintKind.SelectionRequired, DebugValue.Null, 0);
		}

		private static void ValidateNumericValue(DebugValue value, string parameterName)
		{
			if (value == null)
			{
				throw new ArgumentNullException(parameterName);
			}

			if (
				value.Kind != DebugValueKind.SignedInteger
				&& value.Kind != DebugValueKind.UnsignedInteger
				&& value.Kind != DebugValueKind.FloatingPoint
			)
			{
				throw new ArgumentException("Numeric constraints require a numeric DebugValue.", parameterName);
			}
		}

		private static void ValidateLength(int value, string parameterName)
		{
			if (value < 0)
			{
				throw new ArgumentOutOfRangeException(parameterName);
			}
		}
	}
}
