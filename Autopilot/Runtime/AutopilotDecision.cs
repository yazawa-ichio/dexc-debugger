using System.Collections.Generic;

namespace Dexc.Autopilot
{
	/// <summary>
	/// A question a bot asks the people watching it, through <see cref="AutopilotContext.Ask"/>. Asking does not wait: the
	/// bot looks at <see cref="IsAnswered"/> in its steps. A person answers from the dashboard, and when nobody does in
	/// time, the agent driving the workspace does. What a bot does while it waits, or when it waits too long, is its own
	/// choice, made with <see cref="Elapsed"/>. The question is withdrawn when the state that asked it exits.
	/// </summary>
	public sealed class AutopilotDecision
	{
		private readonly float m_Asked;

		internal AutopilotDecision(string id, string question, string[] choices, string machine, string state)
		{
			Id = id;
			Question = question;
			Choices = choices ?? new string[0];
			Machine = machine;
			State = state;
			m_Asked = UnityEngine.Time.realtimeSinceStartup;
		}

		public string Id { get; }

		public string Question { get; }

		/// <summary>The answers to pick from. Empty when any text will do.</summary>
		public IReadOnlyList<string> Choices { get; }

		/// <summary>The machine and the state that asked, or null when asked outside a machine.</summary>
		public string Machine { get; }

		public string State { get; }

		public bool IsAnswered => Answer != null;

		public string Answer { get; private set; }

		/// <summary>"human" or "agent".</summary>
		public string AnsweredBy { get; private set; }

		/// <summary>The state that asked has exited, or the bot was reloaded or failed: no answer will come.</summary>
		public bool IsWithdrawn { get; internal set; }

		/// <summary>Real seconds since it was asked, whatever the time scale.</summary>
		public float Elapsed => UnityEngine.Time.realtimeSinceStartup - m_Asked;

		internal void Settle(string answer, string by)
		{
			Answer = answer;
			AnsweredBy = by;
		}
	}
}
