using System.Collections.Generic;
using System.Globalization;

namespace Dexc
{
	/// <summary>
	/// Carries a <see cref="DebugValue"/> out of the process and back.
	///
	/// Out, a value becomes plain objects - dictionaries, lists, primitives - and whoever is carrying the answer
	/// serializes them. That keeps Dexc out of the business of writing JSON for a caller that already has a serializer,
	/// and works the same whichever one it is.
	///
	/// The value itself holds no type tag: what a value is supposed to be is the descriptor's job, so a reader that
	/// already knows the schema needs nothing more.
	/// </summary>
	internal static class DexcRemoteValue
	{
		/// <summary>
		/// An unsigned value past what a double holds exactly is written as text. Serializers put JSON numbers through a
		/// double, so the digits would come back different, and a value that changes on the way is worse than one the
		/// reader has to convert.
		/// </summary>
		private const ulong LargestExactInteger = 9007199254740992UL;

		internal static object ToObject(DebugValue value)
		{
			switch (value.Kind)
			{
				case DebugValueKind.Boolean:
					value.TryGetBoolean(out var boolean);
					return boolean;
				case DebugValueKind.SignedInteger:
					value.TryGetSignedInteger(out var signed);
					return signed;
				case DebugValueKind.UnsignedInteger:
					value.TryGetUnsignedInteger(out var unsigned);
					return unsigned > LargestExactInteger ? (object)unsigned.ToString(CultureInfo.InvariantCulture) : unsigned;
				case DebugValueKind.FloatingPoint:
					value.TryGetFloatingPoint(out var floating);
					return floating;
				case DebugValueKind.String:
					value.TryGetString(out var text);
					return text;
				case DebugValueKind.Array:
					value.TryGetArray(out var items);
					var list = new List<object>();
					foreach (var item in items)
						list.Add(ToObject(item));
					return list;
				case DebugValueKind.Object:
					value.TryGetObject(out var members);
					var map = new Dictionary<string, object>();
					foreach (var member in members)
						map[member.Name] = ToObject(member.Value);
					return map;
				default:
					return null;
			}
		}
	}

	/// <summary>
	/// Reads a plain object back into a <see cref="DebugValue"/>. Inputs arrive as plain objects for the same reason
	/// answers leave as plain objects: the carrier's reader produced them, and the operations never see the wire.
	/// </summary>
	internal static class DexcRemoteValueReader
	{
		/// <summary>
		/// Reads <paramref name="value"/> as the <paramref name="schema"/> expects, when there is one. Values carry no type tag,
		/// so a whole number arrives as an integer even for a floating point input: <c>2.0</c> is <c>2</c> once JSON has
		/// written it. The schema is what says it is a floating point number.
		/// </summary>
		internal static bool TryFromObject(object value, out DebugValue read, out string error, DebugInputSchema schema = null)
		{
			read = null;
			error = null;
			if (schema != null && schema.ValueKind == DebugValueKind.FloatingPoint)
			{
				switch (value)
				{
					case long signed:
						read = DebugValue.From((double)signed);
						return true;
					case int signed32:
						read = DebugValue.From((double)signed32);
						return true;
					case ulong unsigned:
						read = DebugValue.From((double)unsigned);
						return true;
				}
			}
			switch (value)
			{
				case null:
					read = DebugValue.Null;
					return true;
				case bool boolean:
					read = DebugValue.From(boolean);
					return true;
				case string text:
					read = DebugValue.From(text);
					return true;
				case long signed:
					read = DebugValue.From(signed);
					return true;
				case int signed32:
					read = DebugValue.From((long)signed32);
					return true;
				case ulong unsigned:
					read = DebugValue.From(unsigned);
					return true;
				case double floating:
					read = DebugValue.From(floating);
					return true;
				case float single:
					read = DebugValue.From((double)single);
					return true;
				case IDictionary<string, object> map:
					var members = new List<DebugValueMember>();
					foreach (var pair in map)
					{
						if (!TryFromObject(pair.Value, out var member, out error, MemberSchema(schema, pair.Key)))
							return false;
						members.Add(new DebugValueMember(pair.Key, member));
					}
					read = DebugValue.FromObject(members);
					return true;
				case IEnumerable<object> items:
					var list = new List<DebugValue>();
					foreach (var item in items)
					{
						if (!TryFromObject(item, out var element, out error, schema?.Element))
							return false;
						list.Add(element);
					}
					read = DebugValue.FromArray(list);
					return true;
				default:
					error = "A value of type " + value.GetType().Name + " cannot be carried.";
					return false;
			}
		}

		private static DebugInputSchema MemberSchema(DebugInputSchema schema, string name)
		{
			if (schema?.Members == null)
				return null;
			foreach (var member in schema.Members)
				if (member.Name == name)
					return member.Schema;
			return null;
		}
	}

	/// <summary>
	/// Renders what an operation answered into the wire value this package writes itself. Only a transport that has no
	/// serializer of its own needs this; one that brings its own is handed the plain objects.
	/// </summary>
	internal static class DexcRemoteJsonWriter
	{
		/// <summary>Reads a wire value into the plain objects the operations speak.</summary>
		internal static object ToObject(DexcJsonValue json)
		{
			switch (json.Kind)
			{
				case DexcJsonKind.Boolean:
					json.TryGetBoolean(out var boolean);
					return boolean;
				case DexcJsonKind.String:
					json.TryGetString(out var text);
					return text;
				case DexcJsonKind.Number:
					if (json.TryGetInt64(out var signed))
						return signed;
					if (json.TryGetUInt64(out var unsigned))
						return unsigned;
					json.TryGetDouble(out var floating);
					return floating;
				case DexcJsonKind.Array:
					var items = new List<object>();
					foreach (var item in json.Items)
						items.Add(ToObject(item));
					return items;
				case DexcJsonKind.Object:
					var map = new Dictionary<string, object>();
					foreach (var member in json.Members)
						map[member.Name] = ToObject(member.Value);
					return map;
				default:
					return null;
			}
		}

		internal static DexcJsonValue From(object value)
		{
			switch (value)
			{
				case null:
					return DexcJsonValue.Null;
				case bool boolean:
					return DexcJsonValue.From(boolean);
				case string text:
					return DexcJsonValue.From(text);
				case long signed:
					return DexcJsonValue.From(signed);
				case int signed32:
					return DexcJsonValue.From((long)signed32);
				case ulong unsigned:
					return DexcJsonValue.From(unsigned);
				case double floating:
					return DexcJsonValue.From(floating);
				case float single:
					return DexcJsonValue.From((double)single);
				case IReadOnlyDictionary<string, object> map:
					var members = new List<DexcJsonMember>();
					foreach (var pair in map)
						members.Add(new DexcJsonMember(pair.Key, From(pair.Value)));
					return DexcJsonValue.FromObject(members);
				case IEnumerable<object> items:
					var list = new List<DexcJsonValue>();
					foreach (var item in items)
						list.Add(From(item));
					return DexcJsonValue.FromArray(list);
				default:
					return DexcJsonValue.From(value.ToString());
			}
		}
	}
}
