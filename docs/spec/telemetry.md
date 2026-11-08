# Dexc Telemetry 仕様

ゲームの Player の中で Metrics、Logs、Traces を記録し、OTLP の protobuf としてファイルへ書くパッケージの仕様である。

## 目的と範囲

- データの形式は OpenTelemetry の OTLP に合わせる。解析の道具と collector をそのまま使うためである
- 公式の SDK は使わない。IL2CPP では記録ごとに確保が発生し、毎フレーム計測すると GC の発生源になるためである
- 検証用のビルドだけでなく、リリース用のビルドでも動かす

## パッケージ

| 項目 | 内容 |
|---|---|
| フォルダー | リポジトリ直下の`Telemetry/` |
| パッケージ名 | `jp.yazawa-ichio.dexc-telemetry` |
| バージョン | Dexc本体と揃える |
| アセンブリとnamespace | `Dexc.Telemetry` |
| 必須依存 | なし |

- Dexcを参照しない。連携が必要なときは、Dexc側が任意の連携としてこのパッケージを参照する
- asmdefに`!DISABLE_DEXC_DEBUGGER`を付けない。Dexcを外したビルドにも残すためである
- `DEXC_DEBUGGER`に連動しない
- コーディング規約は[DexcDebugger コーディング規約](coding-conventions.md)に従う
- mutable staticは`TelemetryRoot.s_Current`だけとし、実行中の状態はすべてここから辿る
- main thread専用とする。例外はログの記録で、`TelemetryLogger`とアダプターはどのスレッドからも呼べる。Unityがログを出したスレッドでコールバックを呼ぶためである。記録はロック付きのキューへ入れるだけにする

## レベル

Logsと、MetricsおよびTracesは、別々のレベルを持つ。ログの`Trace`はTracesの機能と紛らわしいので、この名前を使わない。

`TelemetrySeverity`はLogsの重大度である。値はOpenTelemetryのSeverityNumberとする。

| 名前 | SeverityNumber |
|---|---|
| Verbose | 1（OpenTelemetryのTRACE） |
| Debug | 5 |
| Info | 9 |
| Warning | 13 |
| Error | 17 |
| Fatal | 21 |

`TelemetryDetail`はMetricsとTracesの詳細度である。OpenTelemetryに対応するものは無く、このパッケージの定義である。

| 名前 | 内容の例 |
|---|---|
| Basic | フレーム時間のヒストグラム、メモリ、シーン遷移 |
| Standard | システムごとの集計、ステージの区間 |
| Detailed | 毎フレームの値、メソッド単位のspan |

重大度はcompile symbolで消せる。1つ定義すると、それ以上の重大度の呼び出しが残る。どれも無ければ、`logger.Info`などの呼び出しはすべて消える。

| シンボル | 残る呼び出し |
|---|---|
| `DEXC_TELEMETRY_LOG_VERBOSE` | すべて |
| `DEXC_TELEMETRY_LOG_DEBUG` | Debug以上 |
| `DEXC_TELEMETRY_LOG_INFO` | Info以上 |
| `DEXC_TELEMETRY_LOG_WARNING` | Warning以上 |
| `DEXC_TELEMETRY_LOG_ERROR` | Error以上 |
| `DEXC_TELEMETRY_LOG_FATAL` | Fatal |

