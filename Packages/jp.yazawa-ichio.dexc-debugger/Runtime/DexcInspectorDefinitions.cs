using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Dexc
{
	/// <summary>
	/// One member marked with <see cref="DebugInspectorSourceAttribute"/>. Which members carry the attribute is a
	/// definition; what they return when read is not, so evaluation stays with the caller and happens on every search.
	/// </summary>
	internal sealed class DexcInspectorSourceDefinition
	{
		internal MemberInfo Member;
		internal DebugInspectorSourceAttribute Attribute;
		internal string Identity;

		internal object Read()
		{
			var method = Member as MethodInfo;
			if (method != null)
				return method.Invoke(null, null);
			var property = Member as PropertyInfo;
			if (property != null)
				return property.GetValue(null, null);
			return ((FieldInfo)Member).GetValue(null);
		}
	}

	internal static class DexcInspectorDefinitionCollector
	{
		internal static IReadOnlyList<DexcInspectorSourceDefinition> Collect()
		{
			var definitions = new List<DexcInspectorSourceDefinition>();
			foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
			{
				if (assembly.IsDynamic || !DexcDefinitions.ReferencesRuntime(assembly))
					continue;
				Type[] types;
				try
				{
					types = assembly.GetTypes();
				}
				catch
				{
					continue;
				}
				foreach (var type in types)
				{
					MemberInfo[] members;
					try
					{
						members = type.GetMembers(
							BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly
						);
					}
					catch
					{
						continue;
					}
					foreach (var member in members)
					{
						var attribute = member
							.GetCustomAttributes(typeof(DebugInspectorSourceAttribute), false)
							.OfType<DebugInspectorSourceAttribute>()
							.SingleOrDefault();
						if (attribute == null)
							continue;
						// A method that needs arguments cannot be read, so it is not a source at all.
						if (member is MethodInfo && ((MethodInfo)member).GetParameters().Length != 0)
							continue;
						definitions.Add(
							new DexcInspectorSourceDefinition
							{
								Member = member,
								Attribute = attribute,
								Identity = type.FullName + "." + member.Name,
							}
						);
					}
				}
			}
			return definitions;
		}
	}
}
