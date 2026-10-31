# DexcDebugger スコープ

4機能とRuntime／Editor UIの範囲を定める。

## 概要

Unity上のゲームコードが提供したDebugCommand、DebugSetting、DebugInspector、DebugDisplayを、実機Runtime UIとUnity Editorの両方から利用できる。

```text
ゲーム側定義・登録
    ↓
4機能のRegistry
    ↓
機能別read API／operation request
    ↓
Runtime UI・EditorWindow・Player画面描画
```

外部通信がなくても4機能は完結する。QA／AI adapterは同じbackendを使用する。

## Unity前提

基本セットはUnityEngineへ依存する。4機能のUnity非依存Core（`Dexc.Core`）は設けない。Unity object、scene、frame、game time、main thread、Play Mode lifecycleは`Dexc.Runtime`の基本契約として扱う。Unity非依存のassemblyは、外部通信の枠とJSONを扱う`Dexc.Remote.Protocol`（`noEngineReferences: true`）だけとする。

### 対応基準

- 対応系列はUnity 6.3 LTSとする。
- 最低保証versionは`6000.3.20f1`とする。それより前のpatch versionは動作保証対象にしない。
- 確認環境はUnity EditorのPlay Mode、Windows Player、Android Playerとする。
- 共通仕様ではPlayer platform全体の動作保証を宣言しない。remote RPCなどのplatform依存機能は、各機能の仕様で保証対象と確認環境を個別に定義する。
- packageは必須依存を宣言しない。URP、uGUI、Input System、`com.unity.pipeline`はoptionalとし、Version Defines（`DEXC_HAS_URP`、`DEXC_HAS_UGUI`、`DEXC_HAS_INPUT_SYSTEM`、`DEXC_HAS_PIPELINE`など）で有無を判定する。
- World Visualを描画するPlayer rendererの対象render pipelineはUniversal Render Pipeline（URP）とする。

## module構成

| assembly | 参照 | 責務 |
| --- | --- | --- |
| `Dexc.Runtime` | URP Runtime／Core Runtime | 4機能の契約、Reflection、descriptor、実行・変更・描画data、履歴、Unity lifecycle、renderer-neutralな`DexcRuntimeUISkin`契約、DebugDisplayのURP Player renderer、起動メニューの定義と値、`DebugPanel` |
| `Dexc.DebugUI` | `Dexc.Runtime` | Runtime Debug Menu（`DebugMenu`）、IMGUI／uGUI／UI Toolkit renderer、入力focus、画面遷移、`DebugGamepadInputProvider`、起動メニュー（`DebugBootMenu`、`DebugBootScene`） |
| `Dexc.Editor` | `Dexc.Runtime` | IMGUIによるEditorWindow（Dexc Debugger、Dexc Boot）、Unity選択同期、diagnostic表示 |
| `Dexc.Remote.Protocol` | なし（`noEngineReferences: true`） | JSON-RPCの枠とJSONの読み書き |
| `Dexc.Remote` | `Dexc.Runtime`、`Dexc.DebugUI`、`Dexc.Remote.Protocol` | 遠隔操作の操作層 |
| `Dexc.Remote.Pipeline` | `Dexc.Remote`、`Dexc.Remote.Protocol`、`Unity.Pipeline`、`Unity.Pipeline.Attributes` | Unity CLIの運び手 |

4機能を別packageには分けない。機能ごとの内部責務は分離するが、Unity基本セットとして同じ`Dexc.Runtime`に含める。

### Assembly構成

