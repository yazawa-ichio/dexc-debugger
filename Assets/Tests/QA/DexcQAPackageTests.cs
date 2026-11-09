using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Dexc.QA;
using NUnit.Framework;

namespace DexcDebugger.Tests.QA
{
	/// <summary>The rules Dexc QA keeps as a package of its own.</summary>
	public sealed class DexcQAPackageTests
	{
		const string RootField = "Dexc.QA.QARoot.s_Current";

		static Assembly Library => typeof(DexcQA).Assembly;

		static bool IsCompilerGenerated(System.Type type)
		{
			return type.GetCustomAttribute<CompilerGeneratedAttribute>() != null || type.FullName.Contains("<");
		}

		[Test]
		public void OnlyTheRootInstanceIsStoredInAStaticField()
		{
			var offenders = new List<string>();
			foreach (var type in Library.GetTypes().Where(type => !IsCompilerGenerated(type)))
			{
				const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
				foreach (var field in type.GetFields(flags).Where(field => !field.IsLiteral))
				{
					var name = type.FullName + "." + field.Name;
					if (name != RootField)
					{
						offenders.Add(name);
					}
				}
			}
			Assert.That(offenders, Is.Empty, "Running state must hang off QARoot so that it is dropped with it.");
		}

		[Test]
		public void TheLibraryDependsOnDexcAndOptionallyOnTelemetryOnly()
		{
			var references = Library.GetReferencedAssemblies().Select(name => name.Name).Where(name => name.StartsWith("Dexc.")).OrderBy(name => name);

			Assert.That(references, Is.SubsetOf(new[] { "Dexc.Runtime", "Dexc.Telemetry" }));
		}
	}
}
