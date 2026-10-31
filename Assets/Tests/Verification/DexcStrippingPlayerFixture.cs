#if DEXC_DEBUGGER && !DISABLE_DEXC_DEBUGGER && DEXC_I17_STRIPPING_PLAYER
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Dexc;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Scripting;
using DebuggerRuntime = Dexc.DexcDebugger;

namespace DexcDebugger.Verification
{
	[DebugCommandAttribute("I17/Stripping/Command", Access = DebugCommandAccess.ReadOnly)]
	public sealed class DexcStrippingCommand : DebugCommand
	{
		public InputInt Value = new InputInt(7);

		protected override void Execute() { }
	}

	[Preserve]
	public sealed class DexcStrippingSettings : DebugSettingSet<DexcStrippingSettings>
	{
		[DebugSetting("I17/Stripping/Setting")]
		public int Value { get; private set; } = 11;
	}

	public sealed class DexcStrippingComponent : MonoBehaviour
	{
		[DebugField(Access = DebugFieldAccess.ReadOnly)]
		public int Value = 13;
	}

	public static class DexcStrippingSource
	{
		private static DexcStrippingComponent s_Target;

		internal static void SetTarget(DexcStrippingComponent target)
		{
			s_Target = target;
		}

		[DebugInspectorSource("I17/Stripping/Source")]
		private static IEnumerable<DebugSourceEntry<DexcStrippingComponent>> GetEntries()
		{
			if (s_Target != null)
				yield return new DebugSourceEntry<DexcStrippingComponent>(s_Target, "stripping-fixture", "target");
		}
	}

	[DebugInputEditor(7, "Runtime")]
	public sealed class DexcStrippingInputEditor : DebugInputEditor
	{
		public override bool CanEdit(DebugInputEditorDescriptor descriptor)
		{
			return descriptor != null;
		}

		public override void OnGUI(DebugInputEditorContext context, DebugInputEdit edit) { }
	}

