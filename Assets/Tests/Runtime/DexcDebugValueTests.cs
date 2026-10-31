using System;
using System.Collections.Generic;
using System.Linq;
using Dexc;
using NUnit.Framework;

namespace DexcDebugger.Tests
{
	public sealed class DexcDebugValueTests
	{
#if DEXC_DEBUGGER
		[Test]
		public void ScalarFactoriesUseExactKindsAndDoNotImplicitlyReadOtherKinds()
		{
			var signed = DebugValue.From(1);
			var unsigned = DebugValue.From(1U);
			var floating = DebugValue.From(1f);
			long signedValue;
			ulong unsignedValue;

			Assert.That(signed.Kind, Is.EqualTo(DebugValueKind.SignedInteger));
			Assert.That(unsigned.Kind, Is.EqualTo(DebugValueKind.UnsignedInteger));
			Assert.That(floating.Kind, Is.EqualTo(DebugValueKind.FloatingPoint));
			Assert.That(signed.TryGetSignedInteger(out signedValue), Is.True);
			Assert.That(signedValue, Is.EqualTo(1));
			Assert.That(signed.TryGetUnsignedInteger(out unsignedValue), Is.False);
			Assert.That(signed.TryGetFloatingPoint(out _), Is.False);
			Assert.That(floating.TryGetFloatingPoint(out var floatingValue), Is.True);
			Assert.That(floatingValue, Is.EqualTo(1d));
			Assert.That(floating.TryGetString(out _), Is.False);
		}

		[Test]
		public void NullAndNonFiniteValuesAreRejectedAtTheFactoryBoundary()
		{
			Assert.That(DebugValue.Null, Is.SameAs(DebugValue.Null));
			var empty = DebugValue.From(string.Empty);
			string emptyText;
			Assert.That(empty.Kind, Is.EqualTo(DebugValueKind.String));
			Assert.That(empty.TryGetString(out emptyText), Is.True);
			Assert.That(emptyText, Is.EqualTo(string.Empty));
			Assert.That(empty, Is.Not.EqualTo(DebugValue.Null));
			Assert.Throws<ArgumentNullException>(() => DebugValue.From((string)null));
			Assert.Throws<ArgumentOutOfRangeException>(() => DebugValue.From(double.NaN));
			Assert.Throws<ArgumentOutOfRangeException>(() => DebugValue.From(double.PositiveInfinity));
			Assert.Throws<ArgumentOutOfRangeException>(() => DebugValue.From(float.NegativeInfinity));
		}

		[Test]
		public void ArrayAndObjectFactoriesCopyCollectionsAndPreserveOrder()
		{
			var values = new List<DebugValue> { DebugValue.From(1), DebugValue.From(2) };
			var array = DebugValue.FromArray(values);
			values[0] = DebugValue.From(99);
			IReadOnlyList<DebugValue> arrayValues;
			array.TryGetArray(out arrayValues);

			var members = new List<DebugValueMember>
			{
				new DebugValueMember("first", DebugValue.From("a")),
				new DebugValueMember("second", DebugValue.From("b")),
			};
			var obj = DebugValue.FromObject(members);
			members.Clear();
			IReadOnlyList<DebugValueMember> objectMembers;
			obj.TryGetObject(out objectMembers);

			Assert.That(arrayValues.Select(value => value.TryGetSignedInteger(out _)).Count(result => result), Is.EqualTo(2));
			Assert.That(arrayValues[0], Is.EqualTo(DebugValue.From(1)));
			Assert.That(objectMembers.Select(member => member.Name), Is.EqualTo(new[] { "first", "second" }));
			Assert.Throws<ArgumentException>(() =>
				DebugValue.FromObject(new DebugValueMember("same", DebugValue.Null), new DebugValueMember("same", DebugValue.Null))
			);
		}

		[Test]
		public void EqualityIncludesKindOrderNamesAndNestedValues()
		{
			var left = DebugValue.FromObject(
				new DebugValueMember("a", DebugValue.FromArray(DebugValue.From(1), DebugValue.From(2))),
				new DebugValueMember("b", DebugValue.Null)
			);
			var equal = DebugValue.FromObject(
				new DebugValueMember("a", DebugValue.FromArray(DebugValue.From(1), DebugValue.From(2))),
				new DebugValueMember("b", DebugValue.Null)
			);
			var differentOrder = DebugValue.FromObject(
				new DebugValueMember("b", DebugValue.Null),
				new DebugValueMember("a", DebugValue.FromArray(DebugValue.From(1), DebugValue.From(2)))
			);

			Assert.That(left, Is.EqualTo(equal));
			Assert.That(left.GetHashCode(), Is.EqualTo(equal.GetHashCode()));
			Assert.That(left == equal, Is.True);
			Assert.That(left != differentOrder, Is.True);
			Assert.That(DebugValue.From(1), Is.Not.EqualTo(DebugValue.From(1U)));
		}

		[Test]
		public void TreeLimitsRejectDepthNodeCountAndArrayLengthWithoutTruncation()
		{
			var depthLimited = DebugValue.From(1);
			for (var index = 1; index < DebugValue.MaxDepth - 1; index++)
			{
				depthLimited = DebugValue.FromArray(depthLimited);
			}

			Assert.DoesNotThrow(() => DebugValue.FromArray(depthLimited));
			Assert.Throws<ArgumentException>(() => DebugValue.FromArray(DebugValue.FromArray(depthLimited)));

			var tooLong = Enumerable.Range(0, DebugValue.MaxArrayLength + 1).Select(DebugValue.From).ToArray();
			Assert.Throws<ArgumentException>(() => DebugValue.FromArray(tooLong));

			var wideArray = Enumerable.Range(0, DebugValue.MaxArrayLength - 1).Select(DebugValue.From).ToArray();
			Assert.Throws<ArgumentException>(() =>
				DebugValue.FromObject(
					new DebugValueMember("one", DebugValue.FromArray(wideArray)),
					new DebugValueMember("two", DebugValue.FromArray(wideArray)),
					new DebugValueMember("three", DebugValue.FromArray(wideArray)),
					new DebugValueMember("four", DebugValue.FromArray(wideArray))
				)
			);
		}
#else
		[Test]
		public void DisabledAssemblyStillExposesValueTypes()
		{
			Assert.That(DebugValue.Null.Kind, Is.EqualTo(DebugValueKind.Null));
		}
#endif
	}
}
