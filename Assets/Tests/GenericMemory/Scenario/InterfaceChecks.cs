using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;

namespace GenericMemory
{
	/// <summary>
	/// Calls that reach a generic instance or an array through one of its interfaces, in every way the runtime has of
	/// finding the method: the inlined lookup of the generated code, variance, casts, delegates, generic virtual
	/// methods, reflection, and the virtual call through the class that reads the same slot. Where being first matters,
	/// a check uses element types no other check uses, so that its first round is the first to reach those interfaces.
	/// </summary>
	public static class InterfaceChecks
	{
		const int Rounds = 3;

		sealed class CheckFailed : Exception
		{
			public CheckFailed(string message)
				: base(message)
			{
			}
		}

		static readonly KeyValuePair<string, Action>[] s_checks =
		{
			Check("ArrayAsIList", ArrayAsIList),
			Check("ArrayAsOtherInterfaces", ArrayAsOtherInterfaces),
			Check("ArrayVariance", ArrayVariance),
			Check("ArrayOfValues", ArrayOfValues),
			Check("ListThroughInterfaces", ListThroughInterfaces),
			Check("ListVariance", ListVariance),
			Check("NonGenericInterfaces", NonGenericInterfaces),
			Check("Casts", Casts),
			Check("BoxedEnumerators", BoxedEnumerators),
			Check("InterfaceDelegates", InterfaceDelegates),
			Check("OpenDelegates", OpenDelegates),
			Check("GenericVirtualMethods", GenericVirtualMethods),
			Check("InterfaceMap", InterfaceMap),
			Check("ReflectionInvoke", ReflectionInvoke),
			Check("RuntimeMadeTypes", RuntimeMadeTypes),
			Check("LinqOverArray", LinqOverArray),
			Check("LinqOverList", LinqOverList),
			Check("DictionaryCollections", DictionaryCollections),
			Check("DictionaryThroughInterfaces", DictionaryThroughInterfaces),
			Check("VirtualFirstThenInterface", VirtualFirstThenInterface),
			Check("InterfaceFirstThenVirtual", InterfaceFirstThenVirtual),
			Check("Reimplemented", Reimplemented),
			Check("NonGenericAbstract", NonGenericAbstract),
			Check("Comparers", Comparers),
			Check("OwnCollection", OwnCollection),
			Check("BoxedGenericStruct", BoxedGenericStruct),
			Check("DefaultInterfaceMethod", DefaultInterfaceMethod),
			Check("ConstrainedCalls", ConstrainedCalls),
			Check("DerivedFromGeneric", DerivedFromGeneric),
			Check("FirstCallFromManyThreads", FirstCallFromManyThreads),
		};

		public static int Failed { get; private set; }

		static KeyValuePair<string, Action> Check(string name, Action body)
		{
			return new KeyValuePair<string, Action>(name, body);
		}

		/// <summary>Runs every check <see cref="Rounds"/> times and writes one line for each run.</summary>
		public static void Run(StringBuilder text)
		{
			foreach (var check in s_checks)
			{
				for (var round = 0; round < Rounds; round++)
				{
					string detail = null;
					try
					{
						check.Value();
					}
					catch (Exception exception)
					{
						var inner = exception is TargetInvocationException && exception.InnerException != null ? exception.InnerException : exception;
						detail = inner.GetType().Name + ": " + inner.Message.Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ');
						Failed++;
					}
					text.Append("check\t").Append(check.Key).Append('\t').Append(round).Append('\t').Append(detail == null ? "PASS" : "FAIL").Append('\t').Append(detail ?? "").Append('\n');
				}
			}
		}

		static void Expect(bool condition, string what)
		{
			if (!condition)
			{
				throw new CheckFailed(what);
			}
		}

		static void Same(long expected, long actual, string what)
		{
			if (expected != actual)
			{
				throw new CheckFailed(what + ": expected " + expected + ", got " + actual);
			}
		}

		static void Same(string expected, string actual, string what)
		{
			if (expected != actual)
			{
				throw new CheckFailed(what + ": expected " + expected + ", got " + actual);
			}
		}

		// ---- element types: one or two for each check ----

		interface IItem
		{
			int Value { get; }
		}

		abstract class Item : IItem, IComparable<Item>, IComparable
		{
			public int Value { get; set; }

			public int CompareTo(Item other)
			{
				return Value.CompareTo(other.Value);
			}

			public int CompareTo(object other)
			{
				return CompareTo((Item)other);
			}
		}

		sealed class E01 : Item { }
		sealed class E02 : Item { }
		sealed class E03 : Item { }
		sealed class E04 : Item { }
		sealed class E05 : Item { }
		sealed class E06 : Item { }
		sealed class E07 : Item { }
		sealed class E08 : Item { }
		sealed class E09 : Item { }
		sealed class E10 : Item { }
		sealed class E11 : Item { }
		sealed class E12 : Item { }
		sealed class E13 : Item { }
		sealed class E14 : Item { }
		sealed class E15 : Item { }
		sealed class E16 : Item { }
		sealed class E17 : Item { }
		sealed class E18 : Item { }
		sealed class E19 : Item { }
		sealed class E20 : Item { }
		sealed class E21 : Item { }
		sealed class E22 : Item { }
		sealed class E23 : Item { }
		sealed class E24 : Item { }
		sealed class E25 : Item { }
		sealed class E26 : Item { }
		sealed class E27 : Item { }
		sealed class E28 : Item { }
		sealed class E29 : Item { }
		sealed class E30 : Item { }
		sealed class E31 : Item { }
		sealed class E32 : Item { }
		sealed class E33 : Item { }
		sealed class E34 : Item { }
		sealed class E35 : Item { }
		sealed class E36 : Item { }

		struct V01 : IItem
		{
			public int Number;

			public int Value => Number;
		}

		struct V02 : IItem
		{
			public int Number;

			public int Value => Number;
		}

		struct V03 : IItem
		{
			public int Number;

			public int Value => Number;
		}

		static T[] Make<T>(int count)
			where T : Item, new()
		{
			var items = new T[count];
			for (var index = 0; index < count; index++)
			{
				items[index] = new T { Value = index + 1 };
			}
			return items;
		}

		static List<T> MakeList<T>(int count)
			where T : Item, new()
		{
			var list = new List<T>();
			for (var index = 0; index < count; index++)
			{
				list.Add(new T { Value = index + 1 });
			}
			return list;
		}

		// ---- arrays ----

