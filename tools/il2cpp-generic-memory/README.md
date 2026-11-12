# IL2CPP のジェネリックのメタデータの最適化: 索引

IL2CPP の Player は、ジェネリック型の具象型（`List<Foo>` など）を使うたびに、VM のメタデータ（クラス、メソッド、表）を積む。この作業は、それを「使う時まで作らない」「作る数を減らす」ことで減らす。このファイルは、関係するファイルがどこにあって何をするかの索引である。

- ブランチ: メモリの最適化は `il2cpp-generic-memory`、速度の最適化は `il2cpp-speed`（メモリのブランチの上に積む）。
- Unity 6000.3.20f1。動かして確かめたのは Windows x64 と、Android の実機（arm64、Pixel 3 XL）の IL2CPP の Player。iOS では動かしていない。
- 2026-11-11 までの計測の記録と、採らなかった手法の説明は [record.md](record.md)（数値と「ファイル」の表は古い）。Editor を使わないビルドの手順は [offline/README.md](offline/README.md)。

## どこに何があるか

| 場所 | 役割 | Player に入るか |
|---|---|---|
| `Assets/Il2CppLazyMetadata` | **製品。** libil2cpp を「使う時に作る」ように変えるパッチ | iOS のビルドで自動で当たる |
| `Assets/UniTaskStateMachinePool` | **製品。** UniTask の async のステートマシンを寄せる ILPostProcessor | UniTask を使うアセンブリのコンパイルで走る |
| `Assets/Tests/GenericMemory/Editor` | 検証。計測用 Player のビルドと、IL を書き換える段階 | 段階を指定したビルドでだけ走る |
| `Assets/Tests/GenericMemory/Scenario` | 検証。計測用のアセンブリ（パターン、検証、ベンチ） | `GENERIC_MEMORY_SCENARIO` を定義したビルドだけ |
| `tools/il2cpp-generic-memory` | 道具。ビルド、実行、集計のスクリプト | 入らない |
| `tools/il2cpp-generic-memory/.local`、`results` | 作業用。il2cpp フォルダの複製、保存した入力、計測の生の結果 | Git に入れない |

## 目的別の入口

| 知りたいこと | 読むもの |
|---|---|
| パッチが libil2cpp の何を変えるか | `Native~/Il2CppLazyMetadata.h` と `Native~/Il2CppLazyInterfaces.h` の冒頭のコメント。次に `Il2CppLazyMetadata.cs` の `Apply` |
| パッチがどのソースのどの行に入るか | `Il2CppLazyMetadata.cs` の `Apply`（下の「libil2cpp への差し込み」の表） |
| IL の書き換えが何をするか | `GenericMemoryWeaver.cs` の `Run`（段階の振り分け）。次に各段階のクラスの冒頭のコメント |
| 数値を測り直す | このファイルの「測り方」、`offline/README.md`、`m-run.py` |
| 動きが変わっていないかを確かめる | `Scenario` の `*Checks.cs` と `m-run.py checks` |
| Unity の版を上げた時 | `Il2CppLazyMetadataCheck.Run(null)` を Editor で実行する（パッチがその版の libil2cpp に当たるか） |

## 製品のコード

### `Assets/Il2CppLazyMetadata/Editor`

| ファイル | 中身 |
|---|---|
| `Il2CppLazyMetadata.cs` | `OnPostprocessBuild` が iOS のビルドの後に Xcode プロジェクトの中の libil2cpp を書き換える。本体は `Apply(root, switched)`。差し込みは「元の文面 → 新しい文面、何回あるはずか」の並び（`Swap`／`Before`／`After`／`Every`）で、`Edit` が回数を確かめ、合わなければビルドを止める。2 度当てても変わらない |
| `Native~/Il2CppLazyMetadata.h` | libil2cpp/vm へ複製するヘッダー。ジェネリックの具象型のメソッド・プロパティ・イベントを使う時に作る処理と、RGCTX の項目を読まれた時に解決する処理 |
| `Native~/Il2CppLazyInterfaces.h` | 同じく複製するヘッダー。インターフェースの vtable スロット、インターフェースのクラス、配列のメソッドの表を使う時に作る処理。表の項目の下位 2 ビットを印に使う（冒頭のコメントに表がある）。表を読む側は全部 `il2cpp::vm::LazyInterfaces` を通る |

