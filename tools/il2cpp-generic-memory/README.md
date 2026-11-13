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
| `Native~/Il2CppCastCache.h` | **速度のための変更**（`il2cpp-speed` ブランチ）。インターフェースへのキャストの答えと、変性でしか実装していないインターフェースのスロットの場所を、固定の表 2 つ（各 64 KB）に覚える。読む側はロックを取らない |

- `switched` が偽（製品）なら常に遅延する。真なら実行時の環境変数で切り替える形になり、計測用のカウンタ（`libil2cpp/GenericMemoryCounters.h`）が要る。
- ビルド時に環境変数 `IL2CPP_LAZY_METADATA=0` を置くと、パッチを当てない。
- ビルド時に環境変数 `IL2CPP_CAST_CACHE=0` を置くと、キャッシュだけを外す。差し込みの行は残り、ヘッダーの先頭の定義でコンパイル時に消える。前に書き換えたフォルダをもう一度ビルドした時も、その時の指定に従う。
- `Native~` は Unity が読み込まないフォルダで、ヘッダーは C# がファイルとして読んで複製する。

### libil2cpp への差し込み

| libil2cpp のファイル | 差し込むもの |
|---|---|
| `vm/Class.cpp` | 初期化でメンバーを作らない／名前でのメソッドの検索／プロパティとイベントを 1 回だけ作る／インターフェースの一覧と対の表に印つきの項目を置く／`SetupVTable` を 1 つのインターフェースのスロットだけに実行する／キャストの判定／（キャッシュ）インターフェースへのキャストの探索を関数に分け、答えを覚える |
| `vm/ClassInlines.cpp` | 生成コードがインライン展開する検索に外れた時の、遅い経路／（キャッシュ）変性で見つけたスロットの場所を覚える |
| `vm/Object.cpp`、`codegen/il2cpp-codegen.h` | スロットを番号で読む所（空のスロットを埋めてから読む）。`il2cpp-codegen.h` は RGCTX を読む 4 か所も／（キャッシュ）`Object::IsInst` が、オブジェクトと同じクラスへのキャストを `Class::IsAssignableFrom` を呼ぶ前に答える |
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
| `NumericBench.cs`、`NumericKernels.cs`、`NumericKernelsUnchecked.cs`、`NumericBurst.cs`、`NumericNative.cpp` | 数値と SIMD のベンチ。同じ処理を、ふつうの C#、検査なしの C#（`e-numeric-unchecked.py` が書く。手で直さない）、Burst、C++ のソースプラグイン、クラスライブラリで実行し、結果の一致を確かめてから測る。`NumericNative.cpp` は、C++ コンパイラに `GENERIC_MEMORY_NATIVE` の定義が渡った時だけ中身がある |
| `Il2CppSetOptionAttribute.cs` | 検査を外す属性の、このアセンブリの中だけの写し（il2cpp は名前で見つける） |
| `EngineBench.cs` | エンジンを呼ぶループのベンチ。呼び出しの形ごとの 1 回の時間と、自動の書き換えで得られる差（`before`／`after` の対。結果が同じことを確かめる） |
| `KeepAll.cs`、`Repository.cs`、`Slim.cs`、`Support.cs`、`ReflectionBehaviour.cs` | 広い API を通す確認、ベンチと検証が使う型 |