		static void ArrayAsIList()
		{
			var array = Make<E01>(5);
			IList<E01> list = array;
			Same(5, list.Count, "Count");
			Same(3, list[2].Value, "indexer");
			Same(4, list.IndexOf(array[4]), "IndexOf");
			Expect(list.Contains(array[1]), "Contains");
			Expect(list.IsReadOnly, "IsReadOnly");
			var replaced = new E01 { Value = 40 };
			list[3] = replaced;
			Expect(ReferenceEquals(array[3], replaced), "the setter writes the array");
			var copy = new E01[6];
			list.CopyTo(copy, 1);
			Expect(ReferenceEquals(copy[1], array[0]) && copy[0] == null, "CopyTo");
			var refused = false;
			try
			{
				list.Add(new E01());
			}
			catch (NotSupportedException)
			{
				refused = true;
			}
			Expect(refused, "Add on an array throws NotSupportedException");
			var total = 0;
			foreach (var item in list)
			{
				total += item.Value;
			}
			Same(1 + 2 + 3 + 40 + 5, total, "enumeration");
		}

		static void ArrayAsOtherInterfaces()
		{
			ICollection<E02> collection = Make<E02>(4);
			Same(4, collection.Count, "ICollection<T>.Count");
			Expect(!collection.Contains(new E02()), "ICollection<T>.Contains");

			IEnumerable<E03> enumerable = Make<E03>(4);
			var total = 0;
			using (var enumerator = enumerable.GetEnumerator())
			{
				while (enumerator.MoveNext())
				{
					total += enumerator.Current.Value;
				}
			}
			Same(10, total, "IEnumerable<T>");

			IReadOnlyList<E04> readOnly = Make<E04>(6);
			Same(6, readOnly.Count, "IReadOnlyList<T>.Count");
			Same(6, readOnly[5].Value, "IReadOnlyList<T> indexer");

			IReadOnlyCollection<E05> readOnlyCollection = Make<E05>(7);
			Same(7, readOnlyCollection.Count, "IReadOnlyCollection<T>.Count");
			Same(28, readOnlyCollection.Sum(item => item.Value), "IReadOnlyCollection<T> enumeration");
		}

		static void ArrayVariance()
		{
			var array = Make<E06>(4);
			IEnumerable<object> objects = array;
			var count = 0;
			foreach (var item in objects)
			{
				count += ((IItem)item).Value;
			}
			Same(10, count, "E[] as IEnumerable<object>");

			IList<IItem> items = array;
			Same(4, items.Count, "E[] as IList<IItem>, Count");
			Same(2, items[1].Value, "E[] as IList<IItem>, indexer");
			Same(2, items.IndexOf(array[2]), "E[] as IList<IItem>, IndexOf");

			IReadOnlyList<Item> bases = array;
			Same(4, bases[3].Value, "E[] as IReadOnlyList<Item>");
			IList<Item> writable = array;
			var stored = false;
			try
			{
				writable[0] = new E07();
			}
			catch (ArrayTypeMismatchException)
			{
				stored = true;
			}
			Expect(stored, "storing another type through IList<Item> throws ArrayTypeMismatchException");

			var jagged = new[] { Make<E07>(2), Make<E07>(3) };
			IEnumerable<IEnumerable<Item>> nested = jagged;
			var sum = 0;
			foreach (var row in nested)
			{
				foreach (var item in row)
				{
					sum += item.Value;
				}
			}
			Same(3 + 6, sum, "E[][] as IEnumerable<IEnumerable<Item>>");
			IList<E07[]> rows = jagged;
			Same(3, rows[1].Length, "E[][] as IList<E[]>");
		}

		static void ArrayOfValues()
		{
			var values = new[] { new V01 { Number = 3 }, new V01 { Number = 4 } };
			IList<V01> list = values;
			Same(2, list.Count, "V[] as IList<V>, Count");
			Same(4, list[1].Value, "V[] as IList<V>, indexer");
			IEnumerable<V01> enumerable = values;
			Same(7, enumerable.Sum(value => value.Number), "V[] as IEnumerable<V>");
			IReadOnlyList<long> numbers = new long[] { 5, 6, 7 };
			Same(7, numbers[2], "long[] as IReadOnlyList<long>");
			ICollection<KeyValuePair<int, V01>> pairs = new[] { new KeyValuePair<int, V01>(1, values[0]) };
			Same(1, pairs.Count, "KeyValuePair<int, V>[] as ICollection");
			Expect(pairs.Contains(new KeyValuePair<int, V01>(1, values[0])), "KeyValuePair<int, V>[] Contains");
		}

		// ---- List<T> ----

		static void ListThroughInterfaces()
		{
			var list = MakeList<E08>(5);
			IList<E08> asList = list;
			ICollection<E08> asCollection = list;
			IEnumerable<E08> asEnumerable = list;
			IReadOnlyList<E08> asReadOnlyList = list;
			IReadOnlyCollection<E08> asReadOnlyCollection = list;
			// The read-only pair first: its slots come after those of IList<T> and ICollection<T>.
			Same(5, asReadOnlyCollection.Count, "IReadOnlyCollection<T>.Count");
			Same(2, asReadOnlyList[1].Value, "IReadOnlyList<T> indexer");
			var added = new E08 { Value = 6 };
			asCollection.Add(added);
			Same(6, asList.Count, "IList<T>.Count after ICollection<T>.Add");
			Same(5, asList.IndexOf(added), "IList<T>.IndexOf");
			asList.Insert(0, new E08 { Value = 100 });
			asList.RemoveAt(1);
			Expect(asCollection.Remove(added), "ICollection<T>.Remove");
			Expect(!asCollection.IsReadOnly, "ICollection<T>.IsReadOnly");
			var total = 0;
			foreach (var item in asEnumerable)
			{
				total += item.Value;
			}
			Same(100 + 2 + 3 + 4 + 5, total, "IEnumerable<T>");
			Same(5, list.Count, "the list itself");
		}

		static void ListVariance()
		{
			var list = MakeList<E09>(4);
			IEnumerable<object> objects = list;
			var total = 0;
			using (IEnumerator<object> enumerator = objects.GetEnumerator())
			{
				while (enumerator.MoveNext())
				{
					total += ((Item)enumerator.Current).Value;
				}
			}
			Same(10, total, "List<E> as IEnumerable<object>");
			IEnumerable<IItem> items = list;
			Same(10, items.Sum(item => item.Value), "List<E> as IEnumerable<IItem>");
			IReadOnlyList<Item> bases = list;
			Same(3, bases[2].Value, "List<E> as IReadOnlyList<Item>");
			IReadOnlyCollection<object> counted = list;
			Same(4, counted.Count, "List<E> as IReadOnlyCollection<object>");
		}

