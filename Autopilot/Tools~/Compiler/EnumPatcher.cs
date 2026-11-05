using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Dexc.Autopilot.Compiler
{
	/// <summary>
	/// The rewrite of Tools~/Guard/Patch, in memory. See that tool for why a host enum has to reach the Pipeline interpreter
	/// as a plain integer. Reads the references from memory, because a Player build deletes and rewrites their folder.
	/// </summary>
	internal sealed class EnumPatcher : IDisposable
	{
		private readonly MemoryAssemblyResolver m_Resolver;

		public EnumPatcher(string referenceDirectory) => m_Resolver = new MemoryAssemblyResolver(referenceDirectory);

		public void Dispose() => m_Resolver.Dispose();

		public byte[] Patch(byte[] image)
		{
			using var assembly = AssemblyDefinition.ReadAssembly(new MemoryStream(image), new ReaderParameters { AssemblyResolver = m_Resolver });
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

			foreach (var type in module.GetTypes())
			{
				foreach (var field in type.Fields.Where(f => IsHostEnum(f.FieldType)))
					field.FieldType = int32;
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
						local.VariableType = int32;

					foreach (var instruction in method.Body.Instructions.ToList())
					{
						var isCall = instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt;
						if (isCall && instruction.Operand is MethodReference called && called.DeclaringType.Scope != module && IsHostEnum(called.ReturnType))
							Convert(il, instruction, called.ReturnType);

						var isLoad = instruction.OpCode == OpCodes.Ldfld || instruction.OpCode == OpCodes.Ldsfld;
						if (isLoad && instruction.Operand is FieldReference field && field.DeclaringType.Scope != module && IsHostEnum(field.FieldType))
							Convert(il, instruction, field.FieldType);

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
						}
					}
				}
			}

			using var output = new MemoryStream();
			assembly.Write(output);
			return output.ToArray();
		}

		private sealed class MemoryAssemblyResolver : IAssemblyResolver
		{
			private readonly Dictionary<string, AssemblyDefinition> m_Loaded = new Dictionary<string, AssemblyDefinition>();
			private readonly string m_Directory;

			public MemoryAssemblyResolver(string directory) => m_Directory = directory;

			public AssemblyDefinition Resolve(AssemblyNameReference name) => Resolve(name, null);

			public AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters parameters)
			{
				if (m_Loaded.TryGetValue(name.Name, out var known))
					return known;
				var path = Path.Combine(m_Directory, name.Name + ".dll");
				if (!File.Exists(path))
					throw new AssemblyResolutionException(name);
				var loaded = AssemblyDefinition.ReadAssembly(new MemoryStream(File.ReadAllBytes(path)), new ReaderParameters { AssemblyResolver = this });
				m_Loaded[name.Name] = loaded;
				return loaded;
			}

			public void Dispose()
			{
				foreach (var assembly in m_Loaded.Values)
					assembly.Dispose();
				m_Loaded.Clear();
			}
		}
	}
}
