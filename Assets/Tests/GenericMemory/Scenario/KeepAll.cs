using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using GenericMemory.Data;
using GenericMemory.Lib;

namespace GenericMemory
{
	/// <summary>
	/// Uses the generic API widely with one type. A large project calls nearly every member of List, Dictionary, LINQ and
	/// UniTask somewhere, so the linker keeps them all, and every instance then pays for them. The patterns alone call
	/// few members, which would let the linker make the baseline lighter than a real project's.
	/// </summary>
	public static class KeepAll
	{
		public static void Run()
		{
			Lists();
			Maps();
			Sets();
			Queries();
			Repositories();
			Tasks().Forget();
		}

		static void Lists()
		{
			var list = new List<Pinner>(8) { new Pinner(1), new Pinner(2) };
			var other = new List<Pinner>(list);
			list.AddRange(other);
			var readOnly = list.AsReadOnly();
			var comparer = Comparer<Pinner>.Create((a, b) => a.Id - b.Id);
			Sink.Add(list.BinarySearch(list[0], comparer) + list.Capacity + readOnly.Count);
			list.Capacity = 32;
			var ids = list.ConvertAll(item => item.Id);
			var copy = new Pinner[list.Count + 4];
			list.CopyTo(copy);
			list.CopyTo(copy, 1);
			list.CopyTo(0, copy, 0, 1);
			Sink.Add(list.Exists(item => item.Id == 1) ? ids.Count : 0);
			Sink.Keep(list.Find(item => item.Id == 2));
			Sink.Keep(list.FindAll(item => item.Id > 0));
			Sink.Add(list.FindIndex(item => item.Id == 2) + list.FindIndex(1, item => item.Id == 2) + list.FindIndex(0, 1, item => item.Id == 1));
			Sink.Keep(list.FindLast(item => item.Id == 1));
			Sink.Add(list.FindLastIndex(item => item.Id == 1));
			list.ForEach(item => Sink.Add(item.Id));
			Sink.Keep(list.GetRange(0, 1));
			Sink.Add(list.IndexOf(list[1]) + list.IndexOf(list[1], 0) + list.LastIndexOf(list[0]));
			list.Insert(0, new Pinner(3));
			list.InsertRange(0, other);
			list.Remove(list[0]);
			Sink.Add(list.RemoveAll(item => item.Id == 3));
			list.RemoveAt(0);
			list.RemoveRange(0, 1);
			list.Reverse();
			list.Reverse(0, 1);
			list.Sort(comparer);
			list.Sort((a, b) => b.Id - a.Id);
			list.Sort(0, 1, comparer);
			Sink.Keep(list.ToArray());
			list.TrimExcess();
			Sink.Add(list.TrueForAll(item => item.Id > 0) ? 1 : 0);
			IList<Pinner> asList = list;
			ICollection<Pinner> asCollection = list;
			IReadOnlyList<Pinner> asReadOnly = list;
			IEnumerable<Pinner> asEnumerable = list;
			Sink.Add(asList.Count + asCollection.Count + asReadOnly.Count + asEnumerable.Count());
			foreach (var item in list)
			{
				Sink.Add(item.Id);
			}
			list.Clear();

			var queue = new Queue<Pinner>();
			queue.Enqueue(new Pinner(1));
			Sink.Keep(queue.Peek());
			Sink.Keep(queue.Dequeue());
			var stack = new Stack<Pinner>();
			stack.Push(new Pinner(1));
			Sink.Keep(stack.Peek());
			Sink.Keep(stack.Pop());
			var linked = new LinkedList<Pinner>();
			linked.AddLast(new Pinner(1));
			Sink.Keep(linked.First);
		}

		static void Maps()
		{
			var map = new Dictionary<int, Pinner>(8) { { 1, new Pinner(1) } };
			var copy = new Dictionary<int, Pinner>(map);
			map[2] = new Pinner(2);
			Sink.Add(map.Count + copy.Count + (map.ContainsKey(1) ? 1 : 0) + (map.ContainsValue(map[1]) ? 1 : 0));
			Sink.Add(map.TryAdd(3, new Pinner(3)) ? 1 : 0);
			if (map.TryGetValue(2, out var value))
			{
				Sink.Add(value.Id);
			}
			foreach (var key in map.Keys)
			{
				Sink.Add(key);
			}
			foreach (var item in map.Values)
			{
				Sink.Add(item.Id);
			}
			foreach (var pair in map)
			{
				Sink.Add(pair.Key + pair.Value.Id);
			}
			Sink.Keep(map.Comparer);
			Sink.Keep(map.Keys.ToList());
			Sink.Keep(map.Values.ToArray());
			map.Remove(1);
			map.Remove(2, out var removed);
			Sink.Keep(removed);
			IDictionary<int, Pinner> asMap = map;
			IReadOnlyDictionary<int, Pinner> asReadOnly = map;
			Sink.Add(asMap.Count + asReadOnly.Count);
			map.Clear();

			var byName = new Dictionary<string, Pinner> { { "a", new Pinner(1) } };
			Sink.Add(byName.Count);
			var sorted = new SortedDictionary<int, Pinner> { { 1, new Pinner(1) } };
			Sink.Add(sorted.Count);
		}

