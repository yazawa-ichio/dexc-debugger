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

## protobufの書き出し

- エンコーダーは自作する。外部のprotobufライブラリとコード生成を使わない
- 事前に確保したバッファーへ書き、確保しない
- 入れ子のメッセージの長さは4バイト分を確保し、終わったときにパディングしたvarintで埋める。公式の実装と同じ方法である。長さの上限は`2^28 - 1`である
- バッファーに入らない書き込みは捨て、`HasOverflowed`を立てる。以後の書き込みもすべて捨てる。呼び出し側はそのメッセージを破棄する。テレメトリーの都合でゲームを止めないためである

## ファイルの形式

- 4バイトのビッグエンディアンの長さに続けて、メッセージの本体を書く。OpenTelemetry Collectorの`fileexporter`の`format: proto`と同じである
- フレームに信号の種類は無いので、1つのファイルには1種類の信号だけを書く
- 読み手は、途中で切れたフレームを捨て、それより前のフレームを使う
