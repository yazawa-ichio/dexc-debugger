using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Dexc.Guard;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Dexc.Autopilot.Compiler
{
	internal sealed class BuildResult
	{
		public byte[] Assembly;
		public List<string> Errors = new List<string>();
		public List<string> Warnings = new List<string>();
		public long Milliseconds;
		public bool Ok => Assembly != null;
	}

	/// <summary>
	/// Keeps what makes a compile slow between requests: the references, what Roslyn has bound from them, and the parsed
	/// files of a bot. Not thread-safe; the server takes requests one at a time.
	/// </summary>
	internal sealed class BuildEngine : IDisposable
	{
		private const string SnippetTemplate =
			"using System.Collections.Generic;\n"
			+ "using System.Threading.Tasks;\n"
			+ "{usings}\n"
			+ "{types}\n"
			+ "\n"
			+ "public static class DexcEvalSnippet\n"
			+ "{\n"
			+ "	public static object Eval()\n"
			+ "	{\n"
			+ "		{body}\n"
			+ "	}\n"
			+ "}\n";

		private static readonly CSharpParseOptions s_ParseOptions = new CSharpParseOptions(LanguageVersion.CSharp9);
		private static readonly ImmutableArray<DiagnosticAnalyzer> s_Analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new PipelineVmAnalyzer());

		// Debug is not a preference: Release makes async state machines structs, which lose their state on ILRuntime.
		private static readonly CSharpCompilationOptions s_Options = new CSharpCompilationOptions(
			OutputKind.DynamicallyLinkedLibrary,
			optimizationLevel: OptimizationLevel.Debug,
			specificDiagnosticOptions: new Dictionary<string, ReportDiagnostic>
			{
				["CS0162"] = ReportDiagnostic.Suppress,
				["CS1701"] = ReportDiagnostic.Suppress,
				["CS1702"] = ReportDiagnostic.Suppress,
			}
		);

		private readonly Dictionary<string, BotProject> m_Projects = new Dictionary<string, BotProject>(StringComparer.OrdinalIgnoreCase);
		private readonly string m_ResponseFile;
		private readonly string m_ReferenceDirectory;
		private DateTime m_ReferenceStamp;
		private List<MetadataReference> m_References;
		private CSharpCompilation m_SnippetBase;
		private EnumPatcher m_Patcher;

		public BuildEngine(string responseFile, string referenceDirectory)
		{
			m_ResponseFile = responseFile;
			m_ReferenceDirectory = referenceDirectory;
		}

		public int ReferenceCount => m_References?.Count ?? 0;

		public void Dispose() => m_Patcher?.Dispose();

		public BuildResult CompileSnippet(string snippet, string vm, IEnumerable<string> usings)
		{
			var clock = Stopwatch.StartNew();
			Refresh();
			var tree = CSharpSyntaxTree.ParseText(Wrap(snippet, usings), s_ParseOptions, "Eval.cs", Encoding.UTF8);
			var result = Finish(m_SnippetBase.AddSyntaxTrees(tree), vm, false);
			result.Milliseconds = clock.ElapsedMilliseconds;
			return result;
		}

		/// <param name="directories">The bot's folders. A file replaces the one at the same path in the folders before it.</param>
		public BuildResult CompileProject(IReadOnlyList<string> directories, string assemblyName, string vm)
		{
			var clock = Stopwatch.StartNew();
			Refresh();
			var full = directories.Select(Path.GetFullPath).ToList();
			var key = string.Join(";", full);
			if (!m_Projects.TryGetValue(key, out var project))
				m_Projects[key] = project = new BotProject(full, assemblyName, s_ParseOptions, s_Options);
			var result = Finish(project.Update(m_References), vm, true);
			result.Milliseconds = clock.ElapsedMilliseconds;
			return result;
		}

		/// <summary>A Player build rewrites the response file last, so its stamp says the whole set is new.</summary>
		private void Refresh()
		{
			var stamp = File.GetLastWriteTimeUtc(m_ResponseFile);
			if (m_References != null && stamp == m_ReferenceStamp)
				return;
			var references = new List<MetadataReference>();
			foreach (var line in File.ReadAllLines(m_ResponseFile))
			{
				if (line.StartsWith("-r:", StringComparison.Ordinal))
					references.Add(MetadataReference.CreateFromFile(line.Substring(3).Trim('"')));
			}

			m_References = references;
			m_ReferenceStamp = stamp;
			m_SnippetBase = CSharpCompilation.Create("Eval", null, m_References, s_Options);
			m_Patcher?.Dispose();
			m_Patcher = new EnumPatcher(m_ReferenceDirectory);
		}

		private static string Wrap(string snippet, IEnumerable<string> usings)
		{
			var marker = snippet.LastIndexOf("\n@@\n", StringComparison.Ordinal);
			var types = marker < 0 ? string.Empty : snippet.Substring(0, marker);
			var code = marker < 0 ? snippet : snippet.Substring(marker + 4);
			var body = code.Contains(';') ? code + "\nreturn null;" : "return (object)(" + code + ");";
			var header = string.Concat(usings.Select(name => "using " + name + ";\n"));
			return SnippetTemplate.Replace("{usings}", header).Replace("{types}", types).Replace("{body}", body);
		}

		/// <summary>ILRuntime only warns about a blocking wait. The Pipeline interpreter refuses all it cannot run.</summary>
		private BuildResult Finish(CSharpCompilation compilation, string vm, bool withFile)
		{
			var result = new BuildResult();
			using var image = new MemoryStream();
			var emitted = compilation.Emit(image);
			foreach (var diagnostic in emitted.Diagnostics)
			{
				if (diagnostic.Severity == DiagnosticSeverity.Error)
					result.Errors.Add(Format(diagnostic, withFile));
			}

			if (!emitted.Success)
				return result;

			var pipeline = vm == "pipeline";
			foreach (var diagnostic in compilation.WithAnalyzers(s_Analyzers).GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult())
			{
				if (pipeline)
					result.Errors.Add(Format(diagnostic, withFile));
				else if (diagnostic.Id == "DEXC013")
					result.Warnings.Add(Format(diagnostic, withFile).Replace(": error ", ": warning "));
			}

			if (result.Errors.Count > 0)
				return result;
			result.Assembly = pipeline ? m_Patcher.Patch(image.ToArray()) : image.ToArray();
			return result;
		}

		private static string Format(Diagnostic diagnostic, bool withFile)
		{
			var span = diagnostic.Location.GetLineSpan();
			var file = withFile ? Path.GetFileName(span.Path) : string.Empty;
			var severity = diagnostic.Severity.ToString().ToLowerInvariant();
			return string.Format(
				CultureInfo.InvariantCulture,
				"{0}({1},{2}): {3} {4}: {5}",
				file,
				span.StartLinePosition.Line + 1,
				span.StartLinePosition.Character + 1,
				severity,
				diagnostic.Id,
				diagnostic.GetMessage(CultureInfo.InvariantCulture)
			);
		}
	}

	/// <summary>The files of a bot as Roslyn last saw them, so a save reparses only what changed.</summary>
	internal sealed class BotProject
	{
		private readonly Dictionary<string, (long Stamp, SyntaxTree Tree)> m_Files = new Dictionary<string, (long, SyntaxTree)>(StringComparer.OrdinalIgnoreCase);
		private readonly IReadOnlyList<string> m_Directories;
		private readonly CSharpParseOptions m_ParseOptions;
		private CSharpCompilation m_Compilation;
		private IReadOnlyList<MetadataReference> m_References;

		public BotProject(IReadOnlyList<string> directories, string assemblyName, CSharpParseOptions parseOptions, CSharpCompilationOptions options)
		{
			m_Directories = directories;
			m_ParseOptions = parseOptions;
			m_Compilation = CSharpCompilation.Create(assemblyName, null, null, options);
		}

		public CSharpCompilation Update(IReadOnlyList<MetadataReference> references)
		{
			if (!ReferenceEquals(references, m_References))
			{
				m_References = references;
				m_Compilation = m_Compilation.WithReferences(references);
			}

			var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (var path in Sources())
			{
				seen.Add(path);
				var stamp = File.GetLastWriteTimeUtc(path).Ticks;
				if (m_Files.TryGetValue(path, out var known) && known.Stamp == stamp)
					continue;
				var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(path), m_ParseOptions, path, Encoding.UTF8);
				m_Compilation = known.Tree != null ? m_Compilation.ReplaceSyntaxTree(known.Tree, tree) : m_Compilation.AddSyntaxTrees(tree);
				m_Files[path] = (stamp, tree);
			}

			foreach (var gone in m_Files.Keys.Where(k => !seen.Contains(k)).ToList())
			{
				m_Compilation = m_Compilation.RemoveSyntaxTrees(m_Files[gone].Tree);
				m_Files.Remove(gone);
			}

			return m_Compilation;
		}

		private IEnumerable<string> Sources()
		{
			var separator = Path.DirectorySeparatorChar;
			var bin = separator + "bin" + separator;
			var obj = separator + "obj" + separator;
			var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (var directory in m_Directories.Where(Directory.Exists))
			{
				foreach (var path in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
				{
					var relative = path.Substring(directory.Length);
					if (!relative.Contains(bin) && !relative.Contains(obj))
						files[relative] = path;
				}
			}

			return files.Values;
		}
	}
}
