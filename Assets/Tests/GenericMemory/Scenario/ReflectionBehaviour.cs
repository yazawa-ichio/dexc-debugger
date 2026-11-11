using System.Collections;
using UnityEngine;
using UnityEngine.Scripting;

namespace GenericMemory
{
	/// <summary>
	/// A component whose messages are all methods of a generic base class: the engine finds them by name in an instance
	/// of that class (Awake, OnEnable, Start, Update, and the ones SendMessage, Invoke and StartCoroutine name).
	/// </summary>
	public abstract class ReflectionBehaviourBase<T> : MonoBehaviour
	{
		int _awakes;
		int _enables;
		int _starts;
		int _updates;
		int _pings;
		int _laters;
		int _routines;

		[Preserve]
		public T Kept;

		[Preserve]
		void Awake()
		{
			_awakes++;
		}

		[Preserve]
		void OnEnable()
		{
			_enables++;
		}

		[Preserve]
		void Start()
		{
			_starts++;
		}

		[Preserve]
		void Update()
		{
			_updates++;
		}

		[Preserve]
		void Ping(int value)
		{
			_pings += value;
		}

		[Preserve]
		void Later()
		{
			_laters++;
		}

		[Preserve]
		IEnumerator Routine()
		{
			_routines++;
			yield return null;
			_routines++;
		}

		/// <summary>How often each message arrived. Update is only said to have arrived or not: its count depends on the frame.</summary>
		public string Counts()
		{
			return "Awake " + _awakes + ", OnEnable " + _enables + ", Start " + _starts + ", Update " + (_updates > 0)
				+ ", Ping " + _pings + ", Later " + _laters + ", Routine " + _routines;
		}
	}

	public sealed class ReflectionBehaviour : ReflectionBehaviourBase<ReflectionBehaviour.Probe>
	{
		public sealed class Probe
		{
		}
	}
}
