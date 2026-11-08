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

		/// <param name="runtime">The assembly of the runtime, as the module refers to it.</param>
		internal static void Wrap(MethodDefinition method, IMetadataScope runtime, string scope, string name, Options options)
		{
			var module = method.Module;
			var probeType = new TypeReference(Namespace, "TelemetryMethodProbe", module, runtime, true);
			var detailType = new TypeReference(Namespace, "TelemetryDetail", module, runtime, true);
			var enter = new MethodReference("Enter", probeType, probeType) { HasThis = false };
			enter.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
			enter.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
			enter.Parameters.Add(new ParameterDefinition(detailType));
			enter.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int32));
			enter.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int32));
			enter.Parameters.Add(new ParameterDefinition(module.TypeSystem.Boolean));
			var exit = new MethodReference("Exit", module.TypeSystem.Void, probeType) { HasThis = true };

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

			il.InsertBefore(first, Instruction.Create(OpCodes.Ldstr, scope));
			il.InsertBefore(first, Instruction.Create(OpCodes.Ldstr, name));
			il.InsertBefore(first, Instruction.Create(OpCodes.Ldc_I4, options.Detail));
			il.InsertBefore(first, Instruction.Create(OpCodes.Ldc_I4, options.Every));
			il.InsertBefore(first, Instruction.Create(OpCodes.Ldc_I4, options.PerFrame));
			il.InsertBefore(first, Instruction.Create(OpCodes.Ldc_I4, options.AnyThread ? 1 : 0));
			il.InsertBefore(first, Instruction.Create(OpCodes.Call, enter));
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
