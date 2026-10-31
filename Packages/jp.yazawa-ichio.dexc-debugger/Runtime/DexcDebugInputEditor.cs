using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using UnityEngine.Scripting;

namespace Dexc
{
	/// <summary>Renderer-independent metadata associated with an input editor.</summary>
	public abstract class DebugInputEditorMetadata { }

	/// <summary>Marks an input editor provider and assigns its public priority.</summary>
	[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
	[Conditional("DEXC_DEBUGGER")]
	public sealed class DebugInputEditorAttribute : PreserveAttribute
	{
		public DebugInputEditorAttribute(uint priority = 0, string renderer = "")
		{
			Priority = priority;
			Renderer = renderer ?? string.Empty;
		}

		public uint Priority { get; }

		public string Renderer { get; }
	}

	/// <summary>Immutable definition data supplied to provider capability checks.</summary>
	public sealed class DebugInputEditorDescriptor
	{
		public DebugInputEditorDescriptor(
			Type inputType,
			DebugInputSchema schema,
			string semanticType,
			string renderer,
			IEnumerable<DebugInputEditorMetadata> metadata = null
		)
		{
			InputType = inputType ?? throw new ArgumentNullException(nameof(inputType));
			Schema = schema ?? throw new ArgumentNullException(nameof(schema));
			SemanticType = semanticType ?? string.Empty;
			Renderer = renderer ?? string.Empty;
			Metadata = new ReadOnlyCollection<DebugInputEditorMetadata>(
				(metadata ?? Enumerable.Empty<DebugInputEditorMetadata>())
					.Select(item => item ?? throw new ArgumentNullException(nameof(metadata)))
					.ToList()
			);
		}

		public Type InputType { get; }

		public DebugInputSchema Schema { get; }

		public string SemanticType { get; }

		public DebugValueKind ValueKind
		{
			get { return Schema.ValueKind; }
		}

		public string Renderer { get; }

		public IReadOnlyList<DebugInputEditorMetadata> Metadata { get; }
	}

	/// <summary>One-frame immutable view supplied to an editor provider.</summary>
	public sealed class DebugInputEditorContext
	{
		public DebugInputEditorContext(
			string fieldId,
			string label,
			string description,
			DebugValue currentValue,
			bool hasDefaultValue,
			DebugValue defaultValue,
			bool hasPreviousValue,
			DebugValue previousValue,
			IEnumerable<DebugValidationIssue> validationIssues,
			bool readOnly,
			DebugInputEditorDescriptor descriptor
		)
		{
			FieldId = fieldId ?? throw new ArgumentNullException(nameof(fieldId));
			Label = label ?? throw new ArgumentNullException(nameof(label));
			Description = description ?? throw new ArgumentNullException(nameof(description));
			CurrentValue = currentValue ?? throw new ArgumentNullException(nameof(currentValue));
			HasDefaultValue = hasDefaultValue;
			DefaultValue = hasDefaultValue ? defaultValue ?? throw new ArgumentNullException(nameof(defaultValue)) : null;
			HasPreviousValue = hasPreviousValue;
			PreviousValue = hasPreviousValue ? previousValue ?? throw new ArgumentNullException(nameof(previousValue)) : null;
			ValidationIssues = new ReadOnlyCollection<DebugValidationIssue>(
				(validationIssues ?? Enumerable.Empty<DebugValidationIssue>())
					.Select(issue => issue ?? throw new ArgumentNullException(nameof(validationIssues)))
					.ToList()
			);
			ReadOnly = readOnly;
			Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
		}

		public string FieldId { get; }

		public string Label { get; }

		public string Description { get; }

		public DebugValue CurrentValue { get; }

		public bool HasDefaultValue { get; }

		public DebugValue DefaultValue { get; }

		public bool HasPreviousValue { get; }

		public DebugValue PreviousValue { get; }

		public IReadOnlyList<DebugValidationIssue> ValidationIssues { get; }

		public bool ReadOnly { get; }

		public DebugInputEditorDescriptor Descriptor { get; }
	}

	/// <summary>Per-draw collector for the last proposed DebugValue.</summary>
	public sealed class DebugInputEdit
	{
		private DebugValue m_Value;

		public bool HasValue
		{
			get { return m_Value != null; }
		}

		public DebugValue Value
		{
			get { return m_Value; }
		}

		public void SetValue(DebugValue value)
		{
			m_Value = value ?? throw new ArgumentNullException(nameof(value));
		}
	}

	/// <summary>Renderer-independent input editor provider contract.</summary>
	public abstract class DebugInputEditor
	{
		public abstract bool CanEdit(DebugInputEditorDescriptor descriptor);

		public abstract void OnGUI(DebugInputEditorContext context, DebugInputEdit edit);
	}

	internal static class DexcDebugInputEditorSelector
	{
		internal static bool TrySelect(
			DebugInputEditorDescriptor descriptor,
			IEnumerable<Type> providerTypes,
			out DebugInputEditor provider,
			out string errorCode
		)
		{
			if (descriptor == null)
				throw new ArgumentNullException(nameof(descriptor));
			if (providerTypes == null)
				throw new ArgumentNullException(nameof(providerTypes));
			return DexcDebugInputEditorRegistry.Create(providerTypes).TrySelect(descriptor, out provider, out errorCode);
		}
	}

	/// <summary>Collected provider instances and descriptor-scoped selection cache.</summary>
	internal sealed class DexcDebugInputEditorRegistry
	{
		private readonly IReadOnlyList<ProviderRegistration> m_Providers;
		private readonly Dictionary<DebugInputEditorDescriptor, SelectionResult> m_Selections = new Dictionary<
			DebugInputEditorDescriptor,
			SelectionResult
		>(ReferenceComparer.Instance);

		private DexcDebugInputEditorRegistry(IEnumerable<Type> providerTypes)
		{
			var providers = new List<ProviderRegistration>();
			var seenTypes = new HashSet<Type>();
			foreach (var providerType in providerTypes)
			{
				if (
					providerType == null
					|| !seenTypes.Add(providerType)
					|| providerType.IsAbstract
					|| !typeof(DebugInputEditor).IsAssignableFrom(providerType)
				)
					continue;

				DebugInputEditorAttribute attribute;
				try
				{
					attribute = providerType
						.GetCustomAttributes(typeof(DebugInputEditorAttribute), false)
						.OfType<DebugInputEditorAttribute>()
						.SingleOrDefault();
				}
				catch (Exception)
				{
					continue;
				}

				if (attribute == null)
					continue;

				try
				{
					var instance = (DebugInputEditor)Activator.CreateInstance(providerType);
					providers.Add(new ProviderRegistration(instance, (long)attribute.Priority, attribute.Renderer));
				}
				catch (Exception)
				{
					// A broken provider is excluded from this registry.
				}
			}

			m_Providers = new ReadOnlyCollection<ProviderRegistration>(providers);
		}

		internal static DexcDebugInputEditorRegistry Create(IEnumerable<Type> providerTypes)
		{
			if (providerTypes == null)
				throw new ArgumentNullException(nameof(providerTypes));
			return new DexcDebugInputEditorRegistry(providerTypes);
		}

		internal bool TrySelect(DebugInputEditorDescriptor descriptor, out DebugInputEditor provider, out string errorCode)
		{
			if (descriptor == null)
				throw new ArgumentNullException(nameof(descriptor));
			if (m_Selections.TryGetValue(descriptor, out var cached))
			{
				provider = cached.Provider;
				errorCode = cached.ErrorCode;
				return cached.IsSuccess;
			}

			var candidates = new List<ProviderCandidate>();
			foreach (var registration in m_Providers)
			{
				if (
					registration.Renderer.Length != 0
					&& !string.Equals(registration.Renderer, descriptor.Renderer, StringComparison.Ordinal)
				)
					continue;

				try
				{
					if (registration.Provider.CanEdit(descriptor))
						candidates.Add(new ProviderCandidate(registration.Provider, registration.Priority));
				}
				catch (Exception)
				{
					// A broken provider is excluded locally; another provider may still edit this descriptor.
				}
			}

			if (candidates.Count == 0)
			{
				return CacheResult(descriptor, null, "no-provider", out provider, out errorCode);
			}

			var priority = candidates.Max(candidate => candidate.Priority);
			var winners = candidates.Where(candidate => candidate.Priority == priority).ToList();
			if (winners.Count != 1)
			{
				return CacheResult(descriptor, null, "provider-priority-conflict", out provider, out errorCode);
			}

			return CacheResult(descriptor, winners[0].Provider, string.Empty, out provider, out errorCode);
		}

		private bool CacheResult(
			DebugInputEditorDescriptor descriptor,
			DebugInputEditor provider,
			string errorCode,
			out DebugInputEditor selectedProvider,
			out string selectedErrorCode
		)
		{
			var result = new SelectionResult(provider, errorCode);
			m_Selections.Add(descriptor, result);
			selectedProvider = result.Provider;
			selectedErrorCode = result.ErrorCode;
			return result.IsSuccess;
		}

		private sealed class ProviderRegistration
		{
			internal ProviderRegistration(DebugInputEditor provider, long priority, string renderer)
			{
				Provider = provider;
				Priority = priority;
				Renderer = renderer ?? string.Empty;
			}

			internal DebugInputEditor Provider { get; }

			internal long Priority { get; }

			internal string Renderer { get; }
		}

		private readonly struct ProviderCandidate
		{
			public ProviderCandidate(DebugInputEditor provider, long priority)
			{
				Provider = provider;
				Priority = priority;
			}

			public DebugInputEditor Provider { get; }

			public long Priority { get; }
		}

		private sealed class SelectionResult
		{
			internal SelectionResult(DebugInputEditor provider, string errorCode)
			{
				Provider = provider;
				ErrorCode = errorCode ?? string.Empty;
			}

			internal bool IsSuccess
			{
				get { return Provider != null; }
			}

			internal DebugInputEditor Provider { get; }

			internal string ErrorCode { get; }
		}

		private sealed class ReferenceComparer : IEqualityComparer<DebugInputEditorDescriptor>
		{
			internal static readonly ReferenceComparer Instance = new ReferenceComparer();

			public bool Equals(DebugInputEditorDescriptor left, DebugInputEditorDescriptor right)
			{
				return ReferenceEquals(left, right);
			}

			public int GetHashCode(DebugInputEditorDescriptor value)
			{
				return value == null ? 0 : value.GetHashCode();
			}
		}
	}
}
