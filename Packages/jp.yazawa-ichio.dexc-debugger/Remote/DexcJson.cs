using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;

namespace Dexc
{
	internal enum DexcJsonKind
	{
		Null,
		Boolean,
		Number,
		String,
		Array,
		Object,
	}

	/// <summary>
	/// The wire value. JsonUtility cannot carry the protocol's shapes and this assembly has no engine to ask, so the
	/// reader and writer are here. Numbers keep the text they arrived as: a double round trip would quietly lose the
	/// large integers the protocol is allowed to carry.
	/// </summary>
	internal sealed class DexcJsonValue
	{
		private static readonly IReadOnlyList<DexcJsonValue> EmptyItems = new ReadOnlyCollection<DexcJsonValue>(new List<DexcJsonValue>());

		private readonly bool m_Boolean;
		private readonly string m_Text;
		private readonly IReadOnlyList<DexcJsonValue> m_Items;
		private readonly IReadOnlyList<DexcJsonMember> m_Members;

		private DexcJsonValue(
			DexcJsonKind kind,
			bool boolean,
			string text,
			IReadOnlyList<DexcJsonValue> items,
			IReadOnlyList<DexcJsonMember> members
		)
		{
			Kind = kind;
			m_Boolean = boolean;
			m_Text = text;
			m_Items = items;
			m_Members = members;
		}

		internal DexcJsonKind Kind { get; }

		internal static DexcJsonValue Null { get; } = new DexcJsonValue(DexcJsonKind.Null, false, null, null, null);

		internal static DexcJsonValue True { get; } = new DexcJsonValue(DexcJsonKind.Boolean, true, null, null, null);

		internal static DexcJsonValue False { get; } = new DexcJsonValue(DexcJsonKind.Boolean, false, null, null, null);

		internal static DexcJsonValue From(bool value) => value ? True : False;

		internal static DexcJsonValue From(long value) =>
			new DexcJsonValue(DexcJsonKind.Number, false, value.ToString(CultureInfo.InvariantCulture), null, null);

		internal static DexcJsonValue From(ulong value) =>
			new DexcJsonValue(DexcJsonKind.Number, false, value.ToString(CultureInfo.InvariantCulture), null, null);

		/// <summary>Refuses what JSON has no notation for, rather than writing something no reader agrees on.</summary>
		internal static DexcJsonValue From(double value)
		{
			if (double.IsNaN(value) || double.IsInfinity(value))
				throw new ArgumentOutOfRangeException(nameof(value), "JSON has no notation for a non-finite number.");
			return new DexcJsonValue(DexcJsonKind.Number, false, value.ToString("R", CultureInfo.InvariantCulture), null, null);
		}

		internal static DexcJsonValue From(string value) =>
			value == null ? Null : new DexcJsonValue(DexcJsonKind.String, false, value, null, null);

		internal static DexcJsonValue FromArray(IEnumerable<DexcJsonValue> items)
		{
			var list = new List<DexcJsonValue>(items);
			return new DexcJsonValue(DexcJsonKind.Array, false, null, new ReadOnlyCollection<DexcJsonValue>(list), null);
		}

		internal static DexcJsonValue FromObject(IEnumerable<DexcJsonMember> members)
		{
			var list = new List<DexcJsonMember>(members);
			return new DexcJsonValue(DexcJsonKind.Object, false, null, null, new ReadOnlyCollection<DexcJsonMember>(list));
		}

		internal IReadOnlyList<DexcJsonValue> Items => m_Items ?? EmptyItems;

		internal IReadOnlyList<DexcJsonMember> Members => m_Members ?? Array.Empty<DexcJsonMember>();

		internal bool TryGetBoolean(out bool value)
		{
			value = m_Boolean;
			return Kind == DexcJsonKind.Boolean;
		}

		internal bool TryGetString(out string value)
		{
			value = Kind == DexcJsonKind.String ? m_Text : null;
			return Kind == DexcJsonKind.String;
		}

