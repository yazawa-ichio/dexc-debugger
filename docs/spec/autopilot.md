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
| `load:<vm>` | Botを読み込む |
| `eval:<vm>` | スニペットを実行し、VMを破棄する |
| `eval-keep:<vm>` | スニペットを実行し、VMを120秒残す |

- 接続はThreadPoolで受け、実行はメインスレッドの`Pump`で行う
- 要求は届いた順に1つずつ実行する。同時に届いても、ゲームの状態へ並行に触らない

### status

`loadCount`、`active`、`activeState`、`steps`、`switches`、`failure`、`missing`、`log`を返す。ゲーム固有の項目は`StatusRequested`で足す。`log`は直近40件とする。

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
