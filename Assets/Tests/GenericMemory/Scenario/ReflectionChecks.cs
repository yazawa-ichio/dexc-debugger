using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.Scripting;

namespace GenericMemory
{
	/// <summary>
	/// What reflection says about generic instances, written line by line so that two runs of one Player can be compared:
	/// every member in the order reflection gives it, whether two ways to the same member give the same object, what the
	/// members do when invoked, the attributes, when the static constructor runs, the lookups by name that the runtime and
	/// the engine do themselves, and threads that enumerate a type while others use it for the first time.
	///
	/// A check fails only where the run contradicts itself (a type enumerated before its first use and after it, threads
	/// against each other). Everything else is a line, and the lines of two runs have to be the same.
	/// Each check has element types no other check uses, so that it is the first to reach its instances.
	/// </summary>
	public static class ReflectionChecks
	{
		const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

		static readonly BindingFlags[] s_flags =
		{
			BindingFlags.Public | BindingFlags.Instance,
			BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy,
			BindingFlags.NonPublic | BindingFlags.Instance,
			BindingFlags.NonPublic | BindingFlags.Static,
			All | BindingFlags.DeclaredOnly,
		};

		sealed class CheckFailed : Exception
		{
			public CheckFailed(string message)
				: base(message)
			{
			}
		}

		static readonly KeyValuePair<string, Action>[] s_checks =
		{
			Check("Definitions", Definitions),
			Check("BeforeUse", BeforeUse),
			Check("AfterUse", AfterUse),
			Check("InterfaceOrder", InterfaceOrder),
			Check("Identity", Identity),
			Check("Invoke", Invoke),
			Check("Attributes", Attributes),
			Check("StaticConstructor", StaticConstructor),
			Check("NameLookups", NameLookups),
			Check("Behaviour", Behaviour),
			Check("EnumerateWhileFirstUsed", EnumerateWhileFirstUsed),
		};

		static StringBuilder s_text;
		static string s_name;
		static List<string> s_before;
		static ReflectionBehaviour s_behaviour;
		static string s_added;
		static string s_messaged;
		static volatile bool s_go;

		public static int Failed { get; private set; }

		static KeyValuePair<string, Action> Check(string name, Action body)
		{
			return new KeyValuePair<string, Action>(name, body);
		}

		/// <summary>The part that needs frames to pass before <see cref="Run"/>: a component whose messages are methods of a generic class.</summary>
		public static void Begin()
		{
			var host = new GameObject("Generic Memory Reflection");
			UnityEngine.Object.DontDestroyOnLoad(host);
			s_behaviour = host.AddComponent<ReflectionBehaviour>();
			// Awake and OnEnable ran inside AddComponent.
			s_added = s_behaviour.Counts();
			host.SendMessage("Ping", 3);
			s_behaviour.Invoke("Later", 0f);
			s_behaviour.StartCoroutine("Routine");
			s_messaged = s_behaviour.Counts();
		}

		/// <summary>Runs every check once and writes its lines, then one line that says whether it contradicted itself.</summary>
		public static void Run(StringBuilder text)
		{
			s_text = text;
			foreach (var check in s_checks)
			{
				s_name = check.Key;
				string detail = null;
				try
				{
					check.Value();
				}
				catch (Exception exception)
				{
					var inner = exception is TargetInvocationException && exception.InnerException != null ? exception.InnerException : exception;
					detail = inner.GetType().Name + ": " + Flat(inner.Message);
					Failed++;
				}
				text.Append("check\t").Append(check.Key).Append("\t0\t").Append(detail == null ? "PASS" : "FAIL").Append('\t').Append(detail ?? "").Append('\n');
			}
		}

		static string Flat(string text)
		{
			return text.Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ');
		}

		static void Line(string text)
		{
			s_text.Append("line\t").Append(s_name).Append('\t').Append(Flat(text)).Append('\n');
		}

		static void Expect(bool condition, string what)
		{
			if (!condition)
			{
				throw new CheckFailed(what);
			}
		}

		/// <summary>What a read gives, or the exception it ends with: a line must not stop the lines after it.</summary>
		static string Try(Func<object> read)
		{
			try
			{
				return Show(read());
			}
			catch (Exception exception)
			{
				var inner = exception is TargetInvocationException && exception.InnerException != null ? exception.InnerException : exception;
				return "!" + inner.GetType().Name;
			}
		}

		static string Show(object value)
		{
			switch (value)
			{
				case null:
					return "null";
				case string text:
					return text;
				case Elem element:
					return element.GetType().Name + "(" + element.Value + ")";
				case Type type:
					return type.ToString();
				case MemberInfo member:
					return member.ToString();
				case ICollection collection:
					return value.GetType().Name + "[" + collection.Count + "]";
				default:
					return value.GetType().IsPrimitive || value is Enum ? value.ToString() : "<" + value.GetType().Name + ">";
			}
		}

		static string Short(MemberInfo member)
		{
			return member == null ? "null" : member.DeclaringType + "::" + member.Name + "@" + member.MetadataToken.ToString("x8");
		}

		static string Names(IEnumerable<MemberInfo> members)
		{
			return string.Join(", ", members.Select(member => member.Name + "@" + member.MetadataToken.ToString("x8") + "/" + member.DeclaringType));
		}

