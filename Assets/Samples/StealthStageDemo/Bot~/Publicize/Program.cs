using System;
using System.IO;
using Mono.Cecil;

// Writes a copy of an assembly with everything public, for the bot to compile against. Only the compiler sees the copy:
// the bot's IL names members exactly as the real assembly does, and the interpreter finds them there by name.
if (args.Length < 2)
{
	Console.Error.WriteLine("usage: Publicize <output directory> <assembly>...");
	return 2;
}

Directory.CreateDirectory(args[0]);
for (var index = 1; index < args.Length; index++)
{
	var resolver = new DefaultAssemblyResolver();
	resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(args[index])));
	using var assembly = AssemblyDefinition.ReadAssembly(args[index], new ReaderParameters { AssemblyResolver = resolver });
	foreach (var type in assembly.MainModule.GetTypes())
	{
		if (type.IsNested)
			type.IsNestedPublic = true;
		else
			type.IsPublic = true;
		foreach (var field in type.Fields)
			field.IsPublic = true;
		foreach (var method in type.Methods)
			method.IsPublic = true;
	}

	assembly.Write(Path.Combine(args[0], Path.GetFileName(args[index])));
}

return 0;
