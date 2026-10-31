namespace Dexc
{
	/// <summary>
	/// JSON-RPC 2.0 の予約 code。server 固有の失敗は <see cref="ServerError"/> ひとつにまとめ、区別は
	/// <see cref="DexcRemoteReason"/> の文字列で行う。数値の表を増やさない。
	/// </summary>
	internal static class DexcRemoteErrorCode
	{
		internal const int ParseError = -32700;
		internal const int InvalidRequest = -32600;
		internal const int MethodNotFound = -32601;
		internal const int InvalidParams = -32602;
		internal const int InternalError = -32603;
		internal const int ServerError = -32000;
	}

	/// <summary>
	/// protocol error の理由。既存の診断 code と同じ kebab-case の文字列で揃える。操作が成立したうえでの失敗は
	/// protocol error ではなく result の status で返すため、ここには現れない。
	/// </summary>
	internal static class DexcRemoteReason
	{
		/// <summary>frame が JSON として読めない。</summary>
		internal const string MalformedFrame = "malformed-frame";

		/// <summary>JSON ではあるが JSON-RPC 2.0 の request になっていない。</summary>
		internal const string InvalidRequest = "invalid-request";

		/// <summary>その名前の method が無い。</summary>
		internal const string UnknownMethod = "unknown-method";

		/// <summary>params が method の求める形になっていない。</summary>
		internal const string InvalidParams = "invalid-params";

		/// <summary>この経路では扱えない method または引数。</summary>
		internal const string Unsupported = "unsupported";

		/// <summary>page の大きさが hello.limits を超えている。</summary>
		internal const string PageSizeExceeded = "page-size-exceeded";
	}
}
