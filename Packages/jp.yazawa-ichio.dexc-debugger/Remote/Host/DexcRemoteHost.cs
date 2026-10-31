using System;
using System.Globalization;

namespace Dexc
{
	/// <summary>
	/// Answers a frame with a frame, for a carrier that has no framing of its own.
	///
	/// This is one way to reach <see cref="DexcRemoteOperations"/>, not the way. A carrier that already names commands
	/// and serializes results - the Unity CLI does both - calls the operations directly and never comes through here.
	/// Keeping the two apart is what lets a future socket speak JSON-RPC without the operations knowing.
	/// </summary>
	internal sealed class DexcRemoteHost
	{
		private readonly DexcRemoteOperations m_Operations = new DexcRemoteOperations();

		internal const string ProtocolVersion = DexcRemoteOperations.ProtocolVersion;

		/// <summary>
		/// Nothing here throws at the caller: a carrier that received a frame owes an answer, and a failure it cannot
		/// answer with is a failure it cannot report.
		/// </summary>
		internal string Handle(string frame)
		{
			if (!DexcRemoteMessage.TryReadRequest(frame, out var request, out var errorFrame))
				return errorFrame;
			try
			{
				return Dispatch(request);
			}
			catch (DexcRemoteOperations.RefusedException refused)
			{
				return DexcRemoteMessage.WriteError(
					request.Id,
					refused.Reason == DexcRemoteReason.InvalidParams ? DexcRemoteErrorCode.InvalidParams : DexcRemoteErrorCode.ServerError,
					refused.Message,
					refused.Reason
				);
			}
			catch (Exception exception)
			{
				return DexcRemoteMessage.WriteError(
					request.Id,
					DexcRemoteErrorCode.InternalError,
					exception.Message,
					DexcRemoteReason.Unsupported
				);
			}
		}

		private string Dispatch(DexcRemoteRequest request)
		{
			switch (request.Method)
			{
				case "hello":
					// The encoding is the carrier's property, not the operations'. This carrier writes JSON.
					var hello = m_Operations.Hello();
					hello["encoding"] = "json";
					return Result(request, hello);
				case "session.status":
					return Result(request, m_Operations.SessionStatus());
				case "command.list":
					return Result(
						request,
						m_Operations.CommandList(Text(request, "pathPrefix"), Text(request, "searchText"), Offset(request), Count(request))
					);
				case "command.describe":
					return Result(request, m_Operations.CommandDescribe(Text(request, "path")));
				case "command.execute":
					return Result(request, m_Operations.CommandExecute(Text(request, "path"), Inputs(request)));
				case "setting.list":
					return Result(
						request,
						m_Operations.SettingList(Text(request, "pathPrefix"), Text(request, "searchText"), Offset(request), Count(request))
					);
				case "setting.read":
					return Result(request, m_Operations.SettingRead(Text(request, "path")));
				case "setting.presets":
					return Result(request, m_Operations.SettingPresets(Text(request, "pathPrefix"), Offset(request), Count(request)));
				case "setting.previewPreset":
					return Result(request, m_Operations.SettingPreviewPreset(Text(request, "presetId")));
				case "inspector.registered":
					return Result(
						request,
						m_Operations.InspectorRegistered(
							Text(request, "pathPrefix"),
							Text(request, "searchText"),
							Offset(request),
							Count(request)
						)
					);
				case "inspector.scenes":
					return Result(request, m_Operations.InspectorScenes());
				case "inspector.children":
					return Result(
						request,
						m_Operations.InspectorChildren(
							Text(request, "scene"),
							EntityId(request, "parentEntityId"),
							Offset(request),
							Count(request)
						)
					);
				case "inspector.components":
					return Result(request, m_Operations.InspectorComponents(EntityId(request, "entityId")));
				case "inspector.fields":
					return Result(
						request,
						m_Operations.InspectorFields(
							EntityId(request, "entityId"),
							Text(request, "typeName"),
							Offset(request),
							Count(request)
						)
					);
				case "inspector.read":
					return Result(
						request,
						m_Operations.InspectorRead(EntityId(request, "entityId"), Text(request, "typeName"), Names(request), Count(request))
					);
				case "display.frame":
					return Result(request, m_Operations.DisplayFrame());
				case "diagnostics.list":
					return Result(
						request,
						m_Operations.DiagnosticsList(
							Text(request, "sources"),
							Text(request, "features"),
							Text(request, "severity"),
							Offset(request),
							Count(request)
						)
					);
				case "diagnostics.detail":
					return Result(request, m_Operations.DiagnosticsDetail(Text(request, "diagnosticId")));
				case "boot.read":
					return Result(request, m_Operations.BootRead());
				case "boot.start":
					return Result(request, m_Operations.BootStart(Members(request, "values")));
				default:
					return DexcRemoteMessage.WriteUnknownMethod(request.Id, request.Method);
			}
		}

