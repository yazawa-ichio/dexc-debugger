#if DEXC_DEBUGGER
using System;
using System.Collections;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Dexc;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using DebuggerRuntime = Dexc.DexcDebugger;

namespace DexcDebugger.Tests
{
	public sealed class DexcEditorExecutionLifetimeTests
	{
		private DexcDebuggerWindow m_Window;

		[UnitySetUp]
		public IEnumerator SetUp()
		{
			yield return new EnterPlayMode();
			DebuggerRuntime.ResetForTests();
			DebuggerRuntime.Initialize();
			m_Window = ScriptableObject.CreateInstance<DexcDebuggerWindow>();
			m_Window.position = new Rect(0, 0, 1100, 700);
			m_Window.ShowUtility();
			m_Window.ProcessEditorUpdateForTests();
		}

		[UnityTearDown]
		public IEnumerator TearDown()
		{
			if (m_Window != null)
			{
				m_Window.Close();
			}
			DebuggerRuntime.ResetForTests();
			yield return new ExitPlayMode();
		}

		[UnityTest]
		public IEnumerator FaultAndDisposeFailureDoNotBlockUpdateOrNextResult()
		{
			var task = Task.FromException<CommandExecutionResult>(new InvalidOperationException("task-fault"));
			Inject(task, true);
			Assert.DoesNotThrow(m_Window.ProcessEditorUpdateForTests);
			AssertDetached();
			Assert.That(m_Window.CommandNoticeForTests, Does.Contain("task-fault").And.Contain("dispose-fault"));
			var expected = new CommandExecutionResult(
				"test",
				"test",
				CommandExecutionStatus.Succeeded,
				CommandFailureKind.None,
				string.Empty,
				string.Empty,
				default(DebugDiagnosticId),
				DateTimeOffset.UtcNow,
				TimeSpan.Zero,
				null
			);
			Inject(Task.FromResult(expected), false);
			Assert.DoesNotThrow(m_Window.ProcessEditorUpdateForTests);
			AssertDetached();
			Assert.That(m_Window.LastCommandExecutionForTests, Is.SameAs(expected));
			m_Window.Repaint();
			yield return null;
			yield return null;
			LogAssert.NoUnexpectedReceived();
		}

		[UnityTest]
		public IEnumerator InvalidationAndWindowCloseReleasePendingFaultsDespiteCallbackFailure()
		{
			var completion = new TaskCompletionSource<CommandExecutionResult>();
			Inject(completion.Task, true);
			Assert.DoesNotThrow(() => DebuggerRuntime.Initialize());
			AssertDetached();
			Assert.That(m_Window.SelectedDraftIsValidForTests, Is.False);
			completion.SetException(new InvalidOperationException("late-session-fault"));
			m_Window.ProcessEditorUpdateForTests();
			Assert.That(m_Window.LastCommandExecutionForTests, Is.Null);
			var closeCompletion = new TaskCompletionSource<CommandExecutionResult>();
			var source = Inject(closeCompletion.Task, true);
			Assert.DoesNotThrow(m_Window.Close);
			Assert.Throws<ObjectDisposedException>(() =>
			{
				var token = source.Token;
			});
			closeCompletion.SetException(new InvalidOperationException("late-close-fault"));
			m_Window = null;
			yield return null;
			LogAssert.NoUnexpectedReceived();
		}

		private CancellationTokenSource Inject(Task<CommandExecutionResult> task, bool disposalFault)
		{
			var source = new CancellationTokenSource();
			if (disposalFault)
			{
				source.Token.Register(() =>
				{
					throw new InvalidOperationException("dispose-fault");
				});
			}
			Field("m_CommandExecution").SetValue(m_Window, new EditorLifetimeExecution(source, task));
			Field("m_CommandExecutionTask").SetValue(m_Window, task);
			return source;
		}

		private void AssertDetached()
		{
			Assert.That(Field("m_CommandExecution").GetValue(m_Window), Is.Null);
			Assert.That(Field("m_CommandExecutionTask").GetValue(m_Window), Is.Null);
		}

		private static FieldInfo Field(string name) =>
			typeof(DexcDebuggerWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);

		private sealed class EditorLifetimeExecution : DebugCommandExecution
		{
			private readonly Task<CommandExecutionResult> m_Task;

			internal EditorLifetimeExecution(CancellationTokenSource source, Task<CommandExecutionResult> task)
				: base("editor-lifetime-test", source)
			{
				m_Task = task;
			}

			public override DebugCommandExecutionState State => DebugCommandExecutionState.Running;

			public override ValueTask<CommandExecutionResult> WaitAsync() => new ValueTask<CommandExecutionResult>(m_Task);
		}
	}
}
#endif
