# 起動メニュー仕様

## 目的

起動メニューは、ゲームが最初のsceneへ進む前に起動設定を編集し、その値をゲームへ渡してから目的のsceneを読み込む機能である。Dexcのsessionが無くても利用できる。

起動設定の値は`DebugValue`、DebugSetting、Command履歴を経由しない。ゲームは値を開始時のhookで自身の構成へ書き込み、Dexcから読み戻さない。

## 定義

### Set、Block、Item

| 種類 | 基底class | 属性 | 意味 |
| --- | --- | --- | --- |
| Set | `DebugBootSet` | `[DebugBootMenu]` | 名前（Tag）で開き、開始できる1つの起動設定。行き先のsceneを持つ |
| Block | `DebugBootBlock` | `[DebugBootGroup]` | 複数のSetが共有できる項目のまとまり。Tagと行き先を持たない |
| Item | — | `[DebugBootItem]` | SetまたはBlockのpropertyとして宣言する1つの編集項目 |

`DebugBootSet`は`DebugBootBlock`を継承する。SetとBlockはpublic parameterless constructorを持ち、property initializerを宣言値とする。

### 属性

3つの属性はすべて`PreserveAttribute`を継承し、`ConditionalAttribute("DEXC_DEBUGGER")`を持つ。

`DebugBootMenuAttribute`（class、継承しない）:

| member | 意味 |
| --- | --- |
| `Title`（constructor引数） | 表示名。未指定時は型名を単語区切りにした名前 |
| `Order` | Setの並び順 |
| `Tag` | Setの名前。必須 |
| `Scene` | 開始時の行き先。Build Settingsのscene名またはpath。必須 |
| `Groups` | Setが取り込むBlockの型。既定は空 |

`DebugBootGroupAttribute`（class、継承しない）:

| member | 意味 |
| --- | --- |
| `Title`（constructor引数） | 表示名。未指定時は型名を単語区切りにした名前 |
| `Order` | Blockの表示上のOrder |

`DebugBootItemAttribute`（property、継承する）:

| member | 意味 |
| --- | --- |
| `Label`（constructor引数） | 表示名。未指定時はproperty名を単語区切りにした名前 |
| `Description` | 項目の下に表示する説明 |
| `Order` | Set／Block内の並び順 |
| `Persist` | 値を次回起動へ保存するか。既定は`true` |
| `Step` | 数値項目の増減幅。0以下は数値widgetの既定値 |

### 収集

- `Dexc.Runtime`を参照するload済みの非dynamic assemblyから、abstractでない型を収集する。
- `[DebugBootGroup]`を持つ型はBlockとして扱う。同じ型が`[DebugBootMenu]`も持つ場合もBlockとして扱う。
- Itemは、その型のinstance property（public／非public）のうち`[DebugBootItem]`を持ち、indexerでなく、getterとsetter（access修飾子は問わない）を持つものとする。
- Itemの型は`bool`、`string`、enum、`sbyte`、`byte`、`short`、`ushort`、`int`、`uint`、`long`、`ulong`、`float`、`double`とする。それ以外の型のItemはwarningをlogして除外する。
- Itemは`Order`の昇順に並べる。同じ`Order`のItemはpropertyの宣言順に並べる。

次の定義違反は`InvalidOperationException`とする。例外は起動定義を最初に参照した操作（`DebugBootSelection.Open`、`DebugBootMenu.Open`、Dexc Boot window）から送出し、収集結果をcacheしないため、参照のたびに再度送出する。

- `[DebugBootGroup]`を持つ型が`DebugBootBlock`を継承していない
- `[DebugBootMenu]`を持つ型が`DebugBootSet`を継承していない
- `Tag`または`Scene`が空または空白だけ
- `Groups`にnull、同じ型の重複、Blockとして収集されていない型を含む
- 同じ`Tag`（大文字小文字を区別）を複数のSetが宣言している

## 値

### 編集用widget

各Itemは`DebugPanel`のwidgetとして表示する。

| Itemの型 | widget | 値の扱い |
| --- | --- | --- |
| `bool` | `DebugToggleWidget` | On／Off。switchで表示する |
| enum | `DebugChoiceWidget` | 選択肢はenumの名前。現在値が定義済みの値でない場合は先頭を選択中として表示する |
| `string` | `DebugTextWidget` | 文字列 |
| 数値 | `DebugNumberWidget` | 範囲は`DebugRange`があればその値、無ければ整数型は型の値域、`float`／`double`は無限とする。範囲へclampし、整数型は丸めてからpropertyの型へ変換する |