- `switched` が偽（製品）なら常に遅延する。真なら実行時の環境変数で切り替える形になり、計測用のカウンタ（`libil2cpp/GenericMemoryCounters.h`）が要る。
- ビルド時に環境変数 `IL2CPP_LAZY_METADATA=0` を置くと、パッチを当てない。
- `Native~` は Unity が読み込まないフォルダで、ヘッダーは C# がファイルとして読んで複製する。

### libil2cpp への差し込み

| libil2cpp のファイル | 差し込むもの |
|---|---|
| `vm/Class.cpp` | 初期化でメンバーを作らない／名前でのメソッドの検索／プロパティとイベントを 1 回だけ作る／インターフェースの一覧と対の表に印つきの項目を置く／`SetupVTable` を 1 つのインターフェースのスロットだけに実行する／キャストの判定 |
| `vm/ClassInlines.cpp` | 生成コードがインライン展開する検索に外れた時の、遅い経路 |
| `vm/Object.cpp`、`codegen/il2cpp-codegen.h` | スロットを番号で読む所（空のスロットを埋めてから読む）。`il2cpp-codegen.h` は RGCTX を読む 4 か所も |
| `metadata/ArrayMetadata.cpp` | 配列が暗黙に実装するジェネリックインターフェースと、配列のメソッドの表 |
| `metadata/GenericMetadata.h`、`.cpp` | RGCTX を 1 項目ずつ解決する |
| `vm/GenericClass.cpp` | 後から作った表を、出来上がってから見せる（メモリバリア） |
| `metadata/CustomAttributeDataReader.cpp` | 属性の名前つき引数がプロパティの表を読む前に作る |
| `vm/RCW.cpp` | COM オブジェクトの表は元のままにする |

### `Assets/UniTaskStateMachinePool/CodeGen`

| ファイル | 中身 |
|---|---|
| `StateMachinePoolPostProcessor.cs` | ILPostProcessor の入口。UniTask を参照するアセンブリが対象 |
| `StateMachinePoolRewriter.cs` | async のステートマシンを、型ごとにプールした box に載せ、ランナーを結果の型ごとに 1 つにする書き換え。`UNITASK_STATE_MACHINE_POOL_OFF` を定義したビルドは書き換えない |

## 検証のコード

### `Assets/Tests/GenericMemory/Editor`

| ファイル | 中身 |
|---|---|
| `GenericMemoryBuild.cs` | 開いている Editor から Windows の計測用 Player をビルドする入口。`UNITY_IL2CPP_PATH` で il2cpp フォルダの複製を使わせる |
| `GenericMemoryAndroidBuild.cs` | 同じことを Android（arm64）の Player でする入口。アクティブなビルドターゲットが Android の時だけ動き、アプリ ID など変えた設定を戻す。`UNITY_IL2CPP_PATH` は Android のビルドでも効く |
| `GenericMemoryPreLink.cs` | スクリプトのコンパイルの後、UnityLinker の前に全 DLL を書き換える所。環境変数 `GENERIC_MEMORY_VARIANT` が段階（`+` でつなぐ）、`GENERIC_MEMORY_SAVE_MANAGED` が書き換え前の DLL の保存先 |
| `GenericMemoryWeaver.cs` | 書き換えの土台。`Run(folder, steps)` が段階を振り分ける。`WholeProgram` はビルドの全 DLL を 1 つのプログラムとして読み書きする。`Hoister` は段階 `hoist` |
| `Canonicalizer.cs` | 「このメソッドは型引数を実行時に見ない」の解析と、段階 `canon`／`comparers`／`free` |
| `CanonicalizerLocals.cs` | 段階 `enum` |
| `LinqSharer.cs` | 段階 `linq` |
| `AsyncEraser.cs` | 段階 `async`／`unitask` |
| `Verifier.cs` | 書き換えた本体を書き出す前に確かめる（名指ししたメンバーが見つかること、スタックの深さ） |
| `Il2CppLazyMetadataCheck.cs` | ビルドせずに、パッチがこの Editor の libil2cpp に両方の形で当たり、2 度当てても変わらないことを確かめる |

