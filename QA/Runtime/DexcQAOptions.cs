namespace Dexc.QA
{
	/// <summary>How <see cref="DexcQA.Start"/> finds and records defects. Every field has a default.</summary>
	public sealed class DexcQAOptions
	{
		/// <summary>
		/// The folder of the reports. The command line wins over it with <c>-dexc-qa-directory</c>; by default it is
		/// <c>QA</c> in <c>Application.persistentDataPath</c>.
		/// </summary>
		public string Directory;

		/// <summary>Reports the exceptions Unity logs, from any thread.</summary>
		public bool CaptureExceptions = true;

		/// <summary>Reports the errors Unity logs, such as <c>Debug.LogError</c>.</summary>
		public bool CaptureErrors = true;

		/// <summary>
		/// The reports written in full for one signature. Beyond them a defect is only counted: the same exception every
		/// frame must not fill the disk.
		/// </summary>
		public int DetailedReportsPerSignature = 3;

		/// <summary>The reports written in full in one run, whatever their signature. Beyond them they are only counted.</summary>
		public int MaximumReports = 200;

		/// <summary>Takes a screenshot at the end of the frame of a report, where the Player draws.</summary>
		public bool Screenshot = true;

		/// <summary>The log lines kept, of which <see cref="LogsInReport"/> go into a report.</summary>
		public int LogCapacity = 100;

		public int LogsInReport = 50;
	}
}
