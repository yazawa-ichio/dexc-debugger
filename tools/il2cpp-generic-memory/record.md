# IL2CPP のジェネリックが積むメタデータの検証

> 2026-11-11 までの記録。今のファイルの場所と結果は [README.md](README.md) にある。ここの数値は、インターフェースの遅延化と IL の `enum`／`linq` を足す前のもので、「ファイル」の表と「動かし方」も当時のままである。なぜ積まれるかの説明と、採らなかった手法の記録として残している。

IL2CPP の Player で、ジェネリック型を使うたびに VM に積まれるメタデータを測り、減らす手法を小さなシナリオで比べた記録。Unity 6000.3.20f1、Windows Standalone、IL2CPP、Managed Stripping High、C++ は Release で測った。

## 結論

計測は Windows Standalone（x64）だけで、速度は比べていない。Android と iOS ではビルドしていない。以下はその範囲での結果。

- **VM 側の 2 つの遅延化が、最小の変更で全部に同時に効く。** libil2cpp の 6 ファイルへの 2 つのパッチで、IL を変えずに、`List` −35〜50%、`Dictionary` −30〜64%、自作のジェネリッククラス −40〜52%、LINQ −25〜41% になった。
- **UniTask は IL でステートマシンを寄せると、async メソッド 1 本あたり −95%**（6.5 KB → 0.3 KB）。結果の型ごとに残る分は VM の遅延化と重ねて −51%。VM の変更とは独立に効く。
- **メソッドを型の外へ移す IL 加工は、単独なら `List` と自作クラスに −21〜37% 効くが、VM の遅延化を入れると上積みはほぼ無い。** VM に手を入れられない場合の代わりになる。
- **参照型の型引数を寄せる IL 加工は、9,962 か所を書き換えて上積みが 0〜7 ポイントだった。** 手間と危うさに見合わない。
- **VM の遅延化は、消すのではなく後回しにする。** 使われたメソッドと RGCTX の項目は、使われた時に作られる。表の値はシナリオが触れた分だけを払った結果で、同じ型に使う API が増えるほど基準に近づく（`List` は、作るだけで −50%、9 種の API を使うと −35%）。
- **残るのは、仮想スロットとインターフェース実装の `MethodInfo`、インターフェースと配列のクラス、LINQ のイテレータ類。** どれも型のインスタンスごとに要るもので、インスタンスの数そのものを減らす以外に減らせない。
- **`List` と `Dictionary` は、要素の型を寄せて `List<object>`／`Dictionary<K, object>` として持てば、型ごとの消費がほぼ無くなる**（33.2 KB → 0.3 KB、30.3 KB → 0.3 KB）。手で書いたコードで上限を測っただけで、機械的に置き換える加工は作っていない。Unity がシリアライズするフィールドと、コレクションをそのまま外へ渡す所には使えない。

規模の目安: 型を 1,000 個の `List<T>` で使うと 33 MB が 20 MB に、async メソッドが 5,000 本なら 32 MB が 1.7 MB になる計算（1 型・1 本あたりの値を掛けただけで、実プロジェクトでは測っていない）。

## 手法の比較

効果は実測（下の「結果」）。コード量は今回の実装の行数。**速度と GC の確保量は推定**で、計測は `bench.sh` に用意してある。

VM の遅延化だけは一度測った（`results/bench-vm`）。ただし別のビルドと重なった回があり、同じ条件の基準どうしで最大 2 倍ぶれたので、参考にとどまる。負荷の軽かった回どうしで比べると、定常時（`List`、`Dictionary`、自作クラス、async のホットループ 17 種）は、メソッド遅延化・RGCTX 遅延化とも基準より遅くなった項目がぶれの範囲を超えず、型の初回利用（200 型）は `List` で 66 → 44 ms、`Dictionary` で 53 → 34 ms、自作クラスで 121 → 66 ms と短くなった。両方を入れた回は負荷の重い時間帯に当たり、比べられていない。

| 手法 | 効果（1 型／1 本あたり） | コード量 | 間違いが見つかる所 | 速度（推定） | GC の確保（推定） |
|---|---|---|---|---|---|
| VM: メソッド遅延化 | 単独 −2〜21% | libil2cpp +37／−4 行（3 ファイル） | 実行 | 定常は変わらない。型の初回利用は速くなる | 変わらない |
| VM: RGCTX 遅延化 | 単独 0〜47%。上と合わせて List −35〜50%、Dictionary −30〜64%、自作クラス −40〜52%、LINQ −25〜41% | libil2cpp +69／−11 行（3 ファイル） | 実行 | 共有コードが RGCTX を読むたびに比較 1 回。型の初回利用は速くなる | 変わらない |
| IL: メソッド移動 | List −21〜37%、自作クラス −23〜34%、Dictionary −1〜14%。VM の 2 つを入れると上積みなし | 約 500 行 + 土台 130 行 | ビルド（自分のアセンブリだけなら Editor） | ほぼ変わらない。移した先はメソッドごとに RGCTX の初期化確認が入る | 変わらない |
| IL: 型引数を寄せる | 上積み 0〜7 ポイント | 563 行 | ビルド（型の食い違いだけ。判定の誤りは実行） | 呼び出しごとにキャストと再解釈が入る | 変わらない |
| IL: UniTask の寄せ | async 1 本 −95%（6.5 → 0.3 KB） | 226 行 | ビルド（Editor で通すには Debug の形への対応が要る） | 開始は 1 段減る。中断で box とコピー、再開はインターフェース呼び出し | **中断 1 回ごとに box 1 個**（プール未実装） |
| IL: 要素の型を寄せる（未実装） | 寄せられた型は −99%（33.2 → 0.3 KB）。どれだけの型が寄せられるかは未調査 | 見積もり 800〜1,500 行 | Editor | 読み出しごとにキャスト 1 回 | 変わらない |
| 軽い専用コレクション（手書き） | −95%（33.2 → 1.8 KB） | リスト 1 種で 70 行。Dictionary 相当は別に要る | Editor（普通のコード） | List と同程度。読み出しにキャスト | 変わらない |

### 保守性と再現性

