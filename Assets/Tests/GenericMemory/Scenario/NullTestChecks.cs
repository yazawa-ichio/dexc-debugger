using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace GenericMemory
{
	/// <summary>
	/// What a test of an engine object against null answers, for the rewrite that answers it in place (the step
	/// "nulltest"): for a null reference, for a live object, and for a destroyed one, whose reference is not null and
	/// which the engine says is. The expected answers are written out here, so a build without the rewrite proves
	/// them against the engine's own operator, and a build with it proves the rewrite against them.
	/// The checks are written in the shapes the rewrite takes, and in shapes it has to leave alone.
	/// </summary>
	public static class NullTestChecks
	{
		const int Rounds = 2;

		sealed class CheckFailed : Exception
		{
			public CheckFailed(string message)
				: base(message)
			{
			}
		}

		sealed class Holder
		{
			public GameObject Field;

			public GameObject Property => Field;
		}

		static GameObject s_none;
		static GameObject s_live;
		static GameObject s_dead;
		static Transform s_liveTransform;
		static Transform s_deadTransform;

		static readonly KeyValuePair<string, Action>[] s_checks =
		{
			Check("equals.null", EqualsNull),
			Check("notequals.null", NotEqualsNull),
			Check("bool", ToBool),
			Check("reference", Reference),
			Check("component", OfComponent),
			Check("base", OfBase),
			Check("generic", Generic),
			Check("member", Member),
			Check("chain", Chain),
			Check("loop", Loop),
			Check("left", NullOnTheLeft),
			Check("two", TwoObjects),
			Check("picked", Picked),
		};

		public static int Failed { get; private set; }

		public static void Run(StringBuilder text)
		{
			s_none = null;
			s_live = new GameObject("null-test-live");
			s_dead = new GameObject("null-test-dead");
			s_liveTransform = s_live.transform;
			s_deadTransform = s_dead.transform;
			UnityEngine.Object.DestroyImmediate(s_dead);
			foreach (var check in s_checks)
			{
				for (var round = 0; round < Rounds; round++)
				{
					string detail = null;
					try
					{
						check.Value();
					}
					catch (Exception exception)
					{
						detail = exception.GetType().Name + ": " + exception.Message.Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ');
						Failed++;
					}
					text.Append("check\t").Append(check.Key).Append('\t').Append(round).Append('\t').Append(detail == null ? "PASS" : "FAIL").Append('\t').Append(detail ?? "").Append('\n');
				}
			}
			UnityEngine.Object.DestroyImmediate(s_live);
		}

		static KeyValuePair<string, Action> Check(string name, Action check)
		{
			return new KeyValuePair<string, Action>(name, check);
		}

		static void Same(string what, bool expected, bool actual)
		{
			if (expected != actual)
			{
				throw new CheckFailed(what + ": expected " + expected + ", got " + actual);
			}
		}

		// The shapes the rewrite takes: the null is the right side, written as null.

		static void EqualsNull()
		{
			Same("none == null", true, s_none == null);
			Same("live == null", false, s_live == null);
			Same("dead == null", true, s_dead == null);
			var answer = s_dead == null ? 1 : 2;
			Same("dead == null as a choice", true, answer == 1);
		}

		static void NotEqualsNull()
		{
			Same("none != null", false, s_none != null);
			Same("live != null", true, s_live != null);
			Same("dead != null", false, s_dead != null);
		}

		static void ToBool()
		{
			Same("none as bool", false, s_none);
			Same("live as bool", true, s_live);
			Same("dead as bool", false, s_dead);
			Same("!none", true, !s_none);
			Same("!live", false, !s_live);
			Same("!dead", true, !s_dead);
			var count = 0;
			if (s_none)
			{
				count += 1;
			}
			if (s_live)
			{
				count += 10;
			}
			if (s_dead)
			{
				count += 100;
			}
			Same("if of each", true, count == 10);
		}

		// A destroyed object is still an object: only the operator of the engine says it is null.
		static void Reference()
		{
			Same("(object)dead == null", false, (object)s_dead == null);
			Same("ReferenceEquals(dead, null)", false, ReferenceEquals(s_dead, null));
			Same("dead is null", false, s_dead is null);
			Same("dead is object", true, s_dead is object);
			object boxed = s_dead;
			Same("dead as object == null", false, boxed == null);
			Same("(object)none == null", true, (object)s_none == null);
		}

		static void OfComponent()
		{
			Same("live transform == null", false, s_liveTransform == null);
			Same("dead transform == null", true, s_deadTransform == null);
			Same("live transform != null", true, s_liveTransform != null);
			Same("dead transform != null", false, s_deadTransform != null);
			Same("live transform as bool", true, s_liveTransform);
			Same("dead transform as bool", false, s_deadTransform);
			Component component = s_deadTransform;
			Same("dead component == null", true, component == null);
			component = s_liveTransform;
			Same("live component == null", false, component == null);
		}

		static void OfBase()
		{
			UnityEngine.Object none = s_none;
			UnityEngine.Object live = s_live;
			UnityEngine.Object dead = s_dead;
			Same("none of the base == null", true, none == null);
			Same("live of the base == null", false, live == null);
			Same("dead of the base == null", true, dead == null);
			Same("dead of the base != null", false, dead != null);
			Same("dead of the base as bool", false, dead);
		}

		static bool IsNull<T>(T value) where T : UnityEngine.Object
		{
			return value == null;
		}

		static bool IsThere<T>(T value) where T : UnityEngine.Object
		{
			return value != null;
		}

		static bool IsAlive<T>(T value) where T : UnityEngine.Object
		{
			return value;
		}

		static void Generic()
		{
			Same("IsNull<GameObject>(none)", true, IsNull(s_none));
			Same("IsNull<GameObject>(live)", false, IsNull(s_live));
			Same("IsNull<GameObject>(dead)", true, IsNull(s_dead));
			Same("IsNull<Transform>(live)", false, IsNull(s_liveTransform));
			Same("IsNull<Transform>(dead)", true, IsNull(s_deadTransform));
			Same("IsThere<GameObject>(none)", false, IsThere(s_none));
			Same("IsThere<GameObject>(live)", true, IsThere(s_live));
			Same("IsThere<Transform>(dead)", false, IsThere(s_deadTransform));
			Same("IsAlive<GameObject>(none)", false, IsAlive(s_none));
			Same("IsAlive<GameObject>(live)", true, IsAlive(s_live));
			Same("IsAlive<GameObject>(dead)", false, IsAlive(s_dead));
			Same("IsAlive<Transform>(live)", true, IsAlive(s_liveTransform));
		}

		static void Member()
		{
			var holder = new Holder();
			Same("empty field == null", true, holder.Field == null);
			Same("empty property != null", false, holder.Property != null);
			holder.Field = s_live;
			Same("live field == null", false, holder.Field == null);
			Same("live property != null", true, holder.Property != null);
			holder.Field = s_dead;
			Same("dead field == null", true, holder.Field == null);
			Same("dead property as bool", false, holder.Property);
			Same("live.transform == null", false, s_live.transform == null);
			Same("live.transform.parent == null", true, s_live.transform.parent == null);
		}

		// The right side of && and || must not run when the test already answers: a destroyed object throws there.
		static void Chain()
		{
			Same("live != null && live.activeSelf", true, s_live != null && s_live.activeSelf);
			Same("dead != null && dead.activeSelf", false, s_dead != null && s_dead.activeSelf);
			Same("none == null || none.activeSelf", true, s_none == null || s_none.activeSelf);
			Same("dead == null || dead.activeSelf", true, s_dead == null || s_dead.activeSelf);
			Same("live && live.activeSelf", true, s_live && s_live.activeSelf);
			Same("dead && dead.activeSelf", false, s_dead && s_dead.activeSelf);
		}

		static void Loop()
		{
			var objects = new[] { s_none, s_live, s_dead, s_live, s_dead, s_none, s_live };
			var there = 0;
			var gone = 0;
			var alive = 0;
			for (var at = 0; at < objects.Length; at++)
			{
				if (objects[at] != null)
				{
					there++;
				}
				if (objects[at] == null)
				{
					gone++;
				}
				if (objects[at])
				{
					alive++;
				}
			}
			Same("3 of 7 are there", true, there == 3);
			Same("4 of 7 are gone", true, gone == 4);
			Same("3 of 7 are alive", true, alive == 3);
		}

		// The shapes the rewrite leaves alone. They go through the operator of the engine as before.

		static void NullOnTheLeft()
		{
			Same("null == none", true, null == s_none);
			Same("null == live", false, null == s_live);
			Same("null == dead", true, null == s_dead);
			Same("null != live", true, null != s_live);
			Same("null != dead", false, null != s_dead);
		}

		static void TwoObjects()
		{
			var again = s_live;
			var deadAgain = s_dead;
			Same("live == the same", true, s_live == again);
			Same("live != the same", false, s_live != again);
			Same("live == dead", false, s_live == s_dead);
			Same("live != dead", true, s_live != s_dead);
			Same("dead == the same", true, s_dead == deadAgain);
			Same("none == dead", true, s_none == s_dead);
			Same("dead == none", true, s_dead == s_none);
			Same("live == none", false, s_live == s_none);
		}

		// One call of the operator that two paths reach: one with a null written right before it, one with an object.
		static bool EqualsPicked(GameObject value, bool pick, GameObject other)
		{
			return value == (pick ? null : other);
		}

		static void Picked()
		{
			Same("dead == (null)", true, EqualsPicked(s_dead, true, s_live));
			Same("live == (null)", false, EqualsPicked(s_live, true, s_dead));
			Same("live == (live)", true, EqualsPicked(s_live, false, s_live));
			Same("dead == (live)", false, EqualsPicked(s_dead, false, s_live));
			Same("none == (live)", false, EqualsPicked(s_none, false, s_live));
		}
	}
}
