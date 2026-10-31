using System.Linq;
using System.Xml.Linq;
using Dexc;
using NUnit.Framework;

namespace DexcDebugger.Tests
{
	public sealed class DexcUGUILinkerTests
	{
		[TestCase("DEXC_DEBUGGER", true, true)]
		[TestCase("OTHER;DEXC_DEBUGGER", true, true)]
		[TestCase("DEXC_DEBUGGER", false, false)]
		[TestCase("", true, false)]
		[TestCase("DISABLE_DEXC_DEBUGGER", true, false)]
		[TestCase("DEXC_DEBUGGER;DISABLE_DEXC_DEBUGGER", true, false)]
		public void RetentionOnlyAppliesToEnabledBuildsWithUGUI(string symbols, bool present, bool expected)
		{
			Assert.That(DexcUGUILinkerProcessor.ShouldPreserve(symbols, present), Is.EqualTo(expected));
		}

		[Test]
		public void RetainsOnlyReflectedColorPropertiesAndAllowsMissingOptionalAssembly()
		{
			var assembly = XDocument.Parse(DexcUGUILinkerProcessor.LinkXml).Root.Elements("assembly").Single();
			Assert.That((string)assembly.Attribute("fullname"), Is.EqualTo("UnityEngine.UI"));
			Assert.That((string)assembly.Attribute("ignoreIfMissing"), Is.EqualTo("1"));
			var type = assembly.Elements("type").Single();
			Assert.That((string)type.Attribute("fullname"), Is.EqualTo("UnityEngine.UI.ColorBlock"));
			Assert.That((string)type.Attribute("preserve"), Is.EqualTo("nothing"));
			Assert.That(
				type.Elements("property").Select(property => (string)property.Attribute("name")),
				Is.EquivalentTo(
					new[]
					{
						"normalColor",
						"highlightedColor",
						"pressedColor",
						"selectedColor",
						"disabledColor",
						"colorMultiplier",
						"fadeDuration",
					}
				)
			);
		}
	}
}
