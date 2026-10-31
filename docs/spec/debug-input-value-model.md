# Debug Input値モデル仕様

本書はDebugCommandの入力と、UI・履歴・adapterが共有する入力表現を定める。Command Output、DebugSettingの公開field形式、JSON-RPCのwire protocolは対象外とする。

## Input契約

入力型自身がSchemaと値の読み書きを明示し、任意のC#型を暗黙にserializeしない。組み込み入力とユーザー定義入力は同じ契約で扱い、UI、session履歴、外部adapterは型を推測しない。

Commandの入力fieldには、`IDebugInput`を実装したclassだけを使用できる。組み込みの数値、文字列、enum、参照入力も同じinterfaceを実装する。Input fieldの`readonly`は必須にしない。

```csharp
public interface IDebugInput
{
	DebugInputSchema Schema { get; }

	DebugValue GetValue();

	void Validate(
		DebugValue value,
		DebugValidation validation);

	void Apply(DebugValue value);
}
```

- `Schema`は収集時に確定する不変の論理構造を返す。
- `GetValue`は現在値とmutable stateを共有しないDebugValue snapshotを返す。
- `Validate`は値を変更せず、型付き値への変換可能性とInput固有の意味条件をcollectorへ追加する。Null、Kind、Object／Array構造、Schema Constraintの検証はFrameworkが先に行う。
- `Apply`はFrameworkが同じ値のvalidation成功を確認した後にだけ呼ぶ。
- `Apply`は途中まで状態を変更して失敗してはならない。validation成功後のApplyが失敗した場合はInput実装errorとしてdiagnosticへ記録する。

Schemaはdescriptor収集後に変化しない。現在値、候補、validation結果をSchemaへ含めない。

internal runtime adapterはCommand instance生成時に各Input fieldのinstanceを記録する。値はInput interfaceを通して変更し、fieldへ別のInput instanceを代入しない。入力変更通知、validation、実行準備の前後でinstanceの差し替えを検出した場合、そのdraftを実行不可にしてdiagnosticへ記録する。`readonly`でないこと自体はerrorやwarningにしない。

同じ`IDebugInput` instanceを1つのCommand内の複数fieldで共有してはならない。Registryは収集時のCommand instanceで参照重複を検出し、該当Commandを登録しない。

field initializerで作られた初期状態へのresetは`IDebugInput`のmemberにしない。internal draft storeがCommand instance生成直後のDebugValueを保存し、reset時にvalidationしてApplyする。履歴復元と実行instanceへのcopyにも同じValidate／Apply経路を使用する。

### DebugInput<T>

通常のユーザー定義Inputは公開abstract classの`DebugInput<T>`を継承する。

```csharp
public abstract class DebugInput<T> : IDebugInput
{
	protected DebugInput(
		T initialValue,
		DebugInputSchema schema);

	public T Value { get; set; }

	public DebugInputSchema Schema { get; }

	protected abstract DebugValue WriteValue(T value);

	protected abstract DebugInputReadResult<T> ReadValue(
		DebugValue value);

	protected virtual void ValidateValue(
		T value,
		DebugValidation validation);
}
```

基底classはGetValue、Validate、Applyの`IDebugInput`実装を所有する。派生classは型付き値からDebugValueへの書出し、DebugValueから型付き値への読取、必要な場合だけ型付きvalidationを実装する。

`Value`のsetterはpublicとする。internal runtime adapterはValue変更ではなく、Input fieldそのものが別instanceへ差し替えられた場合だけdraftを無効にする。

`DebugInputReadResult<T>`はpublicなimmutable型であり、`Success(T value)`または`Failure(string message)`のfactoryからだけ生成する。成功時は型付きValueだけ、失敗時は変換errorだけを持ち、成功とerrorを同時に表現できない。Failureは利用者向けmessageだけを受け取り、internal runtime adapterが共通の変換失敗codeを付ける。型付き値へ変換できた後の複数constraintは`ValidateValue`からDebugValidationへ追加する。

基底classはReadValueがFailureを返した場合にValidateValueを呼ばず、Applyもしない。Apply時も再度ReadValueを行い、直前のvalidationと異なるFailureになった場合はInput実装errorとする。

`IDebugInput`の直接実装は禁止しない。`InputRef<T>`のように、公開するValue、保存するKey、session handleが単一のTだけでは表せないInputは専用実装を使用できる。組み込みInputと利用者Inputは同じIDebugInput境界へ接続する。

## Command Form element

Commandのpublic fieldには、値入力に加えて`CommandLabel`と`CommandHeader`を置ける。これらはInput interfaceを実装せず、Commandフォームの非入力elementとして扱う。

