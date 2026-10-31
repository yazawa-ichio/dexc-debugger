using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;

namespace DexcDebugger.Tests
{
	public sealed class DexcBuildSettingsRestorationTests
	{
		[TestCase(false)]
		[TestCase(true)]
		public void RestorationPreservesActualPlayerSettingsAndContinuesAfterFailure(bool injectFailure)
		{
			var type = AppDomain
				.CurrentDomain.GetAssemblies()
				.Select(assembly => assembly.GetType("DexcDebugger.Verification.DexcBuildDriverCompletion"))
				.FirstOrDefault(candidate => candidate != null);
			Assert.That(type, Is.Not.Null);
			var restore = type.GetMethod("Restore", BindingFlags.Static | BindingFlags.NonPublic);
			Assert.That(restore, Is.Not.Null);
			// Use an always-serialized field; setting an absent platform default adds YAML keys.
			var version = PlayerSettings.bundleVersion;
			var reachedSave = false;
			try
			{
				PlayerSettings.bundleVersion = version == "0.0.1" ? "0.0.2" : "0.0.1";
				var steps = new Action[]
				{
					() =>
					{
						if (injectFailure)
						{
							throw new InvalidOperationException("injected restoration failure");
						}
					},
					() => PlayerSettings.bundleVersion = version,
					() =>
					{
						AssetDatabase.SaveAssets();
						reachedSave = true;
					},
				};
				var error = (string)restore.Invoke(null, new object[] { steps });
				Assert.That(string.IsNullOrEmpty(error), Is.EqualTo(!injectFailure));
				Assert.That(reachedSave, Is.True);
				Assert.That(PlayerSettings.bundleVersion, Is.EqualTo(version));
			}
			finally
			{
				PlayerSettings.bundleVersion = version;
				AssetDatabase.SaveAssets();
			}
		}
	}
}