| 手法 | 何に依存するか | 版が変わった時 | 別のプロジェクト・プラットフォームで同じになるか |
|---|---|---|---|
| VM: メソッド遅延化 | libil2cpp の `Class::InitLocked` と、メソッドを読む側が自分で作る作り | パッチは元の文字列が無ければ止まるので、当たらなければ気付く。当て直しは小さい | プロジェクトのコードに依らず全部に効く。効果は使う API の広さで変わる。Windows x64 でしか動かしていない |
| VM: RGCTX 遅延化 | 生成コードが RGCTX を 4 つのインライン関数でしか読まないこと | 版ごとに il2cppOutput を数え直して確かめる（今回は 221,431 か所が全て一致）。自動で確かめられる | 同上。ヘッダーを変えるので全 C++ が再コンパイルになる |
| VM の 2 つに共通 | il2cpp フォルダの差し替え（`UNITY_IL2CPP_PATH` と配置合わせ）。モバイルではエクスポートしたプロジェクト内のソースに当てる経路になるはず | 差し替えの仕組み自体が版で変わり得る（6000.3 では開発用の配置を要求された） | iOS／Android の経路は未確認 |
| IL: メソッド移動 | 型やメソッドの名前には依らない（規則で選ぶ）。除外の規則（エンジンのアセンブリ、属性、関数ポインタで取られるもの） | BCL が変わっても同じ規則で動く。除外の見直しだけ | 全ジェネリッククラスに効く。リフレクションで名前を探すコードが多いプロジェクトでは除外が増える。スタックトレースの名前が変わる |
| IL: 型引数を寄せる | il2cpp がどの呼び出しを直接呼び出しにするか（`sealed`／`final`）と、型の違う参照にキャストを入れないこと | 判定の前提を確かめ直す必要がある。前提が崩れても静的には分からない | 効くのは本体が型を見ないメソッドだけで、もともと軽い |
| IL: UniTask の寄せ | C# コンパイラが出す IL の形と、UniTask のビルダーの非公開フィールド 2 つ | 形が合わない所は元のまま残すので、壊れるのではなく効かなくなる。書き換えた件数をログで見れば分かる | UniTask の async 全部に同じ形で効く（今回 1,344 個で取りこぼし 0）。Debug のコンパイルには効かない |
| IL: 要素の型を寄せる | BCL のコレクションの、どのメンバーが要素を受け渡すかの表。流れの判定 | コレクションの API が増えたら表を足す | **プロジェクトの書き方で大きく変わる**。シリアライズされる型と、LINQ や API にそのまま渡す型は残る |
| 軽い専用コレクション | 自前の実装 | 自分で持つコードが増える | 使った所にだけ効く。`List<T>` の代わりに書く必要がある |

### 目的別の見方

- **手軽で効果が大きい**: VM の遅延化 2 つ（計 106 行）。IL も既存のコードも変えずに全部に効く。代わりに、間違いが実行するまで分からない段にあり、モバイルのビルド経路も未確認。UniTask の寄せ（226 行）も、async が多いコードなら行数の割に大きい。代償は中断ごとの box。
- **危険を伴うが強力**: VM の RGCTX 遅延化（ランタイムが生成コードに値を渡す経路に手を入れる）。生成された C++ を変形してスロットの `MethodInfo` を共有する案（未検証）。
- **コード量は多いが効果も大きい**: 要素の型を寄せる（未実装。寄せられた型は −99%）。IL の加工の中では、間違いが Editor で見つかる段に置ける唯一の大きな手。
- **効果の割に合わない**: 型引数を寄せる。メソッド移動は、VM の遅延化を使うなら要らない（VM に手を入れない場合の主力）。

VM に手を入れるかどうかで組み合わせが分かれる。

| 方針 | 組み合わせ | 見込み |
|---|---|---|
| VM に手を入れる | VM の 2 つ + UniTask の寄せ | List −38%、Dictionary −30%、自作クラス −52%、async 1 本 −95%（Windows での実測。async の値はメソッド移動も入ったビルドのもの。メソッド移動は async のパターンに効かないので、この組み合わせでも同じと見ている） |
| VM に手を入れない | メソッド移動 + UniTask の寄せ、その上で要素の型を寄せる | List −30%、Dictionary −1%、自作クラス −34%、async 1 本 −95%（実測）。要素の型を寄せた分は型ごとにほぼ 0 へ |

## なぜ積まれるか

libil2cpp（`Editor/Data/il2cpp/libil2cpp`）のソースと、確保箇所ごとのカウンタで確かめた。

- 参照型どうしで共有されるのはコードだけで、メタデータは `List<A>` と `List<B>` で別に持つ。
- ジェネリック型のインスタンスが初期化されると（`Class::InitLocked`）、定義に残っている**全メソッド**の `MethodInfo` が作られる（`GenericClass::SetupMethods`）。呼ぶかどうかは関係ない。プロパティとイベントも同じ。
- 同じ初期化の最後に、クラスの **RGCTX の全項目**が解決される（`GenericMetadata::InflateRGCTXLocked`）。RGCTX は、そのクラスのメソッドのどれかが実行時に要る型とメソッドの一覧で、項目を解決するとその型やメソッドが作られる。`List<T>` なら `ReadOnlyCollection<T>`、`Comparison<T>`、`Predicate<T>`、Sort の補助などが、使わなくても作られる。
- 仮想メソッドとインターフェース実装はスロットを埋めるので、VTable を作る時点で必ず `MethodInfo` が要る。ここは型のインスタンスごとに避けられない。
- 配列 `T[]` は、ジェネリックなインターフェース（`IList<T>` など）のメソッドを要素型ごとに 15 個持つ。

`new List<T>()` を 1 行書くだけで、1 型あたり 27,984 バイト（27.3 KB）のメタデータが確保される。内訳は次のとおり。

