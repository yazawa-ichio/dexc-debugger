using System;
using System.Collections.Generic;
using System.Linq;
using GenericMemory.Data;

namespace GenericMemory.Lib
{
	public interface IRepository<T>
	{
		int Count { get; }

		T Get(int id);

		void Add(int id, T item);

		bool Remove(int id);

		IEnumerable<T> All();
	}

	/// <summary>
	/// A generic class of the kind a game writes once and uses with every data type: a few members each user needs, and
	/// many that only some users call.
	/// </summary>
	public class Repository<T> : IRepository<T>
		where T : class, IEntity
	{
		readonly List<T> _items = new List<T>();
		readonly Dictionary<int, T> _byId = new Dictionary<int, T>();

		public event Action<T> Added;
		public event Action<T> Removed;

		public int Count => _items.Count;

		public bool IsEmpty => _items.Count == 0;

		public T this[int index] => _items[index];

		public T Get(int id)
		{
			return _byId[id];
		}

		public bool TryGet(int id, out T item)
		{
			return _byId.TryGetValue(id, out item);
		}

		public void Add(int id, T item)
		{
			_items.Add(item);
			_byId[id] = item;
			Added?.Invoke(item);
		}

		public void AddRange(IEnumerable<T> items)
		{
			foreach (var item in items)
			{
				Add(item.Id, item);
			}
		}

		public bool Remove(int id)
		{
			if (!_byId.TryGetValue(id, out var item))
			{
				return false;
			}
			_byId.Remove(id);
			_items.Remove(item);
			Removed?.Invoke(item);
			return true;
		}

		public int RemoveAll(Predicate<T> match)
		{
			var removed = _items.FindAll(match);
			foreach (var item in removed)
			{
				Remove(item.Id);
			}
			return removed.Count;
		}

		public void Clear()
		{
			_items.Clear();
			_byId.Clear();
		}

		public IEnumerable<T> All()
		{
			return _items;
		}

		public bool Contains(T item)
		{
			return _items.Contains(item);
		}

		public bool ContainsId(int id)
		{
			return _byId.ContainsKey(id);
		}

		public int IndexOf(T item)
		{
			return _items.IndexOf(item);
		}

		public T Find(Predicate<T> match)
		{
			return _items.Find(match);
		}

		public List<T> FindAll(Predicate<T> match)
		{
			return _items.FindAll(match);
		}

		public List<T> Where(Func<T, bool> match)
		{
			return _items.Where(match).ToList();
		}

		public List<TResult> Select<TResult>(Func<T, TResult> selector)
		{
			return _items.Select(selector).ToList();
		}

		public bool Any(Func<T, bool> match)
		{
			return _items.Any(match);
		}

		public int CountOf(Func<T, bool> match)
		{
			return _items.Count(match);
		}

		public T First()
		{
			return _items.Count > 0 ? _items[0] : null;
		}

		public T Last()
		{
			return _items.Count > 0 ? _items[_items.Count - 1] : null;
		}

		public T FirstOrDefault(Func<T, bool> match)
		{
			return _items.FirstOrDefault(match);
		}

		public T[] ToArray()
		{
			return _items.ToArray();
		}

		public Dictionary<TKey, T> ToDictionary<TKey>(Func<T, TKey> key)
		{
			return _items.ToDictionary(key);
		}

		public void Sort(Comparison<T> comparison)
		{
			_items.Sort(comparison);
		}

		public List<T> OrderedBy<TKey>(Func<T, TKey> key)
		{
			return _items.OrderBy(key).ToList();
		}

		public void ForEach(Action<T> action)
		{
			foreach (var item in _items)
			{
				action(item);
			}
		}

		public void Swap(int a, int b)
		{
			var held = _items[a];
			_items[a] = _items[b];
			_items[b] = held;
		}

		public void Reverse()
		{
			_items.Reverse();
		}

		public T MaxId()
		{
			T best = null;
			foreach (var item in _items)
			{
				if (best == null || item.Id > best.Id)
				{
					best = item;
				}
			}
			return best;
		}

		public int SumIds()
		{
			var sum = 0;
			foreach (var item in _items)
			{
				sum += item.Id;
			}
			return sum;
		}

		public Queue<T> ToQueue()
		{
			return new Queue<T>(_items);
		}

		public HashSet<T> ToSet()
		{
			return new HashSet<T>(_items);
		}
	}

	public static class Util
	{
		public static T FindById<T>(List<T> list, int id)
			where T : class, IEntity
		{
			foreach (var item in list)
			{
				if (item.Id == id)
				{
					return item;
				}
			}
			return null;
		}

		public static void Swap<T>(List<T> list, int a, int b)
		{
			var held = list[a];
			list[a] = list[b];
			list[b] = held;
		}

		public static T SafeGet<T>(List<T> list, int index, T fallback)
		{
			return index >= 0 && index < list.Count ? list[index] : fallback;
		}

		public static int CountValid<T>(List<T> list)
			where T : class, IEntity
		{
			var count = 0;
			for (var at = 0; at < list.Count; at++)
			{
				if (list[at] != null && list[at].Id > 0)
				{
					count++;
				}
			}
			return count;
		}
	}
}
