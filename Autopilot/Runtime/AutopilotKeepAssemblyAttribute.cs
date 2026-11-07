using System;

namespace Dexc.Autopilot
{
	/// <summary>
	/// Keeps an assembly whole in a build with the autopilot in it, for a bot to reach into by private name. Put it on the
	/// game's autopilot assembly, which is kept as well.
	/// </summary>
	[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
	public sealed class AutopilotKeepAssemblyAttribute : Attribute
	{
		public AutopilotKeepAssemblyAttribute(string assembly) => Assembly = assembly;

		public string Assembly { get; }
	}
}