| 確保されるもの | 個数 | バイト |
|---|---:|---:|
| `MethodInfo`（ジェネリックのインスタンス） | 124 | 10,912 |
| `Il2CppClass` と VTable（ジェネリックのインスタンス） | 14 | 6,000 |
| 配列 `T[]` のクラス、メソッド、インターフェース表 | | 約 3,800 |
| `MethodInfo` ごとの `Il2CppGenericMethod` | 94 | 2,256 |
| `Il2CppType`、引数の型の配列 | | 約 2,200 |
| RGCTX の配列（54 項目）、フィールド、プロパティ、その他 | | 約 2,800 |

これとは別に、メソッドやジェネリック型を引くためのハッシュ表と、プールの端数がある。`new List<T>()` では、それらを含めた IL2CPP 全体の確保は 1 型あたり約 44 KB だった（ハッシュ表が倍に伸びる所をまたぐと大きく出るので、表ではプールへの要求量を使う）。

既存のビルド（Autopilot の Player）を静的に数えると、`List<T>` は 889 個のインスタンスに 57 メソッド、`Dictionary<K,V>` は 561 個に 46 メソッドで、この 2 つでメソッド×インスタンスの半分近くになる。全体の 58% は仮想スロットかコンストラクタで型に固定されている（`Census`）。

## 測り方

- シナリオ（`Assets/Tests/GenericMemory/Scenario`）は 19 パターンで、それぞれ 200 個の型（値型は 40 個）について同じコードを持つ。`generate.py` が書く。
- Player は 1 回の起動で 1 パターンの先頭 n 個だけを実行し、前後の差を書き出す。n を半分と全部の 2 回で測り、その**差を型の数で割った値**を 1 型あたりの消費とする。1 回だけ払うもの（クロージャのクラス、LINQ の初回）は差で消える。
- バイト数は、libil2cpp の複製に入れたカウンタで取る（`libil2cpp/apply-counters.py`）。メタデータのプールへの要求を、呼び出し元の行ごとに数える。Editor 同梱のフォルダには触れない。
- カウンタなしでも `Profiler.GetTotalAllocatedMemoryLong()` の差で同じ傾きが出る（プールの領域が 64 KB 単位なので粗い）。`new List<T>()` で 1 型あたり 28.4 KB（28,350 バイト）と、カウンタの 28.0 KB（27,984 バイト、どちらも 1,000 で割った値）に近かった。
- 各条件で、計算結果の合計と、完了していない async の数が基準と一致することを確かめている。
- `KeepAll.cs` が List、Dictionary、LINQ、UniTask の API を 1 つの型で広く使う。大きなプロジェクトではどのメンバーもどこかで使われてリンカーに残るので、それに合わせるため。
- Player に `-generic-memory-keepall` を付けると、その `KeepAll` を実際に実行する。List、Dictionary、HashSet、LINQ、`Repository<T>` の広い API と、await が 17 回続く async を通して、合計が基準と一致し、例外が出ないことを確かめた（基準と全部入りの加工、それぞれ VM の遅延化なし・あり）。

## 結果

1 型（async のパターンは 1 メソッドまたは 1 組）を足すごとに増える、メタデータのプールへの要求量。括弧は基準との差。

VM の変更だけ（IL は基準のまま）:

| パターン | 基準 | VM: メソッド | VM: RGCTX | VM: 両方 |
|---|---:|---:|---:|---:|
| ArrayUse | 14.6 KB | 14.3 KB (-2%) | 14.5 KB (-0%) | 14.2 KB (-2%) |
| DelegateUse | 2.7 KB | 2.2 KB (-16%) | 2.7 KB (+0%) | 2.2 KB (-16%) |
| DictNew | 17.3 KB | 14.3 KB (-17%) | 9.2 KB (-47%) | 6.2 KB (-64%) |
| DictUse | 30.3 KB | 27.0 KB (-11%) | 24.2 KB (-20%) | 21.1 KB (-30%) |
| EnumDict | 30.4 KB | 26.7 KB (-12%) | 24.1 KB (-21%) | 20.8 KB (-32%) |
| HashSetUse | 26.1 KB | 23.7 KB (-9%) | 21.6 KB (-17%) | 17.8 KB (-32%) |
| LinqBasic | 41.9 KB | 35.9 KB (-14%) | 32.8 KB (-22%) | 24.8 KB (-41%) |
| LinqHeavy | 136.5 KB | 124.5 KB (-9%) | 116.9 KB (-14%) | 102.1 KB (-25%) |
| ListMore | 36.0 KB | 30.5 KB (-15%) | 31.0 KB (-14%) | 23.2 KB (-35%) |
| ListNew | 27.3 KB | 22.0 KB (-20%) | 20.2 KB (-26%) | 13.8 KB (-50%) |
| ListUse | 33.2 KB | 27.6 KB (-17%) | 27.2 KB (-18%) | 20.5 KB (-38%) |
| StructList | 24.7 KB | 19.4 KB (-21%) | 18.6 KB (-25%) | 12.2 KB (-51%) |
| TaskT | 9.5 KB | 8.5 KB (-11%) | 7.1 KB (-25%) | 5.6 KB (-42%) |
| UniTaskInt | 13.7 KB | 13.0 KB (-5%) | 13.3 KB (-3%) | 12.1 KB (-12%) |
| UniTaskT | 27.4 KB | 25.0 KB (-9%) | 22.9 KB (-16%) | 18.9 KB (-31%) |
| UniTaskVoid | 6.5 KB | 6.2 KB (-6%) | 6.4 KB (-3%) | 5.8 KB (-12%) |
| UserClass | 61.7 KB | 49.0 KB (-21%) | 43.1 KB (-30%) | 29.3 KB (-52%) |
| UserClassWide | 76.6 KB | 63.4 KB (-17%) | 62.6 KB (-18%) | 45.9 KB (-40%) |
| UserMethod | 29.2 KB | 23.8 KB (-18%) | 23.1 KB (-21%) | 16.9 KB (-42%) |

IL の加工と、VM との組み合わせ:

