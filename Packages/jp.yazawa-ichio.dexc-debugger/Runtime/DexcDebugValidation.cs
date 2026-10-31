using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Dexc
{
	/// <summary>Per-operation validation issue collector.</summary>
	public sealed class DebugValidation
	{
		private readonly List<DebugValidationIssue> m_Issues = new List<DebugValidationIssue>();
		private readonly ReadOnlyCollection<DebugValidationIssue> m_ReadOnlyIssues;

		public DebugValidation()
		{
			m_ReadOnlyIssues = new ReadOnlyCollection<DebugValidationIssue>(m_Issues);
		}

		public IReadOnlyList<DebugValidationIssue> Issues
		{
			get { return m_ReadOnlyIssues; }
		}

		public bool HasErrors
		{
			get
			{
				foreach (var issue in m_Issues)
				{
					if (issue.Severity == DebugValidationSeverity.Error)
					{
						return true;
					}
				}

				return false;
			}
		}

		public void Error(string code, string message)
		{
			m_Issues.Add(new DebugValidationIssue(DebugValidationSeverity.Error, code, message));
		}

		public void Warning(string code, string message)
		{
			m_Issues.Add(new DebugValidationIssue(DebugValidationSeverity.Warning, code, message));
		}

		public void Require(bool condition, string code, string message)
		{
			if (!condition)
			{
				Error(code, message);
			}
		}
	}
}
