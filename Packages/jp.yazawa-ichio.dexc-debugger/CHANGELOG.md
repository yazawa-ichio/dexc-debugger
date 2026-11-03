# Changelog

このpackageの変更を記録します。書式は[Keep a Changelog](https://keepachangelog.com/ja/1.1.0/)に、versionは[Semantic Versioning](https://semver.org/lang/ja/)に従います。

## [0.1.0-pre.1] - 2026-11-03

最初のプレリリースです。

### Added

- Command: projectのメソッドを登録し、引数を入力して実行する。非同期実行、確認、実行履歴に対応する
- Setting: 実行中に値を編集し、`DebugStorage`へ保存する
- Inspector: 実行中のobjectを階層からたどり、fieldを観測・編集する
- Display: 画面とWorld空間へデバッグ表示を描く
- Panel: projectが組み立てた画面を`DebugPanels`へ登録し、メニューから開く。`DebugPanels.Home`で横並び表示のトップにできる
- Boot: 起動時に設定を選ぶ起動メニュー
- Runtimeメニュー: IMGUI（既定）、uGUI（`DEXC_RUNTIME_UGUI`）、UI Toolkit（`DEXC_RUNTIME_UITOOLKIT`）の3つの描画方式。Skinで寸法・色・fontを変え、広い画面では区分・一覧・ページを横に並べる。gamepadなどの入力は`DebugMenuInputProvider`で接続する
- コマンドと設定の一覧のフォルダ表示、お気に入り、ホームの件数と最近実行したコマンド
- Editorの「Window > Dexc > Dexc Debugger」と起動ウィンドウ
- `com.unity.pipeline`を使ったUnity CLIからの遠隔操作（`Dexc.Remote.Pipeline`）
- `DEXC_DEBUGGER`で機能を有効にし、`DISABLE_DEXC_DEBUGGER`でassemblyごと除外する配布境界
