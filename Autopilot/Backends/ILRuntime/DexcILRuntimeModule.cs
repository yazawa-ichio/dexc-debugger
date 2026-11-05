using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using UnityEngine.Scripting;
using ILRuntimeDomain = ILRuntime.Runtime.Enviorment.AppDomain;

namespace Dexc.Autopilot
{
	/// <summary>One assembly of IL running on ILRuntime. The same shape as the Pipeline module, so a run can use either.</summary>
	public sealed class DexcILRuntimeModule : IDisposable
	{
		/// <summary>Instructions one call may run before it is stopped, about a second of a tight loop.</summary>
		private const long StepLimit = 50_000_000;

		private readonly ILRuntimeDomain m_Domain;
		private readonly MemoryStream m_Image;

		private DexcILRuntimeModule(ILRuntimeDomain domain, MemoryStream image)
		{
			m_Domain = domain;
			m_Image = image;
		}

		/// <param name="assembly">The assembly as it sits on disk.</param>
		/// <param name="delegates">Registers the delegate types compiled code will receive from the assembly.</param>
		public static DexcILRuntimeModule Load(byte[] assembly, Action<ILRuntime.Runtime.Enviorment.DelegateManager> delegates = null)
		{
			var image = new MemoryStream(assembly);
			var domain = new ILRuntimeDomain { StepLimit = StepLimit };
			domain.RegisterCrossBindingAdaptor(new IAsyncStateMachineClassInheritanceAdaptor());
			domain.RegisterCrossBindingAdaptor(new CoroutineAdaptor());
			delegates?.Invoke(domain.DelegateManager);
			domain.LoadAssembly(image);
			return new DexcILRuntimeModule(domain, image);
		}

		public object Invoke(string type, string method, params object[] arguments) => m_Domain.Invoke(type, method, null, arguments);

		public void Dispose()
		{
			m_Domain.Dispose();
			m_Image.Dispose();
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
