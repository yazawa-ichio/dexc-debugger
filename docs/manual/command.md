# Command

メニューから実行する処理です。classに`[DebugCommand(path)]`を付け、基底classを継承します。属性の付いたclassは自動で集められ、登録処理は要りません。

| 基底class | 用途 |
| --- | --- |
| `DebugCommand` | 戻り値なし |
| `DebugCommand<TOutput>` | 戻り値あり。結果がメニューと履歴に出ます |
| `AsyncDebugCommand` | 待機を含む処理 |
| `AsyncDebugCommand<TOutput>` | 待機を含み、戻り値あり |

## 最小の例

```csharp
using Dexc;
using UnityEngine.SceneManagement;

namespace MyGame
{
	[DebugCommand("Scene/Reload", Description = "現在のsceneを読み直します。")]
	public sealed class ReloadSceneCommand : DebugCommand
	{
		protected override void Execute()
		{
			SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
		}
	}
}
```

pathの`/`がメニューの階層になります。

## 入力と戻り値

入力はpublic fieldに置きます。初期値はconstructorで渡します。戻り値は`Execute`の戻り値です。

```csharp
using Dexc;
using UnityEngine;

namespace MyGame
{
	[DebugCommand("Game/Set Time Scale")]
	public sealed class SetTimeScaleCommand : DebugCommand<float>
	{
		public InputFloat TimeScale = new InputFloat(1f);

		protected override float Execute()
		{
			Time.timeScale = TimeScale.Value;
			return Time.timeScale;
		}
	}
}
```

使える入力は`InputBool`、`InputInt`、`InputLong`、`InputFloat`、`InputDouble`、`InputString`、`InputEnum<T>`、`InputColor`、`InputVector2`／`3`／`4`、`InputVector2Int`／`3Int`、`InputChoice<T>`、`InputRef<T>`です。表示だけの行には`CommandLabel`と`CommandHeader`を使えます。public fieldには入力、`CommandLabel`、`CommandHeader`以外を置けません。

数値の入力には`[DebugRange]`で範囲を、`[DebugSlider]`でスライダーを付けられます。範囲外の値は入力時に拒否されます。文字列の入力は`[DebugMultiline]`で複数行の欄になります。

```csharp
using Dexc;
using UnityEngine;

namespace MyGame
{
	[DebugCommand("Game/Announce")]
	public sealed class AnnounceCommand : DebugCommand
	{
		[DebugRange(0.5, 10)]
		[DebugSlider]
		public InputFloat Seconds = new InputFloat(3f);

		[DebugMultiline]
		public InputString Message = new InputString("Hello");

		protected override void Execute()
		{
			Debug.Log(Message.Value + " (" + Seconds.Value + "s)");
		}
	}
}
```

## 実行前の検証

`Validate`で実行できるかを判定します。条件を満たさない間、メニューの実行ボタンは押せません。

```csharp
using Dexc;
using UnityEngine;

namespace MyGame
{
	[DebugCommand("Player/Warp")]
	public sealed class WarpPlayerCommand : DebugCommand
	{
		public CommandHeader Section = new CommandHeader("Destination");
		public InputVector3 Position = new InputVector3(Vector3.zero);
		public InputFloat Height = new InputFloat(0f);

		protected override void Validate(DebugCommandValidation validation)
		{
			validation.Require(Player.Instance != null, "player-missing", "Playerがいません。");
			validation.Require(Height.Value >= 0f, Height, "height-negative", "Heightは0以上にしてください。");
		}

		protected override void Execute()
		{
			Player.Instance.transform.position = Position.Value + Vector3.up * Height.Value;
		}
	}
}
```

`Player`はゲーム側のclassを表す仮の名前です。入力を渡した`Require`はその入力の横にエラーを出します。ゲームが動いていないなど、入力と関係なく使えないときは`validation.Unavailable(code, message)`を使います。実行中の失敗は`Fail(code, message)`で返します。

## 選択肢とobjectの選択

`InputChoice<T>`は候補から1つ選ぶ入力です。`InputRef.Create`は実行中のobjectを候補から選ぶ入力で、候補は一覧を開いたときに作られます。

