#if DEXC_DEBUGGER && !DISABLE_DEXC_DEBUGGER && DEXC_HAS_UGUI
using System;
using System.Collections;
using Dexc;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Events;

namespace DexcDebugger.Tests
{
	public sealed class DexcUGUIReflectionBoundaryTests
	{
		[TestCase("button")]
		[TestCase("slider")]
		[TestCase("input")]
		public void RetainedEventsStopCallingBackAfterTheirControlIsDestroyed(string kind)
		{
			var root = new GameObject("DestroyedControl", typeof(RectTransform));
			try
			{
				var calls = 0;
				Action invoke;
				if (kind == "button")
				{
					var control = DexcUGUIApi.Add(root, DexcUGUIApi.ButtonType);
					DexcUGUIApi.SetEvent(control, "onClick", new UnityAction(() => calls++));
					var retained = (UnityEvent)DexcUGUIApi.Get(control, "onClick");
					invoke = retained.Invoke;
				}
				else if (kind == "slider")
				{
					var control = DexcUGUIApi.Add(root, DexcUGUIApi.SliderType);
					DexcUGUIApi.SetEvent(control, "onValueChanged", new UnityAction<float>(value => calls++));
					var retained = (UnityEvent<float>)DexcUGUIApi.Get(control, "onValueChanged");
					invoke = () => retained.Invoke(0.5f);
				}
				else
				{
					var control = DexcUGUIApi.Add(root, DexcUGUIApi.InputFieldType);
					DexcUGUIApi.SetEvent(control, "onValueChanged", new UnityAction<string>(value => calls++));
					var retained = (UnityEvent<string>)DexcUGUIApi.Get(control, "onValueChanged");
					invoke = () => retained.Invoke("value");
				}
				invoke();
				Assert.That(calls, Is.EqualTo(1));
				UnityEngine.Object.DestroyImmediate(root);
				invoke();
				Assert.That(calls, Is.EqualTo(1), "A retained event must not mutate a draft after its control has been destroyed.");
			}
			finally
			{
				if (root != null)
					UnityEngine.Object.DestroyImmediate(root);
			}
		}

		[Test]
		public void RetainedCancelEventStopsAfterItsControlIsDestroyed()
		{
			var root = new GameObject("DestroyedCancelControl", typeof(RectTransform));
			try
			{
				var calls = 0;
				var button = DexcUGUIApi.Add(root, DexcUGUIApi.ButtonType);
				DexcUGUIApi.SetCancelHandler(button, value => calls++, null);
				var trigger = root.GetComponent(Type.GetType("UnityEngine.EventSystems.EventTrigger, UnityEngine.UI", true));
				var entries = (IList)DexcUGUIApi.Get(trigger, "triggers");
				Assert.That(entries.Count, Is.EqualTo(1));
				var entry = entries[0];
				var callback = entry.GetType().GetField("callback").GetValue(entry);
				var invoke = callback.GetType().GetMethod("Invoke");
				invoke.Invoke(callback, new object[] { null });
				Assert.That(calls, Is.EqualTo(1));
				UnityEngine.Object.DestroyImmediate(root);
				invoke.Invoke(callback, new object[] { null });
				Assert.That(calls, Is.EqualTo(1));
			}
			finally
			{
				if (root != null)
					UnityEngine.Object.DestroyImmediate(root);
			}
		}

		[Test]
		public void BoxedDestroyedControlsAreIgnoredWithoutBreakingManagedValues()
		{
			var root = new GameObject("BoxedDestroyedControl", typeof(RectTransform));
			var control = DexcUGUIApi.Add(root, DexcUGUIApi.ImageType);
			object boxed = control;
			UnityEngine.Object.DestroyImmediate(root);
			Assert.That(DexcUGUIApi.Get(boxed, "color"), Is.Null);
			Assert.DoesNotThrow(() => DexcUGUIApi.Set(boxed, "color", Color.red));
			Assert.DoesNotThrow(() => DexcUGUIApi.Enable(control, true));
			Assert.DoesNotThrow(() => DexcUGUIApi.SetEvent(control, "onClick", null));
			Assert.DoesNotThrow(() => DexcUGUIApi.SetEnum(control, "type", "Simple"));
			var managed = new ManagedValue();
			DexcUGUIApi.Set(managed, "Value", 7);
			Assert.That(DexcUGUIApi.Get(managed, "Value"), Is.EqualTo(7));
		}

		public sealed class ManagedValue
		{
			public int Value { get; set; }
		}
	}
}
#endif