シンボルとは別に、実行時のレベルを持つ。シンボルが残した呼び出しだけに効くので、`DEXC_TELEMETRY_LOG_VERBOSE`を定義したビルドをInfoで動かし、後からVerboseを集められる。詳しくは[Logs](#logs)に書く。

詳細度はcompile symbolで消せる。1つ定義すると、そのレベルより粗い呼び出しも残る。どれも無ければ、記録の呼び出しはすべて消える。引数も評価されない。

| シンボル | 残る呼び出し |
|---|---|
| `DEXC_TELEMETRY_DETAIL_BASIC` | Basic |
| `DEXC_TELEMETRY_DETAIL_STANDARD` | Basic、Standard |
| `DEXC_TELEMETRY_DETAIL_DETAILED` | Basic、Standard、Detailed |

`ConditionalAttribute`は呼び出し側のcompileで判定されるので、シンボルは呼び出すコードのassemblyに定義する。パッケージはシンボルを自動で定義しない。`TelemetryOptions.Detail`は実行時の上限で、シンボルが残した呼び出しを黙らせることだけができる。

## Metrics

入口は`DexcTelemetry`である。namespaceと同じ名前の型を避けるため、`Telemetry`という型名は使わない。

| 計器 | 取得 | 記録 | OTLP |
|---|---|---|---|
| `TelemetryCounter` | `DexcTelemetry.Counter(name, unit)` | `AddBasic`、`AddStandard`、`AddDetailed` | 単調増加のSum（Delta） |
| `TelemetryGauge` | `DexcTelemetry.Gauge(name, unit)` | `SetBasic`、`SetStandard`、`SetDetailed` | Gauge |
| `TelemetryHistogram` | `DexcTelemetry.Histogram(name, unit, bounds)` | `RecordBasic`、`RecordStandard`、`RecordDetailed` | Histogram（Delta） |

- 詳細度は計器ではなく呼び出しごとに決まる。`ConditionalAttribute`はメソッドに固定されるためである
- 名前はOpenTelemetryの規則に従う。英字で始まり、英数字と`_ . - /`だけで、255文字までとする。単位はASCIIで63文字までとする
- 同じ名前で同じ種類の計器は、同じインスタンスを返す。種類、単位、バケットの境界が違えば`ArgumentException`とする
- 計器はrootと一緒に捨てられる
- 属性は持たない。分けたいときは別の名前の計器にする。値の位置を固定するためである
- Counterは1未満の値を無視する。HistogramはNaNを無視する。Histogramの境界の既定値は公式のSDKと同じである
- バケットは上限を含む

計器の登録時に、値を0としたメッセージのバイト列を作る。値はすべて固定長なので、書き出しはバイト列のコピーと値の書き込みだけで済む。記録と書き出しは確保しない。

## Unityの計測元

`TelemetryOptions.CollectUnityMetrics`（既定は有効）で次を集める。計測元の呼び出しはパッケージの中にあるので、compile symbolではなくこの設定と`Detail`で制御する。

| 指標 | 単位 | 取得元 | 間隔 | 詳細度 | 非developmentのIL2CPP |
|---|---|---|---|---|---|
| `unity.frame.time` | ms | `Time.unscaledDeltaTime` | 毎フレーム | Basic | 取れる |
| `unity.frame.main_thread.time` | ms | `CPU Main Thread Frame Time` | 毎フレーム | Basic | 取れる |
| `unity.gc.frame_allocated` | By | `GC Allocated In Frame` | 毎フレーム | Standard | 取れない |
| `unity.gc.collections` | {collection} | `GC.CollectionCount(0)` | 書き出し時 | Basic | 取れる |
| `unity.memory.used` | By | `Total Used Memory` | 書き出し時 | Basic | 取れる |
| `unity.memory.reserved` | By | `Total Reserved Memory` | 書き出し時 | Basic | 取れる |
| `unity.memory.system.used` | By | `System Used Memory` | 書き出し時 | Basic | 取れる |
| `unity.gc.used` | By | `GC Used Memory` | 書き出し時 | Basic | 取れる |
| `unity.gc.reserved` | By | `GC Reserved Memory` | 書き出し時 | Basic | 取れる |
| `unity.memory.gfx.used` | By | `Gfx Used Memory` | 書き出し時 | Basic | 取れない |
| `dexc.telemetry.source.time` | ms | `Stopwatch` | 毎フレーム | Standard | 取れる |
| `dexc.telemetry.export.time` | ms | `Stopwatch` | 次の書き出し | Basic | 取れる |
| `dexc.telemetry.export.size` | By | フレームのサイズ | 次の書き出し | Basic | 取れる |

- 表はWindowsのIL2CPP（Stripping High）で確認した結果である。Androidは未確認である
- 取れない指標は書かない。0とは書かない。最初のフレームの前のメモリも同じである
- メモリは書き出し時の値である。区間の最大値は持たない

## Logs

ロガーはカテゴリごとのインスタンスである。

```csharp
var log = DexcTelemetry.Logger("Game.Stage").With("mode", "hard");
log.Info("stage {Stage} took {Seconds:F2}", stage, seconds);
log.Error(exception, "stage {Stage} failed", stage);
```

| 型、メンバー | 内容 |
|---|---|
| `DexcTelemetry.Logger(category)` | カテゴリのロガー。同じカテゴリは同じインスタンスである |
| `logger.With(key, value)` | 属性を足したロガー。カテゴリとレベルは同じである。呼び出しごとではなく一度だけ作る |
| `logger.WithEvent(name)` | 記録を名前付きのイベントにするロガー。呼び出しごとではなく一度だけ作る |
| `logger.Verbose`から`logger.Fatal` | 記録する。compile symbolで消える。値は4個までである |
| `logger.Error(exception, template, ...)` | 例外を一緒に記録する |
| `logger.IsEnabled(severity)` | 記録されるかを返す |
| `logger.Emit(...)` | アダプター用。本文はテンプレートとして読まない |
| `TelemetryValue` | 値の型。`string`、`int`、`long`、`float`、`double`、`bool`から暗黙に変換する。ほかの型は`TelemetryValue.Of(value)`を使う |

### イベント

OpenTelemetryでは、`EventName`を持つログがイベントである。名前は1種類の出来事を表し、変わる値を含めない。変わる値はテンプレートの値か属性にする。

```csharp
var cleared = log.WithEvent("game.stage.cleared");
cleared.Info("stage {Stage} cleared", stage);
```

### テンプレート

形式は公式の.NETのロガーと同じである。

- `{Name}`の穴に値を順に当てる。名前は値の順番に関係しない。`{Name:F2}`の`:`から後は書式である。`{{`と`}}`は波括弧そのものである
- Bodyはテンプレートのままとし、値は名前をキーにした属性にする。数値と真偽値は型を保つ
- `TelemetryOptions.IncludeFormattedMessage`を有効にすると、Bodyは値を埋めた文になる。属性は変わらない。文を作るので確保が発生する
- 穴と値の数が合わなければ、余った穴と値は無視する。定数のテンプレートは、ビルド時にIL処理が検査してエラーにする（[IL処理](#il処理)）
- `TelemetryValue.Of`の`ToString`は書き出すときに呼ぶ。例外を投げても出力は止めない

### 確保しないこと

- レベルより下の呼び出しは、確保も文字列の組み立てもせずに戻る。値は暗黙の変換でも箱詰めしない。`params`も使わない
- `TelemetryValue.Of`に構造体や列挙型を渡すと箱詰めが起きる。必要なら`logger.IsEnabled`で囲む
- 記録する場合も、数値、真偽値、文字列の値なら確保しない

### レベル

- `DexcTelemetry.LogLevel`が既定のレベルである。`TelemetryOptions.MinimumSeverity`（既定はInfo）を開始時の値にする
- `DexcTelemetry.SetLogLevel(category, level)`はカテゴリとその下に効く。`Game`は`Game.Stage`も含み、`GameOver`は含まない。一番長く一致したものを使う。`ClearLogLevel`で戻す
- いつでも変えられる。ロガーを作り直す必要はない

### 書き出し

- カテゴリはInstrumentationScopeの名前にする。公式と同じである。カテゴリが変わるとScopeLogsを分け、記録の順番は保つ
- `With`の属性は、ロガーの作成時にバイト列にし、各記録へコピーする
- 例外は`exception.type`、`exception.message`、`exception.stacktrace`を属性に持つ。`GetType().Name`と`ToString()`を使う。公式と同じである
- 確保の大きいキューとバッファーは最初の記録で作り、rootも最初に使うときに作る。テレメトリーを使わないゲームには費用をかけない
- Warning以上で文字列のスタックトレースがあれば`code.stacktrace`に持つ。Infoは持たない
- 本文とスタックトレースは4096文字まで、属性の値は1024文字までとし、サロゲートペアの途中では切らない
- キューは1024件である。溢れた分と、1フレームに収まらない記録は捨てて、`dexc.telemetry.logs.dropped`で数える
- エンコードはmain threadで毎フレーム行い、64KBのバッファーへためる。埋まると1フレーム書く。Error以上があればすぐ書く。残りは書き出しの間隔で書く

### アダプター

別のロガーの記録は`ITelemetryLogAdapter`で取り込む。`TelemetryOptions.LogAdapters`に渡すと、開始時に`Attach`、終了時に`Detach`を呼ぶ。

```csharp
sealed class OtherLoggerAdapter : ITelemetryLogAdapter
{
	TelemetryLogger m_Logger;

	public void Attach()
	{
		m_Logger = DexcTelemetry.Logger("Other.Library");
		OtherLogger.Received += OnReceived;
	}

	public void Detach()
	{
		OtherLogger.Received -= OnReceived;
	}

	void OnReceived(string message)
	{
		m_Logger.Emit(TelemetrySeverity.Info, message);
	}
}
```

- `Emit`の本文は平文である。他のロガーの文に波括弧があっても穴として読まない
- `UnityLogAdapter`がUnityのログを取り込むアダプターである。`TelemetryOptions.CollectUnityLogs`（既定は有効）で自動で付く。カテゴリは`Unity`で、`SetLogLevel("Unity", ...)`で絞れる
- `LogType.Log`はInfo、`Warning`はWarning、`Error`と`Assert`と`Exception`はErrorとする。Unityの例外は`Type: message`の文字列から属性を作る

## Traces

```csharp
var tracer = DexcTelemetry.Tracer("Game.Stage");

using (var span = tracer.Start("stage.load"))
{
	span.Set("stage", stage);
	await LoadAsync();
	using (var child = tracer.Start("stage.spawn", span.Context))
	{
	}
}
```

- `TelemetrySpan`は構造体のハンドルで、固定のスロットを指す。開始、属性、終了は確保しない。スロットは256個で、最初のspanのときに作る。空きが無ければ何もしないspanを返し、`dexc.telemetry.spans.dropped`で数える
- 親子関係は`TelemetryContext`を明示して渡す。awaitをまたいでも、そのまま渡せる
- `TelemetryOptions.AmbientContext`を有効にすると、`AsyncLocal`で親を引き継ぐ。明示した親のほうを優先する。IL2CPPでは設定と解除に1.1〜1.4µsと519Bかかるので、既定は無効である
- `logger.WithContext(context)`で、ログに`trace_id`と`span_id`を付ける。`AmbientContext`が有効なら自動で付く
- spanの開始はmain threadだけである。属性、エラー、終了は、`ConfigureAwait(false)`などで別スレッドへ移った継続からも呼べる。1つのspanを同時に複数のスレッドから触るのは対象外である
- main threadでの終了は、その場でエンコードする。別スレッドでの終了は、終了の時刻を記録してスロットの番号をロック付きのキューへ入れるだけにし、次のフレームにmain threadでエンコードする。キューは開いているspan1つにつき1件までなので、スロット数で足りて溢れない
- main threadの取り出しは、件数をロックなしで先に読み、0件ならロックを取らない。.NET 10のReleaseで、キューへ入れるのは約33ns、スレッドの判定は約0.5nsだった（IL2CPPでは最後の確認で測る）
- 値を返すので`ConditionalAttribute`では消せない。代わりにIL処理が、シンボルより細かい`Start`を引数の計算ごと空のspanに置き換える（[spanの除去](#spanの除去)）。消えなかった呼び出しは、実行時に`TelemetryOptions.Detail`で止まる
- 属性は8個までで、超えた分は`dropped_attributes_count`で数える。同じキーをもう一度渡すと値を上書きする。スロットごとに8個分を持ち、256個で約64KBである
- 通常時に毎フレームのspanは作らない。作るのはキャプチャの間だけである
- `Stop`の時点で開いているspanは書かない
- ファイルは`traces-<run>.otlp`である。64KBのバッファーへためて、埋まると書き、書き出しの間隔でも書く

## キャプチャ

```csharp
DexcTelemetry.Capture(120);                                   // 次の120フレーム
DexcTelemetry.Capture(300, TelemetryDetail.Detailed, unityProfiler: true);
DexcTelemetry.StopCapture();
```

- `Capture(frames, detail, unityProfiler)`は、次のフレームの区切りから`frames`フレームの間、詳細度を`detail`へ上げ、Metricsを毎フレーム書く。終わると詳細度と書き出しの間隔を元に戻す。すでに細かい詳細度なら下げない
- 普段と同じファイルへ書く。Metricsは書き出しごとに区間を区切る方式なので、同じ計器を別のファイルへ分けて書くと集計が2つ要るためである。区間は`dexc.capture`のspanで示し、フレームごとに子の`unity.frame`のspanを作る。`unity.frame.number`は`Time.frameCount`である
- キャプチャのspanは`AsyncLocal`の親を使わず、親にもならない。ゲームのspanがフレームのspanの下へ入らないようにするためである
- 公開のstaticのAPIなので、Autopilotの`eval`からそのまま呼べる。パッケージにサーバーは持たせない
- 動いていないとき、キャプチャ中のときは`false`を返す。`StopCapture`は次のフレームの区切りで終える。始まる前なら取り消す。`Stop`はキャプチャを終えてから書く
- シンボルやIL処理で消した呼び出しは、キャプチャでも戻らない。リリース版で何を取れるかは、残した詳細度で決まる
- ログの重大度は変えない。必要なら`SetLogLevel`を呼ぶ
- 毎フレームの書き出しは、IL2CPPの実測（計測元を含め約87µs、約1.9KB）からの推測で、60fpsのフレーム時間の約0.5%、300フレームで約570KBである
- `unityProfiler`を有効にすると、Unity Profilerを`capture-<run>-<n>.raw`へ書く。`Profiler.logFile`、`enableBinaryLog`、`enabled`を使い、終わりに`logFile`を空にしてファイルを閉じ、`enabled`を元に戻す。EditorのProfilerの窓で開ける
  - Profilerが無いPlayer（`Profiler.supported`がfalse）と、ほかがすでにバイナリのログを書いているときは書かない。テレメトリーの記録は続ける
  - `.raw`もフォルダーの上限に数える。上限は`MaximumDirectoryBytes`で変えられる
  - Development Build以外での動きと、`.raw`の大きさは、最後のPlayerの確認で確かめる

## 実行

- `DexcTelemetry.Start(options)`が実行ごとに新しいファイルを開き、`Stop`が残りを書いて閉じる。`Start`の前の記録は、`Start`の後の最初の書き出しに含まれる
- ファイルは`<Directory>/metrics-<run>.otlp`、`logs-<run>.otlp`、`traces-<run>.otlp`である。書いている間は名前の後に`.open`が付く（[受け渡し](#書き出しと受け渡し)）。`Directory`の既定値は`Application.persistentDataPath`の`Telemetry`である
- `MaximumFileBytes`（既定8MiB）を超えると、フレームの切れ目で`logs-<run>-1.otlp`のように次のファイルへ移る
- フォルダーが`MaximumDirectoryBytes`（既定64MiB）を超えると、書き込み中のものを除き、古い`.otlp`とキャプチャの`capture-*.raw`から消す。問題の直前の記録を残すためである。実行の開始時とファイルの切り替え時に確認する
- Metricsの書き出しの間隔は既定60秒である。Metricsは区間の集計なので、メモリの量は間隔に関係しない。データの無い計器は書かず、すべて無ければフレームも書かない
- フレームの処理はPlayerLoopの`Initialization`の最後で行う。前のフレームの記録とUnityの計測値がそろった後であり、処理自体は次のフレームのProfilerのフレームに入る。Play Modeのときだけ登録し、`Stop`で外す
- `OnApplicationPause`と`OnApplicationQuit`でも書く
- 書き込みが`IOException`で失敗したら、`Debug.LogException`で出力し、以後の出力をやめる。ゲームは止めない
- Resourceは次の層を順に重ね、同じキーは後の層が勝つ。最後の2つは変えられない
  1. Unityが知るもの：`service.name`（`ServiceName`か`Application.productName`）、`service.version`（`ServiceVersion`か`Application.version`）、`device.model.identifier`（`SystemInfo.deviceModel`）、`os.description`（`SystemInfo.operatingSystem`）
  2. コードの`ResourceAttributes`
  3. OpenTelemetryの環境変数：`OTEL_RESOURCE_ATTRIBUTES`（`key=value`をカンマで区切り、値はパーセントエンコード。読めなければ全体を捨てる）と`OTEL_SERVICE_NAME`（`service.name`より優先）。SDKの仕様の読み方である
  4. コマンドライン：`-dexc-telemetry-resource key=value`（何度でも）。Playerを起動した道具が書く。ビルド、コミット、シードのように、ゲームが知らない実行の情報を載せるためである
  5. `service.instance.id`（実行ごとに固有）と`telemetry.sdk.name`
- フォルダーは、コマンドラインの`-dexc-telemetry-directory`、`Directory`、既定の順に決める。同じ製品の複数のビルドは`persistentDataPath`が同じなので、並べて動かす道具がビルドごとに分けるためである
- 端末を一意に示すID（`SystemInfo.deviceUniqueIdentifier`など）は載せない
- 記録の時刻は、rootを作ったときの`DateTime.UtcNow`に、`Stopwatch`の経過を足したものである。WindowsのIL2CPPのPlayerでは`DateTime.UtcNow`が約1ms刻みでしか進まず、メソッドのspanの長さが0になったためである（2026-11-08のPlayerの確認）

## 書き出しと受け渡し

データは必ずファイルへ先に書く。ファイルがそのまま、受け渡しを待つ列になる。サーバーがあればPlayerから送り（Push）、無ければホストがファイルを取りに行く（Pull）。数値は、OpenTelemetryの仕様（OTLP 1.11.1、SDKの環境変数）とCollector（exporterhelper、confighttp、fileexporter）の既定値に合わせ、合わせられないものは理由を書く。

### フレーム

| 信号 | 区切り | 本流の値 |
|---|---|---|
| logs、traces | 64KBか、最長`RecordFlushSeconds`（既定5秒）。Error以上はすぐ | SDKのspanは5秒・512件、ログは1秒・512件。64KBは記録500件ほどにあたる |
| metrics | `ExportIntervalSeconds`（既定60秒）。キャプチャ中は毎フレーム | SDKは60秒 |

- ファイルへの書き込みは、専用のスレッド1本で行う。main threadはフレームを組み立て、使い回す領域へ写して列に入れるだけで、ストレージを待たない。書き込み、OSへの受け渡し、ファイルの切り替え、`.otlp`への名前の変更、ディスクへの確定を、受けた順に行う。WebGLはスレッドが無いので、その場で書く
- 書き込みを待つ量は`MaximumPendingBytes`（既定4MiB）までにする。超えたフレームは捨てて`dexc.telemetry.frames.dropped`で数え、メモリを増やさない。`WhenWritingFallsBehind`を`Wait`にすると、main threadが空きを`BacklogWaitSeconds`（既定1秒）まで待ち、空かなければ捨てる。検証用のビルドのためで、ストレージが止まるとゲームも止まる。main threadが自分で書かないのは、書き込みのスレッドが同じファイルに先のフレームを書いていて、フレームの順序が崩れるためである。写す先の領域も、同じ量まで使い回すので、毎回は確保しない。待つものが無ければ、上限より大きいフレームも1つは通す
- 書き終わりを待つのは、明示の`Flush`、`Seal`、`Stop`、アプリの中断のときだけで、最長5秒である。定期の書き出しは待たない。スレッドで起きた書き込みの失敗は、次のフレームでmain threadが受け取り、出力をやめる
- スレッドはフレームを書くたびにOSへ渡す（`Stream.Flush()`）。プロセスが落ちても、渡したフレームは残る。ディスクへの確定（`Flush(true)`）は、アプリの中断と終了のときだけにする。毎回の確定は端末で重いためである
- ログのSDKの1秒より長い5秒にするのは、送り先がネットワークでなくファイルで、区切るほどフレームごとのResourceとScopeが増えるためである
- .NET 10の実測（毎秒ログ100件、span 50個を10分）で、区切りが1秒ならフレームごとのResourceとScopeが大きさの3.8%、5秒なら0.8%だった。CPUの時間はどれもゲームの1秒あたり約0.15msで差は無い。この量では64KBに9秒ほどで達するので、15秒以上は効かない。OSへ渡すのは1フレーム0〜12µs、ディスクへの確定は0.6〜0.9msだった

### ファイルの状態

- 書いている途中のファイルは`<signal>-<run>[-<n>].otlp.open`で、閉じたときに`.otlp`へ名前を変える。取りに行く側は`*.otlp`だけを取ればよい
- 落ちて残った`.open`は、次の`Start`で、ほかの実行のものを`.otlp`に直す。末尾の書きかけのフレームは、読む側が捨てる
- `DexcTelemetry.Seal()`は、ためている記録を書き出し、今のファイルを閉じて次のファイルに移る。Pullの直前に`eval`から呼ぶ

### Pull（サーバーが無いとき）

- ホストが閉じたファイルを取り、取った側が消す。消さなければ、フォルダーの上限で古いものから消える。ファイル名は実行と番号で一意なので、同じ名前を二度取らなければ重複しない
- ファイルに直接触れられない環境のために、`DexcTelemetry.Files()`、`ReadFile(name, offset, buffer)`、`DeleteFile(name)`を持つ。運ぶ経路はパッケージに持たせない（Autopilotの接続はT9）
- ホストのCLIの`pull`は、フォルダー（Windows、Editor）とadb（Android）から取る
- Autopilotのワークスペースは、`ws telemetry pull`でevalから`Seal`を呼び、各ビルドのフォルダーから集める（[Autopilotの仕様](autopilot.md#テレメトリー)）

### Push（サーバーがあるとき）

| 項目 | 規格 | 本流の値 |
|---|---|---|
| 要求 | 同じファイルの続いたフレームの本体を連結して1つの要求にする。protobufでは連結が繰り返しの欄の結合になるので、組み立て直さずに有効な`Export*ServiceRequest`になる | Collector 0.164.0で受け付けを確かめた |
| 大きさ | 1要求1MiBまで（設定で変える）。1フレームが超えればそれだけで送る | OTLPの推奨の上限64MiB、confighttpの既定20MiB。Collectorの束ねの8192件に近い |
| 時期 | 60秒ごと、ファイルを閉じたとき、アプリの中断のとき（待たない） | metricsの書き出し間隔と同じ |
| 同時の数 | Playerは1つ | exporterhelperは10 |
| 圧縮 | 既定は速さ優先のgzip（`CompressionLevel.Fastest`）。無しにもできる。.NET 10の実測で、1MiBのlogsは12%（3.6ms）、tracesは42%（9.3ms）に縮んだ。大きさ優先は4〜5倍遅く、縮みは3割ほどしか増えない | 受ける側はgzipに必ず対応し、送る側は任意。SDKの既定は無し。端末の通信量を優先して既定を変える |
| 待ち時間 | 1要求30秒 | SDKの書き出しの待ち30秒 |
| 再送 | 429、502、503、504と通信の失敗。`Retry-After`に従い、無ければ5秒から1.5倍、最大30秒、ばらつき付き。300秒で諦めても、ファイルは残して次の時期に試す | OTLPの仕様、exporterhelperの既定 |
| 再送しない | 400などほかの4xxと5xxは、その分を捨てて数える。413は要求を半分に分けて送り直し、1フレームでも413なら捨てる。400でも、Statusのメッセージに`too large`があれば413と同じに扱う。部分的な成功は拒否の数を数えて再送しない | OTLPの仕様。413の分け方はこちらの判断。Collector 0.164.0は上限を超えた本文に413でなく400と`http: request body too large`を返した（2026-11-09に確かめた） |
| 位置 | 送り終えた位置をファイルごとに`.sent`に残し、再起動しても二重に送らない。閉じて全部送ったファイルは両方消す | |
| 費用 | 送信用の1MiBの領域を最初に1回確保して使い回す。ファイルの読み込みとgzipは別スレッド、UnityのAPIはmain threadだけ | |

- 送る種類（metrics、logs、traces）は個別に止められる。Unityのログには任意の文字列が入りうるので、logsの送信は既定で止める
- パッケージは端末やユーザーのIDを集めない。送信はゲームが呼んだときだけ始める
- 1つのビルドではPushかPullのどちらかにする。どちらも受け渡した後にファイルを消すためである

### CLI

`Telemetry/Tools~/Cli/`の.NET 10のコンソールである。公式の`.proto`から生成したコードで読む。自作の書き出しと別の実装で読むので、互換の確認にもなる。

| コマンド | 内容 |
|---|---|
| `dump` | 中身の要点を表示する |
| `send` | Pushと同じ規格でcollectorへ送る |
| `pull` | Pullの規格でファイルを取る |
| `captures`、`cut` | キャプチャの区間の一覧を出し、同じ形式のファイルに切り出す |

JSONへの変換は持たない。読むなら`dump`、外部の道具ならcollectorの出力（`fileexporter`など）を使う。

```sh
dotnet run --project Telemetry/Tools~/Cli -c Release -- dump <folder>
dotnet run --project Telemetry/Tools~/Cli -c Release -- send <folder> --endpoint http://127.0.0.1:4318
```

- `.proto`は`Telemetry/Tools~/proto/`に、`upstream.json`の版のものをそのまま置く（Apache License 2.0）。`Google.Protobuf`と`Grpc.Tools`でコードを生成する
- `send`はファイルの状態を残さない。同じファイルを二度送れば二度届く
- `cut`は、キャプチャのtraceのspanと、その間に始まったspan、その間のlogs、区間が重なるmetricsのフレームを、`<signal>-<run>-capture<n>.otlp`に書く

### 本家への追従

- 月1回程度、手元で行う。CIでは行わない。手順は`Telemetry/Tools~/UPSTREAM.md`にある
- `Telemetry/Tools~/upstream.json`に、opentelemetry-proto、specification、semantic-conventions、opentelemetry-dotnet、Collectorの版を固定する
- `upstream check`は最新のタグと比べる。`upstream verify`は、`OtlpFields.cs`を`otlp-fields.json`の対応でタグの`.proto`と照合し、`Tools~/Fixtures`がパッケージの書き出しのコードで書いた固定のフレームを、タグのCollectorに送って確かめる。`Fixtures`はUnityの無いところでランタイムのソースをそのままコンパイルし、Unityの型の代わりを少しだけ置く。手元のデータをリポジトリに置かずに済む

## IL処理

- `Telemetry/CodeGen/`の`ILPostProcessor`（アセンブリ`Unity.Dexc.Telemetry.CodeGen`）が、`Dexc.Telemetry`を参照するアセンブリをコンパイルのたびに処理する。Editor用とPlayerのビルドの両方で動くので、報告はコンパイルエラーになる。依存は`com.unity.nuget.mono-cecil`である
- 処理の本体はUnityの型から切り離し、テストはCecilで組み立てたILに対して行う。誤ったテンプレートをテストのコードに書くと、検査がビルドを止めるためである

### テンプレートの検査

- ログの呼び出しで、テンプレートの穴の数と値の数が違えばエラーにする。穴の数え方は実行時と同じで、テストで一致を確かめる
- 検査できるのは、テンプレートが定数（`ldstr`）で、引数の計算に分岐が無い呼び出しだけである。それ以外は検査しない
- コンパイルのシンボルで消えた呼び出しはILに無いので検査しない。`DEXC_TELEMETRY_LOG_VERBOSE`の無いビルドでは、Verboseの呼び出しは検査されない

### spanの除去

- `TelemetryTracer.Start`の詳細度が、シンボルが残す詳細度より細かければ、呼び出しを引数の計算ごと消し、空のspanにする。`ConditionalAttribute`と同じ扱いで、引数は評価されない
- シンボルは、プロジェクトのもの（`compiledAssembly.Defines`）と、呼び出しのあるソースファイルの先頭の`#define`と`#undef`で決める。後者はアセンブリに残らないが、`ConditionalAttribute`はそれに従うので、ファイルを読んで同じ判定にする。先頭に`#if`があるファイルは判定せず、すべて残す。読めないファイルはプロジェクトのシンボルだけで決める
- `Set`、`SetError`、`Dispose`は残す。空のspanには何もしない
- 詳細度が定数でない呼び出しと、引数の計算に分岐がある呼び出しは消さない。実行時の詳細度で止まる

### 属性による計測

```csharp
[TelemetryMethod]
void Spawn() { }

[TelemetryMethod(Every = 4, PerFrame = 8, AnyThread = true)]
void Path() { }

[TelemetryMethod(Detail = TelemetryDetail.Standard)]
sealed class Stage { }
```

- `TelemetryMethodAttribute`を付けたメソッドを、キャプチャの間だけ、呼び出しごとのspanにする。クラスや構造体に付けると、そこで宣言したメソッドが対象になる。コンストラクター、プロパティとイベントのアクセサー、コンパイラーが作ったコードは除く。メソッドの属性が型の属性より優先する
- IL処理が、本体を`TelemetryMethodProbe.Enter`と`try`/`finally`の`Exit`で囲む。戻り値と例外はそのまま通る。`Detail`（既定はDetailed）をシンボルが残さなければ、何も入れない。シンボルはspanの除去と同じく、プロジェクトとファイルの先頭で決める
- キャプチャの外では、`Enter`はrootを作らずに読み、空のプローブを返す。テレメトリーを使わないゲームにrootを作らせないためである
- spanの名前は`名前空間.型.メソッド`、Scopeはアセンブリの名前である。親は、計測中のメソッドの中から呼ばれればそのspan、無ければキャプチャのフレームのspanである
- `Every`でN回に1回に間引く。記録したspanには`dexc.method.every`を付ける。`PerFrame`（既定32）は1フレームに記録する上限である。間引いた呼び出しは`dexc.telemetry.method_calls.skipped`、上限やキューで落とした呼び出しは`dexc.telemetry.method_calls.dropped`で数える
- 既定ではmain threadの呼び出しだけを記録する。`AnyThread`を有効にすると、ほかのスレッドの呼び出しも記録する。終わった呼び出しをロック付きのキュー（1024件）へ入れ、次のフレームにmain threadで書く。親はその時のフレームのspanで、`thread.id`を付ける
- asyncのメソッドは、最初の呼び出しから完了までを1つのspanにする。状態機械に`<>dexc__probe`の欄を足し、メソッドが状態機械を始める前に`TelemetryMethodProbe.EnterAsync`で設定し、状態機械がビルダーの`SetResult`と`SetException`を呼ぶ直前に終える。`Task`、UniTask、`Awaitable`のどれもビルダーがこの2つを持つので、同じに扱える
- asyncのspanは、その間の呼び出しの親にならない。最初のawaitで呼び出し元へ戻ると、関係の無い呼び出しが続くためである。別スレッドで完了したときは、spanの終了と同じくキューを通して次のフレームに書く
- asyncのメソッドの行は状態機械の`MoveNext`にあるので、シンボルを読むファイルはそこから決める
- 状態機械がコンパイラーの通常の形でなければ、警告を出してそのままにする。EditorのDebugのコンパイル（状態機械はクラス）で確かめた。Releaseのコンパイル（状態機械は構造体）は最後のPlayerの確認で確かめる
- イテレーターは計測しない。本体が状態機械を作るだけで、終わりを示す呼び出しが無いためである
- 同じ名前のオーバーロードは、間引きと上限を共有する
- 記録した呼び出しには確保を付ける。ランタイムがスレッドの確保量を数えるなら、前後の`GC.GetAllocatedBytesForCurrentThread`の差を`dexc.method.allocated_bytes`にする。数えないとき（Editorは常に0を返す）は、Profilerの`Recorder("GC.Alloc")`で確保の回数を`dexc.method.allocations`にする。1つのrecorderは入れ子にできないので、計測中の呼び出しの一番外側だけで数える。asyncの呼び出しには付けない
- どちらの方法も、最初に1回確保して数えられるかを試し、数えられなければ確保は記録しない。0と誤って記録しないためである。WindowsのIL2CPPの実測（2026-11-08）では、`GC.GetAllocatedBytesForCurrentThread`はDevelopment Buildでも通常ビルドでも差が0で、例外は投げなかった。`Recorder`はどちらも有効と答えるが、数えたのはDevelopment Buildだけだった。通常ビルドでは呼び出しごとの確保は取れない

### Watch

```csharp
DexcTelemetry.Watch("Game.Stage.Load");
DexcTelemetry.Watch("Game.Stage.*");
DexcTelemetry.Watch(typeof(Stage).GetMethod(nameof(Stage.Load)));
DexcTelemetry.Watch((Action)stage.Load);
DexcTelemetry.Unwatch("Game.Stage.*");
DexcTelemetry.UnwatchAll();
```

- `DEXC_TELEMETRY_WATCH`をプロジェクトのシンボルに定義したビルドでは、プリセットか関数で選んだメソッドに、止めた計測を入れる。`Watch`した間だけ、テレメトリーが動いていれば、キャプチャの外でも呼び出しごとのspanにする。キャプチャ中はフレームのspanの子になる
- `Watch`は名前（型の完全名、ドット、メソッド）か、`*`で終わる前方一致を取る。`MethodInfo`やデリゲートも渡せる。オーバーロードはまとめて扱う。公開のstaticのAPIなので、`eval`からもプロジェクトのコードからも呼べる
- 止めた計測の費用は、.NET 10のReleaseでの実測で、何もしないメソッド1.5nsに対して次のとおりである。IL2CPPの値は最後に測る

| 状態 | 1回の呼び出し |
|---|---|
| テレメトリーを使わない（rootが無い） | 2.9ns |
| 動いていて、何も`Watch`していない | 3.3ns |
| 別のメソッドを`Watch`している | 21.5ns（名前ごとの判定を辞書で引く） |
| このメソッドを`Watch`している（1フレーム1000回、上限32） | 平均45ns |

- コンストラクター、アクセサー、コンパイラーが作ったコード、イテレーター、`TelemetryMethodAttribute`の付いたメソッドは選ばない
- `BurstCompile`の付いたメソッドと型は、属性でもWatchでも計測しない。Burstはネイティブにコンパイルし、ランタイムを呼べないので、入れた呼び出しでBurstのコンパイルが失敗するためである
- `DEXC_TELEMETRY_WATCH`のあるアセンブリは、コンパイルのたびにCecilで読み書きする。選ぶ関数があるとき、または`assemblies`の無いプリセットでは、シンボルのあるすべてのアセンブリが対象になり、その分コンパイルが遅くなる
- `Watch`と`Unwatch`はmain threadから呼ぶ
- `Dexc.Telemetry`を参照していないアセンブリも選べる。その場合は参照を足す
- ビルド済みのDLL（`Plugins`のものなど）は、コンパイルのIL処理に渡されない。Playerのビルドでは、スクリプトのコンパイルの後（`IPostBuildPlayerScriptDLLs`）にビルドの中のコピーを書き換えて、同じ計測を入れる。プロジェクトのファイルは変えないので、EditorのPlay Modeでは計測されない。この時点の`BuildReport`には、スクリプトのDLLとビルド済みのDLLが`Temp/StagingArea/Data/Managed`の`ManagedLibrary`として載る（2026-11-08のPlayerの確認）

### プリセット

```json
{
  "active": "Stage",
  "presets": [
    { "name": "Stage", "assemblies": ["Game.*"], "namespaces": ["Game.Stage"], "every": 1, "perFrame": 32, "anyThread": false },
    { "name": "Hot", "attributes": ["Game.HotPathAttribute"] }
  ]
}
```

- プロジェクトのプリセットは`ProjectSettings/DexcTelemetryProfiles.json`、ユーザーのものは`UserSettings/DexcTelemetryProfiles.json`に書く。ユーザーのプリセットは同じ名前のプロジェクトのものを置き換え、ユーザーの`active`が優先する
- `assemblies`（`*`で終わるものは前方一致）、`namespaces`（その下も含む）、`attributes`（メソッドか型の属性の完全名）のうち、書いたものすべてに合うメソッドを選ぶ。どれも書かなければ何も選ばない
- `every`、`perFrame`、`anyThread`は、選んだメソッドの間引き、上限、別スレッドの記録である
- IL処理はプロジェクトのフォルダーで動くので、そこからファイルを読む
- Project Settingsの「Dexc Telemetry」でプリセットを選ぶ。選んだものは`UserSettings`の`active`に書く。プロジェクトのファイルは変えない。プリセットの中身はファイルで編集する

### 設定の反映

- ファイルを変えただけではコンパイルが起きない（Editorで確かめた）。そこで`DEXC_TELEMETRY_WATCH`のあるアセンブリには、IL処理が使った両方のファイルのハッシュを`AssemblyMetadataAttribute("DexcTelemetry.Profiles", hash)`で残す
- Editorは、読み込んだときと前面に戻ったときに、読み込んだアセンブリのハッシュとファイルを比べる。違えばビルドのキャッシュを捨てて再コンパイルを要求する（`CompilationPipeline.RequestScriptCompilation(CleanBuildCache)`）
- Playerのビルドは、前にコンパイルしたアセンブリを使い回すことがある。`IPostBuildPlayerScriptDLLs`でビルドのスクリプトのアセンブリのハッシュを比べ、違えばクリーンビルドを求めてビルドを止める。プロファイルだけを変えてビルドすると、Playerのビルドは前のアセンブリを使い回し、この確認で止まった（2026-11-08のPlayerの確認）

### 関数で選ぶ

```csharp
// Unity.MyGame.Telemetry.CodeGen という名前の、Editor用のasmdef。Unity.Dexc.Telemetry.CodeGen を参照する
public sealed class GameSelector : ITelemetryMethodSelector
{
	public bool Select(TelemetryMethodCandidate method) => method.Namespace.StartsWith("Game.") && method.IsPublic;
}
```

- `Unity.`で始まり`.CodeGen`で終わる名前のアセンブリは、IL処理のプロセスに読み込まれる。IL処理はそこから`ITelemetryMethodSelector`を実装した型を探し、プリセットに加えて使う（Editorで確かめた）
- 選ぶ関数はコンパイル中にUnityの外で動くので、UnityEngineとUnityEditorは使えない
- `TelemetryMethodCandidate`は、アセンブリ、namespace、型、メソッド、属性、static、public、async、引数の数を持つ

## protobufの書き出し

- エンコーダーは自作する。外部のprotobufライブラリとコード生成を使わない
- 事前に確保したバッファーへ書き、確保しない
- 入れ子のメッセージの長さは4バイト分を確保し、終わったときにパディングしたvarintで埋める。公式の実装と同じ方法である。長さの上限は`2^28 - 1`である
- バッファーに入らない書き込みは捨て、`HasOverflowed`を立てる。以後の書き込みもすべて捨てる。呼び出し側はそのメッセージを破棄する。テレメトリーの都合でゲームを止めないためである

## ファイルの形式

- 4バイトのビッグエンディアンの長さに続けて、メッセージの本体を書く。OpenTelemetry Collectorの`fileexporter`の`format: proto`と同じである
- フレームに信号の種類は無いので、1つのファイルには1種類の信号だけを書く
- 読み手は、途中で切れたフレームを捨て、それより前のフレームを使う
