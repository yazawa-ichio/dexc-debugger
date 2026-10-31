#if DEXC_DEBUGGER && !DISABLE_DEXC_DEBUGGER
using Dexc;

namespace DexcBootCheck
{
	/// <summary>
	/// The boot set for the Boot Check scene. It only declares what can be chosen and hands the choices to
	/// <see cref="BootCheckConfig"/> on start; the scene reads that, never this.
	/// </summary>
	[DebugBootMenu("Boot Check", Order = 0, Tag = "check", Scene = "BootCheck")]
	public sealed class BootCheckSettings : DebugBootSet
	{
		[DebugBootItem("Runtime UI Renderer", Order = 0, Description = "Debug Menu の描画方式。BootCheck 画面でも切り替えられる")]
		public BootCheckRenderer Renderer { get; private set; } = BootCheckRenderer.IMGUI;

		[DebugBootItem("Show FPS Widget", Order = 1)]
		public bool ShowFps { get; private set; } = true;

		[DebugBootItem("Open Debug Menu On Start", Order = 2)]
		public bool OpenDebugMenuOnStart { get; private set; }

		[DebugBootItem("Player Name", Order = 3)]
		public string PlayerName { get; private set; } = "Tester";

		[DebugBootItem("Time Scale", Order = 4, Step = 0.25)]
		[DebugRange(0.25, 3)]
		public float TimeScale { get; private set; } = 1f;

		/// <summary>The start options sit top-left so they do not cover the launch art on the right.</summary>
		[UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.BeforeSceneLoad)]
		static void PlaceBootMenu()
		{
			DebugBootMenu.Alignment = DexcRuntimeUIAlignment.LeftTop;
		}

		protected override void OnStarting(DebugBootGroupsValues values)
		{
			BootCheckConfig.Renderer = Renderer;
			BootCheckConfig.ShowFps = ShowFps;
			BootCheckConfig.OpenDebugMenuOnStart = OpenDebugMenuOnStart;
			BootCheckConfig.PlayerName = PlayerName;
			BootCheckConfig.TimeScale = TimeScale;
		}

		/// <summary>
		/// The renderer is remembered across launches, and the boot menu itself draws with it, so it has to be in effect
		/// before the menu appears - not only when the item is changed.
		/// </summary>
		protected override void OnOpened(DebugBootGroupsValues values)
		{
			DebugMenu.SetRenderer(ToMenuRenderer(Renderer));
		}

		/// <summary>Changing the item in the boot menu switches the renderer right away, so the boot menu itself re-renders.</summary>
		protected override void OnChanged(string propertyName)
		{
			if (propertyName == nameof(Renderer))
				DebugMenu.SetRenderer(ToMenuRenderer(Renderer));
		}

		internal static DebugMenuRenderer ToMenuRenderer(BootCheckRenderer renderer)
		{
			switch (renderer)
			{
				case BootCheckRenderer.UGUI:
					return DebugMenuRenderer.UGUI;
				case BootCheckRenderer.UIToolkit:
					return DebugMenuRenderer.UIToolkit;
				default:
					return DebugMenuRenderer.IMGUI;
			}
		}
	}
}
#endif
