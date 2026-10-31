# DebugInspector仕様

## 目的

DebugInspectorは、実行中objectの現在値を確認・変更・監視する一つの機能である。

対象を開く入口は次の2分類とする。

1. 登録済み対象（Registered）: Playerボタンや近いEnemy一覧など、ゲーム用途に合わせた入口
2. Scene Explorer: Scene Hierarchyと検索から任意GameObjectを調べる入口

両者は別機能ではなく、対象を選んだ後は同じDebugInspectorでComponentとfieldを表示する。

## 公開APIの流れ

Inspectorの公開readは次の流れに限定する。

```text
RegisteredまたはScene
    ↓
Target Handle
    ↓
Component一覧
    ↓
Field一覧
```

Target、Component、Fieldはsession内handleまたはIDで参照し、Unity Object、`Type`、`FieldInfo`をUIやadapterへ公開しない。

## 入口の分類

実機のInspectorトップ画面で`Registered`と`Scene`を別項目として表示する。一方を他方のfallbackや優先順位として扱わない。

```text
DebugInspector
  ├─ Registered
  │   └─ Category → 対象候補 → Component／Field
  └─ Scene
      └─ Loaded Scene → Hierarchy／Search → Component／Field
```

## Registered

Player、Enemy、Spawnerなどを、ゲーム側がCategoryとして登録する。基本形は各instanceの生成・破棄に合わせた手動登録・解除とし、ゲーム側へ候補一覧の実装を要求しない。

### 手動登録

| 入力 | 意味 |
| --- | --- |
| Path | `Player`、`Enemy/Nearby`などの表示階層 |
| Target | 登録する具体的なinstance。`GameObject`、`Component`、`ScriptableObject`などのassetを受け付ける |
| Label | 名前、game ID、type、距離などの表示 |
| Fields | 対象選択後に優先表示する値 |

`Register(Path, Target, Label)`と`Unregister(Path, Target)`はUnity main threadで呼ぶ戻り値なしのstatic関数とし、`ConditionalAttribute`で呼び出しを除外できる。対象の有効化時に登録し、無効化または破棄時に同じPathとTargetで解除する。解除後は候補一覧から除く。

- `Component`を登録した場合は所有するGameObjectをInspector対象とし、そのGameObjectのComponent一覧へ進む。
- Componentを持たない対象（asset）は、対象自身を1つのComponentとして扱い、その対象のfieldを表示する。
- Register済みの同じPath／Targetへ再登録した場合はLabelを更新する。重複登録されたentryはPathとTargetの組で1件に正規化する。
- UnregisterされていないTargetが破棄された場合は次回maintenanceで除去する。
- Unregister対象が存在しなくても例外にしない。
- sessionが無いときのRegister／Unregisterは何もしない。ゲームはDexcの有無を確かめずに、対象の有効化・無効化で呼べる。
- 空Pathは呼び出し側の誤りとして`ArgumentException`を投げる。
- nullまたは破棄済みのTargetは登録せず、diagnosticへ記録し、`Debug.LogError`を出す。

Registry内部ではUnity Objectのfake nullを含めて生存判定する。

### Sourceによる登録

ゲーム側がすでに候補一覧を管理している場合は、任意の機能としてSourceを定義できる。Sourceは手動登録を置き換えるものではなく、同じRegistered候補を供給する補助経路である。

Source memberへ`DebugInspectorSource`を付け、対象instance、Label、任意の階層Pathを持つ`DebugSourceEntry<T>`を列挙して返す。LabelはSourceの戻り値に含め、対象classへ`DebugLabel` memberを要求しない。

`DebugInspectorSource`にはInputRefから参照する任意のKeyを指定できる。Keyを持たないSourceは`new InputRef<T>()`から、Key付きSourceは`new InputRef<T>(key)`から、型`T`のentryだけが候補になる。InspectorとInputRefはSourceを共有するが、RegistryとUI入口は共有しない。

Sourceの要件:

- `DebugInspectorSource`を付けたstatic memberである
- 引数なし
- 対象instance、Label、任意Pathの`DebugSourceEntry<T>`列を返す
- 呼び出し時点の候補snapshotを返す
- 重複Targetを返さない

Source descriptorは対象型、Inspector Path、任意のRef Keyを持つ。同じ対象型とKeyの重複はRef lookup errorとするが、Inspector Pathが異なる複数Source自体は許可する。

Sourceは画面を開いた時、Refresh時、AI／QA request時に評価する。毎frame評価しない。Source例外は該当Sourceのfailureとして表示し、他Categoryを利用不能にしない。

