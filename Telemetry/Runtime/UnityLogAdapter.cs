using UnityEngine;

namespace Dexc.Telemetry
{
	/// <summary>
	/// Brings in what is logged through Unity, on any thread, including exceptions. Its category is <see cref="Category"/>,
	/// so its level is set like that of any logger.
	/// </summary>
	public sealed class UnityLogAdapter : ITelemetryLogAdapter
	{
		public const string Category = "Unity";

		TelemetryLogger m_Logger;

		public void Attach()
		{
			m_Logger = DexcTelemetry.Logger(Category);
			Application.logMessageReceivedThreaded += Receive;
		}

		public void Detach()
		{
			Application.logMessageReceivedThreaded -= Receive;
		}

		internal static void Forward(TelemetryLogger logger, string condition, string stack, LogType type)
		{
			switch (type)
			{
				case LogType.Log:
					logger.Emit(TelemetrySeverity.Info, condition, stack);
					break;
				case LogType.Warning:
					logger.Emit(TelemetrySeverity.Warning, condition, stack);
					break;
				case LogType.Exception:
					logger.Emit(TelemetrySeverity.Error, condition, stack, true);
					break;
				default:
					logger.Emit(TelemetrySeverity.Error, condition, stack);
					break;
			}
		}

		void Receive(string condition, string stack, LogType type)
		{
			Forward(m_Logger, condition, stack, type);
		}
	}
}
