using System;
using System.Collections.Generic;
using Dexc;
using NUnit.Framework;
using UnityEngine;

namespace DexcDebugger.Tests
{
	public sealed class DexcDebugInputTests
	{
#if DEXC_DEBUGGER
		private enum TestMode
		{
			First,
			Second,
		}

		[Test]
		public void BuiltInInputsUseSpecifiedDefaultsAndRoundTripThroughDebugValue()
		{
			Assert.That(new InputBool().Value, Is.False);
			Assert.That(new InputInt().Value, Is.EqualTo(0));
			Assert.That(new InputLong().Value, Is.EqualTo(0L));
			Assert.That(new InputFloat().Value, Is.EqualTo(0f));
			Assert.That(new InputDouble().Value, Is.EqualTo(0d));
			Assert.That(new InputString().Value, Is.EqualTo(string.Empty));
			Assert.That(new InputString(null).Value, Is.EqualTo(string.Empty));
			Assert.That(new InputColor().Value, Is.EqualTo(Color.clear));
			Assert.That(new InputVector2().Value, Is.EqualTo(Vector2.zero));
			Assert.That(new InputVector3().Value, Is.EqualTo(Vector3.zero));
			Assert.That(new InputVector4().Value, Is.EqualTo(Vector4.zero));
			Assert.That(new InputVector2Int().Value, Is.EqualTo(Vector2Int.zero));
			Assert.That(new InputVector3Int().Value, Is.EqualTo(Vector3Int.zero));

			AssertInputRoundTrip(new InputBool(true), () => new InputBool(), true);
			AssertInputRoundTrip(new InputInt(27), () => new InputInt(), 27);
			AssertInputRoundTrip(new InputLong(27L), () => new InputLong(), 27L);
			AssertInputRoundTrip(new InputFloat(1.25f), () => new InputFloat(), 1.25f);
			AssertInputRoundTrip(new InputDouble(2.5d), () => new InputDouble(), 2.5d);
			AssertInputRoundTrip(new InputString("after"), () => new InputString(), "after");
			AssertInputRoundTrip(new InputEnum<TestMode>(TestMode.Second), () => new InputEnum<TestMode>(TestMode.First), TestMode.Second);
			AssertInputRoundTrip(
				new InputColor(new Color(0.1f, 0.2f, 0.3f, 0.4f)),
				() => new InputColor(),
				new Color(0.1f, 0.2f, 0.3f, 0.4f)
			);
			AssertInputRoundTrip(new InputVector2(new Vector2(1f, 2f)), () => new InputVector2(), new Vector2(1f, 2f));
			AssertInputRoundTrip(new InputVector3(new Vector3(1f, 2f, 3f)), () => new InputVector3(), new Vector3(1f, 2f, 3f));
			AssertInputRoundTrip(new InputVector4(new Vector4(1f, 2f, 3f, 4f)), () => new InputVector4(), new Vector4(1f, 2f, 3f, 4f));
			AssertInputRoundTrip(new InputVector2Int(new Vector2Int(1, 2)), () => new InputVector2Int(), new Vector2Int(1, 2));
			AssertInputRoundTrip(new InputVector3Int(new Vector3Int(1, 2, 3)), () => new InputVector3Int(), new Vector3Int(1, 2, 3));
		}

		[Test]
		public void TypedInputValidationAndApplyAreAtomic()
		{
			var input = new TrackingInput(5);
			var validation = new DebugValidation();
			input.Validate(DebugValue.From("wrong"), validation);

			Assert.That(validation.HasErrors, Is.True);
			Assert.That(validation.Issues[0].Code, Is.EqualTo("input-read-failed"));
			Assert.That(input.Value, Is.EqualTo(5));
			Assert.Throws<InvalidOperationException>(() => input.Apply(DebugValue.From("wrong")));
			Assert.That(input.Value, Is.EqualTo(5));
			input.Apply(DebugValue.From(9));
			Assert.That(input.Value, Is.EqualTo(9));
		}

		[Test]
		public void ChoiceUsesStableKeysAndDoesNotAutoSelectOrFallback()
		{
			var choices = new InputChoice<int>(
				new[] { new InputChoiceEntry<int>("one", 1, "One"), new InputChoiceEntry<int>("two", 2, "Two", "Numbers") }
			);

			Assert.That(choices.Key, Is.Empty);
			Assert.That(choices.GetValue(), Is.SameAs(DebugValue.Null));
			Assert.That(choices.TryGetValue(out _), Is.False);
			choices.Apply(DebugValue.From("two"));
			Assert.That(choices.Key, Is.EqualTo("two"));
			Assert.That(choices.TryGetValue(out var selected), Is.True);
			Assert.That(selected, Is.EqualTo(2));

			var missingValidation = new DebugValidation();
			choices.Validate(DebugValue.From("missing"), missingValidation);
			Assert.That(missingValidation.Issues[0].Code, Is.EqualTo("choice-missing"));
			Assert.Throws<InvalidOperationException>(() => choices.Apply(DebugValue.From("missing")));
			Assert.That(choices.Key, Is.EqualTo("two"));
			Assert.Throws<ArgumentException>(() =>
				new InputChoice<int>(
					new[] { new InputChoiceEntry<int>("same", 1, "First"), new InputChoiceEntry<int>("same", 2, "Second") }
				)
			);
		}