		static void Sets()
		{
			var first = new Pinner(1);
			var set = new HashSet<Pinner> { first, new Pinner(2) };
			var other = new HashSet<Pinner>(set);
			set.UnionWith(other);
			set.IntersectWith(other);
			set.ExceptWith(new[] { new Pinner(9) });
			Sink.Add((set.Overlaps(other) ? 1 : 0) + (set.SetEquals(other) ? 1 : 0) + (set.IsSubsetOf(other) ? 1 : 0) + set.Count);
			Sink.Add(set.RemoveWhere(item => item.Id == 2));
			var copy = new Pinner[4];
			set.CopyTo(copy);
			Sink.Add(set.TryGetValue(first, out var actual) ? actual.Id : 0);
			set.Remove(first);
			set.Clear();
		}

		static void Queries()
		{
			var list = new List<Pinner> { new Pinner(3), new Pinner(1), new Pinner(2) };
			var array = list.ToArray();
			Sink.Keep(list.Where(item => item.Id > 0).ToList());
			Sink.Keep(list.Where((item, index) => index > 0).ToArray());
			Sink.Keep(array.Where(item => item.Id > 0).Select(item => item.Name).ToList());
			Sink.Keep(list.Select(item => item.Id).ToArray());
			Sink.Keep(list.Select((item, index) => item.Id + index).ToList());
			Sink.Keep(list.SelectMany(item => new[] { item, item }).ToList());
			Sink.Keep(list.OrderBy(item => item.Id).ThenBy(item => item.Name).ThenByDescending(item => item.Id).ToList());
			Sink.Keep(list.OrderByDescending(item => item.Id).ToArray());
			Sink.Keep(list.GroupBy(item => item.Id).ToDictionary(group => group.Key, group => group.ToList()));
			Sink.Keep(list.GroupBy(item => item.Id, item => item.Name).ToList());
			Sink.Keep(list.ToDictionary(item => item.Id));
			Sink.Keep(list.ToDictionary(item => item.Id, item => item.Name));
			Sink.Keep(list.ToLookup(item => item.Id));
			Sink.Keep(new HashSet<Pinner>(list.Distinct()));
			Sink.Keep(list.Concat(array).Union(array).Intersect(array).Except(new Pinner[0]).ToList());
			Sink.Keep(list.Skip(1).Take(1).SkipWhile(item => item.Id < 0).TakeWhile(item => item.Id > 0).ToList());
			Sink.Keep(list.Reverse<Pinner>().ToList());
			Sink.Keep(list.Cast<IEntity>().OfType<Pinner>().ToList());
			Sink.Keep(list.DefaultIfEmpty().ToList());
			Sink.Keep(list.Zip(array, (a, b) => a.Id + b.Id).ToList());
			Sink.Keep(list.Join(array, a => a.Id, b => b.Id, (a, b) => a).ToList());
			Sink.Keep(list.Append(new Pinner(5)).Prepend(new Pinner(6)).ToList());
			Sink.Keep(list.First());
			Sink.Keep(list.First(item => item.Id > 0));
			Sink.Keep(list.FirstOrDefault());
			Sink.Keep(list.FirstOrDefault(item => item.Id > 0));
			Sink.Keep(list.Last());
			Sink.Keep(list.Last(item => item.Id > 0));
			Sink.Keep(list.LastOrDefault());
			Sink.Keep(list.LastOrDefault(item => item.Id > 0));
			Sink.Keep(list.Take(1).Single());
			Sink.Keep(list.Take(1).SingleOrDefault());
			Sink.Keep(list.SingleOrDefault(item => item.Id == 1));
			Sink.Keep(list.ElementAt(0));
			Sink.Keep(list.ElementAtOrDefault(9));
			Sink.Keep(list.Aggregate((a, b) => a.Id > b.Id ? a : b));
			Sink.Add(list.Aggregate(0, (sum, item) => sum + item.Id));
			Sink.Add(list.Count() + list.Count(item => item.Id > 1) + (list.Any() ? 1 : 0) + (list.Any(item => item.Id > 1) ? 1 : 0));
			Sink.Add((list.All(item => item.Id > 0) ? 1 : 0) + (list.Contains(list[0]) ? 1 : 0) + (list.SequenceEqual(array) ? 1 : 0));
			Sink.Add(list.Sum(item => item.Id) + list.Max(item => item.Id) + list.Min(item => item.Id) + (int)list.Average(item => item.Id));
			Sink.Add((int)list.Sum(item => (float)item.Id) + (int)list.Max(item => (float)item.Id) + (int)list.LongCount());
		}

