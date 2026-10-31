#if DEXC_DEBUGGER
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Dexc;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using DebuggerRuntime = Dexc.DexcDebugger;

namespace DexcDebugger.Tests
{
	public sealed class DexcEditorIntegerParserTests
	{
		[TestCase(typeof(float), "3.4028234663852886E+38", "3.402824E+38")]
		[TestCase(typeof(double), "1.7976931348623157E+308", "1e309")]
		public void FloatingBoundariesAndGrammar(Type type, string maximum, string overflow)
		{
			var previous = CultureInfo.CurrentCulture;
			try
			{
				foreach (var culture in new[] { "ja-JP", "en-US", "de-DE" })
				{
					CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
					foreach (var valid in new[] { maximum, "-" + maximum, "1.", "2.5", "1e-3" })
						Assert.That(DexcEditorNumericInput.Parse(type, DebugValueKind.FloatingPoint, valid), Is.Not.Null, valid);
					foreach (var invalid in new[] { overflow, "-" + overflow, "", "-", "1e-", "2,5", "1,000", "NaN", "Infinity" })
						Assert.That(DexcEditorNumericInput.Parse(type, DebugValueKind.FloatingPoint, invalid), Is.Null, invalid);
				}
			}
			finally
			{
				CultureInfo.CurrentCulture = previous;
			}
		}

		[TestCase(typeof(sbyte), "-128", "127", "-129", "128", false)]
		[TestCase(typeof(byte), "0", "255", "-1", "256", true)]
		[TestCase(typeof(short), "-32768", "32767", "-32769", "32768", false)]
		[TestCase(typeof(ushort), "0", "65535", "-1", "65536", true)]
		[TestCase(typeof(int), "-2147483648", "2147483647", "-2147483649", "2147483648", false)]
		[TestCase(typeof(uint), "0", "4294967295", "-1", "4294967296", true)]
		[TestCase(typeof(long), "-9223372036854775808", "9223372036854775807", "-9223372036854775809", "9223372036854775808", false)]
		[TestCase(typeof(ulong), "0", "18446744073709551615", "-1", "18446744073709551616", true)]
		public void IntegerBoundariesAndGrammar(Type type, string minimum, string maximum, string below, string above, bool unsigned)
		{
			var previous = CultureInfo.CurrentCulture;
			try
			{
				var kind = unsigned ? DebugValueKind.UnsignedInteger : DebugValueKind.SignedInteger;
				foreach (var culture in new[] { "ja-JP", "en-US", "de-DE" })
				{
					CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
					foreach (var valid in new[] { minimum, maximum })
					{
						var value = DexcEditorNumericInput.Parse(type, kind, valid);
						Assert.That(value, Is.Not.Null);
						Assert.That(value.Kind, Is.EqualTo(kind));
						Assert.That(DexcInputText.Format(value), Is.EqualTo(valid));
					}
					foreach (var invalid in new[] { below, above, "", "-", "1,000", "2.5", "1e3" })
						Assert.That(DexcEditorNumericInput.Parse(type, kind, invalid), Is.Null, invalid);
				}
			}
			finally
			{
				CultureInfo.CurrentCulture = previous;
			}
		}
	}

	public sealed class DexcEditorIntegerTests
	{
		private DexcDebuggerWindow m_Window;
		private readonly Dictionary<string, Rect> m_Controls = new Dictionary<string, Rect>();
		private CultureInfo m_Culture;
		private IMECompositionMode m_ImeMode;
		private string m_RequestedFocus;

		[UnitySetUp]
		public IEnumerator SetUp()
		{
			yield return new EnterPlayMode();
			DebuggerRuntime.ResetForTests();
			DebuggerRuntime.Initialize();
			m_Culture = CultureInfo.CurrentCulture;
			m_ImeMode = Input.imeCompositionMode;
			Input.imeCompositionMode = IMECompositionMode.Off;
			m_Controls.Clear();
			m_Window = EditorWindow.GetWindow<DexcDebuggerWindow>(true, "Numeric input tests", false);
			m_Window.position = new Rect(0, 0, 1100, 800);
			m_Window.ProcessEditorUpdateForTests();
			m_Window.CommandNumericInputForTests.ControlDrawnForTests = ObserveControl;
			m_Window.SettingNumericInputForTests.ControlDrawnForTests = ObserveControl;
		}

