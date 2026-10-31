using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UnityEngine.Scripting;

namespace Dexc
{
	/// <summary>
	/// Names a start configuration and says where starting it goes. The tag is the name the boot scene asks for; the scene
	/// is where the game would have started if Dexc were not installed.
	/// </summary>
	[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
	[Conditional("DEXC_DEBUGGER")]
	public sealed class DebugBootMenuAttribute : PreserveAttribute
	{
		public DebugBootMenuAttribute(string title = null)
		{
			Title = title;
			Groups = Type.EmptyTypes;
		}

		public string Title { get; }
		public int Order { get; set; }

		/// <summary>The name of this set. Required; a <see cref="DebugBootScene"/> asks for a set by this name.</summary>
		public string Tag { get; set; }

		/// <summary>Where Start goes. Required. A scene name or path, as Build Settings knows it.</summary>
		public string Scene { get; set; }

		/// <summary>The blocks this set pulls in. Each must carry <see cref="DebugBootGroupAttribute"/>.</summary>
		public Type[] Groups { get; set; }
	}

	/// <summary>
	/// Marks a reusable group of items. A block has no name to start by and no destination; several sets can list the same
	/// block, and because the stored values are keyed by the block they are the same values in each.
	/// </summary>
	[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
	[Conditional("DEXC_DEBUGGER")]
	public sealed class DebugBootGroupAttribute : PreserveAttribute
	{
		public DebugBootGroupAttribute(string title = null)
		{
			Title = title;
		}

		public string Title { get; }
		public int Order { get; set; }
	}

	/// <summary>Marks a property as one editable item of a boot set or block.</summary>
	[AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
	[Conditional("DEXC_DEBUGGER")]
	public sealed class DebugBootItemAttribute : PreserveAttribute
	{
		public DebugBootItemAttribute(string label = null)
		{
			Label = label;
			Persist = true;
		}

		public string Label { get; }
		public string Description { get; set; }
		public int Order { get; set; }

		/// <summary>Whether the value is remembered for the next launch. Default true.</summary>
		public bool Persist { get; set; }

		public double Step { get; set; }
	}

	/// <summary>
	/// A group of boot items. Derive from this and add <see cref="DebugBootGroupAttribute"/> to make a block that sets can
	/// share; derive from <see cref="DebugBootSet"/> instead for a set that can be started.
	/// </summary>
	[Preserve]
	public abstract class DebugBootBlock
	{
		/// <summary>Called after one of this object's items changed. <paramref name="propertyName"/> is the item.</summary>
		protected virtual void OnChanged(string propertyName) { }

		internal void RaiseChanged(string propertyName)
		{
			OnChanged(propertyName);
		}
	}

	/// <summary>
	/// One start configuration. The boot screen edits its items and then starts it, which hands the values to the game and
	/// loads the scene the game would have started from on its own.
	/// </summary>
	[Preserve]
	public abstract class DebugBootSet : DebugBootBlock
	{
		/// <summary>
		/// Runs once the set has been opened and its stored values are back, before anything is drawn. Use it for a choice
		/// that has to take effect for the boot screen itself rather than for the game - the renderer it draws with, say.
		/// </summary>
		protected virtual void OnOpened(DebugBootGroupsValues values) { }

		/// <summary>
		/// Runs before the destination is loaded. Write the values into the game's own configuration here; the game must not
		/// read them back out of Dexc, or it stops working in a build with the debugger disabled.
		/// </summary>
		protected virtual void OnStarting(DebugBootGroupsValues values) { }

		/// <summary>Runs right after the destination scene was loaded.</summary>
		protected virtual void OnLoadSceneAfter(DebugBootGroupsValues values) { }

		/// <summary>Where Start goes. Defaults to the declared Scene; override to decide at runtime.</summary>
		protected virtual string ResolveScene()
		{
			var attribute = GetType()
				.GetCustomAttributes(typeof(DebugBootMenuAttribute), false)
				.OfType<DebugBootMenuAttribute>()
				.FirstOrDefault();
			return attribute == null ? null : attribute.Scene;
		}

		internal void RaiseOpened(DebugBootGroupsValues values)
		{
			OnOpened(values);
		}

		internal void RaiseStarting(DebugBootGroupsValues values)
		{
			OnStarting(values);
		}

		internal void RaiseLoadSceneAfter(DebugBootGroupsValues values)
		{
			OnLoadSceneAfter(values);
		}

		internal string ResolveSceneInternal()
		{
			return ResolveScene();
		}
	}

	/// <summary>
	/// The blocks of the set being started. Reachable only from the transition hooks, which is the whole window in which
	/// boot values exist.
	/// </summary>
	public sealed class DebugBootGroupsValues
	{
		private readonly Dictionary<Type, DebugBootBlock> m_Blocks;

		internal DebugBootGroupsValues(Dictionary<Type, DebugBootBlock> blocks)
		{
			m_Blocks = blocks;
		}

		/// <summary>The block's values. Only the blocks this set listed in Groups can be read.</summary>
		public T Get<T>()
			where T : DebugBootBlock
		{
			if (m_Blocks.TryGetValue(typeof(T), out var block))
				return (T)block;
			throw new InvalidOperationException(
				typeof(T).FullName + " is not one of this set's groups. Add it to DebugBootMenu.Groups to read it here."
			);
		}
	}

	/// <summary>
	/// One set opened for editing, with the values that go with it. A boot screen holds one of these for as long as it is
	/// open; nothing static keeps it, so two screens never share values and closing one drops what it held.
	/// </summary>
	public sealed class DebugBootSelection
	{
		private readonly DexcBootSetDefinition m_Definition;
		private readonly DebugBootSet m_Set;
		private readonly Dictionary<Type, DebugBootBlock> m_Blocks = new Dictionary<Type, DebugBootBlock>();
		private readonly List<DebugPanel> m_Panels = new List<DebugPanel>();
		private readonly DebugBootGroupsValues m_Values;

		private DebugBootSelection(DexcBootSetDefinition definition)
		{
			m_Definition = definition;
			m_Set = (DebugBootSet)Activator.CreateInstance(definition.Type);
			DexcBootValue.Restore(m_Set, definition.Items);
			foreach (var block in definition.Blocks)
			{
				var instance = (DebugBootBlock)Activator.CreateInstance(block.Type);
				DexcBootValue.Restore(instance, block.Items);
				m_Blocks.Add(block.Type, instance);
			}
			m_Values = new DebugBootGroupsValues(m_Blocks);
			m_Panels.Add(BuildPanel(Title, definition.Attribute.Order, m_Set, definition.Items));
			foreach (var block in definition.Blocks)
			{
				m_Panels.Add(
					BuildPanel(
						string.IsNullOrEmpty(block.Attribute.Title) ? DexcBootValue.Humanize(block.Type.Name) : block.Attribute.Title,
						block.Attribute.Order,
						m_Blocks[block.Type],
						block.Items
					)
				);
			}
		}

		/// <summary>The name this set answers to.</summary>
		public string Tag => m_Definition.Attribute.Tag;

		public string Title =>
			string.IsNullOrEmpty(m_Definition.Attribute.Title)
				? DexcBootValue.Humanize(m_Definition.Type.Name)
				: m_Definition.Attribute.Title;

		/// <summary>The set's own items first, then one panel per block.</summary>
		public IReadOnlyList<DebugPanel> Panels => m_Panels;

		/// <summary>
		/// Opens the set with this name. Throws when the name is empty or no set answers to it: boot is a development-time
		/// feature, and a typo that silently starts the wrong thing costs more than stopping here.
		/// </summary>
		public static DebugBootSelection Open(string tag)
		{
			if (string.IsNullOrWhiteSpace(tag))
				throw new ArgumentException("A boot tag is required.", nameof(tag));
			var definition = DexcDefinitions.Boot.FindByTag(tag);
			if (definition == null)
				throw new InvalidOperationException(
					"No boot set is tagged \""
						+ tag
						+ "\". Declared tags: "
						+ string.Join(", ", DexcDefinitions.Boot.Sets.Select(set => set.Attribute.Tag))
				);
			var selection = new DebugBootSelection(definition);
			selection.m_Set.RaiseOpened(selection.m_Values);
			return selection;
		}

		/// <summary>Where Start goes, or null when the set decided there is nowhere to go.</summary>
		public string ResolveScene()
		{
			return m_Set.ResolveSceneInternal();
		}

		/// <summary>Writes pending values to storage.</summary>
		public void Flush()
		{
			DebugStorage.Flush();
		}

		/// <summary>Returns this set and its blocks to their declared values and forgets what was stored for them.</summary>
		public void Reset()
		{
			DexcBootValue.Reset(m_Set, m_Definition.Items);
			foreach (var block in m_Definition.Blocks)
				DexcBootValue.Reset(m_Blocks[block.Type], block.Items);
			DebugStorage.Flush();
		}

		/// <summary>
		/// Hands the values to the game and goes to the destination. Does nothing when the set resolved no scene: there is
		/// nowhere to go, and picking somewhere else is not the library's call.
		/// </summary>
		public bool Start(Action<string> loadScene)
		{
			if (loadScene == null)
				throw new ArgumentNullException(nameof(loadScene));
			Flush();
			m_Set.RaiseStarting(m_Values);
			var scene = ResolveScene();
			if (string.IsNullOrEmpty(scene))
				return false;
			loadScene(scene);
			m_Set.RaiseLoadSceneAfter(m_Values);
			return true;
		}

		private static DebugPanel BuildPanel(string title, int order, DebugBootBlock owner, IReadOnlyList<DexcBootItemDefinition> items)
		{
			var panel = new DebugPanel(title) { Order = order };
			foreach (var item in items)
				panel.Add(DexcBootValue.BuildWidget(owner, item));
			return panel;
		}
	}

	/// <summary>Reading, writing and storing one boot item. The value lives on the instance its owner holds.</summary>
	internal static class DexcBootValue
	{
		internal static string KeyFor(Type type, PropertyInfo property)
		{
			return "Boot/" + type.FullName + "/" + property.Name;
		}

		internal static void Restore(DebugBootBlock instance, IReadOnlyList<DexcBootItemDefinition> items)
		{
			var type = instance.GetType();
			foreach (var item in items)
			{
				if (!item.Attribute.Persist || !DebugStorage.TryLoad(KeyFor(type, item.Property), out var text))
					continue;
				try
				{
					var value = ParseValue(text, item.Property.PropertyType);
					if (value != null)
						item.Property.SetValue(instance, value, null);
				}
				catch (Exception exception)
				{
					UnityEngine.Debug.LogWarning(
						"Dexc boot restore failed for " + type.Name + "." + item.Property.Name + ": " + exception.Message
					);
				}
			}
		}

		internal static void Reset(DebugBootBlock instance, IReadOnlyList<DexcBootItemDefinition> items)
		{
			var type = instance.GetType();
			var declared = Activator.CreateInstance(type);
			foreach (var item in items)
			{
				DebugStorage.Delete(KeyFor(type, item.Property));
				item.Property.SetValue(instance, item.Property.GetValue(declared, null), null);
				RaiseChanged(instance, item.Property.Name);
			}
		}

		/// <summary>Tells the owner one item changed. A throwing handler is logged so it cannot stop the menu.</summary>
		private static void RaiseChanged(DebugBootBlock owner, string propertyName)
		{
			try
			{
				owner.RaiseChanged(propertyName);
			}
			catch (Exception exception)
			{
				UnityEngine.Debug.LogWarning("Dexc boot OnChanged failed: " + exception.Message);
			}
		}

		internal static DebugWidget BuildWidget(DebugBootBlock owner, DexcBootItemDefinition item)
		{
			var property = item.Property;
			var propertyType = property.PropertyType;
			var type = owner.GetType();
			var label = string.IsNullOrEmpty(item.Attribute.Label) ? Humanize(property.Name) : item.Attribute.Label;
			var key = item.Attribute.Persist ? KeyFor(type, property) : null;
			Action<object> write = value =>
			{
				property.SetValue(owner, value, null);
				if (key != null)
					DebugStorage.Save(key, FormatValue(value, propertyType));
				RaiseChanged(owner, property.Name);
			};
			Func<object> read = () => property.GetValue(owner, null);
			DebugWidget widget;
			if (propertyType == typeof(bool))
				widget = new DebugToggleWidget(label, () => (bool)read(), value => write(value));
			else if (propertyType.IsEnum)
			{
				var names = Enum.GetNames(propertyType);
				var values = Enum.GetValues(propertyType).Cast<object>().ToList();
				widget = new DebugChoiceWidget(label, names, () => Math.Max(0, values.IndexOf(read())), index => write(values[index]));
			}
			else if (propertyType == typeof(string))
				widget = new DebugTextWidget(label, () => (string)read(), value => write(value));
			else
			{
				var range = property.GetCustomAttributes(typeof(DebugRangeAttribute), true).OfType<DebugRangeAttribute>().FirstOrDefault();
				widget = new DebugNumberWidget(
					label,
					() => Convert.ToDouble(read(), CultureInfo.InvariantCulture),
					value => write(Convert.ChangeType(value, propertyType, CultureInfo.InvariantCulture)),
					range != null ? range.Minimum : TypeMinimum(propertyType),
					range != null ? range.Maximum : TypeMaximum(propertyType),
					item.Attribute.Step,
					IsInteger(propertyType)
				);
			}
			widget.Description = item.Attribute.Description;
			// The value a fresh block declares, which is what Reset goes back to; the menu marks an item that differs.
			var declared = property.GetValue(Activator.CreateInstance(type), null);
			widget.IsChanged = () => !Equals(read(), declared);
			return widget;
		}

		internal static bool IsSupported(Type type)
		{
			return type == typeof(bool) || type == typeof(string) || type.IsEnum || IsNumeric(type);
		}

		private static bool IsNumeric(Type type)
		{
			return type == typeof(sbyte)
				|| type == typeof(byte)
				|| type == typeof(short)
				|| type == typeof(ushort)
				|| type == typeof(int)
				|| type == typeof(uint)
				|| type == typeof(long)
				|| type == typeof(ulong)
				|| type == typeof(float)
				|| type == typeof(double);
		}

		private static bool IsInteger(Type type)
		{
			return IsNumeric(type) && type != typeof(float) && type != typeof(double);
		}

		private static object ParseValue(string text, Type type)
		{
			if (type == typeof(bool))
				return text == "1" || string.Equals(text, "true", StringComparison.OrdinalIgnoreCase);
			if (type == typeof(string))
				return text;
			if (type.IsEnum)
				return Enum.IsDefined(type, text) ? Enum.Parse(type, text, false) : null;
			if (
				!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
				|| double.IsNaN(number)
				|| double.IsInfinity(number)
				|| type == typeof(float) && Math.Abs(number) > float.MaxValue
			)
				return null;
			return Convert.ChangeType(number, type, CultureInfo.InvariantCulture);
		}

		private static string FormatValue(object value, Type type)
		{
			if (type == typeof(bool))
				return (bool)value ? "1" : "0";
			if (type == typeof(string))
				return (string)value ?? string.Empty;
			if (type.IsEnum)
				return value.ToString();
			return Convert.ToDouble(value, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture);
		}

		private static double TypeMinimum(Type type)
		{
			if (type == typeof(sbyte))
				return sbyte.MinValue;
			if (type == typeof(byte))
				return byte.MinValue;
			if (type == typeof(short))
				return short.MinValue;
			if (type == typeof(ushort))
				return ushort.MinValue;
			if (type == typeof(int))
				return int.MinValue;
			if (type == typeof(uint))
				return uint.MinValue;
			if (type == typeof(long))
				return long.MinValue;
			if (type == typeof(ulong))
				return ulong.MinValue;
			if (type == typeof(float))
				return float.MinValue;
			return double.NegativeInfinity;
		}

		private static double TypeMaximum(Type type)
		{
			if (type == typeof(sbyte))
				return sbyte.MaxValue;
			if (type == typeof(byte))
				return byte.MaxValue;
			if (type == typeof(short))
				return short.MaxValue;
			if (type == typeof(ushort))
				return ushort.MaxValue;
			if (type == typeof(int))
				return int.MaxValue;
			if (type == typeof(uint))
				return uint.MaxValue;
			// As doubles, long.MaxValue and ulong.MaxValue round up to 2^63 and 2^64, which the types cannot hold, so a value
			// clamped there could not be converted back. The largest doubles below them can.
			if (type == typeof(long))
				return 9223372036854774784d;
			if (type == typeof(ulong))
				return 18446744073709549568d;
			// A float past its range would become Infinity.
			if (type == typeof(float))
				return float.MaxValue;
			return double.PositiveInfinity;
		}

		internal static string Humanize(string name)
		{
			var builder = new System.Text.StringBuilder(name.Length + 4);
			for (var i = 0; i < name.Length; i++)
			{
				var c = name[i];
				if (i > 0 && char.IsUpper(c) && (char.IsLower(name[i - 1]) || i + 1 < name.Length && char.IsLower(name[i + 1])))
					builder.Append(' ');
				builder.Append(c);
			}
			return builder.ToString();
		}
	}
}