IL の段階:

| 段階 | 何をするか | 型の同一性 |
|---|---|---|
| `comparers` | 型を見る所が既定の比較子だけのメソッド（`List<T>.Contains` など）を `object` の文脈で呼ぶ。`canon` を含む | 変えない |
| `enum` | foreach の列挙子など、メソッドの中でしか見えない構造体のローカルを `List<object>.Enumerator` にする | 変えない |
| `linq` | `List` と配列から始まる LINQ の連鎖を、内部だけ `object` で動かす | 利用者から見える値は変えない |
| `canon` | 型引数を見ないジェネリックメソッドを `object` で呼ぶ | 変えない |
| `free` | 型引数を見ないメソッド（`Count`、インデクサなど）を `List<object>` の文脈で呼ぶ。`list[i]` が遅くなるので採っていない | 変えない |
| `hoist`、`hoistall` | ジェネリッククラスのメソッドを型の外の静的メソッドへ移す。VM の遅延化があれば上積みがない | 移したメソッドがリフレクションで元の型から見えなくなる |
| `async`、`unitask` | UniTask のステートマシンを寄せる（製品の ILPostProcessor の元になった試作） | — |

VM の遅延化に重ねる採用案は `comparers+enum+linq`。`enum` は `hoist` より前に置く。

### `Assets/Tests/GenericMemory/Scenario`

| ファイル | 中身 |
|---|---|
| `Runner.cs` | Player の入口。引数でモードを選び、結果を 1 つのファイルに書いて終了する（下の表） |
| `Generated/` | 計測パターン（`ListUse`、`DictUse` など）。`generate.py` が書く。Git に入れない |
| `AsyncChecks.cs`（66 件） | async と UniTask の動き |
| `ReflectionChecks.cs`（11 件） | リフレクションで見えるものと、複数スレッドが同時に型を初めて使う競合 |
| `InterfaceChecks.cs`（90 件） | インターフェース呼び出し、キャスト、変性、配列 |
| `ComparerChecks.cs`（36 件） | 既定の比較子を寄せた時の動き |
| `SharingChecks.cs`（58 件） | `enum`／`linq` が型の同一性を変えていないこと |
| `Bench.cs`、`InterfaceBench.cs`、`SpeedBench.cs` | 速度のカーネル |
| `KeepAll.cs`、`Repository.cs`、`Slim.cs`、`Support.cs`、`ReflectionBehaviour.cs` | 広い API を通す確認、ベンチと検証が使う型 |

| Runner の引数 | すること |
|---|---|
| `-generic-memory-pattern <名前> -generic-memory-n <数>` | パターンの型を `数` 個使い、増えたメタデータを書く。`-generic-memory-names` を足すと、作られたクラスとメソッドの名前も書く |
| `-generic-memory-checks`、`-generic-memory-reflection`、`-generic-memory-interfaces`、`-generic-memory-comparers`、`-generic-memory-sharing` | 上の検証を実行する（`checks` は `AsyncChecks`） |
| `-generic-memory-bench`、`-generic-memory-interface-bench`、`-generic-memory-speed-bench` | 速度のカーネルを測る |
| `-generic-memory-race <深さ>` | リフレクションの検証のうち、競合の検査だけを回す。型の木を `深さ` 段たどる（深さ 2 なら、1 回の実行で 117 回の競合）。失敗した時は、どの型のどの一覧が何個違ったかを書く |
| `-generic-memory-propname <スレッド数>` | 読み込まれている全部の型のプロパティの名前を、その数のスレッドで同時に読み、`null` で返った数を書く（クラスライブラリのキャッシュの競合を見る） |
| `-generic-memory-output <ファイル>` | 結果の書き出し先（必須） |