- `CommandLabel`は固定文字列または`Func<string>`を受け取る。
- `CommandHeader`は固定文字列だけを受け取る。
- `Func<string>`は詳細フォームのsnapshotが要求された時だけmain threadで評価する。Runtime UIでは表示中画面のsnapshot更新、Editorでは再描画・更新、AIではフォーム状態取得が評価契機になる。
- providerは副作用を持たず、短時間で完了しなければならない。
- providerの例外は捕捉し、取得失敗表示とdiagnosticへ変換する。
- LabelとHeaderは`DebugValue`、validation、実行snapshot、履歴Input、外部実行requestへ含めない。
- UIとAIへ公開するフォームsnapshotには、element kind、表示順、評価済みtextを含める。
- elementはfield initializerで生成する。Command instanceの他Inputを参照するためにconstructorでelementを組み立てる形式は契約に含めず、constructorの副作用制約を緩和しない。

### Form elementの表示順

- 既定ではReflectionから得られるfield宣言順を使用する。
- Input、CommandLabel、CommandHeaderを同じ順序列へ並べる。
- elementごとの並べ替えmetadataはない。`DebugCommandAttribute.Order`はCommand一覧の並び順だけに使用し、form elementの順序には影響しない。
- Registryがdescriptor確定時に順序を固定し、Runtime、Editor、AIフォームで同じ順序を使用する。
- 順序は表示上のmetadataであり、履歴や外部requestの入力識別にはfield名またはfield IDを使用する。

## DebugInputSchema

SchemaはInputが受け付けるDebugValueの構造を表す。C#の具象型、Reflection object、UI実装をUI backendと外部adapterへ公開しない。

Schemaは`DebugValueKind ValueKind`を持ち、保存値との一般的な整合をRegistryが検証する。Schema専用の型enumはない。

```csharp
public sealed class DebugInputSchema
{
	public DebugValueKind ValueKind { get; }

	public bool IsNullable { get; }

	public string SemanticType { get; }

	public DebugInputSchema Element { get; }

	public IReadOnlyList<DebugInputMemberSchema> Members { get; }

	public IReadOnlyList<DebugInputConstraint> Constraints { get; }
}
```

- `Element`はArrayだけが持つ。
- `Members`はObjectだけが持ち、DebugValueMemberと同じcase-sensitiveなNameと子Schemaを宣言順で保持する。
- `SemanticType`はゲーム固有ID、date-time、Unity型など、基礎ValueKindだけでは表せない意味を任意文字列で表す。
- `Constraints`は値だけから共通に検査できる閉じたdata定義を持つ。

Schemaは構造ごとのfactoryからだけ生成し、factoryはValueKind、Element、Members、Constraintsの有効な組合せを検証する。mutableなSchemaは公開しない。

```csharp
DebugInputSchema.Boolean(...);
DebugInputSchema.SignedInteger(...);
DebugInputSchema.UnsignedInteger(...);
DebugInputSchema.FloatingPoint(...);
DebugInputSchema.String(...);
DebugInputSchema.Array(DebugInputSchema element, ...);
DebugInputSchema.Object(
	IEnumerable<DebugInputMemberSchema> members,
	...);
```

各factoryは必要に応じて`isNullable`、`semanticType`、`constraints`を受け取る。`DebugInputSchema`、`DebugInputMemberSchema`、`DebugInputConstraint`はimmutable classとする。field属性からConstraintを追加するRegistryはInputが返したSchemaを変更せず、新しい実効Schemaを生成する。

Object Schemaは宣言したMembersだけを持つ閉じた構造とする。値には宣言済みmemberがそれぞれ1件必要であり、memberの欠落、未定義member、重複memberはValidation Errorとする。memberの`IsNullable`は値としてDebugValue.Nullを許可する指定であり、member自体の省略を許可する指定ではない。子Objectにも同じ規則を再帰的に適用する。

DebugValue内のObject memberはSchemaの宣言順で保持する。JSONなどproperty順に意味がない外部表現を受け取るadapterは、case-sensitiveなNameでSchema memberへ対応付け、欠落と未定義memberを検証した後、Schemaの宣言順へ並べてDebugValueを生成する。外部表現のproperty順は要求しない。Framework内部では表示、snapshot、比較に使用する正規順序を1つに固定する。

Array Schemaは単一の`Element` Schemaを持つ同種・順序付きcollectionとする。すべての要素を同じElement Schemaで再帰的に検証する。要素ごとに異なるSchema、tuple、union、sparse arrayはない。

空Arrayはminimum length Constraintが禁止しない限り有効とする。Null要素はElement Schemaの`IsNullable`がtrueの場合だけ許可する。adapterは入力順を保持し、並べ替えや無効要素の除去によって値を補正しない。

### DebugInputConstraint

Constraint Kindは次に限定する。

- numericのinclusive minimum／maximum
- StringまたはArrayのminimum length／maximum length
- Choice／Referenceの未選択可否