| パターン | 基準 | IL: 移動 | IL: 移動 + UniTask | IL: 全部 | IL: 全部 + VM: 両方 |
|---|---:|---:|---:|---:|---:|
| ArrayUse | 14.6 KB | 14.6 KB (-0%) | 14.6 KB (-0%) | 14.6 KB (-0%) | 14.3 KB (-2%) |
| DelegateUse | 2.7 KB | 2.7 KB (+0%) | 2.7 KB (+0%) | 2.7 KB (+0%) | 2.2 KB (-16%) |
| DictNew | 17.3 KB | 14.9 KB (-14%) | 14.9 KB (-14%) | 14.9 KB (-14%) | 6.1 KB (-65%) |
| DictUse | 30.3 KB | 30.1 KB (-1%) | 30.1 KB (-1%) | 30.1 KB (-1%) | 21.6 KB (-29%) |
| EnumDict | 30.4 KB | 29.0 KB (-5%) | 29.0 KB (-5%) | 29.0 KB (-5%) | 21.2 KB (-30%) |
| HashSetUse | 26.1 KB | 25.0 KB (-4%) | 25.0 KB (-4%) | 25.0 KB (-4%) | 18.1 KB (-30%) |
| LinqBasic | 41.9 KB | 32.7 KB (-22%) | 32.7 KB (-22%) | 32.7 KB (-22%) | 24.9 KB (-40%) |
| LinqHeavy | 136.5 KB | 124.8 KB (-9%) | 124.8 KB (-9%) | 124.8 KB (-9%) | 102.4 KB (-25%) |
| ListMore | 36.0 KB | 28.2 KB (-21%) | 28.2 KB (-21%) | 25.8 KB (-28%) | 21.7 KB (-40%) |
| ListNew | 27.3 KB | 17.2 KB (-37%) | 17.2 KB (-37%) | 17.2 KB (-37%) | 13.6 KB (-50%) |
| ListUse | 33.2 KB | 23.2 KB (-30%) | 23.2 KB (-30%) | 23.2 KB (-30%) | 20.4 KB (-39%) |
| StructList | 24.7 KB | 14.7 KB (-40%) | 14.7 KB (-40%) | 14.7 KB (-40%) | 12.0 KB (-51%) |
| TaskT | 9.5 KB | 8.3 KB (-13%) | 8.3 KB (-13%) | 8.3 KB (-13%) | 5.6 KB (-41%) |
| UniTaskInt | 13.7 KB | 13.7 KB (+0%) | 0.7 KB (-95%) | 0.7 KB (-95%) | 0.7 KB (-95%) |
| UniTaskT | 27.4 KB | 27.4 KB (+0%) | 21.2 KB (-23%) | 21.2 KB (-23%) | 13.5 KB (-51%) |
| UniTaskVoid | 6.5 KB | 6.5 KB (+0%) | 0.3 KB (-95%) | 0.3 KB (-95%) | 0.3 KB (-95%) |
| UserClass | 61.7 KB | 40.6 KB (-34%) | 40.6 KB (-34%) | 40.6 KB (-34%) | 29.3 KB (-52%) |
| UserClassWide | 76.6 KB | 59.2 KB (-23%) | 59.2 KB (-23%) | 56.6 KB (-26%) | 44.1 KB (-42%) |
| UserMethod | 29.2 KB | 19.6 KB (-33%) | 19.6 KB (-33%) | 19.0 KB (-35%) | 16.4 KB (-44%) |

列の意味:

| 列 | ビルドの加工 | 実行時の切り替え |
|---|---|---|
| 基準 | なし | なし |
| VM: メソッド | なし | `GENERIC_MEMORY_LAZY_METHODS=1` |
| VM: RGCTX | なし | `GENERIC_MEMORY_LAZY_RGCTX=1` |
| VM: 両方 | なし | 両方 |
| IL: 移動 | `hoist` | なし |
| IL: 移動 + UniTask | `hoist+async` | なし |
| IL: 全部 | `hoist+canon+async` | なし |
| IL: 全部 + VM: 両方 | `hoist+canon+async` | 両方 |

`IL: 移動 + UniTask` に VM の両方を重ねた結果（`results/hoist-async-vm`）は、async 以外のパターンで `VM: 両方` と 0.5 KB 以内で一致した。

| パターン | 1 つの型について実行するコード |
|---|---|
| ListNew | `new List<T>()` だけ |
| ListUse | Add、Count、添字、foreach、Contains、RemoveAt、Clear |
| ListMore | AddRange、Sort、Find、FindIndex、RemoveAll、ToArray、Insert、Reverse、IndexOf |
| DictNew / DictUse | `new Dictionary<int, T>()` だけ / Add、添字、TryGetValue、ContainsKey、foreach、Values、Remove |
| HashSetUse | `HashSet<T>` の Add、Contains、foreach、Remove |
| ArrayUse | `new T[3]`、`Array.IndexOf`、foreach |
| DelegateUse | `Action<T>`、`Func<T, bool>`、`Func<T>` を作って呼ぶ |
| LinqBasic | FirstOrDefault、Any、Where + ToList、Select + ToArray |
| LinqHeavy | OrderBy + ThenByDescending、GroupBy + ToDictionary、Distinct、Skip／Take／Concat、Sum、Max |
| UniTaskVoid | `async UniTask` のメソッド 1 本（`await UniTask.Yield()`） |
| UniTaskInt | `async UniTask<int>` 1 本と、それを await する `async UniTask` 1 本 |
| UniTaskT | `async UniTask<T>` 1 本と、それを await する `async UniTask` 1 本（結果の型も 1 つ増える） |
| TaskT | 同じ形を `Task<T>` と `async void` で |
| UserClass | 自作の `Repository<T>`（メソッド 39 個、中に `List<T>` と `Dictionary<int, T>`）を作って 3 つのメンバーだけ使う |
| UserClassWide | 同じクラスのメンバーを 10 個ほど使う |
| UserMethod | 自作の静的ジェネリックメソッド 4 つを `List<T>` に使う |
| StructList | 値型 40 個について ListUse と同じ |
| EnumDict | enum 40 個について `Dictionary<TEnum, int>` |

## 手法

### VM: メソッドを要る時に作る（`Il2CppLazyMetadata.cs`）

