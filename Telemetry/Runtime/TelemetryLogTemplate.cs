using System.Text;

namespace Dexc.Telemetry
{
	/// <summary>
	/// Reads message templates in the form of the OpenTelemetry .NET logger: <c>stage {Stage} took {Seconds:F2}</c>. Values
	/// go to the holes in order, whatever the names are. <c>{{</c> and <c>}}</c> are plain braces. Nothing here allocates
	/// except <see cref="Format"/>, which is the one place that makes the text.
	/// </summary>
	internal static class TelemetryLogTemplate
	{
		internal struct Hole
		{
			/// <summary>Where the opening brace is.</summary>
			internal int Start;

			/// <summary>Just after the closing brace.</summary>
			internal int End;

			internal int NameLength;

			/// <summary>Where the format text starts, or zero without one.</summary>
			internal int FormatStart;

			internal int NameStart => Start + 1;
		}

		/// <summary>Finds the next hole at or after <paramref name="position"/>, and moves the position past it.</summary>
		internal static bool NextHole(string template, ref int position, out Hole hole)
		{
			hole = default;
			var index = position;
			while (index < template.Length)
			{
				var character = template[index];
				if (character == '{' || character == '}')
				{
					if (index + 1 < template.Length && template[index + 1] == character)
					{
						index += 2;
						continue;
					}
					if (character == '{')
					{
						var close = template.IndexOf('}', index);
						if (close < 0)
						{
							break;
						}
						hole.Start = index;
						hole.End = close + 1;
						ReadName(template, ref hole, close);
						position = hole.End;
						return true;
					}
				}
				index++;
			}
			position = template.Length;
			return false;
		}

		/// <summary>Fills the values in. A hole without a value stays as it is written.</summary>
		internal static void Format(StringBuilder builder, string template, in TelemetryLogs.Entry entry)
		{
			var position = 0;
			var copied = 0;
			var index = 0;
			while (NextHole(template, ref position, out var hole))
			{
				AppendLiteral(builder, template, copied, hole.Start);
				if (index < entry.Count)
				{
					var format = hole.FormatStart == 0 ? null : template.Substring(hole.FormatStart, hole.End - 1 - hole.FormatStart);
					entry.Value(index).Append(builder, format);
				}
				else
				{
					builder.Append(template, hole.Start, hole.End - hole.Start);
				}
				copied = hole.End;
				index++;
			}
			AppendLiteral(builder, template, copied, template.Length);
		}

		static void ReadName(string template, ref Hole hole, int close)
		{
			for (var i = hole.NameStart; i < close; i++)
			{
				if (template[i] == ':' || template[i] == ',')
				{
					hole.NameLength = i - hole.NameStart;
					hole.FormatStart = template[i] == ':' ? i + 1 : 0;
					return;
				}
			}
			hole.NameLength = close - hole.NameStart;
		}

		static void AppendLiteral(StringBuilder builder, string template, int start, int end)
		{
			for (var i = start; i < end; i++)
			{
				var character = template[i];
				builder.Append(character);
				if ((character == '{' || character == '}') && i + 1 < end && template[i + 1] == character)
				{
					i++;
				}
			}
		}
	}
}