widgetから値を書き込むと、propertyへ設定し、`Persist`が`true`なら保存先へ書き、所有するSetまたはBlockの`OnChanged(propertyName)`を呼ぶ。`OnChanged`の例外はwarningをlogし、menuの処理を止めない。

### 永続化

- 保存先は`DebugStorage`とし、keyは`Boot/<SetまたはBlockの型のFullName>/<property名>`とする。BlockのkeyはBlockの型で決まるため、同じBlockを取り込むSet間で値を共有する。
- 文字列表現は、`bool`が`1`／`0`、`string`がそのまま、enumが名前、数値が`double`のround-trip形式（invariant culture）とする。
- 値の変更時に`DebugStorage.Save`を呼ぶ。`DebugStorage.Flush`は開始時とReset時に呼ぶ。
- Setを開く時、`Persist`が`true`のItemだけ保存値を読み戻す。`bool`は`1`または大文字小文字を問わない`true`を真とする。定義されていないenum名と数値として読めない値は無視して宣言値を残す。読み戻し中の例外はwarningをlogして宣言値を残す。読み戻しでは`OnChanged`を呼ばない。

## Hook

`DebugBootBlock`と`DebugBootSet`は次のprotected virtual memberを持つ。

| member | 定義先 | 呼ぶ時 |
| --- | --- | --- |
| `OnChanged(string propertyName)` | `DebugBootBlock` | Itemの値をwidgetまたはResetで変更した後 |
| `OnOpened(DebugBootGroupsValues values)` | `DebugBootSet` | Setを開き、保存値を読み戻した後、描画前に1回 |
| `OnStarting(DebugBootGroupsValues values)` | `DebugBootSet` | 開始時、保存値をflushした後、行き先の決定とscene読込より前 |
| `ResolveScene()` | `DebugBootSet` | `OnStarting`の後。戻り値を行き先とする。既定は`DebugBootMenuAttribute.Scene` |
| `OnLoadSceneAfter(DebugBootGroupsValues values)` | `DebugBootSet` | scene読込のdelegateが戻った直後 |

`ResolveScene`がnullまたは空文字列を返した場合はsceneを読み込まず、`OnLoadSceneAfter`を呼ばない。

### DebugBootGroupsValues

```csharp
public sealed class DebugBootGroupsValues
{
	public T Get<T>() where T : DebugBootBlock;
}
```

`Get<T>`は開いているSetが`Groups`に列挙したBlockのinstanceを返す。列挙していない型は`InvalidOperationException`とする。Set自身の値はSetのinstanceから読む。

## DebugBootSelection

開いている1つのSetと、その値を保持する。instanceはstaticに保持せず、開いた画面が所有する。

```csharp
public sealed class DebugBootSelection
{
	public static DebugBootSelection Open(string tag);
	public string Tag { get; }
	public string Title { get; }
	public IReadOnlyList<DebugPanel> Panels { get; }
	public string ResolveScene();
	public void Flush();
	public void Reset();
	public bool Start(Action<string> loadScene);
}
```

- `Open`は`tag`がnullまたは空白だけなら`ArgumentException`、該当するSetが無ければ宣言済みTagの一覧を含む`InvalidOperationException`とする。SetとBlockのinstanceを新しく生成し、保存値を読み戻し、`OnOpened`を呼ぶ。`OnOpened`の例外は呼出元へ送出する。
- `Title`は`DebugBootMenuAttribute.Title`、未指定時は型名を単語区切りにした名前とする。
- `Panels`はSet自身のItemのpanelを先頭に置き、続けて`Groups`の順に各Blockのpanelを置く。panelの表示名はSet／Blockの表示名、`DebugPanel.Order`は属性の`Order`とする。
- `ResolveScene`はSetの`ResolveScene`を呼ぶ。
- `Flush`は`DebugStorage.Flush`を呼ぶ。
- `Reset`はSetと各Blockの全Item（`Persist`の値を問わない）について保存値を削除し、型の新しいinstanceが持つ宣言値をpropertyへ設定し、Itemごとに`OnChanged`を呼ぶ。最後に`DebugStorage.Flush`を呼ぶ。
- `Start`は`loadScene`がnullなら`ArgumentNullException`とする。`Flush`、`OnStarting`、`ResolveScene`の順に行い、行き先が空なら`false`を返す。行き先があれば`loadScene(scene)`を呼び、`OnLoadSceneAfter`を呼んで`true`を返す。hookと`loadScene`の例外は呼出元へ送出する。

