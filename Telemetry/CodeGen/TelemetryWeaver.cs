using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

namespace Dexc.Telemetry.CodeGen
{
	/// <summary>What the IL processing does to one module. Apart from Unity, so that tests can run it on IL they build.</summary>
	internal static class TelemetryWeaver
	{
		const string LoggerType = "Dexc.Telemetry.TelemetryLogger";
		const string ValueType = "Dexc.Telemetry.TelemetryValue";
		const string TracerType = "Dexc.Telemetry.TelemetryTracer";
		internal const string DetailBasic = "DEXC_TELEMETRY_DETAIL_BASIC";
		internal const string DetailStandard = "DEXC_TELEMETRY_DETAIL_STANDARD";
		internal const string DetailDetailed = "DEXC_TELEMETRY_DETAIL_DETAILED";

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

		/// <summary>
		/// Turns each span start finer than the symbols keep into an empty span, with its arguments, as
		/// <c>ConditionalAttribute</c> does for the calls that return nothing. The symbols are those of the project and those
		/// the file of the call defines. A start whose detail is not a constant, or whose arguments are not straight code,
		/// is left: the detail at run time still stops it.
		/// </summary>
		/// <param name="readSource">The symbols of a source file by its path, or null when it cannot be read.</param>
		/// <returns>Whether anything changed.</returns>
		internal static bool StripSpans(ModuleDefinition module, ICollection<string> defines, Func<string, SourceSymbols> readSource)
		{
			var files = new Dictionary<string, SourceSymbols>();
			var changed = false;
			foreach (var type in module.GetTypes())
			{
				foreach (var method in type.Methods.Where(method => method.HasBody))
				{
					changed |= StripSpans(method, defines, path =>
					{
						if (!files.TryGetValue(path, out var symbols))
						{
							symbols = readSource(path);
							files.Add(path, symbols);
						}
						return symbols;
					});
				}
			}
			return changed;
		}

		static bool StripSpans(MethodDefinition method, ICollection<string> defines, Func<string, SourceSymbols> symbolsOf)
		{
			var body = method.Body;
			var ranges = new List<Instruction[]>();
			foreach (var instruction in body.Instructions)
			{
				if (!(instruction.Operand is MethodReference called) || !IsSpanStart(called))
				{
					continue;
				}
				// this, name, parent, detail
				var detail = StackWalk.Producer(body, instruction, 3);
				if (detail == null || !StackWalk.TryConstant(detail, out var value))
				{
					continue;
				}
				var path = SequencePoint(method, instruction)?.Document.Url;
				var symbols = path == null ? null : symbolsOf(path);
				if (value <= KeptDetail(defines, symbols))
				{
					continue;
				}
				var start = StackWalk.ArgumentsStart(body, instruction);
				if (start == null)
				{
					continue;
				}
				var range = new List<Instruction>();
				for (var current = start; current != instruction; current = current.Next)
				{
					range.Add(current);
				}
				range.Add(instruction);
				if (range.Count >= 3)
				{
					ranges.Add(range.ToArray());
				}
			}
			if (ranges.Count == 0)
			{
				return false;
			}
			body.SimplifyMacros();
			foreach (var range in ranges)
			{
				var span = ((MethodReference)range[range.Length - 1].Operand).ReturnType;
				var empty = new VariableDefinition(span);
				body.Variables.Add(empty);
				// The instructions stay, so that a branch to the first one and the lines of the debugger still hold.
				for (var i = 0; i < range.Length - 3; i++)
				{
					range[i].OpCode = OpCodes.Nop;
					range[i].Operand = null;
				}
				Set(range[range.Length - 3], OpCodes.Ldloca, empty);
				Set(range[range.Length - 2], OpCodes.Initobj, span);
				Set(range[range.Length - 1], OpCodes.Ldloc, empty);
			}
			body.OptimizeMacros();
			return true;
		}

		static void Set(Instruction instruction, OpCode opCode, object operand)
		{
			instruction.OpCode = opCode;
			instruction.Operand = operand;
		}

		static bool IsSpanStart(MethodReference called)
		{
			return called.HasThis && called.Name == "Start" && called.DeclaringType.FullName == TracerType && called.Parameters.Count == 3;
		}

		/// <summary>The finest detail the symbols keep, or zero when they keep none. A file the reader cannot decide keeps all.</summary>
		static int KeptDetail(ICollection<string> defines, SourceSymbols symbols)
		{
			if (symbols != null && !symbols.IsKnown)
			{
				return int.MaxValue;
			}
			if (Holds(DetailDetailed, defines, symbols))
			{
				return 3;
			}
			if (Holds(DetailStandard, defines, symbols))
			{
				return 2;
			}
			return Holds(DetailBasic, defines, symbols) ? 1 : 0;
		}

		static bool Holds(string symbol, ICollection<string> defines, SourceSymbols symbols)
		{
			var byProject = defines.Contains(symbol);
			return symbols == null ? byProject : symbols.Holds(symbol, byProject);
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