Sourceのsignature、対象型、Keyが不正な場合は該当Sourceだけをcatalogから除外する。同じ対象型とKeyが競合した場合は競合したSourceをすべてRef lookupから除外し、一方をReflection順で採用しない。他のSource、手動登録、Scene Explorerは利用可能なまま維持する。

### Registered候補のread

手動登録とSourceの評価結果は、次の1つのread APIからRegistered候補として返す。

```csharp
public static DebugInspectorRegisteredPage GetRegistered(
	DebugListQuery query);
```

`DebugInspectorRegisteredPage`は`DebugPageInfo`と不変なentry一覧を持つ。各entryはTarget Handle、Path、Label、Origin（Manual／Source）、Scene情報、Aliveを持つ。Source descriptor自体を列挙するAPIは設けない。

## Scene Explorer

現在loadされているsceneのHierarchyを表示する。`DontDestroyOnLoad`は独立rootとして扱う。

```text
Loaded Scene
  → GameObject Hierarchy
    → Component
      → Field
```

```csharp
public static DebugInspectorSceneList GetScenes();

public static DebugInspectorHierarchyPage GetHierarchyChildren(
	DebugHierarchyChildrenQuery query);
```

Hierarchy readはScene rootまたは指定GameObjectのdirect childrenだけを返す。各nodeはTarget Handle、Scene Handle、Label、Hierarchy Path、Active、子の有無を持つ。再帰的なHierarchy全量取得は行わない。

- Unity scene APIからLoaded Scene、root GameObject、childrenを遅延取得し、nodeを展開した時だけdirect childを列挙する。
- nodeはsession handle、scene handle、instance ID、hierarchy pathを持つ。
- GameObjectの生成、破棄、親変更、scene load／unloadを反映し、scene load／unloadとGameObject破棄でcacheを無効化する。

### 検索

検索はScene Explorer内でHierarchyを絞る入力として提供する。

| 条件 | 入力 |
| --- | --- |
| Scene | Loaded Sceneの選択 |
| Name | GameObject名の部分一致 |
| Component Type | 型名の候補選択 |
| Active | All、Active、Inactive |

検索結果にはscene、hierarchy path、GameObject名、Component型を表示する。同名候補を自動選択しない。

```csharp
public static DebugResult<DebugInspectorSearchId> StartSearch(
	DebugInspectorSearchQuery query);

public static DebugInspectorSearchPage GetSearchResults(
	DebugInspectorSearchId searchId,
	DebugPageRequest page);

public static void CancelSearch(
	DebugInspectorSearchId searchId);
```

検索pageは`Searching`、`Completed`、`Cancelled`、`Failed`の状態、検査済み件数、総数確定状態、打切り状態、途中結果を持つ。`CancelSearch`は冪等かつ`ConditionalAttribute("DEXC_DEBUGGER")`を適用できる戻り値なしAPIとする。

- 検索は指定scene内のGameObjectを走査するが、field値までは読まない。
- 検索は複数frameへ分割し、Runtimeで長時間main threadを占有しない。1 `Update`あたり最大`64 GameObjects`をinspectする。この値は内部budgetであり、公開Optionsではない。
- search resultの上限は`100`件とする。この値は内部値であり、公開Optionsではない。上限に達した検索は打切り状態とする。

## Componentとfield

どちらの入口から対象を選んでも、GameObjectのComponent一覧を表示する。

- Registeredで登録されたComponent: 登録fieldを表示名、順序、制約付きで優先表示する。
- Scene Explorerで見つけた未登録Component: instance fieldをReflectionで表示する。

対象選択後のComponent一覧APIは入口に依存しない。

```csharp
public static DebugInspectorComponentPage GetComponents(
	DebugInspectorTargetHandle target);
```

Component entryは`DebugInspectorComponentHandle`、型名、表示名、登録済み`DebugField`の有無、Alive、任意のDiagnostic IDを持つ。Component Handleは現在session中のComponentを操作する参照であり、Field一覧、Read、Write、Watch追加、Field履歴をinstance memberとして提供する。

```csharp
public readonly struct DebugInspectorComponentHandle
{
	public DebugInspectorFieldPage GetFields(
		DebugPageRequest page);

	public DebugResult<IReadOnlyList<DebugInspectorFieldValue>> ReadFields(
		IReadOnlyList<DebugInspectorFieldId> fields);

	public DebugResult<InspectorValueSnapshot> WriteField(
		DebugInspectorFieldId field,
		DebugInspectorWriteValue value,
		DebugInspectorWriteAccess access);

	public DebugResult<DebugInspectorWatchId> AddWatch(
		DebugInspectorFieldId field,
		TimeSpan interval);

	public DebugInspectorHistoryPage GetHistory(
		DebugInspectorFieldId field,
		DebugPageRequest page);
}
```

