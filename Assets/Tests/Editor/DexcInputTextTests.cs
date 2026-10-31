using System.Globalization;
using Dexc;
using NUnit.Framework;

namespace DexcDebugger.Tests
{
	public sealed class DexcInputTextTests
	{
		[TestCase("ja-JP")]
		[TestCase("en-US")]
		[TestCase("de-DE")]
		public void NumericGrammarPreservesKindsAndRoundTripsWithoutCulture(string culture)
		{
			var previous = CultureInfo.CurrentCulture;
			try
			{
				CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
				foreach (
					var value in new[]
					{
						DebugValue.From(long.MinValue),
						DebugValue.From(long.MaxValue),
						DebugValue.From(0UL),
						DebugValue.From((ulong)long.MaxValue + 1),
						DebugValue.From(ulong.MaxValue),
						DebugValue.From(2.5),
						DebugValue.From(-0.5),
						DebugValue.From(1e-30),
					}
				)
				{
					var text = DexcInputText.Format(value);
					Assert.That(text, Does.Not.Contain(","));
					Assert.That(DexcInputText.ParseScalar(value.Kind, text), Is.EqualTo(value));
				}
				Assert.That(DexcInputText.Format(DebugValue.From(2.5)), Is.EqualTo("2.5"));
				Assert.That(DexcInputText.ParseScalar(DebugValueKind.FloatingPoint, "1."), Is.EqualTo(DebugValue.From(1d)));
				Assert.That(DexcInputText.ParseScalar(DebugValueKind.FloatingPoint, "1e-3"), Is.EqualTo(DebugValue.From(0.001)));
				foreach (var text in new[] { "", "-", "1e-", "2,5", "1,000", "NaN", "Infinity", "1e309" })
				{
					Assert.That(DexcInputText.ParseScalar(DebugValueKind.FloatingPoint, text), Is.Null, text);
				}
				foreach (var text in new[] { "-1", "-0", "18446744073709551616", "1,000" })
				{
					Assert.That(DexcInputText.ParseScalar(DebugValueKind.UnsignedInteger, text), Is.Null, text);
				}
				foreach (var text in new[] { "9223372036854775808", "-9223372036854775809", "1,000" })
				{
					Assert.That(DexcInputText.ParseScalar(DebugValueKind.SignedInteger, text), Is.Null, text);
				}
				var schema = DebugInputSchema.Object(
					new DebugInputMemberSchema("x", DebugInputSchema.FloatingPoint()),
					new DebugInputMemberSchema("y", DebugInputSchema.FloatingPoint())
				);
				var vector = DebugValue.FromObject(
					new DebugValueMember("x", DebugValue.From(2.5)),
					new DebugValueMember("y", DebugValue.From(-0.5))
				);
				Assert.That(DexcInputText.Parse(schema, DexcInputText.Format(vector)), Is.EqualTo(vector));
				Assert.That(DexcInputText.Parse(schema, "2.5, -0.5"), Is.EqualTo(vector));
				Assert.That(DexcInputText.Parse(schema, "y=2.5, x=-0.5"), Is.Null);
				Assert.That(DexcInputText.Parse(schema, "x=2,5, y=-0,5"), Is.Null);
				Assert.That(DexcInputText.ParseScalar(DebugValueKind.String, "2,5"), Is.EqualTo(DebugValue.From("2,5")));
			}
			finally
			{
				CultureInfo.CurrentCulture = previous;
			}
		}
	}
}
