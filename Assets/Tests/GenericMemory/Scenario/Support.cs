using System.Runtime.CompilerServices;

namespace GenericMemory.Data
{
	public interface IEntity
	{
		int Id { get; }
	}

	/// <summary>The type the whole API is used with once, so that the linker keeps what a large project would keep.</summary>
	public sealed class Pinner : IEntity
	{
		public int Id { get; }
		public string Name;

		public Pinner(int id)
		{
			Id = id;
			Name = "p" + id;
		}
	}
}

namespace GenericMemory
{
	/// <summary>Takes what the patterns compute, so that the compiler cannot drop the work.</summary>
	public static class Sink
	{
		static long s_sum;
		static object s_kept;
		static int s_pending;

		public static long Sum => s_sum;

		public static int Pending => s_pending;

		[MethodImpl(MethodImplOptions.NoInlining)]
		public static void Add(int value)
		{
			s_sum += value;
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		public static void Keep(object value)
		{
			s_kept = value;
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		public static void Begin()
		{
			s_pending++;
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		public static void End()
		{
			s_pending--;
		}
	}
}