| Runner の引数 | すること |
|---|---|
| `-generic-memory-pattern <名前> -generic-memory-n <数>` | パターンの型を `数` 個使い、増えたメタデータを書く。`-generic-memory-names` を足すと、作られたクラスとメソッドの名前も書く |
| `-generic-memory-checks`、`-generic-memory-reflection`、`-generic-memory-interfaces`、`-generic-memory-comparers`、`-generic-memory-sharing` | 上の検証を実行する（`checks` は `AsyncChecks`） |
| `-generic-memory-bench`、`-generic-memory-interface-bench`、`-generic-memory-speed-bench` | 速度のカーネルを測る |
| `-generic-memory-numeric-bench`（`-generic-memory-numeric-pointers` で Burst を関数ポインタでも呼ぶ）、`-generic-memory-engine-bench` | 数値と SIMD のベンチ、エンジン呼び出しのベンチを測る |
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
| `e-android-build.sh`、`e-android-keep.py` | C++ コンパイラの指定を変えた Android のビルド（`GENERIC_MEMORY_BUILD_IL2CPP_ARGS="--compiler-flags=-O3"`）。ビルドの dag から、実際に渡った指定を書き出す |
| `e-android-run.py`、`e-stats.py` | 接続した端末で、条件をラウンドごとに交互に測る（`bench`）。毎回の温度とスロットリングも記録する。`e-stats.py` が条件を並べた表にする |
| `e-numeric-unchecked.py` | `NumericKernels.cs` から、検査なしの版（`NumericKernelsUnchecked.cs`）を書く |
| `speed/cast-cache-test.cpp`、`cast-cache-test.sh` | キャストのキャッシュの単体の負荷試験。ランタイムの外でヘッダーだけをコンパイルし、複数スレッドが同じ場所を奪い合う条件で、当たりの答えが全部正しいことを確かめる。この PC（MSVC）と、接続した Android の実機（NDK の clang、arm64）で動く |

`a-`、`b-`、`c-`、`d-`、`e-`、`m-` の接頭辞は、検証を並行で進めた時の持ち主の印で、役割の区別ではない。

## 測り方の決まり

- **基準**: パッチを当てていない動き。計測用の libil2cpp でスイッチを全部切った時と同じ。
- **単位**: 型 1 つあたりの、メタデータのプールに要求されたバイト数。パターンを 2 つの数で実行し、その差から出す。
- **組**: スイッチの桁の並び。1 桁目がメソッド、2 桁目が RGCTX、3 桁目がインターフェースのスロット（配列のメソッドの表を含む）、4 桁目がインターフェースのクラス、5 桁目がキャストのキャッシュ。`1111` がメモリの遅延化を全部オン、`11111` がキャッシュもオン、`110` が 2026-11-11 までの形。
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

## 速度の最適化（`il2cpp-speed` ブランチ）

| 手 | 状態 | 切り替え方 |
|---|---|---|
| キャストのキャッシュ（`Native~/Il2CppCastCache.h`） | 入っている | ビルド時の環境変数 `IL2CPP_CAST_CACHE=0` で外す |
| 小さいメソッドへの `AggressiveInlining`（IL） | 採っていない。効くのは小さいメソッドの呼び出しだけで、1 回あたり 0.4〜1.4 ns（`list[i]` で 1.57 → 0.95 ns）。LINQ、インターフェース呼び出し、仮想呼び出しは変わらず、DLL は 4.2% 増える。BCL まで書き換える経路も要る。試作は `.local/c/weaver/Speed.cs`（Git の外） | — |
| sealed／final 化（IL） | 採っていない。フラグを足して変わるのは仮想呼び出しの 5%（プロジェクトのコードでは 463 か所のうち 19 か所）で、インターフェース呼び出しは変わらない | — |

キャストのキャッシュの効果（製品と同じ形の Player、Windows x64、7 回の最良値、ns）:

| | パッチなし | 遅延化だけ | 遅延化＋キャッシュ |
|---|---:|---:|---:|
| 同じ型へのキャスト（`x is IList<Foo>`） | 14.1 | 18.6 | 7.1 |
| 変性つきのキャスト | 36.3 | 48.3 | 6.7 |
| 配列の変性つきのキャスト | 70.9 | 81.9 | 6.8 |
| 失敗するキャスト | 29.4 | 37.7 | 6.7 |
| `List` への変性つきのインターフェース呼び出し | 39.0 | 57.5 | 10.5 |