		[UnityTearDown]
		public IEnumerator TearDown()
		{
			CultureInfo.CurrentCulture = m_Culture;
			Input.imeCompositionMode = m_ImeMode;
			if (m_Window != null)
				m_Window.Close();
			DebuggerRuntime.ResetForTests();
			if (EditorApplication.isPlaying)
				yield return new ExitPlayMode();
		}

		[UnityTest]
		public IEnumerator CommandUnsignedFullRangeAndInvalidNativeEditingPreserveDraft()
		{
			var command = DebugCommand
				.Registry.GetList(new DebugListQuery("Tests/ZZFIX03B", string.Empty, new DebugPageRequest(0, 8)))
				.Items.Single();
			DebugCommand.Registry.TryGetDescriptor(command.Id, out var descriptor);
			var unsigned = descriptor.Fields.Single(field => field.Name == "Unsigned");
			var signed = descriptor.Fields.Single(field => field.Name == "Signed");
			var nullable = descriptor.Fields.Single(field => field.Name == "Nullable");
			foreach (var culture in new[] { "ja-JP", "en-US", "de-DE" })
			{
				CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
				m_Window.SelectCommandForTests(command.Id);
				m_Window.UpdateCommandInputForTests(unsigned.Id, DebugValue.From(ulong.MaxValue));
				var version = DraftVersion();
				yield return Repaint();
				yield return Repaint();
				Assert.That(DraftVersion(), Is.EqualTo(version), "Repaint must not change the draft");
				Assert.That(m_Window.CommandValueForTests(unsigned.Id), Is.EqualTo(DebugValue.From(ulong.MaxValue)));
				Assert.That(m_Window.CommandValueForTests(nullable.Id), Is.EqualTo(DebugValue.Null));
				EditorIntegerCommand.s_Count = 0;
				yield return Edit(unsigned.Id.ToString(), "18446744073709551616");
				yield return Repaint();
				Assert.That(m_Window.CommandNumericInputForTests.IsValid, Is.False);
				Click(m_Window.CommandPrimaryRectForTests);
				m_Window.ExecuteSelectedCommandForTests();
				Assert.That(EditorIntegerCommand.s_Count, Is.Zero);
				yield return Edit(unsigned.Id.ToString(), "-1");
				Assert.That(m_Window.CommandExecutionEnabledForTests, Is.False);
				yield return Edit(unsigned.Id.ToString(), "18446744073709551615");
				yield return Repaint();
				yield return Edit(signed.Id.ToString(), "2147483648");
				Assert.That(m_Window.CommandExecutionEnabledForTests, Is.False);
				yield return Edit(signed.Id.ToString(), "-2147483648");
				yield return Repaint();
				yield return Edit(nullable.Id.ToString(), "12");
				Assert.That(m_Window.CommandValueForTests(nullable.Id), Is.EqualTo(DebugValue.From(12)));
				yield return Repaint();
				Assert.That(
					m_Window.CommandExecutionEnabledForTests,
					Is.True,
					"unsigned="
						+ m_Window.CommandNumericInputForTests.TextForTests(unsigned.Id.ToString())
						+ " signed="
						+ m_Window.CommandNumericInputForTests.TextForTests(signed.Id.ToString())
						+ " nullable="
						+ m_Window.CommandNumericInputForTests.TextForTests(nullable.Id.ToString())
				);
				Click(m_Window.CommandPrimaryRectForTests);
				Assert.That(EditorIntegerCommand.s_Count, Is.EqualTo(1));
				Assert.That(EditorIntegerCommand.s_Unsigned, Is.EqualTo(ulong.MaxValue));
				Assert.That(EditorIntegerCommand.s_Signed, Is.EqualTo(int.MinValue));
				yield return Repaint();
				yield return Edit(unsigned.Id.ToString(), "-");
				m_Window.SelectCommandForTests(command.Id);
				Assert.That(m_Window.CommandNumericInputForTests.IsValid, Is.True);
			}
		}

