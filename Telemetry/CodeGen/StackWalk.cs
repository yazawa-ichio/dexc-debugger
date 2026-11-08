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

		/// <summary>
		/// The first instruction of the code that pushes all the arguments of the call, <c>this</c> included. Control may
		/// come in at that first instruction, but nowhere after it. Null when it cannot tell.
		/// </summary>
		internal static Instruction ArgumentsStart(MethodBody body, Instruction call)
		{
			var needed = ArgumentCount((MethodReference)call.Operand, call.OpCode);
			if (needed == 0)
			{
				return null;
			}
			var targets = Targets(body);
			var boundaries = Boundaries(body);
			for (var instruction = call.Previous; instruction != null; instruction = instruction.Previous)
			{
				if (boundaries.Contains(instruction) || IsJump(instruction) || !Effect(instruction, out var pop, out var push))
				{
					return null;
				}
				needed -= push;
				if (needed < 0)
				{
					return null;
				}
				needed += pop;
				if (needed == 0)
				{
					return instruction;
				}
				if (targets.Contains(instruction))
				{
					return null;
				}
			}
			return null;
		}

		/// <summary>The value of an instruction that loads an <c>int</c> constant.</summary>
		internal static bool TryConstant(Instruction instruction, out int value)
		{
			switch (instruction.OpCode.Code)
			{
				case Code.Ldc_I4_M1:
					value = -1;
					return true;
				case Code.Ldc_I4_0:
				case Code.Ldc_I4_1:
				case Code.Ldc_I4_2:
				case Code.Ldc_I4_3:
				case Code.Ldc_I4_4:
				case Code.Ldc_I4_5:
				case Code.Ldc_I4_6:
				case Code.Ldc_I4_7:
				case Code.Ldc_I4_8:
					value = instruction.OpCode.Code - Code.Ldc_I4_0;
					return true;
				case Code.Ldc_I4_S:
					value = (sbyte)instruction.Operand;
					return true;
				case Code.Ldc_I4:
					value = (int)instruction.Operand;
					return true;
				default:
					value = 0;
					return false;
			}
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
			var blocked = Targets(body);
			blocked.UnionWith(Boundaries(body));
			foreach (var instruction in body.Instructions)
			{
				if (IsJump(instruction))
				{
					blocked.Add(instruction);
				}
			}
			return blocked;
		}

		static bool IsJump(Instruction instruction)
		{
			var flow = instruction.OpCode.FlowControl;
			return flow == FlowControl.Branch || flow == FlowControl.Cond_Branch || flow == FlowControl.Return || flow == FlowControl.Throw;
		}

		/// <summary>The instructions a branch can go to.</summary>
		static HashSet<Instruction> Targets(MethodBody body)
		{
			var targets = new HashSet<Instruction>();
			foreach (var instruction in body.Instructions)
			{
				if (instruction.Operand is Instruction target)
				{
					targets.Add(target);
				}
				if (instruction.Operand is Instruction[] many)
				{
					targets.UnionWith(many);
				}
			}
			return targets;
		}

		/// <summary>The edges of the exception blocks.</summary>
		static HashSet<Instruction> Boundaries(MethodBody body)
		{
			var boundaries = new HashSet<Instruction>();
			foreach (var handler in body.ExceptionHandlers)
			{
				Add(boundaries, handler.TryStart);
				Add(boundaries, handler.TryEnd);
				Add(boundaries, handler.HandlerStart);
				Add(boundaries, handler.HandlerEnd);
				Add(boundaries, handler.FilterStart);
			}
			return boundaries;
		}

		static void Add(HashSet<Instruction> set, Instruction instruction)
		{
			if (instruction != null)
			{
				set.Add(instruction);
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
