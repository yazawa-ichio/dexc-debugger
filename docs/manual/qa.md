# QAと性能計測

エージェントがゲームの品質を管理し、不具合を見つけ、直し、直ったかを確かめるための仕組みです。計測と記録はパッケージが用意し、何を測るか、どこを場所とするかはゲームが決めます。

| 層 | 誰のものか | 中身 |
| --- | --- | --- |
| 提供 | Dexc Telemetry（リリースにも残せます） | 注釈、ロード、ヒッチ、場所の受け口、分布の指標 |
| 補助 | Dexc QA（開発用）、ワークスペースの道具とダッシュボード | 不具合の記録、まとめ、確認の再実行、予算、画面 |
| 拡張 | ゲーム、Bot、ワークスペース | 注釈の値、ロードの始まりと終わり、場所、Botの検査、報告に足す項目、外部への送信、予算の値、地図 |

細かな決まりは[Telemetryの仕様](../spec/telemetry.md)、[QAの仕様](../spec/qa.md)、[Autopilotの仕様](../spec/autopilot.md)にあります。

## ゲームの側

### 何をしているか（注釈）

状態が変わったときに注釈を付けると、フレーム時間などの毎フレームの指標が、その値ごとの系列に分かれます。キーは2つまでです。

```csharp
DexcTelemetry.Annotate("stage", "3");
DexcTelemetry.Annotate("mode", "boss");
DexcTelemetry.Annotate("mode", null); // 外す
```

### ロード

操作したときに始め、操作を受け付ける最初のフレームで終えます。ロード中のフレームは、フレーム時間に入りません。

```csharp
var load = DexcTelemetry.BeginLoad("stage3", TelemetryLoadKind.Level);
using (load.Phase("assets"))
{
	await LoadAssetsAsync();
}
using (load.Phase("build"))
{
	BuildStage();
}
// 次のフレームで、操作を受け付けるようになってから
load.End();
```

### 場所

`ITelemetryLocation`を渡します。ヒッチ、位置の記録、不具合の報告に載ります。

```csharp
sealed class PlayerLocation : ITelemetryLocation
{
	public bool TryGetLocation(out string map, out float x, out float y, out float z)
	{
		var position = Player.Instance.transform.position;
		(map, x, y, z) = (SceneManager.GetActiveScene().name, position.x, position.y, position.z);
		return true;
	}
}

DexcTelemetry.SetLocation(new PlayerLocation());
```

### 報告に足す項目、外部への送信

```csharp
sealed class StageFacts : IDexcQAContributor
{
	public void Contribute(DexcQAReport report)
	{
		report.Set("stage", Game.Stage.ToString());
		report.Attach("save.json", Game.SaveToBytes());
	}
}

DexcQA.AddContributor(new StageFacts());
DexcQA.AddSink(new MyIssueTrackerSink()); // 書いた後に呼ばれます
```

例外とエラーのログは、何もしなくても報告になります。同じ不具合は3件まで詳しく書き、あとは数えます。

### Botの検査

Botは、見たことが正しいかを`DexcQA.Check`で確かめます。

```csharp
DexcQA.Check(context.Game.Score >= 0, "score.negative", "The score went below zero");
```

`signature`は同じ不具合をまとめる鍵です。変わる値を入れないでください。

## ワークスペースの側

`ws start`は、テレメトリーとQAの書き出し先をビルドごとに分けて渡します。QAは起動と同時に始まります。

```sh
Dexc.Autopilot.Compiler ws start --seed 42
Dexc.Autopilot.Compiler ws telemetry start --upload   # ダッシュボードへ5秒ごとに送る。場所も1秒ごと
Dexc.Autopilot.Compiler ws bot --load
```

- 予算は`workspace.json`の`budgets`に書きます。例: `{"metric": "unity.frame.time", "stat": "p95", "max": 33.4, "where": {"stage": "1"}}`。超えると種類`perf`の不具合になります
- 地図は`Dashboard/maps/<地図>.json`に書きます。地図の名前は`ITelemetryLocation`が返す名前です

## エージェントの手順

1. `ws agent wait`で待ちます。`kind: defect`が来たら、不具合が見つかったか、直した後にまた起きたということです
2. `ws qa show <id>`で、報告のフォルダー（`report.json`、`screenshot.png`、ログ、注釈、場所、シード）を見ます。性能の不具合なら、ダッシュボードのPerformanceで分布とロードとヒッチを見ます
3. `ws qa set <id> fixing --note "…"`にして直し、ビルドを作り、`ws add`で登録します
4. `ws qa verify <id> --build <新しいビルド>`で、同じシードで走らせ直します。「起きなかった」は「直った」ではありません。条件と時間が経緯に残ります
5. 納得できたら`ws qa set <id> fixed --note "…"`にします。また起きれば、自動で`open`に戻り、エージェントに知らされます

## サンプル

Stealth Stage Demoの`Measure`フォルダーが、ゲームの側の例です。ステージを注釈にし、ステージの組み立てをロードとして測り、ステージごとの地図の上の場所を返し、報告にステージ、状態、検知の数を足します。サンプルのBotは「出口に3秒立ってもステージが進まない」を検査します。`Workspace~`に、ワークスペースへ写す地図と予算の例があります。
