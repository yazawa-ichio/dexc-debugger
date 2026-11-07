using System;
using System.Diagnostics;

namespace Dexc.Telemetry
{
	// One method per severity and number of values, so that a call needs neither an array nor a boxed value.
	public sealed partial class TelemetryLogger
	{
		/// <summary>Records a log. The template has holes such as <c>{Stage}</c>, filled in order with the values.</summary>
		[Conditional(TelemetrySymbols.LogVerbose)]
		public void Verbose(string template)
		{
			Write(TelemetrySeverity.Verbose, null, template, 0);
		}

		/// <summary>Records a log. The template has holes such as <c>{Stage}</c>, filled in order with the values.</summary>
		[Conditional(TelemetrySymbols.LogVerbose)]
		public void Verbose(string template, TelemetryValue value0)
		{
			Write(TelemetrySeverity.Verbose, null, template, 1, value0);
		}

		/// <summary>Records a log. The template has holes such as <c>{Stage}</c>, filled in order with the values.</summary>
		[Conditional(TelemetrySymbols.LogVerbose)]
		public void Verbose(string template, TelemetryValue value0, TelemetryValue value1)
		{
			Write(TelemetrySeverity.Verbose, null, template, 2, value0, value1);
		}

		/// <summary>Records a log. The template has holes such as <c>{Stage}</c>, filled in order with the values.</summary>
		[Conditional(TelemetrySymbols.LogVerbose)]
		public void Verbose(string template, TelemetryValue value0, TelemetryValue value1, TelemetryValue value2)
		{
			Write(TelemetrySeverity.Verbose, null, template, 3, value0, value1, value2);
		}

		/// <summary>Records a log. The template has holes such as <c>{Stage}</c>, filled in order with the values.</summary>
		[Conditional(TelemetrySymbols.LogVerbose)]
		public void Verbose(string template, TelemetryValue value0, TelemetryValue value1, TelemetryValue value2, TelemetryValue value3)
		{
			Write(TelemetrySeverity.Verbose, null, template, 4, value0, value1, value2, value3);
		}

		/// <summary>Records a log. The template has holes such as <c>{Stage}</c>, filled in order with the values.</summary>
		[Conditional(TelemetrySymbols.LogVerbose), Conditional(TelemetrySymbols.LogDebug)]
		public void Debug(string template)
		{
			Write(TelemetrySeverity.Debug, null, template, 0);
		}

		/// <summary>Records a log. The template has holes such as <c>{Stage}</c>, filled in order with the values.</summary>
		[Conditional(TelemetrySymbols.LogVerbose), Conditional(TelemetrySymbols.LogDebug)]
		public void Debug(string template, TelemetryValue value0)
		{
			Write(TelemetrySeverity.Debug, null, template, 1, value0);
		}

		/// <summary>Records a log. The template has holes such as <c>{Stage}</c>, filled in order with the values.</summary>
		[Conditional(TelemetrySymbols.LogVerbose), Conditional(TelemetrySymbols.LogDebug)]
		public void Debug(string template, TelemetryValue value0, TelemetryValue value1)
		{
			Write(TelemetrySeverity.Debug, null, template, 2, value0, value1);
		}

		/// <summary>Records a log. The template has holes such as <c>{Stage}</c>, filled in order with the values.</summary>
		[Conditional(TelemetrySymbols.LogVerbose), Conditional(TelemetrySymbols.LogDebug)]
		public void Debug(string template, TelemetryValue value0, TelemetryValue value1, TelemetryValue value2)
		{
			Write(TelemetrySeverity.Debug, null, template, 3, value0, value1, value2);
		}

		/// <summary>Records a log. The template has holes such as <c>{Stage}</c>, filled in order with the values.</summary>
		[Conditional(TelemetrySymbols.LogVerbose), Conditional(TelemetrySymbols.LogDebug)]
		public void Debug(string template, TelemetryValue value0, TelemetryValue value1, TelemetryValue value2, TelemetryValue value3)
		{
			Write(TelemetrySeverity.Debug, null, template, 4, value0, value1, value2, value3);
		}

		/// <summary>Records a log. The template has holes such as <c>{Stage}</c>, filled in order with the values.</summary>
		[Conditional(TelemetrySymbols.LogVerbose), Conditional(TelemetrySymbols.LogDebug), Conditional(TelemetrySymbols.LogInfo)]
		public void Info(string template)
		{
			Write(TelemetrySeverity.Info, null, template, 0);
		}

