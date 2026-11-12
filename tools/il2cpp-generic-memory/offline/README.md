# Editor を使わないビルド

Editor で 1 回ビルドした時の入力を保存しておき、そこから UnityLinker、il2cpp、C++ コンパイラを直接動かして、計測用の Player を作る。Editor は 1 つしかないので、libil2cpp だけ、または DLL の書き換えだけを変える検証を並行に回すためのもの。Unity 6000.3.20f1、Windows x64、MSVC 14.51 で確かめた。

コマンドは `tools/il2cpp-generic-memory` で実行する。

## ビルドする

保存済みの入力は `.local/inputs/base`（`il2cpp-v2` で Editor がビルドした `off-ref` のもの）。計測用のアセンブリ（`Assets/Tests/GenericMemory/Scenario`）を変えていなければ、これをそのまま使える。

```sh
# 入力からそのまま作る（UnityLinker → il2cpp → C++ → Player）
python offline/build.py --inputs base --il2cpp il2cpp-v2 --work v2 --label off-v2

# libil2cpp だけを変えた: C++ の生成を飛ばし、Editor のビルドが生成した C++ をコンパイルする
python offline/build.py --inputs base --il2cpp il2cpp-v2a --work v2a --label off-v2a --cpp-from inputs

# DLL を書き換える（WeaverHost は先に dotnet build -c Release しておく）
python offline/build.py --inputs base --il2cpp il2cpp-v2 --work hoist --label off-hoist --weave hoist

# コンパイル時の定義と、il2cpp の C++ 生成のオプションを足す
python offline/build.py --inputs base --il2cpp il2cpp-mine --work mine --label mine \
    --define IL2CPP_USE_SPARSEHASH=1 --il2cpp-arg=--emit-comments
```

- `--il2cpp` は `.local` の下のフォルダ名かパス。その `libil2cpp` がコンパイルされる。新しい複製は `kit/make-copy.sh`（Editor のものから）か `offline/clone-il2cpp.sh <元> <新しい名前>`（既にある複製から、パッチとカウンタごと）で作る。
- `--work` は `.local/work` の下のフォルダ名かパス。中間ファイルとキャッシュは全部ここに入る（1 つ約 2.7 GB）。**作業フォルダが別なら同時に走らせてよい。** 同じ作業フォルダは 1 度に 1 つ（`build.lock` で止める。強制終了で残ったら消す）。**名前は 17 文字まで**にする（理由は「仕組み」の最後）。
- `--label` は出力先 `Logs/UnityTests/GenericMemory/<ラベル>`。新しいラベルか、前に `build.py` が作ったラベル（`offline-build.json` がある）だけに書く。Editor がビルドしたラベルには書かない。前と違う入力から同じラベルに作る時は、ひな形の Player を複製し直す。`measure.sh` と `checks.sh` はそのまま使える。
- `--cpp-from` は `inputs`（保存した Editor のビルドの C++）か、別の作業フォルダ。参照するだけで複製しない。
- `--define` は libil2cpp と生成された C++（プリコンパイル済みヘッダーを含む）に付く。bdwgc と zlib には付かない。何度でも書ける。
- `--weaver <WeaverHost.exe>` で、`WeaverHost/bin/Release` 以外の場所にある WeaverHost を使う。
- `--stop-after managed|linker|convert|compile` で途中まで。`--jobs` は同時に動かすコンパイラの数（既定はコア数）。
- 同じ作業フォルダでもう一度実行すると、段階ごとに、読むものが前回と同じなら飛ばす。C++ は、コマンド行と、コンパイラが実際に読んだファイル（`/sourceDependencies` の一覧）の内容が前回と同じ翻訳単位を飛ばす。libil2cpp の `.cpp` を 1 つ変えればその lump とリンクだけ、ヘッダーを変えればそれを読む全部がコンパイルし直しになる。
- 生成された C++ と PDB の場所、かかった時間は `<ラベル>/offline-build.json` に書く。

2 つの Player が同じかは `sh offline/compare.sh <ラベル> <ラベル>` で確かめられる（`checks.sh` の 3 種、リフレクションの出力の全行、`measure.sh` と `kit/same.py`。最後の行が `same:` なら一致）。計測結果は `results/compare-<ラベル>-<組>` に置き、GameAssembly.dll かパターンが変わった時だけ測り直す。

## 入力を保存する（Editor が要るのはここだけ）

計測用のアセンブリの C# を変えたら、Editor でビルドして入力を保存し直す。Editor は 1 つなので、`editor-build.sh` が順番を取る。