		[UnityTest]
		public IEnumerator AllSettingIntegerKindsSurviveRepaintAndWriteTheirEntireRange()
		{
			var names = new[] { "SByte", "Byte", "Int16", "UInt16", "Int32", "UInt32", "Int64", "UInt64" };
			var minima = new[]
			{
				DebugValue.From(sbyte.MinValue),
				DebugValue.From(byte.MinValue),
				DebugValue.From(short.MinValue),
				DebugValue.From(ushort.MinValue),
				DebugValue.From(int.MinValue),
				DebugValue.From(uint.MinValue),
				DebugValue.From(long.MinValue),
				DebugValue.From(ulong.MinValue),
			};
			var maxima = new[]
			{
				DebugValue.From(sbyte.MaxValue),
				DebugValue.From(byte.MaxValue),
				DebugValue.From(short.MaxValue),
				DebugValue.From(ushort.MaxValue),
				DebugValue.From(int.MaxValue),
				DebugValue.From(uint.MaxValue),
				DebugValue.From(long.MaxValue),
				DebugValue.From(ulong.MaxValue),
			};
			for (var index = 0; index < names.Length; index++)
			{
				var setting = DebugSetting
					.GetList(new DebugListQuery(string.Empty, "Tests/Integer/" + names[index], new DebugPageRequest(0, 8)))
					.Items.Single();
				Assert.That(DebugSetting.Write(setting.Id, maxima[index]).Status, Is.EqualTo(DebugOperationStatus.Succeeded));
				m_Window.ShowSettingDetailForTests(setting.Id);
				var history = DebugSetting.GetHistory(setting.Id, new DebugPageRequest(0, 100)).Entries.Count;
				yield return Repaint();
				yield return Repaint();
				Assert.That(m_Window.SettingEditedValueForTests, Is.EqualTo(maxima[index]), names[index]);
				Assert.That(
					DebugSetting.GetHistory(setting.Id, new DebugPageRequest(0, 100)).Entries.Count,
					Is.EqualTo(history),
					"Repaint must not write " + names[index]
				);
				yield return Edit("setting", DexcInputText.Format(minima[index]));
				yield return Repaint();
				Assert.That(m_Window.SettingEditedValueForTests, Is.EqualTo(minima[index]));
				Assert.That(
					DebugSetting.GetHistory(setting.Id, new DebugPageRequest(0, 100)).Entries.Count,
					Is.EqualTo(history),
					"Immediate numbers wait for Enter or focus loss"
				);
				SendKey(KeyCode.Return);
				yield return Repaint();
				Assert.That(DebugSetting.Read(setting.Id).TryGetValue(out var actual), Is.True);
				Assert.That(
					actual,
					Is.EqualTo(minima[index]),
					names[index]
						+ " actual="
						+ DexcInputText.Format(actual)
						+ " apply="
						+ m_Window.SettingApplyRectForTests
						+ " valid="
						+ m_Window.SettingNumericInputForTests.IsValid
				);
				yield return Edit("setting", "18446744073709551616");
				yield return Repaint();
				Assert.That(m_Window.SettingNumericInputForTests.IsValid, Is.False);
				history = DebugSetting.GetHistory(setting.Id, new DebugPageRequest(0, 100)).Entries.Count;
				Click(m_Window.SettingApplyRectForTests);
				Assert.That(m_Window.WriteSelectedSettingForTests(maxima[index]), Is.Null);
				Assert.That(DebugSetting.GetHistory(setting.Id, new DebugPageRequest(0, 100)).Entries.Count, Is.EqualTo(history));
			}
			DebuggerRuntime.ResetForTests();
			m_Window.ProcessEditorUpdateForTests();
			Assert.That(m_Window.SettingNumericInputForTests.IsValid, Is.True);
		}

