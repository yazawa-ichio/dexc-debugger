using System;
using System.Linq;
using Dexc;
using NUnit.Framework;

namespace DexcDebugger.Tests
{
	/// <summary>
	/// The protocol layer is a string in and a string out. These tests call it with no transport and no session, which is
	/// the point of keeping it in an assembly that cannot reach the engine.
	/// </summary>
	public sealed class DexcRemoteJsonTests
	{
		private static DexcJsonValue Member(DexcJsonValue value, string name)
		{
			Assert.That(value.TryGetMember(name, out var member), Is.True, "member \"" + name + "\" is present");
			return member;
		}

		[Test]
		public void EveryKindSurvivesAWriteAndRead()
		{
			var written = DexcJson.Write(
				DexcJsonValue.FromObject(
					new[]
					{
						new DexcJsonMember("nothing", DexcJsonValue.Null),
						new DexcJsonMember("yes", DexcJsonValue.From(true)),
						new DexcJsonMember("count", DexcJsonValue.From(-42L)),
						new DexcJsonMember("ratio", DexcJsonValue.From(1.5)),
						new DexcJsonMember("name", DexcJsonValue.From("dexc")),
						new DexcJsonMember("items", DexcJsonValue.FromArray(new[] { DexcJsonValue.From(1L), DexcJsonValue.From("two") })),
					}
				)
			);

			var read = DexcJson.Parse(written);

			Assert.That(read.Kind, Is.EqualTo(DexcJsonKind.Object));
			Assert.That(Member(read, "nothing").Kind, Is.EqualTo(DexcJsonKind.Null));
			Assert.That(Member(read, "yes").TryGetBoolean(out var flag), Is.True);
			Assert.That(flag, Is.True);
			Assert.That(Member(read, "count").TryGetInt64(out var number), Is.True);
			Assert.That(number, Is.EqualTo(-42L));
			Assert.That(Member(read, "ratio").TryGetDouble(out var real), Is.True);
			Assert.That(real, Is.EqualTo(1.5));
			Assert.That(Member(read, "name").TryGetString(out var text), Is.True);
			Assert.That(text, Is.EqualTo("dexc"));
			Assert.That(Member(read, "items").Items, Has.Count.EqualTo(2));
		}

		/// <summary>
		/// A value larger than a double can hold exactly is allowed on the wire. Keeping the text it arrived as is what
		/// lets it come back the same.
		/// </summary>
		[Test]
		public void ALargeIntegerKeepsEveryDigit()
		{
			const string frame = "{\"big\":9223372036854775807,\"bigger\":18446744073709551615}";

			var read = DexcJson.Parse(frame);

			Assert.That(Member(read, "big").TryGetInt64(out var signed), Is.True);
			Assert.That(signed, Is.EqualTo(long.MaxValue));
			Assert.That(Member(read, "bigger").TryGetUInt64(out var unsigned), Is.True);
			Assert.That(unsigned, Is.EqualTo(ulong.MaxValue));
			Assert.That(DexcJson.Write(read), Is.EqualTo(frame), "the digits come back as they arrived");
		}

		[Test]
		public void EscapesAndControlCharactersSurvive()
		{
			var original = "quote\" backslash\\ newline\n tab\t bell" + (char)7 + " 日本語";
			var value = DexcJsonValue.From(original);

			var written = DexcJson.Write(value);
			var read = DexcJson.Parse(written);

			Assert.That(read.TryGetString(out var text), Is.True);
			Assert.That(text, Is.EqualTo(original));
			Assert.That(written, Does.Contain("\\u0007"), "a control character is escaped, not written raw");
			Assert.That(written, Does.Contain("日本語"), "text outside ASCII is written as it is");
		}

		[Test]
		public void AnEscapedUnicodeSequenceIsRead()
		{
			var read = DexcJson.Parse("\"\\u3042\\u3044\"");

			Assert.That(read.TryGetString(out var text), Is.True);
			Assert.That(text, Is.EqualTo("あい"));
		}

