# DebugDisplay仕様

## 目的

DebugDisplayは、実機アプリケーションのゲーム画面へ診断情報を出力し、同じ情報をAI・QAへ構造化dataとして提供する機能である。

FPSなど対象を持たない純粋表示と、Enemyごとのmetadata・path・認識範囲・target関係を扱うSubject表示を提供する。

表示のON／OFFはDebugSettingで扱う。DebugDisplayはHUD／Overlay／Worldという表示UIを持つが、独自の設定UI、filter UI、操作Menuは持たない。Runtime Debug Menuの開閉とDisplay rendererは独立しており、Menuを閉じても提供中または寿命が残る表示は継続する。

## DebugDisplaySubject

複数のmetadataと描画を同じobjectへ関連付ける場合は`DebugDisplaySubject`を作成する。

Subjectは次を保持する。

| 情報 | 意味 |
| --- | --- |
| ID | session内で一意な自動生成ID |
| Category | `Enemy`、`Player`などの分類 |
| Owner | 対応するUnity Object |
| Label | 人間とAIが対象を区別する変更可能な表示 |
| Metadata | game ID、type、HP、stateなど |
| Visuals | path、range、bounds、relationなど |

`CreateSubject(Category, Owner)`はSubject handleを返す。LabelはOwner名を初期表示としてFramework内で取得し、利用者固有の表示は生成後に`SetLabel`で設定する。Labelは必要な時だけ更新でき、Subjectを作り直さない。Ownerの無効化または破棄時に`Dispose`し、Subject、metadata、Visualsをまとめて削除する。`Dispose`は複数回呼ばれても安全にする。Owner破棄はRegistry側でも検出する。

`DebugDisplaySubject`は`sealed class`かつ`IDisposable`とする。CategoryとLabelは空文字列を拒否し、Ownerは`GameObject`または`Component`に限定する。同じOwnerから複数Subjectを作ることは許可する。

Subject IDはsession内だけで一意であり、外部保存用の永続IDではない。AI・QAが同じsession内でmetadataとVisualsを関連付けるために使用し、public lookupや別sessionへの再接続には使用しない。

## Conditional compilation

`CreateSubject`と`Dispose`はconditional関数にしない。

次はすべて戻り値のない関数とし、`ConditionalAttribute`で呼び出しを除外できるようにする。機能無効buildでは`SetLabel`へ渡す補間文字列の生成もcall siteから除去される。

- `SetMetadata`
- `SetLabel`、`RemoveMetadata`
- `DrawValue`、`DrawLabel`、`DrawLine`、`DrawRay`、`DrawPolyLine`
- `DrawWireSphere`、`DrawWireCube`、`DrawWireDisc`、`DrawWireSector`、`DrawRelation`
- `DrawSphere`、`DrawCube`、`DrawDisc`、`DrawCapsule`
- 各`For`関数
- `Clear`

Debug機能が無効なbuildでは、`CreateSubject`は共有された軽量なno-op Subjectを返す。

## Subject metadata

`SetMetadata(Name, Value)`は、AI・QAと診断UIへ渡す値をSubjectへ設定する。同じNameへ再度設定すると現在値を更新する。

組み込み型のoverloadはbool、signed／unsigned integer、float／double、string、enum、`Color`、`Vector2`、`Vector3`、`Vector4`、`Vector2Int`、`Vector3Int`を扱う。enumは`where TEnum : struct, Enum`のgeneric overloadとする。拡張値向けに`DebugValue` overloadを持つ。

`DebugValue`への暗黙変換は許可しない。`SetMetadata`の明示的なoverloadが値を内部の型付きslotへ直接書き込み、完了frame確定時にだけimmutableな`DebugValue`へcopyする。組み込みoverloadは更新ごとに中間`DebugValue`やboxingを生成しない。既存Nameの同じ型への更新はslotを再利用する。Nameの型を途中で変更した場合は不正入力として更新を拒否し、diagnosticへ記録する。

`RemoveMetadata(Name)`は現在値を削除する。Metadataはframe寿命ではなく、更新、削除、Subject Disposeのいずれかまで維持する。

metadataは描画方法を持たないscalar、enum、string、IDなどに使用する。

## 構造化Visual

視覚化できる意味情報は、汎用metadataへ描画flagを付けず、型付き関数で登録する。

