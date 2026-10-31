# DebugSetting仕様

## 目的

DebugSettingは、ゲーム全体または実行環境へ継続的に適用するデバッグ用設定を表示・変更する機能である。Time Scale、difficulty、通信遅延、天候、spawn間隔などを扱う。

DebugDisplayの表示ON／OFFも継続設定として扱う。DebugDisplayは同じ設定UIを重複して持たない。

実行中の特定EnemyのHPなど、個々のobjectの現在値を直接変更する用途はDebugInspectorで扱う。

## DebugSettingSet

関連するSettingは`DebugSettingSet<TSelf>`を継承する具象classへまとめる。基底classはSet型ごとに現在値を保持する`TSelf Current`を提供する。

```text
DebugSettingSet<SoundDebug>
  ├─ Current: SoundDebug
  ├─ Setting property群
  └─ 同じSoundDebug型のPreset群
```

SettingはSet classのpublic instance propertyとして定義する。propertyはpublic getterとnon-public setterを持つ。ゲーム側は`SoundDebug.Current`のpropertyを通常のC# getterとして直接読み、Registryだけがnon-public setterを通してCurrentを変更する。

Set classはpublic parameterless constructorを持ち、property initializerをDefault snapshotの正本とする。constructor、property initializer、getter、setterでゲーム状態を変更してはならない。外部systemへの反映は`OnChanged`へ記述する。

`Current`のinstanceはstatic fieldではなく`DexcRoot`がclosed generic型ごとに1つ保持する。

- 初回参照時にpublic parameterless constructorで生成し、生成直後に永続化された値を復元する（「永続化」を参照）。
- 参照時の`DexcDebugger`のsessionが、instanceを生成または最後に参照した時のsessionと異なる場合は、前のinstanceを捨てて新しく生成し、同じく永続値を復元する。session未開始の間に参照した場合もinstanceを生成する。
- session終了時は全Scopeを戻した後、そのsessionが収集したSetのinstanceを`DexcRoot`から取り除き、次の参照で生成し直す。`DexcRoot`の作り直しでもinstanceは破棄される。
- 同じsession内では、Settingの収集が後から行われても別instanceへ置き換えず、同じCurrentを使用する。ResetやPreset適用もCurrent参照を差し替えず、property値をcopyする。同じsession内でゲーム側が保持したCurrent参照も値更新を受け取る。sessionをまたいで保持した参照は更新を受け取らない。

Current変更をAudioMixerやUnity APIへ反映する必要があるSetは、基底classの`OnChanged(DebugSettingChanges changes)`をoverrideできる。RegistryがCurrentへ変更をcopyした後に呼び、Preset instanceの生成時には呼ばない。

### ゲーム側のread

ゲーム側の通常readは`Current`からproperty getterを直接呼ぶ。Registry lookup、Reflection、`DebugValue`生成、validation、履歴、allocation、event通知を経由しない。

getterとsetterは副作用を持たず、定数時間で現在値を読み書きする契約とし、auto-propertyを標準形とする。Frameworkはgetter実装の計算量や副作用を検証せず、契約遵守はSetting実装者の責務とする。ゲーム側から履歴付きで変更する場合はproperty setterを公開せず、`DebugSetting`のWrite境界を使用する。

### 永続化

- `DebugSettingAttribute.Persist`が`true`のSetting、および`[DebugPersistent]`を付けたSetの全Settingを永続化対象とする。`Access`が`ReadOnly`のSettingは対象外とする。
- 値は`DebugStorage`へkey `Setting/<Path>`で保存する。
- `Write`、`Reset`、`ApplyPreset`は、検証に通った値をCurrentへcopyした後、`OnChanged`を呼ぶ前に永続化対象の値を保存し、`DebugStorage.Flush`を呼ぶ。`OnChanged`が例外を送出して結果が`Failed`になった場合も、保存済みの値は残る。Scope終了によるDefaultへの復帰は保存しない。
- `Current`の生成直後に、保存済みの値を読み、型とrangeを検証してからpropertyへ設定する。検証に通らない値は無視する。復元中の例外はwarningとしてlogし、そのSettingを飛ばす。この復元では`OnChanged`を呼ばない。
- sessionのSetting収集時にも同じ復元を行い、値を設定したSetの`OnChanged`を変更集合付きで呼ぶ。この復元とcallbackの失敗はdiagnosticへ記録する。

