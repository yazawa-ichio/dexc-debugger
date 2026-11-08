using System.Diagnostics;

namespace Dexc.Telemetry
{
	/// <summary>The kinds of loading of Android Performance Tuner, so that loads of one kind are compared with each other.</summary>
	public enum TelemetryLoadKind
	{
		/// <summary>The first start after installing or updating, with caches to fill.</summary>
		First,

		/// <summary>A start of the application.</summary>
		Cold,

		/// <summary>A return to the application, as from the background.</summary>
		Warm,

		/// <summary>Between levels, stages, scenes.</summary>
		Level,

		Other,
	}

	/// <summary>
	/// A load, from when the player asked for it to the first frame that takes input again. The game starts it and ends it:
	/// only the game knows both moments. While a load is open the frames of Unity are counted as loading frames and left out
	/// of the frame time, which they would spoil. Main thread only.
	/// </summary>
	public sealed class TelemetryLoad : System.IDisposable
	{
		internal const string SpanName = "dexc.load";
		internal const string NameAttribute = "dexc.load.name";
		internal const string KindAttribute = "dexc.load.kind";

		readonly TelemetryRoot m_Root;
		readonly TelemetrySpan m_Span;
		readonly long m_Started;
		bool m_Ended;

		/// <param name="run">The run of the root that counts it as open, or -1 when none does.</param>
		internal TelemetryLoad(TelemetryRoot root, string name, TelemetryLoadKind kind, TelemetrySpan span, int run)
		{
			m_Root = root;
			Name = name;
			Kind = kind;
			m_Span = span;
			Run = run;
			m_Started = Stopwatch.GetTimestamp();
		}

		internal int Run { get; }

		public string Name { get; }

		public TelemetryLoadKind Kind { get; }

		/// <summary>Where the load sits, to parent other spans to it.</summary>
		public TelemetryContext Context => m_Span.Context;

		/// <summary>A step of the load, such as reading assets, decompressing, building the scene, compiling shaders. Dispose it when the step is done.</summary>
		public TelemetrySpan Phase(string name)
		{
			return m_Ended ? default : m_Root.Traces.Tracer(TelemetryCapture.TracerName).Start(name, m_Span.Context, TelemetryDetail.Basic);
		}

		/// <summary>Ends the load: call it at the first frame that takes input. Ending it twice does nothing.</summary>
		public void End()
		{
			if (m_Ended)
			{
				return;
			}
			m_Ended = true;
			m_Span.Dispose();
			m_Root.EndLoad(this, (Stopwatch.GetTimestamp() - m_Started) * 1000.0 / Stopwatch.Frequency);
		}

		/// <inheritdoc cref="End"/>
		public void Dispose()
		{
			End();
		}

		internal static string KindName(TelemetryLoadKind kind)
		{
			switch (kind)
			{
				case TelemetryLoadKind.First:
					return "first";
				case TelemetryLoadKind.Cold:
					return "cold";
				case TelemetryLoadKind.Warm:
					return "warm";
				case TelemetryLoadKind.Level:
					return "level";
				default:
					return "other";
			}
		}
	}
}
