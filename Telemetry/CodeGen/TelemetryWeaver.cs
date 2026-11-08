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
		const string MethodAttributeType = "Dexc.Telemetry.TelemetryMethodAttribute";
		internal const string RuntimeAssembly = "Dexc.Telemetry";
		internal const string WatchSymbol = "DEXC_TELEMETRY_WATCH";
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
			var symbolsOf = Cached(readSource);
			var changed = false;
			foreach (var type in module.GetTypes())
			{
				foreach (var method in type.Methods.Where(method => method.HasBody))
				{
					changed |= StripSpans(method, defines, symbolsOf);
				}
			}
			return changed;
		}

		/// <summary>Reads each source file once.</summary>
		static Func<string, SourceSymbols> Cached(Func<string, SourceSymbols> readSource)
		{
			var files = new Dictionary<string, SourceSymbols>();
			return path =>
			{
				if (!files.TryGetValue(path, out var symbols))
				{
					symbols = readSource(path);
					files.Add(path, symbols);
				}
				return symbols;
			};
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

		/// <summary>
		/// Puts the measuring of <c>TelemetryMethodAttribute</c> in, where the symbols keep its detail. A method's own
		/// attribute wins over its type's. Through the type, constructors, accessors and code the compiler made are left
		/// out. An async method is measured from its first call to its completion. Iterators are left out: their body only
		/// makes the state machine, and nothing marks when it is done.
		/// </summary>
		/// <returns>Whether anything changed.</returns>
		internal static bool InstrumentMethods(ModuleDefinition module, ICollection<string> defines, Func<string, SourceSymbols> readSource, List<TelemetryDiagnostic> diagnostics)
		{
			var changed = false;
			readSource = Cached(readSource);
			foreach (var type in module.GetTypes())
			{
				var typeAttribute = MethodAttribute(type);
				foreach (var method in type.Methods)
				{
					var own = MethodAttribute(method);
					var attribute = own ?? typeAttribute;
					if (attribute == null || !method.HasBody || method.IsConstructor || IsIterator(method))
					{
						continue;
					}
					if (own == null && (method.IsSpecialName || IsCompilerGenerated(method)))
					{
						continue;
					}
					var options = new MethodInstrumenter.Options { Detail = 3, Every = 1, PerFrame = 32 };
					foreach (var property in attribute.Properties)
					{
						switch (property.Name)
						{
							case "Detail":
								options.Detail = (int)property.Argument.Value;
								break;
							case "Every":
								options.Every = (int)property.Argument.Value;
								break;
							case "PerFrame":
								options.PerFrame = (int)property.Argument.Value;
								break;
							case "AnyThread":
								options.AnyThread = (bool)property.Argument.Value;
								break;
						}
					}
					var machine = AsyncMachine(method);
					// The lines of an async method are in its state machine: the method itself only starts it.
					var point = FirstPoint(method) ?? (machine == null ? null : FirstPoint(MethodInstrumenter.MoveNext(machine)));
					if (options.Every < 1 || options.PerFrame < 1)
					{
						diagnostics.Add(TelemetryDiagnostic.Error(point?.Document.Url, point?.StartLine ?? 0, point?.StartColumn ?? 0, "TelemetryMethod needs Every and PerFrame of one or more: " + method.FullName));
						continue;
					}
					var symbols = point == null ? null : readSource(point.Document.Url);
					if (options.Detail > KeptDetail(defines, symbols))
					{
						continue;
					}
					var name = method.DeclaringType.FullName.Replace('/', '.') + "." + method.Name;
					if (machine != null)
					{
						if (MethodInstrumenter.WrapAsync(method, machine, attribute.AttributeType.Scope, module.Assembly.Name.Name, name, options, "EnterAsync", out var problem))
						{
							changed = true;
						}
						else
						{
							diagnostics.Add(TelemetryDiagnostic.Warning(point?.Document.Url, point?.StartLine ?? 0, point?.StartColumn ?? 0, "TelemetryMethod left this async method as written, because " + problem + ": " + method.FullName));
						}
						continue;
					}
					MethodInstrumenter.Wrap(method, attribute.AttributeType.Scope, module.Assembly.Name.Name, name, options, "Enter");
					changed = true;
				}
			}
			return changed;
		}

		/// <summary>
		/// Puts the measuring that <c>DexcTelemetry.Watch</c> turns on into each method the preset or the selectors of the
		/// project choose. It stays off until then. Constructors, accessors, code the compiler made, iterators and methods
		/// that already have <c>TelemetryMethodAttribute</c> are left out. An assembly that does not refer to the runtime
		/// is given the reference.
		/// </summary>
		/// <returns>Whether anything changed.</returns>
		internal static bool InstrumentWatched(ModuleDefinition module, TelemetryProfile preset, IReadOnlyList<ITelemetryMethodSelector> selectors, List<TelemetryDiagnostic> diagnostics)
		{
			var assembly = module.Assembly.Name.Name;
			IMetadataScope runtime = null;
			var changed = false;
			foreach (var type in module.GetTypes())
			{
				if (IsCompilerGenerated(type) || MethodAttribute(type) != null)
				{
					continue;
				}
				foreach (var method in type.Methods)
				{
					if (!method.HasBody || method.IsConstructor || method.IsSpecialName || IsCompilerGenerated(method) || IsIterator(method) || MethodAttribute(method) != null)
					{
						continue;
					}
					var byPreset = preset != null && preset.Chooses(assembly, method);
					if (!byPreset && !Selected(selectors, assembly, method))
					{
						continue;
					}
					var options = new MethodInstrumenter.Options
					{
						Detail = 0,
						Every = preset?.Every ?? 1,
						PerFrame = preset?.PerFrame ?? 32,
						AnyThread = preset?.AnyThread ?? false,
					};
					runtime = runtime ?? Runtime(module);
					var name = method.DeclaringType.FullName.Replace('/', '.') + "." + method.Name;
					var machine = AsyncMachine(method);
					if (machine == null)
					{
						MethodInstrumenter.Wrap(method, runtime, assembly, name, options, "EnterWatched");
						changed = true;
					}
					else if (MethodInstrumenter.WrapAsync(method, machine, runtime, assembly, name, options, "EnterWatchedAsync", out var problem))
					{
						changed = true;
					}
					else
					{
						var point = FirstPoint(MethodInstrumenter.MoveNext(machine));
						diagnostics.Add(TelemetryDiagnostic.Warning(point?.Document.Url, point?.StartLine ?? 0, point?.StartColumn ?? 0, "The watch left this async method as written, because " + problem + ": " + method.FullName));
					}
				}
			}
			return changed;
		}

		static bool Selected(IReadOnlyList<ITelemetryMethodSelector> selectors, string assembly, MethodDefinition method)
		{
			if (selectors.Count == 0)
			{
				return false;
			}
			var candidate = new TelemetryMethodCandidate(assembly, method);
			return selectors.Any(selector => selector.Select(candidate));
		}

		/// <summary>The reference of the module to the runtime, which it is given when it has none.</summary>
		static IMetadataScope Runtime(ModuleDefinition module)
		{
			var existing = module.AssemblyReferences.FirstOrDefault(reference => reference.Name == RuntimeAssembly);
			if (existing != null)
			{
				return existing;
			}
			var added = new AssemblyNameReference(RuntimeAssembly, new Version(0, 0, 0, 0));
			module.AssemblyReferences.Add(added);
			return added;
		}

		static bool IsCompilerGenerated(TypeDefinition type)
		{
			return type.CustomAttributes.Any(attribute => attribute.AttributeType.FullName == "System.Runtime.CompilerServices.CompilerGeneratedAttribute");
		}

		static SequencePoint FirstPoint(MethodDefinition method)
		{
			return method != null && method.DebugInformation.HasSequencePoints ? method.DebugInformation.SequencePoints.FirstOrDefault(sequence => !sequence.IsHidden) : null;
		}

		static CustomAttribute MethodAttribute(ICustomAttributeProvider provider)
		{
			return provider.CustomAttributes.FirstOrDefault(attribute => attribute.AttributeType.FullName == MethodAttributeType);
		}

		/// <summary>An iterator, async or not: its body only makes the state machine, and nothing marks when it is done.</summary>
		static bool IsIterator(MethodDefinition method)
		{
			return method.CustomAttributes.Any(attribute =>
				attribute.AttributeType.FullName == "System.Runtime.CompilerServices.IteratorStateMachineAttribute"
				|| attribute.AttributeType.FullName == "System.Runtime.CompilerServices.AsyncIteratorStateMachineAttribute");
		}

		/// <summary>The state machine of an async method, or null for any other method.</summary>
		static TypeDefinition AsyncMachine(MethodDefinition method)
		{
			var attribute = method.CustomAttributes.FirstOrDefault(candidate => candidate.AttributeType.FullName == "System.Runtime.CompilerServices.AsyncStateMachineAttribute");
			return (attribute?.ConstructorArguments[0].Value as TypeReference)?.Resolve();
		}

		static bool IsCompilerGenerated(MethodDefinition method)
		{
			return method.CustomAttributes.Any(attribute => attribute.AttributeType.FullName == "System.Runtime.CompilerServices.CompilerGeneratedAttribute");
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
