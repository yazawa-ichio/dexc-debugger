using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;

namespace GenericMemory
{
	/// <summary>
	/// What the same work over arrays of numbers costs, written several ways: plain C# (<see cref="NumericKernels"/>),
	/// the same C# without null and bounds checks (<see cref="NumericKernelsUnchecked"/>), Burst
	/// (<see cref="NumericBurst"/>), C++ compiled with the generated code (NumericNative.cpp, only in a build with the
	/// define GENERIC_MEMORY_NATIVE), and where there is one, the method of the class library.
	/// Every way of a kernel has to end with the same result before any of them is timed; a kernel whose ways differ
	/// is reported as such. The data is whole numbers small enough that sums and products of floats are exact in any
	/// order, so a vectorized sum and a fused multiply-add give the same bits as the plain loop. Only sin is compared
	/// with a tolerance: its implementations differ in the last bit.
	/// A line is "bench", kernel.size.way, nanoseconds for one element (for one call in the "call" kernels): the best
	/// of several passes. The sizes are 16 and 4096 elements, so that what a call costs shows against what it saves.
	/// A kernel that only reads would be taken out of the loop of calls by a compiler that sees it has no effects (the
	/// unchecked C# kernels were, at -O3: 0.000 ns), so the caller changes the first element of what the kernel reads
	/// before every call, the same way for every way of the kernel.
	/// With <see cref="Pointers"/> set, Burst is also called through a function pointer (the ways burstdelegate and
	/// burstptr): the direct call that the other Burst ways use goes through a stub that Burst's post-processor writes.
	/// </summary>
	public static unsafe class NumericBench
	{
		const int Elements = 1 << 21;
		const int Passes = 7;
		const float Tolerance = 2e-6f;

#if GENERIC_MEMORY_NATIVE
		static readonly bool s_native = true;

		[DllImport("__Internal")]
		static extern int gm_native_add_one(int value);

		[DllImport("__Internal")]
		static extern float gm_native_float_sum(float* a, int count);

		[DllImport("__Internal")]
		static extern float gm_native_float_sum_simd(float* a, int count);

		[DllImport("__Internal")]
		static extern float gm_native_float_dot(float* a, float* b, int count);

		[DllImport("__Internal")]
		static extern float gm_native_float_dot_simd(float* a, float* b, int count);

		[DllImport("__Internal")]
		static extern void gm_native_saxpy(float* a, float* b, float s, float* c, int count);

		[DllImport("__Internal")]
		static extern int gm_native_int_sum(int* a, int count);

		[DllImport("__Internal")]
		static extern long gm_native_int_min_max(int* a, int count);

		[DllImport("__Internal")]
		static extern int gm_native_index_of(int* a, int count, int value);

		[DllImport("__Internal")]
		static extern int gm_native_index_of_simd(int* a, int count, int value);

		[DllImport("__Internal")]
		static extern int gm_native_index_of_char(ushort* s, int count, ushort c);

		[DllImport("__Internal")]
		static extern int gm_native_bytes_equal(byte* a, byte* b, int count);

		[DllImport("__Internal")]
		static extern void gm_native_copy(int* a, int* b, int count);

		[DllImport("__Internal")]
		static extern void gm_native_transform(float3* source, float3* target, int count, float scale, float3* offset);

		[DllImport("__Internal")]
		static extern void gm_native_multiply(float* matrix, float3* source, float3* target, int count);

		[DllImport("__Internal")]
		static extern void gm_native_sqrt(float* a, float* target, int count);

		[DllImport("__Internal")]
		static extern void gm_native_sin(float* a, float* target, int count);

		[DllImport("__Internal")]
		static extern int gm_native_div_mod(int* a, int count, int divisor);
#else
		static readonly bool s_native = false;
#endif

		/// <summary>Whether the ways that call Burst through a function pointer are measured too (-generic-memory-numeric-pointers).</summary>
		public static bool Pointers;

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		delegate int AddOneFunction(int value);

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		delegate float FloatSumFunction(float* a, int count);

		static StringBuilder s_text;
		static string s_kernel;
		static int s_count;
		static int s_elements;
		static Action s_reset;
		static float[] s_approximate;
		static float[] s_reference;
		static bool s_first;
		static bool s_same;
		static long s_expected;
		static int s_differing;

