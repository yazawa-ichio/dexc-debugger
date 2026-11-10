using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

namespace UniTaskStateMachinePool.CodeGen
{
	/// <summary>
	/// Makes every UniTask async method run on the runner that is shared by all methods of its result type, without
	/// allocating when it suspends.
	///
	/// The compiler hands the state machine struct to the builder as a type argument, and UniTask keeps it in a runner
	/// class of that type, so each async method brings its own runner class into the IL2CPP runtime. The builder needs
	/// the state machine only as IAsyncStateMachine, which a struct can be given as only in a box. The box does not have
	/// to be new each time: the state has to live on the heap while the method is suspended, and nothing more.
	///
	/// So each state machine struct gets a pool of its own boxes, in two static fields and one link:
	/// - at the first await that suspends, a box is taken from the pool, or made, and the struct is written into it; the
	///   builder is called with the box and with IAsyncStateMachine as the type argument. The box knows itself.
	/// - when the method completes, right after it has set its result or its exception, the box is emptied and put back.
	///   MoveNext reads nothing of its own after that, and the runner only drops its reference later.
	/// - a call that never suspends makes no box, as before.
	///
	/// A state machine is rewritten only when every await in it has the form the C# compiler writes; otherwise it is left
	/// as it is. Generic state machines and those that are classes (a Debug compilation) are left as they are.
	/// </summary>
	public static class StateMachinePoolRewriter
	{
		static readonly HashSet<string> s_builders = new HashSet<string>
		{
			"Cysharp.Threading.Tasks.CompilerServices.AsyncUniTaskMethodBuilder",
			"Cysharp.Threading.Tasks.CompilerServices.AsyncUniTaskMethodBuilder`1",
			"Cysharp.Threading.Tasks.CompilerServices.AsyncUniTaskVoidMethodBuilder",
		};

		public sealed class Result
		{
			public int Machines;
			public int Awaits;
			public int Completions;
			public int Starts;
			public int Skipped;

			public override string ToString()
			{
				return Machines + " state machines (" + Awaits + " awaits, " + Completions + " completions, " + Starts + " starts), " + Skipped + " left as they were";
			}
		}

		sealed class Members
		{
			public FieldDefinition Self;
			public FieldDefinition Next;
			public FieldDefinition Pool;
			public FieldDefinition Gate;
			public MethodDefinition Rent;
			public MethodDefinition Return;
		}