		/// <summary>Records a log. The template has holes such as <c>{Stage}</c>, filled in order with the values.</summary>
		[Conditional(TelemetrySymbols.LogVerbose), Conditional(TelemetrySymbols.LogDebug), Conditional(TelemetrySymbols.LogInfo)]
		public void Info(string template, TelemetryValue value0)
		{
			Write(TelemetrySeverity.Info, null, template, 1, value0);
		}

		/// <summary>Records a log. The template has holes such as <c>{Stage}</c>, filled in order with the values.</summary>
		[Conditional(TelemetrySymbols.LogVerbose), Conditional(TelemetrySymbols.LogDebug), Conditional(TelemetrySymbols.LogInfo)]
		public void Info(string template, TelemetryValue value0, TelemetryValue value1)
		{
			Write(TelemetrySeverity.Info, null, template, 2, value0, value1);
		}

		/// <summary>Records a log. The template has holes such as <c>{Stage}</c>, filled in order with the values.</summary>
		[Conditional(TelemetrySymbols.LogVerbose), Conditional(TelemetrySymbols.LogDebug), Conditional(TelemetrySymbols.LogInfo)]
		public void Info(string template, TelemetryValue value0, TelemetryValue value1, TelemetryValue value2)
		{
			Write(TelemetrySeverity.Info, null, template, 3, value0, value1, value2);
		}

		/// <summary>Records a log. The template has holes such as <c>{Stage}</c>, filled in order with the values.</summary>
		[Conditional(TelemetrySymbols.LogVerbose), Conditional(TelemetrySymbols.LogDebug), Conditional(TelemetrySymbols.LogInfo)]
		public void Info(string template, TelemetryValue value0, TelemetryValue value1, TelemetryValue value2, TelemetryValue value3)
		{
			Write(TelemetrySeverity.Info, null, template, 4, value0, value1, value2, value3);
		}

		/// <summary>Records a log. The template has holes such as <c>{Stage}</c>, filled in order with the values.</summary>
		[Conditional(TelemetrySymbols.LogVerbose), Conditional(TelemetrySymbols.LogDebug), Conditional(TelemetrySymbols.LogInfo)]
		[Conditional(TelemetrySymbols.LogWarning)]
		public void Warning(string template)
		{
			Write(TelemetrySeverity.Warning, null, template, 0);
		}

		/// <summary>Records a log. The template has holes such as <c>{Stage}</c>, filled in order with the values.</summary>
		[Conditional(TelemetrySymbols.LogVerbose), Conditional(TelemetrySymbols.LogDebug), Conditional(TelemetrySymbols.LogInfo)]
		[Conditional(TelemetrySymbols.LogWarning)]
		public void Warning(string template, TelemetryValue value0)
		{
			Write(TelemetrySeverity.Warning, null, template, 1, value0);
		}

		/// <summary>Records a log. The template has holes such as <c>{Stage}</c>, filled in order with the values.</summary>
		[Conditional(TelemetrySymbols.LogVerbose), Conditional(TelemetrySymbols.LogDebug), Conditional(TelemetrySymbols.LogInfo)]
		[Conditional(TelemetrySymbols.LogWarning)]
		public void Warning(string template, TelemetryValue value0, TelemetryValue value1)
		{
			Write(TelemetrySeverity.Warning, null, template, 2, value0, value1);
		}

		/// <summary>Records a log. The template has holes such as <c>{Stage}</c>, filled in order with the values.</summary>
		[Conditional(TelemetrySymbols.LogVerbose), Conditional(TelemetrySymbols.LogDebug), Conditional(TelemetrySymbols.LogInfo)]
		[Conditional(TelemetrySymbols.LogWarning)]
		public void Warning(string template, TelemetryValue value0, TelemetryValue value1, TelemetryValue value2)
		{
			Write(TelemetrySeverity.Warning, null, template, 3, value0, value1, value2);
		}

		/// <summary>Records a log. The template has holes such as <c>{Stage}</c>, filled in order with the values.</summary>
		[Conditional(TelemetrySymbols.LogVerbose), Conditional(TelemetrySymbols.LogDebug), Conditional(TelemetrySymbols.LogInfo)]
		[Conditional(TelemetrySymbols.LogWarning)]
		public void Warning(string template, TelemetryValue value0, TelemetryValue value1, TelemetryValue value2, TelemetryValue value3)
		{
			Write(TelemetrySeverity.Warning, null, template, 4, value0, value1, value2, value3);
		}

