using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace GenericMemory.Editor
{
	/// <summary>
	/// Step "nulltest": the test of an engine object against null, answered where it is written.
	///
	/// <c>x == null</c> on a UnityEngine.Object is a call of the operator, which calls CompareBaseObjects, which calls
	/// IsNativeObjectAlive, which calls GetCachedPtr: four calls into another file to learn that the reference is null
	/// or that the engine side of the object is gone. il2cpp does not inline across files, so every such test in the
	/// program pays for them.
	///
	/// The step puts two static methods that say exactly that into UnityEngine.CoreModule and marks them to be inlined
	/// (il2cpp writes a copy of such a method into every file that calls it, and the copy is always inlined). It points
	/// at them the comparisons whose right side is the literal null, and the conversions to bool (<c>if (x)</c>). A
	/// comparison of two objects, and one whose null is not the instruction right before the call, stay as they are.
	///
	/// The two methods are in a class of their own that has no static constructor. As methods of UnityEngine.Object,
	/// which has one, every call would keep the test of whether that constructor has run: two loads and a branch
	/// left in front of each inlined test. They read the private field of the object that holds the pointer to the
	/// engine side; il2cpp does not look at who may read a field.
	///
	/// What the two methods say is read from the bodies of the operator and of the three methods under it. The step
	/// compares those bodies with the ones it was written against and stops the build when one differs: another
	/// version of the engine may mean something else by the test.
	/// </summary>
	sealed class NullTestExpander
	{
		const string HolderNamespace = "UnityEngine";
		const string HolderName = "GenericMemoryNullTest";

		// The bodies of UnityEngine.Object in 6000.3.20f1, as Signature writes them.
		static readonly Dictionary<string, string> s_expected = new Dictionary<string, string>
		{
			{ "op_Equality", "System.Boolean(UnityEngine.Object,UnityEngine.Object);nop;ldarg.0;ldarg.1;call UnityEngine.Object::CompareBaseObjects;stloc.0;br.s @6;ldloc.0;ret" },
			{ "op_Inequality", "System.Boolean(UnityEngine.Object,UnityEngine.Object);nop;ldarg.0;ldarg.1;call UnityEngine.Object::CompareBaseObjects;ldc.i4.0;ceq;stloc.0;br.s @8;ldloc.0;ret" },
			{ "op_Implicit", "System.Boolean(UnityEngine.Object);nop;ldarg.0;ldnull;call UnityEngine.Object::CompareBaseObjects;ldc.i4.0;ceq;stloc.0;br.s @8;ldloc.0;ret" },
			{ "CompareBaseObjects", "System.Boolean(UnityEngine.Object,UnityEngine.Object);nop;ldarg.0;ldnull;ceq;stloc.0;ldarg.1;ldnull;ceq;stloc.1;ldloc.1;ldloc.0;and;stloc.2;ldloc.2;brfalse.s @18;ldc.i4.1;stloc.3;br.s @43;ldloc.1;stloc.s 4;ldloc.s 4;brfalse.s @28;ldarg.0;call UnityEngine.Object::IsNativeObjectAlive;ldc.i4.0;ceq;stloc.3;br.s @43;ldloc.0;stloc.s 5;ldloc.s 5;brfalse.s @38;ldarg.1;call UnityEngine.Object::IsNativeObjectAlive;ldc.i4.0;ceq;stloc.3;br.s @43;ldarg.0;ldarg.1;ceq;stloc.3;br.s @43;ldloc.3;ret" },
			{ "IsNativeObjectAlive", "System.Boolean(UnityEngine.Object);nop;ldarg.0;callvirt UnityEngine.Object::GetCachedPtr;ldsfld System.IntPtr::Zero;call System.IntPtr::op_Inequality;stloc.0;br.s @7;ldloc.0;ret" },
			{ "GetCachedPtr", "System.IntPtr();nop;ldarg.0;ldfld UnityEngine.Object::m_CachedPtr;stloc.0;br.s @5;ldloc.0;ret" },
		};

		readonly WholeProgram _program;
		readonly StringBuilder _log;

		public NullTestExpander(WholeProgram program, StringBuilder log)
		{
			_program = program;
			_log = log;
		}

		public void Run()
		{
			var core = _program.Assemblies.FirstOrDefault(assembly => assembly.Name.Name == "UnityEngine.CoreModule");
			var type = core?.MainModule.GetType("UnityEngine.Object");
			if (type == null)
			{
				throw new InvalidOperationException("nulltest: UnityEngine.Object is not among the assemblies");
			}
			Guard(type);
			var holder = core.MainModule.GetType(HolderNamespace + "." + HolderName);
			if (holder == null)
			{
				holder = new TypeDefinition(HolderNamespace, HolderName, TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit, core.MainModule.TypeSystem.Object);
				core.MainModule.Types.Add(holder);
			}
			var isNull = Helper(holder, type, "IsNull", true);
			var isAlive = Helper(holder, type, "IsAlive", false);
			_program.Touch(core.MainModule);

			var comparisons = 0;
			var conversions = 0;
			var methods = 0;
			var others = 0;
			var jumpedTo = 0;
			var byAssembly = new Dictionary<string, int>();
			foreach (var method in _program.Bodies().ToList())
			{
				var name = method.Module.Assembly.Name.Name;
				if (!InScope(name))
				{
					continue;
				}
				var instructions = method.Body.Instructions;
				HashSet<Instruction> targets = null;
				var changed = 0;
				for (var at = 0; at < instructions.Count; at++)
				{
					var instruction = instructions[at];
					if (instruction.OpCode.Code != Code.Call || !(instruction.Operand is MethodReference called) || called.DeclaringType.FullName != "UnityEngine.Object")
					{
						continue;
					}
					if (called.Name == "op_Implicit" && called.Parameters.Count == 1 && called.ReturnType.FullName == "System.Boolean")
					{
						instruction.Operand = method.Module.ImportReference(isAlive);
						conversions++;
						changed++;
						continue;
					}
					if ((called.Name != "op_Equality" && called.Name != "op_Inequality") || called.Parameters.Count != 2)
					{
						continue;
					}
					if (at == 0 || instructions[at - 1].OpCode.Code != Code.Ldnull)
					{
						others++;
						continue;
					}
					// A jump to the call brings its own right side, which may be something else than the null before it.
					targets = targets ?? Targets(method.Body);
					if (targets.Contains(instruction))
					{
						jumpedTo++;
						continue;
					}
					// The null becomes the call and the call becomes nothing: a jump to the null still finds the left side on
					// the stack and now gets the answer for it. The body keeps its size.
					var before = instructions[at - 1];
					before.OpCode = OpCodes.Call;
					before.Operand = method.Module.ImportReference(called.Name == "op_Equality" ? isNull : isAlive);
					instruction.OpCode = OpCodes.Nop;
					instruction.Operand = null;
					comparisons++;
					changed++;
				}
				if (changed > 0)
				{
					methods++;
					byAssembly[name] = (byAssembly.TryGetValue(name, out var count) ? count : 0) + changed;
					_program.Resized(method);
				}
			}
			_log.AppendLine("nulltest: " + comparisons + " comparisons with null and " + conversions + " conversions to bool, in " + methods + " methods of " + byAssembly.Count + " assemblies");
			_log.AppendLine("  left alone: " + others + " comparisons of two objects or with the null elsewhere, " + jumpedTo + " whose call is jumped to");
			foreach (var pair in byAssembly.OrderByDescending(pair => pair.Value).Take(20))
			{
				_log.AppendLine("  " + pair.Key + ": " + pair.Value);
			}
		}

		// The engine's own modules keep their tests: this step writes UnityEngine.CoreModule for the two methods only.
		static bool InScope(string assembly)
		{
			return assembly != "UnityEngine" && !(assembly.StartsWith("UnityEngine.", StringComparison.Ordinal) && assembly.EndsWith("Module", StringComparison.Ordinal));
		}

		static void Guard(TypeDefinition type)
		{
			var problems = new List<string>();
			foreach (var pair in s_expected)
			{
				var found = type.Methods.Where(method => method.Name == pair.Key && method.HasBody).ToList();
				if (found.Count != 1)
				{
					problems.Add(pair.Key + ": " + found.Count + " methods of that name");
					continue;
				}
				var signature = Signature(found[0]);
				if (signature != pair.Value)
				{
					problems.Add(pair.Key + " is now\n    " + signature);
				}
			}
			var pointer = type.Fields.FirstOrDefault(field => field.Name == "m_CachedPtr");
			if (pointer == null || pointer.IsStatic || pointer.FieldType.FullName != "System.IntPtr")
			{
				problems.Add("m_CachedPtr is not a field of the object that holds a pointer");
			}
			if (problems.Count > 0)
			{
				throw new InvalidOperationException("nulltest: UnityEngine.Object is not what this step was written against, so nothing was rewritten.\n  " + string.Join("\n  ", problems));
			}
		}

		internal static string Signature(MethodDefinition method)
		{
			var instructions = method.Body.Instructions;
			var text = new StringBuilder();
			text.Append(method.ReturnType.FullName).Append('(').Append(string.Join(",", method.Parameters.Select(parameter => parameter.ParameterType.FullName))).Append(')');
			foreach (var instruction in instructions)
			{
				text.Append(';').Append(instruction.OpCode.Name);
				switch (instruction.Operand)
				{
					case null:
						break;
					case MemberReference member:
						text.Append(' ').Append(member.DeclaringType.FullName).Append("::").Append(member.Name);
						break;
					case Instruction target:
						text.Append(" @").Append(instructions.IndexOf(target));
						break;
					case VariableDefinition variable:
						text.Append(' ').Append(variable.Index);
						break;
					default:
						text.Append(' ').Append(instruction.Operand);
						break;
				}
			}
			return text.ToString();
		}

		// bool IsNull(Object value) => (object)value == null || value.m_CachedPtr == 0, and IsAlive, which is its negation.
		static MethodDefinition Helper(TypeDefinition holder, TypeDefinition type, string name, bool isNull)
		{
			var existing = holder.Methods.FirstOrDefault(method => method.Name == name);
			if (existing != null)
			{
				return existing;
			}
			var module = type.Module;
			var method = new MethodDefinition(name, MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig, module.TypeSystem.Boolean);
			method.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None, type));
			method.AggressiveInlining = true;
			var pointer = type.Fields.First(field => field.Name == "m_CachedPtr");
			var il = method.Body.GetILProcessor();
			var none = il.Create(isNull ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
			il.Emit(OpCodes.Ldarg_0);
			il.Emit(OpCodes.Brfalse_S, none);
			il.Emit(OpCodes.Ldarg_0);
			il.Emit(OpCodes.Ldfld, pointer);
			il.Emit(OpCodes.Ldc_I4_0);
			il.Emit(OpCodes.Conv_I);
			il.Emit(isNull ? OpCodes.Ceq : OpCodes.Cgt_Un);
			il.Emit(OpCodes.Ret);
			il.Append(none);
			il.Emit(OpCodes.Ret);
			holder.Methods.Add(method);
			return method;
		}

		static HashSet<Instruction> Targets(MethodBody body)
		{
			var targets = new HashSet<Instruction>();
			foreach (var instruction in body.Instructions)
			{
				switch (instruction.Operand)
				{
					case Instruction target:
						targets.Add(target);
						break;
					case Instruction[] table:
						targets.UnionWith(table);
						break;
				}
			}
			foreach (var handler in body.ExceptionHandlers)
			{
				foreach (var edge in new[] { handler.TryStart, handler.TryEnd, handler.HandlerStart, handler.HandlerEnd, handler.FilterStart })
				{
					if (edge != null)
					{
						targets.Add(edge);
					}
				}
			}
			return targets;
		}
	}
}