| 関数 | 構造化data | 人間向け表示 |
| --- | --- | --- |
| `DrawWireSphere` | center、radius、coordinate space | wire sphere |
| `DrawWireCube` | center、size、rotation | wire cube |
| `DrawWireDisc` | center、normal、radius | wire disc |
| `DrawWireSector` | center、normal、forward、angle、radius | 扇形の輪郭（視界など）。閉じたPolyLineとして記録する |
| `DrawSphere` | center、radius、style | 球 |
| `DrawCube` | center、size、rotation、style | 直方体 |
| `DrawDisc` | center、normal、radius、style | 円盤 |
| `DrawCapsule` | center、radius、height、rotation、style | カプセル |
| `DrawRelation` | source Subject、target Subject／Object | line、arrow、label |
| `DrawPolyLine` | point列、coordinate space | path／軌跡 |

`DrawWireSector`はstaticの`DebugDisplay`にだけ定義し、`DebugDisplaySubject`には定義しない。`DebugDrawOptions`を受けるoverloadだけを持ち、`angle`は`forward`を中心に左右へ均等に開く全開き角（度、0〜360にclamp）とする。中心、弧、中心の順の閉じたpoint列を`DrawPolyLine`として記録するため、snapshotとprimitive kindは`PolyLine`となる。

`DrawPolyLine`のpoint列は`IEnumerable<Vector3>`、`ReadOnlySpan<Vector3>`、`Vector3[]`で受ける。Spanは配列・`stackalloc`・`NativeArray`から列挙なしで渡す口であり、配列版はSpanの経路を通る。記録時の点のコピーはどの版でも行う。

これらは一度の入力から、人間向けWorld VisualとAI用の構造化Visualを作る。色、線幅などは人間向け描画情報であり、AIはgeometryと関係情報を正本として使用する。

World座標でないVisualはCoordinate Spaceと基準Transformを明示する。

### Primitive kind

`DebugDisplayPrimitiveKind`は次の値を持つ。

| 値 | 名前 |
| --- | --- |
| 0 | `Value` |
| 1 | `Label` |
| 2 | `Line` |
| 3 | `Ray` |
| 4 | `PolyLine` |
| 5 | `WireSphere` |
| 6 | `WireCube` |
| 7 | `WireDisc` |
| 8 | `Relation` |
| 9 | `Sphere` |
| 10 | `Cube` |
| 11 | `Disc` |
| 12 | `Capsule` |

### 描画style

`DebugDisplayDrawStyle`は`Wire = 0`、`Solid = 1`、`WireAndSolid = 2`とする。

- Global／Subjectの`DrawSphere`、`DrawCube`、`DrawDisc`、`DrawCapsule`は、path・色・style（既定Wire）を受けるframe overloadと、`DebugDrawOptions` overloadを持つ。duration／Infinityはoptions factoryを使う。
- optionsの`WithStyle(style)`はimmutable copyを返す。fill色は線色と同じRGB、alphaは線色の0.25倍を既定とする。`WithStyle(style, fillColor)`で明示可能。未定義styleや非有限fill色は拒否する。`DrawWireSphere`等のWire APIはoptionsのstyleにかかわらずWireで描く。
- snapshotは`DrawStyle`、`FillColor`、`Height`を持ち、表示・外部読み取り・lifetimeの全経路で保持する。primitive identityはSubject／path単位とし、style変更は同identityの更新とする。
- Sphereは球面、Cubeは6面、Discは法線に垂直な円の内部、Capsuleは円柱側面と両端の半球を塗る。32分割の円周を共通使用する（分割数は内部の描画精度であり公開契約ではない）。WireではSphereの3円、Cubeの12辺、Discの円周、Capsuleの赤道円・4本の側線・半球の経線を描く。
- 任意法線のDisc（Wire含む）は法線から直交基底を生成して描く。
- 線分／Ray／PolyLine／Relationと文字・値に塗りつぶしは適用しない。fillを先、wireを後に描く。
- 描画はURP shaderの`LEqual`、alpha blend、`ZWrite Off`、両面表示とし、通常の不透明物体による遮蔽を保つ。透明形状間の厳密な奥行きソートは範囲外とする。

### Capsule

- Capsuleはcenter、radius、height、rotationで定義する。ローカルY軸が長軸で、カプセル方向は`rotation * Vector3.up`。rotationは正規化する。
- heightは半球込みの全高であり、円柱部分だけの長さではない。
- 有限・非負のradius／height、有限・非ゼロのrotationを要求する。heightが直径未満なら球へ縮退する（有効height=max(height,2*radius)）。
- Localはbasisを要求する。中心はTRS変換、長軸はbasisのscale・rotationで変換する。全高は長軸の伸縮率、半径は直交する二軸の伸縮率の最大値で拡大し、再び全高を直径以上にする。shearを含むCollider形状の厳密再現は保証しない。実Colliderの厳密な寸法が必要な場合は呼び出し元がWorld寸法を渡す。
- Collider値の自動取得は行わない。
- Sphere／DiscのLocal半径はbasisの絶対scale最大値を使い、CubeのLocal寸法は通常のscale規則に従う。