```sh
# <il2cpp フォルダ> <新しいラベル> [保存する入力の名前]
sh offline/editor-build.sh il2cpp-v2 my-ref my-inputs
python offline/build.py --inputs my-inputs --il2cpp il2cpp-v2 --work my-work --label my-offline
```

- ロックはフォルダ `.local/editor-build.lock`（`mkdir` で取る）。中の `info.txt` に、取った時刻、用途、プロセス ID がある。取れない間は 30 秒ごとに試す（`GENERIC_MEMORY_LOCK_WAIT` 秒で諦める。既定は 4 時間）。40 分より古いロックは、Editor が `eval` に応答する（＝ビルド中でない）ことを確かめてから外して取る。
- Editor は必ず `--project-path` で指定し、`Application.dataPath` がこのプロジェクトであることを確かめる。違えば何もせずに終わる（終了コード 2）。
- ビルドの前に `AssetDatabase.Refresh()` をして、コンパイルが終わるのを待つ。`EditorUtility.scriptCompilationFailed` が True なら、ビルドせずに終わる（終了コード 3）。
- ビルドの後、Editor に `UNITY_IL2CPP_PATH` が残っていないことを確かめる。残っていたら外して終了コード 5。
- 入力の名前を付けると、ビルドの間に `GenericMemoryPreLink.cs` がリンク前の全 DLL を `.local/inputs/<名前>/Managed` に複製し（環境変数 `GENERIC_MEMORY_SAVE_MANAGED`。書き換えの前）、ビルドの後に `save-inputs.py` が残りを保存する。次の Editor のビルドが `Library/Bee` を上書きするので、保存はロックの中で行う。
- 既にあるラベルと、既にある入力の名前には書かない。加工（`GENERIC_MEMORY_BUILD_VARIANT`）つきのビルドもできるが、入力の保存は `baseline` の時だけ。

保存されるもの（`.local/inputs/<名前>`、約 0.9 GB）:

| | 中身 |
|---|---|
| `Managed/` | リンク前の全 DLL（`Temp/StagingArea/Data/Managed`、347 個と pdb） |
| `bee/dag.json`、`bee/dag.payloads` | そのビルドの `Library/Bee/Player*.dag.json`。UnityLinker、il2cpp、cl、lib、link のコマンド行と引数ファイル、lump の中身が入っている |
| `rsp/` | UnityLinker と il2cpp の引数ファイルを読める形で取り出したもの（`build.py` は `bee/` から読む） |
| `project/` | UnityLinker の引数ファイルが指すプロジェクト内のファイル（`UnityLinkerInputs` の 4 つと link.xml 2 つ） |
| `player/` | ビルドされた Player（バックアップのフォルダ以外）。GameAssembly.dll とメタデータを差し替える先のひな形 |
| `il2cppOutput/cpp`、`il2cppOutput/data` | Editor のビルドが生成した C++ とメタデータ（`--cpp-from inputs` が使う） |
| `ManagedStripped/` | Editor のビルドで UnityLinker が出した DLL（比べる用） |
| `libil2cpp-sources.txt`、`info.json` | そのビルドの libil2cpp にあった `.cpp`／`.c` の一覧、出どころ |

## 仕組み

- UnityLinker と il2cpp は、保存した引数ファイルのパスを作業フォルダ向けに書き換えて、`--il2cpp` のフォルダの exe（`UnityLinker/bin/Release/net`、`il2cpp/bin/Release/net`。Editor のビルドが使ったのと同じ場所）で動かす。
- C++ は、保存した dag の cl、lib、link のコマンド行をそのまま使う（パスだけ書き換える）。最適化や定義が Editor のビルドと同じになる。生成された C++ はフォルダにあるものを全部コンパイルするので、DLL の書き換えでファイルが増減してもよい。libil2cpp に足した `.cpp`／`.c` は、lump に入れずに 1 つずつコンパイルして `il2cpp.lib` に入れる。
- 生成された C++ をリンカーに渡す順は、Editor のビルドと同じ規則にしている（`.cpp`、次に `.c`。それぞれ名前を大文字にした時の文字コードの順）。ファイルが増減しても、Editor が同じファイルをビルドした時と同じ順になる。
- 書き換えた後のコマンドにプロジェクトの `Library` や `Temp` のパスが残っていたら、実行せずに止まる。カレントフォルダは作業フォルダ。`Library/Bee` には書かない。
- どの libil2cpp が使われたかは、コンパイラが読んだファイルの一覧から毎回確かめ、`--il2cpp` のフォルダ以外の libil2cpp が混ざっていたら失敗させる。
- UnityLinker は、出力した DLL に pdb のフルパスを書き込む。そのパスの長さが変わると DLL の大きさが変わり、il2cpp が一部のアセンブリ（`UnityEngine.AudioModule` など）の型に振る番号の順も変わった（生成された C++ のうち型の表の 5 ファイルと `global-metadata.dat` が変わる。ほかの 486 ファイルは同じ。なぜ順が変わるかは調べていない）。そこで UnityLinker の出力フォルダの名前の長さを調整して、パスの長さを Editor のビルドと同じにしている。`.local/work` の下の作業フォルダなら、名前が 17 文字までの時に調整でき、生成された C++ は Editor のビルドと全ファイル一致する（17 はこのリポジトリの置き場所の深さから決まる数。作業フォルダのパスが、プロジェクトの `Library/Bee/artifacts/WinPlayerBuildProgram/ManagedStripped` のパスより 2 文字以上短ければよい）。それより長いと調整できず、その旨を出力して続ける。その場合も動きと計測値は変わらなかった（`off-hoist-b`）。

