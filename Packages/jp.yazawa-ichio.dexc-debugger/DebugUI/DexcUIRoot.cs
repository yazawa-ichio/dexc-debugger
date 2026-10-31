using System;

namespace Dexc
{
	/// <summary>
	/// Per-root state of the DebugUI assembly. Every static facade in this assembly forwards to one of these slots,
	/// so a <see cref="DexcRoot"/> reset (new Play Mode session, tests) drops all UI state at once.
	/// </summary>
	internal sealed class DexcUIRoot : IDisposable
	{
		// Slots are created on first use: building one can resolve reflection or load a built-in resource, and a game that
		// never opens a given renderer should not pay for it.
		/// <summary>The menu driver, created when the menu is first opened.</summary>
		internal DexcRuntimeUIDriver MenuDriver;

		internal static DexcUIRoot Current
		{
			get
			{
				var root = DexcRoot.Current;
				if (root.UI is DexcUIRoot current)
					return current;
				root.MenuPanel = DexcRuntimeUIReferenceResolver.OpenPanel;
				return (DexcUIRoot)(root.UI = new DexcUIRoot());
			}
		}

		DebugBootMenu.State m_BootMenu;
		internal DebugBootMenu.State BootMenu => m_BootMenu ?? (m_BootMenu = new DebugBootMenu.State());
#if DEXC_DEBUGGER
		DexcBootMenuState.State m_BootMenuState;
		internal DexcBootMenuState.State BootMenuState => m_BootMenuState ?? (m_BootMenuState = new DexcBootMenuState.State());
#endif
#if DEXC_DEBUGGER
		DexcBootMenuInput.State m_BootMenuInput;
		internal DexcBootMenuInput.State BootMenuInput => m_BootMenuInput ?? (m_BootMenuInput = new DexcBootMenuInput.State());
#endif
#if DEXC_DEBUGGER
		DexcBootMenuRenderer.State m_BootMenuRenderer;
		internal DexcBootMenuRenderer.State BootMenuRenderer =>
			m_BootMenuRenderer ?? (m_BootMenuRenderer = new DexcBootMenuRenderer.State());
#endif
#if DEXC_DEBUGGER
		DexcBootMenuDriver.State m_BootMenuDriver;
		internal DexcBootMenuDriver.State BootMenuDriver => m_BootMenuDriver ?? (m_BootMenuDriver = new DexcBootMenuDriver.State());
#endif
		DexcRuntimeCommandExecution.State m_CommandExecution;
		internal DexcRuntimeCommandExecution.State CommandExecution =>
			m_CommandExecution ?? (m_CommandExecution = new DexcRuntimeCommandExecution.State());
		DexcRuntimeCommandForm.State m_CommandForm;
		internal DexcRuntimeCommandForm.State CommandForm => m_CommandForm ?? (m_CommandForm = new DexcRuntimeCommandForm.State());
		DexcUIRows.State m_Home;
		internal DexcUIRows.State Home => m_Home ?? (m_Home = new DexcUIRows.State());
		DexcRuntimeFavorites.State m_Favorites;
		internal DexcRuntimeFavorites.State Favorites => m_Favorites ?? (m_Favorites = new DexcRuntimeFavorites.State());
		DexcRuntimeSplitHome.State m_SplitHome;
		internal DexcRuntimeSplitHome.State SplitHome => m_SplitHome ?? (m_SplitHome = new DexcRuntimeSplitHome.State());
		DexcRuntimeFormDetail.State m_FormDetail;
		internal DexcRuntimeFormDetail.State FormDetail => m_FormDetail ?? (m_FormDetail = new DexcRuntimeFormDetail.State());
		DexcRuntimeHistory.State m_History;
		internal DexcRuntimeHistory.State History => m_History ?? (m_History = new DexcRuntimeHistory.State());
		DexcRuntimeInspectorForm.State m_InspectorForm;
		internal DexcRuntimeInspectorForm.State InspectorForm =>
			m_InspectorForm ?? (m_InspectorForm = new DexcRuntimeInspectorForm.State());
		DexcRuntimeInspectorNavigation.State m_InspectorNavigation;
		internal DexcRuntimeInspectorNavigation.State InspectorNavigation =>
			m_InspectorNavigation ?? (m_InspectorNavigation = new DexcRuntimeInspectorNavigation.State());
		DexcRuntimePanelForm.State m_PanelForm;
		internal DexcRuntimePanelForm.State PanelForm => m_PanelForm ?? (m_PanelForm = new DexcRuntimePanelForm.State());
		DexcRuntimeSettingForm.State m_SettingForm;
		internal DexcRuntimeSettingForm.State SettingForm => m_SettingForm ?? (m_SettingForm = new DexcRuntimeSettingForm.State());
#if DEXC_HAS_UGUI
		DexcRuntimeUGUIRenderer.State m_UGUIRenderer;
		internal DexcRuntimeUGUIRenderer.State UGUIRenderer => m_UGUIRenderer ?? (m_UGUIRenderer = new DexcRuntimeUGUIRenderer.State());
#endif
#if DEXC_HAS_UGUI
		DexcUGUIApi.State m_UGUIApi;
		internal DexcUGUIApi.State UGUIApi => m_UGUIApi ?? (m_UGUIApi = new DexcUGUIApi.State());
#endif
		DexcRuntimeUIRendererSelection.State m_RendererSelection;
		internal DexcRuntimeUIRendererSelection.State RendererSelection =>
			m_RendererSelection ?? (m_RendererSelection = new DexcRuntimeUIRendererSelection.State());
		DexcRuntimeUIState.State m_UIState;
		internal DexcRuntimeUIState.State UIState => m_UIState ?? (m_UIState = new DexcRuntimeUIState.State());
		DexcRuntimeUIRenderer.State m_Renderer;
		internal DexcRuntimeUIRenderer.State Renderer => m_Renderer ?? (m_Renderer = new DexcRuntimeUIRenderer.State());
		DexcRuntimeUIToolkitRenderer.State m_UIToolkitRenderer;
		internal DexcRuntimeUIToolkitRenderer.State UIToolkitRenderer =>
			m_UIToolkitRenderer ?? (m_UIToolkitRenderer = new DexcRuntimeUIToolkitRenderer.State());

		/// <summary>Releases what the states own outside managed memory: Unity event hooks, generated textures, helper objects.</summary>
		public void Dispose()
		{
			DexcUIRootDisposal.Dispose(this);
		}
	}
}