		/// <summary>Rewrites an assembly file in place. For checking the result without a build.</summary>
		public static string RewriteFile(string input, string output)
		{
			using (var resolver = new DefaultAssemblyResolver())
			{
				resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(input)));
				using (var assembly = AssemblyDefinition.ReadAssembly(new MemoryStream(File.ReadAllBytes(input)), new ReaderParameters { AssemblyResolver = resolver }))
				{
					var result = Rewrite(assembly.MainModule);
					assembly.Write(output);
					return result.ToString();
				}
			}
		}

		public static Result Rewrite(ModuleDefinition module)
		{
			var result = new Result();
			var rewritten = new HashSet<TypeDefinition>();
			foreach (var type in module.GetTypes().ToArray())
			{
				var moveNext = type.Methods.FirstOrDefault(method => method.Name == "MoveNext" && method.HasBody);
				var builder = type.Fields.FirstOrDefault(field => !field.IsStatic && s_builders.Contains(field.FieldType.GetElementType().FullName));
				var contract = type.Interfaces.Select(implemented => implemented.InterfaceType).FirstOrDefault(candidate => candidate.FullName == "System.Runtime.CompilerServices.IAsyncStateMachine");
				if (moveNext == null || builder == null || contract == null)
				{
					continue;
				}
				if (!type.IsValueType || type.HasGenericParameters || !Machine(type, moveNext, builder, contract, result))
				{
					result.Skipped++;
					continue;
				}
				rewritten.Add(type);
				result.Machines++;
			}
			// builder.Start(ref machine) only calls machine.MoveNext(); calling that leaves no Start<TStateMachine> to make.
			foreach (var method in module.GetTypes().SelectMany(type => type.Methods).Where(method => method.HasBody))
			{
				var instructions = method.Body.Instructions;
				for (var index = 3; index < instructions.Count; index++)
				{
					var instruction = instructions[index];
					if (instruction.OpCode != OpCodes.Call || !(instruction.Operand is GenericInstanceMethod call) || call.Name != "Start" || call.GenericArguments.Count != 1)
					{
						continue;
					}
					var machine = call.GenericArguments[0] as TypeDefinition;
					if (machine == null || !rewritten.Contains(machine) || !s_builders.Contains(call.DeclaringType.GetElementType().FullName))
					{
						continue;
					}
					// ldloca machine; ldflda builder; ldloca machine; call Start
					var first = instructions[index - 3];
					var field = instructions[index - 2];
					var second = instructions[index - 1];
					if (!IsLoadLocalAddress(first) || field.OpCode != OpCodes.Ldflda || !IsLoadLocalAddress(second) || !ReferenceEquals(first.Operand, second.Operand))
					{
						continue;
					}
					field.OpCode = OpCodes.Nop;
					field.Operand = null;
					second.OpCode = OpCodes.Nop;
					second.Operand = null;
					instruction.Operand = machine.Methods.First(candidate => candidate.Name == "MoveNext");
					result.Starts++;
				}
			}
			return result;
		}

		static bool Machine(TypeDefinition type, MethodDefinition moveNext, FieldDefinition builder, TypeReference contract, Result result)
		{
			var instructions = moveNext.Body.Instructions;
			var awaits = new List<Instruction>();
			var completions = new List<Instruction>();
			for (var index = 0; index < instructions.Count; index++)
			{
				var instruction = instructions[index];
				if (instruction.OpCode != OpCodes.Call || !(instruction.Operand is MethodReference call) || !s_builders.Contains(call.DeclaringType.GetElementType().FullName))
				{
					continue;
				}
				if (call.Name == "AwaitUnsafeOnCompleted" || call.Name == "AwaitOnCompleted")
				{
					// ldarg.0; ldflda builder; ldloca awaiter; ldarg.0; call
					var generic = call as GenericInstanceMethod;
					if (generic == null || generic.GenericArguments.Count != 2 || generic.GenericArguments[1] != type || index < 4
						|| instructions[index - 4].OpCode != OpCodes.Ldarg_0
						|| instructions[index - 3].OpCode != OpCodes.Ldflda || ((FieldReference)instructions[index - 3].Operand).Resolve() != builder
						|| !IsLoadLocalAddress(instructions[index - 2])
						|| instructions[index - 1].OpCode != OpCodes.Ldarg_0)
					{
						return false;
					}
					awaits.Add(instruction);
				}
				else if (call.Name == "SetResult" || call.Name == "SetException")
				{
					completions.Add(instruction);
				}
			}
			if (awaits.Count == 0)
			{
				// Never suspends, so never needs a runner.
				return false;
			}

			var members = AddMembers(type);
			var module = type.Module;
			var box = new VariableDefinition(contract);
			moveNext.Body.Variables.Add(box);
			moveNext.Body.InitLocals = true;
			moveNext.Body.SimplifyMacros();
			var il = moveNext.Body.GetILProcessor();
			VariableDefinition noop = null;

			foreach (var site in awaits)
			{
				var at = instructions.IndexOf(site);
				var first = instructions[at - 4];
				var builderAddress = instructions[at - 3];
				var awaiterAddress = instructions[at - 2];
				var self = instructions[at - 1];
				var after = instructions[at + 1];
				var call = (GenericInstanceMethod)site.Operand;
				var erased = new GenericInstanceMethod(call.ElementMethod);
				erased.GenericArguments.Add(call.GenericArguments[0]);
				erased.GenericArguments.Add(contract);
				var builderField = (FieldReference)builderAddress.Operand;
				var awaiter = (VariableDefinition)awaiterAddress.Operand;
				var have = Instruction.Create(OpCodes.Ldarg_0);
				// The same call for an awaiter that registers nothing: it only makes the builder take a runner.
				var idle = IdleAwaiter(module);
				var prepare = new GenericInstanceMethod(call.ElementMethod);
				prepare.GenericArguments.Add(idle);
				prepare.GenericArguments.Add(contract);
				if (noop == null)
				{
					noop = new VariableDefinition(idle);
					moveNext.Body.Variables.Add(noop);
				}
				var sequence = new[]
				{
					// first stays where it is, so that whatever branched to it still does: ldarg.0
					// The state machine knows its box once it runs in one; on the stack of the method that started it, it has none.
					Instruction.Create(OpCodes.Ldfld, members.Self),
					Instruction.Create(OpCodes.Brtrue, have),
					Instruction.Create(OpCodes.Ldarg_0),
					Instruction.Create(OpCodes.Call, members.Rent),
					Instruction.Create(OpCodes.Stloc, box),
					// The runner first, with nothing registered, so that nothing can resume the box yet.
					Instruction.Create(OpCodes.Ldloc, box),
					Instruction.Create(OpCodes.Unbox, type),
					Instruction.Create(OpCodes.Ldflda, builderField),
					Instruction.Create(OpCodes.Ldloca, noop),
					Instruction.Create(OpCodes.Ldloca, box),
					Instruction.Create(OpCodes.Call, prepare),
					// The method that started the state machine asks its own copy of the builder for the task. The copy is
					// taken before the awaiter is told: once it is, the continuation may run on another thread, or right
					// inside the call, complete, and hand the box back to the pool.
					Instruction.Create(OpCodes.Ldarg_0),
					Instruction.Create(OpCodes.Ldloc, box),
					Instruction.Create(OpCodes.Unbox, type),
					Instruction.Create(OpCodes.Ldfld, builderField),
					Instruction.Create(OpCodes.Stfld, builderField),
					// Last of all, and nothing of the box is touched after it.
					Instruction.Create(OpCodes.Ldloc, box),
					Instruction.Create(OpCodes.Unbox, type),
					Instruction.Create(OpCodes.Ldflda, builderField),
					Instruction.Create(OpCodes.Ldloca, awaiter),
					Instruction.Create(OpCodes.Ldloca, box),
					Instruction.Create(OpCodes.Call, erased),
					Instruction.Create(OpCodes.Br, after),
					// Already in its box, and so already with a runner: the builder takes nothing from the state machine argument.
					have,
					Instruction.Create(OpCodes.Ldflda, builderField),
					Instruction.Create(OpCodes.Ldloca, awaiter),
					Instruction.Create(OpCodes.Ldloca, box),
					Instruction.Create(OpCodes.Call, erased),
				};
				il.Remove(builderAddress);
				il.Remove(awaiterAddress);
				il.Remove(self);
				il.Remove(site);
				var last = first;
				foreach (var instruction in sequence)
				{
					il.InsertAfter(last, instruction);
					last = instruction;
				}
				result.Awaits++;
			}
			foreach (var site in completions)
			{
				var load = Instruction.Create(OpCodes.Ldarg_0);
				il.InsertAfter(site, load);
				il.InsertAfter(load, Instruction.Create(OpCodes.Call, members.Return));
				result.Completions++;
			}
			moveNext.Body.OptimizeMacros();
			return true;
		}

		static Members AddMembers(TypeDefinition type)
		{
			var module = type.Module;
			var members = new Members
			{
				Self = new FieldDefinition("<>pool__box", FieldAttributes.Private, module.TypeSystem.Object),
				Next = new FieldDefinition("<>pool__next", FieldAttributes.Private, module.TypeSystem.Object),
				Pool = new FieldDefinition("<>pool__head", FieldAttributes.Private | FieldAttributes.Static, module.TypeSystem.Object),
				Gate = new FieldDefinition("<>pool__gate", FieldAttributes.Private | FieldAttributes.Static, module.TypeSystem.Int32),
			};
			type.Fields.Add(members.Self);
			type.Fields.Add(members.Next);
			type.Fields.Add(members.Pool);
			type.Fields.Add(members.Gate);

			var interlocked = new TypeReference("System.Threading", "Interlocked", module, module.TypeSystem.CoreLibrary);
			var compareExchange = new MethodReference("CompareExchange", module.TypeSystem.Int32, interlocked);
			compareExchange.Parameters.Add(new ParameterDefinition(new ByReferenceType(module.TypeSystem.Int32)));
			compareExchange.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int32));
			compareExchange.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int32));

			// static object Rent(ref TStateMachine machine): a box that holds a copy of the state machine and knows itself.
			members.Rent = new MethodDefinition("<>pool__Rent", MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig, module.TypeSystem.Object);
			members.Rent.Parameters.Add(new ParameterDefinition("machine", ParameterAttributes.None, new ByReferenceType(type)));
			type.Methods.Add(members.Rent);
			{
				var body = members.Rent.Body;
				body.InitLocals = true;
				var box = new VariableDefinition(module.TypeSystem.Object);
				body.Variables.Add(box);
				var il = body.GetILProcessor();
				var release = Instruction.Create(OpCodes.Ldc_I4_0);
				var skip = Instruction.Create(OpCodes.Ldloc, box);
				var reuse = Instruction.Create(OpCodes.Ldloc, box);
				var set = Instruction.Create(OpCodes.Ldloc, box);
				il.Emit(OpCodes.Ldnull);
				il.Emit(OpCodes.Stloc, box);
				// The pool is entered by one thread at a time; a thread that finds it taken does without, as TaskPool does.
				il.Emit(OpCodes.Ldsflda, members.Gate);
				il.Emit(OpCodes.Ldc_I4_1);
				il.Emit(OpCodes.Ldc_I4_0);
				il.Emit(OpCodes.Call, compareExchange);
				il.Emit(OpCodes.Brtrue, skip);
				il.Emit(OpCodes.Ldsfld, members.Pool);
				il.Emit(OpCodes.Stloc, box);
				il.Emit(OpCodes.Ldloc, box);
				il.Emit(OpCodes.Brfalse, release);
				il.Emit(OpCodes.Ldloc, box);
				il.Emit(OpCodes.Unbox, type);
				il.Emit(OpCodes.Ldfld, members.Next);
				il.Emit(OpCodes.Stsfld, members.Pool);
				il.Append(release);
				il.Emit(OpCodes.Volatile);
				il.Emit(OpCodes.Stsfld, members.Gate);
				il.Append(skip);
				il.Emit(OpCodes.Brtrue, reuse);
				il.Emit(OpCodes.Ldarg_0);
				il.Emit(OpCodes.Ldobj, type);
				il.Emit(OpCodes.Box, type);
				il.Emit(OpCodes.Stloc, box);
				il.Emit(OpCodes.Br, set);
				// A box that was used before: the state machine is written over what it held.
				il.Append(reuse);
				il.Emit(OpCodes.Unbox, type);
				il.Emit(OpCodes.Ldarg_0);
				il.Emit(OpCodes.Ldobj, type);
				il.Emit(OpCodes.Stobj, type);
				il.Append(set);
				il.Emit(OpCodes.Unbox, type);
				il.Emit(OpCodes.Ldloc, box);
				il.Emit(OpCodes.Stfld, members.Self);
				il.Emit(OpCodes.Ldloc, box);
				il.Emit(OpCodes.Ret);
			}

			// static void Return(ref TStateMachine machine): empties the box the state machine ran in and puts it back.
			members.Return = new MethodDefinition("<>pool__Return", MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig, module.TypeSystem.Void);
			members.Return.Parameters.Add(new ParameterDefinition("machine", ParameterAttributes.None, new ByReferenceType(type)));
			type.Methods.Add(members.Return);
			{
				var body = members.Return.Body;
				body.InitLocals = true;
				var box = new VariableDefinition(module.TypeSystem.Object);
				body.Variables.Add(box);
				var il = body.GetILProcessor();
				var go = Instruction.Create(OpCodes.Ldarg_0);
				var done = Instruction.Create(OpCodes.Ret);
				il.Emit(OpCodes.Ldarg_0);
				il.Emit(OpCodes.Ldfld, members.Self);
				il.Emit(OpCodes.Stloc, box);
				il.Emit(OpCodes.Ldloc, box);
				il.Emit(OpCodes.Brtrue, go);
				// It completed without ever suspending: it is still on the stack and has no box.
				il.Emit(OpCodes.Ret);
				// Emptied, so that a box that waits in the pool keeps nothing alive.
				il.Append(go);
				il.Emit(OpCodes.Initobj, type);
				il.Emit(OpCodes.Ldsflda, members.Gate);
				il.Emit(OpCodes.Ldc_I4_1);
				il.Emit(OpCodes.Ldc_I4_0);
				il.Emit(OpCodes.Call, compareExchange);
				il.Emit(OpCodes.Brtrue, done);
				il.Emit(OpCodes.Ldarg_0);
				il.Emit(OpCodes.Ldsfld, members.Pool);
				il.Emit(OpCodes.Stfld, members.Next);
				il.Emit(OpCodes.Ldloc, box);
				il.Emit(OpCodes.Stsfld, members.Pool);
				il.Emit(OpCodes.Ldc_I4_0);
				il.Emit(OpCodes.Volatile);
				il.Emit(OpCodes.Stsfld, members.Gate);
				il.Append(done);
			}
			return members;
		}

		const string IdleAwaiterName = "<StateMachinePool>IdleAwaiter";

		/// <summary>
		/// An awaiter that never calls back, one for each assembly. Handing it to the builder makes the builder take its
		/// runner and register nothing.
		/// </summary>
		static TypeDefinition IdleAwaiter(ModuleDefinition module)
		{
			var existing = module.Types.FirstOrDefault(type => type.Name == IdleAwaiterName);
			if (existing != null)
			{
				return existing;
			}
			var core = module.TypeSystem.CoreLibrary;
			var idle = new TypeDefinition("", IdleAwaiterName, TypeAttributes.NotPublic | TypeAttributes.Sealed | TypeAttributes.SequentialLayout | TypeAttributes.BeforeFieldInit, new TypeReference("System", "ValueType", module, core))
			{
				PackingSize = 0,
				ClassSize = 1,
			};
			var action = new TypeReference("System", "Action", module, core);
			foreach (var contract in new[] { "ICriticalNotifyCompletion", "INotifyCompletion" })
			{
				idle.Interfaces.Add(new InterfaceImplementation(new TypeReference("System.Runtime.CompilerServices", contract, module, core)));
			}
			foreach (var name in new[] { "UnsafeOnCompleted", "OnCompleted" })
			{
				var method = new MethodDefinition(name, MethodAttributes.Public | MethodAttributes.Final | MethodAttributes.Virtual | MethodAttributes.NewSlot | MethodAttributes.HideBySig, module.TypeSystem.Void);
				method.Parameters.Add(new ParameterDefinition("continuation", ParameterAttributes.None, action));
				method.Body.GetILProcessor().Emit(OpCodes.Ret);
				idle.Methods.Add(method);
			}
			module.Types.Add(idle);
			return idle;
		}

		static bool IsLoadLocalAddress(Instruction instruction)
		{
			return instruction.OpCode == OpCodes.Ldloca || instruction.OpCode == OpCodes.Ldloca_S;
		}
	}
}
