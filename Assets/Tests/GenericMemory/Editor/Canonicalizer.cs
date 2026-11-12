using System.Collections.Generic;
using System.Linq;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace GenericMemory.Editor
{
	/// <summary>
	/// Calls generic code with one type in place of every reference type, where the code cannot tell the difference.
	///
	/// il2cpp shares the code of a generic method among reference types, but libil2cpp still makes one MethodInfo, and
	/// whatever its body needs, for each set of type arguments a caller names. A parameter the body only stores, passes
	/// on, reads fields through, or calls through a slot that is really dispatched needs nothing of the type at run time.
	/// Such a parameter is given the same argument at every call site, so all reference types share one instance.
	///
	/// Two kinds of call are not such a use, and both come from how a call reaches its target:
	/// - through a generic interface, the runtime looks the interface up by its exact instance;
	/// - to a sealed or final method, il2cpp calls the target directly and hands it the context the caller names, so the
	///   target would run as if the object were of the stand-in type. That is allowed only when the target cannot tell either.
	///
	/// With <c>comparers</c> on, a parameter whose only use of the type is to ask for its default comparer counts as well,
	/// at the call sites whose argument has no comparison of its own: for such a type the default comparer calls
	/// Object.Equals and Object.GetHashCode, which is what the comparer of the stand-in does. The comparer of a type is
	/// what a search in a list or an array costs most: it is made once for each element type that is searched. This form
	/// also reaches the methods of a generic class, which are called on the object as an instance over the stand-in.
	/// </summary>
	sealed partial class Canonicalizer
	{
		const byte Observed = 0;
		const byte ComparerOnly = 1;
		const byte Free = 2;

		sealed class Freedom
		{
			// For each parameter of the declaring class, and of the method itself, how much of it the body needs.
			public byte[] OfType;
			public byte[] OfMethod;
		}

		readonly WholeProgram _program;
		readonly StringBuilder _log;
		readonly bool _comparers;
		readonly Dictionary<MethodDefinition, Freedom> _free = new Dictionary<MethodDefinition, Freedom>();
		readonly Dictionary<MethodReference, MethodDefinition> _resolved = new Dictionary<MethodReference, MethodDefinition>();
		readonly Dictionary<string, bool> _ownComparison = new Dictionary<string, bool>();
		// The parameters whose default comparer something asks for, whatever else is done with them: "method|T or M|position".
		readonly HashSet<string> _reaches = new HashSet<string>();
		// For each argument type, the sites that would make its default comparer: how many now share one, and what keeps the others.
		readonly Dictionary<string, int[]> _comparerSites = new Dictionary<string, int[]>();
		readonly Dictionary<string, int> _keptBy = new Dictionary<string, int>();

		// "free": a method of a generic class that cannot tell its type argument is called on the object as the instance over
		// the stand-in. That gains something only where a method is made when it is first used, not with its class.
		readonly bool _shareFree;
		// "enum": a local of a generic struct that only this method sees, such as the enumerator of a foreach, becomes the
		// instance over the stand-in, and so do the call that makes it and the calls on it.
		readonly bool _shareLocals;

		// What the two steps above need the reading of a body to tell apart, which the older steps did not.
		bool Extended => _shareFree || _shareLocals;

		public static bool IsStep(string step)
		{
			return step == "canon" || step == "comparers" || step == "free" || step == "enum";
		}

		public Canonicalizer(WholeProgram program, StringBuilder log, bool comparers = false)
		{
			_program = program;
			_log = log;
			_comparers = comparers;
		}

		public Canonicalizer(WholeProgram program, StringBuilder log, HashSet<string> steps)
		{
			_program = program;
			_log = log;
			_comparers = steps.Contains("comparers");
			_shareFree = steps.Contains("free");
			_shareLocals = steps.Contains("enum");
		}

		public void Run()
		{
			MakeAs();
			MakeShared();
			foreach (var method in _program.Bodies())
			{
				var type = method.DeclaringType;
				if (!method.HasGenericParameters && !type.HasGenericParameters)
				{
					continue;
				}
				var freedom = new Freedom { OfType = new byte[type.GenericParameters.Count], OfMethod = new byte[method.GenericParameters.Count] };
				// A static member of a generic class belongs to one instance of the class, and so does what a constructor makes.
				// The constructor of a struct makes no object: it fills the value it is handed, like any other method of the struct.
				var ofType = !method.IsStatic && (!method.IsConstructor || (Extended && type.IsValueType));
				for (var at = 0; at < freedom.OfType.Length; at++)
				{
					freedom.OfType[at] = ofType ? Free : Observed;
				}
				// A virtual generic method is found through the slot with its exact arguments. The methods il2cpp knows by
				// name and writes its own code for are left with the arguments the caller gave them.
				var known = Intrinsic(type);
				for (var at = 0; at < freedom.OfMethod.Length; at++)
				{
					freedom.OfMethod[at] = !known && !method.IsVirtual && !method.GenericParameters[at].HasNotNullableValueTypeConstraint && !ByReference(method, at) ? Free : Observed;
				}
				_free[method] = freedom;
			}
			// Start from "free" and take away what a body proves otherwise, until nothing changes: a parameter that is
			// passed on to another method is free only as far as that method's parameter is.
			for (var changed = true; changed; )
			{
				changed = false;
				foreach (var pair in _free)
				{
					changed |= Narrow(pair.Key, pair.Value.OfType, GenericParameterType.Type);
					changed |= Narrow(pair.Key, pair.Value.OfMethod, GenericParameterType.Method);
				}
			}
			FindNeeds();
			if (_comparers)
			{
				FindComparerUses();
			}
			if (_shareLocals)
			{
				RetypeLocals();
			}

			var sites = 0;
			var onObjects = 0;
			var methods = new HashSet<MethodDefinition>();
			foreach (var caller in _program.Bodies())
			{
				var instructions = caller.Body.Instructions;
				for (var index = 0; index < instructions.Count; index++)
				{
					var instruction = instructions[index];
					if (!(instruction.Operand is MethodReference target))
					{
						continue;
					}
					var isCall = instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt;
					var prefixed = instruction.Previous != null && instruction.Previous.OpCode.OpCodeType == OpCodeType.Prefix;
					var replacedType = new bool[0];
					var replacedMethod = new bool[0];
					MethodDefinition callee = null;

					if (isCall && !prefixed && target is GenericInstanceMethod call && !(call.DeclaringType is GenericInstanceType))
					{
						// A generic method of a class that is not generic.
						callee = Resolve(call.ElementMethod);
						if (callee != null && _free.TryGetValue(callee, out var freedom))
						{
							replacedMethod = new bool[call.GenericArguments.Count];
							GenericInstanceMethod replaced = null;
							for (var at = 0; at < freedom.OfMethod.Length; at++)
							{
								var argument = call.GenericArguments[at];
								if (!Shares(freedom.OfMethod[at], argument, callee.ReturnType, GenericParameterType.Method, at))
								{
									continue;
								}
								var standIn = StandIn(callee.GenericParameters[at], caller.Module);
								if (argument.FullName == standIn.FullName)
								{
									continue;
								}
								if (replaced == null)
								{
									replaced = new GenericInstanceMethod(call.ElementMethod);
									foreach (var each in call.GenericArguments)
									{
										replaced.GenericArguments.Add(each);
									}
								}
								replaced.GenericArguments[at] = standIn;
								replacedMethod[at] = true;
							}
							List<TypeReference> needed = null;
							if (replaced != null && !Inits(callee, null, null, call, replaced, out needed))
							{
								replaced = null;
								replacedMethod = new bool[replacedMethod.Length];
								needed = null;
							}
							if (replaced != null)
							{
								Count(caller, target, callee, replacedType, replacedMethod);
								var retyped = Retype(caller, instruction, callee, call, replaced);
								instruction = Touches(caller, retyped, needed, ReferenceEquals(retyped, instruction));
								index = instructions.IndexOf(instruction);
								// What comes back is what the caller passed in, so the cast states a fact and gives the value its type
								// back. Where the caller is generic itself the type is one of its own parameters, and a cast to that
								// would be the very use this step removes; its code is shared, so the types already agree there.
								var returned = new CallContext(call).Type(callee.ReturnType);
								if (Replaced(callee.ReturnType, call, replaced) && !returned.ContainsGenericParameter)
								{
									caller.Body.GetILProcessor().InsertAfter(instruction, Instruction.Create(OpCodes.Castclass, caller.Module.ImportReference(returned)));
									_program.Resized(caller);
									index++;
								}
								_program.Touch(caller.Module);
								methods.Add(callee);
								sites++;
								continue;
							}
						}
					}
					else if ((_comparers || _shareFree) && isCall && !prefixed && !(target is GenericInstanceMethod) && target.DeclaringType is GenericInstanceType declaring)
					{
						// A method of a generic class, called on the object as an instance over the stand-in.
						callee = Resolve(target);
						if (callee != null && OnObject(caller, instruction, target, declaring, callee, out replacedType, out var made))
						{
							Count(caller, target, callee, replacedType, replacedMethod);
							index = instructions.IndexOf(made);
							methods.Add(callee);
							onObjects++;
							continue;
						}
					}
					if (_comparers)
					{
						Count(caller, target, callee ?? Resolve(target), replacedType, replacedMethod);
					}
				}
			}
			_log.AppendLine((_comparers ? "canon with comparers: " : "canon: ") + (_shareFree ? "(free) " : "") + (_shareLocals ? "(enum) " : "") + _free.Count(pair => pair.Value.OfMethod.Any(level => level == Free)) + " generic methods have a free parameter; "
				+ sites + " call sites of generic methods and " + onObjects + " of methods of generic classes, " + methods.Count + " methods in all, now share an instance");
			if (_shareFree)
			{
				_log.AppendLine("  of the calls of methods of generic classes, " + _freeSites + " share a method that cannot tell its type argument");
			}
			foreach (var group in methods.GroupBy(method => method.DeclaringType.FullName).OrderByDescending(group => group.Count()).Take(25))
			{
				_log.AppendLine("  " + group.Key + ": " + string.Join(" ", group.Select(method => method.Name).Distinct().Take(12)));
			}
			if (_comparers)
			{
				var all = _comparerSites.Count(pair => pair.Value[1] == 0);
				_log.AppendLine("default comparers: " + _comparerSites.Count + " argument types have a site that would make theirs; for " + all + " of them every such site now shares one");
				_log.AppendLine("  sites that share " + _comparerSites.Values.Sum(counts => counts[0]) + ", sites that keep theirs " + _comparerSites.Values.Sum(counts => counts[1]) + ", kept by:");
				foreach (var pair in _keptBy.OrderByDescending(pair => pair.Value).Take(25))
				{
					_log.AppendLine("    " + pair.Value + "  " + pair.Key);
				}
			}
		}

		/// <summary>Whether a parameter that is this free may take the stand-in for this argument.</summary>
		bool Shares(byte level, TypeReference argument, TypeReference returnType, GenericParameterType kind, int position)
		{
			if (level == Observed || !IsReference(argument))
			{
				return false;
			}
			if (level == Free)
			{
				return true;
			}
			// The comparer of the stand-in is the comparer of the argument only when the argument brings none of its own,
			// and nothing gives the value back as the type, where a cast would have to check it.
			return _comparers && !argument.ContainsGenericParameter && !Mentions(returnType, kind, position) && !HasOwnComparison(argument);
		}

		/// <summary>
		/// Notes, for each argument type of the call whose default comparer the callee would make, whether the site now
		/// shares the comparer of the stand-in. The comparer of a type is made once however many sites ask for it, so a
		/// type saves it only when none of its sites is left.
		/// </summary>
		void Count(MethodDefinition caller, MethodReference target, MethodDefinition callee, bool[] replacedType, bool[] replacedMethod)
		{
			if (!_comparers || callee == null)
			{
				return;
			}
			void Note(TypeReference argument, GenericParameterType kind, int position, bool replaced)
			{
				if (argument.ContainsGenericParameter || !IsReference(argument) || !_reaches.Contains(Key(callee, kind, position)))
				{
					return;
				}
				if (!_comparerSites.TryGetValue(argument.FullName, out var counts))
				{
					_comparerSites.Add(argument.FullName, counts = new int[2]);
				}
				counts[replaced ? 0 : 1]++;
				if (!replaced)
				{
					var reason = (HasOwnComparison(argument) ? "(own comparison) " : "") + callee.DeclaringType.FullName + "::" + callee.Name;
					_keptBy[reason] = _keptBy.TryGetValue(reason, out var count) ? count + 1 : 1;
				}
			}
			if (target.DeclaringType is GenericInstanceType declaring)
			{
				for (var at = 0; at < declaring.GenericArguments.Count; at++)
				{
					Note(declaring.GenericArguments[at], GenericParameterType.Type, at, at < replacedType.Length && replacedType[at]);
				}
			}
			if (target is GenericInstanceMethod generic)
			{
				for (var at = 0; at < generic.GenericArguments.Count; at++)
				{
					Note(generic.GenericArguments[at], GenericParameterType.Method, at, at < replacedMethod.Length && replacedMethod[at]);
				}
			}
		}

		static string Key(MethodDefinition method, GenericParameterType kind, int position)
		{
			return method.FullName + (kind == GenericParameterType.Type ? "|T|" : "|M|") + position;
		}

		/// <summary>
		/// Finds every parameter whose default comparer is asked for by the method or by what it passes the parameter to.
		/// This is only for the count of what is left: it says a site would make a comparer, not that it may be redirected.
		/// </summary>
		void FindComparerUses()
		{
			var passes = new List<(string From, string To)>();
			foreach (var method in _free.Keys)
			{
				foreach (var instruction in method.Body.Instructions)
				{
					if (!(instruction.Operand is MethodReference target))
					{
						continue;
					}
					var declaring = target.DeclaringType as GenericInstanceType;
					var generic = target as GenericInstanceMethod;
					var definition = target.DeclaringType.GetElementType().FullName;
					var isDefault = target.Name == "get_Default" && (definition == "System.Collections.Generic.EqualityComparer`1" || definition == "System.Collections.Generic.Comparer`1");
					var callee = isDefault ? null : Resolve(target);
					void Each(TypeReference argument, GenericParameterType calleeKind, int calleePosition)
					{
						foreach (var (kind, position) in Named(argument))
						{
							if (isDefault)
							{
								_reaches.Add(Key(method, kind, position));
							}
							else if (callee != null)
							{
								passes.Add((Key(callee, calleeKind, calleePosition), Key(method, kind, position)));
							}
						}
					}
					for (var at = 0; declaring != null && at < declaring.GenericArguments.Count; at++)
					{
						Each(declaring.GenericArguments[at], GenericParameterType.Type, at);
					}
					for (var at = 0; generic != null && at < generic.GenericArguments.Count; at++)
					{
						Each(generic.GenericArguments[at], GenericParameterType.Method, at);
					}
				}
			}
			for (var changed = true; changed; )
			{
				changed = false;
				foreach (var (from, to) in passes)
				{
					if (_reaches.Contains(from))
					{
						changed |= _reaches.Add(to);
					}
				}
			}
		}

		static IEnumerable<(GenericParameterType Kind, int Position)> Named(TypeReference type)
		{
			switch (type)
			{
				case GenericParameter parameter:
					yield return (parameter.Type, parameter.Position);
					break;
				case GenericInstanceType instance:
					foreach (var argument in instance.GenericArguments)
					{
						foreach (var named in Named(argument))
						{
							yield return named;
						}
					}
					break;
				case TypeSpecification specification:
					foreach (var named in Named(specification.ElementType))
					{
						yield return named;
					}
					break;
			}
		}

		/// <summary>
		/// Redirects a call of a method of a generic class to the same method of the instance over the stand-ins, for the
		/// arguments whose only use in the method is the default comparer. The object keeps its class; only the context
		/// the method runs with changes, and the method was shown not to tell. The parameters have to be the bare type
		/// parameter or free of it, so that the only value whose static type changes is the object itself.
		/// </summary>
		bool OnObject(MethodDefinition caller, Instruction instruction, MethodReference target, GenericInstanceType declaring, MethodDefinition callee, out bool[] replaced, out Instruction call)
		{
			call = null;
			replaced = new bool[declaring.GenericArguments.Count];
			if (callee.IsStatic || callee.IsConstructor || !callee.HasBody || callee.HasGenericParameters || !_free.TryGetValue(callee, out var freedom))
			{
				return false;
			}
			// Really dispatched: the slot of the object gives the target and its context, whatever the token says.
			if (instruction.OpCode == OpCodes.Callvirt && callee.IsVirtual && !callee.IsFinal && !callee.DeclaringType.IsSealed)
			{
				return false;
			}
			if (callee.DeclaringType.IsValueType || declaring.GenericArguments.Count != freedom.OfType.Length)
			{
				return false;
			}
			var gain = false;
			var returned = false;
			var freely = false;
			for (var at = 0; at < replaced.Length; at++)
			{
				var argument = declaring.GenericArguments[at];
				if (freedom.OfType[at] == Free)
				{
					// A parameter that is wholly free gains nothing where the method is made with its class either way.
					// Where a method is made when it is first used, the one of the stand-in is made once for all.
					if (!_shareFree || !IsReference(argument) || argument.ContainsGenericParameter || !Shareable(callee.DeclaringType, at))
					{
						continue;
					}
					// What comes back as the type gets a cast, which needs a reference; a struct made of it has none.
					if (Mentions(callee.ReturnType, GenericParameterType.Type, at) && !Castable(callee.ReturnType))
					{
						continue;
					}
				}
				else if (freedom.OfType[at] != ComparerOnly || !Shares(ComparerOnly, argument, callee.ReturnType, GenericParameterType.Type, at))
				{
					continue;
				}
				if (callee.Parameters.Any(parameter => !(parameter.ParameterType is GenericParameter) && Mentions(parameter.ParameterType, GenericParameterType.Type, at)))
				{
					continue;
				}
				if (Extended && argument.FullName == StandIn(callee.DeclaringType.GenericParameters[at], caller.Module).FullName)
				{
					continue;
				}
				replaced[at] = true;
				gain = true;
				freely |= freedom.OfType[at] == Free;
				returned |= Mentions(callee.ReturnType, GenericParameterType.Type, at);
			}
			if (!gain)
			{
				replaced = new bool[replaced.Length];
				return false;
			}

			var module = caller.Module;
			var shared = new GenericInstanceType(declaring.ElementType);
			for (var at = 0; at < replaced.Length; at++)
			{
				shared.GenericArguments.Add(replaced[at] ? StandIn(callee.DeclaringType.GenericParameters[at], module) : declaring.GenericArguments[at]);
			}
			if (!Inits(callee, declaring, shared, null, null, out var needed))
			{
				replaced = new bool[replaced.Length];
				return false;
			}
			needed.RemoveAll(type => type.FullName == shared.FullName);
			var made = new MethodReference(target.Name, target.ReturnType, module.ImportReference(shared, caller))
			{
				HasThis = target.HasThis,
				ExplicitThis = target.ExplicitThis,
				CallingConvention = target.CallingConvention,
			};
			foreach (var parameter in target.Parameters)
			{
				made.Parameters.Add(new ParameterDefinition(parameter.ParameterType));
			}
			// The object goes through the As of GenericMemoryShared over the instance, which sees to it that the instance is initialized.
			var holder = new GenericInstanceType(module.ImportReference(_shared));
			holder.GenericArguments.Add(module.ImportReference(shared, caller));
			var cast = new MethodReference(_sharedAs.Name, _sharedAs.ReturnType, holder);
			cast.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));

			// The arguments come off the stack into locals, the object under them goes through As, and they go back.
			// The instruction that was the call becomes the first of these, so a branch that led to the call leads here.
			var il = caller.Body.GetILProcessor();
			var count = target.Parameters.Count;
			call = Instruction.Create(instruction.OpCode, made);
			var last = instruction;
			if (count == 0)
			{
				instruction.OpCode = OpCodes.Call;
				instruction.Operand = cast;
			}
			else
			{
				var locals = new VariableDefinition[count];
				for (var at = 0; at < count; at++)
				{
					locals[at] = new VariableDefinition(module.ImportReference(InContext(target.Parameters[at].ParameterType, declaring), caller));
					caller.Body.Variables.Add(locals[at]);
				}
				instruction.OpCode = OpCodes.Stloc;
				instruction.Operand = locals[count - 1];
				for (var at = count - 2; at >= 0; at--)
				{
					il.InsertAfter(last, last = Instruction.Create(OpCodes.Stloc, locals[at]));
				}
				il.InsertAfter(last, last = Instruction.Create(OpCodes.Call, cast));
				for (var at = 0; at < count; at++)
				{
					il.InsertAfter(last, last = Instruction.Create(OpCodes.Ldloc, locals[at]));
				}
			}
			il.InsertAfter(last, call);
			Touches(caller, call, needed, false);
			if (returned)
			{
				// What comes back is an object that was there before the call, so the cast states a fact and gives it its type back.
				il.InsertAfter(call, Instruction.Create(OpCodes.Castclass, module.ImportReference(InContext(callee.ReturnType, declaring), caller)));
			}
			if (freely)
			{
				_freeSites++;
			}
			_program.Resized(caller);
			_program.Touch(module);
			return true;
		}

		int _freeSites;

		/// <summary>
		/// Whether the steps "free" and "enum" may run a method of this type for the stand-in of this parameter. The types of
		/// the engine's own assemblies are left alone, as the hoister leaves them: a Player of the bench does not run their
		/// code paths, so nothing here would show a mistake. A parameter whose constraint names the parameter itself or
		/// another instance (where T : Base&lt;T&gt;) is left alone too: object does not meet such a constraint.
		/// </summary>
		static bool Shareable(TypeDefinition type, int position)
		{
			if (!Hoister.InScope(type.Module.Assembly.Name.Name) || position >= type.GenericParameters.Count)
			{
				return false;
			}
			return !type.GenericParameters[position].Constraints.Any(constraint => constraint.ConstraintType is TypeSpecification || constraint.ConstraintType is GenericParameter);
		}

		/// <summary>Whether a value of this type, of the signature of a method, is a reference, which a cast can give its type back.</summary>
		static bool Castable(TypeReference type)
		{
			switch (type)
			{
				case GenericParameter _:
				case ArrayType _:
					return true;
				case GenericInstanceType instance:
				{
					var definition = instance.ElementType.Resolve();
					return definition != null && !definition.IsValueType;
				}
				default:
					return false;
			}
		}

		static bool IsValueTypeInstance(TypeReference type)
		{
			if (!(type is GenericInstanceType instance))
			{
				return false;
			}
			var definition = instance.ElementType.Resolve();
			return definition != null && definition.IsValueType;
		}

		/// <summary>A type of the signature of a method of a generic class as the caller sees it: the parameters of the class replaced by its arguments.</summary>
		static TypeReference InContext(TypeReference type, GenericInstanceType declaring)
		{
			switch (type)
			{
				case GenericParameter parameter:
					return parameter.Type == GenericParameterType.Type ? declaring.GenericArguments[parameter.Position] : type;
				case GenericInstanceType instance:
				{
					var copy = new GenericInstanceType(instance.ElementType);
					foreach (var argument in instance.GenericArguments)
					{
						copy.GenericArguments.Add(InContext(argument, declaring));
					}
					return copy;
				}
				case ArrayType array:
					return new ArrayType(InContext(array.ElementType, declaring), array.Rank);
				case ByReferenceType reference:
					return new ByReferenceType(InContext(reference.ElementType, declaring));
				default:
					return type;
			}
		}

		/// <summary>
		/// Whether the type, a base of it or an interface of it brings a comparison of its own, which the default comparer
		/// of the type would call in place of Object.Equals. A type that cannot be read is taken to have one.
		/// </summary>
		bool HasOwnComparison(TypeReference type)
		{
			if (type is ArrayType)
			{
				return false;
			}
			if (type is TypeSpecification && !(type is GenericInstanceType))
			{
				return true;
			}
			if (_ownComparison.TryGetValue(type.FullName, out var known))
			{
				return known;
			}
			var own = false;
			for (var at = type.Resolve(); ; at = at.BaseType.Resolve())
			{
				if (at == null)
				{
					own = true;
					break;
				}
				if (Compares(at, 0))
				{
					own = true;
					break;
				}
				if (at.BaseType == null)
				{
					break;
				}
			}
			_ownComparison[type.FullName] = own;
			return own;
		}

		static bool Compares(TypeDefinition type, int depth)
		{
			foreach (var implemented in type.Interfaces)
			{
				var name = implemented.InterfaceType.GetElementType().FullName;
				if (name == "System.IEquatable`1" || name == "System.IComparable`1")
				{
					return true;
				}
				var definition = depth < 8 ? implemented.InterfaceType.Resolve() : null;
				if (definition == null || Compares(definition, depth + 1))
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// Makes the call with the replaced arguments, and gives each argument whose declared type changed with them the
		/// type the callee now declares. il2cpp writes the arguments into the C++ call as they are, and List&lt;A&gt; and
		/// List&lt;object&gt; are unrelated structs there, so the value goes through As, which hands back what it was given.
		/// As is instantiated over the stand-in types only, so it adds one MethodInfo for each of those, not for each type.
		/// A bare T needs none of this: every class derives from the object or the constraint it stands in for.
		/// </summary>
		Instruction Retype(MethodDefinition caller, Instruction instruction, MethodDefinition callee, GenericInstanceMethod call, GenericInstanceMethod replaced)
		{
			var count = callee.Parameters.Count;
			var before = new CallContext(call);
			var after = new CallContext(replaced);
			var declared = new TypeReference[count];
			var needed = new TypeReference[count];
			var any = false;
			for (var at = 0; at < count; at++)
			{
				var type = callee.Parameters[at].ParameterType;
				declared[at] = before.Type(type);
				var now = after.Type(type);
				if (!(type is GenericParameter) && declared[at].FullName != now.FullName && !IsInterface(now))
				{
					needed[at] = now;
					any = true;
				}
			}
			if (!any)
			{
				instruction.Operand = replaced;
				return instruction;
			}
			// The arguments are taken off the stack into locals and put back, each through As where its type changed.
			// The instruction that was the call becomes the first store, so a branch that led to the call leads here.
			var il = caller.Body.GetILProcessor();
			var module = caller.Module;
			var locals = new VariableDefinition[count];
			var casts = new GenericInstanceMethod[count];
			for (var at = 0; at < count; at++)
			{
				locals[at] = new VariableDefinition(module.ImportReference(declared[at], caller));
				if (needed[at] != null)
				{
					casts[at] = new GenericInstanceMethod(module.ImportReference(_as));
					casts[at].GenericArguments.Add(module.ImportReference(needed[at], caller));
				}
			}
			foreach (var local in locals)
			{
				caller.Body.Variables.Add(local);
			}
			var made = Instruction.Create(instruction.OpCode, replaced);
			instruction.OpCode = OpCodes.Stloc;
			instruction.Operand = locals[count - 1];
			var last = instruction;
			for (var at = count - 2; at >= 0; at--)
			{
				il.InsertAfter(last, last = Instruction.Create(OpCodes.Stloc, locals[at]));
			}
			for (var at = 0; at < count; at++)
			{
				il.InsertAfter(last, last = Instruction.Create(OpCodes.Ldloc, locals[at]));
				if (casts[at] != null)
				{
					il.InsertAfter(last, last = Instruction.Create(OpCodes.Call, casts[at]));
				}
			}
			il.InsertAfter(last, made);
			_program.Resized(caller);
			return made;
		}

		static bool IsInterface(TypeReference type)
		{
			var definition = type.Resolve();
			return definition != null && definition.IsInterface;
		}

		MethodDefinition _as;

		/// <summary>
		/// T As&lt;T&gt;(object value): returns its argument. It exists to give a reference another static type. It is made
		/// before the program is walked, since a type cannot be added to a module while its types are being read, and in a
		/// namespace this step leaves alone, so that its own calls keep the type they name.
		/// </summary>
		void MakeAs()
		{
			var module = _program.Assemblies.First(assembly => assembly.Name.Name == "mscorlib").MainModule;
			// A step that ran before this one in the same build has made it already.
			var existing = module.Types.FirstOrDefault(type => type.Namespace == "System.Runtime.CompilerServices" && type.Name == "GenericMemoryUnsafe");
			if (existing != null)
			{
				_as = existing.Methods.First(method => method.Name == "As");
				return;
			}
			var holder = new TypeDefinition("System.Runtime.CompilerServices", "GenericMemoryUnsafe", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit, module.TypeSystem.Object);
			_as = new MethodDefinition("As", MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig, module.TypeSystem.Object)
			{
				ImplAttributes = MethodImplAttributes.AggressiveInlining,
			};
			var parameter = new GenericParameter("T", _as);
			_as.GenericParameters.Add(parameter);
			_as.ReturnType = parameter;
			_as.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None, module.TypeSystem.Object));
			var il = _as.Body.GetILProcessor();
			il.Emit(OpCodes.Ldarg_0);
			il.Emit(OpCodes.Ret);
			holder.Methods.Add(_as);
			module.Types.Add(holder);
			_program.Touch(module);
		}

		TypeDefinition _shared;
		MethodDefinition _sharedAs;
		MethodDefinition _touch;

		/// <summary>
		/// GenericMemoryShared&lt;T&gt;.As(object value): returns its argument as T, like As, for the object a method of
		/// the instance over the stand-in is about to be called on. A method of a class takes for granted that the class
		/// was initialized, because an object of it exists; here the object is of another instance of the class, and the
		/// instance over the stand-in may never have had one. So this type has a static constructor, which the runtime
		/// runs once before the first call of As for each T, and in it asks whether T can take an object, a question
		/// libil2cpp answers only after it has initialized both classes (Class::IsAssignableFrom).
		/// </summary>
		void MakeShared()
		{
			var module = _program.Assemblies.First(assembly => assembly.Name.Name == "mscorlib").MainModule;
			_shared = module.Types.FirstOrDefault(type => type.Namespace == "System.Runtime.CompilerServices" && type.Name == "GenericMemoryShared`1");
			if (_shared != null)
			{
				_sharedAs = _shared.Methods.First(method => method.Name == "As");
				_touch = _shared.Methods.First(method => method.Name == "Touch");
				return;
			}
			var typeType = module.GetType("System.Type");
			var fromHandle = typeType.Methods.First(method => method.Name == "GetTypeFromHandle" && method.Parameters.Count == 1);
			var assignable = typeType.Methods.First(method => method.Name == "IsAssignableFrom" && method.Parameters.Count == 1 && method.Parameters[0].ParameterType.FullName == "System.Type");

			// Not BeforeFieldInit: the static constructor has to have run by the time a static method is called.
			_shared = new TypeDefinition("System.Runtime.CompilerServices", "GenericMemoryShared`1", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed, module.TypeSystem.Object);
			var parameter = new GenericParameter("T", _shared);
			_shared.GenericParameters.Add(parameter);

			var initializer = new MethodDefinition(".cctor", MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName, module.TypeSystem.Void);
			var il = initializer.Body.GetILProcessor();
			il.Emit(OpCodes.Ldtoken, parameter);
			il.Emit(OpCodes.Call, fromHandle);
			il.Emit(OpCodes.Ldtoken, module.TypeSystem.Object);
			il.Emit(OpCodes.Call, fromHandle);
			il.Emit(OpCodes.Callvirt, assignable);
			il.Emit(OpCodes.Pop);
			il.Emit(OpCodes.Ret);
			_shared.Methods.Add(initializer);

			_sharedAs = new MethodDefinition("As", MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig, parameter);
			if (Extended)
			{
				// il2cpp then writes the body into the caller. The check that the static constructor ran is written at the call, and stays.
				_sharedAs.ImplAttributes = MethodImplAttributes.AggressiveInlining;
			}
			_sharedAs.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None, module.TypeSystem.Object));
			il = _sharedAs.Body.GetILProcessor();
			il.Emit(OpCodes.Ldarg_0);
			il.Emit(OpCodes.Ret);
			_shared.Methods.Add(_sharedAs);

			_touch = new MethodDefinition("Touch", MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig, module.TypeSystem.Void);
			if (Extended)
			{
				_touch.ImplAttributes = MethodImplAttributes.AggressiveInlining;
			}
			_touch.Body.GetILProcessor().Emit(OpCodes.Ret);
			_shared.Methods.Add(_touch);

			module.Types.Add(_shared);
			_program.Touch(module);
		}

		// For each generic method, the generic instances it calls a method of directly, written in its own type parameters:
		// its own calls and those of the generic code it calls directly. Such a call takes the context from the caller, so
		// when the method runs for a stand-in the instance over the stand-in is what the called method runs as, and that
		// method takes for granted that its class was initialized.
		readonly Dictionary<MethodDefinition, Dictionary<string, TypeReference>> _needs = new Dictionary<MethodDefinition, Dictionary<string, TypeReference>>();
		// The methods for which that set does not close: a type made from a type made from a type, without end.
		readonly HashSet<MethodDefinition> _unbounded = new HashSet<MethodDefinition>();

		void FindNeeds()
		{
			var edges = new List<(MethodDefinition Caller, MethodDefinition Callee, GenericInstanceType Declaring, GenericInstanceMethod Generic)>();
			foreach (var method in _free.Keys)
			{
				foreach (var instruction in method.Body.Instructions)
				{
					if ((instruction.OpCode != OpCodes.Call && instruction.OpCode != OpCodes.Callvirt) || !(instruction.Operand is MethodReference target))
					{
						continue;
					}
					var declaring = target.DeclaringType as GenericInstanceType;
					var generic = target as GenericInstanceMethod;
					var open = (declaring != null && declaring.ContainsGenericParameter) || (generic != null && generic.GenericArguments.Any(argument => argument.ContainsGenericParameter));
					if (!open)
					{
						// A token without a type parameter names the real instance whatever the method runs for.
						continue;
					}
					var callee = Resolve(target);
					if (callee == null || callee.DeclaringType.IsInterface || IsDelegate(callee.DeclaringType))
					{
						continue;
					}
					if (instruction.OpCode == OpCodes.Callvirt && callee.IsVirtual && !callee.IsFinal && !callee.DeclaringType.IsSealed)
					{
						// Really dispatched: the target runs as the class of the object, which exists and so was initialized.
						continue;
					}
					// The generated code of a method of a struct asks for its class each time (InitializedTypeInfo(method->klass)):
					// it cannot take an object for granted. So only the methods of classes need their instance made ready.
					if (!callee.IsStatic && declaring != null && declaring.ContainsGenericParameter && !(Extended && callee.DeclaringType.IsValueType))
					{
						Need(method, declaring);
					}
					edges.Add((method, callee, declaring, generic));
				}
			}
			for (var changed = true; changed; )
			{
				changed = false;
				foreach (var (caller, callee, declaring, generic) in edges)
				{
					if (_unbounded.Contains(callee))
					{
						changed |= _unbounded.Add(caller);
						continue;
					}
					if (!_needs.TryGetValue(callee, out var needs))
					{
						continue;
					}
					foreach (var pattern in needs.Values.ToList())
					{
						var inCaller = Substitute(pattern, declaring, generic);
						if (inCaller.ContainsGenericParameter)
						{
							changed |= Need(caller, inCaller);
						}
					}
				}
			}
		}

		bool Need(MethodDefinition method, TypeReference pattern)
		{
			if (Depth(pattern) > 4)
			{
				return _unbounded.Add(method);
			}
			if (!_needs.TryGetValue(method, out var needs))
			{
				_needs.Add(method, needs = new Dictionary<string, TypeReference>());
			}
			if (needs.ContainsKey(pattern.FullName))
			{
				return false;
			}
			needs.Add(pattern.FullName, pattern);
			return true;
		}

		static int Depth(TypeReference type)
		{
			switch (type)
			{
				case GenericInstanceType instance:
					return 1 + instance.GenericArguments.Max(Depth);
				case TypeSpecification specification:
					return Depth(specification.ElementType);
				default:
					return 0;
			}
		}

		/// <summary>A type written in the type parameters of a callee, as the token of a call names it.</summary>
		static TypeReference Substitute(TypeReference type, GenericInstanceType declaring, GenericInstanceMethod generic)
		{
			switch (type)
			{
				case GenericParameter parameter:
					if (parameter.Type == GenericParameterType.Method)
					{
						return generic != null && parameter.Position < generic.GenericArguments.Count ? generic.GenericArguments[parameter.Position] : type;
					}
					return declaring != null && parameter.Position < declaring.GenericArguments.Count ? declaring.GenericArguments[parameter.Position] : type;
				case GenericInstanceType instance:
				{
					var copy = new GenericInstanceType(instance.ElementType);
					foreach (var argument in instance.GenericArguments)
					{
						copy.GenericArguments.Add(Substitute(argument, declaring, generic));
					}
					return copy;
				}
				case ArrayType array:
					return new ArrayType(Substitute(array.ElementType, declaring, generic), array.Rank);
				case ByReferenceType reference:
					return new ByReferenceType(Substitute(reference.ElementType, declaring, generic));
				default:
					return type;
			}
		}

		/// <summary>
		/// The instances over a stand-in that a redirected call will run methods of, which have to be initialized first:
		/// what the callee needs, as the new token names it, where that differs from what the old token named. false when
		/// they cannot be listed, and the call has to stay as it is.
		/// </summary>
		bool Inits(MethodDefinition callee, GenericInstanceType declaringBefore, GenericInstanceType declaringAfter, GenericInstanceMethod genericBefore, GenericInstanceMethod genericAfter, out List<TypeReference> inits)
		{
			inits = new List<TypeReference>();
			if (_unbounded.Contains(callee))
			{
				return false;
			}
			if (!_needs.TryGetValue(callee, out var needs))
			{
				return true;
			}
			var found = new Dictionary<string, TypeReference>();
			foreach (var pattern in needs.Values)
			{
				var before = Substitute(pattern, declaringBefore, genericBefore);
				var after = Substitute(pattern, declaringAfter, genericAfter);
				if (after.ContainsGenericParameter)
				{
					return false;
				}
				if (before.FullName != after.FullName)
				{
					found[after.FullName] = after;
				}
			}
			inits.AddRange(found.Values);
			return true;
		}

		/// <summary>
		/// Puts a call of GenericMemoryShared&lt;X&gt;.Touch for each instance in front of a call, so that each is initialized by
		/// the time the call runs. When the call is the instruction the caller had there, a branch may lead to it, so that
		/// instruction becomes the first of these and the call is made anew after them. Returns the instruction that calls.
		/// </summary>
		Instruction Touches(MethodDefinition caller, Instruction call, List<TypeReference> inits, bool original)
		{
			if (inits.Count == 0)
			{
				return call;
			}
			var module = caller.Module;
			var il = caller.Body.GetILProcessor();
			var touches = new List<Instruction>();
			foreach (var type in inits)
			{
				var holder = new GenericInstanceType(module.ImportReference(_shared));
				holder.GenericArguments.Add(module.ImportReference(type, caller));
				touches.Add(Instruction.Create(OpCodes.Call, new MethodReference(_touch.Name, module.TypeSystem.Void, holder)));
			}
			_program.Resized(caller);
			_program.Touch(module);
			if (!original)
			{
				foreach (var touch in touches)
				{
					il.InsertBefore(call, touch);
				}
				return call;
			}
			var made = Instruction.Create(call.OpCode, (MethodReference)call.Operand);
			call.OpCode = OpCodes.Call;
			call.Operand = touches[0].Operand;
			var last = call;
			for (var at = 1; at < touches.Count; at++)
			{
				il.InsertAfter(last, last = touches[at]);
			}
			il.InsertAfter(last, made);
			return made;
		}

		static bool Intrinsic(TypeDefinition type)
		{
			var space = type.Namespace;
			return space == "System.Runtime.InteropServices" || space == "System.Runtime.CompilerServices" || space == "System.Threading"
				|| space == "Unity.Collections.LowLevel.Unsafe" || space == "Internal.Runtime.CompilerServices";
		}

		bool Narrow(MethodDefinition method, byte[] free, GenericParameterType kind)
		{
			var changed = false;
			for (var at = 0; at < free.Length; at++)
			{
				if (free[at] == Observed)
				{
					continue;
				}
				var allowed = Allowed(method, kind, at);
				if (allowed < free[at])
				{
					free[at] = allowed;
					changed = true;
				}
			}
			return changed;
		}

		MethodDefinition Resolve(MethodReference reference)
		{
			if (!_resolved.TryGetValue(reference, out var definition))
			{
				_resolved[reference] = definition = reference.Resolve();
			}
			return definition;
		}

		// A value returned or passed by reference keeps the type the callee gave it, and there is no cast for that.
		// Nor is there one for a struct passed by value: InlinedArray<A> and InlinedArray<B> are two structs in the
		// generated C++, however alike. A reference to an object is a pointer, which il2cpp casts where the types differ.
		static bool ByReference(MethodDefinition method, int position)
		{
			return Fixed(method.ReturnType, position) || method.Parameters.Any(parameter => Fixed(parameter.ParameterType, position));
		}

		static bool Fixed(TypeReference type, int position)
		{
			if (!Mentions(type, GenericParameterType.Method, position))
			{
				return false;
			}
			if (type is ByReferenceType || type is PointerType)
			{
				return true;
			}
			if (type is GenericInstanceType instance)
			{
				var definition = instance.ElementType.Resolve();
				return definition == null || definition.IsValueType;
			}
			return false;
		}

		/// <summary>How much of the parameter the body leaves alone: all of it, all but its default comparer, or not enough.</summary>
		byte Allowed(MethodDefinition method, GenericParameterType kind, int position)
		{
			_scanning = method;
			var body = method.Body;
			foreach (var handler in body.ExceptionHandlers)
			{
				if (handler.CatchType != null && Mentions(handler.CatchType, kind, position))
				{
					return Observed;
				}
			}
			var level = Free;
			foreach (var instruction in body.Instructions)
			{
				var code = instruction.OpCode.Code;
				switch (instruction.Operand)
				{
					case TypeReference type:
						if (!Mentions(type, kind, position))
						{
							break;
						}
						switch (code)
						{
							case Code.Box:
								// A boxed struct is an object of one exact instance. (Boxing the parameter itself, a reference, does nothing.)
								if (Extended && !(type is GenericParameter))
								{
									return Observed;
								}
								break;
							case Code.Initobj:
							case Code.Ldobj:
							case Code.Stobj:
							case Code.Cpobj:
							case Code.Ldelem_Any:
							case Code.Stelem_Any:
								break;
							case Code.Ldelema:
								// The address of an element that is a struct is taken without asking what the array is an array of.
								if (Extended && IsValueTypeInstance(type))
								{
									break;
								}
								if (instruction.Previous == null || instruction.Previous.OpCode != OpCodes.Readonly)
								{
									return Observed;
								}
								break;
							case Code.Constrained:
								// On a reference this is a plain virtual call; what it is called on is checked at the call.
								if (!(type is GenericParameter))
								{
									return Observed;
								}
								break;
							default:
								// newarr, castclass, isinst, unbox.any, ldtoken, sizeof: all of them ask what the type is.
								return Observed;
						}
						break;
					case FieldReference field:
						if (Mentions(field.DeclaringType, kind, position) && code != Code.Ldfld && code != Code.Stfld && code != Code.Ldflda)
						{
							return Observed;
						}
						break;
					case MethodReference target:
					{
						var through = ThroughCall(instruction, target, kind, position);
						if (through == Observed)
						{
							return Observed;
						}
						if (through < level)
						{
							level = through;
						}
						break;
					}
					case CallSite _:
						return Observed;
				}
			}
			return level;
		}

		byte ThroughCall(Instruction instruction, MethodReference target, GenericParameterType kind, int position)
		{
			var generic = target as GenericInstanceMethod;
			var declaring = target.DeclaringType as GenericInstanceType;
			var passed = generic != null && generic.GenericArguments.Any(argument => Mentions(argument, kind, position));
			var declared = Mentions(target.DeclaringType, kind, position);
			if (!passed && !declared)
			{
				return Free;
			}
			if (instruction.OpCode != OpCodes.Call && instruction.OpCode != OpCodes.Callvirt)
			{
				if (Extended && instruction.OpCode == OpCodes.Newobj && declared && !passed && declaring != null)
				{
					// A struct that is made is a value, not an object: its type is only what the code that holds it says.
					// So this is a direct call of the constructor, with the context the caller names.
					var constructor = Resolve(target);
					if (constructor != null && constructor.DeclaringType.IsValueType && _free.TryGetValue(constructor, out var made))
					{
						var free = Free;
						for (var at = 0; at < declaring.GenericArguments.Count && at < made.OfType.Length; at++)
						{
							free = Passed(free, declaring.GenericArguments[at], made.OfType[at], kind, position);
						}
						return free;
					}
				}
				// newobj, ldftn, ldtoken: an object or a pointer of one exact instance.
				return Observed;
			}
			var definition = target.DeclaringType.GetElementType().FullName;
			if (declared && !passed && declaring != null && declaring.GenericArguments.Count == 1 && Is(declaring.GenericArguments[0], kind, position)
				&& target.Name == "get_Default" && (definition == "System.Collections.Generic.EqualityComparer`1" || definition == "System.Collections.Generic.Comparer`1"))
			{
				// The default comparer of the parameter itself. It may only be called: what is called on it is dispatched by
				// the comparer object, but a comparer that was stored or handed on would be the stand-in's where the type's is expected.
				return OnlyCalled(_scanning, instruction) ? ComparerOnly : Observed;
			}
			if (passed && !declared && definition == "System.Runtime.CompilerServices.RuntimeHelpers" && target.Name == "IsReferenceOrContainsReferences" && generic.GenericArguments.All(argument => Is(argument, kind, position)))
			{
				// Asked of a reference type, the answer is the same whichever it is, and only reference types are replaced.
				return Free;
			}
			var callee = Resolve(target);
			if (callee == null)
			{
				return Observed;
			}
			_free.TryGetValue(callee, out var freedom);
			var level = Free;
			if (declared)
			{
				if (declaring == null || callee.DeclaringType.IsInterface)
				{
					return Observed;
				}
				if (IsDelegate(callee.DeclaringType))
				{
					// Invoke runs what the delegate holds and takes nothing from the caller.
					if (callee.Name != "Invoke")
					{
						return Observed;
					}
				}
				else if (instruction.OpCode == OpCodes.Callvirt && callee.IsVirtual && !callee.IsFinal && !callee.DeclaringType.IsSealed)
				{
					// Really dispatched: the slot of the object gives the target and its context.
				}
				else
				{
					// Called directly with the context the caller names, so the target must not be able to tell either.
					if (freedom == null)
					{
						return Observed;
					}
					for (var at = 0; at < declaring.GenericArguments.Count; at++)
					{
						level = Passed(level, declaring.GenericArguments[at], freedom.OfType[at], kind, position);
					}
				}
			}
			if (passed)
			{
				if (freedom == null)
				{
					return Observed;
				}
				for (var at = 0; at < generic.GenericArguments.Count; at++)
				{
					level = Passed(level, generic.GenericArguments[at], freedom.OfMethod[at], kind, position);
				}
			}
			return level;
		}

		MethodDefinition _scanning;

		/// <summary>
		/// Whether the value an instruction pushes is used for nothing but calls made on it: either the next thing that
		/// takes it off the stack is a call it is the object of, or it goes into a local every read of which is that.
		/// </summary>
		static bool OnlyCalled(MethodDefinition method, Instruction push)
		{
			var instructions = method.Body.Instructions;
			var targets = new HashSet<Instruction>();
			foreach (var instruction in instructions)
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
				targets.Add(handler.HandlerStart);
				targets.Add(handler.TryStart);
			}
			return OnlyCalled(method, push, targets, 0);
		}

		static bool OnlyCalled(MethodDefinition method, Instruction push, HashSet<Instruction> targets, int depth)
		{
			// The value is followed along a straight line of code. "over" counts the values above it on the stack.
			var over = 0;
			for (var at = push.Next; at != null; at = at.Next)
			{
				if (targets.Contains(at) || at.OpCode.FlowControl == FlowControl.Branch || at.OpCode.FlowControl == FlowControl.Cond_Branch || at.OpCode.FlowControl == FlowControl.Throw)
				{
					return false;
				}
				if (!Stack(method, at, out var pops, out var pushes))
				{
					return false;
				}
				if (pops <= over)
				{
					over += pushes - pops;
					continue;
				}
				// This instruction takes the value.
				var code = at.OpCode.Code;
				if ((code == Code.Call || code == Code.Callvirt) && at.Operand is MethodReference called && called.HasThis && pops == over + 1 && pops == called.Parameters.Count + 1)
				{
					return true;
				}
				if (over == 0 && depth == 0 && Local(method, at, out var local, out var store) && store)
				{
					foreach (var other in method.Body.Instructions)
					{
						if (!Local(method, other, out var same, out var stores) || same != local)
						{
							if (other.Operand == local && (other.OpCode.Code == Code.Ldloca || other.OpCode.Code == Code.Ldloca_S))
							{
								return false;
							}
							continue;
						}
						if (stores ? other != at : !OnlyCalled(method, other, targets, 1))
						{
							return false;
						}
					}
					return true;
				}
				return false;
			}
			return false;
		}

		static bool Local(MethodDefinition method, Instruction instruction, out VariableDefinition local, out bool store)
		{
			local = null;
			store = false;
			var variables = method.Body.Variables;
			switch (instruction.OpCode.Code)
			{
				case Code.Stloc_0:
					store = true;
					local = variables.Count > 0 ? variables[0] : null;
					break;
				case Code.Stloc_1:
					store = true;
					local = variables.Count > 1 ? variables[1] : null;
					break;
				case Code.Stloc_2:
					store = true;
					local = variables.Count > 2 ? variables[2] : null;
					break;
				case Code.Stloc_3:
					store = true;
					local = variables.Count > 3 ? variables[3] : null;
					break;
				case Code.Stloc:
				case Code.Stloc_S:
					store = true;
					local = instruction.Operand as VariableDefinition;
					break;
				case Code.Ldloc_0:
					local = variables.Count > 0 ? variables[0] : null;
					break;
				case Code.Ldloc_1:
					local = variables.Count > 1 ? variables[1] : null;
					break;
				case Code.Ldloc_2:
					local = variables.Count > 2 ? variables[2] : null;
					break;
				case Code.Ldloc_3:
					local = variables.Count > 3 ? variables[3] : null;
					break;
				case Code.Ldloc:
				case Code.Ldloc_S:
					local = instruction.Operand as VariableDefinition;
					break;
			}
			return local != null;
		}

		// How many values an instruction takes off the stack and puts on it; false where that is not a fixed number.
		static bool Stack(MethodDefinition method, Instruction instruction, out int pops, out int pushes)
		{
			pops = 0;
			pushes = 0;
			var code = instruction.OpCode.Code;
			if (instruction.Operand is MethodReference called && (code == Code.Call || code == Code.Callvirt || code == Code.Newobj))
			{
				pops = called.Parameters.Count + (called.HasThis && code != Code.Newobj ? 1 : 0);
				pushes = code == Code.Newobj || called.ReturnType.MetadataType != MetadataType.Void ? 1 : 0;
				return true;
			}
			if (code == Code.Ret)
			{
				pops = method.ReturnType.MetadataType != MetadataType.Void ? 1 : 0;
				return true;
			}
			switch (instruction.OpCode.StackBehaviourPop)
			{
				case StackBehaviour.Pop0:
					pops = 0;
					break;
				case StackBehaviour.Pop1:
				case StackBehaviour.Popi:
				case StackBehaviour.Popref:
					pops = 1;
					break;
				case StackBehaviour.Pop1_pop1:
				case StackBehaviour.Popi_pop1:
				case StackBehaviour.Popi_popi:
				case StackBehaviour.Popi_popi8:
				case StackBehaviour.Popi_popr4:
				case StackBehaviour.Popi_popr8:
				case StackBehaviour.Popref_pop1:
				case StackBehaviour.Popref_popi:
					pops = 2;
					break;
				case StackBehaviour.Popi_popi_popi:
				case StackBehaviour.Popref_popi_popi:
				case StackBehaviour.Popref_popi_popi8:
				case StackBehaviour.Popref_popi_popr4:
				case StackBehaviour.Popref_popi_popr8:
				case StackBehaviour.Popref_popi_popref:
					pops = 3;
					break;
				default:
					return false;
			}
			switch (instruction.OpCode.StackBehaviourPush)
			{
				case StackBehaviour.Push0:
					pushes = 0;
					break;
				case StackBehaviour.Push1:
				case StackBehaviour.Pushi:
				case StackBehaviour.Pushi8:
				case StackBehaviour.Pushr4:
				case StackBehaviour.Pushr8:
				case StackBehaviour.Pushref:
					pushes = 1;
					break;
				case StackBehaviour.Push1_push1:
					pushes = 2;
					break;
				default:
					return false;
			}
			return true;
		}

		// What passing the parameter on as this argument leaves of it. Asking for the default comparer is carried over only
		// when the argument is the parameter itself: the comparer of a type made from it is another matter.
		static byte Passed(byte level, TypeReference argument, byte callee, GenericParameterType kind, int position)
		{
			if (!Mentions(argument, kind, position))
			{
				return level;
			}
			if (callee == ComparerOnly && !Is(argument, kind, position))
			{
				return Observed;
			}
			return callee < level ? callee : level;
		}

		static bool Is(TypeReference type, GenericParameterType kind, int position)
		{
			return type is GenericParameter parameter && parameter.Type == kind && parameter.Position == position;
		}

		static bool IsDelegate(TypeDefinition type)
		{
			return type.BaseType != null && type.BaseType.FullName == "System.MulticastDelegate";
		}

		static bool Mentions(TypeReference type, GenericParameterType kind, int position)
		{
			switch (type)
			{
				case null:
					return false;
				case GenericParameter parameter:
					return parameter.Type == kind && parameter.Position == position;
				case GenericInstanceType instance:
					return instance.GenericArguments.Any(argument => Mentions(argument, kind, position));
				case TypeSpecification specification:
					return Mentions(specification.ElementType, kind, position);
				default:
					return false;
			}
		}

		// Whether the return type of the callee names a parameter whose argument was replaced at this call.
		static bool Replaced(TypeReference returnType, GenericInstanceMethod before, GenericInstanceMethod after)
		{
			for (var at = 0; at < before.GenericArguments.Count; at++)
			{
				if (!ReferenceEquals(before.GenericArguments[at], after.GenericArguments[at]) && Mentions(returnType, GenericParameterType.Method, at))
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>A type known to be a reference type where the call is written.</summary>
		static bool IsReference(TypeReference type)
		{
			switch (type)
			{
				case GenericParameter parameter:
					return parameter.HasReferenceTypeConstraint;
				case ArrayType _:
					return true;
				case GenericInstanceType instance:
				{
					var definition = instance.ElementType.Resolve();
					return definition != null && !definition.IsValueType;
				}
				case TypeSpecification _:
					return false;
				default:
				{
					var definition = type.Resolve();
					return definition != null && !definition.IsValueType;
				}
			}
		}

		/// <summary>The type every reference type is replaced with: a constraint when there is one, so that it holds; else object.</summary>
		static TypeReference StandIn(GenericParameter parameter, ModuleDefinition module)
		{
			foreach (var constraint in parameter.Constraints)
			{
				var type = constraint.ConstraintType;
				if (type is TypeSpecification || type is GenericParameter)
				{
					continue;
				}
				var definition = type.Resolve();
				if (definition != null && !definition.IsValueType && definition.FullName != "System.ValueType")
				{
					return module.ImportReference(type);
				}
			}
			return module.TypeSystem.Object;
		}

		/// <summary>A type of the callee's signature as the caller wrote it: the callee's parameters replaced by the call's arguments.</summary>
		sealed class CallContext
		{
			readonly GenericInstanceMethod _call;

			public CallContext(GenericInstanceMethod call)
			{
				_call = call;
			}

			public TypeReference Type(TypeReference type)
			{
				switch (type)
				{
					case GenericParameter parameter:
						return parameter.Type == GenericParameterType.Method ? _call.GenericArguments[parameter.Position] : type;
					case GenericInstanceType instance:
					{
						var copy = new GenericInstanceType(instance.ElementType);
						foreach (var argument in instance.GenericArguments)
						{
							copy.GenericArguments.Add(Type(argument));
						}
						return copy;
					}
					case ArrayType array:
						return new ArrayType(Type(array.ElementType), array.Rank);
					default:
						return type;
				}
			}
		}
	}
}
