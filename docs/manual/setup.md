# 導入と基本設定

## Install

Package Managerの`Add package from git URL...`へ次のURLを入力します。`#`の後ろはversionのtagです。

```
https://github.com/yazawa-ichio/dexc-debugger.git?path=Packages/jp.yazawa-ichio.dexc-debugger#v0.1.0-pre.1
```

repositoryをcloneして使う場合は、`Add package from disk...`で`Packages/jp.yazawa-ichio.dexc-debugger/package.json`を選びます。`Packages/manifest.json`へローカル依存を書いても追加できます。次の例は、利用projectと同じ階層へ`dexc-debugger`としてcloneした場合です。

```json
"jp.yazawa-ichio.dexc-debugger": "file:../../dexc-debugger/Packages/jp.yazawa-ichio.dexc-debugger"
```

Unity 6000.3以降が対象です。任意のpackageがあると次の機能が有効になります。

| package | 有効になるもの |
| --- | --- |
| URP（`com.unity.render-pipelines.universal`） | World図形の描画 |
| uGUI（`com.unity.ugui`） | uGUI renderer |
| Input System（`com.unity.inputsystem`） | `DebugGamepadInputProvider`とBoot画面のgamepad操作 |
| Unity CLI（`com.unity.pipeline`） | [Remote](remote.md)の操作 |

## Compile symbol

| symbol | 意味 |
| --- | --- |
| `DEXC_DEBUGGER` | 機能を有効にします。build targetごとにScripting Define Symbolsへ追加します。packageは自動で追加しません。 |
| `DISABLE_DEXC_DEBUGGER` | Dexcのassemblyをcompile対象から外します。通常は使いません。 |

