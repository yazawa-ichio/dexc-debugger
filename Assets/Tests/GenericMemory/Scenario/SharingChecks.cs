using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace GenericMemory
{
	/// <summary>
	/// What the code around a collection can see of it, for the rewrites that run the code of a collection for object
	/// behind the back of that code: the enumerator of a foreach, the methods that cannot tell the element type, and the
	/// calls of Enumerable. Each check says what has to stay as the C# says it: the results, the exceptions, the types
	/// of the objects that come out, and that an object that leaves the method is of the type its code names.
	/// The checks are written in the shapes the rewrites take, and in shapes they have to leave alone.
	/// </summary>
	public static class SharingChecks
	{
		const int Rounds = 2;

		sealed class CheckFailed : Exception
		{
			public CheckFailed(string message)
				: base(message)
			{
			}
		}

		public sealed class Item
		{
			public int Id;
			public string Name;
			public Other Other;

			public Item(int id)
			{
				Id = id;
				Name = "n" + (id % 3);
				Other = new Other { Value = id * 10 };
			}
		}

		public sealed class Other
		{
			public int Value;
		}

		/// <summary>Brings its own comparison: nothing may compare it as an object.</summary>
		public sealed class Keyed : IEquatable<Keyed>
		{
			public int Key;
			public int Serial;

			public bool Equals(Keyed other)
			{
				return other != null && other.Key == Key;
			}

			public override bool Equals(object other)
			{
				// Equal to nothing as an object: a comparison that went through object would show.
				return false;
			}

			public override int GetHashCode()
			{
				return Key;
			}
		}

		/// <summary>Compares by value through Object.Equals, with no interface: the comparer of object does the same.</summary>
		public sealed class ByValue
		{
			public int Number;

			public override bool Equals(object other)
			{
				return other is ByValue value && value.Number == Number;
			}

			public override int GetHashCode()
			{
				return Number;
			}
		}

		public sealed class ItemList : List<Item>
		{
		}

		static readonly KeyValuePair<string, Action>[] s_checks =
		{
			Check("ListForeach", ListForeach),
			Check("ListForeachChanged", ListForeachChanged),
			Check("ListForeachNull", ListForeachNull),
			Check("ListMembers", ListMembers),
			Check("ListMemberErrors", ListMemberErrors),
			Check("ListStaysTyped", ListStaysTyped),
			Check("EnumeratorThatLeaves", EnumeratorThatLeaves),
			Check("DerivedList", DerivedList),
			Check("DictionaryForeach", DictionaryForeach),
			Check("DictionaryOfReferences", DictionaryOfReferences),
			Check("DictionaryStaysTyped", DictionaryStaysTyped),
			Check("HashSetForeach", HashSetForeach),
			Check("OtherCollections", OtherCollections),
			Check("OwnComparison", OwnComparison),
			Check("LinqWhereToList", LinqWhereToList),
			Check("LinqSelect", LinqSelect),
			Check("LinqOrder", LinqOrder),
			Check("LinqElements", LinqElements),
			Check("LinqScalars", LinqScalars),
			Check("LinqOverArray", LinqOverArray),
			Check("LinqNulls", LinqNulls),
			Check("LinqThatLeaves", LinqThatLeaves),
			Check("LinqDeferred", LinqDeferred),
			Check("LinqClosures", LinqClosures),
			Check("LinqDelegateStaysTyped", LinqDelegateStaysTyped),
			Check("LinqOwnComparison", LinqOwnComparison),
			Check("LinqOverInterface", LinqOverInterface),
			Check("LinqSkipTakeConcat", LinqSkipTakeConcat),
			Check("LinqInLoop", LinqInLoop),
		};

		public static int Failed { get; private set; }

		static KeyValuePair<string, Action> Check(string name, Action body)
		{
			return new KeyValuePair<string, Action>(name, body);
		}

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
						detail = exception.GetType().Name + ": " + exception.Message.Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ');
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

		static void Throws<T>(Action body, string what)
			where T : Exception
		{
			try
			{
				body();
			}
			catch (T)
			{
				return;
			}
			catch (Exception other)
			{
				throw new CheckFailed(what + ": expected " + typeof(T).Name + ", got " + other.GetType().Name);
			}
			throw new CheckFailed(what + ": expected " + typeof(T).Name + ", nothing was thrown");
		}

		static List<Item> Items(int count)
		{
			var list = new List<Item>();
			for (var at = 1; at <= count; at++)
			{
				list.Add(new Item(at));
			}
			return list;
		}

		static void ListForeach()
		{
			var list = Items(5);
			var sum = 0;
			var index = 0;
			foreach (var item in list)
			{
				Expect(ReferenceEquals(item, list[index]), "foreach gives the items of the list");
				sum += item.Id;
				index++;
			}
			Same(15, sum, "sum of a foreach");
			// Two loops in one method: the compiler may give them one local.
			foreach (var item in list)
			{
				sum += item.Other.Value;
			}
			Same(165, sum, "sum of the second foreach");
			var empty = new List<Item>();
			foreach (var item in empty)
			{
				sum += item.Id;
			}
			Same(165, sum, "an empty list");
			var withNull = new List<Item> { null, new Item(2) };
			var nulls = 0;
			foreach (var item in withNull)
			{
				nulls += item == null ? 1 : 0;
			}
			Same(1, nulls, "a null in the list");
		}

		static void ListForeachChanged()
		{
			var list = Items(3);
			Throws<InvalidOperationException>(() =>
			{
				foreach (var item in list)
				{
					if (item.Id == 2)
					{
						list.Add(new Item(9));
					}
				}
			}, "a list changed inside its foreach");
		}

		static void ListForeachNull()
		{
			List<Item> list = null;
			Throws<NullReferenceException>(() =>
			{
				foreach (var item in list)
				{
					Sink.Add(item.Id);
				}
			}, "foreach over null");
			Throws<NullReferenceException>(() => Sink.Add(list.Count), "Count of null");
		}

		static void ListMembers()
		{
			var list = Items(4);
			Same(4, list.Count, "Count");
			Same(3, list[2].Id, "the indexer");
			var replaced = new Item(30);
			list[2] = replaced;
			Expect(ReferenceEquals(list[2], replaced), "the indexer sets");
			Expect(list.Contains(replaced), "Contains");
			Same(2, list.IndexOf(replaced), "IndexOf");
			Expect(!list.Contains(new Item(30)), "Contains compares references of a class without Equals");
			Expect(list.Remove(replaced), "Remove");
			Same(3, list.Count, "Count after Remove");
			list.RemoveAt(0);
			Same(2, list[0].Id, "RemoveAt");
			list.Insert(0, replaced);
			Same(30, list[0].Id, "Insert");
			list.Reverse();
			Same(30, list[list.Count - 1].Id, "Reverse");
			var copy = new Item[list.Count];
			list.CopyTo(copy);
			Expect(ReferenceEquals(copy[0], list[0]), "CopyTo");
			list.Clear();
			Same(0, list.Count, "Clear");
			Expect(list.Capacity >= 4, "Capacity stays after Clear");
		}

		static void ListMemberErrors()
		{
			var list = Items(2);
			Throws<ArgumentOutOfRangeException>(() => Sink.Add(list[2].Id), "the indexer out of range");
			Throws<ArgumentOutOfRangeException>(() => list[-1] = null, "the indexer set out of range");
			Throws<ArgumentOutOfRangeException>(() => list.RemoveAt(5), "RemoveAt out of range");
		}

		static void ListStaysTyped()
		{
			var list = Items(3);
			// Enough work on the list that its array was replaced a few times.
			for (var at = 0; at < 40; at++)
			{
				list.Add(new Item(100 + at));
			}
			var sum = 0;
			foreach (var item in list)
			{
				sum += item.Id;
			}
			list.RemoveAt(1);
			list.Insert(1, new Item(7));
			Sink.Add(sum + list.Count + list[0].Id);
			object boxed = list;
			Expect(boxed.GetType() == typeof(List<Item>), "the list is a List<Item>");
			Expect(boxed is IEnumerable<Item> && boxed is IList<Item> && boxed is IReadOnlyList<Item>, "the list has the interfaces of Item");
			Expect(!(boxed is List<object>) && !(boxed is IList<object>), "the list is not a list of object");
			Expect(list.ToArray().GetType() == typeof(Item[]), "ToArray makes an Item[]");
			var field = typeof(List<Item>).GetField("_items", BindingFlags.Instance | BindingFlags.NonPublic);
			if (field != null)
			{
				Expect(field.GetValue(list).GetType() == typeof(Item[]), "the array inside the list is an Item[]");
			}
		}

		static List<Item>.Enumerator Leaves(List<Item> list)
		{
			return list.GetEnumerator();
		}

		static void EnumeratorThatLeaves()
		{
			var list = Items(3);
			// An enumerator that is boxed, returned or handed on is an object or a value of the type the code names.
			object boxed = list.GetEnumerator();
			Expect(boxed.GetType() == typeof(List<Item>.Enumerator), "a boxed enumerator");
			Expect(boxed is IEnumerator<Item>, "a boxed enumerator has the interface of Item");
			object returned = Leaves(list);
			Expect(returned.GetType() == typeof(List<Item>.Enumerator), "a returned enumerator");
			IEnumerable<Item> sequence = list;
			using (var walker = sequence.GetEnumerator())
			{
				Expect(walker.GetType() == typeof(List<Item>.Enumerator), "the enumerator of the interface");
				Expect(walker.MoveNext() && walker.Current.Id == 1, "the enumerator of the interface walks");
			}
			var manual = list.GetEnumerator();
			var sum = 0;
			while (manual.MoveNext())
			{
				sum += manual.Current.Id;
			}
			manual.Dispose();
			Same(6, sum, "an enumerator used by hand");
		}

		static void DerivedList()
		{
			var list = new ItemList { new Item(1), new Item(2) };
			var sum = 0;
			foreach (var item in list)
			{
				sum += item.Id;
			}
			Same(3, sum + list.Count - 2, "a class derived from List<Item>");
			Expect(list.GetType() == typeof(ItemList), "the derived list keeps its class");
			Same(2, list.Where(item => item.Id > 0).ToList().Count, "Enumerable over the derived list");
		}

		static void DictionaryForeach()
		{
			var map = new Dictionary<int, Item>();
			for (var at = 1; at <= 4; at++)
			{
				map[at] = new Item(at * 2);
			}
			var keys = 0;
			var values = 0;
			foreach (var pair in map)
			{
				keys += pair.Key;
				values += pair.Value.Id;
				Expect(ReferenceEquals(pair.Value, map[pair.Key]), "the pair holds the value of the key");
			}
			Same(10, keys, "keys of the pairs");
			Same(20, values, "values of the pairs");
			values = 0;
			foreach (var item in map.Values)
			{
				values += item.Id;
			}
			Same(20, values, "Values");
			keys = 0;
			foreach (var key in map.Keys)
			{
				keys += key;
			}
			Same(10, keys, "Keys");
			// A pair that leaves the loop is a KeyValuePair of Item.
			object boxed = null;
			foreach (var pair in map)
			{
				boxed = pair;
			}
			Expect(boxed.GetType() == typeof(KeyValuePair<int, Item>), "a boxed pair");
			Throws<InvalidOperationException>(() =>
			{
				foreach (var pair in map)
				{
					map[pair.Key + 100] = pair.Value;
				}
			}, "a dictionary changed inside its foreach");
			Expect(map.ContainsKey(2) && !map.ContainsKey(50), "ContainsKey");
			Expect(map.TryGetValue(3, out var found) && found.Id == 6, "TryGetValue");
			Expect(map.Remove(3) && !map.Remove(3), "Remove");
			Throws<KeyNotFoundException>(() => Sink.Add(map[77].Id), "a key that is not there");
		}

		static void DictionaryOfReferences()
		{
			var first = new Item(1);
			var second = new Item(2);
			var map = new Dictionary<Item, Other> { { first, first.Other }, { second, second.Other } };
			var sum = 0;
			foreach (var pair in map)
			{
				sum += pair.Key.Id + pair.Value.Value;
			}
			Same(33, sum, "a dictionary of two classes");
			Expect(ReferenceEquals(map[second], second.Other), "the indexer with a class as the key");
			Expect(!map.ContainsKey(new Item(1)), "a key is compared as a reference");
			var byValue = new Dictionary<ByValue, Item> { { new ByValue { Number = 5 }, first } };
			Expect(byValue.ContainsKey(new ByValue { Number = 5 }), "a key with Equals is compared by it");
			var keyed = new Dictionary<Keyed, Item> { { new Keyed { Key = 5 }, first } };
			Expect(keyed.ContainsKey(new Keyed { Key = 5 }), "a key with IEquatable is compared by it");
			foreach (var pair in keyed)
			{
				sum += pair.Key.Key;
			}
			Same(38, sum, "a dictionary with an IEquatable key");
		}

		static void DictionaryStaysTyped()
		{
			var map = new Dictionary<int, Item> { { 1, new Item(1) } };
			var sum = 0;
			foreach (var item in map.Values)
			{
				sum += item.Id;
			}
			foreach (var pair in map)
			{
				sum += pair.Key;
			}
			Sink.Add(sum);
			object values = map.Values;
			Expect(values.GetType() == typeof(Dictionary<int, Item>.ValueCollection), "Values is the collection of Item");
			Expect(values is ICollection<Item> && values is IEnumerable<Item>, "Values has the interfaces of Item");
			object boxed = map;
			Expect(boxed.GetType() == typeof(Dictionary<int, Item>) && boxed is IDictionary<int, Item>, "the dictionary is a dictionary of Item");
			object walker = map.GetEnumerator();
			Expect(walker.GetType() == typeof(Dictionary<int, Item>.Enumerator), "a boxed enumerator of the dictionary");
		}

		static void HashSetForeach()
		{
			var first = new Item(1);
			var set = new HashSet<Item> { first, new Item(2), new Item(3) };
			var sum = 0;
			foreach (var item in set)
			{
				sum += item.Id;
			}
			Same(6, sum, "sum of a set");
			Expect(set.Contains(first) && !set.Contains(new Item(1)), "a set of a class without Equals compares references");
			Expect(!set.Add(first) && set.Remove(first) && set.Count == 2, "Add and Remove");
			var byValue = new HashSet<ByValue> { new ByValue { Number = 1 } };
			Expect(byValue.Contains(new ByValue { Number = 1 }) && !byValue.Add(new ByValue { Number = 1 }), "a set of a class with Equals compares by it");
			var keyed = new HashSet<Keyed> { new Keyed { Key = 1 } };
			Expect(keyed.Contains(new Keyed { Key = 1 }), "a set of a class with IEquatable compares by it");
			object boxed = set;
			Expect(boxed.GetType() == typeof(HashSet<Item>) && boxed is ISet<Item>, "the set is a set of Item");
			Expect(set.Comparer is IEqualityComparer<Item>, "the comparer of the set is one of Item");
		}

		static void OtherCollections()
		{
			var queue = new Queue<Item>();
			var stack = new Stack<Item>();
			var linked = new LinkedList<Item>();
			for (var at = 1; at <= 3; at++)
			{
				queue.Enqueue(new Item(at));
				stack.Push(new Item(at));
				linked.AddLast(new Item(at));
			}
			var sum = 0;
			foreach (var item in queue)
			{
				sum += item.Id;
			}
			foreach (var item in stack)
			{
				sum = sum * 10 + item.Id;
			}
			foreach (var item in linked)
			{
				sum += item.Id;
			}
			Same(6327, sum, "a queue, a stack and a linked list");
			Same(1, queue.Peek().Id + queue.Count - 3, "Peek");
			Same(3, stack.Pop().Id, "Pop");
			Same(1, queue.Dequeue().Id, "Dequeue");
			object boxed = queue;
			Expect(boxed.GetType() == typeof(Queue<Item>), "the queue is a queue of Item");
			Expect(queue.ToArray().GetType() == typeof(Item[]) && stack.ToArray().GetType() == typeof(Item[]), "their arrays are arrays of Item");
		}

		static void OwnComparison()
		{
			var first = new Keyed { Key = 1, Serial = 1 };
			var list = new List<Keyed> { first, new Keyed { Key = 2, Serial = 2 } };
			Expect(list.Contains(new Keyed { Key = 2 }), "Contains of a class with IEquatable goes through it");
			Same(0, list.IndexOf(new Keyed { Key = 1 }), "IndexOf of a class with IEquatable");
			Same(1, Array.IndexOf(list.ToArray(), new Keyed { Key = 2 }), "Array.IndexOf of a class with IEquatable");
			var values = new List<ByValue> { new ByValue { Number = 4 } };
			Expect(values.Contains(new ByValue { Number = 4 }), "Contains of a class with Equals goes through it");
			var sum = 0;
			foreach (var item in list)
			{
				sum += item.Serial;
			}
			Same(3, sum, "foreach over a list of a class with IEquatable");
		}

		static void LinqWhereToList()
		{
			var list = Items(6);
			var some = list.Where(item => item.Id % 2 == 0).ToList();
			Expect(some.GetType() == typeof(List<Item>), "ToList makes a List<Item>");
			Same(3, some.Count, "Where");
			Expect(ReferenceEquals(some[0], list[1]) && ReferenceEquals(some[2], list[5]), "Where keeps the items and their order");
			some.Add(new Item(50));
			Same(4, some.Count, "the list ToList made takes more items");
			var array = list.Where(item => item.Id > 4).ToArray();
			Expect(array.GetType() == typeof(Item[]), "ToArray makes an Item[]");
			Same(2, array.Length, "Where and ToArray");
			var none = list.Where(item => item.Id > 100).ToList();
			Expect(none.GetType() == typeof(List<Item>) && none.Count == 0, "an empty result");
			var noneArray = list.Where(item => item.Id > 100).ToArray();
			Expect(noneArray.GetType() == typeof(Item[]) && noneArray.Length == 0, "an empty array");
			var twice = list.Where(item => item.Id > 1).Where(item => item.Id < 5).ToList();
			Same(3, twice.Count, "Where twice");
		}

		static void LinqSelect()
		{
			var list = Items(4);
			var ids = list.Select(item => item.Id).ToArray();
			Same(10, ids.Sum(), "Select to int");
			var names = list.Where(item => item.Id > 1).Select(item => item.Name).Distinct().Count();
			Same(3, names, "Where, Select, Distinct, Count");
			var others = list.Select(item => item.Other).ToList();
			Expect(others.GetType() == typeof(List<Other>), "Select to a class and ToList makes a List of it");
			Same(40, others[3].Value, "Select to a class");
			Expect(ReferenceEquals(others[0], list[0].Other), "Select hands on what the lambda returned");
			var indexed = list.Select((item, index) => item.Id * index).Sum();
			Same(20, indexed, "Select with an index");
			var strings = list.Select(item => item.Name);
			Expect(strings is IEnumerable<string>, "a Select that leaves is a sequence of what it selects");
			Same(4, strings.Count(), "a Select that leaves");
		}

		static void LinqOrder()
		{
			var list = new List<Item> { new Item(3), new Item(1), new Item(4), new Item(2) };
			var ordered = list.OrderBy(item => item.Id).ToList();
			Expect(ordered.GetType() == typeof(List<Item>), "OrderBy and ToList make a List<Item>");
			Same(1234, ordered[0].Id * 1000 + ordered[1].Id * 100 + ordered[2].Id * 10 + ordered[3].Id, "OrderBy");
			var byName = list.OrderBy(item => item.Name).ThenByDescending(item => item.Id).ToArray();
			Expect(byName.GetType() == typeof(Item[]), "ThenBy and ToArray make an Item[]");
			Same(3, byName[0].Id, "OrderBy a string, ThenByDescending");
			var last = list.OrderByDescending(item => item.Id).First();
			Same(4, last.Id, "OrderByDescending, First");
			var reversed = list.Where(item => item.Id > 0).Reverse().ToList();
			Same(2, reversed[0].Id, "Reverse");
		}

		static void LinqElements()
		{
			var list = Items(5);
			var found = list.FirstOrDefault(item => item.Id == 3);
			Expect(ReferenceEquals(found, list[2]), "FirstOrDefault gives the item");
			Expect(list.FirstOrDefault(item => item.Id == 30) == null, "FirstOrDefault without a match");
			Expect(ReferenceEquals(list.First(item => item.Id > 3), list[3]), "First with a predicate");
			Expect(ReferenceEquals(list.Last(item => item.Id < 3), list[1]), "Last with a predicate");
			Expect(ReferenceEquals(list.Single(item => item.Id == 5), list[4]), "Single with a predicate");
			Expect(ReferenceEquals(list.First(), list[0]) && ReferenceEquals(list.Last(), list[4]), "First and Last of the list itself");
			Expect(ReferenceEquals(list.ElementAt(2), list[2]), "ElementAt");
			Throws<InvalidOperationException>(() => list.First(item => item.Id > 50), "First without a match");
			Throws<InvalidOperationException>(() => list.Single(item => item.Id > 1), "Single with more than one");
			Throws<InvalidOperationException>(() => new List<Item>().Where(item => item.Id > 0).First(), "First of nothing");
			Expect(ReferenceEquals(list.Where(item => item.Id > 1).First(), list[1]), "Where, First");
			Expect(ReferenceEquals(list.Where(item => item.Id > 1).LastOrDefault(), list[4]), "Where, LastOrDefault");
			Expect(ReferenceEquals(list.Aggregate((best, item) => item.Id > best.Id ? item : best), list[4]), "Aggregate");
		}

		static void LinqScalars()
		{
			var list = Items(4);
			Expect(list.Any(item => item.Id == 4) && !list.Any(item => item.Id == 9), "Any with a predicate");
			Expect(list.All(item => item.Id > 0) && !list.All(item => item.Id > 1), "All");
			Same(2, list.Count(item => item.Id > 2), "Count with a predicate");
			Same(4, list.Count(), "Count of the list itself");
			Same(10, list.Sum(item => item.Id), "Sum");
			Same(4, list.Max(item => item.Id), "Max");
			Same(1, list.Min(item => item.Id), "Min");
			Same(25, (long)(list.Average(item => item.Id) * 10), "Average");
			Same(100, list.Sum(item => (long)item.Other.Value), "Sum of long");
			Expect(list.Where(item => item.Id > 3).Any() && !list.Where(item => item.Id > 4).Any(), "Where, Any");
			Expect(list.Contains(list[1]) && !list.Contains(new Item(2)), "Contains of Enumerable or of the list");
			Throws<InvalidOperationException>(() => new List<Item>().Max(item => item.Id), "Max of nothing");
		}

		static void LinqOverArray()
		{
			var array = Items(5).ToArray();
			var some = array.Where(item => item.Id > 2).ToArray();
			Expect(some.GetType() == typeof(Item[]) && some.Length == 3, "Where over an array makes an Item[]");
			var list = array.Where(item => item.Id < 3).ToList();
			Expect(list.GetType() == typeof(List<Item>) && list.Count == 2, "Where over an array makes a List<Item>");
			Same(15, array.Sum(item => item.Id), "Sum over an array");
			Expect(ReferenceEquals(array.FirstOrDefault(item => item.Id == 4), array[3]), "FirstOrDefault over an array");
			Same(5, array.Select(item => item.Id).Max(), "Select over an array");
			Expect(ReferenceEquals(array.OrderByDescending(item => item.Id).First(), array[4]), "OrderByDescending over an array");
			Same(5, array.ToList().Count, "ToList of the array itself");
		}

		static void LinqNulls()
		{
			List<Item> list = null;
			Func<Item, bool> none = null;
			Func<Item, int> noSelector = null;
			var real = Items(2);
			Throws<ArgumentNullException>(() => list.Where(item => item.Id > 0).ToList(), "Where over null");
			Throws<ArgumentNullException>(() => list.Select(item => item.Id).ToArray(), "Select over null");
			Throws<ArgumentNullException>(() => list.FirstOrDefault(item => item.Id > 0), "FirstOrDefault over null");
			Throws<ArgumentNullException>(() => list.Any(item => item.Id > 0), "Any over null");
			Throws<ArgumentNullException>(() => Sink.Add(list.Sum(item => item.Id)), "Sum over null");
			Throws<ArgumentNullException>(() => list.OrderBy(item => item.Id).ToList(), "OrderBy over null");
			Throws<ArgumentNullException>(() => real.Where(none).ToList(), "Where with no predicate");
			Throws<ArgumentNullException>(() => real.Select(noSelector).ToArray(), "Select with no selector");
			Throws<ArgumentNullException>(() => real.Any(none), "Any with no predicate");
			Throws<ArgumentNullException>(() => Sink.Add(real.Sum(noSelector)), "Sum with no selector");
			var withNull = new List<Item> { null, new Item(1) };
			Same(1, withNull.Where(item => item != null).ToList().Count, "a null among the items");
			Expect(withNull.FirstOrDefault(item => item == null) == null && withNull.Any(item => item == null), "a null that is found");
		}

		static IEnumerable<Item> Even(List<Item> list)
		{
			return list.Where(item => item.Id % 2 == 0);
		}

		static void LinqThatLeaves()
		{
			var list = Items(4);
			// A sequence of Item that is stored, returned or enumerated is an IEnumerable<Item>.
			var stored = list.Where(item => item.Id > 2);
			Expect(stored is IEnumerable<Item>, "a stored Where is a sequence of Item");
			object returned = Even(list);
			Expect(returned is IEnumerable<Item>, "a returned Where is a sequence of Item");
			var sum = 0;
			foreach (var item in list.Where(item => item.Id > 1))
			{
				sum += item.Id;
			}
			foreach (var item in stored)
			{
				sum += item.Id;
			}
			foreach (var item in Even(list).Skip(1))
			{
				sum += item.Id;
			}
			Same(20, sum, "sequences that were enumerated");
			IOrderedEnumerable<Item> ordered = list.OrderByDescending(item => item.Id);
			Same(4, ordered.First().Id, "a stored OrderBy");
			Same(2, stored.ToList().Count, "ToList of a stored sequence");
		}

		static void LinqDeferred()
		{
			var list = Items(3);
			var later = list.Where(item => item.Id > 1);
			list.Add(new Item(4));
			Same(3, later.Count(), "a stored Where sees what was added later");
			var counted = 0;
			var some = list.Where(item =>
			{
				counted++;
				return item.Id > 2;
			}).ToList();
			Same(4, counted, "the predicate ran once for each item");
			Same(2, some.Count, "what the counting predicate kept");
			counted = 0;
			var first = list.Select(item =>
			{
				counted++;
				return item;
			}).FirstOrDefault(item => item.Id == 2);
			Same(2, counted, "FirstOrDefault stops at the match");
			Same(2, first.Id, "what it stopped at");
			Expect(ChangeThrows(false) == ChangeThrows(true), "a list changed by the predicate that walks it does what it does behind its interface");
		}

		/// <summary>Whether Where over a list that its own predicate changes throws: over the list, or over the list seen as IEnumerable.</summary>
		static bool ChangeThrows(bool behindInterface)
		{
			var list = Items(3);
			Func<Item, bool> grows = item =>
			{
				if (list.Count < 6)
				{
					list.Add(new Item(9));
				}
				return true;
			};
			try
			{
				if (behindInterface)
				{
					IEnumerable<Item> sequence = list;
					Sink.Add(sequence.Where(grows).ToList().Count);
				}
				else
				{
					Sink.Add(list.Where(grows).ToList().Count);
				}
			}
			catch (InvalidOperationException)
			{
				return true;
			}
			return false;
		}

		static void LinqClosures()
		{
			var list = Items(5);
			var limit = 2;
			var above = list.Where(item => item.Id > limit).ToList();
			Same(3, above.Count, "a lambda that captures a local");
			limit = 4;
			Same(1, list.Count(item => item.Id > limit), "the local changed");
			var holder = new Other { Value = 3 };
			Same(12, list.Where(item => item.Id >= holder.Value).Sum(item => item.Id), "a lambda that captures an object");
			Same(15, Total(list, item => item.Id), "a selector handed in");
		}

		static int Total(List<Item> list, Func<Item, int> selector)
		{
			return list.Sum(selector);
		}

		static void LinqDelegateStaysTyped()
		{
			var list = Items(3);
			Func<Item, bool> big = item => item.Id > 1;
			var some = list.Where(big).ToList();
			var any = list.Any(big);
			Expect(some.Count == 2 && any, "a delegate used twice");
			object boxed = big;
			Expect(boxed.GetType() == typeof(Func<Item, bool>), "the delegate is a Func of Item");
			Expect(big(list[2]) && !big(list[0]), "the delegate called as itself");
			Predicate<Item> match = item => item.Id == 2;
			Expect(ReferenceEquals(list.Find(match), list[1]), "List.Find");
		}

		static void LinqOwnComparison()
		{
			var list = new List<Keyed>
			{
				new Keyed { Key = 1, Serial = 1 }, new Keyed { Key = 1, Serial = 2 }, new Keyed { Key = 2, Serial = 3 },
			};
			Same(2, list.Where(item => item.Serial > 0).Distinct().Count(), "Distinct of a class with IEquatable goes through it");
			Expect(list.Where(item => item.Serial > 1).Contains(new Keyed { Key = 2 }), "Contains of a class with IEquatable goes through it");
			Same(2, list.Select(item => item).Distinct().ToList().Count, "Select, Distinct of a class with IEquatable");
			var values = new List<ByValue> { new ByValue { Number = 1 }, new ByValue { Number = 1 }, new ByValue { Number = 2 } };
			Same(2, values.Where(item => item.Number > 0).Distinct().Count(), "Distinct of a class with Equals goes through it");
			var items = Items(3);
			items.Add(items[0]);
			Same(3, items.Where(item => item.Id > 0).Distinct().Count(), "Distinct of a class without Equals compares references");
		}

		static int OverInterface(IEnumerable<Item> items)
		{
			return items.Where(item => item.Id > 1).Select(item => item.Id).Sum() + items.Count(item => item.Id > 0);
		}

		static void LinqOverInterface()
		{
			var list = Items(3);
			Same(8, OverInterface(list), "Enumerable over a list seen as its interface");
			Same(8, OverInterface(list.ToArray()), "Enumerable over an array seen as its interface");
			Same(8, OverInterface(new HashSet<Item>(list)), "Enumerable over a set");
			IReadOnlyList<Item> readOnly = list;
			Same(2, readOnly.Where(item => item.Id > 1).ToList().Count, "Enumerable over IReadOnlyList");
			var map = new Dictionary<int, Item> { { 1, list[0] }, { 2, list[1] } };
			Same(3, map.Values.Where(item => item.Id > 0).Sum(item => item.Id), "Enumerable over Values");
			Same(1, map.Where(pair => pair.Value.Id > 1).Select(pair => pair.Key).Count(), "Enumerable over the pairs");
			var groups = list.GroupBy(item => item.Name).ToDictionary(group => group.Key, group => group.First());
			Expect(groups.GetType() == typeof(Dictionary<string, Item>) && groups.Count == 3, "GroupBy and ToDictionary");
			var lookup = list.ToLookup(item => item.Id % 2);
			Same(2, lookup[1].Count(), "ToLookup");
			Same(3, list.OfType<Item>().Count() + list.Cast<object>().Count() - 3, "OfType and Cast");
		}

		static void LinqSkipTakeConcat()
		{
			var list = Items(5);
			var other = Items(2);
			var last = list.Skip(1).Take(2).Concat(other).LastOrDefault();
			Expect(ReferenceEquals(last, other[1]), "Skip, Take, Concat, LastOrDefault");
			var joined = list.Where(item => item.Id > 3).Concat(other.Where(item => item.Id > 1)).ToList();
			Expect(joined.GetType() == typeof(List<Item>) && joined.Count == 3, "Concat of two Where");
			Same(3, list.Skip(2).ToArray().Length, "Skip, ToArray");
			Same(2, list.TakeWhile(item => item.Id < 3).Count(), "TakeWhile");
			Same(3, list.SkipWhile(item => item.Id < 3).ToList().Count, "SkipWhile");
			Same(1, list.Where(item => item.Id > 9).DefaultIfEmpty().Count(), "DefaultIfEmpty");
		}

		static void LinqInLoop()
		{
			var lists = new List<List<Item>> { Items(2), Items(3), Items(4) };
			var sum = 0;
			foreach (var list in lists)
			{
				var some = list.Where(item => item.Id > 1).ToList();
				sum += some.Count;
				for (var at = 0; at < 2; at++)
				{
					sum += list.Count(item => item.Id > at);
				}
			}
			Same(21, sum, "Enumerable inside loops");
			IEnumerable<IEnumerable<Item>> nested = lists;
			Same(9, nested.Sum(list => list.Count()), "a sequence of sequences");
			Same(9, lists.SelectMany(list => list).Count(), "SelectMany");
		}
	}
}
