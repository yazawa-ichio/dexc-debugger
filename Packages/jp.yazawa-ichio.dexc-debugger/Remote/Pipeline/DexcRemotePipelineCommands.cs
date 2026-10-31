#if DEXC_HAS_PIPELINE
using System.Collections.Generic;
using System.Globalization;
using Unity.Pipeline.Commands;

namespace Dexc
{
	/// <summary>
	/// Reaches Dexc through the Unity CLI, when com.unity.pipeline is in the project.
	///
	/// The package already carries what a remote path needs - an HTTP server in the Editor and in a development Player,
	/// a command registry, main-thread dispatch, a token, and a serializer - so none of that is written here. What is
	/// written here is only the naming: each operation gets a command, and the answer is handed over as plain objects
	/// for the server to serialize.
	///
	/// Nothing else depends on this file. Without the package the define is absent, the file compiles to nothing, and
	/// Dexc is exactly what it was.
	/// </summary>
	internal static class DexcRemotePipelineCommands
	{
		// The carrier is one per process, so the state a connection accumulates hangs off it here.
		private static readonly DexcRemoteOperations s_Operations = new DexcRemoteOperations();

		[CliCommand(
			"dexc_status",
			"Whether Dexc is compiled in, whether a session is running, and which run it is.",
			Tags = new[] { "dexc" }
		)]
		private static IDictionary<string, object> Status() => s_Operations.Hello();

		[CliCommand("dexc_initialize", "Starts a Dexc session if the game has not started one.", Tags = new[] { "dexc" })]
		private static IDictionary<string, object> Initialize() => s_Operations.Initialize();

		[CliCommand("dexc_command_list", "Lists the debug commands the game declares.", Tags = new[] { "dexc" })]
		private static IDictionary<string, object> CommandList(
			[CliArg("path_prefix", "Only commands under this path.")] string pathPrefix = null,
			[CliArg("search", "Only commands matching this text.")] string search = null,
			[CliArg("offset", "How many to skip.")] int offset = 0,
			[CliArg("count", "How many to return.")] int count = 20
		) => s_Operations.CommandList(pathPrefix, search, offset, count);

		[CliCommand("dexc_command_describe", "Describes one debug command and the inputs it takes.", Tags = new[] { "dexc" })]
		private static IDictionary<string, object> CommandDescribe(
			[CliArg("path", "The command's path, as the game declared it.", Required = true)] string path
		) => s_Operations.CommandDescribe(path);

		[CliCommand("dexc_command_execute", "Runs one debug command and returns what it produced.", Tags = new[] { "dexc" })]
		private static IDictionary<string, object> CommandExecute(
			[CliArg("path", "The command's path, as the game declared it.", Required = true)] string path,
			[CliArg("inputs", "The command's inputs as a JSON object, keyed by input name.")] string inputs = null
		) => s_Operations.CommandExecute(path, ReadObject("inputs", inputs));

		/// <summary>
		/// An object argument arrives as text because a CLI argument is text. Reading it here keeps the operations
		/// speaking plain objects, the same as every other carrier hands them.
		/// </summary>
		private static IDictionary<string, object> ReadObject(string name, string text)
		{
			if (string.IsNullOrWhiteSpace(text))
				return null;
			if (!DexcJson.TryParse(text, out var json, out var error))
				throw new DexcRemoteOperations.RefusedException(DexcRemoteReason.InvalidParams, error);
			if (json.Kind != DexcJsonKind.Object)
				throw new DexcRemoteOperations.RefusedException(DexcRemoteReason.InvalidParams, "\"" + name + "\" must be a JSON object.");
			return (IDictionary<string, object>)DexcRemoteJsonWriter.ToObject(json);
		}

		[CliCommand("dexc_setting_list", "Lists the debug settings the game declares.", Tags = new[] { "dexc" })]
		private static IDictionary<string, object> SettingList(
			[CliArg("path_prefix", "Only settings under this path.")] string pathPrefix = null,
			[CliArg("search", "Only settings matching this text.")] string search = null,
			[CliArg("offset", "How many to skip.")] int offset = 0,
			[CliArg("count", "How many to return.")] int count = 20
		) => s_Operations.SettingList(pathPrefix, search, offset, count);

		[CliCommand("dexc_setting_read", "Reads one debug setting's current value.", Tags = new[] { "dexc" })]
		private static IDictionary<string, object> SettingRead(
			[CliArg("path", "The setting's path, as the game declared it.", Required = true)] string path
		) => s_Operations.SettingRead(path);

		[CliCommand("dexc_setting_presets", "Lists the presets of the debug settings, by their declared IDs.", Tags = new[] { "dexc" })]
		private static IDictionary<string, object> SettingPresets(
			[CliArg("path_prefix", "Only presets of sets that have a setting under this path.")] string pathPrefix = null,
			[CliArg("offset", "How many to skip.")] int offset = 0,
			[CliArg("count", "How many to return.")] int count = 20
		) => s_Operations.SettingPresets(pathPrefix, offset, count);

		[CliCommand(
			"dexc_setting_preview_preset",
			"Shows what applying one preset would change, without changing anything.",
			Tags = new[] { "dexc" }
		)]
		private static IDictionary<string, object> SettingPreviewPreset(
			[CliArg("preset_id", "The preset's declared ID, as dexc_setting_presets lists it.", Required = true)] string presetId
		) => s_Operations.SettingPreviewPreset(presetId);

		[CliCommand("dexc_inspector_registered", "Lists the objects the game registered with the Inspector.", Tags = new[] { "dexc" })]
		private static IDictionary<string, object> InspectorRegistered(
			[CliArg("path_prefix", "Only entries under this path.")] string pathPrefix = null,
			[CliArg("search", "Only entries matching this text.")] string search = null,
			[CliArg("offset", "How many to skip.")] int offset = 0,
			[CliArg("count", "How many to return.")] int count = 20
		) => s_Operations.InspectorRegistered(pathPrefix, search, offset, count);