```csharp
using System.Linq;
using Dexc;

namespace MyGame
{
	[DebugCommand("Enemy/Damage")]
	public sealed class DamageEnemyCommand : DebugCommand
	{
		public InputRef<Enemy> Target = InputRef.Create(
			() => Enemy.All.Select(enemy => new InputChoiceEntry<Enemy>(enemy.Id, enemy, enemy.name))
		);

		public InputChoice<int> Damage = new InputChoice<int>(
			new[]
			{
				new InputChoiceEntry<int>("small", 10, "Small (10)"),
				new InputChoiceEntry<int>("large", 100, "Large (100)"),
			},
			"small"
		);

		protected override void Execute()
		{
			if (!Target.TryGetValue(out var enemy))
			{
				Fail("target-missing", "対象がいません。");
			}
			if (!Damage.TryGetValue(out var damage))
			{
				Fail("damage-missing", "Damageを選んでください。");
			}
			enemy.TakeDamage(damage);
		}
	}
}
```

`Enemy`（`MonoBehaviour`）、`Enemy.All`、`Id`、`TakeDamage`はゲーム側の仮の名前です。選ばれた値は`TryGetValue`で取り出します。`InputRef<T>`は選ばれていない、または対象が消えた場合に自動でエラーになります。選ばなくても実行できるようにするには`InputRef.Create(..., optional: true)`にします。その場合は`TryGetValue`がfalseを返します。`InputChoiceEntry`のkeyは候補ごとに一意で、一覧を開き直しても変わらない値にします。

Inspectorの`[DebugInspectorSource]`に`Key`を付けると、同じ候補をCommandからも選べます。`new InputRef<T>(key)`にそのKeyを渡します。

```csharp
using System.Collections.Generic;
using Dexc;

namespace MyGame
{
	public static class EnemySources
	{
		[DebugInspectorSource("Game/Enemies", Key = "enemies")]
		static IEnumerable<DebugSourceEntry<Enemy>> All()
		{
			foreach (var enemy in Enemy.All)
			{
				yield return new DebugSourceEntry<Enemy>(enemy, enemy.name);
			}
		}
	}

	[DebugCommand("Enemy/Kill")]
	public sealed class KillEnemyCommand : DebugCommand
	{
		public InputRef<Enemy> Target = new InputRef<Enemy>("enemies");

		protected override void Execute()
		{
			if (Target.TryGetValue(out var enemy))
			{
				UnityEngine.Object.Destroy(enemy.gameObject);
			}
		}
	}
}
```

## 非同期Command

待機が要る処理だけ`AsyncDebugCommand`を使います。既定のtimeoutは5秒で、`Timeout`をoverrideして変えられます。

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Dexc;
using UnityEngine.SceneManagement;

namespace MyGame
{
	[DebugCommand("Scene/Reload After Delay")]
	public sealed class ReloadAfterDelayCommand : AsyncDebugCommand
	{
		public InputFloat DelaySeconds = new InputFloat(0.5f);

		protected override TimeSpan Timeout => TimeSpan.FromSeconds(10);

		protected override async ValueTask ExecuteAsync(CancellationToken cancellationToken)
		{
			await Task.Delay(TimeSpan.FromSeconds(DelaySeconds.Value), cancellationToken);
			SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
		}
	}
}
```

## 属性のオプション

| property | 内容 |
| --- | --- |
| `Description` | メニューに出す説明 |
| `ConfirmationMessage` | 実行前に確認を出す文 |
| `Access` | `ReadOnly`は他の`ReadOnly`と並んで実行でき、`Lock`は単独で実行します。実行中に重なったCommandは待たずに失敗します |
| `Order` | 並び順 |
| `Tags` | 検索用のtag |

## コードから実行する

Commandのinstanceに値を入れ、requestを作って実行します。sessionが必要です。

```csharp
var command = new SetTimeScaleCommand();
command.TimeScale.Value = 2f;
var validation = DebugCommand.Draft.Validate(command);
if (validation.CanExecute)
{
	var result = DebugCommand.Draft.CreateRequest(command).Dispatch();
	if (result.TryGetOutput(out var timeScale))
	{
		UnityEngine.Debug.Log(timeScale);
	}
}
```

非同期Commandは`DispatchAsync`で実行します。
