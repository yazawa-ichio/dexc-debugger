using System;
using System.Collections.Generic;
using Dexc.Autopilot.Bridge;

namespace Dexc.Autopilot
{
	/// <summary>
	/// The interpreter of the Pipeline package. It runs a subset of C#, so a bot needs the guardrails in Tools~/Guard.
	/// A member the build does not have is refused here, not at whatever frame first reaches it.
	/// </summary>
	public sealed class PipelineBackend : IAutopilotBackend
	{
		private readonly Func<IEnumerable<Type>> m_OpenTypes;

		/// <param name="openTypes">Types whose private members a bot may reach.</param>
		public PipelineBackend(Func<IEnumerable<Type>> openTypes) => m_OpenTypes = openTypes;

		public string Name => "pipeline";

		public IAutopilotModule Load(byte[] assembly) => new Module(DexcIlModule.Load(assembly, m_OpenTypes()));

		private sealed class Module : IAutopilotModule
		{
			private readonly DexcIlModule m_Module;

			public Module(DexcIlModule module) => m_Module = module;

			public IReadOnlyList<string> UnboundMembers => m_Module.UnboundMembers;

			public object Invoke(string type, string method, params object[] arguments)
			{
				if (m_Module.UnboundMembers.Count > 0)
					throw new InvalidOperationException("Not available in this build: " + string.Join(", ", m_Module.UnboundMembers));
				return m_Module.Invoke(method, arguments);
			}

			public void Dispose() => m_Module.Dispose();
		}
	}
}