- 遅延化がキャストと変性つきの呼び出しに足した負担を打ち消し、パッチなしより速くなる。配列への変性つきの呼び出し（12.2 → 15.5）と、変性なしの呼び出しは変わらない。
- 表は 1 つのキーに 1 か所しか持たない。同じ場所に当たる 2 組のクラスを交互に使うと覚え直しになり、探索と同じ速さに戻る。
- キャッシュを外したビルドは、差し込みがない時よりキャストが 1〜6 ns 遅い（探索を関数に分けた分。雑音の幅と同じ程度）。
- 型あたりのメタデータは変わらない（静的な表 128 KB が増えるだけ）。
- 確かめたこと: 検証 5 種が、スイッチつきの形の 4 つの組と、製品の形のキャッシュあり・なしで通る。製品の形での繰り返し（リフレクション 60 回、インターフェース 30 回、型の同一性 20 回）で失敗なし。`speed/cast-cache-test` は x64 と arm64 の実機で、版を見ない読み手が誤答する条件でも誤答 0 回。
- Android の実機（arm64、Pixel 3 XL）で、キャッシュを入れた常時オンのランタイムに IL の書き換えを重ねた Player がビルドでき、検証 5 種が通った。インターフェースの検証の繰り返し 40 回と、競合の検査 35,100 回で失敗は 0 回だった。
- 確かめていないこと: iOS でのビルドと実行。

Android の実機（arm64、Pixel 3 XL、`-Os`）での速度（5 ラウンドの中央値、ns）。「同じクラスの比較」は、2026-11-13 に `Object::IsInst` へ足した 1 行:

| | 遅延化だけ | ＋キャッシュ（比較なし） | ＋キャッシュ＋同じクラスの比較 |
|---|---:|---:|---:|
| 同じ型へのキャスト（`x is IList<Foo>`） | 41.6 | 15.1 | 16.1 |
| 変性つきのキャスト | 118.0 | 15.0 | 16.1 |
| 配列の変性つきのキャスト | 217.6 | 15.1 | 16.2 |
| 失敗するキャスト | 95.4 | 15.7 | 16.7 |
| `List` への変性つきのインターフェース呼び出し | 137.0 | 29.6 | 29.8 |
| エンジンがオブジェクトを返す呼び出し（`component.transform`） | 53.7 | 57.6 | 53.3 |
| 同（`transform.parent`） | 42.7 | 48.9 | 40.7 |
| 共有コードでの `(T)` へのキャスト（`slim.index`） | 11.3 | 13.1 | 10.1 |

- キャッシュを入れただけの形は、オブジェクトと同じクラスへのキャストを 2〜6 ns 遅くしていた。共有ジェネリックコードの `(T)` と、エンジンが返すオブジェクトの受け取りが全部ここを通る。表の照会が `Class::IsAssignableFrom` に入ったことで、コンパイラが関数の先頭でレジスタを退避するようになり、最初の比較（同じクラスなら true）までその退避と復帰を通る形になったため（4 命令 → 13 命令）。
- 直し方は 2 つ試した。最初の比較だけを `Class::IsAssignableFrom` に残して残りを別の関数にする形でも戻るが、`Object::IsInst` で先に比べる形のほうが関数を呼ばずに済み、0.4〜1.5 ns 速い。後者を採った。
- インターフェースへのキャストは、どちらの形でも約 1 ns 遅く測れた。足した命令（比較 1 つ）では説明がつかず、原因は切り分けていない。この端末では、ライブラリの中のコードの位置が変わるだけで、関係のない小さいカーネルが 0.4〜0.8 ns 動く。
- 確かめたこと: Android と Windows で検証 5 種が通る。結果は `results/e-fix2`、`results/e-eng4`。

## コンパイラの指定、SIMD、エンジン呼び出しの実測（2026-11-13、Pixel 3 XL、arm64）

ここから採用を決めたものはない。判断の材料として数値を残す。条件を 5 ラウンド交互に測った中央値（数値のベンチは最良値）で、ランタイムは遅延化だけを入れたもの。道具は `e-android-build.sh`、`e-android-run.py`、`e-stats.py`。

### コンパイラの指定

- 渡し方は `PlayerSettings.SetAdditionalIl2CppArgs("--compiler-flags=-O3")`。フラグ 1 つにつきオプションを 1 つ書く（カンマでつなぐと 1 つのフラグとして扱われる）。既定の `-Os` の後ろに付いて、後ろが勝つ。
- Android は Debug 以外で `-Os`、LTO なし。iOS は `-O3`、LTO なし（Editor のビルドの DLL を読んだ結果で、iOS ではビルドしていない）。

