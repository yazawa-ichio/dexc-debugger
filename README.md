# DexcDebugger (Debug Execution Command Debugger)

Unity向けのデバッグ処理、環境設定、実行中objectの観測・変更、画面・空間表示を統合して提供するツールセットです。

## Package compile symbols

`DEXC_DEBUGGER`は利用projectが明示的に定義した場合に機能を有効化します。どちらのsymbolも未定義ならpackage assemblyは存在したまま機能がDisabledになり、`DISABLE_DEXC_DEBUGGER`を定義すると`Dexc.Runtime`／`Dexc.DebugUI`／`Dexc.Editor`／`Dexc.Remote.Protocol`／`Dexc.Remote`／`Dexc.Remote.Pipeline`とDexc test assemblyをcompile対象から除外します。packageは`DEXC_DEBUGGER`を自動追加しません。

## Install and enable

Unity Package Managerの`Add package from git URL...`へ次のURLを入力します。`#`の後ろはversionのtagです。

```
https://github.com/yazawa-ichio/dexc-debugger.git?path=Packages/jp.yazawa-ichio.dexc-debugger#v0.1.0-pre.1
```

repositoryをcloneして使う場合は、`Add package from disk...`で`Packages/jp.yazawa-ichio.dexc-debugger/package.json`を選びます。利用projectの`Packages/manifest.json`へローカル依存を書いても追加できます。次の例は、利用projectと同じ階層へ`dexc-debugger`としてcloneした場合です。

```json
"jp.yazawa-ichio.dexc-debugger": "file:../../dexc-debugger/Packages/jp.yazawa-ichio.dexc-debugger"
```

利用するbuild targetごとに、Project Settings > Player > Other Settings > Scripting Define Symbolsへ`DEXC_DEBUGGER`を追加します。Windows StandaloneとAndroidはそれぞれのtargetのsymbol欄へ設定し、`DISABLE_DEXC_DEBUGGER`は設定しません。packageはsymbolを自動追加しないため、未定義時はassemblyが存在しても機能はDisabledです。

## Initialize before registering features

packageの暗黙初期化はありません。最初のCommand／Setting／Inspector／Display登録より前に、project側のroot bootstrapから明示的にsessionを開始します。

```csharp
#if DEXC_DEBUGGER && !DISABLE_DEXC_DEBUGGER
using Dexc;
using UnityEngine;

public static class DexcProjectBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void InitializeDexc()
    {
        DexcDebugger.Initialize();
    }
}
#endif
```

必要な場合は`DexcDebugger.Initialize(options => { ... })`でoptionsを設定します。`DexcDebugger.IsEnabled`と`DexcDebugger.IsInitialized`で有効状態とsession開始状態を確認できます。

URPは必須依存ではありません。`com.unity.render-pipelines.universal`がある場合だけ`DEXC_HAS_URP`が定義され、World描画（3D図形）が有効になります。Built-inやHDRPのプロジェクトでもメニュー・HUD・Setting・Command・Inspector・Boot Menuは利用できます。

Runtime renderer、単一Skin、任意dependency、asset包含境界の設定は[マニュアルの導入](docs/manual/setup.md)を参照してください。

## Open the UI

Runtimeでは、明示的な`Initialize`後にprojectの入力やボタンから`DebugMenu.Open()`、`DebugMenu.Toggle()`、`DebugMenu.Close()`を呼び出します。Runtime menuのHomeからCommand、Setting、Inspector、History、Diagnostics、Panels画面へ移動できます。Homeにはお気に入り（CommandとSettingの詳細ページのヘッダーの★で追加）と最近実行したCommandも並びます。区分・一覧・ページを横に並べる表示ではHomeを出さず、`DebugPanels.Home`に登録済みのPanelを指定していればそのPanelを、指定が無ければ前回開いた区分（初回はCommand）を開きます。`DEXC_DEBUGGER`が無効なbuildではこれらのConditional API呼出しも有効化されません。

Editorでは、packageを有効化した状態でPlay Modeへ入り、Window > Dexc > Dexc Debuggerを開きます。EditorのCommand操作はPlay Mode外ではdisabledです。Editorはcompleted snapshotのdetail／diagnosticを閲覧し、World Visualを独自描画しません。

## Boot Menu (起動時設定)

ゲーム起動時に設定を選択するメニューです。`DexcDebugger.Initialize()`なしで利用でき、値は`DebugStorage`経由で永続化されます。

**セット**は1つの起動設定で、名前（`Tag`）と行き先（`Scene`）を持ちます。行き先は、Dexcを入れなければ最初に起動していたはずのsceneです。**ブロック**は複数のセットで使い回す項目のまとまりで、保存値はブロック単位なのでどのセットから開いても同じ値になります。

