using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

namespace GenericMemory.Editor
{
	/// <summary>Applies the named steps to every assembly of a Player build, as one program.</summary>
	static class GenericMemoryWeaver
	{
		public static string Run(string folder, string[] steps)
		{
			var log = new StringBuilder();
			using (var program = WholeProgram.Load(folder))
			{
				foreach (var step in steps)
				{
					switch (step)
					{
						case "hoist":
							new Hoister(program, log, false).Run();
							break;
						case "hoistall":
							new Hoister(program, log, true).Run();
							break;
						case "canon":
							new Canonicalizer(program, log).Run();
							break;
						case "comparers":
							new Canonicalizer(program, log, true).Run();
							break;
						case "async":
							new AsyncEraser(program, log).Run();
							break;
						case "unitask":
							// Names a build whose difference is in the source of UniTask, not in a rewrite.
							break;
						default:
							throw new ArgumentException("unknown step " + step);
					}
				}
				program.Save(log);
			}
			return log.ToString();
		}
	}

	/// <summary>Every assembly of the build, read into memory and resolved against each other only.</summary>
	sealed class WholeProgram : IAssemblyResolver
	{
		readonly Dictionary<string, AssemblyDefinition> _byName = new Dictionary<string, AssemblyDefinition>(StringComparer.OrdinalIgnoreCase);
		readonly Dictionary<AssemblyDefinition, string> _paths = new Dictionary<AssemblyDefinition, string>();
		readonly HashSet<AssemblyDefinition> _dirty = new HashSet<AssemblyDefinition>();
		readonly HashSet<MethodDefinition> _resized = new HashSet<MethodDefinition>();

		public IEnumerable<AssemblyDefinition> Assemblies => _paths.Keys;

		public static WholeProgram Load(string folder)
		{
			var program = new WholeProgram();
			foreach (var path in Directory.GetFiles(folder, "*.dll"))
			{
				var symbols = Path.ChangeExtension(path, ".pdb");
				var hasSymbols = File.Exists(symbols);
				try
				{
					var assembly = AssemblyDefinition.ReadAssembly(
						new MemoryStream(File.ReadAllBytes(path)),
						new ReaderParameters
						{
							AssemblyResolver = program,
							ReadingMode = ReadingMode.Deferred,
							ReadSymbols = hasSymbols,
							SymbolStream = hasSymbols ? new MemoryStream(File.ReadAllBytes(symbols)) : null,
							SymbolReaderProvider = hasSymbols ? new DefaultSymbolReaderProvider(false) : null,
						}
					);
					program._byName[assembly.Name.Name] = assembly;
					program._paths[assembly] = path;
				}
				catch (BadImageFormatException)
				{
					// A native library that sits among the managed ones.
				}
			}
			return program;
		}

		public void Touch(ModuleDefinition module)
		{
			_dirty.Add(module.Assembly);
		}

		/// <summary>
		/// Says that instructions were put into the body. A short branch reaches 127 bytes, and Cecil writes it as it
		/// is even when the target moved out of reach, so these bodies get their branches sized again before the write.
		/// </summary>
		public void Resized(MethodDefinition method)
		{
			_resized.Add(method);
			_dirty.Add(method.Module.Assembly);
		}

		public void Save(StringBuilder log)
		{
			foreach (var method in _resized)
			{
				method.Body.SimplifyMacros();
				method.Body.OptimizeMacros();
			}
			foreach (var assembly in _dirty)
			{
				var path = _paths[assembly];
				var symbols = Path.ChangeExtension(path, ".pdb");
				var written = assembly.MainModule.HasSymbols;
				using (var output = File.Create(path))
				using (var pdb = written ? File.Create(symbols) : null)
				{
					assembly.Write(output, new WriterParameters { WriteSymbols = written, SymbolStream = pdb, SymbolWriterProvider = written ? new PortablePdbWriterProvider() : null });
				}
				log.AppendLine("wrote " + Path.GetFileName(path));
			}
		}

		public AssemblyDefinition Resolve(AssemblyNameReference name)
		{
			if (_byName.TryGetValue(name.Name, out var assembly))
			{
				return assembly;
			}
			throw new AssemblyResolutionException(name);
		}

		public AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters parameters)
		{
			return Resolve(name);
		}

		public void Dispose()
		{
			foreach (var assembly in _paths.Keys)
			{
				assembly.Dispose();
			}
		}