		[UnityTest]
		public IEnumerator FloatingCommandPreservesRawTextAndRejectsInvalidComponents()
		{
			var command = DebugCommand
				.Registry.GetList(new DebugListQuery("Tests/ZZFIX03C2", string.Empty, new DebugPageRequest(0, 8)))
				.Items.Single();
			DebugCommand.Registry.TryGetDescriptor(command.Id, out var descriptor);
			var amount = descriptor.Fields.Single(field => field.Name == "Amount");
			var direction = descriptor.Fields.Single(field => field.Name == "Direction");
			foreach (var culture in new[] { "ja-JP", "en-US", "de-DE" })
			{
				CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
				m_Window.SelectCommandForTests(command.Id);
				EditorNumericCommand.s_Count = 0;
				yield return Repaint();
				foreach (var invalid in new[] { "", "-", "1e-", "2,5", "1,000", "3.402824e38" })
				{
					yield return Edit(amount.Id.ToString(), invalid);
					yield return Repaint();
					Assert.That(m_Window.CommandNumericInputForTests.TextForTests(amount.Id.ToString()), Is.EqualTo(invalid));
					Assert.That(m_Window.CommandExecutionEnabledForTests, Is.False);
					Click(m_Window.CommandPrimaryRectForTests);
					m_Window.ExecuteSelectedCommandForTests();
					Assert.That(EditorNumericCommand.s_Count, Is.Zero);
				}
				yield return Edit(amount.Id.ToString(), "1.");
				yield return Repaint();
				var version = DraftVersion();
				yield return Repaint();
				Assert.That(DraftVersion(), Is.EqualTo(version));
				Assert.That(m_Window.CommandNumericInputForTests.TextForTests(amount.Id.ToString()), Is.EqualTo("1."));
				yield return Edit(direction.Id + "/x", "3.402824e38");
				yield return Repaint();
				yield return Edit(direction.Id + "/y", "2.5");
				Assert.That(m_Window.CommandExecutionEnabledForTests, Is.False);
				yield return Edit(direction.Id + "/x", "1e-3");
				yield return Repaint();
				Assert.That(m_Window.CommandExecutionEnabledForTests, Is.True);
				Click(m_Window.CommandPrimaryRectForTests);
				Assert.That(EditorNumericCommand.s_Count, Is.EqualTo(1));
				Assert.That(EditorNumericCommand.s_Amount, Is.EqualTo(1f));
				Assert.That(EditorNumericCommand.s_Direction, Is.EqualTo(new Vector2(0.001f, 2.5f)));
			}
			// Core rejection must survive an edit of another component in the same input.
			m_Window.SelectCommandForTests(command.Id);
			EditorNumericCommand.s_Count = 0;
			var bounded = descriptor.Fields.Single(field => field.Name == "Bounded");
			yield return Repaint();
			yield return Edit(bounded.Id + "/x", "20");
			yield return Repaint();
			yield return Edit(bounded.Id + "/y", "2.5");
			yield return Repaint();
			Assert.That(m_Window.CommandExecutionEnabledForTests, Is.False);
			Assert.That(m_Window.CommandNumericInputForTests.TextForTests(bounded.Id + "/x"), Is.EqualTo("20"));
			m_Window.ExecuteSelectedCommandForTests();
			Assert.That(EditorNumericCommand.s_Count, Is.Zero);
			yield return Edit(bounded.Id + "/x", "2.5");
			yield return Repaint();
			Assert.That(m_Window.CommandExecutionEnabledForTests, Is.True);
			Click(m_Window.CommandPrimaryRectForTests);
			Assert.That(EditorNumericCommand.s_Count, Is.EqualTo(1));
		}

