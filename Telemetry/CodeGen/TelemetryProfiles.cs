using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Mono.Cecil;

namespace Dexc.Telemetry.CodeGen
{
	/// <summary>
	/// One preset: which methods get the measuring that <c>DexcTelemetry.Watch</c> turns on, and how it thins them. Each
	/// list that is not empty must match; with all of them empty the preset chooses nothing.
	/// </summary>
	internal sealed class TelemetryProfile
	{
		internal string Name = string.Empty;

		/// <summary>Assembly names. One that ends with <c>*</c> is a prefix.</summary>
		internal readonly List<string> Assemblies = new List<string>();

		/// <summary>Namespaces, with the ones under them.</summary>
		internal readonly List<string> Namespaces = new List<string>();

		/// <summary>Full names of attributes on the method or on its type.</summary>
		internal readonly List<string> Attributes = new List<string>();

		internal int Every = 1;
		internal int PerFrame = 32;
		internal bool AnyThread;

		bool ChoosesAnything => Assemblies.Count > 0 || Namespaces.Count > 0 || Attributes.Count > 0;

		/// <summary>Whether a method of the assembly could be chosen, so that the assembly is worth reading.</summary>
		internal bool MayChoose(string assembly)
		{
			return ChoosesAnything && (Assemblies.Count == 0 || Assemblies.Any(pattern => Matches(pattern, assembly)));
		}

		internal bool Chooses(string assembly, MethodDefinition method)
		{
			if (!MayChoose(assembly))
			{
				return false;
			}
			var outer = method.DeclaringType;
			while (outer.DeclaringType != null)
			{
				outer = outer.DeclaringType;
			}
			var space = outer.Namespace;
			if (Namespaces.Count > 0 && !Namespaces.Any(name => space == name || space.StartsWith(name + ".", StringComparison.Ordinal)))
			{
				return false;
			}
			return Attributes.Count == 0 || Attributes.Any(name => HasAttribute(method, name));
		}

		static bool HasAttribute(MethodDefinition method, string name)
		{
			if (method.CustomAttributes.Any(attribute => attribute.AttributeType.FullName == name))
			{
				return true;
			}
			for (var type = method.DeclaringType; type != null; type = type.DeclaringType)
			{
				if (type.CustomAttributes.Any(attribute => attribute.AttributeType.FullName == name))
				{
					return true;
				}
			}
			return false;
		}

		static bool Matches(string pattern, string name)
		{
			return pattern.EndsWith("*", StringComparison.Ordinal)
				? name.StartsWith(pattern.Substring(0, pattern.Length - 1), StringComparison.Ordinal)
				: name == pattern;
		}
	}

	/// <summary>
	/// The presets of the project, in <c>ProjectSettings</c>, and of the user, in <c>UserSettings</c>. A preset of the user
	/// replaces the one of the project with the same name, and the <c>active</c> of the user wins.
	/// </summary>
	internal sealed class TelemetryProfiles
	{
		internal const string ProjectFile = "ProjectSettings/DexcTelemetryProfiles.json";
		internal const string UserFile = "UserSettings/DexcTelemetryProfiles.json";

		internal readonly List<TelemetryProfile> Presets = new List<TelemetryProfile>();
		internal string ActiveName;

		/// <summary>The hash of both files, which the processed assemblies keep to tell when they are out of date.</summary>
		internal string Hash;

		/// <summary>The chosen preset, or null when none is chosen or it does not exist.</summary>
		internal TelemetryProfile Active => ActiveName == null ? null : Presets.FirstOrDefault(preset => preset.Name == ActiveName);

		/// <summary>Reads the files under the project folder. A file that does not exist counts as empty.</summary>
		/// <exception cref="FormatException">A file is not valid.</exception>
		internal static TelemetryProfiles Load(string projectFolder)
		{
			return Read(ReadFile(Path.Combine(projectFolder, ProjectFile)), ReadFile(Path.Combine(projectFolder, UserFile)));
		}

		/// <exception cref="FormatException">A text is not valid.</exception>
		internal static TelemetryProfiles Read(string project, string user)
		{
			var profiles = new TelemetryProfiles();
			profiles.Add(project, ProjectFile);
			profiles.Add(user, UserFile);
			using (var sha = SHA256.Create())
			{
				var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes((project ?? string.Empty) + "\0" + (user ?? string.Empty)));
				profiles.Hash = string.Concat(bytes.Select(value => value.ToString("x2")));
			}
			return profiles;
		}

		static string ReadFile(string path)
		{
			return File.Exists(path) ? File.ReadAllText(path) : null;
		}

		void Add(string text, string file)
		{
			if (string.IsNullOrWhiteSpace(text))
			{
				return;
			}
			if (!(MiniJson.Parse(text) is Dictionary<string, object> root))
			{
				throw new FormatException(file + " must hold an object.");
			}
			if (root.TryGetValue("active", out var active) && active is string name && name.Length > 0)
			{
				ActiveName = name;
			}
			if (!root.TryGetValue("presets", out var presets))
			{
				return;
			}
			if (!(presets is List<object> list))
			{
				throw new FormatException(file + ": presets must be a list.");
			}
			foreach (var item in list)
			{
				var preset = Preset(item as Dictionary<string, object>, file);
				Presets.RemoveAll(existing => existing.Name == preset.Name);
				Presets.Add(preset);
			}
		}

		static TelemetryProfile Preset(Dictionary<string, object> values, string file)
		{
			if (values == null || !(values.TryGetValue("name", out var name) && name is string text && text.Length > 0))
			{
				throw new FormatException(file + ": each preset needs a name.");
			}
			var preset = new TelemetryProfile { Name = text };
			Strings(values, "assemblies", preset.Assemblies, file);
			Strings(values, "namespaces", preset.Namespaces, file);
			Strings(values, "attributes", preset.Attributes, file);
			if (values.TryGetValue("every", out var every))
			{
				preset.Every = Positive(every, "every", file);
			}
			if (values.TryGetValue("perFrame", out var perFrame))
			{
				preset.PerFrame = Positive(perFrame, "perFrame", file);
			}
			if (values.TryGetValue("anyThread", out var anyThread))
			{
				preset.AnyThread = anyThread is bool flag ? flag : throw new FormatException(file + ": anyThread must be true or false.");
			}
			return preset;
		}

		static void Strings(Dictionary<string, object> values, string key, List<string> into, string file)
		{
			if (!values.TryGetValue(key, out var value))
			{
				return;
			}
			if (!(value is List<object> list) || list.Any(item => !(item is string)))
			{
				throw new FormatException(file + ": " + key + " must be a list of strings.");
			}
			into.AddRange(list.Cast<string>());
		}

		static int Positive(object value, string key, string file)
		{
			if (!(value is double number) || number < 1 || number != Math.Floor(number) || number > int.MaxValue)
			{
				throw new FormatException(file + ": " + key + " must be a whole number of one or more.");
			}
			return (int)number;
		}
	}
}
