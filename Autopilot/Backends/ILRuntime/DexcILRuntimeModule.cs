using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using ILRuntime.Runtime.Enviorment;
using UnityEngine.Scripting;
using ILRuntimeDomain = ILRuntime.Runtime.Enviorment.AppDomain;

namespace Dexc.Autopilot
{
	/// <summary>The VM that runs ordinary C#. Each assembly gets a domain of its own, so one that breaks takes nothing with it.</summary>
	public sealed class ILRuntimeBackend : IAutopilotBackend
	{
		/// <summary>Instructions one call may run before it is stopped, about a second of a tight loop.</summary>
		private const long StepLimit = 50_000_000;

		private readonly Action<DelegateManager> m_Delegates;

		/// <param name="delegates">Registers the delegate types compiled code receives from a bot.</param>
		public ILRuntimeBackend(Action<DelegateManager> delegates = null) => m_Delegates = delegates;

		public string Name => "ilruntime";

		public IAutopilotModule Load(byte[] assembly)
		{
			var image = new MemoryStream(assembly);
			var domain = new ILRuntimeDomain { StepLimit = StepLimit };
			domain.RegisterCrossBindingAdaptor(new IAsyncStateMachineClassInheritanceAdaptor());
			domain.RegisterCrossBindingAdaptor(new CoroutineAdaptor());
			m_Delegates?.Invoke(domain.DelegateManager);
			domain.LoadAssembly(image);
			return new Module(domain, image);
		}

		private sealed class Module : IAutopilotModule
		{
			private static readonly string[] s_NoMembers = new string[0];

			private readonly ILRuntimeDomain m_Domain;
			private readonly MemoryStream m_Image;

			public Module(ILRuntimeDomain domain, MemoryStream image)
			{
				m_Domain = domain;
				m_Image = image;
			}

			public IReadOnlyList<string> UnboundMembers => s_NoMembers;

			public object Invoke(string type, string method, params object[] arguments) =>
				m_Domain.Invoke(type, method, null, arguments);

			public void Dispose()
			{
				m_Domain.Dispose();
				m_Image.Dispose();
			}
		}

		/// <summary>
		/// Never called. IL2CPP generates a generic method only for the instantiations compiled code names, and the
		/// interpreter reaches these by reflection, so they are named here.
		/// </summary>
		[Preserve]
		private static void GenericHints()
		{
			IAsyncStateMachineClassInheritanceAdaptor.IAsyncStateMachineAdaptor machine = null;
			var task = default(TaskAwaiter);
			var taskOfInt = default(TaskAwaiter<int>);
			var yielded = default(YieldAwaitable.YieldAwaiter);
			var plain = default(AsyncTaskMethodBuilder);
			plain.Start(ref machine);
			plain.AwaitUnsafeOnCompleted(ref task, ref machine);
			plain.AwaitUnsafeOnCompleted(ref taskOfInt, ref machine);
			plain.AwaitUnsafeOnCompleted(ref yielded, ref machine);
			var ofInt = default(AsyncTaskMethodBuilder<int>);
			ofInt.Start(ref machine);
			ofInt.AwaitUnsafeOnCompleted(ref task, ref machine);
			ofInt.AwaitUnsafeOnCompleted(ref taskOfInt, ref machine);
			ofInt.AwaitUnsafeOnCompleted(ref yielded, ref machine);
			var none = default(AsyncVoidMethodBuilder);
			none.Start(ref machine);
			none.AwaitUnsafeOnCompleted(ref task, ref machine);
			none.AwaitUnsafeOnCompleted(ref taskOfInt, ref machine);
			none.AwaitUnsafeOnCompleted(ref yielded, ref machine);
		}
	}
}