Android では、引数はインテントの `unity` の追加情報（`am start -e unity "<引数>"`）か、アプリのフォルダの `generic-memory-args.txt` から読む。フルパスでない書き出し先は、アプリのフォルダ（`Application.persistentDataPath`）の下になる。

## 道具（`tools/il2cpp-generic-memory`）

| ファイル | 中身 |
|---|---|
| `generate.py` | 計測パターンのコードを書く |
| `offline/` | Editor を使わないビルド。`build.py`（保存した入力から Player を作る）、`save-inputs.py`、`editor-build.sh`（Editor を順番に使うためのロックつきのビルド）、`compare.sh`（2 つの Player が同じか）、`clone-il2cpp.sh` |
| `m-run.py` | 計測（`measure`）、検証（`checks`）、繰り返し（`repeat`）。4 桁の組に対応し、Player のプロセスの終了を待たない |
| `measure.sh`、`checks.sh` | 同じことをする元のスクリプト。組は 3 桁で、プロセスの終了を待つ |
| `report.py`、`summary.py` | 結果のフォルダから、型 1 つあたりの量と、条件を並べた表を出す |
| `kit/` | `same.py`（2 つの結果が同じ数か）、`names.py`／`breakdown.py`（何が作られたかの内訳）、`population.py`（実際の Player にある具象型の母集団）、`make-copy.sh`（Editor の libil2cpp から複製を作る）、`repeat.sh`、`names-run.sh`、`check-syntax.bat` |
| `libil2cpp/` | 計測用のカウンタ（`GenericMemoryCounters.h`、`apply-counters.py`）。`apply-lazy-interfaces.py` は製品に入る前の試作で、今の形には使わない |
| `Census/`、`rows.py`、`sharing.py` | ビルド済みの DLL と il2cpp の出力から、型やメソッドを数える |
| `WeaverHost/` | IL の段階を Unity の外で実行する。`Assets` の同じソースをコンパイルする |
| `c-bench.py`、`c-bench.sh`、`c-stats.py`、`c-final.sh`、`m-bench.py` | 速度の計測と集計。条件を交互に実行して、最良値、中央値、幅を出す |
| `c-build.py`、`c-relink.py`、`c-measure.py` | 速度の検証用。il2cpp とコンパイラの引数を変えたビルド、生成された C++ の一部だけを差し替えた Player |
| `a-run.sh`、`b-names-run.sh`、`bench.sh`、`bench.py` | 4 桁の組での実行、名前つきの一覧、以前の速度の計測 |
| `d-android-switch.sh`、`d-android-build.sh`、`d-android-keep.py` | Android の実機確認。ビルドターゲットを Android にする、Editor のロックの中で Android の Player をビルドする、そのビルドがどの libil2cpp をコンパイルしたかを確かめてシンボルつきのライブラリを残す |
| `d-android-run.py` | 接続した Android の端末に入れて実行する（`install`、`checks`、`repeat`、`measure`）。計測用のアプリのパッケージだけを扱う。`D_ANDROID_LOAD` で端末に負荷をかけながら回せる |
| `d-win-run.py` | 同じ検証を、比較用の Windows の Player で回す |

`a-`、`b-`、`c-`、`m-` の接頭辞は、検証を並行で進めた時の持ち主の印で、役割の区別ではない。

## 測り方の決まり

- **基準**: パッチを当てていない動き。計測用の libil2cpp でスイッチを全部切った時と同じ。
- **単位**: 型 1 つあたりの、メタデータのプールに要求されたバイト数。パターンを 2 つの数で実行し、その差から出す。
- **組**: スイッチの桁の並び。1 桁目がメソッド、2 桁目が RGCTX、3 桁目がインターフェースのスロット（配列のメソッドの表を含む）、4 桁目がインターフェースのクラス。`1111` が全部オン、`110` が 2026-11-11 までの形。
- **ラベル**: `Logs/UnityTests/GenericMemory/<ラベル>` にある Player。計測の結果は `results/<名前>`、検証の結果は `results/checks`。
- 計測用の Player は、結果を書いてからプロセスが消えるまで 20〜55 秒かかる。`m-run.py` と `c-bench.py` はそれを待たない。
- 速度は、ほかのビルドや計測が動いていない時に測る。プールのバイト数は決定的なので、並行でもよい。

