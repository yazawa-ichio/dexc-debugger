namespace Dexc
{
	/// <summary>
	/// Releases what the DebugUI states own beyond managed memory when their root is dropped: render pipeline hooks,
	/// generated IMGUI textures and the helper objects the menu created. Runs while the root is still reachable through
	/// <see cref="DexcRoot.Current"/>, so the static facades below still resolve to the state being torn down.
	/// </summary>
	internal static class DexcUIRootDisposal
	{
		internal static void Dispose(DexcUIRoot root)
		{
#if DEXC_HAS_URP
			DexcPlayerRenderer.Shutdown();
#endif
			DexcRuntimeUIRenderer.DisposeStyleCache();
#if DEXC_HAS_UGUI
			DexcUGUIApi.ReleaseFallbackEventSystem();
			DexcRuntimeUGUIRenderer.DestroyInstance();
#endif
			DexcRuntimeUIToolkitRenderer.DestroyInstance();
#if DEXC_DEBUGGER
			DexcBootMenuDriver.Destroy();
			DexcRuntimeUIDriver.Destroy();
#endif
		}
	}
}
