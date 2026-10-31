# DexcRemote 運び手仕様

[DexcRemote仕様](remote-control.md)の操作を外部toolへ運ぶ経路と、外部へ口を開く条件、開いたときに見える範囲を定める。操作は[DexcRemote仕様](remote-control.md)を正本とする。

運び手は操作の中身を知らず、操作は運び方を知らない。

## 運び手が満たすもの

| | 契約 |
| --- | --- |
| 要求と応答を1対1で対応づける | 必須 |
| 操作の名前と引数を運ぶ | 必須 |
| 答えを直列化する | 運び手が自前で持つか、本仕様の書き出しを使う |
| main threadへ渡す | 必須。facadeはmain threadを要求する |
| 認証と接続管理 | 運び手の責任 |

満たせないものはcapabilityとして宣言し、操作の集合は変えない。

## Unity CLI

`com.unity.pipeline`が入っているとき、Dexcの各操作をCLIコマンドとして登録する。

```
unity command --project-path <project> dexc_command_list --path_prefix App
```

Editor／Playerへの接続、操作の登録と一覧（`[CliCommand]`、`[CliArg]`、JSON schema生成）、main threadへの受け渡し（`MainThreadRequired`）、直列化、認証（port fileのtoken）、長時間の要求の切り離し実行は`com.unity.pipeline`のものを使う。Dexcが書くのは名前付けだけとする。

### 依存の入れ方

- `com.unity.pipeline`をDexcの依存に加えない。Version Definesで`DEXC_HAS_PIPELINE`を受け、packageがある場合だけCLIの口が生える。無ければ該当fileは何も生まない。`DEXC_HAS_URP`、`DEXC_HAS_UGUI`と同じ扱いである。
- 運び手のassemblyには、packageの`Unity.Pipeline.asmdef`が持つDefine Constraintsをすべて同じ形で付ける。Dexc独自の条件を作らない。Playerへ入るのは、`ENABLE_RUNTIME_PIPELINE_DEV`（development build）か`ENABLE_RUNTIME_PIPELINE`（それ以外）を定義したbuildである。
- この運び手を操作層と別assemblyに置く。`Dexc.Remote.Pipeline`が`Unity.Pipeline`とcommand属性の`Unity.Pipeline.Attributes`を参照し、操作層の`Dexc.Remote`は参照しない。

### 登録する名前

| コマンド | 操作 |
| --- | --- |
| `dexc_status` | status |
| `dexc_initialize` | sessionを開始する |
| `dexc_command_list` | command.list |
| `dexc_command_describe` | command.describe |
| `dexc_command_execute` | command.execute |
| `dexc_setting_list` | setting.list |
| `dexc_setting_read` | setting.read |
| `dexc_setting_presets` | setting.presets |
| `dexc_setting_preview_preset` | setting.previewPreset |
| `dexc_inspector_registered` | inspector.registered |
| `dexc_inspector_scenes` | inspector.scenes |
| `dexc_inspector_children` | inspector.children |
| `dexc_inspector_components` | inspector.components |
| `dexc_inspector_fields` | inspector.fields |
| `dexc_inspector_read` | inspector.read |
| `dexc_display_frame` | display.frame |
| `dexc_diagnostics_list` | diagnostics.list |
| `dexc_diagnostics_detail` | diagnostics.detail |
| `dexc_boot_read` | boot.read |
| `dexc_boot_start` | boot.start。`values`はJSON objectの文字列で渡す |

### sessionの開始

`dexc_initialize`はsymbolを持つassemblyの中に置き、外部toolがsessionを作る唯一の手段とする（`DexcDebugger.Initialize`は`[Conditional("DEXC_DEBUGGER")]`であり、symbolを持たない呼び出し元からは呼出しごと消える）。

### 非同期Command

この運び手は要求をmain threadへ渡し、返るまで待つ。`command.execute`は非同期Commandを`unsupported`で拒否する。

## JSON-RPCの枠

名前付けと直列化を自前で行う運び手のために、JSON-RPC 2.0の枠とJSONの書き出しを用意する。

- 要求は`method`と`params`、応答は`result`または`error`
- `error`の`data.reason`に[DexcRemote仕様](remote-control.md)の理由の文字列を載せる
- 数値codeはJSON-RPCの予約分に留め、理由ごとに数値を増やさない
- encodingは運び手の性質であり、操作ではなくこの層が宣言する

この枠は操作の外側にあり、操作は枠を知らない。枠のassemblyはUnity型を参照しない。Unity CLIの経路ではこの枠を使わない。

## 口を開く条件

- ゲーム側が明示的に開始する。packageによる自動開始は行わない。Unity CLIの経路では、serverを起こすのは`com.unity.pipeline`の設定であり、Dexcは口を足すだけである。
- 有効化の条件は`DEXC_DEBUGGER`と同じとする。専用のdefineを増やさない。
- `DISABLE_DEXC_DEBUGGER`のときはassemblyごと存在しない。
- Playerでserverが動くのはdevelopment buildのみであり、既定は無効である。Dexcはこれを緩めない。

## 露出の範囲

- Setting、Inspectorへの書込みは操作に無い。外部から変更できるのはCommandの実行だけである。
- Editorから実体を送り込む操作もCommandとして表現し、専用の口を作らない。
- Dexcは実体の中身を解釈しない。運ぶのは不透明なbyte列と参照だけとする。
- Commandは`DebugCommandAccess`の既存gateに従う。remote専用の許可listを作らない。

## 実体の受け渡し

[DexcRemote仕様](remote-control.md)のとおり、大きなpayloadは参照と実体に分ける。

- 参照は不透明なtokenとする。外部toolがtokenを構文解析または生成することを禁じる。
- file systemを共有する経路では、実体をfileで受け渡す。外部toolとEditor、同一PCのPlayerがこれにあたる。
- 共有しない経路では、実体を運び手で運ぶ。別machineのPlayerがこれにあたる。

Unity CLIのserverはEditorもPlayerもIPv4のloopbackにしかbindしないため、この運び手では相手は常に同一machineであり、実体は常にfileで受け渡す。分割、整合性の確認、流量制御は持たない。

## 対象

- Unity CLI（`com.unity.pipeline`があるとき）。EditorとdevelopmentのPlayerの両方
- JSON-RPCの枠とJSONの書き出し

## 非対象

- WebSocket、MCP、中継server
- 認証、認可の追加
- server起点の送信
- 実体を運ぶ仕組み（分割、整合性の確認、流量制御）
- captureの取得
- 非同期CommandをUnity CLI経由で待つこと
