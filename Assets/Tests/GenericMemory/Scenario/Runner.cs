using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using GenericMemory.Patterns;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Scripting;

namespace GenericMemory
{
	/// <summary>
	/// Runs the first n methods of one pattern in the Player and writes what the metadata of the runtime grew by.
	/// Arguments: -generic-memory-pattern &lt;name&gt; -generic-memory-n &lt;count&gt; -generic-memory-output &lt;file&gt;.
	/// With -generic-memory-bench it times the kernels of <see cref="Bench"/> instead; -generic-memory-keepall runs <see cref="KeepAll"/> first.
	/// With -generic-memory-checks it runs <see cref="AsyncChecks"/> and writes what each check ended with;
	/// -generic-memory-interfaces does the same with <see cref="InterfaceChecks"/>, -generic-memory-comparers with
	/// <see cref="ComparerChecks"/>, and -generic-memory-interface-bench
	/// times the kernels of <see cref="InterfaceBench"/>; -generic-memory-speed-bench those of <see cref="SpeedBench"/>.
	/// -generic-memory-reflection writes the lines of <see cref="ReflectionChecks"/>.
	/// -generic-memory-race &lt;depth&gt; runs only the race of threads of those checks, over a tree of types that deep.
	/// -generic-memory-sharing runs <see cref="SharingChecks"/>, like -generic-memory-interfaces.
	/// With -generic-memory-names a measured pattern also writes, beside its output, every class and method the runtime made.
	/// On Android the arguments come from the intent or from a file, and an output that is not a full path is a file of
	/// the app's own folder: see <see cref="Arguments"/>.
	/// </summary>
	public sealed class Runner : MonoBehaviour
	{
		// The values of Il2CppStat in il2cpp-api-types.h.
		const int StatInitializedClasses = 1;
		const int StatMethods = 2;
		const int StatGenericInstances = 4;
		const int StatGenericClasses = 5;
		const int StatInflatedMethods = 6;
		const int StatInflatedTypes = 7;

		const int SuspendingCalls = 20000;
		const int SuspendingRounds = 4;

		[DllImport("__Internal")]
		static extern ulong il2cpp_stats_get_value(int stat);

		[DllImport("__Internal")]
		static extern void il2cpp_generic_memory_snapshot();

		[DllImport("__Internal")]
		[return: MarshalAs(UnmanagedType.U1)]
		static extern bool il2cpp_generic_memory_dump([MarshalAs(UnmanagedType.LPStr)] string path);

		[DllImport("__Internal")]
		[return: MarshalAs(UnmanagedType.U1)]
		static extern bool il2cpp_generic_memory_names([MarshalAs(UnmanagedType.LPStr)] string path);

		string _pattern;
		int _count;
		string _output;
		bool _bench;
		bool _checks;
		bool _interfaces;
		bool _interfaceBench;
		bool _speedBench;
		bool _comparers;
		bool _sharing;
		bool _reflection;
		int _race = -1;
		int _frame;
		int _ranAt = -1;
		ulong[] _before;
		long _privateBefore;
		long _unityBefore;
		long _managedBefore;
		double _elapsed;
		StringBuilder _benchText;
		int _round;
		long _roundStart;

#if !UNITY_EDITOR
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		static void Boot()
		{
			// This assembly is in every Player the project builds. Only one that was started for the bench gets the runner;
			// any other would be told to quit for lack of the arguments.
			if (!Array.Exists(Arguments(), argument => argument.StartsWith("-generic-memory-", StringComparison.Ordinal)))
			{
				return;
			}
#if UNITY_ANDROID
			// A run is driven from the PC and nobody touches the device: a screen that goes dark would stop the frames.
			Screen.sleepTimeout = SleepTimeout.NeverSleep;
#endif
			var host = new GameObject("Generic Memory Runner");
			DontDestroyOnLoad(host);
			host.AddComponent<Runner>();
		}
#endif

