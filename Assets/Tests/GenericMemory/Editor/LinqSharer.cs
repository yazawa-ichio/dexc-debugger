using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace GenericMemory.Editor
{
	/// <summary>
	/// The step "linq": a chain of System.Linq.Enumerable calls over a List&lt;Foo&gt; or a Foo[] runs over object inside,
	/// where nothing outside the chain can see the difference.
	///
	///   list.Where(x => x.Id > 1).Select(x => x.Name).Count()        every call runs for object; the result is an int
	///   list.OrderBy(x => x.Id).ToList()                             OrderBy runs for object; the List&lt;Foo&gt; is made as one
	///   list.FirstOrDefault(x => x.Id == 2)                          runs for object; what comes back is cast to Foo
	///
	/// libil2cpp makes, for every element type, each generic method of Enumerable that is called and each iterator class
	/// behind it. Run for object, all element types share them.
	///
	/// What is kept as it was:
	/// - The list or array stays what it is. It is handed to the chain as it is (an array is an object[] already; a list
	///   goes into the list iterators of Enumerable as a List&lt;object&gt;, which is what the shared code sees anyway).
	/// - What the chain hands back to the code around it has the type the code names: an element is cast, a list or an
	///   array is made with the element type, a result that does not name the element type (int, IEnumerable&lt;string&gt;)
	///   is what the call for object returns already.
	/// - A sequence of the element type, IEnumerable&lt;Foo&gt;, that leaves the chain (stored, returned, enumerated by a
	///   foreach, passed to anything but another call that is rewritten) would be an IEnumerable&lt;object&gt; that is not an
	///   IEnumerable&lt;Foo&gt;. Such a call is not rewritten, and neither is what feeds it.
	/// - A call whose meaning depends on the element type is not rewritten: Cast, OfType, the grouping and joining calls
	///   whose lambdas take IGrouping&lt;K, Foo&gt;, anything that takes a comparer of the element type. A type that brings
	///   its own comparison (IEquatable, IComparable) is never replaced, so Distinct, Contains, Max and the like compare
	///   as before.
	/// - A call on a list itself that Enumerable answers from ICollection or IList (Count(), First(), ToList(), ...) is
	///   left alone: for object it would have to walk the list.
	///
	/// A lambda of the chain is still made as a Func&lt;Foo, ...&gt;. It is called as a Func&lt;object, ...&gt;, which is the same
	/// code for a delegate: Invoke takes what the delegate holds and nothing from its class.
	/// </summary>
	sealed class LinqSharer
	{
		const string EnumerableName = "System.Linq.Enumerable";
		const string HelperName = "GenericMemoryLinq";

		static readonly HashSet<string> s_never = new HashSet<string>
		{
			"Cast", "OfType", "AsEnumerable", "Empty", "Repeat", "Range", "ToDictionary", "ToLookup", "GroupBy", "GroupJoin", "Join", "Zip", "ToHashSet",
		};

		// Without a lambda, Enumerable answers these from ICollection<T> or IList<T> when the source has them.
		static readonly HashSet<string> s_collectionPath = new HashSet<string>
		{
			"Count", "LongCount", "ElementAt", "ElementAtOrDefault", "First", "FirstOrDefault", "Last", "LastOrDefault", "Single", "SingleOrDefault",
			"ToList", "ToArray", "Reverse", "Contains", "SequenceEqual",
		};

		// With a predicate, these are the same as Where(predicate) followed by the call without one: the same items, the
		// same order, the same result when nothing matches. (First, Single and Last are not: they say another thing when
		// nothing matches.)
		static readonly HashSet<string> s_overWhere = new HashSet<string> { "Any", "Count", "LongCount", "FirstOrDefault", "LastOrDefault" };

		enum ParameterKind { Plain, Sequence, Delegate, Bare }

		enum ResultKind { Exact, Element, Intermediate, List, Array }

		enum SourceKind { None, List, Array, Intermediate }

		sealed class Value
		{
			public Instruction Producer;
			public bool Merged;
			public readonly List<Instruction> Consumers = new List<Instruction>();
		}

		sealed class Site
		{
			public Instruction Call;
			public GenericInstanceMethod Method;
			public MethodDefinition Definition;
			public bool[] Replaced;
			public ParameterKind[] Kinds;
			public SourceKind[] Sources;
			public TypeReference[] SourceTypes;
			public ResultKind Result;
			public TypeReference Element;
			public Value[] Arguments;
			public Value Made;
			public bool Viable = true;
			public string Why;
		}

		readonly WholeProgram _program;
		readonly StringBuilder _log;
		readonly Dictionary<string, bool> _ownComparison = new Dictionary<string, bool>();
		readonly Dictionary<string, int> _rewritten = new Dictionary<string, int>();
		readonly Dictionary<string, int> _kept = new Dictionary<string, int>();
		readonly Dictionary<string, int[]> _byAssembly = new Dictionary<string, int[]>();

		ModuleDefinition _core;
		TypeDefinition _enumerable;
		MethodDefinition _as;
		MethodDefinition _wrapList;
		MethodDefinition _whereList;
		MethodDefinition _selectList;
		MethodDefinition _toList;
		MethodDefinition _toArray;

		public LinqSharer(WholeProgram program, StringBuilder log)
		{
			_program = program;
			_log = log;
		}

		public void Run()
		{
			var core = _program.Assemblies.FirstOrDefault(assembly => assembly.Name.Name == "System.Core");
			_enumerable = core?.MainModule.GetType(EnumerableName);
			if (_enumerable == null)
			{
				_log.AppendLine("linq: no System.Linq.Enumerable in this build");
				return;
			}
			_core = core.MainModule;
			MakeAs();
			if (!MakeHelpers())
			{
				_log.AppendLine("linq: Enumerable is not the one this step knows (its list iterators were not found); nothing was rewritten");
				return;
			}

			var sites = 0;
			var methods = 0;
			foreach (var caller in _program.Bodies().ToArray())
			{
				if (caller.DeclaringType == _enumerable || caller.DeclaringType.DeclaringType == _enumerable)
				{
					continue;
				}
				if (!caller.Body.Instructions.Any(IsEnumerableCall))
				{
					continue;
				}
				var done = Rewrite(caller);
				if (done > 0)
				{
					sites += done;
					methods++;
				}
			}
			_log.AppendLine("linq: " + sites + " calls of Enumerable in " + methods + " methods now run for object");
			foreach (var pair in _rewritten.OrderByDescending(pair => pair.Value).Take(40))
			{
				_log.AppendLine("  shared " + pair.Value + "  " + pair.Key);
			}
			foreach (var pair in _kept.OrderByDescending(pair => pair.Value).Take(40))
			{
				_log.AppendLine("  kept " + pair.Value + "  " + pair.Key);
			}
			foreach (var pair in _byAssembly.OrderByDescending(pair => pair.Value[0] + pair.Value[1]).Take(25))
			{
				_log.AppendLine("  in " + pair.Key + ": shared " + pair.Value[0] + ", kept " + pair.Value[1]);
			}
		}

		static bool IsEnumerableCall(Instruction instruction)
		{
			return instruction.OpCode == OpCodes.Call && instruction.Operand is GenericInstanceMethod method && method.DeclaringType.FullName == EnumerableName;
		}

		int Rewrite(MethodDefinition caller)
		{
			if (!Simulate(caller, out var taken, out var made))
			{
				Keep(caller, "(a method whose stack this step cannot follow)", 1);
				return 0;
			}
			var sites = new List<Site>();
			var byCall = new Dictionary<Instruction, Site>();
			var byValue = new Dictionary<Value, Site>();
			foreach (var instruction in caller.Body.Instructions)
			{
				if (!IsEnumerableCall(instruction) || !taken.TryGetValue(instruction, out var arguments))
				{
					continue;
				}
				var site = Read(caller, instruction, arguments);
				if (site == null)
				{
					continue;
				}
				made.TryGetValue(instruction, out site.Made);
				sites.Add(site);
				byCall.Add(instruction, site);
				if (site.Made != null)
				{
					byValue.Add(site.Made, site);
				}
			}
			if (sites.Count == 0)
			{
				return 0;
			}
			// Where each sequence comes from.
			foreach (var site in sites.Where(site => site.Viable))
			{
				for (var at = 0; at < site.Kinds.Length; at++)
				{
					if (site.Kinds[at] != ParameterKind.Sequence)
					{
						continue;
					}
					var value = site.Arguments[at];
					if (byValue.TryGetValue(value, out var producer) && producer.Result == ResultKind.Intermediate)
					{
						site.Sources[at] = SourceKind.Intermediate;
						continue;
					}
					var element = SequenceElement(site, at);
					var type = value.Merged ? null : TypeOf(caller, value);
					if (type is GenericInstanceType list && list.ElementType.FullName == "System.Collections.Generic.List`1" && list.GenericArguments[0].FullName == element.FullName)
					{
						site.Sources[at] = SourceKind.List;
						site.SourceTypes[at] = type;
					}
					else if (type is ArrayType array && array.IsVector && array.ElementType.FullName == element.FullName)
					{
						site.Sources[at] = SourceKind.Array;
						site.SourceTypes[at] = type;
					}
					else
					{
						Fail(site, "the source is not a list, an array or a call of the chain");
					}
				}
				if (site.Viable && s_collectionPath.Contains(site.Definition.Name) && !site.Kinds.Contains(ParameterKind.Delegate)
					&& (site.Sources.Contains(SourceKind.List) || site.Sources.Contains(SourceKind.Array)))
				{
					Fail(site, "answered from the list or array itself (ICollection, IList)");
				}
			}
			// A sequence of the element type has to stay inside the chain, and a chain needs all its links.
			for (var changed = true; changed; )
			{
				changed = false;
				foreach (var site in sites.Where(site => site.Viable))
				{
					for (var at = 0; at < site.Kinds.Length; at++)
					{
						if (site.Sources[at] == SourceKind.Intermediate && !byValue[site.Arguments[at]].Viable)
						{
							Fail(site, "what feeds it stays as it was");
						}
					}
					if (site.Viable && site.Result == ResultKind.Intermediate)
					{
						var value = site.Made;
						Site consumer = null;
						var inside = value != null && !value.Merged && value.Consumers.Count == 1 && byCall.TryGetValue(value.Consumers[0], out consumer) && consumer.Viable
							&& Enumerable.Range(0, consumer.Kinds.Length).Count(at => ReferenceEquals(consumer.Arguments[at], value)) == 1
							&& Enumerable.Range(0, consumer.Kinds.Length).Any(at => ReferenceEquals(consumer.Arguments[at], value) && consumer.Kinds[at] == ParameterKind.Sequence);
						if (!inside)
						{
							Fail(site, "its sequence of the element type leaves the chain");
						}
					}
					changed |= !site.Viable;
				}
			}
			var count = 0;
			foreach (var site in sites)
			{
				if (!site.Viable)
				{
					Keep(caller, site.Definition.Name + ": " + site.Why, 1);
					continue;
				}
				Apply(caller, site);
				count++;
				_rewritten[site.Definition.Name] = _rewritten.TryGetValue(site.Definition.Name, out var done) ? done + 1 : 1;
			}
			if (count > 0)
			{
				Counts(caller)[0] += count;
				_program.Resized(caller);
				_program.Touch(caller.Module);
			}
			return count;
		}

		static void Fail(Site site, string why)
		{
			site.Viable = false;
			site.Why = site.Why ?? why;
		}

		int[] Counts(MethodDefinition caller)
		{
			var name = caller.Module.Assembly.Name.Name;
			if (!_byAssembly.TryGetValue(name, out var counts))
			{
				_byAssembly.Add(name, counts = new int[2]);
			}
			return counts;
		}

		void Keep(MethodDefinition caller, string reason, int count)
		{
			_kept[reason] = _kept.TryGetValue(reason, out var known) ? known + count : count;
			Counts(caller)[1] += count;
		}

		/// <summary>What a call of Enumerable would be if it ran for object: null when no type argument of it would be replaced.</summary>
		Site Read(MethodDefinition caller, Instruction instruction, Value[] arguments)
		{
			var method = (GenericInstanceMethod)instruction.Operand;
			var definition = method.ElementMethod.Resolve();
			if (definition == null || definition.DeclaringType != _enumerable || definition.Parameters.Count != arguments.Length)
			{
				return null;
			}
			var replaced = new bool[method.GenericArguments.Count];
			var any = false;
			for (var at = 0; at < replaced.Length; at++)
			{
				replaced[at] = Replaceable(method.GenericArguments[at]);
				any |= replaced[at];
			}
			if (!any)
			{
				return null;
			}
			var site = new Site
			{
				Call = instruction,
				Method = method,
				Definition = definition,
				Replaced = replaced,
				Kinds = new ParameterKind[arguments.Length],
				Sources = new SourceKind[arguments.Length],
				SourceTypes = new TypeReference[arguments.Length],
				Arguments = arguments,
			};
			if (s_never.Contains(definition.Name))
			{
				Fail(site, "its meaning depends on the element type");
				return site;
			}
			if (instruction.Previous != null && instruction.Previous.OpCode.OpCodeType == OpCodeType.Prefix)
			{
				Fail(site, "a prefixed call");
				return site;
			}
			for (var at = 0; at < site.Kinds.Length; at++)
			{
				var type = definition.Parameters[at].ParameterType;
				if (!Mentions(type, replaced))
				{
					site.Kinds[at] = ParameterKind.Plain;
				}
				else if (type is GenericParameter)
				{
					site.Kinds[at] = ParameterKind.Bare;
				}
				else if (IsSequence(type))
				{
					site.Kinds[at] = ParameterKind.Sequence;
				}
				else if (type is GenericInstanceType instance && IsDelegate(instance) && instance.GenericArguments.All(argument => argument is GenericParameter || !Mentions(argument, replaced)))
				{
					site.Kinds[at] = ParameterKind.Delegate;
				}
				else
				{
					Fail(site, "a parameter made of the element type: " + type.Name);
					return site;
				}
			}
			var returns = definition.ReturnType;
			if (!Mentions(returns, replaced))
			{
				site.Result = ResultKind.Exact;
			}
			else if (returns is GenericParameter parameter)
			{
				site.Result = ResultKind.Element;
				site.Element = method.GenericArguments[parameter.Position];
			}
			else if (IsSequence(returns))
			{
				site.Result = ResultKind.Intermediate;
			}
			else if (definition.Name == "ToList" && definition.Parameters.Count == 1 && returns is GenericInstanceType list && list.GenericArguments.Count == 1 && list.GenericArguments[0] is GenericParameter of)
			{
				site.Result = ResultKind.List;
				site.Element = method.GenericArguments[of.Position];
			}
			else if (definition.Name == "ToArray" && definition.Parameters.Count == 1 && returns is ArrayType array && array.IsVector && array.ElementType is GenericParameter ofArray)
			{
				site.Result = ResultKind.Array;
				site.Element = method.GenericArguments[ofArray.Position];
			}
			else
			{
				Fail(site, "a result made of the element type: " + returns.Name);
			}
			return site;
		}

		/// <summary>IEnumerable&lt;T&gt; or IOrderedEnumerable&lt;T&gt; of a type parameter of the method.</summary>
		static bool IsSequence(TypeReference type)
		{
			if (!(type is GenericInstanceType instance) || instance.GenericArguments.Count != 1 || !(instance.GenericArguments[0] is GenericParameter))
			{
				return false;
			}
			var name = instance.ElementType.FullName;
			return name == "System.Collections.Generic.IEnumerable`1" || name == "System.Linq.IOrderedEnumerable`1";
		}

		static bool IsDelegate(GenericInstanceType type)
		{
			var definition = type.ElementType.Resolve();
			return definition != null && definition.BaseType != null && definition.BaseType.FullName == "System.MulticastDelegate";
		}

		static TypeReference SequenceElement(Site site, int at)
		{
			var parameter = (GenericParameter)((GenericInstanceType)site.Definition.Parameters[at].ParameterType).GenericArguments[0];
			return site.Method.GenericArguments[parameter.Position];
		}

		static bool Mentions(TypeReference type, bool[] replaced)
		{
			switch (type)
			{
				case GenericParameter parameter:
					return parameter.Type == GenericParameterType.Method && parameter.Position < replaced.Length && replaced[parameter.Position];
				case GenericInstanceType instance:
					return instance.GenericArguments.Any(argument => Mentions(argument, replaced));
				case TypeSpecification specification:
					return Mentions(specification.ElementType, replaced);
				default:
					return false;
			}
		}

		/// <summary>
		/// A class or an interface known where the call is written, other than object, that brings no comparison of its own:
		/// for such a type everything Enumerable does with object is what it does with the type.
		/// </summary>
		bool Replaceable(TypeReference type)
		{
			if (type.ContainsGenericParameter || type is ArrayType || type.FullName == "System.Object")
			{
				return false;
			}
			if (type is TypeSpecification && !(type is GenericInstanceType))
			{
				return false;
			}
			var definition = type.Resolve();
			if (definition == null || definition.IsValueType)
			{
				return false;
			}
			if (definition.BaseType != null && definition.BaseType.FullName == "System.MulticastDelegate")
			{
				return false;
			}
			return !HasOwnComparison(type, definition);
		}

		bool HasOwnComparison(TypeReference type, TypeDefinition definition)
		{
			if (_ownComparison.TryGetValue(type.FullName, out var known))
			{
				return known;
			}
			var own = false;
			for (var at = definition; ; )
			{
				if (at == null || Compares(at, 0))
				{
					own = true;
					break;
				}
				if (at.BaseType == null)
				{
					break;
				}
				at = at.BaseType.Resolve();
			}
			_ownComparison[type.FullName] = own;
			return own;
		}

		static bool Compares(TypeDefinition type, int depth)
		{
			foreach (var implemented in type.Interfaces)
			{
				var name = implemented.InterfaceType.GetElementType().FullName;
				if (name == "System.IEquatable`1" || name == "System.IComparable`1" || name == "System.IComparable")
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

		// ---- the rewrite of one call

		void Apply(MethodDefinition caller, Site site)
		{
			var module = caller.Module;
			var il = caller.Body.GetILProcessor();
			var definition = site.Definition;
			var count = site.Kinds.Length;
			var after = new GenericInstanceMethod(site.Method.ElementMethod);
			for (var at = 0; at < site.Replaced.Length; at++)
			{
				after.GenericArguments.Add(site.Replaced[at] ? module.TypeSystem.Object : site.Method.GenericArguments[at]);
			}
			// The arguments come off the stack into locals and go back, each as what the call for object takes.
			var locals = new VariableDefinition[count];
			for (var at = 0; at < count; at++)
			{
				TypeReference type;
				if (site.Kinds[at] == ParameterKind.Sequence)
				{
					type = site.Sources[at] == SourceKind.Intermediate ? InCall(definition.Parameters[at].ParameterType, after) : site.SourceTypes[at];
				}
				else
				{
					type = InCall(definition.Parameters[at].ParameterType, site.Method);
				}
				locals[at] = new VariableDefinition(module.ImportReference(type, caller));
				caller.Body.Variables.Add(locals[at]);
			}
			var call = site.Call;
			call.OpCode = OpCodes.Stloc;
			call.Operand = locals[count - 1];
			var last = call;
			for (var at = count - 2; at >= 0; at--)
			{
				il.InsertAfter(last, last = Instruction.Create(OpCodes.Stloc, locals[at]));
			}

			// Where and Select on a list go straight into the list iterators, as they do in Enumerable for the element type.
			var onList = count == 2 && site.Sources[0] == SourceKind.List && site.Kinds[1] == ParameterKind.Delegate
				&& definition.Parameters[1].ParameterType is GenericInstanceType lambda && lambda.GenericArguments.Count == 2 && lambda.ElementType.FullName == "System.Func`2";
			var whereOnList = onList && definition.Name == "Where" && site.Result == ResultKind.Intermediate;
			var selectOnList = onList && definition.Name == "Select" && definition.GenericParameters.Count == 2 && site.Replaced[0];
			// Sum, Max and the like with a selector are written in Enumerable as M(Select(source, selector)). On a list the
			// same is written here, with the Select for lists, so that the list is walked as it was.
			MethodReference outer = null;
			TypeReference selected = null;
			if (onList && !whereOnList && !selectOnList && site.Replaced[0] && (site.Result == ResultKind.Exact || site.Result == ResultKind.Element) && OverSelect(definition, out var inner, out var selectedIn))
			{
				selected = InCall(selectedIn, after);
				if (inner is GenericInstanceMethod generic)
				{
					var made = new GenericInstanceMethod(module.ImportReference(generic.ElementMethod));
					foreach (var argument in generic.GenericArguments)
					{
						made.GenericArguments.Add(module.ImportReference(InCall(argument, after), caller));
					}
					outer = made;
				}
				else
				{
					outer = module.ImportReference(inner);
				}
				selectOnList = true;
			}
			// Any, Count, FirstOrDefault and LastOrDefault with a predicate, on a list: the Where for lists, then the call
			// without the predicate. The list is walked by its own enumerator, as Where walks it.
			if (onList && !whereOnList && !selectOnList && site.Replaced[0] && definition.GenericParameters.Count == 1 && s_overWhere.Contains(definition.Name)
				&& ((GenericInstanceType)definition.Parameters[1].ParameterType).GenericArguments[1].MetadataType == MetadataType.Boolean)
			{
				var plain = _enumerable.Methods.FirstOrDefault(method => method.Name == definition.Name && method.Parameters.Count == 1 && method.GenericParameters.Count == 1);
				if (plain != null)
				{
					var made = new GenericInstanceMethod(module.ImportReference(plain));
					made.GenericArguments.Add(module.TypeSystem.Object);
					outer = made;
					whereOnList = true;
				}
			}

			var listOfObject = new GenericInstanceType(module.ImportReference(_wrapList.Parameters[0].ParameterType.GetElementType()));
			listOfObject.GenericArguments.Add(module.TypeSystem.Object);
			for (var at = 0; at < count; at++)
			{
				il.InsertAfter(last, last = Instruction.Create(OpCodes.Ldloc, locals[at]));
				if (site.Kinds[at] == ParameterKind.Sequence && site.Sources[at] == SourceKind.List)
				{
					il.InsertAfter(last, last = Instruction.Create(OpCodes.Call, As(module, listOfObject)));
					if (!whereOnList && !selectOnList)
					{
						il.InsertAfter(last, last = Instruction.Create(OpCodes.Call, module.ImportReference(_wrapList)));
					}
				}
				else if (site.Kinds[at] == ParameterKind.Delegate)
				{
					il.InsertAfter(last, last = Instruction.Create(OpCodes.Call, As(module, module.ImportReference(InCall(definition.Parameters[at].ParameterType, after), caller))));
				}
			}

			MethodReference target;
			if (whereOnList)
			{
				target = module.ImportReference(_whereList);
			}
			else if (selectOnList)
			{
				var select = new GenericInstanceMethod(module.ImportReference(_selectList));
				select.GenericArguments.Add(module.ImportReference(selected ?? after.GenericArguments[1], caller));
				target = select;
			}
			else if (site.Result == ResultKind.List || site.Result == ResultKind.Array)
			{
				var exact = new GenericInstanceMethod(module.ImportReference(site.Result == ResultKind.List ? _toList : _toArray));
				exact.GenericArguments.Add(module.ImportReference(site.Element, caller));
				target = exact;
			}
			else
			{
				var imported = new GenericInstanceMethod(site.Method.ElementMethod);
				foreach (var argument in after.GenericArguments)
				{
					imported.GenericArguments.Add(module.ImportReference(argument, caller));
				}
				target = imported;
			}
			il.InsertAfter(last, last = Instruction.Create(OpCodes.Call, target));
			if (outer != null)
			{
				il.InsertAfter(last, last = Instruction.Create(OpCodes.Call, outer));
			}
			if (site.Result == ResultKind.Element)
			{
				// What comes back is an element of the source, so the cast states a fact and gives it its type back.
				il.InsertAfter(last, Instruction.Create(OpCodes.Castclass, module.ImportReference(site.Element, caller)));
			}
		}

		/// <summary>Whether the body of a method of Enumerable is "return Inner(Select(source, selector));": then the inner call and what the Select selects.</summary>
		static bool OverSelect(MethodDefinition definition, out MethodReference inner, out TypeReference selected)
		{
			inner = null;
			selected = null;
			var body = definition.Body.Instructions;
			if (definition.Parameters.Count != 2 || body.Count != 5 || body[0].OpCode != OpCodes.Ldarg_0 || body[1].OpCode != OpCodes.Ldarg_1 || body[2].OpCode != OpCodes.Call
				|| body[3].OpCode != OpCodes.Call || body[4].OpCode != OpCodes.Ret)
			{
				return false;
			}
			if (!(body[2].Operand is GenericInstanceMethod select) || select.Name != "Select" || select.DeclaringType.FullName != EnumerableName || select.GenericArguments.Count != 2
				|| !(select.GenericArguments[0] is GenericParameter source) || source.Position != 0 || select.Parameters.Count != 2)
			{
				return false;
			}
			inner = (MethodReference)body[3].Operand;
			selected = select.GenericArguments[1];
			return inner.DeclaringType.FullName == EnumerableName && inner.Parameters.Count == 1;
		}

		MethodReference As(ModuleDefinition module, TypeReference type)
		{
			var method = new GenericInstanceMethod(module.ImportReference(_as));
			method.GenericArguments.Add(type);
			return method;
		}

		/// <summary>A type of the signature of a generic method as a call of it names it.</summary>
		static TypeReference InCall(TypeReference type, GenericInstanceMethod call)
		{
			switch (type)
			{
				case GenericParameter parameter:
					return parameter.Type == GenericParameterType.Method ? call.GenericArguments[parameter.Position] : type;
				case GenericInstanceType instance:
				{
					var copy = new GenericInstanceType(instance.ElementType);
					foreach (var argument in instance.GenericArguments)
					{
						copy.GenericArguments.Add(InCall(argument, call));
					}
					return copy;
				}
				case ArrayType array:
					return new ArrayType(InCall(array.ElementType, call), array.Rank);
				default:
					return type;
			}
		}

		// ---- what is on the stack

		/// <summary>
		/// Follows the evaluation stack through the body, each instruction once, from the start and from each handler:
		/// which values each instruction takes, and which it makes. Where two different values meet at an instruction that
		/// two paths reach, both are marked as merged. false when the paths disagree on how many values there are.
		/// </summary>
		static bool Simulate(MethodDefinition method, out Dictionary<Instruction, Value[]> taken, out Dictionary<Instruction, Value> made)
		{
			taken = new Dictionary<Instruction, Value[]>();
			made = new Dictionary<Instruction, Value>();
			var body = method.Body;
			if (body.Instructions.Count == 0)
			{
				return false;
			}
			var stateAt = new Dictionary<Instruction, List<Value>>();
			var work = new Stack<Instruction>();
			bool Reach(Instruction target, List<Value> from)
			{
				if (target == null)
				{
					return false;
				}
				if (stateAt.TryGetValue(target, out var known))
				{
					if (known.Count != from.Count)
					{
						return false;
					}
					for (var at = 0; at < known.Count; at++)
					{
						if (!ReferenceEquals(known[at], from[at]))
						{
							known[at].Merged = true;
							from[at].Merged = true;
						}
					}
					return true;
				}
				stateAt.Add(target, new List<Value>(from));
				work.Push(target);
				return true;
			}
			Reach(body.Instructions[0], new List<Value>());
			foreach (var handler in body.ExceptionHandlers)
			{
				var catches = handler.HandlerType == ExceptionHandlerType.Catch || handler.HandlerType == ExceptionHandlerType.Filter;
				if (!Reach(handler.HandlerStart, catches ? new List<Value> { new Value { Merged = true } } : new List<Value>()))
				{
					return false;
				}
				if (handler.FilterStart != null && !Reach(handler.FilterStart, new List<Value> { new Value { Merged = true } }))
				{
					return false;
				}
			}
			while (work.Count > 0)
			{
				var instruction = work.Pop();
				var state = new List<Value>(stateAt[instruction]);
				var code = instruction.OpCode.Code;
				if (code == Code.Leave || code == Code.Leave_S)
				{
					// Whatever is on the stack is dropped: it leaves the chain.
					foreach (var dropped in state)
					{
						dropped.Merged = true;
					}
					if (!Reach((Instruction)instruction.Operand, new List<Value>()))
					{
						return false;
					}
					continue;
				}
				if (!Verifier.Effect(method, instruction, out var pops, out var pushes) || pops > state.Count)
				{
					return false;
				}
				var arguments = state.GetRange(state.Count - pops, pops).ToArray();
				state.RemoveRange(state.Count - pops, pops);
				taken[instruction] = arguments;
				foreach (var argument in arguments)
				{
					argument.Consumers.Add(instruction);
				}
				if (code == Code.Dup)
				{
					state.Add(arguments[0]);
					state.Add(arguments[0]);
				}
				else
				{
					for (var at = 0; at < pushes; at++)
					{
						var value = new Value { Producer = instruction };
						state.Add(value);
						made[instruction] = value;
					}
				}
				switch (instruction.OpCode.FlowControl)
				{
					case FlowControl.Branch:
						if (!Reach((Instruction)instruction.Operand, state))
						{
							return false;
						}
						break;
					case FlowControl.Cond_Branch:
						if (instruction.Operand is Instruction[] many)
						{
							foreach (var target in many)
							{
								if (!Reach(target, state))
								{
									return false;
								}
							}
						}
						else if (!Reach((Instruction)instruction.Operand, state))
						{
							return false;
						}
						if (!Reach(instruction.Next, state))
						{
							return false;
						}
						break;
					case FlowControl.Return:
					case FlowControl.Throw:
						break;
					default:
						if (!Reach(instruction.Next, state))
						{
							return false;
						}
						break;
				}
			}
			return true;
		}

		/// <summary>The type the IL gives a value, where that can be read off the instruction that made it; else null.</summary>
		static TypeReference TypeOf(MethodDefinition method, Value value)
		{
			var producer = value.Producer;
			if (producer == null)
			{
				return null;
			}
			var variables = method.Body.Variables;
			switch (producer.OpCode.Code)
			{
				case Code.Ldloc_0:
					return variables[0].VariableType;
				case Code.Ldloc_1:
					return variables[1].VariableType;
				case Code.Ldloc_2:
					return variables[2].VariableType;
				case Code.Ldloc_3:
					return variables[3].VariableType;
				case Code.Ldloc:
				case Code.Ldloc_S:
					return ((VariableDefinition)producer.Operand).VariableType;
				case Code.Ldarg_0:
					return Argument(method, 0);
				case Code.Ldarg_1:
					return Argument(method, 1);
				case Code.Ldarg_2:
					return Argument(method, 2);
				case Code.Ldarg_3:
					return Argument(method, 3);
				case Code.Ldarg:
				case Code.Ldarg_S:
					return ((ParameterDefinition)producer.Operand).ParameterType;
				case Code.Ldfld:
				case Code.Ldsfld:
				{
					var field = (FieldReference)producer.Operand;
					return field.DeclaringType is GenericInstanceType || field.FieldType.ContainsGenericParameter ? null : field.FieldType;
				}
				case Code.Newobj:
					return ((MethodReference)producer.Operand).DeclaringType;
				case Code.Call:
				case Code.Callvirt:
				{
					var called = (MethodReference)producer.Operand;
					var type = Returned(called.ReturnType, called);
					return type == null || type.ContainsGenericParameter ? null : type;
				}
				case Code.Castclass:
					return (TypeReference)producer.Operand;
				default:
					return null;
			}
		}

		static TypeReference Argument(MethodDefinition method, int index)
		{
			if (method.HasThis)
			{
				if (index == 0)
				{
					return null;
				}
				index--;
			}
			return index < method.Parameters.Count ? method.Parameters[index].ParameterType : null;
		}

		/// <summary>The return type of a call, with the type parameters of the callee replaced by what the call names.</summary>
		static TypeReference Returned(TypeReference type, MethodReference called)
		{
			switch (type)
			{
				case GenericParameter parameter:
					if (parameter.Type == GenericParameterType.Method)
					{
						return called is GenericInstanceMethod generic && parameter.Position < generic.GenericArguments.Count ? generic.GenericArguments[parameter.Position] : null;
					}
					return called.DeclaringType is GenericInstanceType declaring && parameter.Position < declaring.GenericArguments.Count ? declaring.GenericArguments[parameter.Position] : null;
				case GenericInstanceType instance:
				{
					var copy = new GenericInstanceType(instance.ElementType);
					foreach (var argument in instance.GenericArguments)
					{
						var inner = Returned(argument, called);
						if (inner == null)
						{
							return null;
						}
						copy.GenericArguments.Add(inner);
					}
					return copy;
				}
				case ArrayType array:
				{
					var element = Returned(array.ElementType, called);
					return element == null ? null : new ArrayType(element, array.Rank);
				}
				case TypeSpecification _:
					return null;
				default:
					return type;
			}
		}

		// ---- what this step adds to the class libraries

		/// <summary>T As&lt;T&gt;(object value) of GenericMemoryUnsafe in mscorlib: the one the canonicalizer makes, or the same made here.</summary>
		void MakeAs()
		{
			var module = _program.Assemblies.First(assembly => assembly.Name.Name == "mscorlib").MainModule;
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

		/// <summary>
		/// Enumerable.GenericMemoryLinq, a nested class, so that it may name the iterators of Enumerable:
		///
		///   static IEnumerable&lt;object&gt; List(List&lt;object&gt; source)                        a list as a sequence the calls for object walk
		///   static IEnumerable&lt;object&gt; Where(List&lt;object&gt; source, Func&lt;object, bool&gt;)      what Where makes for a list
		///   static IEnumerable&lt;R&gt; Select&lt;R&gt;(List&lt;object&gt; source, Func&lt;object, R&gt;)         what Select makes for a list
		///   static List&lt;T&gt; ToList&lt;T&gt;(IEnumerable&lt;object&gt; source)                           a List&lt;T&gt; of the elements
		///   static T[] ToArray&lt;T&gt;(IEnumerable&lt;object&gt; source)                              a T[] of the elements
		///
		/// The list iterators call methods of List&lt;object&gt; on a list of another element type. Such a method takes for
		/// granted that its class was initialized, so the static constructor of this class makes one List&lt;object&gt;.
		/// </summary>
		bool MakeHelpers()
		{
			var existing = _enumerable.NestedTypes.FirstOrDefault(type => type.Name == HelperName);
			if (existing != null)
			{
				_wrapList = existing.Methods.First(method => method.Name == "List");
				_whereList = existing.Methods.First(method => method.Name == "Where");
				_selectList = existing.Methods.First(method => method.Name == "Select");
				_toList = existing.Methods.First(method => method.Name == "ToList");
				_toArray = existing.Methods.First(method => method.Name == "ToArray");
				return true;
			}
			var module = _core;
			var whereIterator = _enumerable.NestedTypes.FirstOrDefault(type => type.Name == "WhereListIterator`1");
			var selectIterator = _enumerable.NestedTypes.FirstOrDefault(type => type.Name == "WhereSelectListIterator`2");
			var whereMake = whereIterator?.Methods.FirstOrDefault(method => method.IsConstructor && !method.IsStatic && method.Parameters.Count == 2);
			var selectMake = selectIterator?.Methods.FirstOrDefault(method => method.IsConstructor && !method.IsStatic && method.Parameters.Count == 3);
			var error = module.GetType("System.Linq.Error")?.Methods.FirstOrDefault(method => method.Name == "ArgumentNull" && method.Parameters.Count == 1);
			var toArray = _enumerable.Methods.FirstOrDefault(method => method.Name == "ToArray" && method.Parameters.Count == 1);
			if (whereMake == null || selectMake == null || error == null || toArray == null)
			{
				return false;
			}
			var mscorlib = _program.Assemblies.First(assembly => assembly.Name.Name == "mscorlib").MainModule;
			var objectType = module.TypeSystem.Object;
			var list = module.ImportReference(mscorlib.GetType("System.Collections.Generic.List`1"));
			var listDefinition = mscorlib.GetType("System.Collections.Generic.List`1");
			var enumerableOf = module.ImportReference(mscorlib.GetType("System.Collections.Generic.IEnumerable`1"));
			var enumeratorDefinition = mscorlib.GetType("System.Collections.Generic.IEnumerator`1");
			var enumeratorOf = module.ImportReference(enumeratorDefinition);
			var enumerator = module.ImportReference(mscorlib.GetType("System.Collections.IEnumerator"));
			var disposable = module.ImportReference(mscorlib.GetType("System.IDisposable"));
			var func2 = module.ImportReference(mscorlib.GetType("System.Func`2"));
			var arrayType = mscorlib.GetType("System.Array");
			var copy = module.ImportReference(arrayType.Methods.First(method => method.Name == "Copy" && method.Parameters.Count == 3 && method.Parameters[2].ParameterType.MetadataType == MetadataType.Int32));

			GenericInstanceType Of(TypeReference definition, params TypeReference[] arguments)
			{
				var instance = new GenericInstanceType(definition);
				foreach (var argument in arguments)
				{
					instance.GenericArguments.Add(argument);
				}
				return instance;
			}
			// A member of an instance of a generic type: the signature is the one of the definition, written in its parameters.
			MethodReference On(MethodDefinition definition, GenericInstanceType instance)
			{
				var imported = module.ImportReference(definition);
				var reference = new MethodReference(imported.Name, imported.ReturnType, instance)
				{
					HasThis = imported.HasThis,
					ExplicitThis = imported.ExplicitThis,
					CallingConvention = imported.CallingConvention,
				};
				foreach (var parameter in imported.Parameters)
				{
					reference.Parameters.Add(new ParameterDefinition(parameter.ParameterType));
				}
				return reference;
			}

			var listOfObject = Of(list, objectType);
			var sequenceOfObject = Of(enumerableOf, objectType);
			var enumeratorOfObject = Of(enumeratorOf, objectType);
			var predicate = Of(func2, objectType, module.TypeSystem.Boolean);
			var same = Of(func2, objectType, objectType);
			var delegateMake = mscorlib.GetType("System.Func`2").Methods.First(method => method.IsConstructor);

			var helpers = new TypeDefinition("", HelperName, TypeAttributes.NestedPublic | TypeAttributes.Abstract | TypeAttributes.Sealed, objectType);
			_enumerable.NestedTypes.Add(helpers);

			// static object Same(object value) => value;
			var sameMethod = new MethodDefinition("Same", MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig, objectType);
			sameMethod.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None, objectType));
			var il = sameMethod.Body.GetILProcessor();
			il.Emit(OpCodes.Ldarg_0);
			il.Emit(OpCodes.Ret);
			helpers.Methods.Add(sameMethod);

			var sameField = new FieldDefinition("s_same", FieldAttributes.Private | FieldAttributes.Static | FieldAttributes.InitOnly, same);
			var keepField = new FieldDefinition("s_list", FieldAttributes.Private | FieldAttributes.Static | FieldAttributes.InitOnly, listOfObject);
			helpers.Fields.Add(sameField);
			helpers.Fields.Add(keepField);

			var initializer = new MethodDefinition(".cctor", MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName, module.TypeSystem.Void);
			il = initializer.Body.GetILProcessor();
			il.Emit(OpCodes.Newobj, On(listDefinition.Methods.First(method => method.IsConstructor && !method.IsStatic && method.Parameters.Count == 0), listOfObject));
			il.Emit(OpCodes.Stsfld, keepField);
			il.Emit(OpCodes.Ldnull);
			il.Emit(OpCodes.Ldftn, sameMethod);
			il.Emit(OpCodes.Newobj, On(delegateMake, same));
			il.Emit(OpCodes.Stsfld, sameField);
			il.Emit(OpCodes.Ret);
			helpers.Methods.Add(initializer);

			void ThrowIfNull(ILProcessor to, int argument, string name)
			{
				var ok = Instruction.Create(OpCodes.Nop);
				to.Emit(argument == 0 ? OpCodes.Ldarg_0 : OpCodes.Ldarg_1);
				to.Emit(OpCodes.Brtrue, ok);
				to.Emit(OpCodes.Ldstr, name);
				to.Emit(OpCodes.Call, error);
				to.Emit(OpCodes.Throw);
				to.Append(ok);
			}

			// static IEnumerable<object> List(List<object> source): null stays null, so that the call it goes to says what is missing.
			_wrapList = new MethodDefinition("List", MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig, sequenceOfObject);
			_wrapList.Parameters.Add(new ParameterDefinition("source", ParameterAttributes.None, listOfObject));
			il = _wrapList.Body.GetILProcessor();
			{
				var have = Instruction.Create(OpCodes.Ldarg_0);
				il.Emit(OpCodes.Ldarg_0);
				il.Emit(OpCodes.Brtrue, have);
				il.Emit(OpCodes.Ldnull);
				il.Emit(OpCodes.Ret);
				il.Append(have);
				il.Emit(OpCodes.Ldnull);
				il.Emit(OpCodes.Ldsfld, sameField);
				il.Emit(OpCodes.Newobj, On(selectMake, Of(selectIterator, objectType, objectType)));
				il.Emit(OpCodes.Ret);
			}
			helpers.Methods.Add(_wrapList);

			// static IEnumerable<object> Where(List<object> source, Func<object, bool> predicate)
			_whereList = new MethodDefinition("Where", MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig, sequenceOfObject);
			_whereList.Parameters.Add(new ParameterDefinition("source", ParameterAttributes.None, listOfObject));
			_whereList.Parameters.Add(new ParameterDefinition("predicate", ParameterAttributes.None, predicate));
			il = _whereList.Body.GetILProcessor();
			ThrowIfNull(il, 0, "source");
			ThrowIfNull(il, 1, "predicate");
			il.Emit(OpCodes.Ldarg_0);
			il.Emit(OpCodes.Ldarg_1);
			il.Emit(OpCodes.Newobj, On(whereMake, Of(whereIterator, objectType)));
			il.Emit(OpCodes.Ret);
			helpers.Methods.Add(_whereList);

			// static IEnumerable<TResult> Select<TResult>(List<object> source, Func<object, TResult> selector)
			_selectList = new MethodDefinition("Select", MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig, objectType);
			var result = new GenericParameter("TResult", _selectList);
			_selectList.GenericParameters.Add(result);
			_selectList.ReturnType = Of(enumerableOf, result);
			_selectList.Parameters.Add(new ParameterDefinition("source", ParameterAttributes.None, listOfObject));
			_selectList.Parameters.Add(new ParameterDefinition("selector", ParameterAttributes.None, Of(func2, objectType, result)));
			il = _selectList.Body.GetILProcessor();
			ThrowIfNull(il, 0, "source");
			ThrowIfNull(il, 1, "selector");
			il.Emit(OpCodes.Ldarg_0);
			il.Emit(OpCodes.Ldnull);
			il.Emit(OpCodes.Ldarg_1);
			il.Emit(OpCodes.Newobj, On(selectMake, Of(selectIterator, objectType, result)));
			il.Emit(OpCodes.Ret);
			helpers.Methods.Add(_selectList);

			// static List<T> ToList<T>(IEnumerable<object> source): walks the source and adds each element, as List<T>(IEnumerable<T>) does.
			_toList = new MethodDefinition("ToList", MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig, objectType);
			{
				var element = new GenericParameter("T", _toList);
				_toList.GenericParameters.Add(element);
				var listOfElement = Of(list, element);
				_toList.ReturnType = listOfElement;
				_toList.Parameters.Add(new ParameterDefinition("source", ParameterAttributes.None, sequenceOfObject));
				var body = _toList.Body;
				body.InitLocals = true;
				var made = new VariableDefinition(listOfElement);
				var walker = new VariableDefinition(enumeratorOfObject);
				body.Variables.Add(made);
				body.Variables.Add(walker);
				il = body.GetILProcessor();
				ThrowIfNull(il, 0, "source");
				il.Emit(OpCodes.Newobj, On(listDefinition.Methods.First(method => method.IsConstructor && !method.IsStatic && method.Parameters.Count == 0), listOfElement));
				il.Emit(OpCodes.Stloc, made);
				il.Emit(OpCodes.Ldarg_0);
				il.Emit(OpCodes.Callvirt, On(mscorlib.GetType("System.Collections.Generic.IEnumerable`1").Methods.First(method => method.Name == "GetEnumerator"), sequenceOfObject));
				il.Emit(OpCodes.Stloc, walker);
				var condition = Instruction.Create(OpCodes.Ldloc, walker);
				var tryStart = Instruction.Create(OpCodes.Br, condition);
				il.Append(tryStart);
				var loop = Instruction.Create(OpCodes.Ldloc, made);
				il.Append(loop);
				il.Emit(OpCodes.Ldloc, walker);
				il.Emit(OpCodes.Callvirt, On(enumeratorDefinition.Methods.First(method => method.Name == "get_Current"), enumeratorOfObject));
				il.Emit(OpCodes.Callvirt, On(listDefinition.Methods.First(method => method.Name == "Add" && method.Parameters.Count == 1), listOfElement));
				il.Append(condition);
				il.Emit(OpCodes.Callvirt, module.ImportReference(mscorlib.GetType("System.Collections.IEnumerator").Methods.First(method => method.Name == "MoveNext")));
				il.Emit(OpCodes.Brtrue, loop);
				var end = Instruction.Create(OpCodes.Ldloc, made);
				il.Emit(OpCodes.Leave, end);
				var handlerStart = Instruction.Create(OpCodes.Ldloc, walker);
				var skip = Instruction.Create(OpCodes.Endfinally);
				il.Append(handlerStart);
				il.Emit(OpCodes.Brfalse, skip);
				il.Emit(OpCodes.Ldloc, walker);
				il.Emit(OpCodes.Callvirt, module.ImportReference(mscorlib.GetType("System.IDisposable").Methods.First(method => method.Name == "Dispose")));
				il.Append(skip);
				il.Append(end);
				il.Emit(OpCodes.Ret);
				body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Finally)
				{
					TryStart = tryStart,
					TryEnd = handlerStart,
					HandlerStart = handlerStart,
					HandlerEnd = end,
				});
			}
			helpers.Methods.Add(_toList);

			// static T[] ToArray<T>(IEnumerable<object> source): the array Enumerable makes for object, copied into one of the element type.
			_toArray = new MethodDefinition("ToArray", MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig, objectType);
			{
				var element = new GenericParameter("T", _toArray);
				_toArray.GenericParameters.Add(element);
				_toArray.ReturnType = new ArrayType(element);
				_toArray.Parameters.Add(new ParameterDefinition("source", ParameterAttributes.None, sequenceOfObject));
				var body = _toArray.Body;
				body.InitLocals = true;
				var buffer = new VariableDefinition(new ArrayType(objectType));
				var array = new VariableDefinition(new ArrayType(element));
				body.Variables.Add(buffer);
				body.Variables.Add(array);
				il = body.GetILProcessor();
				var ofObject = new GenericInstanceMethod(toArray);
				ofObject.GenericArguments.Add(objectType);
				il.Emit(OpCodes.Ldarg_0);
				il.Emit(OpCodes.Call, ofObject);
				il.Emit(OpCodes.Stloc, buffer);
				il.Emit(OpCodes.Ldloc, buffer);
				il.Emit(OpCodes.Ldlen);
				il.Emit(OpCodes.Conv_I4);
				il.Emit(OpCodes.Newarr, element);
				il.Emit(OpCodes.Stloc, array);
				il.Emit(OpCodes.Ldloc, buffer);
				il.Emit(OpCodes.Ldloc, array);
				il.Emit(OpCodes.Ldloc, buffer);
				il.Emit(OpCodes.Ldlen);
				il.Emit(OpCodes.Conv_I4);
				il.Emit(OpCodes.Call, copy);
				il.Emit(OpCodes.Ldloc, array);
				il.Emit(OpCodes.Ret);
			}
			helpers.Methods.Add(_toArray);

			_program.Touch(module);
			return true;
		}
	}
}
