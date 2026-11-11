using System.Collections.Generic;
using System.Linq;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace GenericMemory.Editor
{
	/// <summary>
	/// Runs every UniTask async method on one runner class for each result type, instead of one for each method.
	///
	/// The compiler makes a state machine struct for each async method and hands it to the builder as a type argument:
	/// AwaitUnsafeOnCompleted&lt;TAwaiter, TStateMachine&gt; and Start&lt;TStateMachine&gt;. UniTask then keeps the struct in
	/// AsyncUniTask&lt;TStateMachine, T&gt;, so each async method brings its own instance of that class, of its pool, of its
	/// interfaces and of the builder's methods into the runtime.
	///
	/// The builder needs the state machine only as IAsyncStateMachine. So at the first await that really suspends, the
	/// struct is boxed and the box is handed over with IAsyncStateMachine as the type argument. From then on the method
	/// runs on the box, as it ran on the copy inside the runner before. Start only calls MoveNext, so it is called directly.
	/// MoveNext keeps its name and place, so a stack trace reads as before.
	/// </summary>
	sealed class AsyncEraser
	{
		// The builders of UniTask and the field in which each keeps its runner.
		static readonly Dictionary<string, string> s_runnerFields = new Dictionary<string, string>
		{
			{ "Cysharp.Threading.Tasks.CompilerServices.AsyncUniTaskMethodBuilder", "runnerPromise" },
			{ "Cysharp.Threading.Tasks.CompilerServices.AsyncUniTaskMethodBuilder`1", "runnerPromise" },
			{ "Cysharp.Threading.Tasks.CompilerServices.AsyncUniTaskVoidMethodBuilder", "runner" },
		};

		readonly WholeProgram _program;
		readonly StringBuilder _log;

		public AsyncEraser(WholeProgram program, StringBuilder log)
		{
			_program = program;
			_log = log;
		}

		public void Run()
		{
			var awaits = 0;
			var starts = 0;
			var skipped = 0;
			var machines = new HashSet<TypeDefinition>();
			foreach (var method in _program.Bodies().ToArray())
			{
				var instructions = method.Body.Instructions;
				for (var index = 0; index < instructions.Count; index++)
				{
					var instruction = instructions[index];
					if (instruction.OpCode != OpCodes.Call || !(instruction.Operand is GenericInstanceMethod call) || !s_runnerFields.ContainsKey(call.DeclaringType.GetElementType().FullName))
					{
						continue;
					}
					// What this step wrote itself names the interface, and is left alone.
					var named = call.GenericArguments[call.GenericArguments.Count - 1].Resolve();
					if (named == null || !named.IsValueType)
					{
						continue;
					}
					if (call.Name == "Start" && call.GenericArguments.Count == 1)
					{
						if (Start(method, index, call))
						{
							starts++;
						}
						else
						{
							skipped++;
						}
					}
					else if ((call.Name == "AwaitUnsafeOnCompleted" || call.Name == "AwaitOnCompleted") && call.GenericArguments.Count == 2)
					{
						if (Await(method, index, call))
						{
							awaits++;
							machines.Add(method.DeclaringType);
						}
						else
						{
							skipped++;
						}
					}
				}
			}
			_log.AppendLine("async: " + machines.Count + " state machines, " + awaits + " awaits and " + starts + " starts rewritten, " + skipped + " left as they were");
		}

		/// <summary>builder.Start(ref machine) becomes machine.MoveNext(), which is all that Start does.</summary>
		bool Start(MethodDefinition method, int index, GenericInstanceMethod call)
		{
			var machine = call.GenericArguments[0];
			var definition = machine.Resolve();
			var instructions = method.Body.Instructions;
			if (definition == null || !definition.IsValueType || index < 3)
			{
				return false;
			}
			// ldloca machine; ldflda builder; ldloca machine; call Start
			var first = instructions[index - 3];
			var field = instructions[index - 2];
			var second = instructions[index - 1];
			if (!IsLoadAddress(first) || field.OpCode != OpCodes.Ldflda || !IsLoadAddress(second) || !SameLocal(first, second))
			{
				return false;
			}
			var moveNext = new MethodReference("MoveNext", method.Module.TypeSystem.Void, machine) { HasThis = true };
			field.OpCode = OpCodes.Nop;
			field.Operand = null;
			second.OpCode = OpCodes.Nop;
			second.Operand = null;
			instructions[index].Operand = moveNext;
			_program.Touch(method.Module);
			return true;
		}

		/// <summary>
		/// builder.AwaitUnsafeOnCompleted(ref awaiter, ref this) inside MoveNext of a state machine struct. Where the builder
		/// has no runner yet, the struct is boxed, the call is made on the builder inside the box, and the builder is
		/// copied back so that the method that started the state machine finds the runner when it asks for the task.
		/// </summary>
		bool Await(MethodDefinition method, int index, GenericInstanceMethod call)
		{
			var machine = call.GenericArguments[1];
			var definition = machine.Resolve();
			var instructions = method.Body.Instructions;
			if (definition == null || !definition.IsValueType || definition != method.DeclaringType || index < 4)
			{
				return false;
			}
			// ldarg.0; ldflda builder; ldloca awaiter; ldarg.0; call AwaitUnsafeOnCompleted
			var first = instructions[index - 4];
			var builderAddress = instructions[index - 3];
			var awaiterAddress = instructions[index - 2];
			var self = instructions[index - 1];
			if (first.OpCode != OpCodes.Ldarg_0 || builderAddress.OpCode != OpCodes.Ldflda || !IsLoadAddress(awaiterAddress) || self.OpCode != OpCodes.Ldarg_0)
			{
				return false;
			}
			var builderField = (FieldReference)builderAddress.Operand;
			var builderType = builderField.FieldType;
			var builderDefinition = builderType.Resolve();
			var runnerDefinition = builderDefinition?.Fields.FirstOrDefault(field => field.Name == s_runnerFields[builderDefinition.FullName]);
			var asyncStateMachine = definition.Interfaces.Select(implemented => implemented.InterfaceType).FirstOrDefault(type => type.FullName == "System.Runtime.CompilerServices.IAsyncStateMachine");
			if (runnerDefinition == null || asyncStateMachine == null)
			{
				return false;
			}
			var module = method.Module;
			var importedRunner = module.ImportReference(runnerDefinition);
			var runnerField = new FieldReference(importedRunner.Name, importedRunner.FieldType, builderType);
			var erased = new GenericInstanceMethod(call.ElementMethod);
			erased.GenericArguments.Add(call.GenericArguments[0]);
			erased.GenericArguments.Add(asyncStateMachine);

			var box = method.Body.Variables.FirstOrDefault(variable => variable.VariableType.FullName == asyncStateMachine.FullName);
			if (box == null)
			{
				box = new VariableDefinition(asyncStateMachine);
				method.Body.Variables.Add(box);
				method.Body.InitLocals = true;
			}
			var awaiter = awaiterAddress.OpCode;
			var awaiterOperand = awaiterAddress.Operand;
			var after = instructions[index + 1];
			var have = Instruction.Create(OpCodes.Ldarg_0);
			var il = method.Body.GetILProcessor();
			var sequence = new List<Instruction>
			{
				// first stays where it is, so that whatever branched to it still does: ldarg.0
				Instruction.Create(OpCodes.Ldflda, builderField),
				Instruction.Create(OpCodes.Ldfld, runnerField),
				Instruction.Create(OpCodes.Brtrue, have),
				// No runner yet: this is the copy on the stack of the method that started the state machine.
				Instruction.Create(OpCodes.Ldarg_0),
				Instruction.Create(OpCodes.Ldobj, machine),
				Instruction.Create(OpCodes.Box, machine),
				Instruction.Create(OpCodes.Stloc, box),
				Instruction.Create(OpCodes.Ldloc, box),
				Instruction.Create(OpCodes.Unbox, machine),
				Instruction.Create(OpCodes.Ldflda, builderField),
				Instruction.Create(awaiter, (VariableDefinition)awaiterOperand),
				Instruction.Create(OpCodes.Ldloca, box),
				Instruction.Create(OpCodes.Call, erased),
				Instruction.Create(OpCodes.Ldarg_0),
				Instruction.Create(OpCodes.Ldloc, box),
				Instruction.Create(OpCodes.Unbox, machine),
				Instruction.Create(OpCodes.Ldfld, builderField),
				Instruction.Create(OpCodes.Stfld, builderField),
				Instruction.Create(OpCodes.Br, after),
				// The runner is there: the builder takes nothing from the state machine argument.
				have,
				Instruction.Create(OpCodes.Ldflda, builderField),
				Instruction.Create(awaiter, (VariableDefinition)awaiterOperand),
				Instruction.Create(OpCodes.Ldloca, box),
				Instruction.Create(OpCodes.Call, erased),
			};
			// The four instructions after the first, and the call, give way to the sequence.
			il.Remove(builderAddress);
			il.Remove(awaiterAddress);
			il.Remove(self);
			var originalCall = instructions[instructions.IndexOf(first) + 1];
			var at = first;
			foreach (var instruction in sequence)
			{
				il.InsertAfter(at, instruction);
				at = instruction;
			}
			il.Remove(originalCall);
			_program.Resized(method);
			return true;
		}

		static bool IsLoadAddress(Instruction instruction)
		{
			return instruction.OpCode == OpCodes.Ldloca || instruction.OpCode == OpCodes.Ldloca_S;
		}

		static bool SameLocal(Instruction a, Instruction b)
		{
			return ReferenceEquals(a.Operand, b.Operand);
		}
	}
}