		/// <summary>Records a log. The template has holes such as <c>{Stage}</c>, filled in order with the values.</summary>
		[Conditional(TelemetrySymbols.LogVerbose), Conditional(TelemetrySymbols.LogDebug), Conditional(TelemetrySymbols.LogInfo)]
		[Conditional(TelemetrySymbols.LogWarning), Conditional(TelemetrySymbols.LogError)]
		public void Error(string template)
		{
			Write(TelemetrySeverity.Error, null, template, 0);
		}

		/// <summary>Records a log. The template has holes such as <c>{Stage}</c>, filled in order with the values.</summary>
		[Conditional(TelemetrySymbols.LogVerbose), Conditional(TelemetrySymbols.LogDebug), Conditional(TelemetrySymbols.LogInfo)]
		[Conditional(TelemetrySymbols.LogWarning), Conditional(TelemetrySymbols.LogError)]
		public void Error(string template, TelemetryValue value0)
		{
			Write(TelemetrySeverity.Error, null, template, 1, value0);
		}

		/// <summary>Records a log. The template has holes such as <c>{Stage}</c>, filled in order with the values.</summary>
		[Conditional(TelemetrySymbols.LogVerbose), Conditional(TelemetrySymbols.LogDebug), Conditional(TelemetrySymbols.LogInfo)]
		[Conditional(TelemetrySymbols.LogWarning), Conditional(TelemetrySymbols.LogError)]
		public void Error(string template, TelemetryValue value0, TelemetryValue value1)
		{
			Write(TelemetrySeverity.Error, null, template, 2, value0, value1);
		}

		/// <summary>Records a log. The template has holes such as <c>{Stage}</c>, filled in order with the values.</summary>
		[Conditional(TelemetrySymbols.LogVerbose), Conditional(TelemetrySymbols.LogDebug), Conditional(TelemetrySymbols.LogInfo)]
		[Conditional(TelemetrySymbols.LogWarning), Conditional(TelemetrySymbols.LogError)]
		public void Error(string template, TelemetryValue value0, TelemetryValue value1, TelemetryValue value2)
		{
			Write(TelemetrySeverity.Error, null, template, 3, value0, value1, value2);
		}

		/// <summary>Records a log. The template has holes such as <c>{Stage}</c>, filled in order with the values.</summary>
		[Conditional(TelemetrySymbols.LogVerbose), Conditional(TelemetrySymbols.LogDebug), Conditional(TelemetrySymbols.LogInfo)]
		[Conditional(TelemetrySymbols.LogWarning), Conditional(TelemetrySymbols.LogError)]
		public void Error(string template, TelemetryValue value0, TelemetryValue value1, TelemetryValue value2, TelemetryValue value3)
		{
			Write(TelemetrySeverity.Error, null, template, 4, value0, value1, value2, value3);
		}

		/// <summary>Records a log with an exception, which becomes the exception attributes.</summary>
		[Conditional(TelemetrySymbols.LogVerbose), Conditional(TelemetrySymbols.LogDebug), Conditional(TelemetrySymbols.LogInfo)]
		[Conditional(TelemetrySymbols.LogWarning), Conditional(TelemetrySymbols.LogError)]
		public void Error(Exception exception, string template)
		{
			Write(TelemetrySeverity.Error, exception, template, 0);
		}

		/// <summary>Records a log with an exception, which becomes the exception attributes.</summary>
		[Conditional(TelemetrySymbols.LogVerbose), Conditional(TelemetrySymbols.LogDebug), Conditional(TelemetrySymbols.LogInfo)]
		[Conditional(TelemetrySymbols.LogWarning), Conditional(TelemetrySymbols.LogError)]
		public void Error(Exception exception, string template, TelemetryValue value0)
		{
			Write(TelemetrySeverity.Error, exception, template, 1, value0);
		}

		/// <summary>Records a log with an exception, which becomes the exception attributes.</summary>
		[Conditional(TelemetrySymbols.LogVerbose), Conditional(TelemetrySymbols.LogDebug), Conditional(TelemetrySymbols.LogInfo)]
		[Conditional(TelemetrySymbols.LogWarning), Conditional(TelemetrySymbols.LogError)]
		public void Error(Exception exception, string template, TelemetryValue value0, TelemetryValue value1)
		{
			Write(TelemetrySeverity.Error, exception, template, 2, value0, value1);
		}

