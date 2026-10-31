using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DexcDebugger.Tests
{
	public sealed class UnityRuntimeSmokeTests
	{
		[UnityTest]
		public IEnumerator RuntimeTestRunnerAdvancesAFrame()
		{
			var gameObject = new GameObject("DexcDebugger.RuntimeSmoke");

			try
			{
				yield return null;
				Assert.That(gameObject, Is.Not.Null);
				Assert.That(Application.isPlaying, Is.True);
			}
			finally
			{
				Object.Destroy(gameObject);
			}
		}
	}
}
