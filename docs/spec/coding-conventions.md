# DexcDebugger コーディング規約

DexcDebuggerの規範的なコーディング規約である。規約を変更する場合は、この文書と実装を同じ変更で更新する。

## 基本方針

- 読みやすさとUnity上での挙動の予測可能性を優先する。
- 公開APIは小さく保ち、実装詳細は`internal`または`private`にする。
- 宣言、登録、検証、実行、表示、通信を別責務として扱う。
- 暗黙の規則を使う場合は、同じ操作を明示的に指定する方法も用意する。
- Editor専用処理をRuntimeアセンブリへ混在させない。
- DebugCommand、DebugSetting、DebugInspector、DebugDisplayのRegistryとlifecycleを混在させない。
- Runtime UI、Editor、Transport／MCP adapterは、4機能のbackendを利用する別レイヤーとして扱う。
- 利用コードを作る前に、対象選択の汎用モデルを確定しない。

各機能と表示レイヤーの責務やモジュール境界については[機能設計](feature-design.md)を参照する。

## ファイルと構造

- 原則として、主要な型1つにつき1ファイルとする。
- ファイル名は主要な型名と一致させる。
- RuntimeとEditorはディレクトリとasmdefの両方で分離する。
- Testsはrepositoryの`Assets`以下に置き、配布packageへ含めない。test用のReflection対象型を利用projectへ公開しない。
- 4機能とbackendは`Dexc.Runtime`、Runtime UIは`Dexc.DebugUI`、Editor UIは`Dexc.Editor`へ分離する。4機能ごとのasmdefは増やさない。
- 任意package連携はVersion Definesとcompile symbolを優先し、packageごとのintegration asmdefを作らない。
- Dexcの製品・test asmdefは`!DISABLE_DEXC_DEBUGGER`をDefine Constraintに持つ。`DEXC_DEBUGGER`は機能有効化、`DISABLE_DEXC_DEBUGGER`はassembly除外として使い分ける。
- 機能有効状態の公開判定にはread-only propertyの`DexcDebugger.IsEnabled`を使用する。public `const`へcompile symbolの状態を埋め込まない。
- `DexcDebugger`へ4機能のAPIを集約しない。Runtime操作画面は`DebugMenu`、表示data出力は`DebugDisplay`、各機能の操作は各トップレベル型へ置く。
- Unityアセットを追加・移動するときは対応する`.meta`も管理する。
- 条件付きコンパイルを使う場合、シンボルの意味とビルド包含条件をREADMEまたは該当モジュールの文書に記載する。

## レイアウト

- インデントはタブ1文字。
- 波括弧は改行して配置する（Allmanスタイル）。
- 1行に複数の文を書かない。条件と処理を1行にまとめない。
- 改行コードとファイル末尾の改行をリポジトリ内で統一する。
- `using`はファイル先頭、`namespace`の外側に置く。
- namespaceはブロック形式を使用する。
- 短い自明なプロパティや関数には式形式を使用してよい。
- 条件分岐の本体は、単文でも原則として波括弧を付ける。

## 命名

| 対象 | 規則 | 例 |
| --- | --- | --- |
| namespace | PascalCase、機能階層 | `Dexc.Transport.JsonRpc` |
| class／struct／enum | PascalCase | `CommandDescriptor` |
| interface | `I` + PascalCase | `ICommandHandler` |
| attribute | PascalCase + `Attribute` | `DexcCommandAttribute` |
| public／protected member | PascalCase | `ExecuteAsync` |
| private／internal instance field | `m_` + PascalCase | `m_Registry` |
| private／internal static field | `s_` + PascalCase | `s_DefaultOptions` |
| const | PascalCase | `DefaultPort` |
| parameter／local variable | camelCase | `commandName` |
| generic type parameter | `T`または意味のある`T`接頭辞 | `TParams`, `TResult` |
| async関数 | `Async`接尾辞 | `ExecuteAsync` |
| bool | 状態や判定が読める名前 | `IsRegistered`, `HasResult` |

略語は単語として扱う。新規公開APIでは`Rpc`、`Json`、`Mcp`を使用し、`RPC`、`JSON`、`MCP`を型名の途中へ混在させない。ただし製品の画面レイヤーを表す`UI`は固定略語として常に大文字で表記し、`Ui`は使用しない。たとえばRuntime UIの型名は`DexcRuntimeUI`とする。

綴り間違いは互換性より前に修正する。公開APIの綴り間違いを維持しない。

## アクセス修飾子とメンバー

- 公開する理由がない型とメンバーは`internal`または`private`にする。
- 公開型のアクセス修飾子は省略しない。
- privateフィールドではアクセス修飾子を省略してよい。
- 変更不要なフィールドは`readonly`にする。
- 継承を想定しない公開classは`sealed`を検討する。
- setterを公開する必要がないプロパティは`private set`またはgetterのみとする。
- interfaceは利用側が必要とする最小の契約にする。
- 明示的interface実装は、公開面を狭める場合や同名メンバーの意味を分離する場合に使用する。
- IL処理によって非公開関数を暗黙にpublicへ変更しない。