		/// <summary>
		/// The arguments the Player was started with. An Android app has no command line of its own: Unity makes one of the
		/// intent's "unity" extra (am start -e unity "..."). When nothing of the bench is in it, the arguments are read from
		/// the file generic-memory-args.txt of the app's own folder (Application.persistentDataPath).
		/// </summary>
		static string[] Arguments()
		{
			var args = Environment.GetCommandLineArgs();
#if UNITY_ANDROID && !UNITY_EDITOR
			if (!Array.Exists(args, argument => argument.StartsWith("-generic-memory-", StringComparison.Ordinal)))
			{
				var file = Path.Combine(Application.persistentDataPath, "generic-memory-args.txt");
				if (File.Exists(file))
				{
					args = File.ReadAllText(file).Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
				}
			}
#endif
			return args;
		}

		void Start()
		{
			var args = Arguments();
			for (var at = 0; at < args.Length - 1; at++)
			{
				switch (args[at])
				{
					case "-generic-memory-pattern":
						_pattern = args[at + 1];
						break;
					case "-generic-memory-n":
						_count = int.Parse(args[at + 1]);
						break;
					case "-generic-memory-output":
						_output = args[at + 1];
						break;
					case "-generic-memory-race":
						_race = int.Parse(args[at + 1]);
						break;
				}
			}
			_bench = Array.IndexOf(args, "-generic-memory-bench") >= 0;
			if (_bench)
			{
				_pattern = "Bench";
			}
			_checks = Array.IndexOf(args, "-generic-memory-checks") >= 0;
			_interfaces = Array.IndexOf(args, "-generic-memory-interfaces") >= 0;
			_interfaceBench = Array.IndexOf(args, "-generic-memory-interface-bench") >= 0;
			_speedBench = Array.IndexOf(args, "-generic-memory-speed-bench") >= 0;
			_comparers = Array.IndexOf(args, "-generic-memory-comparers") >= 0;
			_sharing = Array.IndexOf(args, "-generic-memory-sharing") >= 0;
			_reflection = Array.IndexOf(args, "-generic-memory-reflection") >= 0;
			if (_checks || _interfaces || _interfaceBench || _speedBench || _comparers || _sharing || _reflection || _race >= 0)
			{
				_pattern = "Checks";
			}
			if (Array.IndexOf(args, "-generic-memory-keepall") >= 0)
			{
				KeepAll.Run();
			}
#if UNITY_ANDROID && !UNITY_EDITOR
			// Whoever starts the run names the file, not where the device keeps the app's folder. adb reads it from there.
			if (_output != null && !Path.IsPathRooted(_output))
			{
				_output = Path.Combine(Application.persistentDataPath, _output);
			}
			UnityEngine.Debug.Log("Generic memory: " + string.Join(" ", args) + " -> " + _output);
#endif
			if (_pattern == null || _output == null)
			{
				UnityEngine.Debug.LogError("Generic memory: -generic-memory-pattern and -generic-memory-output are required");
				Application.Quit(2);
			}
		}