Handle自身はUnity Object、descriptor、履歴、Watchを所有しない。session identityとComponent identityで対象を解決する。旧sessionまたは対象消失後のHandleは各resultの`Stale`を返し、同名Componentへ再接続しない。default Handleなど構造的に不正な引数だけを例外とする。

Field entryはField ID、表示名、型表示、登録状態、`DebugFieldAccess`を反映したread／write可否、constraint、editor metadataを持つ。`GetFields`は値を読まず、表示中fieldの値だけを`ReadFields`でまとめて取得する。

登録済み`DebugField`を先に表示し、Scene Explorerから開いたComponentでは未登録instance fieldも続けて列挙する。

### DebugFieldAccess

登録済み`DebugField`の変更可否は`DebugFieldAccess`で指定する。

```csharp
public enum DebugFieldAccess
{
	Default,
	ReadOnly,
	Locked,
}
```

- `Default`は登録fieldの通常accessとし、書込み可能なfieldは通常のWrite経路で変更できる。
- `ReadOnly`は値を表示するが、通常のUIとAPIから書き込めない。
- `Locked`はUIの明示的な編集modeへ切り替えても書き込めない固定指定とする。

`DebugFieldAttribute.Access`の既定値は`Default`とする。readonly、const、static fieldは属性指定にかかわらず読み取り専用であり、書込み可能へ緩和しない。未登録fieldの明示編集modeは別の操作規則であり、登録済みfieldのAccessを変更しない。

### 型ごとの表示

| field型 | 表示・入力 |
| --- | --- |
| bool | Toggle |
| 数値 | Number fieldまたはSlider |
| string | Text field |
| enum | Dropdown |
| Unity標準struct | Vector、Colorなどの複合field |
| Unity Object参照 | Registered候補またはScene Explorerから選択 |
| collection | 件数と要素の読み取り |

### 登録field descriptor

`DebugField`付きmemberは型単位でReflection収集し、descriptorをcacheする。表示名、read-only、constraint、editor hint、member accessorを保持する。

`DebugField`のmember種別、型、accessor、constraintが不正な場合は該当fieldだけを登録済みfield一覧から除外し、同じ型の正常fieldと対象instanceは利用可能にする。

### 未登録field

Scene Explorerから開いたComponentではinstance fieldをReflection収集する。

- property getter、setter、methodは自動公開・自動列挙しない。
- static、const、readonlyは読み取り専用とする。
- 未登録fieldは既定read-onlyとし、書き込みは利用者が明示的に編集modeへ切り替えた場合だけ許可する。
- private fieldはRuntime UIでは返さず、Editorの明示的な詳細modeだけで取得・表示可能とする。
- 循環参照と深いobject graphは展開せず、scalarとUnity標準値型を中心に表示する。
- 型ごとのdescriptor cacheを使用し、対象instanceごとにmember情報を再構築しない。
- read failureでComponent全体を閉じず、該当fieldを`Read Failure`として表示する。

## 値snapshot

Inspectorはすべての値を`DebugValue`へ変換できることを要求しない。読取結果は`InspectorValueSnapshot`で表し、次の状態を区別する。

| 状態 | 内容 |
| --- | --- |
| Value | `DebugValue`へ完全変換できた値 |
| Unity Reference | session handle、実行時型、Label、scene／Hierarchy情報。Null参照も区別する |
| Unsupported | 対応外のC#型と型名 |
| Read Failure | accessor／Reflection失敗とDiagnostic ID |

`InspectorValueSnapshot`はUnity Object、Reflection member、例外objectへの強参照をUI backend、履歴、外部adapterへ渡さない。Unity Referenceのsession handleは現在sessionだけで解決でき、対象消失後に同名objectや別instanceへ自動解決しない。

Value状態で共有する変換規則はSettingと同じbool、整数型、float、double、string、enum、`Color`、Vector系とする。collectionは要素数と、上限内の対応要素を読み取り専用でsnapshot化する。任意class／structの再帰展開、循環object graph、JsonUtilityによる暗黙変換、collectionの書込みは対象外とする。

ゲーム内に既に存在するNaN／Infinity、未対応型、壊れた参照を定義errorとして対象全体から除外しない。完全な`DebugValue`に変換できない状態として明示し、Writeは拒否する。

