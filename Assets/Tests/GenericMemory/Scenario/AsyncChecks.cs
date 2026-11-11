using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;

namespace GenericMemory
{
	/// <summary>
	/// What an async method has to keep doing whatever holds its state machine while it is suspended: results, locals
	/// across awaits, exceptions, cancellation, completion on other threads, continuations that run inside the
	/// registration, many instances of one method at once. Each check is an async method that throws when it finds
	/// something wrong. The same checks run in a Player built with the state machine pool and in one built without it.
	/// </summary>
	public static class AsyncChecks
	{
		const int Rounds = 3;
		const double TimeoutSeconds = 20;

		sealed class CheckFailed : Exception
		{
			public CheckFailed(string message)
				: base(message)
			{
			}
		}

		static readonly KeyValuePair<string, Func<UniTask>>[] s_checks =
		{
			Check("ResultAfterYield", ResultAfterYield),
			Check("LocalsAcrossAwaits", LocalsAcrossAwaits),
			Check("SynchronousCompletion", SynchronousCompletion),
			Check("Exceptions", Exceptions),
			Check("Unwinding", Unwinding),
			Check("Cancellation", Cancellation),
			Check("InlineContinuation", InlineContinuation),
			Check("RefusedRegistration", RefusedRegistration),
			Check("CompletedOnThreadPool", CompletedOnThreadPool),
			Check("ThreadPoolAndBack", ThreadPoolAndBack),
			Check("ResumedFromAnotherThread", ResumedFromAnotherThread),
			Check("ManyAtOnce", ManyAtOnce),
			Check("Recursion", Recursion),
			Check("OneAfterAnother", OneAfterAnother),
			Check("FireAndForget", FireAndForget),
			Check("WhenAny", WhenAnyOfTheSameMethod),
			Check("Generic", Generic),
			Check("StartedFromManyThreads", StartedFromManyThreads),
			Check("SurvivesCollection", SurvivesCollection),
			Check("TaskInterop", TaskInterop),
			Check("Lambdas", Lambdas),
			Check("InstanceMethods", InstanceMethods),
		};

		static int s_mainThread;
		static int s_index;
		static int s_round;
		static int s_state;
		static int s_run;
		static string s_detail;
		static long s_startedAt;
		static int s_failed;

		public static int Failed => s_failed;

		static KeyValuePair<string, Func<UniTask>> Check(string name, Func<UniTask> body)
		{
			return new KeyValuePair<string, Func<UniTask>>(name, body);
		}