`Class::InitLocked` が、ジェネリックのインスタンスについてはメソッド、プロパティ、イベントを作らないようにする。libil2cpp は読む側（`Class::GetMethods`、`GetProperties`、リフレクションの icall）で既に「無ければ作る」を呼んでいるので、初期化でやめるだけで足りる。名前で探す所（静的コンストラクタ、デリゲートの `Invoke`）は、定義から見つけて 1 つだけ作るようにした。変更は 5 か所。

- リフレクションで列挙した時は、その時に全部作られる作りなので、結果は変わらないはず。ただしリフレクションを使うコードでは確かめていない。
- 仮想スロットと RGCTX が指すメソッドは、これだけでは減らない。

### VM: RGCTX を読まれた項目だけ解決する（`Il2CppLazyMetadata.cs`）

生成された C++ が RGCTX を読むのは、`il2cpp-codegen.h` の 4 つのインライン関数（`il2cpp_rgctx_data`、`il2cpp_rgctx_data_no_init`、`il2cpp_rgctx_type`、`il2cpp_rgctx_method`）だけで、どれも配列と添字を受ける。基準のビルドの il2cppOutput 全体（384 ファイル）で、`->rgctx_data` の 221,431 か所が全てこの 4 つの関数の引数で、添字で直接読む所は無かった。そこで、配列を空で作り、手前の 3 スロットに出どころ（image、token、ジェネリックの文脈）を置き、空の項目を読んだ時にその 1 項目だけを解決する。変更は 3 ファイル。

- 共有コードが RGCTX を読むたびに、NULL との比較が 1 回増える。
- 解決はメタデータのロックの中で行う。読む側はロックを取らないので、項目は別の場所で解決し終えてから、`GenericClass::CreateClass` がクラスを見せるのと同じアトミックな書き込みで配列に入れる。ジェネリックメソッドの RGCTX は今でも初回に遅れて作られており、それと同じロックを使う。
- 型の静的コンストラクタが走る時点は変わらない（RGCTX の解決は、型のサイズとレイアウトまでしか初期化しない）。

### IL: メソッドを型の外へ移す（`Hoister`）

ジェネリッククラスの、スロットを埋めないメソッド（非仮想、コンストラクタ以外）を、ジェネリックでないクラスの静的ジェネリックメソッドへ移し、プログラム全体の呼び出し元を書き換える。型のインスタンスが払うのはスロットに要るメソッドだけになり、移したメソッドは実際に呼ぶ型の分だけ作られる。移したメソッドが要っていた RGCTX の項目もクラスから消えるので、連鎖して作られる型も減る。

- `IPostBuildPlayerScriptDLLs` で動かす。その時点で `Temp/StagingArea/Data/Managed` に mscorlib を含む全 DLL があり、UnityLinker はそこを読むので、BCL、UniTask、自分のコードを 1 回で扱える。
- 今回は 514 クラスの 2,411 メソッドが移った。`List<T>` は 74 → 30、`Dictionary<K,V>` は 60 → 43、シナリオの `Repository<T>` は 39 → 6（リンク前の数）。
- 移さないもの: 仮想メソッドとインターフェース実装、コンストラクタ、`ldftn` や `ldtoken` で値として取られるメソッド、`constrained.` で呼ばれるメソッド、`UnityEngine.Object` の派生とデリゲート、`UnityEngine*`／`Unity.*` のアセンブリの型、静的コンストラクタが厳密な型の静的メソッド、`Preserve` などの属性が付いたメソッド。
- 変わること: 移したメソッドはリフレクションで元の型から見えない（アクセサが両方移ったプロパティは消える）。スタックトレースでは `List_1_Hoisted.Find` のように出る。レシーバーが null の時、例外は呼び出し元ではなく移した先の最初のフィールドアクセスで出る。
- 値型のジェネリックは対象外にした。値型のクラスは、box されるか配列の要素になるまで全体の初期化がされないため。

### IL: 参照型の型引数を 1 つの型に寄せる（`Canonicalizer`）

ジェネリックメソッドの本体が型引数について実行時に何も要らないなら、呼び出し側が参照型を何にしても同じ結果になる。そういう引数を、全ての呼び出し元で同じ型（制約があればその型、無ければ `object`）に書き換えると、参照型の数だけあった `MethodInfo` が 1 つになる。「何も要らない」は IL から機械的に決める。

- 要らないと見なす使い方: 引数や局所変数として持つ、フィールドを読む、`box`／`initobj`／配列要素の読み書き、デリゲートの `Invoke`、本当に仮想ディスパッチされる呼び出し、同じく要らないと分かっているメソッドへの受け渡し。
- 要ると見なす使い方: `newobj`、`newarr`、`castclass`／`isinst`、`ldtoken`、静的フィールド、**ジェネリックなインターフェース経由の呼び出し**（ランタイムがインターフェースを厳密なインスタンスで探すため）、`sealed`／`final` なメソッドの呼び出し（il2cpp が直接呼び出しにして、呼び出し側の文脈を渡すため。相手も要らないと分かっている時だけ通す）。
- 参照の型が変わる引数は、渡したものをそのまま返す `As<T>(object)` を通す。il2cpp は型の違う参照にキャストを入れず、`List<A>` と `List<object>` は C++ では別の構造体なので、通さないと C++ のコンパイルが通らない。`As` は寄せた先の型の数だけ作られる。
- 値型のジェネリックを値で受け渡すメソッド（`InlinedArray<T>` など）と、参照渡しのあるメソッドは対象外。C++ の構造体は互いにキャストできない。
- LINQ の演算子はほぼ対象にならない。本体が `IEnumerable<T>` を経由して呼び、イテレータのクラスを `newobj` するため。

今回は 257 個のメソッドの 9,962 か所が書き換わった（`List` の `Find`／`FindIndex`／`Exists`／`ForEach`／`Sort`、`Array` の同種、自作の `Util.Swap` など）。上積みは ListMore で 7 ポイント、UserClassWide と UserMethod で 2〜3 ポイント、ほかは 0 だった。型のインスタンスが払う分のほとんどはスロットに固定されたメソッドと連鎖して作られる型で、「寄せられるメソッド」はもともと軽い。

### IL: UniTask のステートマシンを 1 つのランナーに寄せる（`AsyncEraser`）