		[UnityTest]
		public IEnumerator FloatingAndSliderSettingsPreserveTextAndBlockStaleApply()
		{
			foreach (var culture in new[] { "ja-JP", "en-US", "de-DE" })
			{
				CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
				foreach (var name in new[] { "Amount", "Double", "IntSlider", "FloatSlider" })
				{
					var setting = DebugSetting
						.GetList(new DebugListQuery(string.Empty, "ZZTests/FIX03C2/" + name, new DebugPageRequest(0, 8)))
						.Items.Single();
					if (name == "IntSlider")
						EditorNumericSettings.Current.IntSlider = int.MaxValue;
					if (name == "FloatSlider")
						EditorNumericSettings.Current.FloatSlider = 20.125f;
					m_Window.ShowSettingDetailForTests(setting.Id);
					var before = m_Window.SettingEditedValueForTests;
					var history = DebugSetting.GetHistory(setting.Id, new DebugPageRequest(0, 100)).Entries.Count;
					yield return Repaint();
					Assert.That(m_Window.SettingEditedValueForTests, Is.EqualTo(before));
					Assert.That(DebugSetting.GetHistory(setting.Id, new DebugPageRequest(0, 100)).Entries.Count, Is.EqualTo(history));
					foreach (var invalid in new[] { "-", "1e-", "2,5", "1,000", name == "Double" ? "1e309" : "3.402824e38" })
					{
						yield return Edit("setting", invalid);
						yield return Repaint();
						Assert.That(m_Window.SettingNumericInputForTests.TextForTests("setting"), Is.EqualTo(invalid));
						Assert.That(m_Window.SettingNumericInputForTests.IsValid, Is.False);
						Click(m_Window.SettingApplyRectForTests);
						Assert.That(m_Window.WriteSelectedSettingForTests(before), Is.Null);
						Assert.That(DebugSetting.GetHistory(setting.Id, new DebugPageRequest(0, 100)).Entries.Count, Is.EqualTo(history));
					}
					if (name.EndsWith("Slider", StringComparison.Ordinal))
					{
						var track = m_Controls["setting/slider"];
						Click(new Rect(Math.Min(track.x + track.width * 0.75f, m_Window.position.width - 20f), track.y, 1f, track.height));
						Assert.That(m_Window.SettingNumericInputForTests.IsValid, Is.True, "Track corrects invalid raw text");
						yield return Repaint();
					}
					var text = name == "IntSlider" ? "2" : "1.";
					yield return Edit("setting", text);
					yield return Repaint();
					Click(m_Window.SettingApplyRectForTests);
					yield return Repaint();
					Assert.That(m_Window.SettingNumericInputForTests.TextForTests("setting"), Is.EqualTo(text));
					Assert.That(DebugSetting.Read(setting.Id).TryGetValue(out var actual), Is.True);
					Assert.That(actual, Is.EqualTo(name == "IntSlider" ? DebugValue.From(2) : DebugValue.From(1d)));
				}
			}
		}

		[UnityTest]
		public IEnumerator SettingComponentsWaitForCorrectionAndPreserveOtherEdits()
		{
			foreach (var name in new[] { "Vector2", "Vector3", "Vector4", "Color" })
			{
				var setting = DebugSetting
					.GetList(new DebugListQuery(string.Empty, "ZZTests/FIX03C2/" + name, new DebugPageRequest(0, 8)))
					.Items.Single();
				m_Window.ShowSettingDetailForTests(setting.Id);
				yield return Repaint();
				var first = name == "Color" ? "r" : "x";
				var second = name == "Color" ? "g" : "y";
				var history = DebugSetting.GetHistory(setting.Id, new DebugPageRequest(0, 100)).Entries.Count;
				yield return Edit("setting/" + first, "-");
				yield return Repaint();
				yield return Edit("setting/" + second, "0.5");
				yield return Repaint();
				Assert.That(DebugSetting.GetHistory(setting.Id, new DebugPageRequest(0, 100)).Entries.Count, Is.EqualTo(history));
				Assert.That(m_Window.WriteSelectedSettingForTests(m_Window.SettingEditedValueForTests), Is.Null);
				yield return Edit("setting/" + first, "0.");
				Assert.That(
					DebugSetting.GetHistory(setting.Id, new DebugPageRequest(0, 100)).Entries.Count,
					Is.EqualTo(history),
					"Correction is still an unconfirmed edit"
				);
				SendKey(KeyCode.Return);
				yield return Repaint();
				Assert.That(DebugSetting.Read(setting.Id).TryGetValue(out var actual), Is.True);
				actual.TryGetObject(out var members);
				Assert.That(members.Single(member => member.Name == second).Value, Is.EqualTo(DebugValue.From(0.5f)));
				Assert.That(m_Window.SettingNumericInputForTests.TextForTests("setting/" + first), Is.EqualTo("0."));
				Assert.That(DebugSetting.GetHistory(setting.Id, new DebugPageRequest(0, 100)).Entries.Count, Is.EqualTo(history + 1));
				yield return Edit("setting/" + first, "-");
				m_Window.ShowSettingDetailForTests(setting.Id);
				Assert.That(m_Window.SettingNumericInputForTests.IsValid, Is.True);
			}
		}

