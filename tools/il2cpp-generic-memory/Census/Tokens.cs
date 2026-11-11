using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

// Counts the tokens in the IL of a Player that name a generic instance with a reference type among its arguments, by
// the instruction that carries them, and sorts them by whether the token could name the instance over object instead.
// What decides that is what the code reached through the token does with the type parameter the argument binds, which
// is found for each type parameter on its own and followed through the generic code it is passed on to.
// usage: Census --tokens <ManagedStripped folder> [assembly name prefix to leave out]...
static class Tokens
{
	// What generic code does with one of its type parameters.
	//   typeof, T[], cast, new-kept, static, create   it looks at what the argument is: the token has to stay
	//   comparer                                       it asks for the default comparer: the same for every argument that has none of its own
	//   itf-inv                                        it calls through an interface that takes exactly this argument
	//   itf-var                                        it calls through an interface or a delegate that varies in this argument
	//   newobj, G<T>[], cast-G, cache                  it makes or names another generic type over the argument, which moves along with it
	static readonly string[] s_hard = { "typeof", "T[]", "cast", "new-kept", "static", "create" };
	static readonly string[] s_soft = { "newobj", "new-delegate", "G<T>[]", "cast-G", "cache" };

	static readonly Dictionary<string, HashSet<string>> s_facts = new Dictionary<string, HashSet<string>>();
	static readonly List<(string From, string To)> s_edges = new List<(string, string)>();

	sealed class Token
	{
		public string Construct;
		public string Name;
		public string Key;
		public string Canon;
		public string Declaring;
		public string Canonical;
		public List<string> Parameters = new List<string>();
		public List<string> Variance = new List<string>();
	}

	static readonly List<Token> s_tokens = new List<Token>();
	static readonly Dictionary<string, int[]> s_typeTokens = new Dictionary<string, int[]>();
	static readonly Dictionary<string, HashSet<string>> s_typeTokenKeys = new Dictionary<string, HashSet<string>>();

