using System;

namespace Dexc.Telemetry
{
	/// <summary>
	/// Records each call of the method as a span during a capture. On a class or struct it covers the methods declared
	/// there, except constructors, property and event accessors, and code the compiler made. The attribute of a method wins
	/// over that of its type. The IL processing puts the measuring in at build time, only where the compile symbols keep
	/// <see cref="Detail"/>; without them the method is left as written. Async methods and iterators are not measured.
	/// </summary>
	[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
	public sealed class TelemetryMethodAttribute : Attribute
	{
		/// <summary>How fine the measuring is.</summary>
		public TelemetryDetail Detail { get; set; } = TelemetryDetail.Detailed;

		/// <summary>Records one call in this many, to keep down the cost of a method called often. The rest are counted.</summary>
		public int Every { get; set; } = 1;

		/// <summary>The most calls recorded in a frame. The rest are counted.</summary>
		public int PerFrame { get; set; } = 32;

		/// <summary>
		/// Also records the calls on other threads. They go through a lock and are written on the next frame, so they cost
		/// more. Without it only the main thread is recorded.
		/// </summary>
		public bool AnyThread { get; set; }
	}
}
