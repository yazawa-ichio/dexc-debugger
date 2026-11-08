using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Dexc.Telemetry.CodeGen
{
	/// <summary>What the IL processing does to one module. Apart from Unity, so that tests can run it on IL they build.</summary>
	internal static class TelemetryWeaver
	{
		const string LoggerType = "Dexc.Telemetry.TelemetryLogger";
		const string ValueType = "Dexc.Telemetry.TelemetryValue";

		/// <summary>
		/// Reports each log call whose template has a different number of holes than the call has values. Only a template
		/// written as a constant, with no branch in the arguments, can be read here. Calls the compile symbols removed are
		/// not in the IL, so they are not checked.
		/// </summary>
		internal static void Check(ModuleDefinition module, List<TelemetryDiagnostic> diagnostics)
		{
			foreach (var type in module.GetTypes())
			{
				foreach (var method in type.Methods.Where(method => method.HasBody))
				{
					CheckTemplates(method, diagnostics);
				}
			}
		}

		static void CheckTemplates(MethodDefinition method, List<TelemetryDiagnostic> diagnostics)
		{
			foreach (var instruction in method.Body.Instructions)
			{
				if (!(instruction.Operand is MethodReference called) || !IsLogCall(called))
				{
					continue;
				}
				var parameters = called.Parameters;
				var template = -1;
				var values = 0;
				for (var i = 0; i < parameters.Count; i++)
				{
					var parameterType = parameters[i].ParameterType.FullName;
					if (template < 0 && parameterType == "System.String")
					{
						template = i;
					}
					else if (parameterType == ValueType)
					{
						values++;
					}
				}
				if (template < 0)
				{
					continue;
				}
				// The logger is the first argument.
				var producer = StackWalk.Producer(method.Body, instruction, template + 1);
				if (producer == null || producer.OpCode.Code != Code.Ldstr)
				{
					continue;
				}
				var text = (string)producer.Operand;
				var holes = TemplateHoles.Count(text);
				if (holes != values)
				{
					var point = SequencePoint(method, instruction);
					diagnostics.Add(TelemetryDiagnostic.Error(
						point?.Document.Url,
						point?.StartLine ?? 0,
						point?.StartColumn ?? 0,
						"The log template has " + holes + " hole(s) for " + values + " value(s): \"" + text + "\" in " + method.FullName
					));
				}
			}
		}

		static bool IsLogCall(MethodReference called)
		{
			return called.HasThis && IsSeverity(called.Name) && called.DeclaringType.FullName == LoggerType;
		}

		static bool IsSeverity(string name)
		{
			switch (name)
			{
				case "Verbose":
				case "Debug":
				case "Info":
				case "Warning":
				case "Error":
				case "Fatal":
					return true;
				default:
					return false;
			}
		}

		/// <summary>The line of the call: the nearest point at or before it that is not hidden.</summary>
		static SequencePoint SequencePoint(MethodDefinition method, Instruction instruction)
		{
			if (!method.DebugInformation.HasSequencePoints)
			{
				return null;
			}
			for (var current = instruction; current != null; current = current.Previous)
			{
				var point = method.DebugInformation.GetSequencePoint(current);
				if (point != null && !point.IsHidden)
				{
					return point;
				}
			}
			return null;
		}
	}
}