		internal bool TryGetInt64(out long value)
		{
			value = 0;
			return Kind == DexcJsonKind.Number && long.TryParse(m_Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
		}

		internal bool TryGetUInt64(out ulong value)
		{
			value = 0;
			return Kind == DexcJsonKind.Number && ulong.TryParse(m_Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
		}

		internal bool TryGetDouble(out double value)
		{
			value = 0;
			return Kind == DexcJsonKind.Number && double.TryParse(m_Text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
		}

		/// <summary>The number exactly as it was written, for a caller that needs the digits rather than a type.</summary>
		internal bool TryGetNumberText(out string value)
		{
			value = Kind == DexcJsonKind.Number ? m_Text : null;
			return Kind == DexcJsonKind.Number;
		}

		internal bool TryGetMember(string name, out DexcJsonValue value)
		{
			value = null;
			if (Kind != DexcJsonKind.Object)
				return false;
			foreach (var member in Members)
			{
				if (!string.Equals(member.Name, name, StringComparison.Ordinal))
					continue;
				value = member.Value;
				return true;
			}
			return false;
		}

		public override string ToString() => DexcJson.Write(this);
	}

	internal readonly struct DexcJsonMember
	{
		internal DexcJsonMember(string name, DexcJsonValue value)
		{
			Name = name ?? throw new ArgumentNullException(nameof(name));
			Value = value ?? DexcJsonValue.Null;
		}

		internal string Name { get; }
		internal DexcJsonValue Value { get; }
	}

	internal sealed class DexcJsonException : Exception
	{
		internal DexcJsonException(string message, int position)
			: base(message + " (at " + position.ToString(CultureInfo.InvariantCulture) + ")")
		{
			Position = position;
		}

		internal int Position { get; }
	}

	internal static class DexcJson
	{
		/// <summary>
		/// How deep a document may nest. This is a guard against a frame written to exhaust the stack, not a protocol
		/// limit: what the protocol allows a value to be is decided where DebugValue is mapped, and is far smaller.
		/// </summary>
		internal const int MaxDepth = 64;

		internal static DexcJsonValue Parse(string text)
		{
			if (text == null)
				throw new ArgumentNullException(nameof(text));
			var reader = new Reader(text);
			reader.SkipWhitespace();
			var value = reader.ReadValue(0);
			reader.SkipWhitespace();
			if (!reader.AtEnd)
				throw new DexcJsonException("Trailing content after the JSON value.", reader.Position);
			return value;
		}

		internal static bool TryParse(string text, out DexcJsonValue value, out string error)
		{
			try
			{
				value = Parse(text);
				error = null;
				return true;
			}
			catch (Exception exception) when (exception is DexcJsonException || exception is ArgumentNullException)
			{
				value = null;
				error = exception.Message;
				return false;
			}
		}

		internal static string Write(DexcJsonValue value)
		{
			var builder = new StringBuilder();
			WriteValue(builder, value ?? DexcJsonValue.Null);
			return builder.ToString();
		}

		private static void WriteValue(StringBuilder builder, DexcJsonValue value)
		{
			switch (value.Kind)
			{
				case DexcJsonKind.Null:
					builder.Append("null");
					break;
				case DexcJsonKind.Boolean:
					value.TryGetBoolean(out var boolean);
					builder.Append(boolean ? "true" : "false");
					break;
				case DexcJsonKind.Number:
					value.TryGetNumberText(out var number);
					builder.Append(number);
					break;
				case DexcJsonKind.String:
					value.TryGetString(out var text);
					WriteString(builder, text);
					break;
				case DexcJsonKind.Array:
					builder.Append('[');
					for (var index = 0; index < value.Items.Count; index++)
					{
						if (index > 0)
							builder.Append(',');
						WriteValue(builder, value.Items[index]);
					}
					builder.Append(']');
					break;
				default:
					builder.Append('{');
					for (var index = 0; index < value.Members.Count; index++)
					{
						if (index > 0)
							builder.Append(',');
						WriteString(builder, value.Members[index].Name);
						builder.Append(':');
						WriteValue(builder, value.Members[index].Value);
					}
					builder.Append('}');
					break;
			}
		}

		private static void WriteString(StringBuilder builder, string text)
		{
			builder.Append('"');
			foreach (var character in text ?? string.Empty)
			{
				switch (character)
				{
					case '"':
						builder.Append("\\\"");
						break;
					case '\\':
						builder.Append("\\\\");
						break;
					case '\b':
						builder.Append("\\b");
						break;
					case '\f':
						builder.Append("\\f");
						break;
					case '\n':
						builder.Append("\\n");
						break;
					case '\r':
						builder.Append("\\r");
						break;
					case '\t':
						builder.Append("\\t");
						break;
					default:
						if (character < ' ')
							builder.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
						else
							builder.Append(character);
						break;
				}
			}
			builder.Append('"');
		}

		private struct Reader
		{
			private readonly string m_Text;
			private int m_Position;

			internal Reader(string text)
			{
				m_Text = text;
				m_Position = 0;
			}

			internal int Position => m_Position;

			internal bool AtEnd => m_Position >= m_Text.Length;

			internal void SkipWhitespace()
			{
				while (m_Position < m_Text.Length)
				{
					var character = m_Text[m_Position];
					if (character != ' ' && character != '\t' && character != '\n' && character != '\r')
						return;
					m_Position++;
				}
			}

			internal DexcJsonValue ReadValue(int depth)
			{
				if (depth > MaxDepth)
					throw new DexcJsonException("The JSON value nests deeper than the reader accepts.", m_Position);
				SkipWhitespace();
				if (AtEnd)
					throw new DexcJsonException("The JSON value is missing.", m_Position);
				var character = m_Text[m_Position];
				switch (character)
				{
					case '{':
						return ReadObject(depth);
					case '[':
						return ReadArray(depth);
					case '"':
						return DexcJsonValue.From(ReadString());
					case 't':
						ReadLiteral("true");
						return DexcJsonValue.True;
					case 'f':
						ReadLiteral("false");
						return DexcJsonValue.False;
					case 'n':
						ReadLiteral("null");
						return DexcJsonValue.Null;
					default:
						return ReadNumber();
				}
			}

			private DexcJsonValue ReadObject(int depth)
			{
				m_Position++;
				var members = new List<DexcJsonMember>();
				var seen = new HashSet<string>(StringComparer.Ordinal);
				SkipWhitespace();
				if (!AtEnd && m_Text[m_Position] == '}')
				{
					m_Position++;
					return DexcJsonValue.FromObject(members);
				}
				while (true)
				{
					SkipWhitespace();
					if (AtEnd || m_Text[m_Position] != '"')
						throw new DexcJsonException("A JSON object member needs a name.", m_Position);
					var name = ReadString();
					if (!seen.Add(name))
						throw new DexcJsonException("A JSON object member name is repeated: " + name, m_Position);
					SkipWhitespace();
					if (AtEnd || m_Text[m_Position] != ':')
						throw new DexcJsonException("A JSON object member needs a colon.", m_Position);
					m_Position++;
					members.Add(new DexcJsonMember(name, ReadValue(depth + 1)));
					SkipWhitespace();
					if (AtEnd)
						throw new DexcJsonException("The JSON object is not closed.", m_Position);
					if (m_Text[m_Position] == ',')
					{
						m_Position++;
						continue;
					}
					if (m_Text[m_Position] == '}')
					{
						m_Position++;
						return DexcJsonValue.FromObject(members);
					}
					throw new DexcJsonException("The JSON object needs a comma or a closing brace.", m_Position);
				}
			}

			private DexcJsonValue ReadArray(int depth)
			{
				m_Position++;
				var items = new List<DexcJsonValue>();
				SkipWhitespace();
				if (!AtEnd && m_Text[m_Position] == ']')
				{
					m_Position++;
					return DexcJsonValue.FromArray(items);
				}
				while (true)
				{
					items.Add(ReadValue(depth + 1));
					SkipWhitespace();
					if (AtEnd)
						throw new DexcJsonException("The JSON array is not closed.", m_Position);
					if (m_Text[m_Position] == ',')
					{
						m_Position++;
						continue;
					}
					if (m_Text[m_Position] == ']')
					{
						m_Position++;
						return DexcJsonValue.FromArray(items);
					}
					throw new DexcJsonException("The JSON array needs a comma or a closing bracket.", m_Position);
				}
			}

			private void ReadLiteral(string literal)
			{
				if (
					m_Position + literal.Length > m_Text.Length
					|| string.CompareOrdinal(m_Text, m_Position, literal, 0, literal.Length) != 0
				)
				{
					throw new DexcJsonException("Expected " + literal + ".", m_Position);
				}
				m_Position += literal.Length;
			}

			private DexcJsonValue ReadNumber()
			{
				var start = m_Position;
				if (!AtEnd && m_Text[m_Position] == '-')
					m_Position++;
				var digits = 0;
				while (!AtEnd && m_Text[m_Position] >= '0' && m_Text[m_Position] <= '9')
				{
					m_Position++;
					digits++;
				}
				if (digits == 0)
					throw new DexcJsonException("The JSON number has no digits.", start);
				if (!AtEnd && m_Text[m_Position] == '.')
				{
					m_Position++;
					var fraction = 0;
					while (!AtEnd && m_Text[m_Position] >= '0' && m_Text[m_Position] <= '9')
					{
						m_Position++;
						fraction++;
					}
					if (fraction == 0)
						throw new DexcJsonException("The JSON number has no digits after the point.", m_Position);
				}
				if (!AtEnd && (m_Text[m_Position] == 'e' || m_Text[m_Position] == 'E'))
				{
					m_Position++;
					if (!AtEnd && (m_Text[m_Position] == '+' || m_Text[m_Position] == '-'))
						m_Position++;
					var exponent = 0;
					while (!AtEnd && m_Text[m_Position] >= '0' && m_Text[m_Position] <= '9')
					{
						m_Position++;
						exponent++;
					}
					if (exponent == 0)
						throw new DexcJsonException("The JSON number has no digits in the exponent.", m_Position);
				}
				var text = m_Text.Substring(start, m_Position - start);
				return new NumberFactory(text).Value;
			}

			private string ReadString()
			{
				m_Position++;
				var builder = new StringBuilder();
				while (true)
				{
					if (AtEnd)
						throw new DexcJsonException("The JSON string is not closed.", m_Position);
					var character = m_Text[m_Position++];
					if (character == '"')
						return builder.ToString();
					if (character != '\\')
					{
						if (character < ' ')
							throw new DexcJsonException("The JSON string holds an unescaped control character.", m_Position - 1);
						builder.Append(character);
						continue;
					}
					if (AtEnd)
						throw new DexcJsonException("The JSON string ends inside an escape.", m_Position);
					var escape = m_Text[m_Position++];
					switch (escape)
					{
						case '"':
							builder.Append('"');
							break;
						case '\\':
							builder.Append('\\');
							break;
						case '/':
							builder.Append('/');
							break;
						case 'b':
							builder.Append('\b');
							break;
						case 'f':
							builder.Append('\f');
							break;
						case 'n':
							builder.Append('\n');
							break;
						case 'r':
							builder.Append('\r');
							break;
						case 't':
							builder.Append('\t');
							break;
						case 'u':
							builder.Append(ReadUnicode());
							break;
						default:
							throw new DexcJsonException("The JSON string holds an unknown escape.", m_Position - 1);
					}
				}
			}

			private char ReadUnicode()
			{
				if (m_Position + 4 > m_Text.Length)
					throw new DexcJsonException("The JSON escape needs four hex digits.", m_Position);
				var code = 0;
				for (var index = 0; index < 4; index++)
				{
					var character = m_Text[m_Position + index];
					var digit =
						character >= '0' && character <= '9' ? character - '0'
						: character >= 'a' && character <= 'f' ? character - 'a' + 10
						: character >= 'A' && character <= 'F' ? character - 'A' + 10
						: -1;
					if (digit < 0)
						throw new DexcJsonException("The JSON escape needs four hex digits.", m_Position + index);
					code = code * 16 + digit;
				}
				m_Position += 4;
				return (char)code;
			}
		}

		/// <summary>Builds a number value from the text it was written as, without going through a floating point type.</summary>
		private readonly struct NumberFactory
		{
			private readonly string m_Text;

			internal NumberFactory(string text)
			{
				m_Text = text;
			}

			internal DexcJsonValue Value
			{
				get
				{
					if (long.TryParse(m_Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var signed))
						return DexcJsonValue.From(signed);
					if (ulong.TryParse(m_Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unsigned))
						return DexcJsonValue.From(unsigned);
					if (!double.TryParse(m_Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var floating))
						throw new DexcJsonException("The JSON number cannot be read.", 0);
					// A grammar-valid number such as 1e400 overflows to infinity, which the value type refuses.
					if (double.IsInfinity(floating))
						throw new DexcJsonException("The JSON number is out of range.", 0);
					return DexcJsonValue.From(floating);
				}
			}
		}
	}
}
