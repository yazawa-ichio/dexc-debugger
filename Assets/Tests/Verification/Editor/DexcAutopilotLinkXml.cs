using System;
using System.IO;
using System.Text;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.UnityLinker;

namespace DexcDebugger.Verification
{
	/// <summary>
	/// What stripping must leave for the autopilot Player: the game a bot reaches into by private name, which no compiled
	/// code refers to, and what async needs from the class library. Nothing else is kept whole. An engine member only a bot
	/// calls is stripped, and the bot does not compile against it.
	///
	/// Handed to the linker for this one build. A link.xml under Assets would apply to every build of the project.
	/// </summary>
	public sealed class DexcAutopilotLinkXml : IUnityLinkerProcessor
	{
		static readonly string[] s_ClassLibrary =
		{
			"System.Nullable`1",
			"System.Threading.Tasks.Task",
			"System.Threading.Tasks.Task`1",
			"System.Runtime.CompilerServices.INotifyCompletion",
			"System.Runtime.CompilerServices.ICriticalNotifyCompletion",
			"System.Runtime.CompilerServices.IAsyncStateMachine",
			"System.Runtime.CompilerServices.TaskAwaiter",
			"System.Runtime.CompilerServices.TaskAwaiter`1",
			"System.Runtime.CompilerServices.YieldAwaitable",
			"System.Runtime.CompilerServices.YieldAwaitable/YieldAwaiter",
			"System.Runtime.CompilerServices.AsyncTaskMethodBuilder",
			"System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1",
			"System.Runtime.CompilerServices.AsyncVoidMethodBuilder",
		};

		public int callbackOrder => 0;

		public string GenerateAdditionalLinkXmlFile(BuildReport report, UnityLinkerBuildPipelineData data)
		{
			if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DEXC_AUTOPILOT_BUILD_PATH")))
				return string.Empty;
			var configuration = DexcAutopilotPlayerBuildDriver.Configuration;
			if (configuration == DexcAutopilotPlayerBuildDriver.WithoutAutopilot)
				return string.Empty;

			var content = new StringBuilder("<linker>\n");
			Keep(content, "DexcDebugger.Samples.StealthStageDemo.Gameplay");
			Keep(content, "DexcDebugger.Samples.StealthStageDemo.Autopilot");
			Keep(content, "Dexc.Autopilot");
			if (configuration == DexcAutopilotPlayerBuildDriver.WithBothVms)
			{
				Keep(content, "Unity.Pipeline.IlInterpreter");
				Keep(content, "UnityPipeline.System.Reflection.Metadata");
			}

			content.Append("  <assembly fullname=\"mscorlib\">\n");
			foreach (var type in s_ClassLibrary)
				content.Append("    <type fullname=\"").Append(type).Append("\" preserve=\"all\"/>\n");
			content.Append("  </assembly>\n</linker>\n");

			var path = Path.GetFullPath(Path.Combine("Temp", "DexcAutopilot.link.xml"));
			File.WriteAllText(path, content.ToString());
			return path;
		}

		static void Keep(StringBuilder content, string assembly) =>
			content.Append("  <assembly fullname=\"").Append(assembly).Append("\" preserve=\"all\"/>\n");
	}
}
