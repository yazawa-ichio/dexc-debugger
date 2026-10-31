using Dexc;

namespace DexcDebugger.Samples.StealthStageDemo
{
	/// <summary>
	/// Which server to talk to. Both start configurations list this block, and the stored value is keyed by the block, so
	/// choosing a server in one of them chooses it in the other too.
	/// </summary>
	[DebugBootGroup("Server", Order = 0)]
	public sealed class StealthServerSettings : DebugBootBlock
	{
		[DebugBootItem("Environment", Order = 0, Description = "Production starts the demo with the debugger off.")]
		public StealthServerEnvironment Environment { get; private set; } = StealthServerEnvironment.Local;

		[DebugBootItem("Host", Order = 1)]
		public string Host { get; private set; } = "localhost";
	}

	/// <summary>What to fight and how much of it. Only the battle check needs this.</summary>
	[DebugBootGroup("Battle", Order = 1)]
	public sealed class StealthBattleSettings : DebugBootBlock
	{
		[DebugBootItem("Start Stage", Order = 0)]
		public StealthStartStage StartStage { get; private set; } = StealthStartStage.Stage1;

		[DebugBootItem("Guard Count", Order = 1, Description = "Guards per stage, up to the patrols the stage has.")]
		[DebugRange(0, 3)]
		public int GuardCount { get; private set; } = 3;
	}

	/// <summary>
	/// The everyday way in: pick a server and go. Start configurations are development-only, so a project usually has a
	/// couple of them - this one and <see cref="StealthBattleBoot"/> - and ships neither.
	/// </summary>
	[DebugBootMenu("Normal", Order = 0, Tag = "stealth", Scene = "StealthStageDemo", Groups = new[] { typeof(StealthServerSettings) })]
	public sealed class StealthNormalBoot : DebugBootSet
	{
		[DebugBootItem("Invincible", Order = 0, Description = "Guards still raise the alert, but being seen no longer restarts.")]
		public bool Invincible { get; private set; }

		[DebugBootItem("Time Scale", Order = 1, Step = 0.25)]
		[DebugRange(0.25, 4)]
		public float TimeScale { get; private set; } = 1f;

		protected override void OnStarting(DebugBootGroupsValues values)
		{
			StealthDemoBoot.ApplyServer(values.Get<StealthServerSettings>());
			StealthDemoConfig.Invincible = Invincible;
			StealthDemoConfig.TimeScale = TimeScale;
			StealthDemoConfig.StartStage = StealthStartStage.Stage1;
			StealthDemoConfig.GuardCount = 3;
		}
	}

	/// <summary>
	/// The battle check: the same server choice plus what to fight. It reuses <see cref="StealthServerSettings"/>, so the
	/// server picked here is the one the normal entry starts with as well.
	/// </summary>
	[DebugBootMenu(
		"Battle Check",
		Order = 1,
		Tag = "stealth-battle",
		Scene = "StealthStageDemo",
		Groups = new[] { typeof(StealthServerSettings), typeof(StealthBattleSettings) }
	)]
	public sealed class StealthBattleBoot : DebugBootSet
	{
		[DebugBootItem("Time Scale", Order = 0, Step = 0.25)]
		[DebugRange(0.25, 4)]
		public float TimeScale { get; private set; } = 1f;

		protected override void OnStarting(DebugBootGroupsValues values)
		{
			var battle = values.Get<StealthBattleSettings>();
			StealthDemoBoot.ApplyServer(values.Get<StealthServerSettings>());
			StealthDemoConfig.Invincible = false;
			StealthDemoConfig.TimeScale = TimeScale;
			StealthDemoConfig.StartStage = battle.StartStage;
			StealthDemoConfig.GuardCount = battle.GuardCount;
		}
	}

	/// <summary>Shared by both start configurations.</summary>
	internal static class StealthDemoBoot
	{
		internal static void ApplyServer(StealthServerSettings server)
		{
			StealthDemoConfig.Environment = server.Environment;
			StealthDemoConfig.Host = server.Host;
			// What was chosen in the boot screen decides whether the debugger comes up at all; StealthDebugSystem reads it.
			StealthDemoConfig.DebuggerRequested = server.Environment != StealthServerEnvironment.Production;
		}
	}
}
