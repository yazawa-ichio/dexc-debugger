using System.Collections.Generic;

namespace Dexc.Autopilot
{
	/// <summary>
	/// What a bot remembers. Boards are layered: a read that misses here goes on to the parent, and a write stays here.
	///
	/// The typed accessors are not generic on purpose. A generic method over a value type has to exist in an IL2CPP build
	/// ahead of time, which interpreted code cannot arrange.
	/// </summary>
	public sealed class AutopilotBlackboard
	{
		private readonly Dictionary<string, object> m_Values = new Dictionary<string, object>();
		private readonly AutopilotBlackboard m_Parent;

		public AutopilotBlackboard(AutopilotBlackboard parent = null) => m_Parent = parent;

		public bool Has(string key) => m_Values.ContainsKey(key) || (m_Parent != null && m_Parent.Has(key));

		public object Get(string key) => m_Values.TryGetValue(key, out var value) ? value : m_Parent?.Get(key);

		public void Set(string key, object value) => m_Values[key] = value;

		public void Remove(string key) => m_Values.Remove(key);

		public void Clear() => m_Values.Clear();

		public int GetInt(string key, int fallback = 0) => Get(key) is int value ? value : fallback;

		public float GetFloat(string key, float fallback = 0f) => Get(key) is float value ? value : fallback;

		public bool GetBool(string key, bool fallback = false) => Get(key) is bool value ? value : fallback;

		public string GetString(string key, string fallback = null) => Get(key) as string ?? fallback;

		public void SetInt(string key, int value) => m_Values[key] = value;

		public void SetFloat(string key, float value) => m_Values[key] = value;

		public void SetBool(string key, bool value) => m_Values[key] = value;

		public void SetString(string key, string value) => m_Values[key] = value;
	}
}