### Collectionの公開形式

`ICollection`を実装する配列・List等は`InspectorValueSnapshot.State = Value`とし、`DebugValue.Object`で`count`（総件数）、`items`（先頭最大128件のObject配列）、`truncated`（総件数が取得済み件数を上回るか）を返す。各itemは`index`、`state`、`value`、`label`を持つ。128件を超えて列挙せず、Countを持たない`IEnumerable`のみの型はUnsupportedとする。

対応scalar／Vector／Color／enumの要素は`Value`と変換値、nullは`Value`と`DebugValue.Null`、Unity参照は`UnityReference`と表示名、未対応型・入れ子collectionは`Unsupported`と型名を返す。破棄済みUnity参照はUnityReferenceのnull表示とし、通常のnullと区別する。getter・Count・enumerator失敗は親snapshotを`ReadFailure`としDiagnosticを付ける。要素を再帰展開せず、参照の編集・navigationを持たない。取得後に実体が変わってもsnapshotを変更しない。

collectionは登録済みfieldでも常にreadonlyで、API Writeも拒否する。UIの通常行は件数を示し、「…」からindex・状態・値を読む。Runtime renderer・Editor・Watchは同じsnapshotを使い、詳細はscroll領域内に収める。

## Read／Write

`ReadFields`は表示中field IDの有限集合を受け取り、main threadで対象生存を確認してからmember accessorを呼ぶ。全体resultが成功した場合も、個別fieldの`InspectorValueSnapshot`はValue、Unity Reference、Unsupported、Read Failureをそれぞれ表せる。

`WriteField`は次の順に行う。

1. targetとComponentの生存確認
2. memberの書き込み可否確認
3. deserializeと型validation
4. constraint validation
5. main threadでwrite
6. 書き込み後の値を再read
7. before／afterとresultを履歴へ追加

Writeは`DebugResult<InspectorValueSnapshot>`を返す。型、constraint、書き込み可否の問題は利用者が修正可能なvalidation issueとして返し、対象消失は`Stale`として返す。accessorやゲーム側property setterの例外はDiagnostic IDへ関連付け、例外objectをresultへ含めない。

`DebugInspectorWriteValue`は`DebugValue`、Unity ObjectのTarget Handle、null Unity Referenceのいずれかだけを表す。任意`object`、文字列変換、JsonUtility fallbackを受け付けない。

`DebugInspectorWriteAccess`は`RegisteredOnly`と`AllowUnregistered`の2値とする。通常のUI、AI、QAは`RegisteredOnly`を使用し、Scene Explorerの未登録fieldを確認後に編集する場合だけ`AllowUnregistered`を明示する。

Write statusは`Succeeded`、`Rejected`、`ReadOnly`、`ExplicitAccessRequired`、`Stale`、`NotFound`、`Failed`、`Disabled`とする。`Rejected`はvalidation issueを、`Failed`は任意のDiagnostic IDを返す。

Write可能なのは、対応する`DebugValue`からfieldの正確なC#型へ再構築できる値と、有効なsession handleから再解決できるUnity Object参照に限定する。数値Kind間の暗黙変換、`Convert.ChangeType`、`ToString()`、serializer fallbackを使用しない。Unity Object参照はWrite直前に生存と代入互換性を再検証する。Component参照のsnapshot handleはそのComponent自体を保持し、所属GameObjectのhandleへ置換しない。UIで対象GameObjectを選んだ場合、互換Componentが複数あればinstanceを明示選択する。破棄後の同名対象へ付け替えない。入力値はApply前に検証し、Unity main threadで反映する。

書込み履歴はtarget handle、実行時Label、Component／Field identity、before／afterの`InspectorValueSnapshot`、result、UTCの`DateTimeOffset`による実行時刻を保持する。callerは保持しない。履歴から値を復元するAPIは設けない。対象破棄後も実行時LabelとHierarchy情報を表示できるが、参照を再解決しない。

未登録fieldの編集modeはsession中のUI状態であり、member自体をRegisteredへ追加しない。AI／QAは未登録fieldを既定でread-onlyとし、明示的な危険操作指定なしにwriteできない。

## 対象の消失

対象が破棄された場合は`Missing`と表示し、同名objectや別instanceへ自動で切り替えない。候補一覧とHierarchyからは消滅した対象を除く。

## Watch

field行からWatchへ追加する。Watchは入口の分類に関係なく、対象参照、Component、fieldを保持する。時系列graphが必要な値はDebugDisplayへ渡す。