## Subjectを持たない純粋表示

FPS、global message、一時的なrayなど、特定objectへ関連付けない表示は`DebugDisplay`のstatic関数（`DrawValue`、`DrawLine`、`DrawLabelFor`など）へ直接渡す。この経路ではSubject metadataを生成しない。

## 表示寿命

### 1 frame

通常の`DrawValue`、`DrawLabel`、`DrawLine`、`DrawRay`、`DrawPolyLine`、`DrawWireSphere`、`DrawWireCube`、`DrawWireDisc`、`DrawRelation`、`DrawSphere`、`DrawCube`、`DrawDisc`、`DrawCapsule`のpath・色を直接受ける版は、呼び出したframeだけ有効とする。`DebugDrawOptions`を受ける版（`DrawWireSector`を含む）はOptionsのLifetimeに従い、`DebugDrawOptions.Frame`で作ったOptionsでは呼び出したframeだけ有効とする。継続表示する場合は毎frame渡すか、duration／InfinityのOptionsを使う。

sessionが無いときのDraw系と`Clear`は何もせず、`CreateSubject`は何もしないSubjectを返し、`TryGetCompletedFrame`はfalseを返す。ゲームはDexcの有無を確かめずに描ける。

### For関数

各primitiveの`DrawLabelFor`、`DrawLineFor`、`DrawPolyLineFor`などはdurationと`DebugDisplayTimeScope`を受け取る。

| TimeScope | durationの意味 | 終了条件 |
| --- | --- | --- |
| Realtime | unscaled秒 | 実時間でduration経過 |
| GameTime | scaled秒 | game timeでduration経過 |
| Infinity | 使用しない | `Clear`されるまで継続 |

Infinityではdurationへ`0`を渡す。同じSubjectとName、または同じglobal Pathへ再度渡した場合はdataと残り時間を更新する。

### TimeScopeと期限管理

- Realtimeは`unscaledTime`基準のexpires atを保持する。
- GameTimeはscaled game time基準のexpires atを保持する。
- Infinityは期限を持たず、ClearまたはSubject Disposeで削除する。
- 同じSubject＋Nameまたはglobal Pathへの再登録でdataと期限を置換する。

Time Scale変更やpause中でもRealtimeは進み、GameTimeはゲーム時間に従って停止する。

## DebugDrawOptions

Path、Color、寿命は不変な`sealed class`の`DebugDrawOptions`へまとめて事前定義できる。

OptionsはPath、Color、Lifetime、TimeScope、Durationを保持し、次のfactoryだけから生成する。

```csharp
DebugDrawOptions.Frame(string path, Color color);
DebugDrawOptions.Realtime(string path, Color color, float durationSeconds);
DebugDrawOptions.GameTime(string path, Color color, float durationSeconds);
DebugDrawOptions.Infinity(string path, Color color);
```

factoryは空Path、非有限Color、0以下または非有限durationを拒否し、Infinityとdurationの矛盾を型の生成経路から除く。Optionsはsession状態とUnity Objectを保持せず、static fieldとして再利用できる。

各primitiveはPath／Colorを直接受ける通常版、durationとTimeScopeを受ける`For`版、Options版を持つ。通常版と`For`版は内部で一時Options objectを生成せず、共通のinternal引数へ展開する。

OptionsのPathはglobal描画では全体identity、Subject描画ではSubject内Nameとして扱う。Coordinate Spaceとbasis TransformはOptionsへ含めず、描画関数へ渡す。

### Clear

Subject内では`Subject.Clear(Name)`、global表示では`DebugDisplay.Clear(Path)`を使用する。個々のVisual handleは返さない。

Clearは同じidentityのcurrent write bufferとRealtime／GameTime／Infinity保持dataを削除する。存在しないidentityへのClearは成功する冪等操作とする。SubjectのClearはVisualだけを対象とし、Metadataは`RemoveMetadata`で削除する。全global表示または全Subject表示を一括削除するpublic `ClearAll`は設けない。

## Identityと座標系

global描画はPath、Subject描画はSubject IDとNameの組をidentityとする。同じframeまたは保持期間中に同じidentityへ再登録した場合は、最後の正常なdataと期限で置換する。primitive kindの変更は拒否し、複数Visualが必要な場合は異なるPath／Nameを使用する。

