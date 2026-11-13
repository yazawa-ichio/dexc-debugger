using System;

namespace Unity.IL2CPP.CompilerServices
{
	/// <summary>
	/// The checks IL2CPP can be told to leave out of the C++ it generates. A copy of the file in the Editor's il2cpp
	/// folder (Il2CppSetOptionAttribute.cs), internal to this assembly: il2cpp finds the attribute by its name.
	/// </summary>
	internal enum Option
	{
		/// <summary>Null checks. Without them, code that reads through null crashes instead of throwing.</summary>
		NullChecks = 1,

		/// <summary>Array bounds checks. Without them, an index outside the array reads or writes other memory.</summary>
		ArrayBoundsChecks = 2,

		/// <summary>Divide by zero checks. Off unless turned on.</summary>
		DivideByZeroChecks = 3,
	}

	/// <summary>Turns one of the checks on or off for an assembly, a type, a method or a property.</summary>
	[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Struct | AttributeTargets.Class | AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Delegate, Inherited = false, AllowMultiple = true)]
	internal sealed class Il2CppSetOptionAttribute : Attribute
	{
		public Option Option { get; private set; }

		public object Value { get; private set; }

		public Il2CppSetOptionAttribute(Option option, object value)
		{
			Option = option;
			Value = value;
		}
	}
}