- package IDを`jp.yazawa-ichio.dexc-debugger`とする単一UPM packageとする。
- public namespaceは全assemblyで`Dexc`とする。
- `UnityEditor`へ依存する処理だけを`Dexc.Editor`（Editor platform限定）へ分ける。`Dexc.Editor`は`Dexc.DebugUI`を参照しない。
- 全assemblyのDefine Constraintsに`!DISABLE_DEXC_DEBUGGER`を持つ。`DISABLE_DEXC_DEBUGGER`が定義されたbuild targetではDexc assembly自体をcompileも参照もせず、packageをassembly単位で無効化する。
- `Dexc.Remote.Pipeline`はDefine Constraintsに、`Unity.Pipeline`と同じ条件も持つ（[運び手仕様](remote-transport.md)）。
- `DISABLE_DEXC_DEBUGGER`を使用するproject側assemblyは、Dexc型を参照するコードまたはassemblyも同じsymbolで除外する責任を持つ。`ConditionalAttribute`だけではDexc型への参照を解決できない。
- 任意packageの有無はVersion Definesで判定する。`Dexc.Runtime`は`com.unity.inputsystem`（1.0以上）で`DEXC_HAS_INPUT_SYSTEM`、`com.unity.render-pipelines.universal`で`DEXC_HAS_URP`、同17.6以上で`DEXC_URP_COMPATIBILITY_REMOVED`を定義する。`Dexc.DebugUI`は`com.unity.ugui`（1.0.0以上）で`DEXC_HAS_UGUI`、`Dexc.Remote.Pipeline`は`com.unity.pipeline`で`DEXC_HAS_PIPELINE`を定義する。任意packageの型はこれらのsymbol内でだけ参照し、任意連携ごとのasmdefは作らない。
- test codeとtest assemblyはrepositoryの`Assets`以下へ配置し、配布UPM packageへ含めない。
- uGUI版、UI Toolkit版のrendererは`Dexc.DebugUI`に含め、依存packageの有無をVersion Definesで判定する。uGUI／UI Toolkitのlayout targetは[Debug UIレイアウト](debug-ui-layout.md)に従う。
- public `DexcRuntimeUISkin`と`DexcDebuggerOptions.RuntimeUI.Skin`は`Dexc.Runtime`へ置き、uGUI／UI Toolkit型を公開fieldへ含めない。Skinのrenderer-neutralなprofile／palette／Unity asset参照を`Dexc.DebugUI`の各rendererが解釈し、RuntimeからDebugUIへのassembly参照を作らない。
- Runtime用Skin／Texture／Material／Shaderはruntime `Resources`、Preloaded Assets、Always Included Shadersへ置かず、`DEXC_DEBUGGER`有効なgame側bootstrapから参照された場合だけPlayerへ含める。

## DebugCommand

範囲は次のとおりとする。

- 専用Command classのruntime Reflection収集
- `IDebugInput`または`DebugInput<T>`を使用するpublic instance fieldによる入力
- Input Schemaと`DebugValue`による組み込み／ユーザー定義Inputの共通処理
- 戻り値なし／任意の型付きOutput
- 同期／非同期の別契約
- 同期／非同期とOutput有無で分かれる4つの公開Command基底class
- `DebugCommand`直下へstatic操作methodを置かず、`Registry`／`Draft`のnested static classへ利用箇所別に分ける公開境界
- Command instanceまたはUI draftからimmutable requestを生成し、requestの`Dispatch`／`DispatchAsync`拡張メソッドで実行する境界
- `Default`／`ReadOnly`／`Lock`によるCommand access宣言とsession全体のreader／writer排他
- 対象なしCommand
- Command当たり0個以上の`InputRef<T>` fieldと、InputRefごと1件を選ぶ階層候補
- keyなし／Key付き登録Sourceとinline Source
- 複数InputRef、入力変更通知、field initializer、前回入力によるdraft
- collector形式の実行可能validation
- field validation、実行、cancel、result
- session履歴

任意methodへの属性、delegate、lambda、動的Command、条件一致対象への一括適用を対象選択契約へ含めない。一括処理の条件は通常のCommand入力とし、対象列挙はCommand実装内で行う。

詳細は[DebugCommand詳細設計](debug-command.md)を正本とする。

## DebugSetting

範囲は次のとおりとする。

- `DebugSettingSet<TSelf>` classとpublic getter／non-public setterを持つinstance propertyのReflection収集
- bool、整数型、float、double、string、enum、Color、Vector系
- game codeからRegistryや`DebugValue`を経由せず`Current` propertyを直接読む高速経路
- Default、Scope、即時反映／Apply反映
- RangeとSliderの独立metadata
- Reset
- 同じSet型のstatic Preset property収集、preview、apply
- session内の変更履歴
- Display表示切替用Setting

