using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Dexc
{
	/// <summary>One setting property, as declared. The declared default is read once from a throwaway instance.</summary>
	internal sealed class DexcSettingPropertyDefinition
	{
		internal PropertyInfo Property;
		internal DebugSettingAttribute Attribute;
		internal DebugRangeAttribute Range;
		internal bool HasSlider;
		internal Type ValueType;
		internal DebugValue DefaultValue;
		internal bool Persist;
	}

	/// <summary>One preset, as declared. Values are keyed by the property they set, not by an id.</summary>
	internal sealed class DexcSettingPresetDefinition
	{
		internal DebugPresetAttribute Attribute;
		internal Dictionary<PropertyInfo, DebugValue> Values;
	}

	/// <summary>One setting set, as declared. Holds no live instance.</summary>
	internal sealed class DexcSettingSetDefinition
	{
		internal Type Type;
		internal List<DexcSettingPropertyDefinition> Properties = new List<DexcSettingPropertyDefinition>();
		internal List<DexcSettingPresetDefinition> Presets = new List<DexcSettingPresetDefinition>();
	}

	internal sealed class DexcSettingDefinitionSet
	{
		internal DexcSettingDefinitionSet(
			IReadOnlyList<DexcSettingSetDefinition> sets,
			IReadOnlyList<DexcDefinitionDiagnostic> diagnostics,
			int excludedCount,
			int errorCount
		)
		{
			Sets = sets;
			Diagnostics = diagnostics;
			ExcludedCount = excludedCount;
			ErrorCount = errorCount;
		}

		internal IReadOnlyList<DexcSettingSetDefinition> Sets { get; }
		internal IReadOnlyList<DexcDefinitionDiagnostic> Diagnostics { get; }
		internal int ExcludedCount { get; }
		internal int ErrorCount { get; }
	}

	internal static class DexcSettingDefinitionCollector
	{
		internal static DexcSettingDefinitionSet Collect()
		{
			var sets = new List<DexcSettingSetDefinition>();
			var diagnostics = new List<DexcDefinitionDiagnostic>();
			var excluded = 0;
			var errors = 0;
			foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
			{
				if (assembly.IsDynamic || !DexcSettingRegistry.ReferencesRuntime(assembly))
					continue;
				Type[] types;
				try
				{
					types = assembly.GetTypes();
				}
				catch (Exception exception)
				{
					excluded++;
					errors++;
					diagnostics.Add(Error(assembly.FullName, exception));
					continue;
				}
				foreach (var type in types)
				{
					if (!DexcSettingRegistry.IsSettingSet(type))
						continue;
					object defaults;
					try
					{
						DexcSettingRegistry.ValidateSetType(type);
						defaults = Activator.CreateInstance(type);
					}
					catch (Exception exception)
					{
						excluded++;
						errors++;
						diagnostics.Add(Error(type.FullName, exception));
						continue;
					}
					var set = new DexcSettingSetDefinition { Type = type };
					ReadProperties(type, defaults, set, diagnostics, ref excluded, ref errors);
					ReadPresets(type, set, diagnostics, ref excluded, ref errors);
					sets.Add(set);
				}
			}
			RemoveDuplicatePaths(sets, diagnostics, ref excluded, ref errors);
			RemoveDuplicatePresets(sets, diagnostics, ref excluded, ref errors);
			return new DexcSettingDefinitionSet(sets, diagnostics, excluded, errors);
		}

		private static void ReadProperties(
			Type type,
			object defaults,
			DexcSettingSetDefinition set,
			List<DexcDefinitionDiagnostic> diagnostics,
			ref int excluded,
			ref int errors
		)
		{
			var setPersistent = type.GetCustomAttributes(typeof(DebugPersistentAttribute), false).Length != 0;
			foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
			{
				var attribute = property
					.GetCustomAttributes(typeof(DebugSettingAttribute), false)
					.OfType<DebugSettingAttribute>()
					.SingleOrDefault();
				if (attribute == null)
					continue;
				try
				{
					DexcSettingRegistry.ValidateSettingProperty(property, attribute);
					var range = property
						.GetCustomAttributes(typeof(DebugRangeAttribute), false)
						.OfType<DebugRangeAttribute>()
						.SingleOrDefault();
					var slider = property.GetCustomAttributes(typeof(DebugSliderAttribute), false).Any();
					DexcSettingRegistry.ValidateConstraint(property.PropertyType, range, slider);
					set.Properties.Add(
						new DexcSettingPropertyDefinition
						{
							Property = property,
							Attribute = attribute,
							Range = range,
							HasSlider = slider,
							ValueType = property.PropertyType,
							DefaultValue = DexcSettingRegistry.ToValue(property.GetValue(defaults, null), property.PropertyType),
							Persist = attribute.Persist || setPersistent,
						}
					);
				}
				catch (Exception exception)
				{
					excluded++;
					errors++;
					diagnostics.Add(Error(type.FullName + "." + property.Name, exception));
				}
			}
		}

		private static void ReadPresets(
			Type type,
			DexcSettingSetDefinition set,
			List<DexcDefinitionDiagnostic> diagnostics,
			ref int excluded,
			ref int errors
		)
		{
			foreach (var property in type.GetProperties(BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy))
			{
				var attribute = property
					.GetCustomAttributes(typeof(DebugPresetAttribute), false)
					.OfType<DebugPresetAttribute>()
					.SingleOrDefault();
				if (attribute == null)
					continue;
				try
				{
					if (property.PropertyType != type || property.GetGetMethod(false) == null)
						throw new InvalidOperationException("Preset must return its setting set type and have a public getter.");
					var presetSet = property.GetValue(null, null);
					if (presetSet == null)
						throw new InvalidOperationException("Preset getter returned null.");
					set.Presets.Add(
						new DexcSettingPresetDefinition
						{
							Attribute = attribute,
							Values = set.Properties.ToDictionary(
								item => item.Property,
								item => DexcSettingRegistry.ToValue(item.Property.GetValue(presetSet, null), item.ValueType)
							),
						}
					);
				}
				catch (Exception exception)
				{
					excluded++;
					errors++;
					diagnostics.Add(Error(type.FullName + "." + property.Name, exception));
				}
			}
		}

		/// <summary>A path names one setting across the whole project, so a clash removes every claimant.</summary>
		private static void RemoveDuplicatePaths(
			List<DexcSettingSetDefinition> sets,
			List<DexcDefinitionDiagnostic> diagnostics,
			ref int excluded,
			ref int errors
		)
		{
			var all = sets.SelectMany(set => set.Properties.Select(property => new { set, property })).ToList();
			foreach (
				var duplicate in all.GroupBy(item => item.property.Attribute.Path, StringComparer.Ordinal)
					.Where(group => group.Count() > 1)
					.ToList()
			)
			{
				foreach (var item in duplicate)
				{
					item.set.Properties.Remove(item.property);
					foreach (var preset in item.set.Presets)
						preset.Values.Remove(item.property.Property);
					excluded++;
					errors++;
					diagnostics.Add(
						new DexcDefinitionDiagnostic(
							"duplicate-setting-path",
							"All settings using a duplicate path were excluded.",
							duplicate.Key
						)
					);
				}
			}
		}

		private static void RemoveDuplicatePresets(
			List<DexcSettingSetDefinition> sets,
			List<DexcDefinitionDiagnostic> diagnostics,
			ref int excluded,
			ref int errors
		)
		{
			var all = sets.SelectMany(set => set.Presets.Select(preset => new { set, preset })).ToList();
			foreach (
				var duplicate in all.GroupBy(item => item.preset.Attribute.Id, StringComparer.Ordinal)
					.Where(group => group.Count() > 1)
					.ToList()
			)
			{
				foreach (var item in duplicate)
				{
					item.set.Presets.Remove(item.preset);
					excluded++;
					errors++;
					diagnostics.Add(
						new DexcDefinitionDiagnostic(
							"duplicate-setting-preset",
							"All presets using a duplicate ID were excluded.",
							item.preset.Attribute.DisplayName
						)
					);
				}
			}
		}

		private static DexcDefinitionDiagnostic Error(string identity, Exception exception)
		{
			return new DexcDefinitionDiagnostic("invalid-setting-definition", "A setting definition was excluded.", identity, exception);
		}
	}
}
