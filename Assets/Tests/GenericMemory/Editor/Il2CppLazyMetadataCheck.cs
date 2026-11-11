using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;

namespace GenericMemory.Editor
{
	/// <summary>
	/// Checks the changes of <see cref="Il2CppLazyMetadata"/> against this Editor's libil2cpp without building anything:
	/// they must apply to a fresh copy, and in their switched form give, file for file, what the bench built and measured.
	/// </summary>
	public static class Il2CppLazyMetadataCheck
	{
		static void Apply(string root, bool switched)
		{
			if (switched)
			{
				GenericMemoryLazyPatch.Apply(root, true);
			}
			else
			{
				Il2CppLazy.Il2CppLazyMetadata.Apply(root);
			}
		}

		static readonly string[] s_files =
		{
			"vm/Class.cpp",
			"metadata/ArrayMetadata.cpp",
			"vm/ClassInlines.cpp",
			"vm/GenericClass.cpp",
			"metadata/CustomAttributeDataReader.cpp",
			"metadata/GenericMetadata.h",
			"metadata/GenericMetadata.cpp",
			"codegen/il2cpp-codegen.h",
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
				Apply(root, switched);
				// Applying again must change nothing.
				var once = s_files.Select(file => File.ReadAllText(Path.Combine(root, "libil2cpp", file))).ToArray();
				Apply(root, switched);
				var same = s_files.Select((file, at) => File.ReadAllText(Path.Combine(root, "libil2cpp", file)) == once[at]).All(equal => equal);
				log.Append(switched ? "switched" : "always").Append(": applied, again unchanged=").Append(same);
				if (switched && measured != null)
				{
					foreach (var file in s_files)
					{
						var mine = File.ReadAllText(Path.Combine(root, "libil2cpp", file)).Replace("\r\n", "\n");
						var theirs = File.ReadAllText(Path.Combine(measured, "libil2cpp", file)).Replace("\r\n", "\n");
						log.Append(", ").Append(Path.GetFileName(file)).Append(mine == theirs ? "=same" : "=DIFFERENT");
					}
				}
				log.Append('\n');
			}
			return log.ToString();
		}
	}
}
