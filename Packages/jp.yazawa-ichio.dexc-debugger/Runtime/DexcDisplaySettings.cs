using UnityEngine.Scripting;

namespace Dexc
{
	[Preserve]
	internal sealed class DexcDisplaySettings : DebugSettingSet<DexcDisplaySettings>
	{
		[Preserve]
		public DexcDisplaySettings() { }

		[DebugSetting("Display/HUD")]
		public bool HUD { get; private set; } = true;

		[DebugSetting("Display/Overlay")]
		public bool Overlay { get; private set; } = true;

		[DebugSetting("Display/World")]
		public bool World { get; private set; } = true;
	}
}