		[CliCommand("dexc_inspector_scenes", "Lists the loaded scenes.", Tags = new[] { "dexc" })]
		private static IDictionary<string, object> InspectorScenes() => s_Operations.InspectorScenes();

		[CliCommand("dexc_inspector_children", "Lists one level of a scene's hierarchy.", Tags = new[] { "dexc" })]
		private static IDictionary<string, object> InspectorChildren(
			[CliArg("scene", "The scene's path, or its name when it has none.", Required = true)] string scene,
			[CliArg("parent_entity_id", "The parent's entity id. Leave it out for the scene's roots.")] string parentEntityId = null,
			[CliArg("offset", "How many to skip.")] int offset = 0,
			[CliArg("count", "How many to return.")] int count = 20
		) => s_Operations.InspectorChildren(scene, ReadEntityId(parentEntityId, false), offset, count);

		[CliCommand("dexc_inspector_components", "Lists the components on one object.", Tags = new[] { "dexc" })]
		private static IDictionary<string, object> InspectorComponents(
			[CliArg("entity_id", "The object's entity id, as a listing gave it.", Required = true)] string entityId
		) => s_Operations.InspectorComponents(ReadEntityId(entityId, true));

		/// <summary>
		/// The id arrives as text because a CLI argument is text, and because it can pass what a double holds exactly - the
		/// same reason a listing may hand it out as a string.
		/// </summary>
		private static ulong ReadEntityId(string entityId, bool required)
		{
			if (string.IsNullOrWhiteSpace(entityId))
			{
				if (!required)
					return 0UL;
				throw new DexcRemoteOperations.RefusedException(DexcRemoteReason.InvalidParams, "This operation needs an entity id.");
			}
			if (!ulong.TryParse(entityId.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value))
				throw new DexcRemoteOperations.RefusedException(
					DexcRemoteReason.InvalidParams,
					"\"" + entityId + "\" is not an entity id."
				);
			return value;
		}

		[CliCommand("dexc_inspector_fields", "Lists the fields one component declares.", Tags = new[] { "dexc" })]
		private static IDictionary<string, object> InspectorFields(
			[CliArg("entity_id", "The object's entity id, as a listing gave it.", Required = true)] string entityId,
			[CliArg("type_name", "The component's type name.", Required = true)] string typeName,
			[CliArg("offset", "How many to skip.")] int offset = 0,
			[CliArg("count", "How many to return.")] int count = 20
		) => s_Operations.InspectorFields(ReadEntityId(entityId, true), typeName, offset, count);

		[CliCommand("dexc_inspector_read", "Reads named fields on one component.", Tags = new[] { "dexc" })]
		private static IDictionary<string, object> InspectorRead(
			[CliArg("entity_id", "The object's entity id, as a listing gave it.", Required = true)] string entityId,
			[CliArg("type_name", "The component's type name.", Required = true)] string typeName,
			[CliArg("fields", "The field names to read, separated by commas.", Required = true)] string fields,
			[CliArg("count", "How many field descriptors to look at per page while resolving the names.")] int count = 20
		) => s_Operations.InspectorRead(ReadEntityId(entityId, true), typeName, ReadNames(fields), count);

		/// <summary>The names arrive as one argument because a CLI argument is text, and a field name holds no comma.</summary>
		private static IReadOnlyList<string> ReadNames(string fields)
		{
			if (string.IsNullOrWhiteSpace(fields))
				return null;
			var names = new List<string>();
			foreach (var name in fields.Split(','))
			{
				var trimmed = name.Trim();
				if (trimmed.Length != 0)
					names.Add(trimmed);
			}
			return names;
		}

		[CliCommand("dexc_display_frame", "Returns the last frame the game finished drawing debug visuals for.", Tags = new[] { "dexc" })]
		private static IDictionary<string, object> DisplayFrame() => s_Operations.DisplayFrame();

		[CliCommand("dexc_diagnostics_list", "Lists what Dexc recorded going wrong.", Tags = new[] { "dexc" })]
		private static IDictionary<string, object> DiagnosticsList(
			[CliArg("sources", "Only these sources: Collection, Operation, or All.")] string sources = null,
			[CliArg("features", "Only these features: Command, Setting, Inspector, Display, InputEditor, or All.")] string features = null,
			[CliArg("severity", "Only this severity: Warning or Error.")] string severity = null,
			[CliArg("offset", "How many to skip.")] int offset = 0,
			[CliArg("count", "How many to return.")] int count = 20
		) => s_Operations.DiagnosticsList(sources, features, severity, offset, count);

		[CliCommand("dexc_diagnostics_detail", "Returns the exception behind one diagnostic.", Tags = new[] { "dexc" })]
		private static IDictionary<string, object> DiagnosticsDetail(
			[CliArg("diagnostic_id", "The id a listing gave for this diagnostic.", Required = true)] string diagnosticId
		) => s_Operations.DiagnosticsDetail(diagnosticId);

		[CliCommand("dexc_boot_read", "Reads the open boot menu: its groups, and each item's value and what it accepts.", Tags = new[] { "dexc" })]
		private static IDictionary<string, object> BootRead() => s_Operations.BootRead();

		[CliCommand("dexc_boot_start", "Sets boot menu items and starts the game, as pressing Start would.", Tags = new[] { "dexc" })]
		private static IDictionary<string, object> BootStart(
			[CliArg("values", "Items to set first, as a JSON object keyed \"group/item\" the way dexc_boot_read lists them.")]
				string values = null
		) => s_Operations.BootStart(ReadObject("values", values));
	}
}
#endif
