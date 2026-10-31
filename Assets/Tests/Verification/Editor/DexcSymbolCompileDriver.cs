using System;
using UnityEditor;
using UnityEditor.Build;

namespace DexcDebugger.Verification
{
	public static class DexcSymbolCompileDriver
	{
		public static void ApplyRequestedSymbols()
		{
			var commonSymbols = Environment.GetEnvironmentVariable("DEXC_SYMBOLS") ?? string.Empty;
			var standaloneSymbols = Environment.GetEnvironmentVariable("DEXC_SYMBOLS_STANDALONE") ?? commonSymbols;
			var androidSymbols = Environment.GetEnvironmentVariable("DEXC_SYMBOLS_ANDROID") ?? commonSymbols;
			PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.Standalone, standaloneSymbols);
			PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.Android, androidSymbols);
			AssetDatabase.SaveAssets();
			EditorApplication.Exit(0);
		}
	}
}
