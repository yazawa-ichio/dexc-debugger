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
- main thread専用とする

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

## 実行

- `DexcTelemetry.Start(options)`が実行ごとに新しいファイルを開き、`Stop`が残りを書いて閉じる。`Start`の前の記録は、`Start`の後の最初の書き出しに含まれる
- ファイルは`<Directory>/metrics-<run>.otlp`である。`Directory`の既定値は`Application.persistentDataPath`の`Telemetry`である
- Metricsの書き出しの間隔は既定60秒である。Metricsは区間の集計なので、メモリの量は間隔に関係しない。データの無い計器は書かず、すべて無ければフレームも書かない
- `OnApplicationPause`と`OnApplicationQuit`でも書く
- 書き込みが`IOException`で失敗したら、`Debug.LogException`で出力し、以後の出力をやめる。ゲームは止めない
- Resourceには`service.name`、`service.version`、`service.instance.id`（実行ごとに固有）、`telemetry.sdk.name`と、`ResourceAttributes`を載せる

## protobufの書き出し

- エンコーダーは自作する。外部のprotobufライブラリとコード生成を使わない
- 事前に確保したバッファーへ書き、確保しない
- 入れ子のメッセージの長さは4バイト分を確保し、終わったときにパディングしたvarintで埋める。公式の実装と同じ方法である。長さの上限は`2^28 - 1`である
- バッファーに入らない書き込みは捨て、`HasOverflowed`を立てる。以後の書き込みもすべて捨てる。呼び出し側はそのメッセージを破棄する。テレメトリーの都合でゲームを止めないためである

## ファイルの形式

- 4バイトのビッグエンディアンの長さに続けて、メッセージの本体を書く。OpenTelemetry Collectorの`fileexporter`の`format: proto`と同じである
- フレームに信号の種類は無いので、1つのファイルには1種類の信号だけを書く
- 読み手は、途中で切れたフレームを捨て、それより前のフレームを使う