		[UnityTest]
		public IEnumerator FIX06DetailGeometryAndDraftSurviveRepaintAndResize()
		{
			var setting = DebugSetting
				.GetList(new DebugListQuery(string.Empty, "ZZTests/FIX03C2/Amount", new DebugPageRequest(0, 8)))
				.Items.Single();
			var command = DebugCommand
				.Registry.GetList(new DebugListQuery("Tests/ZZFIX03B", string.Empty, new DebugPageRequest(0, 8)))
				.Items.Single();
			DebugCommand.Registry.TryGetDescriptor(command.Id, out var descriptor);
			var field = descriptor.Fields.Single(item => item.Name == "Unsigned");
			var pageField = typeof(DexcDebuggerWindow).GetField(
				"m_Page",
				System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
			);
			m_Window.minSize = new Vector2(760, 420);
			foreach (var width in new[] { 1100, 760 })
			{
				m_Window.position = new Rect(0, 0, width, 800);
				m_Window.ShowSettingDetailForTests(setting.Id);
				yield return Repaint();
				yield return Repaint();
				var rect = m_Controls["setting"];
				Debug.Log(
					"FIX06 geometry requested=" + width + " actual=" + m_Window.position + " min=" + m_Window.minSize + " field=" + rect
				);
				Assert.That(m_Window.position.width, Is.EqualTo(width).Within(1), "test must use the production minimum width");
				Assert.That(rect.width, Is.GreaterThan(40), "usable numeric field width");
				Assert.That(rect.xMin, Is.GreaterThanOrEqualTo(0));
				Assert.That(rect.xMax, Is.LessThanOrEqualTo(width + 1), "input must remain inside window at " + width);
				Assert.That(m_Window.SettingApplyRectForTests.xMax, Is.LessThanOrEqualTo(width + 1));
				yield return Edit("setting", "3.75");
				for (var repeat = 0; repeat < 3; repeat++)
				{
					m_Window.ProcessEditorUpdateForTests();
					yield return Repaint();
				}
				Assert.That(pageField.GetValue(m_Window).ToString(), Is.EqualTo("SettingDetail"));
				Assert.That(m_Window.SelectedSettingPathForTests, Is.EqualTo(setting.Path));
				Assert.That(m_Window.SettingEditedValueForTests, Is.EqualTo(DebugValue.From(3.75f)));
				m_Window.SelectCommandForTests(command.Id);
				m_Window.UpdateCommandInputForTests(field.Id, DebugValue.From(ulong.MaxValue));
				var draft = m_Window.CommandDraftForTests;
				var version = DraftVersion();
				for (var repeat = 0; repeat < 3; repeat++)
				{
					m_Window.ProcessEditorUpdateForTests();
					yield return Repaint();
				}
				Assert.That(pageField.GetValue(m_Window).ToString(), Is.EqualTo("CommandDetail"));
				Assert.That(m_Window.CommandDraftForTests, Is.EqualTo(draft));
				Assert.That(DraftVersion(), Is.EqualTo(version));
				Assert.That(m_Window.CommandValueForTests(field.Id), Is.EqualTo(DebugValue.From(ulong.MaxValue)));
			}
			m_Window.position = new Rect(0, 0, 1100, 800);
			m_Window.RefreshCommandPageForTests();
			Assert.That(DebugCommand.Draft.CreateRequest(m_Window.CommandDraftForTests).TryGetRequest(out var request), Is.True);
			Assert.That(request.Dispatch().Status, Is.EqualTo(CommandExecutionStatus.Succeeded));
			m_Window.RefreshHistoryForTests();
			var history = m_Window.CommandHistoryForTests.Entries.Single().Id;
			m_Window.SelectHistoryPageForTests();
			yield return Repaint();
			Assert.That(pageField.GetValue(m_Window).ToString(), Is.EqualTo("History"));
			Assert.That(m_Window.ReuseCommandHistoryForTests(history), Is.True);
			yield return Repaint();
			yield return Repaint();
			yield return Edit(field.Id.ToString(), "42");
			yield return Repaint();
			Assert.That(pageField.GetValue(m_Window).ToString(), Is.EqualTo("CommandDetail"));
			Assert.That(m_Window.CommandValueForTests(field.Id), Is.EqualTo(DebugValue.From(42UL)));
		}

