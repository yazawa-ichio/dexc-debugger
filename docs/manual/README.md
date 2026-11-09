# DexcDebugger 利用マニュアル

Unity向けのデバッグツールです。Command（デバッグ処理の実行）、Setting（調整値の編集）、Inspector（実行中objectの確認・変更）、Display（画面・空間への表示）、Panel（projectが組み立てるページ）、Boot（起動時の設定選択）を提供します。

## ページ

- [導入と基本設定](setup.md): install、compile symbol、初期化、メニューの開閉、入力、renderer、Editor画面、release build
- [Command](command.md): メニューから実行する処理
- [Setting](setting.md): メニューから変更する設定値
- [Inspector](inspector.md): 実行中objectのfieldの確認・変更
- [Display](display.md): HUD、Subject、World図形
- [Panel](panel.md): projectが組み立てるメニューのページ
- [Boot](boot.md): 起動時の設定メニューと保存先
- [Remote](remote.md): Unity CLIからの操作
- [Autopilot](autopilot.md): PlayerでのC#の実行とBot
- [QAと性能計測](qa.md): 注釈、ロード、場所、不具合の記録と確認、予算、地図
- [サンプル](sample.md): Stealth Stage Demo の読み方

## 5分で動かす

1. Package Managerの`Add package from git URL...`で`https://github.com/yazawa-ichio/dexc-debugger.git?path=Packages/jp.yazawa-ichio.dexc-debugger#v0.1.0-pre.1`を追加します（ほかの方法は[導入と基本設定](setup.md#install)）。
2. Project Settings > Player > Other Settings > Scripting Define Symbolsへ`DEXC_DEBUGGER`を追加します。
3. 次のscriptを置きます。ゲーム開始時にsessionを始め、F1でメニューを開閉します。

```csharp
using Dexc;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MyGame
{
	public sealed class DebugBootstrap : MonoBehaviour
	{
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		static void Initialize()
		{
			DexcDebugger.Initialize();
			var host = new GameObject("DebugBootstrap");
			DontDestroyOnLoad(host);
			host.AddComponent<DebugBootstrap>();
		}

		void Update()
		{
			if (Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame)
			{
				DebugMenu.Toggle();
			}
		}
	}
}
```

4. Play Modeに入り、F1を押します。最初のCommandは[Command](command.md)の例をそのまま追加して試せます。

例はInput Systemのキーボードを読んでいます。旧Input Managerを使うprojectでは`Input.GetKeyDown(KeyCode.F1)`に置き換えます。
