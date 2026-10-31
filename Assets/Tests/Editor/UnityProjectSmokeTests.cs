using System;
using NUnit.Framework;
using UnityEngine;

namespace DexcDebugger.Tests
{
	public sealed class UnityProjectSmokeTests
	{
		[Test]
		public void UsesExpectedUnityEditorVersion()
		{
			Assert.That(Application.unityVersion, Is.EqualTo("6000.3.20f1"));
		}

		[Test]
		public void JsonUtilityRoundTripsSerializableData()
		{
			var source = new SnapshotData
			{
				Name = "smoke",
				Count = 3,
				Enabled = true,
			};

			var json = JsonUtility.ToJson(source);
			var restored = JsonUtility.FromJson<SnapshotData>(json);

			Assert.That(restored, Is.Not.Null);
			Assert.That(restored.Name, Is.EqualTo(source.Name));
			Assert.That(restored.Count, Is.EqualTo(source.Count));
			Assert.That(restored.Enabled, Is.EqualTo(source.Enabled));
		}

		[Serializable]
		private sealed class SnapshotData
		{
			public string Name;
			public int Count;
			public bool Enabled;
		}
	}
}
