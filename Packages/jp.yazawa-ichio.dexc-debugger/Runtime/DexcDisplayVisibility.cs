using System;
using System.Collections.Generic;

namespace Dexc
{
	internal sealed class DexcDisplayVisibility
	{
		readonly IReadOnlyDictionary<string, bool> m_Values;

		DexcDisplayVisibility(IReadOnlyDictionary<string, bool> values)
		{
			m_Values = values;
		}

		internal static DexcDisplayVisibility Capture()
		{
			return new DexcDisplayVisibility(
				DexcDebugger.CurrentSession?.Settings?.ReadBooleanPrefix("Display/") ?? new Dictionary<string, bool>()
			);
		}

		internal bool Layer(string layer) => !m_Values.TryGetValue("Display/" + layer, out var visible) || visible;

		internal bool IsVisible(DebugDisplayVisualSnapshot visual, bool subject)
		{
			var text = visual.Kind == DebugDisplayPrimitiveKind.Label || visual.Kind == DebugDisplayPrimitiveKind.Value;
			if (
				!Layer(
					text
						? subject
							? "Overlay"
							: "HUD"
						: "World"
				)
			)
			{
				return false;
			}

			foreach (var pair in m_Values)
			{
				if (pair.Value || pair.Key == "Display/HUD" || pair.Key == "Display/Overlay" || pair.Key == "Display/World")
				{
					continue;
				}

				var prefix = pair.Key.Substring("Display/".Length);
				if (visual.Path == prefix || visual.Path.StartsWith(prefix + "/", StringComparison.Ordinal))
				{
					return false;
				}
			}

			return true;
		}
	}
}