## 確かめたこと

基準は、`il2cpp-v2` で Editor がビルドし直した `off-ref`（入力は `base`）と、同じく Editor で `hoist` をビルドした `off-ref-hoist`。「一致」は `offline/compare.sh` の結果で、`checks.sh` の 3 種が `000` と `110` の両方で全部通り（66／11／90 件）、リフレクションの出力が両方の組で全行同じ、`measure.sh`（7 パターン、遅延化なしとあり）が `kit/same.py` で 14 回 × 2 組とも差なし、の全部を指す。

| 確かめたこと | 結果 |
|---|---|
| 入力 `base` から作った `off-final` と、Editor の `off-ref` | 生成された C++ 491 ファイルと `global-metadata.dat` がバイト単位で同じ。GameAssembly.dll は同じ大きさ（51,124,736 バイト。中身は比べていない）。一致（リフレクションは 3,545 行） |
| 同じ生成済み C++ に `il2cpp-v2a` を組み合わせた `off-v2a`（`--cpp-from inputs`） | Editor がビルドした `v2a` の計測（`results/v2a-on`）と 14 回とも差なし（`results/off-v2a-on`） |
| WeaverHost の `hoist` を挟んだ `off-hoist2` と、Editor の `off-ref-hoist` | 生成された C++ 455 ファイル（基準より 52 減って 16 増える）と `global-metadata.dat` がバイト単位で同じ。GameAssembly.dll は同じ大きさ（44,395,008 バイト）。一致（リフレクションは 3,208 行） |
| 2 つを同時に（`il2cpp-v2a` の複製で `--cpp-from inputs` の `off-t1`、`hoist` を最初から作る `off-hoist-b`） | `off-t1` は `results/v2a-on` と差なし。`off-hoist-b` は 1 つずつ作った `off-hoist` と一致 |
| `Library/Bee` と既存のラベルに書いていないか | `off-v2`、`off-v2a`、`off-hoist`、`off-t1`、`off-hoist-b` のビルドと `off-t1` の作り直しの前後で、`Library/Bee` の 8,219 ファイルの時刻と大きさが変わっていない。既存のラベルのフォルダも変わっていない。その後のビルドでは比べていない（間に Editor のビルドを挟んだ） |
| 作り直しの判定 | 何も変えずにもう一度: コンパイル 0 個。libil2cpp の `.cpp` を 1 つ変える: 1 個。6 個が読むヘッダーを変えて `.cpp` を 1 つ足す: 7 個。プリコンパイル済みヘッダーに入るヘッダーを変える: 412 個。その後の Player も `results/v2a-on` と差なしで、3 種の検証が通る。足した `.cpp` と、単独でコンパイルされる `.cpp` 1 つを取り除くと、どちらのオブジェクトも `il2cpp.lib` から外れる |
| `--define` | 付けた定義が、libil2cpp の 112 個、生成された C++ の 490 個、プリコンパイル済みヘッダーの 2 個のコマンドに入り、bdwgc と zlib の 16 個には入らない。値を使う定義で動きが変わることは確かめていない |
| `--il2cpp-arg` | `--emit-comments` を渡して、生成された C++ にコメントが入ることを見た（C++ の生成まで。コンパイルはしていない） |
| `--weaver` | 別の場所に複製した WeaverHost で書き換え、既定のものと同じ DLL になることを見た（書き換えまで） |
| `editor-build.sh` | 50 分前の時刻を書いたロックを外して取り、ビルドして入力（`base2`）を保存し、ロックを外す。保存した `base2` は `base` と同じ中身。取られているロックは待ち、時間切れなら終了コード 1。先のビルドが終わると次の呼び出しがロックを取ってビルドする。`hoist` つきのビルド（`off-ref-hoist`）もこれで行った。コンパイルエラーがある時、別の Editor に届いた時、ビルドが失敗した時、`UNITY_IL2CPP_PATH` が残った時の中止は、実際には起こしていない |

