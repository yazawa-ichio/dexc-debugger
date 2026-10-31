#if DEXC_DEBUGGER
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Dexc;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DexcDebugger.Tests
{
	public sealed class DexcInspectorCollectionTests
	{
		GameObject m_Object;
		DexcCollectionFixture m_Fixture;
		DebugInspectorComponentHandle m_Component;
		DebugInspectorFieldDescriptor m_Field;

		[SetUp]
		public void SetUp()
		{
			Dexc.DexcDebugger.ResetForTests();
			Dexc.DexcDebugger.Initialize();
			m_Object = new GameObject("Collection fixture");
			m_Fixture = m_Object.AddComponent<DexcCollectionFixture>();
			DebugInspector.Register("Tests/Collection", m_Fixture, "Collection");
			var target = DebugInspector
				.GetRegistered(new DebugListQuery("Tests/Collection", "", new DebugPageRequest(0, 20)))
				.Entries.First()
				.Target;
			m_Component = DebugInspector
				.GetComponents(target)
				.Components.Single(item => item.TypeName == typeof(DexcCollectionFixture).FullName)
				.Component;
			m_Field = m_Component.GetFields(new DebugPageRequest(0, 20)).Fields.Single(item => item.Name == "Values");
		}

		[TearDown]
		public void TearDown()
		{
			Object.DestroyImmediate(m_Object);
			Dexc.DexcDebugger.ResetForTests();
		}

		InspectorValueSnapshot Read() => m_Component.ReadFields(new[] { m_Field.Id }).Value.Single().Snapshot;

		static DebugValue Member(DebugValue value, string name)
		{
			Assert.That(value.TryGetObject(out var members), Is.True);
			return members.Single(member => member.Name == name).Value;
		}

		[TestCase(0)]
		[TestCase(127)]
		[TestCase(128)]
		[TestCase(129)]
		public void BoundsArrayAndRejectsWrites(int count)
		{
			m_Fixture.Values = Enumerable.Range(0, count).ToArray();
			var snapshot = Read();
			Assert.That(snapshot.State, Is.EqualTo(DebugInspectorSnapshotState.Value));
			Assert.That(Member(snapshot.Value, "count"), Is.EqualTo(DebugValue.From(count)));
			Member(snapshot.Value, "items").TryGetArray(out var items);
			Assert.That(items.Count, Is.EqualTo(Math.Min(count, 128)));
			Assert.That(Member(snapshot.Value, "truncated"), Is.EqualTo(DebugValue.From(count > 128)));
			Assert.That(m_Field.IsCollection, Is.True);
			Assert.That(m_Field.CanWrite, Is.False);
			Assert.That(
				m_Component
					.WriteField(m_Field.Id, DebugInspectorWriteValue.From(DebugValue.Null), DebugInspectorWriteAccess.RegisteredOnly)
					.Status,
				Is.EqualTo(DebugOperationStatus.ReadOnly)
			);
			Assert.That(DexcInspectorRuntime.Registry.CanEdit(m_Component, m_Field.Id, true), Is.False);
		}

		[Test]
		public void NullListAndHeterogeneousItemsKeepDistinctStatesAndOldValues()
		{
			m_Fixture.Values = null;
			Assert.That(Read().Value, Is.EqualTo(DebugValue.Null));
			var reference = new GameObject("Referenced item");
			Object.DestroyImmediate(reference);
			var values = new ArrayList { null, 4, new Vector3(1, 2, 3), reference, new object(), new[] { 1 } };
			m_Fixture.Values = values;
			var snapshot = Read();
			Member(snapshot.Value, "items").TryGetArray(out var items);
			Assert.That(
				items.Select(item => Member(item, "state")),
				Is.EqualTo(new[] { "Value", "Value", "Value", "UnityReference", "Unsupported", "Unsupported" }.Select(DebugValue.From))
			);
			Assert.That(Member(items[0], "value"), Is.EqualTo(DebugValue.Null));
			Assert.That(Member(items[3], "label"), Is.EqualTo(DebugValue.From("null")));
			values[1] = 99;
			Assert.That(Member(items[1], "value"), Is.EqualTo(DebugValue.From(4)));
			Assert.That(DexcInspectorSnapshotText.Format(snapshot), Is.EqualTo("6 items"));
			Assert.That(DexcInspectorSnapshotText.Format(snapshot, true), Does.Contain("[5] Unsupported"));
			m_Fixture.Values = new List<int> { 5, 6 };
			var watch = m_Component.AddWatch(m_Field.Id, TimeSpan.Zero);
			Assert.That(watch.Status, Is.EqualTo(DebugOperationStatus.Succeeded));
			var watchEntry = DebugInspector.GetWatches(new DebugPageRequest(0, 20)).Entries.Single(entry => entry.WatchId == watch.Value);
			Assert.That(DexcInspectorSnapshotText.Format(watchEntry.Snapshot), Is.EqualTo("2 items"));
		}

		[Test]
		public void EnumerationIsBoundedAndFailuresAreParentReadFailures()
		{
			var collection = new ProbeCollection();
			m_Fixture.Values = collection;
			Assert.That(Read().State, Is.EqualTo(DebugInspectorSnapshotState.Value));
			Assert.That(collection.Moves, Is.EqualTo(128));
			Assert.That(collection.Disposed, Is.True);
			collection.Throw = true;
			var failure = Read();
			Assert.That(failure.State, Is.EqualTo(DebugInspectorSnapshotState.ReadFailure));
			Assert.That(failure.Error, Does.Contain("enumerator failed"));
		}

		sealed class ProbeCollection : ICollection, IEnumerator, IDisposable
		{
			internal int Moves;
			internal bool Disposed;
			internal bool Throw;
			public int Count => 1000;
			public bool IsSynchronized => false;
			public object SyncRoot => this;
			public object Current => Moves;

			public IEnumerator GetEnumerator() => this;

			public bool MoveNext()
			{
				if (Throw)
					throw new InvalidOperationException("enumerator failed");
				Moves++;
				return true;
			}

			public void Reset() => Moves = 0;

			public void Dispose() => Disposed = true;

			public void CopyTo(Array array, int index) => throw new NotSupportedException();
		}
	}

	public sealed class DexcCollectionFixture : MonoBehaviour
	{
		[DebugField]
		public ICollection Values;
	}
}
#endif
