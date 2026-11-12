using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace GenericMemory.Editor
{
	/// <summary>
	/// The step "enum": a local of a generic struct over a reference type, which nothing but its own method sees, becomes
	/// the instance over the stand-in. The enumerator of a foreach is the case this is for:
	///
	///   List&lt;Foo&gt;.Enumerator e = list.GetEnumerator(); while (e.MoveNext()) { Foo x = e.Current; ... } e.Dispose();
	///
	/// becomes the same code over List&lt;object&gt;.Enumerator, with the list handed to GetEnumerator of List&lt;object&gt; and a
	/// cast on what Current gives back. The list stays a List&lt;Foo&gt;. The struct is a value on the stack of this method,
	/// so no object of another class comes to be. What is saved is the class List&lt;Foo&gt;.Enumerator, its methods, and the
	/// method List&lt;Foo&gt;.GetEnumerator, for every Foo.
	///
	/// A local is taken only when every use of it is one of: the store of what a call returned, a call of a method without
	/// parameters on its address, or a constrained call on its address. Each method that then runs for the stand-in has to
	/// be one that cannot tell (Free). A struct that such a call returns by value (KeyValuePair of a dictionary's
	/// enumerator) has to go straight into a local that passes the same test. Anything else leaves the whole group as it was.
	/// </summary>
	sealed partial class Canonicalizer
	{
		sealed class LocalPlan
		{
			public readonly Dictionary<VariableDefinition, GenericInstanceType> Locals = new Dictionary<VariableDefinition, GenericInstanceType>();
			public readonly Dictionary<VariableDefinition, GenericInstanceType> Before = new Dictionary<VariableDefinition, GenericInstanceType>();
			// The stores that are accounted for: each takes what a call in this plan returned.
			public readonly HashSet<Instruction> Stores = new HashSet<Instruction>();
			public readonly List<(Instruction Call, MethodReference Target, GenericInstanceType Shared, List<TypeReference> Needed)> Roots = new List<(Instruction, MethodReference, GenericInstanceType, List<TypeReference>)>();
			public readonly List<(Instruction Call, GenericInstanceType Type, TypeReference Cast, List<TypeReference> Needed)> Calls = new List<(Instruction, GenericInstanceType, TypeReference, List<TypeReference>)>();
			public readonly List<(Instruction Prefix, GenericInstanceType Type)> Constrained = new List<(Instruction, GenericInstanceType)>();
			public string Failure;
		}

		readonly Dictionary<string, int> _localsKept = new Dictionary<string, int>();
		readonly Dictionary<string, int> _localsShared = new Dictionary<string, int>();
		readonly Dictionary<string, int[]> _localsByAssembly = new Dictionary<string, int[]>();

		void RetypeLocals()
		{
			var shared = 0;
			var kept = 0;
			foreach (var caller in _program.Bodies())
			{
				var body = caller.Body;
				if (!body.HasVariables || !body.Variables.Any(variable => IsLocalCandidate(variable.VariableType)))
				{
					continue;
				}
				var uses = Uses(caller);
				var targets = BranchTargets(caller);
				var tried = new HashSet<VariableDefinition>();
				foreach (var variable in body.Variables.ToArray())
				{
					if (tried.Contains(variable) || !IsLocalCandidate(variable.VariableType) || !uses.TryGetValue(variable, out var all))
					{
						continue;
					}
					// A root is a local that takes what a method of a generic class returned.
					var root = all.FirstOrDefault(use => IsStore(caller, use, variable) && RootOf(caller, use, variable, out _, out _, out _, out _));
					if (root == null)
					{
						continue;
					}
					var plan = new LocalPlan();
					var ok = PlanStore(caller, root, variable, uses, targets, plan);
					foreach (var local in plan.Locals.Keys)
					{
						tried.Add(local);
					}
					tried.Add(variable);
					var name = variable.VariableType.GetElementType().FullName;
					if (!_localsByAssembly.TryGetValue(caller.Module.Assembly.Name.Name, out var counts))
					{
						_localsByAssembly.Add(caller.Module.Assembly.Name.Name, counts = new int[2]);
					}
					if (!ok)
					{
						kept++;
						counts[1]++;
						var reason = name + ": " + (plan.Failure ?? "?");
						_localsKept[reason] = _localsKept.TryGetValue(reason, out var count) ? count + 1 : 1;
						continue;
					}
					Apply(caller, plan);
					shared++;
					counts[0]++;
					_localsShared[name] = _localsShared.TryGetValue(name, out var done) ? done + 1 : 1;
				}
			}
			_log.AppendLine("enum: " + shared + " groups of struct locals now are the instance over the stand-in, " + kept + " stay as they were");
			foreach (var pair in _localsShared.OrderByDescending(pair => pair.Value).Take(15))
			{
				_log.AppendLine("  shared " + pair.Value + "  " + pair.Key);
			}
			foreach (var pair in _localsKept.OrderByDescending(pair => pair.Value).Take(25))
			{
				_log.AppendLine("  kept " + pair.Value + "  " + pair.Key);
			}
			foreach (var pair in _localsByAssembly.OrderByDescending(pair => pair.Value[0] + pair.Value[1]).Take(25))
			{
				_log.AppendLine("  in " + pair.Key + ": shared " + pair.Value[0] + ", kept " + pair.Value[1]);
			}
		}

		/// <summary>A generic struct with an argument that is a reference type known where the local is declared.</summary>
		static bool IsLocalCandidate(TypeReference type)
		{
			return type is GenericInstanceType instance && IsValueTypeInstance(type)
				&& instance.GenericArguments.Any(argument => !argument.ContainsGenericParameter && IsReference(argument));
		}

		static Dictionary<VariableDefinition, List<Instruction>> Uses(MethodDefinition method)
		{
			var uses = new Dictionary<VariableDefinition, List<Instruction>>();
			foreach (var instruction in method.Body.Instructions)
			{
				VariableDefinition local;
				if (!Local(method, instruction, out local, out _))
				{
					var code = instruction.OpCode.Code;
					if (code != Code.Ldloca && code != Code.Ldloca_S)
					{
						continue;
					}
					local = instruction.Operand as VariableDefinition;
					if (local == null)
					{
						continue;
					}
				}
				if (!uses.TryGetValue(local, out var list))
				{
					uses.Add(local, list = new List<Instruction>());
				}
				list.Add(instruction);
			}
			return uses;
		}

		static HashSet<Instruction> BranchTargets(MethodDefinition method)
		{
			var targets = new HashSet<Instruction>();
			foreach (var instruction in method.Body.Instructions)
			{
				if (instruction.Operand is Instruction target)
				{
					targets.Add(target);
				}
				else if (instruction.Operand is Instruction[] many)
				{
					targets.UnionWith(many);
				}
			}
			foreach (var handler in method.Body.ExceptionHandlers)
			{
				targets.Add(handler.TryStart);
				targets.Add(handler.HandlerStart);
				if (handler.FilterStart != null)
				{
					targets.Add(handler.FilterStart);
				}
			}
			return targets;
		}

		static bool IsStore(MethodDefinition method, Instruction instruction, VariableDefinition variable)
		{
			return Local(method, instruction, out var local, out var store) && store && local == variable;
		}

		static bool IsPlainCall(Instruction instruction)
		{
			if (instruction == null || (instruction.OpCode != OpCodes.Call && instruction.OpCode != OpCodes.Callvirt))
			{
				return false;
			}
			return instruction.Previous == null || instruction.Previous.OpCode.OpCodeType != OpCodeType.Prefix;
		}

		/// <summary>
		/// Whether the store takes what a method of a generic class, called on an object and without arguments, returned,
		/// and that method can run for the stand-in: then the type the local would have, and what the call needs.
		/// </summary>
		bool RootOf(MethodDefinition caller, Instruction store, VariableDefinition variable, out Instruction call, out GenericInstanceType shared, out GenericInstanceType type, out List<TypeReference> needed)
		{
			call = store.Previous;
			shared = null;
			type = null;
			needed = null;
			if (!IsPlainCall(call) || !(call.Operand is MethodReference target) || target is GenericInstanceMethod || !target.HasThis || target.Parameters.Count != 0)
			{
				return false;
			}
			if (!(target.DeclaringType is GenericInstanceType declaring) || !(variable.VariableType is GenericInstanceType before))
			{
				return false;
			}
			var callee = Resolve(target);
			if (callee == null || callee.IsStatic || callee.IsConstructor || !callee.HasBody || callee.HasGenericParameters || callee.DeclaringType.IsValueType || !_free.TryGetValue(callee, out var freedom))
			{
				return false;
			}
			// Really dispatched: the slot of the object gives the target and its context, whatever the token says.
			if (call.OpCode == OpCodes.Callvirt && callee.IsVirtual && !callee.IsFinal && !callee.DeclaringType.IsSealed)
			{
				return false;
			}
			if (declaring.GenericArguments.Count != freedom.OfType.Length || InContext(callee.ReturnType, declaring).FullName != before.FullName)
			{
				return false;
			}
			var module = caller.Module;
			var any = false;
			shared = new GenericInstanceType(declaring.ElementType);
			for (var at = 0; at < declaring.GenericArguments.Count; at++)
			{
				var argument = declaring.GenericArguments[at];
				var standIn = StandIn(callee.DeclaringType.GenericParameters[at], module);
				var replace = freedom.OfType[at] == Free && !argument.ContainsGenericParameter && IsReference(argument) && argument.FullName != standIn.FullName
					&& Mentions(callee.ReturnType, GenericParameterType.Type, at) && Shareable(callee.DeclaringType, at);
				shared.GenericArguments.Add(replace ? standIn : argument);
				any |= replace;
			}
			if (!any || !(InContext(callee.ReturnType, shared) is GenericInstanceType after) || !Inits(callee, declaring, shared, null, null, out needed))
			{
				return false;
			}
			var sharedName = shared.FullName;
			needed.RemoveAll(each => each.FullName == sharedName);
			type = after;
			return true;
		}

		bool PlanStore(MethodDefinition caller, Instruction store, VariableDefinition variable, Dictionary<VariableDefinition, List<Instruction>> uses, HashSet<Instruction> targets, LocalPlan plan)
		{
			if (plan.Stores.Contains(store))
			{
				return true;
			}
			if (targets.Contains(store) || !RootOf(caller, store, variable, out var call, out var shared, out var type, out var needed))
			{
				plan.Failure = plan.Failure ?? "a store that takes something else than what the method of the collection returned";
				return false;
			}
			plan.Stores.Add(store);
			plan.Roots.Add((call, (MethodReference)call.Operand, shared, needed));
			return PlanLocal(caller, variable, type, uses, targets, plan);
		}

		bool PlanLocal(MethodDefinition caller, VariableDefinition variable, GenericInstanceType after, Dictionary<VariableDefinition, List<Instruction>> uses, HashSet<Instruction> targets, LocalPlan plan)
		{
			if (plan.Locals.TryGetValue(variable, out var planned))
			{
				if (planned.FullName != after.FullName)
				{
					plan.Failure = plan.Failure ?? "one local for two types";
					return false;
				}
				return true;
			}
			var before = (GenericInstanceType)variable.VariableType;
			var definition = before.ElementType.Resolve();
			if (definition == null || !definition.IsValueType || before.GenericArguments.Count != after.GenericArguments.Count || before.GenericArguments.Count != definition.GenericParameters.Count)
			{
				plan.Failure = plan.Failure ?? "not a struct that can be read";
				return false;
			}
			var changed = new bool[before.GenericArguments.Count];
			for (var at = 0; at < changed.Length; at++)
			{
				changed[at] = before.GenericArguments[at].FullName != after.GenericArguments[at].FullName;
				// The struct has to get, for its own parameter, the type every one of its methods was read with.
				if (changed[at] && (after.GenericArguments[at].FullName != StandIn(definition.GenericParameters[at], caller.Module).FullName || !Shareable(definition, at)))
				{
					plan.Failure = plan.Failure ?? "the struct would get another stand-in than its own, or is one of the engine's";
					return false;
				}
			}
			plan.Locals.Add(variable, after);
			plan.Before.Add(variable, before);
			if (!uses.TryGetValue(variable, out var all))
			{
				return true;
			}
			foreach (var use in all)
			{
				if (Local(caller, use, out _, out var store))
				{
					if (!store)
					{
						plan.Failure = plan.Failure ?? "read as a value";
						return false;
					}
					if (plan.Stores.Contains(use))
					{
						continue;
					}
					// Another store: what another call of the same kind returned, or what a call on a local of this plan will return.
					var producer = use.Previous;
					if (IsPlainCall(producer) && producer.Previous != null && (producer.Previous.OpCode.Code == Code.Ldloca || producer.Previous.OpCode.Code == Code.Ldloca_S)
						&& producer.Previous.Operand is VariableDefinition owner && owner != variable)
					{
						// It is accounted for when the uses of that local are read; that local needs a root of its own first.
						if (!plan.Locals.ContainsKey(owner))
						{
							var first = uses.TryGetValue(owner, out var theirs) ? theirs.FirstOrDefault(each => IsStore(caller, each, owner)) : null;
							if (first == null || !PlanStore(caller, first, owner, uses, targets, plan))
							{
								plan.Failure = plan.Failure ?? "a store from a local that cannot be shared";
								return false;
							}
						}
						if (!plan.Stores.Contains(use))
						{
							plan.Failure = plan.Failure ?? "a store that was not accounted for";
							return false;
						}
						continue;
					}
					if (!PlanStore(caller, use, variable, uses, targets, plan))
					{
						return false;
					}
					continue;
				}
				// The address of the local: what follows has to be a call on it.
				var next = use.Next;
				if (next != null && next.OpCode == OpCodes.Constrained)
				{
					var call = next.Next;
					if (!(next.Operand is TypeReference constrained) || constrained.FullName != before.FullName || call == null || call.OpCode != OpCodes.Callvirt
						|| !(call.Operand is MethodReference through) || through.Parameters.Count != 0 || through is GenericInstanceMethod || through.DeclaringType is GenericInstanceType
						|| through.ReturnType.ContainsGenericParameter || targets.Contains(next) || targets.Contains(call))
					{
						plan.Failure = plan.Failure ?? "a constrained call that is not a plain one";
						return false;
					}
					var implementation = Implementation(definition, through);
					if (implementation == null || !FreeFor(implementation, changed) || !Inits(implementation, before, after, null, null, out var initialized) || initialized.Count != 0)
					{
						plan.Failure = plan.Failure ?? "the method behind a constrained call can tell the type: " + through.Name;
						return false;
					}
					plan.Constrained.Add((next, after));
					continue;
				}
				if (!IsPlainCall(next) || targets.Contains(next) || !(next.Operand is MethodReference target) || target is GenericInstanceMethod || !target.HasThis
					|| target.Parameters.Count != 0 || target.DeclaringType.FullName != before.FullName)
				{
					plan.Failure = plan.Failure ?? "its address goes somewhere else than into a call without arguments";
					return false;
				}
				var callee = Resolve(target);
				if (callee == null || !callee.HasBody || callee.DeclaringType != definition || !FreeFor(callee, changed) || !Inits(callee, before, after, null, null, out var needed))
				{
					plan.Failure = plan.Failure ?? "a method of the struct can tell the type: " + target.Name;
					return false;
				}
				TypeReference cast = null;
				var returns = callee.ReturnType;
				var mentions = false;
				for (var at = 0; at < changed.Length; at++)
				{
					mentions |= changed[at] && Mentions(returns, GenericParameterType.Type, at);
				}
				if (mentions)
				{
					if (Castable(returns))
					{
						cast = InContext(returns, before);
						if (cast.ContainsGenericParameter)
						{
							plan.Failure = plan.Failure ?? "what comes back has no closed type to cast to";
							return false;
						}
					}
					else if (IsValueTypeInstance(returns) && !(returns is ByReferenceType))
					{
						// A struct made of the type: it has to go straight into a local that passes the same test.
						var into = next.Next;
						if (into == null || targets.Contains(into) || !Local(caller, into, out var holder, out var stores) || !stores
							|| holder.VariableType.FullName != InContext(returns, before).FullName || !(InContext(returns, after) is GenericInstanceType holderAfter))
						{
							plan.Failure = plan.Failure ?? "a struct that comes back does not go straight into a local: " + target.Name;
							return false;
						}
						plan.Stores.Add(into);
						if (!PlanLocal(caller, holder, holderAfter, uses, targets, plan))
						{
							return false;
						}
					}
					else
					{
						plan.Failure = plan.Failure ?? "what comes back cannot be given its type back: " + target.Name;
						return false;
					}
				}
				plan.Calls.Add((next, after, cast, needed));
			}
			return true;
		}

		bool FreeFor(MethodDefinition callee, bool[] changed)
		{
			if (!_free.TryGetValue(callee, out var freedom) || freedom.OfType.Length != changed.Length || callee.HasGenericParameters || callee.IsStatic)
			{
				return false;
			}
			for (var at = 0; at < changed.Length; at++)
			{
				if (changed[at] && freedom.OfType[at] != Free)
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>The method of the struct that a constrained call of an interface method ends in. null when it is not one of the struct's own.</summary>
		static MethodDefinition Implementation(TypeDefinition type, MethodReference through)
		{
			var declaring = through.DeclaringType.FullName;
			MethodDefinition byName = null;
			foreach (var method in type.Methods)
			{
				if (!method.HasBody || method.IsStatic || method.Parameters.Count != 0 || method.HasGenericParameters)
				{
					continue;
				}
				if (method.Overrides.Any(overridden => overridden.Name == through.Name && overridden.DeclaringType.FullName == declaring))
				{
					return method;
				}
				if (method.Name == through.Name && method.IsPublic && method.IsVirtual)
				{
					byName = method;
				}
			}
			if (byName == null)
			{
				return null;
			}
			// By name it is the implementation only when the struct lists the interface.
			return type.Interfaces.Any(implemented => implemented.InterfaceType.FullName == declaring) ? byName : null;
		}

		void Apply(MethodDefinition caller, LocalPlan plan)
		{
			var module = caller.Module;
			var il = caller.Body.GetILProcessor();
			foreach (var (call, target, shared, needed) in plan.Roots)
			{
				var imported = module.ImportReference(shared, caller);
				var made = new MethodReference(target.Name, target.ReturnType, imported)
				{
					HasThis = target.HasThis,
					ExplicitThis = target.ExplicitThis,
					CallingConvention = target.CallingConvention,
				};
				// The object goes through the As of GenericMemoryShared over the instance, which sees to it that the instance is initialized.
				var holder = new GenericInstanceType(module.ImportReference(_shared));
				holder.GenericArguments.Add(imported);
				var cast = new MethodReference(_sharedAs.Name, _sharedAs.ReturnType, holder);
				cast.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));
				// The instruction that was the call becomes the As, so a branch that led to the call leads here.
				var again = Instruction.Create(call.OpCode, made);
				call.OpCode = OpCodes.Call;
				call.Operand = cast;
				il.InsertAfter(call, again);
				Touches(caller, again, needed, false);
			}
			foreach (var pair in plan.Locals)
			{
				pair.Key.VariableType = module.ImportReference(pair.Value, caller);
			}
			foreach (var (call, type, cast, needed) in plan.Calls)
			{
				var target = (MethodReference)call.Operand;
				call.Operand = new MethodReference(target.Name, target.ReturnType, module.ImportReference(type, caller))
				{
					HasThis = target.HasThis,
					ExplicitThis = target.ExplicitThis,
					CallingConvention = target.CallingConvention,
				};
				Touches(caller, call, needed, false);
				if (cast != null)
				{
					il.InsertAfter(call, Instruction.Create(OpCodes.Castclass, module.ImportReference(cast, caller)));
				}
			}
			foreach (var (prefix, type) in plan.Constrained)
			{
				prefix.Operand = module.ImportReference(type, caller);
			}
			_program.Resized(caller);
			_program.Touch(module);
		}
	}
}
