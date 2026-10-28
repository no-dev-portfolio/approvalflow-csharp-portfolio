# C#業務Web案件の要件調査

調査日: 2026-10-28（日本時間）

## 方法と限界

専用Deep Research実行機能が利用できない環境のため、Web検索から募集元の個別案件ページとMicrosoft公式資料を確認しました。検索結果の集約サイトだけで結論を出さず、取得できた募集元本文の必須・歓迎を区別しています。公開ページの便宜的な標本であり、市場全体の頻度や採用確率を推定する統計調査ではありません。掲載は継続募集中であることを保証しません。年数要件と制作物で示す技術は別です。

## 確認した募集元

| 募集元・個別案件 | 必須・必要スキルの要約 | 歓迎・業務内容の要約 | 作品への反映 |
|---|---|---|---|
| [PE-BANK: 業務システム追加開発・改修](https://pe-bank.jp/project/csharp/51481-38/) | C#3年以上、.NET実務、SQL、設計〜試験 | ASP.NET、AWS/Azure、リーダー経験は尚可 | C#、SQL Server、設計書と試験。実務3年やリーダー経験の代替とはしない |
| [Midworks: 業務システム向けC# Web開発](https://mid-works.com/projects/36897) | 必須欄はC# | ASP.NET Core / T-SQL / SQL Serverは歓迎。業務にはDB設計・ストアド作成等を記載 | ASP.NET CoreとSQL Serverを採用。ストアドとDataSpiderは未実装 |
| [PE-BANK: C#/.NET移行対応支援](https://pe-bank.jp/project/dotnet/52889-N08/) | C#/.NET実務、SQL Server/T-SQL、Git、Azure基礎、Isolated Worker、Azure Pipelines | Azure Functionsの実行モデル移行 | SQLとGit向け構成を示す。Functions移行・Pipelines経験は対象外 |

補助調査として [レバテッククリエイター: 医療管理システム保守](https://creator.levtech.jp/project/detail/36483/) の検索索引にはC#/ASP.NET/MSSQLを必須、ASP.NET Coreを歓迎とする情報がありましたが、本文の直接取得に失敗したため上表の検証済み標本には含めていません。

追加で本文を確認した募集終了案件として、[レバテッククリエイター: メディア向けWebアプリ開発](https://creator.levtech.jp/project/detail/39639/) があります。求めるスキルにC#3年以上（LINQ・非同期処理等）、JavaScript、MVC開発を挙げ、ASP.NET Core Web API・React・Dockerは歓迎としています。旧バージョンの記載がある終了案件なので現在のバージョン選定の根拠には使用せず、言語・Webの基礎能力を確認する補助資料にしました。本作はLINQ・async/await・JavaScriptを実装しますが、MVCフレームワーク経験と同一とは扱いません。

[レバテック: 既存システム再構築PL](https://freelance.levtech.jp/project/detail/575072/) は募集終了表示を確認しました。検索索引にはASP.NET Core・SQL・長期の実務経験がありましたが、取得本文からスキル欄を確認できなかったため要件集計には含めません。

## 判断

業務Webを狙う今回の作品では、C#だけの小規模ツールより、DB設計から認証・認可・状態管理・試験まで説明できるASP.NET Coreアプリが適しています。これは募集の業務内容とユーザーの希望に基づく設計判断です。特定フレームワークやクラウドが全案件で必須という結論ではありません。

.NET Framework既存資産の改修案件もありますが、本作は新規Web向けの.NET 10 LTSを採用しています。旧Frameworkの移行経験を示すものではありません。[Microsoftサポートポリシー](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)

## 技術の対応表

| 対象能力 | 実装証拠 | 検証・説明 |
|---|---|---|
| C# / ASP.NET Core HTTP処理 | src/ApprovalFlow/Program.cs | 認証、JSON API、DI、非同期処理 |
| RDB設計 / SQL Server | Models.cs、database/schema.sql | FK、金額・状態のCHECK、複合インデックス |
| EF Core / LINQ | Models.cs、Workflow.cs、Program.cs | パラメーター化、検索・ページング、非同期I/O |
| 業務ルール | Workflow.cs | 所有者、承認者、差戻し、状態遷移 |
| 整合性 | Workflow.cs | バージョンと状態付きUPDATE、トランザクション |
| セキュリティ | Program.cs / app.js | Cookie認証、CSRF、権限制限、textContentによる表示 |
| 品質・保守 | tests/、.github/workflows/ci.yml | 実SQL Server統合テスト、警告をエラー扱い、整形・監査 |
| 説明能力 | DESIGN.md / TESTING.md | 設計意図、制約、再現手順 |
| Azure | OPERATIONS.md | 検討手順のみ。デプロイ・本番運用実績はない |

実務年数、顧客折衝、チーム開発、リーダー経験、既存システム移行、Azure運用は、この作品を作成しても充足したとは扱いません。応募時は業務経歴と自主制作を分けて記載してください。

## 設計に参照した公式資料

- [EF Coreの競合制御](https://learn.microsoft.com/en-us/ef/core/saving/concurrency): バージョンを条件にした更新。本作のExecuteUpdateでは自動判定に依存せずWHEREを明示。
- [ASP.NET CoreのCSRF対策](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0): Cookie認証の更新操作にCSRF検証。
- [SQL Serverコンテナの条件](https://learn.microsoft.com/en-us/sql/linux/install-upgrade/quickstart-install-docker?view=sql-server-ver17): x86-64が公式対応。ARMエミュレーション成功を正式サポートと混同しない。
- [Pkcsのセキュリティ勧告](https://github.com/advisories/GHSA-846f-f764-76mx): 依存パッケージ監査で検出した古い推移依存を更新。
