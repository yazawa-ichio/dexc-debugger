# DexcRemote仕様

外部tool（Unity CLI、AI、QA runner）がprocess外からDexcを操作するときの、操作の集合と要求・応答の形を定める。運び方は[DexcRemote 運び手仕様](remote-transport.md)、値は[Debug入力・値モデル](debug-input-value-model.md)を正本とする。

遠隔操作はUIと同じfacadeを使い、別経路を作らない。本仕様は運び方を定めず、操作の集合と形はどの運び手で運んでも同じとする。

## 層

| 層 | 責務 | 知ってはならないもの |
| --- | --- | --- |
| Facade | 4機能と起動メニュー（`DebugBootMenu`） | 外部通信の存在 |
| 操作 | Dexcを外から見た形。要求を受け、素のobjectで答える | 運び方、直列化の形式、接続 |
| 運び手 | 運ぶ。名前付け、直列化、接続、認証 | facadeの型、handle実体 |

### GUIとの境界

Editor UIとRuntime UIは操作層を通らず、facadeを直接呼ぶ。遠隔の口をすべて取り除いても、Editor UIとRuntime UIの挙動は変わらない。

### 答えの形

操作層は`Dictionary`、`List`、primitiveの素のobjectで答え、JSON文字列を組み立てない。直列化は運び手に委ねる。自前の直列化を持たない運び手のために、素のobjectをJSONへ書き出す経路も用意する（[運び手仕様](remote-transport.md)）。

## 操作

| 操作 | Play Mode外 | 裏で呼ぶfacade |
| --- | :-: | --- |
| hello | ○ | protocolの版、`DexcDebugger.IsEnabled`／`IsInitialized` |
| session.status | ○ | `DexcDebugger.IsEnabled`／`IsInitialized` |
| command.list | △ | `DebugCommand.Registry.GetList` |
| command.describe | △ | `DebugCommand.Registry.TryGetDescriptor` |
| command.execute | × | `Draft.Create`→`UpdateInput`→`CreateRequest`→`Dispatch` |
| setting.list | △ | `DebugSetting.GetList` |
| setting.read | △ | `DebugSetting.Read` |
| setting.presets | △ | `DebugSetting.GetList`→`GetPresets` |
| setting.previewPreset | △ | `DebugSetting.PreviewPreset` |
| inspector.registered／scenes／children／components／fields／read | × | `DebugInspector.*` |
| display.frame | × | `DebugDisplay.TryGetCompletedFrame` |
| diagnostics.list／detail | × | `DebugDiagnostics.*` |
| boot.read | × | `DebugBootMenu`の開いている起動設定 |
| boot.start | × | 項目を設定してから`DebugBootMenu.Start` |

△はsessionがあれば成立する。sessionを作るのはゲーム側の`DexcDebugger.Initialize`であり、Edit Modeでも作れる。sessionが無い場合は`status`に`disabled`を返す。

書込み系（setting.write／reset／applyPreset、inspector.write）は持たない。

## 対象の指定

handleは外部表現として使わない。

| 対象 | 外部表現 |
| --- | --- |
| Command | `path`（`DebugCommandDescriptor.Path`） |
| Commandのfield | field名（`DebugCommandFieldDescriptor.Name`） |
| Setting | `path` |
| SettingのPreset | `DebugPresetAttribute`で宣言したID（`DebugSettingPresetItem.Key`） |
| Inspectorの対象 | `entityId`（`UnityEngine.Object.GetEntityId()`の生値） |
| Inspectorのcomponent／field | `entityId`と型名・field名 |
| Inspectorのscene | 不透明token。pathまたは名前でも指せる |
| history、diagnostic | 不透明token |

`entityId`のためにfacadeは次を公開する。

| API | |
| --- | --- |
| `DebugInspectorRegisteredEntry.EntityId` | 一覧で値を返す |
| `DebugInspectorHierarchyEntry.EntityId` | 同上 |
| `DebugInspector.TryGetTarget(ulong, out ...)` | 値からcomponentを引く |
| `DebugInspector.TryGetObject(ulong, out ...)` | 値からhierarchyの親を引く |

索引はInspectorが持つものをこの値に合わせ、別の索引を作らない。

`entityId`は`GetEntityId()`から得る。`GetInstanceID()`は使わない。Unityの`EntityId`型を外へ出さず、生値の`ulong`を出す。生値の取り出しは`UNITY_6000_6_OR_NEWER`で`EntityId.ToULong`、それ以前は`int`経由とし、この分岐はfacadeの1箇所に閉じる。値の規則どおり`2^53`を超える生値は文字列となる。

sceneの不透明tokenは一覧の`DebugSceneHandle`の文字列表現をそのまま使い、照合のたびに一覧から引き直す。操作層は何も保持しない。名前で指したsceneが重複する場合は`rejected`とする。

PathはsessionをまたいでもDexcが同じ対象を指す。`entityId`はprocess内でのみ一意であり、再生をまたいで同じ対象を指さない。

Pathまたはentity idが解決できない場合は`status`に`not-found`を返す。Pathが重複する場合は`rejected`とし、いずれかを黙って選ばない。

## session

全ての答えにsessionの状態を載せる。

| | |
| --- | --- |
| `enabled` | Dexcがbuildに入っているか |
| `state` | `active`または`not-initialized` |
| `epoch` | sessionが差し替わるたびに増える整数 |

`DebugSessionId`のGuidは外へ出さない。epochが変わると、それ以前のentity idとtokenは無効になる。Pathは有効なままである。

## 値

