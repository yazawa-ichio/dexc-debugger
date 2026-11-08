namespace Dexc.Telemetry.CodeGen
{
	/// <summary>A finding of the weaver, kept apart from the type of Unity so that the weaver runs without the compiler.</summary>
	internal readonly struct TelemetryDiagnostic
	{
		internal readonly bool IsError;
		internal readonly string File;
		internal readonly int Line;
		internal readonly int Column;
		internal readonly string Message;

		TelemetryDiagnostic(bool isError, string file, int line, int column, string message)
		{
			IsError = isError;
			File = file;
			Line = line;
			Column = column;
			Message = message;
		}

		internal static TelemetryDiagnostic Error(string file, int line, int column, string message)
		{
			return new TelemetryDiagnostic(true, file, line, column, message);
		}

		internal static TelemetryDiagnostic Warning(string file, int line, int column, string message)
		{
			return new TelemetryDiagnostic(false, file, line, column, message);
		}
	}
}
