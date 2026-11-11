using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using GenericMemory.Data;
using UnityEngine.Profiling;
using UnityEngine.Scripting;

namespace GenericMemory
{
	/// <summary>
	/// The speed that the choice between the rewrites rests on. The List kernels are the same work in every build, so a
	/// build whose List methods were moved can be read against one where they were not. The LINQ kernels run the bodies
	/// of the LinqBasic and LinqHeavy patterns as they are written for a type, and as they are written for object.
	/// Each kernel runs several times with the collector off and the best run kept; the number is nanoseconds for one
	/// call (one element for the loops, one search for Contains, one whole body for LINQ).
	/// </summary>
	public static class SpeedBench
	{
		const int Count = 1 << 20;
		const int Mask = 1023;
		const int Passes = 16;
		const int Searches = 20000;
		const int Bodies = 2000;

		static readonly E0000[] s_items = new E0000[Mask + 1];
		static List<E0000> s_list;
		static E0000[] s_array;
		static List<E0000> s_smallList;
		static E0000[] s_smallArray;
		static IList<E0000> s_listInterface;
		static IList<E0000> s_arrayInterface;
		static List<E0000> s_source;

		public static void Run(StringBuilder text)
		{
			for (var at = 0; at <= Mask; at++)
			{
				s_items[at] = new E0000(at);
			}
			s_list = new List<E0000>();
			s_array = new E0000[Count];
			s_smallList = new List<E0000>();
			s_smallArray = new E0000[Mask + 1];
			for (var at = 0; at < Count; at++)
			{
				s_list.Add(s_items[at & Mask]);
				s_array[at] = s_items[at & Mask];
			}
			for (var at = 0; at <= Mask; at++)
			{
				s_smallList.Add(s_items[at]);
				s_smallArray[at] = s_items[at];
			}
			s_listInterface = s_list;
			s_arrayInterface = s_array;
			// A hundred items with ids and names that repeat, so that grouping and Distinct have something to do.
			s_source = new List<E0000>();
			for (var at = 0; at < 100; at++)
			{
				s_source.Add(new E0000((at * 7) % 37 + 1) { Name = "n" + (at % 11) });
			}
			text.Append("same\tbasic\t").Append(BasicTyped(s_source) == BasicErased(s_source) ? "true" : "false").Append('\n');
			text.Append("same\theavy\t").Append(HeavyTyped(s_source) == HeavyErased(s_source) ? "true" : "false").Append('\n');

			Report(text, "list.add", AddList);
			Report(text, "list.index", IndexList);
			Report(text, "list.foreach", ForeachList);
			Report(text, "list.contains1024", ContainsList);
			Report(text, "list.ilist", InterfaceList);
			Report(text, "array.index", IndexArray);
			Report(text, "array.foreach", ForeachArray);
			Report(text, "array.contains1024", ContainsArray);
			Report(text, "array.ilist", InterfaceArray);
			Report(text, "linq.basic.typed", BasicTypedLoop);
			Report(text, "linq.basic.erased", BasicErasedLoop);
			Report(text, "linq.heavy.typed", HeavyTypedLoop);
			Report(text, "linq.heavy.erased", HeavyErasedLoop);
		}

		static void Report(StringBuilder text, string name, Func<long> kernel)
		{
			kernel();
			var best = double.MaxValue;
			var bytes = double.MaxValue;
			for (var run = 0; run < 7; run++)
			{
				// What a kernel allocates is collected between the runs, never inside one, so the growth of the heap is
				// what the run allocated.
				GC.Collect();
				GarbageCollector.GCMode = GarbageCollector.Mode.Disabled;
				var used = Profiler.GetMonoUsedSizeLong();
				var start = Stopwatch.GetTimestamp();
				var calls = kernel();
				var ticks = Stopwatch.GetTimestamp() - start;
				var grown = Profiler.GetMonoUsedSizeLong() - used;
				GarbageCollector.GCMode = GarbageCollector.Mode.Enabled;
				best = Math.Min(best, ticks * 1e9 / Stopwatch.Frequency / calls);
				bytes = Math.Min(bytes, grown / (double)calls);
			}
			text.Append("bench\t").Append(name).Append('\t').Append(best.ToString("F2", CultureInfo.InvariantCulture)).Append('\n');
			text.Append("bench\t").Append(name).Append(".bytes\t").Append(bytes.ToString("F1", CultureInfo.InvariantCulture)).Append('\n');
		}

		// From empty, so the growth of the array is in the number.
		static long AddList()
		{
			var list = new List<E0000>();
			for (var at = 0; at < Count; at++)
			{
				list.Add(s_items[at & Mask]);
			}
			Sink.Add(list.Count);
			return Count;
		}

		static long IndexList()
		{
			var list = s_list;
			var sum = 0;
			for (var pass = 0; pass < Passes; pass++)
			{
				for (var at = 0; at < list.Count; at++)
				{
					sum += list[at].Id;
				}
			}
			Sink.Add(sum);
			return (long)Passes * list.Count;
		}

