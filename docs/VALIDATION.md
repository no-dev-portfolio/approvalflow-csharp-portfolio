# v1.0.1 検証結果・一次再審査

実施日: 2026-10-28（日本時間）。Codexによるセルフレビューと実行検証です。受領したv1.0.0の反証報告に対して改修しました。独立した人間の監査・第三者認証ではありません。

## 判定

自主制作・ローカルデモの範囲で一次再審査を通過。確認した範囲で未解消のP0/P1/P2はありません。CR-01、CR-07、CR-08の問題を実行で確認して改修。P3の試験・証跡・表現・設定も改善しました。設計上の制限はOPERATIONS.mdに明記しています。

## 最新の正式検証

実行ID: `20261028T035746Z-43798524`。生ログは [該当ディレクトリー](../evidence/runs/20261028T035746Z-43798524/run.json) から辿れます。時刻はUTCを明示し、各ステップに開始・終了・終了コードを記録しています。

| 項目 | 実測 | 証跡（同じ実行IDのディレクトリー内） |
|---|---|---|
| 配布Dockerイメージのビルド・実HTTP | 成功、DB接続ready、時刻Z付き、不正JSON400 | evidence/image-build.log / image.json / after/image-http.json |
| 復元・Releaseビルド・SQL Server統合試験 | 40成功、0失敗、0スキップ | integration.stdout.log / integration-20261028T035746Z-43798524.trx |
| ソース整形検査 | 終了0、詳細ログあり | format-source.stdout.log / meta.json |
| 試験整形検査 | 終了0、詳細ログあり | format-tests.stdout.log / meta.json |
| JavaScript画面処理 | 4成功、0失敗 | frontend.stdout.log / meta.json |
| 推移依存を含む公開脆弱性監査 | 検出0、監査ゲート終了0 | dependency-audit.json / audit-gate.stdout.log |
| 試験DB残存 | 当該実行IDで0件、確認SQL終了0 | database-cleanup.stdout.log / meta.json（SQL本文あり） |

残存0件は正常終了後のSQL観測です。強制停止など異常終了時の削除を保証する主張ではありません。TRXを実行ごとの名前にし、別の実行で上書きしていません。

## 反例の再現と解消

- SQL Server datetime2読出しでKindがUnspecifiedになることを、修正前の回帰試験で確認。旧APIの時刻は `2026-10-28T03:19:06.7988962`、修正後は `2026-10-28T03:19:06.7988962Z`。既存データを変更せず、同じ履歴の画面が03:19から正しいJSTの12:19に変わりました。before/http.json、after/http.json、docs/screenshots/approved.pngに保存。
- UTC 15:30を日本時間で翌日00:30と表示する試験を追加。詳細・履歴・一覧のZ付与も確認。
- セッション時計を31分進める試験で401→旧CSRFで400→匿名CSRFを再取得して200を確認。実際のapp.jsを使うNode試験で、401後とログイン直前の取得を確認。ブラウザーで30分待つ試験ではありません。
- Developmentの不正JSONは旧Kestrel実行で500、修正後のKestrel実行で400。小数・型違い・空本文・不正Version・Content-Typeの異常も統合試験に追加。Version欠落・空GUIDは400。
- 新規作成・編集・申請・承認・差戻しで、監査INSERTをトリガーで失敗させて原子性を確認。
- 作成と編集のロール違反は403、状態遷移のロール違反は409と区別して確認。
- Productionの認証CookieとCSRF CookieのSecure設定を確認。
- 既定のログイン制限は実HTTPで401が10回、11回目429。日時・対象URL付きのafter/rate-limit.jsonに保存。

修正前のC#回帰16件は9失敗・7成功、フロントエンド4件は3失敗・1成功。新たにProduction設定の1件を加え、修正後は既存23件と合わせてC#40件です。beforeのREADMEに記録の来歴と制限を明記しました。

## 環境・未実施

.NET SDK 10.0.403 / Runtime 10.0.14、EF Core 10.0.14、SQL Server 2022 Developer 16.0.4305.5。Docker上のARM64 SDKとamd64 SQL Serverエミュレーションを利用。Microsoftがエミュレーションを正式にサポートするという意味ではありません。

GitHub Actionsのタグ存在は公式リポジトリーで確認しaction-refs.jsonに記録しましたが、GitHub Actionsそのものは未実行。Azure実デプロイ、Windows/IIS、負荷・障害復旧、外部ペネトレーション、全ブラウザー・モバイル実機は未実施です。本番導入に必要な追加作業はOPERATIONS.mdを参照してください。
