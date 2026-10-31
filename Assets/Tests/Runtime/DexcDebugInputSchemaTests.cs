using System;
using System.Collections.Generic;
using Dexc;
using NUnit.Framework;

namespace DexcDebugger.Tests
{
	public sealed class DexcDebugInputSchemaTests
	{
#if DEXC_DEBUGGER
		[Test]
		public void SchemaAndConstraintFactoriesRejectInvalidCombinations()
		{
			Assert.Throws<ArgumentException>(() =>
				DebugInputSchema.FloatingPoint(constraints: new[] { DebugInputConstraint.Minimum(DebugValue.From(1)) })
			);
			Assert.Throws<ArgumentException>(() =>
				DebugInputSchema.String(constraints: new[] { DebugInputConstraint.Minimum(DebugValue.From(1)) })
			);
			Assert.Throws<ArgumentException>(() =>
				DebugInputSchema.String(
					constraints: new[] { DebugInputConstraint.SelectionRequired(), DebugInputConstraint.Minimum(DebugValue.From(1)) }
				)
			);
			Assert.Throws<ArgumentException>(() =>
				DebugInputSchema.SignedInteger(
					constraints: new[]
					{
						DebugInputConstraint.Minimum(DebugValue.From(2)),
						DebugInputConstraint.Maximum(DebugValue.From(1)),
					}
				)
			);
			Assert.Throws<ArgumentOutOfRangeException>(() => DebugInputConstraint.MinimumLength(-1));
			Assert.Throws<ArgumentException>(() =>
				DebugInputSchema.Object(
					new DebugInputMemberSchema("duplicate", DebugInputSchema.Boolean()),
					new DebugInputMemberSchema("duplicate", DebugInputSchema.Boolean())
				)
			);
		}

		[Test]
		public void SchemaCopiesConstraintsAndValidatesClosedObjectStructure()
		{
			var constraints = new List<DebugInputConstraint>
			{
				DebugInputConstraint.Minimum(DebugValue.From(1)),
				DebugInputConstraint.Maximum(DebugValue.From(3)),
			};
			var schema = DebugInputSchema.SignedInteger(constraints: constraints);
			constraints.Clear();
			Assert.That(schema.Constraints, Has.Count.EqualTo(2));

			var objectSchema = DebugInputSchema.Object(
				new DebugInputMemberSchema("count", schema),
				new DebugInputMemberSchema("label", DebugInputSchema.String())
			);
			var valid = DebugValue.FromObject(
				new DebugValueMember("count", DebugValue.From(2)),
				new DebugValueMember("label", DebugValue.From("ok"))
			);
			var invalid = DebugValue.FromObject(
				new DebugValueMember("count", DebugValue.From(4)),
				new DebugValueMember("extra", DebugValue.From(true))
			);
			var validation = new DebugValidation();
			var invalidResult = objectSchema.ValidateValue(invalid, validation);

			Assert.That(objectSchema.Members, Has.Count.EqualTo(2));
			Assert.That(objectSchema.Members[0].Name, Is.EqualTo("count"));
			Assert.That(objectSchema.ValidateValue(valid, new DebugValidation()), Is.True);
			Assert.That(invalidResult, Is.False);
			Assert.That(validation.Issues, Has.Some.Property("Code").EqualTo("constraint-maximum"));
			Assert.That(validation.Issues, Has.Some.Property("Code").EqualTo("missing-object-member"));
			Assert.That(validation.Issues, Has.Some.Property("Code").EqualTo("unknown-object-member"));
		}

		[Test]
		public void NullableAndLengthConstraintsFollowSchemaRules()
		{
			var schema = DebugInputSchema.String(
				isNullable: true,
				constraints: new[] { DebugInputConstraint.MinimumLength(2), DebugInputConstraint.MaximumLength(4) }
			);
			var nullValidation = new DebugValidation();
			var shortValidation = new DebugValidation();

			Assert.That(schema.ValidateValue(DebugValue.Null, nullValidation), Is.True);
			Assert.That(nullValidation.HasErrors, Is.False);

			var minimumBoundaryValidation = new DebugValidation();
			Assert.That(schema.ValidateValue(DebugValue.From("ab"), minimumBoundaryValidation), Is.True);
			Assert.That(minimumBoundaryValidation.HasErrors, Is.False);

			var maximumBoundaryValidation = new DebugValidation();
			Assert.That(schema.ValidateValue(DebugValue.From("abcd"), maximumBoundaryValidation), Is.True);
			Assert.That(maximumBoundaryValidation.HasErrors, Is.False);

			Assert.That(schema.ValidateValue(DebugValue.From("x"), shortValidation), Is.False);
			Assert.That(shortValidation.Issues[0].Code, Is.EqualTo("constraint-minimum-length"));

			var longValidation = new DebugValidation();
			Assert.That(schema.ValidateValue(DebugValue.From("abcde"), longValidation), Is.False);
			Assert.That(longValidation.Issues[0].Code, Is.EqualTo("constraint-maximum-length"));
		}

		[Test]
		public void SchemaRejectsKindMismatchAndEnforcesDepthAndNodeLimits()
		{
			var kindValidation = new DebugValidation();
			Assert.That(DebugInputSchema.SignedInteger().ValidateValue(DebugValue.From(1U), kindValidation), Is.False);
			Assert.That(kindValidation.Issues, Has.Count.EqualTo(1));
			Assert.That(kindValidation.Issues[0].Code, Is.EqualTo("value-kind-mismatch"));

			var depthLimited = DebugInputSchema.Boolean();
			for (var index = 1; index < DebugValue.MaxDepth - 1; index++)
			{
				depthLimited = DebugInputSchema.Array(depthLimited);
			}

			Assert.DoesNotThrow(() => DebugInputSchema.Array(depthLimited));
			Assert.Throws<ArgumentException>(() => DebugInputSchema.Array(DebugInputSchema.Array(depthLimited)));

			var validMembers = new List<DebugInputMemberSchema>();
			for (var index = 0; index < DebugValue.MaxNodeCount - 1; index++)
			{
				validMembers.Add(new DebugInputMemberSchema("member-" + index, DebugInputSchema.Boolean()));
			}

			Assert.DoesNotThrow(() => DebugInputSchema.Object(validMembers));
			validMembers.Add(new DebugInputMemberSchema("member-over-limit", DebugInputSchema.Boolean()));
			Assert.Throws<ArgumentException>(() => DebugInputSchema.Object(validMembers));
		}

		[Test]
		public void ValidationKeepsOrderAndRequireOnlyAddsErrorsWhenFalse()
		{
			var validation = new DebugValidation();
			validation.Warning("warning", "warning message");
			validation.Require(true, "not-added", "not added");
			validation.Require(false, "required", "required message");
			validation.Error("error", "error message");

			Assert.That(validation.Issues, Has.Count.EqualTo(3));
			Assert.That(validation.Issues[0].Code, Is.EqualTo("warning"));
			Assert.That(validation.Issues[1].Code, Is.EqualTo("required"));
			Assert.That(validation.Issues[2].Code, Is.EqualTo("error"));
			Assert.That(validation.HasErrors, Is.True);
		}
#else
		[Test]
		public void DisabledAssemblyStillExposesSchemaTypes()
		{
			Assert.That(DebugInputSchema.Boolean().ValueKind, Is.EqualTo(DebugValueKind.Boolean));
		}
#endif
	}
}