ConstraintはKindと比較値だけを持つimmutable dataとし、delegate、任意interface、ゲームobjectへの参照を含めない。Range属性はminimum／maximum Constraintへ変換する。ゲーム固有形式、catalog上の存在、複数member間の条件はConstraintへ追加せず、`IDebugInput.Validate`で検証する。

`DebugInputConstraint`は次のfactoryから生成する。

```csharp
DebugInputConstraint.Minimum(DebugValue value);
DebugInputConstraint.Maximum(DebugValue value);
DebugInputConstraint.MinimumLength(int value);
DebugInputConstraint.MaximumLength(int value);
DebugInputConstraint.SelectionRequired();
```

Null許可はConstraintとして定義せず、Schemaの`IsNullable`を正本とする。

numeric Constraintの比較値は、対象Schemaの`ValueKind`と同じ数値Kindで保持する。`SignedInteger`、`UnsignedInteger`、`FloatingPoint`の間では、値域内であっても暗黙変換を行わない。たとえば`FloatingPoint` Schemaへ`SignedInteger`のminimumを指定することは定義Errorとする。Schema factoryはConstraint追加時にKindの一致を検証し、実行時の値検証も一致するKind同士だけで比較する。

Frameworkは宣言されたConstraintをそれぞれ独立して検証し、複数Constraintの統合、包含関係の比較、有効値が空になるかの解析は行わない。組み込みのRange、length、選択必須属性は`AllowMultiple = false`とする。

単一属性または単一Constraintだけで判定できる不正は定義Errorとする。例として、1つのRange宣言内でminimumがmaximumを上回る場合、負のlength、Constraintの対象外ValueKind、数値Kindの不一致を含む。ユーザー定義Inputが複数経路から組み立てたConstraintの整合はInput実装者の責務とする。

### 検証順序

FrameworkはInput固有処理より先に、SchemaとDebugValueを次の順序で検証する。

1. Nullと`IsNullable`を検証する。
2. `ValueKind`を検証する。
3. ArrayのElementまたはObjectのMembersを再帰的に検証する。
4. Schemaの共通Constraintsを検証する。
5. `IDebugInput.Validate`で変換とInput固有の意味条件を検証する。

`IDebugInput.Validate`はSchemaが表す一般構造を再実装せず、catalog上のID存在、ゲーム固有形式、複数member間の意味的整合などだけを追加する。

Frameworkは1～4の構造検証に成功した場合だけ`IDebugInput.Validate`を呼び、すべてのValidation Errorがない場合だけ`Apply`を呼ぶ。不正値を別の値へ補正したり、部分的に適用した状態を成功として扱ったりしない。

### Editor metadata

人間向けの編集方法はSchemaへ含めず、field属性またはInput型に関連付いたEditor metadataとしてdescriptorへ保持する。共通基底は`DebugInputEditorMetadata`とし、metadata自身は描画処理やUI状態を持たない。

- Commandの数値Input fieldの`DebugRangeAttribute(minimum, maximum)`、`DebugSliderAttribute`と、文字列Input fieldの`DebugMultilineAttribute`は、Schemaを変えずにfield descriptorの`HasRange`、`RangeMinimum`、`RangeMaximum`、`IsSlider`、`IsMultiline`へ保持する。
- Rangeの外の数値は、Inputの検証に加えてError `constraint-minimum`／`constraint-maximum`とする。draftへの入力変更、Command validation、Dispatch直前のいずれでも同じ判定をする。
- `DebugSliderAttribute`はRangeを必須とする。数値でないInputへのRange、Rangeの無いSlider、文字列でないInputへのMultilineは、そのCommandの定義errorとする。
- Rangeだけが指定されたnumberを自動的にSliderへしない。
- Attribute classは`DebugInputEditorMetadata`を継承しない。
- InputChoice、InputRef、Color、VectorなどはInput型に対応する組み込みmetadataを提供できる。
- 利用者定義metadataとeditor providerを登録できる公開拡張境界を持つ。UI framework固有objectをSchemaやdescriptorへ保存しない。

### Editor providerの収集と優先度

editor provider classへ`DebugInputEditorAttribute`を付けてReflection収集する。Attributeは`PreserveAttribute`を継承し、`ConditionalAttribute("DEXC_DEBUGGER")`を持つ。

providerは現在のrendererに対応するRegistryへ収集され、descriptorとrendererから作られたcontextに対して編集可能かを判定する。contextはfieldのEditor metadata、Input具象型、Schema、SemanticType、DebugValueKindを読み取れる。

editor選択の唯一の比較軸はPriorityとする。

1. 現在のrendererに登録されたproviderから、contextを編集可能なものを抽出する。
2. 実効Priorityが最大のproviderを選ぶ。
3. 最大Priorityが同じproviderが複数あれば、Reflection順や登録順で決めず定義Errorとする。
4. providerがなければ読み取り専用とする。