		private long DraftVersion()
		{
			Assert.That(DebugCommand.Draft.CreateRequest(m_Window.CommandDraftForTests).TryGetRequest(out var request), Is.True);
			return request.DraftVersion;
		}

		private IEnumerator Repaint()
		{
			m_Window.Repaint();
			yield return null;
		}

		private void ObserveControl(string key, Rect rect)
		{
			m_Controls[key] = m_Window.WindowRectForTests(rect);
			if (m_RequestedFocus != key)
				return;
			GUI.FocusControl("DexcEditorNumeric:" + key);
			m_RequestedFocus = null;
		}

		private IEnumerator Edit(string key, string text)
		{
			Assert.That(m_Controls.ContainsKey(key), Is.True, key);
			m_RequestedFocus = key;
			yield return Repaint();
			yield return Repaint();
			Assert.That(m_RequestedFocus, Is.Null, "The requested production field must be drawn");
			var control = m_Controls[key];
			var right = Math.Min(control.xMax, m_Window.position.width - 4f);
			Assert.That(right, Is.GreaterThan(control.xMin), "The production text field must have a visible hit area");
			Click(Rect.MinMaxRect(control.xMin, control.yMin, right, control.yMax));
			SendKey(KeyCode.A, default(char), EventModifiers.Control);
			SendKey(KeyCode.Backspace);
			foreach (var character in text)
				SendKey(KeyCode.None, character);
			yield return Repaint();
		}

		private void SendKey(KeyCode keyCode, char character = default(char), EventModifiers modifiers = EventModifiers.None)
		{
			var input =
				character == default(char)
					? Event.KeyboardEvent(keyCode.ToString().ToLowerInvariant())
					: new Event
					{
						type = EventType.KeyDown,
						keyCode = keyCode,
						character = character,
					};
			// Preserve FunctionKey on Backspace; clearing it turns deletion into an ignored event.
			input.modifiers |= modifiers;
			m_Window.SendEvent(input);
			input.type = EventType.KeyUp;
			m_Window.SendEvent(input);
		}

		private void Click(Rect rect)
		{
			Assert.That(rect.width, Is.GreaterThan(0));
			m_Window.SendEvent(
				new Event
				{
					type = EventType.MouseDown,
					button = 0,
					mousePosition = rect.center,
				}
			);
			m_Window.SendEvent(
				new Event
				{
					type = EventType.MouseUp,
					button = 0,
					mousePosition = rect.center,
				}
			);
		}
	}

	[DebugCommandAttribute("Tests/ZZFIX03C2/Floating", Access = DebugCommandAccess.ReadOnly)]
	public sealed class EditorNumericCommand : DebugCommand
	{
		internal static int s_Count;
		internal static float s_Amount;
		internal static Vector2 s_Direction;
		public InputFloat Amount = new InputFloat();
		public InputVector2 Direction = new InputVector2();
		public EditorBoundedVectorInput Bounded = new EditorBoundedVectorInput();

		protected override void Execute()
		{
			s_Count++;
			s_Amount = Amount.Value;
			s_Direction = Direction.Value;
		}
	}

	public sealed class EditorBoundedVectorInput : DebugInput<Vector2>
	{
		public EditorBoundedVectorInput()
			: base(
				Vector2.zero,
				DebugInputSchema.Object(
					new DebugInputMemberSchema("x", DebugInputSchema.FloatingPoint()),
					new DebugInputMemberSchema("y", DebugInputSchema.FloatingPoint())
				)
			) { }

		protected override DebugValue WriteValue(Vector2 value) =>
			DebugValue.FromObject(new DebugValueMember("x", DebugValue.From(value.x)), new DebugValueMember("y", DebugValue.From(value.y)));