		static void NonGenericInterfaces()
		{
			var list = MakeList<E10>(3);
			IList plain = list;
			Same(3, plain.Count, "IList.Count");
			Same(2, ((Item)plain[1]).Value, "IList indexer");
			plain.Add(new E10 { Value = 9 });
			Expect(plain.Contains(list[3]), "IList.Contains");
			var refused = false;
			try
			{
				plain.Add("text");
			}
			catch (ArgumentException)
			{
				refused = true;
			}
			Expect(refused, "IList.Add of another type throws ArgumentException");
			var total = 0;
			foreach (object item in (IEnumerable)list)
			{
				total += ((Item)item).Value;
			}
			Same(15, total, "IEnumerable");
			ICollection collection = list;
			var copy = new object[4];
			collection.CopyTo(copy, 0);
			Expect(ReferenceEquals(copy[3], list[3]), "ICollection.CopyTo");

			IList array = Make<E11>(3);
			Same(3, array.Count, "E[] as IList, Count");
			Same(1, array.IndexOf(array[1]), "E[] as IList, IndexOf");
		}

		static void Casts()
		{
			object list = MakeList<E12>(2);
			Expect(list is IList<E12>, "List<E> is IList<E>");
			Expect(list is IEnumerable<object>, "List<E> is IEnumerable<object>");
			Expect(list is IReadOnlyList<Item>, "List<E> is IReadOnlyList<Item>");
			Expect(!(list is IList<object>), "List<E> is not IList<object>");
			Expect(!(list is IList<E13>), "List<E> is not IList of another type");
			Expect(list is IList, "List<E> is IList");
			var collection = list as ICollection<E12>;
			Expect(collection != null, "List<E> as ICollection<E>");
			Same(2, collection.Count, "Count after as");
			Same(2, ((IReadOnlyCollection<IItem>)list).Count, "Count after a cast to a variant interface");

			object array = Make<E13>(2);
			Expect(array is IList<E13>, "E[] is IList<E>");
			Expect(array is IList<Item>, "E[] is IList<Item>");
			Expect(array is IEnumerable<IItem>, "E[] is IEnumerable<IItem>");
			Expect(!(array is IList<E12>), "E[] is not IList of another type");
			Same(2, ((IReadOnlyList<object>)array).Count, "Count after a cast of the array");
			var invalid = false;
			try
			{
				GC.KeepAlive((IList<E12>)array);
			}
			catch (InvalidCastException)
			{
				invalid = true;
			}
			Expect(invalid, "a cast to an interface the array does not have throws InvalidCastException");

			Expect(typeof(IList<E12>).IsAssignableFrom(list.GetType()), "IsAssignableFrom");
			Expect(typeof(IEnumerable<Item>).IsAssignableFrom(array.GetType()), "IsAssignableFrom of the array");
			Expect(list.GetType().GetInterfaces().Contains(typeof(IReadOnlyList<E12>)), "GetInterfaces");
		}

		static void BoxedEnumerators()
		{
			var list = MakeList<E14>(3);
			IEnumerator<E14> typed = ((IEnumerable<E14>)list).GetEnumerator();
			IEnumerator plain = typed;
			var total = 0;
			while (plain.MoveNext())
			{
				total += typed.Current.Value;
				total += ((Item)plain.Current).Value;
			}
			Same(12, total, "List<E>.Enumerator through IEnumerator<E> and IEnumerator");
			plain.Reset();
			Expect(plain.MoveNext() && typed.Current.Value == 1, "Reset");
			((IDisposable)typed).Dispose();

			var dictionary = new Dictionary<int, E14> { { 1, list[0] }, { 2, list[1] } };
			IEnumerator<KeyValuePair<int, E14>> pairs = ((IEnumerable<KeyValuePair<int, E14>>)dictionary).GetEnumerator();
			var keys = 0;
			while (pairs.MoveNext())
			{
				keys += pairs.Current.Key + pairs.Current.Value.Value;
			}
			pairs.Dispose();
			Same(6, keys, "Dictionary<int, E>.Enumerator through IEnumerator<KeyValuePair>");
			IDictionaryEnumerator entries = ((IDictionary)dictionary).GetEnumerator();
			var values = 0;
			while (entries.MoveNext())
			{
				values += (int)entries.Key + ((Item)entries.Value).Value + ((Item)entries.Entry.Value).Value;
			}
			Same(9, values, "Dictionary<int, E>.Enumerator through IDictionaryEnumerator");

			var numbers = new List<V02> { new V02 { Number = 5 }, new V02 { Number = 6 } };
			IEnumerator<V02> boxed = ((IEnumerable<V02>)numbers).GetEnumerator();
			var sum = 0;
			while (boxed.MoveNext())
			{
				sum += boxed.Current.Number;
			}
			boxed.Dispose();
			Same(11, sum, "List<V>.Enumerator through IEnumerator<V>");

			IEnumerator<E15> array = ((IEnumerable<E15>)Make<E15>(3)).GetEnumerator();
			var arrayTotal = 0;
			while (((IEnumerator)array).MoveNext())
			{
				arrayTotal += array.Current.Value;
			}
			Same(6, arrayTotal, "the enumerator of an array");
		}

		// ---- delegates ----

		static void InterfaceDelegates()
		{
			var list = MakeList<E16>(3);
			IList<E16> asList = list;
			Func<E16, bool> contains = asList.Contains;
			Action<E16> add = asList.Add;
			Func<E16, int> indexOf = asList.IndexOf;
			Func<IEnumerator<E16>> enumerate = ((IEnumerable<E16>)list).GetEnumerator;
			var added = new E16 { Value = 4 };
			add(added);
			Expect(contains(added), "a delegate to ICollection<T>.Contains");
			Same(3, indexOf(added), "a delegate to IList<T>.IndexOf");
			var total = 0;
			using (var enumerator = enumerate())
			{
				while (enumerator.MoveNext())
				{
					total += enumerator.Current.Value;
				}
			}
			Same(10, total, "a delegate to IEnumerable<T>.GetEnumerator");

			IList<E17> array = Make<E17>(3);
			Func<E17, int> arrayIndexOf = array.IndexOf;
			Func<E17, bool> arrayContains = ((ICollection<E17>)array).Contains;
			Same(2, arrayIndexOf(array[2]), "a delegate to IList<T>.IndexOf of an array");
			Expect(arrayContains(array[0]), "a delegate to ICollection<T>.Contains of an array");
		}