Presetの外部file読込、project間共有、永続user profile、複数Settingのtransactionは含めない。

詳細は[DebugSetting詳細設計](debug-setting.md)を正本とする。

## DebugInspector

範囲は次のとおりとする。

- instanceの手動Register／Unregister
- Reflectionで収集した任意のSourceによる候補提供
- Scene Hierarchyからの遅延探索と名前／型検索
- 登録memberの優先表示と編集
- 未登録Componentの安全な読み取り専用表示
- 明示的な追加操作を経た未登録member編集
- Watchの追加、更新、解除
- session内の書込み履歴

InspectorのManual登録instanceをCommand対象として自動流用しない。`DebugInspectorSource`とCommandの`InputRef<T>`は、対象型、任意Key、候補entryを扱う内部Source catalogだけを共有できる。InspectorとCommandのRegistry、UI入口、履歴、対象handleは統合しない。

詳細は[DebugInspector詳細設計](debug-inspector.md)を正本とする。

## DebugDisplay

範囲は次のとおりとする。

- HUD／OverlayのDrawLabel、DrawValue
- WorldのDrawLine、DrawRay、DrawPolyLine、DrawWireSphere、DrawWireCube、DrawWireDisc、DrawWireSector、DrawSphere、DrawCube、DrawDisc、DrawCapsule、DrawRelation
- 1 frame表示
- `TimeScope`によるRealtime、GameTime、Infinity表示
- global描画関数と`Clear`
- object単位の`DebugDisplaySubject`
- Subject metadataとVisualの同一snapshot化
- URP Player画面用のRuntime World renderer（`Dexc.Runtime`、`DEXC_HAS_URP`のときだけcompile）
- Player rendererと同じcompleted snapshotを閲覧するEditor detail
- AI／QAが取得できる直近完了frame snapshot
- 件数上限、期限切れ、Subject破棄によるcleanup

DebugDisplay専用の操作一覧、Visual単位handle、時系列Graph、Icon、永続保存、remote描画命令は含めない。人間向け表示切替はDebugSettingで行う。

詳細は[DebugDisplay詳細設計](debug-display.md)を正本とする。

## UI

### Runtime

- Runtime操作画面の公開入口は`DebugMenu.Open()`、`DebugMenu.Close()`、`DebugMenu.Toggle()`とする。開閉状態は`DebugMenu.IsOpen`で読める。menuを開く入力や画面上のボタンはpackageが持たず、projectが自身の入力またはUIからこれらを呼ぶ。
- `DexcDebugger`はpackage有効状態だけを公開し、4機能やRuntime MenuのFacadeにはしない。
- Runtime Debug Menuの開閉とDebugDisplayのHUD／Overlay／World rendererを分離する。Menuを閉じても有効なDisplay描画は継続する。
- HomeからCommand、Setting、Inspector、History、Diagnostics、Panelsへpush遷移
- IMGUI／uGUI／UI Toolkitの3 rendererと、`DebugMenu.SetRenderer`による実行中の切替
- 起動前に起動設定を編集して開始する起動メニュー（[起動メニュー仕様](debug-boot.md)）
- touch／gamepad／keyboardで一方向に完結する操作
- provider未設定でも、pointer（mouse、touch）とtext入力は各rendererのUI eventで受け取り、tapやclickでRuntime UIを操作できる。
- gamepadとproject固有入力は、`DebugMenu.SetInputProvider`で明示した単一active provider経由でnavigation、submit、cancelを操作する。別providerの競合は現在providerの暗黙置換にせずdiagnosticとする。入力backend（新Input System、Legacy Input Manager）ごとの標準providerは設けず、組み込みproviderは`Dexc.DebugUI`の`DebugGamepadInputProvider`だけとする。
- menu close、text focus、provider解除、session再初期化に応じて、providerがgameplay inputを停止・復帰する。
- text入力中のgame input抑止
- virtualized listと遅延Hierarchy
- Command実行、Setting編集、Inspector観測・変更
- HUD／Overlay／WorldのDisplay描画

