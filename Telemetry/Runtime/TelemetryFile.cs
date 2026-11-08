namespace Dexc.Telemetry
{
	/// <summary>A file in the telemetry folder, as <see cref="DexcTelemetry.Files"/> lists it.</summary>
	public readonly struct TelemetryFile
	{
		internal TelemetryFile(string name, long length, bool isOpen)
		{
			Name = name;
			Length = length;
			IsOpen = isOpen;
		}

		/// <summary>The name in the folder, such as <c>metrics-&lt;run&gt;.otlp</c>.</summary>
		public string Name { get; }

		public long Length { get; }

		/// <summary>Still being written, by this Player or another: its name ends with <c>.otlp.open</c>. Its last frame may be cut short.</summary>
		public bool IsOpen { get; }
	}
}
