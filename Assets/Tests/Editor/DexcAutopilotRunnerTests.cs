using System;
using System.Collections.Generic;
using System.Linq;
using Dexc.Autopilot;
using NUnit.Framework;

namespace DexcDebugger.Tests.Editor
{
	public sealed class DexcAutopilotRunnerTests
	{
		private sealed class FakeBackend : IAutopilotBackend
		{
			public Action<AutopilotBuilder<AutopilotContext>> Configure;

			public string Name => "fake";

			public IAutopilotModule Load(byte[] assembly) => new FakeModule(Configure);
		}

		private sealed class FakeModule : IAutopilotModule
		{
			private readonly Action<AutopilotBuilder<AutopilotContext>> m_Configure;

			public FakeModule(Action<AutopilotBuilder<AutopilotContext>> configure) => m_Configure = configure;

			public IReadOnlyList<string> UnboundMembers => new string[0];

			public object Invoke(string type, string method, params object[] arguments)
			{
				m_Configure((AutopilotBuilder<AutopilotContext>)arguments[0]);
				return null;
			}

			public void Dispose() { }
		}

		private readonly FakeBackend m_Backend = new FakeBackend();
		private AutopilotContext m_Context;
		private AutopilotRunner<AutopilotContext> m_Runner;

		[SetUp]
		public void SetUp()
		{
			m_Context = new AutopilotContext();
			m_Runner = new AutopilotRunner<AutopilotContext>(m_Context, "Entry", m_Backend);
		}

		private void Load(Action<AutopilotBuilder<AutopilotContext>> configure)
		{
			m_Backend.Configure = configure;
			var result = m_Runner.Load(new byte[0], "fake");
			Assert.That(result["loaded"], Is.True, result.ContainsKey("error") ? result["error"].ToString() : null);
		}

		private string ActiveState => (string)m_Runner.Status()["activeState"];

		[Test]
		public void StateMovesOnWhenItsConditionHolds()
		{
			var log = new List<string>();
			var ready = false;
			Load(builder =>
			{
				var machine = builder.Machine("Quest").When(_ => true);
				machine.State("Walk").Enter(_ => log.Add("enter walk")).Exit(_ => log.Add("exit walk")).Goto("Talk", _ => ready);
				machine.State("Talk").Enter(_ => log.Add("enter talk"));
			});

			m_Runner.Tick();
			ready = true;
			m_Runner.Tick();

			Assert.That(log, Is.EqualTo(new[] { "enter walk", "exit walk", "enter talk" }));
			Assert.That(ActiveState, Is.EqualTo("Talk"));
		}

		[Test]
		public void InterruptedMachineGoesOnFromItsState()
		{
			var battle = false;
			var ready = false;
			Load(builder =>
			{
				builder.Machine("Battle").When(_ => battle).Step(_ => { });
				var quest = builder.Machine("Quest").When(_ => true);
				quest.State("Walk").Goto("Talk", _ => ready);
				quest.State("Talk");
			});

			ready = true;
			m_Runner.Tick();
			m_Runner.Tick();
			ready = false;
			battle = true;
			m_Runner.Tick();
			Assert.That(m_Runner.Status()["active"], Is.EqualTo("Battle"));
			battle = false;
			m_Runner.Tick();

			Assert.That(m_Runner.Status()["active"], Is.EqualTo("Quest"));
			Assert.That(ActiveState, Is.EqualTo("Talk"));
		}

		[Test]
		public void RestartOnEnterBeginsFromTheFirstState()
		{
			var battle = false;
			var ready = true;
			Load(builder =>
			{
				builder.Machine("Battle").When(_ => battle).Step(_ => { });
				var quest = builder.Machine("Quest").When(_ => true).RestartOnEnter();
				quest.State("Walk").Goto("Talk", _ => ready);
				quest.State("Talk");
			});

			m_Runner.Tick();
			m_Runner.Tick();
			ready = false;
			battle = true;
			m_Runner.Tick();
			battle = false;
			m_Runner.Tick();

			Assert.That(ActiveState, Is.EqualTo("Walk"));
		}

		[Test]
		public void ReloadedBotGoesOnFromTheStateAndBoards()
		{
			Action<AutopilotBuilder<AutopilotContext>> bot = builder =>
			{
				var quest = builder.Machine("Quest").When(_ => true);
				quest.State("Walk").Goto("Talk", context => context.Shared.GetBool("ready"));
				quest.State("Talk");
			};
			Load(bot);
			m_Runner.Tick();
			m_Context.Shared.SetBool("ready", true);
			m_Context.Machine.SetInt("visits", 3);
			m_Runner.Tick();

			Load(bot);
			m_Runner.Tick();

			Assert.That(ActiveState, Is.EqualTo("Talk"));
			Assert.That(m_Context.Machine.GetInt("visits"), Is.EqualTo(3));
		}

