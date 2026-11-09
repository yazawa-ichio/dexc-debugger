# Autopilot仕様

IL2CPPのPlayerの中で、外から渡したC#を実行する機能の取り決めを定める。使い方は[マニュアル](../manual/autopilot.md)に置く。

## 目的

- コーディングエージェントが書いた普通のC#が、実際のゲームの型のまま動く
- 文字列でメンバーを探すAPIを作らない。コードは型付きで書き、誤りはコンパイル時に分かる
- evalと、ステートマシンのBotを同じ基盤で動かす
- 入力はInput Systemの独自デバイスを通し、プレイヤーの入力と同じ経路を通す

対象の主体はIL2CPPとする。

## 層

| 層 | 置き場所 | 責務 | 知ってはならないもの |
| --- | --- | --- | --- |
| Runtime | `Autopilot/Runtime` | ステートマシン、Blackboard、受け口 | VMの実装、ゲーム |
| Backend | `Autopilot/Backends` | アセンブリの読み込みと呼び出し | ステートマシン、ゲーム |
| ゲーム用アセンブリ | プロジェクト側 | Context、仮想デバイス、起動処理 | VMの内部 |
| Bot | プロジェクト側の別csproj | 目的の進行 | 受け口、VM |
| ツール | `Autopilot/Tools~`、`tools/unity-tests` | コンパイル、参照の生成、Playerの操作 | Player内の状態 |

BackendはRuntimeへ`IAutopilotBackend`と`IAutopilotModule`だけを見せる。

## VM

| VM | 名前 | 位置付け |
| --- | --- | --- |
| ILRuntime | `ilruntime` | 既定。普通のC#を動かす |
| Pipelineの`IlInterpreter` | `pipeline` | 代替。動かせないコードを事前に止める |

### ILRuntime

- `Autopilot/ThirdParty/ILRuntime`へソースで同梱する。元のコミットとパッチは同じ場所の`README.md`に記録する
- 元のソースへの変更は、`Patches~`の番号付きパッチだけで行う
- Botとスニペットは最適化なしでコンパイルする。最適化すると`async`のステートマシンが構造体になり、中断した状態を失う

### Pipeline

- `com.unity.pipeline`のソースをコピーしない。内部APIへは`.asmref`で届く
- `Unity.Pipeline`、Roslyn、`Dexc.Remote.Pipeline`はPlayerに入れない

## 受け口

`AutopilotServer`はloopbackのTCPで待ち受ける。起動引数`-dexc-autopilot-token`があるときだけ開く。受け取るものがコードのため、トークンは省略できない。

| 起動引数 | 内容 | 既定 |
| --- | --- | --- |
| `-dexc-autopilot-token` | 要求に必須の文字列 | なし（開かない） |
| `-dexc-autopilot-port` | 待ち受けポート | `7890` |

要求は1接続1行で、応答はJSON1行とする。

```text
<token> <operation> [base64のアセンブリ]
```

トークンが一致しない接続には何も返さず閉じる。

| 操作 | 内容 |
| --- | --- |
| `status` | Botとゲームの状態を返す |
| `graph` | 読み込んだBotのマシン、状態、遷移先を返す |
| `answer:<id>:<human\|agent>` | 判断点に答える。答えはbase64で付ける |
| `load:<vm>` | Botを読み込む |
| `eval:<vm>` | スニペットを実行し、VMを破棄する |
| `eval-keep:<vm>` | スニペットを実行し、VMを120秒残す |

- 接続はThreadPoolで受け、実行はメインスレッドの`Pump`で行う
- 要求は届いた順に1つずつ実行する。同時に届いても、ゲームの状態へ並行に触らない

### status

`loadCount`、`active`、`activeState`、`steps`、`switches`、`failure`、`missing`、`log`、`decisions`を返す。ゲーム固有の項目は`StatusRequested`で足す。`log`は直近40件とする。

### graph

マシンごとに`name`、`when`（条件の有無）、`dwell`、`restartOnEnter`、`first`、`current`、`states`を返す。`current`は、割り込まれたマシンが戻る状態でもある。状態は`name`と`goto`（遷移先の名前の並び、宣言順）を持つ。条件はdelegateなので、名前を持たず返さない。ほかに`loadCount`、`active`、`selectInterval`を返す。

### 判断点

