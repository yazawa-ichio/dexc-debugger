using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Dexc.Telemetry.CodeGen
{
	/// <summary>
	/// Reads JSON into dictionaries, lists, strings, doubles and booleans. The IL processing runs outside Unity, where
	/// <c>JsonUtility</c> does not exist, and only has to read the small profile files.
	/// </summary>
	internal sealed class MiniJson
	{
		readonly string m_Text;
		int m_Position;

		MiniJson(string text)
		{
			m_Text = text;
		}

		/// <exception cref="FormatException">The text is not JSON.</exception>
		internal static object Parse(string text)
		{
			var reader = new MiniJson(text);
			var value = reader.Value();
			reader.Space();
			if (reader.m_Position != text.Length)
			{
				throw reader.Error("text after the value");
			}
			return value;
		}

		/// <summary>Writes what <see cref="Parse"/> reads, indented by two spaces.</summary>
		internal static string Write(object value)
		{
			var builder = new StringBuilder();
			Write(builder, value, 0);
			return builder.Append('\n').ToString();
		}

		static void Write(StringBuilder builder, object value, int depth)
		{
			switch (value)
			{
				case null:
					builder.Append("null");
					break;
				case bool flag:
					builder.Append(flag ? "true" : "false");
					break;
				case double number:
					builder.Append(number.ToString("R", CultureInfo.InvariantCulture));
					break;
				case string text:
					WriteString(builder, text);
					break;
				case Dictionary<string, object> map:
					builder.Append('{');
					var first = true;
					foreach (var pair in map)
					{
						builder.Append(first ? "\n" : ",\n").Append(' ', (depth + 1) * 2);
						WriteString(builder, pair.Key);
						builder.Append(": ");
						Write(builder, pair.Value, depth + 1);
						first = false;
					}
					builder.Append(first ? "}" : "\n" + new string(' ', depth * 2) + "}");
					break;
				case List<object> list:
					builder.Append('[');
					for (var i = 0; i < list.Count; i++)
					{
						builder.Append(i == 0 ? "\n" : ",\n").Append(' ', (depth + 1) * 2);
						Write(builder, list[i], depth + 1);
					}
					builder.Append(list.Count == 0 ? "]" : "\n" + new string(' ', depth * 2) + "]");
					break;
				default:
					throw new ArgumentException("Not a JSON value: " + value.GetType(), nameof(value));
			}
		}

		static void WriteString(StringBuilder builder, string text)
		{
			builder.Append('"');
			foreach (var character in text)
			{
				switch (character)
				{
					case '"':
						builder.Append("\\\"");
						break;
					case '\\':
						builder.Append("\\\\");
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
						{
							builder.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
						}
						else
						{
							builder.Append(character);
						}
						break;
				}
			}
			builder.Append('"');
		}

		object Value()
		{
			Space();
			if (m_Position >= m_Text.Length)
			{
				throw Error("a value");
			}
			switch (m_Text[m_Position])
			{
				case '{':
					return Object();
				case '[':
					return Array();
				case '"':
					return String();
				case 't':
					return Word("true", true);
				case 'f':
					return Word("false", false);
				case 'n':
					return Word("null", null);
				default:
					return Number();
			}
		}

		Dictionary<string, object> Object()
		{
			var result = new Dictionary<string, object>(StringComparer.Ordinal);
			m_Position++;
			Space();
			if (Peek('}'))
			{
				m_Position++;
				return result;
			}
			while (true)
			{
				Space();
				var key = String();
				Space();
				Expect(':');
				result[key] = Value();
				Space();
				if (Peek(','))
				{
					m_Position++;
					continue;
				}
				Expect('}');
				return result;
			}
		}

		List<object> Array()
		{
			var result = new List<object>();
			m_Position++;
			Space();
			if (Peek(']'))
			{
				m_Position++;
				return result;
			}
			while (true)
			{
				result.Add(Value());
				Space();
				if (Peek(','))
				{
					m_Position++;
					continue;
				}
				Expect(']');
				return result;
			}
		}

		string String()
		{
			Expect('"');
			var builder = new StringBuilder();
			while (m_Position < m_Text.Length)
			{
				var character = m_Text[m_Position++];
				if (character == '"')
				{
					return builder.ToString();
				}
				if (character != '\\')
				{
					builder.Append(character);
					continue;
				}
				if (m_Position >= m_Text.Length)
				{
					break;
				}
				var escaped = m_Text[m_Position++];
				switch (escaped)
				{
					case 'n':
						builder.Append('\n');
						break;
					case 't':
						builder.Append('\t');
						break;
					case 'r':
						builder.Append('\r');
						break;
					case 'b':
						builder.Append('\b');
						break;
					case 'f':
						builder.Append('\f');
						break;
					case 'u':
						if (m_Position + 4 > m_Text.Length)
						{
							throw Error("four hex digits");
						}
						builder.Append((char)int.Parse(m_Text.Substring(m_Position, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
						m_Position += 4;
						break;
					default:
						builder.Append(escaped);
						break;
				}
			}
			throw Error("the end of a string");
		}

		object Number()
		{
			var start = m_Position;
			while (m_Position < m_Text.Length && "+-0123456789.eE".IndexOf(m_Text[m_Position]) >= 0)
			{
				m_Position++;
			}
			if (start == m_Position || !double.TryParse(m_Text.Substring(start, m_Position - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
			{
				throw Error("a value");
			}
			return number;
		}

		object Word(string word, object value)
		{
			if (string.CompareOrdinal(m_Text, m_Position, word, 0, word.Length) != 0)
			{
				throw Error(word);
			}
			m_Position += word.Length;
			return value;
		}

		void Space()
		{
			while (m_Position < m_Text.Length && char.IsWhiteSpace(m_Text[m_Position]))
			{
				m_Position++;
			}
		}

		bool Peek(char character)
		{
			return m_Position < m_Text.Length && m_Text[m_Position] == character;
		}

		void Expect(char character)
		{
			if (!Peek(character))
			{
				throw Error("'" + character + "'");
			}
			m_Position++;
		}

		FormatException Error(string expected)
		{
			return new FormatException("Expected " + expected + " at " + m_Position + ".");
		}
	}
}