		void Update()
		{
			_frame++;
			if (_pattern == null || _output == null)
			{
				return;
			}
			if (_interfaceBench || _speedBench)
			{
				if (_frame == 5)
				{
					var text = new StringBuilder();
					if (_interfaceBench)
					{
						InterfaceBench.Run(text);
					}
					if (_speedBench)
					{
						SpeedBench.Run(text);
					}
					File.WriteAllText(_output, text.ToString());
					_pattern = null;
					Application.Quit(0);
				}
				return;
			}
			if (_race >= 0)
			{
				if (_frame == 5)
				{
					var text = new StringBuilder();
					try
					{
						ReflectionChecks.RaceStress(text, _race);
						text.Append("failed\t").Append(ReflectionChecks.Failed).Append('\n');
					}
					catch (Exception exception)
					{
						text.Append("check\tRaceStress\t0\tFAIL\t").Append(exception.GetType().Name).Append('\n').Append("failed\t1\n");
					}
					File.WriteAllText(_output, text.ToString());
					_pattern = null;
					Application.Quit(ReflectionChecks.Failed == 0 ? 0 : 4);
				}
				return;
			}
			if (_reflection)
			{
				if (_frame == 5)
				{
					ReflectionChecks.Begin();
				}
				// A few frames later, so that Start, Update, the invoked method and the coroutine of its component have run.
				if (_frame == 12)
				{
					var text = new StringBuilder();
					try
					{
						ReflectionChecks.Run(text);
						text.Append("failed\t").Append(ReflectionChecks.Failed).Append('\n');
					}
					catch (Exception exception)
					{
						text.Append("check\tRun\t0\tFAIL\t").Append(exception.GetType().Name).Append('\n').Append("failed\t1\n");
					}
					File.WriteAllText(_output, text.ToString());
					_pattern = null;
					Application.Quit(ReflectionChecks.Failed == 0 ? 0 : 4);
				}
				return;
			}
			if (_interfaces || _comparers || _sharing)
			{
				if (_frame == 5)
				{
					var text = new StringBuilder();
					var failed = 0;
					if (_interfaces)
					{
						InterfaceChecks.Run(text);
						failed += InterfaceChecks.Failed;
					}
					if (_sharing)
					{
						SharingChecks.Run(text);
						failed += SharingChecks.Failed;
					}
					if (_comparers)
					{
						if (Array.IndexOf(Arguments(), "-generic-memory-comparers-warm") >= 0)
						{
							ComparerChecks.Warm();
						}
						ComparerChecks.Run(text, _output);
						failed += ComparerChecks.Failed;
					}
					text.Append("failed\t").Append(failed).Append('\n');
					File.WriteAllText(_output, text.ToString());
					_pattern = null;
					Application.Quit(failed == 0 ? 0 : 4);
				}
				return;
			}
			if (_checks)
			{
				UpdateChecks();
				return;
			}
			if (_bench)
			{
				UpdateBench();
				return;
			}
			// A few frames first, so that what the engine sets up on its own is over before the counters are read.
			if (_frame == 5)
			{
				var types = Registry.Types(_pattern);
				if (types == 0 || _count > types)
				{
					UnityEngine.Debug.LogError("Generic memory: pattern " + _pattern + " has " + types + " types, asked for " + _count);
					Application.Quit(2);
					_pattern = null;
					return;
				}
				_before = Stats();
				_privateBefore = PrivateBytes();
				_unityBefore = Profiler.GetTotalAllocatedMemoryLong();
				_managedBefore = Profiler.GetMonoUsedSizeLong();
				il2cpp_generic_memory_snapshot();
				var start = Stopwatch.GetTimestamp();
				for (var index = 0; index < _count; index++)
				{
					Registry.Run(_pattern, index);
				}
				// The first use of each type: making its metadata is most of this time.
				_elapsed = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
				_ranAt = _frame;
				return;
			}
			if (_ranAt < 0 || _frame < _ranAt + 5 || (Sink.Pending > 0 && _frame < _ranAt + 600))
			{
				return;
			}
			Write();
			_pattern = null;
			Application.Quit(Sink.Pending == 0 ? 0 : 3);
		}

		void UpdateChecks()
		{
			if (_frame < 5)
			{
				return;
			}
			if (_benchText == null)
			{
				_benchText = new StringBuilder();
			}
			if (!AsyncChecks.Step(_benchText))
			{
				return;
			}
			_benchText.Append("failed\t").Append(AsyncChecks.Failed).Append('\n');
			File.WriteAllText(_output, _benchText.ToString());
			_pattern = null;
			Application.Quit(AsyncChecks.Failed == 0 ? 0 : 4);
		}

