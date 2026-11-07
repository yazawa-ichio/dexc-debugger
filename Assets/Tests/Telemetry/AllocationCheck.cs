using NUnit.Framework;
using UnityEngine.TestTools.Constraints;

namespace DexcDebugger.Tests.Telemetry
{
	/// <summary>
	/// Checks for allocation by majority. One reading is not enough: the constraint of Unity reports an allocation for about
	/// one run in a hundred even for an empty action. Code that allocates shows it in every run.
	/// </summary>
	internal static class AllocationCheck
	{
		const int Runs = 21;

		internal static int AllocatingRuns(TestDelegate action)
		{
			var constraint = new AllocatingGCMemoryConstraint();
			var allocating = 0;
			for (var i = 0; i < Runs; i++)
			{
				if (constraint.ApplyTo(action).IsSuccess)
				{
					allocating++;
				}
			}
			return allocating;
		}

		internal static void AssertNone(TestDelegate action, string message = "")
		{
			Assert.That(AllocatingRuns(action), NUnit.Framework.Is.LessThan(Runs / 2), message);
		}

		internal static void AssertSome(TestDelegate action)
		{
			Assert.That(AllocatingRuns(action), NUnit.Framework.Is.GreaterThan(Runs / 2));
		}
	}
}
