using System;
using System.Collections.Generic;
using System.IO;

namespace Dexc.QA
{
	/// <summary>
	/// Adds what only the game knows to a report: the stage, the state of the player, a save. Called on the main thread
	/// while the report is written. A contributor that throws is noted in the report and the others go on.
	/// </summary>
	public interface IDexcQAContributor
	{
		void Contribute(DexcQAReport report);
	}

	/// <summary>
	/// Hears of each report once it is written, to send it on, such as to an issue tracker. Called on the main thread. A
	/// sink that throws is skipped; the report stays in the folder.
	/// </summary>
	public interface IDexcQASink
	{
		void OnReportWritten(DexcQAReportInfo report);
	}

	/// <summary>A report being written, as a contributor sees it.</summary>
	public sealed class DexcQAReport
	{
		internal readonly List<KeyValuePair<string, string>> Fields = new List<KeyValuePair<string, string>>();
		internal readonly List<KeyValuePair<string, byte[]>> Attachments = new List<KeyValuePair<string, byte[]>>();
		internal readonly List<KeyValuePair<string, object>> Sections = new List<KeyValuePair<string, object>>();

		/// <summary>The members of report.json that are the report's own, which a section cannot take.</summary>
		static bool IsReserved(string name)
		{
			switch (name)
			{
				case "id":
				case "run":
				case "kind":
				case "signature":
				case "title":
				case "detail":
				case "occurrence":
				case "time":
				case "realtime":
				case "frame":
				case "build":
				case "resource":
				case "annotations":
				case "location":
				case "telemetry":
				case "logs":
				case "fields":
				case "attachments":
				case "contributorErrors":
				case "screenshot":
					return true;
				default:
					return false;
			}
		}

		internal DexcQAReport(string kind, string signature, string title, string detail)
		{
			Kind = kind;
			Signature = signature;
			Title = title;
			Detail = detail;
		}

		public string Kind { get; }
		public string Signature { get; }
		public string Title { get; }
		public string Detail { get; }

		/// <summary>Adds a field of the game. A key set again takes the new value.</summary>
		public void Set(string key, string value)
		{
			if (string.IsNullOrEmpty(key))
			{
				throw new ArgumentException("A field needs a key.", nameof(key));
			}
			Fields.RemoveAll(pair => pair.Key == key);
			Fields.Add(new KeyValuePair<string, string>(key, value));
		}

		/// <summary>
		/// Adds a part of its own to report.json, as a bridge to another tool does: the Watch of the Dexc Inspector, its
		/// diagnostics. The value is null, a bool, a number, a string, a list of key-value pairs with string keys (an
		/// object, in order) or any other sequence (an array), nested as deep as needed. A name set again takes the new value.
		/// </summary>
		/// <exception cref="ArgumentException">The name is empty or one of the report's own.</exception>
		public void SetSection(string name, object value)
		{
			if (string.IsNullOrEmpty(name) || IsReserved(name))
			{
				throw new ArgumentException("A section needs a name the report does not use itself: " + name, nameof(name));
			}
			Sections.RemoveAll(pair => pair.Key == name);
			Sections.Add(new KeyValuePair<string, object>(name, value));
		}

		/// <summary>Adds a file beside the report, such as a save. The name is a plain file name.</summary>
		public void Attach(string fileName, byte[] bytes)
		{
			if (string.IsNullOrEmpty(fileName) || Path.GetFileName(fileName) != fileName || fileName == QARoot.ReportFile || fileName == QARoot.ScreenshotFile)
			{
				throw new ArgumentException("An attachment is a plain file name other than the report's own: " + fileName, nameof(fileName));
			}
			Attachments.Add(new KeyValuePair<string, byte[]>(fileName, bytes ?? Array.Empty<byte>()));
		}
	}

	/// <summary>A report as written.</summary>
	public readonly struct DexcQAReportInfo
	{
		internal DexcQAReportInfo(string id, string kind, string signature, string title, string folder)
		{
			Id = id;
			Kind = kind;
			Signature = signature;
			Title = title;
			Folder = folder;
		}

		public string Id { get; }
		public string Kind { get; }
		public string Signature { get; }
		public string Title { get; }

		/// <summary>The folder of the report, with <c>report.json</c> in it.</summary>
		public string Folder { get; }
	}
}
