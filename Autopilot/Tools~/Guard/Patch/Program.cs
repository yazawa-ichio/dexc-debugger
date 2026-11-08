using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

// Rewrites a bot assembly so a host enum reaches the Pipeline interpreter as a plain integer.
//
// The interpreter keeps a host enum boxed, in a reference slot. A comparison with the enum's zero member compiles to a test
// it reads as "not null", so `if (state == Running)` and `switch (state)` go wrong without a word. Everything typed as a
// host enum in the bot therefore becomes int32, and each value is converted at the point it enters.
//
// usage: Dexc.Guard.Patch <assembly> <reference directory>
if (args.Length != 2)
{
	Console.Error.WriteLine("usage: Dexc.Guard.Patch <assembly> <reference directory>");
	return 2;
}

var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(args[1]);
var image = new MemoryStream(File.ReadAllBytes(args[0]));
using var assembly = AssemblyDefinition.ReadAssembly(image, new ReaderParameters { AssemblyResolver = resolver });
var module = assembly.MainModule;
var int32 = module.TypeSystem.Int32;
var hashCode = new MethodReference("GetHashCode", int32, module.TypeSystem.Object) { HasThis = true };

bool IsHostEnum(TypeReference type)
{
	if (type == null || type.IsArray || type.IsByReference || type.IsGenericParameter || type.IsGenericInstance)
		return false;
	if (type.Scope == module)
		return false;
	try
	{
		return type.Resolve()?.IsEnum == true;
	}
	catch (AssemblyResolutionException)
	{
		return false;
	}
}

// Boxing first is the form the interpreter reads correctly.
void Convert(ILProcessor il, Instruction after, TypeReference enumType)
{
	var box = il.Create(OpCodes.Box, enumType);
	il.InsertAfter(after, box);
	il.InsertAfter(box, il.Create(OpCodes.Callvirt, hashCode));
}

var changed = 0;
foreach (var type in module.GetTypes())
{
	foreach (var field in type.Fields.Where(f => IsHostEnum(f.FieldType)))
	{
		field.FieldType = int32;
		changed++;
	}

	foreach (var method in type.Methods)
	{
		foreach (var parameter in method.Parameters.Where(p => IsHostEnum(p.ParameterType)))
			parameter.ParameterType = int32;
		if (IsHostEnum(method.ReturnType))
			method.ReturnType = int32;
	}
}

foreach (var type in module.GetTypes())
{
	foreach (var method in type.Methods.Where(m => m.HasBody))
	{
		var il = method.Body.GetILProcessor();
		foreach (var local in method.Body.Variables.Where(v => IsHostEnum(v.VariableType)))
		{
			local.VariableType = int32;
			changed++;
		}

		foreach (var instruction in method.Body.Instructions.ToList())
		{
			var isCall = instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt;
			if (isCall && instruction.Operand is MethodReference called && called.DeclaringType.Scope != module && IsHostEnum(called.ReturnType))
			{
				Convert(il, instruction, called.ReturnType);
				changed++;
			}

			var isLoad = instruction.OpCode == OpCodes.Ldfld || instruction.OpCode == OpCodes.Ldsfld;
			if (isLoad && instruction.Operand is FieldReference field && field.DeclaringType.Scope != module && IsHostEnum(field.FieldType))
			{
				Convert(il, instruction, field.FieldType);
				changed++;
			}

			// x.ToString() on an enum goes through the address of a local. Boxing the value keeps the name.
			if (instruction.OpCode == OpCodes.Constrained && instruction.Operand is TypeReference constrained && IsHostEnum(constrained))
			{
				var address = instruction.Previous;
				if (address.OpCode == OpCodes.Ldloca || address.OpCode == OpCodes.Ldloca_S)
					address.OpCode = OpCodes.Ldloc;
				else if (address.OpCode == OpCodes.Ldarga || address.OpCode == OpCodes.Ldarga_S)
					address.OpCode = OpCodes.Ldarg;
				else if (address.OpCode == OpCodes.Ldflda)
					address.OpCode = OpCodes.Ldfld;
				instruction.OpCode = OpCodes.Box;
				changed++;
			}
		}
	}
}

assembly.Write(args[0]);
Console.WriteLine("patched " + changed);
return 0;
