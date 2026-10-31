using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Dexc
{
	/// <summary>Immutable named child schema in an object schema.</summary>
	public sealed class DebugInputMemberSchema
	{
		public DebugInputMemberSchema(string name, DebugInputSchema schema)
		{
			if (string.IsNullOrEmpty(name))
			{
				throw new ArgumentException("Member name must not be empty.", nameof(name));
			}

			Name = name;
			Schema = schema ?? throw new ArgumentNullException(nameof(schema));
		}

		public string Name { get; }

		public DebugInputSchema Schema { get; }
	}

	/// <summary>Immutable structure and constraint contract for a DebugValue.</summary>
	public sealed class DebugInputSchema
	{
		private DebugInputSchema(
			DebugValueKind valueKind,
			bool isNullable,
			string semanticType,
			DebugInputSchema element,
			IReadOnlyList<DebugInputMemberSchema> members,
			IReadOnlyList<DebugInputConstraint> constraints
		)
		{
			ValueKind = valueKind;
			IsNullable = isNullable;
			SemanticType = semanticType ?? string.Empty;
			Element = element;
			Members = members;
			Constraints = constraints;
			ValidateDefinition();
		}

		public DebugValueKind ValueKind { get; }

		public bool IsNullable { get; }

		public string SemanticType { get; }

		public DebugInputSchema Element { get; }

		public IReadOnlyList<DebugInputMemberSchema> Members { get; }

		public IReadOnlyList<DebugInputConstraint> Constraints { get; }

		public static DebugInputSchema Boolean(
			bool isNullable = false,
			string semanticType = null,
			IEnumerable<DebugInputConstraint> constraints = null
		)
		{
			return Primitive(DebugValueKind.Boolean, isNullable, semanticType, constraints);
		}

		public static DebugInputSchema SignedInteger(
			bool isNullable = false,
			string semanticType = null,
			IEnumerable<DebugInputConstraint> constraints = null
		)
		{
			return Primitive(DebugValueKind.SignedInteger, isNullable, semanticType, constraints);
		}

		public static DebugInputSchema UnsignedInteger(
			bool isNullable = false,
			string semanticType = null,
			IEnumerable<DebugInputConstraint> constraints = null
		)
		{
			return Primitive(DebugValueKind.UnsignedInteger, isNullable, semanticType, constraints);
		}

		public static DebugInputSchema FloatingPoint(
			bool isNullable = false,
			string semanticType = null,
			IEnumerable<DebugInputConstraint> constraints = null
		)
		{
			return Primitive(DebugValueKind.FloatingPoint, isNullable, semanticType, constraints);
		}

		public static DebugInputSchema String(
			bool isNullable = false,
			string semanticType = null,
			IEnumerable<DebugInputConstraint> constraints = null
		)
		{
			return Primitive(DebugValueKind.String, isNullable, semanticType, constraints);
		}

		public static DebugInputSchema Array(
			DebugInputSchema element,
			bool isNullable = false,
			string semanticType = null,
			IEnumerable<DebugInputConstraint> constraints = null
		)
		{
			if (element == null)
			{
				throw new ArgumentNullException(nameof(element));
			}

			return new DebugInputSchema(
				DebugValueKind.Array,
				isNullable,
				semanticType,
				element,
				new ReadOnlyCollection<DebugInputMemberSchema>(new List<DebugInputMemberSchema>()),
				CopyConstraints(constraints)
			);
		}

		public static DebugInputSchema Object(
			IEnumerable<DebugInputMemberSchema> members,
			bool isNullable = false,
			string semanticType = null,
			IEnumerable<DebugInputConstraint> constraints = null
		)
		{
			if (members == null)
			{
				throw new ArgumentNullException(nameof(members));
			}

			var memberCopy = new List<DebugInputMemberSchema>();
			var names = new HashSet<string>(StringComparer.Ordinal);
			foreach (var member in members)
			{
				if (member == null)
				{
					throw new ArgumentNullException(nameof(members));
				}

				if (!names.Add(member.Name))
				{
					throw new ArgumentException("Object schema member names must be unique.", nameof(members));
				}

				memberCopy.Add(member);
			}

			return new DebugInputSchema(
				DebugValueKind.Object,
				isNullable,
				semanticType,
				null,
				new ReadOnlyCollection<DebugInputMemberSchema>(memberCopy),
				CopyConstraints(constraints)
			);
		}

		public static DebugInputSchema Object(params DebugInputMemberSchema[] members)
		{
			return Object((IEnumerable<DebugInputMemberSchema>)members);
		}

		internal bool ValidateValue(DebugValue value, DebugValidation validation)
		{
			if (validation == null)
			{
				throw new ArgumentNullException(nameof(validation));
			}

			if (value == null)
			{
				validation.Error("null-debug-value", "DebugValue must not be C# null.");
				return false;
			}

			if (value.Kind == DebugValueKind.Null)
			{
				if (!IsNullable)
				{
					validation.Error("null-not-allowed", "This schema does not allow a Null value.");
				}

				foreach (var constraint in Constraints)
				{
					if (constraint.Kind == DebugInputConstraintKind.SelectionRequired)
					{
						validation.Error("selection-required", "A selection is required.");
					}
				}

				return IsNullable && !validation.HasErrors;
			}

			if (value.Kind != ValueKind)
			{
				validation.Error("value-kind-mismatch", "The DebugValue kind does not match the schema.");
				return false;
			}

			var isValid = true;
			if (ValueKind == DebugValueKind.Array)
			{
				IReadOnlyList<DebugValue> values;
				value.TryGetArray(out values);
				foreach (var child in values)
				{
					if (!Element.ValidateValue(child, validation))
					{
						isValid = false;
					}
				}
			}
			else if (ValueKind == DebugValueKind.Object)
			{
				IReadOnlyList<DebugValueMember> values;
				value.TryGetObject(out values);
				for (var index = 0; index < Members.Count; index++)
				{
					var member = Members[index];
					DebugValueMember match = null;
					foreach (var valueMember in values)
					{
						if (string.Equals(valueMember.Name, member.Name, StringComparison.Ordinal))
						{
							match = valueMember;
							break;
						}
					}

					if (match == null)
					{
						validation.Error("missing-object-member", "The object value is missing a schema member.");
						isValid = false;
					}
					else if (!member.Schema.ValidateValue(match.Value, validation))
					{
						isValid = false;
					}
				}

				foreach (var valueMember in values)
				{
					var declared = false;
					foreach (var member in Members)
					{
						if (string.Equals(member.Name, valueMember.Name, StringComparison.Ordinal))
						{
							declared = true;
							break;
						}
					}

					if (!declared)
					{
						validation.Error("unknown-object-member", "The object value contains a member not declared by the schema.");
						isValid = false;
					}
				}
			}

			ValidateConstraints(value, validation);
			return isValid && !validation.HasErrors;
		}

		private static DebugInputSchema Primitive(
			DebugValueKind kind,
			bool isNullable,
			string semanticType,
			IEnumerable<DebugInputConstraint> constraints
		)
		{
			return new DebugInputSchema(
				kind,
				isNullable,
				semanticType,
				null,
				new ReadOnlyCollection<DebugInputMemberSchema>(new List<DebugInputMemberSchema>()),
				CopyConstraints(constraints)
			);
		}

		private static IReadOnlyList<DebugInputConstraint> CopyConstraints(IEnumerable<DebugInputConstraint> constraints)
		{
			var copy = new List<DebugInputConstraint>();
			if (constraints != null)
			{
				foreach (var constraint in constraints)
				{
					if (constraint == null)
					{
						throw new ArgumentNullException(nameof(constraints));
					}

					copy.Add(constraint);
				}
			}

			return new ReadOnlyCollection<DebugInputConstraint>(copy);
		}

		private void ValidateDefinition()
		{
			if ((ValueKind == DebugValueKind.Array && Element == null) || (ValueKind != DebugValueKind.Array && Element != null))
			{
				throw new ArgumentException("Only an Array schema may have an Element schema.");
			}

			if (Members == null)
			{
				throw new ArgumentException("Schema members must be initialized.");
			}

			if (ValueKind != DebugValueKind.Object && Members.Count != 0)
			{
				throw new ArgumentException("Only an Object schema may have members.");
			}

			ValidateConstraintsForKind();
			ValidateSchemaTree(this);
		}

		private void ValidateConstraintsForKind()
		{
			DebugInputConstraint minimum = null;
			DebugInputConstraint maximum = null;
			foreach (var constraint in Constraints)
			{
				switch (constraint.Kind)
				{
					case DebugInputConstraintKind.Minimum:
					case DebugInputConstraintKind.Maximum:
						if (
							ValueKind != DebugValueKind.SignedInteger
							&& ValueKind != DebugValueKind.UnsignedInteger
							&& ValueKind != DebugValueKind.FloatingPoint
						)
						{
							throw new ArgumentException("Numeric constraints require a numeric schema.");
						}

						if (constraint.Value.Kind != ValueKind)
						{
							throw new ArgumentException("Numeric constraint kind must match the schema kind.");
						}

						if (constraint.Kind == DebugInputConstraintKind.Minimum && minimum == null)
						{
							minimum = constraint;
						}
						if (constraint.Kind == DebugInputConstraintKind.Maximum && maximum == null)
						{
							maximum = constraint;
						}
						break;
					case DebugInputConstraintKind.MinimumLength:
					case DebugInputConstraintKind.MaximumLength:
						if (ValueKind != DebugValueKind.String && ValueKind != DebugValueKind.Array)
						{
							throw new ArgumentException("Length constraints require a String or Array schema.");
						}
						break;
					case DebugInputConstraintKind.SelectionRequired:
						if (ValueKind != DebugValueKind.String)
						{
							throw new ArgumentException("SelectionRequired requires a String schema.");
						}
						break;
					default:
						throw new ArgumentOutOfRangeException(nameof(Constraints));
				}
			}

			if (minimum != null && maximum != null && CompareNumeric(minimum.Value, maximum.Value) > 0)
			{
				throw new ArgumentException("Minimum constraint must not exceed Maximum constraint.");
			}
		}

		private void ValidateConstraints(DebugValue value, DebugValidation validation)
		{
			foreach (var constraint in Constraints)
			{
				switch (constraint.Kind)
				{
					case DebugInputConstraintKind.Minimum:
						if (CompareNumeric(value, constraint.Value) < 0)
						{
							validation.Error("constraint-minimum", "The value is below the schema minimum.");
						}
						break;
					case DebugInputConstraintKind.Maximum:
						if (CompareNumeric(value, constraint.Value) > 0)
						{
							validation.Error("constraint-maximum", "The value is above the schema maximum.");
						}
						break;
					case DebugInputConstraintKind.MinimumLength:
						if (GetLength(value) < constraint.Length)
						{
							validation.Error("constraint-minimum-length", "The value is shorter than the schema minimum length.");
						}
						break;
					case DebugInputConstraintKind.MaximumLength:
						if (GetLength(value) > constraint.Length)
						{
							validation.Error("constraint-maximum-length", "The value is longer than the schema maximum length.");
						}
						break;
					case DebugInputConstraintKind.SelectionRequired:
						if (
							value.Kind == DebugValueKind.Null
							|| (value.Kind == DebugValueKind.String && value.TryGetString(out var key) && key.Length == 0)
						)
						{
							validation.Error("selection-required", "A selection is required.");
						}
						break;
				}
			}
		}

		private static int GetLength(DebugValue value)
		{
			if (value.Kind == DebugValueKind.String)
			{
				string text;
				value.TryGetString(out text);
				return text.Length;
			}

			IReadOnlyList<DebugValue> values;
			value.TryGetArray(out values);
			return values.Count;
		}

		private static int CompareNumeric(DebugValue left, DebugValue right)
		{
			if (left.Kind == DebugValueKind.SignedInteger)
			{
				long leftValue;
				long rightValue;
				left.TryGetSignedInteger(out leftValue);
				right.TryGetSignedInteger(out rightValue);
				return leftValue.CompareTo(rightValue);
			}

			if (left.Kind == DebugValueKind.UnsignedInteger)
			{
				ulong leftValue;
				ulong rightValue;
				left.TryGetUnsignedInteger(out leftValue);
				right.TryGetUnsignedInteger(out rightValue);
				return leftValue.CompareTo(rightValue);
			}

			double leftFloating;
			double rightFloating;
			left.TryGetFloatingPoint(out leftFloating);
			right.TryGetFloatingPoint(out rightFloating);
			return leftFloating.CompareTo(rightFloating);
		}

		private static void ValidateSchemaTree(DebugInputSchema root)
		{
			var nodeCount = 0;
			ValidateSchemaTree(root, 1, ref nodeCount);
		}

		private static void ValidateSchemaTree(DebugInputSchema schema, int depth, ref int nodeCount)
		{
			if (depth > DebugValue.MaxDepth)
			{
				throw new ArgumentException("DebugInputSchema depth exceeds the configured limit.");
			}

			nodeCount++;
			if (nodeCount > DebugValue.MaxNodeCount)
			{
				throw new ArgumentException("DebugInputSchema node count exceeds the configured limit.");
			}

			if (schema.ValueKind == DebugValueKind.Array)
			{
				ValidateSchemaTree(schema.Element, depth + 1, ref nodeCount);
			}
			else if (schema.ValueKind == DebugValueKind.Object)
			{
				foreach (var member in schema.Members)
				{
					ValidateSchemaTree(member.Schema, depth + 1, ref nodeCount);
				}
			}
		}
	}
}