		protected override DebugInputReadResult<Vector2> ReadValue(DebugValue value)
		{
			if (!value.TryGetObject(out var members))
				return DebugInputReadResult<Vector2>.Failure("Object required.");
			var x = members.Single(member => member.Name == "x").Value;
			var y = members.Single(member => member.Name == "y").Value;
			return x.TryGetFloatingPoint(out var first) && y.TryGetFloatingPoint(out var second) && first <= 10
				? DebugInputReadResult<Vector2>.Success(new Vector2((float)first, (float)second))
				: DebugInputReadResult<Vector2>.Failure("x must not exceed 10.");
		}
	}

	public sealed class EditorNumericSettings : DebugSettingSet<EditorNumericSettings>
	{
		[DebugSetting("ZZTests/FIX03C2/Amount", ApplyMode = DebugSettingApplyMode.Submit)]
		public float Amount { get; internal set; } = 2.5f;

		[DebugSetting("ZZTests/FIX03C2/Double", ApplyMode = DebugSettingApplyMode.Submit)]
		public double Double { get; internal set; } = double.MaxValue;

		[DebugSetting("ZZTests/FIX03C2/IntSlider", ApplyMode = DebugSettingApplyMode.Submit), DebugRange(0d, 10d), DebugSlider]
		public int IntSlider { get; internal set; } = 4;

		[DebugSetting("ZZTests/FIX03C2/FloatSlider", ApplyMode = DebugSettingApplyMode.Submit), DebugRange(0d, 10d), DebugSlider]
		public float FloatSlider { get; internal set; } = 2.5f;

		[DebugSetting("ZZTests/FIX03C2/Vector2", ApplyMode = DebugSettingApplyMode.Immediate)]
		public Vector2 Vector2 { get; internal set; } = UnityEngine.Vector2.zero;

		[DebugSetting("ZZTests/FIX03C2/Vector3", ApplyMode = DebugSettingApplyMode.Immediate)]
		public Vector3 Vector3 { get; internal set; } = UnityEngine.Vector3.zero;

		[DebugSetting("ZZTests/FIX03C2/Vector4", ApplyMode = DebugSettingApplyMode.Immediate)]
		public Vector4 Vector4 { get; internal set; } = UnityEngine.Vector4.zero;

		[DebugSetting("ZZTests/FIX03C2/Color", ApplyMode = DebugSettingApplyMode.Immediate)]
		public Color Color { get; internal set; } = UnityEngine.Color.clear;
	}

	[DebugCommandAttribute("Tests/ZZFIX03B/Integer", Access = DebugCommandAccess.ReadOnly)]
	public sealed class EditorIntegerCommand : DebugCommand
	{
		internal static int s_Count;
		internal static ulong s_Unsigned;
		internal static int s_Signed;
		public EditorUnsignedInput Unsigned = new EditorUnsignedInput();
		public InputInt Signed = new InputInt();
		public EditorNullableIntegerInput Nullable = new EditorNullableIntegerInput();

		protected override void Execute()
		{
			s_Count++;
			s_Unsigned = Unsigned.Value;
			s_Signed = Signed.Value;
		}
	}

	public sealed class EditorNullableIntegerInput : DebugInput<int?>
	{
		public EditorNullableIntegerInput()
			: base(null, DebugInputSchema.SignedInteger(true)) { }

		protected override DebugValue WriteValue(int? value) => value.HasValue ? DebugValue.From(value.Value) : DebugValue.Null;

		protected override DebugInputReadResult<int?> ReadValue(DebugValue value)
		{
			if (value.Kind == DebugValueKind.Null)
				return DebugInputReadResult<int?>.Success(null);
			return value.TryGetSignedInteger(out var result) && result >= int.MinValue && result <= int.MaxValue
				? DebugInputReadResult<int?>.Success((int)result)
				: DebugInputReadResult<int?>.Failure("Int32 required.");
		}
	}

	public sealed class EditorUnsignedInput : DebugInput<ulong>
	{
		public EditorUnsignedInput()
			: base(0UL, DebugInputSchema.UnsignedInteger()) { }

		protected override DebugValue WriteValue(ulong value) => DebugValue.From(value);

		protected override DebugInputReadResult<ulong> ReadValue(DebugValue value) =>
			value.TryGetUnsignedInteger(out var result)
				? DebugInputReadResult<ulong>.Success(result)
				: DebugInputReadResult<ulong>.Failure("Unsigned integer required.");
	}
}
#endif