		[TestCase("{", TestName = "unclosed object")]
		[TestCase("[1,]", TestName = "trailing comma")]
		[TestCase("{\"a\":1}{\"b\":2}", TestName = "two documents")]
		[TestCase("{a:1}", TestName = "unquoted name")]
		[TestCase("\"unterminated", TestName = "unclosed string")]
		[TestCase("{\"a\":1,\"a\":2}", TestName = "repeated name")]
		[TestCase("", TestName = "empty frame")]
		[TestCase("1e400", TestName = "number beyond double")]
		public void MalformedInputIsRefusedWithoutThrowing(string frame)
		{
			Assert.That(DexcJson.TryParse(frame, out _, out var error), Is.False);
			Assert.That(error, Is.Not.Null.And.Not.Empty);
		}

		/// <summary>A frame written to exhaust the stack is refused by the reader, not by falling over.</summary>
		[Test]
		public void DeepNestingIsRefused()
		{
			var frame = new string('[', DexcJson.MaxDepth + 8) + new string(']', DexcJson.MaxDepth + 8);

			Assert.That(DexcJson.TryParse(frame, out _, out var error), Is.False);
			Assert.That(error, Does.Contain("nests deeper"));
		}

		[Test]
		public void ANonFiniteNumberIsRefusedAtTheSource()
		{
			Assert.Throws<ArgumentOutOfRangeException>(() => DexcJsonValue.From(double.NaN));
			Assert.Throws<ArgumentOutOfRangeException>(() => DexcJsonValue.From(double.PositiveInfinity));
		}
	}

	public sealed class DexcRemoteMessageTests
	{
		private static DexcJsonValue Member(DexcJsonValue value, string name)
		{
			Assert.That(value.TryGetMember(name, out var member), Is.True, "member \"" + name + "\" is present");
			return member;
		}

		private static string Reason(string frame)
		{
			var read = DexcJson.Parse(frame);
			Assert.That(Member(Member(read, "error"), "data").TryGetMember("reason", out var reason), Is.True);
			Assert.That(reason.TryGetString(out var text), Is.True);
			return text;
		}

		[Test]
		public void AWellFormedRequestIsRead()
		{
			const string frame = "{\"jsonrpc\":\"2.0\",\"id\":7,\"method\":\"command.list\",\"params\":{\"pathPrefix\":\"App\"}}";

			Assert.That(DexcRemoteMessage.TryReadRequest(frame, out var request, out var errorFrame), Is.True);

			Assert.That(errorFrame, Is.Null);
			Assert.That(request.Method, Is.EqualTo("command.list"));
			Assert.That(request.Id.TryGetInt64(out var id), Is.True);
			Assert.That(id, Is.EqualTo(7L));
			Assert.That(Member(request.Parameters, "pathPrefix").TryGetString(out var text), Is.True);
			Assert.That(text, Is.EqualTo("App"));
		}

		[Test]
		public void ARequestWithoutParamsIsRead()
		{
			Assert.That(
				DexcRemoteMessage.TryReadRequest("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"hello\"}", out var request, out _),
				Is.True
			);

			Assert.That(request.Method, Is.EqualTo("hello"));
			Assert.That(request.Parameters.Kind, Is.EqualTo(DexcJsonKind.Null));
		}

		[Test]
		public void TheReplyCarriesBackTheIdItWasGiven()
		{
			var written = DexcRemoteMessage.WriteResult(
				DexcJsonValue.From("abc"),
				DexcJsonValue.FromObject(new[] { new DexcJsonMember("status", DexcJsonValue.From("succeeded")) })
			);

			var read = DexcJson.Parse(written);

			Assert.That(Member(read, "jsonrpc").TryGetString(out var version), Is.True);
			Assert.That(version, Is.EqualTo("2.0"));
			Assert.That(Member(read, "id").TryGetString(out var idText), Is.True);
			Assert.That(idText, Is.EqualTo("abc"));
			Assert.That(Member(read, "result").TryGetMember("status", out _), Is.True);
			Assert.That(read.TryGetMember("error", out _), Is.False, "a reply carries a result or an error, never both");
		}