		/// <summary>Records a log with an exception, which becomes the exception attributes.</summary>
		[Conditional(TelemetrySymbols.LogVerbose), Conditional(TelemetrySymbols.LogDebug), Conditional(TelemetrySymbols.LogInfo)]
		[Conditional(TelemetrySymbols.LogWarning), Conditional(TelemetrySymbols.LogError)]
		public void Error(Exception exception, string template, TelemetryValue value0, TelemetryValue value1, TelemetryValue value2)
		{
			Write(TelemetrySeverity.Error, exception, template, 3, value0, value1, value2);
		}

		/// <summary>Records a log with an exception, which becomes the exception attributes.</summary>
		[Conditional(TelemetrySymbols.LogVerbose), Conditional(TelemetrySymbols.LogDebug), Conditional(TelemetrySymbols.LogInfo)]
		[Conditional(TelemetrySymbols.LogWarning), Conditional(TelemetrySymbols.LogError)]
		public void Error(Exception exception, string template, TelemetryValue value0, TelemetryValue value1, TelemetryValue value2, TelemetryValue value3)
		{
			Write(TelemetrySeverity.Error, exception, template, 4, value0, value1, value2, value3);
		}

		/// <summary>Records a log. The template has holes such as <c>{Stage}</c>, filled in order with the values.</summary>
		[Conditional(TelemetrySymbols.LogVerbose), Conditional(TelemetrySymbols.LogDebug), Conditional(TelemetrySymbols.LogInfo)]
		[Conditional(TelemetrySymbols.LogWarning), Conditional(TelemetrySymbols.LogError), Conditional(TelemetrySymbols.LogFatal)]
		public void Fatal(string template)
		{
			Write(TelemetrySeverity.Fatal, null, template, 0);
		}

		/// <summary>Records a log. The template has holes such as <c>{Stage}</c>, filled in order with the values.</summary>
		[Conditional(TelemetrySymbols.LogVerbose), Conditional(TelemetrySymbols.LogDebug), Conditional(TelemetrySymbols.LogInfo)]
		[Conditional(TelemetrySymbols.LogWarning), Conditional(TelemetrySymbols.LogError), Conditional(TelemetrySymbols.LogFatal)]
		public void Fatal(string template, TelemetryValue value0)
		{
			Write(TelemetrySeverity.Fatal, null, template, 1, value0);
		}

		/// <summary>Records a log. The template has holes such as <c>{Stage}</c>, filled in order with the values.</summary>
		[Conditional(TelemetrySymbols.LogVerbose), Conditional(TelemetrySymbols.LogDebug), Conditional(TelemetrySymbols.LogInfo)]
		[Conditional(TelemetrySymbols.LogWarning), Conditional(TelemetrySymbols.LogError), Conditional(TelemetrySymbols.LogFatal)]
		public void Fatal(string template, TelemetryValue value0, TelemetryValue value1)
		{
			Write(TelemetrySeverity.Fatal, null, template, 2, value0, value1);
		}

		/// <summary>Records a log. The template has holes such as <c>{Stage}</c>, filled in order with the values.</summary>
		[Conditional(TelemetrySymbols.LogVerbose), Conditional(TelemetrySymbols.LogDebug), Conditional(TelemetrySymbols.LogInfo)]
		[Conditional(TelemetrySymbols.LogWarning), Conditional(TelemetrySymbols.LogError), Conditional(TelemetrySymbols.LogFatal)]
		public void Fatal(string template, TelemetryValue value0, TelemetryValue value1, TelemetryValue value2)
		{
			Write(TelemetrySeverity.Fatal, null, template, 3, value0, value1, value2);
		}

		/// <summary>Records a log. The template has holes such as <c>{Stage}</c>, filled in order with the values.</summary>
		[Conditional(TelemetrySymbols.LogVerbose), Conditional(TelemetrySymbols.LogDebug), Conditional(TelemetrySymbols.LogInfo)]
		[Conditional(TelemetrySymbols.LogWarning), Conditional(TelemetrySymbols.LogError), Conditional(TelemetrySymbols.LogFatal)]
		public void Fatal(string template, TelemetryValue value0, TelemetryValue value1, TelemetryValue value2, TelemetryValue value3)
		{
			Write(TelemetrySeverity.Fatal, null, template, 4, value0, value1, value2, value3);
		}
	}
}
