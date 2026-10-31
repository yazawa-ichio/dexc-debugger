# Remote

Unity CLIから、Editorやdevelopment buildのPlayerで動いているDexcを操作できます。AIやテストからCommandを実行したり、値を読んだりする用途です。

## 準備

利用するprojectへ`com.unity.pipeline`を追加します。packageがあるときだけ、DexcのCLIコマンドが登録されます。PlayerはProject Settings > PipelineでPipelineの組み込みを有効にし（development buildなら`ENABLE_RUNTIME_PIPELINE_DEV`が付きます）、development buildで動かします。

## 呼び出し

Editorへは`--project-path`、Playerへは`--runtime-path`（Playerのフォルダ）を指定します。

```sh
unity command --project-path <project> dexc_status
unity command --project-path <project> dexc_command_list --path_prefix Game
unity command --project-path <project> dexc_command_execute --path "Game/Set Time Scale" --inputs '{"TimeScale":2}'
unity command --runtime-path <player-folder> dexc_setting_read --path "Game/Player Speed"
```

`--inputs`はCommandの入力field名をkeyにしたJSONです。

## コマンド

| コマンド | 内容 |
| --- | --- |
| `dexc_status` | Dexcが有効か、sessionが動いているか |
| `dexc_initialize` | sessionが無ければ始めます |
| `dexc_command_list`／`dexc_command_describe`／`dexc_command_execute` | Commandの一覧、入力の確認、実行 |
| `dexc_setting_list`／`dexc_setting_read` | Settingの一覧と値 |
| `dexc_setting_presets`／`dexc_setting_preview_preset` | Presetの一覧と、適用した場合の差分（適用はしない） |
| `dexc_inspector_registered` | Inspectorに登録されたobject |
| `dexc_inspector_scenes`／`dexc_inspector_children` | sceneとHierarchy |
| `dexc_inspector_components`／`dexc_inspector_fields`／`dexc_inspector_read` | componentとfieldの値 |
| `dexc_display_frame` | 最後に完了したフレームのDisplay |
| `dexc_diagnostics_list`／`dexc_diagnostics_detail` | Dexcが記録したエラー |
| `dexc_boot_read`／`dexc_boot_start` | 開いているBootメニューの読み取りと開始 |

一覧系は`--path_prefix`、`--search`、`--offset`、`--count`で絞り込めます。Inspector系は一覧で返る`entity_id`で対象を指定します。`entity_id`はその実行の間だけ有効です。
