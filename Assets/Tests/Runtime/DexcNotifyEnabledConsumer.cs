#define DEXC_DEBUGGER
using Dexc;

namespace DexcDebugger.Tests
{
	// This call site stays enabled when the referenced Runtime assembly is Disabled.
	internal static class DexcNotifyEnabledConsumer
	{
		internal static void Notify(object command, params IDebugInput[] inputs)
		{
			if (command is DebugCommand sync)
			{
				DebugCommand.Draft.NotifyInputsChanged(sync, inputs);
			}
			else if (command is DebugCommand<int> typed)
			{
				DebugCommand.Draft.NotifyInputsChanged(typed, inputs);
			}
			else if (command is AsyncDebugCommand asyncCommand)
			{
				DebugCommand.Draft.NotifyInputsChanged(asyncCommand, inputs);
			}
			else
			{
				DebugCommand.Draft.NotifyInputsChanged((AsyncDebugCommand<int>)command, inputs);
			}
		}
	}
}