`DEXC_DEBUGGER`が無いbuildでも、Dexcのassemblyは残り、呼び出しが消えるだけです（[Release build](#release-build)）。

## Assembly参照

asmdefを使うゲームコードは`Dexc.Runtime`を参照します。`DebugMenu`、`DebugBootMenu`、`DebugBootScene`、`DebugGamepadInputProvider`を使うassemblyは`Dexc.DebugUI`も参照します。asmdefの無い`Assembly-CSharp`からは自動で参照できます。

## 初期化

Dexcは自動では始まりません。Command、Setting、Inspector、Displayを使う前に`DexcDebugger.Initialize`でsessionを始めます。Skinを渡すときはoptionsで指定します。

```csharp
using Dexc;
using UnityEngine;

namespace MyGame
{
	[DefaultExecutionOrder(-1000)]
	public sealed class DebugSystem : MonoBehaviour
	{
		[SerializeField]
		DexcRuntimeUISkin m_Skin;

		void Awake()
		{
			if (!DexcDebugger.IsInitialized)
			{
				var skin = m_Skin;
				DexcDebugger.Initialize(options => options.RuntimeUI.Skin = skin);
			}
		}
	}
}
```

`Initialize`をもう一度呼ぶと、今のsessionを捨てて新しく始めます。Boot sceneなどで先に始まっている場合に備えて`IsInitialized`を確認します。sessionはPlay Modeを抜けると消えます。

## Skin

Project windowの`Assets > Create > Dexc > Runtime UI Skin`でSkinを作ります。Skinはパネルの大きさと位置（Windows／Android縦／Android横）、色、Font、World図形のShader、UI Toolkit用のPanelSettingsを持ちます。

各profileの`Layout`を`Auto`または`Split`にすると、横幅に余裕がある画面でセクション・一覧・ページを横に並べて表示します。並べたときのパネルの大きさは`SplitPanelSize`（0なら960×560）です。既定は`Single`で、1ページずつ表示します。横に並べるのは横長の画面だけです。並べられる幅があるときはヘッダーに`Single`／`Split`の切替が出ます（切替は保存されません）。

Skinは無くても動きます。その場合、World図形とUI Toolkit rendererは使えません。日本語を表示する場合は、権利を持つ日本語Fontを`Font`へ設定します。このリポジトリには`Assets/DexcDebugger/Font/DexcRuntimeUISkin-NotoSansJP.asset`があります。

## メニューを開く

`DebugMenu.Open()`、`Close()`、`Toggle()`をゲームのキーやボタンから呼びます。メニューのHomeからCommand、Setting、Inspector、History、Diagnostics、Panelsへ移動できます。Homeにはお気に入りのCommand・Settingと、最近実行したCommandも並びます。CommandとSettingは詳細ページのヘッダーの★でお気に入りに追加します。

区分・一覧・ページを横に並べる表示ではHomeを出しません。左端の区分の列がHomeの代わりになり、メニューを開くと前回開いた区分（初回はCommand）を開きます。最初に見せたいPanelがあれば`DebugPanels.Home`に指定します（[Panel](panel.md)）。gamepadでは、区分の一番上のページでEast（戻る）か左を押すと区分の列へ移り、上下で選んで右か決定で開きます。

```csharp
if (Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame)
{
	DebugMenu.Toggle();
}
```

`DebugMenu.IsOpen`で開いているかを確認できます。メニューは自分では開かないので、ゲームがキーや画面のボタンから呼びます。開いたメニューは、どのrendererでもマウスやタップで操作できます。画面のボタンで開く例は[サンプル](sample.md)の`StealthDebugSystem`にあります。

## 入力

マウス、キーボード、タッチはそのまま使えます。gamepadで操作するときは`DebugMenuInputProvider`を渡します。Input Systemのgamepad（十字キー、South／Startで決定、Eastで戻る）を読むだけなら`DebugGamepadInputProvider`を使えます。

```csharp
DebugMenu.SetInputProvider(new DebugGamepadInputProvider());
```

ゲーム側の入力に合わせる場合は自分で作ります。`OnInputStateChanged`でメニューが閉じたかどうかを受け取り、ゲームの操作を止めたり戻したりします。

```csharp
using Dexc;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MyGame
{
	public sealed class GameMenuInput : DebugMenuInputProvider
	{
		public override DebugMenuInputFrame ReadInput()
		{
			var gamepad = Gamepad.current;
			if (gamepad == null)
			{
				return new DebugMenuInputFrame(Vector2.zero, false, false);
			}
			return new DebugMenuInputFrame(
				gamepad.dpad.ReadValue(),
				gamepad.buttonSouth.wasPressedThisFrame,
				gamepad.buttonEast.wasPressedThisFrame
			);
		}

		public override void OnInputStateChanged(DebugMenuInputState state)
		{
			GameInput.Enabled = state == DebugMenuInputState.Closed;
		}
	}
}
```

`GameInput.Enabled`はゲーム側の入力の切替を表す仮の名前です。providerは同時に1つです。外すときは`DebugMenu.ClearInputProvider(provider)`を呼びます。

## Renderer

メニューはIMGUI、uGUI、UI Toolkitのどれでも描けます。既定はScripting Define Symbolsで決まります。

| symbol | renderer | 条件 |
| --- | --- | --- |
| なし | IMGUI | なし |
| `DEXC_RUNTIME_UGUI` | uGUI | `com.unity.ugui`が必要 |
| `DEXC_RUNTIME_UITOOLKIT` | UI Toolkit | PanelSettingsを持つSkinが必要 |

両方を定義した場合はUI Toolkitになります。実行中に切り替えるときは`DebugMenu.SetRenderer`を呼びます。uGUIパッケージが無いときに`UGUI`を指定すると`false`が返ります。UI Toolkitを使えない（SkinにPanelSettingsが無い）ときはIMGUIで描画し、`DebugMenu.Renderer`は`IMGUI`を返します。

```csharp
DebugMenu.SetRenderer(DebugMenuRenderer.UGUI);
```

uGUIはsceneに`EventSystem`が無いとき、pointer専用の`EventSystem`を自分で作り、メニューを閉じると消します。

## Editor画面

| menu | 内容 |
| --- | --- |
| Window > Dexc > Dexc Debugger | Inspector、Command、Setting、History、Display、Panels、DiagnosticsをEditorから見て操作します。Play Mode中に使います。 |
| Window > Dexc > Dexc Boot | Boot setを選んで値を編集し、そのまま開始します（[Boot](boot.md)）。 |

## Release build

`DEXC_DEBUGGER`を定義しないbuildでは、次のようになります。

- `[Conditional("DEXC_DEBUGGER")]`の付いた呼び出しは、引数の評価ごとcompileで消えます。`DexcDebugger.Initialize`、`DebugMenu.Open`／`Close`／`Toggle`／`SetInputProvider`／`ClearInputProvider`、`DebugInspector.Register`／`Unregister`、`DebugDisplay`の`Draw…`、Subjectの`SetLabel`／`SetMetadata`／`Draw…`、`DebugBootMenu.Open`などが対象です。
- `[DebugCommand]`、`[DebugSetting]`、`[DebugField]`、`[DebugBootMenu]`などの属性も消えます。
- `DebugDisplay.CreateSubject`は何もしないSubjectを返し、`Dispose`も安全に呼べます。
- `DexcDebugger.IsEnabled`と`IsInitialized`は`false`です。

このため、ゲームコードは`#if`で囲まずにDexcを呼べます。呼び出しの前に描画用のデータを組み立てる場合は、その処理ごと`[Conditional("DEXC_DEBUGGER")]`のmethodへ入れます（[Display](display.md#releaseで消えるようにする)）。

`DISABLE_DEXC_DEBUGGER`はassemblyごと外します。Dexcを参照するasmdefには、Define Constraintsへ`!DISABLE_DEXC_DEBUGGER`を付ける必要があります。
