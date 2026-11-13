using System;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace GenericMemory
{
	/// <summary>
	/// What a call into the engine costs inside a loop, and what a rewrite of the IL that needs no knowledge of the
	/// program could save there. Transform is only the object at hand: the kernels are named after the shape of the
	/// call (static or on an object; a number, a struct or an object across the boundary), since the shape decides
	/// what the generated code does around the call.
	/// "call.*" is one call an iteration, timed alone. A pair ("*.before" / "*.after") is the same work as the C#
	/// compiler writes it and as the rewrite would leave it; both have to return the same value before they are timed.
	/// A line is "bench", name, nanoseconds for one iteration: the best of several passes.
	/// The objects have no renderer, no collider and no children of their own, so a write is the cheapest there is.
	/// </summary>
	public static class EngineBench
	{
		const int Objects = 1024;
		const int Mask = Objects - 1;
		const int Calls = 1 << 18;
		const int Passes = 7;

		static StringBuilder s_text;
		static string s_pair;
		static bool s_first;
		static bool s_same;
		static long s_expected;
		static int s_differing;
		static float s_field = 0.5f;

		/// <summary>A class with a static constructor: every read of its field is preceded by a test that it ran.</summary>
		static class Table
		{
			public static readonly float Scale;

			static Table()
			{
				Scale = float.Parse("0.5", CultureInfo.InvariantCulture);
			}
		}

		public static void Run(StringBuilder text)
		{
			s_text = text;
			var start = Stopwatch.GetTimestamp();

			var flat = new Transform[Objects];
			var deep = new Transform[Objects];
			var components = new Component[Objects];
			var objects = new UnityEngine.Object[Objects];
			var values = new float[Objects];
			for (var at = 0; at < Objects; at++)
			{
				var one = new GameObject("engine-bench-flat");
				flat[at] = one.transform;
				flat[at].localPosition = new Vector3(at, 1, 2);
				components[at] = flat[at];
				objects[at] = at % 8 == 7 ? null : one;
				values[at] = at & 15;
				Transform parent = null;
				for (var depth = 0; depth < 4; depth++)
				{
					var next = new GameObject("engine-bench-deep").transform;
					if (parent != null)
					{
						next.SetParent(parent, false);
					}
					next.localPosition = new Vector3(1, depth, at);
					parent = next;
				}
				deep[at] = parent;
			}
			// Every eighth object of the null test is destroyed: a reference that is not null to an object that is gone.
			for (var at = 3; at < Objects; at += 8)
			{
				var gone = new GameObject("engine-bench-gone");
				objects[at] = gone;
				UnityEngine.Object.DestroyImmediate(gone);
			}
			text.Append("engineObjects\t").Append(Objects * 5).Append('\n');
			Handles(components[0]);

			Shapes(flat, deep, components, objects);
			Rewrites(flat, deep, components, objects, values);
			End();

			for (var at = 0; at < Objects; at++)
			{
				UnityEngine.Object.Destroy(flat[at].gameObject);
				UnityEngine.Object.Destroy(deep[at].root.gameObject);
			}
			text.Append("differing\t").Append(s_differing).Append('\n');
			text.Append("engineMs\t").Append(((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency).ToString("F0", CultureInfo.InvariantCulture)).Append('\n');
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		static float Managed()
		{
			return s_field;
		}

		/// <summary>The fields of UnityEngine.Object as they lie in a Player: the pointer to the engine's object comes first.</summary>
		sealed class Mirror
		{
#pragma warning disable 0649
			public IntPtr CachedPtr;
#pragma warning restore 0649
		}

		/// <summary>What UnityEngine.Bindings.Unmarshal.UnmarshalUnityObject does, as a method the caller cannot inline.</summary>
		[MethodImpl(MethodImplOptions.NoInlining)]
		static T Unmarshal<T>(IntPtr handle) where T : class
		{
			if (handle == IntPtr.Zero)
			{
				return null;
			}
			return (T)GCHandle.FromIntPtr(handle).Target;
		}

		/// <summary>The object of a handle: read directly when the handle is a strong one (its lowest bit is clear).</summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		static unsafe object Target(IntPtr handle)
		{
			if (handle == IntPtr.Zero)
			{
				return null;
			}
#if ENABLE_IL2CPP
			if (((long)handle & 1) == 0)
			{
				return UnsafeUtility.As<IntPtr, object>(ref UnsafeUtility.AsRef<IntPtr>((void*)handle));
			}
#endif
			return GCHandle.FromIntPtr(handle).Target;
		}

		/// <summary>
		/// Whether the handle that the engine returns for an object is a weak one. A weak handle cannot be read
		/// directly: the runtime takes its lock and the collector's for every read.
		/// </summary>
		static void Handles(Component component)
		{
			foreach (var name in new[] { "get_transform_Injected", "get_gameObject_Injected" })
			{
				string kind;
				try
				{
					var method = typeof(Component).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static);
					UnityEngine.Object one = component;
					var pointer = UnsafeUtility.As<UnityEngine.Object, Mirror>(ref one).CachedPtr;
					var handle = (IntPtr)method.Invoke(null, new object[] { pointer });
					kind = ((long)handle & 1) != 0 ? "weak" : "strong";
				}
				catch (Exception exception)
				{
					kind = "unknown: " + exception.GetType().Name;
				}
				s_text.Append("handle\t").Append(name).Append('\t').Append(kind).Append('\n');
			}
		}

		static long Bits(float value)
		{
			return BitConverter.SingleToInt32Bits(value);
		}

		/// <summary>One call an iteration, by the shape of the call.</summary>
		static void Shapes(Transform[] flat, Transform[] deep, Component[] components, UnityEngine.Object[] objects)
		{
			// A call of a method of the program that is not inlined: what any call costs.
			One("call.managed.float", calls =>
			{
				var sum = 0f;
				for (var call = 0; call < calls; call++)
				{
					sum += Managed();
				}
				return Bits(sum);
			});
			// Static, nothing in, a number out; the engine reads a field.
			One("call.static.float", calls =>
			{
				var sum = 0f;
				for (var call = 0; call < calls; call++)
				{
					sum += Time.deltaTime;
				}
				return Bits(sum);
			});
			One("call.static.int", calls =>
			{
				long sum = 0;
				for (var call = 0; call < calls; call++)
				{
					sum += Time.frameCount;
				}
				return sum;
			});
			// Static, a number in, a number out.
			One("call.static.arg", calls =>
			{
				long sum = 0;
				for (var call = 0; call < calls; call++)
				{
					if (Mathf.IsPowerOfTwo(call))
					{
						sum++;
					}
				}
				return sum;
			});
			// On an object, a number out.
			One("call.object.int", calls =>
			{
				long sum = 0;
				for (var call = 0; call < calls; call++)
				{
					sum += flat[call & Mask].childCount;
				}
				return sum;
			});
			// On an object, a struct out: stored as it is, and computed over four parents.
			One("call.object.struct.stored", calls =>
			{
				var sum = 0f;
				for (var call = 0; call < calls; call++)
				{
					sum += flat[call & Mask].localPosition.x;
				}
				return Bits(sum);
			});
			One("call.object.struct.root", calls =>
			{
				var sum = 0f;
				for (var call = 0; call < calls; call++)
				{
					sum += flat[call & Mask].position.x;
				}
				return Bits(sum);
			});
			One("call.object.struct.depth4", calls =>
			{
				var sum = 0f;
				for (var call = 0; call < calls; call++)
				{
					sum += deep[call & Mask].position.x;
				}
				return Bits(sum);
			});
			// On an object, a struct in: a write, which the engine has to tell its other parts about.
			One("call.object.set.stored", calls =>
			{
				for (var call = 0; call < calls; call++)
				{
					flat[call & Mask].localPosition = new Vector3((call >> 10) & 255, 1, 2);
				}
				return Bits(flat[5].localPosition.x);
			});
			One("call.object.set.depth4", calls =>
			{
				for (var call = 0; call < calls; call++)
				{
					deep[call & Mask].position = new Vector3((call >> 10) & 255, 1, 2);
				}
				return Bits(deep[5].position.x);
			});
			// On an object, an object out: the engine's object has to be turned back into the C# one.
			One("call.object.object", calls =>
			{
				long sum = 0;
				for (var call = 0; call < calls; call++)
				{
					if ((object)components[call & Mask].transform != null)
					{
						sum++;
					}
				}
				return sum;
			});
			One("call.object.object.parent", calls =>
			{
				long sum = 0;
				for (var call = 0; call < calls; call++)
				{
					if ((object)deep[call & Mask].parent != null)
					{
						sum++;
					}
				}
				return sum;
			});
			// On an object, an object in.
			One("call.object.objectarg", calls =>
			{
				long sum = 0;
				for (var call = 0; call < calls; call++)
				{
					if (deep[call & Mask].IsChildOf(flat[call & Mask]))
					{
						sum++;
					}
				}
				return sum;
			});
			// On an object, a string out: the engine's text is copied into a new string every time.
			One("call.object.string", calls =>
			{
				long sum = 0;
				for (var call = 0; call < calls; call++)
				{
					sum += flat[call & Mask].name.Length;
				}
				return sum;
			}, Calls / 16);
			// A generic call that the engine answers by the type.
			One("call.object.getcomponent", calls =>
			{
				long sum = 0;
				for (var call = 0; call < calls; call++)
				{
					if ((object)components[call & Mask].GetComponent<Transform>() != null)
					{
						sum++;
					}
				}
				return sum;
			});
		}

		/// <summary>The same work before and after a rewrite that a tool could do from the IL alone.</summary>
		static void Rewrites(Transform[] flat, Transform[] deep, Component[] components, UnityEngine.Object[] objects, float[] values)
		{
			// A value that cannot change while the loop runs, asked for in every iteration.
			Pair("hoist.frame");
			Way("before", calls =>
			{
				var sum = 0f;
				for (var call = 0; call < calls; call++)
				{
					sum += values[call & Mask] * Time.deltaTime;
				}
				return Bits(sum);
			});
			Way("after", calls =>
			{
				var sum = 0f;
				var delta = Time.deltaTime;
				for (var call = 0; call < calls; call++)
				{
					sum += values[call & Mask] * delta;
				}
				return Bits(sum);
			});

			// The object of an object that the loop does not change, asked for in every iteration.
			Pair("hoist.object");
			Way("before", calls =>
			{
				var owner = components[3];
				long sum = 0;
				for (var call = 0; call < calls; call++)
				{
					sum += owner.transform.childCount + call;
				}
				return sum;
			});
			Way("after", calls =>
			{
				var owner = components[3];
				var transform = owner.transform;
				long sum = 0;
				for (var call = 0; call < calls; call++)
				{
					sum += transform.childCount + call;
				}
				return sum;
			});

			// The same value read three times with nothing between the reads.
			Pair("reread");
			Way("before", calls =>
			{
				var sum = 0f;
				for (var call = 0; call < calls; call++)
				{
					var one = flat[call & Mask];
					sum += one.localPosition.x + one.localPosition.y + one.localPosition.z;
				}
				return Bits(sum);
			});
			Way("after", calls =>
			{
				var sum = 0f;
				for (var call = 0; call < calls; call++)
				{
					var position = flat[call & Mask].localPosition;
					sum += position.x + position.y + position.z;
				}
				return Bits(sum);
			});

			// Two writes of the same object in a row, for which the engine has one call.
			var turnA = Quaternion.Euler(0, 10, 0);
			var turnB = Quaternion.Euler(0, 20, 0);
			Pair("merge.set.stored");
			Way("before", calls =>
			{
				for (var call = 0; call < calls; call++)
				{
					var one = flat[call & Mask];
					one.localPosition = new Vector3((call >> 10) & 255, 1, 2);
					one.localRotation = ((call >> 10) & 1) == 0 ? turnA : turnB;
				}
				return Bits(flat[5].localPosition.x) + 3 * Bits(flat[5].localRotation.y);
			});
			Way("after", calls =>
			{
				for (var call = 0; call < calls; call++)
				{
					flat[call & Mask].SetLocalPositionAndRotation(new Vector3((call >> 10) & 255, 1, 2), ((call >> 10) & 1) == 0 ? turnA : turnB);
				}
				return Bits(flat[5].localPosition.x) + 3 * Bits(flat[5].localRotation.y);
			});
			Pair("merge.set.depth4");
			Way("before", calls =>
			{
				for (var call = 0; call < calls; call++)
				{
					var one = deep[call & Mask];
					one.position = new Vector3((call >> 10) & 255, 1, 2);
					one.rotation = ((call >> 10) & 1) == 0 ? turnA : turnB;
				}
				return Bits(deep[5].localPosition.x) + 3 * Bits(deep[5].localRotation.y);
			});
			Way("after", calls =>
			{
				for (var call = 0; call < calls; call++)
				{
					deep[call & Mask].SetPositionAndRotation(new Vector3((call >> 10) & 255, 1, 2), ((call >> 10) & 1) == 0 ? turnA : turnB);
				}
				return Bits(deep[5].localPosition.x) + 3 * Bits(deep[5].localRotation.y);
			});

			// Two reads of the same object in a row, for which the engine has one call.
			Pair("merge.get.depth4");
			Way("before", calls =>
			{
				var sum = 0f;
				for (var call = 0; call < calls; call++)
				{
					var one = deep[call & Mask];
					var position = one.position;
					var rotation = one.rotation;
					sum += position.x + rotation.w;
				}
				return Bits(sum);
			});
			Way("after", calls =>
			{
				var sum = 0f;
				for (var call = 0; call < calls; call++)
				{
					deep[call & Mask].GetPositionAndRotation(out var position, out var rotation);
					sum += position.x + rotation.w;
				}
				return Bits(sum);
			});

			// The test of an engine object against null: the operator of UnityEngine.Object, which is two calls deep, and
			// the same test written out (a null reference, or an object whose engine side is gone). The array holds
			// null references, live objects and destroyed ones, so the two have to agree on all three.
			Pair("null");
			Way("before", calls =>
			{
				long sum = 0;
				for (var call = 0; call < calls; call++)
				{
					if (objects[call & Mask] != null)
					{
						sum++;
					}
				}
				return sum;
			});
			Way("after", calls =>
			{
				long sum = 0;
				for (var call = 0; call < calls; call++)
				{
					var one = objects[call & Mask];
					if ((object)one != null && UnsafeUtility.As<UnityEngine.Object, Mirror>(ref one).CachedPtr != IntPtr.Zero)
					{
						sum++;
					}
				}
				return sum;
			});
			// The plain comparison of references: less than any rewrite could leave, and a different answer for a
			// destroyed object.
			One("null.reference", calls =>
			{
				long sum = 0;
				for (var call = 0; call < calls; call++)
				{
					if ((object)objects[call & Mask] != null)
					{
						sum++;
					}
				}
				return sum;
			});

			// What the generated code does after an engine call that returns an object: the engine hands back a handle,
			// and a generic method of the engine's C# side turns it into the object and casts it. "before" is that
			// method written again here (the original is private); "after" reads the object out of a strong handle
			// and does not cast.
			var handles = new IntPtr[Objects];
			for (var at = 0; at < Objects; at++)
			{
				handles[at] = GCHandle.ToIntPtr(GCHandle.Alloc(components[at]));
			}
			Pair("unmarshal");
			Way("before", calls =>
			{
				long sum = 0;
				for (var call = 0; call < calls; call++)
				{
					if ((object)Unmarshal<Transform>(handles[call & Mask]) != null)
					{
						sum++;
					}
				}
				return sum;
			});
			Way("after", calls =>
			{
				long sum = 0;
				for (var call = 0; call < calls; call++)
				{
					if (Target(handles[call & Mask]) != null)
					{
						sum++;
					}
				}
				return sum;
			});
			for (var at = 0; at < Objects; at++)
			{
				GCHandle.FromIntPtr(handles[at]).Free();
			}

			// A static field of a class with a static constructor, read in a loop that also makes a call.
			Pair("static.init");
			Way("before", calls =>
			{
				var sum = 0f;
				for (var call = 0; call < calls; call++)
				{
					sum += Table.Scale * Managed();
				}
				return Bits(sum);
			});
			Way("after", calls =>
			{
				var sum = 0f;
				var scale = Table.Scale;
				for (var call = 0; call < calls; call++)
				{
					sum += scale * Managed();
				}
				return Bits(sum);
			});
		}

		static void Pair(string name)
		{
			End();
			s_pair = name;
			s_first = true;
			s_same = true;
		}

		static void End()
		{
			if (s_pair == null)
			{
				return;
			}
			s_text.Append("same\t").Append(s_pair).Append('\t').Append(s_same ? "true" : "false").Append('\n');
			if (!s_same)
			{
				s_differing++;
			}
			s_pair = null;
		}

		/// <summary>One side of a pair: compared with the other over two rounds of the objects, then timed.</summary>
		static void Way(string way, Func<int, long> body)
		{
			var value = body(Objects * 2);
			if (s_first)
			{
				s_expected = value;
			}
			else if (value != s_expected)
			{
				s_same = false;
				s_text.Append("differs\t").Append(s_pair).Append('.').Append(way).Append('\t').Append(value).Append('\t').Append(s_expected).Append('\n');
			}
			s_first = false;
			Measure(s_pair + "." + way, body);
		}

		static void One(string name, Func<int, long> body, int calls = Calls)
		{
			End();
			Measure(name, body, calls);
		}

		static void Measure(string name, Func<int, long> body, int calls = Calls)
		{
			body(calls / 4);
			var best = double.MaxValue;
			long kept = 0;
			for (var pass = 0; pass < Passes; pass++)
			{
				var start = Stopwatch.GetTimestamp();
				kept += body(calls);
				var ticks = Stopwatch.GetTimestamp() - start;
				best = Math.Min(best, ticks * 1e9 / Stopwatch.Frequency / calls);
			}
			if (kept == long.MinValue)
			{
				s_text.Append("kept\t").Append(kept).Append('\n');
			}
			s_text.Append("bench\t").Append(name).Append('\t').Append(best.ToString("F3", CultureInfo.InvariantCulture)).Append('\n');
		}
	}
}
