using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Dexc
{
	/// <summary>One item, as declared. Which property and what the attribute says about it.</summary>
	internal sealed class DexcBootItemDefinition
	{
		internal PropertyInfo Property;
		internal DebugBootItemAttribute Attribute;
	}

	/// <summary>A block: items that several sets can share. It has no name of its own and no destination.</summary>
	internal sealed class DexcBootBlockDefinition
	{
		internal Type Type;
		internal DebugBootGroupAttribute Attribute;
		internal IReadOnlyList<DexcBootItemDefinition> Items;
	}

	/// <summary>A set: a named start configuration with a destination scene and the blocks it pulls in.</summary>
	internal sealed class DexcBootSetDefinition
	{
		internal Type Type;
		internal DebugBootMenuAttribute Attribute;
		internal IReadOnlyList<DexcBootItemDefinition> Items;
		internal IReadOnlyList<DexcBootBlockDefinition> Blocks;
	}

	internal sealed class DexcBootDefinitionSet
	{
		internal DexcBootDefinitionSet(IReadOnlyList<DexcBootSetDefinition> sets, IReadOnlyList<DexcBootBlockDefinition> blocks)
		{
			Sets = sets;
			Blocks = blocks;
		}

		internal IReadOnlyList<DexcBootSetDefinition> Sets { get; }
		internal IReadOnlyList<DexcBootBlockDefinition> Blocks { get; }

		internal DexcBootSetDefinition FindByTag(string tag)
		{
			return Sets.FirstOrDefault(set => string.Equals(set.Attribute.Tag, tag, StringComparison.Ordinal));
		}
	}

	internal static class DexcBootDefinitionCollector
	{
		internal static DexcBootDefinitionSet Collect()
		{
			var blockTypes = new Dictionary<Type, DexcBootBlockDefinition>();
			var setTypes = new List<Tuple<Type, DebugBootMenuAttribute>>();
			var marker = typeof(DexcRuntimeAssemblyMarker).Assembly;
			foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
			{
				if (assembly.IsDynamic)
					continue;
				bool references;
				try
				{
					references =
						assembly == marker || assembly.GetReferencedAssemblies().Any(reference => reference.Name == marker.GetName().Name);
				}
				catch
				{
					references = false;
				}
				if (!references)
					continue;
				Type[] types;
				try
				{
					types = assembly.GetTypes();
				}
				catch (ReflectionTypeLoadException exception)
				{
					types = exception.Types.Where(type => type != null).ToArray();
				}
				catch
				{
					continue;
				}
				foreach (var type in types)
				{
					if (type.IsAbstract)
						continue;
					var group = type.GetCustomAttributes(typeof(DebugBootGroupAttribute), false)
						.OfType<DebugBootGroupAttribute>()
						.FirstOrDefault();
					if (group != null)
					{
						if (!typeof(DebugBootBlock).IsAssignableFrom(type))
							throw new InvalidOperationException(
								"Dexc boot block " + type.FullName + " must derive from " + nameof(DebugBootBlock) + "."
							);
						blockTypes[type] = new DexcBootBlockDefinition
						{
							Type = type,
							Attribute = group,
							Items = ReadItems(type),
						};
						continue;
					}
					var menu = type.GetCustomAttributes(typeof(DebugBootMenuAttribute), false)
						.OfType<DebugBootMenuAttribute>()
						.FirstOrDefault();
					if (menu != null)
						setTypes.Add(Tuple.Create(type, menu));
				}
			}

			var sets = new List<DexcBootSetDefinition>();
			foreach (var pair in setTypes)
				sets.Add(ReadSet(pair.Item1, pair.Item2, blockTypes));
			// A tag names one set. Two sets answering to the same name means the boot scene cannot say which it wants.
			foreach (var duplicate in sets.GroupBy(set => set.Attribute.Tag, StringComparer.Ordinal).Where(group => group.Count() > 1))
				throw new InvalidOperationException(
					"Dexc boot tag \""
						+ duplicate.Key
						+ "\" is declared by "
						+ string.Join(", ", duplicate.Select(set => set.Type.FullName))
				);
			return new DexcBootDefinitionSet(sets, blockTypes.Values.ToList());
		}

		private static DexcBootSetDefinition ReadSet(
			Type type,
			DebugBootMenuAttribute menu,
			Dictionary<Type, DexcBootBlockDefinition> blocks
		)
		{
			if (!typeof(DebugBootSet).IsAssignableFrom(type))
				throw new InvalidOperationException("Dexc boot set " + type.FullName + " must derive from " + nameof(DebugBootSet) + ".");
			if (string.IsNullOrWhiteSpace(menu.Tag))
				throw new InvalidOperationException("Dexc boot set " + type.FullName + " must declare a Tag.");
			if (string.IsNullOrWhiteSpace(menu.Scene))
				throw new InvalidOperationException("Dexc boot set " + type.FullName + " must declare a Scene.");
			var declared = new List<DexcBootBlockDefinition>();
			var seen = new HashSet<Type>();
			foreach (var blockType in menu.Groups ?? Type.EmptyTypes)
			{
				if (blockType == null)
					throw new InvalidOperationException("Dexc boot set " + type.FullName + " lists a null group.");
				if (!seen.Add(blockType))
					throw new InvalidOperationException(
						"Dexc boot set " + type.FullName + " lists " + blockType.FullName + " more than once."
					);
				if (!blocks.TryGetValue(blockType, out var block))
					throw new InvalidOperationException(
						"Dexc boot set "
							+ type.FullName
							+ " lists "
							+ blockType.FullName
							+ ", which is not a block. A block derives from "
							+ nameof(DebugBootBlock)
							+ " and carries ["
							+ nameof(DebugBootGroupAttribute)
							+ "]."
					);
				declared.Add(block);
			}
			return new DexcBootSetDefinition
			{
				Type = type,
				Attribute = menu,
				Items = ReadItems(type),
				Blocks = declared,
			};
		}

		private static IReadOnlyList<DexcBootItemDefinition> ReadItems(Type type)
		{
			var items = new List<DexcBootItemDefinition>();
			foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
			{
				var attribute = property
					.GetCustomAttributes(typeof(DebugBootItemAttribute), true)
					.OfType<DebugBootItemAttribute>()
					.FirstOrDefault();
				if (
					attribute == null
					|| property.GetIndexParameters().Length != 0
					|| property.GetGetMethod(true) == null
					|| property.GetSetMethod(true) == null
				)
					continue;
				if (!DexcBootValue.IsSupported(property.PropertyType))
				{
					UnityEngine.Debug.LogWarning(
						"Dexc boot item " + type.Name + "." + property.Name + " has an unsupported type " + property.PropertyType.Name + "."
					);
					continue;
				}
				items.Add(new DexcBootItemDefinition { Property = property, Attribute = attribute });
			}
			// OrderBy is stable, so items with the same Order keep the order they were declared in. List.Sort is not.
			return items.OrderBy(item => item.Attribute.Order).ToList();
		}
	}
}