## DebugBootMenu

`Dexc.DebugUI`のstatic classであり、`DebugMenu`と独立して起動メニューを開閉する。状態はRuntime UIのroot状態が保持し、`DexcRoot`の作り直しで`Started`の購読、`Title`、`AutoStartSeconds`、`Alignment`、providerを含めて破棄する。

```csharp
public static class DebugBootMenu
{
	public static bool IsOpen { get; }
	public static float AutoStartSeconds { get; set; }
	public static DexcRuntimeUIAlignment? Alignment { get; set; }
	public static string Title { get; set; }
	public static event Action Started;

	[Conditional("DEXC_DEBUGGER")]
	public static void Open(string tag, Action<string> loadScene);

	[Conditional("DEXC_DEBUGGER")]
	public static void Close();

	[Conditional("DEXC_DEBUGGER")]
	public static void Start();

	public static void SetInputProvider(DebugMenuInputProvider provider);
}
```

| member | 動作 |
| --- | --- |
| `IsOpen` | 起動メニューが開いているか |
| `AutoStartSeconds` | 自動開始までの秒数。0以下は自動開始しない。`Open`の時点の値を使う |
| `Alignment` | 起動メニューをRuntime Debug Menuで表示する時のpanel配置。nullならSkinのprofileの配置を使う。単独表示には適用しない |
| `Title` | headerの表示名。既定は`Boot Menu` |
| `Started` | `Start`で閉じた後に呼ぶevent |
| `Open` | 下記 |
| `Close` | 開始せずに閉じる。hookを呼ばない。Runtime Debug Menuで表示していた場合はRuntime Debug Menuも閉じる |
| `Start` | 下記 |
| `SetInputProvider` | 単独表示で使うproviderを設定する |

### Open

1. `loadScene`がnullなら`ArgumentNullException`とする。
2. `DebugBootSelection.Open(tag)`でSetを開く。例外は呼出元へ送出し、それまでの状態を変えない。
3. 開いたSetと`loadScene`を保持し、`IsOpen`を`true`にする。focus、notice、自動開始の期限を初期化する。
4. 入力の読み取りと自動開始のためのdriver（`DexcBootMenu` GameObject、`DontDestroyOnLoad`）を用意する。
5. `DexcDebugger.IsInitialized`が`true`ならRuntime Debug Menuで表示する。`false`なら単独のIMGUI表示とする。

既に開いている間に`Open`した場合は、新しいSetで置き換える。

### Start

開いていない場合は何もしない。開いている場合は、`Close`と同じく閉じてから、保持していたSelectionの`Start(loadScene)`を呼ぶ。続けて`Started`を呼ぶ。`Start(loadScene)`と`Started`の例外はそれぞれ`Debug.LogException`で出力し、`Started`は行き先が無い場合や`Start(loadScene)`が例外を送出した場合も呼ぶ。

### SetInputProvider

`SetInputProvider`は`ConditionalAttribute`を持たない。現在のproviderを拒否せずに置き換え、nullを渡すと解除する。`DebugMenu.SetInputProvider`のような競合diagnosticと解除手順は持たない。

設定したproviderは単独表示でだけ使う。providerが未設定の場合、単独表示は新Input Systemの`Gamepad.current`を`DebugGamepadInputProvider`と同じ規則で直接読む。providerの例外はそのframeを入力なしとして扱い、例外のmessageをnoticeへ表示する。sessionがあれば、Runtime Debug Menuと同じcode `input-provider-read-failed`のdiagnosticを、providerの型・例外の型・messageの組ごとに1回だけ記録する。Runtime Debug Menuで表示している間は、このproviderを使わず`DebugMenu`のactive providerを使う。

## 表示

### Runtime Debug Menuでの表示

`Open`の時点でsessionがある場合に使う。