		static void Repositories()
		{
			var repository = new Repository<Pinner>();
			repository.Added += item => Sink.Add(item.Id);
			repository.Removed += item => Sink.Add(item.Id);
			repository.Add(1, new Pinner(1));
			repository.AddRange(new[] { new Pinner(2), new Pinner(3) });
			Sink.Add(repository.Count + (repository.IsEmpty ? 1 : 0) + repository[0].Id + repository.Get(1).Id);
			Sink.Add(repository.TryGet(2, out var got) ? got.Id : 0);
			Sink.Add((repository.Contains(got) ? 1 : 0) + (repository.ContainsId(1) ? 1 : 0) + repository.IndexOf(got));
			Sink.Keep(repository.Find(item => item.Id == 1));
			Sink.Keep(repository.FindAll(item => item.Id > 0));
			Sink.Keep(repository.Where(item => item.Id > 0));
			Sink.Keep(repository.Select(item => item.Name));
			Sink.Add((repository.Any(item => item.Id > 0) ? 1 : 0) + repository.CountOf(item => item.Id > 0));
			Sink.Keep(repository.First());
			Sink.Keep(repository.Last());
			Sink.Keep(repository.FirstOrDefault(item => item.Id == 3));
			Sink.Keep(repository.ToArray());
			Sink.Keep(repository.ToDictionary(item => item.Name));
			repository.Sort((a, b) => a.Id - b.Id);
			Sink.Keep(repository.OrderedBy(item => item.Name));
			repository.ForEach(item => Sink.Add(item.Id));
			repository.Swap(0, 1);
			repository.Reverse();
			Sink.Keep(repository.MaxId());
			Sink.Add(repository.SumIds());
			Sink.Keep(repository.ToQueue());
			Sink.Keep(repository.ToSet());
			Sink.Keep(repository.All());
			Sink.Add(repository.RemoveAll(item => item.Id == 3));
			repository.Remove(1);
			repository.Clear();
			IRepository<Pinner> asInterface = repository;
			Sink.Add(asInterface.Count);
			Sink.Keep(Util.FindById(new List<Pinner>(), 1));
		}

		static async UniTask Tasks()
		{
			// Counted as pending, so that the Player waits for the last await before it writes its result.
			Sink.Begin();
			var source = new UniTaskCompletionSource<Pinner>();
			source.TrySetResult(new Pinner(1));
			var one = await source.Task;
			var (a, b) = await UniTask.WhenAll(Make(1), Make(2));
			var many = await UniTask.WhenAll(new[] { Make(3), Make(4) });
			var (index, first, second) = await UniTask.WhenAny(Make(5), Make(6));
			Sink.Add(one.Id + a.Id + b.Id + many.Length + index + (first ?? second).Id);
			var lazy = UniTask.Lazy(() => Make(7));
			Sink.Keep(await lazy);
			Sink.Keep(await Make(8).AsTask());
			Sink.Keep(await UniTask.FromResult(new Pinner(9)));
			Sink.Keep(await Make(10).Preserve());
			Sink.Keep(await Make(11).AttachExternalCancellation(CancellationToken.None));
			Sink.Keep(await Make(12).Timeout(TimeSpan.FromSeconds(1)));
			await Make(13).ContinueWith(item => Sink.Add(item.Id));
			Sink.Keep(await Make(14).SuppressCancellationThrow());
			await UniTask.Delay(1);
			await UniTask.NextFrame();
			await UniTask.WaitUntil(() => true);
			await UniTask.SwitchToMainThread();
			Make(15).Forget();
			// Reached only when every await above resumed.
			Sink.Add(100000);
			Sink.End();
		}

		static async UniTask<Pinner> Make(int id)
		{
			await UniTask.Yield();
			return new Pinner(id);
		}
	}
}
