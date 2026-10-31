using System.Collections.Generic;
using Object = UnityEngine.Object;

namespace Dexc
{
	internal interface IDexcReferenceInput
	{
		InputReferenceState State { get; }
		bool IsOptional { get; }
		DexcReferenceSelection CaptureSelection();
		void RestoreSelection(DexcReferenceSelection selection);
		IEnumerable<DexcReferenceCandidate> GetCandidates();
	}

	internal readonly struct DexcReferenceSelection
	{
		internal DexcReferenceSelection(string key, Object value, bool bound)
		{
			Key = key;
			Value = value;
			Bound = bound;
		}

		internal string Key { get; }
		internal Object Value { get; }
		internal bool Bound { get; }
	}

	internal readonly struct DexcReferenceCandidate
	{
		internal DexcReferenceCandidate(string key, string label, string path, Object value)
		{
			Key = key;
			Label = label;
			Path = path;
			Value = value;
		}

		internal string Key { get; }
		internal string Label { get; }
		internal string Path { get; }
		internal Object Value { get; }
	}

	internal sealed class DexcInputSnapshot
	{
		private readonly IDebugInput m_Input;
		private readonly DebugValue m_Value;
		private readonly DexcReferenceSelection m_Reference;

		internal DexcInputSnapshot(IDebugInput input)
		{
			m_Input = input;
			m_Value = input.GetValue();
			if (input is IDexcReferenceInput reference)
			{
				m_Reference = reference.CaptureSelection();
			}
		}

		internal void Restore()
		{
			if (m_Input is IDexcReferenceInput reference)
			{
				reference.RestoreSelection(m_Reference);
			}
			else
			{
				m_Input.Apply(m_Value);
			}
		}
	}
}
