# Boot

ゲームの起動時に設定を選ぶメニューです。`DexcDebugger.Initialize`の前でも使えます。選んだ値は保存され、次の起動で戻ります。

- **セット**: 1つの起動設定です。名前（`Tag`）と行き先（`Scene`）を持ちます。行き先は、Dexcが無ければ最初に起動していたsceneです。
- **ブロック**: 複数のセットで使い回す項目のまとまりです。値はブロックごとに保存されるので、どのセットから開いても同じ値です。

## セットとブロックを書く

```csharp
using Dexc;

namespace MyGame
{
	[DebugBootGroup("Server")]
	public sealed class ServerBoot : DebugBootBlock
	{
		[DebugBootItem("Host")]
		public string Host { get; private set; } = "localhost";
	}

	[DebugBootMenu("Normal", Tag = "normal", Scene = "Title", Groups = new[] { typeof(ServerBoot) })]
	public sealed class NormalBoot : DebugBootSet
	{
		[DebugBootItem("Skip Intro")]
		public bool SkipIntro { get; private set; }

		[DebugBootItem("Time Scale", Step = 0.25)]
		[DebugRange(0.25, 4)]
		public float TimeScale { get; private set; } = 1f;

		protected override void OnStarting(DebugBootGroupsValues values)
		{
			GameConfig.SkipIntro = SkipIntro;
			GameConfig.TimeScale = TimeScale;
			GameConfig.Host = values.Get<ServerBoot>().Host;
		}
	}
}
```

`GameConfig`はゲーム自身の設定を表す仮の名前です。値は`OnStarting`でゲームの設定へ書き込みます。ゲームからDexcの値を読み返すと、Dexcを無効にしたbuildで動かなくなります。

項目の型は`bool`、`string`、enum、数値です。`Tag`と`Scene`は必須で、無いとエラーになります。`Groups`に書いたブロックだけを`values.Get<T>()`で読めます。

| hook | 呼ばれるとき |
| --- | --- |
| `OnOpened(values)` | 値を読み込んだ直後、描画の前 |
| `OnStarting(values)` | 行き先のsceneを読む前 |
| `OnLoadSceneAfter(values)` | 行き先のsceneを読んだ直後 |
| `ResolveScene()` | 行き先を実行時に決めるときにoverrideします |

## 起動sceneを作る

1. 開発用のsceneを作り、空のGameObjectへ`DebugBootScene`（Add Component > Dexc > Debug Boot Scene）を付けます。
2. `Tag`へセットの名前（例では`normal`）を入れます。
3. このsceneをBuild Settingsの先頭に置き、行き先のsceneもBuild Settingsへ入れます。

`DebugBootScene`の項目は次のとおりです。

| 項目 | 内容 |
| --- | --- |
| `Tag` | 開くセットの名前 |
| `Initialize Dexc` | sessionが無ければ`DexcDebugger.Initialize`を呼びます |
| `Auto Start Seconds` | 指定秒後に自動で開始します。操作すると止まります。0なら待ちます |
| `Title` | ヘッダーの文字 |
| `Skin` | 指定するとsessionを始め、選んだrendererで描きます |

`DEXC_DEBUGGER`が無いbuildでは、このcomponentは何もしません。起動sceneは製品buildに入れません。

## 操作

上下で項目、左右で値を変え、フッターの`Start`で開始します。`Reset`で全項目を初期値へ戻します。初期値から変わった項目は、項目名の後ろに`*`が付きます。文字列と数値の行はEnter／Aで入力を始め、Esc／B（またはもう一度Enter／A）で確定します。gamepadは十字キーを読みます。

## sceneを使わずに開く

```csharp
DebugBootMenu.Title = "Boot Menu";
DebugBootMenu.Open("normal", scene => SceneManager.LoadScene(scene));
```

開始後に何かしたいときは`DebugBootMenu.Started`を購読します。起動画面だけ別の位置に出すときは`DebugBootMenu.Alignment`を設定します。

## Editorから開始する

Window > Dexc > Dexc Bootでセットを選び、値を編集して`Start`を押すと、セットの`Scene`を開いてPlay Modeに入ります。起動sceneは通りません。

## 保存先

既定の保存先は`PlayerPrefs`（`DebugPlayerPrefsStorage`）です。Boot項目は既定で保存されます（保存しない項目は`[DebugBootItem(Persist = false)]`）。[Setting](setting.md#値を保存する)の保存も同じ保存先を使います。

保存先を変えるときは、作り方を`DebugStorage.Factory`へ登録します。

```csharp
using Dexc;
using UnityEngine;

namespace MyGame
{
	public static class DebugStorageSetup
	{
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		static void Setup()
		{
			DebugStorage.Factory = () => new DebugFileStorage(Application.persistentDataPath + "/debug.txt");
		}
	}
}
```

Dexcは内部の状態を作り直すたびに、この作り方で保存先を作り直します。Dexc Bootウィンドウ（Edit Mode）でも同じ保存先を使うときは、Editor側でも`[UnityEditor.InitializeOnLoadMethod]`から同じ`Factory`を設定します。

| 型 | 保存先 |
| --- | --- |
| `DebugPlayerPrefsStorage` | `PlayerPrefs` |
| `DebugFileStorage` | 1つのtext file。pathを省くと`persistentDataPath/dexc-debug.txt` |
| `DebugMemoryStorage` | memoryのみ |

独自の保存先は`IDebugStorage`（`TryLoad`、`Save`、`Delete`、`Flush`）を実装します。
