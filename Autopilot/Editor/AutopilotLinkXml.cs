using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.UnityLinker;

namespace Dexc.Autopilot
{
	/// <summary>
	/// What stripping must leave for a Player with the autopilot in it: the game a bot reaches into by private name, which
	/// no compiled code refers to, and what async needs from the class library. Nothing else is kept whole. An engine member
	/// only a bot calls is stripped, and a bot compiled against the Player's own assemblies does not compile against it.
	///
	/// It looks at the assemblies this build compiled, not at settings, so it holds however the build is started: a define
	/// can come in through the build's own options, which nothing here can read. Handed to the linker for the one build:
	/// a link.xml under Assets would apply to every build of the project.
	/// </summary>
	public sealed class AutopilotLinkXml : IUnityLinkerProcessor
	{
		static readonly string[] s_PipelineAssemblies =
		{
			"Unity.Pipeline.IlInterpreter",
			"UnityPipeline.System.Reflection.Metadata",
		};

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

		const string TelemetryAssembly = "Dexc.Telemetry";
		const string QAAssembly = "Dexc.QA";

		public int callbackOrder => 0;

		public string GenerateAdditionalLinkXmlFile(BuildReport report, UnityLinkerBuildPipelineData data)
		{
			if (!IsInBuild("Dexc.Autopilot"))
				return string.Empty;

			var content = new StringBuilder("<linker>\n");
			Keep(content, "Dexc.Autopilot");
			foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
			{
				if (assembly.IsDynamic)
					continue;
				var declared = assembly.GetCustomAttributes<AutopilotKeepAssemblyAttribute>();
				var any = false;
				foreach (var attribute in declared)
				{
					Keep(content, attribute.Assembly);
					any = true;
				}

				if (any)
					Keep(content, assembly.GetName().Name);
			}

			foreach (var assembly in s_PipelineAssemblies)
			{
				if (IsInBuild(assembly))
					Keep(content, assembly);
			}

			// The workspace starts telemetry, captures and seals through eval, which the game's code need not call.
			if (IsInBuild(TelemetryAssembly))
				Keep(content, TelemetryAssembly);
			// Bots check and agents report through Dexc QA, which the game's code need not call either.
			if (IsInBuild(QAAssembly))
				Keep(content, QAAssembly);

			content.Append("  <assembly fullname=\"mscorlib\">\n");
			foreach (var type in s_ClassLibrary)
				content.Append("    <type fullname=\"").Append(type).Append("\" preserve=\"all\"/>\n");
			content.Append("  </assembly>\n</linker>\n");

			var path = Path.GetFullPath(Path.Combine("Temp", "DexcAutopilot.link.xml"));
			File.WriteAllText(path, content.ToString());
			return path;
		}

		static bool IsInBuild(string assembly) =>
			File.Exists(Path.Combine("Library", "Bee", "PlayerScriptAssemblies", assembly + ".dll"));

		static void Keep(StringBuilder content, string assembly) =>
			content.Append("  <assembly fullname=\"").Append(assembly).Append("\" preserve=\"all\"/>\n");
	}
}
