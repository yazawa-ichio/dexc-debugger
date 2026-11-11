using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace GenericMemory
{
	/// <summary>
	/// What a search or a sort answers when it finds its comparison through the default comparer of the element type:
	/// types with no comparison of their own, types that override Equals(object), types that implement IEquatable or
	/// IComparable with an answer that differs on purpose from the one Object.Equals would give, interfaces, strings,
	/// structs, derived lists. Each check writes what it saw next to its verdict, so two Players can be compared line by
	/// line: a rewrite that sends a call to the comparer of another type shows up as another line.
	/// </summary>
	public static class ComparerChecks
	{
		const int Rounds = 2;

		sealed class CheckFailed : Exception
		{
			public CheckFailed(string message)
				: base(message)
			{
			}
		}

		static readonly KeyValuePair<string, Func<string>>[] s_checks =
		{
			Check("PlainList", PlainList),
			Check("PlainArray", PlainArray),
			Check("ByValueList", ByValueList),
			Check("SplitList", SplitList),
			Check("SplitChildList", SplitChildList),
			Check("InterfaceList", InterfaceList),
			Check("Strings", Strings),
			Check("Structs", Structs),
			Check("DerivedLists", DerivedLists),
			Check("UserGenericMethods", UserGenericMethods),
			Check("UserGenericClass", UserGenericClass),
			Check("Sorting", Sorting),
			Check("SortingWithOwnOrder", SortingWithOwnOrder),
			Check("Unsortable", Unsortable),
			Check("Linq", Linq),
			Check("HashedCollections", HashedCollections),
			Check("ArraysAndObjects", ArraysAndObjects),
			// Last: where a rewrite reaches a class nobody initialized, this one ends the process.
			Check("NestedGenericClass", NestedGenericClass),
		};

		public static int Failed { get; private set; }

		static KeyValuePair<string, Func<string>> Check(string name, Func<string> body)
		{
			return new KeyValuePair<string, Func<string>>(name, body);
		}

		/// <summary>
		/// Makes one object of every generic class over object that a rewrite may send a call to, so that those classes
		/// are initialized before the checks run. Only with -generic-memory-comparers-warm: without it the checks show
		/// what happens when nothing else has made them.
		/// </summary>
		public static void Warm()
		{
			GC.KeepAlive(new List<object>());
			GC.KeepAlive(new Finder<object>());
			GC.KeepAlive(new Roster<object>());
			GC.KeepAlive(new Queue<object>());
			GC.KeepAlive(new Stack<object>());
			GC.KeepAlive(new LinkedList<object>());
			GC.KeepAlive(new HashSet<object>());
			GC.KeepAlive(new Dictionary<object, int>());
			GC.KeepAlive(new Dictionary<int, object>());
			GC.KeepAlive(new Dictionary<object, object>());
			GC.KeepAlive(EqualityComparer<object>.Default);
			GC.KeepAlive(Comparer<object>.Default);
		}

		/// <summary>Runs the checks. With a file, what is known is written after every check, so a check that ends the process leaves the others.</summary>
		public static void Run(StringBuilder text, string output = null)
		{
			foreach (var check in s_checks)
			{
				for (var round = 0; round < Rounds; round++)
				{
					string seen;
					var passed = true;
					try
					{
						seen = check.Value();
					}
					catch (Exception exception)
					{
						passed = false;
						seen = exception.GetType().Name + ": " + exception.Message.Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ');
						Failed++;
					}
					text.Append("check\t").Append(check.Key).Append('\t').Append(round).Append('\t').Append(passed ? "PASS" : "FAIL").Append('\t').Append(seen).Append('\n');
				}
				if (output != null)
				{
					System.IO.File.WriteAllText(output, text + "unfinished\tafter " + check.Key + "\n");
				}
			}
		}

		/// <summary>Collects what a check saw; a value that is certain is also checked on the spot.</summary>
		sealed class Seen
		{
			readonly StringBuilder _text = new StringBuilder();

			public Seen Note(string what, object value)
			{
				_text.Append(what).Append('=').Append(value ?? "null").Append(' ');
				return this;
			}

			public Seen Is(string what, object value, object expected)
			{
				Note(what, value);
				if (!Equals(value, expected))
				{
					throw new CheckFailed(what + ": expected " + expected + ", got " + value + " (after " + _text + ")");
				}
				return this;
			}

			public override string ToString()
			{
				return _text.ToString().TrimEnd();
			}
		}

		// ---- the element types ----

		interface IThing
		{
			int Number { get; }
		}

		/// <summary>No comparison of its own: equal only to itself.</summary>
		class Plain : IThing
		{
			public int Number { get; set; }
		}

		sealed class PlainChild : Plain
		{
		}

		/// <summary>Equal by value, through Object.Equals.</summary>
		class ByValue : IThing
		{
			public int Number { get; set; }

			public override bool Equals(object other)
			{
				return other is ByValue && ((ByValue)other).Number == Number;
			}

			public override int GetHashCode()
			{
				return Number;
			}
		}

		sealed class ByValueChild : ByValue
		{
		}

		/// <summary>Equal by value through IEquatable, and never equal through Object.Equals: which one was asked shows.</summary>
		class Split : IThing, IEquatable<Split>
		{
			public int Number { get; set; }

			public bool Equals(Split other)
			{
				return other != null && other.Number == Number;
			}

			public override bool Equals(object other)
			{
				return false;
			}

			public override int GetHashCode()
			{
				return Number;
			}
		}

		sealed class SplitChild : Split
		{
		}

		/// <summary>Ordered through the IComparable that is not generic.</summary>
		sealed class Ordered : IComparable
		{
			public int Number;

			public int CompareTo(object other)
			{
				return Number.CompareTo(((Ordered)other).Number);
			}
		}

		/// <summary>Ascending through IComparable&lt;T&gt;, descending through IComparable: which one was asked shows.</summary>
		sealed class OwnOrder : IComparable<OwnOrder>, IComparable
		{
			public int Number;

			public int CompareTo(OwnOrder other)
			{
				return Number.CompareTo(other.Number);
			}

			public int CompareTo(object other)
			{
				return ((OwnOrder)other).Number.CompareTo(Number);
			}
		}

		struct Cell
		{
			public int X;
		}

		struct OwnCell : IEquatable<OwnCell>
		{
			public int X;

			public bool Equals(OwnCell other)
			{
				return X == other.X;
			}

			public override bool Equals(object other)
			{
				return false;
			}

			public override int GetHashCode()
			{
				return X;
			}
		}

		sealed class Team : List<ByValue>
		{
		}

		class Roster<T> : List<T>
		{
		}

		sealed class Finder<T>
		{
			readonly T[] _items;

			public Finder(params T[] items)
			{
				_items = items;
			}

			public bool Has(T item)
			{
				var comparer = EqualityComparer<T>.Default;
				foreach (var candidate in _items)
				{
					if (comparer.Equals(candidate, item))
					{
						return true;
					}
				}
				return false;
			}

			public int Find(T item)
			{
				return Array.IndexOf(_items, item);
			}
		}

		/// <summary>The search of the outer class is the search of an inner one, which is made for the same type.</summary>
		sealed class Shelf<T>
		{
			readonly Box<T> _box = new Box<T>();

			public void Put(T item)
			{
				_box.Put(item);
			}

			public bool Has(T item)
			{
				return _box.Has(item);
			}

			public int Find(T item)
			{
				return _box.Find(item);
			}
		}

		sealed class Box<T>
		{
			readonly LinkedList<T> _items = new LinkedList<T>();
			T _last;

			public void Put(T item)
			{
				_items.AddLast(item);
				_last = item;
			}

			public bool Has(T item)
			{
				return _items.Contains(item) || EqualityComparer<T>.Default.Equals(_last, item);
			}

			public int Find(T item)
			{
				var at = 0;
				foreach (var candidate in _items)
				{
					if (EqualityComparer<T>.Default.Equals(candidate, item))
					{
						return at;
					}
					at++;
				}
				return -1;
			}
		}

		// ---- the checks ----

		static string NestedGenericClass()
		{
			// Shelf<object>, Box<object> and LinkedList<object> are made nowhere in this assembly: a call that is sent to
			// them finds them as the runtime left them.
			var seen = new Seen();
			var plain = new Plain { Number = 1 };
			var plains = new Shelf<Plain>();
			plains.Put(plain);
			seen.Is("plain", plains.Has(plain), true).Is("plainOther", plains.Has(new Plain { Number = 1 }), false).Is("plainFind", plains.Find(plain), 0);
			var values = new Shelf<ByValue>();
			values.Put(new ByValue { Number = 2 });
			seen.Is("byValue", values.Has(new ByValue { Number = 2 }), true).Is("byValueFind", values.Find(new ByValue { Number = 2 }), 0);
			var splits = new Shelf<Split>();
			splits.Put(new Split { Number = 3 });
			seen.Is("split", splits.Has(new Split { Number = 3 }), true).Is("splitFind", splits.Find(new Split { Number = 3 }), 0);
			return seen.ToString();
		}

		static string PlainList()
		{
			var seen = new Seen();
			var a = new Plain { Number = 1 };
			var b = new Plain { Number = 2 };
			var c = new Plain { Number = 3 };
			var absent = new Plain { Number = 2 };
			var list = new List<Plain> { a, b, c, b };
			seen.Is("contains", list.Contains(b), true).Is("containsAbsent", list.Contains(absent), false).Is("containsNull", list.Contains(null), false);
			seen.Is("indexOf", list.IndexOf(b), 1).Is("lastIndexOf", list.LastIndexOf(b), 3).Is("indexOfAbsent", list.IndexOf(absent), -1);
			seen.Is("indexOfFrom", list.IndexOf(b, 2), 3).Is("lastIndexOfAbsent", list.LastIndexOf(absent), -1);
			seen.Is("remove", list.Remove(b), true).Is("count", list.Count, 3).Is("indexOfAfter", list.IndexOf(b), 2).Is("removeAbsent", list.Remove(absent), false);
			list.Add(null);
			seen.Is("containsNullInside", list.Contains(null), true).Is("indexOfNull", list.IndexOf(null), 3).Is("containsAfterNull", list.Contains(b), true);
			seen.Is("removeNull", list.Remove(null), true).Is("countAfter", list.Count, 3);
			var children = new List<PlainChild> { new PlainChild { Number = 1 } };
			seen.Is("child", children.Contains(children[0]), true).Is("childAbsent", children.Contains(new PlainChild { Number = 1 }), false);
			var mixed = new List<Plain> { a, children[0] };
			seen.Is("derivedInside", mixed.IndexOf(children[0]), 1);
			return seen.ToString();
		}

		static string PlainArray()
		{
			var seen = new Seen();
			var a = new Plain { Number = 1 };
			var b = new Plain { Number = 2 };
			var c = new Plain { Number = 3 };
			var array = new[] { a, b, c, b, null };
			seen.Is("indexOf", Array.IndexOf(array, b), 1).Is("lastIndexOf", Array.LastIndexOf(array, b), 3).Is("absent", Array.IndexOf(array, new Plain { Number = 2 }), -1);
			seen.Is("from", Array.IndexOf(array, c, 1), 2).Is("range", Array.IndexOf(array, a, 1, 2), -1).Is("null", Array.IndexOf(array, null), 4);
			seen.Is("exists", Array.Exists(array, item => item == c), true).Is("contains", ((ICollection<Plain>)array).Contains(b), true);
			return seen.ToString();
		}

		static string ByValueList()
		{
			var seen = new Seen();
			var list = new List<ByValue> { new ByValue { Number = 1 }, new ByValue { Number = 2 }, new ByValue { Number = 3 }, new ByValue { Number = 2 } };
			var probe = new ByValue { Number = 2 };
			seen.Is("contains", list.Contains(probe), true).Is("containsAbsent", list.Contains(new ByValue { Number = 9 }), false);
			seen.Is("indexOf", list.IndexOf(probe), 1).Is("lastIndexOf", list.LastIndexOf(probe), 3);
			seen.Is("remove", list.Remove(probe), true).Is("count", list.Count, 3).Is("indexOfAfter", list.IndexOf(probe), 2);
			var array = list.ToArray();
			seen.Is("arrayIndexOf", Array.IndexOf(array, new ByValue { Number = 3 }), 1).Is("arrayLastIndexOf", Array.LastIndexOf(array, probe), 2);
			var holes = new List<ByValue> { new ByValue { Number = 1 }, null, new ByValue { Number = 3 } };
			seen.Is("pastNull", holes.Contains(new ByValue { Number = 3 }), true).Is("null", holes.Contains(null), true).Is("indexOfNull", holes.IndexOf(null), 1);
			var withChild = new List<ByValue> { new ByValue { Number = 4 }, new ByValueChild { Number = 5 } };
			seen.Is("childInside", withChild.Contains(new ByValue { Number = 5 }), true);
			var children = new List<ByValueChild> { new ByValueChild { Number = 6 } };
			seen.Is("children", children.Contains(new ByValueChild { Number = 6 }), true).Is("childrenIndexOf", children.IndexOf(new ByValueChild { Number = 7 }), -1);
			return seen.ToString();
		}

		static string SplitList()
		{
			var seen = new Seen();
			var first = new Split { Number = 1 };
			var list = new List<Split> { first, new Split { Number = 2 }, new Split { Number = 3 } };
			var probe = new Split { Number = 2 };
			// Equals(Split) says yes and Equals(object) says no: the default comparer of the type asks the first.
			seen.Is("contains", list.Contains(probe), true).Is("indexOf", list.IndexOf(probe), 1).Is("lastIndexOf", list.LastIndexOf(new Split { Number = 3 }), 2);
			seen.Is("containsAbsent", list.Contains(new Split { Number = 9 }), false);
			seen.Is("arrayIndexOf", Array.IndexOf(list.ToArray(), new Split { Number = 3 }), 2);
			seen.Is("remove", list.Remove(probe), true).Is("count", list.Count, 2);
			var withChild = new List<Split> { new SplitChild { Number = 5 } };
			seen.Is("childInside", withChild.Contains(new Split { Number = 5 }), true);
			return seen.ToString();
		}

		static string SplitChildList()
		{
			// The base implements IEquatable<Split>, the element type is the derived class: what the runtime picks is
			// written down and compared between Players, not asserted.
			var seen = new Seen();
			var list = new List<SplitChild> { new SplitChild { Number = 1 }, new SplitChild { Number = 2 } };
			var probe = new SplitChild { Number = 2 };
			seen.Note("contains", list.Contains(probe)).Note("indexOf", list.IndexOf(probe)).Note("same", list.Contains(list[1]));
			seen.Note("arrayIndexOf", Array.IndexOf(list.ToArray(), probe)).Note("remove", list.Remove(probe)).Note("count", list.Count);
			seen.Note("comparer", EqualityComparer<SplitChild>.Default.GetType().Name);
			seen.Note("generic", Same(list[0], new SplitChild { Number = 1 }));
			return seen.ToString();
		}

		static string InterfaceList()
		{
			var seen = new Seen();
			var plain = new Plain { Number = 1 };
			var list = new List<IThing> { plain, new ByValue { Number = 2 }, new Split { Number = 3 } };
			seen.Is("same", list.Contains(plain), true).Is("plain", list.Contains(new Plain { Number = 1 }), false);
			seen.Is("byValue", list.Contains(new ByValue { Number = 2 }), true).Is("indexOf", list.IndexOf(new ByValue { Number = 2 }), 1);
			// Asked through the interface type, even Split is compared with Object.Equals.
			seen.Is("split", list.Contains(new Split { Number = 3 }), false).Is("splitSame", list.Contains(list[2]), false);
			seen.Is("remove", list.Remove(new ByValue { Number = 2 }), true).Is("count", list.Count, 2);
			seen.Is("array", Array.IndexOf(new IThing[] { plain, new ByValue { Number = 7 } }, new ByValue { Number = 7 }), 1);
			return seen.ToString();
		}

		static string Strings()
		{
			var seen = new Seen();
			var list = new List<string> { "one", "two", "three", null };
			var made = new string('o', 1) + "ne";
			seen.Is("contains", list.Contains(made), true).Is("indexOf", list.IndexOf(new string('t', 1) + "wo"), 1).Is("absent", list.Contains("four"), false);
			seen.Is("null", list.IndexOf(null), 3).Is("remove", list.Remove(made), true).Is("count", list.Count, 3);
			seen.Is("array", Array.IndexOf(new[] { "a", "b" }, new string('b', 1)), 1);
			return seen.ToString();
		}

		static string Structs()
		{
			var seen = new Seen();
			var cells = new List<Cell> { new Cell { X = 1 }, new Cell { X = 2 } };
			seen.Is("cell", cells.Contains(new Cell { X = 2 }), true).Is("cellIndexOf", cells.IndexOf(new Cell { X = 2 }), 1).Is("cellAbsent", cells.Contains(new Cell { X = 3 }), false);
			var own = new List<OwnCell> { new OwnCell { X = 1 }, new OwnCell { X = 2 } };
			seen.Is("own", own.Contains(new OwnCell { X = 2 }), true).Is("ownIndexOf", own.IndexOf(new OwnCell { X = 1 }), 0).Is("ownRemove", own.Remove(new OwnCell { X = 1 }), true);
			seen.Is("ownArray", Array.IndexOf(new[] { new OwnCell { X = 4 }, new OwnCell { X = 5 } }, new OwnCell { X = 5 }), 1);
			var numbers = new List<int> { 3, 4, 5 };
			seen.Is("int", numbers.Contains(4), true).Is("intIndexOf", numbers.IndexOf(5), 2);
			var maybe = new List<int?> { 1, null, 3 };
			seen.Is("nullable", maybe.Contains(3), true).Is("nullableNull", maybe.IndexOf(null), 1);
			var days = new List<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Friday };
			seen.Is("enum", days.IndexOf(DayOfWeek.Friday), 1);
			return seen.ToString();
		}

		static string DerivedLists()
		{
			var seen = new Seen();
			var team = new Team { new ByValue { Number = 1 }, new ByValue { Number = 2 } };
			seen.Is("team", team.Contains(new ByValue { Number = 2 }), true).Is("teamIndexOf", team.IndexOf(new ByValue { Number = 2 }), 1);
			seen.Is("teamRemove", team.Remove(new ByValue { Number = 1 }), true).Is("teamCount", team.Count, 1);
			List<ByValue> asBase = team;
			seen.Is("teamAsList", asBase.Contains(new ByValue { Number = 2 }), true);
			var plain = new Plain { Number = 1 };
			var roster = new Roster<Plain> { plain };
			seen.Is("roster", roster.Contains(plain), true).Is("rosterAbsent", roster.Contains(new Plain { Number = 1 }), false);
			var splits = new Roster<Split> { new Split { Number = 4 } };
			seen.Is("rosterSplit", splits.Contains(new Split { Number = 4 }), true).Is("rosterSplitIndexOf", splits.IndexOf(new Split { Number = 4 }), 0);
			return seen.ToString();
		}

		static bool Same<T>(T first, T second)
		{
			return EqualityComparer<T>.Default.Equals(first, second);
		}

		static int Order<T>(T first, T second)
		{
			return Math.Sign(Comparer<T>.Default.Compare(first, second));
		}

		static string Describe<T>(T first, T second)
		{
			return typeof(T).Name + ":" + EqualityComparer<T>.Default.Equals(first, second);
		}

		static IEqualityComparer<T> Default<T>()
		{
			return EqualityComparer<T>.Default;
		}

		static string UserGenericMethods()
		{
			var seen = new Seen();
			var plain = new Plain { Number = 1 };
			seen.Is("plainSame", Same(plain, plain), true).Is("plainOther", Same(plain, new Plain { Number = 1 }), false).Is("plainNull", Same(plain, null), false).Is("nulls", Same<Plain>(null, null), true);
			seen.Is("byValue", Same(new ByValue { Number = 2 }, new ByValue { Number = 2 }), true).Is("byValueOther", Same(new ByValue { Number = 2 }, new ByValue { Number = 3 }), false);
			seen.Is("split", Same(new Split { Number = 2 }, new Split { Number = 2 }), true);
			seen.Is("thing", Same<IThing>(new ByValue { Number = 2 }, new ByValue { Number = 2 }), true).Is("thingSplit", Same<IThing>(new Split { Number = 2 }, new Split { Number = 2 }), false);
			seen.Is("object", Same<object>(new Split { Number = 2 }, new Split { Number = 2 }), false);
			seen.Is("string", Same(new string('x', 2), "xx"), true).Is("int", Same(3, 3), true).Is("cell", Same(new OwnCell { X = 1 }, new OwnCell { X = 1 }), true);
			seen.Is("order", Order(new Ordered { Number = 1 }, new Ordered { Number = 2 }), -1).Is("ownOrder", Order(new OwnOrder { Number = 1 }, new OwnOrder { Number = 2 }), -1);
			// The type itself is asked for here, so the call cannot be made for another.
			seen.Is("describe", Describe(plain, plain), "Plain:True").Is("describeByValue", Describe(new ByValue { Number = 1 }, new ByValue { Number = 1 }), "ByValue:True");
			// A comparer that is handed out is the comparer of the type.
			seen.Is("handedOut", Default<Plain>().GetType().GetGenericArguments()[0].Name, "Plain").Is("handedOutByValue", Default<ByValue>().GetType().GetGenericArguments()[0].Name, "ByValue");
			seen.Note("comparerOfPlain", Default<Plain>().GetType().Name).Note("comparerOfSplit", Default<Split>().GetType().Name);
			return seen.ToString();
		}

		static string UserGenericClass()
		{
			var seen = new Seen();
			var plain = new Plain { Number = 1 };
			var plains = new Finder<Plain>(plain, new Plain { Number = 2 });
			seen.Is("plain", plains.Has(plain), true).Is("plainOther", plains.Has(new Plain { Number = 2 }), false).Is("plainFind", plains.Find(plain), 0);
			var values = new Finder<ByValue>(new ByValue { Number = 1 }, new ByValue { Number = 2 });
			seen.Is("byValue", values.Has(new ByValue { Number = 2 }), true).Is("byValueFind", values.Find(new ByValue { Number = 2 }), 1);
			var splits = new Finder<Split>(new Split { Number = 1 }, new Split { Number = 2 });
			seen.Is("split", splits.Has(new Split { Number = 2 }), true).Is("splitFind", splits.Find(new Split { Number = 2 }), 1);
			var things = new Finder<IThing>(new Split { Number = 1 }, new ByValue { Number = 2 });
			seen.Is("thing", things.Has(new ByValue { Number = 2 }), true).Is("thingSplit", things.Has(new Split { Number = 1 }), false);
			return seen.ToString();
		}

		static string Numbers<T>(IEnumerable<T> items, Func<T, int> number)
		{
			return string.Join(",", items.Select(item => number(item).ToString()).ToArray());
		}

		static string Sorting()
		{
			var seen = new Seen();
			var list = new List<Ordered> { new Ordered { Number = 3 }, new Ordered { Number = 1 }, new Ordered { Number = 2 } };
			list.Sort();
			seen.Is("sort", Numbers(list, item => item.Number), "1,2,3");
			seen.Is("binarySearch", list.BinarySearch(new Ordered { Number = 2 }), 1);
			var array = new[] { new Ordered { Number = 9 }, new Ordered { Number = 7 }, new Ordered { Number = 8 } };
			Array.Sort(array);
			seen.Is("arraySort", Numbers(array, item => item.Number), "7,8,9").Is("arrayBinarySearch", Array.BinarySearch(array, new Ordered { Number = 9 }), 2);
			seen.Is("max", list.Max().Number, 3).Is("min", list.Min().Number, 1);
			seen.Is("orderBy", Numbers(array.OrderByDescending(item => item), item => item.Number), "9,8,7");
			return seen.ToString();
		}

		static string SortingWithOwnOrder()
		{
			var seen = new Seen();
			var list = new List<OwnOrder> { new OwnOrder { Number = 3 }, new OwnOrder { Number = 1 }, new OwnOrder { Number = 2 } };
			list.Sort();
			// Ascending is the answer of CompareTo(OwnOrder); CompareTo(object) would give 3,2,1.
			seen.Is("sort", Numbers(list, item => item.Number), "1,2,3").Is("binarySearch", list.BinarySearch(new OwnOrder { Number = 3 }), 2);
			var array = new[] { new OwnOrder { Number = 9 }, new OwnOrder { Number = 7 }, new OwnOrder { Number = 8 } };
			Array.Sort(array);
			seen.Is("arraySort", Numbers(array, item => item.Number), "7,8,9");
			seen.Is("max", list.Max().Number, 3).Is("orderBy", Numbers(array.OrderBy(item => item), item => item.Number), "7,8,9");
			return seen.ToString();
		}

		static string Unsortable()
		{
			// Nothing to compare with: what is thrown is written down and compared between Players.
			var seen = new Seen();
			var list = new List<Plain> { new Plain { Number = 2 }, new Plain { Number = 1 } };
			try
			{
				list.Sort();
				seen.Note("sort", "no exception");
			}
			catch (Exception exception)
			{
				seen.Note("sort", exception.GetType().Name + "/" + (exception.InnerException == null ? "none" : exception.InnerException.GetType().Name));
			}
			try
			{
				seen.Note("max", list.Max().Number);
			}
			catch (Exception exception)
			{
				seen.Note("max", exception.GetType().Name);
			}
			seen.Is("single", new List<Plain> { new Plain { Number = 5 } }.Max().Number, 5);
			return seen.ToString();
		}

		static string Linq()
		{
			var seen = new Seen();
			var values = new List<ByValue> { new ByValue { Number = 1 }, new ByValue { Number = 1 }, new ByValue { Number = 2 } };
			var plain = new Plain { Number = 1 };
			var plains = new List<Plain> { plain, new Plain { Number = 1 }, plain };
			var splits = new List<Split> { new Split { Number = 1 }, new Split { Number = 1 }, new Split { Number = 2 } };
			seen.Is("contains", values.Where(item => true).Contains(new ByValue { Number = 2 }), true).Is("containsPlain", plains.Where(item => true).Contains(new Plain { Number = 1 }), false);
			seen.Is("containsSplit", splits.Where(item => true).Contains(new Split { Number = 2 }), true).Is("containsList", ((IEnumerable<ByValue>)values).Contains(new ByValue { Number = 1 }), true);
			seen.Is("distinct", values.Distinct().Count(), 2).Is("distinctPlain", plains.Distinct().Count(), 2).Is("distinctSplit", splits.Distinct().Count(), 2);
			seen.Is("sequenceEqual", values.SequenceEqual(new[] { new ByValue { Number = 1 }, new ByValue { Number = 1 }, new ByValue { Number = 2 } }), true);
			seen.Is("sequenceEqualPlain", plains.SequenceEqual(new[] { plain, new Plain { Number = 1 }, plain }), false);
			seen.Is("sequenceEqualSplit", splits.SequenceEqual(new[] { new Split { Number = 1 }, new Split { Number = 1 }, new Split { Number = 2 } }), true);
			seen.Is("except", values.Except(new[] { new ByValue { Number = 1 } }).Count(), 1).Is("union", values.Union(new[] { new ByValue { Number = 3 } }).Count(), 3);
			seen.Is("intersect", plains.Intersect(new[] { plain }).Count(), 1).Is("intersectSplit", splits.Intersect(new[] { new Split { Number = 2 } }).Count(), 1);
			seen.Is("groupBy", values.GroupBy(item => item).Count(), 2).Is("groupByPlain", plains.GroupBy(item => item).Count(), 2).Is("groupBySplit", splits.GroupBy(item => item).Count(), 2);
			seen.Is("toLookup", values.ToLookup(item => item)[new ByValue { Number = 1 }].Count(), 2);
			seen.Is("toDictionary", values.Distinct().ToDictionary(item => item, item => item.Number).ContainsKey(new ByValue { Number = 2 }), true);
			seen.Is("join", values.Join(values, item => item, item => item, (first, second) => 1).Count(), 5);
			return seen.ToString();
		}

		static string HashedCollections()
		{
			var seen = new Seen();
			var plain = new Plain { Number = 1 };
			var plains = new HashSet<Plain> { plain, new Plain { Number = 1 } };
			seen.Is("setPlain", plains.Count, 2).Is("setPlainContains", plains.Contains(plain), true).Is("setPlainOther", plains.Contains(new Plain { Number = 1 }), false);
			var values = new HashSet<ByValue> { new ByValue { Number = 1 }, new ByValue { Number = 1 } };
			seen.Is("setByValue", values.Count, 1).Is("setByValueContains", values.Contains(new ByValue { Number = 1 }), true);
			var splits = new HashSet<Split> { new Split { Number = 1 }, new Split { Number = 1 } };
			seen.Is("setSplit", splits.Count, 1).Is("setSplitContains", splits.Contains(new Split { Number = 1 }), true);
			var byPlain = new Dictionary<Plain, int> { { plain, 1 } };
			seen.Is("dictPlain", byPlain.ContainsKey(plain), true).Is("dictPlainOther", byPlain.ContainsKey(new Plain { Number = 1 }), false);
			var byValue = new Dictionary<ByValue, int> { { new ByValue { Number = 1 }, 1 } };
			seen.Is("dictByValue", byValue.ContainsKey(new ByValue { Number = 1 }), true);
			var bySplit = new Dictionary<Split, int> { { new Split { Number = 1 }, 1 } };
			seen.Is("dictSplit", bySplit.ContainsKey(new Split { Number = 1 }), true);
			seen.Is("dictValue", byPlain.ContainsValue(1), true).Is("dictValueOfThing", new Dictionary<int, ByValue> { { 1, new ByValue { Number = 4 } } }.ContainsValue(new ByValue { Number = 4 }), true);
			var queue = new Queue<ByValue>(new[] { new ByValue { Number = 1 } });
			var stack = new Stack<Split>(new[] { new Split { Number = 1 } });
			seen.Is("queue", queue.Contains(new ByValue { Number = 1 }), true).Is("stack", stack.Contains(new Split { Number = 1 }), true);
			var linked = new LinkedList<ByValue>(new[] { new ByValue { Number = 1 } });
			seen.Is("linked", linked.Contains(new ByValue { Number = 1 }), true);
			return seen.ToString();
		}

		static string ArraysAndObjects()
		{
			var seen = new Seen();
			var row = new[] { new Plain { Number = 1 } };
			var rows = new List<Plain[]> { row, new[] { row[0] } };
			seen.Is("arrayElement", rows.IndexOf(row), 0).Is("arrayElementOther", rows.Contains(new[] { row[0] }), false);
			var objects = new List<object> { 5, "five", new ByValue { Number = 5 }, null };
			seen.Is("boxed", objects.Contains(5), true).Is("string", objects.IndexOf(new string('f', 1) + "ive"), 1).Is("byValue", objects.IndexOf(new ByValue { Number = 5 }), 2).Is("null", objects.IndexOf(null), 3);
			var lists = new List<List<ByValue>> { new List<ByValue>() };
			seen.Is("listOfLists", lists.Contains(lists[0]), true).Is("listOfListsOther", lists.Contains(new List<ByValue>()), false);
			var actions = new List<Func<int>>();
			Func<int> five = () => 5;
			actions.Add(five);
			seen.Is("delegate", actions.Contains(five), true).Is("delegateIndexOf", actions.IndexOf(five), 0);
			return seen.ToString();
		}
	}
}