### Editor

- dock可能なEditorWindow
- Navigation、List／Hierarchy、Detail、下部panel
- Unity Selection同期とlock
- Command、Setting、Inspectorを同時比較しやすい広域layout
- Player rendererのsnapshot／diagnostic閲覧（Editor独自のWorld描画なし）
- Reflection diagnostic表示

Play Mode外でのDebugCommand実行は対象外とする。

詳細は[Debug UIレイアウト・遷移仕様](debug-ui-layout.md)を正本とする。

## 機能別read API

UI専用のReflectionやinternal Registry直参照を使わず、4機能のFacadeから用途別のread APIと操作requestを公開する。総合的なbackend facadeと汎用観測APIは設けない。

- descriptorとdiagnosticの一覧
- draftとvalidation
- running、result、history
- Setting current／defaultとpreset preview
- Inspector instance／field／Watch
- Display completed-frame snapshot

Validation issueとDiagnostic IDは4機能で共通の小さな情報型を使用する。Command実行は`CommandExecutionResult`、Setting／Inspectorの操作は`DebugResult`／`DebugResult<T>`で返す。

Reflection catalogは`Disabled`、`Ready`、`Unavailable`の収集状態を公開し、定義Errorが存在する部分成功も`Ready`として扱う。各機能の一覧結果は共通の`DebugCollectionSummary`を含むが、descriptor一覧と操作状態は機能別に保つ。検出できた除外定義はDiagnostic IDとともにsession中保持し、未知のPathと区別する。

一覧、page、展開node、Watch、完了frameは機能別の同期read APIで取得する。共通Token、Version、`Changed`／`Unchanged`／`Stale`、汎用delta protocolは公開しない。対象失効は機能固有resultで表す。AI／QA readもqueryで範囲を制限しつつ応答自体は自己完結させる。

外部adapterはこのbackendをUnity main threadから同期的かつ機械的に利用する。thread切替、request queue、通信Cancellation、送信はadapter側の責務とする。

## Reflectionとlifecycle

- EditorとPlayerで同じruntime Reflection経路を使用する。
- 各Registryは初回利用時の収集結果をcacheする。
- script compile後はUnityのassembly reloadとstatic resetを経て、次の利用時に収集し直す。明示Refresh APIやassembly load監視は設けない。
- libraryが持つmutable staticは`DexcRoot`だけとする。`DexcRoot`はSubsystem RegistrationとEditorのEdit Mode進入時に作り直し、Domain Reload無効時も前Play sessionの状態を引き継がない。`DebugStorage.Factory`は作り直しをまたいで保持する。
- `DexcDebugger.Initialize`は明示的に新しいsession generationを開始する。暗黙のsession初期化とpackage自身によるsession自動初期化は行わない。package内部のRuntime driver生成、render callback登録、UI state resetだけを行うroot bootstrapはsessionを作成せず、この契約に含まれない。
- 再呼出し時はOptions callbackと検証より先に旧sessionのOptions、cache、handle、request、draft、実行追跡、履歴、Watch、Display状態を破棄する。初期化失敗時は旧sessionへrollbackせず未初期化状態を維持する。
- Cancelまたは再初期化後に遅延完了した旧Commandは、新sessionのresult、history、diagnostic、実行access gateを変更しない。
- `DexcDebugger.IsInitialized`で、Enabledだが明示初期化前または初期化失敗後の状態を副作用なく確認できる。
- scene unload、instance破棄、unregisterでsession handleを失効させる。staleな対象は操作しない。
- 公開APIはUnity main threadからだけ呼ぶ。別threadからの呼出しはサポートせず、runtimeで検査もしない。
- Reflection収集の入口Attributeは`PreserveAttribute`を継承し、`ConditionalAttribute("DEXC_DEBUGGER")`を付与する。symbol有効時は収集対象を保持し、無効時はAttribute使用とDexcDebuggerだけを参照する型をstripping可能にする。
- Reflection収集で定義Errorを検出しても、該当する最小単位だけを除外し、正常なCommand、Setting、Inspector Source／field、Input editorの利用を継続する。競合定義は順序で選ばず、競合した全項目を除外する。不正な定義や一部assemblyの読込み失敗はRegistry全体を未初期化にせず、対象単位のdiagnosticとする。
- strippingによって列挙対象そのものが消えた場合はruntimeで検出しない。入口Attributeで保持するが、利用者拡張を含むすべての保持をlibraryだけで保証しない。
- `DEXC_DEBUGGER`は利用projectがbuild targetごとのScripting Define Symbolsへ明示的に追加する。packageによる自動追加や、`UNITY_EDITOR`／`DEVELOPMENT_BUILD`による暗黙有効化は行わない。
- `DEXC_DEBUGGER`も`DISABLE_DEXC_DEBUGGER`もない場合、Dexc assemblyは存在するが機能状態を`Disabled`とする。RegistryはReflection収集、登録、操作、描画を行わず、機能別read APIとUIへ「定義0件」ではなく無効状態を返す。
- 両symbolが定義された場合は`DISABLE_DEXC_DEBUGGER`を優先し、Define ConstraintsによってDexc assemblyをcompile対象外にする。
- 共通公開API`DexcDebugger.IsEnabled`は`public static bool`のread-only propertyとし、Enabledなら`true`、assemblyが存在するDisabled状態なら`false`を返す。`const`にはせず、副作用を持たせない。

