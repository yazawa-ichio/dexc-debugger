# DebugCommand仕様

本書はCommand class、InputRef、収集、実行、履歴を定める。`IDebugInput`、`DebugInput<T>`、Schema、値snapshotは[Debug Input値モデル仕様](debug-input-value-model.md)、画面は[Debug UIレイアウト・遷移仕様](debug-ui-layout.md)に従う。

## 操作インターフェース

DebugCommandは、開始と完了があるデバッグ処理を実行する機能である。操作経路は次のとおり。

| 利用者 | UI／インターフェース | 用途 |
| --- | --- | --- |
| 人間（ゲーム内） | Category／Pathのリスト、検索、入力フォーム、Executeボタン | Play Mode中の手動実行 |
| 人間（Editor） | EditorWindow、選択中Commandの入力・結果パネル | 開発中の反復実行、診断 |
| AI・外部ツール | JSON-RPCなどのrequest／response | 名前、入力、対象を指定した実行 |
| QA自動テスト | test adapterまたは型付き実行境界 | UIを介さない再現実行 |

すべての経路は同じ実行結果を返す。UIやJSON-RPCはゲーム側処理を直接呼び出さず、DebugCommandの実行境界へ接続する。

## 参照入力と対象の指定

実行中のinstanceを利用者が候補から選ぶ入力は`InputRef<T>`で表す。Commandの対象はInputRef入力の代表用途であり、同じCommandを適用できる具体的な候補から実行先を選ぶ場合に使用する（例: 生存中Enemyから1体を選んでkillする）。

次はCommand対象として扱わない。

| 内容 | 扱い |
| --- | --- |
| 現在scene、active UI root、game service | Command実装が使用する依存関係 |
| Enemy type、team、area | 通常Inputである検索条件 |
| 条件に一致する全Enemy | Commandが実行時に条件から求める処理対象 |
| Item ID、effect ID、screen ID | 処理内容を指定する通常Input |
| 複数instanceのcheckbox選択 | 対象選択に含めない |

1つの`InputRef<T>`は未選択または具体的な1件だけを保持する。1つのCommandに複数のInputRef fieldを定義してよい。

候補には利用者が区別できるLabelと選択用Pathを表示する。選択後はそのinstanceをsession handleで固定し、実行前に消失した場合は別objectへ差し替えず参照不在として終了する。

### InputRefのSource指定

| 定義 | 使用するSource |
| --- | --- |
| `new InputRef<T>()` | Keyを持たない`DebugInspectorSource`のうち、型`T`のentry |
| `new InputRef<T>("Near")` | Keyが`Near`の`DebugInspectorSource`のうち、型`T`のentry |
| `InputRef.Create(...)` | そのCommand内だけで使用するinline Source |

登録済みSourceから作る候補は、同じKeyを持つすべてのSourceのentryを合わせたものとする。候補のkeyは対象objectのinstance IDの文字列、Labelはentryの`Label`、PathはSourceのPathとentryの`Path`をつないだものとする。該当するSourceが無い場合は候補0件とし、定義errorにしない。

`new InputRef<T>(key, optional: true)`と`InputRef.Create(source, optional: true)`は任意参照とし、`IsOptional`がtrueになる。

Sourceは候補instanceだけでなくLabelと選択用Pathを返す。Pathを持つSourceは、Scene、Group、instanceなどの階層をRuntime UIとEditor UIへ提供できる。RegistryはHierarchyやmemoryから用途固有階層を推測しない。

対象型をCommandのgeneric引数で指定する形式はない。対象が1件だけでも、public Input fieldとして`InputRef<T>`を明示する。

```csharp
public InputRef<EnemyController> Target = new(key: "Near");
```

## 入力と結果

- 組み込みまたはユーザー定義Input classをfieldへ置ける。
- 入力値は実行開始時にsnapshotとして確定する。
- 入力不正、対象不在、実行不可、想定内Failure、Cancelled、内部Failureを区別する。
- 同期Commandと非同期Commandを区別し、非同期Commandは進行中状態、cancel、timeoutを扱う。
- 前回入力と実行履歴を参照できるが、UIの履歴をAI・QAへ自動適用しない。

## Command class契約

DebugCommandは専用基底classを継承する具象classとして定義する。任意method、delegate、lambdaはCommandにならない。

- PathとDescriptionはclass属性`DebugCommandAttribute`で宣言する。
- `IDebugInput`を実装したpublic instance fieldを入力として扱う。
- public instance fieldは`IDebugInput`、`CommandLabel`、`CommandHeader`のいずれかとし、その他のraw値fieldがあればCommand定義errorにする。property、static、const fieldは入力にしない。
- Input fieldの`readonly`は必須にしない。internal runtime adapterは構築後のInput instance差し替えを検出し、該当draftを実行不可にする。
- 1つのCommand instance内で同じ`IDebugInput` instanceを複数のpublic fieldへ格納することを禁止し、Command定義Errorとする。
- private／protected fieldはCommand内部状態として扱う。
- public parameterless constructorを要求する。
- constructorではfield initializer以外の初期化、Unity Object探索、service取得、ゲーム状態変更を行わない。

公開するCommand基底classは次の4種類に固定する。

```csharp
public abstract class DebugCommand
{
	public static class Registry { }
	public static class Draft { }
}

public abstract class DebugCommand<TOutput>;
public abstract class AsyncDebugCommand;
public abstract class AsyncDebugCommand<TOutput>;
```

2つのnested static classは非genericの`DebugCommand`にだけ定義する。`DebugCommand`直下にはstatic操作methodを置かない。実行はrequestの拡張メソッドとして提供する。

利用者へ共通の`DebugCommandBase`やmarker interfaceを公開しない。

利用者がoverrideするprotected APIは各基底classに共通して`OnInputsChanged`、`Validate`、`Fail`を持ち、実行memberだけが同期／非同期とOutput有無で異なる。

```csharp
protected virtual void OnInputsChanged(
	DebugInputChanges changes);

protected virtual void Validate(
	DebugCommandValidation validation);

[System.Diagnostics.CodeAnalysis.DoesNotReturn]
protected void Fail(
	string code,
	string message);
```

実行memberは順に`void Execute()`、`TOutput Execute()`、`ValueTask ExecuteAsync(CancellationToken)`、`ValueTask<TOutput> ExecuteAsync(CancellationToken)`とする。