## 型と式

- 右辺から型が明確なローカル変数には`var`を使用する。
- 公開API、field、parameter、戻り値では型を明示する。
- `default`リテラルは対象型が明確な場合に使用する。
- nullが正当な状態かどうかをAPI上で明確にする。
- 文字列による識別子は、登録時に正規化・検証する。
- タプルは局所的な内部処理に限定し、公開APIには名前付き型を優先する。
- マジックナンバーやプロトコル文字列を散在させない。

## 属性とreflection

- 属性はメタデータの宣言に使い、重い処理や状態を持たせない。
- 属性コンストラクターには必須値、名前付きプロパティには任意値を置く。
- 属性名から`Attribute`を省略して利用できる命名にする。
- 戻り値が`void`のdebug専用呼出しは必要に応じて`ConditionalAttribute("DEXC_DEBUGGER")`を使用する。戻り値を持つAPIへ`ConditionalAttribute`を使用せず、明示的なDisabled resultまたはno-op objectを返す。
- 定義はEditorとPlayerで同じ経路により、loaded assemblyのReflection走査で収集する。DebugCommandのCommand class、DebugSettingのSet、起動メニューのSetとBlock、DebugInspectorのSource、入力editor providerは、どれも`Dexc.Runtime`を参照するassemblyだけを走査する。型名から型を引く解決も、名前ごとの結果をassemblyの寿命の間cacheする。
- これらの定義収集以外の目的でAppDomain全体をReflection走査しない。
- Reflection収集結果はcacheし、一覧表示や毎frame処理で再走査しない。
- Reflection収集の例外と不完全なassemblyは黙って無視せずdiagnosticへ記録する。`GetTypes()`、Reflectionによるfield access、シリアライズの例外を未整理のまま上位へ流さない。
- 静的コンストラクターだけで登録状態を固定しない。
- 任意methodへの属性、delegate、lambdaをCommandとして公開しない。
- Commandのpublic instance fieldは`IDebugInput`、`CommandLabel`、`CommandHeader`のいずれかとする。通常のユーザー定義Inputは`DebugInput<T>`を継承し、その他のraw値fieldを公開しない。
- OutputはCommand実行memberの戻り値として表す。
- Command classは副作用のないpublic parameterless constructorを持つ。
- Reflection収集の対象範囲とcode strippingへの対応は機能ごとの仕様で定める。

## エラー処理と診断

- 登録エラーと実行エラーを区別する。
- エラーには、Command Path、対象型、原因を含める。
- 同名登録や曖昧なハンドラー選択を暗黙に上書きしない。
- 回復不能な構成エラーは早期に報告する。
- ログはUnityの`Debug.LogException`、`Debug.LogWarning`、`Debug.LogError`を直接使う。ロガーの抽象化や注入は設けない。
- 利用者codeの例外を捕捉してUIやlibraryの処理を継続する場合、session diagnosticへ記録できるものはdiagnosticへ記録する。diagnosticの対象外の例外はUnityのlogへ出力し、握りつぶさない。

## 同期、非同期、Unity

- 同期Commandと非同期Commandは別の基底型とRegistry entry pointを使用する。
- 待機を必要としないCommandを`Task`または`ValueTask`で包まない。
- 非同期Commandは`ValueTask<T>`または`Task<T>`のどちらかへ統一する。
- `async void`はUnityイベントなど戻り値を持てない境界以外では使用しない。
- 非同期Commandは`CancellationToken`を受け取り、同期Commandは受け取らない。
- Unity Objectのnull判定は境界で明示的に検証する。
- libraryが持つmutable staticは`DexcRoot`だけとし、実行中の状態はすべて`DexcRoot`から辿る。`DexcRoot`はSubsystem RegistrationとEditorのEdit Mode進入時に作り直す。
- libraryはmain thread専用を仕様とし、別threadからの呼出しを検査する処理を置かない。
- Editor APIは`Editor` asmdefへ隔離し、`#if UNITY_EDITOR`だけに依存した分離を避ける。

## テスト

- RuntimeとEditorのテストasmdefを分離する。
- 公開機能には正常系、入力不正、重複登録、例外、キャンセルのテストを書く。
- Command契約には、Input interface fieldなし／あり、Output戻り値なし／ありを含める。
- 非同期処理には完了、例外、キャンセル、timeoutを含める。
- Unityライフタイム処理には、scene破棄とDomain Reload無効を含める。
- Reflection収集には、assembly読込失敗、継承、generic Output、abstract class、無効constructor、Input interface fieldを含める。
- 履歴処理には、Command単位の前回Input取得、Failure、Cancelled、capacity超過を含める。
- 不具合修正には、修正前に失敗する回帰テストを追加する。

## コメントとドキュメント

- コメントは「何をしているか」より「なぜ必要か」を記述する。
- 公開APIにはXMLドキュメントコメントを付ける。
- 制約、thread affinity、所有権、例外、戻り値の意味を公開APIに記載する。
- TODOには目的または追跡先を含める。
- 古いコードをコメントアウトして残さない。