		private static System.Collections.Generic.IDictionary<string, object> Inputs(DexcRemoteRequest request) =>
			Members(request, "inputs");

		/// <summary>An optional object parameter, as plain objects keyed by name.</summary>
		private static System.Collections.Generic.IDictionary<string, object> Members(DexcRemoteRequest request, string name)
		{
			if (!request.Parameters.TryGetMember(name, out var member) || member.Kind == DexcJsonKind.Null)
				return null;
			if (member.Kind != DexcJsonKind.Object)
			{
				throw new DexcRemoteOperations.RefusedException(DexcRemoteReason.InvalidParams, "\"" + name + "\" must be an object.");
			}
			return (System.Collections.Generic.IDictionary<string, object>)DexcRemoteJsonWriter.ToObject(member);
		}

		/// <summary>
		/// An entity id arrives as a number, or as text when it passed what a double holds exactly on the way out. Both are
		/// read here so a caller can hand back exactly what it was given without knowing which of the two it got.
		/// </summary>
		private static ulong EntityId(DexcRemoteRequest request, string name)
		{
			if (!request.Parameters.TryGetMember(name, out var member) || member.Kind == DexcJsonKind.Null)
				return 0UL;
			if (member.Kind == DexcJsonKind.String)
			{
				member.TryGetString(out var text);
				if (!ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
				{
					throw new DexcRemoteOperations.RefusedException(
						DexcRemoteReason.InvalidParams,
						"\"" + name + "\" must be an entity id."
					);
				}
				return parsed;
			}
			if (!member.TryGetUInt64(out var value))
				throw new DexcRemoteOperations.RefusedException(DexcRemoteReason.InvalidParams, "\"" + name + "\" must be an entity id.");
			return value;
		}

		/// <summary>The field names to read. An array of strings, because a field is named, not numbered.</summary>
		private static System.Collections.Generic.IReadOnlyList<string> Names(DexcRemoteRequest request)
		{
			if (!request.Parameters.TryGetMember("fields", out var member) || member.Kind == DexcJsonKind.Null)
				return null;
			if (member.Kind != DexcJsonKind.Array)
				throw new DexcRemoteOperations.RefusedException(DexcRemoteReason.InvalidParams, "\"fields\" must be an array.");
			var names = new System.Collections.Generic.List<string>();
			foreach (var item in member.Items)
			{
				if (!item.TryGetString(out var text))
					throw new DexcRemoteOperations.RefusedException(DexcRemoteReason.InvalidParams, "\"fields\" must hold strings.");
				names.Add(text);
			}
			return names;
		}

		private static int Offset(DexcRemoteRequest request) => (int)Number(request, "offset", 0);

		private static int Count(DexcRemoteRequest request) => (int)Number(request, "count", 20);

		private static string Text(DexcRemoteRequest request, string name)
		{
			if (!request.Parameters.TryGetMember(name, out var member) || member.Kind == DexcJsonKind.Null)
				return string.Empty;
			if (!member.TryGetString(out var text))
				throw new DexcRemoteOperations.RefusedException(DexcRemoteReason.InvalidParams, "\"" + name + "\" must be a string.");
			return text;
		}

		private static long Number(DexcRemoteRequest request, string name, long fallback)
		{
			if (!request.Parameters.TryGetMember(name, out var member) || member.Kind == DexcJsonKind.Null)
				return fallback;
			if (!member.TryGetInt64(out var value))
			{
				throw new DexcRemoteOperations.RefusedException(DexcRemoteReason.InvalidParams, "\"" + name + "\" must be a whole number.");
			}
			return value;
		}

		private static string Result(DexcRemoteRequest request, object answer) =>
			DexcRemoteMessage.WriteResult(request.Id, DexcRemoteJsonWriter.From(answer));
	}
}