コンパイラは async メソッドごとにステートマシンの構造体を作り、ビルダーに型引数として渡す（`AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>`、`Start<TStateMachine>`）。UniTask はそれを `AsyncUniTask<TStateMachine, T>` に持つので、async メソッドごとに、このクラス、そのプール、インターフェース、ビルダーのメソッドのインスタンスが作られる。

ビルダーがステートマシンに求めるのは `IAsyncStateMachine` だけなので、次のように書き換える。

- 最初に本当に中断する await で、構造体を box し、型引数を `IAsyncStateMachine` にして渡す。以後はその box の上で走る（元のコードがランナー内の複写の上で走るのと同じ）。
- ビルダーにランナーが既にあれば、ステートマシンの引数は使われないので、空の参照を渡す。
- `Start<TStateMachine>` は `MoveNext` を呼ぶだけなので、直接呼ぶ。
- `MoveNext` の名前と場所は変えないので、スタックトレースは元のまま。

今回は 1,344 個のステートマシン（UniTask 自身の中のものを含む）の 2,014 か所の await が書き換わった。ランナーのクラスは結果の型ごとに 1 つ（`UniTask` と `UniTaskVoid` は全体で 1 つずつ）になる。

- 計測用の 3 パターンは await が 1 回の形で、600 本とも完了して結果が基準と一致した。
- await が 17 回続くメソッド（`KeepAll.Tasks`: `UniTaskCompletionSource`、`WhenAll`、`WhenAny`、`Lazy`、`AsTask`、`Preserve`、`Timeout`、`ContinueWith`、`Delay`、`NextFrame`、`WaitUntil` など）も `-generic-memory-keepall` で実行し、最後の await まで再開して、合計が基準と一致した。2 回目以降の await（ランナーが既にある側）はここで通している。

- **中断する呼び出し 1 回につき box を 1 つ確保する**。UniTask の「確保なし」は崩れる。box をステートマシンの型ごとの static 変数に 1 つ置いて `Interlocked.Exchange` で借り、ランナーが返却される時（`TryReturn`）に戻せば確保は消せるはずだが、今回は作っていない。戻す時点は、UniTask 自身がランナーをプールへ戻す時点と同じにできる。
- C# のコンパイラが出す形（`ldarg.0; ldflda builder; ldloca awaiter; ldarg.0; call`）にだけ合わせている。合わない所は元のまま残す。Debug のコンパイル（ステートマシンがクラス）は対象外。
- ビルダーの非公開フィールド（`runnerPromise`、`runner`）を IL から読む。UniTask の版が変わったら確かめ直す。

## 安定性で並べる

間違いがどこで見つかるかで分けると、次の 3 段になる。上ほど安定。

| 間違いが見つかる所 | 手法 | 備考 |
|---|---|---|
| **Editor**（ILPostProcess で書き換え、Editor も同じ IL で動く） | 要素の型を寄せる（下の節。未実装）、軽い専用コレクションへの差し替え（未実装）、自分のアセンブリのジェネリッククラスのメソッド移動、UniTask の寄せ | ILPostProcess はアセンブリを 1 つずつ見るので、BCL や UniTask 自身の中は書き換えられない。UniTask の寄せは、Editor の Debug コンパイルではステートマシンがクラスになるので、クラスの形にも対応しないと Editor では通らない（クラスなら box は要らず、型引数を替えるだけで済む） |
| **ビルド**（全 DLL を機械的に置き換え、漏れや食い違いはビルドが止める） | メソッド移動（BCL と UniTask を含む）、型引数を寄せる、UniTask の寄せ（今回の実装の位置） | 今回、置き換えの漏れは UnityLinker の「Unresolved method reference」、型の食い違いは C++ のコンパイルエラー、壊れた分岐は il2cpp のエラーで、どれもビルドで止まった。実行して初めて分かった間違いは無かった |
| **実行**（動かすまで分からない） | VM の遅延化 2 つ | IL は変わらないが、ランタイムの動きが変わる。静的に確かめる手段が無い |

## List と Dictionary を狙う別の手

VM に手を入れない場合、`List<T>` と `Dictionary<K,V>` にメソッド移動で届くのは −1〜37% までで、残りはスロットに固定されたメソッド、インターフェースのクラス、配列 `T[]` のクラスが占める。これらは「その型のインスタンスがある」限り要るので、減らすには**インスタンスそのものを作らせない**しかない。そのための形を 2 つ、手で書いたコードで上限だけ測った（加工は未実装）。

| パターン | 1 型あたり | そのまま使う場合との差 |
|---|---:|---:|
| ListUse（`List<T>` をそのまま） | 33.2 KB | |
| ListErased（`List<object>` に入れ、読む所でキャスト） | 0.3 KB | −99% |
| SlimListUse（下の軽い専用リスト） | 1.8 KB | −95% |
| DictUse（`Dictionary<int, T>` をそのまま） | 30.3 KB | |
| DictErased（`Dictionary<int, object>` に入れ、読む所でキャスト） | 0.3 KB | −99% |

どれも同じ操作をして、合計は一致した。0.3 KB は要素のクラス自身（ジェネリックでない）の分で、そのクラスを使う限りどの形でも払う。VM の遅延化を入れても、この 3 つの値は変わらない。

### 要素の型を寄せる（`List<T>` → `List<object>`、`Dictionary<K, V>` → `Dictionary<K, object>`）

参照型を入れるコレクションを、実行時には `List<object>` 1 つで持ち、要素を読む所に `castclass T` を入れる。型ごとの消費は残らない。ILPostProcess で、アセンブリの中で閉じている所だけを機械的に置き換えられる。

