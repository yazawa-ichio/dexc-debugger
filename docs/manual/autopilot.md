# Autopilot

IL2CPPのPlayerの中で、C#のスニペットとBotを動かす機能です。エージェントやテストが、ゲームの実際の型を使ってゲームを調べ、操作します。

コードは普通のC#で書きます。製品の`private`メンバーも型付きで参照でき、文字列でメンバーを探すAPIはありません。

## 全体の形

| 部品 | 置き場所 | 役割 |
| --- | --- | --- |
| パッケージ | `Autopilot/` | ステートマシン、VM、Playerの受け口 |
| ゲーム用アセンブリ | 例: `Assets/Samples/StealthStageDemo/Autopilot/` | Context、Input Systemの仮想デバイス、起動処理 |
| Botプロジェクト | 例: `Assets/Samples/StealthStageDemo/Bot~/StealthBot/` | ゲームを進めるステートマシン。別のcsprojでコンパイルします |
| 常駐コンパイラ | `Autopilot/Tools~/Compiler/` | スニペットとBotを短時間でコンパイルします |
| 操作用スクリプト | `tools/unity-tests/` | Playerのビルド、起動、eval |

Botの操作は、仮想のGamepadを通してプレイヤーの入力と同じ経路でゲームに届きます。ゲーム側に受け口を足す必要はありません。

## ゲームへの組み込み

`Packages/manifest.json`へパッケージを追加します。

```json
"jp.yazawa-ichio.dexc-autopilot": "file:../Autopilot"
```

ゲーム用のasmdefを作り、`Dexc.Autopilot`、`Dexc.Autopilot.ILRuntime`、`Dexc.Autopilot.Backend.ILRuntime`、`Unity.InputSystem`を参照します。`defineConstraints`には次を指定し、通常のビルドに入らないようにします。

```json
"defineConstraints": ["!DISABLE_DEXC_DEBUGGER", "UNITY_EDITOR || DEXC_AUTOPILOT"]
```

Botが`private`メンバーを使うアセンブリを、ゲーム用のアセンブリで宣言します。Autopilotを含むビルドだけ、ストリップの対象から外れます。

```csharp
[assembly: AutopilotKeepAssembly("MyGame.Gameplay")]
```

Contextに、Botへ渡すゲームの入口と操作を書きます。

```csharp
public sealed class MyContext : AutopilotContext
{
	internal Vector2 MoveValue;

	public MyGame Game => MyGame.Instance;

	public void Move(Vector2 value) => MoveValue = value;
}
```

`MonoBehaviour`から、毎フレーム受け口とBotを動かします。

```csharp
void Awake()
{
	m_Runner = new AutopilotRunner<MyContext>(m_Context, "MyBot.Entry", new ILRuntimeBackend(delegates =>
	{
		delegates.RegisterFunctionDelegate<MyContext, bool>();
		delegates.RegisterMethodDelegate<MyContext>();
	}));
	m_Server = AutopilotServer.TryStart(m_Runner);
}

void Update()
{
	m_Server?.Pump();
	if (m_Runner.Tick())
		m_Gamepad.Write(m_Context.MoveValue);
	else
		m_Gamepad.Write(Vector2.zero);
}
```

`RegisterFunctionDelegate`と`RegisterMethodDelegate`は、BotからホストへDelegateを渡す型ごとに必要です。

仮想デバイスの作り方は`StealthAutopilotGamepad.cs`を参照してください。`Gamepad`を継承し、`InputSystem.QueueStateEvent`で状態を送ります。

## Playerの準備

ビルドの方法は問いません。手元でもCIでも、次の条件を満たしたPlayerを使えます。

