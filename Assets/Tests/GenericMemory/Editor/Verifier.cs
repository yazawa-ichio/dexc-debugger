using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace GenericMemory.Editor
{
	/// <summary>
	/// Checks every body a step put instructions into, before anything is written: that each member it names is found in
	/// the program, and that the evaluation stack has one depth at every instruction whichever way it is reached, never
	/// goes below empty, and holds exactly the result at a return. A body that fails stops the build here.
	///
	/// What this does not check is the types of the values on the stack. A value of the wrong reference type shows up as
	/// an error of the C++ compiler, since il2cpp writes the types of the IL into the C++ as they are.
	/// </summary>
	static class Verifier
	{
		public static void Run(WholeProgram program, StringBuilder log)
		{
			var problems = new List<string>();
			var count = 0;
			foreach (var method in program.ResizedMethods)
			{
				count++;
				var problem = Check(method);
				if (problem != null)
				{
					problems.Add(method.FullName + ": " + problem);
				}
			}
			log.AppendLine("verify: " + count + " rewritten bodies, " + problems.Count + " with a problem");
			foreach (var problem in problems.Take(40))
			{
				log.AppendLine("  " + problem);
			}
			if (problems.Count > 0)
			{
				throw new InvalidOperationException("The rewritten assemblies were not written.\n" + log);
			}
		}

		static string Check(MethodDefinition method)
		{
			var body = method.Body;
			var instructions = body.Instructions;
			if (instructions.Count == 0)
			{
				return "no instructions";
			}
			foreach (var instruction in instructions)
			{
				switch (instruction.Operand)
				{
					case MethodReference called when !(called.DeclaringType is ArrayType):
						if (called.Resolve() == null)
						{
							return "method not found: " + called.FullName;
						}
						break;
					case FieldReference field:
						if (field.Resolve() == null)
						{
							return "field not found: " + field.FullName;
						}
						break;
					case Instruction target:
						if (!instructions.Contains(target))
						{
							return "branch out of the body at " + instruction;
						}
						break;
				}
			}

			var depth = new Dictionary<Instruction, int>();
			var work = new Stack<Instruction>();
			string problem = null;
			void Reach(Instruction instruction, int at, Instruction from)
			{
				if (instruction == null)
				{
					problem = problem ?? "runs off the end after " + from;
					return;
				}
				if (depth.TryGetValue(instruction, out var known))
				{
					if (known != at)
					{
						problem = problem ?? "stack depth " + known + " or " + at + " at " + instruction;
					}
					return;
				}
				depth.Add(instruction, at);
				work.Push(instruction);
			}
			Reach(instructions[0], 0, null);
			foreach (var handler in body.ExceptionHandlers)
			{
				var catches = handler.HandlerType == ExceptionHandlerType.Catch || handler.HandlerType == ExceptionHandlerType.Filter;
				Reach(handler.HandlerStart, catches ? 1 : 0, null);
				if (handler.FilterStart != null)
				{
					Reach(handler.FilterStart, 1, null);
				}
			}
			while (work.Count > 0 && problem == null)
			{
				var instruction = work.Pop();
				var at = depth[instruction];
				if (!Effect(method, instruction, out var pops, out var pushes))
				{
					return "an instruction whose effect on the stack is not known: " + instruction;
				}
				var code = instruction.OpCode.Code;
				if (code == Code.Leave || code == Code.Leave_S)
				{
					Reach((Instruction)instruction.Operand, 0, instruction);
					continue;
				}
				if (pops > at)
				{
					return "takes " + pops + " of " + at + " at " + instruction;
				}
				at += pushes - pops;
				switch (instruction.OpCode.FlowControl)
				{
					case FlowControl.Return:
						if (code == Code.Ret && at != 0)
						{
							return "returns with " + at + " left on the stack";
						}
						break;
					case FlowControl.Throw:
						break;
					case FlowControl.Branch:
						Reach((Instruction)instruction.Operand, at, instruction);
						break;
					case FlowControl.Cond_Branch:
						if (instruction.Operand is Instruction[] many)
						{
							foreach (var target in many)
							{
								Reach(target, at, instruction);
							}
						}
						else
						{
							Reach((Instruction)instruction.Operand, at, instruction);
						}
						Reach(instruction.Next, at, instruction);
						break;
					default:
						Reach(instruction.Next, at, instruction);
						break;
				}
			}
			return problem;
		}

		static bool IsVoid(TypeReference type)
		{
			while (type is IModifierType modified)
			{
				type = modified.ElementType;
			}
			return type.MetadataType == MetadataType.Void;
		}

		internal static bool Effect(MethodDefinition method, Instruction instruction, out int pops, out int pushes)
		{
			pops = 0;
			pushes = 0;
			var code = instruction.OpCode.Code;
			switch (code)
			{
				case Code.Call:
				case Code.Callvirt:
				case Code.Newobj:
				{
					var called = (MethodReference)instruction.Operand;
					pops = called.Parameters.Count + (called.HasThis && !called.ExplicitThis && code != Code.Newobj ? 1 : 0);
					pushes = code == Code.Newobj || !IsVoid(called.ReturnType) ? 1 : 0;
					return true;
				}
				case Code.Calli:
				{
					var site = (CallSite)instruction.Operand;
					pops = site.Parameters.Count + (site.HasThis && !site.ExplicitThis ? 1 : 0) + 1;
					pushes = IsVoid(site.ReturnType) ? 0 : 1;
					return true;
				}
				case Code.Ret:
					pops = IsVoid(method.ReturnType) ? 0 : 1;
					return true;
			}
			switch (instruction.OpCode.StackBehaviourPop)
			{
				case StackBehaviour.Pop0:
				case StackBehaviour.PopAll:
					pops = 0;
					break;
				case StackBehaviour.Pop1:
				case StackBehaviour.Popi:
				case StackBehaviour.Popref:
					pops = 1;
					break;
				case StackBehaviour.Pop1_pop1:
				case StackBehaviour.Popi_pop1:
				case StackBehaviour.Popi_popi:
				case StackBehaviour.Popi_popi8:
				case StackBehaviour.Popi_popr4:
				case StackBehaviour.Popi_popr8:
				case StackBehaviour.Popref_pop1:
				case StackBehaviour.Popref_popi:
					pops = 2;
					break;
				case StackBehaviour.Popi_popi_popi:
				case StackBehaviour.Popref_popi_popi:
				case StackBehaviour.Popref_popi_popi8:
				case StackBehaviour.Popref_popi_popr4:
				case StackBehaviour.Popref_popi_popr8:
				case StackBehaviour.Popref_popi_popref:
					pops = 3;
					break;
				default:
					return false;
			}
			switch (instruction.OpCode.StackBehaviourPush)
			{
				case StackBehaviour.Push0:
					pushes = 0;
					break;
				case StackBehaviour.Push1:
				case StackBehaviour.Pushi:
				case StackBehaviour.Pushi8:
				case StackBehaviour.Pushr4:
				case StackBehaviour.Pushr8:
				case StackBehaviour.Pushref:
					pushes = 1;
					break;
				case StackBehaviour.Push1_push1:
					pushes = 2;
					break;
				default:
					return false;
			}
			return true;
		}
	}
}
