using System;
using System.Collections.Generic;
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
	}
}
