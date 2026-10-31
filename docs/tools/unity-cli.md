# Unity CLI検証手順

## 目的

Unity CLIで本プロジェクトのコンパイル、EditModeテスト、PlayModeテストを再現可能に確認する。実行用ラッパーはPOSIX `sh`で提供するため、Windows（Git Bash等）、macOS、Linuxから同じ手順を使える。

## 確認環境

- Unity CLI: `1.0.0-beta.5`
- Unity Editor: `6000.3.20f1`（Unity 6.3.20f1）
- 確認済みOS: Windows
- 想定シェル: POSIX `sh`（WindowsではGit Bash等）
- 対象プロジェクト: `dexc-debugger`

## CLIの導入と確認

Unity CLIの導入方法はUnity CLIの配布手順に従う。PATHに配置できない場合は、以降のコマンドで`UNITY_CLI`に実行ファイルのパスを指定する。

```sh
unity --version
unity editors --installed --format json --no-banner
```

例（Windows Git Bash）：

```sh
export UNITY_CLI="/c/Users/<user>/AppData/Local/Unity/bin/unity.exe"
```

macOS／Linuxでは、通常は次のように指定する。

```sh
export UNITY_CLI=/path/to/unity
```

Python 3が`python3`または`python`として見つからない場合は、`PYTHON_BIN`で指定する。

```sh
export PYTHON_BIN=/path/to/python3
```

## ローカル設定

マシン固有の実行ファイルパスは、リポジトリルートの`.local/unity-tests.env`へ記述できる。`.local/`はGit管理対象外で、各ランナーの共通ローダーが起動時に読み込む。

```sh
UNITY_CLI="/Applications/Unity Hub.app/Contents/Hub/Editor/6000.3.20f1/Unity.app/Contents/MacOS/Unity"
PYTHON_BIN="/opt/homebrew/bin/python3"
DEXC_PROJECT_PATH="/Users/example/src/dexc-debugger"
```

設定ファイルの場所を変更する場合は`DEXC_UNITY_TEST_CONFIG`で指定する。

```sh
DEXC_UNITY_TEST_CONFIG=/Users/example/.config/dexc-unity.env \
  sh tools/unity-tests/run-editmode-tests.sh
```

コマンドラインで設定した環境変数はローカル設定より優先するため、一時的な切り替えも可能である。

## 検証用スクリプト

スクリプトは[tools/unity-tests](../../tools/unity-tests)にある。作業ツリー内で実行したログとNUnit XMLは`Logs/UnityTests`へ出力される。

| スクリプト | 用途 |
| --- | --- |
| `check-compilation.sh` | Unity Editorを`-batchmode -quit`で起動し、コンパイルエラーと終了コードを確認 |
| `run-editmode-tests.sh` | EditModeテストを実行し、NUnit XMLを解析 |
| `run-playmode-tests.sh` | PlayModeテストを実行し、NUnit XMLを解析 |
| `parse-unity-test-result.py` | NUnit XMLを読み、件数と成否をJSONで出力 |
| `test.sh` | 日常の入口。[テスト実行フロー](testing.md) |
| `report-slow-tests.py` | 遅いテストと fixture の集計 |

各スクリプトは対象プロジェクトのEditorが閉じている必要がある。Editorが開いていてPlay Modeでなければ、`close-editor.sh`でEditorを終了してから実行する（`DEXC_CLOSE_EDITOR=0`で無効化）。Play Mode中は終了せず、実行を中止する。`Temp/UnityLockfile`の存在だけではEditorの起動状態を判定しない。

## 標準手順

プロジェクトルートから実行する。

```sh
sh tools/unity-tests/check-compilation.sh
sh tools/unity-tests/run-editmode-tests.sh
sh tools/unity-tests/run-playmode-tests.sh
```

Unity CLIがPATHにない場合は、各コマンドの前に`UNITY_CLI`を指定する。

```sh
UNITY_CLI=/path/to/unity sh tools/unity-tests/run-editmode-tests.sh
```

特定プロジェクトを指定する場合は`--project`を使う。テスト名の絞り込みには`--filter`、上限時間には`--timeout`を使う。

```sh
sh tools/unity-tests/run-tests.sh \
  --project /path/to/UnityProject \
  --mode EditMode \
  --filter 'Dexc.*' \
  --timeout 600
```

## 結果と終了コード

テストランナーはUnity CLIの終了コードとNUnit解析結果の両方を確認する。次のすべてを満たした場合だけ終了コード`0`になる。

- Unity CLIが成功終了する
- レポートが存在する
- テスト件数が1件以上ある
- failed、skipped、inconclusiveが0件である

それ以外は終了コード`1`（引数や前提条件のエラーは`2`）で終了する。解析結果は次の形式で標準出力へ出る。

```json
{
  "success": true,
  "result": "Passed",
  "total": 2,
  "passed": 2,
  "failed": 0,
  "skipped": 0,
  "inconclusive": 0,
  "report": "Logs/UnityTests/EditMode-<timestamp>.xml"
}
```

コンパイル確認は次のJSONを出力する。

```json
{
  "success": true,
  "editorExitCode": 0,
  "compilerErrorCount": 0,
  "log": "Logs/UnityTests/Compile-<timestamp>.log",
  "editorVersion": "6000.3.20f1"
}
```

Editor終了は既定で終了コード`73`を要求する。CLIの戻り値は環境により正規化される可能性があるため、スクリプトは戻り値だけでなくUnityLockfileが消えたことも確認する。

## テストの置き場所

検証用テストは`Assets/Tests/Editor`と`Assets/Tests/Runtime`に配置する。これは設計検証用のTest assemblyであり、Runtime／Editor本体の実装ではない。