**ゲーム側はDexcを参照しません。** 値はセットの`OnStarting`がゲーム自身の設定へ書き込みます。ゲームがDexcから値を読み返すと、`DISABLE_DEXC_DEBUGGER`のbuildでゲームが成立しなくなります。

1. **開発用のsceneへ**`DebugBootScene`コンポーネントを置き、`Tag`に出したいセットの名前を入れて、Build Settingsの先頭へ追加します。このsceneは開発専用で、製品buildには入れません（`DEXC_DEBUGGER`が無効なときコンポーネントは何もしません）。
2. セット: `DebugBootSet`を継承したクラスへ`[DebugBootMenu(Tag = "...", Scene = "...")]`、propertyへ`[DebugBootItem]`（bool／enum／数値／string、`[DebugRange]`で範囲）を付けます。`Groups`に列挙したブロックの項目も同じ画面に並びます。
3. ブロック: `DebugBootBlock`を継承したクラスへ`[DebugBootGroup]`。セットが`Groups`に列挙して使います。`OnStarting`の引数から`values.Get<MyBlock>()`で読めます。
4. 操作: 上下で項目、左右で値変更、Enter／Aボタンで決定、**フッターの`Start`**で開始（どのページでも見えます）。宣言値から変わった項目は名前の後ろに` *`が付きます。起動画面のヘッダーには閉じるボタンを出しません（閉じる＝開始で紛らわしいため）。戻るボタンは一覧より下の階層にいるときだけ出ます。文字列・数値の行はEnter／Aで入力を開始し、Esc／B（またはもう一度Enter／A）で確定して抜けます。入力中は上下移動しません。Input Systemのgamepadはd-padだけを読み（スティックは無効）、`DebugMenu.SetInputProvider`／`DebugBootMenu.SetInputProvider`で差し替えられます。sceneを使わない場合は`DebugBootMenu.Open(tag, loadScene)`を直接呼びます。
5. Editorから: `Window > Dexc > Dexc Boot`でセットを選び、編集して`Start`を押すと、そのセットの`Scene`を開いてPlay Modeに入ります。起動sceneは通りません。
6. 確認用の入口: このリポジトリでは`Assets/Scenes/DebugBootScene.unity`（index 0）→`Assets/Scenes/BootCheck.unity`の順に起動します。BootCheck画面の左上のボタンで、Debug Menuの開閉、renderer（IMGUI／uGUI／UI Toolkit）の切替、Boot Menuの再表示ができます。rendererの実行時切替は`DebugMenu.SetRenderer(DebugMenuRenderer.UGUI)`で、gamepad入力は`DebugMenu.SetInputProvider(new DebugGamepadInputProvider())`で接続できます。

### 起動の流れ

```
DebugBootScene (build index 0、開発専用)
  DebugBootScene.Start
    DexcDebugger無効 ─────────────────────────────→ 何もしない
    Skin指定 or Initialize Dexc → DexcDebugger.Initialize(Skin)
    保留中のrenderer切替を適用（永続化した選択など）
    providerが無ければ DebugGamepadInputProvider を接続
    DebugBootMenu.Open(Tag, LoadScene) → Tagのセットとそのブロックを開く
  操作（値は変更時に保存。Auto Startは操作で解除）
  Start / Auto Start
    Flush → OnStarting（ゲームの設定へ書く）→ ResolveScene()のsceneをロード → OnLoadSceneAfter → DebugBootMenu.Started
BootCheck（セットが宣言した行き先）
  BootCheckScreen.Start: 自分の BootCheckConfig を読んで適用（TimeScale／renderer／FPS widget／Debug Menu）
  「Back to Boot Scene」: index 0 を再読込して最初から
```

Tagが空、または一致するセットが無いときは例外で止まります。起動設定は開発時の機能で、打ち間違いを黙って回避するより、その場で気付ける方がよいためです。

パネルの表示位置は Skin の`Alignment`（`Bottom`／`LeftTop`／`LeftCenter`／`RightTop`／`RightCenter`／`RightBottom`）で決まります。起動画面だけ別の位置に置くなら`DebugBootMenu.Alignment`を設定します（nullならSkinどおり）。Skinの`Layout`を`Auto`／`Split`にすると、横幅に余裕がある横長の画面では区分・一覧・ページを横に並べます（既定は`Single`）。

sessionが無い場合（`Initialize`前に`DebugBootMenu.Open`を呼んだ場合）は専用のIMGUI描画で同じ流れになります。uGUI rendererはsceneに`EventSystem`が無いときだけpointer専用の`EventSystem`を自動生成し、menuを閉じると破棄します（gamepad／keyboardはproviderが担当）。