provider class自体の定義が不正な場合は該当providerだけをRegistryから除外する。最大Priorityが競合した場合はproviderをglobalに無効化せず、競合が発生したdescriptorとrendererの組合せだけeditor選択失敗とし、そのfieldを読み取り専用へfallbackする。他のdescriptorで一意に選択できるproviderは引き続き利用できる。

`DebugInputEditorAttribute.Priority`は`uint`で公開する。利用者providerの実効Priorityは0以上とし、組み込みproviderは内部だけで負のPriorityを使用する。対応可能な利用者providerは組み込みfallbackより必ず優先される。内部の実効Priorityは`long`で比較し、`uint`を`int`へcastしない。組み込みPriorityの具体値は公開契約にしない。

providerの適合判定と選択はdescriptorとrendererの組合せごとに1回行いcacheする。毎frameのReflectionやprovider全件走査を行わない。適合判定は副作用を持たず、Unity object探索を行わない。

provider Registryはrendererごとに持つ。同じmetadataに対し、rendererごとに異なるproviderを選択できる。

### Editor providerの公開境界

RuntimeとEditorのproviderは、共通のpublic abstract classである`DebugInputEditor`を継承し、次の2つのmemberを実装する。

- `CanEdit(DebugInputEditorDescriptor descriptor)`: descriptorだけを使い、そのproviderが編集可能かを返す。
- `OnGUI(DebugInputEditorContext context, DebugInputEdit edit)`: 現在frameのIMGUIを描画し、変更候補があれば`edit.SetValue(DebugValue)`へ渡す。

`CanEdit`へ現在値やUI状態を渡さない。適合可否はInput具象型、Schema、SemanticType、DebugValueKind、Editor metadata、renderer種別だけから決め、結果をdescriptorとrendererの組合せごとにcacheする。現在値によって適合可否を変えるproviderは認めない。

`DebugInputEditorDescriptor`は収集済みの不変な定義情報を公開する。少なくともInput具象型、Schema、SemanticType、DebugValueKind、Editor metadata、renderer種別を含む。生の`FieldInfo`やCommand instanceは公開しない。

`DebugInputEditorContext`は1回の描画呼び出しにだけ有効な不変viewであり、次を公開する。

- fieldを識別するUI用ID
- LabelとDescription
- 現在編集中の`DebugValue`
- 既定値と前回実行値。存在しない場合を区別できる
- 現在のvalidation issue一覧
- ReadOnlyかどうか
- 対応する`DebugInputEditorDescriptor`

contextへ`IDebugInput`、Command instance、Registry、対象Unity Object、生のReflection memberを渡さない。providerはInputの`Apply`、Command実行、Setting変更、対象探索を直接行えない。

`DebugInputEdit`はUIからbackendへ変更候補を返すための呼び出し単位のcollectorであり、変更操作は`SetValue(DebugValue value)`だけとする。同一の`OnGUI`中に複数回呼ばれた場合は最後の値を採用し、例外にはしない。

`SetValue`はCommand instanceやInputへ即時反映しない。描画後にSchemaとConstraintで検証し、成功した値だけをUI draftへ反映する。Commandでは`CreateRequest`時にInput固有validationを含めて再検証し、不変snapshotを作る。providerが不正なKindやConstraint違反値を渡しても実行対象を直接変更しない。

provider classは引数なしconstructorから収集時に1 instanceを生成し、以後再利用する。provider instanceはfieldごとの可変UI状態を保持しない。focus、foldout、選択途中のtextなどはrendererがUI用IDをkeyに保持する。静的に再利用できる変換情報のcacheは許可する。

Runtime providerは`UnityEngine`のIMGUI API、Editor providerは`Dexc.Editor`内から`UnityEditor`の`EditorGUI`または`EditorGUILayout`を使用できる。`DebugInputEditorAttribute`のrenderer指定により対応Registryを分け、Editor専用providerはRuntime Playerへ含まれない。

`OnGUI`は毎frame呼ばれ、Reflection再走査、scene全探索、対象列挙、backend operationを行わない。providerから例外が送出された場合はDiagnosticを記録し、そのfieldを当該frameでは読み取り専用表示へfallbackする。他fieldとDebug UI全体の描画は継続する。

Reference用editorを解決できないUIでは、InputRefを読み取り専用または入力不可とし、自由なString入力へfallbackしない。

custom formatの解釈に対応しないUIは、Referenceを除き基礎となるStringまたはObject editorへfallbackしてよい（例: date-timeのSemanticTypeを持つString Schema、x、y、zのmemberを持つ`float3`のObject Schema）。

### 公開範囲と失敗の境界

Input拡張に必要な次の型はpublicとする。