## 履歴と初期値

- Command、Setting、Inspectorは機能別の有限session履歴を持つ。
- UIのHistory画面は3機能の履歴を時刻順へ投影する。
- DebugDisplayは履歴へ入れず、直近の完了frame snapshotだけを保持する。
- Command入力はfield initializerを既定値とし、明示操作で前回入力を復元できる。
- Settingは定義Defaultと現在値を区別する。
- InputRef依存のCommand初期値は、InputRef変更後の入力変更通知で計算する。
- 履歴をPlay session外へ永続化しない。

## 規模と資源

- frameごとにReflectionを再走査しない。
- history、operation diagnostic、Watch、Display buffer、query結果の上限は有限とする。公開Optionsで設定できるのはHistory上限であり、その他は[機能設計のOptions](feature-design.md#options)のとおりinternal既定値とする。
- Registry descriptor数は容量によって正常定義を除外しない。

## 既定値

| 項目 | 既定値 |
| --- | --- |
| History | `100` |
| Operation diagnostic | `100` |
| Watch | `64`／frame updates `64` |
| Display | subject `256`／metadata `32`／subject visuals `256`／frame visuals `2048`／PolyLine points `256` |
| Query | page `100`／Hierarchy children `100`／search results `100` |
| Hierarchy searchの内部frame budget | `64 GameObjects/Update` |
| Runtime IMGUIの内部baseline | safe-area margin `12 px`、panelはprofileごとの既定サイズ（[Runtime UI layout](debug-ui-layout.md)の表）、list row `28 px` |

## スコープ外

remoteに関する項目は[DexcRemote仕様](remote-control.md)と[DexcRemote 運び手仕様](remote-transport.md)を正本とし、そちらで扱う範囲を定める。

- screenshot、video、大量log、memory dumpなどのfile生成・file transfer
- 認証、権限
- source generator、build時catalog、生成factory
- 4機能のUnity非依存Core
- 複数Registry instance
- 永続履歴、cloud同期、user profile（例外: Runtime メニューのお気に入りと横並び表示で最後に開いたセクション。コマンドのパスと設定グループの見出しを `DebugStorage` の `UI/Favorites/*` に、セクションを `UI/LastSection` に保存する）
- 任意memory viewer
- 条件に一致するobjectを共通機構で自動解決する機能
- DebugDisplayの録画／再生
- QA test runner、assertion framework
- Play Mode外のDebugCommand実行
- Unity `6000.3.20f1`より前の完全互換
