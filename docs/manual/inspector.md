# Inspector

実行中のobjectのfieldをメニューから確認・変更します。

## 登録する

componentが自分を登録し、見せたいfieldに`[DebugField]`を付けます。auto-propertyには`[field: DebugField]`と書きます。

```csharp
using Dexc;
using UnityEngine;

namespace MyGame
{
	public sealed class Enemy : MonoBehaviour
	{
		const string InspectorPath = "Game/Enemies";

		[DebugField]
		public int HitPoints = 100;

		[DebugField(Access = DebugFieldAccess.ReadOnly, DisplayName = "Patrol Index")]
		int m_PatrolIndex;

		[field: DebugField(Access = DebugFieldAccess.ReadOnly)]
		public bool IsAlert { get; private set; }

		void OnEnable()
		{
			DebugInspector.Register(InspectorPath, this, name);
		}

		void OnDisable()
		{
			DebugInspector.Unregister(InspectorPath, this);
		}
	}
}
```

`Register`はpath、対象、表示名を取ります。対象は`GameObject`、`Component`、`ScriptableObject`などの`UnityEngine.Object`です。sessionが無いとき、`Register`と`Unregister`は何もしないので、`IsInitialized`を確認する必要はありません。ただし登録はsessionが始まった後に呼ぶ必要があるため、初期化するcomponentは`[DefaultExecutionOrder]`で先に動かします。

## Access

| `DebugFieldAccess` | 内容 |
| --- | --- |
| `Default` | 読み書きできます |
| `ReadOnly` | 表示だけ |
| `Locked` | 表示だけ。編集modeに切り替えても変更できません |

`readonly`、`const`、`static`のfieldは常に表示だけです。

## Sceneから探す

登録していないobjectも、メニューのInspectorからsceneのHierarchyをたどって選べます。`[DebugField]`の無いfieldも並びますが、既定では表示だけで、変更するには編集modeへ切り替えます。見続けたい値はWatchへ追加できます。

## 一覧をゲームから渡す

ゲームがすでに一覧を持っている場合は、引数なしのstatic memberに`[DebugInspectorSource(path)]`を付けて候補を返せます。

```csharp
using System.Collections.Generic;
using Dexc;

namespace MyGame
{
	public static class EnemyInspectorSource
	{
		[DebugInspectorSource("Game/Enemies By Type")]
		static IEnumerable<DebugSourceEntry<Enemy>> GetEnemies()
		{
			foreach (var enemy in Enemy.All)
			{
				yield return new DebugSourceEntry<Enemy>(enemy, enemy.name, path: enemy.Type.ToString());
			}
		}
	}
}
```

`Enemy.All`と`Type`はゲーム側の仮の名前です。