		static string Describe(MemberInfo member)
		{
			var text = new StringBuilder();
			text.Append(Try(() => member.MemberType)).Append(' ').Append(Try(() => member.ToString()));
			text.Append(" | declared ").Append(Try(() => member.DeclaringType));
			text.Append(" | reflected ").Append(Try(() => member.ReflectedType));
			text.Append(" | token ").Append(Try(() => member.MetadataToken.ToString("x8")));
			switch (member)
			{
				case MethodBase method:
					text.Append(" | ").Append(Try(() => method.Attributes));
					text.Append(" | generic ").Append(Try(() => method.IsGenericMethodDefinition));
					text.Append(" | (").Append(Try(() => string.Join(", ", method.GetParameters().Select(parameter => parameter.ParameterType + " " + parameter.Name + " " + parameter.Position)))).Append(')');
					if (method is MethodInfo info)
					{
						text.Append(" -> ").Append(Try(() => info.ReturnType));
						text.Append(" | base ").Append(Try(() => Short(info.GetBaseDefinition())));
					}
					break;
				case PropertyInfo property:
					text.Append(" | ").Append(Try(() => property.PropertyType));
					text.Append(" | read ").Append(Try(() => property.CanRead)).Append(" write ").Append(Try(() => property.CanWrite));
					text.Append(" | get ").Append(Try(() => Short(property.GetGetMethod(true)))).Append(" set ").Append(Try(() => Short(property.GetSetMethod(true))));
					text.Append(" | index ").Append(Try(() => property.GetIndexParameters().Length));
					break;
				case EventInfo raised:
					text.Append(" | ").Append(Try(() => raised.EventHandlerType));
					text.Append(" | add ").Append(Try(() => Short(raised.GetAddMethod(true)))).Append(" remove ").Append(Try(() => Short(raised.GetRemoveMethod(true))));
					break;
				case FieldInfo field:
					text.Append(" | ").Append(Try(() => field.FieldType)).Append(" | ").Append(Try(() => field.Attributes));
					break;
				case Type nested:
					text.Append(" | ").Append(Try(() => nested.Attributes));
					break;
			}
			return text.ToString();
		}

		/// <summary>Everything reflection lists for a type, in the order it lists it.</summary>
		static void Members(Type type, List<string> lines)
		{
			// The interfaces are sorted here: the runtime gives them in the order of the addresses of their classes, which is
			// the order they were first needed in, and that is not the same for a type listed first and a type used first.
			lines.Add("type " + type + " | base " + Try(() => type.BaseType) + " | " + Try(() => type.Attributes)
				+ " | interfaces " + Try(() => string.Join(", ", type.GetInterfaces().Select(implemented => implemented.ToString()).OrderBy(name => name, StringComparer.Ordinal))));
			MemberInfo[] members;
			try
			{
				members = type.GetMembers(All);
			}
			catch (Exception exception)
			{
				lines.Add(" members !" + exception.GetType().Name);
				members = new MemberInfo[0];
			}
			foreach (var member in members)
			{
				lines.Add("  " + Describe(member));
			}
			foreach (var flags in s_flags)
			{
				var chosen = flags;
				lines.Add(" " + chosen + " | methods " + Try(() => Names(type.GetMethods(chosen))));
				lines.Add(" " + chosen + " | constructors " + Try(() => Names(type.GetConstructors(chosen))));
				lines.Add(" " + chosen + " | properties " + Try(() => Names(type.GetProperties(chosen))));
				lines.Add(" " + chosen + " | events " + Try(() => Names(type.GetEvents(chosen))));
				lines.Add(" " + chosen + " | fields " + Try(() => Names(type.GetFields(chosen))));
				lines.Add(" " + chosen + " | nested " + Try(() => Names(type.GetNestedTypes(chosen))));
			}
		}

		// ---- element types: a set for each check ----

		public class Elem
		{
			public int Value;
		}

		sealed class ElemA1 : Elem { }
		sealed class ElemA2 : Elem { }
		sealed class ElemB1 : Elem { }
		sealed class ElemB2 : Elem { }
		sealed class ElemC : Elem { }
		sealed class ElemD : Elem { }
		sealed class ElemD2 : Elem { }
		sealed class ElemE : Elem { }
		sealed class ElemF : Elem { }
		sealed class ElemF2 : Elem { }
		sealed class ElemG : Elem { }
		sealed class ElemG2 : Elem { }
		sealed class ElemH1 : Elem { }
		sealed class ElemH2 : Elem { }
		sealed class ElemH3 : Elem { }
		sealed class ElemH4 : Elem { }
		sealed class ElemH5 : Elem { }
		sealed class ElemH6 : Elem { }
		sealed class ElemH7 : Elem { }
		sealed class ElemH8 : Elem { }
		sealed class ElemI : Elem { }

		struct ValA
		{
			public int Number;
		}

		struct ValB
		{
			public int Number;
		}

		sealed class Wrap<T>
		{
			public T Inner;
		}

		static class Counters
		{
			public static int StaticConstructors;
		}

		// ---- the generic types of the project's own: every kind of member, each with an attribute ----