## 結果の要約（2026-11-12、Windows x64）

| | 60% 以上減ったパターン | `List` を使うパターン |
|---|---|---|
| 基準 | — | 34.9 KB |
| VM の遅延化（製品のパッチ） | 16 のうち 9 | 10.5 KB |
| VM の遅延化＋IL の `comparers+enum+linq` | 16 のうち 13 | 4.8 KB |
| IL だけ | 0 | 28.8 KB |

- 届かないのは、Task、UniTask、デリゲートのパターン。
- 代償: 共有コードが RGCTX を読むたびに検査が 1 つ入る。ジェネリックなインターフェースへのキャストと、変性つきのインターフェース呼び出しが遅くなる。インターフェースを初めて呼ぶたびに対の表を複製する。`linq` は LINQ の 1 回あたりの GC のバイト数を 1〜7% 増やす。
- `Type.GetInterfaces()` の並び順が変わる（中身は同じ）。

### Android の実機（2026-11-12、Pixel 3 XL、arm64、Android 12）

- 製品と同じ形（常時オン）のパッチ入り libil2cpp は、Unity の Android ビルド（NDK の clang）でコンパイル・リンクできる。
- 検証 5 種が、常時オンと、常時オン＋IL の両方で通る。型あたりのメタデータは、16 パターンとも Windows と同じ値だった。
- 競合の検査（複数スレッドが同時に型を初めて使いながらリフレクションで走査する）の失敗 / 競合の回数:

| ランタイム | arm64 | 備考 |
|---|---:|---|
| パッチなし | 2 / 6,839 | 素の libil2cpp にある競合。内訳が取れた 1 回は、`List<T>` のインターフェースが 8 個中 1 個しか列挙されなかった。Windows でも PC に負荷をかけると 300 回の実行で 39 回失敗し、1 回クラッシュした |
| 常時オン | 0 / 14,447 | |
| 常時オン＋IL | 5 / 200,673 | 5 回とも、プロパティの名前が一瞬 `null` で返ったもの（下の説明）。ランタイムの競合ではなかった。読み直して比べるようにしてからは 0 / 37,440 |

- 常時オン＋IL の失敗は、クラスライブラリ（Mono の BCL）の `RuntimePropertyInfo` が原因だった。名前や getter をランタイムから取った結果を自分の中に持ち、取得済みの印を別のフィールドに立てるが、その間にロックもバリアもない。書いた順を保たない arm64 では、別のスレッドが印を先に見て、まだ `null` の名前を読むことがある。個数は同じで名前だけが違う一覧になり、失敗はどれもプロパティの一番多い `Dictionary<T,int>` で出ていた。x64 では起きない（同じ形の Player で、負荷をかけた 21 万回の競合で 0 回）。libil2cpp にもこのパッチにも関係がなく、パッチなしでも同じコードである。
- 検査は、名前が `null` なら読み直した値で比べ、その回数を結果の `cacheRaces` に書く。読み直すようにした版は、37,440 回の競合で失敗 0 回、`null` を見たのは 2 回だった。パッチなしのランタイムでも、58,383 回の競合で `null` を 1 回見た（失敗は 0 回）。`-generic-memory-propname` は、この競合だけを、ジェネリックも型の初回利用もなしで回す。
- 途中で「ジェネリックの具象型のフィールドの表が、バリアなしで公開されている」ことを原因と見てバリアを 2 か所足したが、足した版でも同じ頻度で失敗したので取り消した。
- 製品のパッチ（`Il2CppLazyMetadata.cs`）が自動で当たるのは iOS だけである。Android では、パッチを当てた il2cpp フォルダの複製を `UNITY_IL2CPP_PATH` で使わせて確かめた。
