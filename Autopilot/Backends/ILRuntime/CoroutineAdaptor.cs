using System;
using System.Collections;
using System.Collections.Generic;
using ILRuntime.CLR.Method;
using ILRuntime.Runtime.Enviorment;
using ILRuntime.Runtime.Intepreter;
using ILRuntimeDomain = ILRuntime.Runtime.Enviorment.AppDomain;

namespace Dexc.Autopilot
{
	/// <summary>Lets an iterator written in interpreted code be driven by compiled code, such as StartCoroutine.</summary>
	public sealed class CoroutineAdaptor : CrossBindingAdaptor
	{
		public override Type BaseCLRType => null;
		public override Type[] BaseCLRTypes => new[] { typeof(IEnumerator<object>), typeof(IEnumerator), typeof(IDisposable) };
		public override Type AdaptorType => typeof(Iterator);

		public override object CreateCLRInstance(ILRuntimeDomain appdomain, ILTypeInstance instance) => new Iterator(appdomain, instance);

		public sealed class Iterator : IEnumerator<object>, CrossBindingAdaptorType
		{
			private readonly ILRuntimeDomain m_Domain;
			private readonly ILTypeInstance m_Instance;
			private readonly IMethod m_MoveNext;
			private readonly IMethod m_Current;
			private readonly IMethod m_Dispose;
			private readonly IMethod m_Reset;

			public Iterator(ILRuntimeDomain domain, ILTypeInstance instance)
			{
				m_Domain = domain;
				m_Instance = instance;
				m_MoveNext = Find("MoveNext");
				m_Current = Find(
					"get_Current",
					"System.Collections.IEnumerator.get_Current",
					"System.Collections.Generic.IEnumerator<System.Object>.get_Current"
				);
				m_Dispose = Find("Dispose", "System.IDisposable.Dispose");
				m_Reset = Find("Reset", "System.Collections.IEnumerator.Reset");
			}

			public ILTypeInstance ILInstance => m_Instance;
			public object Current => m_Domain.Invoke(m_Current, m_Instance, null);

			public bool MoveNext() => (bool)m_Domain.Invoke(m_MoveNext, m_Instance, null);

			public void Reset()
			{
				if (m_Reset != null)
					m_Domain.Invoke(m_Reset, m_Instance, null);
			}

			public void Dispose()
			{
				if (m_Dispose != null)
					m_Domain.Invoke(m_Dispose, m_Instance, null);
			}

			private IMethod Find(params string[] names)
			{
				foreach (var name in names)
				{
					var method = m_Instance.Type.GetMethod(name, 0);
					if (method != null)
						return method;
				}

				return null;
			}
		}
	}
}
