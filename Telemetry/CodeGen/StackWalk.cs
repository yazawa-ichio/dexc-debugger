using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Dexc.Telemetry.CodeGen
{
	/// <summary>
	/// Walks back from a call through the code that pushed its arguments. It only follows straight code: a branch, a branch
	/// target or an exception block in the way means it does not know, and gives up.
	/// </summary>
	internal static class StackWalk
	{
		/// <summary>The instruction that pushed the argument, counting <c>this</c> as the first. Null when it cannot tell.</summary>
		internal static Instruction Producer(MethodBody body, Instruction call, int argument)
		{
			var method = (MethodReference)call.Operand;
			var count = ArgumentCount(method, call.OpCode);
			if (argument < 0 || argument >= count)
			{
				return null;
			}
			var blocked = Blocked(body);
			// The values the call still needs from code before the walked one.
			var needed = count;
			for (var instruction = call.Previous; instruction != null; instruction = instruction.Previous)
			{
				if (blocked.Contains(instruction) || !Effect(instruction, out var pop, out var push))
				{
					return null;
				}
				var before = needed;
				needed -= push;
				if (needed < 0)
				{
					return null;
				}
				if (push == 1 && before == argument + 1)
				{
					return instruction;
				}
				if (needed < argument)
				{
					return null;
				}
				needed += pop;
			}
			return null;
		}

		static int ArgumentCount(MethodReference method, OpCode opCode)
		{
			var count = method.Parameters.Count;
			if (method.HasThis && !method.ExplicitThis && opCode.Code != Code.Newobj)
			{
				count++;
			}
			return count;
		}

		/// <summary>Where control can come in or go out other than straight down.</summary>
		static HashSet<Instruction> Blocked(MethodBody body)
		{
			var blocked = new HashSet<Instruction>();
			foreach (var instruction in body.Instructions)
			{
				var flow = instruction.OpCode.FlowControl;
				if (flow == FlowControl.Branch || flow == FlowControl.Cond_Branch || flow == FlowControl.Return || flow == FlowControl.Throw)
				{
					blocked.Add(instruction);
				}
				if (instruction.Operand is Instruction target)
				{
					blocked.Add(target);
				}
				if (instruction.Operand is Instruction[] targets)
				{
					blocked.UnionWith(targets);
				}
			}
			foreach (var handler in body.ExceptionHandlers)
			{
				Add(blocked, handler.TryStart);
				Add(blocked, handler.TryEnd);
				Add(blocked, handler.HandlerStart);
				Add(blocked, handler.HandlerEnd);
				Add(blocked, handler.FilterStart);
			}
			return blocked;
		}

		static void Add(HashSet<Instruction> blocked, Instruction instruction)
		{
			if (instruction != null)
			{
				blocked.Add(instruction);
			}
		}

		/// <summary>How many values the instruction takes from the stack and leaves on it.</summary>
		internal static bool Effect(Instruction instruction, out int pop, out int push)
		{
			var opCode = instruction.OpCode;
			if (opCode.Code == Code.Call || opCode.Code == Code.Callvirt || opCode.Code == Code.Newobj)
			{
				var method = (MethodReference)instruction.Operand;
				pop = ArgumentCount(method, opCode);
				push = opCode.Code == Code.Newobj || method.ReturnType.MetadataType != MetadataType.Void ? 1 : 0;
				return true;
			}
			pop = Count(opCode.StackBehaviourPop.ToString(), "Pop");
			push = Count(opCode.StackBehaviourPush.ToString(), "Push");
			return pop >= 0 && push >= 0;
		}

		/// <summary>
		/// Reads the count from the name of the stack behaviour: <c>Pop0</c> takes none, <c>Popref_popi</c> two. The
		/// variable ones, which only calls have, are unknown.
		/// </summary>
		static int Count(string behaviour, string prefix)
		{
			if (behaviour == prefix + "0")
			{
				return 0;
			}
			if (!behaviour.StartsWith(prefix) || behaviour.StartsWith("Var") || behaviour == "PopAll")
			{
				return -1;
			}
			return behaviour.Split('_').Length;
		}
	}
}