		static void OpenDelegates()
		{
			// The method of the class is final, and its slot belongs to an interface.
			var list = MakeList<E18>(4);
			var count = (Func<List<E18>, int>)Delegate.CreateDelegate(typeof(Func<List<E18>, int>), typeof(List<E18>).GetProperty("Count").GetGetMethod());
			Same(4, count(list), "an open delegate to List<T>.Count");
			var contains = (Func<List<E18>, E18, bool>)Delegate.CreateDelegate(typeof(Func<List<E18>, E18, bool>), typeof(List<E18>).GetMethod("Contains"));
			Expect(contains(list, list[2]), "an open delegate to List<T>.Contains");

			var other = MakeList<E19>(5);
			var interfaceCount = (Func<ICollection<E19>, int>)Delegate.CreateDelegate(typeof(Func<ICollection<E19>, int>), typeof(ICollection<E19>).GetProperty("Count").GetGetMethod());
			Same(5, interfaceCount(other), "an open delegate to ICollection<T>.Count");
			Same(2, interfaceCount(Make<E19>(2)), "an open delegate to ICollection<T>.Count, on an array");
			var closed = (Func<E19, bool>)Delegate.CreateDelegate(typeof(Func<E19, bool>), other, typeof(ICollection<E19>).GetMethod("Contains"));
			Expect(closed(other[1]), "a closed delegate made from the method of the interface");
			var closedOnClass = (Func<E19, int>)Delegate.CreateDelegate(typeof(Func<E19, int>), other, typeof(List<E19>).GetMethod("IndexOf", new[] { typeof(E19) }));
			Same(4, closedOnClass(other[4]), "a closed delegate made from the method of the class");
		}

		// ---- generic virtual methods ----

		interface IPipe
		{
			string Name<TValue>(TValue value);
		}

		interface IPipe<T>
		{
			int Count<TOther>(T first, TOther second);
		}

		sealed class Pipe<T> : IPipe, IPipe<T>
		{
			public string Name<TValue>(TValue value)
			{
				return typeof(T).Name + ":" + typeof(TValue).Name + ":" + value;
			}

			public int Count<TOther>(T first, TOther second)
			{
				return (first == null ? 0 : 1) + (second == null ? 0 : 1);
			}
		}

		static void GenericVirtualMethods()
		{
			IPipe pipe = new Pipe<E20>();
			Same("E20:String:text", pipe.Name("text"), "a generic method through an interface, reference type");
			Same("E20:Int32:7", pipe.Name(7), "a generic method through an interface, value type");
			IPipe<E20> typed = new Pipe<E20>();
			Same(2, typed.Count(new E20(), "second"), "a generic method through a generic interface");
			Same(1, typed.Count<object>(new E20(), null), "the same with another type argument");
			Func<long, string> name = pipe.Name;
			Same("E20:Int64:5", name(5), "a delegate to a generic method of an interface");
		}

		// ---- reflection ----

		static void InterfaceMap()
		{
			var map = typeof(List<E21>).GetInterfaceMap(typeof(IList<E21>));
			Same(map.InterfaceMethods.Length, map.TargetMethods.Length, "methods of the map");
			Expect(map.InterfaceMethods.Length >= 5, "IList<T> has its methods");
			for (var index = 0; index < map.InterfaceMethods.Length; index++)
			{
				Expect(map.TargetMethods[index] != null, "no target for " + map.InterfaceMethods[index].Name);
				Expect(map.TargetMethods[index].Name.EndsWith(map.InterfaceMethods[index].Name, StringComparison.Ordinal), map.InterfaceMethods[index].Name + " is mapped to " + map.TargetMethods[index].Name);
			}
			var list = MakeList<E21>(3);
			var indexOf = map.TargetMethods[Array.FindIndex(map.InterfaceMethods, method => method.Name == "IndexOf")];
			Same(2, (int)indexOf.Invoke(list, new object[] { list[2] }), "the target of the map, invoked");

			var collection = typeof(Dictionary<int, E21>).GetInterfaceMap(typeof(ICollection<KeyValuePair<int, E21>>));
			Expect(collection.TargetMethods.All(method => method != null), "the map of an interface implemented explicitly");
		}

		static void ReflectionInvoke()
		{
			var list = MakeList<E22>(3);
			var added = new E22 { Value = 4 };
			typeof(ICollection<E22>).GetMethod("Add").Invoke(list, new object[] { added });
			Same(4, list.Count, "ICollection<T>.Add invoked");
			Same(2, ((Item)typeof(IList<E22>).GetProperty("Item").GetValue(list, new object[] { 1 })).Value, "the indexer of IList<T> invoked");
			Expect((bool)typeof(List<E22>).GetMethod("Contains").Invoke(list, new object[] { added }), "List<T>.Contains invoked");
			Same(4, (int)typeof(IReadOnlyCollection<E22>).GetProperty("Count").GetValue(list), "IReadOnlyCollection<T>.Count invoked");

			var array = Make<E23>(3);
			Same(3, (int)typeof(ICollection<E23>).GetProperty("Count").GetValue(array), "ICollection<T>.Count invoked on an array");
			Same(1, (int)typeof(IList<E23>).GetMethod("IndexOf").Invoke(array, new object[] { array[1] }), "IList<T>.IndexOf invoked on an array");
			Same(3, (int)typeof(IReadOnlyCollection<Item>).GetProperty("Count").GetValue(array), "a variant interface invoked on an array");
		}

		static void RuntimeMadeTypes()
		{
			var type = typeof(List<>).MakeGenericType(typeof(E24));
			var made = Activator.CreateInstance(type);
			var typed = (ICollection<E24>)made;
			typed.Add(new E24 { Value = 3 });
			typed.Add(new E24 { Value = 4 });
			Same(2, ((IReadOnlyCollection<Item>)made).Count, "a List<E> made at run time, through a variant interface");
			Same(7, ((IEnumerable<IItem>)made).Sum(item => item.Value), "the same, enumerated");

			var array = Array.CreateInstance(typeof(E25), 3);
			array.SetValue(new E25 { Value = 8 }, 1);
			var items = (IList<E25>)array;
			Same(3, items.Count, "an array made at run time, Count");
			Same(8, items[1].Value, "an array made at run time, indexer");
		}

		// ---- LINQ ----

