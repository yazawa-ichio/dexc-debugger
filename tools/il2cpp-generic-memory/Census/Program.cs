using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Mono.Cecil;

// Counts, for each generic type definition that survived the linker, the members libil2cpp inflates when one of its
// instances is initialized, and joins that with how many instances the il2cpp output names.
// usage: Census <ManagedStripped folder> <il2cppOutput folder> [top]
static class Program
{
	sealed class Definition
	{
		public string Assembly;
		public string Name;
		public string Mangled;
		public bool IsValueType;
		public bool IsInterface;
		public int Methods;
		public int Pinned;
		public int Properties;
		public int Events;
		public int Fields;
		public int Instances;
	}

	static int Main(string[] args)
	{
		if (args[0] == "--il")
		{
			return DumpIl(args[1], args[2], args.Length > 3 ? args[3] : null);
		}
		if (args[0] == "--slots")
		{
			return DumpSlots(args[1], args.Skip(2).ToArray());
		}
		if (args[0] == "--erasable")
		{
			return Erasable.Run(args[1], args.Skip(2).ToArray());
		}
		if (args[0] == "--callers")
		{
			return DumpCallers(args[1], args.Skip(2).ToArray());
		}
		if (args[0] == "--identity")
		{
			return Identity.Run(args[1], args.Skip(2).ToArray());
		}
		if (args[0] == "--tokens")
		{
			return Tokens.Run(args[1], args.Skip(2).ToArray());
		}
		if (args[0] == "--types")
		{
			return DumpTypes(args[1]);
		}
		var managed = args[0];
		var output = args[1];
		var top = args.Length > 2 ? int.Parse(args[2]) : 40;
		var resolver = new DefaultAssemblyResolver();
		resolver.AddSearchDirectory(managed);
		var definitions = new List<Definition>();
		foreach (var path in Directory.GetFiles(managed, "*.dll"))
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
			foreach (var type in assembly.MainModule.GetTypes().Where(type => type.HasGenericParameters))
			{
				definitions.Add(new Definition
				{
					Assembly = assembly.Name.Name,
					Name = type.FullName,
					Mangled = Mangle(type.Name),
					IsValueType = type.IsValueType,
					IsInterface = type.IsInterface,
					Methods = type.Methods.Count,
					Pinned = type.Methods.Count(Pinned),
					Properties = type.Properties.Count,
					Events = type.Events.Count,
					Fields = type.Fields.Count,
				});
			}
		}

		// Il2CppGenericClassTable.c names every generic class the conversion saw as <Mangled>_t<hash>.
		var table = File.ReadAllText(Path.Combine(output, "Il2CppGenericClassTable.c"));
		var counts = new Dictionary<string, int>();
		foreach (Match match in Regex.Matches(table, @"&(\w+?)_t[0-9A-F]{40}_GenericClass"))
		{
			counts[match.Groups[1].Value] = counts.TryGetValue(match.Groups[1].Value, out var count) ? count + 1 : 1;
		}
		foreach (var group in definitions.GroupBy(definition => definition.Mangled))
		{
			// Definitions that share a simple name cannot be told apart in the table, so the count goes to each of them.
			foreach (var definition in group)
			{
				counts.TryGetValue(group.Key, out definition.Instances);
			}
		}

