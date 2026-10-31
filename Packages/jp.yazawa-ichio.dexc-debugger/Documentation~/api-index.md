# 公開APIの入口

公開APIを用途別に整理した索引です。型・overload・返却結果の詳細はリンク先sourceを参照してください。

| 用途 | 入口 | source |
| --- | --- | --- |
| 初期化と有効状態 | `DexcDebugger.Initialize`、`IsEnabled`、`IsInitialized` | [session](../Runtime/DexcDebugger.cs) |
| 起動時の設定 | `DexcDebuggerOptions`の`Command`（`DefaultAccess`）／`History`（`CapacityPerFeature`）／`RuntimeUI`（`Skin`） | [options](../Runtime/DexcDebuggerOptions.cs) |
| UIの見た目 | `DexcRuntimeUISkin`とprofileの`RowHeight`／`Layout`（`DexcRuntimeUILayoutMode`）／`SplitPanelSize`、font・色・描画asset | [skin](../Runtime/DexcRuntimeUISkin.cs) |
| メニュー操作と入力接続 | `DebugMenu.Open`／`Close`／`Toggle`／`IsOpen`／`SetInputProvider`／`ClearInputProvider`／`InputProvider`、`DebugMenuInputProvider`、`DebugMenuInputFrame`、`DebugMenuInputState` | [Runtime UI](../DebugUI/DexcRuntimeUI.cs) |
| Command定義 | `DebugCommandAttribute`、`DebugCommand`／`DebugCommand<TOutput>`、`AsyncDebugCommand`／`AsyncDebugCommand<TOutput>` | [Command](../Runtime/DexcCommand.cs) |
| Commandの一覧・編集・実行 | `DebugCommand.Registry`、`DebugCommand.Draft`、requestの`Dispatch`。descriptor・draft・history・result | [Command操作](../Runtime/DexcCommand.cs) |
| Command実行の寿命 | 実行context、取消とtimeout、型付きの実行結果 | [実行モデル](../Runtime/DexcCommandExecution.cs) |
| Setting定義 | `DebugSettingSet<TSelf>`（`Current`）、`DebugSettingAttribute`、`DebugRangeAttribute`、`DebugSliderAttribute`、`DebugPresetAttribute`、`DebugSettingChanges` | [Setting](../Runtime/DexcSetting.cs) |
| Boot定義 | `DebugBootSet`、`DebugBootBlock`、`DebugBootMenuAttribute`（`Tag`／`Scene`／`Groups`）、`DebugBootGroupAttribute`、`DebugBootItemAttribute`、`DebugBootGroupsValues` | [Boot](../Runtime/DexcBoot.cs) |
| Boot起動 | `DebugBootSelection.Open`／`Tag`／`Title`／`Panels`／`ResolveScene`／`Flush`／`Start`／`Reset` | [Boot](../Runtime/DexcBoot.cs) |
| パネル | `DebugPanel`／`DebugWidget`各種（Toggle／Choice／Number／Text／Button／Label）、`DebugPanels.Register`／`Unregister`／`All`／`Home`（横並び表示でmenuを開いたときに出すPanel）。Runtime menuの`Panels`ページに3rendererで表示、Editorは`Dexc Debugger`の`Panels`ページ | [Panel](../Runtime/DexcPanel.cs) |
| HUD Widget | `DebugDisplayWidget`、`DebugDisplay.RegisterWidget`／`UnregisterWidget` | [Display Widget](../Runtime/DexcDisplayWidget.cs) |
| Runtime UI 切替 | `DebugMenuRenderer`、`DebugMenu.Renderer`／`SetRenderer`／`RendererName`、`DebugGamepadInputProvider` | [Runtime UI](../DebugUI/DexcRuntimeUI.cs)、[gamepad](../DebugUI/DebugGamepadInputProvider.cs) |
| Boot UI | `DebugBootMenu.Open(tag, loadScene)`／`Start`／`Close`／`IsOpen`／`Title`／`Alignment`／`AutoStartSeconds`／`Started`／`SetInputProvider`、`DebugBootScene` component（`Tag`）、Editorの`Dexc Boot` window（`DexcBootWindow`） | [Boot Menu](../DebugUI/DexcBootMenu.cs)、[Boot Scene](../DebugUI/DebugBootScene.cs)、[Boot window](../Editor/DexcBootWindow.cs) |
| Editor操作画面 | `Dexc Debugger` window（`DexcDebuggerWindow`、menu `Window/Dexc/Dexc Debugger`） | [Debugger window](../Editor/DexcDebuggerWindow.cs) |
| 診断 | `DebugDiagnostics.GetSummaries`／`TryGetDetail`、`DebugDiagnosticQuery`、`DebugDiagnosticDetail` | [共通契約](../Runtime/DexcCommonContracts.cs) |
| 永続化 | `DebugStorage.Storage`／`Factory`／`TryLoad`／`Save`／`Delete`／`Flush`、`IDebugStorage`、`DebugPlayerPrefsStorage`／`DebugFileStorage`／`DebugMemoryStorage`、`DebugPersistentAttribute`、`DebugSettingAttribute.Persist` | [Persistence](../Runtime/DexcPersistence.cs) |
| 共通result | `DebugResult`（Status／Succeeded／Issues／DiagnosticId）、`DebugResult<T>`（＋Value／TryGetValue）、`DebugOperationStatus`。Setting／Inspectorの操作は全てこの2型で返る | [共通契約](../Runtime/DexcCommonContracts.cs) |
| Setting操作 | `DebugSetting.GetList`／`TryGetDescriptor`／`Read`／`Write`／`TryRead<T>`／`Write<T>`／`Reset`／`GetPresets`／`PreviewPreset`／`ApplyPreset`／`GetHistory` | [Setting操作](../Runtime/DexcSetting.cs) |
| Inspector公開 | `DebugFieldAttribute`、`DebugInspectorSourceAttribute`、`DebugInspector.Register`／`Unregister`。Targetは`GameObject`／`Component`／`ScriptableObject`などのasset。Pathが空なら`ArgumentException`、対象がnullなら`Debug.LogError` | [Inspector](../Runtime/DexcInspector.cs) |
| Inspector参照・変更・Watch | 登録一覧、Hierarchy／Component検索、component handleの`ReadFields`／`WriteField`、Watch | [Inspector操作](../Runtime/DexcInspector.cs) |
| Inspector対象のprocess外からの指定 | `DebugInspectorRegisteredEntry.EntityId`／`DebugInspectorHierarchyEntry.EntityId`（`ulong`）、`DebugInspector.TryGetTarget`／`TryGetObject`。handleはsessionを内包しprocess外で復元できないため、外部toolはこの値で対象を指す。再生をまたいで同じ対象を指さない | [Inspector操作](../Runtime/DexcInspector.cs) |
| HUD・World表示 | `DebugDisplay.DrawValue`／`DrawLabel`／`DrawLine`／`DrawRay`／`DrawPolyLine`／`DrawRelation`／`DrawWireSphere`／`DrawWireCube`／`DrawWireDisc`／`DrawWireSector`（static `DebugDisplay`のみ）／`Clear`、`TryGetCompletedFrame` | [Display](../Runtime/DexcDisplay.cs) |
| 表示の所有と寿命 | `DebugDisplaySubject`、`CreateSubject`、`Draw…For`、`DebugDrawOptions` | [Displayの寿命](../Runtime/DexcDisplay.cs) |
| 塗りつぶし・Collider用形状 | `DrawSphere`／`DrawCube`／`DrawDisc`／`DrawCapsule`、`DebugDrawOptions.WithStyle`、`DebugDisplayDrawStyle` | [Display形状](../Runtime/DexcDisplay.cs) |
| 共通入力 | `DebugInput<T>`と組み込み`InputBool`／`InputInt`など | [入力基底](../Runtime/DexcDebugInput.cs)、[組み込み入力](../Runtime/DexcBuiltInInputs.cs) |
| 入力の形式・制約 | `DebugInputSchema`、`DebugInputConstraint` | [schema](../Runtime/DexcDebugInputSchema.cs)、[constraint](../Runtime/DexcDebugInputConstraint.cs) |
| 値 | `DebugValue`、`DebugValueKind`、`DebugValueMember` | [値](../Runtime/DexcDebugValue.cs) |
| 入力UI拡張 | `DebugInputEditor`、対応attribute／context、`DebugMultilineAttribute` | [入力editor](../Runtime/DexcDebugInputEditor.cs)、[attribute](../Runtime/DexcSetting.cs) |

