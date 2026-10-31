using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace Dexc
{
	/// <summary>
	/// A problem found while reading definitions. Collection does not know which state will record it, so the record is
	/// returned and the caller writes it into its own diagnostic store.
	/// </summary>
	internal sealed class DexcDefinitionDiagnostic
	{
		internal DexcDefinitionDiagnostic(string code, string message, string identity, Exception exception = null)
		{
			Code = code;
			Message = message;
			Identity = identity;
			Exception = exception;
		}

		internal string Code { get; }
		internal string Message { get; }
		internal string Identity { get; }
		internal Exception Exception { get; }
	}

	/// <summary>One public field of a command, as declared. Holds no value and no instance.</summary>
	internal sealed class DexcCommandFieldDefinition
	{
		internal FieldInfo Field;
		internal bool IsInput;
		internal bool IsLabel;
		internal bool IsHeader;
		internal DebugInputSchema Schema;
		internal DebugRangeAttribute Range;
		internal bool IsSlider;
		internal bool IsMultiline;
	}

	/// <summary>One command, as declared. Derived from the attribute and the type only.</summary>
	internal sealed class DexcCommandDefinition
	{
		internal Type Type;
		internal DebugCommandAttribute Attribute;
		internal IReadOnlyList<DexcCommandFieldDefinition> Fields;
		internal bool IsAsync;
		internal bool HasOutput;
		internal TimeSpan Timeout;
	}

	/// <summary>Everything one reflection pass found, plus what it had to exclude.</summary>
	internal sealed class DexcCommandDefinitionSet
	{
		internal DexcCommandDefinitionSet(
			IReadOnlyList<DexcCommandDefinition> definitions,
			IReadOnlyList<DexcDefinitionDiagnostic> diagnostics,
			int excludedCount,
			int errorCount
		)
		{
			Definitions = definitions;
			Diagnostics = diagnostics;
			ExcludedCount = excludedCount;
			ErrorCount = errorCount;
		}

		internal IReadOnlyList<DexcCommandDefinition> Definitions { get; }
		internal IReadOnlyList<DexcDefinitionDiagnostic> Diagnostics { get; }
		internal int ExcludedCount { get; }
		internal int ErrorCount { get; }
	}

	/// <summary>
	/// The definitions the library derives from attributes and the loaded assemblies. They do not depend on a session or
	/// on the root, so the Editor can read them while nothing is playing. The lifetime is the assembly load: definitions
	/// only change when assemblies change, and in Unity that comes with a domain reload which drops this cache with it.
	/// Collection runs on first access, so a project that never calls a Dexc API never pays for the reflection.
	/// </summary>
	internal static class DexcDefinitions
	{
		private static DexcCommandDefinitionSet s_Commands;
		private static DexcSettingDefinitionSet s_Settings;
		private static IReadOnlyList<DexcInspectorSourceDefinition> s_InspectorSources;
		private static DexcBootDefinitionSet s_Boot;
		private static IReadOnlyList<Type> s_InputEditors;
		private static readonly Dictionary<string, Type> s_Types = new Dictionary<string, Type>(StringComparer.Ordinal);

		internal static DexcCommandDefinitionSet Commands
		{
			get { return s_Commands ?? (s_Commands = DexcCommandDefinitionCollector.Collect()); }
		}

		internal static DexcSettingDefinitionSet Settings
		{
			get { return s_Settings ?? (s_Settings = DexcSettingDefinitionCollector.Collect()); }
		}

		internal static IReadOnlyList<DexcInspectorSourceDefinition> InspectorSources
		{
			get { return s_InspectorSources ?? (s_InspectorSources = DexcInspectorDefinitionCollector.Collect()); }
		}

		internal static DexcBootDefinitionSet Boot
		{
			get { return s_Boot ?? (s_Boot = DexcBootDefinitionCollector.Collect()); }
		}

		/// <summary>The non-abstract <see cref="DebugInputEditor"/> types marked with <see cref="DebugInputEditorAttribute"/>.</summary>
		internal static IReadOnlyList<Type> InputEditors
		{
			get { return s_InputEditors ?? (s_InputEditors = CollectInputEditors()); }
		}

		/// <summary>
		/// The type a <see cref="Type.FullName"/> names in the loaded assemblies, or null. A name outside the core library
		/// otherwise means asking every assembly in turn, so each name is looked up once.
		/// </summary>
		internal static Type ResolveType(string name)
		{
			if (string.IsNullOrEmpty(name))
				return null;
			if (s_Types.TryGetValue(name, out var type))
				return type;
			type = Type.GetType(name);
			if (type == null)
			{
				foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
				{
					type = assembly.GetType(name, false);
					if (type != null)
						break;
				}
			}
			s_Types[name] = type;
			return type;
		}

		/// <summary>
		/// Whether an assembly can declare Dexc definitions. Every attribute and base type the definitions use lives in the
		/// runtime assembly, so the others are never read; in the Editor they are most of what is loaded.
		/// </summary>
		internal static bool ReferencesRuntime(Assembly assembly)
		{
			var runtime = typeof(DexcRuntimeAssemblyMarker).Assembly;
			if (assembly == runtime)
				return true;
			try
			{
				var name = runtime.GetName().Name;
				return assembly.GetReferencedAssemblies().Any(reference => reference.Name == name);
			}
			catch
			{
				return false;
			}
		}

		static IReadOnlyList<Type> CollectInputEditors()
		{
			var result = new List<Type>();
			foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
			{
				if (assembly.IsDynamic || !ReferencesRuntime(assembly))
					continue;
				Type[] types;
				try
				{
					types = assembly.GetTypes();
				}
				catch (ReflectionTypeLoadException exception)
				{
					types = exception.Types;
				}
				catch (Exception)
				{
					continue;
				}
				foreach (var type in types)
					if (
						type != null
						&& !type.IsAbstract
						&& typeof(DebugInputEditor).IsAssignableFrom(type)
						&& type.GetCustomAttributes(typeof(DebugInputEditorAttribute), false).Length != 0
					)
						result.Add(type);
			}
			return result;
		}
	}

	internal static class DexcCommandDefinitionCollector
	{
		internal static DexcCommandDefinitionSet Collect()
		{
			var definitions = new List<DexcCommandDefinition>();
			var diagnostics = new List<DexcDefinitionDiagnostic>();
			var excluded = 0;
			var errors = 0;
			foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
			{
				if (assembly.IsDynamic || !DexcDefinitions.ReferencesRuntime(assembly))
					continue;
				Type[] types;
				try
				{
					types = assembly.GetTypes();
				}
				catch (ReflectionTypeLoadException exception)
				{
					types = exception.Types.Where(type => type != null).ToArray();
					excluded += exception.LoaderExceptions.Length;
					errors += exception.LoaderExceptions.Length;
				}
				catch
				{
					excluded++;
					errors++;
					continue;
				}
				foreach (var type in types)
				{
					if (type.IsAbstract || !IsCommand(type))
						continue;
					try
					{
						var attribute = type.GetCustomAttributes(typeof(DebugCommandAttribute), false)
							.OfType<DebugCommandAttribute>()
							.SingleOrDefault();
						if (attribute == null)
							continue;
						definitions.Add(Read(type, attribute));
					}
					catch (Exception exception)
					{
						excluded++;
						errors++;
						diagnostics.Add(
							new DexcDefinitionDiagnostic(
								"invalid-command-definition",
								"A command definition was excluded.",
								type.FullName,
								exception
							)
						);
					}
				}
			}
			RemoveDuplicatePaths(definitions, diagnostics, ref excluded, ref errors);
			definitions.Sort(
				(left, right) =>
				{
					var order = left.Attribute.Order.CompareTo(right.Attribute.Order);
					return order != 0 ? order : StringComparer.Ordinal.Compare(left.Attribute.Path, right.Attribute.Path);
				}
			);
			return new DexcCommandDefinitionSet(definitions, diagnostics, excluded, errors);
		}

		/// <summary>
		/// Reads one command. A throwaway instance is created to read the input schemas and the timeout; nothing from it
		/// is kept, so no user object outlives the collection.
		/// </summary>
		private static DexcCommandDefinition Read(Type type, DebugCommandAttribute attribute)
		{
			attribute.ValidateDefinition();
			var instance = Activator.CreateInstance(type);
			var fields = new List<DexcCommandFieldDefinition>();
			var seenInputs = new HashSet<IDebugInput>();
			foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
			{
				if (typeof(IDebugInput).IsAssignableFrom(field.FieldType))
				{
					var input = field.GetValue(instance) as IDebugInput;
					if (input == null || !seenInputs.Add(input))
						throw new InvalidOperationException("Command input fields must contain unique non-null instances.");
					var initial = input.GetValue();
					var check = new DebugValidation();
					input.Validate(initial, check);
					if (check.HasErrors)
						throw new InvalidOperationException("Command input initial value is invalid.");
					var range = field.GetCustomAttributes(typeof(DebugRangeAttribute), false).OfType<DebugRangeAttribute>().FirstOrDefault();
					var slider = field.IsDefined(typeof(DebugSliderAttribute), false);
					var multiline = field.IsDefined(typeof(DebugMultilineAttribute), false);
					var kind = input.Schema == null ? DebugValueKind.Null : input.Schema.ValueKind;
					var numeric =
						kind == DebugValueKind.SignedInteger || kind == DebugValueKind.UnsignedInteger || kind == DebugValueKind.FloatingPoint;
					if (range != null && !numeric)
						throw new InvalidOperationException("DebugRange requires a numeric command input.");
					if (slider && range == null)
						throw new InvalidOperationException("DebugSlider requires a numeric command input with a range.");
					if (multiline && kind != DebugValueKind.String)
						throw new InvalidOperationException("DebugMultiline requires a string command input.");
					fields.Add(
						new DexcCommandFieldDefinition
						{
							Field = field,
							IsInput = true,
							Schema = input.Schema,
							Range = range,
							IsSlider = slider,
							IsMultiline = multiline,
						}
					);
				}
				else if (field.FieldType == typeof(CommandLabel))
					fields.Add(new DexcCommandFieldDefinition { Field = field, IsLabel = true });
				else if (field.FieldType == typeof(CommandHeader))
					fields.Add(new DexcCommandFieldDefinition { Field = field, IsHeader = true });
				else
					throw new InvalidOperationException("Public command fields must be inputs, labels, or headers.");
			}
			var isAsync =
				type.BaseType == typeof(AsyncDebugCommand)
				|| type.BaseType.IsGenericType && type.BaseType.GetGenericTypeDefinition() == typeof(AsyncDebugCommand<>);
			var hasOutput =
				type.BaseType.IsGenericType
				&& (
					type.BaseType.GetGenericTypeDefinition() == typeof(DebugCommand<>)
					|| type.BaseType.GetGenericTypeDefinition() == typeof(AsyncDebugCommand<>)
				);
			var timeout = TimeSpan.Zero;
			if (isAsync)
			{
				var timeoutProperty = type.GetProperty("Timeout", BindingFlags.Instance | BindingFlags.NonPublic);
				timeout = timeoutProperty == null ? TimeSpan.FromSeconds(5) : (TimeSpan)timeoutProperty.GetValue(instance, null);
				if (timeout != Timeout.InfiniteTimeSpan && timeout <= TimeSpan.Zero)
					throw new InvalidOperationException("Async command timeout must be positive or infinite.");
			}
			return new DexcCommandDefinition
			{
				Type = type,
				Attribute = attribute,
				Fields = fields,
				IsAsync = isAsync,
				HasOutput = hasOutput,
				Timeout = timeout,
			};
		}

		private static void RemoveDuplicatePaths(
			List<DexcCommandDefinition> definitions,
			List<DexcDefinitionDiagnostic> diagnostics,
			ref int excluded,
			ref int errors
		)
		{
			var duplicates = definitions
				.GroupBy(definition => definition.Attribute.Path, StringComparer.Ordinal)
				.Where(group => group.Count() > 1)
				.ToList();
			foreach (var duplicate in duplicates)
			{
				foreach (var definition in duplicate)
				{
					definitions.Remove(definition);
					excluded++;
					errors++;
					diagnostics.Add(
						new DexcDefinitionDiagnostic(
							"duplicate-command-path",
							"All commands using a duplicate path were excluded.",
							duplicate.Key
						)
					);
				}
			}
		}

		private static bool IsCommand(Type type)
		{
			return typeof(IDexcCommandRuntimeMarker).IsAssignableFrom(type)
				&& type.BaseType != null
				&& (
					type.BaseType == typeof(DebugCommand)
					|| type.BaseType == typeof(AsyncDebugCommand)
					|| type.BaseType.IsGenericType
						&& (
							type.BaseType.GetGenericTypeDefinition() == typeof(DebugCommand<>)
							|| type.BaseType.GetGenericTypeDefinition() == typeof(AsyncDebugCommand<>)
						)
				);
		}
	}
}
