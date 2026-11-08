namespace Dexc.Telemetry.CodeGen
{
	/// <summary>
	/// Counts the holes of a log template the way the runtime reads them: <c>{{</c> and <c>}}</c> are plain braces, and an
	/// opening brace without a closing one ends the template. A copy of <c>TelemetryLogTemplate.NextHole</c>, which this
	/// Editor assembly cannot reference. A test keeps the two the same.
	/// </summary>
	internal static class TemplateHoles
	{
		internal static int Count(string template)
		{
			var count = 0;
			var index = 0;
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
							return count;
						}
						count++;
						index = close + 1;
						continue;
					}
				}
				index++;
			}
			return count;
		}
	}
}
