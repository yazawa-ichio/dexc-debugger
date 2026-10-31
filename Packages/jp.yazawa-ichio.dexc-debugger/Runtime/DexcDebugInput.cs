using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace Dexc
{
	/// <summary>Common contract implemented by every command input.</summary>
	public interface IDebugInput
	{
		DebugInputSchema Schema { get; }

		DebugValue GetValue();

		void Validate(DebugValue value, DebugValidation validation);

		void Apply(DebugValue value);
	}

	/// <summary>Immutable result of converting a DebugValue to a typed input value.</summary>
	public sealed class DebugInputReadResult<T>
	{
		private DebugInputReadResult(bool isSuccess, T value, string errorMessage)
		{
			IsSuccess = isSuccess;
			Value = value;
			ErrorMessage = errorMessage;
		}

		public bool IsSuccess { get; }

		public T Value { get; }

		public string ErrorMessage { get; }

		public static DebugInputReadResult<T> Success(T value)
		{
			return new DebugInputReadResult<T>(true, value, string.Empty);
		}

		public static DebugInputReadResult<T> Failure(string message)
		{
			if (string.IsNullOrWhiteSpace(message))
			{
				throw new ArgumentException("Failure message must not be empty.", nameof(message));
			}

			return new DebugInputReadResult<T>(false, default(T), message);
		}
	}

	/// <summary>Typed base class for user-defined and built-in value inputs.</summary>
	public abstract class DebugInput<T> : IDebugInput
	{
		protected DebugInput(T initialValue, DebugInputSchema schema)
		{
			Value = initialValue;
			Schema = schema ?? throw new ArgumentNullException(nameof(schema));
		}

		public T Value { get; set; }

		public DebugInputSchema Schema { get; }

		public DebugValue GetValue()
		{
			var value = WriteValue(Value);
			if (value == null)
			{
				throw new InvalidOperationException("DebugInput.WriteValue must return a DebugValue.");
			}

			return value;
		}

		public void Validate(DebugValue value, DebugValidation validation)
		{
			if (validation == null)
			{
				throw new ArgumentNullException(nameof(validation));
			}

			if (value == null)
			{
				validation.Error("null-debug-value", "DebugValue must not be C# null.");
				return;
			}

			var result = ReadValue(value);
			if (result == null)
			{
				validation.Error("input-read-failed", "Input conversion returned no result.");
				return;
			}

			if (!result.IsSuccess)
			{
				validation.Error("input-read-failed", result.ErrorMessage);
				return;
			}

			ValidateValue(result.Value, validation);
		}

		public void Apply(DebugValue value)
		{
			if (value == null)
			{
				throw new ArgumentNullException(nameof(value));
			}

			var result = ReadValue(value);
			if (result == null || !result.IsSuccess)
			{
				throw new InvalidOperationException(result == null ? "Input conversion returned no result." : result.ErrorMessage);
			}

			Value = result.Value;
		}

		protected abstract DebugValue WriteValue(T value);

		protected abstract DebugInputReadResult<T> ReadValue(DebugValue value);

		protected virtual void ValidateValue(T value, DebugValidation validation) { }
	}

	/// <summary>Fixed or evaluated text displayed as a non-input command form element.</summary>
	public sealed class CommandLabel
	{
		private readonly Func<string> m_Provider;

		public CommandLabel(string text)
		{
			if (text == null)
				throw new ArgumentNullException(nameof(text));
			m_Provider = () => text;
		}

		public CommandLabel(Func<string> provider)
		{
			m_Provider = provider ?? throw new ArgumentNullException(nameof(provider));
		}

		public string Evaluate()
		{
			var text = m_Provider();
			return text ?? throw new InvalidOperationException("CommandLabel provider returned C# null.");
		}
	}

	/// <summary>Fixed text displayed as a non-input command form header.</summary>
	public sealed class CommandHeader
	{
		public CommandHeader(string text)
		{
			Text = text ?? throw new ArgumentNullException(nameof(text));
		}

		public string Text { get; }
	}

	/// <summary>Immutable candidate entry used by choice and reference inputs.</summary>
	public sealed class InputChoiceEntry<T>
	{
		public InputChoiceEntry(string key, T value, string label, string path = "")
		{
			if (string.IsNullOrWhiteSpace(key))
				throw new ArgumentException("Choice key must not be empty.", nameof(key));
			if (label == null)
				throw new ArgumentNullException(nameof(label));
			if (path == null)
				throw new ArgumentNullException(nameof(path));
			Key = key;
			Value = value;
			Label = label;
			Path = path;
		}

		public string Key { get; }

		public T Value { get; }

		public string Label { get; }

		public string Path { get; }
	}

	internal interface IDexcChoiceInput
	{
		IReadOnlyList<DexcChoiceCandidate> GetCandidates();
	}

	internal sealed class DexcChoiceCandidate
	{
		internal DexcChoiceCandidate(string key, string label, string path)
		{
			Key = key;
			Label = label;
			Path = path;
		}

		internal string Key { get; }
		internal string Label { get; }
		internal string Path { get; }
	}

	/// <summary>Choice input base class whose snapshot stores only a stable string key.</summary>
	public abstract class InputChoiceBase<T> : IDebugInput, IDexcChoiceInput
	{
		private static readonly DebugInputSchema s_Schema = DebugInputSchema.String(isNullable: true, semanticType: "choice");
		private string m_Key;

		protected InputChoiceBase(string initialKey = null)
		{
			Key = initialKey ?? string.Empty;
		}

		public string Key
		{
			get { return m_Key; }
			set { m_Key = value ?? string.Empty; }
		}

		public DebugInputSchema Schema
		{
			get { return s_Schema; }
		}

		public DebugValue GetValue()
		{
			return string.IsNullOrEmpty(Key) ? DebugValue.Null : DebugValue.From(Key);
		}

		public void Validate(DebugValue value, DebugValidation validation)
		{
			if (validation == null)
				throw new ArgumentNullException(nameof(validation));
			if (value == null)
			{
				validation.Error("null-debug-value", "DebugValue must not be C# null.");
				return;
			}

			if (value.Kind == DebugValueKind.Null)
			{
				return;
			}

			if (!value.TryGetString(out var key) || string.IsNullOrEmpty(key))
			{
				validation.Error("choice-key-invalid", "Choice value must be a non-empty String key.");
				return;
			}

			if (!TryFindChoice(key, out _, out var error))
			{
				validation.Error(error.Code, error.Message);
			}
		}

		public void Apply(DebugValue value)
		{
			if (value == null)
				throw new ArgumentNullException(nameof(value));
			if (value.Kind == DebugValueKind.Null)
			{
				Key = string.Empty;
				return;
			}

			if (!value.TryGetString(out var key) || string.IsNullOrEmpty(key))
			{
				throw new InvalidOperationException("Choice value must be a non-empty String key.");
			}

			if (!TryFindChoice(key, out _, out var error))
			{
				throw new InvalidOperationException(error.Message);
			}

			Key = key;
		}

		public bool TryGetValue(out T value)
		{
			if (string.IsNullOrEmpty(Key) || !TryFindChoice(Key, out var entry, out _))
			{
				value = default(T);
				return false;
			}

			value = entry.Value;
			return true;
		}

		protected abstract IEnumerable<InputChoiceEntry<T>> GetChoices();

		IReadOnlyList<DexcChoiceCandidate> IDexcChoiceInput.GetCandidates()
		{
			var choices = GetChoices() ?? throw new InvalidOperationException("Choice source returned C# null.");
			var keys = new HashSet<string>(StringComparer.Ordinal);
			var result = new List<DexcChoiceCandidate>();
			foreach (var choice in choices)
			{
				if (choice == null || !keys.Add(choice.Key))
					throw new InvalidOperationException("Choice source contains a null entry or duplicate key.");
				result.Add(new DexcChoiceCandidate(choice.Key, choice.Label, choice.Path));
			}
			return result;
		}

		private bool TryFindChoice(string key, out InputChoiceEntry<T> match, out ChoiceError error)
		{
			match = null;
			error = default(ChoiceError);
			var found = false;
			try
			{
				var choices = GetChoices();
				if (choices == null)
				{
					error = new ChoiceError("choice-source-invalid", "Choice source returned C# null.");
					return false;
				}

				var keys = new HashSet<string>(StringComparer.Ordinal);
				foreach (var choice in choices)
				{
					if (choice == null)
					{
						error = new ChoiceError("choice-entry-invalid", "Choice source returned a C# null entry.");
						return false;
					}
					if (!keys.Add(choice.Key))
					{
						error = new ChoiceError("choice-key-duplicate", "Choice source returned a duplicate key.");
						return false;
					}
					if (string.Equals(choice.Key, key, StringComparison.Ordinal))
					{
						found = true;
						match = choice;
					}
				}
			}
			catch (Exception exception)
			{
				error = new ChoiceError("choice-source-failed", exception.Message);
				return false;
			}

			if (!found)
			{
				error = new ChoiceError("choice-missing", "Choice key is not present in the current source.");
			}

			return found;
		}

		private readonly struct ChoiceError
		{
			public ChoiceError(string code, string message)
			{
				Code = code;
				Message = string.IsNullOrWhiteSpace(message) ? code : message;
			}

			public string Code { get; }

			public string Message { get; }
		}
	}

	/// <summary>General-purpose choice input using a fixed or evaluated candidate source.</summary>
	public sealed class InputChoice<T> : InputChoiceBase<T>
	{
		private readonly IReadOnlyList<InputChoiceEntry<T>> m_FixedChoices;
		private readonly Func<IEnumerable<InputChoiceEntry<T>>> m_Source;

		public InputChoice()
			: this((IEnumerable<InputChoiceEntry<T>>)Array.Empty<InputChoiceEntry<T>>()) { }

		public InputChoice(IEnumerable<InputChoiceEntry<T>> choices, string initialKey = null)
			: base(initialKey)
		{
			if (choices == null)
				throw new ArgumentNullException(nameof(choices));
			m_FixedChoices = CopyChoices(choices);
		}

		public InputChoice(Func<IEnumerable<InputChoiceEntry<T>>> source, string initialKey = null)
			: base(initialKey)
		{
			m_Source = source ?? throw new ArgumentNullException(nameof(source));
		}

		protected override IEnumerable<InputChoiceEntry<T>> GetChoices()
		{
			return m_Source == null ? m_FixedChoices : m_Source();
		}

		private static IReadOnlyList<InputChoiceEntry<T>> CopyChoices(IEnumerable<InputChoiceEntry<T>> choices)
		{
			var copy = choices.ToList();
			var keys = new HashSet<string>(StringComparer.Ordinal);
			foreach (var choice in copy)
			{
				if (choice == null)
					throw new ArgumentNullException(nameof(choices));
				if (!keys.Add(choice.Key))
					throw new ArgumentException("Choice keys must be unique.", nameof(choices));
			}
			return new ReadOnlyCollection<InputChoiceEntry<T>>(copy);
		}
	}

	/// <summary>State of an InputRef selection.</summary>
	public enum InputReferenceState
	{
		Unselected = 0,
		Resolved = 1,
		Missing = 2,
	}

	/// <summary>
	/// Reference input whose snapshot stores a key rather than a Unity Object. Candidates come from an inline source
	/// (<see cref="InputRef.Create{T}"/>) or from the <see cref="DebugInspectorSourceAttribute"/> members whose Key matches
	/// the source key given to the constructor (no key: the sources without a Key). A selection is required unless the
	/// input is optional.
	/// </summary>
	public sealed class InputRef<T> : IDebugInput, IDexcReferenceInput
		where T : UnityEngine.Object
	{
		private static readonly DebugInputSchema s_Schema = DebugInputSchema.String(isNullable: true, semanticType: "reference");
		private readonly string m_SourceKey;
		private readonly Func<IEnumerable<InputChoiceEntry<T>>> m_InlineSource;
		private readonly bool m_Optional;
		private string m_Key;
		private T m_ResolvedValue;
		private bool m_HasBoundValue;

		public InputRef()
			: this(string.Empty) { }

		/// <param name="sourceKey">The Key of the <see cref="DebugInspectorSourceAttribute"/> members to pick from.</param>
		/// <param name="optional">The command runs with nothing selected.</param>
		public InputRef(string sourceKey, bool optional = false)
			: this(string.Empty, sourceKey, null, optional) { }

		private InputRef(string key, string sourceKey, Func<IEnumerable<InputChoiceEntry<T>>> inlineSource, bool optional)
		{
			m_Key = key ?? string.Empty;
			m_SourceKey = sourceKey ?? string.Empty;
			m_InlineSource = inlineSource;
			m_Optional = optional;
		}

		/// <summary>The command runs with nothing selected; <see cref="TryGetValue"/> then returns false.</summary>
		public bool IsOptional
		{
			get { return m_Optional; }
		}

		public string Key
		{
			get { return m_Key; }
			set
			{
				m_Key = value ?? string.Empty;
				m_HasBoundValue = false;
				m_ResolvedValue = null;
			}
		}

		public string SourceKey
		{
			get { return m_SourceKey; }
		}

		public InputReferenceState State
		{
			get
			{
				if (string.IsNullOrEmpty(Key))
					return InputReferenceState.Unselected;
				return TryGetValue(out _) ? InputReferenceState.Resolved : InputReferenceState.Missing;
			}
		}

		public DebugInputSchema Schema
		{
			get { return s_Schema; }
		}

		public DebugValue GetValue()
		{
			return string.IsNullOrEmpty(Key) ? DebugValue.Null : DebugValue.From(Key);
		}

		public void Validate(DebugValue value, DebugValidation validation)
		{
			if (validation == null)
				throw new ArgumentNullException(nameof(validation));
			if (value == null)
			{
				validation.Error("null-debug-value", "DebugValue must not be C# null.");
				return;
			}
			if (value.Kind == DebugValueKind.Null)
			{
				return;
			}
			if (!value.TryGetString(out var key) || string.IsNullOrEmpty(key))
			{
				validation.Error("reference-key-invalid", "Reference value must be a non-empty String key.");
				return;
			}
			if (!TryResolveKey(key, out _))
			{
				validation.Error("reference-missing", "The selected reference is not present in the current source.");
			}
		}

		public void Apply(DebugValue value)
		{
			if (value == null)
				throw new ArgumentNullException(nameof(value));
			if (value.Kind == DebugValueKind.Null)
			{
				Key = string.Empty;
				return;
			}
			if (!value.TryGetString(out var key) || string.IsNullOrEmpty(key))
			{
				throw new InvalidOperationException("Reference value must be a non-empty String key.");
			}
			if (!TryResolveKey(key, out var resolved))
				throw new InvalidOperationException("The selected reference is missing.");
			BindResolved(key, resolved);
		}

		public bool TryGetValue(out T value)
		{
			return TryResolveKey(Key, out value);
		}

		private bool TryResolveKey(string key, out T value)
		{
			if (string.IsNullOrEmpty(key))
			{
				value = null;
				return false;
			}

			if (m_HasBoundValue && string.Equals(m_Key, key, StringComparison.Ordinal))
			{
				value = m_ResolvedValue;
				return value != null;
			}

			try
			{
				foreach (var choice in Choices())
				{
					if (choice != null && string.Equals(choice.Key, key, StringComparison.Ordinal))
					{
						value = choice.Value;
						if (value != null && string.Equals(m_Key, key, StringComparison.Ordinal))
						{
							BindResolved(key, value);
						}
						return value != null;
					}
				}
			}
			catch (Exception) { }

			value = null;
			return false;
		}

		/// <summary>
		/// The candidates: the inline source, or else the entries of type T from every registered source whose Key equals
		/// this input's source key. An input without a source key reads the sources without a Key.
		/// </summary>
		private IEnumerable<InputChoiceEntry<T>> Choices()
		{
			if (m_InlineSource != null)
				return m_InlineSource() ?? Enumerable.Empty<InputChoiceEntry<T>>();
			return SourceChoices(m_SourceKey);
		}

		private static IEnumerable<InputChoiceEntry<T>> SourceChoices(string sourceKey)
		{
			foreach (var definition in DexcDefinitions.InspectorSources)
			{
				if (!string.Equals(definition.Attribute.Key, sourceKey, StringComparison.Ordinal))
					continue;
				if (!(definition.Read() is IEnumerable entries))
					continue;
				foreach (var item in entries)
				{
					if (item is IDexcInspectorSourceEntry entry && entry.Target is T target)
					{
						var path = string.IsNullOrEmpty(entry.Path) ? definition.Attribute.Path : definition.Attribute.Path + "/" + entry.Path;
						// The object itself is the identity, so its instance id is the key the selection stores.
						yield return new InputChoiceEntry<T>(target.GetInstanceID().ToString(CultureInfo.InvariantCulture), target, entry.Label, path);
					}
				}
			}
		}

		internal void BindResolved(string key, T value)
		{
			m_Key = key;
			m_ResolvedValue = value;
			m_HasBoundValue = true;
		}

		DexcReferenceSelection IDexcReferenceInput.CaptureSelection()
		{
			TryGetValue(out _);
			return new DexcReferenceSelection(m_Key, m_ResolvedValue, m_HasBoundValue);
		}

		void IDexcReferenceInput.RestoreSelection(DexcReferenceSelection selection)
		{
			if (!ReferenceEquals(selection.Value, null) && !(selection.Value is T))
			{
				throw new InvalidOperationException("The reference target type is incompatible with the input.");
			}
			m_Key = selection.Key ?? string.Empty;
			m_ResolvedValue = (T)selection.Value;
			m_HasBoundValue = selection.Bound;
		}

		IEnumerable<DexcReferenceCandidate> IDexcReferenceInput.GetCandidates()
		{
			foreach (var entry in Choices())
			{
				if (entry != null)
				{
					yield return new DexcReferenceCandidate(entry.Key, entry.Label, entry.Path, entry.Value);
				}
			}
		}

		/// <param name="optional">The command runs with nothing selected.</param>
		public static InputRef<T> Create(Func<IEnumerable<InputChoiceEntry<T>>> inlineSource, bool optional = false)
		{
			return new InputRef<T>(
				string.Empty,
				string.Empty,
				inlineSource ?? throw new ArgumentNullException(nameof(inlineSource)),
				optional
			);
		}
	}

	/// <summary>Factory for an inline InputRef source.</summary>
	public static class InputRef
	{
		/// <param name="optional">The command runs with nothing selected.</param>
		public static InputRef<T> Create<T>(Func<IEnumerable<InputChoiceEntry<T>>> inlineSource, bool optional = false)
			where T : UnityEngine.Object
		{
			return InputRef<T>.Create(inlineSource, optional);
		}
	}
}
