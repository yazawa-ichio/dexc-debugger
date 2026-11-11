using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

// Counts the instances of the collection types that could be replaced by the instance over object, and what keeps
// the others. The metadata of an instance is paid once for the whole program, so an instance is worth erasing only
// when every use of it can go: one use that has to stay keeps the instance.
// usage: Census --erasable <ManagedStripped folder> [assembly name prefix to leave out]...
static class Erasable
{
	[Flags]
	enum Use
	{
		None = 0,
		Local = 1 << 0,
		PrivateSignature = 1 << 1,
		PublicSignature = 1 << 2,
		Field = 1 << 3,
		SerializedField = 1 << 4,
		TypeTest = 1 << 5,
		ArgumentOfOther = 1 << 6,
		GenericMethodArgument = 1 << 7,
		BoundarySignature = 1 << 8,
		Inherited = 1 << 9,
		Boundary = 1 << 10,
		Member = 1 << 11,
		MaybeInterface = 1 << 12,
		PrivateField = 1 << 13,
		InternalSignature = 1 << 14,
		Linq = 1 << 15,
		CarrierArgument = 1 << 16,
	}

	sealed class Instance
	{
		public string Key;
		public Use Uses;
		public readonly HashSet<string> Assemblies = new HashSet<string>();
		public readonly HashSet<string> Outers = new HashSet<string>();
		public readonly HashSet<string> Methods = new HashSet<string>();
		public bool Comparable;
	}

	static readonly HashSet<string> s_targets = new HashSet<string>
	{
		"System.Collections.Generic.List`1",
		"System.Collections.Generic.Dictionary`2",
		"System.Collections.Generic.HashSet`1",
		"System.Collections.Generic.Queue`1",
		"System.Collections.Generic.Stack`1",
	};

	// Generic types that only carry their argument: an instance of a target inside one of these is erased along with it.
	static readonly HashSet<string> s_carriers = new HashSet<string>
	{
		"System.Collections.Generic.List`1/Enumerator",
		"System.Collections.Generic.Dictionary`2/Enumerator",
		"System.Collections.Generic.Dictionary`2/KeyCollection",
		"System.Collections.Generic.Dictionary`2/ValueCollection",
		"System.Collections.Generic.Dictionary`2/KeyCollection/Enumerator",
		"System.Collections.Generic.Dictionary`2/ValueCollection/Enumerator",
		"System.Collections.Generic.HashSet`1/Enumerator",
		"System.Collections.Generic.Queue`1/Enumerator",
		"System.Collections.Generic.Stack`1/Enumerator",
		"System.Collections.Generic.KeyValuePair`2",
	};

	static readonly HashSet<string> s_interfaces = new HashSet<string>
	{
		"System.Collections.Generic.IEnumerable`1",
		"System.Collections.Generic.ICollection`1",
		"System.Collections.Generic.IList`1",
		"System.Collections.Generic.IReadOnlyList`1",
		"System.Collections.Generic.IReadOnlyCollection`1",
		"System.Collections.Generic.IDictionary`2",
		"System.Collections.Generic.IReadOnlyDictionary`2",
		"System.Collections.Generic.ISet`1",
	};

	static readonly Dictionary<string, Instance> s_instances = new Dictionary<string, Instance>();
	static readonly HashSet<string> s_valueOnlyKeys = new HashSet<string>();
	static readonly HashSet<string> s_openKeys = new HashSet<string>();