- **寄せてよいもの**: private なフィールド、局所変数、private メソッドの引数と戻り値のうち、同じ値が通る先が全部その中に収まっているもの。同じ値が通る所を 1 つの組にまとめ、組の中に 1 つでも外へ出る所があれば組ごと寄せない。この判定は IL だけで決まる。
- **外へ出る所**（寄せない）:
  - **Unity がシリアライズするフィールド**（`MonoBehaviour`／`ScriptableObject`／`[Serializable]` 型の public または `[SerializeField]` のフィールド、`[SerializeReference]`）。宣言された型そのものが要る。
  - 他のアセンブリから見えるメンバー。
  - コレクション自身のメソッド以外に渡す所（LINQ、Unity の API、`IEnumerable<T>` への代入）。`List<object>` は `IEnumerable<T>` を実装していないので、インターフェース経由で呼ぶと実行時に失敗する。
  - `typeof`、`is`、`as`、要素の型つきの配列を返す `ToArray`。
- **Dictionary のキーは替えない。** 比較の仕方がキーの型で決まる。値だけを寄せる。
- **効くのは、その要素型のコレクションがプログラムのどこにも残らない時だけ。** 消費は型ごとに 1 回なので、シリアライズされるフィールドなどで 1 か所でも `List<T>` が残れば、その型の分は払う。
- リストに渡すデリゲート（`Predicate<T>`、`Comparison<T>`）も同じ組に入れて寄せる。ラムダの引数を `object` にして先頭で `castclass` する。

### 軽い専用コレクションに差し替える（`SlimList<T>`、`Scenario/Slim.cs`）

型は `T` のまま残したい所（他のアセンブリに見せる、寄せる判定で外に出ると出た所）向け。インターフェースを持たず（埋めるスロットが無い）、自分のメソッドはコンストラクタだけ（初期化で作るものが無い）、要素は `object[]` に持つ（要素型ごとの配列のクラスが要らない）。操作は静的ジェネリックメソッドで、呼んだ型の分だけ作られる。

- `List<T>` と同じ動きにはならない（インターフェースが無いので LINQ や `foreach` のインターフェース経由が使えない）。差し替えられるのは、使い方がこの型の操作に収まる所だけ。
- Unity のシリアライズは `List<T>` と配列しか知らないので、シリアライズされるフィールドには使えない。

## まだ試していない方向

`List<T>` と `Dictionary<K,V>` は BCL の型で、Unity のシリアライズも API もこの型そのものを前提にするので、型を大きく変えることはできない。型は残したまま 1 インスタンスの重さを削る方向を、広めに挙げておく。どれも未検証で、効果の数字は基準の内訳からの見積もり。

VM の遅延化を両方入れた後、`List<T>` を普通に使う 1 型（20.5 KB）に残っているのは、スロットに固定されたメソッドの `MethodInfo`（約 30 個）、配列 `T[]` のクラスとそのインターフェースメソッド（約 3.8 KB）、インターフェースのクラス（5 個、約 1.8 KB）、クラス本体と VTable（約 0.8 KB）、それに実際に使ったメソッドの分。

- **生成された C++ を変形して、スロットの `MethodInfo` を参照型の間で共有する。** 共有コードはジェネリックの文脈を `method->klass->rgctx_data` から取るので、`MethodInfo` は型ごとに別でなければならない。参照型のクラスのインスタンスメソッドに限れば、文脈は `__this->klass` からも取れる。il2cpp の出力をビルドの途中で書き換えてそうすれば、VM は VTable に共通の `MethodInfo` を入れられ、スロットの分が型ごとには要らなくなる。構造体と静的メソッドには使えない。スタックトレースやリフレクションで `MethodInfo` から型を引く所への影響を調べる必要がある。安定性は「実行するまで分からない」の段。
- **配列のインターフェースメソッドを共有または遅延にする（VM）。** `T[]` のクラスを作ると、`IList<T>` などのメソッド 15 個が要素型ごとに作られる。参照型の配列は中身の扱いが同じなので、上と同じ考えで共有できる可能性がある。
- **インターフェースのクラスを、キャストかディスパッチで要る時まで作らない（VM）。** いまはクラスを作る時点で、実装する全インターフェースのクラスが作られる。
- **BCL の `List<T>` をビルド前に作り替える（IL）。** 参照型の要素を `object[]` に持たせて配列のクラスを作らせない、非ジェネリックの `IList`／`ICollection` の実装を非ジェネリックの基底クラスに寄せてスロットを型ごとに持たない、など。型の同一性とシリアライズは保てるが、BCL の実装を手で書き直すことになる。
- **参照型の `List<T>` を VM の中で 1 つのクラスとして扱う。** 型ごとの消費はほぼ無くなるが、型の同一性が無くなる。Unity のシリアライズは要素の型をクラスから取り、`is`／`typeof` も区別できなくなるので、そのままでは使えない。

## どこに挟むか

- **IL の加工**は `IPostBuildPlayerScriptDLLs`（`GenericMemoryPreLink.cs`）。命令を挿入したメソッドは、書き出す前に分岐を長い形式に直してから詰め直す（Cecil は届かなくなった短い分岐をそのまま書く）。
- **VM の遅延化を iOS のビルドに入れる**のは `Il2CppLazyMetadata.cs`（`IPostprocessBuildWithReport`）。iOS のビルドは libil2cpp のソースを Xcode プロジェクト（`Il2CppOutputProject/IL2CPP`）に書き出し、Xcode がそれをコンパイルするので、Unity がプロジェクトを書き出した後にそこのソースを書き換える。Editor のインストール先にも、ゲームの C# と IL にも触れない。置き換える元の文字列が見つからなければビルドを失敗させる。環境変数 `IL2CPP_LAZY_METADATA=0` で外せる。**iOS のビルドそのものは実行していない。** 確かめたのは、この Editor の libil2cpp の複製に当てた結果が、計測に使ったパッチと 6 ファイルとも一致すること、二重に当てても変わらないこと（`Il2CppLazyMetadataCheck.Run`）。libil2cpp の場所は `Il2CppOutputProject/IL2CPP` を想定し、無ければ出力フォルダから `libil2cpp/vm/Class.cpp` を探す。
- **計測用の Windows のビルド**では、il2cpp フォルダの複製に当てて `UNITY_IL2CPP_PATH` で差し替えた。6000.3 では、この変数を設定すると開発用の配置（`<tool>/bin/<x>/<y>/<tool>.exe`、`<lib>/bin/<x>/<tfm>/`、`build/tests/BCLExtensions/net471`）を要求されるので、複製の中で `build/deploy` へのジャンクションを作って合わせた。変数は Player のビルドの間だけ設定する。Editor に残すと、Editor 自身のスクリプトコンパイルが同じ場所を見て止まる（`GenericMemoryBuild.cs` が `finally` で外す）。
- iOS の Xcode プロジェクトと Android のエクスポートしたプロジェクトには libil2cpp のソースが入るので、そこへ当てる方法もあるはずだが、試していない。

