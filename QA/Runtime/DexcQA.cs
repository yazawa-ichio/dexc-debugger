namespace Dexc.QA
{
	/// <summary>
	/// Finds and records defects in a development Player, for an agent or a person to fix: exceptions and errors Unity
	/// logs, checks of bots that fail, and what an agent reports. Each report is a folder with <c>report.json</c> and, where
	/// the Player draws, <c>screenshot.png</c>, with what is needed to reproduce it: the build, the seed, what the game was
	/// doing, where the player was, the last logs. The same defect, by its signature, is written in full a few times and
	/// counted after that. A Player started with <c>-dexc-qa-directory</c> starts on its own. Main thread only.
	/// </summary>
	public static class DexcQA
	{
		/// <summary>True between <see cref="Start"/>, or the first report, and <see cref="Stop"/>.</summary>
		public static bool IsRunning => QARoot.Current.IsRunning;

		/// <summary>The folder of the reports, or null before the first start.</summary>
		public static string Folder => QARoot.Current.Folder;

		/// <summary>Starts finding defects: from now on exceptions and errors become reports.</summary>
		/// <exception cref="System.InvalidOperationException">It is already running.</exception>
		public static void Start(DexcQAOptions options = null)
		{
			QARoot.Current.Start(options);
		}

		/// <summary>Writes the counts and stops.</summary>
		public static void Stop()
		{
			QARoot.Current.Stop();
		}

		/// <summary>
		/// Reports a defect. Starts with the default options when not running.
		/// </summary>
		/// <param name="kind">Who found it: <c>check</c>, <c>bot</c>, <c>agent</c>, or a kind of the game's own.</param>
		/// <param name="signature">What makes two reports the same defect, such as <c>stage1.exit-blocked</c>. Not what varies.</param>
		/// <param name="title">One line for a person.</param>
		/// <param name="detail">What else there is to say.</param>
		/// <returns>The id of the report, or null when it was only counted.</returns>
		/// <exception cref="System.ArgumentException">The kind or signature is empty.</exception>
		public static string Report(string kind, string signature, string title, string detail = null)
		{
			return QARoot.Current.Report(kind, signature, title, detail);
		}

		/// <summary>Reports a defect of kind <c>check</c> when the condition does not hold, as a bot checks what it sees.</summary>
		/// <returns>The condition.</returns>
		public static bool Check(bool condition, string signature, string message)
		{
			if (!condition)
			{
				QARoot.Current.Report("check", signature, message, null);
			}
			return condition;
		}

		/// <summary>Writes the counts of every signature now, as before the reports are taken.</summary>
		public static void Flush()
		{
			QARoot.Current.Flush();
		}

		public static void AddContributor(IDexcQAContributor contributor)
		{
			QARoot.Current.AddContributor(contributor);
		}

		public static void RemoveContributor(IDexcQAContributor contributor)
		{
			QARoot.Current.RemoveContributor(contributor);
		}

		public static void AddSink(IDexcQASink sink)
		{
			QARoot.Current.AddSink(sink);
		}

		public static void RemoveSink(IDexcQASink sink)
		{
			QARoot.Current.RemoveSink(sink);
		}
	}
}