		static void LinqOverArray()
		{
			var array = Make<E26>(6);
			Same(3, array.Where(item => item.Value % 2 == 0).Count(), "Where, Count");
			Same(21, array.Select(item => item.Value).Sum(), "Select, Sum");
			Same(6, array.OrderByDescending(item => item.Value).First().Value, "OrderByDescending, First");
			Expect(array.Any(item => item.Value == 4), "Any");
			Same(6, array.ToList().Count, "ToList");
			Same(12, array.Concat(array).ToArray().Length, "Concat, ToArray");
			Expect(array.Contains(array[3]), "Contains");
			Same(6, array.ToDictionary(item => item.Value).Count, "ToDictionary");
			Same(2, array.GroupBy(item => item.Value % 2).Count(), "GroupBy");
			Expect(array.SequenceEqual(array.Reverse().Reverse()), "Reverse, SequenceEqual");
			Same(5, array.Skip(1).Take(10).Cast<Item>().OfType<IItem>().Last().Value - 1, "Skip, Take, Cast, OfType, Last");
			Same(3, array.Zip(array.Skip(3), (first, second) => first.Value + second.Value).Count(), "Zip");
		}

		static void LinqOverList()
		{
			var list = MakeList<E27>(6);
			Same(3, list.Where(item => item.Value % 2 == 0).Count(), "Where, Count");
			Same(21, list.Select(item => item.Value).Sum(), "Select, Sum");
			Same(1, list.OrderBy(item => item.Value).ThenBy(item => -item.Value).First().Value, "OrderBy, ThenBy, First");
			Expect(list.All(item => item.Value > 0), "All");
			Same(6, list.ToArray().Length, "ToArray");
			Same(6, list.Distinct().Count(), "Distinct");
			Same(6, list.Union(list).Count(), "Union");
			Same(0, list.Except(list).Count(), "Except");
			Same(6, list.ToLookup(item => item.Value).Count, "ToLookup");
			Same(6, new HashSet<E27>(list).Count, "HashSet from IEnumerable<T>");
			Same(4, list.ElementAt(3).Value, "ElementAt");
			Same(6, list.Max(item => item.Value), "Max");
			Same(6, new List<E27>(list.Select(item => item)).Count, "List from an iterator");
			Same(12, list.SelectMany(item => new[] { item, item }).Count(), "SelectMany");
		}

		// ---- Dictionary<TKey, TValue> ----

		static void DictionaryCollections()
		{
			var dictionary = new Dictionary<string, E28>();
			foreach (var item in Make<E28>(4))
			{
				dictionary.Add("k" + item.Value, item);
			}
			ICollection<string> keys = dictionary.Keys;
			Same(4, keys.Count, "KeyCollection as ICollection<string>");
			Expect(keys.Contains("k3"), "KeyCollection.Contains");
			Expect(keys.IsReadOnly, "KeyCollection.IsReadOnly");
			IEnumerable<string> keyEnumerable = dictionary.Keys;
			Same(8, keyEnumerable.Sum(key => key.Length), "KeyCollection as IEnumerable<string>");
			ICollection<E28> values = dictionary.Values;
			Same(4, values.Count, "ValueCollection as ICollection<E>");
			Expect(values.Contains(dictionary["k2"]), "ValueCollection.Contains");
			IEnumerable<Item> valueEnumerable = dictionary.Values;
			Same(10, valueEnumerable.Sum(item => item.Value), "ValueCollection as IEnumerable<Item>");
			IReadOnlyCollection<E28> readOnly = dictionary.Values;
			Same(4, readOnly.Count, "ValueCollection as IReadOnlyCollection<E>");
			var copy = new E28[4];
			values.CopyTo(copy, 0);
			Same(10, copy.Sum(item => item.Value), "ValueCollection.CopyTo");
			ICollection plain = dictionary.Values;
			Same(4, plain.Count, "ValueCollection as ICollection");
		}

		static void DictionaryThroughInterfaces()
		{
			var source = Make<E29>(4);
			IDictionary<E29, int> dictionary = new Dictionary<E29, int>();
			foreach (var item in source)
			{
				dictionary.Add(item, item.Value);
			}
			Same(4, dictionary.Count, "IDictionary<K, V>.Count");
			Same(3, dictionary[source[2]], "IDictionary<K, V> indexer");
			Expect(dictionary.ContainsKey(source[0]) && !dictionary.ContainsKey(new E29()), "IDictionary<K, V>.ContainsKey");
			int found;
			Expect(dictionary.TryGetValue(source[3], out found) && found == 4, "IDictionary<K, V>.TryGetValue");
			Same(4, dictionary.Keys.Count, "IDictionary<K, V>.Keys");
			Same(10, dictionary.Values.Sum(), "IDictionary<K, V>.Values");
			IReadOnlyDictionary<E29, int> readOnly = (Dictionary<E29, int>)dictionary;
			Same(10, readOnly.Keys.Sum(key => key.Value), "IReadOnlyDictionary<K, V>.Keys");
			Same(10, readOnly.Values.Sum(), "IReadOnlyDictionary<K, V>.Values");
			ICollection<KeyValuePair<E29, int>> pairs = dictionary;
			Expect(pairs.Contains(new KeyValuePair<E29, int>(source[1], 2)), "ICollection<KeyValuePair>.Contains");
			Expect(pairs.Remove(new KeyValuePair<E29, int>(source[1], 2)), "ICollection<KeyValuePair>.Remove");
			var total = 0;
			foreach (var pair in (IEnumerable<KeyValuePair<E29, int>>)dictionary)
			{
				total += pair.Value;
			}
			Same(8, total, "IEnumerable<KeyValuePair>");
			IDictionary plain = (IDictionary)dictionary;
			Expect(plain.Contains(source[0]), "IDictionary.Contains");
			Same(1, (int)plain[source[0]], "IDictionary indexer");
			IEqualityComparer<E29> comparer = EqualityComparer<E29>.Default;
			Expect(comparer.Equals(source[0], source[0]) && !comparer.Equals(source[0], source[1]), "the default comparer through IEqualityComparer<T>");
		}

		// ---- a slot that both a virtual call and an interface call read ----

		interface IShape
		{
			int Area();

			string Name();
		}

		abstract class Shape<T> : IShape
		{
			public abstract int Area();

			public virtual string Name()
			{
				return "shape of " + typeof(T).Name;
			}
		}

		class Square<T> : Shape<T>
		{
			public int Side;

			public override int Area()
			{
				return Side * Side;
			}
		}

		sealed class NamedSquare<T> : Square<T>
		{
			public override string Name()
			{
				return "square of " + typeof(T).Name;
			}
		}

