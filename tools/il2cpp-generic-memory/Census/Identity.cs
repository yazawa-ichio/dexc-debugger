using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

// Finds where the code of a generic definition looks at what its type argument is, instead of only passing values of
// it along. A definition that never looks can be given one instance for every argument of the same shape without
// anything being able to tell. What a method reaches through the generic code it calls counts as its own.
// usage: Census --identity <ManagedStripped folder> [type full name prefix]...
static class Identity
{
	// typeof     ldtoken of T or of a type made from T
	// T[]        an array of T is made: the array says what T is to whoever gets it
	// G<T>[]     an array of a type made from T is made (the entries of a dictionary): internal as long as it stays inside
	// cast       a value is checked to be a T
	// new-kept   a List<T> is made: a type that has to stay what it is
	// static     a static field of a type made from T is read or written: one for each T
	// comparer   the default comparer of T is asked for: it depends on what T implements
	// create     a T is created
	// call-on-T  a method is called on a T through a constraint: for a value type that is the method of that very type
	static readonly string[] s_kinds = { "typeof", "T[]", "G<T>[]", "cast", "new-kept", "static", "comparer", "create", "call-on-T" };

	sealed class Facts
	{
		public readonly HashSet<string> Direct = new HashSet<string>();
		public readonly HashSet<string> All = new HashSet<string>();
		public readonly List<MethodDefinition> Callees = new List<MethodDefinition>();
	}

	public static int Run(string folder, string[] prefixes)
	{
		var resolver = new DefaultAssemblyResolver();
		resolver.AddSearchDirectory(folder);
		var facts = new Dictionary<MethodDefinition, Facts>();
		var types = new List<TypeDefinition>();
		foreach (var path in Directory.GetFiles(folder, "*.dll"))
		{
			AssemblyDefinition assembly;
			try
			{
				assembly = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { AssemblyResolver = resolver });
			}
			catch (BadImageFormatException)
			{
				continue;
			}
			foreach (var type in assembly.MainModule.GetTypes())
			{
				types.Add(type);
				foreach (var method in type.Methods.Where(method => method.HasBody && (type.HasGenericParameters || method.HasGenericParameters)))
				{
					facts[method] = Scan(method);
				}
			}
		}
		foreach (var fact in facts.Values)
		{
			fact.All.UnionWith(fact.Direct);
		}
		// What a method reaches through the generic code it calls is added until nothing changes.
		for (var changed = true; changed;)
		{
			changed = false;
			foreach (var fact in facts.Values)
			{
				foreach (var callee in fact.Callees)
				{
					if (facts.TryGetValue(callee, out var reached))
					{
						var before = fact.All.Count;
						fact.All.UnionWith(reached.All);
						changed |= fact.All.Count != before;
					}
				}
			}
		}

		Console.WriteLine("meth clean  " + string.Join(" ", s_kinds.Select(kind => kind.PadLeft(9))) + "  definition   (methods that reach each, directly or through what they call)");
		foreach (var type in types.OrderBy(type => type.FullName, StringComparer.Ordinal))
		{
			if (prefixes.Length > 0 && !prefixes.Any(prefix => type.FullName.StartsWith(prefix, StringComparison.Ordinal)))
			{
				continue;
			}
			if (type.HasGenericParameters)
			{
				Print(type.FullName, type.Methods.ToList(), facts, prefixes.Length > 0);
			}
			else
			{
				foreach (var group in type.Methods.Where(method => method.HasGenericParameters).GroupBy(method => method.Name).OrderBy(group => group.Key, StringComparer.Ordinal))
				{
					Print(type.FullName + "::" + group.Key, group.ToList(), facts, false);
				}
			}
		}
		return 0;
	}

	static void Print(string name, List<MethodDefinition> methods, Dictionary<MethodDefinition, Facts> facts, bool detail)
	{
		var where = s_kinds.ToDictionary(kind => kind, kind => methods.Where(method => facts.TryGetValue(method, out var fact) && fact.All.Contains(kind)).Select(method => method.Name).Distinct().ToList());
		var clean = methods.Count(method => !facts.TryGetValue(method, out var fact) || fact.All.Count == 0);
		Console.WriteLine($"{methods.Count,4} {clean,5}  " + string.Join(" ", s_kinds.Select(kind => (where[kind].Count == 0 ? "." : where[kind].Count.ToString()).PadLeft(9))) + "  " + name);
		if (!detail)
		{
			return;
		}
		foreach (var kind in s_kinds.Where(kind => where[kind].Count > 0))
		{
			Console.WriteLine($"            {kind}: {string.Join(", ", where[kind].OrderBy(method => method, StringComparer.Ordinal).Take(14))}{(where[kind].Count > 14 ? ", ..." : "")}");
		}
	}

	static Facts Scan(MethodDefinition method)
	{
		var facts = new Facts();
		foreach (var instruction in method.Body.Instructions)
		{
			var code = instruction.OpCode.Code;
			switch (instruction.Operand)
			{
				case TypeReference type when Open(type):
					if (code == Code.Ldtoken)
					{
						facts.Direct.Add("typeof");
					}
					else if (code == Code.Newarr)
					{
						facts.Direct.Add(type is GenericParameter ? "T[]" : "G<T>[]");
					}
					else if ((code == Code.Castclass || code == Code.Isinst || code == Code.Unbox_Any || code == Code.Unbox) && type is GenericParameter)
					{
						facts.Direct.Add("cast");
					}
					else if (code == Code.Constrained && type is GenericParameter)
					{
						facts.Direct.Add("call-on-T");
					}
					break;
				case FieldReference field when (code == Code.Ldsfld || code == Code.Stsfld || code == Code.Ldsflda) && Open(field.DeclaringType):
					facts.Direct.Add("static");
					break;
				case MethodReference target:
				{
					var declaring = target.DeclaringType.GetElementType().FullName;
					var open = Open(target.DeclaringType) || target is GenericInstanceMethod instance && instance.GenericArguments.Any(Open);
					if (!open)
					{
						break;
					}
					if (code == Code.Ldtoken)
					{
						facts.Direct.Add("typeof");
					}
					else if (target.Name == "get_Default" && (declaring == "System.Collections.Generic.EqualityComparer`1" || declaring == "System.Collections.Generic.Comparer`1"))
					{
						facts.Direct.Add("comparer");
					}
					else if (declaring == "System.Activator")
					{
						facts.Direct.Add("create");
					}
					else if (code == Code.Newobj && declaring == "System.Collections.Generic.List`1" && method.DeclaringType.FullName != declaring)
					{
						facts.Direct.Add("new-kept");
					}
					var resolved = target.Resolve();
					if (resolved != null && resolved != method)
					{
						facts.Callees.Add(resolved);
					}
					break;
				}
			}
		}
		return facts;
	}

	static bool Open(TypeReference type)
	{
		switch (type)
		{
			case GenericParameter _:
				return true;
			case GenericInstanceType instance:
				return instance.GenericArguments.Any(Open);
			case TypeSpecification specification:
				return Open(specification.ElementType);
			default:
				return false;
		}
	}
}
