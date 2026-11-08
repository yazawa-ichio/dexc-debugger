using System;
using System.Collections.Generic;
using IlInterpreter;
using IlInterpreter.Interpreter;
using UnityEngine;

namespace Dexc.Autopilot.Bridge
{
	/// <summary>
	/// One assembly of IL running on the Pipeline package's interpreter.
	///
	/// This file compiles into Unity.Pipeline.IlInterpreter through the .asmref beside it, because the interpreter is
	/// internal to that assembly. It depends on nothing else in the package, which is what lets a Player leave the rest
	/// out. Everything here is public so the game's autopilot assembly never sees an interpreter type.
	/// </summary>
	public sealed class DexcIlModule : IDisposable
	{
		private sealed class Image : IScript
		{
			public Image(byte[] bytes) => Il = bytes;

			public string Name => "DexcAutopilot";
			public ReadOnlyMemory<byte> Il { get; }
		}

		private readonly ScriptInterpreter m_Interpreter;

		private DexcIlModule(ScriptInterpreter interpreter, List<string> bindFailures)
		{
			m_Interpreter = interpreter;
			BindFailures = bindFailures;
		}

		/// <summary>Host members the assembly refers to that did not bind. Reaching one throws.</summary>
		public IReadOnlyList<string> UnboundMembers => m_Interpreter.UnboundHostMembers;

		/// <summary>Types that could not be opened up for private access, and why.</summary>
		public IReadOnlyList<string> BindFailures { get; }

		/// <param name="assembly">The assembly as it sits on disk.</param>
		/// <param name="openTypes">Types whose private members the assembly may reach.</param>
		public static DexcIlModule Load(byte[] assembly, IEnumerable<Type> openTypes)
		{
			var binding = CreateBinding();
			var failures = new List<string>();
			foreach (var type in openTypes)
			{
				try
				{
					binding = binding.AllowType(type).AllowNonPublicInstanceMembers(type);
				}
				catch (Exception exception)
				{
					failures.Add(type.FullName + ": " + exception.Message);
				}
			}

			var interpreter = new ScriptInterpreter(binding, message => Debug.Log(message));
			interpreter.Load(new Image(assembly));
			return new DexcIlModule(interpreter, failures);
		}

		public object Invoke(string method, params object[] arguments) => m_Interpreter.Invoke(method, arguments);

		public void Dispose() => m_Interpreter.Dispose();

		/// <summary>
		/// The BCL shims, the types a null check on a Unity object needs, and every other type by name on first use.
		/// The value types are named because the interpreter keeps them in flat slots only when told about them up front.
		/// </summary>
		private static HostBinding CreateBinding()
		{
			HostBinding.Warn = message => Debug.LogWarning(message);
			var binding = new HostBinding()
				.AllowBcl()
				.AllowType<UnityEngine.Object>()
				.AllowType<GameObject>()
				.AllowType<Transform>()
				.AllowType<Vector2>()
				.AllowTypeStruct<Vector3>()
				.AllowType<Quaternion>()
				.AllowType(typeof(Mathf))
				.AllowType(typeof(Time))
				.AllowType(typeof(Debug))
				.AllowType(typeof(System.Collections.IEnumerator))
				.AllowType(typeof(string))
				.AllowType(typeof(Convert))
				.AllowConstructor(
					"Vector2",
					(_, arguments) => (object)new Vector2(Convert.ToSingle(arguments[0]), Convert.ToSingle(arguments[1])),
					2
				);
			binding.AutoBindResolver = Resolve;
			return binding;
		}

		private static Type Resolve(string fullName)
		{
			foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
			{
				var name = assembly.GetName().Name ?? string.Empty;
				if (name == "mscorlib" || name == "netstandard" || name == "System" || name.StartsWith("System.", StringComparison.Ordinal))
					continue;
				var type = assembly.GetType(fullName, false);
				if (type != null)
					return type;
			}

			return null;
		}
	}
}
