# 本家への追従の手順

OpenTelemetry の更新に、月1回程度、手元で追従する。CI では行わない。Claude に「`Telemetry/Tools~/UPSTREAM.md` の手順で確認して」と頼めば、この手順で進める。定期的に自動で頼む設定は置いていない。

## 要るもの

- git、.NET 10 の SDK、Docker
- Windows では Docker Desktop の `resources/bin` を `PATH` に入れる

以下の `dexc-telemetry` は `dotnet run --project Telemetry/Tools~/Cli -c Release --` のことである。

## 手順

1. 版を比べる

   ```sh
   dexc-telemetry upstream check
   ```

   `upstream.json` の版と、各リポジトリの最新のタグを並べる。新しいものにはリリースノートのURLを付ける。どれかが古ければ終了コードは3である。

2. 新しい版のリリースノートを読む。見るところは次のとおり

   | リポジトリ | 見るところ |
   |---|---|
   | opentelemetry-proto | 書いているメッセージの欄の追加、変更、非推奨。`OtlpFields.cs` に関わる |
   | opentelemetry-specification | OTLP（再送の番号、上限、圧縮）、SDK の環境変数の既定値、データモデル。仕様書の「書き出しと受け渡し」の数値に関わる |
   | semantic-conventions | 使っている属性の名前と安定度：`service.*`、`telemetry.sdk.name`、`exception.*`、`thread.id`、`device.*` |
   | opentelemetry-dotnet | 性能の比較を測り直す必要があるか |
   | opentelemetry-collector-contrib | OTLP の受け口、`confighttp`、`fileexporter` の既定値と、上限を超えた要求への応答（0.164.0 は 413 でなく 400 を返す） |

3. 新しい版で確かめる

   ```sh
   dexc-telemetry upstream verify --proto <新しいprotoのタグ> --collector <新しいCollectorのタグ>
   ```

   - `OtlpFields.cs` の定数を、`otlp-fields.json` の対応で、そのタグの `.proto` の欄の番号と比べる。合わない定数、`.proto` に無い欄、対応の無い定数を表示する
   - `Fixtures` がパッケージの書き出しのコードで固定のフレームを書き、そのタグの Collector に送る。span 2つ、ログ4件、指標3つを読めれば通る
   - 置いてある `.proto` と違うファイルも表示する

4. 通れば更新する

   - `upstream.json` の版を書き換える
   - `.proto` が変わったなら、`%TEMP%/dexc-telemetry-proto-<タグ>/opentelemetry/proto/` から `proto/opentelemetry/proto/` の同じファイルへ写す
   - `dexc-telemetry upstream verify` を引数無しでもう一度通す
   - 1つのコミットにする

5. 通らなければ、何が違うかを書き残し、`OtlpFields.cs`、書き出しのコード、仕様書のどこを変えるかを決めてから直す
