// Stand-ins for the few UnityEngine and package types the encoder reaches, so that it builds without Unity.

namespace UnityEngine
{
	static class Debug
	{
		public static void LogWarning(object message)
		{
			System.Console.Error.WriteLine("warning: " + message);
		}
	}
}

namespace UnityEngine.Profiling
{
	sealed class Recorder
	{
		public static Recorder Get(string name)
		{
			return new Recorder();
		}

		public bool isValid => false;

		public bool enabled { get; set; }

		public int sampleBlockCount => 0;

		public void FilterToCurrentThread()
		{
		}
	}
}

namespace Dexc.Telemetry
{
	sealed class TelemetryCapture
	{
		internal bool IsActive => false;

		internal TelemetryContext FrameContext => default;
	}

	sealed class TelemetryRoot
	{
		internal static TelemetryRoot Existing => null;

		internal TelemetryMethods Methods => null;
	}
}