Watch entryはtarget handle、Component型、field identity、更新間隔、最新の`InspectorValueSnapshot`、状態を持つ。

Watch追加はComponent Handleの`AddWatch`を使用する。`TimeSpan.Zero`は毎frame更新対象、正の値は指定間隔、負数は`ArgumentOutOfRangeException`とする。成功時は`DebugInspectorWatchId`を返す。

Watch全体のreadと解除は`DebugInspector`へ置く。

```csharp
public static DebugInspectorWatchPage GetWatches(
	DebugPageRequest page);

public static void RemoveWatch(
	DebugInspectorWatchId watchId);
```

`RemoveWatch`は冪等かつ`ConditionalAttribute("DEXC_DEBUGGER")`を適用できる。Watch状態は`Active`、`Missing`、`Unsupported`、`ReadFailure`とする。対象消失後も`Missing`として一覧へ残し、別instanceへ自動接続しない。

- 更新間隔に達したentryだけreadする。
- 1frame当たりのWatch更新数に上限を設ける。
- target消失、read failure、unsupported valueを状態として保持する。
- Watchを外しても対象やfield登録には影響しない。
- graph化する場合は値sampleをDebugDisplayへ転送するadapterを使用する。

Watchによるreadは書込み履歴へ追加せず、時系列sampleもInspector内に蓄積しない。

## 実装構成

### 保持する状態

Inspectorはsessionごとに次を保持する。

- 手動登録されたtarget entry
- Reflection収集したSource descriptor
- Refと共有するSource catalog projection
- Reflection収集したfield descriptor
- Scene Hierarchyのsession node
- target／objectのsession handle
- Watch entry
- read／write diagnostic

RegisteredとScene Explorerは対象発見方法だけが異なり、選択後は同じ`InspectorTarget`、`ComponentDescriptor`、`FieldDescriptor`を使用する。

### InspectorTarget

| 項目 | 意味 |
| --- | --- |
| Handle | session内だけ有効な参照 |
| Origin | Manual、Source、Scene |
| Path | Registered CategoryまたはHierarchy Path |
| Target | Unity Object参照。外部へserializeしない |
| Label | 人間とAI向け表示 |
| Alive | 現在参照可能か |

同じobjectがManualとSceneの両方から見つかっても、UI入口は分けたまま内部handleを共有できる。

## UI接続

レイアウトと遷移は[Debug UIレイアウト・遷移仕様](debug-ui-layout.md)に従う。

UIは`GetRegistered`、`GetScenes`、`GetHierarchyChildren`、`StartSearch`、`GetSearchResults`、`CancelSearch`、`GetComponents`を使用してComponent Handleへ到達する。その後はHandleの`GetFields`、`ReadFields`、`WriteField`、`AddWatch`、`GetHistory`と、`DebugInspector.GetWatches`／`RemoveWatch`を使用する。

### Runtime

- InspectorトップでRegistered、Scene、Watchを分ける。
- Hierarchyは遅延展開し、検索は進行状況と件数上限を表示する。
- Component一覧とfield一覧を別画面にし、小画面で深い表を作らない。
- 未登録fieldの編集modeは追加確認の後に有効にする。

### Editor

- EditorWindowはRegisteredとScene Explorerの両方を提供し、tabで切り替える。
- Unity Hierarchy／Scene viewの現在選択をScene Explorerへ同期でき、lockを提供する。
- 中央paneにHierarchy、右paneにComponent／Field、下paneにWatchを置く。

## AI／QA

AI／QAはRegistered Category Path、またはscene、hierarchy path、名前、Component型の検索条件から候補のtarget handleを取得し、そのhandleを指定してread／writeする。返却dataにはhandle、label、scene、hierarchy path、Component型を含める。

handleはsession終了、scene unload、target破棄で失効する。永続保存や別sessionでの再利用を許可しない。

process外の外部toolはhandleを受け取らない（handleはctorがinternalでsessionを内包する）。外部toolは`GetEntityId()`の生値で対象を指し、読取のみを行う。この値は`DebugInspectorRegisteredEntry.EntityId`と`DebugInspectorHierarchyEntry.EntityId`が`ulong`で返し、`DebugInspector.TryGetTarget`／`TryGetObject`が受ける。詳細は[DexcRemote仕様](remote-control.md)を正本とする。

## Threadとlifecycle

- Register、Source評価、Hierarchy列挙、read、write、Watch更新はmain threadで行う。
- Scene検索はframe budgetを守って分割する。
- scene unload時にnode、target handle、Watchを失効させる。
- subsystem registrationでstatic entryとcacheをresetする。