		[Preserve]
		[AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
		sealed class MarkAttribute : Attribute
		{
			[Preserve]
			public MarkAttribute(int number)
			{
				Number = number;
			}

			[Preserve]
			public int Number { get; }

			[Preserve]
			public string Name { get; set; }

			[Preserve]
			public Type Kind { get; set; }

			[Preserve]
			public int Field;
		}

		[Preserve]
		interface IOwn<T>
		{
			T Value { get; set; }

			event Action<T> Changed;

			int Count(T item);
		}

		[Preserve]
		[Mark(1, Name = "type", Kind = typeof(int))]
		class Own<T> : IOwn<T>
		{
			static int s_made;

			[Preserve]
			[Mark(2, Name = "field", Field = 7)]
			public T Kept;

			T _value;

			static Own()
			{
				Interlocked.Increment(ref Counters.StaticConstructors);
			}

			[Preserve]
			[Mark(8, Name = "constructor")]
			public Own()
			{
				Interlocked.Increment(ref s_made);
			}

			[Preserve]
			public Own(T value)
				: this()
			{
				_value = value;
			}

			[Preserve]
			[Mark(3, Name = "property", Kind = typeof(string))]
			public T Value
			{
				[Preserve]
				get
				{
					return _value;
				}
				[Preserve]
				set
				{
					_value = value;
					Changed?.Invoke(value);
				}
			}

			[Preserve]
			public static int Made
			{
				[Preserve]
				get
				{
					return s_made;
				}
			}

			[Preserve]
			protected int Hidden
			{
				[Preserve]
				get;
				[Preserve]
				private set;
			}

			[Mark(4, Name = "event")]
			public event Action<T> Changed;

			[Preserve]
			[Mark(5, Name = "method")]
			[Mark(9, Name = "again", Field = 2)]
			public int Count([Mark(6, Name = "parameter")] T item)
			{
				return EqualityComparer<T>.Default.Equals(item, _value) ? 1 : 0;
			}

			[Preserve]
			public TOther Convert<TOther>(Func<T, TOther> convert)
			{
				return convert(_value);
			}

			[Preserve]
			public static Own<T> Create(T value)
			{
				return new Own<T>(value);
			}

			[Preserve]
			int Private(int number)
			{
				Hidden = number;
				return Hidden + 1;
			}

			[Preserve]
			public virtual string Describe()
			{
				return typeof(T).Name;
			}

			[Preserve]
			public sealed class Nested
			{
				[Preserve]
				public T Inner;

				[Preserve]
				public int Twice(int number)
				{
					return number * 2;
				}
			}

			[Preserve]
			public struct Pair
			{
				public T First;
				public T Second;
			}
		}

		[Preserve]
		class Derived<T> : Own<T>
		{
			[Preserve]
			public Derived(T value)
				: base(value)
			{
			}

			[Preserve]
			public override string Describe()
			{
				return "derived " + base.Describe();
			}

			[Preserve]
			public int Extra
			{
				[Preserve]
				get
				{
					return 4;
				}
			}
		}

		/// <summary>The instances the first three checks list, for one set of element types.</summary>
		static Type[] Shapes<T1, T2, TValue>()
			where T1 : Elem
			where T2 : Elem
			where TValue : struct
		{
			return new[]
			{
				typeof(List<T1>),
				typeof(List<T1>.Enumerator),
				typeof(Dictionary<T1, T2>),
				typeof(Dictionary<T1, T2>.KeyCollection),
				typeof(HashSet<T1>),
				typeof(Func<T1, T2>),
				typeof(Action<T1>),
				typeof(Comparison<T1>),
				typeof(IList<T1>),
				typeof(IEnumerable<T1>),
				typeof(IEqualityComparer<T1>),
				typeof(KeyValuePair<T1, int>),
				typeof(KeyValuePair<int, TValue>),
				typeof(TValue?),
				typeof(List<TValue>),
				typeof(Lazy<T1>),
				typeof(Tuple<T1, T2>),
				typeof(ValueTuple<T1, int>),
				typeof(T1[]),
				typeof(Own<T1>),
				typeof(Own<T1>.Nested),
				typeof(Own<T1>.Pair),
				typeof(Derived<T1>),
				typeof(IOwn<T1>),
				typeof(Own<TValue>),
			};
		}

		/// <summary>Uses every one of those instances the ordinary way, without reflection.</summary>
		static long Use<T1, T2, TValue>(T1 first, T2 second, TValue value)
			where T1 : Elem
			where T2 : Elem
			where TValue : struct
		{
			long sum = 0;
			var list = new List<T1> { first, first };
			sum += list.Count + list.IndexOf(first) + (list.Contains(first) ? 1 : 0);
			foreach (var item in list)
			{
				sum += item.Value;
			}
			var enumerator = list.GetEnumerator();
			enumerator.MoveNext();
			sum += enumerator.Current.Value;
			IList<T1> asList = list;
			sum += asList.Count;
			IEnumerable<T1> asEnumerable = list;
			sum += asEnumerable.Count();
			Comparison<T1> comparison = (left, right) => left.Value - right.Value;
			list.Sort(comparison);
			var dictionary = new Dictionary<T1, T2> { { first, second } };
			sum += dictionary.TryGetValue(first, out var found) ? found.Value : -1;
			sum += dictionary.Keys.Count;
			var set = new HashSet<T1>(EqualityComparer<T1>.Default) { first };
			sum += set.Count;
			Func<T1, T2> convert = _ => second;
			sum += convert(first).Value;
			var heard = 0;
			Action<T1> hear = item => heard += item.Value;
			hear(first);
			var pair = new KeyValuePair<T1, int>(first, 3);
			sum += pair.Value + pair.Key.Value;
			var other = new KeyValuePair<int, TValue>(4, value);
			sum += other.Key;
			TValue? nullable = value;
			sum += nullable.HasValue ? 1 : 0;
			var values = new List<TValue> { value };
			sum += values.Count;
			var lazy = new Lazy<T1>(() => first);
			sum += lazy.Value.Value;
			var tuple = Tuple.Create(first, second);
			sum += tuple.Item1.Value + tuple.Item2.Value;
			var valueTuple = new ValueTuple<T1, int>(first, 5);
			sum += valueTuple.Item2;
			var array = new[] { first };
			IList<T1> arrayAsList = array;
			sum += array.Length + arrayAsList.IndexOf(first);
			var own = new Own<T1>(first);
			own.Changed += hear;
			own.Value = first;
			sum += own.Count(first) + own.Convert(item => item.Value) + (Own<T1>.Made > 0 ? 1 : 0);
			own.Kept = first;
			var nested = new Own<T1>.Nested { Inner = first };
			sum += nested.Twice(2) + nested.Inner.Value;
			var both = new Own<T1>.Pair { First = first, Second = first };
			sum += both.First.Value + both.Second.Value;
			var derived = new Derived<T1>(first);
			sum += derived.Describe().Length + derived.Extra;
			IOwn<T1> viaInterface = own;
			viaInterface.Changed += hear;
			viaInterface.Value = first;
			sum += viaInterface.Count(first);
			var ownValue = new Own<TValue>(value);
			sum += ownValue.Count(value);
			return sum + heard;
		}

		// ---- the checks ----

		static void Definitions()
		{
			var lines = new List<string>();
			Members(typeof(Own<>), lines);
			Members(typeof(IOwn<>), lines);
			Members(typeof(Derived<>), lines);
			Members(typeof(List<>), lines);
			foreach (var line in lines)
			{
				Line(line);
			}
		}

		static void BeforeUse()
		{
			s_before = new List<string>();
			foreach (var type in Shapes<ElemA1, ElemA2, ValA>())
			{
				Members(type, s_before);
			}
			foreach (var line in s_before)
			{
				Line(line);
			}
		}

		static void AfterUse()
		{
			Expect(s_before != null, "BeforeUse did not run");
			Line("use " + Use(new ElemB1 { Value = 3 }, new ElemB2 { Value = 5 }, new ValB { Number = 7 }));
			var after = new List<string>();
			foreach (var type in Shapes<ElemB1, ElemB2, ValB>())
			{
				Members(type, after);
			}
			foreach (var line in after)
			{
				Line(line);
			}
			// Listed after their first use, these read the same as the ones listed before theirs, but for the names.
			Expect(s_before.Count == after.Count, "lines before use " + s_before.Count + ", after use " + after.Count);
			for (var at = 0; at < after.Count; at++)
			{
				var renamed = after[at].Replace("ElemB", "ElemA").Replace("ValB", "ValA");
				Expect(renamed == s_before[at], "line " + at + " before use: " + s_before[at] + " <> after use: " + renamed);
			}
			// And the ones listed first read the same again once they were used.
			Line("use " + Use(new ElemA1 { Value = 3 }, new ElemA2 { Value = 5 }, new ValA { Number = 7 }));
			var again = new List<string>();
			foreach (var type in Shapes<ElemA1, ElemA2, ValA>())
			{
				Members(type, again);
			}
			Expect(s_before.Count == again.Count, "lines the first time " + s_before.Count + ", the second " + again.Count);
			for (var at = 0; at < again.Count; at++)
			{
				Expect(again[at] == s_before[at], "line " + at + " the first time: " + s_before[at] + " <> the second: " + again[at]);
			}
		}

		/// <summary>
		/// The interfaces in the order the runtime gives them. These lines are the only ones that may differ between two
		/// runs: the order is that of addresses, so it follows whatever made the classes of the interfaces first.
		/// </summary>
		static void InterfaceOrder()
		{
			foreach (var type in Shapes<ElemA1, ElemA2, ValA>().Concat(Shapes<ElemB1, ElemB2, ValB>()))
			{
				var listed = type;
				Line(type + ": " + Try(() => string.Join(", ", listed.GetInterfaces().Select(implemented => implemented.ToString()))));
			}
		}

		static string SameObjects(MemberInfo[] left, MemberInfo[] right)
		{
			if (left.Length != right.Length)
			{
				return "lengths " + left.Length + " and " + right.Length;
			}
			var equal = 0;
			var same = 0;
			for (var at = 0; at < left.Length; at++)
			{
				equal += left[at] == right[at] ? 1 : 0;
				same += ReferenceEquals(left[at], right[at]) ? 1 : 0;
			}
			return equal + " equal and " + same + " the same object of " + left.Length;
		}

		static void Identity()
		{
			var type = typeof(List<ElemC>);
			var list = new List<ElemC> { new ElemC { Value = 2 } };
			Func<ElemC, bool> contains = list.Contains;
			var all = type.GetMethods(All);
			var byName = type.GetMethod("Contains");
			Line("GetMethod among GetMethods: " + Try(() => all.Count(method => method == byName) + " equal, " + all.Count(method => ReferenceEquals(method, byName)) + " the same object"));
			Line("the method of a delegate == GetMethod: " + Try(() => contains.Method == byName));
			Line("GetMethods twice: " + Try(() => SameObjects(all, type.GetMethods(All))));
			Line("GetProperties twice: " + Try(() => SameObjects(type.GetProperties(All), type.GetProperties(All))));
			Line("GetConstructors twice: " + Try(() => SameObjects(type.GetConstructors(All), type.GetConstructors(All))));
			Line("getter == get_Count: " + Try(() => type.GetProperty("Count").GetGetMethod() == type.GetMethod("get_Count")));
			Line("from its handle: " + Try(() => MethodBase.GetMethodFromHandle(byName.MethodHandle, type.TypeHandle) == byName));
			Line("every method from its handle: " + Try(() => all.Count(method => MethodBase.GetMethodFromHandle(method.MethodHandle, type.TypeHandle) == method) + " of " + all.Length));
			Line("the token of the definition: " + Try(() => typeof(List<>).GetMethod("Contains").MetadataToken == byName.MetadataToken));
			Line("the definition of the type: " + Try(() => type.GetGenericTypeDefinition() == typeof(List<>)));
			var map = type.GetInterfaceMap(typeof(IList<ElemC>));
			Line("targets of the map among GetMethods: " + Try(() => map.TargetMethods.Count(target => all.Any(method => method == target)) + " of " + map.TargetMethods.Length));
			var ofInterface = typeof(IList<ElemC>).GetMethods(All);
			Line("methods of the map among those of the interface: " + Try(() => map.InterfaceMethods.Count(mapped => ofInterface.Any(method => method == mapped)) + " of " + map.InterfaceMethods.Length));
			Line("the map: " + Try(() => string.Join(", ", map.InterfaceMethods.Select((mapped, at) => mapped.Name + ">" + Short(map.TargetMethods[at])))));

			var own = typeof(Own<ElemC>);
			var derived = typeof(Derived<ElemC>);
			Line("add == add_Changed: " + Try(() => own.GetEvent("Changed").GetAddMethod() == own.GetMethod("add_Changed")));
			Line("setter == set_Value: " + Try(() => own.GetProperty("Value").GetSetMethod() == own.GetMethod("set_Value")));
			Line("inherited, declared by: " + Try(() => derived.GetMethod("Count").DeclaringType) + ", reflected by: " + Try(() => derived.GetMethod("Count").ReflectedType));
			Line("inherited == the one of the base: " + Try(() => derived.GetMethod("Count") == own.GetMethod("Count")));
			Line("base definition: " + Try(() => derived.GetMethod("Describe").GetBaseDefinition() == own.GetMethod("Describe")));
			Line("generic method: " + Try(() => own.GetMethod("Convert").IsGenericMethodDefinition) + ", made: " + Try(() => own.GetMethod("Convert").MakeGenericMethod(typeof(string)))
				+ ", back: " + Try(() => own.GetMethod("Convert").MakeGenericMethod(typeof(string)).GetGenericMethodDefinition() == own.GetMethod("Convert")));
			Line("the map of the project's own: " + Try(() =>
			{
				var ownMap = own.GetInterfaceMap(typeof(IOwn<ElemC>));
				return string.Join(", ", ownMap.InterfaceMethods.Select((mapped, at) => mapped.Name + ">" + Short(ownMap.TargetMethods[at])));
			}));
			Line("the constructor of the type made at run time: " + Try(() => typeof(Own<>).MakeGenericType(typeof(ElemC)) == own));
		}

		static void Invoke()
		{
			var type = typeof(List<ElemD>);
			var list = Activator.CreateInstance(type);
			var item = new ElemD { Value = 9 };
			foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
			{
				// The ones that take one element or nothing, in the order reflection lists them.
				var parameters = method.GetParameters();
				if (method.IsGenericMethodDefinition || method.Name == "GetHashCode" || method.Name == "ToString" || method.Name == "GetType")
				{
					continue;
				}
				object[] arguments;
				if (parameters.Length == 0)
				{
					arguments = new object[0];
				}
				else if (parameters.Length == 1 && parameters[0].ParameterType == typeof(ElemD))
				{
					arguments = new object[] { item };
				}
				else
				{
					continue;
				}
				var invoked = method;
				Line("List." + method.Name + " -> " + Try(() => invoked.Invoke(list, arguments)));
			}
			((List<ElemD>)list).Add(item);
			foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
			{
				var read = property;
				Line("List." + property.Name + " = " + Try(() => read.GetValue(list, read.GetIndexParameters().Length == 0 ? null : new object[] { 0 })));
			}
			Line("InvokeMember: " + Try(() => type.InvokeMember("Count", BindingFlags.GetProperty | BindingFlags.Public | BindingFlags.Instance, null, list, null)));

			var own = typeof(Own<ElemD>);
			Line("constructors: " + Try(() => string.Join(", ", own.GetConstructors(All).Select(constructor => constructor.ToString()))));
			var made = own.GetConstructor(new[] { typeof(ElemD) }).Invoke(new object[] { item });
			Line("constructed: " + Try(() => made.GetType()));
			var heard = 0;
			Line("AddEventHandler: " + Try(() =>
			{
				own.GetEvent("Changed").AddEventHandler(made, (Action<ElemD>)(changed => heard += changed.Value));
				return "added";
			}));
			Line("SetValue: " + Try(() =>
			{
				own.GetProperty("Value").SetValue(made, new ElemD { Value = 4 });
				return "heard " + heard;
			}));
			Line("GetValue: " + Try(() => own.GetProperty("Value").GetValue(made)));
			Line("Count: " + Try(() => own.GetMethod("Count").Invoke(made, new object[] { item })));
			Line("Create: " + Try(() => own.GetMethod("Create").Invoke(null, new object[] { item }).GetType()));
			Line("Made: " + Try(() => (int)own.GetProperty("Made").GetValue(null) > 0));
			Line("Private: " + Try(() => own.GetMethod("Private", All).Invoke(made, new object[] { 6 })));
			Line("Hidden: " + Try(() => own.GetProperty("Hidden", All).GetValue(made)));
			Line("Describe: " + Try(() => own.GetMethod("Describe").Invoke(made, null)));
			Line("Describe of the derived, through the method of the base: " + Try(() => own.GetMethod("Describe").Invoke(new Derived<ElemD>(item), null)));
			Line("Convert<string>: " + Try(() => own.GetMethod("Convert").MakeGenericMethod(typeof(string)).Invoke(made, new object[] { (Func<ElemD, string>)(converted => "x" + converted.Value) })));
			Line("Kept: " + Try(() =>
			{
				own.GetField("Kept").SetValue(made, item);
				return own.GetField("Kept").GetValue(made);
			}));
			Line("Nested: " + Try(() =>
			{
				var nestedType = typeof(Own<ElemD>.Nested);
				var nested = Activator.CreateInstance(nestedType);
				return nestedType.GetMethod("Twice").Invoke(nested, new object[] { 21 });
			}));
			Line("CreateDelegate from the method: " + Try(() => ((Func<ElemD, int>)Delegate.CreateDelegate(typeof(Func<ElemD, int>), made, own.GetMethod("Count")))(item)));
			Line("CreateDelegate from the name: " + Try(() => ((Func<ElemD, int>)Delegate.CreateDelegate(typeof(Func<ElemD, int>), made, "Count"))(item)));
			Line("an open delegate: " + Try(() => ((Func<Own<ElemD>, ElemD, int>)Delegate.CreateDelegate(typeof(Func<Own<ElemD>, ElemD, int>), own.GetMethod("Count")))((Own<ElemD>)made, item)));

			// A type the program never names, made at run time.
			var runtime = typeof(Own<>).MakeGenericType(typeof(ElemD2));
			Line("made at run time: " + Try(() => runtime) + ", methods " + Try(() => runtime.GetMethods(All).Length) + ", properties " + Try(() => runtime.GetProperties(All).Length) + ", events " + Try(() => runtime.GetEvents(All).Length));
			Line("made at run time, constructed: " + Try(() => Activator.CreateInstance(runtime).GetType()));
			Line("made at run time, Create and Count: " + Try(() =>
			{
				var element = new ElemD2 { Value = 8 };
				var created = runtime.GetMethod("Create").Invoke(null, new object[] { element });
				return runtime.GetMethod("Count").Invoke(created, new object[] { element });
			}));
		}

		static string Marks(Func<object[]> read)
		{
			return Try(() => string.Join(", ", read().Cast<MarkAttribute>().Select(mark => mark.Number + ":" + mark.Name + ":" + mark.Kind + ":" + mark.Field)));
		}

		static void Attributes()
		{
			var own = typeof(Own<ElemE>);
			Line("type | defined " + Try(() => own.IsDefined(typeof(MarkAttribute), false)) + " | marks " + Marks(() => own.GetCustomAttributes(typeof(MarkAttribute), false)) + " | all " + Try(() => own.GetCustomAttributes(true).Length));
			foreach (var member in own.GetMembers(All | BindingFlags.DeclaredOnly))
			{
				var read = member;
				Line(member.MemberType + " " + member.Name + " | defined " + Try(() => read.IsDefined(typeof(MarkAttribute), false))
					+ " | marks " + Marks(() => read.GetCustomAttributes(typeof(MarkAttribute), false))
					+ " | all " + Try(() => read.GetCustomAttributes(true).Length)
					+ " | data " + Try(() => string.Join("; ", CustomAttributeData.GetCustomAttributes(read).Select(data =>
						data.AttributeType.Name + "(" + string.Join(",", data.ConstructorArguments.Select(argument => argument.Value)) + ")"
						+ "{" + string.Join(",", data.NamedArguments.Select(argument => argument.MemberName + "=" + argument.TypedValue.Value)) + "}"))));
			}
			foreach (var parameter in own.GetMethod("Count").GetParameters())
			{
				var read = parameter;
				Line("parameter " + parameter.Name + " | marks " + Marks(() => read.GetCustomAttributes(typeof(MarkAttribute), false)));
			}
			// The same members, reached through the derived instance and through the interface.
			Line("inherited method | marks " + Marks(() => typeof(Derived<ElemE>).GetMethod("Count").GetCustomAttributes(typeof(MarkAttribute), true)));
			Line("interface method | marks " + Marks(() => typeof(IOwn<ElemE>).GetMethod("Count").GetCustomAttributes(typeof(MarkAttribute), true)));
			Line("the engine's own | " + Try(() => typeof(List<ElemE>).GetCustomAttributes(true).Length) + " on the type, "
				+ Try(() => typeof(List<ElemE>).GetMembers(All).Sum(member => member.GetCustomAttributes(true).Length)) + " on its members");
		}

		static void StaticConstructor()
		{
			var start = Counters.StaticConstructors;
			var type = typeof(Own<ElemF>);
			Members(type, new List<string>());
			Line("after listing: " + (Counters.StaticConstructors - start));
			type.GetMethod("Count");
			type.GetProperty("Made");
			type.GetConstructors(All);
			Line("after lookups: " + (Counters.StaticConstructors - start));
			Line("the type initializer: " + Try(() => type.TypeInitializer));
			Line("after asking for the type initializer: " + (Counters.StaticConstructors - start));
			Line("Made: " + Try(() => type.GetProperty("Made").GetValue(null)));
			Line("after the static getter: " + (Counters.StaticConstructors - start));
			var made = new Own<ElemF>();
			Line("after new: " + (Counters.StaticConstructors - start) + ", " + (made != null));
			// The runtime finds the static constructor of this one by its name, before anything listed its methods.
			RuntimeHelpers.RunClassConstructor(typeof(Own<ElemF2>).TypeHandle);
			Line("after RunClassConstructor: " + (Counters.StaticConstructors - start));
			RuntimeHelpers.RunClassConstructor(typeof(Own<ElemF2>).TypeHandle);
			Line("after RunClassConstructor again: " + (Counters.StaticConstructors - start));
			Line("Made of that one: " + Own<ElemF2>.Made + ", methods " + Try(() => typeof(Own<ElemF2>).GetMethods(All).Length));
		}

		static void NameLookups()
		{
			var item = new ElemG { Value = 6 };
			var own = new Own<ElemG>(item);
			Func<ElemG, int> count = own.Count;
			// Invoke of a delegate type is found by name the first time.
			Line("DynamicInvoke: " + Try(() => count.DynamicInvoke(item)));
			Line("Invoke of the delegate type: " + Try(() => typeof(Func<ElemG, int>).GetMethod("Invoke")));
			Line("Invoke, invoked: " + Try(() => typeof(Func<ElemG, int>).GetMethod("Invoke").Invoke(count, new object[] { item })));
			Line("the method of the delegate: " + Try(() => Short(count.Method)) + ", target " + Try(() => count.Target.GetType()));
			Action<ElemG2> never = _ => { };
			Line("an Action nobody listed: " + Try(() => never.DynamicInvoke(new ElemG2())) + ", " + Try(() => never.GetType().GetMethod("Invoke").GetParameters().Length));

			var type = typeof(List<ElemG>);
			Line("GetMethod with types: " + Try(() => type.GetMethod("IndexOf", new[] { typeof(ElemG) })));
			Line("GetMethod with more types: " + Try(() => type.GetMethod("IndexOf", new[] { typeof(ElemG), typeof(int) })));
			Line("GetProperty with a type: " + Try(() => type.GetProperty("Item", typeof(ElemG))));
			Line("GetMember: " + Try(() => string.Join(", ", type.GetMember("Add*", All).Select(member => member.ToString()))));
			Line("GetDefaultMembers: " + Try(() => string.Join(", ", type.GetDefaultMembers().Select(member => member.ToString()))));
			Line("GetRuntimeMethods: " + Try(() => type.GetRuntimeMethods().Count()) + ", properties " + Try(() => type.GetRuntimeProperties().Count()) + ", events " + Try(() => typeof(Own<ElemG>).GetRuntimeEvents().Count()));
			Line("GetConstructor: " + Try(() => type.GetConstructor(Type.EmptyTypes)) + ", " + Try(() => type.GetConstructor(new[] { typeof(int) })));
			Line("CreateInstance with arguments: " + Try(() => Activator.CreateInstance(typeof(Own<ElemG2>), new ElemG2 { Value = 2 }).GetType()));
			Line("CreateInstance of a type made at run time: " + Try(() => Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(typeof(ElemG2), typeof(ElemG))).GetType()));
			Line("a comparer made by the runtime: " + Try(() => EqualityComparer<ElemG2>.Default.GetType()) + ", " + Try(() => Comparer<ElemG2>.Default.GetType()));
			Line("an enumerator, boxed: " + Try(() =>
			{
				IEnumerable<ElemG> items = new List<ElemG> { item };
				var enumerator = items.GetEnumerator();
				var moved = enumerator.GetType().GetMethod("MoveNext").Invoke(enumerator, null);
				return enumerator.GetType() + " " + moved + " " + Show(enumerator.GetType().GetProperty("Current").GetValue(enumerator, null));
			}));
		}