	internal static class DexcStrippingPlayerProbe
	{
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		private static void Run()
		{
			var result = new DexcStrippingPlayerResult { Backend = "IL2CPP", Stripping = "High" };
			GameObject fixtureObject = null;
			try
			{
				DebuggerRuntime.Initialize();
				fixtureObject = new GameObject("DexcI17StrippingFixture");
				var component = fixtureObject.AddComponent<DexcStrippingComponent>();
				DexcStrippingSource.SetTarget(component);
				var page = new DebugPageRequest(0, 100);
				var commandList = DebugCommand.Registry.GetList(new DebugListQuery("I17/Stripping", string.Empty, page));
				var settingList = DebugSetting.GetList(new DebugListQuery("I17/Stripping", string.Empty, page));
				var inspectorList = DebugInspector.GetRegistered(new DebugListQuery("I17/Stripping", string.Empty, page));
				var settingType = typeof(DexcStrippingSettings);
				var settingProperties = settingType.GetProperties(BindingFlags.Instance | BindingFlags.Public);
				var inputEditorCount = typeof(DexcStrippingInputEditor)
					.Assembly.GetTypes()
					.Count(type => type.GetCustomAttributes(typeof(DebugInputEditorAttribute), false).Any());
				result.Initialized = DebuggerRuntime.IsInitialized;
				result.CommandCount = commandList.Items.Count;
				result.SettingCount = settingList.Items.Count;
				result.SettingPropertyCount = settingProperties.Length;
				result.SettingAttributeCount = settingProperties.Count(property =>
					property.GetCustomAttributes(typeof(DebugSettingAttribute), false).Any()
				);
				result.InspectorSourceCount = inspectorList.Entries.Count(entry => entry.Origin == DebugInspectorEntryOrigin.Source);
				result.InputEditorCount = inputEditorCount;
				var displaySettings = DebugSetting.GetList(new DebugListQuery("Display", string.Empty, page));
				foreach (var layer in new[] { "HUD", "Overlay", "World" })
				{
					var setting = displaySettings.Items.Single(item => item.Path == "Display/" + layer);
					Require(
						DebugSetting.Write(setting.Id, DebugValue.From(false)).Status == DebugOperationStatus.Succeeded,
						"Built-in Display setter metadata"
					);
					Require(
						DebugSetting.Read(setting.Id).TryGetValue(out var read) && read.Equals(DebugValue.From(false)),
						"Built-in Display getter metadata"
					);
					Require(DebugSetting.Reset(setting.Id).Status == DebugOperationStatus.Succeeded, "Built-in Display default value");
				}
				result.UGUIReflection = VerifyUGUIReflection();
				result.Pass =
					result.Initialized
					&& result.CommandCount > 0
					&& result.SettingCount > 0
					&& result.InspectorSourceCount > 0
					&& result.InputEditorCount > 0
					&& result.UGUIReflection;
			}
			catch (Exception exception)
			{
				result.Error = exception.GetType().FullName + ": " + exception.Message;
				result.Pass = false;
			}
			finally
			{
				var resultPath = Environment.GetEnvironmentVariable("DEXC_I17_STRIPPING_RESULT");
				if (string.IsNullOrEmpty(resultPath))
					resultPath = Path.Combine(Application.persistentDataPath, "Dexc-I17-StrippingResult.json");
				Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(resultPath)));
				File.WriteAllText(resultPath, JsonUtility.ToJson(result, true));
				if (fixtureObject != null)
					UnityEngine.Object.Destroy(fixtureObject);
				Application.Quit(result.Pass ? 0 : 1);
			}
		}

		private static bool VerifyUGUIReflection()
		{
#if DEXC_HAS_UGUI
			var root = new GameObject("DexcStrippingUGUI", typeof(RectTransform));
			try
			{
				var button = DexcUGUIApi.Add(root, DexcUGUIApi.ButtonType);
				var image = DexcUGUIApi.SetImage(root, Color.red, true);
				DexcUGUIApi.Set(button, "targetGraphic", image);
				Require(Equals(DexcUGUIApi.Get(image, "color"), Color.red), "Image.color metadata");
				Require(
					image.GetType().GetMethod("CrossFadeColor", new[] { typeof(Color), typeof(float), typeof(bool), typeof(bool) }) != null,
					"Graphic.CrossFadeColor metadata"
				);
				DexcUGUIApi.ResetGraphicTint(root);
				var colors = DexcUGUIApi.Get(button, "colors");
				foreach (var name in new[] { "normalColor", "highlightedColor", "pressedColor", "selectedColor", "disabledColor" })
				{
					DexcUGUIApi.Set(colors, name, Color.green);
					Require(Equals(DexcUGUIApi.Get(colors, name), Color.green), "ColorBlock." + name + " metadata");
				}
				DexcUGUIApi.Set(button, "colors", colors);
				Require(Equals(DexcUGUIApi.Get(DexcUGUIApi.Get(button, "colors"), "normalColor"), Color.green), "ColorBlock metadata");
				var sliderRoot = new GameObject("Slider", typeof(RectTransform));
				sliderRoot.transform.SetParent(root.transform, false);
				var slider = DexcUGUIApi.Add(sliderRoot, DexcUGUIApi.SliderType);
				DexcUGUIApi.Set(slider, "value", 0.5f);
				Require(Equals(DexcUGUIApi.Get(slider, "value"), 0.5f), "Slider.value metadata");
				var inputRoot = new GameObject("Input", typeof(RectTransform));
				inputRoot.transform.SetParent(root.transform, false);
				var input = DexcUGUIApi.Add(inputRoot, DexcUGUIApi.InputFieldType);
				DexcUGUIApi.Set(input, "text", "probe");
				Require(Equals(DexcUGUIApi.Get(input, "text"), "probe"), "InputField.text metadata");
				DexcUGUIApi.SetExplicitNavigation(button, slider, input);
				Require(
					ReferenceEquals(DexcUGUIApi.Get(DexcUGUIApi.Get(button, "navigation"), "selectOnDown"), input),
					"Navigation metadata"
				);
				var scrollbarRoot = new GameObject("Scrollbar", typeof(RectTransform));
				scrollbarRoot.transform.SetParent(root.transform, false);
				var scrollbar = DexcUGUIApi.Add(scrollbarRoot, DexcUGUIApi.ScrollbarType);
				DexcUGUIApi.SetEnum(scrollbar, "direction", "BottomToTop");
				Require(DexcUGUIApi.Get(scrollbar, "direction")?.ToString() == "BottomToTop", "Scrollbar enum metadata");
				var calls = 0;
				DexcUGUIApi.SetEvent(button, "onClick", new UnityAction(() => calls++));
				DexcUGUIApi.SetEvent(slider, "onValueChanged", new UnityAction<float>(value => calls++));
				DexcUGUIApi.SetEvent(input, "onEndEdit", new UnityAction<string>(value => calls++));
				var click = (UnityEvent)DexcUGUIApi.Get(button, "onClick");
				var changed = (UnityEvent<float>)DexcUGUIApi.Get(slider, "onValueChanged");
				var endEdit = (UnityEvent<string>)DexcUGUIApi.Get(input, "onEndEdit");
				DexcUGUIApi.InvokeButton(button);
				changed.Invoke(0.5f);
				endEdit.Invoke("probe");
				Require(calls == 3, "Live reflected callbacks");
				UnityEngine.Object.DestroyImmediate(root);
				click.Invoke();
				changed.Invoke(0.5f);
				endEdit.Invoke("probe");
				Require(calls == 3 && DexcUGUIApi.Get(image, "color") == null, "Destroyed controls stop callbacks and reflected access");
				return true;
			}
			finally
			{
				if (root != null)
					UnityEngine.Object.DestroyImmediate(root);
			}
#else
			throw new InvalidOperationException("The stripping validation fixture requires the installed uGUI package.");
#endif
		}

		private static void Require(bool condition, string message)
		{
			if (!condition)
				throw new InvalidOperationException(message);
		}
	}

	[Serializable]
	public sealed class DexcStrippingPlayerResult
	{
		public bool Pass;
		public bool Initialized;
		public int CommandCount;
		public int SettingCount;
		public int SettingPropertyCount;
		public int SettingAttributeCount;
		public int InspectorSourceCount;
		public int InputEditorCount;
		public bool UGUIReflection;
		public string Backend;
		public string Stripping;
		public string Error;
	}
}
#endif