- 同じ入力からでも、UnityLinker が出す DLL は実行のたびにバイト単位では変わる（MVID）。そこから生成される C++ は変わらない。
- 最初に作った `off-v2` も `off-ref` と一致した（出力フォルダの長さ合わせとリンクの順を入れる前の `build.py`。生成された C++ はこの時も全ファイル同じだった）。

## 所要時間

論理 8 コア。比較のための Editor のビルドは、C++ を全部コンパイルし直した回が 435 秒（`off-ref`）、`hoist` が 374 秒（`off-ref-hoist`）、何も変わっていない回が 42 秒（`off-ref2`）だった。

| 何をしたか | 全体 | 内訳 |
|---|---:|---|
| 最初のビルド（新しい作業フォルダ、入力から全部） | 359 秒、376 秒（2 回） | UnityLinker 16〜18、il2cpp 12〜14、C++ 620 個とリンク 331〜343 |
| libil2cpp だけを替えた、新しい作業フォルダ（`--cpp-from inputs`） | 326 秒 | C++ 620 個とリンク。il2cpp フォルダが変わるとコマンド行が変わるので全部コンパイルする |
| 同じ il2cpp フォルダの `.cpp` を 1 つ書き換えた | 15 秒 | lump 1 個、lib、link |
| 同じ il2cpp フォルダの、プリコンパイル済みヘッダーに入るヘッダーを書き換えた | 277 秒 | 412 個とリンク |
| 何も変えずにもう一度 | 2 秒 | |
| DLL を書き換えた（`hoist`）。基準をビルド済みの作業フォルダで | 290 秒 | WeaverHost 33、UnityLinker 16、il2cpp 12、C++ 584 個のうち中身が変わった 372 個とリンク 229 |
| 2 つ同時（別々の作業フォルダ、どちらも全部コンパイル） | 447 秒と 607 秒 | 後者は `hoist` を最初から。重なっている間は WeaverHost が 191 秒、UnityLinker が 40 秒、il2cpp が 36 秒かかった |

libil2cpp を何度も書き換える時は、複製を 1 つ決めてその中で書き換え、同じ作業フォルダでビルドするのが速い。

## できないこと、注意

- **C# を変えた時は Editor のビルドが要る。** スクリプトのコンパイルは Editor がする。ここで替えられるのは、libil2cpp、DLL の書き換え、il2cpp と C++ コンパイラへの指定だけ。
- **Player のデータは保存した Player のまま。** シーン、`globalgamemanagers`、`ScriptingAssemblies.json`、`RuntimeInitializeOnLoads.json`、`boot.config`、Burst のネイティブプラグイン（`lib_burst_generated.dll`）は作り直さない。アセンブリの数や `RuntimeInitializeOnLoadMethod` を変える書き換え、Burst がコンパイルするメソッドを変える書き換えには追従しない。
- **ビルドの設定は保存した入力のもの。** Windows x64、Release、Managed Stripping High、コード生成は速度優先。変えるなら Editor でビルドして保存し直す。Development Build とほかのプラットフォームは試していない。
- **`il2cpp.exe --compile-cpp` は試していない。** `--il2cpp-arg` で生成を変えた C++ のコンパイルと、動きを変える `--define`（`IL2CPP_USE_SPARSEHASH=1` など）も、通るかどうかは試していない。
- バックアップのフォルダ（`*_BackUpThisFolder_ButDontShipItWithYourGame`）は作らない。生成された C++ と PDB は作業フォルダにあり、場所は `offline-build.json` に書く。
- `--cpp-from <別の作業フォルダ>` の相手で C++ を生成し直すと、こちらもコンパイルし直しになる。相手のビルドと同時には走らせない。
- インストールされているコンパイラと Windows SDK のヘッダーは、時刻と大きさしか見ない。
- 作業フォルダの中の古いもの（取り除いたソースのオブジェクトなど）は消さない。リンクには入らない。
- `measure.sh <ラベル> <名前>` は `results/<名前>` を消してから書く。ほかの検証の結果の名前を使わない。