`DebugValue`を素のobjectへ写す。

| DebugValueKind | object |
| --- | --- |
| Null | `null` |
| Boolean | `bool` |
| SignedInteger | `long` |
| UnsignedInteger | `ulong`。`2^53`を超える値は`string` |
| FloatingPoint | `double` |
| String | `string` |
| Array | `List<object>` |
| Object | `Dictionary<string, object>` |

値に型tagを付けない。型の正本はdescriptorが持つ（`DebugSettingDescriptor.ValueType`、`DebugInputSchema.ValueKind`）。

`DebugValue`の上限（深さ16、node数4096、配列長1024、非有限のfloating pointを扱わない）をそのまま使い、上限を二重に持たない。

## pageと上限

`DebugListQuery`、`DebugPageRequest`、`DebugPageInfo`を1対1で写す。新しいpaging概念を作らない。

上限超過は黙って丸めず、`page-size-exceeded`として失敗させる。上限は要求を組み立てるときと読むときの両方で検査し、どちらで検出した超過も同じ扱いとする。

## SettingのPreset

setting.presetsは`pathPrefix`の下にSettingを持つSetのPresetを、Setの最初のSettingのPath順、Set内は宣言順に返す。各要素は`id`、`displayName`、`description`を持つ。`pathPrefix`を省略するとすべてのSetのPresetを返す。

setting.previewPresetは`presetId`のPresetを適用した場合の差分を返し、値を変更しない。差分は`differences`に、Settingごとの`path`、`current`、`preset`として載せる。適用できない理由は`issues`に`severity`、`code`、`message`として載せ、`status`を`rejected`とする。該当するPresetが無い場合は`not-found`とする。

## 起動メニュー

boot.readとboot.startはsessionを要求しない。Dexcが入っていなければ`disabled`、起動メニューが開いていなければ`not-found`を返す。

boot.readは開いている起動設定のtag、title、グループごとの項目を返す。項目はlabel、description、type、現在値を持つ。typeは`bool`、`choice`、`integer`、`number`、`text`のいずれかで、`choice`は選べる値、数値は範囲も返す。

boot.startは`values`に渡した項目を設定してから、Startを押したのと同じく開始する。キーは「グループ名/項目名」で、boot.readが返すtitleとlabelをそのまま使う。値は型に合わせ、`choice`は選べる値の名前で渡す。全ての値を検査してから書き込み、該当しない項目は`not-found`、型や範囲が合わない値は`rejected`を返し、何も書き込まずに起動メニューを開いたままにする。書き込んだ値はメニューで変えたときと同じく保存され、ゲームに変更が通知される。

## 失敗の伝え方

2階建てとする。

| 階 | 使う場面 |
| --- | --- |
| 失敗 | 要求そのものが成り立たない。理由をkebab-caseの文字列で持つ |
| status | 要求は成立したが操作が失敗した。答えの中の`status` |

`DebugOperationStatus`の9値（`succeeded`／`rejected`／`read-only`／`explicit-access-required`／`stale`／`not-found`／`definition-unavailable`／`failed`／`disabled`）はすべてstatusとする。`stale`と`not-found`を失敗にしない。

理由の文字列は既存の診断codeと同じkebab-caseで揃え、`malformed-frame`、`invalid-request`、`unknown-method`、`invalid-params`、`unsupported`、`page-size-exceeded`のいずれかとする。運び手ごとの数値codeへの対応は運び手が決める。

Diagnosticは`diagnosticId`のtokenだけを返す。例外型名、message、stack traceを既定で流さない。

## 大きなpayload

取得と送り込みの両方向で、参照と実体を分ける。

- 実体を答えにinlineで置かない。参照を返し、別の操作で取得する。
- 実体を要求のinputへ直接渡さない。先に実体を送り、返った参照をinputにする。

遠隔のdata転送のために`DebugValue`の上限を緩めない。

Editorから実体を送り込む操作もCommandとして表現し、送り込み専用の操作を作らない。外部から変更できるのはCommandの実行だけである。外部toolは実体をfileとして置き、その参照を文字列のinputとしてCommandへ渡す。

Dexcは実体の中身を解釈しない。適用するのはゲーム側のCommandである。

参照の表現と実体の運び方は[運び手仕様](remote-transport.md)で定める。

## Command実行

### 入力

draftの多段（`Create`→`UpdateInput`→`Validate`→`CreateRequest`）を外へ出さない。操作層がその中で完結させ、外部からはpathとinputを一度に渡す。

`DebugCommandFieldDescriptor.InputType`（`System.Type`）は外部表現に含めない。`InputTypeName`を使う。

inputの更新またはrequestの作成が通らなかった場合、`status`に加えて`issues`へCommandの検証結果を載せる。各要素は`severity`、`code`、`message`、`unavailable`を持ち、特定のinputに結び付く場合は`field`にinput名を入れる。

### 排他

`DebugCommandAccess`の排他gateはそのまま効く。取得できない場合は失敗の階ではなくstatusの階で答え、`status`を`failed`、`errorCode`を`command-locked`とする。remote専用の許可listを作らない。

## 対象

- hello、session.status
- Command: list、describe、execute
- Setting: list、read、presets、previewPreset
- Inspector: registered、scenes、children、components、fields、read
- Display: frame
- Diagnostics: list、detail
- Boot: read、start

## 非対象

- Setting、Inspectorへの書込み。Presetの適用を含む
- 通知とserver起点の送信
- 認証、認可
- Displayの連続配信
- Command履歴の機能横断取得。Command単位に留める
- Inspectorのwatch
