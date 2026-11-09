# Dexc Bridge仕様

Dexc Debugger、Dexc Telemetry、Dexc QAをつなぐパッケージ（`Bridge/`、`jp.yazawa-ichio.dexc-bridge`）の取り決めを定める。3つのパッケージはお互いを参照しない。つなぐ所はすべてここに置く。

## 層

| パッケージ | 参照するもの |
|---|---|
| Dexc Debugger | なし |
| Dexc Telemetry | なし（リリースにも残す） |
| Dexc QA | Dexc Telemetry（あれば、`DEXC_QA_HAS_TELEMETRY`） |
| Dexc Bridge | Dexc Debugger。Dexc TelemetryとDexc QAは、あるものだけ（`DEXC_BRIDGE_HAS_TELEMETRY`、`DEXC_BRIDGE_HAS_QA`） |

- asmdefは`!DISABLE_DEXC_DEBUGGER`を持つ。Dexcと同じく開発用である。Dexcを外したリリース版に残るのはTelemetryだけになる
- 公開APIだけを使う。どのパッケージにも、Bridgeのための内部の入口を作らない
- 状態を持たない。Dexcのパネルが覚える値はパネルの中に、QAの拡張点はQAのrootにある

## Telemetryパネル

Telemetryがあれば、DexcのメニューのPanelsに`Telemetry`を足す。

| 項目 | 内容 |
|---|---|
| 状態 | 動作中か、キャプチャ中か、書き出し先（`DexcTelemetry.Folder`） |
| キャプチャ | フレーム数と、Unity Profilerの`.raw`も書くか。`DexcTelemetry.Capture` |
| Seal | 今のファイルを閉じる |
| 送信 | 送り先（既定は同じマシンのダッシュボード`http://127.0.0.1:7892`）、logsも送るか（既定は送らない）、開始と停止。`DexcTelemetry.StartUpload`と`StopUpload` |

- 開始はゲームが行う。パネルはテレメトリーを始めない
- 送信は、動いているテレメトリーに後から付ける。計測のサーバーを後から手元に立て、そこへつなぐためである。送り先を変えるときは、止めてから始める
- 送信中のビルドのファイルは、送った後に消える。ワークスペースの`ws telemetry pull`は、送信中のPlayerのファイルを取らない（[Autopilotの仕様](autopilot.md#テレメトリー)）

## QAの報告

QAがあれば、実行の始まり（assemblyの読み込みの後）に報告の拡張点を1つ足す。報告に次の2つの節を書く。

| 節 | 内容 |
|---|---|
| `inspector` | InspectorのWatch。対象（登録したラベルとパス）、Component、fieldの表示名、Watchの状態、値の写し。50件まで |
| `diagnostics` | DexcのDiagnosticsの要約。20件まで |

- Dexcが動いていなければ、どちらもnullとする
- 値は`DebugValue`をそのまま数値、文字列、配列、objectにする。Unity参照はラベル、読めない値は型名とエラーを書く。Inspectorのhandleはその実行の中でしか意味を持たないので、値の写しだけを残す
- 名前を引けなかったWatch（対象が消えた等）は、`error`に理由を書き、ログには書かない。Errorのログは報告に戻ってくるためである