- 選択中のrenderer（IMGUI／uGUI／UI Toolkit）でRuntime Debug Menuを開き、Panels一覧を表示する。Panels一覧と詳細は、登録済みの`DebugPanel`ではなく開いているSetの`Panels`を表示する。
- headerは`Title`、renderer名、自動開始の残り秒数（計測中のみ）を` · `で連結して表示する。`Title`が空なら`Boot Menu`を使う。Panel詳細ではこの文字列をmenu内の位置としてpanel名の上に小さく表示する。
- Panels一覧が最上位であり、headerの戻るボタンと閉じるボタンを表示しない。Panels一覧での戻る操作（Escape、providerのcancelを含む）は何もしない。
- 全ページのfooterにResetとStartを置く。Resetは左に固定幅、Startは残りの幅とする。Panels一覧のnavigation順はpanel行、Reset、Startとする。
- 項目の表示はRuntime Debug Menuの共通フォームに従う（boolはswitch、enumは‹ 値 ›）。SetまたはBlockの宣言値（型の新しいinstanceが持つ値。Resetで戻る値）と異なる項目は、項目名の後ろに`*`を付ける。行の高さは変えない。
- 横並び表示ではセクション列を置かない。
- 表示中にRuntime Debug Menuを閉じる呼出し（`DebugMenu.Close`、開いている間の`DebugMenu.Toggle`）は`DebugBootMenu.Start`として扱う。renderer切替に伴う一時的なcloseは除く。
- 数値と文字列の項目は、入力中の文字をdraftとして保持し、Enterまたはfocus離脱で確定する。数値として読めない入力はnoticeに表示し、値を変えない。
- `Alignment`を適用する。

### 単独表示

`Open`の時点でsessionが無い場合に使う。driverが`OnGUI`でIMGUIのpanelを描く。

- panelの寸法と配置はRuntime UIのprofileに従う。sessionが無い間はprofileの既定値を使う。
- headerに`Title`と自動開始の残り秒数、contentにpanelごとの見出しと項目行、footerにResetとStartを置く。Resetは左に固定幅、Startは残りの幅とする。項目が1つも無い場合は案内文を表示する。widgetの例外とReset結果はcontent下部のnoticeへ表示する。
- 項目行は項目名を左（行幅の40%）、値を右に置く。boolはswitch、enumは`‹ 値 ›`、数値は`‹ 入力欄 ›`、文字列は入力欄で表示する。宣言値と異なる項目は項目名の後ろに`*`を付ける。
- 横並び表示（Runtime UIの`Layout`）でpanelが2つ以上ある場合は、contentの左にpanelの見出しを並べた列を置く。focus中の項目を含むpanelを選択表示にし、見出しを押すとそのpanelの最初の項目へfocusを移して表示位置へスクロールする。この列はpointer専用で、focus順に含めない。横並びにできる画面（横長で幅が足りる場合）ではheaderに`Single`／`Split`の切替を置く。
- focus順はlabel以外の項目行、Reset、Startとする。focus中の項目行は選択背景、footerのボタンは輪郭で示し、「▶」の印を付けない。
- keyboardとproviderによる操作は、1 frameに1つまで処理する。

| 入力 | 動作 |
| --- | --- |
| 上下矢印、Tab／Shift+Tab | focus移動 |
| 左右矢印 | focus中の項目を調整（toggleは反転、choiceは前後、数値は`Step`だけ増減） |
| Enter、Space | focus中の項目を実行（toggleは反転、choiceは次、数値と文字列は入力開始、Reset、Start） |
| Escape | 自動開始の取消 |
| 文字列入力中のEnter、Escape | 入力を確定して入力欄を離れる |
| providerのnavigation | 縦方向はfocus移動、横方向は調整。dead zoneは0.35、同じ方向の初回repeatは0.35秒後、以降0.08秒間隔 |
| providerのsubmit | 実行 |
| providerのcancel | 自動開始の取消。文字列入力中は入力欄を離れる |
| pointer | 行を押すとfocusし、各ボタン（‹、›、値、Reset、Start）を実行する。switchは値の領域のどこを押しても反転する |

### 自動開始

`Open`の時点で`AutoStartSeconds`が0より大きい場合、実時間（`Time.realtimeSinceStartup`）で期限を設ける。期限に達し、取り消されていなければ`Start`を呼ぶ。focus移動、調整、実行、cancel、行の選択、Runtime Debug Menuでのnavigationと戻る操作は自動開始を取り消す。取り消した自動開始は再開しない。