- `IDebugInput`
- `DebugInput<T>`
- `DebugInputReadResult<T>`
- `DebugInputSchema`、`DebugInputMemberSchema`、`DebugInputConstraint`
- `DebugValue`、`DebugValueMember`、`DebugValueKind`
- `InputChoiceBase<T>`、`InputChoiceEntry<T>`
- `DebugInputEditor`と、その公開memberのsignatureに現れるdescriptor、context、edit型

Schema、Constraint、DebugValueとそれらのmember型はimmutableとする。Choiceの公開型はゲーム固有Choiceを実装するために必要な契約だけを公開し、候補cacheやsession解決状態は公開しない。`InputRef<T>`は`InputChoiceBase<T>`を継承しない。

失敗は発生源によって次のように扱う。

- public factoryへ利用者コードが契約上不正な引数を渡した場合は、各factoryが定める引数例外を送出する。
- UI、履歴、外部requestから受け取った不正値は、例外を公開境界へ漏らさずValidation Errorへ変換する。
- `IDebugInput`実装が不変Schema、snapshot独立性、変換、validation、Applyの契約に違反した場合はDiagnosticへ記録し、該当draftだけを実行不可にする。
- `Apply`が失敗した場合は値の補正やrollback成功を仮定せず、Input実装errorとして扱う。

## DebugValidation

Input、Command、Setting、Inspectorは公開型`DebugValidationIssue`と`DebugValidationSeverity`による同じvalidation issue形式を使用する。severityは`Error`と`Warning`の2種類とし、各issueは次を持つ。

| 項目 | 意味 |
| --- | --- |
| Severity | `Error`または`Warning` |
| Code | 機械判定用の空でない文字列 |
| Message | 人間へ表示する空でない公開文面 |

CodeはPathではなく、validationを追加した実装の範囲で安定した識別子とする。UIはMessageを表示し、AI／QAとadapterはSeverityとCodeを機械判定に使用する。Messageを識別子として使用しない。

共通の`DebugValidationIssue`はField IDを持たない。Commandは機能固有の`DebugCommandValidationIssue`で任意の`DebugCommandFieldId`とIssueを関連付ける。SettingとInspectorは単一の操作対象に対するresultがIssue一覧を持つ。

`IDebugInput.Validate`と`DebugInput<T>.ValidateValue`へ渡す`DebugValidation`は、internal runtime adapterが現在のInput fieldへ事前にbindしたcollectorとする。Input実装はfield名やField IDを受け取らず、自身のfieldへ自動的にissueを関連付ける。

```csharp
validation.Error(code, message);
validation.Warning(code, message);
validation.Require(condition, code, message);
```

`Require`はconditionがfalseの場合だけErrorを1件追加し、独自のseverityや返り値を持たない。Warning用の条件APIはなく、Warningは明示的に追加する。

collectorは追加順を保持し、同じissueの自動deduplicateを行わない。validationの呼出しごとに新しいcollectorを使用し、前回結果へ追記しない。CodeまたはMessageが空、未知のSeverityなどcollector契約自体が不正な場合は利用者入力errorではなく定義／実装diagnosticとする。

Errorが1件以上あれば適用またはCommand実行を禁止する。Warningだけなら実行可能とし、UIと機械向けsnapshotへ残す。

## DebugValue

`DebugValue`はInputの現在値とsnapshotを表すimmutableな`sealed class`の共通値treeである。値種別は`Null`、`Boolean`、`SignedInteger`、`UnsignedInteger`、`FloatingPoint`、`String`、`Array`、`Object`の8種類に固定する。

```csharp
public sealed class DebugValue : IEquatable<DebugValue>
{
	public DebugValueKind Kind { get; }

	public static DebugValue Null { get; }

	public static DebugValue From(bool value);
	public static DebugValue From(int value);
	public static DebugValue From(long value);
	public static DebugValue From(uint value);
	public static DebugValue From(ulong value);
	public static DebugValue From(float value);
	public static DebugValue From(double value);
	public static DebugValue From(string value);

	public static DebugValue FromArray(
		IEnumerable<DebugValue> values);

	public static DebugValue FromObject(
		IEnumerable<DebugValueMember> members);
}
```

数値`From`は上記に加えて`byte`、`sbyte`、`short`、`ushort`の個別overloadを持つ。Array／Object factoryは`params` overloadも持つ。

| Kind | 格納値 |
| --- | --- |
| Null | 値なし |
| Boolean | `bool` |
| SignedInteger | `long` |
| UnsignedInteger | `ulong` |
| FloatingPoint | `double` |
| String | `string` |
| Array | 順序付き`DebugValue`一覧 |
| Object | 順序付き`DebugValueMember`一覧 |

`DebugValueMember`はcase-sensitiveで空でないNameとDebugValueを持つ。Object内のName重複を禁止し、member順序を保持する。Objectをdictionaryとして公開しない。Arrayも入力順序を保持する。