		void UpdateBench()
		{
			if (_frame < 5)
			{
				return;
			}
			if (_frame == 5)
			{
				_benchText = new StringBuilder();
				Bench.RunSync(_benchText);
				return;
			}
			// Rounds of async methods that suspend once. The first rounds fill the pools; the last is what is reported.
			if (_ranAt < 0)
			{
				// Nothing is collected while a round runs, so the growth of the heap is what the round allocated.
				GarbageCollector.GCMode = GarbageCollector.Mode.Disabled;
				var managed = Profiler.GetMonoUsedSizeLong();
				_roundStart = Stopwatch.GetTimestamp();
				Bench.StartSuspending(SuspendingCalls);
				var started = Stopwatch.GetTimestamp();
				_benchText.Append("bench\tasync.start.").Append(_round).Append('\t').Append(PerCall(started - _roundStart)).Append('\n');
				_benchText.Append("bench\tasync.startBytes.").Append(_round).Append('\t').Append(((Profiler.GetMonoUsedSizeLong() - managed) / (double)SuspendingCalls).ToString("F1", CultureInfo.InvariantCulture)).Append('\n');
				_ranAt = _frame;
				return;
			}
			if (Sink.Pending > 0 && _frame < _ranAt + 600)
			{
				return;
			}
			// From the start of the round to the frame after everything resumed: the frame's own work is in here too.
			_benchText.Append("bench\tasync.whole.").Append(_round).Append('\t').Append(PerCall(Stopwatch.GetTimestamp() - _roundStart)).Append('\n');
			GarbageCollector.GCMode = GarbageCollector.Mode.Enabled;
			GC.Collect();
			_round++;
			_ranAt = -1;
			if (_round < SuspendingRounds)
			{
				return;
			}
			_benchText.Append("pending\t").Append(Sink.Pending).Append('\n');
			_benchText.Append("sum\t").Append(Sink.Sum).Append('\n');
			File.WriteAllText(_output, _benchText.ToString());
			_pattern = null;
			Application.Quit(Sink.Pending == 0 ? 0 : 3);
		}

		static string PerCall(long ticks)
		{
			return (ticks * 1e9 / Stopwatch.Frequency / SuspendingCalls).ToString("F1", CultureInfo.InvariantCulture);
		}

		void Write()
		{
			var dump = _output + ".sites";
			var dumped = il2cpp_generic_memory_dump(dump);
			var after = Stats();
			var text = new StringBuilder();
			text.Append("pattern\t").Append(_pattern).Append('\n');
			text.Append("n\t").Append(_count).Append('\n');
			text.Append("pending\t").Append(Sink.Pending).Append('\n');
			text.Append("sum\t").Append(Sink.Sum).Append('\n');
			text.Append("elapsedMs\t").Append(_elapsed.ToString("F3", CultureInfo.InvariantCulture)).Append('\n');
			text.Append("initializedClasses\t").Append(after[0] - _before[0]).Append('\n');
			text.Append("methods\t").Append(after[1] - _before[1]).Append('\n');
			text.Append("genericInstances\t").Append(after[2] - _before[2]).Append('\n');
			text.Append("genericClasses\t").Append(after[3] - _before[3]).Append('\n');
			text.Append("inflatedMethods\t").Append(after[4] - _before[4]).Append('\n');
			text.Append("inflatedTypes\t").Append(after[5] - _before[5]).Append('\n');
			text.Append("privateBytes\t").Append(PrivateBytes() - _privateBefore).Append('\n');
			text.Append("unityAllocated\t").Append(Profiler.GetTotalAllocatedMemoryLong() - _unityBefore).Append('\n');
			text.Append("managedUsed\t").Append(Profiler.GetMonoUsedSizeLong() - _managedBefore).Append('\n');
			if (dumped)
			{
				text.Append(File.ReadAllText(dump));
				File.Delete(dump);
			}
			File.WriteAllText(_output, text.ToString());
			// Last of all, because naming a type can make the runtime allocate: every class and method made since the snapshot.
			if (Array.IndexOf(Arguments(), "-generic-memory-names") >= 0)
			{
				il2cpp_generic_memory_names(_output + ".names");
			}
		}

		static ulong[] Stats()
		{
			return new[]
			{
				il2cpp_stats_get_value(StatInitializedClasses),
				il2cpp_stats_get_value(StatMethods),
				il2cpp_stats_get_value(StatGenericInstances),
				il2cpp_stats_get_value(StatGenericClasses),
				il2cpp_stats_get_value(StatInflatedMethods),
				il2cpp_stats_get_value(StatInflatedTypes),
			};
		}

		static long PrivateBytes()
		{
			try
			{
				using (var process = Process.GetCurrentProcess())
				{
					return process.PrivateMemorySize64;
				}
			}
			catch (Exception)
			{
				// Not every platform's class library has it; the counters of the runtime are what the result rests on.
				return 0;
			}
		}
	}
}
