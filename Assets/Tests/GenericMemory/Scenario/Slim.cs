using System;

namespace GenericMemory.Lib
{
	/// <summary>
	/// A list made to cost the runtime as little as a generic class can: no interface, so no slot to fill for each type;
	/// no method of its own but the constructor, so nothing to inflate when it is initialized; and its items in an
	/// object[], so no array class for each element type. What it can do lives in static generic methods, which the
	/// runtime makes only for the types that call them.
	/// </summary>
	public sealed class SlimList<T>
		where T : class
	{
		internal object[] Items = SlimListOps.Empty;
		internal int Size;
	}

	public static class SlimListOps
	{
		internal static readonly object[] Empty = new object[0];

		public static int Count<T>(this SlimList<T> list)
			where T : class
		{
			return list.Size;
		}

		public static void Add<T>(this SlimList<T> list, T item)
			where T : class
		{
			if (list.Size == list.Items.Length)
			{
				Array.Resize(ref list.Items, list.Size == 0 ? 4 : list.Size * 2);
			}
			list.Items[list.Size++] = item;
		}

		public static T Get<T>(this SlimList<T> list, int index)
			where T : class
		{
			if ((uint)index >= (uint)list.Size)
			{
				throw new ArgumentOutOfRangeException(nameof(index));
			}
			return (T)list.Items[index];
		}

		public static bool Contains<T>(this SlimList<T> list, T item)
			where T : class
		{
			return Array.IndexOf(list.Items, item, 0, list.Size) >= 0;
		}

		public static void RemoveAt<T>(this SlimList<T> list, int index)
			where T : class
		{
			if ((uint)index >= (uint)list.Size)
			{
				throw new ArgumentOutOfRangeException(nameof(index));
			}
			list.Size--;
			Array.Copy(list.Items, index + 1, list.Items, index, list.Size - index);
			list.Items[list.Size] = null;
		}

		public static void Clear<T>(this SlimList<T> list)
			where T : class
		{
			Array.Clear(list.Items, 0, list.Size);
			list.Size = 0;
		}
	}
}
