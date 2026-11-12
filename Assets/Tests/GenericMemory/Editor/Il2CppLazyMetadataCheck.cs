using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;

namespace GenericMemory.Editor
{
	/// <summary>
	/// Checks the changes of <see cref="Il2CppLazy.Il2CppLazyMetadata"/> against this Editor's libil2cpp without building
	/// anything: they must apply to a fresh copy in both forms, and applying them again must change nothing. Given the
	/// il2cpp folder the bench was built from, it also says whether that folder holds what the switched form gives.
	/// </summary>
	public static class Il2CppLazyMetadataCheck
	{
		static readonly string[] s_files =
		{
			"vm/Class.cpp",
			"vm/Object.cpp",
			"vm/RCW.cpp",
			"metadata/ArrayMetadata.cpp",
			"vm/ClassInlines.cpp",
			"vm/GenericClass.cpp",
			"metadata/CustomAttributeDataReader.cpp",
			"metadata/GenericMetadata.h",
			"metadata/GenericMetadata.cpp",
			"codegen/il2cpp-codegen.h",
		};

		// What the changes add to libil2cpp.
		static readonly string[] s_added =
		{
			"vm/Il2CppLazyMetadata.h",
			"vm/Il2CppLazyInterfaces.h",
		};

		/// <summary><paramref name="measured"/> is the il2cpp folder the bench was built from, or null to skip the comparison.</summary>
		public static string Run(string measured)
		{
			var source = Path.Combine(EditorApplication.applicationContentsPath, "il2cpp", "libil2cpp");
			var log = new StringBuilder();
			foreach (var switched in new[] { true, false })
			{
				var root = Path.GetFullPath("Library/Il2CppLazyMetadataCheck/" + (switched ? "switched" : "always"));
				foreach (var file in s_files)
				{
					var target = Path.Combine(root, "libil2cpp", file);
					Directory.CreateDirectory(Path.GetDirectoryName(target));
					File.Copy(Path.Combine(source, file), target, true);
				}
				var all = s_files.Concat(s_added).ToArray();
				Il2CppLazy.Il2CppLazyMetadata.Apply(root, switched);
				// Applying again must change nothing.
				var once = all.Select(file => File.ReadAllText(Path.Combine(root, "libil2cpp", file))).ToArray();
				Il2CppLazy.Il2CppLazyMetadata.Apply(root, switched);
				var same = all.Select((file, at) => File.ReadAllText(Path.Combine(root, "libil2cpp", file)) == once[at]).All(equal => equal);
				log.Append(switched ? "switched" : "always").Append(": applied, again unchanged=").Append(same);
				if (switched && measured != null)
				{
					foreach (var file in all)
					{
						var mine = File.ReadAllText(Path.Combine(root, "libil2cpp", file)).Replace("\r\n", "\n");
						// The bench's copy also notes each class and method it makes (apply-counters.py); that is not part of the changes.
						var theirs = System.Text.RegularExpressions.Regex.Replace(
							File.ReadAllText(Path.Combine(measured, "libil2cpp", file)).Replace("\r\n", "\n"), @" generic_memory::Record\([^;]*\);", "");
						log.Append(", ").Append(Path.GetFileName(file)).Append(mine == theirs ? "=same" : "=DIFFERENT");
					}
				}
				log.Append('\n');
			}
			return log.ToString();
		}
	}
}