座標系は`World`と`Local`に限定する。Localではbasis Transformを明示する。Screen座標はHUD／Overlayの`DrawValue`と`DrawLabel`で扱い、World primitiveのcoordinate spaceへ含めない。Colorやmatrixをglobal mutable stateとして設定する`Gizmos.color`／`Gizmos.matrix`相当APIは設けない。

汎用`Shape`とIconは対象外とする。意味が明確なprimitiveへ限定し、wire／solidの描画styleは`DebugDisplayDrawStyle`で指定する。

## 表示ON／OFF（DebugSettingとの境界）

組込みSettingクラス`Display`にboolの`Display/HUD`、`Display/Overlay`、`Display/World`を用意する。既定値はtrue、Immediateで、追加の公開操作APIは設けない。3Pathは予約し、ゲーム側の重複定義は重複定義診断で扱う。

HUDはglobal Label／Value、OverlayはSubjectのLabel／Value、Worldは全World primitiveを対象とする。メニューを閉じても同じ設定を維持する。

ゲーム側のbool Setting（例: `Display/Enemy/Path`）は`Display/`を除いたPathをvisual Pathとsegment単位のprefixで照合する。合致するfalseが1件でもあれば非表示。`Enemy`は`EnemyBoss`に一致しない。未定義・読取失敗・bool以外は描画を維持し、読取失敗はSettingの診断で扱う。組込み3Pathは区分切替専用で、このprefix照合には使用しない。

表示OFFは人間向け描画だけを抑止し、Subject metadataとVisualsの収集とcompleted snapshotを変更しない。geometry生成を省略したい場合はゲーム側が同じSettingまたは専用Settingを確認して生成を省略し、その場合そのdataはAI・QAからも取得できない。

## AI・QA snapshot

AI・QAは直近の完了frameをsnapshotとして取得する。snapshotはframe単位で固定し、取得中に別frameのmetadataやVisualを混在させない。

snapshotは次を含む。

- frame番号とtimestamp
- Subject ID、Category、Label
- Subject metadata
- WireSphere、WireCube、WireDisc、Relation、PolyLineなどのVisuals
- Subjectを持たないglobal表示

Relationのtargetが別Subjectとして登録済みなら、そのSubject IDを返す。未登録Unity Objectの場合は、session内で利用できるobject情報だけを返す。

完了frameの公開readは次とする。

```csharp
public static bool TryGetCompletedFrame(
	out DebugDisplayFrameSnapshot frame);
```

まだ完了frameがない場合と機能Disabled時はfalseを返す。無効状態は`DexcDebugger.IsEnabled`で確認する。

`DebugDisplayFrameSnapshot`は不変dataとしてFrame番号、Timestamp、Subject一覧、Subjectを持たないglobal表示、上限によりdropした件数を持つ。Subject snapshotはSubject ID、Category、Label、Owner情報、Metadata、Visualsを持つ。

Owner情報へUnity Object参照を含めず、session内Object Handle、実行時型名、Scene、Hierarchy Path、Aliveだけをcopyする。Owner消失後に新しく確定するframeからはSubjectを除外する。既に確定済みのframeは取得中に書き換えない。

## 実装構成

### 保持する状態

Displayはsessionごとに次を保持する。

- Subject table
- global表示table
- frame buffer
- duration／Infinity表示table
- renderer向けsnapshot
- AI／QA向けstructured snapshot
- 上限超過と不正入力のdiagnostic

ゲーム側の公開入口は`DebugDisplay`と`DebugDisplaySubject`とする。renderer、UI、AI adapterは確定済みsnapshotを読み、ゲーム側呼び出しを直接購読しない。

### Subject record

`CreateSubject`時にsession IDを発行し、CategoryとOwnerを不変情報、Labelを更新可能な表示情報としてrecordへ保持する。metadataとVisualはName単位で更新する。

- CategoryとOwnerは作成後に変更しない。
- Labelは`SetLabel`で更新できる。毎frame更新を要求しない。
- Ownerは強参照で寿命を延ばさない形で保持する。
- Disposeでrecordを削除し、relationの参照先だった場合はtarget missingとしてsnapshotへ反映する。

no-op Subjectは共有singletonとし、すべてのvoid memberを何もしない実装にする。Disposeしても他利用者へ影響しない。

### Frame buffer

ゲーム側のvoid描画呼び出しはcurrent write bufferへ追加する。Unity frame終了時にbufferを確定し、renderer／AI用read snapshotと入れ替える。

```text
Game calls → Write Buffer → End of Frame → Read Snapshot
```

- 1 frame dataは次の確定時に置き換える。
- metadataはSubject recordの現在値をsnapshotへcopyする。
- duration／Infinity dataは期限判定後にsnapshotへ含める。
- rendererとAIが読み終わるまでread snapshotを変更しない。