		static long IndexArray()
		{
			var array = s_array;
			var sum = 0;
			for (var pass = 0; pass < Passes; pass++)
			{
				for (var at = 0; at < array.Length; at++)
				{
					sum += array[at].Id;
				}
			}
			Sink.Add(sum);
			return (long)Passes * array.Length;
		}

		static long ForeachList()
		{
			var sum = 0;
			for (var pass = 0; pass < Passes; pass++)
			{
				foreach (var item in s_list)
				{
					sum += item.Id;
				}
			}
			Sink.Add(sum);
			return (long)Passes * s_list.Count;
		}

		static long ForeachArray()
		{
			var sum = 0;
			for (var pass = 0; pass < Passes; pass++)
			{
				foreach (var item in s_array)
				{
					sum += item.Id;
				}
			}
			Sink.Add(sum);
			return (long)Passes * s_array.Length;
		}

		// The last of 1024 items, so each call walks them all.
		static long ContainsList()
		{
			var last = s_items[Mask];
			var found = 0;
			for (var at = 0; at < Searches; at++)
			{
				if (s_smallList.Contains(last))
				{
					found++;
				}
			}
			Sink.Add(found);
			return Searches;
		}

		static long ContainsArray()
		{
			var last = s_items[Mask];
			var found = 0;
			for (var at = 0; at < Searches; at++)
			{
				if (Array.IndexOf(s_smallArray, last) >= 0)
				{
					found++;
				}
			}
			Sink.Add(found);
			return Searches;
		}

		// Count and the indexer through IList<T>, for each element.
		static long InterfaceList()
		{
			return Through(s_listInterface);
		}

		static long InterfaceArray()
		{
			return Through(s_arrayInterface);
		}

		static long Through(IList<E0000> list)
		{
			var sum = 0;
			for (var pass = 0; pass < Passes / 2; pass++)
			{
				for (var at = 0; at < list.Count; at++)
				{
					sum += list[at].Id;
				}
			}
			Sink.Add(sum);
			return (long)(Passes / 2) * list.Count;
		}

		// ---- the bodies of the LINQ patterns ----

		static int BasicTyped(List<E0000> list)
		{
			var first = list.FirstOrDefault(item => item.Id == 2);
			var any = list.Any(item => item.Id == 3);
			var some = list.Where(item => item.Id > 1).ToList();
			var ids = list.Select(item => item.Id).ToArray();
			return first.Id + (any ? 1 : 0) + some.Count + ids.Length;
		}

		static int BasicErased(List<E0000> list)
		{
			IEnumerable<object> source = list;
			var first = (E0000)source.FirstOrDefault(item => ((E0000)item).Id == 2);
			var any = source.Any(item => ((E0000)item).Id == 3);
			var some = source.Where(item => ((E0000)item).Id > 1).ToList();
			var ids = source.Select(item => ((E0000)item).Id).ToArray();
			return first.Id + (any ? 1 : 0) + some.Count + ids.Length;
		}

		static int HeavyTyped(List<E0000> list)
		{
			var ordered = list.OrderBy(item => item.Id).ThenByDescending(item => item.Name).ToList();
			var byId = list.GroupBy(item => item.Id).ToDictionary(group => group.Key, group => group.First());
			var names = list.Where(item => item.Id > 1).Select(item => item.Name).Distinct().Count();
			var last = list.Skip(1).Take(2).Concat(ordered).LastOrDefault();
			var total = list.Sum(item => item.Id) + list.Max(item => item.Id);
			return ordered.Count + byId.Count + names + last.Id + total;
		}

		static int HeavyErased(List<E0000> list)
		{
			IEnumerable<object> source = list;
			var ordered = source.OrderBy(item => ((E0000)item).Id).ThenByDescending(item => ((E0000)item).Name).ToList();
			var byId = source.GroupBy(item => ((E0000)item).Id).ToDictionary(group => group.Key, group => group.First());
			var names = source.Where(item => ((E0000)item).Id > 1).Select(item => ((E0000)item).Name).Distinct().Count();
			var last = (E0000)source.Skip(1).Take(2).Concat(ordered).LastOrDefault();
			var total = source.Sum(item => ((E0000)item).Id) + source.Max(item => ((E0000)item).Id);
			return ordered.Count + byId.Count + names + last.Id + total;
		}

		static long BasicTypedLoop()
		{
			var sum = 0;
			for (var at = 0; at < Bodies * 4; at++)
			{
				sum += BasicTyped(s_source);
			}
			Sink.Add(sum);
			return Bodies * 4;
		}

		static long BasicErasedLoop()
		{
			var sum = 0;
			for (var at = 0; at < Bodies * 4; at++)
			{
				sum += BasicErased(s_source);
			}
			Sink.Add(sum);
			return Bodies * 4;
		}

		static long HeavyTypedLoop()
		{
			var sum = 0;
			for (var at = 0; at < Bodies; at++)
			{
				sum += HeavyTyped(s_source);
			}
			Sink.Add(sum);
			return Bodies;
		}

		static long HeavyErasedLoop()
		{
			var sum = 0;
			for (var at = 0; at < Bodies; at++)
			{
				sum += HeavyErased(s_source);
			}
			Sink.Add(sum);
			return Bodies;
		}
	}
}
