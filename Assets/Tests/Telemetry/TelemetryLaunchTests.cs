using System.Collections.Generic;
using System.Linq;
using Dexc.Telemetry;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DexcDebugger.Tests.Telemetry
{
	public sealed class TelemetryLaunchTests
	{
		static string[] Arguments(params string[] arguments)
		{
			return new[] { "Game.exe" }.Concat(arguments).ToArray();
		}

		static Dictionary<string, string> Map(IEnumerable<KeyValuePair<string, string>> pairs)
		{
			return pairs.ToDictionary(pair => pair.Key, pair => pair.Value);
		}

		[Test]
		public void TheCommandLineGivesTheFolderAndTheResource()
		{
			var arguments = Arguments("-batchmode", "-dexc-telemetry-directory", "D:/ws/a", "-dexc-telemetry-resource", "dexc.build=fix-1234", "-dexc-telemetry-resource", "vcs.ref.head.revision=abc=1", "-dexc-telemetry-resource", "=no key");

			Assert.That(TelemetryLaunch.Directory(arguments), Is.EqualTo("D:/ws/a"));
			Assert.That(Map(TelemetryLaunch.CommandLineResource(arguments)), Is.EqualTo(new Dictionary<string, string>
			{
				["dexc.build"] = "fix-1234",
				["vcs.ref.head.revision"] = "abc=1",
			}));
			Assert.That(TelemetryLaunch.Directory(Arguments("-dexc-telemetry-directory")), Is.Null, "no value after it");
		}

		[Test]
		public void TheEnvironmentIsReadAsTheSpecificationSays()
		{
			var resource = Map(TelemetryLaunch.Merge(TelemetryLaunch.EnvironmentResource("service.name=from-attributes, deployment.environment.name=qa ,note=a%2Cb%3Dc", "from-name")));

			Assert.That(resource["service.name"], Is.EqualTo("from-name"), "OTEL_SERVICE_NAME wins");
			Assert.That(resource["deployment.environment.name"], Is.EqualTo("qa"));
			Assert.That(resource["note"], Is.EqualTo("a,b=c"), "values are percent-decoded");
		}

		[Test]
		public void AnEnvironmentValueThatCannotBeReadIsDiscardedWhole()
		{
			LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("OTEL_RESOURCE_ATTRIBUTES is left out"));

			var resource = TelemetryLaunch.EnvironmentResource("good=1,broken", null);

			Assert.That(resource, Is.Empty);
		}

		[Test]
		public void ALaterLayerWinsAndAKeyKeepsItsPlace()
		{
			var merged = TelemetryLaunch.Merge(
				new[] { new KeyValuePair<string, string>("service.name", "game"), new KeyValuePair<string, string>("device.model.identifier", "pc") },
				null,
				new[] { new KeyValuePair<string, string>("service.name", "launcher"), new KeyValuePair<string, string>("dexc.seed", "7") }
			);

			Assert.That(merged.Select(pair => pair.Key + "=" + pair.Value), Is.EqualTo(new[] { "service.name=launcher", "device.model.identifier=pc", "dexc.seed=7" }));
		}
	}
}
