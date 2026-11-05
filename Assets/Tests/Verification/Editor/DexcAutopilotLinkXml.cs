using System;
using System.IO;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.UnityLinker;

namespace DexcDebugger.Verification
{
	/// <summary>
	/// What stripping must leave for the autopilot Player: the interpreter, and the game a bot reaches into by
	/// private name, which no compiled code refers to. Nothing else is kept whole. An engine member only a bot calls is
	/// stripped, and Load reports it as unbound.
	///
	/// Handed to the linker for this one build. A link.xml under Assets would apply to every build of the project.
	/// </summary>
	public sealed class DexcAutopilotLinkXml : IUnityLinkerProcessor
	{
		const string Content =
			"<linker>\n"
			+ "  <assembly fullname=\"DexcDebugger.Samples.StealthStageDemo.Gameplay\" preserve=\"all\"/>\n"
			+ "  <assembly fullname=\"DexcDebugger.Samples.StealthStageDemo.Autopilot\" preserve=\"all\"/>\n"
			+ "  <assembly fullname=\"Dexc.Autopilot\" preserve=\"all\"/>\n"
			+ "  <assembly fullname=\"Unity.Pipeline.IlInterpreter\" preserve=\"all\"/>\n"
			+ "  <assembly fullname=\"UnityPipeline.System.Reflection.Metadata\" preserve=\"all\"/>\n"
			+ "  <assembly fullname=\"UniTask\" preserve=\"all\"/>\n"
			+ "  <assembly fullname=\"mscorlib\">\n"
			+ "    <type fullname=\"System.GC\" preserve=\"all\"/>\n"
			+ "    <type fullname=\"System.Nullable`1\" preserve=\"all\"/>\n"
			+ "    <type fullname=\"System.Diagnostics.Stopwatch\" preserve=\"all\"/>\n"
			+ "    <type fullname=\"System.Threading.Tasks.Task\" preserve=\"all\"/>\n"
			+ "    <type fullname=\"System.Threading.Tasks.Task`1\" preserve=\"all\"/>\n"
			+ "    <type fullname=\"System.Runtime.CompilerServices.INotifyCompletion\" preserve=\"all\"/>\n"
			+ "    <type fullname=\"System.Runtime.CompilerServices.ICriticalNotifyCompletion\" preserve=\"all\"/>\n"
			+ "    <type fullname=\"System.Runtime.CompilerServices.IAsyncStateMachine\" preserve=\"all\"/>\n"
			+ "    <type fullname=\"System.Runtime.CompilerServices.TaskAwaiter\" preserve=\"all\"/>\n"
			+ "    <type fullname=\"System.Runtime.CompilerServices.TaskAwaiter`1\" preserve=\"all\"/>\n"
			+ "    <type fullname=\"System.Runtime.CompilerServices.YieldAwaitable\" preserve=\"all\"/>\n"
			+ "    <type fullname=\"System.Runtime.CompilerServices.YieldAwaitable/YieldAwaiter\" preserve=\"all\"/>\n"
			+ "    <type fullname=\"System.Runtime.CompilerServices.AsyncTaskMethodBuilder\" preserve=\"all\"/>\n"
			+ "    <type fullname=\"System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1\" preserve=\"all\"/>\n"
			+ "    <type fullname=\"System.Runtime.CompilerServices.AsyncVoidMethodBuilder\" preserve=\"all\"/>\n"
			+ "  </assembly>\n"
			+ "</linker>\n";

		public int callbackOrder => 0;

		public string GenerateAdditionalLinkXmlFile(BuildReport report, UnityLinkerBuildPipelineData data)
		{
			if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DEXC_AUTOPILOT_BUILD_PATH")))
				return string.Empty;
			var path = Path.GetFullPath(Path.Combine("Temp", "DexcAutopilot.link.xml"));
			File.WriteAllText(path, Content);
			return path;
		}
	}
}
