using System;
using System.Threading;
using Dexc.Telemetry;
using NUnit.Framework;

namespace DexcDebugger.Tests.Telemetry
{
	public sealed class TelemetryClockTests
	{
		[Test]
		public void TheClockFollowsTheWallClockFinely()
		{
			var clock = new TelemetryClock();

			var first = clock.UtcTicks;
			var second = clock.UtcTicks;
			Thread.Sleep(20);
			var later = clock.UtcTicks;

			Assert.That(Math.Abs(first - DateTime.UtcNow.Ticks), Is.LessThan(TimeSpan.TicksPerSecond));
			Assert.That(second, Is.GreaterThanOrEqualTo(first));
			Assert.That(later - first, Is.InRange(15 * TimeSpan.TicksPerMillisecond, 2000 * TimeSpan.TicksPerMillisecond));
		}
	}
}