		[Test]
		public void BoardsReadThroughAndStateBoardEndsWithItsState()
		{
			var ready = false;
			Load(builder =>
			{
				var quest = builder.Machine("Quest").When(_ => true);
				quest.State("Walk").Goto("Talk", _ => ready);
				quest.State("Talk");
			});

			m_Runner.Tick();
			m_Context.Shared.SetString("goal", "exit");
			m_Context.State.SetInt("steps", 1);
			Assert.That(m_Context.State.GetString("goal"), Is.EqualTo("exit"));

			ready = true;
			m_Runner.Tick();

			Assert.That(m_Context.State.Has("steps"), Is.False);
			Assert.That(m_Context.State.GetString("goal"), Is.EqualTo("exit"));
		}

		[Test]
		public void FailingBotStopsAndReportsIt()
		{
			Load(builder => builder.Machine("Quest").When(_ => true).Step(_ => throw new InvalidOperationException("broken")));

			Assert.That(m_Runner.Tick(), Is.False);

			Assert.That((string)m_Runner.Status()["failure"], Does.Contain("broken"));
			Assert.That(m_Runner.Tick(), Is.False);
		}

		[Test]
		public void TheGraphShowsEachMachineItsStatesAndWhereTheyLead()
		{
			Load(builder =>
			{
				var quest = builder.Machine("Quest").When(_ => true).Dwell(2f);
				quest.State("Walk").Goto("Talk", _ => false).Goto("Rest", _ => false);
				quest.State("Talk");
				quest.State("Rest");
				builder.Machine("Idle").Step(_ => { });
			});
			m_Runner.Tick();

			var graph = m_Runner.Graph();
			var machines = (List<object>)graph["machines"];
			var quest = (Dictionary<string, object>)machines[0];
			var walk = (Dictionary<string, object>)((List<object>)quest["states"])[0];

			Assert.That(graph["active"], Is.EqualTo("Quest"));
			Assert.That(machines, Has.Count.EqualTo(2));
			Assert.That(quest["first"], Is.EqualTo("Walk"));
			Assert.That(quest["current"], Is.EqualTo("Walk"));
			Assert.That(quest["dwell"], Is.EqualTo(2f));
			Assert.That(walk["goto"], Is.EqualTo(new[] { "Talk", "Rest" }));
			Assert.That(((Dictionary<string, object>)machines[1])["when"], Is.False);
		}

		[Test]
		public void AQuestionWaitsInTheStatusUntilItIsAnswered()
		{
			AutopilotDecision decision = null;
			Load(builder => builder.Machine("Quest").When(_ => true).State("Fork").Enter(context => decision = context.Ask("Which way?", "left", "right")));
			m_Runner.Tick();

			var waiting = (Dictionary<string, object>)((List<object>)m_Runner.Status()["decisions"]).Single();
			Assert.That(waiting["question"], Is.EqualTo("Which way?"));
			Assert.That(waiting["state"], Is.EqualTo("Fork"));
			Assert.That(m_Runner.Answer(decision.Id, "up", "human")["ok"], Is.False, "only one of the choices");
			Assert.That(decision.IsAnswered, Is.False);

			Assert.That(m_Runner.Answer(decision.Id, "right", "agent")["ok"], Is.True);

			Assert.That(decision.Answer, Is.EqualTo("right"));
			Assert.That(decision.AnsweredBy, Is.EqualTo("agent"));
			Assert.That((List<object>)m_Runner.Status()["decisions"], Is.Empty);
			Assert.That(m_Runner.Answer(decision.Id, "left", "human")["ok"], Is.False, "answered once");
		}

		[Test]
		public void AQuestionIsWithdrawnWhenItsStateExitsOrTheBotIsReloaded()
		{
			var leave = false;
			var decisions = new List<AutopilotDecision>();
			Action<AutopilotBuilder<AutopilotContext>> bot = builder =>
			{
				var quest = builder.Machine("Quest").When(_ => true);
				quest.State("Fork").Enter(context => decisions.Add(context.Ask("Which way?"))).Goto("Walk", _ => leave);
				quest.State("Walk").Enter(context => decisions.Add(context.Ask("How fast?")));
			};
			Load(bot);
			m_Runner.Tick();
			leave = true;
			m_Runner.Tick();

			Assert.That(decisions[0].IsWithdrawn, Is.True);
			Assert.That(decisions[1].IsWithdrawn, Is.False);
			Assert.That(m_Runner.Answer(decisions[0].Id, "left", "human")["ok"], Is.False);

			Load(bot);

			Assert.That(decisions[1].IsWithdrawn, Is.True);
			Assert.That((List<object>)m_Runner.Status()["decisions"], Is.Empty);
		}
	}
}
