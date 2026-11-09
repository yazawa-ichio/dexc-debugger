# Dexc QA仕様

開発用のPlayerで不具合を見つけ、直すための記録を残す仕組みを定める。主な使い手は、ワークスペースを操作するエージェントとBotである。報告の型と書き出しは、人の報告にも使える共通の部品とする。

## 層

| 層 | 場所 | 持つもの |
|---|---|---|
| Dexc本体 | `Packages/jp.yazawa-ichio.dexc-debugger` | 4機能とDiagnostics。スクリーンショットと報告のファイルは本体の範囲外である（[スコープ](core-scope.md)） |
| Dexc Bridge | `Bridge/` | DexcのInspectorのWatchとDiagnosticsを報告に足す（[Bridgeの仕様](bridge.md)） |
| Dexc QA | `QA/`（`jp.yazawa-ichio.dexc-qa`） | Playerの中での不具合の検出と記録 |
| Dexc Telemetry | `Telemetry/` | 注釈と場所。あれば報告に載せる |
| ツール | `Autopilot/Tools~` | 報告を集めて不具合にまとめ、状態を管理する。修正の確認 |
| ゲーム | プロジェクト | Botの検査の中身、報告に足す項目、外部への送信 |

- `Dexc.QA`はDexc本体を参照しない。Dexcの情報はDexc Bridgeが拡張点から足す。`Dexc.Telemetry`はVersion Definesの`DEXC_QA_HAS_TELEMETRY`で任意とする
- Autopilotには依存しない。BotとevalはPlayerの型として`DexcQA`を直接呼ぶ
- asmdefは`!DISABLE_DEXC_DEBUGGER`を持ち、Dexcと同じく開発用とする。リリースに残すのはテレメトリーだけである
- mutable staticは`QARoot.s_Current`だけとする。Subsystem Registrationで作り直す
- main thread専用とする。ただしUnityのログのcallbackは任意のthreadから来るので、受けてキューに入れるだけにし、報告はmain threadで行う

## 開始

- 起動引数`-dexc-qa-directory <dir>`があれば、assemblyの読み込みの後に自分で始まる。ワークスペースはこれで最初のフレームからの例外を拾う
- ゲームは`DexcQA.Start(options)`でも始められる。始まっていないときの`Report`は、既定の設定で始める
- 書き出し先は、起動引数、`Directory`、`persistentDataPath/QA`の順に決める

## 報告

`DexcQA.Report(kind, signature, title, detail)`で報告する。`DexcQA.Check(condition, signature, message)`は、条件が偽のときだけ種類`check`で報告する。

| 種類 | 出どころ |
|---|---|
| `exception` | Unityが記録した例外（`CaptureExceptions`、既定は有効） |
| `error` | Unityが記録したエラーとAssert（`CaptureErrors`、既定は有効） |
| `check` | `DexcQA.Check` |
| `bot`、`agent` | BotとエージェントのReport。ゲームが独自の種類を使ってもよい |

- `signature`は同じ不具合をまとめる鍵である。変わる値（時刻、個体の番号）を入れない
- 同じ鍵は、最初の`DetailedReportsPerSignature`（既定3）件だけを詳しく書き、あとは数える。1回の実行で詳しく書くのは`MaximumReports`（既定200）件までとする。同じ例外が毎フレーム起きても、ディスクを埋めないためである
- 例外の鍵は`exception:<型>@<先頭のフレーム>;<2番目のフレーム>`とする。エラーの鍵は`error:<最初の行>@<フレーム>`とし、最初の行の数字は`#`に置き換える。フレームからは`(at ...)`の場所とアドレスを除き、`UnityEngine.Debug`の行を飛ばす。行番号が変わる修正の前後でも、同じ不具合が同じ鍵になるようにするためである
- Botとevalのインタープリターのフレーム（`System.Reflection.`、`ILRuntime.`、`Dexc.Autopilot.`）に来たら、そこで読むのをやめる。インタープリターが動かすコードは自分のフレームを持たず、その先はBotを呼ぶ側で、どの不具合でも同じだからである。それでフレームが残らない例外は、数字を`#`にしたメッセージを鍵に足す

## ファイル

報告ごとに`<日時>-<実行の先頭8文字>-<番号>/`のフォルダーを作る。

| ファイル | 内容 |
|---|---|
| `report.json` | id、実行、種類、鍵、題名、詳細、その鍵の何回目か、時刻、フレーム、ビルド（製品、版、Unity、platform、development）、resource（`-dexc-telemetry-resource`のビルド、コミット、シード）、注釈と場所とテレメトリーのフォルダー（Telemetryがあるとき）、直近のログ（`LogsInReport`、既定50件）、ゲームの項目、添付の名前、拡張点が足した節（Bridgeがあれば`inspector`と`diagnostics`）、スクリーンショット |
| `screenshot.png` | フレームの終わりに撮る。描かないPlayer（batch mode、graphicsなし）とEdit Modeでは撮らず、`screenshot`をnullとする。撮るまでの間は`pending`である |
| 添付 | ゲームが足したファイル |

- `report.json`は一時ファイルを経て置き換える。ツールが半分の報告を読まないためである
- 実行ごとの`signatures-<実行>.json`に、鍵ごとの種類、題名、回数、最初と最後の時刻、詳しく書いた報告のidを書く。報告を書いたとき、10秒ごと（変化があれば）、`DexcQA.Flush()`、中断、終了で書く
- テレメトリーのファイルは閉じない。報告のたびに`Seal`すると、書き込みを待つためmain threadが止まりうるからである。時刻でテレメトリーと突き合わせる

## Inspectorとの連携

Dexc Bridgeが、報告にInspectorのWatchとDexcのDiagnosticsを足す。決まりは[Bridgeの仕様](bridge.md#qaの報告)にある。

## 拡張点

| 拡張点 | 呼ばれるとき |
|---|---|
| `IDexcQAContributor.Contribute(report)` | 報告を書く前。`Set(key, value)`で項目、`SetSection(name, value)`で`report.json`の節、`Attach(name, bytes)`でファイルを足す。節の名前は報告が自分で使う名前を取れない。値はnull、bool、数、文字列、キーと値の組の並び（object）、そのほかの並び（配列）を入れ子にしたもの。投げた例外は報告の`contributorErrors`に書き、ほかは続ける |
| `IDexcQASink.OnReportWritten(info)` | 書いた後。課題管理などへの送信はここでゲームが行う。投げた例外は警告にして飛ばす |

- QAは自分のエラーをErrorのログに書かない。書くと報告に戻ってくるためである。問題は報告の中か、Warningに書く

## 範囲外

- 人の報告の画面（Dexcメニューのパネル）。後で足すときも`Report`の上に置く
- 動画、入力の記録と再生
- 外部への送信の実装
- リリース版での報告
