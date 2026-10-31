# Unity検証runner

日常の使い方は [テスト実行フロー](../../docs/tools/testing.md)、CLI の導入は [Unity CLI検証手順](../../docs/tools/unity-cli.md) を参照。ログ・XML・一時 fixture は `Logs/UnityTests` 以下へ出力し、Git へ含めない。`run-tests.sh` は Editor 本体のログも `<Mode>-<timestamp>.editor.log` として同じ場所へ残す。

## 入口

| スクリプト | 用途 |
| --- | --- |
| `test.sh` | `quick`／`edit`／`full`／`player`／`remote`／`matrix`／`compile`／`slow` |
| `run-tests.sh` | `--mode`、`--filter`、`--timeout`、`--visual-capture`、`--graphics` を受ける下位 runner |
| `run-editmode-tests.sh`／`run-playmode-tests.sh` | `run-tests.sh` の mode 固定版 |
| `check-compilation.sh` | `-batchmode -quit` のコンパイル確認 |
| `run-remote-player.sh` | 開発用プレイヤーを起動し、Editor と同じ Unity CLI のクライアントで外部から操作する（`test.sh remote`） |
| `run-graphics-tests.ps1` | Windows で `run-tests.sh --graphics` が使う下位スクリプト。直接は呼ばない |
| `parse-unity-test-result.py`／`report-slow-tests.py` | NUnit XML の成否集計と遅いテストの集計 |

各 runner は対象 project の Editor が開いていれば `close-editor.sh` で終了を試みる（Play Mode 中は終了しない）。終了できなければ実行しない。`DEXC_CLOSE_EDITOR=0` で自動終了を止められる。

## 描画を伴う検証

```sh
sh tools/unity-tests/run-playmode-tests.sh --graphics --filter DexcFormControlTests
sh tools/unity-tests/run-visual-capture.sh
```

`--graphics` は `WaitForEndOfFrame` を使う実 control の PNG 取得用で、PlayMode と明示 filter が必須。`run-visual-capture.sh` は3renderer のメニューを PNG 化し、`verify-visual-capture.py` と renderer ごとの verifier で manifest を検査する。

## 配布前の検証

| スクリプト | 用途 |
| --- | --- |
| `run-symbol-matrix.sh [--boundary]` | `DEXC_DEBUGGER`／`DISABLE_DEXC_DEBUGGER` の組み合わせで compile と test を確認。開始時の define を保存し終了時に復元する |
| `run-stripping-player.sh` | IL2CPP High stripping の Player を build し、Reflection 定義の残存を確認 |
| `run-stealth-sample-validation.sh` | `Assets/Samples/StealthStageDemo` を Play Mode で動かして撮影し、manifest を確認 |
| `run-runtime-ui-asset-matrix.sh` | Skin／shader の Player 包含境界を確認 |
| `run-performance-acceptance.sh` | 性能受入テストだけを実行 |

Player build を伴う runner は起動直前の `ProjectSettings.asset` を出力 directory へ保存し、終了後に復元して比較する。

## runner 自体の回帰（Unity 不要）

```sh
python3 -B tools/unity-tests/test-runner-safety.py
python3 -B tools/unity-tests/test-png-decoder.py
```
