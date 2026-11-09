using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Dexc.QA
{
	/// <summary>
	/// Writes reports as JSON: null, bools, strings, numbers, ordered members (a list of key-value pairs) and lists. Only
	/// writes: the tools read the reports, the Player never does.
	/// </summary>
	internal static class QAJson
	{
		internal static string Write(object value)
		{
			var builder = new StringBuilder();
			Write(builder, value, 0);
			return builder.ToString();
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
				case string text:
					WriteString(builder, text);
					break;
				case double number when double.IsNaN(number) || double.IsInfinity(number):
					builder.Append("null");
					break;
				case float number when float.IsNaN(number) || float.IsInfinity(number):
					builder.Append("null");
					break;
				case IFormattable number:
					builder.Append(number.ToString(null, CultureInfo.InvariantCulture));
					break;
				case IEnumerable<KeyValuePair<string, object>> members:
					builder.Append('{');
					var first = true;
					foreach (var member in members)
					{
						Separate(builder, ref first, depth + 1);
						WriteString(builder, member.Key);
						builder.Append(": ");
						Write(builder, member.Value, depth + 1);
					}
					Close(builder, first, depth, '}');
					break;
				case IEnumerable items:
					builder.Append('[');
					var firstItem = true;
					foreach (var item in items)
					{
						Separate(builder, ref firstItem, depth + 1);
						Write(builder, item, depth + 1);
					}
					Close(builder, firstItem, depth, ']');
					break;
				default:
					WriteString(builder, value.ToString());
					break;
			}
		}

		/// <summary>One member a line, so that a report reads well and diffs well.</summary>
		static void Separate(StringBuilder builder, ref bool first, int depth)
		{
			if (!first)
			{
				builder.Append(',');
			}
			first = false;
			builder.Append('\n').Append('\t', depth);
		}

		static void Close(StringBuilder builder, bool empty, int depth, char closing)
		{
			if (!empty)
			{
				builder.Append('\n').Append('\t', depth);
			}
			builder.Append(closing);
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
	}
}
