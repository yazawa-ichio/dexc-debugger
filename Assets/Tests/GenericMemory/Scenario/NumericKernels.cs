using System.Runtime.CompilerServices;
using Unity.Mathematics;
using UnityEngine;

namespace GenericMemory
{
	/// <summary>
	/// The kernels of <see cref="NumericBench"/> as plain C#: what IL2CPP makes C++ of, with its null checks and its
	/// array bounds checks. NumericKernelsUnchecked.cs is this file with the checks turned off, written by
	/// tools/il2cpp-generic-memory/e-numeric-unchecked.py: change a kernel here and run that script.
	/// </summary>
	public static class NumericKernels
	{
		[MethodImpl(MethodImplOptions.NoInlining)]
		public static float FloatSum(float[] a)
		{
			var sum = 0f;
			for (var at = 0; at < a.Length; at++)
			{
				sum += a[at];
			}
			return sum;
		}

		// Four sums side by side: the reordering a compiler may not do to floats by itself, written by hand.
		[MethodImpl(MethodImplOptions.NoInlining)]
		public static float FloatSum4(float[] a)
		{
			var s0 = 0f;
			var s1 = 0f;
			var s2 = 0f;
			var s3 = 0f;
			var at = 0;
			for (; at + 3 < a.Length; at += 4)
			{
				s0 += a[at];
				s1 += a[at + 1];
				s2 += a[at + 2];
				s3 += a[at + 3];
			}
			for (; at < a.Length; at++)
			{
				s0 += a[at];
			}
			return (s0 + s1) + (s2 + s3);
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		public static float FloatDot(float[] a, float[] b)
		{
			var sum = 0f;
			for (var at = 0; at < a.Length; at++)
			{
				sum += a[at] * b[at];
			}
			return sum;
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		public static void Saxpy(float[] a, float[] b, float s, float[] c)
		{
			for (var at = 0; at < c.Length; at++)
			{
				c[at] = a[at] * s + b[at];
			}
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		public static int IntSum(int[] a)
		{
			var sum = 0;
			for (var at = 0; at < a.Length; at++)
			{
				sum += a[at];
			}
			return sum;
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		public static long IntMinMax(int[] a)
		{
			var min = int.MaxValue;
			var max = int.MinValue;
			for (var at = 0; at < a.Length; at++)
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

		[MethodImpl(MethodImplOptions.NoInlining)]
		public static int IndexOf(int[] a, int value)
		{
			for (var at = 0; at < a.Length; at++)
			{
				if (a[at] == value)
				{
					return at;
				}
			}
			return -1;
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		public static int IndexOfChar(string s, char c)
		{
			for (var at = 0; at < s.Length; at++)
			{
				if (s[at] == c)
				{
					return at;
				}
			}
			return -1;
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		public static int BytesEqual(byte[] a, byte[] b)
		{
			if (a.Length != b.Length)
			{
				return 0;
			}
			for (var at = 0; at < a.Length; at++)
			{
				if (a[at] != b[at])
				{
					return 0;
				}
			}
			return 1;
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		public static void Copy(int[] a, int[] b)
		{
			for (var at = 0; at < a.Length; at++)
			{
				b[at] = a[at];
			}
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		public static void Transform(Vector3[] source, Vector3[] target, float scale, Vector3 offset)
		{
			for (var at = 0; at < source.Length; at++)
			{
				target[at] = source[at] * scale + offset;
			}
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		public static void TransformMath(float3[] source, float3[] target, float scale, float3 offset)
		{
			for (var at = 0; at < source.Length; at++)
			{
				target[at] = source[at] * scale + offset;
			}
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		public static void Multiply(Matrix4x4 matrix, Vector3[] source, Vector3[] target)
		{
			for (var at = 0; at < source.Length; at++)
			{
				target[at] = matrix.MultiplyPoint3x4(source[at]);
			}
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		public static void MultiplyMath(float4x4 matrix, float3[] source, float3[] target)
		{
			for (var at = 0; at < source.Length; at++)
			{
				target[at] = math.transform(matrix, source[at]);
			}
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		public static void Sqrt(float[] a, float[] target)
		{
			for (var at = 0; at < a.Length; at++)
			{
				target[at] = Mathf.Sqrt(a[at]);
			}
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		public static void SqrtMath(float[] a, float[] target)
		{
			for (var at = 0; at < a.Length; at++)
			{
				target[at] = math.sqrt(a[at]);
			}
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		public static void Sin(float[] a, float[] target)
		{
			for (var at = 0; at < a.Length; at++)
			{
				target[at] = Mathf.Sin(a[at]);
			}
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		public static void SinMath(float[] a, float[] target)
		{
			for (var at = 0; at < a.Length; at++)
			{
				target[at] = math.sin(a[at]);
			}
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		public static int DivMod(int[] a, int divisor)
		{
			var sum = 0;
			for (var at = 0; at < a.Length; at++)
			{
				sum += a[at] / divisor + a[at] % divisor;
			}
			return sum;
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		public static int AddOne(int value)
		{
			return value + 1;
		}
	}
}