		/// <summary>The methods that have IL, of every assembly.</summary>
		public IEnumerable<MethodDefinition> Bodies()
		{
			foreach (var assembly in _paths.Keys)
			{
				foreach (var type in assembly.MainModule.GetTypes())
				{
					foreach (var method in type.Methods)
					{
						if (method.HasBody)
						{
							yield return method;
						}
					}
				}
			}
		}

		public static string Key(MethodReference method)
		{
			return method.DeclaringType.GetElementType().FullName + "::" + method.Name;
		}
	}

	/// <summary>
	/// Moves the methods of generic classes that no slot holds into static generic methods of a class that is not generic,
	/// and points every caller in the program at them.
	///
	/// libil2cpp inflates every method a generic class still has, once for each instance of the class that is initialized
	/// (GenericClass::SetupMethods), whether the instance calls it or not. A generic method of a class that is not generic
	/// is inflated only when a caller that runs names it. So after the move an instance pays for the methods its slots need,
	/// and for the others only where they are used.
	/// </summary>
	sealed class Hoister
	{
		const string Suffix = "_Hoisted";

		readonly WholeProgram _program;
		readonly StringBuilder _log;
		readonly bool _valueTypes;
		readonly Dictionary<MethodDefinition, MethodDefinition> _moved = new Dictionary<MethodDefinition, MethodDefinition>();
		readonly Dictionary<TypeDefinition, TypeDefinition> _holders = new Dictionary<TypeDefinition, TypeDefinition>();
		readonly HashSet<string> _keys = new HashSet<string>();
		// The calls to candidates, found before anything moved: the instruction, its module, and the method it meant.
		readonly List<(Instruction Instruction, ModuleDefinition Module, MethodDefinition Target)> _sites = new List<(Instruction, ModuleDefinition, MethodDefinition)>();

		public Hoister(WholeProgram program, StringBuilder log, bool valueTypes)
		{
			_program = program;
			_log = log;
			_valueTypes = valueTypes;
		}

		public void Run()
		{
			var candidates = Plan();
			foreach (var method in candidates)
			{
				_keys.Add(WholeProgram.Key(method));
			}
			var pinned = Pinned(candidates);
			var types = new Dictionary<TypeDefinition, int[]>();
			foreach (var method in candidates)
			{
				if (!types.TryGetValue(method.DeclaringType, out var counts))
				{
					types[method.DeclaringType] = counts = new[] { method.DeclaringType.Methods.Count, 0 };
				}
				if (!pinned.Contains(method))
				{
					Move(method);
					counts[1]++;
				}
			}
			Redirect();
			Remove();
			_log.AppendLine("hoist: moved " + _moved.Count + " methods of " + types.Count(pair => pair.Value[1] > 0) + " generic classes, kept " + pinned.Count + " that are used as values");
			foreach (var pair in types.OrderByDescending(pair => pair.Value[1]).Take(40))
			{
				_log.AppendLine("  " + pair.Key.FullName + ": " + pair.Value[0] + " -> " + (pair.Value[0] - pair.Value[1]));
			}
		}

		HashSet<MethodDefinition> Plan()
		{
			var candidates = new HashSet<MethodDefinition>();
			foreach (var assembly in _program.Assemblies)
			{
				if (!InScope(assembly.Name.Name))
				{
					continue;
				}
				foreach (var type in assembly.MainModule.GetTypes())
				{
					if (!type.HasGenericParameters || type.IsInterface || (type.IsValueType && !_valueTypes) || type.Name.EndsWith(Suffix, StringComparison.Ordinal))
					{
						continue;
					}
					if (type.IsValueType && type.CustomAttributes.Any(attribute => attribute.AttributeType.Name == "IsByRefLikeAttribute"))
					{
						continue;
					}
					if (IsDelegateOrEngineObject(type))
					{
						continue;
					}
					// A type whose static constructor runs at a precise moment runs it when a static method is called.
					// A static method that moved out would no longer do that.
					var precise = !type.IsBeforeFieldInit && type.Methods.Any(method => method.IsConstructor && method.IsStatic);
					foreach (var method in type.Methods)
					{
						if (!method.HasBody || method.IsVirtual || method.IsConstructor || method.IsAbstract || method.IsPInvokeImpl || method.IsInternalCall || method.IsRuntime)
						{
							continue;
						}
						if (method.Name == "Finalize" || (method.IsStatic && precise))
						{
							continue;
						}
						if (method.CustomAttributes.Any(attribute => attribute.AttributeType.Name.Contains("Preserve") || attribute.AttributeType.Namespace == "System.Runtime.Serialization"))
						{
							continue;
						}
						candidates.Add(method);
					}
				}
			}
			return candidates;
		}

		// The engine's own assemblies are left as they are: the native side finds some of their members by name.
		static bool InScope(string assembly)
		{
			return !assembly.StartsWith("UnityEngine", StringComparison.Ordinal)
				&& !assembly.StartsWith("Unity.", StringComparison.Ordinal)
				&& !assembly.StartsWith("UnityEditor", StringComparison.Ordinal)
				&& !assembly.StartsWith("Newtonsoft", StringComparison.Ordinal)
				&& !assembly.StartsWith("Mono.", StringComparison.Ordinal)
				&& !assembly.StartsWith("nunit", StringComparison.Ordinal);
		}

		static bool IsDelegateOrEngineObject(TypeDefinition type)
		{
			for (var at = type; at != null; )
			{
				var parent = at.BaseType;
				if (parent == null)
				{
					return false;
				}
				var name = parent.GetElementType().FullName;
				if (name == "System.MulticastDelegate" || name == "System.Delegate" || name == "UnityEngine.Object")
				{
					return true;
				}
				at = parent.Resolve();
			}
			// The chain could not be followed, so nothing is known about the type.
			return true;
		}

		/// <summary>
		/// The candidates that have to stay methods of their class: taken as a function pointer or a token, called through
		/// a constrained prefix, or named without the instance of the class. The plain calls are remembered as sites.
		/// </summary>
		HashSet<MethodDefinition> Pinned(HashSet<MethodDefinition> candidates)
		{
			var pinned = new HashSet<MethodDefinition>();
			foreach (var method in _program.Bodies())
			{
				foreach (var instruction in method.Body.Instructions)
				{
					if (!(instruction.Operand is MethodReference reference) || !_keys.Contains(WholeProgram.Key(reference)))
					{
						continue;
					}
					var definition = reference.Resolve();
					if (definition == null || !candidates.Contains(definition))
					{
						continue;
					}
					var call = instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt;
					var constrained = instruction.Previous != null && instruction.Previous.OpCode == OpCodes.Constrained;
					if (call && !constrained && reference.DeclaringType is GenericInstanceType)
					{
						_sites.Add((instruction, method.Module, definition));
					}
					else
					{
						pinned.Add(definition);
					}
				}
			}
			return pinned;
		}

		void Move(MethodDefinition old)
		{
			var type = old.DeclaringType;
			var module = type.Module;
			var created = new MethodDefinition(old.Name, MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig, module.TypeSystem.Void)
			{
				ImplAttributes = old.ImplAttributes,
			};
			Holder(type).Methods.Add(created);
			var arity = type.GenericParameters.Count;
			foreach (var parameter in type.GenericParameters)
			{
				created.GenericParameters.Add(new GenericParameter(parameter.Name, created) { Attributes = parameter.Attributes & ~GenericParameterAttributes.VarianceMask });
			}
			foreach (var parameter in old.GenericParameters)
			{
				created.GenericParameters.Add(new GenericParameter(parameter.Name, created) { Attributes = parameter.Attributes });
			}
			var context = new GenericContext(created, arity);
			for (var at = 0; at < created.GenericParameters.Count; at++)
			{
				var source = at < arity ? type.GenericParameters[at] : old.GenericParameters[at - arity];
				foreach (var constraint in source.Constraints)
				{
					created.GenericParameters[at].Constraints.Add(new GenericParameterConstraint(context.Type(constraint.ConstraintType)));
				}
			}
			created.ReturnType = context.Type(old.ReturnType);

			ParameterDefinition self = null;
			if (!old.IsStatic)
			{
				var instance = new GenericInstanceType(type);
				for (var at = 0; at < arity; at++)
				{
					instance.GenericArguments.Add(created.GenericParameters[at]);
				}
				self = new ParameterDefinition("self", ParameterAttributes.None, type.IsValueType ? (TypeReference)new ByReferenceType(instance) : instance);
				created.Parameters.Add(self);
			}
			var parameters = old.Parameters.ToArray();
			old.Parameters.Clear();
			foreach (var parameter in parameters)
			{
				parameter.ParameterType = context.Type(parameter.ParameterType);
				created.Parameters.Add(parameter);
			}

			var from = old.Body;
			var thisParameter = from.ThisParameter;
			var to = created.Body;
			to.InitLocals = from.InitLocals;
			to.MaxStackSize = from.MaxStackSize;
			foreach (var variable in from.Variables.ToArray())
			{
				variable.VariableType = context.Type(variable.VariableType);
				to.Variables.Add(variable);
			}
			foreach (var instruction in from.Instructions.ToArray())
			{
				if (ReferenceEquals(instruction.Operand, thisParameter))
				{
					instruction.Operand = self;
				}
				else
				{
					instruction.Operand = context.Operand(instruction.Operand);
				}
				to.Instructions.Add(instruction);
			}
			foreach (var handler in from.ExceptionHandlers.ToArray())
			{
				if (handler.CatchType != null)
				{
					handler.CatchType = context.Type(handler.CatchType);
				}
				to.ExceptionHandlers.Add(handler);
			}
			foreach (var attribute in old.CustomAttributes)
			{
				created.CustomAttributes.Add(attribute);
			}
			old.Body = new MethodBody(old);
			_moved[old] = created;
			_program.Touch(module);
		}

		TypeDefinition Holder(TypeDefinition type)
		{
			if (!_holders.TryGetValue(type, out var holder))
			{
				var outer = type;
				var name = type.Name;
				while (outer.DeclaringType != null)
				{
					outer = outer.DeclaringType;
					name = outer.Name + "_" + name;
				}
				holder = new TypeDefinition(
					outer.Namespace,
					name.Replace('`', '_').Replace('<', '_').Replace('>', '_') + Suffix,
					TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
					type.Module.TypeSystem.Object
				);
				type.Module.Types.Add(holder);
				_holders[type] = holder;
			}
			return holder;
		}

		void Redirect()
		{
			foreach (var site in _sites)
			{
				if (!_moved.TryGetValue(site.Target, out var created))
				{
					continue;
				}
				// The operand may have been rewritten when its body moved; its arguments are read as they are now.
				var reference = (MethodReference)site.Instruction.Operand;
				var target = new GenericInstanceMethod(site.Module.ImportReference(created));
				foreach (var argument in ((GenericInstanceType)reference.DeclaringType).GenericArguments)
				{
					target.GenericArguments.Add(argument);
				}
				if (reference is GenericInstanceMethod generic)
				{
					foreach (var argument in generic.GenericArguments)
					{
						target.GenericArguments.Add(argument);
					}
				}
				site.Instruction.OpCode = OpCodes.Call;
				site.Instruction.Operand = target;
				_program.Touch(site.Module);
			}
		}

		void Remove()
		{
			foreach (var type in _moved.Keys.Select(method => method.DeclaringType).Distinct().ToArray())
			{
				foreach (var property in type.Properties.ToArray())
				{
					if (property.GetMethod != null && _moved.ContainsKey(property.GetMethod))
					{
						property.GetMethod = null;
					}
					if (property.SetMethod != null && _moved.ContainsKey(property.SetMethod))
					{
						property.SetMethod = null;
					}
					if (property.GetMethod == null && property.SetMethod == null)
					{
						type.Properties.Remove(property);
					}
				}
				foreach (var declared in type.Events.ToArray())
				{
					if ((declared.AddMethod != null && _moved.ContainsKey(declared.AddMethod)) || (declared.RemoveMethod != null && _moved.ContainsKey(declared.RemoveMethod)))
					{
						type.Events.Remove(declared);
					}
				}
			}
			foreach (var old in _moved.Keys)
			{
				old.DeclaringType.Methods.Remove(old);
			}
		}
	}

	/// <summary>
	/// Rewrites the types a method body names when the body moves from a method of a generic class to a generic method:
	/// the parameters of the class become the first parameters of the method, and the method's own follow them.
	/// Only what is written in the context of the body is rewritten; the signature of a member that the body names is in
	/// the context of that member and stays.
	/// </summary>
	sealed class GenericContext
	{
		readonly MethodDefinition _method;
		readonly int _typeArity;

		public GenericContext(MethodDefinition method, int typeArity)
		{
			_method = method;
			_typeArity = typeArity;
		}

		public object Operand(object operand)
		{
			switch (operand)
			{
				case TypeReference type:
					return Type(type);
				case FieldReference field:
					return Field(field);
				case MethodReference method:
					return Method(method);
				case CallSite site:
				{
					var copy = new CallSite(Type(site.ReturnType)) { HasThis = site.HasThis, ExplicitThis = site.ExplicitThis, CallingConvention = site.CallingConvention };
					foreach (var parameter in site.Parameters)
					{
						copy.Parameters.Add(new ParameterDefinition(Type(parameter.ParameterType)));
					}
					return copy;
				}
				default:
					return operand;
			}
		}

		FieldReference Field(FieldReference field)
		{
			var declaring = Type(field.DeclaringType);
			return ReferenceEquals(declaring, field.DeclaringType) ? field : new FieldReference(field.Name, field.FieldType, declaring);
		}

		MethodReference Method(MethodReference method)
		{
			if (method is GenericInstanceMethod generic)
			{
				var element = Method(generic.ElementMethod);
				var changed = !ReferenceEquals(element, generic.ElementMethod);
				var arguments = new TypeReference[generic.GenericArguments.Count];
				for (var at = 0; at < arguments.Length; at++)
				{
					arguments[at] = Type(generic.GenericArguments[at]);
					changed |= !ReferenceEquals(arguments[at], generic.GenericArguments[at]);
				}
				if (!changed)
				{
					return method;
				}
				var instance = new GenericInstanceMethod(element);
				foreach (var argument in arguments)
				{
					instance.GenericArguments.Add(argument);
				}
				return instance;
			}
			var declaring = Type(method.DeclaringType);
			// The accessors of an array have no definition; their signature is written in the context of the body.
			var contextual = method.DeclaringType is ArrayType;
			if (ReferenceEquals(declaring, method.DeclaringType) && !contextual)
			{
				return method;
			}
			var copy = new MethodReference(method.Name, contextual ? Type(method.ReturnType) : method.ReturnType, declaring)
			{
				HasThis = method.HasThis,
				ExplicitThis = method.ExplicitThis,
				CallingConvention = method.CallingConvention,
			};
			foreach (var parameter in method.Parameters)
			{
				copy.Parameters.Add(new ParameterDefinition(contextual ? Type(parameter.ParameterType) : parameter.ParameterType));
			}
			foreach (var parameter in method.GenericParameters)
			{
				copy.GenericParameters.Add(new GenericParameter(parameter.Name, copy));
			}
			return copy;
		}

		public TypeReference Type(TypeReference type)
		{
			switch (type)
			{
				case null:
					return null;
				case GenericParameter parameter:
					return _method.GenericParameters[parameter.Type == GenericParameterType.Type ? parameter.Position : _typeArity + parameter.Position];
				case GenericInstanceType instance:
				{
					var changed = false;
					var arguments = new TypeReference[instance.GenericArguments.Count];
					for (var at = 0; at < arguments.Length; at++)
					{
						arguments[at] = Type(instance.GenericArguments[at]);
						changed |= !ReferenceEquals(arguments[at], instance.GenericArguments[at]);
					}
					if (!changed)
					{
						return type;
					}
					var copy = new GenericInstanceType(instance.ElementType);
					foreach (var argument in arguments)
					{
						copy.GenericArguments.Add(argument);
					}
					return copy;
				}
				case ArrayType array:
				{
					var element = Type(array.ElementType);
					if (ReferenceEquals(element, array.ElementType))
					{
						return type;
					}
					var copy = new ArrayType(element, array.Rank);
					if (!array.IsVector)
					{
						for (var at = 0; at < array.Rank; at++)
						{
							copy.Dimensions[at] = array.Dimensions[at];
						}
					}
					return copy;
				}
				case ByReferenceType reference:
				{
					var element = Type(reference.ElementType);
					return ReferenceEquals(element, reference.ElementType) ? type : new ByReferenceType(element);
				}
				case PointerType pointer:
				{
					var element = Type(pointer.ElementType);
					return ReferenceEquals(element, pointer.ElementType) ? type : new PointerType(element);
				}
				case PinnedType pinned:
				{
					var element = Type(pinned.ElementType);
					return ReferenceEquals(element, pinned.ElementType) ? type : new PinnedType(element);
				}
				case RequiredModifierType required:
				{
					var element = Type(required.ElementType);
					return ReferenceEquals(element, required.ElementType) ? type : new RequiredModifierType(required.ModifierType, element);
				}
				case OptionalModifierType optional:
				{
					var element = Type(optional.ElementType);
					return ReferenceEquals(element, optional.ElementType) ? type : new OptionalModifierType(optional.ModifierType, element);
				}
				default:
					return type;
			}
		}
	}
}