		[Test]
		public void DerivedChoiceEvaluatesDynamicCandidatesAndReportsMissingWithoutFallback()
		{
			var available = true;
			var choice = new DynamicChoice(() =>
				available ? new[] { new InputChoiceEntry<int>("one", 1, "One") } : Array.Empty<InputChoiceEntry<int>>()
			);

			choice.Apply(DebugValue.From("one"));
			Assert.That(choice.TryGetValue(out var selected), Is.True);
			Assert.That(selected, Is.EqualTo(1));
			available = false;
			Assert.That(choice.TryGetValue(out _), Is.False);
			var validation = new DebugValidation();
			choice.Validate(choice.GetValue(), validation);
			Assert.That(validation.Issues[0].Code, Is.EqualTo("choice-missing"));
			Assert.That(choice.Key, Is.EqualTo("one"));
		}

		[Test]
		public void InputRefSeparatesSourceSelectionAndStaleStates()
		{
			var target = new GameObject("InputRefTarget");
			var available = true;
			try
			{
				var reference = InputRef<GameObject>.Create(() =>
					available
						? new[] { new InputChoiceEntry<GameObject>("target", target, "Target", "Scene") }
						: Array.Empty<InputChoiceEntry<GameObject>>()
				);

				Assert.That(reference.State, Is.EqualTo(InputReferenceState.Unselected));
				Assert.That(reference.SourceKey, Is.Empty);
				Assert.That(reference.Key, Is.Empty);
				reference.Apply(DebugValue.From("target"));
				Assert.That(reference.State, Is.EqualTo(InputReferenceState.Resolved));
				Assert.That(reference.TryGetValue(out var resolved), Is.True);
				Assert.That(resolved, Is.SameAs(target));
				available = false;
				Assert.That(
					reference.State,
					Is.EqualTo(InputReferenceState.Resolved),
					"selection retains the same live object after source removal"
				);
				UnityEngine.Object.DestroyImmediate(target);
				Assert.That(reference.State, Is.EqualTo(InputReferenceState.Missing));
				Assert.That(reference.GetValue(), Is.EqualTo(DebugValue.From("target")));
				var missing = new DebugValidation();
				reference.Validate(DebugValue.From("gone"), missing);
				Assert.That(missing.Issues[0].Code, Is.EqualTo("reference-missing"));
				Assert.That(reference.Key, Is.EqualTo("target"));
				Assert.Throws<InvalidOperationException>(() => reference.Apply(DebugValue.From("gone")));
				Assert.That(reference.Key, Is.EqualTo("target"));

				var nullableValidation = new DebugValidation();
				reference.Validate(DebugValue.Null, nullableValidation);
				Assert.That(nullableValidation.HasErrors, Is.False);
				reference.Apply(DebugValue.Null);
				Assert.That(reference.State, Is.EqualTo(InputReferenceState.Unselected));

				var registered = new InputRef<GameObject>("Near");
				Assert.That(registered.SourceKey, Is.EqualTo("Near"));
				Assert.That(registered.Key, Is.Empty);
				Assert.That(registered.GetValue(), Is.SameAs(DebugValue.Null));
				Assert.That(registered.State, Is.EqualTo(InputReferenceState.Unselected));
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(target);
			}
		}

		[Test]
		public void CommandLabelsAndHeadersRemainOutsideInputValues()
		{
			var label = new CommandLabel(() => "dynamic");
			var header = new CommandHeader("section");
			Assert.That(label.Evaluate(), Is.EqualTo("dynamic"));
			Assert.That(header.Text, Is.EqualTo("section"));
		}