- `DEXC_AUTOPILOT`を定義してビルドする
- Player本体と、`*_BackUpThisFolder_ButDontShipItWithYourGame/Managed`を残す。[ワークスペース](#ワークスペース)の`managed`がこのフォルダーを参照に使います

サンプルは次のスクリプトでビルドして動かせます。

```sh
sh tools/unity-tests/run-autopilot-player.sh build
sh tools/unity-tests/run-autopilot-player.sh refs
sh tools/unity-tests/run-autopilot-player.sh start
```

| コマンド | 内容 |
| --- | --- |
| `build` | IL2CPPのPlayerをビルドします |
| `refs` | Playerに残ったアセンブリから、コンパイル用の参照を作ります |
| `start`／`stop` | Playerを起動、停止します |
| `ensure` | 応答がなければ、Playerを起動し直します |
| `status` | Botとゲームの状態を表示します |
| `load <bot.dll>` | Botを読み込みます |

| 環境変数 | 内容 | 既定 |
| --- | --- | --- |
| `DEXC_AUTOPILOT_CONFIGURATION` | ビルド構成。`none`、`ilruntime`、`both` | `ilruntime` |
| `DEXC_AUTOPILOT_VM` | `load`で使うVM | `ilruntime` |
| `DEXC_AUTOPILOT_PORT` | Playerの待ち受けポート | `7890` |
| `DEXC_AUTOPILOT_HEADLESS` | `1`でウィンドウなしで起動します | `0` |

Playerを再ビルドしたら、`refs`も実行します。

## eval

実行中のPlayerでC#を実行し、結果を受け取ります。

```sh
python3 tools/unity-tests/dexc-eval.py "StealthStageDemoController.Instance.DistanceToExit"
python3 tools/unity-tests/dexc-eval.py "var game = StealthStageDemoController.Instance; return game.m_Guards.Count;"
python3 tools/unity-tests/dexc-eval.py --file checks.txt
```

- セミコロンがなければ式として評価し、その値を返します。
- セミコロンがあれば文として実行します。値が欲しいときは`return`を書きます。
- `--file`は、`---`だけの行で区切った複数のスニペットを順に実行します。
- 型を宣言するときは、宣言と本体の間に`@@`だけの行を置きます。

```csharp
class Counter { public int Value; }
@@
var counter = new Counter { Value = 2 };
return counter.Value * 21;
```

`DEXC_EVAL_VM`でVMを選びます。

| 値 | 内容 |
| --- | --- |
| `ilruntime` | 既定。実行が終わるとVMを破棄します |
| `ilruntime-keep` | 返った後もVMを120秒残します。`await`の続きを動かすスニペットで使います |
| `pipeline` | Pipeline版のVMで実行します。`both`構成のPlayerが必要です |

eval同士やBotとの間で、変数は共有されません。続きの処理に値を渡すときは、ゲーム側の状態やBlackboardを使います。

Playerに残っていないAPIは、実行時ではなくコンパイルエラーになります。

別のビルドの参照でコンパイルしたスニペットでは、Playerに無いメンバーは応答の`missing`に一覧で返ります。そのメンバーに届くと例外で止まり、届かなければそのまま動きます。

## Bot

Botのエントリーは、`Configure`を持つ静的クラスです。先に宣言したマシンほど優先されます。

```csharp
public static class Entry
{
	public static void Configure(AutopilotBuilder<MyContext> builder)
	{
		builder.Machine("Paused").When(context => context.Game.IsPaused).Step(context => context.Stop());
		builder.Machine("Battle").When(context => context.Game.InBattle).Dwell(1f).Step(Fight);
		DeclareQuest(builder.Machine("Quest").When(context => true));
	}

	static void DeclareQuest(AutopilotMachine<MyContext> machine)
	{
		machine.State("ToTown")
			.Step(context => context.Move(context.Game.TownDirection))
			.Goto("Talk", context => context.Game.InTown);
		machine.State("Talk")
			.Enter(context => context.Shared.SetInt("visits", context.Shared.GetInt("visits") + 1))
			.Step(Talk);
	}
}
```

| API | 内容 |
| --- | --- |
| `builder.SelectInterval` | 実行するマシンを選び直す間隔（秒）。`0`は毎フレームです |
| `When` | そのマシンを実行したい条件 |
| `Dwell` | 実行を始めてから、切り替えを待つ最短の時間 |
| `RestartOnEnter` | 切り替わるたびに最初の状態から始めます |
| `Step` | 1状態だけのマシンを作ります |
| `State` | 状態を宣言します。最初に宣言した状態から始まります |
| `Enter`／`Step`／`Exit` | 状態に入るとき、毎フレーム、出るときの処理 |
| `Goto` | 条件が成り立ったら同じマシンの別の状態へ移ります。先に書いたものが優先です |

割り込まれたマシンは、次に選ばれたときに続きの状態から再開します。

値の受け渡しには、寿命の違う3つのBlackboardを使います。

| Blackboard | 残る範囲 |
| --- | --- |
| `context.Shared` | Runnerと同じ。Botを読み直しても残ります |
| `context.Machine` | マシンごと。割り込みやBotの読み直しの後も残ります |
| `context.State` | 状態ごと。別の状態へ移ると消えます。割り込みでは残ります |

`State`から読むと`Machine`、`Shared`の順にさかのぼって探します。

Botを読み直すと、マシンは新しいコードに置き換わります。同じ名前の状態があれば、その状態から続きます。Botで例外が起きるとBotだけを止めます。ゲームは動き続け、内容は`status`の`failure`に出ます。

### Botのコンパイル

csprojはIDEの補完に使えます。コンパイルは最適化なしで行います。最適化すると、ILRuntimeで`await`の状態が失われます。

## 常駐コンパイラ

コンパイラをプロセスとして残し、読み込み済みの参照を使い回します。`dexc-eval.py`は、常駐コンパイラが動いていれば自動で使います。動いていなければ`csc`でコンパイルします。

```sh
dotnet build "Autopilot/Tools~/Compiler/Dexc.Autopilot.Compiler.csproj" -c Release
```

| コマンド | 内容 |
| --- | --- |
| `serve` | 常駐コンパイラを起動します |
| `serve --watch` | Botの保存を検知し、コンパイルしてPlayerへ読み込みます |
| `eval "<snippet>"` | スニペットをコンパイルし、Playerで実行します |
| `bot --load` | Botをコンパイルし、Playerへ読み込みます |
| `status`／`stop` | 状態の確認、停止 |

オプションは`--vm pipeline`、`--keep`、`--compile-only`、`--out <file>`です。待ち受けポートは`7891`で、`DEXC_COMPILER_PORT`で変更できます。

Playerを再ビルドして`refs`を実行すると、参照は自動で読み直します。

最後の要求から30分たつと、常駐コンパイラは自動で終了します。`serve --watch`で起動したときは終了しません。

## ワークスペース

複数のビルドを並べて動かし、同じBotやスニペットを同時に試します。バグ修正ごとのビルドを比べるときに使います。

コマンドは常駐コンパイラの`ws`です。ビルド済みのPlayerを登録して使うので、どこでビルドしたかは問いません。

```sh
Dexc.Autopilot.Compiler ws init D:/work/stealth --using UnityEngine,MyGame --publicize MyGame.Gameplay
cd D:/work/stealth
Dexc.Autopilot.Compiler ws add fix-1234 D:/ci/1234/Player
Dexc.Autopilot.Compiler ws add fix-1250 D:/ci/1250/Player
Dexc.Autopilot.Compiler ws start
Dexc.Autopilot.Compiler ws bot --load
Dexc.Autopilot.Compiler ws eval "MyGame.Instance.Score"
```

結果はビルドごとに1行で返ります。1つが失敗しても、他のビルドには影響しません。

| コマンド | 内容 |
| --- | --- |
| `ws init <dir>` | ワークスペースを作ります。`--using`はスニペットの`using`、`--publicize`は`private`を使うアセンブリです |
| `ws add <name> <player>` | ビルドを登録し、参照を作ります。Playerはコピーしません。`--commit <sha>`で、テレメトリーに載せるコミットを記録します |
| `ws remove <name>` | 登録を消します |
| `ws refs` | 参照を作り直します |
| `ws start`／`ws stop` | Playerを起動、停止します。`--headless`でウィンドウなしで起動します。`--seed <n>`で、テレメトリーに載せるシードを渡します |
| `ws status` | 各Playerの状態を表示します |
| `ws bot --load` | Botをビルドごとにコンパイルし、読み込みます |
| `ws eval "<snippet>"` | 動いている全ビルドでスニペットを実行します |
| `ws watch` | 保存を検知して読み込みます。`Common`の保存は全ビルドへ、`Builds/<name>`の保存はそのビルドへ送ります |
| `ws telemetry <command>` | 各ビルドのDexc Telemetryを操作します。[テレメトリー](#テレメトリー)を参照してください |
| `ws agent <command>` | ダッシュボードの、エージェントの側です。[ダッシュボード](#ダッシュボード)を参照してください |
| `ws qa pull` | Dexc QAの報告を集め、不具合（`QA/defects.json`）にまとめます。新しい不具合と、直した後にまた起きた不具合を表示します |
| `ws qa list [--state open]`／`ws qa show <id>`／`ws qa set <id> <state> [--note text]` | 不具合を見て、状態（`open`、`fixing`、`fixed`、`wontfix`）を変えます |

`--build a,b`で対象のビルドを絞れます。`workspace.json`のある場所の外から使うときは、`DEXC_WORKSPACE`で指定します。

ワークスペースは、ブランチの切り替えで消えない場所に置きます。

```text
workspace.json
Common/              全ビルドで共通のBot
Builds/fix-1234/     そのビルド専用のBot
.state/              参照、トークン、ポート、ログ、テレメトリーの書きかけ、ダッシュボードの経緯
Telemetry/fix-1234/  集めたテレメトリーのファイル
Dashboard/           プロジェクトのページ
```

### テレメトリー

Dexc Telemetryのパッケージがあるビルドでは、テレメトリーをevalで操作します。ゲームにテレメトリーのコードは要りません。

```sh
Dexc.Autopilot.Compiler ws start --seed 42
Dexc.Autopilot.Compiler ws telemetry start --watch MyGame.Stage.*
Dexc.Autopilot.Compiler ws telemetry capture 300 --profiler
Dexc.Autopilot.Compiler ws telemetry pull
```

| コマンド | 内容 |
| --- | --- |
| `ws telemetry start` | 動いていなければ`DexcTelemetry.Start()`を呼びます。`--watch a,b`で`DexcTelemetry.Watch`も呼びます。`--upload`で、5秒ごとにダッシュボードへ送らせます |
| `ws telemetry capture <frames>` | 全ビルドで同時にキャプチャを始めます。`--profiler`で、Development BuildならUnity Profilerの`.raw`も書きます |
| `ws telemetry pull` | `DexcTelemetry.Seal()`で今のファイルを閉じさせ、閉じたファイルを`Telemetry/<name>`へ移します |
| `ws telemetry stop` | `DexcTelemetry.Stop()`を呼び、ファイルを移します |

- `ws start`は、ビルドごとに`.state/<name>/telemetry`を書き出し先にし、`dexc.build`、`vcs.ref.head.revision`（`--commit`）、`dexc.seed`（`--seed`）をResourceに載せるよう、Playerの引数で渡します。同じ製品の複数のビルドが同じフォルダーに書かないようにするためです
- 止まったPlayerの`pull`は、閉じたファイルだけを移します
- 集めたファイルは、`Telemetry/Tools~/Cli`の`dump`、`captures`、`cut`、`send`で読み、送ります
- `capture`のフレーム数は、オプションより前に書きます

### ダッシュボード

ワークスペースのビルドを、ブラウザーで見ます。ビルドの状態、マシンの図、性能のグラフ、Botの質問、エージェントへの指示を扱います。

```sh
cd D:/work/stealth
dotnet run --project <repo>/Autopilot/Tools~/Dashboard -c Release
```

表示されるアドレス（`http://127.0.0.1:7892/?token=...`）を開きます。トークンはダッシュボードを起動するたびに変わります。

| オプション | 内容 | 既定 |
| --- | --- | --- |
| `--port` | 待ち受けのポート | `7892` |
| `--interval` | 各Playerの`status`を取る間隔（秒） | `1` |
| `--human-timeout` | Botの質問を人が答えずにいたら、エージェントへ回すまでの秒数 | `60` |
| `--forward` | 受けたOTLPを、そのまま送る先（collectorや`grafana/otel-lgtm`） | なし |

ページは次のとおりです。

| ページ | 内容 |
| --- | --- |
| Builds | ビルドごとの状態、`log`、`failure`。答えを待つ質問、エージェントへの指示、経緯 |
| Machines | マシンの状態と遷移の図。今の状態を塗り、割り込まれたマシンが戻る状態を枠で示します |
| Defects | Dexc QAが見つけた不具合。ビルドごとの回数、経緯、スクリーンショットとログ。状態を変えられます |
| Performance | 分布の表（ビルドと注釈ごと。最初のビルドよりp95が1割以上悪い行に印）、ロードの表、ヒッチの一覧、指標の線グラフ。`ws telemetry start --upload`で送らせたものです |

#### Botの質問

Botは`context.Ask`で、見ている人に聞きます。待たずに返るので、`Enter`で聞いてStateのBlackboardに持ち、`Step`や`Goto`で答えを見ます。

```csharp
machine.State("Fork")
	.Enter(context => context.State.Set("way", context.Ask("Which way?", "left", "right")))
	.Goto("Left", context => ((AutopilotDecision)context.State.Get("way")).Answer == "left");
```

- 人が`--human-timeout`の間に答えなければ、エージェントへ回ります
- 聞いた状態を出ると、質問は取り下げられます
- 答えを待ちすぎたときにどうするかは、Botが`Elapsed`を見て決めます

#### エージェントの側

ワークスペースを操作するエージェントは、`ws agent wait`を繰り返して、回ってきた質問と人からの指示を1つずつ受けます。

```sh
Dexc.Autopilot.Compiler ws agent wait              # 次の1件をJSONで出します。90秒で何も無ければ終了コード3
Dexc.Autopilot.Compiler ws agent answer a d1 left  # ビルドaの質問d1に、エージェントとして答えます
Dexc.Autopilot.Compiler ws agent say "stage 2 を試します"
```

- `wait`の1件は、`kind`が`decision`（質問）、`instruction`（指示）、`defect`（新しい不具合か、直した後にまた起きた不具合）のどれかです。指示の`builds`が空なら、全ビルドへの指示です
- 1件は1回だけ渡ります。`--timeout`で待つ秒数を変えられます。既定の90秒は、エージェントのシェルの既定の時間切れ（2分）に収まる長さです
- 経緯は`.state/.dashboard/journal.jsonl`に残ります

#### プロジェクトのページ

ワークスペースの`Dashboard/`に置いたHTMLは、`/project/`で配られ、上のバーに並びます。エージェントが、プロジェクトに合わせたページを足せます。データは`StatusRequested`で足した項目と、テレメトリーの指標から取ります。

```html
<link rel="stylesheet" href="/style.css">
<script src="/dexc.js"></script>
<script>
	Dexc.nav();
	Dexc.on("build", build => { /* build.status にゲームが足した項目がある */ });
</script>
```

| API | 内容 |
| --- | --- |
| `GET /api/builds`、`/api/status`、`/api/graph`、`/api/history` | ビルドの一覧、`status`、マシンの図、直近の状態の移り変わり。`?build=`で1つに絞ります |
| `GET /api/perf/builds`、`/api/perf/metrics`、`/api/perf/points`、`/api/perf/spans`、`/api/perf/logs` | 受けたテレメトリー。`points`は`?build=&name=&since=` |
| `GET /api/perf/summary`、`/api/perf/loads`、`/api/perf/hitches` | 分布（`?metric=&since=`、ビルドと系列ごとのp50、p95、p99、遅いフレームと止まったフレームの割合）、ロードと段階、ヒッチ |
| `GET /api/decisions`、`/api/journal` | 答えを待つ質問、経緯 |
| `GET /api/events` | SSE。`build`、`graph`、`perf`、`journal` |
| `POST /api/decisions/answer`、`/api/instructions` | 答える、指示を送る。`Dexc.post`がトークンを付けます |

### 共通のBotと専用のBot

`Common`と`Builds/<name>`を合わせて、ビルドごとに1つのアセンブリへコンパイルします。`Builds/<name>`に同じ相対パスのファイルがあれば、`Common`のファイルと置き換わります。

専用の動きを足すときは、エントリーのファイルを専用のフォルダーへ置きます。先に宣言したマシンが優先されるので、共通の動きを上書きできます。

```csharp
public static void Configure(AutopilotBuilder<MyContext> builder)
{
	builder.Machine("Repro1234").When(IsReproStage).Step(Repro);
	Common.Declare(builder);
}
```

### 参照の選び方

`ws add`のオプションで、コンパイルに使う参照を選びます。

| 参照 | オプション | 差が分かるとき |
| --- | --- | --- |
| `managed` | なし | コンパイル時。Playerに残ったアセンブリを使います |
| `dlls` | `--dlls <folder>,... --editor <dir>` | 読み込み時。指定のフォルダーのDLLで置き換えます |
| `csproj` | `--csproj <file>` | 読み込み時。csprojをビルドしたDLLで置き換えます |

`dlls`と`csproj`は、Playerとコードが完全に一致していなくても動かしたいときに使います。`--editor`にはUnity Editorのフォルダーを指定します。ビルド前のDLLが必要とする`netstandard`を補います。Playerに`Managed`が残っていないときは、エンジンとクラスライブラリも補います。

差があるメンバーは、読み込みの応答と`status`の`missing`に一覧で出ます。Botはそのまま動き、そのメンバーに届くと例外で止まります。内容は`failure`に出ます。

`Library/ScriptAssemblies`のDLLやcsprojは、Editor向けにコンパイルされています。`UNITY_EDITOR`の中のメンバーもコンパイルは通り、`missing`で初めて分かります。

## 使えないもの

### ILRuntime

- `checked`の演算は実行時にエラーになります。
- `.Result`や`Wait()`は、完了していない処理を待つとゲーム全体が止まります。警告`DEXC013`が出ます。
- ホストへ渡すDelegateの型は、`ILRuntimeBackend`に登録が必要です。

### Pipeline版

動かせないコードは、コンパイル時にエラーにします。主なものは`try/catch`、`async`、`ref`／`out`引数の宣言、ジェネリックの宣言、LINQです。全ルールは[仕様](../spec/autopilot.md#ガードレール)を参照してください。

## 異常からの復旧

| 状況 | 動作 |
| --- | --- |
| 無限ループ、深い再帰 | 例外で打ち切ります。ゲームは動き続けます |
| Botの例外 | Botだけ止まります |
| evalの例外 | そのevalだけ失敗します |
| Playerの異常終了、応答なし | `run-autopilot-player.sh ensure`で起動し直します |

詳しい取り決めは[Autopilot仕様](../spec/autopilot.md)にあります。
