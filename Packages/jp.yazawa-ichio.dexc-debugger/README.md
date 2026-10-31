# Dexc Debugger

UnityのCommand実行、Setting編集、実行中ObjectのInspector、画面・空間へのデバッグ表示をまとめるpackageです。

## 導入

1. Unity Package Managerの「Add package from git URL」へ`https://github.com/yazawa-ichio/dexc-debugger.git?path=Packages/jp.yazawa-ichio.dexc-debugger#v0.1.0-pre.1`を入力します。repositoryをcloneした場合は「Add package from disk」で、このディレクトリの`package.json`を選びます。
2. Unity 6000.3で使用します。packageに必須の依存はありません。World描画（3D図形）はURP（`com.unity.render-pipelines.universal`）が入っているprojectでだけ有効になるため、使う場合はURPを導入し、Render Pipeline Assetも設定してください。URPがなくてもメニュー・HUD・Setting・Command・Inspector・Boot Menuは使えます。
3. 利用するbuild targetのScripting Define Symbolsに`DEXC_DEBUGGER`を追加します。packageは自動でsymbolを追加しません。
4. featureを使う前に、project側から明示的に初期化します。

```csharp
#if DEXC_DEBUGGER && !DISABLE_DEXC_DEBUGGER
using Dexc;
using UnityEngine;

public static class ProjectDebugBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        DexcDebugger.Initialize();
    }
}
#endif
```

`DebugMenu.Open()`／`Toggle()`／`Close()`をprojectの入力やボタンへ接続します。Editor画面は「Window > Dexc > Dexc Debugger」です。Commandなど実行中の状態を扱う操作はPlay Modeで利用します。

## 描画と入力

| 選択 | 前提 |
| --- | --- |
| 既定のIMGUI | 追加UI package不要 |
| `DEXC_RUNTIME_UGUI` | `com.unity.ugui`を別途導入 |
| `DEXC_RUNTIME_UITOOLKIT` | 有効なSkinとPanelSettings template。uGUI指定との併用時はこちらを優先 |

Skinは「Assets > Create > Dexc > Runtime UI Skin」で作り、`Initialize(options => options.RuntimeUI.Skin = skin)`へ渡します。profileごとの行高・サイズ、広い画面で一覧とページを横に並べるか（`Layout`、既定は`Single`）とそのときのサイズ（`SplitPanelSize`）、色、fontなどを設定できます。World Visual ShaderもSkinから参照します。Skinを指定しないIMGUI／uGUIは基本表示を使えますが、World描画は有効になりません。UI Toolkitの前提が不足する場合はIMGUIへfallbackします。

Skinと描画用assetはprojectのserialized fieldから明示的に保持してください。日本語表示には必要な文字を収録したfontを指定します。Skinは初期化時にsnapshotされるため、Play中のasset変更はそのsessionへ自動反映されません。

gamepadなどの操作は`DebugMenuInputProvider`で接続します。数値入力の小数点はOS言語によらず`.`です。

## 無効化と配布境界

`DEXC_DEBUGGER`が未定義ならassemblyは残り、機能はDisabledになります。`DISABLE_DEXC_DEBUGGER`を定義すると`Dexc.Runtime`／`Dexc.DebugUI`／`Dexc.Editor`／`Dexc.Remote.Protocol`／`Dexc.Remote`／`Dexc.Remote.Pipeline`を除外します。両方定義した場合も除外が優先します。除外する構成では上の例のように、Dexc型を使う宣言・usingも含めてguardするか、連携asmdefへ`!DISABLE_DEXC_DEBUGGER`制約を付けてください。

製品buildではDisabledと除外のどちらを使っても構いません。Disabledはassemblyを残したまま機能を止め、除外はassemblyごと外します。

`com.unity.pipeline`を導入している場合、Unity CLIから操作するための`Dexc.Remote.Pipeline`はEditorと、Pipelineを組み込んだbuild（`ENABLE_RUNTIME_PIPELINE_DEV`または`ENABLE_RUNTIME_PIPELINE`の定義時）にだけ含まれます。`com.unity.pipeline`自身と同じ条件で、組み込みを有効にしない限り製品buildには入りません。

package本体にはrepositoryのtest assemblyや検証driverを含めません。Input SystemとuGUIは本体の直接必須依存ではありません。

## sampleとAPI

sample「Stealth Stage Demo」はpackageに含めず、repositoryの`Assets/Samples/StealthStageDemo`に置いています。sampleにはInput SystemとuGUIが必要です。

[公開APIの入口](Documentation~/api-index.md)から、用途ごとの型とsourceへ進めます。