		public static void Run(StringBuilder text)
		{
			s_text = text;
			var start = Stopwatch.GetTimestamp();
			// Whether the Burst column is Burst's code, and whether the C++ column is there at all.
			text.Append("burst\t").Append(NumericBurst.IsBurst() == 1 ? "true" : "false").Append('\n');
			text.Append("native\t").Append(s_native ? "true" : "false").Append('\n');
			text.Append("pointers\t").Append(Pointers ? "true" : "false").Append('\n');
			Calls();
			Sized(16);
			Sized(4096);
			End();
			text.Append("differing\t").Append(s_differing).Append('\n');
			text.Append("numericMs\t").Append(((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency).ToString("F0", CultureInfo.InvariantCulture)).Append('\n');
		}

		/// <summary>Starts a kernel: the ways that follow are compared with each other and timed over <paramref name="count"/> elements a call.</summary>
		static void Begin(string kernel, int count, int elements = Elements, Action reset = null, float[] approximate = null)
		{
			End();
			s_kernel = kernel;
			s_count = count;
			s_elements = elements;
			s_reset = reset;
			s_approximate = approximate;
			s_reference = null;
			s_first = true;
			s_same = true;
		}

		static void End()
		{
			if (s_kernel == null)
			{
				return;
			}
			s_text.Append("same\t").Append(s_kernel).Append('\t').Append(s_same ? "true" : "false").Append('\n');
			if (!s_same)
			{
				s_differing++;
			}
			s_kernel = null;
		}

		/// <summary>
		/// One way of the kernel. The body makes that many calls and returns what they computed: the sum of what they
		/// returned, or a checksum of what they wrote. One call is compared with the first way's; then it is timed.
		/// </summary>
		static void Time(string way, Func<int, long> body)
		{
			var name = s_kernel + "." + way;
			s_reset?.Invoke();
			var value = body(1);
			if (s_approximate != null)
			{
				if (s_reference == null)
				{
					s_reference = (float[])s_approximate.Clone();
				}
				else
				{
					var most = 0f;
					for (var at = 0; at < s_reference.Length; at++)
					{
						most = Math.Max(most, Math.Abs(s_reference[at] - s_approximate[at]));
					}
					s_text.Append("difference\t").Append(name).Append('\t').Append(most.ToString("G4", CultureInfo.InvariantCulture)).Append('\n');
					if (!(most <= Tolerance))
					{
						s_same = false;
					}
				}
			}
			else if (s_first)
			{
				s_expected = value;
			}
			else if (value != s_expected)
			{
				s_same = false;
				s_text.Append("differs\t").Append(name).Append('\t').Append(value).Append('\t').Append(s_expected).Append('\n');
			}
			s_first = false;

			var calls = Math.Max(1, s_elements / s_count);
			body(calls / 4 + 1);
			var best = double.MaxValue;
			long kept = 0;
			for (var pass = 0; pass < Passes; pass++)
			{
				var start = Stopwatch.GetTimestamp();
				kept += body(calls);
				var ticks = Stopwatch.GetTimestamp() - start;
				best = Math.Min(best, ticks * 1e9 / Stopwatch.Frequency / ((double)calls * s_count));
			}
			Sink.Add((int)kept);
			s_text.Append("bench\t").Append(name).Append('\t').Append(best.ToString("F4", CultureInfo.InvariantCulture)).Append('\n');
		}

		static long Checksum(float[] values)
		{
			long sum = 0;
			for (var at = 0; at < values.Length; at++)
			{
				sum += (long)BitConverter.SingleToInt32Bits(values[at]) * (at + 1);
			}
			return sum;
		}

		static long Checksum(int[] values)
		{
			long sum = 0;
			for (var at = 0; at < values.Length; at++)
			{
				sum += (long)values[at] * (at + 1);
			}
			return sum;
		}

		static long Checksum(Vector3[] values)
		{
			long sum = 0;
			for (var at = 0; at < values.Length; at++)
			{
				sum += ((long)BitConverter.SingleToInt32Bits(values[at].x) + 3L * BitConverter.SingleToInt32Bits(values[at].y) + 7L * BitConverter.SingleToInt32Bits(values[at].z)) * (at + 1);
			}
			return sum;
		}

		static long Checksum(float3[] values)
		{
			long sum = 0;
			for (var at = 0; at < values.Length; at++)
			{
				sum += ((long)BitConverter.SingleToInt32Bits(values[at].x) + 3L * BitConverter.SingleToInt32Bits(values[at].y) + 7L * BitConverter.SingleToInt32Bits(values[at].z)) * (at + 1);
			}
			return sum;
		}

		/// <summary>What a call costs when there is nothing to do: one that adds one, and a sum over no elements.</summary>
		static void Calls()
		{
			var none = new float[4];

			Begin("call.addone", 1);
			Time("cs", calls =>
			{
				var value = 0;
				for (var call = 0; call < calls; call++)
				{
					value = NumericKernels.AddOne(value);
				}
				return value;
			});
			Time("csnc", calls =>
			{
				var value = 0;
				for (var call = 0; call < calls; call++)
				{
					value = NumericKernelsUnchecked.AddOne(value);
				}
				return value;
			});
			Time("burst", calls =>
			{
				var value = 0;
				for (var call = 0; call < calls; call++)
				{
					value = NumericBurst.AddOne(value);
				}
				return value;
			});
#if GENERIC_MEMORY_NATIVE
			Time("cpp", calls =>
			{
				var value = 0;
				for (var call = 0; call < calls; call++)
				{
					value = gm_native_add_one(value);
				}
				return value;
			});
#endif

			if (Pointers)
			{
				var addOne = BurstCompiler.CompileFunctionPointer<AddOneFunction>(NumericBurst.AddOne);
				var addOneDelegate = addOne.Invoke;
				var addOnePointer = addOne.Value;
				Time("burstdelegate", calls =>
				{
					var value = 0;
					for (var call = 0; call < calls; call++)
					{
						value = addOneDelegate(value);
					}
					return value;
				});
				Time("burstptr", calls =>
				{
					var function = (delegate* unmanaged[Cdecl]<int, int>)addOnePointer;
					var value = 0;
					for (var call = 0; call < calls; call++)
					{
						value = function(value);
					}
					return value;
				});
			}

			Begin("call.sum0", 1);
			Time("cs", calls =>
			{
				var empty = Array.Empty<float>();
				long sum = 0;
				for (var call = 0; call < calls; call++)
				{
					sum += (long)NumericKernels.FloatSum(empty);
				}
				return sum;
			});
			Time("burst", calls =>
			{
				long sum = 0;
				fixed (float* a = none)
				{
					for (var call = 0; call < calls; call++)
					{
						a[0] = call & 1;
						sum += (long)NumericBurst.FloatSum(a, 0);
					}
				}
				return sum;
			});
#if GENERIC_MEMORY_NATIVE
			Time("cpp", calls =>
			{
				long sum = 0;
				fixed (float* a = none)
				{
					for (var call = 0; call < calls; call++)
					{
						a[0] = call & 1;
						sum += (long)gm_native_float_sum(a, 0);
					}
				}
				return sum;
			});
#endif
		}

		static void Sized(int n)
		{
			// Whole numbers from 0 to 15: every sum and product below stays under 2^24, where a float is exact.
			var fa = new float[n];
			var fb = new float[n];
			var fc = new float[n];
			var angles = new float[n];
			var ia = new int[n];
			var ib = new int[n];
			var ba = new byte[n];
			var bb = new byte[n];
			var va = new Vector3[n];
			var vb = new Vector3[n];
			var ma = new float3[n];
			var mb = new float3[n];
			for (var at = 0; at < n; at++)
			{
				fa[at] = (at * 7) & 15;
				fb[at] = (at * 5) & 7;
				// From 0 to 4 at both sizes, in steps a float holds exactly.
				angles[at] = at * (4f / n);
				// Every value is another one: the multiplier is odd.
				ia[at] = (int)((uint)at * 2654435761u);
				ba[at] = (byte)(at * 31);
				bb[at] = (byte)(at * 31);
				va[at] = new Vector3(at & 15, (at * 3) & 15, (at * 5) & 15);
				ma[at] = new float3(at & 15, (at * 3) & 15, (at * 5) & 15);
			}
			var last = ia[n - 1];
			var word = new string('a', n - 1) + "z";
			const float scale = 3f;
			var offset = new Vector3(1f, 2f, 3f);
			var offsetMath = new float3(1f, 2f, 3f);
			var matrix = Matrix4x4.identity;
			matrix.SetRow(0, new Vector4(1f, 2f, 0f, 3f));
			matrix.SetRow(1, new Vector4(0f, 1f, 2f, 1f));
			matrix.SetRow(2, new Vector4(2f, 0f, 1f, 2f));
			var matrixMath = new float4x4(
				new float4(matrix.m00, matrix.m10, matrix.m20, matrix.m30),
				new float4(matrix.m01, matrix.m11, matrix.m21, matrix.m31),
				new float4(matrix.m02, matrix.m12, matrix.m22, matrix.m32),
				new float4(matrix.m03, matrix.m13, matrix.m23, matrix.m33));
			// A local the kernels get at run time, so that no compiler sees the divisor as a constant.
			var by7 = 7;
			var size = "." + n.ToString(CultureInfo.InvariantCulture);

			// ---- the sum of floats
			Begin("fsum" + size, n);
			Time("cs", calls =>
			{
				long sum = 0;
				for (var call = 0; call < calls; call++)
				{
					fa[0] = call & 1;
					sum += (long)NumericKernels.FloatSum(fa);
				}
				return sum;
			});
			Time("cs4", calls =>
			{
				long sum = 0;
				for (var call = 0; call < calls; call++)
				{
					fa[0] = call & 1;
					sum += (long)NumericKernels.FloatSum4(fa);
				}
				return sum;
			});
			Time("csnc", calls =>
			{
				long sum = 0;
				for (var call = 0; call < calls; call++)
				{
					fa[0] = call & 1;
					sum += (long)NumericKernelsUnchecked.FloatSum(fa);
				}
				return sum;
			});
			Time("csnc4", calls =>
			{
				long sum = 0;
				for (var call = 0; call < calls; call++)
				{
					fa[0] = call & 1;
					sum += (long)NumericKernelsUnchecked.FloatSum4(fa);
				}
				return sum;
			});
			Time("burst", calls =>
			{
				long sum = 0;
				fixed (float* a = fa)
				{
					for (var call = 0; call < calls; call++)
					{
						a[0] = call & 1;
						sum += (long)NumericBurst.FloatSum(a, n);
					}
				}
				return sum;
			});
			Time("burstfast", calls =>
			{
				long sum = 0;
				fixed (float* a = fa)
				{
					for (var call = 0; call < calls; call++)
					{
						a[0] = call & 1;
						sum += (long)NumericBurst.FloatSumFast(a, n);
					}
				}
				return sum;
			});
#if GENERIC_MEMORY_NATIVE
			Time("cpp", calls =>
			{
				long sum = 0;
				fixed (float* a = fa)
				{
					for (var call = 0; call < calls; call++)
					{
						a[0] = call & 1;
						sum += (long)gm_native_float_sum(a, n);
					}
				}
				return sum;
			});
			Time("cppsimd", calls =>
			{
				long sum = 0;
				fixed (float* a = fa)
				{
					for (var call = 0; call < calls; call++)
					{
						a[0] = call & 1;
						sum += (long)gm_native_float_sum_simd(a, n);
					}
				}
				return sum;
			});
#endif

			if (Pointers)
			{
				var sumPointer = BurstCompiler.CompileFunctionPointer<FloatSumFunction>(NumericBurst.FloatSumFast).Value;
				Time("burstfastptr", calls =>
				{
					var function = (delegate* unmanaged[Cdecl]<float*, int, float>)sumPointer;
					long sum = 0;
					fixed (float* a = fa)
					{
						for (var call = 0; call < calls; call++)
						{
							a[0] = call & 1;
							sum += (long)function(a, n);
						}
					}
					return sum;
				});
			}

			// ---- the dot product of floats
			Begin("fdot" + size, n);
			Time("cs", calls =>
			{
				long sum = 0;
				for (var call = 0; call < calls; call++)
				{
					fa[0] = call & 1;
					sum += (long)NumericKernels.FloatDot(fa, fb);
				}
				return sum;
			});
			Time("csnc", calls =>
			{
				long sum = 0;
				for (var call = 0; call < calls; call++)
				{
					fa[0] = call & 1;
					sum += (long)NumericKernelsUnchecked.FloatDot(fa, fb);
				}
				return sum;
			});
			Time("burst", calls =>
			{
				long sum = 0;
				fixed (float* a = fa, b = fb)
				{
					for (var call = 0; call < calls; call++)
					{
						a[0] = call & 1;
						sum += (long)NumericBurst.FloatDot(a, b, n);
					}
				}
				return sum;
			});
			Time("burstfast", calls =>
			{
				long sum = 0;
				fixed (float* a = fa, b = fb)
				{
					for (var call = 0; call < calls; call++)
					{
						a[0] = call & 1;
						sum += (long)NumericBurst.FloatDotFast(a, b, n);
					}
				}
				return sum;
			});
#if GENERIC_MEMORY_NATIVE
			Time("cpp", calls =>
			{
				long sum = 0;
				fixed (float* a = fa, b = fb)
				{
					for (var call = 0; call < calls; call++)
					{
						a[0] = call & 1;
						sum += (long)gm_native_float_dot(a, b, n);
					}
				}
				return sum;
			});
			Time("cppsimd", calls =>
			{
				long sum = 0;
				fixed (float* a = fa, b = fb)
				{
					for (var call = 0; call < calls; call++)
					{
						a[0] = call & 1;
						sum += (long)gm_native_float_dot_simd(a, b, n);
					}
				}
				return sum;
			});
#endif

			// ---- c[i] = a[i] * s + b[i]
			Begin("saxpy" + size, n, reset: () => Array.Clear(fc, 0, n));
			Time("cs", calls =>
			{
				for (var call = 0; call < calls; call++)
				{
					NumericKernels.Saxpy(fa, fb, scale, fc);
				}
				return Checksum(fc);
			});
			Time("csnc", calls =>
			{
				for (var call = 0; call < calls; call++)
				{
					NumericKernelsUnchecked.Saxpy(fa, fb, scale, fc);
				}
				return Checksum(fc);
			});
			Time("burst", calls =>
			{
				fixed (float* a = fa, b = fb, c = fc)
				{
					for (var call = 0; call < calls; call++)
					{
						NumericBurst.Saxpy(a, b, scale, c, n);
					}
				}
				return Checksum(fc);
			});
#if GENERIC_MEMORY_NATIVE
			Time("cpp", calls =>
			{
				fixed (float* a = fa, b = fb, c = fc)
				{
					for (var call = 0; call < calls; call++)
					{
						gm_native_saxpy(a, b, scale, c, n);
					}
				}
				return Checksum(fc);
			});
#endif

			// ---- the sum of ints
			Begin("isum" + size, n);
			Time("cs", calls =>
			{
				long sum = 0;
				for (var call = 0; call < calls; call++)
				{
					ia[0] = call & 1;
					sum += NumericKernels.IntSum(ia);
				}
				return sum;
			});
			Time("csnc", calls =>
			{
				long sum = 0;
				for (var call = 0; call < calls; call++)
				{
					ia[0] = call & 1;
					sum += NumericKernelsUnchecked.IntSum(ia);
				}
				return sum;
			});
			Time("burst", calls =>
			{
				long sum = 0;
				fixed (int* a = ia)
				{
					for (var call = 0; call < calls; call++)
					{
						a[0] = call & 1;
						sum += NumericBurst.IntSum(a, n);
					}
				}
				return sum;
			});
#if GENERIC_MEMORY_NATIVE
			Time("cpp", calls =>
			{
				long sum = 0;
				fixed (int* a = ia)
				{
					for (var call = 0; call < calls; call++)
					{
						a[0] = call & 1;
						sum += gm_native_int_sum(a, n);
					}
				}
				return sum;
			});
#endif

			// ---- the smallest and the largest int
			Begin("iminmax" + size, n);
			Time("cs", calls =>
			{
				long sum = 0;
				for (var call = 0; call < calls; call++)
				{
					ia[0] = call & 1;
					sum += NumericKernels.IntMinMax(ia);
				}
				return sum;
			});
			Time("csnc", calls =>
			{
				long sum = 0;
				for (var call = 0; call < calls; call++)
				{
					ia[0] = call & 1;
					sum += NumericKernelsUnchecked.IntMinMax(ia);
				}
				return sum;
			});
			Time("burst", calls =>
			{
				long sum = 0;
				fixed (int* a = ia)
				{
					for (var call = 0; call < calls; call++)
					{
						a[0] = call & 1;
						sum += NumericBurst.IntMinMax(a, n);
					}
				}
				return sum;
			});
#if GENERIC_MEMORY_NATIVE
			Time("cpp", calls =>
			{
				long sum = 0;
				fixed (int* a = ia)
				{
					for (var call = 0; call < calls; call++)
					{
						a[0] = call & 1;
						sum += gm_native_int_min_max(a, n);
					}
				}
				return sum;
			});
#endif

			// ---- where an int is: it is the last one
			Begin("indexof" + size, n);
			Time("bcl", calls =>
			{
				long sum = 0;
				for (var call = 0; call < calls; call++)
				{
					ia[0] = call & 1;
					sum += Array.IndexOf(ia, last);
				}
				return sum;
			});
			Time("cs", calls =>
			{
				long sum = 0;
				for (var call = 0; call < calls; call++)
				{
					ia[0] = call & 1;
					sum += NumericKernels.IndexOf(ia, last);
				}
				return sum;
			});
			Time("csnc", calls =>
			{
				long sum = 0;
				for (var call = 0; call < calls; call++)
				{
					ia[0] = call & 1;
					sum += NumericKernelsUnchecked.IndexOf(ia, last);
				}
				return sum;
			});
			Time("burst", calls =>
			{
				long sum = 0;
				fixed (int* a = ia)
				{
					for (var call = 0; call < calls; call++)
					{
						a[0] = call & 1;
						sum += NumericBurst.IndexOf(a, n, last);
					}
				}
				return sum;
			});
#if GENERIC_MEMORY_NATIVE
			Time("cpp", calls =>
			{
				long sum = 0;
				fixed (int* a = ia)
				{
					for (var call = 0; call < calls; call++)
					{
						a[0] = call & 1;
						sum += gm_native_index_of(a, n, last);
					}
				}
				return sum;
			});
			Time("cppsimd", calls =>
			{
				long sum = 0;
				fixed (int* a = ia)
				{
					for (var call = 0; call < calls; call++)
					{
						a[0] = call & 1;
						sum += gm_native_index_of_simd(a, n, last);
					}
				}
				return sum;
			});
#endif

			// ---- where a char is in a string: it is the last one
			Begin("strchr" + size, n);
			Time("bcl", calls =>
			{
				long sum = 0;
				fixed (char* s = word)
				{
					for (var call = 0; call < calls; call++)
					{
						s[0] = (char)('a' + (call & 1));
						sum += word.IndexOf('z');
					}
				}
				return sum;
			});
			Time("cs", calls =>
			{
				long sum = 0;
				fixed (char* s = word)
				{
					for (var call = 0; call < calls; call++)
					{
						s[0] = (char)('a' + (call & 1));
						sum += NumericKernels.IndexOfChar(word, 'z');
					}
				}
				return sum;
			});
			Time("csnc", calls =>
			{
				long sum = 0;
				fixed (char* s = word)
				{
					for (var call = 0; call < calls; call++)
					{
						s[0] = (char)('a' + (call & 1));
						sum += NumericKernelsUnchecked.IndexOfChar(word, 'z');
					}
				}
				return sum;
			});
			Time("burst", calls =>
			{
				long sum = 0;
				fixed (char* s = word)
				{
					for (var call = 0; call < calls; call++)
					{
						s[0] = (char)('a' + (call & 1));
						sum += NumericBurst.IndexOfChar((ushort*)s, n, 'z');
					}
				}
				return sum;
			});
#if GENERIC_MEMORY_NATIVE
			Time("cpp", calls =>
			{
				long sum = 0;
				fixed (char* s = word)
				{
					for (var call = 0; call < calls; call++)
					{
						s[0] = (char)('a' + (call & 1));
						sum += gm_native_index_of_char((ushort*)s, n, 'z');
					}
				}
				return sum;
			});
#endif

			// ---- whether two runs of bytes are the same: they are
			Begin("bytes" + size, n);
			Time("bcl", calls =>
			{
				long sum = 0;
				for (var call = 0; call < calls; call++)
				{
					ba[0] = bb[0] = (byte)(call & 1);
					sum += ba.AsSpan().SequenceEqual(bb) ? 1 : 0;
				}
				return sum;
			});
			Time("unity", calls =>
			{
				long sum = 0;
				fixed (byte* a = ba, b = bb)
				{
					for (var call = 0; call < calls; call++)
					{
						a[0] = b[0] = (byte)(call & 1);
						sum += UnsafeUtility.MemCmp(a, b, n) == 0 ? 1 : 0;
					}
				}
				return sum;
			});
			Time("cs", calls =>
			{
				long sum = 0;
				for (var call = 0; call < calls; call++)
				{
					ba[0] = bb[0] = (byte)(call & 1);
					sum += NumericKernels.BytesEqual(ba, bb);
				}
				return sum;
			});
			Time("csnc", calls =>
			{
				long sum = 0;
				for (var call = 0; call < calls; call++)
				{
					ba[0] = bb[0] = (byte)(call & 1);
					sum += NumericKernelsUnchecked.BytesEqual(ba, bb);
				}
				return sum;
			});
			Time("burst", calls =>
			{
				long sum = 0;
				fixed (byte* a = ba, b = bb)
				{
					for (var call = 0; call < calls; call++)
					{
						a[0] = b[0] = (byte)(call & 1);
						sum += NumericBurst.BytesEqual(a, b, n);
					}
				}
				return sum;
			});
#if GENERIC_MEMORY_NATIVE
			Time("cpp", calls =>
			{
				long sum = 0;
				fixed (byte* a = ba, b = bb)
				{
					for (var call = 0; call < calls; call++)
					{
						a[0] = b[0] = (byte)(call & 1);
						sum += gm_native_bytes_equal(a, b, n);
					}
				}
				return sum;
			});
#endif

			// ---- a copy of ints
			Begin("copy" + size, n, reset: () => Array.Clear(ib, 0, n));
			Time("bcl", calls =>
			{
				for (var call = 0; call < calls; call++)
				{
					Array.Copy(ia, ib, n);
				}
				return Checksum(ib);
			});
			Time("blockcopy", calls =>
			{
				for (var call = 0; call < calls; call++)
				{
					Buffer.BlockCopy(ia, 0, ib, 0, n * sizeof(int));
				}
				return Checksum(ib);
			});
			Time("unity", calls =>
			{
				fixed (int* a = ia, b = ib)
				{
					for (var call = 0; call < calls; call++)
					{
						UnsafeUtility.MemCpy(b, a, (long)n * sizeof(int));
					}
				}
				return Checksum(ib);
			});
			Time("cs", calls =>
			{
				for (var call = 0; call < calls; call++)
				{
					NumericKernels.Copy(ia, ib);
				}
				return Checksum(ib);
			});
			Time("csnc", calls =>
			{
				for (var call = 0; call < calls; call++)
				{
					NumericKernelsUnchecked.Copy(ia, ib);
				}
				return Checksum(ib);
			});
			Time("burst", calls =>
			{
				fixed (int* a = ia, b = ib)
				{
					for (var call = 0; call < calls; call++)
					{
						NumericBurst.Copy(a, b, n);
					}
				}
				return Checksum(ib);
			});
#if GENERIC_MEMORY_NATIVE
			Time("cpp", calls =>
			{
				fixed (int* a = ia, b = ib)
				{
					for (var call = 0; call < calls; call++)
					{
						gm_native_copy(a, b, n);
					}
				}
				return Checksum(ib);
			});
#endif

			// ---- target[i] = source[i] * scale + offset, over vectors of three floats
			Begin("v3" + size, n, reset: () =>
			{
				Array.Clear(vb, 0, n);
				Array.Clear(mb, 0, n);
			});
			Time("cs", calls =>
			{
				for (var call = 0; call < calls; call++)
				{
					NumericKernels.Transform(va, vb, scale, offset);
				}
				return Checksum(vb);
			});
			Time("csnc", calls =>
			{
				for (var call = 0; call < calls; call++)
				{
					NumericKernelsUnchecked.Transform(va, vb, scale, offset);
				}
				return Checksum(vb);
			});
			Time("csmath", calls =>
			{
				for (var call = 0; call < calls; call++)
				{
					NumericKernels.TransformMath(ma, mb, scale, offsetMath);
				}
				return Checksum(mb);
			});
			Time("csmathnc", calls =>
			{
				for (var call = 0; call < calls; call++)
				{
					NumericKernelsUnchecked.TransformMath(ma, mb, scale, offsetMath);
				}
				return Checksum(mb);
			});
			Time("burst", calls =>
			{
				var by = offsetMath;
				fixed (float3* source = ma, target = mb)
				{
					for (var call = 0; call < calls; call++)
					{
						NumericBurst.Transform(source, target, n, scale, &by);
					}
				}
				return Checksum(mb);
			});
#if GENERIC_MEMORY_NATIVE
			Time("cpp", calls =>
			{
				var by = offsetMath;
				fixed (float3* source = ma, target = mb)
				{
					for (var call = 0; call < calls; call++)
					{
						gm_native_transform(source, target, n, scale, &by);
					}
				}
				return Checksum(mb);
			});
#endif

			// ---- a matrix times points
			Begin("m4" + size, n, reset: () =>
			{
				Array.Clear(vb, 0, n);
				Array.Clear(mb, 0, n);
			});
			Time("cs", calls =>
			{
				for (var call = 0; call < calls; call++)
				{
					NumericKernels.Multiply(matrix, va, vb);
				}
				return Checksum(vb);
			});
			Time("csnc", calls =>
			{
				for (var call = 0; call < calls; call++)
				{
					NumericKernelsUnchecked.Multiply(matrix, va, vb);
				}
				return Checksum(vb);
			});
			Time("csmath", calls =>
			{
				for (var call = 0; call < calls; call++)
				{
					NumericKernels.MultiplyMath(matrixMath, ma, mb);
				}
				return Checksum(mb);
			});
			Time("csmathnc", calls =>
			{
				for (var call = 0; call < calls; call++)
				{
					NumericKernelsUnchecked.MultiplyMath(matrixMath, ma, mb);
				}
				return Checksum(mb);
			});
			Time("burst", calls =>
			{
				var by = matrixMath;
				fixed (float3* source = ma, target = mb)
				{
					for (var call = 0; call < calls; call++)
					{
						NumericBurst.Multiply(&by, source, target, n);
					}
				}
				return Checksum(mb);
			});
#if GENERIC_MEMORY_NATIVE
			Time("cpp", calls =>
			{
				var by = matrixMath;
				fixed (float3* source = ma, target = mb)
				{
					for (var call = 0; call < calls; call++)
					{
						gm_native_multiply((float*)&by, source, target, n);
					}
				}
				return Checksum(mb);
			});
#endif

			// ---- square roots
			Begin("sqrt" + size, n, reset: () => Array.Clear(fc, 0, n));
			Time("cs", calls =>
			{
				for (var call = 0; call < calls; call++)
				{
					NumericKernels.Sqrt(fa, fc);
				}
				return Checksum(fc);
			});
			Time("csnc", calls =>
			{
				for (var call = 0; call < calls; call++)
				{
					NumericKernelsUnchecked.Sqrt(fa, fc);
				}
				return Checksum(fc);
			});
			Time("csmath", calls =>
			{
				for (var call = 0; call < calls; call++)
				{
					NumericKernels.SqrtMath(fa, fc);
				}
				return Checksum(fc);
			});
			Time("burst", calls =>
			{
				fixed (float* a = fa, target = fc)
				{
					for (var call = 0; call < calls; call++)
					{
						NumericBurst.Sqrt(a, target, n);
					}
				}
				return Checksum(fc);
			});
#if GENERIC_MEMORY_NATIVE
			Time("cpp", calls =>
			{
				fixed (float* a = fa, target = fc)
				{
					for (var call = 0; call < calls; call++)
					{
						gm_native_sqrt(a, target, n);
					}
				}
				return Checksum(fc);
			});
#endif

			// ---- sines: the implementations differ in the last bit, so they are compared with a tolerance
			Begin("sin" + size, n, elements: Elements / 8, reset: () => Array.Clear(fc, 0, n), approximate: fc);
			Time("cs", calls =>
			{
				for (var call = 0; call < calls; call++)
				{
					NumericKernels.Sin(angles, fc);
				}
				return 0;
			});
			Time("csnc", calls =>
			{
				for (var call = 0; call < calls; call++)
				{
					NumericKernelsUnchecked.Sin(angles, fc);
				}
				return 0;
			});
			Time("csmath", calls =>
			{
				for (var call = 0; call < calls; call++)
				{
					NumericKernels.SinMath(angles, fc);
				}
				return 0;
			});
			Time("burst", calls =>
			{
				fixed (float* a = angles, target = fc)
				{
					for (var call = 0; call < calls; call++)
					{
						NumericBurst.Sin(a, target, n);
					}
				}
				return 0;
			});
#if GENERIC_MEMORY_NATIVE
			Time("cpp", calls =>
			{
				fixed (float* a = angles, target = fc)
				{
					for (var call = 0; call < calls; call++)
					{
						gm_native_sin(a, target, n);
					}
				}
				return 0;
			});
#endif

			// ---- division and remainder by a number the compiler does not know
			Begin("divmod" + size, n);
			Time("cs", calls =>
			{
				long sum = 0;
				for (var call = 0; call < calls; call++)
				{
					ia[0] = call & 1;
					sum += NumericKernels.DivMod(ia, by7);
				}
				return sum;
			});
			Time("csnc", calls =>
			{
				long sum = 0;
				for (var call = 0; call < calls; call++)
				{
					ia[0] = call & 1;
					sum += NumericKernelsUnchecked.DivMod(ia, by7);
				}
				return sum;
			});
			Time("burst", calls =>
			{
				long sum = 0;
				fixed (int* a = ia)
				{
					for (var call = 0; call < calls; call++)
					{
						a[0] = call & 1;
						sum += NumericBurst.DivMod(a, n, by7);
					}
				}
				return sum;
			});
#if GENERIC_MEMORY_NATIVE
			Time("cpp", calls =>
			{
				long sum = 0;
				fixed (int* a = ia)
				{
					for (var call = 0; call < calls; call++)
					{
						a[0] = call & 1;
						sum += gm_native_div_mod(a, n, by7);
					}
				}
				return sum;
			});
#endif
		}
	}
}
