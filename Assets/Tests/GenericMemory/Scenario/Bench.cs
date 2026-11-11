using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Cysharp.Threading.Tasks;
using GenericMemory.Data;
using GenericMemory.Lib;

namespace GenericMemory
{
	/// <summary>
	/// What the rewrites cost in time. Each kernel is one kind of call in a tight loop on one type, run several times with
	/// the best run kept, so the number is what a call costs once everything it needs has been made.
	/// </summary>
	public static class Bench
	{
		const int Count = 1000000;
		const int Mask = 1023;

		static readonly List<Pinner> s_list = new List<Pinner>(Count);
		static readonly List<Pinner> s_small = new List<Pinner>();
		static readonly List<object> s_erased = new List<object>(Count);
		static readonly SlimList<Pinner> s_slim = new SlimList<Pinner>();
		static readonly Dictionary<int, Pinner> s_map = new Dictionary<int, Pinner>();
		static readonly Repository<Pinner> s_repository = new Repository<Pinner>();
		static readonly Pinner[] s_items = new Pinner[Mask + 1];
		static readonly Predicate<Pinner> s_last = item => item.Id == 99;

		public static void RunSync(StringBuilder text)
		{
			for (var at = 0; at <= Mask; at++)
			{
				s_items[at] = new Pinner(at);
				s_map[at] = s_items[at];
				s_repository.Add(at, s_items[at]);
			}
			for (var at = 0; at < 100; at++)
			{
				s_small.Add(s_items[at]);
			}
			Report(text, "list.add", ListAdd);
			Report(text, "list.index", ListIndex);
			Report(text, "list.foreach", ListForeach);
			Report(text, "list.capacity", ListCapacity);
			Report(text, "list.find100", ListFind);
			Report(text, "dict.tryget", DictTryGet);
			Report(text, "dict.set", DictSet);
			Report(text, "repo.item", RepositoryItem);
			Report(text, "repo.get", RepositoryGet);
			Report(text, "util.safeget", UtilSafeGet);
			Report(text, "erased.add", ErasedAdd);
			Report(text, "erased.index", ErasedIndex);
			Report(text, "slim.add", SlimAdd);
			Report(text, "slim.index", SlimIndex);
			Report(text, "async.completed", AsyncCompleted);
		}

		static void Report(StringBuilder text, string name, Func<long> kernel)
		{
			kernel();
			var best = double.MaxValue;
			for (var run = 0; run < 7; run++)
			{
				var start = Stopwatch.GetTimestamp();
				var calls = kernel();
				var ticks = Stopwatch.GetTimestamp() - start;
				best = Math.Min(best, ticks * 1e9 / Stopwatch.Frequency / calls);
			}
			text.Append("bench\t").Append(name).Append('\t').Append(best.ToString("F2", CultureInfo.InvariantCulture)).Append('\n');
		}

		static long ListAdd()
		{
			var list = s_list;
			list.Clear();
			for (var at = 0; at < Count; at++)
			{
				list.Add(s_items[at & Mask]);
			}
			return Count;
		}

		static long ListIndex()
		{
			var list = s_list;
			var sum = 0;
			for (var at = 0; at < list.Count; at++)
			{
				sum += list[at].Id;
			}
			Sink.Add(sum);
			return list.Count;
		}

		static long ListForeach()
		{
			var sum = 0;
			foreach (var item in s_list)
			{
				sum += item.Id;
			}
			Sink.Add(sum);
			return s_list.Count;
		}

		// A method that is not in a slot: the hoisting moves it, and the canonicalizer then calls it for object.
		static long ListCapacity()
		{
			var list = s_small;
			var sum = 0;
			for (var at = 0; at < Count; at++)
			{
				sum += list.Capacity;
			}
			Sink.Add(sum);
			return Count;
		}

		static long ListFind()
		{
			var list = s_small;
			var sum = 0;
			for (var at = 0; at < 20000; at++)
			{
				sum += list.Find(s_last).Id;
			}
			Sink.Add(sum);
			return 20000;
		}

		static long DictTryGet()
		{
			var map = s_map;
			var sum = 0;
			for (var at = 0; at < Count; at++)
			{
				if (map.TryGetValue(at & Mask, out var value))
				{
					sum += value.Id;
				}
			}
			Sink.Add(sum);
			return Count;
		}

		static long DictSet()
		{
			var map = s_map;
			for (var at = 0; at < Count; at++)
			{
				map[at & Mask] = s_items[at & Mask];
			}
			return Count;
		}

		// The indexer and IsEmpty are not in a slot, so the hoisting moves them.
		static long RepositoryItem()
		{
			var repository = s_repository;
			var sum = 0;
			for (var at = 0; at < Count; at++)
			{
				sum += repository[at & Mask].Id + (repository.IsEmpty ? 1 : 0);
			}
			Sink.Add(sum);
			return Count;
		}

		// Get and Count implement an interface, so they stay where they are.
		static long RepositoryGet()
		{
			var repository = s_repository;
			var sum = 0;
			for (var at = 0; at < Count; at++)
			{
				sum += repository.Get(at & Mask).Id + repository.Count;
			}
			Sink.Add(sum);
			return Count;
		}

		static long UtilSafeGet()
		{
			var list = s_small;
			var fallback = s_items[0];
			var sum = 0;
			for (var at = 0; at < Count; at++)
			{
				sum += Util.SafeGet(list, at & 127, fallback).Id;
			}
			Sink.Add(sum);
			return Count;
		}

		static long ErasedAdd()
		{
			var list = s_erased;
			list.Clear();
			for (var at = 0; at < Count; at++)
			{
				list.Add(s_items[at & Mask]);
			}
			return Count;
		}

		static long ErasedIndex()
		{
			var list = s_erased;
			var sum = 0;
			for (var at = 0; at < list.Count; at++)
			{
				sum += ((Pinner)list[at]).Id;
			}
			Sink.Add(sum);
			return list.Count;
		}

		static long SlimAdd()
		{
			var list = s_slim;
			list.Clear();
			for (var at = 0; at < Count; at++)
			{
				list.Add(s_items[at & Mask]);
			}
			return Count;
		}

		static long SlimIndex()
		{
			var list = s_slim;
			var sum = 0;
			for (var at = 0; at < list.Count(); at++)
			{
				sum += list.Get(at).Id;
			}
			Sink.Add(sum);
			return list.Count();
		}

		// An async method that never suspends: the builder is used, the runner is not.
		static long AsyncCompleted()
		{
			var sum = 0;
			for (var at = 0; at < 200000; at++)
			{
				sum += Completed(at).GetAwaiter().GetResult();
			}
			Sink.Add(sum);
			return 200000;
		}

		static async UniTask<int> Completed(int value)
		{
			await UniTask.CompletedTask;
			return value & 1;
		}

		/// <summary>Starts async methods that each suspend once. The caller times the start and waits for Sink.Pending.</summary>
		public static void StartSuspending(int count)
		{
			for (var at = 0; at < count; at++)
			{
				Suspending(at).Forget();
			}
		}

		static async UniTask Suspending(int value)
		{
			Sink.Begin();
			var got = await Yielding(value);
			Sink.Add(got);
			Sink.End();
		}

		static async UniTask<int> Yielding(int value)
		{
			await UniTask.Yield();
			return value & 1;
		}
	}
}
