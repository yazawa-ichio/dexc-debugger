# テスト実行フロー

目的は「変更のたびに数分で回せる」こと。全件は commit 前の1回だけ回す。Unity CLI の導入と `.local/unity-tests.env` は [Unity CLI検証手順](unity-cli.md) を参照。

## コマンド

```sh
sh tools/unity-tests/test.sh quick DexcBootTests      # 変更箇所のPlayModeテストだけ（1〜2分）
sh tools/unity-tests/test.sh quick                    # PlayMode全件（約3分）
sh tools/unity-tests/test.sh edit DexcEditorIntegerTests   # EditModeを絞って実行
sh tools/unity-tests/test.sh full                     # EditMode＋PlayMode全件＋プレイヤービルド＋リモート確認と遅いテストの集計。commit前
sh tools/unity-tests/test.sh player                   # プレイヤービルドだけ（約6分）
sh tools/unity-tests/test.sh remote                   # 開発用プレイヤーをビルドし、Unity CLIから外部操作して確認
sh tools/unity-tests/test.sh matrix                   # DEXC_DEBUGGER / DISABLE_DEXC_DEBUGGER の組み合わせ全部（7〜8分）。マージ前
sh tools/unity-tests/test.sh compile                  # コンパイルだけ確認
sh tools/unity-tests/test.sh slow                     # 直近レポートから遅いテストを集計
```

filter はテストのフルネームに対する部分一致（正規表現可）。クラス名だけで十分に絞れる。

対象プロジェクトの Unity Editor が開いていて Play Mode でなければ、各ランナーは `close-editor.sh` で Editor を終了してから実行する（`DEXC_CLOSE_EDITOR=0` で無効化）。Play Mode 中は終了せず、実行を中止する。

## 速度の内訳と方針

| 要因 | 目安 | 対策 |
| --- | ---: | --- |
| Unity batch 起動（import＋compile） | 約60秒／回 | 1回の起動でまとめて実行する。`compile` を別に回さず `quick` に含める |
| EditMode の `EnterPlayMode`（domain reload あり） | 1テスト 5〜20秒 | Project Settings > Editor > Enter Play Mode Options で Domain Reload と Scene Reload を無効化済み。ライブラリは `SubsystemRegistration` で状態を初期化するので無効化に対応している |
| PlayMode の描画・入力系テスト | 322件で約160秒 | `quick` を filter 付きで回し、全件は `full` の時だけ |
| IMGUI 実イベント系（`DexcFormControlTests` など） | 環境依存 | 他の Unity インスタンスが起動中だと失敗する。落ちたら単独で再実行する |
| シンボルマトリクス | 7〜8分 | `full` には**入れない**。4構成それぞれでコンパイルとテストを回す。define は `ProjectSettings.asset` を直接書き換えて戻すので、途中でコンパイルが壊れても元に戻る |
| プレイヤービルド | 約6分 | `full` に含む。`UNITY_INCLUDE_TESTS` と Editor が無い側をコンパイルするのはこれだけで、Editor 上のテストでは片側しか見ていない。IL2CPP＋Stripping High でビルドし、出来たプレイヤーを起動して初期化まで確認する |
| Boot scene の見た目確認 | 任意 | `DEXC_BOOT_CAPTURE=1 sh tools/unity-tests/run-playmode-tests.sh --graphics --filter DexcBootSceneCaptureTests` で 3renderer × 複数解像度の PNG を `Logs/UnityTests/BootScene` に出す |

## テストを書くときの決まり

- 1テスト 2秒以内を目安にする。5秒を超えたら `test.sh slow` に出るので、待ちを減らすか分割する。
- EditMode テストで `EnterPlayMode` を使わない。Runtime の挙動は PlayMode テストに書く。EditorWindow が Play Mode 中の状態を要る場合だけ例外とし、1 fixture にまとめて往復回数を減らす。
- `yield return null` の連打や `WaitForSeconds` で同期を取らない。状態を直接確認する。
- 製品コードに `ForTests` 用の観測フィールドを足さない。`InternalsVisibleTo` があるので `internal` を直接読む。
- 値を持つ `static` 変数を足さない。実行中の状態は `DexcRoot`（UI側は `DexcUIRoot` の slot）へ置く。`DexcRootStateTests` が library assembly を反射して見張っているので、足すとそこで落ちる。
- PlayMode テストの TearDown で scene を unload するときは自分が load した scene だけにする。test runner は自分の scene に住んでいるので、それを unload すると run が完了せず timeout する。
- batch mode では OnGUI が走らないので IMGUI の provider 入力は `DexcRuntimeUIRenderer.ProcessProviderInputForTests()` で直接流す。`Time.realtimeSinceStartup` は Play Mode 開始時にほぼ 0 から始まる。

## 結果の見方

各実行は `Logs/UnityTests/<Mode>-<timestamp>.xml` と `.log`（CLI の出力）、`.editor.log`（Editor 本体のログ）を出す。report が無いまま timeout したときは `.editor.log` の末尾で、どのテストの途中で止まったかを見る。ランナーは JSON で件数と成否を出し、失敗があれば終了コード 1。失敗テストの詳細は次で取れる。

```sh
python tools/unity-tests/parse-unity-test-result.py Logs/UnityTests/PlayMode-<timestamp>.xml --json
```

## その他のランナー

| スクリプト | 用途 |
| --- | --- |
| `run-visual-capture.sh` | 3renderer のメニューを PNG 化し、manifest を検証 |
| `run-stripping-player.sh` | IL2CPP High stripping の Player を build して Reflection 定義の残存を確認 |
| `run-symbol-matrix.sh` | `DEXC_DEBUGGER`／`DISABLE_DEXC_DEBUGGER` の組み合わせで compile とテストを確認 |
| `run-stealth-sample-validation.sh` | `Assets/Samples/StealthStageDemo` を Play Mode で動かして撮影し、manifest を確認 |
| `run-runtime-ui-asset-matrix.sh` | Skin／shader の Player 包含境界を確認 |
| `run-performance-acceptance.sh` | 性能受入テストだけを実行 |

これらは配布前にだけ回す。