```csharp
// 複数のセットで使い回す単位
[DebugBootGroup("Server")]
public sealed class ServerSettings : DebugBootBlock
{
	[DebugBootItem("Host")] public string Host { get; private set; } = "localhost";
}

[DebugBootMenu("Startup", Tag = "startup", Scene = "Title", Groups = new[] { typeof(ServerSettings) })]
public sealed class StartupBoot : DebugBootSet
{
	[DebugBootItem("Stage")] public StageId Stage { get; private set; } = StageId.Title;
	[DebugBootItem("Skip Intro")] public bool SkipIntro { get; private set; }
	[DebugBootItem("Time Scale", Step = 0.25)] [DebugRange(0.25, 4)] public float TimeScale { get; private set; } = 1f;

	// 値はここでゲーム自身の設定へ渡す。ゲームはDexcを参照しない
	protected override void OnStarting(DebugBootGroupsValues values)
	{
		GameConfig.Stage = Stage;
		GameConfig.SkipIntro = SkipIntro;
		GameConfig.Host = values.Get<ServerSettings>().Host;
	}
}
```

### HUD Widget

FPSやメモリなど毎フレーム更新する表示は、取得方法をプロジェクト側で決めて`DebugDisplay.RegisterWidget(new DebugDisplayWidget("HUD/FPS", () => fpsText))`で登録します。Displayの他のHUDラベルと同じpathフィルタで表示／非表示を切り替えられます。

### 状態の寿命（root）

実行中の状態はすべて内部の`DexcRoot`インスタンス1つにぶら下がります。library内で可変な`static`変数はこのrootへの参照だけで、session・storage・登録済みpanel・Runtime menuの状態・各rendererはすべてrootの中にあります。属性から読み取った**定義**は別で、アセンブリの寿命に合わせたキャッシュが持ちます。定義はアセンブリが同じなら常に同じ結果なので、前回の実行から引き継いで困るものがありません。rootは次のタイミングで作り直され、古いrootはUnity objectやevent購読を解放してから捨てられます。`DebugStorage.Factory`で登録した保存先の作り方だけは設定として次のrootへ引き継ぎます。

- Play Modeに入るとき（`SubsystemRegistration`。Domain Reloadを無効にしていても走ります）
- EditorがPlay Modeを抜けてEdit Modeに戻ったとき

このため、Domain Reloadを切ったまま再生を繰り返しても前回の実行の値やUnity objectを引き継ぎません。`[DebugSetting]`のクラスの`Current`もrootが持つので、再生ごとに永続化された値から作り直されます。起動設定の値はrootではなく起動画面が持ち、画面を閉じると一緒に消えます。`DebugBootMenu.Started`のようなeventの購読もrootと一緒に捨てられるため、購読側がEditor windowのようにrootより長生きする場合は購読し直してください。

### 永続化とストレージ

既定は`PlayerPrefs`（`DebugPlayerPrefsStorage`）です。保存先を変えるときは、作り方を`DebugStorage.Factory = () => new DebugFileStorage(path)`のように登録します。Dexcは内部の状態を作り直すたびにこの作り方で保存先を作り直すので、Play ModeでもEdit Modeでも同じ保存先を使い、ファイルもそのたびに読み直します。作り直す前には、書き込み待ちの値を保存先へ書き出します。`DebugStorage.Storage`へ直接設定した保存先は、次に状態を作り直すまでの間だけ使われます。既存のDebugSettingも、クラスへ`[DebugPersistent]`、または`[DebugSetting(..., Persist = true)]`で書込み成功時に保存し、次回`Initialize`時に復元します（範囲外・型不一致の保存値は無視）。

## Documents

使い方は[マニュアル](docs/manual/README.md)、文書の役割分担は[Docs索引](docs/README.md)を参照してください。

### 実装仕様

- [機能設計](docs/spec/feature-design.md)
- [スコープ](docs/spec/core-scope.md)
- [DebugCommand仕様](docs/spec/debug-command.md)
- [DebugSetting仕様](docs/spec/debug-setting.md)
- [DebugInspector仕様](docs/spec/debug-inspector.md)
- [DebugDisplay仕様](docs/spec/debug-display.md)
- [Debug UIレイアウト・遷移仕様](docs/spec/debug-ui-layout.md)
- [起動メニュー仕様](docs/spec/debug-boot.md)
- [DexcRemote仕様](docs/spec/remote-control.md)
- [DexcRemote 運び手仕様](docs/spec/remote-transport.md)
- [Dexc Telemetry仕様](docs/spec/telemetry.md)
- [公開APIの入口](Packages/jp.yazawa-ichio.dexc-debugger/Documentation~/api-index.md)
- [Debug Input値モデル仕様](docs/spec/debug-input-value-model.md)
- [コーディング規約](docs/spec/coding-conventions.md)

### Tools

- [テスト実行フロー](docs/tools/testing.md)
- [Unity CLI検証手順](docs/tools/unity-cli.md)
