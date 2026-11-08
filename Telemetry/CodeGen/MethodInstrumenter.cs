using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

namespace Dexc.Telemetry.CodeGen
{
	/// <summary>
	/// Wraps a method body so that each call goes through <c>TelemetryMethodProbe</c>:
	/// <code>
	/// var probe = TelemetryMethodProbe.Enter(scope, name, detail, every, perFrame, anyThread);
	/// try { body } finally { probe.Exit(); }
	/// </code>
	/// Each return becomes a store of the result and a leave to one return after the finally block.
	/// </summary>
	internal static class MethodInstrumenter
	{
		const string Namespace = "Dexc.Telemetry";

		internal struct Options
		{
			internal int Detail;
			internal int Every;
			internal int PerFrame;
			internal bool AnyThread;
		}

		const string BuilderField = "<>t__builder";
		const string StateField = "<>1__state";
		const string ProbeField = "<>dexc__probe";

		static TypeReference ProbeType(ModuleDefinition module, IMetadataScope runtime)
		{
			return new TypeReference(Namespace, "TelemetryMethodProbe", module, runtime, true);
		}

		static MethodReference EnterMethod(ModuleDefinition module, IMetadataScope runtime, string methodName)
		{
			var probeType = ProbeType(module, runtime);
			var enter = new MethodReference(methodName, probeType, probeType) { HasThis = false };
			enter.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
			enter.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
			enter.Parameters.Add(new ParameterDefinition(new TypeReference(Namespace, "TelemetryDetail", module, runtime, true)));
			enter.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int32));
			enter.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int32));
			enter.Parameters.Add(new ParameterDefinition(module.TypeSystem.Boolean));
			return enter;
		}

		static MethodReference ExitMethod(ModuleDefinition module, IMetadataScope runtime)
		{
			return new MethodReference("Exit", module.TypeSystem.Void, ProbeType(module, runtime)) { HasThis = true };
		}

		static Instruction[] EnterCall(MethodReference enter, string scope, string name, Options options)
		{
			return new[]
			{
				Instruction.Create(OpCodes.Ldstr, scope),
				Instruction.Create(OpCodes.Ldstr, name),
				Instruction.Create(OpCodes.Ldc_I4, options.Detail),
				Instruction.Create(OpCodes.Ldc_I4, options.Every),
				Instruction.Create(OpCodes.Ldc_I4, options.PerFrame),
				Instruction.Create(OpCodes.Ldc_I4, options.AnyThread ? 1 : 0),
				Instruction.Create(OpCodes.Call, enter),
			};
		}

		/// <summary>
		/// Measures an async method from its first call to its completion. The state machine gets a field for the probe.
		/// The method sets it before it starts the machine, and the machine ends it just before it reports the result or
		/// the exception to its builder, which every awaitable type has.
		/// </summary>
		/// <param name="problem">What did not fit, when nothing changed.</param>
		/// <returns>False when the method is not in the shape the compiler makes, and nothing changed.</returns>
		internal static bool WrapAsync(MethodDefinition method, TypeDefinition machine, IMetadataScope runtime, string scope, string name, Options options, out string problem)
		{
			var module = method.Module;
			var moveNext = MoveNext(machine);
			var builder = machine.Fields.FirstOrDefault(field => field.Name == BuilderField);
			if (moveNext == null || builder == null)
			{
				problem = "the state machine has no MoveNext or builder";
				return false;
			}
			var builderType = builder.FieldType.GetElementType().FullName;
			var stub = method.Body;
			var next = moveNext.Body;
			stub.SimplifyMacros();
			next.SimplifyMacros();

			var start = stub.Instructions.FirstOrDefault(instruction => IsBuilderCall(instruction, builderType, "Start"));
			var state = stub.Instructions.Select(instruction => instruction.Operand).OfType<FieldReference>().FirstOrDefault(field => field.Name == StateField);
			var own = next.Instructions.Select(instruction => instruction.Operand).OfType<FieldReference>().FirstOrDefault(field => field.Name == BuilderField);
			var startArguments = start == null ? null : StackWalk.ArgumentsStart(stub, start);
			// The builder, then the machine by reference.
			var machineLoad = start == null ? null : StackWalk.Producer(stub, start, 1);
			var ends = next.Instructions.Where(instruction => IsBuilderCall(instruction, builderType, "SetResult") || IsBuilderCall(instruction, builderType, "SetException")).ToList();
			var endArguments = ends.Select(end => StackWalk.ArgumentsStart(next, end)).ToList();
			if (startArguments == null || state == null || own == null || machineLoad == null || machineLoad.OpCode.Code != Code.Ldloca || ends.Count == 0 || endArguments.Contains(null))
			{
				stub.OptimizeMacros();
				next.OptimizeMacros();
				problem = start == null || startArguments == null || machineLoad == null || machineLoad.OpCode.Code != Code.Ldloca
					? "the start of the state machine is not in the usual shape"
					: "the end of the state machine is not in the usual shape";
				return false;
			}

			var probeType = ProbeType(module, runtime);
			var field = new FieldDefinition(ProbeField, FieldAttributes.Public, probeType);
			machine.Fields.Add(field);
			// The fields as the code refers to them: the method through the instance of its own generic arguments.
			var fromStub = new FieldReference(field.Name, probeType, state.DeclaringType);
			var fromMachine = new FieldReference(field.Name, probeType, own.DeclaringType);

			var variable = (VariableDefinition)machineLoad.Operand;
			var setProbe = new List<Instruction> { Instruction.Create(machine.IsValueType ? OpCodes.Ldloca : OpCodes.Ldloc, variable) };
			setProbe.AddRange(EnterCall(EnterMethod(module, runtime, "EnterAsync"), scope, name, options));
			setProbe.Add(Instruction.Create(OpCodes.Stfld, fromStub));
			InsertBefore(stub, startArguments, setProbe.ToArray());

			var exit = ExitMethod(module, runtime);
			foreach (var end in endArguments)
			{
				InsertBefore(next, end, Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Ldflda, fromMachine), Instruction.Create(OpCodes.Call, exit));
			}
			stub.OptimizeMacros();
			next.OptimizeMacros();
			problem = null;
			return true;
		}

		internal static MethodDefinition MoveNext(TypeDefinition machine)
		{
			return machine.Methods.FirstOrDefault(candidate => candidate.Name == "MoveNext" && candidate.HasBody);
		}

		static bool IsBuilderCall(Instruction instruction, string builderType, string name)
		{
			return (instruction.OpCode.Code == Code.Call || instruction.OpCode.Code == Code.Callvirt)
				&& instruction.Operand is MethodReference called
				&& called.Name == name
				&& called.DeclaringType.GetElementType().FullName == builderType;
		}

		/// <summary>Inserts before the instruction, and makes what led to it lead to the new code.</summary>
		static void InsertBefore(MethodBody body, Instruction target, params Instruction[] inserted)
		{
			var il = body.GetILProcessor();
			foreach (var instruction in inserted)
			{
				il.InsertBefore(target, instruction);
			}
			var first = inserted[0];
			foreach (var instruction in body.Instructions)
			{
				if (instruction.Operand == target)
				{
					instruction.Operand = first;
				}
				else if (instruction.Operand is Instruction[] targets)
				{
					for (var i = 0; i < targets.Length; i++)
					{
						if (targets[i] == target)
						{
							targets[i] = first;
						}
					}
				}
			}
			foreach (var handler in body.ExceptionHandlers)
			{
				handler.TryStart = handler.TryStart == target ? first : handler.TryStart;
				handler.TryEnd = handler.TryEnd == target ? first : handler.TryEnd;
				handler.HandlerStart = handler.HandlerStart == target ? first : handler.HandlerStart;
				handler.HandlerEnd = handler.HandlerEnd == target ? first : handler.HandlerEnd;
				handler.FilterStart = handler.FilterStart == target ? first : handler.FilterStart;
			}
		}

		/// <param name="runtime">The assembly of the runtime, as the module refers to it.</param>
		internal static void Wrap(MethodDefinition method, IMetadataScope runtime, string scope, string name, Options options)
		{
			var module = method.Module;
			var probeType = ProbeType(module, runtime);
			var enter = EnterMethod(module, runtime, "Enter");
			var exit = ExitMethod(module, runtime);

			var body = method.Body;
			body.SimplifyMacros();
			body.InitLocals = true;
			var il = body.GetILProcessor();
			var probe = new VariableDefinition(probeType);
			body.Variables.Add(probe);
			var returns = method.ReturnType.MetadataType != MetadataType.Void;
			var result = returns ? new VariableDefinition(method.ReturnType) : null;
			if (result != null)
			{
				body.Variables.Add(result);
			}
			var first = body.Instructions[0];
			var handlerStart = Instruction.Create(OpCodes.Ldloca, probe);
			var end = returns ? Instruction.Create(OpCodes.Ldloc, result) : Instruction.Create(OpCodes.Ret);

			foreach (var instruction in body.Instructions.ToList())
			{
				if (instruction.OpCode.Code == Code.Tail)
				{
					// A tail call must be followed by a return, which is now a leave.
					instruction.OpCode = OpCodes.Nop;
				}
				else if (instruction.OpCode.Code == Code.Ret)
				{
					// Changed in place, so that a branch to the return still lands.
					if (returns)
					{
						instruction.OpCode = OpCodes.Stloc;
						instruction.Operand = result;
						il.InsertAfter(instruction, Instruction.Create(OpCodes.Leave, end));
					}
					else
					{
						instruction.OpCode = OpCodes.Leave;
						instruction.Operand = end;
					}
				}
			}
			// A handler that ran to the end of the method must not take in what is appended.
			foreach (var handler in body.ExceptionHandlers)
			{
				if (handler.TryEnd == null)
				{
					handler.TryEnd = handlerStart;
				}
				if (handler.HandlerEnd == null)
				{
					handler.HandlerEnd = handlerStart;
				}
			}
			il.Append(handlerStart);
			il.Append(Instruction.Create(OpCodes.Call, exit));
			il.Append(Instruction.Create(OpCodes.Endfinally));
			il.Append(end);
			if (returns)
			{
				il.Append(Instruction.Create(OpCodes.Ret));
			}

			// Before the first instruction but outside the try block: a branch to the first one stays inside it.
			foreach (var instruction in EnterCall(enter, scope, name, options))
			{
				il.InsertBefore(first, instruction);
			}
			il.InsertBefore(first, Instruction.Create(OpCodes.Stloc, probe));

			body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Finally)
			{
				TryStart = first,
				TryEnd = handlerStart,
				HandlerStart = handlerStart,
				HandlerEnd = end,
			});
			body.OptimizeMacros();
		}
	}
}
