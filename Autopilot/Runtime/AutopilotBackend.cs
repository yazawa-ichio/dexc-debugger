using System;
using System.Collections.Generic;

namespace Dexc.Autopilot
{
	/// <summary>One VM a bot or a snippet can run on.</summary>
	public interface IAutopilotBackend
	{
		/// <summary>The name a tool asks for it by.</summary>
		string Name { get; }

		/// <exception cref="Exception">The assembly cannot run on this VM.</exception>
		IAutopilotModule Load(byte[] assembly);
	}

	/// <summary>One assembly loaded into a VM of its own. Disposing it ends everything the assembly left running.</summary>
	public interface IAutopilotModule : IDisposable
	{
		/// <summary>Host members the assembly names that this build does not have.</summary>
		IReadOnlyList<string> UnboundMembers { get; }

		object Invoke(string type, string method, params object[] arguments);
	}
}