### DebugCommandAttribute

`DebugCommandAttribute`はclassだけを対象とするsealed属性で、継承と複数指定を許可しない。`UnityEngine.Scripting.PreserveAttribute`を継承し、`ConditionalAttribute("DEXC_DEBUGGER")`を持つ。

| member | 型 | 既定値 |
| --- | --- | --- |
| Path（constructor引数、必須） | `string` | なし |
| `Description` | `string` | 空文字列 |
| `ConfirmationMessage` | `string` | 空文字列 |
| `Access` | `DebugCommandAccess` | `Default` |
| `Order` | `int` | 0 |
| `Tags` | `string[]` | 空配列 |

timeoutは持たない。空Path、null文字列、null Tags、null tag要素、空tag、未定義Accessは定義Errorとする。

属性短縮名`[DebugCommand]`は基底class`DebugCommand`と競合するため、属性は`[DebugCommandAttribute]`と完全名で記述する。別名の属性はない。

### Input型

Input fieldには`IDebugInput`を実装したclassだけを使用する。通常のユーザー定義Inputは`DebugInput<T>`を継承する。primitive、任意class、Unity値型をraw fieldとして置かない。組み込みInputとユーザー定義Inputは同じ`DebugInputSchema`と`DebugValue`契約を使用する。

実行中instanceの参照には、同じInput interfaceを実装する`InputRef<T>`を使う。

### Outputと同期種別

- Outputなし／ありは別の基底classで表す。Outputありは戻り値型をgeneric引数へ指定する。
- 複数値は利用者定義class／structとして返す。
- OutputへInput interfaceや組み込みOutput型の使用を要求しない。任意の`TOutput`を型付き結果として返せる。
- C#／QAの型付き実行境界は、実行直後の`TOutput`をその型のまま取得できる。
- UI backendとsession履歴は任意objectを直接保持せず、完了時に結果projectionを作る。
- bool、number、string、enum、Color、Unity Vector系などFrameworkが認識するtop-level値は組み込みprojectionする。
- その他のclass／structは`JsonUtility.ToJson`を既定projectionとする。Unity serialization規則に適合するOutputを返す責任は利用者が持つ。
- 配列などJsonUtilityがtop-levelで正しく扱わない組み込み対象は、Frameworkの内部wrapperを使用してprojectionする。配列の内部wrapperは`Items` memberを持つ。
- JsonUtilityが返すJSONの完全性や、無視されたfieldの検出は保証しない。
- JsonUtilityが例外を投げた場合はOutput projection失敗として型名とdiagnosticだけを残し、`ToString()`へfallbackしない。
- 公開結果の`Output`は`DebugValue`とする。bool、8整数型、有限float/double、string、enum、Color、Vector2/3/4、Vector2Int/3Intは対応する組み込み値とし、その他のclass/structと配列はJSON文字列を`DebugValue.String`へ格納する。JSON全文をInput用value treeへ再parseせず、Inputの配列長・depth制約をOutput全体へ適用しない。
- 組み込みprojection対象のNaN/Infinityは変換失敗とし、0や有限値へ丸めない。これはOutput表現の変換失敗であり、Input validationへ分類しない。
- Outputありのnullは`DebugValue.Null`、Outputなしは`Output == null`とする。`OutputTypeName`は宣言した`TOutput`の型名を持つread-only string propertyとし、Outputなしは空文字列とする。変換失敗時にも型名を残す。
- Outputを永続化せず、sessionを越えた互換性を持たない。
- 同期と非同期は別基底class、別Dispatch entry pointとする。非同期Commandだけが`CancellationToken`を受け取る。
- 非同期基底型の規定memberは`ValueTask`を返す。`Task`版とUniTask版の基底型はない。
- dispatcherは返された`ValueTask`を1回だけawaitし、複数回await、blocking wait、未完了taskの再利用を行わない。
- Inputなしはpublic input fieldがないことで表す。`NoInput`／`NoOutput`型はない。

### Command instance

Command機能はCommand singletonを持たない。draftごとのinstanceを入力通知とvalidationに使用し、dispatcherは実行時に別の新しいinstanceへ各Inputの`DebugValue` snapshotを適用する。同じdraftを再実行しても、前回実行の内部状態を共有しない。

## Command識別子

`DebugCommandId`、`DebugCommandFieldId`、`DebugCommandDraftId`、`DebugReferenceHandle`はpublic `readonly struct`とする。public constructorを持たず、`IsValid`、equality、`GetHashCode`、diagnostic用`ToString`だけを公開する。大小比較、加算、数値への暗黙変換、永続化形式を公開しない。

Command IDとField IDはcollection generation、Draft IDとReference Handleはruntime sessionへscopeする。旧generationまたは旧sessionの値を同名の別定義・別instanceへ自動再接続しない。

## Path

Pathは人間向け表示階層とsession内の文字列IDを兼ねる。別の永続外部IDはない。

- `/`区切りで、最後のsegmentを表示名とする。
- 空Path、空segment、segment前後の空白を禁止する。
- `/`のescapeはない。
- Command Registry内で一意とし、大小文字を区別する。
- overloadを許可しない。
- renameによる保存済み入力や外部呼出しの互換性は保証しない。

## UIの基本形