## Metadata／Visual validation

metadataは対応serializer profileに適合するscalar、enum、string、ID、対応structへ限定する。巨大collection、循環object、Unity Objectそのものをmetadata値として保存しない。

Visualごとに次を検証する。

- NaN／Infinityを含まない座標
- 非負のradius／size
- point数上限
- Coordinate Spaceと基準Transformの整合
- DrawRelation targetの型と生存状態

Conditional void関数はerrorを返さず、不正dataを登録せずdiagnostic counterと直近messageへ記録する。

## Capacity

次の上限を設ける。上限値は内部値であり、公開Optionsではない。snapshotのbyte量の上限は設けない。

| 上限 | 値 | 対象 |
| --- | --- | --- |
| Subject数 | 256 | 生存中のSubject |
| Subject当たりmetadata数 | 32 | Subjectごとのmetadata Name |
| Subject当たりVisual数 | 256 | Subjectごとに保持中のVisual identity。新しいidentityの登録時に判定する |
| frame当たりglobal Visual数 | 2048 | Subjectを持たない描画のうち、そのframeで新しいidentityとして受け付けた件数。寿命（frame／duration／Infinity）を問わず数え、frame確定時に0へ戻す。既存identityの更新は数えない |
| PolyLine point数 | 256 | 1つのPolyLineのpoint数 |

上限超過時は古いInfinity dataを暗黙削除せず、新規登録を破棄してdiagnosticへ記録する。1 frame dataはframe終了時に一括破棄する。

既存Subjectまたは既存Metadata Nameの更新は新規件数として扱わない。上限到達後の新しいSubject生成はno-op Subject、新しいMetadataは登録拒否とし、古いdataを退避させない。void APIの拒否はdrop counterとDiagnosticへ記録する。

Visualも既存identityの更新を許可し、新しいidentityだけをdropする。PolyLineがpoint上限を超えた場合は切り詰めずprimitive全体を拒否する。NaN／Infinity、不正radius／size、無効basisを含むprimitiveも全体を拒否し、partial dataを完了frameへ入れない。

## Player renderer

World Visualの唯一の表示経路は、同じVisual snapshotを読むRuntime Player rendererである。対象pipelineはURPで、実行中Playerのcamera出力へ通常のUnity描画として合成する。

### Runtime

- HUD／OverlayをPlayer画面へ描画する。
- World VisualをPlayerのruntime camera出力へ描画する。
- DebugSettingのPathを参照して人間向け描画を絞る。
- safe area、最大表示件数、text重なりを制御する。
- `OnDrawGizmos`、`Gizmos`、`UnityEngine.Debug.Draw*`、`UnityEditor`、`Handles`をDexc World Visualの描画またはfallbackに使用しない。
- Player rendererは`Dexc.Runtime`に置き、`DEXC_HAS_URP`のときだけcompileする。同一のWorld primitive／Local basis／rotationを実機で表示する。描画不能なprimitiveをEditor-onlyの代替表示へ逃がさず、diagnosticへ記録する。

### Editor

- EditorはDexc World VisualをGame View／Scene Viewへ独自に描画しない。
- Play Mode中にScene Viewが通常のPlayer-rendered objectを表示することは妨げないが、Dexc.EditorはGizmos／Handlesによるadapter、capture、fallbackを追加しない。
- Subject選択時にmetadataとVisual一覧をdetail paneへ表示する。

renderer固有のcolor、line width、depth testをstructured geometryから分離する。AI snapshotにはgeometryとrelationを必須とし、描画styleは補助情報とする。

## UI接続

DebugDisplayは独立Navigationを持たない。レイアウトは[Debug UIレイアウト・遷移仕様](debug-ui-layout.md)に従う。

- 表示ON／OFFはDebugSetting画面を使用する。
- RuntimeはHUD／Overlay／WorldをPlayer rendererへ直接描画する。
- EditorはSubject detail、snapshot、diagnosticを使用し、World Visualの独自描画はしない。
- `DebugMenu.Close()`はDisplay dataの削除、renderer停止、Subject破棄を行わない。
- diagnostic panelからSubject数、drop数、直近snapshot frameを確認する。

## Threadとlifecycle

- Subject作成、metadata更新、Visual追加、Clear、Disposeはmain threadを要求する。
- frame snapshot確定はUnity lifecycleの規定時点で行う。
- scene unload時は破棄済みOwnerのSubjectをmaintenanceで除去する。
- session終了時にSubject、global表示、snapshotをすべて破棄する。
