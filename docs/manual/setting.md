# Setting

メニューから変更する設定値です。関連する値を`DebugSettingSet<TSelf>`を継承したclassへまとめ、propertyに`[DebugSetting(path)]`を付けます。classは自動で集められます。

## 最小の例

```csharp
using Dexc;

namespace MyGame
{
	public sealed class GameDebugSettings : DebugSettingSet<GameDebugSettings>
	{
		[DebugSetting("Game/Player Speed")]
		[DebugRange(1, 12)]
		[DebugSlider]
		public float PlayerSpeed { get; private set; } = 5f;

		[DebugSetting("Game/Invincible")]
		public bool Invincible { get; private set; }

		protected override void OnChanged(DebugSettingChanges changes)
		{
			ApplyTo(Player.Instance);
		}

		public static void ApplyTo(Player player)
		{
			if (player == null)
			{
				return;
			}
			player.Speed = Current.PlayerSpeed;
			player.Invincible = Current.Invincible;
		}
	}
}
```

`Player`はゲーム側の仮の名前です。現在の値は`GameDebugSettings.Current`で読めます。propertyはpublicなgetterとpublicでないsetter（`private set`）にします。初期値はproperty initializerで書きます。

## ゲームへ値を渡す

`OnChanged`はメニューやpresetで値が変わったときに呼ばれます。起動時の値では呼ばれないので、ゲームの開始時に一度`ApplyTo`のように渡します。

```csharp
void Start()
{
	GameDebugSettings.ApplyTo(Player.Instance);
}
```

`DEXC_DEBUGGER`の無いbuildでは`Current`は初期値のままです。

## 使える型と属性

型は`bool`、整数、浮動小数、`string`、enum、`Color`、`Vector2`／`3`／`4`、`Vector2Int`／`3Int`です。

| 属性 | 内容 |
| --- | --- |
| `[DebugRange(min, max)]` | 数値の範囲 |
| `[DebugSlider]` | sliderで編集します。数値と`DebugRange`が必要です |
| `[DebugSetting(path, ApplyMode = DebugSettingApplyMode.Submit)]` | Applyを押すまで反映しません。既定は`Immediate`です |
| `[DebugSetting(path, Access = DebugSettingAccess.ReadOnly)]` | 表示だけ |

## Preset

同じclassを返すstatic propertyに`[DebugPreset(id, displayName, description)]`を付けると、メニューからまとめて値を切り替えられます。書かなかったpropertyは初期値になります。

```csharp
[DebugPreset("easy", "Easy", "速く動けて、敵に見つかりにくい設定")]
public static GameDebugSettings Easy =>
	new GameDebugSettings
	{
		PlayerSpeed = 8f,
		Invincible = true,
	};
```

## 値を保存する

`Persist = true`を付けた値は、変更時に保存され、次の起動で戻ります。classに`[DebugPersistent]`を付けると全部の値が対象になります。保存先は[Boot](boot.md#保存先)と同じ`DebugStorage`です。

```csharp
[DebugSetting("Game/Invincible", Persist = true)]
public bool Invincible { get; private set; }
```

## Display用のSetting

`Display/HUD`、`Display/Overlay`、`Display/World`は組み込みのSettingで、Displayの各層を表示・非表示にします。ゲーム側で`Display/<path>`のbool Settingを作ると、そのpath以下のDisplayを`false`で隠せます（[Display](display.md#表示の切替)）。