| 指定 | libil2cpp.so | ビルド時間 | 既存のカーネル 41 本 |
|---|---:|---|---|
| `-O2` | +3.6% | 変わらず | LINQ の重い方 −5%、キャスト +4〜12% |
| `-O3` | +11.0% | 変わらず | 速い 7、遅い 8、±2% 以内 24 |
| `-O3` と `-flto=thin` | +29.5% | 約 2 倍 | 速い 22（`list[i]` −43〜−56%、`IList<T>` 経由 −23〜−41%）、遅い 5、±2% 以内 13 |

- 配列を添字で回すループが、`-O3` と ThinLTO で 55% 遅くなる（2.31 → 3.6 ns）。`-O2` では出ない。原因は分かっていない。
- 起動時間は変わらない。検証 5 種は全条件で通る（各 1 回）。`-O2` と ThinLTO の組み合わせは試していない。

### SIMD と演算（4,096 要素、1 要素あたりの ns）

| 処理 | C# `-Os` | C# `-O3` | 検査なしの C# `-O3` | Burst | NEON の手書き |
|---|---:|---:|---:|---:|---:|
| int の合計 | 1.073 | 0.135 | 0.135 | 0.138 | |
| float の合計 | 1.072 | 1.069 | 1.069 | 1.070（`FloatMode.Fast`: 0.139） | 0.113 |
| y に a×x を足す | 2.148 | 1.672 | 0.230 | 0.249 | |
| コピー | 1.431 | 0.174 | 0.190 | 0.274 | |
| sin | 8.960 | 8.746 | 8.739 | 4.507 | |

- 既定の `-Os` では、C# にも C++ にも自動のベクトル化が 1 つも入らない（逆アセンブルで確認）。
- float の合計は指定では速くならない（足す順序を変えられないため）。範囲の検査が残るループは `-O3` でもベクトル化されず、`[Il2CppSetOption]` で検査を外すと入る。
- 呼び出し 1 回の固定費: C# の非インライン呼び出し 1.9 ns、C++ のソースプラグイン（P/Invoke）1.9〜3.0 ns、Burst の直接呼び出し 14.1 ns（C# の関数ポインタ経由なら 1.9 ns）。16 要素では、Burst の直接呼び出しは `-O3` の C# に負ける。
- sqrt と整数の割り算は、どの手段でも変わらない。

### エンジン呼び出し（自動の書き換えで削れる余地）

Player に入るコード（エンジンとクラスライブラリを除く 38,630 メソッド）を IL から数えた。エンジンを呼ぶ箇所は 2,141、うちループの中は 295。中身はこのプロジェクトのライブラリ、サンプル、URP、UI、Input System で、ゲーム本体のコードではない。

- 呼び出しを規則で消す書き換え（ループの外へ出す、読み直しを消す、2 回を 1 回の API にまとめる）が当てはまるのは 56 か所（2.6%）だけ。
- 数値や構造体をやり取りする呼び出しの橋渡しは最小に近く、`-O3` でも ThinLTO でも変わらない。
- 余地があるのは 2 か所。エンジンのオブジェクトの null 比較（1,121 か所）は 10.1 → 2.3 ns（参照だけの比較は 1.4 ns）。オブジェクトを返す呼び出し（490 か所）の受け取りは、まねた経路で 13.4 → 6.4 ns で、呼び出し全体（42〜54 ns）の 13〜17%。どちらも実装していない。
- 1 回の時間: static の getter 約 5 ns、数値を返す getter 8.6 ns、構造体を返す getter 38.6 ns（同じオブジェクトの 2 回目以降は約 10.7 ns）、親が 4 段ある `position` 96.9 ns とその代入 134.4 ns、`name` 401 ns、`GetComponent<T>` 340 ns。大半はエンジンの中の時間。
- エンジンが返すハンドルは強参照で、ロックを通る経路はない。
