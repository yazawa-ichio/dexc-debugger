using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using GenericMemory.Data;

namespace GenericMemory
{
	/// <summary>
	/// What a call through an interface costs once its slots are there. Each kernel is one kind of call in a tight loop,
	/// run several times with the best run kept; the receivers sit in static fields of the interface type, so the
	/// generated code has to look the interface up on every call.
	/// </summary>
	public static class InterfaceBench
	{
		const int Count = 1000000;
		const int Mask = 1023;

		interface IMeter
		{
			int Read();
		}

		abstract class Meter<T> : IMeter
		{
			public abstract int Read();
		}

		sealed class FixedMeter<T> : Meter<T>
		{
			public int Reading;

			public override int Read()
			{
				return Reading;
			}
		}

		static IList<Pinner> s_list;
		static IReadOnlyList<Pinner> s_readOnly;
		static IReadOnlyList<IEntity> s_variant;
		static IEnumerable<Pinner> s_enumerable;
		static IList<Pinner> s_array;
		static IReadOnlyList<IEntity> s_arrayVariant;
		static IDictionary<int, Pinner> s_map;
		static object s_object;
		static object s_arrayObject;
		static Meter<Pinner> s_meter;
		static IMeter s_meterInterface;

		public static void Run(StringBuilder text)
		{
			var items = new Pinner[Mask + 1];
			var list = new List<Pinner>();
			var map = new Dictionary<int, Pinner>();
			for (var at = 0; at <= Mask; at++)
			{
				items[at] = new Pinner(at);
				list.Add(items[at]);
				map[at] = items[at];
			}
			s_list = list;
			s_readOnly = list;
			s_variant = list;
			s_enumerable = list;
			s_array = items;
			s_arrayVariant = items;
			s_map = map;
			s_object = list;
			s_arrayObject = items;
			s_meter = new FixedMeter<Pinner> { Reading = 3 };
			s_meterInterface = s_meter;
			Report(text, "itf.list.count", ListCount);
			Report(text, "itf.list.index", ListIndex);
			Report(text, "itf.list.readonlyIndex", ReadOnlyIndex);
			Report(text, "itf.list.variantIndex", VariantIndex);
			Report(text, "itf.list.enumerate", Enumerate);
			Report(text, "itf.array.index", ArrayIndex);
			Report(text, "itf.array.variantIndex", ArrayVariantIndex);
			Report(text, "itf.dict.tryget", MapTryGet);
			Report(text, "itf.user.call", MeterInterface);
			Report(text, "virtual.user.call", MeterVirtual);
			Report(text, "cast.is.exact", IsExact);
			Report(text, "cast.is.variant", IsVariant);
			Report(text, "cast.is.arrayVariant", IsArrayVariant);
			Report(text, "cast.is.miss", IsMiss);
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

		static long ListCount()
		{
			var sum = 0;
			for (var at = 0; at < Count; at++)
			{
				sum += s_list.Count;
			}
			Sink.Add(sum);
			return Count;
		}

		static long ListIndex()
		{
			var sum = 0;
			for (var at = 0; at < Count; at++)
			{
				sum += s_list[at & Mask].Id;
			}
			Sink.Add(sum);
			return Count;
		}

		static long ReadOnlyIndex()
		{
			var sum = 0;
			for (var at = 0; at < Count; at++)
			{
				sum += s_readOnly[at & Mask].Id;
			}
			Sink.Add(sum);
			return Count;
		}

		static long VariantIndex()
		{
			var sum = 0;
			for (var at = 0; at < Count; at++)
			{
				sum += s_variant[at & Mask].Id;
			}
			Sink.Add(sum);
			return Count;
		}

		static long Enumerate()
		{
			var sum = 0;
			long calls = 0;
			for (var round = 0; round < Count / (Mask + 1); round++)
			{
				foreach (var item in s_enumerable)
				{
					sum += item.Id;
					calls++;
				}
			}
			Sink.Add(sum);
			return calls;
		}

		static long ArrayIndex()
		{
			var sum = 0;
			for (var at = 0; at < Count; at++)
			{
				sum += s_array[at & Mask].Id;
			}
			Sink.Add(sum);
			return Count;
		}

		static long ArrayVariantIndex()
		{
			var sum = 0;
			for (var at = 0; at < Count; at++)
			{
				sum += s_arrayVariant[at & Mask].Id;
			}
			Sink.Add(sum);
			return Count;
		}

		static long MapTryGet()
		{
			var sum = 0;
			for (var at = 0; at < Count; at++)
			{
				Pinner found;
				if (s_map.TryGetValue(at & Mask, out found))
				{
					sum += found.Id;
				}
			}
			Sink.Add(sum);
			return Count;
		}

		static long MeterInterface()
		{
			var sum = 0;
			for (var at = 0; at < Count; at++)
			{
				sum += s_meterInterface.Read();
			}
			Sink.Add(sum);
			return Count;
		}

		static long MeterVirtual()
		{
			var sum = 0;
			for (var at = 0; at < Count; at++)
			{
				sum += s_meter.Read();
			}
			Sink.Add(sum);
			return Count;
		}

		static long IsExact()
		{
			var sum = 0;
			for (var at = 0; at < Count; at++)
			{
				if (s_object is IReadOnlyList<Pinner>)
				{
					sum++;
				}
			}
			Sink.Add(sum);
			return Count;
		}

		static long IsVariant()
		{
			var sum = 0;
			for (var at = 0; at < Count; at++)
			{
				if (s_object is IReadOnlyList<IEntity>)
				{
					sum++;
				}
			}
			Sink.Add(sum);
			return Count;
		}

		static long IsArrayVariant()
		{
			var sum = 0;
			for (var at = 0; at < Count; at++)
			{
				if (s_arrayObject is IList<IEntity>)
				{
					sum++;
				}
			}
			Sink.Add(sum);
			return Count;
		}

		static long IsMiss()
		{
			var sum = 0;
			for (var at = 0; at < Count; at++)
			{
				if (s_object is IDictionary<int, Pinner>)
				{
					sum++;
				}
			}
			Sink.Add(sum);
			return Count;
		}
	}
}