		static void VirtualFirstThenInterface()
		{
			Shape<E30> shape = new Square<E30> { Side = 3 };
			Same(9, shape.Area(), "the abstract method through the class");
			Same("shape of E30", shape.Name(), "the virtual method through the class");
			IShape asInterface = shape;
			Same(9, asInterface.Area(), "the abstract method through the interface");
			Same("shape of E30", asInterface.Name(), "the virtual method through the interface");
			Shape<E30> named = new NamedSquare<E30> { Side = 2 };
			Same("square of E30", named.Name(), "the override through the class");
			Same("square of E30", ((IShape)named).Name(), "the override through the interface");
			Same(4, ((IShape)named).Area(), "the inherited override through the interface");
		}

		static void InterfaceFirstThenVirtual()
		{
			IShape asInterface = new NamedSquare<E31> { Side = 5 };
			Same(25, asInterface.Area(), "the abstract method through the interface");
			Same("square of E31", asInterface.Name(), "the override through the interface");
			var shape = (Shape<E31>)asInterface;
			Same(25, shape.Area(), "the abstract method through the class");
			Same("square of E31", shape.Name(), "the override through the class");
			Func<int> area = shape.Area;
			Func<string> name = asInterface.Name;
			Same(25, area(), "a delegate to the virtual method");
			Same("square of E31", name(), "a delegate to the method of the interface");
			Same("shape of E31", new Square<E31>().Name(), "the base method of another class of the same family");
		}

		interface INamed
		{
			string Name();

			string Kind();
		}

		class Plain<T> : INamed
		{
			public string Name()
			{
				return "plain";
			}

			public virtual string Kind()
			{
				return "kind of " + typeof(T).Name;
			}
		}

		// Implements the interface again: the slot its base uses for the virtual Kind stays, and the interface gets other methods.
		class Renamed<T> : Plain<T>, INamed
		{
			public new string Name()
			{
				return "renamed";
			}

			string INamed.Kind()
			{
				return "explicit";
			}
		}

		sealed class Twice<T> : Renamed<T>
		{
			public override string Kind()
			{
				return "twice";
			}
		}

		class Lender<T>
		{
			public int Area()
			{
				return 12;
			}

			public string Name()
			{
				return "lent to " + typeof(T).Name;
			}
		}

		// The methods of the interface are those of the base class, which knows nothing of the interface.
		sealed class Borrower<T> : Lender<T>, IShape
		{
		}

		static void Reimplemented()
		{
			Plain<E32> twice = new Twice<E32>();
			Same("twice", twice.Kind(), "the override through the class, before any interface call");
			Same("plain", twice.Name(), "the method of the base through the class");
			Same("explicit", ((INamed)twice).Kind(), "the explicit method through the interface");
			Same("renamed", ((INamed)twice).Name(), "the new method through the interface");

			INamed renamed = new Renamed<E33>();
			Same("renamed", renamed.Name(), "the interface first: the new method");
			Same("explicit", renamed.Kind(), "the interface first: the explicit method");
			Same("kind of E33", ((Plain<E33>)renamed).Kind(), "then the virtual method through the class");

			INamed plain = new Plain<E34>();
			Same("plain", plain.Name(), "the base class alone, through the interface");
			Same("kind of E34", plain.Kind(), "its virtual method through the interface");
			Same("kind of E34", ((Plain<E34>)plain).Kind(), "its virtual method through the class");

			IShape borrowed = new Borrower<E35>();
			Same(12, borrowed.Area(), "a method the base class lends to the interface");
			Same("lent to E35", borrowed.Name(), "another one");
			Same(12, ((Lender<E35>)borrowed).Area(), "the same method through the class");
		}

		abstract class Animal : IShape
		{
			public abstract int Area();

			public virtual string Name()
			{
				return "animal";
			}
		}

		sealed class Cat : Animal
		{
			public override int Area()
			{
				return 4;
			}

			public override string Name()
			{
				return "cat";
			}
		}

		static void NonGenericAbstract()
		{
			Animal animal = new Cat();
			IShape shape = animal;
			Same(4, animal.Area(), "through the class");
			Same(4, shape.Area(), "through the interface");
			Same("cat", animal.Name(), "the override through the class");
			Same("cat", shape.Name(), "the override through the interface");
			var shapes = new List<IShape> { animal, new Square<E32> { Side = 2 } };
			Same(8, shapes.Sum(item => item.Area()), "a list of the interface");
		}

		static void Comparers()
		{
			// Comparer<T> and EqualityComparer<T> are abstract classes whose virtual methods implement an interface.
			var list = MakeList<E33>(5);
			list.Reverse();
			list.Sort();
			Same(1, list[0].Value, "List<T>.Sort with the default comparer");
			var array = Make<E33>(5);
			Array.Reverse(array);
			Array.Sort(array);
			Same(5, array[4].Value, "Array.Sort with the default comparer");
			Comparer<E33> comparer = Comparer<E33>.Default;
			Expect(comparer.Compare(array[0], array[1]) < 0, "Comparer<T>.Compare through the class");
			IComparer<E33> asInterface = comparer;
			Expect(asInterface.Compare(array[1], array[0]) > 0, "Comparer<T>.Compare through IComparer<T>");
			IComparer plain = comparer;
			Same(0, plain.Compare(array[2], array[2]), "Comparer<T>.Compare through IComparer");
			var sorted = new SortedDictionary<E33, int>(asInterface);
			foreach (var item in array)
			{
				sorted.Add(item, item.Value);
			}
			Same(1, sorted.First().Value, "SortedDictionary with the comparer");
			var custom = Comparer<E33>.Create((first, second) => second.Value - first.Value);
			list.Sort(custom);
			Same(5, list[0].Value, "List<T>.Sort with a comparer made from a delegate");
			IEqualityComparer plainEquality = EqualityComparer<E33>.Default;
			Expect(plainEquality.Equals(array[0], array[0]), "EqualityComparer<T> through IEqualityComparer");
			Expect(EqualityComparer<E33>.Default.Equals(array[0], array[0]), "EqualityComparer<T> through the class");
		}

		// ---- a collection of this assembly ----

		sealed class Bag<T> : IList<T>, IReadOnlyList<T>
		{
			readonly List<T> _items = new List<T>();

			public int Count => _items.Count;

			public bool IsReadOnly => false;

			public T this[int index]
			{
				get { return _items[index]; }
				set { _items[index] = value; }
			}

			public void Add(T item)
			{
				_items.Add(item);
			}

			public void Clear()
			{
				_items.Clear();
			}

			public bool Contains(T item)
			{
				return _items.Contains(item);
			}

			public void CopyTo(T[] array, int arrayIndex)
			{
				_items.CopyTo(array, arrayIndex);
			}

			public int IndexOf(T item)
			{
				return _items.IndexOf(item);
			}

