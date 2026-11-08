using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;

namespace Dexc.Telemetry.CodeGen
{
	/// <summary>
	/// Chooses methods for the measuring that <c>DexcTelemetry.Watch</c> turns on, in code of the project, beside the
	/// presets. Put a public class with a constructor without parameters in an Editor assembly whose name starts with
	/// <c>Unity.</c> and ends with <c>.CodeGen</c>, and that refers to <c>Unity.Dexc.Telemetry.CodeGen</c>: the IL
	/// processing finds it. It runs while Unity compiles, in a process of its own, so it cannot use UnityEngine or
	/// UnityEditor. It only applies to a build with <c>DEXC_TELEMETRY_WATCH</c>.
	/// </summary>
	public interface ITelemetryMethodSelector
	{
		bool Select(TelemetryMethodCandidate method);
	}

	/// <summary>A method the IL processing could measure.</summary>
	public sealed class TelemetryMethodCandidate
	{
		internal TelemetryMethodCandidate(string assembly, MethodDefinition method)
		{
			var outer = method.DeclaringType;
			while (outer.DeclaringType != null)
			{
				outer = outer.DeclaringType;
			}
			Assembly = assembly;
			Namespace = outer.Namespace;
			Type = method.DeclaringType.FullName.Replace('/', '.');
			Method = method.Name;
			Attributes = method.CustomAttributes.Select(attribute => attribute.AttributeType.FullName).ToList();
			TypeAttributes = method.DeclaringType.CustomAttributes.Select(attribute => attribute.AttributeType.FullName).ToList();
			IsStatic = method.IsStatic;
			IsPublic = method.IsPublic && method.DeclaringType.IsPublic;
			IsAsync = method.CustomAttributes.Any(attribute => attribute.AttributeType.FullName == "System.Runtime.CompilerServices.AsyncStateMachineAttribute");
			ParameterCount = method.Parameters.Count;
		}

		public string Assembly { get; }

		/// <summary>The namespace of the outermost type.</summary>
		public string Namespace { get; }

		/// <summary>The full name of the type, with dots between nested types.</summary>
		public string Type { get; }

		public string Method { get; }

		/// <summary>The name <c>DexcTelemetry.Watch</c> takes: the type, a dot and the method.</summary>
		public string FullName => Type + "." + Method;

		/// <summary>The full names of the attributes of the method.</summary>
		public IReadOnlyList<string> Attributes { get; }

		/// <summary>The full names of the attributes of the type that declares it.</summary>
		public IReadOnlyList<string> TypeAttributes { get; }

		public bool IsStatic { get; }

		/// <summary>Public, in a public type. A nested type counts as not public.</summary>
		public bool IsPublic { get; }

		public bool IsAsync { get; }

		public int ParameterCount { get; }
	}
}