	public static int Run(string folder, string[] leftOut)
	{
		var resolver = new DefaultAssemblyResolver();
		resolver.AddSearchDirectory(folder);
		var assemblies = new List<AssemblyDefinition>();
		foreach (var path in Directory.GetFiles(folder, "*.dll"))
		{
			try
			{
				assemblies.Add(AssemblyDefinition.ReadAssembly(path, new ReaderParameters { AssemblyResolver = resolver }));
			}
			catch (BadImageFormatException)
			{
			}
		}
		foreach (var assembly in assemblies)
		{
			var name = assembly.Name.Name;
			if (leftOut.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
			{
				continue;
			}
			var boundary = IsBoundary(name);
			foreach (var type in assembly.MainModule.GetTypes())
			{
				Scan(type, name, boundary);
			}
		}

		var all = s_instances.Values.ToList();
		Console.WriteLine($"instances of List/Dictionary/HashSet/Queue/Stack with a reference argument: {all.Count}");
		Console.WriteLine($"  (only value arguments: {s_valueOnlyKeys.Count}, over a type parameter: {s_openKeys.Count})");
		var groups = new (string Name, Func<Instance, bool> Test)[]
		{
			("used by the BCL or the engine itself", instance => instance.Uses.HasFlag(Use.Boundary)),
			("serialized field", instance => instance.Uses.HasFlag(Use.SerializedField)),
			("typeof / is / as", instance => instance.Uses.HasFlag(Use.TypeTest)),
			("base type or interface of a type", instance => instance.Uses.HasFlag(Use.Inherited)),
			("in the signature of a BCL or engine member that is called (not LINQ)", instance => instance.Uses.HasFlag(Use.BoundarySignature)),
			("argument of a generic method or type that is not a delegate, a collection interface or LINQ", instance => (instance.Uses & (Use.GenericMethodArgument | Use.ArgumentOfOther)) != 0),
			("public signature, or a field that is not plainly private", instance => (instance.Uses & (Use.Field | Use.PublicSignature)) != 0),
			("internal signature", instance => instance.Uses.HasFlag(Use.InternalSignature)),
			("private field (and maybe LINQ, delegates, interfaces)", instance => instance.Uses.HasFlag(Use.PrivateField)),
			("LINQ, a delegate or a collection interface over it", instance => (instance.Uses & (Use.Linq | Use.CarrierArgument)) != 0),
			("free: locals, private signatures, compiler-generated types", instance => true),
		};
		var rest = all;
		foreach (var group in groups)
		{
			var taken = rest.Where(group.Test).ToList();
			rest = rest.Where(instance => !group.Test(instance)).ToList();
			var maybe = taken.Count(instance => instance.Uses.HasFlag(Use.MaybeInterface));
			var comparable = taken.Count(instance => instance.Comparable);
			Console.WriteLine($"{taken.Count,6}  {group.Name}   (may flow to a generic interface: {maybe}, argument has its own IEquatable/IComparable: {comparable})");
			foreach (var instance in taken.OrderBy(instance => instance.Key, StringComparer.Ordinal).Take(6))
			{
				var detail = instance.Outers.Count > 0 ? "  in " + string.Join(", ", instance.Outers.Take(3)) : instance.Methods.Count > 0 ? "  to " + string.Join(", ", instance.Methods.Take(2)) : "";
				Console.WriteLine($"          {instance.Key}  [{string.Join(",", instance.Assemblies.Take(3))}]{detail}");
			}
		}

		Console.WriteLine();
		Console.WriteLine("what the instances that stay are arguments of:");
		foreach (var outer in all.Where(instance => instance.Uses.HasFlag(Use.ArgumentOfOther)).SelectMany(instance => instance.Outers).GroupBy(outer => outer).OrderByDescending(group => group.Count()).Take(15))
		{
			Console.WriteLine($"{outer.Count(),6}  {outer.Key}");
		}
		Console.WriteLine("which generic methods they are passed to:");
		foreach (var method in all.Where(instance => instance.Uses.HasFlag(Use.GenericMethodArgument)).SelectMany(instance => instance.Methods).GroupBy(method => method).OrderByDescending(group => group.Count()).Take(15))
		{
			Console.WriteLine($"{method.Count(),6}  {method.Key}");
		}
		return 0;
	}

	static bool IsBoundary(string assembly)
	{
		return assembly == "mscorlib" || assembly == "netstandard" || assembly == "System" || assembly.StartsWith("System.", StringComparison.Ordinal)
			|| assembly == "UnityEngine" || assembly.StartsWith("UnityEngine.", StringComparison.Ordinal) || assembly.StartsWith("Mono.", StringComparison.Ordinal);
	}

	static void Scan(TypeDefinition type, string assembly, bool boundary)
	{
		var generated = type.FullName.Contains("<");
		var visible = type.IsPublic || type.IsNestedPublic || type.IsNestedFamily;
		Use Kind(Use use) => boundary ? Use.Boundary : use;

		if (type.BaseType != null)
		{
			Visit(type.BaseType, null, Kind(Use.Inherited), assembly, null);
		}
		foreach (var implemented in type.Interfaces)
		{
			Visit(implemented.InterfaceType, null, Kind(Use.Inherited), assembly, null);
		}
		foreach (var field in type.Fields)
		{
			var use = generated ? Use.PrivateSignature : IsSerialized(type, field) ? Use.SerializedField : IsPlainPrivate(type, field) ? Use.PrivateField : Use.Field;
			Visit(field.FieldType, null, Kind(use), assembly, null);
		}
		foreach (var method in type.Methods)
		{
			var signature = generated || method.IsPrivate ? Use.PrivateSignature : visible && (method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly) ? Use.PublicSignature : Use.InternalSignature;
			Visit(method.ReturnType, null, Kind(signature), assembly, null);
			foreach (var parameter in method.Parameters)
			{
				Visit(parameter.ParameterType, null, Kind(signature), assembly, null);
			}
			if (!method.HasBody)
			{
				continue;
			}
			var held = new HashSet<string>();
			var interfaces = new HashSet<string>();
			void Hold(TypeReference reference)
			{
				foreach (var key in Visit(reference, null, Use.None, assembly, null))
				{
					held.Add(key);
				}
			}
			Hold(method.ReturnType);
			NoteInterface(method.ReturnType, null, interfaces);
			foreach (var parameter in method.Parameters)
			{
				Hold(parameter.ParameterType);
			}
			foreach (var variable in method.Body.Variables)
			{
				Visit(variable.VariableType, null, Kind(Use.Local), assembly, null);
				Hold(variable.VariableType);
			}
			foreach (var instruction in method.Body.Instructions)
			{
				switch (instruction.Operand)
				{
					case TypeReference operand:
					{
						var test = instruction.OpCode == OpCodes.Ldtoken || instruction.OpCode == OpCodes.Castclass || instruction.OpCode == OpCodes.Isinst || instruction.OpCode == OpCodes.Unbox_Any;
						Visit(operand, null, Kind(test ? Use.TypeTest : Use.Local), assembly, null);
						break;
					}
					case FieldReference operand:
						Visit(operand.DeclaringType, null, Kind(Use.Member), assembly, null);
						Hold(operand.DeclaringType);
						if (instruction.OpCode == OpCodes.Stfld || instruction.OpCode == OpCodes.Stsfld)
						{
							NoteInterface(operand.FieldType, Map(operand.DeclaringType, null), interfaces);
						}
						break;
					case MethodReference operand:
					{
						Visit(operand.DeclaringType, null, Kind(Use.Member), assembly, null);
						Hold(operand.DeclaringType);
						var instance = operand as GenericInstanceMethod;
						var declaring = operand.DeclaringType.GetElementType().FullName;
						var name = declaring + "::" + operand.Name;
						var linq = declaring == "System.Linq.Enumerable";
						if (instance != null)
						{
							foreach (var argument in instance.GenericArguments)
							{
								Visit(argument, null, Kind(linq ? Use.Linq : Use.GenericMethodArgument), assembly, name);
							}
						}
						var map = Map(operand.DeclaringType, instance);
						var target = s_targets.Contains(declaring) || s_carriers.Contains(declaring);
						var outside = !target && IsBoundary(Scope(operand.DeclaringType));
						if (outside && !boundary)
						{
							Visit(operand.ReturnType, map, linq ? Use.Linq : Use.BoundarySignature, assembly, name);
						}
						foreach (var parameter in operand.Parameters)
						{
							if (outside && !boundary)
							{
								Visit(parameter.ParameterType, map, linq ? Use.Linq : Use.BoundarySignature, assembly, name);
							}
							if (!target)
							{
								NoteInterface(parameter.ParameterType, map, interfaces);
							}
						}
						break;
					}
				}
			}
			if (boundary)
			{
				continue;
			}
			// A collection and a generic interface over the same arguments in one method: the collection may be handed over as that interface.
			foreach (var key in held)
			{
				var arguments = key.Substring(key.IndexOf('<'));
				if (interfaces.Contains(arguments) && s_instances.TryGetValue(key, out var instance))
				{
					instance.Uses |= Use.MaybeInterface;
				}
			}
		}
	}

	static string Scope(TypeReference type)
	{
		var resolved = type.Resolve();
		if (resolved != null)
		{
			return resolved.Module.Assembly.Name.Name;
		}
		return type.GetElementType().Scope.Name;
	}

	static Func<GenericParameter, TypeReference> Map(TypeReference declaring, GenericInstanceMethod method)
	{
		var instance = declaring as GenericInstanceType;
		if (instance == null && method == null)
		{
			return null;
		}
		return parameter =>
		{
			if (parameter.Type == GenericParameterType.Method)
			{
				return method != null && parameter.Position < method.GenericArguments.Count ? method.GenericArguments[parameter.Position] : parameter;
			}
			return instance != null && parameter.Position < instance.GenericArguments.Count ? instance.GenericArguments[parameter.Position] : parameter;
		};
	}

	// The arguments of every generic collection interface in a type, as "<A,B>", to match against the key of an instance.
	static void NoteInterface(TypeReference type, Func<GenericParameter, TypeReference> map, HashSet<string> interfaces)
	{
		type = Resolve(type, ref map);
		if (type is GenericInstanceType instance)
		{
			if (s_interfaces.Contains(instance.ElementType.FullName))
			{
				interfaces.Add("<" + string.Join(",", instance.GenericArguments.Select(argument => Key(argument, map))) + ">");
			}
			foreach (var argument in instance.GenericArguments)
			{
				NoteInterface(argument, map, interfaces);
			}
		}
		else if (type is TypeSpecification specification)
		{
			NoteInterface(specification.ElementType, map, interfaces);
		}
	}

	// What a type parameter stands for. What it stands for is already in the caller's terms, so the map is dropped for it.
	static TypeReference Resolve(TypeReference type, ref Func<GenericParameter, TypeReference> map)
	{
		if (type is GenericParameter parameter && map != null)
		{
			type = map(parameter);
			map = null;
		}
		return type;
	}

	static string Key(TypeReference type, Func<GenericParameter, TypeReference> map)
	{
		type = Resolve(type, ref map);
		switch (type)
		{
			case GenericInstanceType instance:
				return instance.ElementType.FullName + "<" + string.Join(",", instance.GenericArguments.Select(argument => Key(argument, map))) + ">";
			case ArrayType array:
				return Key(array.ElementType, map) + "[]";
			case TypeSpecification specification:
				return Key(specification.ElementType, map);
			default:
				return type.FullName;
		}
	}

	static bool IsOpen(TypeReference type, Func<GenericParameter, TypeReference> map)
	{
		type = Resolve(type, ref map);
		switch (type)
		{
			case GenericParameter _:
				return true;
			case GenericInstanceType instance:
				return instance.GenericArguments.Any(argument => IsOpen(argument, map));
			case TypeSpecification specification:
				return IsOpen(specification.ElementType, map);
			default:
				return false;
		}
	}

	// Records every instance of a target inside the type, and returns their keys.
	static List<string> Visit(TypeReference type, Func<GenericParameter, TypeReference> map, Use use, string assembly, string method, GenericInstanceType outer = null, List<string> found = null)
	{
		found = found ?? new List<string>();
		type = Resolve(type, ref map);
		if (type is GenericInstanceType instance)
		{
			var definition = instance.ElementType.FullName;
			if (s_targets.Contains(definition))
			{
				var key = Key(instance, map);
				var arguments = instance.GenericArguments.Select(argument => { var inner = map; return Resolve(argument, ref inner); }).ToList();
				if (IsOpen(instance, map))
				{
					s_openKeys.Add(key);
				}
				else if (arguments.All(argument => argument.IsValueType))
				{
					s_valueOnlyKeys.Add(key);
				}
				else
				{
					found.Add(key);
					if (use != Use.None)
					{
						if (!s_instances.TryGetValue(key, out var record))
						{
							record = new Instance { Key = key, Comparable = arguments.Any(HasOwnComparison) };
							s_instances.Add(key, record);
						}
						record.Uses |= use;
						record.Assemblies.Add(assembly);
						if (method != null)
						{
							record.Methods.Add(method);
						}
						if (outer != null)
						{
							var outerName = outer.ElementType.FullName;
							if (!s_targets.Contains(outerName) && !s_carriers.Contains(outerName) && use != Use.Boundary)
							{
								record.Uses |= IsCarrierLike(outerName) ? Use.CarrierArgument : Use.ArgumentOfOther;
								record.Outers.Add(outerName);
							}
						}
					}
				}
			}
			foreach (var argument in instance.GenericArguments)
			{
				Visit(argument, map, use == Use.GenericMethodArgument ? Use.GenericMethodArgument : use, assembly, method, instance, found);
			}
		}
		else if (type is TypeSpecification specification)
		{
			Visit(specification.ElementType, map, use, assembly, method, outer, found);
		}
		return found;
	}

	// Whether the default comparer of the type would call something other than Object.Equals or IComparable.
	static bool HasOwnComparison(TypeReference type)
	{
		if (type.IsValueType || type is ArrayType || type is GenericParameter)
		{
			return false;
		}
		var definition = type.Resolve();
		if (definition == null)
		{
			return false;
		}
		return definition.Interfaces.Any(implemented => implemented.InterfaceType.GetElementType().FullName == "System.IEquatable`1" || implemented.InterfaceType.GetElementType().FullName == "System.IComparable`1");
	}

	// A delegate or a collection interface: it only passes its argument along, so a rewrite can change it together with the collection.
	static bool IsCarrierLike(string name)
	{
		return s_interfaces.Contains(name) || name.StartsWith("System.Func`", StringComparison.Ordinal) || name.StartsWith("System.Action`", StringComparison.Ordinal)
			|| name == "System.Predicate`1" || name == "System.Comparison`1" || name == "System.Collections.Generic.IEnumerator`1";
	}

	// A private field nothing but the code of its own type reads: no attribute on it, and its type is not marked for a serializer.
	static bool IsPlainPrivate(TypeDefinition type, FieldDefinition field)
	{
		if (!field.IsPrivate || type.IsSerializable || field.Name.Contains("<"))
		{
			return false;
		}
		return !field.CustomAttributes.Any(attribute => attribute.AttributeType.Name != "CompilerGeneratedAttribute" && attribute.AttributeType.Name != "DebuggerBrowsableAttribute" && attribute.AttributeType.Name != "NonSerializedAttribute")
			&& !type.CustomAttributes.Any(attribute => attribute.AttributeType.Name.Contains("Contract") || attribute.AttributeType.Name.Contains("MessagePack") || attribute.AttributeType.Name.Contains("Json"));
	}

	static bool IsSerialized(TypeDefinition type, FieldDefinition field)
	{
		if (field.IsStatic || field.IsNotSerialized)
		{
			return false;
		}
		if (!field.IsPublic && !field.CustomAttributes.Any(attribute => attribute.AttributeType.Name == "SerializeField" || attribute.AttributeType.Name == "SerializeReference"))
		{
			return false;
		}
		if (type.IsSerializable)
		{
			return true;
		}
		for (var current = type; current != null; current = current.BaseType?.Resolve())
		{
			if (current.FullName == "UnityEngine.Object")
			{
				return true;
			}
		}
		return false;
	}
}