		/// <summary>
		/// The reason is what tells the caller what went wrong. The numeric code stays the small reserved set, so a new
		/// failure needs a new string and no new number.
		/// </summary>
		[Test]
		public void AnErrorCarriesAReasonAlongsideTheReservedCode()
		{
			var written = DexcRemoteMessage.WriteError(
				DexcJsonValue.From(1L),
				DexcRemoteErrorCode.ServerError,
				"The page is larger than this session allows.",
				DexcRemoteReason.PageSizeExceeded
			);

			var read = DexcJson.Parse(written);

			Assert.That(Member(Member(read, "error"), "code").TryGetInt64(out var code), Is.True);
			Assert.That(code, Is.EqualTo(DexcRemoteErrorCode.ServerError));
			Assert.That(Reason(written), Is.EqualTo("page-size-exceeded"));
			Assert.That(read.TryGetMember("result", out _), Is.False, "a reply carries a result or an error, never both");
		}

		[TestCase("not json", DexcRemoteReason.MalformedFrame, TestName = "not json")]
		[TestCase("[1,2]", DexcRemoteReason.InvalidRequest, TestName = "not an object")]
		[TestCase("{\"id\":1,\"method\":\"a\"}", DexcRemoteReason.InvalidRequest, TestName = "no version")]
		[TestCase("{\"jsonrpc\":\"1.0\",\"id\":1,\"method\":\"a\"}", DexcRemoteReason.InvalidRequest, TestName = "wrong version")]
		[TestCase("{\"jsonrpc\":\"2.0\",\"id\":1}", DexcRemoteReason.InvalidRequest, TestName = "no method")]
		[TestCase("{\"jsonrpc\":\"2.0\",\"method\":\"a\"}", DexcRemoteReason.InvalidRequest, TestName = "no id")]
		[TestCase("{\"jsonrpc\":\"2.0\",\"id\":null,\"method\":\"a\"}", DexcRemoteReason.InvalidRequest, TestName = "null id")]
		[TestCase(
			"{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"a\",\"params\":[]}",
			DexcRemoteReason.InvalidParams,
			TestName = "array params"
		)]
		public void ARefusedRequestStillProducesAnAnswer(string frame, string reason)
		{
			Assert.That(DexcRemoteMessage.TryReadRequest(frame, out var request, out var errorFrame), Is.False);

			Assert.That(request, Is.Null);
			Assert.That(errorFrame, Is.Not.Null.And.Not.Empty, "a refused frame is still answered");
			Assert.That(Reason(errorFrame), Is.EqualTo(reason));
		}

		[Test]
		public void AnUnknownMethodIsAnswered()
		{
			var written = DexcRemoteMessage.WriteUnknownMethod(DexcJsonValue.From(3L), "command.nope");
			var read = DexcJson.Parse(written);

			Assert.That(Member(Member(read, "error"), "code").TryGetInt64(out var code), Is.True);
			Assert.That(code, Is.EqualTo(DexcRemoteErrorCode.MethodNotFound));
			Assert.That(Member(Member(read, "error"), "message").TryGetString(out var text), Is.True);
			Assert.That(text, Does.Contain("command.nope"));
			Assert.That(Reason(written), Is.EqualTo("unknown-method"));
		}

		/// <summary>
		/// The protocol assembly must not be able to reach the engine. The asmdef says so; this says so where a failure is
		/// visible as a test rather than as a broken player build much later.
		/// </summary>
		[Test]
		public void TheProtocolAssemblyStandsOnItsOwn()
		{
			var assembly = typeof(DexcJson).Assembly;

			Assert.That(assembly.GetName().Name, Is.EqualTo("Dexc.Remote.Protocol"));
			var referenced = assembly.GetReferencedAssemblies().Select(item => item.Name).ToArray();
			Assert.That(referenced, Has.None.StartsWith("UnityEngine"), "the protocol does not reach the engine");
			Assert.That(referenced, Has.None.StartsWith("UnityEditor"), "the protocol does not reach the Editor");
			Assert.That(referenced, Has.None.StartsWith("Dexc."), "the protocol does not reach the features");
		}
	}
}
