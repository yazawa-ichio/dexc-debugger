using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Dexc.Telemetry;
using NUnit.Framework;
using UnityEngine.TestTools;
using Field = DexcDebugger.Tests.Telemetry.ProtobufTestReader.Field;

namespace DexcDebugger.Tests.Telemetry.Watched
{
	/// <summary>
	/// Chosen by the preset Tests of ProjectSettings/DexcTelemetryProfiles.json. This assembly defines
	/// DEXC_TELEMETRY_WATCH through its versionDefines, so the IL processing puts the measuring in.
	/// </summary>
	internal static class Targets
	{
		internal static int Add(int left, int right)
		{
			return left + right;
		}

		internal static object[] Allocate()
		{
			return new object[16];
		}

		internal static int Calls()
		{
			return Add(1, 2);
		}

		internal static async Task<int> LaterAsync()
		{
			await Task.Yield();
			return 3;
		}
	}
}

namespace DexcDebugger.Tests.Telemetry.Selected
{
	/// <summary>Not in the preset. The selector of Unity.DexcDebugger.Tests.TelemetrySelector.CodeGen chooses ByCode.</summary>
	internal static class Chosen
	{
		internal static int ByCode()
		{
			return 5;
		}

		internal static int NotChosen()
		{
			return 6;
		}
	}
}

namespace DexcDebugger.Tests.Telemetry
{
	using Chosen = Selected.Chosen;
	using Targets = Watched.Targets;

	public sealed class TelemetryWatchTests
	{
		const string Prefix = "DexcDebugger.Tests.Telemetry.";

		TelemetryRoot m_Root;
		MemoryStream m_Traces;

		[SetUp]
		public void SetUp()
		{
			TelemetryRoot.Reset();
			m_Root = TelemetryRoot.Current;
			m_Traces = new MemoryStream();
			var options = new TelemetryOptions { CollectUnityMetrics = false, CollectUnityLogs = false };
			m_Root.Start(
				new TelemetryFrameWriter(new MemoryStream()),
				new TelemetryFrameWriter(new MemoryStream()),
				new TelemetryFrameWriter(m_Traces),
				options,
				new KeyValuePair<string, string>[0],
				0
			);
		}

		[TearDown]
		public void TearDown()
		{
			TelemetryRoot.Reset();
		}

		List<List<Field>> Spans()
		{
			m_Root.Flush();
			return TelemetryMethodTests.ReadSpans(m_Traces);
		}

		List<List<Field>> Named(string name)
		{
			return Spans().Where(span => TelemetryMethodTests.Name(span) == Prefix + name).ToList();
		}

		static List<Field> Attribute(List<Field> span, string key)
		{
			return span
				.Where(field => field.Number == 9)
				.Select(field => ProtobufTestReader.Read(field.Bytes))
				.SingleOrDefault(attribute => attribute.Single(field => field.Number == 1).Text == key);
		}

		[Test]
		public void AChosenMethodIsNotRecordedUntilItIsWatched()
		{
			Assert.That(Targets.Add(1, 2), Is.EqualTo(3));

			Assert.That(Spans(), Is.Empty);
		}

		[Test]
		public void AWatchedMethodIsASpanWithoutACapture()
		{
			DexcTelemetry.Watch(Prefix + "Watched.Targets.Add");

			Assert.That(Targets.Add(1, 2), Is.EqualTo(3));

			var span = Named("Watched.Targets.Add").Single();
			Assert.That(span.Any(field => field.Number == 4), Is.False, "no parent outside a capture");
		}

		[Test]
		public void APrefixWatchesTheMethodsUnderIt()
		{
			DexcTelemetry.Watch(Prefix + "Watched.*");

			Targets.Calls();

			var spans = Spans();
			var calls = spans.Single(span => TelemetryMethodTests.Name(span) == Prefix + "Watched.Targets.Calls");
			var add = spans.Single(span => TelemetryMethodTests.Name(span) == Prefix + "Watched.Targets.Add");
			Assert.That(TelemetryMethodTests.Id(add, 4), Is.EqualTo(TelemetryMethodTests.Id(calls, 2)));
		}

		[Test]
		public void AMethodCanBeWatchedByReflectionOrDelegateAndUnwatched()
		{
			var add = typeof(Targets).GetMethod(nameof(Targets.Add), BindingFlags.NonPublic | BindingFlags.Static);
			DexcTelemetry.Watch(add);
			DexcTelemetry.Watch((Func<object[]>)Targets.Allocate);
			Targets.Add(1, 2);
			Targets.Allocate();

			DexcTelemetry.Unwatch(add);
			Targets.Add(1, 2);

			Assert.That(Named("Watched.Targets.Add").Count, Is.EqualTo(1));
			Assert.That(Named("Watched.Targets.Allocate").Count, Is.EqualTo(1));
			DexcTelemetry.UnwatchAll();
			Targets.Allocate();
			Assert.That(Named("Watched.Targets.Allocate").Count, Is.EqualTo(1));
		}

		[Test]
		public void TheAllocationOfAWatchedCallIsRecorded()
		{
			DexcTelemetry.Watch((Func<object[]>)Targets.Allocate);

			Targets.Allocate();

			var span = Named("Watched.Targets.Allocate").Single();
			var allocation = Attribute(span, "dexc.method.allocations") ?? Attribute(span, "dexc.method.allocated_bytes");
			Assert.That(allocation, Is.Not.Null, "the Editor counts allocations through the Profiler");
			Assert.That(ProtobufTestReader.Read(allocation.Single(field => field.Number == 2).Bytes).Single().Value, Is.GreaterThan(0UL));
		}

		[Test]
		public void TheSelectorOfTheProjectChoosesToo()
		{
			DexcTelemetry.Watch(Prefix + "Selected.*");

			Assert.That(Chosen.ByCode(), Is.EqualTo(5));
			Assert.That(Chosen.NotChosen(), Is.EqualTo(6));

			Assert.That(Named("Selected.Chosen.ByCode").Count, Is.EqualTo(1));
			Assert.That(Named("Selected.Chosen.NotChosen"), Is.Empty, "neither the preset nor the selector chose it");
		}

		[UnityTest]
		public IEnumerator AWatchedAsyncMethodRunsToItsCompletion()
		{
			DexcTelemetry.Watch(Prefix + "Watched.Targets.LaterAsync");

			var task = Targets.LaterAsync();
			while (!task.IsCompleted)
			{
				yield return null;
			}

			Assert.That(task.Result, Is.EqualTo(3));
			Assert.That(Named("Watched.Targets.LaterAsync").Count, Is.EqualTo(1));
		}
	}
}