### Reset

ResetボタンはSelectionの`Reset`を呼び、全項目を宣言値へ戻し、保存値を削除する。確認dialogは表示しない。結果として`Reset to defaults.`をnoticeへ表示する。Runtime Debug Menuでの表示では入力中のdraftも破棄する。例外はnoticeへ表示する。

## DebugBootScene

`Dexc.DebugUI`の`MonoBehaviour`であり、ゲームの最初のsceneより前に置く開発用sceneで起動メニューを開く。component menuは`Dexc/Debug Boot Scene`とする。classはDexc assemblyが存在する全buildに存在し、serialized fieldと処理は`DEXC_DEBUGGER`のときだけ存在する。

| field | 意味 |
| --- | --- |
| `m_Tag`（public property `Tag`） | 開くSetのTag |
| `m_InitializeDexc` | sessionが無い場合に`DexcDebugger.Initialize`を呼ぶ |
| `m_AutoStartSeconds` | `DebugBootMenu.AutoStartSeconds`へ設定する値 |
| `m_Title` | `DebugBootMenu.Title`へ設定する値。空なら`Boot Menu` |
| `m_Skin` | 指定時はsessionが無ければこのSkinで`DexcDebugger.Initialize`を呼ぶ |

`Start`での動作:

1. `DexcDebugger.IsEnabled`が`false`なら何もしない。
2. `m_InitializeDexc`が`true`または`m_Skin`が指定されていて、sessionが無ければ、`RuntimeUI.Skin`に`m_Skin`を設定して`DexcDebugger.Initialize`を呼ぶ。
3. sessionがあれば、保留中のrenderer切替を適用し、`DebugMenu`にproviderが無ければ`DebugGamepadInputProvider`を設定する。
4. `DebugBootMenu.Title`と`AutoStartSeconds`を設定し、`SceneManager.LoadScene(scene, LoadSceneMode.Single)`を行き先の読込として`DebugBootMenu.Open(Tag, ...)`を呼ぶ。

`OnDestroy`では起動メニューが開いていれば`DebugBootMenu.Close`を呼ぶ。

## Dexc Boot window

`Dexc.Editor`のEditorWindowであり、menu `Window/Dexc/Dexc Boot`から開く。起動用sceneを経由せずにSetを選び、編集し、Play Modeを開始する。

- Dexcが無効なprojectでは無効である旨だけを表示する。
- Play Mode中は、停止してから選ぶよう案内する文だけを表示し、編集と開始を受け付けない。
- 左側にSetを`Order`、`Tag`の順で並べ、表示名（`Title`、未指定時は型名）と`Scene`を表示する。選んだSetを`DebugBootSelection.Open`で開き、windowが保持する。開けない場合は例外のmessageを表示する。
- 右側に各panelの見出しと項目を表示し、Startボタンと`Reset to declared values`ボタンを置く。`ResolveScene`が空の場合はStartを無効にし、行き先が無い旨を表示する。
- `Reset to declared values`はSelectionの`Reset`を呼ぶ。
- Startは先に変更済みsceneの保存を確認する。取り消した場合はSelectionの`Start`を呼ばず、値の保存もPlay Modeへの移行もしない。保存または破棄が選ばれた場合にSelectionの`Start`を呼ぶ。scene読込のdelegateは、Build Settingsのsceneのうちfile名またはpathが行き先と一致するsceneを開く。Build Settingsに無い場合は例外のmessageを表示する。`Start`が`true`を返した場合はPlay Modeへ入る。

## 遠隔操作

`boot.read`と`boot.start`（Unity CLIでは`dexc_boot_read`、`dexc_boot_start`）は`DebugBootMenu`で開いている起動メニューを扱う。sessionを要求しない。要求と応答の形は[DexcRemote仕様](remote-control.md#起動メニュー)を正本とする。

- `boot.read`は開いているSetのTag、`DebugBootMenu.Title`、`Panels`ごとの見出しと項目を返す。
- `boot.start`の項目指定は「panelの見出し/項目のLabel」とし、最初の`/`で分割して、一致する最初のpanelと最初の項目を対象にする。全ての値を検査してからwidgetを通して書き込むため、保存と`OnChanged`はmenuで変更した場合と同じになる。書き込んだ後に`DebugBootMenu.Start`を呼ぶ。