	public static int Run(string folder, string[] leftOut)
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
			var counted = !leftOut.Any(prefix => assembly.Name.Name.StartsWith(prefix, StringComparison.Ordinal));
			foreach (var type in assembly.MainModule.GetTypes())
			{
				foreach (var method in type.Methods.Where(method => method.HasBody))
				{
					Scan(method, counted);
				}
			}
		}
		// What a type parameter is passed on to counts as done to it.
		for (var changed = true; changed;)
		{
			changed = false;
			foreach (var (from, to) in s_edges)
			{
				if (s_facts.TryGetValue(from, out var reached))
				{
					var facts = Facts(to);
					var before = facts.Count;
					facts.UnionWith(reached);
					changed |= facts.Count != before;
				}
			}
		}

		Console.WriteLine("tokens that name a generic instance with a reference type among its arguments");
		Console.WriteLine();
		Console.WriteLine(" sites  distinct  as-object  instruction");
		foreach (var group in s_tokens.GroupBy(token => token.Construct).OrderByDescending(group => group.Count()))
		{
			Console.WriteLine($"{group.Count(),6} {group.Select(token => token.Key).Distinct().Count(),9} {group.Select(token => token.Canon).Distinct().Count(),10}  {group.Key}");
		}
		foreach (var pair in s_typeTokens.OrderByDescending(pair => pair.Value[0]))
		{
			Console.WriteLine($"{pair.Value[0],6} {s_typeTokenKeys[pair.Key].Count,9} {"",10}  {pair.Key}");
		}

		foreach (var construct in new[] { "direct call: method of a generic type", "direct call: generic method", "newobj", "newobj of a delegate", "ldftn" })
		{
			var tokens = s_tokens.Where(token => token.Construct == construct).ToList();
			if (tokens.Count == 0)
			{
				continue;
			}
			Console.WriteLine();
			Console.WriteLine($"{construct}: what the code behind the token does with the reference arguments");
			Console.WriteLine(" sites  distinct  as-object  ");
			foreach (var group in tokens.GroupBy(Classify).OrderBy(group => group.Key, StringComparer.Ordinal))
			{
				Console.WriteLine($"{group.Count(),6} {group.Select(token => token.Key).Distinct().Count(),9} {group.Select(token => token.Canon).Distinct().Count(),10}  {group.Key.Substring(2)}");
				foreach (var declaring in group.GroupBy(token => token.Declaring).OrderByDescending(inner => inner.Select(token => token.Key).Distinct().Count()).Take(6))
				{
					Console.WriteLine($"                              {declaring.Select(token => token.Key).Distinct().Count(),5}  {declaring.Key}");
				}
			}
			var looks = tokens.Where(token => Classify(token).StartsWith("5", StringComparison.Ordinal)).ToList();
			if (looks.Count > 0)
			{
				Console.WriteLine("       of those that look, by what they do (a token can do several):");
				foreach (var kind in s_hard)
				{
					var with = looks.Where(token => token.Parameters.Any(parameter => Facts(parameter).Contains(kind))).ToList();
					Console.WriteLine($"{with.Count,6} {with.Select(token => token.Key).Distinct().Count(),9}             {kind}");
				}
			}
		}

		foreach (var definition in new[] { "System.Collections.Generic.List`1", "System.Collections.Generic.Dictionary`2", "System.Linq.Enumerable" })
		{
			Console.WriteLine();
			Console.WriteLine($"{definition}: each method that a token names, with what it does with the reference arguments");
			Console.WriteLine(" sites  distinct  class and what it reaches");
			foreach (var group in s_tokens.Where(token => token.Declaring == definition && token.Construct.StartsWith("direct call", StringComparison.Ordinal)).GroupBy(token => token.Name).OrderByDescending(group => group.Select(token => token.Key).Distinct().Count()))
			{
				var reached = new SortedSet<string>(group.SelectMany(token => token.Parameters).SelectMany(parameter => Facts(parameter)), StringComparer.Ordinal);
				reached.Remove("call-on-T");
				Console.WriteLine($"{group.Count(),6} {group.Select(token => token.Key).Distinct().Count(),9}  {group.Key,-28} {Classify(group.First()).Substring(0, 1)}  {string.Join(" ", reached)}");
			}
		}

		Console.WriteLine();
		Console.WriteLine("call through an interface: how the interface takes the reference arguments");
		Console.WriteLine(" sites  distinct interface instances  as-object");
		foreach (var group in s_tokens.Where(token => token.Construct == "call through an interface").GroupBy(token => token.Variance.Contains("inv") ? "exactly (invariant)" : "varying (co- or contravariant)"))
		{
			Console.WriteLine($"{group.Count(),6} {group.Select(token => token.Declaring + "|" + token.Key.Split(new[] { "::" }, StringSplitOptions.None)[0]).Distinct().Count(),9} {group.Select(token => token.Canonical).Distinct().Count(),22}  {group.Key}");
			foreach (var declaring in group.GroupBy(token => token.Declaring).OrderByDescending(inner => inner.Count()).Take(8))
			{
				Console.WriteLine($"                              {declaring.Count(),5} sites  {declaring.Key}");
			}
		}
		return 0;
	}

	// The class of a token: the worst of what is done with each of its reference arguments.
	static string Classify(Token token)
	{
		var all = new HashSet<string>();
		foreach (var parameter in token.Parameters)
		{
			all.UnionWith(Facts(parameter));
		}
		if (s_hard.Any(all.Contains))
		{
			return "5 looks at what the argument is";
		}
		if (all.Contains("itf-inv"))
		{
			return "4 passes it through an interface that takes it exactly";
		}
		if (all.Contains("comparer"))
		{
			return "3 asks for its default comparer";
		}
		if (all.Contains("itf-var"))
		{
			return "2 passes it through an interface or delegate that varies in it";
		}
		if (s_soft.Any(all.Contains))
		{
			return "1 only makes other generic types over it";
		}
		return "0 does nothing with it";
	}

	static HashSet<string> Facts(string key)
	{
		if (!s_facts.TryGetValue(key, out var facts))
		{
			s_facts.Add(key, facts = new HashSet<string>());
		}
		return facts;
	}

	static string KeyOf(GenericParameter parameter, MethodDefinition context)
	{
		return (parameter.Type == GenericParameterType.Type ? "T|" : "M|") + context.FullName + "|" + parameter.Position;
	}

	static void Parameters(TypeReference type, List<GenericParameter> found)
	{
		switch (type)
		{
			case GenericParameter parameter:
				found.Add(parameter);
				break;
			case GenericInstanceType instance:
				foreach (var argument in instance.GenericArguments)
				{
					Parameters(argument, found);
				}
				break;
			case TypeSpecification specification:
				Parameters(specification.ElementType, found);
				break;
		}
	}

	static List<GenericParameter> Parameters(TypeReference type)
	{
		var found = new List<GenericParameter>();
		Parameters(type, found);
		return found;
	}

	static bool IsReference(TypeReference type)
	{
		if (type is GenericParameter || type is ByReferenceType || type is PointerType)
		{
			return false;
		}
		return type is ArrayType || !type.IsValueType;
	}

	static string Canon(TypeReference type)
	{
		switch (type)
		{
			case GenericParameter parameter:
				return "!" + parameter.Position;
			case GenericInstanceType instance when instance.IsValueType:
				return instance.ElementType.FullName + "<" + string.Join(",", instance.GenericArguments.Select(Canon)) + ">";
			default:
				return IsReference(type) ? "object" : type.FullName;
		}
	}

	static void Scan(MethodDefinition method, bool counted)
	{
		void Fact(GenericParameter parameter, string kind)
		{
			Facts(KeyOf(parameter, method)).Add(kind);
		}
		// An instance of the type can have any of its methods called, so what the type does with a parameter is what any of them does.
		for (var index = 0; index < method.DeclaringType.GenericParameters.Count; index++)
		{
			s_edges.Add(("T|" + method.FullName + "|" + index, "TYPE|" + method.DeclaringType.FullName + "|" + index));
		}
		foreach (var instruction in method.Body.Instructions)
		{
			var code = instruction.OpCode.Code;
			switch (instruction.Operand)
			{
				case TypeReference type:
				{
					var open = Parameters(type);
					if (open.Count > 0)
					{
						foreach (var parameter in open)
						{
							var bare = type is GenericParameter;
							if (code == Code.Ldtoken)
							{
								Fact(parameter, "typeof");
							}
							else if (code == Code.Newarr)
							{
								Fact(parameter, bare ? "T[]" : "G<T>[]");
							}
							else if (code == Code.Castclass || code == Code.Isinst || code == Code.Unbox_Any || code == Code.Unbox)
							{
								Fact(parameter, bare ? "cast" : "cast-G");
							}
						}
					}
					else if (counted && type is GenericInstanceType closed && closed.GenericArguments.Any(IsReference))
					{
						var name = code == Code.Newarr ? "newarr of a generic instance" : code == Code.Castclass || code == Code.Isinst || code == Code.Unbox_Any ? "castclass / isinst" : code == Code.Ldtoken ? "ldtoken" : null;
						if (name != null)
						{
							if (!s_typeTokens.TryGetValue(name, out var count))
							{
								s_typeTokens.Add(name, count = new int[1]);
								s_typeTokenKeys.Add(name, new HashSet<string>());
							}
							count[0]++;
							s_typeTokenKeys[name].Add(type.FullName);
						}
					}
					break;
				}
				case FieldReference field when code == Code.Ldsfld || code == Code.Stsfld || code == Code.Ldsflda:
					foreach (var parameter in Parameters(field.DeclaringType))
					{
						// The compiler keeps a delegate it made once in a static field of a type it generated: nothing depends on which instance holds it.
						Fact(parameter, field.DeclaringType.GetElementType().Name.Contains("<") ? "cache" : "static");
					}
					break;
				case MethodReference target:
					Call(method, instruction, target, counted);
					break;
			}
		}
	}

	static void Call(MethodDefinition method, Instruction instruction, MethodReference target, bool counted)
	{
		var code = instruction.OpCode.Code;
		var declaring = target.DeclaringType as GenericInstanceType;
		var instance = target as GenericInstanceMethod;
		if (declaring == null && instance == null)
		{
			return;
		}
		var resolved = target.Resolve();
		var definitionType = declaring?.ElementType.FullName ?? target.DeclaringType.FullName;
		var isInterface = resolved != null && resolved.DeclaringType.IsInterface;
		var isDelegate = resolved != null && resolved.DeclaringType.BaseType != null && resolved.DeclaringType.BaseType.FullName == "System.MulticastDelegate";

		// Each argument of the token with the type parameter of the callee it binds.
		var bindings = new List<(TypeReference Argument, string Parameter, GenericParameter Declared)>();
		if (declaring != null)
		{
			var declared = resolved?.DeclaringType.GenericParameters;
			for (var index = 0; index < declaring.GenericArguments.Count; index++)
			{
				bindings.Add((declaring.GenericArguments[index], "T|" + (resolved?.FullName ?? target.GetElementMethod().FullName) + "|" + index, declared != null && index < declared.Count ? declared[index] : null));
			}
		}
		if (instance != null)
		{
			for (var index = 0; index < instance.GenericArguments.Count; index++)
			{
				bindings.Add((instance.GenericArguments[index], "M|" + (resolved?.FullName ?? target.GetElementMethod().FullName) + "|" + index, null));
			}
		}

		// For a reference type the answer is the same whatever it is.
		var benign = target.Name == "IsReferenceOrContainsReferences";
		var created = code == Code.Newobj && !isDelegate && resolved != null && declaring != null;
		var anyOpen = false;
		foreach (var (argument, parameter, declared) in bindings)
		{
			var open = Parameters(argument);
			anyOpen |= open.Count > 0;
			foreach (var caller in open)
			{
				var callerKey = KeyOf(caller, method);
				if (code == Code.Ldtoken)
				{
					Facts(callerKey).Add("typeof");
				}
				else if (isInterface && declaring != null && parameter.StartsWith("T|", StringComparison.Ordinal))
				{
					var varies = declared != null && (declared.IsCovariant || declared.IsContravariant) && argument is GenericParameter;
					Facts(callerKey).Add(varies ? "itf-var" : "itf-inv");
				}
				else if (isDelegate)
				{
					if (code == Code.Newobj)
					{
						Facts(callerKey).Add("new-delegate");
					}
				}
				else if (target.Name == "get_Default" && (definitionType == "System.Collections.Generic.EqualityComparer`1" || definitionType == "System.Collections.Generic.Comparer`1"))
				{
					Facts(callerKey).Add("comparer");
				}
				else if (definitionType == "System.Activator")
				{
					Facts(callerKey).Add("create");
				}
				else if (!benign)
				{
					if (code == Code.Newobj)
					{
						Facts(callerKey).Add(definitionType == "System.Collections.Generic.List`1" && method.DeclaringType.FullName != definitionType ? "new-kept" : "newobj");
					}
					s_edges.Add((parameter, callerKey));
					if (created && parameter.StartsWith("T|", StringComparison.Ordinal))
					{
						s_edges.Add(("TYPE|" + resolved.DeclaringType.FullName + "|" + parameter.Substring(parameter.LastIndexOf('|') + 1), callerKey));
					}
				}
			}
		}

		if (!counted || anyOpen || !bindings.Any(binding => IsReference(binding.Argument)))
		{
			return;
		}
		var token = new Token
		{
			Name = target.Name,
			Key = target.FullName,
			Declaring = definitionType,
			Canonical = definitionType + "<" + string.Join(",", bindings.Select(binding => Canon(binding.Argument))) + ">",
		};
		token.Canon = token.Canonical + "::" + target.Name + "/" + target.Parameters.Count;
		foreach (var (argument, parameter, declared) in bindings.Where(binding => IsReference(binding.Argument)))
		{
			token.Parameters.Add(created && parameter.StartsWith("T|", StringComparison.Ordinal) ? "TYPE|" + resolved.DeclaringType.FullName + "|" + parameter.Substring(parameter.LastIndexOf('|') + 1) : parameter);
			token.Variance.Add(declared != null && (declared.IsCovariant || declared.IsContravariant) ? "var" : "inv");
		}
		if (resolved == null)
		{
			token.Construct = "other (not resolved)";
		}
		else if (code == Code.Newobj)
		{
			token.Construct = isDelegate ? "newobj of a delegate" : "newobj";
		}
		else if (code == Code.Ldftn || code == Code.Ldvirtftn)
		{
			token.Construct = "ldftn";
		}
		else if (code == Code.Ldtoken)
		{
			token.Construct = "ldtoken of a method";
		}
		else if (isDelegate)
		{
			token.Construct = "call of a delegate";
		}
		else if (isInterface)
		{
			token.Construct = "call through an interface";
		}
		else if (code == Code.Callvirt && resolved.IsVirtual && !resolved.IsFinal && !resolved.DeclaringType.IsSealed)
		{
			token.Construct = "virtual call";
		}
		else
		{
			token.Construct = instance != null ? "direct call: generic method" : "direct call: method of a generic type";
		}
		s_tokens.Add(token);
	}
}