		[Test]
		public void EditorEditUsesLastCandidateAndRegistryCachesInstancesAndSelections()
		{
			var descriptor = new DebugInputEditorDescriptor(typeof(InputInt), DebugInputSchema.SignedInteger(), string.Empty, "Runtime");
			LowPriorityEditor.ResetCounters();
			HighPriorityEditor.ResetCounters();
			TiedPriorityEditor.ResetCounters();
			var registry = DexcDebugInputEditorRegistry.Create(new[] { typeof(LowPriorityEditor), typeof(HighPriorityEditor) });
			Assert.That(LowPriorityEditor.ConstructorCount, Is.EqualTo(1));
			Assert.That(HighPriorityEditor.ConstructorCount, Is.EqualTo(1));
			Assert.That(TiedPriorityEditor.ConstructorCount, Is.EqualTo(0));

			Assert.That(registry.TrySelect(descriptor, out var provider, out var error), Is.True);
			Assert.That(provider, Is.TypeOf<HighPriorityEditor>());
			Assert.That(error, Is.Empty);
			Assert.That(registry.TrySelect(descriptor, out var cachedProvider, out error), Is.True);
			Assert.That(cachedProvider, Is.SameAs(provider));
			Assert.That(HighPriorityEditor.CanEditCount, Is.EqualTo(1));
			Assert.That(TiedPriorityEditor.CanEditCount, Is.EqualTo(0));

			var conflictDescriptor = new DebugInputEditorDescriptor(
				typeof(InputInt),
				DebugInputSchema.SignedInteger(),
				string.Empty,
				"Runtime"
			);
			var conflictRegistry = DexcDebugInputEditorRegistry.Create(new[] { typeof(HighPriorityEditor), typeof(TiedPriorityEditor) });
			Assert.That(HighPriorityEditor.ConstructorCount, Is.EqualTo(2));
			Assert.That(TiedPriorityEditor.ConstructorCount, Is.EqualTo(1));
			Assert.That(conflictRegistry.TrySelect(conflictDescriptor, out _, out error), Is.False);
			Assert.That(error, Is.EqualTo("provider-priority-conflict"));
			Assert.That(conflictRegistry.TrySelect(conflictDescriptor, out _, out error), Is.False);
			Assert.That(HighPriorityEditor.CanEditCount, Is.EqualTo(2));
			Assert.That(TiedPriorityEditor.CanEditCount, Is.EqualTo(1));

			var edit = new DebugInputEdit();
			edit.SetValue(DebugValue.From(1));
			edit.SetValue(DebugValue.From(2));
			Assert.That(edit.Value, Is.EqualTo(DebugValue.From(2)));
		}

		private static void AssertInputRoundTrip<T>(DebugInput<T> source, System.Func<DebugInput<T>> createTarget, T expected)
		{
			var snapshot = source.GetValue();
			var validation = new DebugValidation();
			source.Validate(snapshot, validation);
			Assert.That(validation.HasErrors, Is.False);
			var target = createTarget();
			target.Apply(snapshot);
			Assert.That(target.Value, Is.EqualTo(expected));
		}

		private sealed class DynamicChoice : InputChoiceBase<int>
		{
			private readonly System.Func<IEnumerable<InputChoiceEntry<int>>> m_Source;

			public DynamicChoice(System.Func<IEnumerable<InputChoiceEntry<int>>> source)
				: base()
			{
				m_Source = source;
			}

			protected override IEnumerable<InputChoiceEntry<int>> GetChoices()
			{
				return m_Source();
			}
		}

		private sealed class TrackingInput : DebugInput<int>
		{
			private static readonly DebugInputSchema s_Schema = DebugInputSchema.SignedInteger();

			public TrackingInput(int initialValue)
				: base(initialValue, s_Schema) { }

			protected override DebugValue WriteValue(int value)
			{
				return DebugValue.From(value);
			}

			protected override DebugInputReadResult<int> ReadValue(DebugValue value)
			{
				return value.TryGetSignedInteger(out var result) && result >= int.MinValue && result <= int.MaxValue
					? DebugInputReadResult<int>.Success((int)result)
					: DebugInputReadResult<int>.Failure("Tracking input requires a signed integer.");
			}
		}

		[DebugInputEditor(1, "Runtime")]
		public sealed class LowPriorityEditor : DebugInputEditor
		{
			public static int ConstructorCount { get; private set; }
			public static int CanEditCount { get; private set; }

			public LowPriorityEditor()
			{
				ConstructorCount++;
			}

			public static void ResetCounters()
			{
				ConstructorCount = 0;
				CanEditCount = 0;
			}

			public override bool CanEdit(DebugInputEditorDescriptor descriptor)
			{
				CanEditCount++;
				return true;
			}

			public override void OnGUI(DebugInputEditorContext context, DebugInputEdit edit) { }
		}

		[DebugInputEditor(5, "Runtime")]
		public sealed class HighPriorityEditor : DebugInputEditor
		{
			public static int ConstructorCount { get; private set; }
			public static int CanEditCount { get; private set; }

			public HighPriorityEditor()
			{
				ConstructorCount++;
			}

			public static void ResetCounters()
			{
				ConstructorCount = 0;
				CanEditCount = 0;
			}

			public override bool CanEdit(DebugInputEditorDescriptor descriptor)
			{
				CanEditCount++;
				return true;
			}

			public override void OnGUI(DebugInputEditorContext context, DebugInputEdit edit) { }
		}

		[DebugInputEditor(5, "Runtime")]
		public sealed class TiedPriorityEditor : DebugInputEditor
		{
			public static int ConstructorCount { get; private set; }
			public static int CanEditCount { get; private set; }

			public TiedPriorityEditor()
			{
				ConstructorCount++;
			}

			public static void ResetCounters()
			{
				ConstructorCount = 0;
				CanEditCount = 0;
			}

			public override bool CanEdit(DebugInputEditorDescriptor descriptor)
			{
				CanEditCount++;
				return true;
			}

			public override void OnGUI(DebugInputEditorContext context, DebugInputEdit edit) { }
		}
#else
		[Test]
		public void DisabledAssemblyStillExposesInputTypes()
		{
			Assert.That(typeof(IDebugInput).IsInterface, Is.True);
		}
#endif
	}
}
