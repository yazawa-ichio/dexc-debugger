using System.Collections.Generic;

namespace Dexc.Telemetry.CodeGen
{
	/// <summary>
	/// What a source file defines and undefines with <c>#define</c> and <c>#undef</c>. C# allows them only before the first
	/// token, so reading the top of the file is enough. They are not in the compiled assembly, but
	/// <c>ConditionalAttribute</c> obeys them, and the IL processing must decide the same way.
	/// </summary>
	internal sealed class SourceSymbols
	{
		readonly HashSet<string> m_Defined = new HashSet<string>();
		readonly HashSet<string> m_Undefined = new HashSet<string>();

		SourceSymbols()
		{
		}

		/// <summary>False when an <c>#if</c> comes before the first token: then the symbols depend on others, and nothing is decided.</summary>
		internal bool IsKnown { get; private set; } = true;

		/// <summary>Whether the symbol holds in this file, given whether the project defines it.</summary>
		internal bool Holds(string symbol, bool definedByProject)
		{
			if (m_Undefined.Contains(symbol))
			{
				return false;
			}
			return definedByProject || m_Defined.Contains(symbol);
		}

		internal static SourceSymbols Read(string text)
		{
			var symbols = new SourceSymbols();
			var inComment = false;
			foreach (var raw in text.TrimStart('﻿').Split('\n'))
			{
				var line = raw.Trim();
				if (inComment)
				{
					var close = line.IndexOf("*/");
					if (close < 0)
					{
						continue;
					}
					inComment = false;
					line = line.Substring(close + 2).Trim();
				}
				if (line.Length == 0 || line.StartsWith("//"))
				{
					continue;
				}
				if (line.StartsWith("/*"))
				{
					var close = line.IndexOf("*/", 2);
					if (close < 0)
					{
						inComment = true;
						continue;
					}
					if (line.Substring(close + 2).Trim().Length == 0)
					{
						continue;
					}
					return symbols;
				}
				if (!line.StartsWith("#"))
				{
					return symbols;
				}
				var words = line.Substring(1).Trim().Split(new[] { ' ', '\t' }, 3, System.StringSplitOptions.RemoveEmptyEntries);
				var directive = words.Length > 0 ? words[0] : string.Empty;
				switch (directive)
				{
					case "define":
						if (words.Length > 1)
						{
							symbols.m_Defined.Add(words[1]);
							symbols.m_Undefined.Remove(words[1]);
						}
						break;
					case "undef":
						if (words.Length > 1)
						{
							symbols.m_Undefined.Add(words[1]);
							symbols.m_Defined.Remove(words[1]);
						}
						break;
					case "if":
					case "elif":
					case "else":
					case "endif":
						symbols.IsKnown = false;
						return symbols;
				}
			}
			return symbols;
		}
	}
}