		static void Behaviour()
		{
			Expect(s_behaviour != null, "Begin did not run");
			Line("after AddComponent: " + s_added);
			Line("after SendMessage, Invoke and StartCoroutine: " + s_messaged);
			Line("some frames later: " + s_behaviour.Counts());
			Line("methods of the base: " + Try(() => Names(typeof(ReflectionBehaviour).BaseType.GetMethods(All | BindingFlags.DeclaredOnly))));
			Line("GetComponent through the base: " + Try(() => s_behaviour.GetComponent<ReflectionBehaviourBase<ReflectionBehaviour.Probe>>() == s_behaviour));
			Line("GetComponent by type: " + Try(() => s_behaviour.GetComponent(typeof(ReflectionBehaviourBase<ReflectionBehaviour.Probe>)) == s_behaviour));
		}

		// ---- threads: some list a type while others use it for the first time ----

		static uint Fnv(uint hash, string text)
		{
			foreach (var letter in text)
			{
				hash = (hash ^ letter) * 16777619;
			}
			return hash;
		}

		static uint Scan(Type[] types)
		{
			var hash = 2166136261;
			foreach (var type in types)
			{
				foreach (var member in type.GetMembers(All))
				{
					hash = Fnv(hash, member.MemberType + member.Name + member.MetadataToken);
				}
				foreach (var method in type.GetMethods(All))
				{
					hash = Fnv(hash, method.Name + method.GetParameters().Length + method.MetadataToken);
				}
				foreach (var property in type.GetProperties(All))
				{
					var getter = property.GetGetMethod(true);
					hash = Fnv(hash, property.Name + (getter == null ? "" : getter.Name));
				}
				foreach (var raised in type.GetEvents(All))
				{
					var add = raised.GetAddMethod(true);
					hash = Fnv(hash, raised.Name + (add == null ? "" : add.Name));
				}
				foreach (var constructor in type.GetConstructors(All))
				{
					hash = Fnv(hash, constructor.Name + constructor.GetParameters().Length);
				}
			}
			return hash;
		}