## 呼び出し側で扱う結果

- queryはpage情報と返却件数を確認し、一覧を1回だけ取得して全件と扱わないでください。
- Read／Write／draft操作は返却statusを確認します。失効したhandleや定義の消失を、成功した空値と取り違えないでください。
- Command履歴の編集復元は再実行ではありません。値を確認してrequestを作り、実行操作へ進みます。
- 取消が先に受理されたCommandは、実処理の終了を待ってCancelledとなり、Outputを返しません。
- Setting画面はクラス内の複数項目をまとめますが、既存Writeはfield単位です。クラス全体のtransaction成功を保証するAPIではありません。
- Inspectorの参照・WatchとDisplay Subjectは対象Objectの寿命を考慮します。別sessionへ古いhandleを持ち越して再利用しないでください。
- Inspectorの配列・List等の`ICollection`はreadonlyです。Value内のObjectに`count`、`items`（最大128件）、`truncated`を返し、各itemは`index`／`state`／`value`／`label`を持ちます。入れ子は展開しません。
- Settingの`Display/HUD`／`Display/Overlay`／`Display/World`は既定trueです。OFFでもDisplay snapshotの記録を継続します。ゲーム側のbool Setting `Display/Enemy/Path`はvisualの`Enemy/Path`以下をsegment単位で非表示にできます。
- 形状はWireが既定です。Solid／WireAndSolidの既定塗り色は線色のalphaの25%。Capsuleのheightは半球込みの全高です。Localの非一様scaleは近似表示なので、厳密なCollider寸法が必要ならWorld寸法を渡してください。

## assemblyの役割

`Dexc.Runtime`は4機能と共通値・設定、起動設定の定義、URPのWorld描画、`Dexc.DebugUI`はRuntimeメニューと各renderer、起動メニュー、`Dexc.Editor`はEditor操作画面です。`Dexc.Remote.Protocol`はprocess外からの操作に使うJSON-RPCの枠、`Dexc.Remote`はその操作、`Dexc.Remote.Pipeline`はUnity CLIからの呼び口で、いずれも公開APIを持ちません。画面内部のrendererやbindingを公開操作APIとして使わず、上の入口から操作します。