- Botは`AutopilotContext.Ask(question, choices)`で、見ている人に聞く。返す`AutopilotDecision`は待たずに返り、Botは`IsAnswered`を`Step`で見る
- 人が期限内に答えなければ、ワークスペースを操作しているエージェントが答える。期限と回し方はホストの道具が決める。Playerは答えた側（`human`か`agent`）を記録するだけである
- 答えを待つ間と、待ちすぎたときの振る舞いはBotが`Elapsed`で決める。ライブラリは方針を持たない
- 聞いた状態が`Exit`したとき、Botの読み直し、Botの失敗で取り下げる（`IsWithdrawn`）。`Enter`で聞いてStateのBlackboardに持つ形を前提とする。`Step`で聞けば毎回聞き直す
- `status`の`decisions`は、答えを待つものの`id`、`question`、`choices`、`machine`、`state`、`elapsed`を返す
- 選択肢があるときは、そのどれかでない答えを断る。答えは1回だけ受ける

## eval

- スニペットごとにVMを作る。実行中のBotとVMを共有しない
- 型は`DexcEvalSnippet`、メソッドは`Eval`とする
- 応答は`ok`、`value`、`type`、`loadMs`、`runMs`を返す。失敗時は`error`と`trace`を返す
- `value`は文字列にして返す。生きたobjectは外へ出さない
- `UnboundMembers`は応答の`missing`で返す。実行するかどうかはVMが決める。[メンバーの不足](#メンバーの不足)を参照する

VMを分けることを、異常からの復旧の手段とする。壊れたスニペットの影響は、そのVMの中で止まる。

## ステートマシン

### マシンの選択

- 先に宣言したマシンが優先する
- `SelectInterval`の間隔で、条件が成り立つ先頭のマシンを選ぶ。既定の`0`は毎フレームとする
- 実行中のマシンの開始から`Dwell`の時間が過ぎるまでは、選び直さない
- 条件が成り立つマシンがなければ、どのマシンも実行しない

### 切り替え

- 切り替えでは、実行中の状態の`Exit`を呼ぶ
- 割り込まれたマシンは今の状態を保ち、次に選ばれたときにその状態の`Enter`から再開する
- `RestartOnEnter`のマシンは、切り替わるたびに`Machine`と`State`のBlackboardを消し、最初の状態から始める

### 1回の実行

1. 必要ならマシンを選ぶ
2. `Goto`の条件を宣言順に調べ、最初に成り立ったものへ移る
3. 今の状態の`Step`を呼ぶ

状態の遷移では、`Exit`、`State`のBlackboardの消去、`Enter`の順に行う。

### Blackboard

| 段 | 親 | 残る範囲 |
| --- | --- | --- |
| `Shared` | なし | Runnerが生きている間 |
| `Machine` | `Shared` | マシン名ごと。割り込みと読み直しの後も残る |
| `State` | `Machine` | 別の状態へ移るまで。割り込みでは残る |

読み取りは親へさかのぼる。書き込みはその段にだけ行う。

### Botの読み直し

- BotのDelegateは作ったVMと一緒に失われるため、実行中のマシンを書き換えない。すべて破棄して新しいアセンブリに宣言させる
- Blackboardは読み直しの後も残す
- 同じ名前のマシンに同じ名前の状態があれば、その状態を引き継ぐ

### 失敗

Botの例外はBotを止め、`failure`へ記録する。ゲームは止めない。

## ビルド構成

| 構成 | 内容 |
| --- | --- |
| `none` | Autopilotを含めない |
| `ilruntime` | ILRuntimeだけを含める。既定 |
| `both` | ILRuntimeとPipelineの`IlInterpreter`を含める。`ENABLE_RUNTIME_PIPELINE`を定義する |

- `ilruntime`と`both`は`DEXC_AUTOPILOT`を定義する
- 検証用のビルドは、非development、Managed Stripping High、C++のReleaseとする
- `link.xml`はビルド時に生成する。保持はAutopilot、`AutopilotKeepAssembly`で宣言したアセンブリと宣言した側のアセンブリ、VMが実行時に必要とする型に絞り、すべてを保持しない
- `link.xml`の生成は、そのビルドがコンパイルしたアセンブリに`Dexc.Autopilot`があるかで決める。ビルドの方法には依存しない

ライブラリはビルドの手順を持たない。`DEXC_AUTOPILOT`を定義したビルドであれば、どこで作ったPlayerでも扱う。

## 参照

既定の参照は、PlayerのIL2CPPビルドが残したストリップ済みのアセンブリとする。Player本体の隣の`*_BackUpThisFolder_ButDontShipItWithYourGame/Managed`にある。

- Playerに残っていないAPIは、コンパイルエラーになる
- 参照は`Publicize`で`private`を公開する。Botは製品の`private`メンバーを型付きで使える
- ワークスペースでは、ビルド前のDLLやcsprojで参照を置き換えられる。差はコンパイル時に分からなくなるので、[メンバーの不足](#メンバーの不足)で扱う

## コンパイル

| 経路 | 使う条件 |
| --- | --- |
| 常駐コンパイラ | ポート`7891`が応答するとき |
| `csc`のコンパイラサーバー | 上記が使えないとき |

どちらの経路も、VMが同じなら同じガードレールを適用する。

常駐コンパイラは、参照と`Compilation`を要求の間で保持する。要求は1つずつ処理する。`refs.rsp`の更新を検知したら参照を読み直す。Botはファイルごとの構文木を保持し、更新されたファイルだけ解析し直す。

常駐コンパイラは、最後の要求から30分で終了する。`--watch`のときは終了しない。

参照は`refs.rsp`ごとに保持する。要求は`refs.rsp`、送り先のポート、トークンを指定でき、省略すると既定の1組を使う。

## ワークスペース

複数のビルドを同時に動かして比べる単位とする。

- 入力はビルド済みのPlayerとする。ビルドの手順、ブランチ、成果物の取得方法を持たない
- `workspace.json`がビルドの一覧を持つ。Playerはコピーしない
- トークン、ポート、PID、参照はビルドごとに持つ。ポートは空きを割り当てる
- 操作は全ビルドへ並行に行い、結果をビルドごとに返す。1つの失敗は他のビルドを止めない
- 各Playerは別々の時間で動く。フレーム単位では揃えない

Botは`Common`と`Builds/<name>`を合わせ、ビルドごとにそのビルドの参照で1つのアセンブリへコンパイルする。同じ相対パスのファイルは`Builds/<name>`を使う。

| 参照 | 内容 | 差の検出 |
| --- | --- | --- |
| `managed` | Playerのストリップ済みアセンブリ | コンパイル時 |
| `dlls` | 上に指定のフォルダーのDLLを重ねる | 読み込み時 |
| `csproj` | 上にcsprojの`HintPath`とビルド結果を重ねる | 読み込み時 |

`dlls`は`--editor`を必須とする。ビルド前のDLLは`netstandard`を参照するが、Playerの`Managed`には無い。`Managed`が無いPlayerは、`--editor`でEditorのエンジンとクラスライブラリを土台にする。

### テレメトリー

- Dexc Telemetryへの依存は持たない。Playerの起動の引数と、evalのスニペットだけでつなぐ。パッケージの無いPlayerは引数を読まず、evalがコンパイルエラーになる
- `ws start`は`-dexc-telemetry-directory .state/<name>/telemetry`と、`-dexc-telemetry-resource`で`dexc.build`、`vcs.ref.head.revision`、`dexc.seed`を渡す。同じ製品のビルドは`persistentDataPath`を共有するので、フォルダーを分ける
- `ws telemetry start|capture|pull|stop`は、evalで`DexcTelemetry`を呼ぶ。`pull`は`Seal`の後、閉じたファイル（`.otlp`と`capture-*.raw`）を`Telemetry/<name>`へ移す。テレメトリーのPullの規格に従う
- link.xmlは、ビルドに`Dexc.Telemetry`があれば丸ごと残す。evalからだけ呼ぶAPIがストリップされないためである
- `ws telemetry start --upload`は、ダッシュボードが動いているときに、書き出しと送信を5秒ごとにして、`DexcTelemetry.StartUpload`でダッシュボードへ送らせる

### 不具合

- Dexc QAへの依存は持たない。`ws start`が`-dexc-qa-directory .state/<name>/qa`を渡し、パッケージのあるPlayerはそれで自分で始まる。link.xmlは、ビルドに`Dexc.QA`があれば丸ごと残す。BotとevalがQAのAPIを呼ぶためである（[QAの仕様](qa.md)）
- 集めるのにevalを使わない。Playerが自分のフォルダーに報告と鍵ごとの回数を書くので、それを読む
- `ws qa pull`は、まとまった報告を`QA/reports/<name>/`へ移し、回数のファイルを写して、`QA/defects.json`の不具合を新しくする。スクリーンショットを撮り終えていない報告は、Playerが動いている間は移さない
- 不具合は鍵でまとめる。idは`D0001`から振る。回数はビルドと実行ごとに持ち、同じ実行は置き換え、実行どうしは足す。実行は回数を0から数えるためである。詳しい報告は10件まで紐づける
- 状態は`open`、`fixing`、`fixed`、`wontfix`とする。`ws qa set <id> <state> --note`とダッシュボードで変え、変更は不具合の`notes`に残す。`fixed`にした後の時刻にまた起きたら`open`に戻し、そう記録する
- `QA/defects.json`は`ws`とダッシュボードの両方が書くので、`QA/.lock`を取ってから読み書きする

### ダッシュボード

`Autopilot/Tools~/Dashboard`の.NET 10の実行ファイルとする。`ws`のコマンドに含めないのは、Compilerの起動を軽く保ち、ASP.NET CoreとprotobufをCompilerに持ち込まないためである。ワークスペースの読み方はCompilerのコードを使う。

| 層 | 責務 |
| --- | --- |
| データ | 各Playerの`status`を間隔ごとに取り、`loadCount`が変わったら`graph`を取る。質問と指示の列、OTLPの受け取り |
| View | 組み込みのページと、ワークスペースの`Dashboard/`のページ。データの層とはHTTPのAPIとSSEだけでつながる |

- Playerを起動しない。`ws start`で起動したビルドを見る
- 1つのビルドへの要求は1つずつ行い、前の要求が終わるまで次を出さない
- 待ち受けは127.0.0.1だけにする。Hostが`127.0.0.1`か`localhost`でない要求は421で断る。DNS rebindingで、ほかのサイトから届かないようにするためである
- 起動ごとにトークンを作り、変更を伴う要求（GETとHEAD以外）には`X-Dexc-Token`を求める。ほかのサイトからのPOSTで、エージェントへ指示を押し込めないようにするためである。トークンとアドレスは`.state/.dashboard/endpoint.json`に書き、終了時に消す
- 例外はOTLPの`/v1/`とする。Playerはダッシュボードの起動し直しをまたいで送り続け、そのたびにトークンが変わるためである。ここに届きうるのは数値であって、指示ではない
- evalはAPIに出さない

### 質問の中継

1. `status`の`decisions`に新しい質問が現れたら、人へ出す。同じidでも質問の文が違えば、別の質問とする（Playerを起動し直すと、idは`d1`から数え直すため）
2. `--human-timeout`（既定60秒）を過ぎたら、エージェントの列へ入れる
3. 答えはPlayerの`answer`で送り、Playerが受けたら答えたものとする。`status`から答えなしで消えた質問は、取り下げとする
4. エージェントの列から取り出すときに、答えたものと取り下げたものは飛ばす

- エージェントへはロングポールで渡す（`POST /api/agent/next?timeout=`、既定90秒、上限300秒）。取り出しで1件が渡るので、POSTにしてトークンを求める
- MCPのelicitationは、サーバーからクライアントの利用者へ聞く仕組みで、エージェントへ指示を流す用途に合わない。Claude CodeのChannelsはresearch previewのため使わない。必要になれば、このAPIの上に足す
- 経緯（質問、回した、渡した、答えた、取り下げた、指示、エージェントの発言）は`.state/.dashboard/journal.jsonl`に追記し、起動時に直近200件を読む

### 不具合の画面と通知

- ダッシュボードは5秒ごとに`ws qa pull`と同じ収集を行う。新しい不具合（`found`）と、直した後にまた起きた不具合（`recurred`）は、エージェントの列へ`kind: defect`として入れ、journalに残す
- 「Defects」のページは、状態で絞った一覧と、ビルドごとの回数、経緯、直近の詳しい報告（スクリーンショット、詳細、注釈、場所、resource、ログ）を見せる。人は状態を変えられる
- `GET /api/qa/defects?state=`、`/api/qa/report?build=&id=`、`/api/qa/file?build=&id=&name=`、`POST /api/qa/state`。ファイルの名前は単なる名前だけを受け、報告のフォルダーの外は配らない

### 性能の受け取り

- `POST /v1/metrics|traces|logs`でOTLP/HTTPのprotobufを受ける。gzipにも対応する。`.proto`は`Telemetry/Tools~/proto`のものを使う
- Resourceの`dexc.build`でビルドに分け、無ければ`service.name`を使う。指標は名前と属性ごとに2000点、spanとlogsは500件までメモリに持つ
- ヒストグラムは、1回の書き出しの平均（`sum / count`）と`max`で描く
- 系列は名前と属性の組で分ける。ヒストグラムはバケットも持ち、時刻の範囲の点のバケットを足し合わせて、p50、p95、p99と、遅いフレーム（16.7ms超）と止まったフレーム（700ms超）の割合を出す（`/api/perf/summary`）。分位数はバケットの中を線形に補い、最初と最後のバケットは最小値と最大値で閉じる（Prometheusと同じ）。割合は、その値から始まるバケットで数えるので、境界にその値が要る。Dexc Telemetryのフレーム時間の境界は16.7と700を持つ
- ロード（span `dexc.load`）とヒッチ（イベント`dexc.hitch`）は、直近のspanとlogsとは別に、ビルドごとに500件まで持つ。忙しいゲームのspanに押し出されないようにするためである。ロードの段階は、受けたspanのうち親がそのロードのものを、問い合わせのときに集める
- `ws telemetry start --upload`はlogsも送らせる。ヒッチのイベントがlogsで届くためである
- `--forward`があれば、受けた本文をそのまま送る。失敗しても受け取りは成り立たせ、標準エラーに書く

## ガードレール

### Pipeline

Pipelineの`IlInterpreter`で動かないコードは、3段階で止める。黙って誤動作させない。

1. コンパイル時: Analyzer（`Tools~/Guard/Analyzer`）がエラーにする
2. コンパイル後: Patchツール（`Tools~/Guard/Patch`）がホストのenumの比較を`int`へ書き換える
3. 読み込み時: `PipelineBackend`が解決できないメンバーを報告し、実行を拒否する

| ルール | 対象 |
| --- | --- |
| DEXC001 | `try/catch` |
| DEXC002 | `async`／`await` |
| DEXC003 | `ref`／`out`引数の宣言 |
| DEXC004 | ジェネリックの宣言 |
| DEXC005 | ホストの構造体の引数 |
| DEXC006 | LINQ |
| DEXC007 | `object`から値型へのキャスト |
| DEXC008 | ホストのenumの`Equals` |
| DEXC009 | `IReadOnly*`のコレクション |
| DEXC010 | 解釈実行のイテレーターの駆動 |
| DEXC011 | `lock`、`checked`、`decimal`、`dynamic`、多次元配列、refローカル、`stackalloc` |
| DEXC012 | `typeof`、Reflection、スレッドの型 |
| DEXC013 | `.Result`、`Wait`、`GetResult`のブロッキング待ち |
| DEXC014 | BCLの型に対する型判定 |
| DEXC015 | ローカル関数や`IEnumerator`以外の`yield` |

### ILRuntime

`ilruntime.globalconfig`により、DEXC013だけを警告とし、他は無効にする。完了済みのTaskを待つ用途があるため、エラーにしない。

### メンバーの不足

別の参照でコンパイルしたコードは、Playerに無いメンバーを含むことがある。

| VM | 動作 |
| --- | --- |
| ILRuntime | 読み込み時に全メソッドを調べて`missing`で返し、実行する。届いたら例外にする |
| Pipeline | `missing`で返し、実行を拒否する |

ILRuntimeの対象は、メソッド、型、フィールド、戻り値とフィールドの型の違いとする。ジェネリックの定義は実体が無いので調べず、届いたときに例外にする。黙って別の値を返さない。

## 実行の制限

ILRuntimeは、1回の呼び出しの制限を超えたら例外で打ち切る。

| 制限 | 値 |
| --- | --- |
| 命令数 | 5,000万 |
| ネイティブスタック | 512KB |

Pipelineも無限ループを打ち切る。

## 復旧

| 障害 | 範囲 | 復旧 |
| --- | --- | --- |
| スニペットの例外、打ち切り | そのVM | 応答に`error`を返す |
| Botの例外、打ち切り | Bot | `failure`へ記録し、再度`load`する |
| Playerの異常終了、応答なし | Player | `ensure`で起動し直す |

Blackboardはプロセスの中にあるため、Playerの起動し直しで失われる。

## 範囲外

- 外部のマシンからの接続
- 状態の保存と復元。必要な状態はevalとBlackboardで組み立てる