## 試していないこと、注意

- **Windows Standalone（x64）でしか測っていない。** Android と iOS では動かしていない。
- **測ったのはメタデータのプールへの要求量。** ハッシュ表とプールの端数は表に入れていない（`results/` の `heap` に残してある）。GC ヒープ、コードサイズ、速度は比べていない。
- **VM のパッチは速度への影響を測っていない。** RGCTX を読むたびの比較 1 回と、初回の解決でロックを取る分が増える。複数スレッドから同時に初めて読む場合の負荷試験、Development Build、マネージドデバッガの接続、コード生成を「サイズ優先」にした構成は試していない。
- **シナリオは小さく、正常に終わる経路しか通していない。** async の例外とキャンセル、`AwaitOnCompleted`（Unsafe でない方）、別スレッドからの再開は実行していない。
- **IL の加工は Player のビルドにだけ入る。** Editor とは別の IL で動くことになる。移動したメソッドのデバッグ情報（行番号）は捨てている。
- **リフレクションやシリアライザがジェネリッククラスのメンバーを名前で探すコード**に対して、メソッドの移動が何を壊すかは確かめていない（シナリオには無い）。
- **既存ビルドの静的な集計は上限の目安**で、実行時に初期化されるインスタンスだけが払う。型の名前が同じ入れ子の型は区別できていない。

## 動かし方

```sh
# シナリオのコードを書く（型の数は引数で変えられる）
python3 tools/il2cpp-generic-memory/generate.py 200 40

# il2cpp フォルダを複製してカウンタと 2 つのパッチを当てる（配置合わせのジャンクションは README の「どこに挟むか」）
cp -r "<Editor>/Data/il2cpp" tools/il2cpp-generic-memory/.local/il2cpp
python3 tools/il2cpp-generic-memory/libil2cpp/apply-counters.py tools/il2cpp-generic-memory/.local/il2cpp
unity command eval 'GenericMemory.Editor.Il2CppLazyMetadata.Apply("<複製した il2cpp フォルダ>", true); return "ok";'   # true: 実行時に環境変数で切り替える形

# 開いている Editor でビルドする。1 つ目の引数が加工（baseline、hoist、hoist+async、hoist+canon+async）
unity command eval 'return GenericMemory.Editor.GenericMemoryBuild.Run("hoist+async", "<複製した il2cpp フォルダ>");'

# 全パターンを 2 つの数で実行して表を出す。VM の遅延化は環境変数で切り替える
sh tools/il2cpp-generic-memory/measure.sh hoist+async hoist-async
GENERIC_MEMORY_LAZY_METHODS=1 GENERIC_MEMORY_LAZY_RGCTX=1 sh tools/il2cpp-generic-memory/measure.sh hoist+async hoist-async-vm
python3 tools/il2cpp-generic-memory/summary.py tools/il2cpp-generic-memory/results baseline hoist-async hoist-async-vm
python3 tools/il2cpp-generic-memory/report.py tools/il2cpp-generic-memory/results/hoist-async tools/il2cpp-generic-memory/results/baseline --sites ListNew
```

Player は `Logs/UnityTests/GenericMemory/<加工>/` に出る。ビルドは Standalone の ScriptingBackend、API レベル、Stripping、IL2CPP のコード生成の設定を書き換える。

## ファイル

| 場所 | 中身 |
|---|---|
| `Assets/Tests/GenericMemory/Scenario` | 計測用のアセンブリ。`Runner.cs` が 1 パターンを実行して差を書き出す。`Generated/` は `generate.py` が書く（Git には入れない）。このアセンブリと `Generated/` のコードは `GENERIC_MEMORY_SCENARIO` を定義したビルドでだけコンパイルされ、`GenericMemoryBuild` がそれを定義する。Editor とほかの Player には入らない |
| `Assets/Tests/GenericMemory/Scenario/Slim.cs` | 軽い専用リスト（上限を測るための手書き） |
| `Assets/Tests/GenericMemory/Editor/GenericMemoryWeaver.cs` | ビルドの全 DLL を 1 つのプログラムとして読み書きする土台と、メソッドの移動 |
| `Assets/Tests/GenericMemory/Editor/Canonicalizer.cs` | 型引数の寄せ |
| `Assets/Tests/GenericMemory/Editor/AsyncEraser.cs` | UniTask のステートマシンの寄せ |
| `Assets/Tests/GenericMemory/Editor/GenericMemoryBuild.cs`、`GenericMemoryPreLink.cs` | ビルドの入口と、加工を挟む所 |
| `Assets/Tests/GenericMemory/Editor/Il2CppLazyMetadata.cs` | VM の遅延化 2 つ。iOS のビルド後に Xcode プロジェクト内の libil2cpp へ当てるフックと、適用の本体。「この文字列をこれに置き換える」の並びで、元の文字列が見つからなければ止まる |
| `Assets/Tests/GenericMemory/Editor/Il2CppLazyMetadataCheck.cs` | ビルドせずに、パッチがこの Editor の libil2cpp に当たるかを確かめる |
| `libil2cpp/` | 計測用のカウンタ（`apply-counters.py`、`GenericMemoryCounters.h`） |
| `Census/` | リンク後の DLL と il2cpp の出力から、型ごとのメソッド数とインスタンス数を数える。`--il` でメソッドの IL を出す |
| `results/` | 計測の生の結果（条件ごとのフォルダ）。Git には入れていないので、スクリプトを実行すると作られる。`final-*` は RGCTX のパッチと検証コードを直した後の測り直しで、以前の値と全パターンで一致した。`erased`／`erased-vm` は要素の型を寄せた形と軽い専用リスト |
