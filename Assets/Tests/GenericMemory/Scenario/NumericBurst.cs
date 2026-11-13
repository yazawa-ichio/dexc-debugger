using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace GenericMemory
{
	/// <summary>
	/// The kernels of <see cref="NumericBench"/> for Burst: static methods over pointers, which a Player's build compiles
	/// ahead of time into its own library, with Burst's own options. C# calls them like any static method; Burst's IL
	/// post-processor makes the call go to the compiled code. The loops are written the plain way, so that what is
	/// measured is what Burst vectorizes by itself. A pointer is marked NoAlias where a C# caller would know as much.
	/// </summary>
	[BurstCompile]
	public static unsafe class NumericBurst
	{
		/// <summary>1 when the code that answers was compiled by Burst: Burst drops the call that says "managed".</summary>
		[BurstCompile]
		public static int IsBurst()
		{
			var managed = 0;
			Managed(ref managed);
			return managed == 0 ? 1 : 0;
		}

		[BurstDiscard]
		static void Managed(ref int managed)
		{
			managed = 1;
		}

		[BurstCompile]
		public static int AddOne(int value)
		{
			return value + 1;
		}

		[BurstCompile]
		public static float FloatSum([NoAlias] float* a, int count)
		{
			var sum = 0f;
			for (var at = 0; at < count; at++)
			{
				sum += a[at];
			}
			return sum;
		}

		// FloatMode.Fast lets Burst reorder the additions, which is what a vectorized sum of floats needs.
		[BurstCompile(FloatMode = FloatMode.Fast)]
		public static float FloatSumFast([NoAlias] float* a, int count)
		{
			var sum = 0f;
			for (var at = 0; at < count; at++)
			{
				sum += a[at];
			}
			return sum;
		}

		[BurstCompile]
		public static float FloatDot([NoAlias] float* a, [NoAlias] float* b, int count)
		{
			var sum = 0f;
			for (var at = 0; at < count; at++)
			{
				sum += a[at] * b[at];
			}
			return sum;
		}

		[BurstCompile(FloatMode = FloatMode.Fast)]
		public static float FloatDotFast([NoAlias] float* a, [NoAlias] float* b, int count)
		{
			var sum = 0f;
			for (var at = 0; at < count; at++)
			{
				sum += a[at] * b[at];
			}
			return sum;
		}

		[BurstCompile]
		public static void Saxpy([NoAlias] float* a, [NoAlias] float* b, float s, [NoAlias] float* c, int count)
		{
			for (var at = 0; at < count; at++)
			{
				c[at] = a[at] * s + b[at];
			}
		}

		[BurstCompile]
		public static int IntSum([NoAlias] int* a, int count)
		{
			var sum = 0;
			for (var at = 0; at < count; at++)
			{
				sum += a[at];
			}
			return sum;
		}

		[BurstCompile]
		public static long IntMinMax([NoAlias] int* a, int count)
		{
			var min = int.MaxValue;
			var max = int.MinValue;
			for (var at = 0; at < count; at++)
			{
				var value = a[at];
				if (value < min)
				{
					min = value;
				}
				if (value > max)
				{
					max = value;
				}
			}
			return ((long)max << 32) | (uint)min;
		}

		[BurstCompile]
		public static int IndexOf([NoAlias] int* a, int count, int value)
		{
			for (var at = 0; at < count; at++)
			{
				if (a[at] == value)
				{
					return at;
				}
			}
			return -1;
		}

		[BurstCompile]
		public static int IndexOfChar([NoAlias] ushort* s, int count, ushort c)
		{
			for (var at = 0; at < count; at++)
			{
				if (s[at] == c)
				{
					return at;
				}
			}
			return -1;
		}

		[BurstCompile]
		public static int BytesEqual([NoAlias] byte* a, [NoAlias] byte* b, int count)
		{
			return UnsafeUtility.MemCmp(a, b, count) == 0 ? 1 : 0;
		}

		[BurstCompile]
		public static void Copy([NoAlias] int* a, [NoAlias] int* b, int count)
		{
			UnsafeUtility.MemCpy(b, a, (long)count * sizeof(int));
		}

		[BurstCompile]
		public static void Transform([NoAlias] float3* source, [NoAlias] float3* target, int count, float scale, [NoAlias] float3* offset)
		{
			var by = *offset;
			for (var at = 0; at < count; at++)
			{
				target[at] = source[at] * scale + by;
			}
		}

		[BurstCompile]
		public static void Multiply([NoAlias] float4x4* matrix, [NoAlias] float3* source, [NoAlias] float3* target, int count)
		{
			var by = *matrix;
			for (var at = 0; at < count; at++)
			{
				target[at] = math.transform(by, source[at]);
			}
		}

		[BurstCompile]
		public static void Sqrt([NoAlias] float* a, [NoAlias] float* target, int count)
		{
			for (var at = 0; at < count; at++)
			{
				target[at] = math.sqrt(a[at]);
			}
		}

		[BurstCompile]
		public static void Sin([NoAlias] float* a, [NoAlias] float* target, int count)
		{
			for (var at = 0; at < count; at++)
			{
				target[at] = math.sin(a[at]);
			}
		}

		[BurstCompile]
		public static int DivMod([NoAlias] int* a, int count, int divisor)
		{
			var sum = 0;
			for (var at = 0; at < count; at++)
			{
				sum += a[at] / divisor + a[at] % divisor;
			}
			return sum;
		}
	}
}