			public void Insert(int index, T item)
			{
				_items.Insert(index, item);
			}

			public bool Remove(T item)
			{
				return _items.Remove(item);
			}

			public void RemoveAt(int index)
			{
				_items.RemoveAt(index);
			}

			public IEnumerator<T> GetEnumerator()
			{
				foreach (var item in _items)
				{
					yield return item;
				}
			}

			IEnumerator IEnumerable.GetEnumerator()
			{
				return GetEnumerator();
			}
		}

		static void OwnCollection()
		{
			var bag = new Bag<E34>();
			// Through the class first: Count and the indexer each fill slots of two interfaces.
			bag.Add(new E34 { Value = 1 });
			Same(1, bag.Count, "Count through the class");
			IReadOnlyList<E34> readOnly = bag;
			Same(1, readOnly[0].Value, "the indexer through IReadOnlyList<T>");
			IList<E34> list = bag;
			list.Add(new E34 { Value = 2 });
			Same(2, list.Count, "Count through IList<T>");
			Same(2, readOnly.Count, "Count through IReadOnlyCollection<T>");
			Same(3, ((IEnumerable<Item>)bag).Sum(item => item.Value), "the iterator through a variant interface");
			var total = 0;
			foreach (object item in (IEnumerable)bag)
			{
				total += ((Item)item).Value;
			}
			Same(3, total, "the explicit IEnumerable");
			Same(2, new List<E34>(bag).Count, "List<T> made from the collection");
			Same(2, new ReadOnlyCollection<E34>(bag).Count, "ReadOnlyCollection<T> over the collection");
		}

		struct Pair<T> : IEquatable<Pair<T>>, IComparable<Pair<T>>, IItem
			where T : Item
		{
			public T First;
			public int Second;

			public int Value => First.Value + Second;

			public bool Equals(Pair<T> other)
			{
				return ReferenceEquals(First, other.First) && Second == other.Second;
			}

			public int CompareTo(Pair<T> other)
			{
				return Value.CompareTo(other.Value);
			}

			public override bool Equals(object other)
			{
				return other is Pair<T> && Equals((Pair<T>)other);
			}

			public override int GetHashCode()
			{
				return Second;
			}

			public override string ToString()
			{
				return "pair " + Value;
			}
		}

		static void BoxedGenericStruct()
		{
			var item = new E35 { Value = 3 };
			var pair = new Pair<E35> { First = item, Second = 4 };
			object boxed = pair;
			Same("pair 7", boxed.ToString(), "ToString of the box");
			Same(4, boxed.GetHashCode(), "GetHashCode of the box");
			var equatable = (IEquatable<Pair<E35>>)boxed;
			Expect(equatable.Equals(pair), "IEquatable<T> of the box");
			var comparable = (IComparable<Pair<E35>>)boxed;
			Expect(comparable.CompareTo(new Pair<E35> { First = item, Second = 9 }) < 0, "IComparable<T> of the box");
			Same(7, ((IItem)boxed).Value, "another interface of the box");
			var pairs = new List<Pair<E35>> { new Pair<E35> { First = item, Second = 9 }, pair };
			pairs.Sort();
			Same(7, pairs[0].Value, "List<T>.Sort of the struct");
			Same(1, pairs.IndexOf(new Pair<E35> { First = item, Second = 9 }), "List<T>.IndexOf of the struct");
			Expect(new Dictionary<Pair<E35>, int> { { pair, 1 } }.ContainsKey(pair), "the struct as a key");
		}

		interface IGreeter
		{
			string Subject { get; }

			string Greet()
			{
				return "hello " + Subject;
			}
		}

		sealed class Greeter<T> : IGreeter
		{
			public string Subject => typeof(T).Name;
		}

		sealed class LoudGreeter<T> : IGreeter
		{
			public string Subject => typeof(T).Name;

			public string Greet()
			{
				return "HELLO " + Subject;
			}
		}

		static void DefaultInterfaceMethod()
		{
			IGreeter greeter = new Greeter<E36>();
			Same("hello E36", greeter.Greet(), "the method of the interface itself");
			IGreeter loud = new LoudGreeter<E36>();
			Same("HELLO E36", loud.Greet(), "the method of the class");
			Same("E36", greeter.Subject, "the property the class implements");
		}

		static int CountThrough<TList, T>(TList list)
			where TList : IList<T>
		{
			var count = list.Count;
			foreach (var item in list)
			{
				count += item == null ? 0 : 1;
			}
			return count;
		}

		static int SumOf<T>(IEnumerable<T> items)
			where T : IItem
		{
			var total = 0;
			foreach (var item in items)
			{
				total += item.Value;
			}
			return total;
		}

		struct Three<T> : IList<T>
		{
			public T A;
			public T B;
			public T C;

			public int Count => 3;

			public bool IsReadOnly => true;

			public T this[int index]
			{
				get { return index == 0 ? A : index == 1 ? B : C; }
				set { throw new NotSupportedException(); }
			}

			public void Add(T item)
			{
				throw new NotSupportedException();
			}

			public void Clear()
			{
				throw new NotSupportedException();
			}

			public bool Contains(T item)
			{
				return IndexOf(item) >= 0;
			}

			public void CopyTo(T[] array, int arrayIndex)
			{
				array[arrayIndex] = A;
				array[arrayIndex + 1] = B;
				array[arrayIndex + 2] = C;
			}

			public int IndexOf(T item)
			{
				var comparer = EqualityComparer<T>.Default;
				return comparer.Equals(A, item) ? 0 : comparer.Equals(B, item) ? 1 : comparer.Equals(C, item) ? 2 : -1;
			}

			public void Insert(int index, T item)
			{
				throw new NotSupportedException();
			}

			public bool Remove(T item)
			{
				throw new NotSupportedException();
			}

			public void RemoveAt(int index)
			{
				throw new NotSupportedException();
			}

			public IEnumerator<T> GetEnumerator()
			{
				yield return A;
				yield return B;
				yield return C;
			}

			IEnumerator IEnumerable.GetEnumerator()
			{
				return GetEnumerator();
			}
		}