- 任意の利用者objectを格納しない。
- Unity Objectの強参照を格納しない。
- Reflection member、例外object、delegateを格納しない。
- 値はsession snapshotとして独立し、元のInput instanceとmutable stateを共有しない。
- Schemaと値が一致しない場合は適用せず、validation errorまたはdiagnosticにする。
- 値としてのNullはsingletonの`DebugValue.Null`で表す。C#の`null`は有効なDebugValueとして扱わず、Input実装errorまたはrequest不正とする。
- public factoryはbool、C#数値primitive、string、Array、Objectから生成できる。数値factoryは呼出し側でcastを要求せず、元のC#型から生成するKindを一意に固定する。
- 各Kindの読取は型別APIを使用し、暗黙の数値変換を行わない。
- 値の変換に`Convert.ChangeType`、serializer、`ToString`を使用しない。DebugValue Kindをまたぐ変換（数値とString、Kind間の数値など）も行わない。
- constructorは公開せず、Kindごとのfactoryだけから生成する。
- Array／Object factoryは受け取ったcollectionとmemberを防御的にcopyし、生成後に外部から内容を変更できないread-only viewだけを公開する。
- `DebugValueMember`もimmutableな`sealed class`とし、生成時に空Nameとnull Valueを拒否する。

読取は格納型に対応する次の非throwing APIを公開する。

```csharp
bool TryGetBoolean(out bool value);
bool TryGetSignedInteger(out long value);
bool TryGetUnsignedInteger(out ulong value);
bool TryGetFloatingPoint(out double value);
bool TryGetString(out string value);
bool TryGetArray(out IReadOnlyList<DebugValue> values);
bool TryGetObject(out IReadOnlyList<DebugValueMember> members);
```

Kind不一致は例外ではなく`false`を返す。factoryへ不正な利用者コードが渡された場合は、Null引数を`ArgumentNullException`、非有限浮動小数点数を`ArgumentOutOfRangeException`、重複Object member、不正Name、tree上限超過を`ArgumentException`として拒否する。外部adapterはこれらの例外をrequest処理へ漏らさず、事前検証または非throwingな内部変換によってValidation Errorへ変換する。

### 数値factory

数値primitiveごとの`From` overloadは次のKindへ変換する。

| C#型 | DebugValueKind | 格納時の変換 |
| --- | --- | --- |
| `sbyte`、`short`、`int`、`long` | SignedInteger | `long`へ正確に拡張 |
| `byte`、`ushort`、`uint`、`ulong` | UnsignedInteger | `ulong`へ正確に拡張 |
| `float`、`double` | FloatingPoint | `double`へ変換 |

`DebugValue.From(1)`、`DebugValue.From(1U)`、`DebugValue.From(1f)`はそれぞれSignedInteger、UnsignedInteger、FloatingPointとなる。

`decimal`、`nint`、`nuint`、`Half`、`char`、enumには数値factoryを設けない。enumは組み込みInputChoiceと同様にString Keyで表す。

数値の読取APIは`TryGetSignedInteger(out long)`、`TryGetUnsignedInteger(out ulong)`、`TryGetFloatingPoint(out double)`だけとする。`int`、`uint`、`float`などへのnarrowingと範囲検証は、その型を所有するInput実装が明示的に行う。暗黙変換を伴う読取API（`TryGetInt32`など）はない。

### FloatingPointの有限値制約

FloatingPointは有限の`double`だけを保持する。`DebugValue.From(double)`はNaN、正Infinity、負Infinityを拒否し、暗黙にStringへ変換しない。`InputFloat`、`InputDouble`、Color、Vector系もDebugValueへ書き出す時点で同じ制約を受ける。

game側Input実装の`GetValue`が非有限値を渡した場合はInput実装errorとしてdiagnosticへ記録する。外部requestやUI編集値として非有限値が指定された場合は入力validation errorとする。非有限値専用のError codeは設けない。

非有限値をStringへ自動変換しない。特殊値を扱うInputの作成者が、文字列表現、許可する値、parse規則をSemanticTypeと`IDebugInput.Validate`で明示する。

### 等価性

`DebugValue`は`IEquatable<DebugValue>`を実装し、参照ではなく値tree全体を構造比較する。`Equals(object)`、`GetHashCode()`、`==`、`!=`も同じ意味へ揃える。

- Kindが異なる値は等しくない。
- Boolean、SignedInteger、UnsignedInteger、FloatingPointは対応するC#値の`Equals`で比較する。
- Stringはordinal、case-sensitiveで比較する。
- Arrayは要素数、順序、各要素がすべて等しい場合だけ等しい。
- Objectはmember数、順序、case-sensitiveなName、各Valueがすべて等しい場合だけ等しい。同じNameとValueでも順序が異なれば等しくない。
- Nullはすべて`DebugValue.Null`と等しく、C#のnullとは等しくない。
- `GetHashCode()`は上記のKind、順序、Name、値を反映し、等しい値が同じhashになる契約を守る。

