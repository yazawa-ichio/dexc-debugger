using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Dexc
{
	internal sealed class DexcCommandReferenceStore
	{
		private readonly DebugSessionId m_SessionId;
		private readonly Dictionary<DebugReferenceHandle, DexcReferenceSelection> m_Entries =
			new Dictionary<DebugReferenceHandle, DexcReferenceSelection>();
		private readonly Dictionary<Object, Dictionary<string, DebugReferenceHandle>> m_Handles = new Dictionary<
			Object,
			Dictionary<string, DebugReferenceHandle>
		>(DexcCommandRuntime.ReferenceEqualityComparer<Object>.Instance);
		private long m_Next;

		internal DexcCommandReferenceStore(DebugSessionId sessionId)
		{
			m_SessionId = sessionId;
		}

		internal DebugReferenceHandle GetOrAdd(string key, Object value)
		{
			if (value == null)
			{
				return default(DebugReferenceHandle);
			}
			if (!m_Handles.TryGetValue(value, out var keys))
			{
				keys = new Dictionary<string, DebugReferenceHandle>(StringComparer.Ordinal);
				m_Handles.Add(value, keys);
			}
			if (!keys.TryGetValue(key, out var handle))
			{
				handle = DebugReferenceHandle.Create(m_SessionId, ++m_Next);
				keys.Add(key, handle);
				m_Entries.Add(handle, new DexcReferenceSelection(key, value, true));
			}
			return handle;
		}

		internal bool TryResolve(DebugReferenceHandle handle, out DexcReferenceSelection selection)
		{
			return m_Entries.TryGetValue(handle, out selection) && selection.Value != null;
		}

		internal void PruneDestroyed()
		{
			foreach (var target in m_Handles.Keys.Where(value => value == null).ToArray())
			{
				foreach (var handle in m_Handles[target].Values)
				{
					m_Entries.Remove(handle);
				}
				m_Handles.Remove(target);
			}
		}

		internal void Clear()
		{
			m_Entries.Clear();
			m_Handles.Clear();
		}
	}
}
