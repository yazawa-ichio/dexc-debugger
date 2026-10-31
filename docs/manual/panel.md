# Panel

projectが組み立てるメニューのページです。トグルやボタンを並べたページを`DebugPanels`へ登録すると、Runtime menuの`Panels`と、Editorの`Dexc Debugger`の`Panels`に出ます。`DexcDebugger.Initialize`の前でも登録できます。

## Panelを作る

```csharp
using Dexc;
using UnityEngine;

namespace MyGame
{
	public sealed class CheatPanel : MonoBehaviour
	{
		DebugPanel m_Panel;

		void OnEnable()
		{
			m_Panel = new DebugPanel("Cheats")
				.Label(() => "HP: " + Player.Hp)
				.Toggle("God Mode", () => Player.GodMode, value => Player.GodMode = value, key: "Cheats/GodMode")
				.Choice("Difficulty", new[] { "Easy", "Normal", "Hard" }, () => Game.Difficulty, value => Game.Difficulty = value)
				.Number("Speed", () => Player.Speed, value => Player.Speed = (float)value, minimum: 0.5, maximum: 4, step: 0.5)
				.Text("Name", () => Player.Name, value => Player.Name = value)
				.Button("Full Heal", () => Player.Hp = Player.MaxHp);
			DebugPanels.Register(m_Panel);
		}

		void OnDisable()
		{
			DebugPanels.Unregister(m_Panel);
		}
	}
}
```

| 部品 | 内容 |
| --- | --- |
| `Toggle` | On／Off |
| `Choice` | 選択肢から1つ。値は選択肢の番号 |
| `Number` | 数値。`minimum`／`maximum`で範囲、`step`で左右の増減幅、`integer`で整数だけ |
| `Text` | 文字列 |
| `Button` | 押すと処理を呼ぶ |
| `Label` | 表示だけ。関数を渡すと毎回読み直す |

部品は値を持たず、渡した関数で毎回読み書きします。`key`を付けた部品は値を`DebugStorage`へ保存し、登録したときに戻します。

Panelの並び順は`Order`、同じ`Order`なら`Title`の順です。登録はPlay Modeに入り直すと消えるので、登録するcomponentや`[RuntimeInitializeOnLoadMethod]`から毎回登録します。

## 横並び表示の最初のページにする

区分・一覧・ページを横に並べる表示（[Skin](setup.md#skin)の`Layout`）では、メニューを開くと前回開いた区分が出ます。代わりに決まったPanelを出したいときは、登録したPanelを`DebugPanels.Home`に指定します。

```csharp
DebugPanels.Register(m_Panel);
DebugPanels.Home = m_Panel;
```

登録していないPanelを指定しても使われません。1ページずつ表示するときは、Homeから始まります。

Boot画面のPanelは`DebugPanels`ではなく、Bootのセットとブロックで宣言します（[Boot](boot.md)）。
