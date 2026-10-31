using System.Collections.Generic;

namespace Dexc
{
	/// <summary>
	/// One request as it arrived. The id is kept as it was written: JSON-RPC lets it be a string, a number or null, and
	/// the reply has to carry back the same one.
	/// </summary>
	internal sealed class DexcRemoteRequest
	{
		internal DexcRemoteRequest(string method, DexcJsonValue parameters, DexcJsonValue id)
		{
			Method = method;
			Parameters = parameters ?? DexcJsonValue.Null;
			Id = id ?? DexcJsonValue.Null;
		}

		internal string Method { get; }
		internal DexcJsonValue Parameters { get; }
		internal DexcJsonValue Id { get; }
	}

	/// <summary>
	/// Reads and writes the JSON-RPC 2.0 envelope. It knows nothing about the methods, so a new method needs no change
	/// here, and it can be exercised without a transport.
	/// </summary>
	internal static class DexcRemoteMessage
	{
		private const string Version = "2.0";

		/// <summary>
		/// Reads one frame. A frame that cannot be read is not an exception: the caller still has to answer, and the
		/// answer is the error this produces.
		/// </summary>
		internal static bool TryReadRequest(string frame, out DexcRemoteRequest request, out string errorFrame)
		{
			request = null;
			errorFrame = null;
			if (!DexcJson.TryParse(frame, out var document, out var parseError))
			{
				errorFrame = WriteError(DexcJsonValue.Null, DexcRemoteErrorCode.ParseError, parseError, DexcRemoteReason.MalformedFrame);
				return false;
			}
			if (document.Kind != DexcJsonKind.Object)
			{
				errorFrame = InvalidRequest(DexcJsonValue.Null, "A request must be a JSON object.");
				return false;
			}
			var id = document.TryGetMember("id", out var idValue) ? idValue : DexcJsonValue.Null;
			var hasId = document.TryGetMember("id", out _);
			if (!document.TryGetMember("jsonrpc", out var version) || !version.TryGetString(out var versionText) || versionText != Version)
			{
				errorFrame = InvalidRequest(id, "A request must carry \"jsonrpc\":\"2.0\".");
				return false;
			}
			if (
				!document.TryGetMember("method", out var method)
				|| !method.TryGetString(out var methodName)
				|| string.IsNullOrEmpty(methodName)
			)
			{
				errorFrame = InvalidRequest(id, "A request must name a method.");
				return false;
			}
			if (!hasId)
			{
				errorFrame = InvalidRequest(DexcJsonValue.Null, "A request must carry an id. Notifications are not answered.");
				return false;
			}
			if (id.Kind != DexcJsonKind.String && id.Kind != DexcJsonKind.Number)
			{
				errorFrame = InvalidRequest(DexcJsonValue.Null, "A request id must be a string or a number.");
				return false;
			}
			var parameters = document.TryGetMember("params", out var value) ? value : DexcJsonValue.Null;
			if (parameters.Kind != DexcJsonKind.Null && parameters.Kind != DexcJsonKind.Object)
			{
				errorFrame = WriteError(
					id,
					DexcRemoteErrorCode.InvalidParams,
					"params must be a JSON object.",
					DexcRemoteReason.InvalidParams
				);
				return false;
			}
			request = new DexcRemoteRequest(methodName, parameters, id);
			return true;
		}

		internal static string WriteResult(DexcJsonValue id, DexcJsonValue result)
		{
			return DexcJson.Write(
				DexcJsonValue.FromObject(
					new[]
					{
						new DexcJsonMember("jsonrpc", DexcJsonValue.From(Version)),
						new DexcJsonMember("id", id ?? DexcJsonValue.Null),
						new DexcJsonMember("result", result ?? DexcJsonValue.Null),
					}
				)
			);
		}

		internal static string WriteError(DexcJsonValue id, int code, string message, string reason)
		{
			var members = new List<DexcJsonMember>
			{
				new DexcJsonMember("code", DexcJsonValue.From(code)),
				new DexcJsonMember("message", DexcJsonValue.From(message ?? string.Empty)),
			};
			if (!string.IsNullOrEmpty(reason))
			{
				members.Add(
					new DexcJsonMember("data", DexcJsonValue.FromObject(new[] { new DexcJsonMember("reason", DexcJsonValue.From(reason)) }))
				);
			}
			return DexcJson.Write(
				DexcJsonValue.FromObject(
					new[]
					{
						new DexcJsonMember("jsonrpc", DexcJsonValue.From(Version)),
						new DexcJsonMember("id", id ?? DexcJsonValue.Null),
						new DexcJsonMember("error", DexcJsonValue.FromObject(members)),
					}
				)
			);
		}

		internal static string WriteUnknownMethod(DexcJsonValue id, string method)
		{
			return WriteError(
				id,
				DexcRemoteErrorCode.MethodNotFound,
				"No method is named \"" + method + "\".",
				DexcRemoteReason.UnknownMethod
			);
		}

		private static string InvalidRequest(DexcJsonValue id, string message)
		{
			return WriteError(id, DexcRemoteErrorCode.InvalidRequest, message, DexcRemoteReason.InvalidRequest);
		}
	}
}
