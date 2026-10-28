# 試験の再現（v1.0.1）

Docker / ComposeとPython 3を使用します。アプリの.NET SDK、SQL Server、フロントエンド試験のNodeはDocker内で動きます。

```sh
cp .env.example .env
# .envのデモ用パスワードを設定する
python3 scripts/verify.py
```

各実行を `evidence/runs/<UTC日時と識別子>/` に保存します。復元、ソース整形、テスト整形、SQL Server統合試験、依存一覧、脆弱性監査、フロントエンド試験、DB残存SQLについて、生ログ・開始終了時刻・終了コードを記録します。TRX名にも実行IDを付け、上書きしません。異常終了時は削除が行われない場合があります。残存0件はその実行の正常終了後にSQLで観測した結果です。

接続先は専用のデモSQL Serverに限定し、業務データを使わないでください。テストごとではなくFactoryごとにランダム名DBを作成し、DB作成・削除権限を使用します。`--env-file`、`--project-name`、`--nuget-cache`で既存のローカル環境とキャッシュを指定できます。検証スクリプトは停止を自動実行しません。必要に応じ `docker compose -p approvalflow-verification down` で停止します。

## 追加した回帰試験

- SQL Server読出しのUTC Kind、詳細・履歴・一覧のZ付きJSON、JSTの翌日表示
- セッションの時計を31分進め、旧CSRFが400、新規匿名CSRFで再ログインが200
- 実際のapp.jsをNodeのDOM・通信スタブ環境で動かし、401後の匿名トークン再取得、ログイン直前の取得、取得失敗時の送信抑止
- Developmentで不正JSON、金額型違い、小数、空本文、不正Version、Content-Type欠落・不適合を4xxで拒否
- 作成403・編集403と、状態遷移のロール違反409の区別
- 作成・編集・申請・承認・差戻しの履歴INSERTをDBトリガーで失敗させ、申請・状態・Version・履歴が変わらないこと
- Productionで認証Cookie・CSRF CookieがSecureになること

通常の統合試験は大量のログインを行うためIP制限値を1000に設定します。製品既定10回/分は別の実HTTP確認です。

```sh
# アプリを起動し、直前にログイン試行を行っていない状態で実行
python3 scripts/smoke-rate-limit.py
```

成功・失敗を含むIP単位の制限なので、試験後は通常のログインも1分間制限される場合があります。結果JSONには実行日時と対象URLを含みます。

CI定義の.NET検証もverify-dotnet.shを使用します。GitHubの実行成功は本検証に含みません。各Actionのタグ存在は公式リポジトリで確認しました。
