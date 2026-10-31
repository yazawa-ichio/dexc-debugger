# サンプル: Stealth Stage Demo

`Assets/Samples/StealthStageDemo`は、Dexcの機能をひととおり使う小さなステルスゲームです。このリポジトリにだけあり、packageには含まれません。動かし方は同じフォルダの`README.md`を参照してください。

## 構成

ゲーム本体（`Runtime`）は`Dexc.Runtime`を直接参照し、状態を持つcomponentがその場でDexcを呼びます。debuggerにだけ必要なもの（`Debug`）は別assemblyです。

| file | 見どころ |
| --- | --- |
| `Runtime/StealthStageDemoController.cs` | 自分をInspectorへ登録し、`[DebugField]`と`[field: DebugField]`でfieldを見せます。HUDラベル、World図形、playerのSubjectを描きます |
| `Runtime/StealthGuardController.cs` | 警備ごとのInspector登録とSubject。描画を`[Conditional("DEXC_DEBUGGER")]`のmethodへまとめ、`stackalloc`したspanで巡回路を`DrawPolyLine`します。視界は`DrawWireSector`、playerへの線は`DrawRelation`です |
| `Debug/StealthDebugSystem.cs` | Skinを指定した`Initialize`、input providerの接続と解除、HUD widget、メニューを開くキーとDEBUGボタン |
| `Debug/StealthDebugMenuInputProvider.cs` | gamepadを読むinput provider。メニューを開いている間はゲームの操作を止めます |
| `Debug/StealthDemoCommands.cs` | 検証付きのCommand（stage番号、enum、bool、float入力） |
| `Debug/StealthDemoSettings.cs` | 範囲とslider、`ApplyMode`、preset、`OnChanged`でゲームへ値を渡すSetting |
| `Debug/StealthDemoBootSettings.cs` | 2つのセットと、それらで共有するブロック。`OnStarting`でゲームの設定へ値を書きます |

## 書き方の要点

- ゲームコードは`IsInitialized`を確認せずにDexcを呼びます。sessionが無いとき、Inspector登録、Display描画、`DebugMenu.SetInputProvider`／`ClearInputProvider`は何もしません。
- `DEXC_DEBUGGER`が無いbuildでは呼び出しが消え、同じゲームがDexc無しで動きます。
- Boot画面で選んだ値は、ゲーム自身の`StealthDemoConfig`へ書かれます。ゲームはDexcから値を読み返しません。
- メニューはF1、gamepadのStart、画面右下のDEBUGボタン（マウスやタップ）で開きます。ボタンは`StealthDebugSystem.OnGUI`にあります。