		static void ConstrainedCalls()
		{
			var list = MakeList<E01>(4);
			Same(8, CountThrough<List<E01>, E01>(list), "a constrained call on List<E>");
			Same(8, CountThrough<IList<E01>, E01>(list), "the same through the interface");
			Same(8, CountThrough<E02[], E02>(Make<E02>(4)), "a constrained call on an array");
			var three = new Three<V03> { A = new V03 { Number = 1 }, B = new V03 { Number = 2 }, C = new V03 { Number = 3 } };
			Same(6, CountThrough<Three<V03>, V03>(three), "a constrained call on a struct");
			Same(6, SumOf(three), "the struct boxed as IEnumerable<T>");
			Same(6, SumOf((IEnumerable<V03>)new[] { three.A, three.B, three.C }), "an array of structs as IEnumerable<T>");
			Same(1, ((IList<V03>)three).IndexOf(three.B), "the struct boxed as IList<T>");
			Same(10, SumOf(list), "List<E> as IEnumerable<T> of a generic method");
		}

		sealed class Team : List<E03>
		{
		}

		class Roster<T> : List<T>
		{
		}

		sealed class Squad : Roster<E04>
		{
		}

		static void DerivedFromGeneric()
		{
			var team = new Team { new E03 { Value = 1 }, new E03 { Value = 2 } };
			Same(2, ((ICollection<E03>)team).Count, "a class derived from List<E>, through ICollection<E>");
			Same(3, ((IEnumerable<Item>)team).Sum(item => item.Value), "the same through a variant interface");
			var roster = new Roster<E05> { new E05 { Value = 5 } };
			Same(1, ((IReadOnlyList<E05>)roster).Count, "a generic class derived from List<T>");
			Same(5, ((IList<E05>)roster)[0].Value, "the same through IList<T>");
			Same(0, ((IList)roster).IndexOf(roster[0]), "the same through IList");
			var squad = new Squad { new E04 { Value = 7 } };
			Same(7, ((IEnumerable<IItem>)squad).First().Value, "a class derived from a generic class derived from List<T>");
			Collection<E06> collection = new ObservableBag<E06>();
			collection.Add(new E06 { Value = 2 });
			Same(1, ((ICollection<E06>)collection).Count, "Collection<T> through ICollection<T>");
			Same(1, ((ObservableBag<E06>)collection).Inserted, "the virtual method Collection<T> calls on itself");
			((IList<E06>)collection).Insert(0, new E06 { Value = 3 });
			Same(2, ((ObservableBag<E06>)collection).Inserted, "the same, reached through IList<T>");
		}

		sealed class ObservableBag<T> : Collection<T>
		{
			public int Inserted;

			protected override void InsertItem(int index, T item)
			{
				Inserted++;
				base.InsertItem(index, item);
			}
		}

		// ---- many threads ----

		sealed class Layer<T>
			where T : class
		{
			public T Inner;
			public int Depth;
		}

		static volatile bool s_go;

		/// <summary>
		/// For each of a row of types nobody has used, several threads make their first call through the same
		/// interfaces at once. The types are made by the recursion, so each depth has a List, an array and a Dictionary
		/// whose interfaces are untouched.
		/// </summary>
		static void FirstCallFromManyThreads()
		{
			var wrong = Race(new E07(), 24);
			Same(0, wrong, "threads that saw something else");
		}

		static int Race<T>(T seed, int depth)
			where T : class
		{
			const int Threads = 6;
			var array = new T[5];
			var list = new List<T>();
			var dictionary = new Dictionary<T, int>();
			var layers = new List<Layer<T>>();
			for (var index = 0; index < array.Length; index++)
			{
				array[index] = seed;
				list.Add(seed);
				layers.Add(new Layer<T> { Inner = seed, Depth = index });
			}
			dictionary[seed] = 11;
			var results = new int[Threads];
			var errors = new Exception[Threads];
			var threads = new Thread[Threads];
			var ready = 0;
			s_go = false;
			for (var at = 0; at < Threads; at++)
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
						// At an even depth every thread starts from the same interface, so its first call comes from all of
						// them at once; at an odd depth each starts from another, so different interfaces are filled at once.
						results[mine] = Touch(array, list, dictionary, layers, depth % 2 == 0 ? depth / 2 : mine);
					}
					catch (Exception exception)
					{
						errors[mine] = exception;
					}
				});
				threads[at].Start();
			}
			while (Volatile.Read(ref ready) < Threads)
			{
				Thread.Yield();
			}
			s_go = true;
			foreach (var thread in threads)
			{
				thread.Join();
			}
			var expected = Touch(array, list, dictionary, layers, 0);
			var wrong = 0;
			for (var at = 0; at < Threads; at++)
			{
				if (errors[at] != null)
				{
					throw new CheckFailed("depth " + depth + ", thread " + at + ": " + errors[at].GetType().Name + ": " + errors[at].Message);
				}
				if (results[at] != expected)
				{
					wrong++;
				}
			}
			if (depth > 0)
			{
				wrong += Race(new Layer<T> { Inner = seed, Depth = depth }, depth - 1);
			}
			return wrong;
		}

		static int Touch<T>(T[] array, List<T> list, Dictionary<T, int> dictionary, List<Layer<T>> layers, int order)
			where T : class
		{
			var total = 0;
			for (var step = 0; step < 8; step++)
			{
				switch ((step + order) % 8)
				{
					case 6:
						// The interfaces that are not generic wait as well.
						total += ((IList)list).Count + ((IList)list).IndexOf(array[0]);
						foreach (object item in (IEnumerable)list)
						{
							total += item == null ? 0 : 1;
						}
						break;
					case 7:
						total += ((ICollection)dictionary).Count + (((IDictionary)dictionary).Contains(array[0]) ? 1 : 0);
						foreach (object item in (IEnumerable)layers)
						{
							total += ((Layer<T>)item).Depth;
						}
						break;
					case 0:
						total += ((IList<T>)array).Count + ((IList<T>)array).IndexOf(array[0]);
						break;
					case 1:
						total += ((IReadOnlyList<T>)list).Count + (((IReadOnlyList<T>)list)[1] == null ? 0 : 1);
						break;
					case 2:
						foreach (var item in (IEnumerable<object>)array)
						{
							total += item == null ? 0 : 1;
						}
						break;
					case 3:
						foreach (var item in (IEnumerable<T>)list)
						{
							total += item == null ? 0 : 1;
						}
						total += ((ICollection<T>)list).Contains(array[0]) ? 1 : 0;
						break;
					case 4:
						total += ((IDictionary<T, int>)dictionary)[array[0]] + ((IReadOnlyDictionary<T, int>)dictionary).Count;
						total += ((ICollection<T>)dictionary.Keys).Count;
						break;
					default:
						total += ((IReadOnlyCollection<Layer<T>>)layers).Count;
						foreach (var layer in (IEnumerable<object>)layers)
						{
							total += ((Layer<T>)layer).Depth;
						}
						break;
				}
			}
			return total;
		}
	}
}
