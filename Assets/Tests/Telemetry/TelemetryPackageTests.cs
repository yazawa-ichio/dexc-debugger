using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Dexc.Telemetry;
using NUnit.Framework;

namespace DexcDebugger.Tests.Telemetry
{
	/// <summary>
	/// Holds the promises that make this a package of its own while it shares a repository with Dexc: it does not know Dexc,
	/// it survives a build Dexc was removed from, and its running state lives on one root.
	/// </summary>
	public sealed class TelemetryPackageTests
	{
		const string RootField = "Dexc.Telemetry.TelemetryRoot.s_Current";
		const string AsmdefPath = "Packages/jp.yazawa-ichio.dexc-telemetry/Runtime/Dexc.Telemetry.asmdef";

		static Assembly Library => typeof(TelemetrySeverity).Assembly;

		static bool IsCompilerGenerated(Type type)
		{
			for (var current = type; current != null; current = current.DeclaringType)
			{
				if (current.Name.StartsWith("<", StringComparison.Ordinal) || current.IsDefined(typeof(CompilerGeneratedAttribute), false))
				{
					return true;
				}
			}
			return false;
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
			Assert.That(offenders, Is.Empty, "Running state must hang off TelemetryRoot so that it is dropped with it.");
		}

		[Test]
		public void TheRootIsReplacedOnReset()
		{
			var before = TelemetryRoot.Current;

			TelemetryRoot.Reset();

			Assert.That(TelemetryRoot.Current, Is.Not.SameAs(before));
		}

		[Test]
		public void ResettingMakesNoRootUntilOneIsUsed()
		{
			var field = typeof(TelemetryRoot).GetField("s_Current", BindingFlags.Static | BindingFlags.NonPublic);

			TelemetryRoot.Reset();

			Assert.That(field.GetValue(null), Is.Null, "a game that never uses telemetry pays for nothing");
			Assert.That(TelemetryRoot.Current, Is.Not.Null);
		}

		[Test]
		public void TheAssemblyDoesNotReferenceDexc()
		{
			var references = Library.GetReferencedAssemblies().Select(name => name.Name);
			Assert.That(references.Where(name => name.StartsWith("Dexc", StringComparison.Ordinal)), Is.Empty);
		}

		[Test]
		public void TheAsmdefNamesNoDexcAssemblyOrSymbol()
		{
			var asmdef = File.ReadAllText(AsmdefPath);
			Assert.That(asmdef, Does.Contain("\"references\": []"), "a reference would tie the package to another assembly");
			Assert.That(asmdef, Does.Contain("\"defineConstraints\": []"), "a constraint such as !DISABLE_DEXC_DEBUGGER would remove telemetry from release builds");
		}

		[TestCase(TelemetrySeverity.Verbose, 1)]
		[TestCase(TelemetrySeverity.Debug, 5)]
		[TestCase(TelemetrySeverity.Info, 9)]
		[TestCase(TelemetrySeverity.Warning, 13)]
		[TestCase(TelemetrySeverity.Error, 17)]
		[TestCase(TelemetrySeverity.Fatal, 21)]
		public void SeverityIsTheOpenTelemetrySeverityNumber(TelemetrySeverity severity, int number)
		{
			Assert.That((int)severity, Is.EqualTo(number));
		}

		[Test]
		public void DetailOrdersFromCoarseToFine()
		{
			Assert.That(TelemetryDetail.Basic, Is.LessThan(TelemetryDetail.Standard));
			Assert.That(TelemetryDetail.Standard, Is.LessThan(TelemetryDetail.Detailed));
		}
	}
}