		/// <summary>True when the state machines of this assembly went through the pool rewrite.</summary>
		public static bool Rewritten()
		{
			foreach (var type in typeof(AsyncChecks).GetNestedTypes(BindingFlags.NonPublic))
			{
				if (type.GetField("<>pool__box", BindingFlags.Instance | BindingFlags.NonPublic) != null)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// Called once a frame. Runs the checks one at a time, each <see cref="Rounds"/> times so that the later rounds
		/// run on what the first left in the pools, and returns true when the last has ended. No async here: a check is
		/// started and its awaiter asked directly, so that the harness does not rest on what it checks.
		/// </summary>
		public static bool Step(StringBuilder text)
		{
			if (s_index >= s_checks.Length)
			{
				return true;
			}
			if (s_mainThread == 0)
			{
				s_mainThread = Thread.CurrentThread.ManagedThreadId;
				text.Append("rewritten\t").Append(Rewritten() ? "true" : "false").Append('\n');
			}
			if (Volatile.Read(ref s_state) == 0)
			{
				Begin();
			}
			var state = Volatile.Read(ref s_state);
			string outcome;
			string detail;
			if (state == 2)
			{
				detail = s_detail;
				outcome = detail == null ? "PASS" : "FAIL";
			}
			else if ((Stopwatch.GetTimestamp() - s_startedAt) / (double)Stopwatch.Frequency < TimeoutSeconds)
			{
				return false;
			}
			else
			{
				// Whatever completes it later is no longer listened to.
				Interlocked.Increment(ref s_run);
				outcome = "TIMEOUT";
				detail = "not completed in " + TimeoutSeconds + " s";
			}
			if (outcome != "PASS")
			{
				s_failed++;
			}
			text.Append("check\t").Append(s_checks[s_index].Key).Append('\t').Append(s_round).Append('\t').Append(outcome).Append('\t').Append(detail ?? "").Append('\n');
			Volatile.Write(ref s_state, 0);
			s_round++;
			if (s_round == Rounds)
			{
				s_round = 0;
				s_index++;
			}
			return s_index >= s_checks.Length;
		}

		static void Begin()
		{
			var run = Interlocked.Increment(ref s_run);
			s_detail = null;
			s_startedAt = Stopwatch.GetTimestamp();
			Volatile.Write(ref s_state, 1);
			try
			{
				var awaiter = s_checks[s_index].Value().GetAwaiter();
				if (awaiter.IsCompleted)
				{
					Finish(run, awaiter);
				}
				else
				{
					awaiter.OnCompleted(() => Finish(run, awaiter));
				}
			}
			catch (Exception exception)
			{
				Complete(run, "start: " + exception);
			}
		}

		static void Finish(int run, UniTask.Awaiter awaiter)
		{
			string detail = null;
			try
			{
				awaiter.GetResult();
			}
			catch (Exception exception)
			{
				detail = exception.GetType().Name + ": " + exception.Message.Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ');
			}
			Complete(run, detail);
		}

		static void Complete(int run, string detail)
		{
			if (run != Volatile.Read(ref s_run))
			{
				return;
			}
			s_detail = detail;
			Volatile.Write(ref s_state, 2);
		}

		static void Expect(bool condition, string what)
		{
			if (!condition)
			{
				throw new CheckFailed(what);
			}
		}

		static void Same(long expected, long actual, string what)
		{
			if (expected != actual)
			{
				throw new CheckFailed(what + ": expected " + expected + ", got " + actual);
			}
		}

		static void Same(string expected, string actual, string what)
		{
			if (expected != actual)
			{
				throw new CheckFailed(what + ": expected " + expected + ", got " + actual);
			}
		}

		static bool OnMainThread => Thread.CurrentThread.ManagedThreadId == s_mainThread;

		// ---- awaiters of the checks ----

		/// <summary>Never completed when asked, and calls the continuation right inside the registration.</summary>
		readonly struct Inline : INotifyCompletion
		{
			readonly int _value;

			public Inline(int value)
			{
				_value = value;
			}

			public Inline GetAwaiter()
			{
				return this;
			}

			public bool IsCompleted => false;

			public int GetResult()
			{
				return _value;
			}

			public void OnCompleted(Action continuation)
			{
				continuation();
			}
		}

		/// <summary>The same through UnsafeOnCompleted, which is the other entry of the builder.</summary>
		readonly struct InlineCritical : ICriticalNotifyCompletion
		{
			readonly int _value;

			public InlineCritical(int value)
			{
				_value = value;
			}

			public InlineCritical GetAwaiter()
			{
				return this;
			}

			public bool IsCompleted => false;

			public int GetResult()
			{
				return _value;
			}

			public void OnCompleted(Action continuation)
			{
				continuation();
			}

			public void UnsafeOnCompleted(Action continuation)
			{
				continuation();
			}
		}

		/// <summary>Throws when the continuation is handed to it.</summary>
		readonly struct Refusing : ICriticalNotifyCompletion
		{
			public Refusing GetAwaiter()
			{
				return this;
			}

			public bool IsCompleted => false;

			public void GetResult()
			{
			}

			public void OnCompleted(Action continuation)
			{
				throw new InvalidOperationException("refused");
			}

			public void UnsafeOnCompleted(Action continuation)
			{
				throw new InvalidOperationException("refused");
			}
		}

		/// <summary>Keeps the continuation until someone, on any thread, opens it.</summary>
		sealed class Gate : ICriticalNotifyCompletion
		{
			Action _continuation;

			public Gate GetAwaiter()
			{
				return this;
			}

			public bool IsCompleted => false;

			public void GetResult()
			{
			}

			public void OnCompleted(Action continuation)
			{
				Volatile.Write(ref _continuation, continuation);
			}

			public void UnsafeOnCompleted(Action continuation)
			{
				Volatile.Write(ref _continuation, continuation);
			}

			public void Open()
			{
				var continuation = Interlocked.Exchange(ref _continuation, null);
				if (continuation != null)
				{
					continuation();
				}
			}
		}

		sealed class Counter
		{
			public int Value;
		}

		// ---- results and locals ----

		static async UniTask<int> AddAfterYield(int a, int b)
		{
			await UniTask.Yield();
			return a + b;
		}

		static async UniTask ResultAfterYield()
		{
			Same(7, await AddAfterYield(3, 4), "sum");
		}

		static async UniTask<long> ManyAwaits(int seed, string text)
		{
			long total = seed;
			var items = new List<int>();
			await UniTask.Yield();
			items.Add(seed + 1);
			total += await AddAfterYield(seed, 1);
			await UniTask.NextFrame();
			items.Add(seed + 2);
			for (var index = 0; index < 3; index++)
			{
				await UniTask.DelayFrame(1);
				total += index * seed;
			}
			await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate);
			total += text.Length;
			foreach (var item in items)
			{
				total += item;
				await UniTask.Yield();
			}
			return total;
		}

		static async UniTask LocalsAcrossAwaits()
		{
			// seed + (seed + 1) + 3 * seed + length + (seed + 1) + (seed + 2)
			Same(77, await ManyAwaits(10, "abc"), "total");
			Same(11, await ManyAwaits(1, ""), "total of the second call");
		}

		static async UniTask<int> MaybeSuspend(bool suspend, int value)
		{
			await UniTask.CompletedTask;
			var got = await UniTask.FromResult(value);
			if (suspend)
			{
				await UniTask.Yield();
			}
			return got + 1;
		}

		static async UniTask SynchronousCompletion()
		{
			var done = MaybeSuspend(false, 4);
			Expect(done.Status == UniTaskStatus.Succeeded, "a method that never suspends is complete when it returns");
			Same(5, await done, "value without suspension");
			var pending = MaybeSuspend(true, 6);
			Expect(pending.Status == UniTaskStatus.Pending, "a method that suspended is pending when it returns");
			Same(7, await pending, "value after suspension");
		}

		// ---- exceptions ----

		static async UniTask<int> Throws(int suspensions, string message)
		{
			for (var index = 0; index < suspensions; index++)
			{
				await UniTask.Yield();
			}
			throw new InvalidOperationException(message);
		}

		static async UniTask Exceptions()
		{
			for (var suspensions = 0; suspensions < 3; suspensions++)
			{
				var task = Throws(suspensions, "boom" + suspensions);
				Expect(task.Status == (suspensions == 0 ? UniTaskStatus.Faulted : UniTaskStatus.Pending), "status with " + suspensions + " suspensions");
				var thrown = false;
				try
				{
					await task;
				}
				catch (InvalidOperationException exception)
				{
					thrown = true;
					Same("boom" + suspensions, exception.Message, "message");
				}
				Expect(thrown, "no exception with " + suspensions + " suspensions");
			}
		}

		static async UniTask<string> Unwind(List<string> log, int depth)
		{
			log.Add("enter" + depth);
			try
			{
				await UniTask.Yield();
				if (depth == 0)
				{
					throw new ArgumentException("deep");
				}
				return await Unwind(log, depth - 1);
			}
			catch (ArgumentException) when (depth == 1)
			{
				await UniTask.Yield();
				log.Add("caught" + depth);
				return "handled";
			}
			finally
			{
				log.Add("exit" + depth);
			}
		}

		static async UniTask Unwinding()
		{
			var log = new List<string>();
			Same("handled", await Unwind(log, 3), "result");
			Same("enter3 enter2 enter1 enter0 exit0 caught1 exit1 exit2 exit3", string.Join(" ", log), "order");
		}

		// ---- cancellation ----

		static async UniTask<int> WaitFrames(int frames, CancellationToken token)
		{
			for (var index = 0; index < frames; index++)
			{
				await UniTask.Yield(PlayerLoopTiming.Update, token);
			}
			return frames;
		}

		static async UniTask<int> ThrowsCanceled()
		{
			await UniTask.Yield();
			throw new OperationCanceledException();
		}

		static async UniTask Cancellation()
		{
			using (var source = new CancellationTokenSource())
			{
				var task = WaitFrames(100000, source.Token);
				await UniTask.Yield();
				source.Cancel();
				var canceled = false;
				try
				{
					await task;
				}
				catch (OperationCanceledException)
				{
					canceled = true;
				}
				Expect(canceled, "a canceled wait throws");
			}
			using (var source = new CancellationTokenSource())
			{
				var task = WaitFrames(100000, source.Token).SuppressCancellationThrow();
				await UniTask.Yield();
				source.Cancel();
				var outcome = await task;
				Expect(outcome.IsCanceled, "a canceled wait reports it");
			}
			var direct = await ThrowsCanceled().SuppressCancellationThrow();
			Expect(direct.IsCanceled, "OperationCanceledException thrown by the method itself");
			Same(2, await WaitFrames(2, CancellationToken.None), "a wait nobody cancels");
		}

		// ---- continuations that run inside the registration ----

		static async UniTask<int> InlineOnly(int value)
		{
			var got = await new Inline(value);
			return got + 1;
		}

		static async UniTask<int> InlineCriticalOnly(int value)
		{
			var got = await new InlineCritical(value);
			return got + 1;
		}

		static async UniTask<int> InlineThenYield(int value)
		{
			var got = await new Inline(value);
			await UniTask.Yield();
			got += await new InlineCritical(value);
			return got;
		}

		static async UniTask<int> YieldThenInline(int value)
		{
			await UniTask.Yield();
			var got = await new Inline(value);
			got += await new InlineCritical(1);
			return got;
		}

		static async UniTask InlineContinuation()
		{
			// The method runs to its end inside its first suspension, before the call that started it has its task.
			var task = InlineOnly(5);
			Expect(task.Status == UniTaskStatus.Succeeded, "completed inside the registration");
			Same(6, await task, "inline");
			Same(8, await InlineCriticalOnly(7), "inline, unsafe");
			Same(20, await InlineThenYield(10), "inline, then a frame");
			Same(11, await YieldThenInline(10), "a frame, then inline");
			for (var index = 0; index < 200; index++)
			{
				Same(index + 1, await InlineOnly(index), "inline again");
				Same(index + 1, await InlineCriticalOnly(index), "inline, unsafe, again");
			}
		}

		static async UniTask<int> RefusedFirst()
		{
			await new Refusing();
			return 1;
		}

		static async UniTask<int> RefusedLater()
		{
			await UniTask.Yield();
			await new Refusing();
			return 1;
		}

		static async UniTask RefusedRegistration()
		{
			for (var index = 0; index < 4; index++)
			{
				var thrown = 0;
				try
				{
					await RefusedFirst();
				}
				catch (InvalidOperationException exception)
				{
					thrown++;
					Same("refused", exception.Message, "message at the first suspension");
				}
				try
				{
					await RefusedLater();
				}
				catch (InvalidOperationException exception)
				{
					thrown++;
					Same("refused", exception.Message, "message at a later suspension");
				}
				Same(2, thrown, "exceptions");
			}
			Same(7, await AddAfterYield(3, 4), "the next method after a refusal");
		}

		// ---- other threads ----

		static async UniTask<int> Hop(int value)
		{
			await UniTask.SwitchToThreadPool();
			return value * 3;
		}

		static async UniTask CompletedOnThreadPool()
		{
			// Each completes on another thread, at any moment from inside its registration on.
			var tasks = new UniTask<int>[2000];
			for (var index = 0; index < tasks.Length; index++)
			{
				tasks[index] = Hop(index);
			}
			var results = await UniTask.WhenAll(tasks);
			await UniTask.SwitchToMainThread();
			for (var index = 0; index < results.Length; index++)
			{
				if (results[index] != index * 3)
				{
					Same(index * 3, results[index], "result " + index);
				}
			}
		}

		static async UniTask<int> HopAndBack(int value)
		{
			await UniTask.SwitchToThreadPool();
			var away = !OnMainThread;
			var computed = value * 3;
			await UniTask.SwitchToMainThread();
			return away && OnMainThread ? computed : -1;
		}

		static async UniTask ThreadPoolAndBack()
		{
			var tasks = new UniTask<int>[300];
			for (var index = 0; index < tasks.Length; index++)
			{
				tasks[index] = HopAndBack(index);
			}
			var results = await UniTask.WhenAll(tasks);
			await UniTask.SwitchToMainThread();
			for (var index = 0; index < results.Length; index++)
			{
				if (results[index] != index * 3)
				{
					Same(index * 3, results[index], "result " + index);
				}
			}
		}

		static async UniTask<int> ThroughGate(Gate gate, int value)
		{
			await gate;
			var away = !OnMainThread;
			await UniTask.SwitchToMainThread();
			return away ? value : -1;
		}

		static async UniTask ResumedFromAnotherThread()
		{
			var gates = new Gate[200];
			var tasks = new UniTask<int>[gates.Length];
			for (var index = 0; index < gates.Length; index++)
			{
				gates[index] = new Gate();
				tasks[index] = ThroughGate(gates[index], index);
			}
			ThreadPool.QueueUserWorkItem(state =>
			{
				foreach (var gate in (Gate[])state)
				{
					gate.Open();
				}
			}, gates);
			var results = await UniTask.WhenAll(tasks);
			await UniTask.SwitchToMainThread();
			for (var index = 0; index < results.Length; index++)
			{
				if (results[index] != index)
				{
					Same(index, results[index], "result " + index);
				}
			}
		}

		static async UniTask<int> Worker(int seed)
		{
			var wrong = 0;
			for (var index = 0; index < 3000; index++)
			{
				var value = seed * 100000 + index;
				if (await Hop(value) != value * 3)
				{
					wrong++;
				}
				if (await InlineOnly(value) != value + 1)
				{
					wrong++;
				}
			}
			return wrong;
		}

		static async UniTask StartedFromManyThreads()
		{
			// The same two methods started and completed from eight threads at once: their pools are taken from every side.
			var workers = new UniTask<int>[8];
			for (var index = 0; index < workers.Length; index++)
			{
				var seed = index;
				workers[index] = UniTask.RunOnThreadPool<int>(() => Worker(seed));
			}
			var wrong = await UniTask.WhenAll(workers);
			await UniTask.SwitchToMainThread();
			for (var index = 0; index < wrong.Length; index++)
			{
				Same(0, wrong[index], "wrong results of worker " + index);
			}
		}

		// ---- many instances of one method ----

		static async UniTask<int> Slow(int value, int frames)
		{
			var local = value * 2;
			await UniTask.DelayFrame(frames);
			local += value;
			await UniTask.Yield();
			return local;
		}

		static async UniTask ManyAtOnce()
		{
			for (var round = 0; round < 3; round++)
			{
				var tasks = new UniTask<int>[1000];
				for (var index = 0; index < tasks.Length; index++)
				{
					tasks[index] = Slow(index + round, 1 + index % 3);
				}
				var results = await UniTask.WhenAll(tasks);
				for (var index = 0; index < results.Length; index++)
				{
					if (results[index] != (index + round) * 3)
					{
						Same((index + round) * 3, results[index], "result " + index + " of round " + round);
					}
				}
			}
		}

		static async UniTask<int> Depth(int depth)
		{
			if (depth == 0)
			{
				await UniTask.Yield();
				return 0;
			}
			var inner = await Depth(depth - 1);
			await UniTask.Yield();
			return inner + 1;
		}

		static async UniTask Recursion()
		{
			Same(60, await Depth(60), "depth");
		}

		static async UniTask OneAfterAnother()
		{
			// Each call ends before the next starts, so each takes what the one before handed back.
			var total = 0;
			for (var index = 0; index < 100; index++)
			{
				total += await AddAfterYield(index, 1);
			}
			Same(5050, total, "total");
		}

		static async UniTaskVoid Fire(Counter counter, int frames)
		{
			await UniTask.DelayFrame(frames);
			counter.Value++;
		}

		static async UniTaskVoid FireAndThrow()
		{
			await UniTask.Yield();
			throw new InvalidOperationException("unobserved");
		}

		static async UniTask Bump(Counter counter)
		{
			await UniTask.Yield();
			counter.Value++;
		}

		static async UniTask FireAndForget()
		{
			var counter = new Counter();
			for (var index = 0; index < 300; index++)
			{
				Fire(counter, 1 + index % 3).Forget();
			}
			for (var index = 0; index < 100; index++)
			{
				Bump(counter).Forget();
				AddAfterYield(index, index).Forget();
			}
			await UniTask.DelayFrame(10);
			Same(400, counter.Value, "methods that ran to their end");

			Exception seen = null;
			Action<Exception> handler = exception => seen = exception;
			UniTaskScheduler.UnobservedTaskException += handler;
			try
			{
				FireAndThrow().Forget();
				await UniTask.DelayFrame(5);
			}
			finally
			{
				UniTaskScheduler.UnobservedTaskException -= handler;
			}
			Expect(seen is InvalidOperationException, "the exception of a UniTaskVoid method reaches UnobservedTaskException");
			Same("unobserved", seen.Message, "message");
		}

		static async UniTask WhenAnyOfTheSameMethod()
		{
			var won = await UniTask.WhenAny(new[] { Slow(1, 6), Slow(2, 1), Slow(3, 9) });
			Same(1, won.Item1, "winner");
			Same(6, won.Item2, "result of the winner");
			// The two that lost end before the next check starts.
			await UniTask.DelayFrame(15);
		}

		// ---- what the rewrite leaves alone ----

		static async UniTask<T> Echo<T>(T value)
		{
			await UniTask.Yield();
			return value;
		}

		sealed class Holder<T>
		{
			readonly T _value;

			public Holder(T value)
			{
				_value = value;
			}

			public async UniTask<T> Get()
			{
				await UniTask.Yield();
				return _value;
			}
		}

		static async UniTask Generic()
		{
			Same("text", await Echo("text"), "generic method, reference type");
			Same(9, await Echo(9), "generic method, value type");
			Same("held", await new Holder<string>("held").Get(), "method of a generic class, reference type");
			Same(12, await new Holder<long>(12).Get(), "method of a generic class, value type");
		}

		// ---- the collector ----

		static async UniTask<int> HoldsReferences(int seed)
		{
			var text = new string('x', 8 + seed % 5);
			var numbers = new int[16];
			for (var index = 0; index < numbers.Length; index++)
			{
				numbers[index] = seed + index;
			}
			var boxed = (object)seed;
			await UniTask.DelayFrame(3);
			var total = text.Length + (int)boxed;
			foreach (var number in numbers)
			{
				total += number;
			}
			await UniTask.Yield();
			return total;
		}

		static async UniTask SurvivesCollection()
		{
			var tasks = new UniTask<int>[300];
			for (var index = 0; index < tasks.Length; index++)
			{
				tasks[index] = HoldsReferences(index);
			}
			// What the suspended methods hold is reachable only through wherever their state is kept.
			for (var round = 0; round < 2; round++)
			{
				await UniTask.Yield();
				var garbage = new List<byte[]>();
				for (var index = 0; index < 200; index++)
				{
					garbage.Add(new byte[1024]);
				}
				GC.Collect();
				GC.KeepAlive(garbage);
			}
			var results = await UniTask.WhenAll(tasks);
			for (var index = 0; index < results.Length; index++)
			{
				var expected = 8 + index % 5 + index + 16 * index + 120;
				if (results[index] != expected)
				{
					Same(expected, results[index], "result " + index);
				}
			}
		}

		// ---- other awaitables ----

		static async UniTask<int> FromTask(int value)
		{
			var got = await Task.Run(() => value * 2);
			got += await Task.Run(() => value).AsUniTask();
			await UniTask.SwitchToMainThread();
			got += await UniTask.RunOnThreadPool(() => value);
			return OnMainThread ? got : -1;
		}

		static async UniTask TaskInterop()
		{
			for (var index = 1; index <= 20; index++)
			{
				Same(index * 4, await FromTask(index), "value " + index);
			}
		}

		static async UniTask Lambdas()
		{
			Func<int, UniTask<int>> twice = async value =>
			{
				await UniTask.Yield();
				return value * 2;
			};
			var offset = 5;
			Func<int, UniTask<int>> shifted = async value =>
			{
				await UniTask.Yield();
				var first = value + offset;
				await UniTask.Yield();
				return first + offset;
			};
			for (var index = 0; index < 50; index++)
			{
				Same(index * 2, await twice(index), "lambda");
				Same(index + 10, await shifted(index), "lambda with a capture");
			}
			var both = await UniTask.WhenAll(new[] { twice(3), shifted(3), twice(4), shifted(4) });
			Same(6 + 13 + 8 + 14, both[0] + both[1] + both[2] + both[3], "lambdas at once");
		}

		sealed class Account
		{
			int _balance;

			public async UniTask<int> Deposit(int amount)
			{
				await UniTask.Yield();
				_balance += amount;
				await UniTask.Yield();
				return _balance;
			}
		}

		struct Point
		{
			public int X;
			public int Y;

			public async UniTask<int> Sum(int scale)
			{
				await UniTask.Yield();
				return (X + Y) * scale;
			}
		}

		static async UniTask InstanceMethods()
		{
			var account = new Account();
			Same(5, await account.Deposit(5), "first deposit");
			Same(12, await account.Deposit(7), "second deposit");
			var deposits = await UniTask.WhenAll(new[] { account.Deposit(1), account.Deposit(2), account.Deposit(3) });
			Same(18, deposits[2], "deposits at once");
			var point = new Point { X = 2, Y = 3 };
			Same(20, await point.Sum(4), "method of a struct");
		}
	}
}
