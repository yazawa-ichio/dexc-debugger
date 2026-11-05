# ILRuntime

Bot と eval の実行基盤。元のソースにパッチを当てたものをここに置く。

| | |
| --- | --- |
| 取得元 | https://github.com/Ourpalm/ILRuntime |
| コミット | 482cb88c114d493f7acacf07e06f6b9e020b43f0 |
| ライセンス | MIT（`License.md`、`Mono.Cecil/LICENSE.txt`） |

取り込むのは `ILRuntime/` と `Mono.Cecil/` のソースだけで、`Mono.Cecil/symbols` は入れない。

## パッチ

`Patches~` に番号順で置く。

| パッチ | 内容 |
| --- | --- |
| `0001-private-fields.patch` | publicize した参照でコンパイルした Bot が、製品の private フィールドを読み書きできるようにする。 |
| `0002-attribute-type.patch` | アセンブリ名のない型名は IL2CPP で解決できず、null のまま属性の処理で落ちていた。 |
| `0003-unwrap-exceptions.patch` | Reflection 経由の呼び出しは TargetInvocationException に包むので、catch が本来の型に一致しなかった。 |
| `0004-nullable.patch` | 箱詰めした Nullable<T> は null か T になり、Reflection でメンバーを呼べない。 |
| `0005-lazy-jit-worker.patch` | AppDomain を作るたびにスレッドが増えないようにする。eval は呼び出しごとに AppDomain を作る。 |
| `0006-step-and-stack-limits.patch` | 無限ループと再帰の暴走で、ゲームを止めずに例外で打ち切る。解釈実行の呼び出しはネイティブの再帰になるので、スタックは残量で判定する。 |
| `0007-disable-pdb.patch` | デバッグ情報は読まないので Mono.Cecil/symbols を取り込んでいない。実行時に使わないパッチツールの参照だけを外す。 |
| `0008-nullable-constrained.patch` | `constrained` 呼び出しで Nullable<T> の値を受けられるようにする。`(n + 1).ToString()` が未実装のエラーになっていた。 |

## 更新の手順

1. 新しいソースで `ILRuntime/` と `Mono.Cecil/` を置き換える
2. このフォルダで、番号順に適用する

```sh
for patch in Patches~/*.patch; do git apply "$patch"; done
```

適用できないパッチがあれば、その箇所の変更を確認して作り直す。