internal draft storeはこの構造的等価性をfieldのdirty判定、既定値との差、前回値との差へ使用し、各Input型のC# Valueや参照一致を別途比較しない。

enum、Choice、InputRefの現在値はStringのKeyとして表す。InputRefが解決したUnity Objectとsession handleはDebugValueへ含めず、Command session storeが別に管理する。ColorとVector系は名前付きmemberを持つObjectとして表す。

DateTime、ゲーム固有IDなどは新しいValue Kindを追加せず、StringまたはObjectのSchemaへsemantic type／custom formatを付けて表現する。

### 値treeの上限

Coreは次の上限を持つ。

| 上限 | 値 |
| --- | ---: |
| rootを含む最大再帰深度 | 16 |
| 1つのSchemaまたはDebugValue treeに含まれる総node数 | 4,096 |
| 1つのArrayに含まれる最大要素数 | 1,024 |

rootの深度を1とし、Array要素またはObject memberのValueへ移るごとに1増やす。総node数はNullとscalarを含む各DebugValue instanceを1 nodeとして数え、`DebugValueMember` wrapper自体は数えない。Schemaもroot、Element、各Member Schemaを同じtree node単位で数える。

上限はSchemaとDebugValueの両方へ適用し、子Objectと子Arrayも総node数へ含める。Array上限は各Arrayへ個別に適用する。上限値はCoreの公開定数として参照でき、利用者は緩和できない。

上限超過を切詰め、要素除去、既定値への置換で成功扱いにしない。外部requestまたはUI編集値はValidation Error、ゲーム側InputのSchemaまたは`GetValue`が上限を超えた場合は定義／実装diagnosticとする。

文字列長、JSON nesting、request byte数、response byte数などwire formatに依存する容量は各Transportで別に制限する。TransportはCoreより厳しいdepth、node数、Array要素数の上限を設けてよいが、Core上限を緩和できない。Object member数の独立したCore上限はない。

## 組み込みInput

Dexcは次のInputを組み込み提供する。

- `InputBool`
- `InputInt`
- `InputLong`
- `InputFloat`
- `InputDouble`
- `InputString`
- `InputEnum<T>`
- `InputChoice<T>`
- `InputColor`
- `InputVector2`
- `InputVector3`
- `InputVector4`
- `InputVector2Int`
- `InputVector3Int`
- `InputRef<T>`

`InputChoice<T>`はItem IDなどUnity Objectではない候補から1件を選ぶ。`InputRef<T>`は実行中instanceの候補から1件を選ぶ。組み込み実装についてはSchema、値変換、snapshot、標準editorの整合をDexcが保証する。

parameterless constructorと初期値は次のとおりとする。各Inputは明示的な初期値を受けるconstructorも持つ。

| Input | parameterless時の初期値 |
| --- | --- |
| `InputBool` | `false` |
| `InputInt`、`InputLong` | `0` |
| `InputFloat`、`InputDouble` | `0` |
| `InputString` | `string.Empty` |
| Vector系 | zero |
| `InputColor` | `Color.clear` |
| `InputChoice<T>` | 未選択 |
| `InputRef<T>` | 未選択 |

`InputEnum<T>`は`default(T)`を暗黙の初期値にせず、constructorで初期値を必須とする。

ColorとVector系はObject Schemaを使用し、各componentを名前付きmemberとして保持する。たとえば`InputVector3`は`x`、`y`、`z`のFloatingPoint memberを宣言し、`WriteValue`で各`float`を`DebugValue.From`へ渡す。`ReadValue`はSchema検証済みの各`double`が`float`の値域へ収まることを確認してからVector3へ変換する。

### String Input

`InputString`はNullを値として扱わない。

- `Value`は常に非Nullとし、既定値を`string.Empty`とする。
- constructorまたは`Value` setterへC#のNullが渡された場合は`string.Empty`へ正規化する。
- Schemaは`ValueKind.String`かつ`IsNullable = false`とする。
- `DebugValue.From(string)`はNullを拒否し、`DebugValue.Null`から`InputString`へApplyしない。
- UI、履歴、Coreの値適用ではNullと空文字列を相互変換しない。

外部requestでInputStringに明示的なNullが渡された場合はValidation Errorとする。外部Objectにmember自体が存在しない場合も、Coreの閉じたObject Schema規則では欠落Errorとする。

### Choice Input

選択入力の共通実装としてabstractな`InputChoiceBase<T>`を公開する。汎用の`InputChoice<T>`もこれを継承し、利用者はゲーム固有の選択Inputを同じ基底から定義できる。