		long allMethods = 0, allPinned = 0, allInstances = 0;
		foreach (var definition in definitions.Where(definition => !definition.IsInterface))
		{
			allMethods += (long)definition.Methods * definition.Instances;
			allPinned += (long)definition.Pinned * definition.Instances;
			allInstances += definition.Instances;
		}
		Console.WriteLine($"generic definitions {definitions.Count}, instances in table {counts.Values.Sum()}, matched instances {allInstances}");
		Console.WriteLine($"methods x instances {allMethods}, of which pinned by a slot or a constructor {allPinned} ({100.0 * allPinned / Math.Max(1, allMethods):F1}%)");
		Console.WriteLine();
		Console.WriteLine("inst  meth  pinned prop evt fld  meth*inst  kind  type");
		foreach (var definition in definitions.OrderByDescending(definition => (long)definition.Methods * definition.Instances).Take(top))
		{
			var kind = definition.IsInterface ? "itf" : definition.IsValueType ? "val" : "ref";
			Console.WriteLine($"{definition.Instances,4} {definition.Methods,5} {definition.Pinned,6} {definition.Properties,4} {definition.Events,3} {definition.Fields,3} {(long)definition.Methods * definition.Instances,10}  {kind}   {definition.Assembly}:{definition.Name}");
		}
		return 0;
	}

	// Prints the IL of the methods of a type, to check what a rewrite produced. usage: Census --il <dll> <type> [method]
	static int DumpIl(string path, string typeName, string methodName)
	{
		var resolver = new DefaultAssemblyResolver();
		resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
		var assembly = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { AssemblyResolver = resolver });
		foreach (var type in assembly.MainModule.GetTypes().Where(type => type.FullName == typeName || type.Name == typeName))
		{
			Console.WriteLine(type.FullName + " : " + type.Methods.Count + " methods");
			foreach (var method in type.Methods.Where(method => methodName == null || method.Name == methodName))
			{
				Console.WriteLine("  " + method.FullName);
				if (methodName != null && method.HasBody)
				{
					foreach (var instruction in method.Body.Instructions)
					{
						Console.WriteLine("    " + instruction);
					}
				}
			}
		}
		return 0;
	}

	// Prints one line for each type: the name il2cpp gives it (without the hash), what it is, and for a value type its
	// size and whether it holds references. sharing.py joins this with the tables of the il2cpp output.
	// usage: Census --types <ManagedStripped folder>
	static int DumpTypes(string folder)
	{
		var resolver = new DefaultAssemblyResolver();
		resolver.AddSearchDirectory(folder);
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
				var kind = type.IsEnum ? "enum" : type.IsInterface ? "interface" : type.IsValueType ? "struct"
					: type.BaseType != null && type.BaseType.FullName == "System.MulticastDelegate" ? "delegate" : "class";
				var size = -1;
				var references = false;
				if (type.IsValueType && !type.HasGenericParameters)
				{
					size = SizeOf(type, ref references, 0);
				}
				var slots = type.Methods.Count(method => method.IsVirtual && !method.IsAbstract);
				var shape = type.IsValueType && !type.HasGenericParameters ? Shape(type, 0) : null;
				Console.WriteLine($"{Mangle(type.Name)}\t{kind}\t{type.GenericParameters.Count}\t{type.Methods.Count}\t{slots}\t{type.Interfaces.Count}\t{size}\t{(references ? 1 : 0)}\t{assembly.Name.Name}\t{type.FullName}\t{shape ?? "-"}\t{type.Methods.Count(method => method.IsVirtual)}\t{type.Methods.Count(method => method.IsConstructor)}\t{type.Methods.Count(IsFieldAccessor)}");
			}
		}
		return 0;
	}

	// A method that only reads or writes one field of its own object: a call of it can be the field access itself.
	static bool IsFieldAccessor(MethodDefinition method)
	{
		if (!method.HasBody || method.IsStatic || method.IsVirtual || method.IsConstructor)
		{
			return false;
		}
		var codes = method.Body.Instructions.Where(instruction => instruction.OpCode.Code != Mono.Cecil.Cil.Code.Nop).Select(instruction => instruction.OpCode.Code).ToList();
		var get = codes.Count == 3 && codes[0] == Mono.Cecil.Cil.Code.Ldarg_0 && codes[1] == Mono.Cecil.Cil.Code.Ldfld && codes[2] == Mono.Cecil.Cil.Code.Ret;
		var set = codes.Count == 4 && codes[0] == Mono.Cecil.Cil.Code.Ldarg_0 && codes[1] == Mono.Cecil.Cil.Code.Ldarg_1 && codes[2] == Mono.Cecil.Cil.Code.Stfld && codes[3] == Mono.Cecil.Cil.Code.Ret;
		return get || set;
	}

	// The fields of a value type, flattened to what each is to the machine: an integer of 1, 2, 4 or 8 bytes, a float (f), a
	// double (d) or a reference (r). Two value types with the same string are laid out and passed the same way, which the
	// same size alone does not give: three floats and three ints differ in how a call passes them. null when it cannot be told.
	static string Shape(TypeReference reference, int depth)
	{
		switch (reference.MetadataType)
		{
			case MetadataType.Boolean:
			case MetadataType.SByte:
			case MetadataType.Byte:
				return "1";
			case MetadataType.Char:
			case MetadataType.Int16:
			case MetadataType.UInt16:
				return "2";
			case MetadataType.Int32:
			case MetadataType.UInt32:
				return "4";
			case MetadataType.Int64:
			case MetadataType.UInt64:
			case MetadataType.IntPtr:
			case MetadataType.UIntPtr:
			case MetadataType.Pointer:
				return "8";
			case MetadataType.Single:
				return "f";
			case MetadataType.Double:
				return "d";
			case MetadataType.Class:
			case MetadataType.String:
			case MetadataType.Object:
			case MetadataType.Array:
				return "r";
		}
		if (reference is GenericInstanceType || reference is GenericParameter || depth > 8)
		{
			return null;
		}
		var type = reference.Resolve();
		if (type == null || !type.IsValueType || type.IsExplicitLayout || type.ClassSize > 0 || type.PackingSize > 0)
		{
			return null;
		}
		var parts = new List<string>();
		foreach (var field in type.Fields.Where(field => !field.IsStatic))
		{
			var part = Shape(field.FieldType, depth + 1);
			if (part == null)
			{
				return null;
			}
			parts.Add(part);
		}
		// A value type with one field is that field to the machine.
		return parts.Count == 1 ? parts[0] : "(" + string.Join("", parts) + ")";
	}

	// The size of a value type whose fields are all primitives, enums or such value types; -1 when it cannot be told here.
	static int SizeOf(TypeReference reference, ref bool references, int depth)
	{
		switch (reference.MetadataType)
		{
			case MetadataType.Boolean:
			case MetadataType.SByte:
			case MetadataType.Byte:
				return 1;
			case MetadataType.Char:
			case MetadataType.Int16:
			case MetadataType.UInt16:
				return 2;
			case MetadataType.Int32:
			case MetadataType.UInt32:
			case MetadataType.Single:
				return 4;
			case MetadataType.Int64:
			case MetadataType.UInt64:
			case MetadataType.Double:
			case MetadataType.IntPtr:
			case MetadataType.UIntPtr:
			case MetadataType.Pointer:
				return 8;
			case MetadataType.Class:
			case MetadataType.String:
			case MetadataType.Object:
			case MetadataType.Array:
				references = true;
				return 8;
		}
		if (reference is GenericInstanceType || reference is GenericParameter || depth > 8)
		{
			return -1;
		}
		var type = reference.Resolve();
		if (type == null || !type.IsValueType || type.IsExplicitLayout)
		{
			return -1;
		}
		// Sequential layout with natural alignment, which is what il2cpp gives a struct that asks for nothing else.
		var size = 0;
		var alignment = 1;
		foreach (var field in type.Fields.Where(field => !field.IsStatic))
		{
			var fieldSize = SizeOf(field.FieldType, ref references, depth + 1);
			if (fieldSize < 0)
			{
				return -1;
			}
			var fieldAlignment = Math.Min(fieldSize, 8);
			fieldAlignment = fieldAlignment == 0 ? 1 : fieldAlignment;
			alignment = Math.Max(alignment, fieldAlignment > 4 ? 8 : fieldAlignment > 2 ? 4 : fieldAlignment);
			size = (size + fieldAlignment - 1) / fieldAlignment * fieldAlignment + fieldSize;
		}
		size = Math.Max(1, (size + alignment - 1) / alignment * alignment);
		return type.ClassSize > 0 ? type.ClassSize : size;
	}

	// Prints who still calls each method of the given types after the linker ran. A method of an interface that nothing
	// calls is removed by the linker, and with it a slot of every class and of every array that implements the interface.
	// usage: Census --callers <ManagedStripped folder> <type full name>...
	static int DumpCallers(string folder, string[] typeNames)
	{
		var resolver = new DefaultAssemblyResolver();
		resolver.AddSearchDirectory(folder);
		var calls = new Dictionary<string, Dictionary<string, int>>();
		var sites = new Dictionary<string, List<string>>();
		var kept = new Dictionary<string, int>();
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
				if (typeNames.Contains(type.FullName))
				{
					foreach (var method in type.Methods)
					{
						kept[type.FullName + "::" + method.Name] = 0;
					}
				}
				foreach (var method in type.Methods.Where(method => method.HasBody))
				{
					foreach (var instruction in method.Body.Instructions)
					{
						if (!(instruction.Operand is MethodReference target) || !typeNames.Contains(target.DeclaringType.GetElementType().FullName))
						{
							continue;
						}
						var key = target.DeclaringType.GetElementType().FullName + "::" + target.Name;
						if (!calls.TryGetValue(key, out var byAssembly))
						{
							calls.Add(key, byAssembly = new Dictionary<string, int>());
							sites.Add(key, new List<string>());
						}
						byAssembly[assembly.Name.Name] = byAssembly.TryGetValue(assembly.Name.Name, out var count) ? count + 1 : 1;
						sites[key].Add(type.FullName + "::" + method.Name);
					}
				}
			}
		}
		foreach (var key in kept.Keys.OrderBy(key => key, StringComparer.Ordinal))
		{
			if (!calls.TryGetValue(key, out var byAssembly))
			{
				Console.WriteLine($"{0,5}  {key}   (kept, no caller: an implementation or the runtime holds it)");
				continue;
			}
			Console.WriteLine($"{byAssembly.Values.Sum(),5}  {key}   {string.Join(", ", byAssembly.OrderByDescending(pair => pair.Value).Select(pair => pair.Key + " " + pair.Value))}");
			foreach (var site in sites[key].GroupBy(site => site.Split(new[] { "::" }, StringSplitOptions.None)[0]).OrderByDescending(group => group.Count()).Take(4))
			{
				Console.WriteLine($"           {site.Count(),4}  {site.Key}");
			}
		}
		return 0;
	}

	// Prints what the vtable of a type asks for when one of its instances is initialized: a MethodInfo for each method
	// that fills a slot, and a class for each generic interface. usage: Census --slots <dll> <type>...
	static int DumpSlots(string path, string[] typeNames)
	{
		var resolver = new DefaultAssemblyResolver();
		resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
		var assembly = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { AssemblyResolver = resolver });
		Console.WriteLine("slots  fill  ownVirt  gItf  itf  meth  type");
		foreach (var type in assembly.MainModule.GetTypes().Where(type => typeNames.Length == 0 ? type.HasGenericParameters && !type.IsInterface : typeNames.Contains(type.FullName) || typeNames.Contains(type.Name)))
		{
			// Every method of every interface is a slot; so is every virtual method the type or a base introduces.
			var interfaceSlots = 0;
			var genericInterfaces = 0;
			foreach (var implemented in type.Interfaces)
			{
				var definition = implemented.InterfaceType.Resolve();
				interfaceSlots += definition == null ? 0 : definition.Methods.Count;
				genericInterfaces += implemented.InterfaceType is GenericInstanceType ? 1 : 0;
			}
			var introduced = 0;
			var fill = 0;
			for (var current = type; current != null; current = current.BaseType?.Resolve())
			{
				introduced += current.Methods.Count(method => method.IsVirtual && method.IsNewSlot && !method.IsFinal);
				// A method of a generic type that fills a slot is inflated for each instance; one of a plain base is not.
				fill += current.HasGenericParameters ? current.Methods.Count(method => method.IsVirtual && !method.IsAbstract) : 0;
			}
			Console.WriteLine($"{interfaceSlots + introduced,5} {fill,5} {type.Methods.Count(method => method.IsVirtual),8} {genericInterfaces,5} {type.Interfaces.Count,4} {type.Methods.Count,5}  {type.FullName}");
			if (typeNames.Length > 0)
			{
				foreach (var implemented in type.Interfaces)
				{
					Console.WriteLine("         " + implemented.InterfaceType.FullName + " : " + (implemented.InterfaceType.Resolve()?.Methods.Count ?? 0));
				}
			}
		}
		return 0;
	}

	// A method that has to stay on the type whatever the callers do: it fills a slot, or the runtime finds it by name.
	static bool Pinned(MethodDefinition method)
	{
		return method.IsVirtual || method.IsConstructor || method.IsAbstract;
	}

	static string Mangle(string name)
	{
		return Regex.Replace(name, @"[^A-Za-z0-9_]", match => match.Value == "`" ? "_" : "U" + ((int)match.Value[0]).ToString("X4"));
	}
}