1. Category／Pathのリストまたは検索でCommandを選ぶ。
2. InputRef fieldを持つCommandだけ、各InputRefのSourceが返す階層から具体的な候補を選ぶ。
3. Descriptionと定義済み入力を共通の縦フォームで表示する。項目名の配置は[共通フォーム](debug-ui-layout.md#共通フォーム)に従う（Runtimeは左に項目名、右に値。Editorは直上で全文表示）。
4. immutable requestを生成し、`ConfirmationMessage`があるCommandだけconfirmationを表示する。
5. 実行中も入力欄を残して無効化する。完了後はフォーム末尾にstatus、output、duration、diagnosticを表示し、後から入力を編集しても過去結果を変更しない。
6. 履歴から「入力を開く」で新しいdraftへ復元できる。復元は実行せず、必要なら改めてExecuteする。参照失効・確認・snapshotの契約を維持する。

画面構成・固定エラー枠・focus・フォールバックは[UIレイアウト](debug-ui-layout.md#runtime-command)に従う。

## 公開境界

ゲーム側は、Commandのdescriptor取得、request生成、入力validation、実行、結果取得を提供する。JSON-RPCはこの境界のadapterとし、JSON-RPC固有の名前や認証をCommand定義へ持ち込まない。

| 公開入口 | 責務 | 主な利用箇所 |
| --- | --- | --- |
| `DebugCommand.Registry` | 定義一覧、descriptor検索、収集状態のread | Runtime UI、Editor UI、adapter |
| `DebugCommand.Draft` | draft作成・編集、validation、immutable request生成、履歴 | 入力画面、QA、型付き実行準備 |
| request拡張メソッド | Dispatch、DispatchAsync、throw形式、追跡開始 | UI、QA、ゲーム側の直接実行 |

Command classまたはUI draftを直接実行せず、一度immutableな`DebugCommandRequest`へ変換してから実行する。

### Registryの公開範囲

`Registry`は次の同期readだけを公開する。Reflection収集の明示Refresh、Reflection型取得、Command instance生成、validation、実行、履歴取得は提供しない。

```csharp
public static DebugCommandListPage GetList(
	DebugListQuery query);

public static bool TryGetDescriptor(
	DebugCommandId commandId,
	out DebugCommandDescriptor descriptor);
```

型からIDを取得するAPIはない。UIとadapterは一覧が返したIDを使用する。

`DebugCommandListPage`は`DebugCollectionSummary`、`DebugPageInfo`、不変な`DebugCommandListItem`一覧を持つ。List itemはCommand ID、Path、Display Name、Description、Order、Tags、同期種別、Output有無、Confirmation有無、解決後Accessを持つ。

`DebugCommandDescriptor`はList itemの情報に加え、field IDごとのInput Schema、Label／Header、InputRefの対象型とSource key、Output型、非同期timeoutを持つ。`Type`、`FieldInfo`、属性instanceなどのReflection objectを公開しない。

### Draftの公開範囲

UIとadapterがIDで操作するAPIは次に限定する。

```csharp
public static DebugCommandDraftOperationResult Create(
	DebugCommandId commandId);

public static DebugCommandDraftOperationResult UpdateInput(
	DebugCommandDraftId draftId,
	DebugCommandFieldId fieldId,
	DebugValue value);

public static DebugCommandDraftOperationResult SelectReference(
	DebugCommandDraftId draftId,
	DebugCommandFieldId fieldId,
	DebugReferenceHandle reference);

public static DebugCommandDraftOperationResult ClearReference(
	DebugCommandDraftId draftId,
	DebugCommandFieldId fieldId);

public static DebugCommandReferenceListPage GetReferenceEntries(
	DebugCommandDraftId draftId,
	DebugCommandFieldId fieldId,
	DebugListQuery query);

public static DebugCommandDraftOperationResult Validate(
	DebugCommandDraftId draftId);

public static DebugCommandRequestCreationResult CreateRequest(
	DebugCommandDraftId draftId);

public static void Discard(
	DebugCommandDraftId draftId);
```

`UpdateInput`、`SelectReference`、`ClearReference`は1回の変更として`OnInputsChanged`を呼び、その後にvalidationを実行する。Schema変換または通知処理に失敗した場合はdraftを変更前へ戻す。`Discard`は既に破棄済みまたはstaleなIDにも成功する冪等操作とする。

`Create`は新しいCommand instanceを生成し、field initializerのInput値、Schema、初回validationからdraftを作る。作成時に`OnInputsChanged`は呼ばない。`Create`へ前回実行値を自動適用しない。

Draft操作のstatusとresultは次とする。

```csharp
public enum DebugCommandDraftOperationStatus
{
	Succeeded,
	Rejected,
	Stale,
	NotFound,
	DefinitionUnavailable,
	Disabled,
}

public readonly struct DebugCommandDraftOperationResult
{
	public DebugCommandDraftOperationStatus Status { get; }
	public DebugDiagnosticId DiagnosticId { get; }

	public bool TryGetDraftId(
		out DebugCommandDraftId draftId);

	public bool TryGetValidation(
		out DebugCommandValidationResult validation);
}
```

Schema上は正しいがcross-field validationで実行不可になった更新は`Succeeded`かつ`CanExecute == false`とする。`Rejected`はSchema不一致、Constraint違反、通知失敗など変更自体を採用できない場合に使用する。`NotFound`は未知の定義、`DefinitionUnavailable`は収集時に検出した除外定義を表す。

型付きCommand instance用の`Validate`と`CreateRequest`は、4つの公開基底classに対応するoverloadを持つ。共通public interfaceや`object`引数のoverloadはない。

```csharp
public static DebugCommandValidationResult Validate(
	DebugCommand command);

public static DebugCommandValidationResult Validate<TOutput>(
	DebugCommand<TOutput> command);

public static DebugCommandValidationResult Validate(
	AsyncDebugCommand command);

public static DebugCommandValidationResult Validate<TOutput>(
	AsyncDebugCommand<TOutput> command);

public static DebugCommandRequest CreateRequest(
	DebugCommand command);

public static DebugCommandRequest<TOutput> CreateRequest<TOutput>(
	DebugCommand<TOutput> command);

public static AsyncDebugCommandRequest CreateRequest(
	AsyncDebugCommand command);

public static AsyncDebugCommandRequest<TOutput> CreateRequest<TOutput>(
	AsyncDebugCommand<TOutput> command);
```

型付きinstanceの入力変更後に`OnInputsChanged`を明示適用するため、4基底classに対応する`NotifyInputsChanged` overloadを提供する。各overloadはCommandと`params IDebugInput[]`を受け、変更fieldをそのCommandが保持する`IDebugInput` instanceで指定する。UI draftの更新APIは自動通知するため、このAPIを使用しない。

```csharp
DebugCommand.Draft.NotifyInputsChanged(
	command,
	command.Target);
```

null、空配列、重複、現在のCommandに属さないInputは`ArgumentException`とする。通知開始時に全Inputをsnapshot化し、通知内で例外が発生した場合は通知による変更を戻す。利用者が通知前に行った変更は維持する。

`GetReferenceEntries`はstatus、`DebugPageInfo`、不変な`DebugCommandReferenceEntry`一覧を持つ`DebugCommandReferenceListPage`を返す。EntryはReference Handle、Label、Path、Alive、対象型表示名を持ち、Unity Object参照を含めない。draftまたはfieldの失効はpageの`Stale`、未知のIDは`NotFound`で表す。Source例外はoperation diagnosticへ記録し、その候補取得を利用不可にするが、Command定義を恒久的には除外しない。

`Validate`は副作用を持たない判定結果とissue一覧を返す。`CreateRequest`は事前の`Validate`結果に関わらずvalidationを必ず再実行し、成功時にimmutable requestを返す。`CreateRequest`後に元のCommand instanceまたはdraftを変更してもrequestへ影響しない。

型付きinstanceの`CreateRequest`は、実行不可なら`DebugCommandValidationException : InvalidOperationException`を投げる。例外は`DebugCommandValidationResult Validation`を持つ。nullは`ArgumentNullException`、未初期化は`InvalidOperationException`とする。

UI draftのrequest生成は通常の競合やstaleを例外にせず、次を返す。

```csharp
public enum DebugCommandRequestCreationStatus
{
	Created,
	ValidationFailed,
	Stale,
	DefinitionUnavailable,
	Disabled,
}

public readonly struct DebugCommandRequestCreationResult
{
	public DebugCommandRequestCreationStatus Status { get; }
	public DebugCommandValidationResult Validation { get; }
	public DebugDiagnosticId DiagnosticId { get; }

	public bool TryGetRequest(
		out DebugCommandRequest request);
}
```

`TryGetRequest`は`Created`の場合だけtrueを返す。Confirmationはrequest生成後にUI／adapterが処理し、拒否時はrequestを破棄して実行結果と履歴を作らない。

### Request

Requestの公開型は実行種別とOutput有無に対応する。

```csharp
public class DebugCommandRequest;

public sealed class DebugCommandRequest<TOutput>
	: DebugCommandRequest;

public sealed class AsyncDebugCommandRequest
	: DebugCommandRequest;

public sealed class AsyncDebugCommandRequest<TOutput>
	: AsyncDebugCommandRequest;
```

Requestはpublic constructorを持たないimmutable classとする。Command ID、Path、Session ID、Collection Generation、実行種別、field IDごとのDebugValue、InputRef session handle、作成元draft version、Access、confirmation情報、Output型を保持する。

C#／QAが渡したCommand instanceはrequestを作るための型付きdraftとしてだけ使用する。dispatcherはrequestから別の新しいCommand instanceを生成し、snapshotを適用して規定memberを呼ぶ。元のCommand instanceそのものを実行しない。

### 実行の公開範囲

実行はimmutable requestの拡張メソッドとして公開する。

```csharp
CommandExecutionResult Dispatch(
	this DebugCommandRequest request);

CommandExecutionResult<TOutput> Dispatch<TOutput>(
	this DebugCommandRequest<TOutput> request);

void DispatchOrThrow(
	this DebugCommandRequest request);

TOutput DispatchOrThrow<TOutput>(
	this DebugCommandRequest<TOutput> request);

ValueTask<CommandExecutionResult> DispatchAsync(
	this AsyncDebugCommandRequest request,
	CancellationToken cancellationToken = default);

ValueTask<CommandExecutionResult<TOutput>> DispatchAsync<TOutput>(
	this AsyncDebugCommandRequest<TOutput> request,
	CancellationToken cancellationToken = default);

ValueTask DispatchOrThrowAsync(
	this AsyncDebugCommandRequest request,
	CancellationToken cancellationToken = default);

ValueTask<TOutput> DispatchOrThrowAsync<TOutput>(
	this AsyncDebugCommandRequest<TOutput> request,
	CancellationToken cancellationToken = default);
```

通常の実行経路は`Dispatch`／`DispatchAsync`とする。`DispatchOrThrow`／`DispatchOrThrowAsync`は、Command実装の想定内`Fail`、cancel、timeout、未処理例外を対応する公開例外へ変換して送出するC#呼出し向けの利便APIであり、UIと外部adapterの通常経路には使用しない。

非同期実行のcancelは`CancellationToken`で要求する。tokenのcancelはCommandへCancellationを要求するが、強制停止、完了待機、既に発生した副作用のrollbackを保証しない。

実行中表示を保持するUIは、非同期requestの`StartExecution`拡張メソッドを使用する。

```csharp
DebugCommandExecution StartExecution(
	this AsyncDebugCommandRequest request,
	CancellationToken cancellationToken = default);

DebugCommandExecution<TOutput> StartExecution<TOutput>(
	this AsyncDebugCommandRequest<TOutput> request,
	CancellationToken cancellationToken = default);
```

execution objectは`Id`、`State`、`Cancel()`、`WaitAsync()`、`Dispose()`を公開する。`Cancel`は冪等とし、`Dispose`は実行中ならCancellationを要求するがblockしない。完了resultは`WaitAsync`から取得する。汎用的なexecution snapshot、実行一覧query、execution IDからの再接続は提供しない。

## 実装構成

公開nested classと拡張メソッドはinternal実装へ処理を委譲する入口であり、internal serviceを利用者へ公開しない。差替え可能なservice locatorとしても使用しない。

| 構成 | 責務 |
| --- | --- |
| `DebugCommand.Registry` | 定義read用の公開projection |
| internal command registry | Reflection収集、定義検証、descriptorとindex |
| `DebugCommand.Draft` | draft操作用の公開projection |
| internal draft store | UI編集中の入力とversion |
| internal command validator | Input固有validationとCommandの`Validate`呼出し、結果生成 |
| internal request factory | validation済み入力からimmutable requestを生成 |
| request拡張API | 実行操作用の公開projection |
| internal dispatcher | request再検証、instance生成、実行 |
| internal history store | session履歴 |
| internal diagnostic store | 実行例外とdiagnostic |

### Registry

internal Command RegistryはCommand定義の台帳であり、次を保持する。

- Reflectionで収集した`CommandDescriptor`
- PathとCommand型のindex
- Source定義への参照
- 収集・定義validation diagnostic

Registryはrequest生成、実行、draft、session handle、履歴、実行diagnosticを所有しない。Command instanceやReflection object、internal indexをUIへ公開しない。

### CommandDescriptor

| 項目 | 意味 |
| --- | --- |
| Path | 表示階層と文字列ID |
| Description | 影響と前提条件 |
| Command Type | C#内部lookupとdiagnostic |
| Execution Kind | Synchronous／Asynchronous |
| Form Elements | field ID、表示順、任意Order、Input SchemaまたはLabel／Header metadata |
| Output Type | なし、またはC#戻り値型。構造化projection可否を併記 |
| Reference Fields | `InputRef<T>`の型、既定Source key、候補表示情報 |
| Confirmation Message | UI／adapterが確認を表示するための任意文面 |
| Access | `Default`、`ReadOnly`、`Lock`の宣言値と解決後の実行access |

descriptorは収集後に不変とする。現在の入力、選択対象、実行結果を保持しない。

### CommandDraft

UIで編集中の値は、internal draft storeの`CommandDraft`が保持する。

- Command Path
- fieldごとのInput Schemaと`DebugValue`
- field initializerから得た初期値
- InputRef fieldごとの選択中session handle
- InputRef fieldごとの現在Source binding
- validation error
- dirty状態

internal draft storeはdraft IDとdraft専用Command instanceを保持し、生成・更新・validation・snapshot化・破棄を行う。Command instanceをUIへ公開しない。Runtime UIは画面stack、Editor UIは選択tabごとにdraft IDだけを保持する。実行時はdraft instanceを直接実行せず、新しいCommand instanceへ入力snapshotをcopyする。

## 入力変更通知

Commandは`OnInputsChanged(DebugInputChanges changes)`をoverrideできる。scalar、enum、InputRef、履歴復元、外部adapterからの入力を同じ通知で扱い、InputRef専用通知はない。

`DebugInputChanges`はFrameworkが生成する不変classとし、`Count`と`Contains(IDebugInput input)`だけを公開する。field名文字列、Field ID、mutable collection、変更前後の任意objectは公開しない。

1. `DebugCommand.Draft`が1回の利用者操作をinternal draftへ反映する。
2. 変更field集合を渡して`OnInputsChanged`を1回呼ぶ。
3. 通知内で変更された他fieldも同じdraft変更へ含める。
4. 通知内の変更について通知を再帰的に呼ばない。
5. 完成したdraftをvalidationする。
6. draftのread結果を更新する。

通知は関連InputRefの解除、Sourceの差し替え、対象依存初期値、入力値の補正に使用する。ゲーム状態の変更、Command処理の先行実行、長時間処理は禁止する。

Frameworkは複数InputRefの依存順序を宣言・解析しない。前段InputRefの変更時に後段InputRefを解除するか、古い選択を保持してvalidation errorにするかはCommandが通知内で決める。候補を他入力から動的に絞る場合は、現在draftを参照できるinline Sourceを使用するか、InputRefが提供する操作でSource bindingを変更する。fieldのInput instance自体は置き換えない。

## 実行可能validation

Commandは`void Validate(DebugCommandValidation validation)`をoverrideして、cross-field条件、InputRefの整合、現在のゲーム状態を検証できる。

`DebugCommandValidation`はInputで使用する`DebugValidation`と同じissue形式を収集するCommand用collectorであり、Frameworkだけが生成する。公開memberはCommand全体用とfield用の`Require`、`Error`、`Warning`、およびCommand全体用の`Unavailable`とする。

```csharp
validation.Error(code, message);
validation.Error(field, code, message);
validation.Warning(code, message);
validation.Warning(field, code, message);
validation.Require(condition, code, message);
validation.Require(condition, field, code, message);
validation.Unavailable(code, message);
```

field指定は現在のCommand instanceがpublic fieldに保持する`IDebugInput` instanceを直接渡す。internal runtime adapterは参照一致からField IDへ解決する。現在のCommandに属さないInput、収集後に差し替えられたInput、nullを指定した場合はvalidation issueとして表示せずCommand実装diagnosticとする。

- collectorを実行可否の唯一の正本とし、利用者は実行可否を返さない。
- `Require`はconditionがfalseの場合だけErrorを追加する。
- `Unavailable(code, message)`は入力値の誤りではなく、現在のゲーム状態ではCommandを利用できないことを表す。
- Warningは説明として表示できるが、実行を禁止しない。
- ErrorはfieldまたはCommand全体へ関連付ける。

公開結果は次のimmutable classとする。

```csharp
public sealed class DebugCommandValidationResult
{
	public bool CanExecute { get; }
	public bool HasWarnings { get; }
	public IReadOnlyList<DebugCommandValidationIssue> Issues { get; }
}
```

`DebugCommandValidationIssue`は共通の`DebugValidationIssue`、任意の`DebugCommandFieldId`、Command全体のUnavailable条件かどうかを持つimmutable classとする。Field IDがないIssueはCommand全体へ関連する。`Unavailable`が追加したIssueはError severityを持ち、UIは入力修正ではなくゲーム状態による利用不可であることを区別できる。共通の`DebugValidationIssue`自体はField IDやUnavailable区分を持たない。

`Error`または`Unavailable`が1件以上あれば`CanExecute`をfalse、Warningだけならtrueとする。

validationはdraft生成後、入力変更通知後、InputRef失効時、Dispatch直前に実行する。UIで`CanExecute`だった場合もDispatch直前に再検証する。validationは複数回呼ばれ得るため、副作用を持たず、main threadを長時間占有しない処理とする。

`InputRef<T>`は未選択またはMissingならError `reference-missing`とする。任意参照は未選択でもErrorにせず、`TryGetValue`がfalseを返す。任意参照でもMissingはErrorとする。外部から実行する場合、指定しなかったInputはdraftの初期値のままとなるため、任意参照でない`InputRef<T>`は値の指定が必要になる。Command側validationは、複数InputRef間の意味的整合などFrameworkが判断できない条件を追加する。

InputRefは`Unselected`、`Resolved`、`Missing`の状態を持つ。ゲーム側は`TryGetValue(out T value)`で現在の同一instanceを取得し、UIは状態を分けて表示する。

## Reflection収集・検証

1. Registryの初回利用時に、load済みの非dynamic assemblyを列挙する。
2. `Dexc.Runtime`自身と、`Dexc.Runtime`を参照するassemblyだけを型走査の候補にする。
3. assembly除外属性が付いた候補を走査対象から外す。include属性は要求しない。
4. 候補assemblyからCommand基底classの具象型を収集する。
5. class属性、constructor、同期／非同期基底型、Output型を検証する。
6. `IDebugInput`、`CommandLabel`、`CommandHeader`のpublic instance fieldをform elementとして収集する。
7. Input instance、同一instanceの複数field共有、Schema、初期DebugValue、Label provider、重複名を検証する。
8. `IDebugInput` fieldの`DebugRangeAttribute`、`DebugSliderAttribute`、`DebugMultilineAttribute`をInputのSchemaと照合する。
9. Path重複がなければdescriptorとindexを確定する。
10. 不正型は登録せずdiagnosticへ理由を記録する。

`ReflectionTypeLoadException`では取得できた型の検証を継続し、各loader errorをdiagnosticへ記録する。他のassembly列挙／型取得例外もassembly単位のdiagnosticへ変換し、Registry全体を未初期化のまま残さない。

Command class、constructor、基底型、Output契約、form element、Input instance、Schema、Sourceのいずれかが不正で、安全な実行requestを構築できない場合は該当Command全体を登録しない。壊れたInput fieldだけを非表示にして残りの引数で実行することはしない。他の正常なCommandの収集と実行は継続する。同一Pathが重複した場合は競合したCommandをすべて登録せず、いずれかをReflection順で採用しない。

収集結果はPlay session中cacheし、一覧取得や画面遷移のたびにReflectionを再実行しない。assembly追加の監視、後からloadされたassemblyの自動検出、明示Refresh APIはない。script compile時はstatic状態をresetし、次の利用時に新しいassembly集合から収集する。

## InputRef Sourceとsession handle

登録済みSourceは`DebugInspectorSource`属性の付いたstatic memberであり、InputRefはそれをKeyで参照する。InputRefはInspectorの登録状態やUIへ依存しない。inline SourceはInputRef instance内だけに保持する。

Sourceは利用者が候補一覧を開いた時に評価する。登録済みSourceと`InputRef.Create`のinline Source（`Func<IEnumerable<InputChoiceEntry<T>>>`）はどちらも引数を取らない。FrameworkはInputRef間の依存グラフを作らない。Sourceは候補entryとして次を返す。

| 項目 | 意味 |
| --- | --- |
| Handle | session内でのみ有効な参照 |
| Label | UIとAI向け表示 |
| Path | Source内の選択階層 |
| Value Type | `InputRef<T>`のT。型検証用 |
| Object Reference | 実行先。外部へserializeしない |
| Alive | 現在実行可能か |

候補一覧を取得した時点でhandleを発行する。Execute時にhandleが同じobjectを指し、対象が生存し、target型に適合することを再検証する。

候補のPath、並び順、LabelはSource側が提供する。Registryは型名やHierarchy名から用途固有labelを暗黙生成しない。

## 実行accessと同時実行

Commandのゲーム状態に対するaccessを`DebugCommandAccess`で宣言する。

```csharp
public enum DebugCommandAccess
{
	Default,
	ReadOnly,
	Lock,
}
```

`DebugCommandAttribute.Access`の既定値は`Default`とする。`Default`はproject側が設定するCommand既定accessへ解決し、projectが指定しない場合のpackage既定値は`Lock`とする。解決後のaccessはdescriptorとrequestへ保持し、Dispatch時の判定をUI表示と一致させる。

| Access | 契約 | 同時実行 |
| --- | --- | --- |
| `Default` | project既定値を使用する | 解決先に従う |
| `ReadOnly` | ゲーム状態を変更しないとCommand実装者が宣言する | 複数の`ReadOnly`を同時実行できる |
| `Lock` | ゲーム状態を変更する可能性があり、実行中の排他を必要とする | 他の`ReadOnly`と`Lock`を含むすべてのCommandと同時実行しない |

Frameworkは`ReadOnly` Commandが実際にゲーム状態を変更しないことを検証しない。この宣言の正しさはCommand実装者の責務とする。

実行accessはsession全体のreader／writer gateとして扱う。複数readerは許可し、writerは単独でだけ実行する。競合したrequestを待機queueへ入れず、`Failed`／`Unavailable`、Error code `command-locked`として拒否する。

非同期`Lock` Commandは、Cancelまたはtimeoutを要求した時点ではlockを解除せず、返された`ValueTask`が実際に終了するまで保持する。

## 実行状態機械

```text
Draft → Validating → Request Created → Running
                                       ├─ Succeeded
                                       ├─ Failed
                                       ├─ Cancelled
                                       └─ TimedOut
```

終端状態は`CommandExecutionStatus`の`Succeeded`、`Failed`、`Cancelled`、`TimedOut`とする。`TimedOut`は非同期Commandだけが取る。

1. draftまたは型付きCommand instanceからimmutable requestを生成し、すべてのInputRef handleをsnapshot化する。
2. request生成時にInput、すべてのInputRef、availabilityをvalidationする。
3. Dispatch受付時にも、現在のゲーム状態とInputRefの生存を再validationする。
4. 新しいCommand instanceへInputをcopyする。
5. Unity main threadで規定memberを実行する。
6. Outputを検証し、resultと履歴を確定する。

同期Commandは`Running`中にUI threadを長時間占有する用途へ使わない。非同期Commandの通常の`DispatchAsync`は、UIを閉じても呼出側tokenがcancelされない限り継続する。`StartExecution`を使用したUIはexecution objectの`Cancel`または`Dispose`からCancellationを要求できる。

### Confirmation

Confirmationはbackendの実行契約ではなく、UI／adapterによる誤操作防止機能とする。`DebugCommandAttribute.ConfirmationMessage`は任意の表示metadataであり、dispatcherは承認状態を保持しない。承認済み実行用の専用APIはない。

UIは現在のdraftからimmutable requestを生成した後、descriptorの`ConfirmationMessage`が設定されている場合だけ確認dialogを表示する。承認された場合は通常の`Dispatch`／`DispatchAsync`へrequestを渡し、拒否された場合はrequestを破棄する。拒否は実行受付前であるためresultと履歴を作らない。

C#、QA、RPCなどUI以外の呼び出し元は、確認を省略または独自に実装できる。実行を禁止すべき条件はConfirmationへ依存させず、`Validate`で強制する。

### Cancellationとtimeout

非同期Commandだけ、次のprotected virtual propertyを持つ。

```csharp
protected virtual TimeSpan Timeout =>
	TimeSpan.FromSeconds(5);
```

- package既定timeoutは5秒とし、Commandごとにoverrideできる。
- timeoutなしは`System.Threading.Timeout.InfiniteTimeSpan`とする。正のTimeSpanまたは`InfiniteTimeSpan`だけを許可し、`TimeSpan.Zero`とその他の負数はCommand定義Errorとする。
- 同期CommandにはTimeout propertyを設けず、timeoutを適用しない。
- dispatcherはtimeout到達時に同じ実行のtokenをcancelし、状態を`TimingOut`へ変更する。
- 利用者によるCancel要求では状態を`Cancelling`へ変更する。
- Commandが同じtokenに由来する`OperationCanceledException`で終了した場合、Cancel要求が原因なら`Cancelled`、timeoutが原因なら`TimedOut`とする。
- timeoutと手動Cancelが競合した場合は、先にtokenをcancelした原因を最終分類に使用する。
- Commandがtokenを無視した場合は強制終了しない。`TimingOut`または`Cancelling`のまま実行を追跡し、終了済みresultを返さない。
- timeout後に通常完了した場合も`Succeeded`へ変更せず、`TimedOut`として完了させ、遅延完了したOutputは公開しない。
- 手動Cancel、呼出側token、session終了による取消を先に受け付けた実行は、Commandが取消を無視して通常完了しても`Cancelled`とし、Outputを公開しない。実処理の終了前には完了resultを返さず、後発のtimeoutで取消原因を上書きしない。
- timeoutはCommand実行そのものの契約であり、Runtime UI、Editor、RPC／QAで共通に適用する。
- Transportの接続待ちやtest runner全体の制限時間はCommand timeoutと別である。

### 想定内Failure

Commandは規定member内からprotected `Fail(code, message)`を呼び出して、想定されたゲーム上の失敗として実行を終了できる。

- `Fail`は`DoesNotReturnAttribute`を持ち、呼出し元へ戻らず、その実行を直ちに終了する。
- codeは機械判定用の空でない文字列、messageは人間向けの公開文面とする。
- Outputは生成せず、Statusを`Failed`、Failure Kindを`Expected`とする。
- `Fail`の内部実装方法は公開契約にしない。
- validation error、InputRef消失、Cancelled、未処理例外とは別に分類する。
- `Fail`はExecute／ExecuteAsyncの実行中だけ使用できる。constructor、入力変更通知、validationからの呼出しは定義または実行errorとする。
- 未処理例外は`Fail`へ変換せず、Failure Kindを`UnhandledException`としてdiagnosticへ関連付ける。

## Resultと履歴

`CommandExecutionStatus`は次の4種類だけとする。

- `Succeeded`
- `Failed`
- `Cancelled`
- `TimedOut`

`Failed`の場合だけ`CommandFailureKind`を持つ。

| Failure Kind | 意味 |
| --- | --- |
| `Expected` | Commandが`Fail`で返した想定内Failure |
| `Validation` | Input Schema、値、constraint、またはCommand validationが不正 |
| `Unavailable` | Commandまたは必要なゲーム状態を現在利用できない |
| `MissingReference` | 選択したInputRefが未選択または消失 |
| `UnhandledException` | Command実装から漏れた未処理例外 |
| `Internal` | Registry、入力copy、projectionなどFramework内部の失敗 |

validationまたはavailabilityにより規定memberを呼ぶ前に拒否したrequestも、`Failed`の実行結果として返す。`Cancelled`と`TimedOut`にはFailure Kindを設定しない。UIはStatusを主表示とし、QA／RPCはFailure KindとError codeで機械判定できる。

### Errorとdiagnostic

`CommandExecutionResult`はError code、人間向けMessage、任意のDiagnostic IDを持つ。例外objectそのものはresult、UI read結果、履歴へ渡さない。

Diagnostic recordは例外型名、例外message、stack trace、inner exception、発生phase、Command Path、Execution IDを保持する。`Expected` FailureにはDiagnosticを作らず、`UnhandledException`と`Internal`へ関連付ける。Runtime UIとEditorはDiagnostic IDから詳細を取得できる。外部adapterへのstack trace公開は各Transport仕様に従う。

stack traceは例外が持つ文字列をそのままsession中だけ保持する。frame数や文字数で切り詰めず、System、async state machine、Dexc内部frameも削除しない。operation diagnostic全体の件数は内部上限で有限にする。

`CommandExecutionResult`は次を持つ。

- Execution ID
- Command Path
- Refごとのfield名、実行時Label、session情報
- Input snapshot
- Status
- Failure Kind
- Outputの型名と、組み込みまたはJsonUtilityによる構造化projection
- Error codeと公開message
- Started At、Duration

C#／QAの型付きDispatchでは、非generic resultを継承する`CommandExecutionResult<TOutput>`を返す。成功時だけ`TryGetOutput(out TOutput output)`で実行直後の型付きOutputを取得できる。Failed、Cancelled、TimedOutでは型付きOutputを保持・公開せず、TryGetOutputのfalse時のout値はdefaultとする。

型付きOutputは呼出し直後のresultだけが保持し、session履歴へ保存しない。JsonUtilityによるprojectionが失敗した場合は`Failed`／`Internal`とし、型付きOutputも公開せず、diagnosticを関連付ける。`ToString()`へfallbackしない。

### 履歴

履歴は有限件のsession memoryとし、Commandごとに取得する。Output object、Unity Object参照、例外object、巨大dataを直接保存しない。`DebugCommand.Draft`は履歴について次だけを公開する。

```csharp
public static DebugCommandHistoryPage GetHistory(
	DebugCommandId commandId,
	DebugPageRequest page);

public static DebugCommandDraftOperationResult CreateFromHistory(
	DebugHistoryId historyId);
```

`DebugCommandHistoryPage`は`DebugPageInfo`と新しい順のentry一覧を持つ。entryはHistory ID、UTCの`DateTimeOffset`による実行時刻、status、対象label、Input summary、Output summary、Error codeを持ち、callerを保持しない。再実行に必要な完全なInput snapshotはinternal history storeが保持し、履歴詳細APIはない。

`CreateFromHistory`はInputを新しいdraftへcopyし、通常の入力変更通知とvalidationを行う。各InputRefは現在も同一instanceと確認できた場合だけ再選択し、消失していればMissingとする。履歴復元だけで自動実行しない。削除済み履歴または旧sessionのHistory IDは`Stale`を返す。

## UI接続

### Runtime

- Category／Pathはpush型listで表示する。
- Refと通常Inputをfield順に表示する。
- 1画面にRef、通常Input、Executeを収め、長いInputはscrollする。
- Running画面はprogressとCancelだけを更新する。

### Editor

- 中央paneにCommand treeとRef候補、右paneにdraftとresultを置く。
- Commandを切り替えてもdraftを保持する。
- 下paneへ履歴とdiagnosticを表示する。

UIは定義一覧とdescriptorを`DebugCommand.Registry`から取得する。draft作成・更新、参照候補、validation、request生成、Command別履歴と履歴復元には`DebugCommand.Draft`を使用する。実行にはrequestの`Dispatch`／`StartExecution`拡張メソッドを使用する。Command memberやinternal registry、validator、request factory、dispatcherを直接呼ばない。Confirmationはdescriptor metadataを読み、UI自身のdialog状態として扱う。

## Threadとlifecycle

- `DebugCommand.Registry`と`DebugCommand.Draft`の全API、Reflection収集、Source評価、draft初期化、execute、execution objectの`Cancel`／`Dispose`、呼出側tokenのcancelはUnity main threadで行う。別threadからの呼出しはサポートせず、検査もしない。
- Enabledだが未初期化の場合、RegistryとDraft APIは`InvalidOperationException`とする。
- DisabledではRegistryの一覧へ`DebugCollectionState.Disabled`を返し、Draft操作は`DebugCommandDraftOperationStatus.Disabled`、request生成は`DebugCommandRequestCreationStatus.Disabled`を返す。型付き`Validate`は`CanExecute == false`のUnavailable issue、型付き`CreateRequest`は`DebugCommandValidationException`とする。
- `Discard`と`NotifyInputsChanged`は戻り値がvoidの補助APIとして`ConditionalAttribute("DEXC_DEBUGGER")`を付与できる。他の戻り値付きAPIには付与しない。
- 非同期CommandのcontinuationはCommand実装の契約に従うが、result確定とUI通知はUnityのSynchronizationContextでmain threadへ戻す。Runtimeは独自のmain thread queue、timer thread、同期プリミティブを持たない。timeoutは`Task.Delay`で待つ。
- Play／Player session終了時にRef handle、draft、running request、履歴を破棄する。
- `DexcDebugger.Initialize`の再呼出しでも同じsession状態を破棄し、旧generationのrequestとhandleをstaleにする。
- 再初期化時は実行中の非同期CommandへCancellationを要求するが、旧処理の強制停止やゲーム側副作用のrollbackは保証しない。遅延完了は新sessionへ反映しない。
- Domain Reload無効でもsubsystem registrationでstatic stateをresetする。

## ReflectionとPlayer build

EditorとPlayerで同じruntime Reflection収集を使用する。初回収集後はdescriptorをcacheし、一覧取得や実行のたびにassemblyを再走査しない。`UnityEditor.TypeCache`、source generator、Player build時catalog、生成factory、serializer binding生成、手書きCommand一覧は使用しない。

`DEXC_DEBUGGER`が定義されているassemblyでは、Command classへ`DebugCommandAttribute`を付けることがReflection収集の印であると同時に、Command型と既定constructorをmanaged strippingから保持する印になる。

`DEXC_DEBUGGER`は利用projectがPlayer SettingsのScripting Define Symbolsへ明示的に追加した場合だけ有効とする。packageはsymbolを自動追加せず、`UNITY_EDITOR`、`DEVELOPMENT_BUILD`、`DEBUG`などを代替条件にしない。Editor上でも`DEXC_DEBUGGER`が未定義ならCommand Attributeはmetadataへ出力されず、Command Registryへ収集されない。build targetごとのsymbol設定は利用project側の責任とする。

`DISABLE_DEXC_DEBUGGER`はDexcの各asmdefに設定する`!DISABLE_DEXC_DEBUGGER` Define Constraintの否定条件とする。このsymbolが定義されたbuild targetではDexc assembly自体をcompile対象外にする。`DEXC_DEBUGGER`と同時に定義された場合も`DISABLE_DEXC_DEBUGGER`を優先する。

どちらのsymbolも未定義の場合はDexc assemblyと`DebugCommand.Registry`／`Draft`およびrequest拡張APIを残すが、Command機能状態を`Disabled`とする。Reflection収集、draft生成、validation、実行、履歴作成は行わない。read APIは登録0件と区別できる無効状態を返し、操作requestはCommand Failureとして扱わず、機能無効を示す受付前resultを返す。

戻り値が`void`の補助的な登録・通知APIは`ConditionalAttribute("DEXC_DEBUGGER")`でcall siteごと除去できる。descriptor取得や実行のように戻り値を持つAPIには使用せず、Disabled状態を返す。symbol未定義でもgame側codeはcompileでき、デバッグ処理は実行されない。

CommandのInput／Form elementはfield initializerで生成する。収集とdispatcherが既定constructorからCommand instanceを生成する依存を起点として、field initializer、Input型、overrideした実行memberを保持する。`DebugCommandAttribute`だけですべてのfieldやmethodが無条件に保持されるとは扱わない。

`DEXC_DEBUGGER`が定義されていないassemblyでは、`DebugCommandAttribute`の使用自体がcompiled metadataから除かれる。ゲームコードから別の参照がないCommandとInputはstripping対象にできる。通常のゲームコードから参照される型までDexcDebuggerが強制的に除去するものではない。

Command以外も、Reflection収集の入口となるtype、field、property、method用Attributeは`PreserveAttribute`を継承し、`ConditionalAttribute("DEXC_DEBUGGER")`を付与する。memberへ付与する入口Attributeは、そのmemberと宣言型、signatureで参照する型を保持する。

## 非対象

継続的な値編集、FPS表示、空間描画、任意methodの公開、任意object graphのserialization、Play Mode外のEditorでのCommand実行はDebugCommandの対象外とする。