		static int UseOnce<T>(T seed)
			where T : class
		{
			var sum = 0;
			var list = new List<T> { seed, seed };
			sum += list.Count + list.IndexOf(seed) + (list.Contains(seed) ? 1 : 0);
			Func<T, bool> contains = list.Contains;
			sum += contains(seed) ? 1 : 0;
			IList<T> asList = list;
			sum += asList.Count;
			var dictionary = new Dictionary<T, int> { { seed, 11 } };
			sum += dictionary[seed];
			var set = new HashSet<T> { seed };
			sum += set.Count;
			var own = new Own<T>(seed);
			var heard = 0;
			Action<T> hear = _ => heard++;
			own.Changed += hear;
			own.Value = seed;
			sum += own.Count(seed) + own.Convert(item => item == null ? 0 : 2);
			IOwn<T> viaInterface = own;
			sum += viaInterface.Count(seed);
			Func<T, int> count = own.Count;
			sum += count(seed);
			var derived = new Derived<T>(seed);
			sum += derived.Describe().Length > 0 ? 1 : 0;
			sum += new Own<T>.Nested { Inner = seed }.Twice(2);
			return sum + heard;
		}

		/// <summary>
		/// One race for T, then the same for a type made of T, which nothing has touched either: every level is a first use.
		/// </summary>
		static void Race<T>(T seed, int depth, string label)
			where T : class
		{
			const int Scanners = 3;
			const int Users = 3;
			var types = new[]
			{
				typeof(List<T>),
				typeof(Dictionary<T, int>),
				typeof(HashSet<T>),
				typeof(Own<T>),
				typeof(Derived<T>),
				typeof(IOwn<T>),
				typeof(Own<T>.Nested),
				typeof(Func<T, bool>),
				typeof(Func<T, int>),
				typeof(Action<T>),
				typeof(IList<T>),
			};
			var scans = new uint[Scanners];
			var uses = new int[Users];
			var errors = new Exception[Scanners + Users];
			var threads = new Thread[Scanners + Users];
			var ready = 0;
			s_go = false;
			for (var at = 0; at < threads.Length; at++)
			{
				var mine = at;
				threads[at] = new Thread(() =>
				{
					Interlocked.Increment(ref ready);
					while (!s_go)
					{
					}
					try
					{
						if (mine < Scanners)
						{
							scans[mine] = Scan(types);
						}
						else
						{
							uses[mine - Scanners] = UseOnce(seed);
						}
					}
					catch (Exception exception)
					{
						errors[mine] = exception;
					}
				});
				threads[at].Start();
			}
			while (Volatile.Read(ref ready) < threads.Length)
			{
				Thread.Yield();
			}
			s_go = true;
			foreach (var thread in threads)
			{
				thread.Join();
			}
			for (var at = 0; at < errors.Length; at++)
			{
				Expect(errors[at] == null, label + ", thread " + at + ": " + (errors[at] == null ? "" : errors[at].GetType().Name + ": " + Flat(errors[at].Message)));
			}
			// What one thread gets afterwards is what every thread of the race has to have got.
			var scan = Scan(types);
			var use = UseOnce(seed);
			for (var at = 0; at < Scanners; at++)
			{
				Expect(scans[at] == scan, label + ", thread " + at + " listed " + scans[at].ToString("x8") + ", afterwards " + scan.ToString("x8"));
			}
			for (var at = 0; at < Users; at++)
			{
				Expect(uses[at] == use, label + ", thread " + (Scanners + at) + " computed " + uses[at] + ", afterwards " + use);
			}
			Line(label + " | listed " + scan.ToString("x8") + " | computed " + use);
			if (depth > 0)
			{
				Race(new Wrap<T> { Inner = seed }, depth - 1, label + "+");
			}
		}

		static void EnumerateWhileFirstUsed()
		{
			Race(new ElemH1(), 2, "H1");
			Race(new ElemH2(), 2, "H2");
			Race(new ElemH3(), 2, "H3");
			Race(new ElemH4(), 2, "H4");
			Race(new ElemH5(), 2, "H5");
			Race(new ElemH6(), 2, "H6");
			Race(new ElemH7(), 2, "H7");
			Race(new ElemH8(), 2, "H8");
			Race(new ElemI(), 24, "I");
		}
	}
}