```csharp
protected abstract IEnumerable<InputChoiceEntry<T>> GetChoices();
```

- `InputChoiceBase<T>`の派生型は`GetChoices()`をoverrideして候補を返す。
- `GetChoices()`は候補一覧を開いた時と実行直前に呼ばれ、その時点のcatalogを返せる。
- 汎用の`InputChoice<T>`は、固定候補または`Func<IEnumerable<InputChoiceEntry<T>>>`をconstructorで受け取る。
- Entryは一意な文字列Key、内部値`T`、Label、任意Pathを持つ。
- UI、履歴、外部adapterはKeyを保存・入力し、`T`自体をserializeしない。
- 選択済み状態は空でないKeyをStringとして、未選択状態は`DebugValue.Null`として表す。空文字列を未選択用の予約Keyとして使用しない。
- overrideまたはdelegateによる動的Sourceは候補一覧を開いた時と実行直前に評価する。
- 既定状態は未選択とし、候補一覧の先頭を自動選択しない。
- constructorまたは派生型から初期Keyを明示できる。初期Keyが現在の候補に存在しない場合はMissingとする。
- 選択中Keyが実行直前の候補に存在しない場合はMissingとして実行不可にし、別候補へ自動変更しない。
- 固定候補も同じEntry形式を使い、収集時にKey重複を検証する。
- Sourceの例外、null Entry、空Key、Key重複はvalidation errorまたはdiagnosticへ変換する。

`InputRef<T>`は`InputChoiceBase<T>`を継承しない。

Choice／Reference Schemaはdraftで未選択状態を保持できるようNull表現を持つ。実行時に選択を必須とするかは未選択禁止Constraintで別に宣言し、必須Inputの初期状態がNullである場合は、定義Errorではなく利用者が選択するまでのValidation Errorとする。未選択を許可するInputはNullのままCommandへ適用できる。存在しないKeyや消失した参照はNullへ自動変換せず、Missingとして扱う。

組み込み型は閉じた一覧ではない。DateTime、Burstの`float3`、ゲーム固有IDなどは、利用者または任意moduleが同じInput interfaceを実装して追加できる。

## ユーザー定義Input

ユーザー定義Inputは組み込みInputと同じFramework、UI、履歴、adapter経路を利用できる。次は実装者の責任とする。

- Schemaと読書きする`DebugValue`が一致すること
- snapshotが元のmutable stateを共有しないこと
- reset後の値がSchemaへ適合すること
- 読取り、適用、validationが予測可能な時間で完了すること
- 収集後にSchemaを変更しないこと

Dexcはユーザー定義Inputの意味的な正しさ、version間互換性、外部protocolとの互換性を保証しない。interface呼出しの例外、Schema不一致、不正値は捕捉し、該当Commandを実行不可にしてdiagnosticへ記録する。

## InputRef

`InputRef<T>`も`IDebugInput`を実装する。Schemaはreferenceであること、対象型、Source key、候補表示構造を表す。

現在値とsnapshotにはUnity Objectそのものを含めず、選択済み状態は空でないString Key、未選択状態は`DebugValue.Null`として保持する。解決したUnity Objectとsession handleはCommand session storeが値treeの外で管理する。外部adapterへ公開する選択Keyの形式は各protocolで決める。session handleを永続IDや別sessionの参照として使用しない。

## JsonUtilityとの関係

Command入力の対応可否と正しさはInput interface、Schema、DebugValueを正本とする。JsonUtilityが対応する任意classをCommand fieldへ直接置くことはできない。

組み込みまたはユーザー定義Inputが内部実装としてJsonUtilityを利用することは妨げない。ただし、JsonUtilityがfieldを無視する挙動をFrameworkの暗黙契約にはしない。

## 他機能での再利用

DebugSettingは対応propertyのCurrent、Default、Preset、履歴を`DebugValue`へ完全変換し、game codeの通常readでは変換せず型付きproperty getterを直接使用する。DebugInspectorは対応値だけを`DebugValue`へ変換し、Unity Object参照、Unsupported、Read Failureを含む観測結果を専用の`InspectorValueSnapshot`で表す。

SettingやInspectorのゲーム側公開APIはInput classではない。それぞれの公開形式、対応型、snapshot、write規則は各機能仕様を正本とする。

Command OutputはInput interfaceを実装せず、Inputの制約を適用しない。Frameworkが認識するOutputだけが結果projectionに`DebugValue`相当の構造化値を持てる。

## 外部protocolとの境界

JSON-RPCを含む外部protocolは、DebugInputSchemaとDebugValueをprotocol固有schemaとwire valueへ変換するadapterとする。JsonUtilityのJSON表現をwire formatとして流用しない。

protocolがcustom formatを表現できない場合のfallback、数値範囲、参照Key、version互換性はTransport仕様で定める。
