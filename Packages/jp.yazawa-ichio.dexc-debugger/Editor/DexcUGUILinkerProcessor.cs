using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.UnityLinker;
using UnityEngine;

namespace Dexc
{
	internal sealed class DexcUGUILinkerProcessor : IUnityLinkerProcessor
	{
		public int callbackOrder => 0;

		internal static bool ShouldPreserve(string symbols, bool hasUGUI)
		{
			var defined = (symbols ?? string.Empty).Split(';');
			return hasUGUI && defined.Contains("DEXC_DEBUGGER") && !defined.Contains("DISABLE_DEXC_DEBUGGER");
		}

		internal static string LinkXml =>
			"<linker><assembly fullname=\"UnityEngine.UI\" ignoreIfMissing=\"1\">"
			+ "<type fullname=\"UnityEngine.UI.ColorBlock\" preserve=\"nothing\">"
			+ "<property name=\"normalColor\"/><property name=\"highlightedColor\"/>"
			+ "<property name=\"pressedColor\"/><property name=\"selectedColor\"/>"
			+ "<property name=\"disabledColor\"/><property name=\"colorMultiplier\"/>"
			+ "<property name=\"fadeDuration\"/></type></assembly></linker>";

		public string GenerateAdditionalLinkXmlFile(BuildReport report, UnityLinkerBuildPipelineData data)
		{
			var target = NamedBuildTarget.FromBuildTargetGroup(BuildPipeline.GetBuildTargetGroup(report.summary.platform));
			if (
				!ShouldPreserve(
					PlayerSettings.GetScriptingDefineSymbols(target),
					Type.GetType("UnityEngine.UI.ColorBlock, UnityEngine.UI", false) != null
				)
			)
			{
				return null;
			}

			// Packages cannot supply an Assets link.xml; the build callback owns this generated file.
			var path = Path.GetFullPath(Path.Combine(Application.dataPath, "../Library/DexcDebugger/link.xml"));
			Directory.CreateDirectory(Path.GetDirectoryName(path));
			File.WriteAllText(path, LinkXml);
			return path;
		}
	}
}