## 利用者へ提供する操作

| 利用者 | インターフェース | 入力 |
| --- | --- | --- |
| ゲーム中の人間 | 画面上のDebugメニュー | Toggle、Number field、Slider、Dropdown、Text field、Preset選択 |
| Editor上の人間 | EditorWindow | 同じクラスフォーム・適用・プリセット、検索 |
| AI・外部ツール | JSON-RPC（[DexcRemote](remote-control.md)） | Pathによるget、Preset一覧・preview。set、reset、applyは持たない |
| QA自動テスト | test adapter | 設定値またはPresetを適用し、現在値を取得 |

UIはSettingSetクラス単位の一覧と、クラス内の複数propertyを縦に並べるフォームとする。項目名は[共通フォーム](debug-ui-layout.md#共通フォーム)に従って置き（Runtimeは左に項目名、右に値。Editorは入力の直上へ全文表示）、footerにプリセット選択と「変更を適用」を置く。現在値／初期値の比較列、個別Reset、入力取消ボタンは置かない。

## 公開API

UI、AI、QAが使用する公開入口はstatic classの`DebugSetting`とする。ゲームコードが高頻度に読む経路は`DebugSettingSet<TSelf>.Current`であり、このFacadeを経由しない。

```csharp
public static DebugSettingListPage GetList(
	DebugListQuery query);

public static bool TryGetDescriptor(
	DebugSettingId settingId,
	out DebugSettingDescriptor descriptor);

public static DebugResult<DebugValue> Read(
	DebugSettingId settingId);

public static DebugResult<DebugValue> Write(
	DebugSettingId settingId,
	DebugValue value);

public static bool TryRead<T>(
	DebugSettingId settingId,
	out T value);

public static DebugResult<DebugValue> Write<T>(
	DebugSettingId settingId,
	T value);

public static DebugResult<DebugValue> Reset(
	DebugSettingId settingId);
```

`TryRead<T>`は`Read`の結果を対応型`T`へ変換して返す。Readが成功しない場合、または値を`T`へ変換できない場合は`false`を返し、`value`はdefaultとする。`Write<T>`は`value`を`T`の型で`DebugValue`へ変換してから`Write(DebugSettingId, DebugValue)`と同じ検証と書込みを行う。`T`がSettingの型と異なる場合は通常のWriteと同じく型validationで拒否する。`T`が対応型でない場合、未定義のenum値、非有限floating pointは変換時に例外となる。`DEXC_DEBUGGER`が無い場合、`TryRead<T>`は`false`、`Write<T>`は`Disabled`を返す。

`DebugSettingListPage`は`DebugCollectionSummary`、`DebugPageInfo`、不変なlist item一覧を持つ。DescriptorはSetting ID、Set ID、Path、型、Default、Scope、Apply Mode、Access、Constraint、Editor metadataを持ち、Reflection objectを公開しない。

`Read`は要求したSettingだけをcache済みgetterで読み、`DebugValue`へ変換する。`DebugResult<DebugValue>`はstatusとDiagnostic IDを持ち、成功時だけ`TryGetValue(out DebugValue value)`で現在値を返す。

公開`SettingDraft`は設けない。Submit形式の編集値、Dirty、入力途中のvalidation表示はUI stateとして保持し、Apply時に`Write`へ渡す。Immediate形式は入力確定時に`Write`する。AIとQAは常に完成した`DebugValue`を直接`Write`する。

## 1つのSettingへ与える入力

ゲーム側はSet内の1つのSetting propertyについて次を提供する。

| 入力 | 意味 |
| --- | --- |
| Path | 表示名と文字列ID。例: `Game/Time Scale` |
| Value Type | propertyの型。「値とsnapshot」の対応型のいずれか |
| Current Value | 現在適用されている値 |
| Default Value | Setのproperty initializerから得るReset値 |
| Constraints | min、max、step、選択肢 |
| Apply Mode | 入力直後、またはApply押下時 |
| Scope | Global、Scene、Sessionのいずれか |

入力はConstraintsで検証してから反映する。反映に失敗した場合は編集値を現在値として表示せず、固定エラー枠から理由を開けるようにする。理由文の挿入で行の寸法を変えない。

Settingの登録、値の範囲、UI表現は別の属性で指定する。

- `DebugSetting`: Path、ScopeなどSetting自体の定義
- `DebugRange`: min／maxのvalidation
- `DebugSlider`: 正確なNumber fieldにSlider操作を添える表示指定

`DebugRange`だけを付けた場合も範囲validationを行い、UIはNumber fieldのままとする。`DebugSlider`は表示方法だけを決め、範囲validationを兼ねない。最小値と最大値が同じ、または境界が非有限でSliderの位置を定義できない場合は、Slider操作を無効にしてNumber fieldを維持する。数値欄では型とrangeのvalidationを適用する。

### 値とsnapshot

Current、Default、Preset、履歴の値をimmutableな`DebugValue`へ完全変換できることをSettingの定義条件とする。snapshotは元のpropertyやobjectとmutable stateを共有しない。

対応型と`DebugValue`の対応は次のとおりとする。

| propertyの型 | DebugValue |
| --- | --- |
| `bool` | Boolean |
| `sbyte`、`short`、`int`、`long` | SignedInteger |
| `byte`、`ushort`、`uint`、`ulong` | UnsignedInteger |
| `float`、`double` | FloatingPoint |
| `string` | String |
| enum | String。定義済みのenum名 |
| `Color` | Object。FloatingPointの`r`、`g`、`b`、`a` |
| `Vector2`、`Vector3`、`Vector4` | Object。FloatingPointの`x`、`y`、`z`、`w`（次元数まで） |
| `Vector2Int`、`Vector3Int` | Object。SignedIntegerの`x`、`y`、`z`（次元数まで） |

Unity Object参照、collection、任意class／structは対象外とし、該当propertyは定義errorとして除外する。

数値Kind、narrowing、enum値、非有限floating pointは共通変換規則で検証する。`Convert.ChangeType`、`ToString()`、JsonUtilityによる任意class copyへfallbackしない。

`DebugValue`への変換はUI／AIによるRead、Write／Reset／Preset、Default／Preset初回収集、before／after履歴の作成時だけ行う。Current全体を毎frame比較またはsnapshot化せず、game codeによるproperty readへ変換処理を入れない。

## Preset

Presetは、同じDebugSettingSet型の完全な値snapshotである。複数のSettingへ入力する値を名前付きでまとめたものであり、独立した機能種別ではない。

1つのPresetは次を持つ。

| 入力 | 意味 |
| --- | --- |
| ID | UI、AI、QAから指定する文字列。全Setを通して一意とし、重複したPresetはすべて除外する |
| Display Name | Dropdownに表示する名前 |
| Description | 適用目的と前提条件 |
| Values | DebugSettingSet instanceのpublic property値 |

PresetはSet class上のstatic propertyとして定義し、Set型の新しいinstanceを返す。Registryは`DebugPreset`付きpropertyをReflection収集し、戻り値型がSet型と一致することを検証する。

Presetでpropertyを省略した場合もproperty initializerの値が入るため、Presetは常に完全snapshotとなる。一部propertyだけを変更するpartial presetは存在しない。

AI・QAはPreset IDを入力する。遠隔操作が提供するのはPreset一覧とPreset内容のpreviewであり、applyは持たない。

Presetの公開APIは次に限定する。

```csharp
public static DebugSettingPresetPage GetPresets(
	DebugSettingSetId setId,
	DebugPageRequest page);

public static DebugResult<IReadOnlyList<DebugSettingPresetDifference>> PreviewPreset(
	DebugSettingPresetId presetId);

public static DebugResult ApplyPreset(
	DebugSettingPresetId presetId);
```

Preset一覧はsession内の`DebugSettingPresetId`、宣言したIDの`Key`、Set ID、Display Name、Descriptionを返す。Previewは現在値との差分とvalidation issueを返すがCurrentを変更しない。Applyは同じ検証を再実行し、validation成功後にCurrentへcopyする。setter／`OnChanged`の例外では一部変更が残り得るため、全体rollbackを保証しない。

## ScopeとReset

| Scope | 有効期間 | Reset条件 |
| --- | --- | --- |
| Global | Debug環境全体 | 明示ResetまたはDebug環境終了 |
| Scene | 現在scene | scene unload |
| Session | 現在のPlay／Player session | Play／Player終了 |

Preset適用後も各SettingのScopeは変えない。異なるScopeを含むPresetは許可し、各Settingが自身のReset条件に従う。

履歴から前回値を再入力する場合も、現在のSetting descriptorで再検証する。前回値やPreset値を無条件に復元しない。

### Scope lifecycle

- Global: 明示Resetまたはdebug subsystem終了まで保持する。
- Scene: 登録時にscene handleを関連付け、scene unload時にDefaultへ戻す。
- Session: Play／Player session終了時にDefaultへ戻し、subsystem resetでCurrentを破棄する。

Current instanceがDebugSetting値を保持し、Registryがsession中のownerとなる。Scope終了時は該当propertyをDefault snapshotからCurrentへ戻し、変更集合を`OnChanged`へ通知する。

## 保持する状態

Settingの状態はsessionごとに保持し、Set単位のdescriptor、Setting descriptor、ID index、Current instance、Default snapshot、Preset catalog、変更履歴を持つ。Reflectionによる定義の収集結果はassemblyの寿命の間cacheし、sessionごとにIDを割り当ててCurrentを解決する。

UIはCurrent instanceを直接Reflection操作しない。`DebugSetting`のread／write／resetを使用する。

Set単位では次を保持する。

| 項目 | 意味 |
| --- | --- |
| Set Type | `DebugSettingSet<TSelf>`の具象型 |
| Set ID | session内のSet識別子 |
| Current | session中の現在値instance |
| Default Snapshot | property initializerから生成した値 |
| Properties | Setに属するSetting |
| Presets | 同じSet型の完全snapshot |

### DebugSettingDescriptor

| 項目 | 意味 |
| --- | --- |
| `Id` | session内のSetting ID |
| `SetId` | 所属するSetのID |
| `Path` | 表示階層と文字列ID |
| `ValueType` | propertyの型のfull name（`Type.FullName`）の文字列 |
| `DefaultValue` | Default snapshotのproperty値 |
| `Scope` | Global、Scene、Session |
| `ApplyMode` | Immediate／Submit |
| `Access` | Read-only／Read-write |
| `HasRange`、`RangeMinimum`、`RangeMaximum` | `DebugRange`の有無と範囲 |
| `IsSlider` | `DebugSlider`の有無 |

`DebugSettingListItem`は`Id`、`SetId`、`Path`、`DisplayName`（Pathの最後の階層）、`ValueType`、`Scope`、`ApplyMode`、`Access`を持つ。

`DebugSetting`、`DebugRange`、`DebugSlider`は別に収集し、validationとUI表現を混在させない。

## Reflection収集・検証

1. `DebugSettingSet<TSelf>`を継承する具象classを収集する。
2. TSelfが具象class自身と一致し、public parameterless constructorを持つことを検証する。
3. 基底classのCurrentを取得し、別の新しいSet instanceからDefault snapshotを作る。
4. `DebugSetting`が付いたpublic instance propertyを収集する。
5. public getter、non-public setter、indexerでないこと、およびSetのBase Pathとproperty Path、型、Scope、Apply Modeを検証する。
6. `DebugRange`などのconstraintが型に適合することを検証する。
7. `DebugSlider`に必要な数値型とrange情報を検証する。
8. `DebugPreset`付きstatic propertyの戻り値型とID重複を検証する。
9. Path重複がなければdescriptorとfield accessorをcacheする。

Set constructor、Preset getter、`OnChanged`の例外はRegistry境界で捕捉し、収集またはwrite failureとして返す。UI描画中にReflectionを繰り返さない。

Set型、constructor、CurrentまたはDefault snapshotを構築できない場合は該当Set全体を登録しない。個別Setting propertyのaccessor、型、constraint、editor metadataが不正な場合は該当propertyだけを除外し、同じSet内の正常なSettingは利用可能にする。Preset定義または収集時getter評価が不正な場合は該当Presetだけを除外する。PathまたはPreset IDが重複した場合は競合した全項目を除外し、Reflection順で一方を採用しない。いずれの場合も他SetとRegistry全体の利用は継続し、理由をdiagnosticへ記録する。

## Read／Write／Reset

- Readはcache済みgetterからCurrent instanceのproperty値を取得し、その要求に対する`DebugValue` snapshotを作る。
- Writeは`DebugValue`からの厳密な型変換、型validation、constraint validation、cache済みsetterによるCurrent propertyへのcopy、`OnChanged`の順に行う。
- ResetはDefault snapshotのproperty値を現在のconstraintで再検証してCurrentへcopyする。
- read-only SettingへWrite／Resetを行わない。
- write成功後に変更eventと履歴entryを発行する。

個別のWrite／Resetは`DebugResult<DebugValue>`、Preset適用は`DebugResult`を返す。validation issueは利用者が修正可能なSetting単位の理由として返し、`OnChanged`などゲーム側実装の例外はDiagnostic IDへ関連付ける。例外objectをresultへ含めない。

readとwrite系resultのstatusは次の共通の意味を持つ。

| Status | 意味 |
| --- | --- |
| `Succeeded` | read、write、resetまたはapplyが完了した |
| `Rejected` | 値、型、constraint、Preset全体の事前検証に失敗した |
| `ReadOnly` | writeまたはresetが許可されていない |
| `Stale` | 旧sessionのIDを使用した |
| `NotFound` | 現sessionに未知のIDである |
| `DefinitionUnavailable` | 収集時に検出した不正定義である |
| `Failed` | accessorまたは`OnChanged`が例外を送出した |
| `Disabled` | compile symbolにより機能が無効である |

`Rejected`はvalidation issueを、`DefinitionUnavailable`と`Failed`は任意のDiagnostic IDを返す。書込み失敗を成功値として表示せず、再度`Read`して現在値を確認する。

Setting変更eventはDebugDisplay rendererなどが購読できる。Settingは購読先の具体実装を知らない。

## Preset catalogと適用

Presetは次の不変dataとしてRegistryへ読み込む。

- ID、Display Name、Description
- Setting Pathと`DebugValue` snapshotの組

適用手順:

1. 全Pathの存在、書き込み可否、型、constraintをpreview時に検証する。
2. 現在値と適用後値のdiffを返す。
3. Apply時に再度全件を検証する。
4. validation成功後、収集済みの全Setting propertyをCurrentへcopyする。
5. 各Settingの成功／失敗をsummaryへ記録する。
6. 変更property集合を渡してSetの`OnChanged`を1回呼ぶ。

外部systemまで含むtransactionは保証しない。`OnChanged`が途中で例外を発生させた場合、Currentや適用済みの外部副作用を自動rollbackせず、反映failureを返す。

Preset propertyは毎回新しいinstanceを返す。Registryは収集時にsnapshot化し、UI表示やapplyのたびにproperty getterを呼ばない。

## UI接続

寸法・文字・フォールバックは[Debug UIレイアウト・遷移仕様](debug-ui-layout.md)と[文字配置](debug-ui-text-layout.md)に従う。

UIは`GetList`、`TryGetDescriptor`、`Read`、`Write`、`Reset`、`GetPresets`、`PreviewPreset`、`ApplyPreset`を使用する。

### クラス単位の一覧・フォーム

Runtime IMGUI／uGUI／UI ToolkitとEditorは同じ契約を使う。`GetList`の項目を`SetId`でグループ化し、クラスを選ぶと同クラスの複数項目を表示する。ラベルはPath階層の共通の親Pathを使い、公開されていないReflection型名をUIから調べない。共通親がなければ項目名で識別できる一覧表現にする。検索に一致しない同クラスの項目も詳細から到達可能とする。

Query上限とpageを守り、部分pageをクラスの全項目と誤認しない。クラス一覧の収集と、選択クラスのdescriptor／値の取得を分ける。収集中・read失敗・失効を空の正常データとして扱わない。

フォームは項目名を共通フォームの規則で置き、Vector／Color成分を縦に並べる。readonlyは同じ行高で操作不可。生の型名・Scope・現在値／初期値の比較を常設しない。エラーは固定枠から詳細へ開く（Runtime IMGUIは行の下へエラー文を書く）。

### Apply／Immediate

- Submitはfieldごとにraw text、解析値、dirty、エラーを保持する。「変更を適用」は変更済み・書込み可能なSubmit項目を表示順に個別`Write`する。同クラスの不正な入力が残る間は開始しない。
- 複数Writeを1つのtransactionとして扱わない。各resultを保存し、成功項目だけdirtyを解除する。失敗項目は入力とエラーを残し、後続の独立項目も処理する。再適用は残るdirty項目だけを対象とする。
- `OnChanged`は個別Writeごとに通知する。setter／callback失敗時は実値を再readし、失敗を消したり成功と表示したりしない。UIからrollbackを推測実行しない。
- Immediateの数値／一行文字列はEnterまたはfocus離脱による入力確定で個別Writeする。途中の空文字・符号・指数・不正値を送らない。Bool／選択／Sliderは有効な変更操作で確定する。構造値は全成分が有効なときだけ元の型の値としてWriteする。
- Immediateの成功で他のSubmitや編集中fieldを再初期化しない。外部更新を観測してもdirty入力を上書きせず、適用時は通常のWrite契約で再検証する。
- 戻る／閉じる／別クラス・別機能への移動／プリセット画面への移動では未適用入力を破棄し、適用済み値は維持する。確認や入力取消ボタンは置かない。同じフォームのEnum・エラー・全文編集・Editor表示タブ・幅切替では入力を保持する。

### プリセットと適用状態

- プリセット選択はフォーム下部の適用操作の隣に置く。選択画面は「初期値」と登録済みPresetの名前だけを列挙し、名前を選んでから明示的に適用する。現在値・適用後の値・件数・説明・全差分の常設表示、別の確認画面、人間向けのpreview画面は設けない。表示を簡略化してもbackendのvalidationを省略しない。
- 名前付きPresetは`GetPresets`から取得し、選択後の適用で`ApplyPreset`を呼ぶ。選択・閲覧だけでは変更しない。validation失敗は適用前に拒否し、callback等による一部変更は`ApplyPreset`の結果に従って再readする。
- 「初期値」は同クラスの書込み可能な項目へ個別`Reset`するUI操作であり、公開Preset IDや公開APIを持たない。readonlyを変更しない。全体transactionを持たず、各結果を集約し、失敗を明示する。書込み可能な項目がないクラスでは適用不可。
- 適用中表示は、再readした実値と`DefaultValue`／Preset previewの差分で判定する。初期値比較の対象は書込み可能な項目。名前付きPresetはpreview成功かつ差分0なら一致。不正／失効したPresetを一致とみなさない。
- 複数が一致する場合は初期値を優先し、次に安定したPreset ID順で決める。一致なしは「カスタム」、取得失敗・収集中は未判定とし、前回選んだ名前を適用中と表示しない。dirty入力は適用状態の比較に使わない。
- 数値の表記差は解析後の`DebugValue`で比較する。型の異なる数値を丸めて同値にせず、64bit整数・構造成分を保持する。

## HistoryとAI／QA

変更履歴はSetting ID、Path、before／afterの`DebugValue` snapshot、操作種別、UTCの`DateTimeOffset`による実行時刻、resultを保持する。callerは保持しない。履歴はSettingごとの有限pageとして取得する。

```csharp
public static DebugSettingHistoryPage GetHistory(
	DebugSettingId settingId,
	DebugPageRequest page);
```

履歴値を復元する専用操作は設けない。UIがbeforeまたはafterを選び、現在のdescriptorで通常の`Write`へ渡すことで再検証する。AI／QAも同じIDと`DebugValue`でread／write／resetを行い、UIの編集値や前回値を暗黙利用しない。
