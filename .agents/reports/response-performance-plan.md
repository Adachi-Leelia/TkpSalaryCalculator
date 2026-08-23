# TkpSalaryCalculator レスポンス性能 改善調査・実装計画

- 作成日: 2026-08-23
- 目的: 実機計測・プロファイリングを行わず、現在のコードからユーザー操作後のレスポンス低下につながる可能性が高い箇所を特定し、実装順序を定める。
- 文書種別: 要件・仕様書ではなく、性能改善作業のための調査・実装計画。
- 今回の実施範囲: 調査と計画作成のみ。コード、テスト、DB、既存ドキュメントは変更していない。

## 事前確認

### 確認した仕様

`tkp-project-docs` skill の手順に従い、リポジトリルートを確定してから `docs/` を一覧化し、次を確認した。

- `docs/requirements.md`
- `docs/screen_specification.md`
- `docs/database_specification.md`
- `docs/setting_history_data_model.md`
- `docs/salary_calculation_specification.md`
- `docs/test_specification.md`
- `docs/ui_implementation_plan.md`
- `docs/default_setting.md`
- `docs/adr/0001-ui-framework.md`

本計画で固定する制約は次のとおり。

- Android 上でオフライン完結することを維持する。
- Onion Architecture と `UI → Application → Domain/Ports → Infrastructure` の依存方向を維持する。
- 勤務記録約 30 年、1 日最大 20 件、約 219,000 件の DATA-LARGE を想定する。加えて、締め日履歴を前月「1日締め」から当月「31日締め」へ変更した場合、給与期間は前月2日から当月31日までの最長61日となる。この集中ケースは最大 1,220 件（`61 × 20`）として扱う。
- 保存、日別/月別/給与期間表示、給与期間切替、集計の 2 秒以内という既存目標を変更しない。
- 月別の不変設定スナップショット、勤務開始日の暦月での設定選択、締め日履歴、祝日版の選択規則を変更しない。
- 給与計算結果を正本として永続化しない。計算規則、丸め、未計算判定、履歴整合性を変更しない。
- SQLite の各接続における `foreign_keys=ON`、`journal_mode=WAL`、`synchronous=FULL`、トランザクション境界、インポート時の全置換と画面状態リセットを維持する。
- 実測前に永続集計キャッシュを導入しない。並列化は共有データと順序・整合性が確認できる場合に限り検討する。

### Solution / Project 構成

- Solution: `TkpSalaryCalculator.sln`
- UI: `src/TkpSalaryCalculator.App`（.NET MAUI / Android、Page、ViewModel、Navigation、DI）
- Application: `src/TkpSalaryCalculator.Application`（UseCase、DTO、Ports）
- Domain: `src/TkpSalaryCalculator.Domain`（給与計算、給与期間計算、値オブジェクト、履歴モデル）
- Infrastructure: `src/TkpSalaryCalculator.Infrastructure`（SQLite、データ入出力）
- Tests: `tests/TkpSalaryCalculator.App.Tests`、`Application.Tests`、`Domain.Tests`、`Infrastructure.Tests`

`MauiProgram.RegisterInfrastructure/RegisterUseCases` は Repository と UseCase を Singleton、Page/ViewModel を原則 Transient としている。`AppShell.ConfigureMainTabs/CreateContent` はルートタブを `DataTemplate` で遅延生成し、生成後のルート Page を Shell が保持する構造である。詳細画面の Transient 生成自体は通常のナビゲーション単位であり、コードだけから全体遅延の主因とは判断できない。

## 1. 全体所見

アプリ全体に共通する性能リスクがある。特に疑わしいのは **Application の集計オーケストレーションと Repository / SQLite の境界** であり、次に **画面表示時の無条件再ロード** である。利用者から「各画面操作、画面遷移など全般が遅い」と報告されていることとも整合する。

最も強い根拠は `SalaryQueryUseCase` である。月・給与期間の勤務記録は一度範囲取得している一方、日ごとに設定スナップショットと祝日カレンダーを再取得し、カレンダーでは曜日シフトも日ごとに取得する。31 日のカレンダーなら、接続時 PRAGMA を除く読取 SQL だけでも通常経路の概算で `1 + 31 × (11 + 2 + 1) = 435` コマンドになり得る。これは実測値ではなくコード経路上の概算だが、明確に削減可能な N+1 型アクセスである。

さらに、各 Page の `OnAppearing` がほぼ一律に `LoadAsync` を呼ぶため、タブ切替や詳細画面からの復帰でも重い共通集計が再実行される。この組合せにより、特定画面だけでなく「画面遷移全般」が遅く感じられる可能性が高い。

一方、次は主要因として確認されなかった。

- 本体コードに `.Wait()`、`GetAwaiter().GetResult()`、`Task.Run` はない。
- `ServiceSettingsViewModel.LoadAsync` の `.Result` は `Task.WhenAll` 完了後の結果参照であり、同期ブロックではない。
- `ObservableCollection` は主に初期設定の限定された編集項目に使われ、日常の主要画面で大量レコードを 1 件ずつ追加する構造ではない。
- ルート Page は遅延生成後に保持されるため、タブ切替ごとの Page / ViewModel 再生成が明示的に起きる構造ではない。

優先すべき順は、(1) 給与集計の N+1 解消、(2) 日別・勤務入力の重複ロード統合、(3) 入力候補での全履歴走査と索引整合、(4) 変更有無に応じた画面再ロード制御、である。計算式や履歴仕様を変える最適化は対象外とする。

## 2. 主要操作と処理経路

以下は性能に関係する主要経路である。末尾の UI 更新は MAUI の Binding / `PropertyChanged` による。

| 操作 | 主な処理経路 | 主な性能論点 |
|---|---|---|
| アプリ起動 | `App.CreateWindow` → `StartupViewModel.StartAsync` → `AppStartupCoordinator.StartAsync` → `SqliteDatabase.InitializeAsync` → `InitialSetupUseCase.GetStateAsync` → `SqliteAppMetadataRepository` / `SqliteSettingSnapshotRepository` / `SqliteClosingRuleRepository` → `PayrollPeriodSettingsUseCase.FindPeriodAsync` → `AppRootNavigator.SetRootAsync` → `AppShell` → ルート Page `OnAppearing` | 現行 DB でも bootstrap の書込トランザクションと祝日再投入を試行。その直後にホーム集計を開始する。 |
| ホーム表示 | `HomePage.OnAppearing` → `HomeViewModel.LoadAsync/QueueSummaryRequestAsync` → `PayrollPeriodSettingsUseCase.FindPeriodAsync`（必要時）→ `SalaryQueryUseCase.GetPayrollPeriodAsync` → 締め日履歴、勤務範囲、日別設定/祝日、手当 → `SalaryCalculator` → `HomeViewModel.ApplySummary` → UI | 日別設定/祝日の N+1。再表示ごとの再集計。 |
| ルート画面遷移 | `AppShell` のタブ → 遅延生成済み `HomePage` / `CalendarPage` / `SettingsMenuPage` → 各 `OnAppearing` → `LoadAsync` | Page 再生成より、遷移先で毎回発生するロードが共通リスク。 |
| 詳細画面遷移 | `Shell*Navigator` → `Shell.GoToAsync` → DI で Transient Page/ViewModel 生成 → QueryProperty 適用 → `OnAppearing` → `LoadAsync` | 生成自体より、遷移完了時の DB ロード・集計・UI 構築が支配的になり得る。 |
| カレンダー表示 / 月切替 | `CalendarPage.OnAppearing` / `CalendarViewModel.MoveMonthAsync` → `CalendarViewModel.LoadMonthCoreAsync` → `SalaryQueryUseCase.GetCalendarMonthAsync` → 月範囲勤務 → 日ごとの設定/祝日/曜日シフト → `GetDayAsync` で選択日を再取得 → `BuildCells` → UI | 空日を含む日単位 N+1、曜日シフト 31 回、選択日の二重読込。 |
| 日付選択 | `CalendarViewModel.SelectDateCoreAsync` → `SalaryQueryUseCase.GetDayAsync` → 1 日勤務、設定、祝日、計算 → `ApplySelectedDate` → UI | 1 日単位としては妥当だが、月ロード直後だけは同じ日を重複取得。 |
| 日別詳細表示 | `DayPage.OnAppearing` → `DayViewModel.LoadCoreAsync` → `GetDayAsync` + `GetForDateAsync` + `GetInputOptionsAsync` + `BasicShiftUseCase.PreviewForDateAsync` →複数 Repository → `SalaryCalculator` → 行 ViewModel 配列 → UI | 同じ勤務日、設定、祝日、勤務記録を複数経路で重複取得。`Task.WhenAll` は重複を除去しない。 |
| 勤務入力表示 / 日付変更 | `WorkEditorPage.OnAppearing` → `WorkEditorViewModel.LoadCoreAsync` → `GetInputOptionsAsync` + `GetForDateAsync`（編集時）+ `HolidayCalendarRepository.GetAsync` → `PreviewCoreAsync` → `WorkRecordUseCase.PreviewAsync` → 設定/勤務/祝日/給与計算 → UI | 設定、既存勤務、祝日を再取得。入力候補作成で全履歴集計。 |
| 勤務保存 / 編集 | `WorkEditorViewModel.SaveAsync` → UI 側 `PreviewCoreAsync` → `WorkRecordUseCase.SaveAsync/SaveCoreAsync` → BEGIN IMMEDIATE → 設定再確認、既存行、再プレビュー、保存、変更時刻更新 → 戻る → 前画面 `OnAppearing` 再ロード | 保存前プレビューとトランザクション内検証の重複。整合性のためトランザクション内再検証は残す必要がある。 |
| 勤務削除 | `DayViewModel.DeleteRecordAsync` → `WorkRecordUseCase.DeleteAsync` → Repository / transaction → `DayViewModel.LoadCoreAsync` | 削除後は再表示が必要だが、現在の日別画面の複数重複ロードをすべて再実行。 |
| 給与期間切替 | `HomeViewModel.MoveByAsync/MoveToCurrentAsync` → `QueueSummaryRequestAsync` → `SalaryQueryUseCase.GetPayrollPeriodAsync` → UI | リクエスト順序制御はあるが、各期間で日別 N+1 を再実行。 |
| 給与計算結果詳細 | `CalculationDetailPage.OnAppearing` → `CalculationDetailViewModel.LoadCoreAsync` → `SalaryQueryUseCase.GetPayrollPeriodAsync` → 日/勤務/割増/件数加算 ViewModel を全構築 → `ScrollView` 内の多重 `BindableLayout` | DB N+1 に加え、締め日履歴変更を含む集中ケースでは最大 1,220 勤務と内訳を非仮想化 UI に一括生成。 |
| 設定メニュー | `SettingsMenuPage.OnAppearing` → `SettingsMenuViewModel.LoadAsync` → `SettingsMonthContext.RefreshAsync` → `MonthSettingsUseCase.GetAsync` → `SqliteSettingSnapshotRepository.GetEffectiveForMonthAsync` → UI | ヘッダーは年月だけで生成できるが、毎回設定スナップショット全体を読む。 |
| 設定一覧 / 編集 | 各 Settings Page `OnAppearing` → 各 ViewModel `LoadAsync` → `SettingsMonthContext.RefreshAsync` → `MonthSettingsUseCase` → snapshot Repository → UI。保存時は `PreviewReplacementCoreAsync` → 月勤務全件 → 変更前後計算 → 確認 → `CloneAndReplaceAsync` → 新スナップショット全行挿入 | 設定画面間で同一スナップショットを反復読込。プレビューは各勤務で日付用設定を再構築。保存はスナップショット全体を多数の個別 INSERT で複製。 |
| 月額手当一覧 | `MonthlyAllowancePage.OnAppearing` → `MonthlyAllowanceViewModel.LoadCoreAsync` → `SalaryQueryUseCase.GetPayrollPeriodAsync` + `PayrollPeriodSettingsUseCase.GetAllowancesAsync` → UI | 期間境界と手当だけの画面で給与全体を計算し、手当も二重取得。 |
| 基本シフト一覧 / 編集 | `BasicShiftViewModel.LoadCoreAsync` / Editor → `WorkRecordUseCase.GetInputOptionsAsync` → 設定、全プリセット、全履歴使用回数、最終勤務 → `BasicShiftUseCase` / shift Repository → UI | 名称・設定だけが必要な箇所でも全履歴ランキングを作成。 |
| 基本シフト反映 | Calendar/Day → `BasicShiftUseCase.PreviewForDateAsync` → 設定、曜日シフト、祝日、日勤務 → 確認 → `ApplyAsync` で同じ状態をトランザクション内再確認して保存 → 画面再ロード | 保存時再確認は必要。表示側の重複コンテキスト取得は統合余地あり。 |
| データ管理 | `DataManagementPage.OnAppearing` → `BackupReminderUseCase.GetStateAsync`。Export/Import → `DataTransferUseCase` → streaming JSON / SQLite → Import 後 `AppRootNavigator` で状態リセット | ストリーミング設計であり、日常的な画面遷移の主要因とは見なしにくい。Import 後の全リロードは仕様上必要。 |

## 3. 改善候補一覧

### PERF-01 給与集計の日単位 N+1 を範囲単位へ統合

- **分類:** A（コード上ほぼ明確に改善可能）
- **対象ファイル:** `src/TkpSalaryCalculator.Application/UseCases/SalaryQueryUseCase.cs`、`src/TkpSalaryCalculator.Infrastructure/Sqlite/SettingSnapshotRepository.cs`、`src/TkpSalaryCalculator.Infrastructure/Sqlite/Repositories.cs`
- **クラス:** `SalaryQueryUseCase`、`SqliteSettingSnapshotRepository`、`SqliteHolidayCalendarRepository`、`SqliteBasicShiftRepository`
- **メソッド:** `GetCalendarMonthAsync` (28-50)、`GetPayrollPeriodAsync` (63-90)、`CalculateDayAsync` (92-110)、`GetEffectiveForMonthAsync`、`LoadSnapshotAsync` (190-221)、`GetAsync` (612-636)、`GetForWeekdayAsync`
- **現在の処理:** 月または給与期間の勤務は範囲取得後に日別へ分けるが、各日で `CalculateDayAsync` を呼び、設定スナップショットと祝日カレンダーを毎回 DB から復元する。カレンダーは空日にも同じ処理を行い、曜日シフトを 31 日分取得する。
- **問題になり得る理由:** 日数に比例して複数テーブルのスナップショット復元と祝日読込が反復される。31 日カレンダーでは、接続 PRAGMA を除く読取 SQL の概算が通常経路で約 435 コマンドになり得る。給与期間も勤務のある日ごとに同じ復元を行う。
- **コード上の根拠:** `GetCalendarMonthAsync` 41-48 行のループ内で `CalculateDayAsync` と `GetForWeekdayAsync`。`CalculateDayAsync` 94-95 行で設定と祝日を取得。`LoadSnapshotAsync` 201-220 行と 392-502 行でヘッダー、サービス、区分、単価、割増本体と 3 子表、件数加算本体と子表を別 SQL で読む。祝日は `SqliteHolidayCalendarRepository.GetAsync` で存在確認と日付一覧の 2 SQL。
- **影響するユーザー操作:** ホーム、給与期間切替、カレンダー表示/月切替、給与計算詳細、月額手当一覧、これらの画面への遷移・復帰。
- **共通処理か:** はい。ホーム・カレンダー・計算詳細など主要読取画面の中心経路。
- **改善案:** Application に範囲集計用の読取コンテキストを導入し、(1) 勤務を 1 回、(2) 対象範囲の暦月ごとに有効スナップショットを 1 回、(3) 使用する祝日版ごとに 1 回、(4) 基本シフトは 7 曜日分を 1 回の有界読取、で取得する。カレンダーの空日は給与計算用設定・祝日を取得せずゼロ集計を作る。永続キャッシュは使わず、1 操作内だけで不変データを共有する。Domain の `SalaryCalculator` と設定選択規則は変更しない。
- **ユーザー体感への影響:** 大
- **確度:** 高
- **影響範囲:** アプリ全体 / 複数画面
- **修正コスト:** 中
- **修正リスク:** 中（暦月境界、祝日版、履歴選択を誤らないテストが必要）

### PERF-02 日別画面の重複読込を単一の画面用読取へ統合

- **分類:** A
- **対象ファイル:** `src/TkpSalaryCalculator.App/Presentation/Features/Calendar/DayViewModel.cs`、`src/TkpSalaryCalculator.Application/UseCases/SalaryQueryUseCase.cs`、`WorkRecordUseCase.cs`、`BasicShiftUseCase.cs`
- **クラス:** `DayViewModel`、`SalaryQueryUseCase`、`WorkRecordUseCase`、`BasicShiftUseCase`
- **メソッド:** `DayViewModel.LoadCoreAsync` (123-184)、`GetDayAsync`、`GetForDateAsync`、`GetInputOptionsAsync`、`PreviewForDateAsync` (73-81)
- **現在の処理:** `LoadCoreAsync` は 4 タスクを並行開始する。各タスクは同じ日の勤務、設定、祝日を独立に読み、日別給与、保存行、名称、基本シフト候補を作る。
- **問題になり得る理由:** `Task.WhenAll` で待ち時間を重ねても、同じ SQLite DB への重複 SQL、接続初期化、オブジェクト復元は残る。複数接続が同時に走るため端末によっては競合も起こり得る。
- **コード上の根拠:** `DayViewModel` 125-128 行で 4 経路を開始。`SalaryQueryUseCase.GetDayAsync` と `WorkRecordUseCase.GetForDateAsync` と `BasicShiftUseCase.PreviewForDateAsync` が同じ日を範囲取得し、`GetDayAsync`、`GetInputOptionsAsync`、`PreviewForDateAsync` が設定を別々に取得する。
- **影響するユーザー操作:** 日別詳細表示、削除後の再表示、基本シフト反映後の再表示、勤務編集から戻る操作。
- **共通処理か:** 日別画面固有だが、カレンダーから頻繁に到達する主要操作。
- **改善案:** `GetDayScreenAsync` 相当の Application 読取ユースケースを設け、1 日の勤務、設定、祝日、該当曜日シフトを一度ずつ取得して、給与・行表示・シフト候補を同じコンテキストから構築する。入力候補ランキングが不要な名称表示では `GetInputOptionsAsync` を呼ばない。
- **ユーザー体感への影響:** 大
- **確度:** 高
- **影響範囲:** 単一画面（高頻度）
- **修正コスト:** 中
- **修正リスク:** 中

### PERF-03 入力候補作成の全履歴走査と索引不整合を解消

- **分類:** B（性能影響が大きい可能性が高い）
- **対象ファイル:** `src/TkpSalaryCalculator.Application/UseCases/WorkRecordUseCase.cs`、`src/TkpSalaryCalculator.Infrastructure/Sqlite/Repositories.cs`、`src/TkpSalaryCalculator.Infrastructure/Sqlite/SqliteStorage.cs`、入力候補を呼ぶ各 ViewModel
- **クラス:** `WorkRecordUseCase`、`SqliteWorkRecordRepository`、`WorkEditorViewModel`、`DayViewModel`、`BasicShiftViewModel`、`BasicShiftEditorViewModel`、`CalendarViewModel`
- **メソッド:** `GetInputOptionsAsync` (36-61)、`FindMostRecentAsync` (286-290)、`GetServicePresetUsageCountsAsync` (292-307)、`WorkEditorViewModel.LoadCoreAsync/EnsureOptionsForSelectedDateAsync`、`BasicShiftViewModel.LoadCoreAsync`
- **現在の処理:** 入力候補を作るたび、設定、全プリセット、全勤務からのプリセット別使用回数、全勤務中の最終更新行を取得する。名称や設定だけ必要な日別・基本シフト画面も同じ重い API を使う。
- **問題になり得る理由:** DATA-LARGE 約 219,000 件に対し、使用回数は全件 `GROUP BY`、最新行は `ORDER BY updated_at_utc DESC, work_date DESC, id DESC LIMIT 1`。現 DDL には `work_date`、`(service_id, work_date)` 等はあるが、`source_service_preset_id` と最新行順序に合う索引がない。
- **コード上の根拠:** Repository 299-300 行の全履歴 GROUP BY、286-290 行の全履歴 ORDER BY。`SqliteStorage.cs` 769-793 行の索引一覧に該当索引がない。呼出しは `WorkEditorViewModel` 297/359 行、`DayViewModel` 127 行、`BasicShiftViewModels.cs` 97 行など複数画面に存在する。
- **影響するユーザー操作:** 勤務入力表示、勤務日変更、日別詳細、カレンダーのシフト確認、基本シフト一覧・編集。
- **共通処理か:** はい。複数画面で共有する入力・名称解決経路。
- **改善案:** (1) 設定/名称だけ、候補ランキング込み、という用途別の読取契約に分ける。(2) 候補ランキングが必要な画面だけ全履歴統計を読む。(3) 正確な全履歴使用回数という現仕様は維持したまま、候補索引を migration で追加する案を検証する。(4) Repository で同じ接続・有界読取にまとめる。
- **索引の採否ゲート:** `EXPLAIN QUERY PLAN` は候補クエリが実際に索引を使用することの確認に使うが、採否の根拠をそれだけにしない。DATA-LARGE と61日・1,220件集中ケースの両方で、索引あり/なしを同一 Release 構成・同一端末状態で A/B 測定し、(a) 使用回数 `GROUP BY`、(b) 最新行取得、(c) 勤務保存、更新、インポート、(d) DB 容量を記録する。読取の改善が書込・インポート時間または容量の許容できない悪化を伴わず、対象読取が実際に索引を使用するときだけ採用する。測定結果と採否理由を PR に残す。
- **ユーザー体感への影響:** 大
- **確度:** 高（影響量は実測が必要）
- **影響範囲:** 複数画面
- **修正コスト:** 中
- **修正リスク:** 中（DB migration と候補順序互換性）

### PERF-04 勤務入力の表示・プレビュー・保存における重複検証を整理

- **分類:** A
- **対象ファイル:** `src/TkpSalaryCalculator.App/Presentation/Features/Calendar/WorkEditorViewModel.cs`、`src/TkpSalaryCalculator.Application/UseCases/WorkRecordUseCase.cs`、`src/TkpSalaryCalculator.Application/Internal/ApplicationSupport.cs`
- **クラス:** `WorkEditorViewModel`、`WorkRecordUseCase`、`ApplicationSupport`
- **メソッド:** `LoadCoreAsync` (291-332)、`PreviewCoreAsync` (334-347)、`SaveAsync` (269-287)、`WorkRecordUseCase.PreviewAsync` (75-85)、`SaveCoreAsync` (110-135)、内部 `PreviewCoreAsync` (284-304)、`ApplicationSupport.CalculateAsync` (142-151)
- **現在の処理:** 画面表示で入力候補と対象日全勤務を取得し、祝日を読み、さらにプレビューが設定・既存勤務・祝日を再取得する。内部プレビューは 293 行で祝日を取得後、302 行の `CalculateAsync` でも同じ祝日版を再取得する。保存直前に UI がプレビューし、その後 `SaveCoreAsync` がトランザクション内で再度設定・既存行・プレビューを検証する。
- **問題になり得る理由:** 画面表示と保存という体感しやすい操作で、同一データの DB 往復が重なる。編集対象 1 件のために対象日の全件を取得している。安全に必要な保存時再検証と、直前の UI プレビューが混在している。
- **コード上の根拠:** 上記各行。特に `WorkRecordUseCase.PreviewCoreAsync` 293 行と `ApplicationSupport.CalculateAsync` 149 行は同一祝日版の明確な二重取得。
- **影響するユーザー操作:** 勤務入力画面表示、編集、日付変更、プレビュー、保存。
- **共通処理か:** 勤務操作全般の共通経路。
- **改善案:** 画面ロード用 DTO で対象 1 件、入力設定、祝日を一度で取得する。Application 内部計算は既に取得済みの `HolidayCalendar` を受け取れる形にし、同一処理内の再取得をなくす。保存時は UI の直前プレビュー結果を保存可否の正本にせず、`SaveAsync` のトランザクション内再検証を必ず残し、保存結果を UI に返す。保存ボタン押下時に追加の UI プレビューを先行させる必要性をテストで確認し、入力変更時に既にプレビュー済みなら重複呼出しを除く。
- **ユーザー体感への影響:** 大
- **確度:** 高
- **影響範囲:** 複数操作
- **修正コスト:** 中
- **修正リスク:** 中（冪等保存、古いプレビュー拒否、正規化結果を維持）

### PERF-05 `OnAppearing` の無条件ロードを変更通知ベースにする

- **分類:** B
- **対象ファイル:** `src/TkpSalaryCalculator.App/Presentation/Features/**/**Page.xaml.cs`、`HomeViewModel.cs`、`AppRootNavigation.cs`、各 Navigator / ViewModel
- **クラス:** `HomePage`、`CalendarPage`、`DayPage`、`CalculationDetailPage`、全 Settings Page、対応 ViewModel、`IAppSessionState`
- **メソッド:** 各 `OnAppearing`、各 `LoadAsync`。例: `HomePage.OnAppearing` 15-19、`CalendarPage.OnAppearing` 15-19、`HomeViewModel.LoadAsync` 269-273
- **現在の処理:** 主要 Page は表示のたびに `LoadAsync` を呼ぶ。Home のコメントも「画面を表示するたびに…最新状態を読み直す」と明示する。ルートタブは Page を保持するが、タブ復帰で集計は再実行される。
- **問題になり得る理由:** データ変更を伴わないタブ切替、詳細閲覧からの復帰でも DB 読取・給与集計・コレクション再構築が走る。PERF-01〜04 の重い経路を画面遷移のたびに増幅する。
- **コード上の根拠:** `rg` で、Home、Calendar、Day、WorkEditor、CalculationDetail、DataManagement、ほぼ全 Settings Page の `OnAppearing → LoadAsync` を確認。`AppShell.CreateContent` はルート Page を遅延生成するため、共通負荷は再生成ではなく再ロードにある。
- **影響するユーザー操作:** タブ切替、詳細画面への移動と復帰、設定画面間移動、OS 復帰を含む広範な画面表示。
- **共通処理か:** はい。最も横断的な UI 共通経路。
- **改善案:** 最初の表示時は必ずロードし、その後は「勤務・設定・手当・締め日・基本シフト・Import が変更された世代」をアプリ内で通知し、表示内容が依存する世代が変化したときだけ再ロードする。Import は仕様どおりルートと状態をリセットする。これは給与結果の永続キャッシュではなく、既存 Page 状態を再利用するための無効化制御である。手動再読込とエラー再試行は残す。先に PERF-01〜04 を実施し、誤った stale 表示を防ぐ契約を定めてから導入する。
- **ユーザー体感への影響:** 大
- **確度:** 中（実際の `OnAppearing` 発火頻度と遷移割合は実機確認が必要）
- **影響範囲:** アプリ全体
- **修正コスト:** 中
- **修正リスク:** 中〜大（更新漏れによる古い表示を防ぐ必要）

### PERF-06 カレンダー月ロード時の選択日二重読込を統合

- **分類:** A
- **対象ファイル:** `src/TkpSalaryCalculator.App/Presentation/Features/Calendar/CalendarViewModel.cs`、`src/TkpSalaryCalculator.Application/UseCases/SalaryQueryUseCase.cs`
- **クラス:** `CalendarViewModel`、`SalaryQueryUseCase`
- **メソッド:** `LoadMonthCoreAsync` (216-232)、`GetCalendarMonthAsync`、`GetDayAsync`
- **現在の処理:** 月全日を `GetCalendarMonthAsync` で集計した直後、選択日を `GetDayAsync` で再取得して上位 3 勤務を表示する。
- **問題になり得る理由:** 月集計の中で既に取得・計算した選択日の勤務と給与を破棄し、同じ日を再度 DB から読む。
- **コード上の根拠:** `CalendarViewModel` 221 行と 228 行の連続呼出し。`SalaryQueryUseCase.GetCalendarMonthAsync` は 35-48 行で対象日勤務と日別給与を一度持っている。
- **影響するユーザー操作:** カレンダー初期表示、月切替、基本シフト反映後の月再ロード。
- **共通処理か:** カレンダー固有。
- **改善案:** 月画面用読取結果に選択日の `DailySalaryDto` を含める、または月ロード API に選択日を渡し、同じ範囲読取コンテキストからセルと選択日詳細を返す。単独の日付選択時は `GetDayAsync` を維持する。
- **ユーザー体感への影響:** 中〜大
- **確度:** 高
- **影響範囲:** 単一画面
- **修正コスト:** 小〜中
- **修正リスク:** 小

### PERF-07 同一日付の計算用設定再構築を 1 回にする

- **分類:** A
- **対象ファイル:** `src/TkpSalaryCalculator.Application/UseCases/SalaryQueryUseCase.cs`、`MonthSettingsUseCase.cs`、`BasicShiftUseCase.cs`、`src/TkpSalaryCalculator.Application/Internal/ApplicationSupport.cs`
- **クラス:** `SalaryQueryUseCase`、`MonthSettingsUseCase`、`BasicShiftUseCase`、`ApplicationSupport`
- **メソッド:** `CalculateDayAsync` (92-110)、`PreviewReplacementCoreAsync` (150-193)、`BasicShiftUseCase.ApplyAsync` (113-120)、`ForCalculationDate` (153-160)
- **現在の処理:** `ForCalculationDate` は割増を日付条件で絞り、必要なら新しい `SettingSnapshot` を作る。日別集計では同じ日の各勤務ごと、設定置換プレビューでは変更前後を各勤務ごと、基本シフト反映では各シフトごとに呼ぶ。
- **問題になり得る理由:** 1 日最大 20 件に対して同一条件の割増絞込と不変スナップショットのコピー・検証を反復する。締め日履歴変更を含む61日・1,220件集中ケースの設定置換プレビューでは、変更前後を最大 1,220 件ずつ反復する。
- **コード上の根拠:** `SalaryQueryUseCase` 96-98 行、`MonthSettingsUseCase` 181-187 行、`BasicShiftUseCase` 113-120 行。`ApplicationSupport.ForCalculationDate` 155-159 行で LINQ と `SettingSnapshot` 生成。
- **影響するユーザー操作:** ホーム、カレンダー、日別詳細、給与詳細、設定変更プレビュー、基本シフト反映。
- **共通処理か:** はい。給与計算を横断する共通 CPU / allocation 経路。
- **改善案:** 日ごと・スナップショットごと・祝日版ごとに `ForCalculationDate` を一度だけ作り、その日の全勤務で共有する。設定置換は勤務を日付でグループ化し、変更前/後それぞれ 1 日 1 回作る。計算順、計算式、結果は変更しない。
- **ユーザー体感への影響:** 中
- **確度:** 高
- **影響範囲:** 複数画面
- **修正コスト:** 小
- **修正リスク:** 小

### PERF-08 設定メニューの不要なスナップショット全読込を除く

- **分類:** A
- **対象ファイル:** `src/TkpSalaryCalculator.App/Presentation/Features/Settings/SettingsMenuViewModel.cs`、`SettingsMonthContext.cs`
- **クラス:** `SettingsMenuViewModel`、`SettingsMonthContext`
- **メソッド:** `SettingsMenuViewModel.LoadAsync` (68-72)、`SettingsMonthContext.RefreshAsync` (31-36)
- **現在の処理:** 設定メニュー表示時に `context.RefreshAsync` で設定スナップショット全体を復元するが、その直後は年月ヘッダーの `PropertyChanged` しか行わない。ヘッダーは `SelectedMonth` だけから生成できる。
- **問題になり得る理由:** 設定タブを開くたび、複数 SQL から成るスナップショット全復元が不要に走る。子設定画面も自身の `OnAppearing` で改めて `RefreshAsync` する。
- **コード上の根拠:** `SettingsMenuViewModel` 68-72 行と `SettingsMonthContext.HeaderText` 21-23 行。`RefreshAsync` は `MonthSettingsUseCase.GetAsync` を呼ぶ。
- **影響するユーザー操作:** 設定タブ表示、ホーム/カレンダーから設定への遷移、設定メニューへの復帰。
- **共通処理か:** 設定画面群の入口に共通。
- **改善案:** メニューの初期表示は年月状態と成功メッセージだけ更新し、スナップショットは実際に内容を表示する子画面で読む。月移動時にメニュー自身が内容を必要としないなら DB 読取を伴わない月変更 API を分ける。子画面間の再利用は PERF-05 の明示的な変更世代と組み合わせ、更新漏れを防ぐ。
- **ユーザー体感への影響:** 中
- **確度:** 高
- **影響範囲:** 複数画面
- **修正コスト:** 小
- **修正リスク:** 小

### PERF-09 月額手当一覧から不要な給与全計算と手当二重取得を除く

- **分類:** A
- **対象ファイル:** `src/TkpSalaryCalculator.App/Presentation/Features/Settings/MonthlyAllowanceViewModels.cs`、Application の給与期間読取契約
- **クラス:** `MonthlyAllowanceViewModel`、`SalaryQueryUseCase`、`PayrollPeriodSettingsUseCase`
- **メソッド:** `MonthlyAllowanceViewModel.LoadCoreAsync` (113-129)、`SalaryQueryUseCase.GetPayrollPeriodAsync`
- **現在の処理:** 手当一覧は期間境界表示のため `GetPayrollPeriodAsync` で給与全体を計算し、同時に `GetAllowancesAsync` でも手当を取得する。`PayrollPeriodSummaryDto` 自身も手当一覧を含む。
- **問題になり得る理由:** この画面が表示するのは期間境界、手当一覧、手当合計であり、勤務給与の日別再計算は不要。手当 SELECT も二重になる。
- **コード上の根拠:** `MonthlyAllowanceViewModel` 117-123 行。`SalaryQueryUseCase.GetPayrollPeriodAsync` 84-89 行で手当を取得し DTO に含める。
- **影響するユーザー操作:** 月額手当一覧表示、期間切替、追加/編集/削除後の復帰。
- **共通処理か:** 単一設定画面。
- **改善案:** `PayrollPeriodSettingsUseCase.FindPeriodAsync` 相当で期間境界を求め、手当は 1 回だけ取得する画面用 DTO に変更する。給与全計算は行わない。締め日履歴による期間計算規則は既存 Domain をそのまま使用する。
- **ユーザー体感への影響:** 中〜大
- **確度:** 高
- **影響範囲:** 単一画面
- **修正コスト:** 小
- **修正リスク:** 小

### PERF-10 現行 DB 起動時の bootstrap 再書込を端末ローカル版管理する

- **分類:** A
- **対象ファイル:** `src/TkpSalaryCalculator.Infrastructure/Sqlite/SqliteStorage.cs`、`src/TkpSalaryCalculator.Infrastructure/DataTransfer/SqliteDataTransfer.cs`、`docs/database_specification.md`（必要に応じてエクスポート形式版規則も更新）
- **クラス:** `SqliteDatabase`
- **メソッド:** `InitializeAsync` (71-107)、`EnsureBootstrapAsync` (278-292)、`BootstrapVersionOneAsync` (294 以降)
- **現在の処理:** schema が既に Current でも毎プロセス起動時に `EnsureBootstrapAsync` が BEGIN IMMEDIATE を開始し、祝日衝突検証、カレンダー `INSERT OR IGNORE`、バンドル祝日の日付ごとの `INSERT OR IGNORE` を行ってから初期スナップショット有無を確認する。
- **問題になり得る理由:** 起動のクリティカルパスで、`synchronous=FULL` の書込トランザクションと複数 INSERT を毎回試行する。データが変わっていない通常起動には不要。
- **コード上の根拠:** `InitializeAsync` 97-100 行、`EnsureBootstrapAsync` 280-285 行、`BootstrapVersionOneAsync` 297-328 行。初期スナップショット確認は祝日 INSERT 群の後の 331-336 行。
- **影響するユーザー操作:** アプリ起動、起動直後のホーム到達。
- **共通処理か:** アプリ起動共通。
- **改善案:** バンドル bootstrap 版マーカーは、給与計算の再現に必要なエクスポートデータではなく、DB 内で保持する端末ローカル状態として扱い、`SqliteDataTransfer` の export/import 対象から除外する。マーカーは新規 DB と `bootstrapDefaults: false` の import candidate では必ず「未適用」で初期化する。candidate の検証・live DB 全置換コミット後、root reset より前に live DB に対して bootstrap を再実行し、未適用の同梱祝日版だけを衝突検証して投入し、成功した同一トランザクションで版マーカーを更新する。これにより、エクスポートが参照中スナップショットの祝日版だけを含む場合でも、古い端末ローカルの「適用済み」状態によって同梱祝日の再投入が省略されない。置換前 live DB は再投入成功まで復元可能な状態で保持し、再投入が失敗した場合は置換を復元して Import 全体を失敗扱いとし、root reset しない。同一プロセス内の Import 後も、DB の初期化状態・Session/画面キャッシュを破棄した上で再投入済み DB から root を再構築する。マーカー列、candidate の初期値、export 非対象、全置換コミット後の再投入と失敗時復元の順序を DB仕様書とデータ形式規則へ明記する。単なる `initial_snapshot_id` の早期 return だけでは祝日版更新を阻害するため採用しない。
- **ユーザー体感への影響:** 中
- **確度:** 高
- **影響範囲:** アプリ全体（起動時）
- **修正コスト:** 小〜中
- **修正リスク:** 中

### PERF-11 計算内訳の非仮想化された大規模 UI ツリーを縮小

- **分類:** B
- **対象ファイル:** `src/TkpSalaryCalculator.App/Presentation/Features/Home/CalculationDetailPage.xaml`、`CalculationDetailViewModel.cs`
- **クラス:** `CalculationDetailPage`、`CalculationDetailViewModel`
- **メソッド:** `LoadCoreAsync` (117 以降)、`CreateDay`、`CreateRecord` (180-213)
- **現在の処理:** 給与期間の全日・全勤務・各勤務の割増・件数加算を ViewModel 配列へ展開し、`ScrollView` 内の `VerticalStackLayout` と多重 `BindableLayout` で一括生成する。
- **問題になり得る理由:** 通常の月内期間は最大31日・620勤務だが、締め日履歴を前月「1日締め」から当月「31日締め」へ変更した61日集中ケースでは最大 1,220 勤務となる。内訳子要素を含めると数百〜数千以上の MAUI VisualElement を同時に生成・計測・配置する可能性がある。`BindableLayout` は一覧仮想化を提供しない。
- **コード上の根拠:** XAML 11 行の `ScrollView`、43 行以降および日/勤務/内訳にある複数 `BindableLayout.ItemsSource`。`CreateRecord` 198-213 行で子行配列も一括生成。
- **影響するユーザー操作:** 給与計算詳細表示、未計算日/勤務詳細表示。
- **共通処理か:** 単一画面だが大量データで目立つ。
- **改善案:** 仕様上の全内訳へ到達可能なまま、仮想化可能な `CollectionView` 用に表示行を平坦化する、または日単位の展開時に子 View を作る。最初に MAUI Android で要素数・レイアウト時間を測定し、テンプレート変更の効果を確認する。
- **ユーザー体感への影響:** 中〜大
- **確度:** 中
- **影響範囲:** 単一画面
- **修正コスト:** 大
- **修正リスク:** 中（表示内容、アクセシビリティ、スクロール位置）

### PERF-12 SQLite 接続ごとの PRAGMA と接続数を計測する

- **分類:** C（性能影響がある可能性がある）
- **対象ファイル:** 性能計測コード・テスト。`SqliteStorage.cs` の PRAGMA 契約を変更対象に含めない。
- **クラス:** `SqliteDatabase`
- **メソッド:** `OpenUninitializedConnectionAsync` (206-224)、`ReadAsync`、`WriteAsync`
- **現在の処理:** ambient transaction がない Repository 呼出しごとに接続を開き、`foreign_keys=ON`、`journal_mode=WAL`、`synchronous=FULL` の 3 PRAGMA を実行する。PERF-01/02 の N+1 により接続数も増える。
- **問題になり得る理由:** WAL モードは DB ファイルに持続する設定であり、毎接続の再設定は余分な SQL になり得る。ただし Microsoft.Data.Sqlite の pooling や Android ストレージでの実コストはコードだけでは評価できない。
- **コード上の根拠:** `SqliteStorage.cs` 211-217 行。各 Repository は通常 `database.ReadAsync` 単位で接続する。
- **影響するユーザー操作:** ほぼすべての DB 読取・保存。
- **共通処理か:** はい。Repository 共通。
- **改善案:** まず N+1 を解消して接続数を減らし、接続数、PRAGMA 実行数、wall-clock と lock/error を計測する。現行 DB 仕様どおり、すべての接続で `foreign_keys=ON`、`journal_mode=WAL`、`synchronous=FULL` を実行・維持する。WAL を初期化／マイグレーション時だけに移す変更、`synchronous` の低下、長寿命 singleton 接続は本計画の実装対象にしない。将来これらを検討する場合は、DB仕様の明示的な変更を別 PR でレビュー・承認してから、新たな性能施策として計画する。
- **ユーザー体感への影響:** 中の可能性
- **確度:** 低〜中
- **影響範囲:** アプリ全体
- **修正コスト:** 中
- **修正リスク:** 中

### PERF-13 設定スナップショット保存の個別 INSERT 群を効率化

- **分類:** B
- **対象ファイル:** `src/TkpSalaryCalculator.Infrastructure/Sqlite/SettingSnapshotRepository.cs`、`src/TkpSalaryCalculator.Application/UseCases/MonthSettingsUseCase.cs`
- **クラス:** `SqliteSettingSnapshotRepository`、`MonthSettingsUseCase`
- **メソッド:** `InsertSnapshotAsync` (224-349)、`CloneAndReplaceAsync`、`PreviewReplacementCoreAsync`、`ValidateConfirmationAsync`
- **現在の処理:** 設定 1 項目の変更でも仕様どおりスナップショット全体を複製する。保存時はサービス、時間区分、単価、割増と各子条件、件数加算と対象サービスを 1 行ずつコマンド生成・実行する。プレビューと確定時には月勤務全件と現設定も再読込する。
- **問題になり得る理由:** 設定項目数に比例してコマンド作成と SQLite 往復が増える。プレビュー後の再検証は整合性上必要だが、BEGIN IMMEDIATE 内で同じ状態を複数回復元している箇所は整理余地がある。
- **コード上の根拠:** `InsertSnapshotAsync` 241-348 行の複数 foreach と逐次 `ExecuteAsync/ExecuteChildAsync`。`MonthSettingsUseCase` 201-205 行で確定時に現設定・前月設定・勤務を再取得する。
- **影響するユーザー操作:** サービス、時間区分、単価、割増、件数加算、前月コピーの保存。
- **共通処理か:** 設定保存の共通経路。
- **改善案:** トランザクションを維持しつつ、SQL/parameter を再利用する prepared command、同種行のバッチ挿入、定義行の `INSERT OR IGNORE` 集約を検討する。確認トークンの stale 検出は維持し、確定トランザクション内で一度取得した現状態を Repository 操作間で共有できる契約を設計する。スナップショット不変性と全体複製の仕様は変更しない。
- **ユーザー体感への影響:** 中
- **確度:** 中
- **影響範囲:** 複数設定画面
- **修正コスト:** 中
- **修正リスク:** 中〜大（履歴の不変性、FK、原子性）

### PERF-14 Page / ViewModel の Transient 生成と DI グラフ

- **分類:** D（実測しなければ判断困難）
- **対象ファイル:** `src/TkpSalaryCalculator.App/MauiProgram.cs`、`AppShell.xaml.cs`、各 Navigation 登録
- **クラス:** `MauiProgram`、`AppShell`、各 Page/ViewModel
- **メソッド:** `RegisterPresentation` (93-158)、`ConfigureMainTabs/CreateContent` (45-80)
- **現在の処理:** ルート Page/ViewModel と詳細 Page/ViewModel は DI 上 Transient。ルートは `DataTemplate` で遅延生成され、詳細はナビゲーションごとに生成される。
- **問題になり得る理由:** XAML 読込や大きなコンストラクタが遅ければ遷移に影響し得るが、現コードのコンストラクタは主に依存保持と Command 作成であり、DB I/O は `LoadAsync` 側にある。
- **コード上の根拠:** `MauiProgram` 120-157 行の Transient 登録と `AppShell` 74-80 行の DataTemplate。コンストラクタ内の同期 DB I/O は確認されない。
- **影響するユーザー操作:** 初回タブ表示、詳細画面遷移。
- **共通処理か:** Navigation / DI 共通。
- **改善案:** 現時点では lifetime を変更しない。実測で Page construction / XAML inflate が支配的と判明した場合のみ、ページごとの生成時間と保持コスト、古い QueryProperty 状態の危険を比較する。
- **ユーザー体感への影響:** 不明
- **確度:** 低
- **影響範囲:** アプリ全体の可能性
- **修正コスト:** 中
- **修正リスク:** 大（状態漏れ、メモリ保持、ナビゲーション意味変更）

## 4. 対応優先順位

追加情報として、利用者の報告は特定処理ではなく各画面操作・画面遷移全般に及ぶ。このため、共通経路への寄与を優先順位へ強く反映する。

### 1. 最優先

1. **PERF-01 給与集計の日単位 N+1** — 影響大、確度高、ホーム・カレンダー・詳細に共通。
2. **PERF-03 入力候補の全履歴走査と索引不整合** — DATA-LARGE で増幅し、勤務・日別・基本シフトに共通。
3. **PERF-02 日別画面の重複読込** — 同じ日/設定/祝日を 4 経路で読む明確な重複。
4. **PERF-04 勤務入力の重複読込・再プレビュー** — 表示・保存という高頻度操作に直結。
5. **PERF-05 無条件 `OnAppearing` ロード** — 全画面遷移へ波及。ただし先に重いロード自体を軽くし、更新通知契約を固める。

### 2. 優先

1. **PERF-07 同一日付の計算用設定再構築** — 低コストで集計全般の allocation を減らせる。
2. **PERF-06 カレンダー選択日の二重読込** — 明確で、月切替の体感へ直結。
3. **PERF-08 設定メニューの不要スナップショット読込** — 設定タブ遷移の共通負荷を小コストで削減。
4. **PERF-09 月額手当一覧の不要な給与全計算** — 明確な過剰処理を小コストで除去。
5. **PERF-10 起動時 bootstrap 再書込** — 起動経路の明確な不要処理。ただし export 非対象の端末ローカル版マーカーと Import 後再投入を正しく設計する必要がある。

### 3. 余裕があれば

1. **PERF-11 計算内訳 UI の仮想化** — 単一画面で修正規模が大きく、まず実測が必要。
2. **PERF-13 設定スナップショット INSERT 効率化** — 設定保存に限定され、履歴整合性リスクが高め。
3. **PERF-12 接続ごとの PRAGMA 計測** — N+1 解消後の残余コストを測るだけとし、仕様変更は別レビューとする。

### 4. 現時点では対応しない

1. **PERF-14 DI lifetime / Page 再利用の変更** — 主因のコード根拠が弱く、状態漏れリスクが高い。
2. 永続給与集計キャッシュ — 現時点で実測根拠がなく、仕様上も正本化できない。
3. `synchronous=FULL` の緩和、トランザクション再検証の削除 — データ整合性を損なうため対象外。

## 5. 実装フェーズ案

各 Phase は独立した PR とし、直前 Phase の結果互換テストと性能ゲートを通してから次へ進む。優先順位2位の PERF-03 を Phase 2 に置き、日別画面統合（PERF-02）より先に、全履歴走査と索引採否を確定する。実装前後の比較では、実機時間だけでなく Repository 呼出し回数 / SQL コマンド数も記録する。

### Phase 0: 実装前ベースラインと共通性能ゲート

- **目的:** 最適化の前後比較を可能にし、速度改善のために既存の2秒要件や結果互換性を失わないようにする。
- **対象:** `PERF-001`〜`PERF-005`。以後の全 PR に適用する必須ゲート。
- **測定条件:** Release 構成、デバッガーなし、代表実機、固定時刻・固定端末状態で実施する。`DATA-LARGE`（30年・約21.9万件）に加え、前月「1日締め」から当月「31日締め」へ変更し、31日の月が連続する61日・1,220件の給与期間集中型 `DATA-LARGE` ケースを用意する。各ケースを3回ウォームアップ後に10回測定し、各回の wall-clock、SQL数、Repository 呼出し数を操作単位で記録する。
- **記録内容:** 実機の機種・OS・空き容量、APK/コミット、データ版、計測区間、ウォームアップ回数、測定回数、最小値・中央値・最悪値、SQL数、Repository呼出し数、エラー/lock を PR ごとに残す。
- **合格・中止条件:** 各 P0 操作の最悪値は2秒以内とする。結果互換性、SQL数/Repository呼出し数の削減根拠、または変更理由を確認できない PR はマージしない。最悪値が2秒を超える、既存ベースラインより最悪値が10%かつ100msを超えて悪化する、または性能低下を説明できない場合は、その Phase を中止して変更をロールバックまたは原因を解消してから再測定する。

### Phase 1: 集計用読取コンテキストと日付計算再利用

- **目的:** ホーム、カレンダー、給与詳細に共通する日単位 N+1 と同日再構築を除く。
- **対象:** PERF-01、PERF-07。
- **修正予定箇所:** `ISalaryQueryUseCase` 周辺の範囲読取契約、`SalaryQueryUseCase.GetCalendarMonthAsync/GetPayrollPeriodAsync/CalculateDayAsync`、設定/祝日/基本シフト Repository の有界バッチ読取。
- **期待できる効果:** 日数比例の設定・祝日復元を暦月数/祝日版本数へ縮小。空日の設定読込をゼロ化。ホーム・カレンダー・計算詳細の共通改善。
- **リスク:** 給与期間が 2 暦月にまたがる場合の設定選択、祝日版、無効化済み設定の履歴計算を誤る可能性。
- **必要なテスト:** `SettingsAndSalaryUseCaseTests` に既存結果との完全一致、月跨ぎ、締め日変更履歴、異なる祝日版、空日、未計算、1 日 20 件、および前月「1日締め」→当月「31日締め」の61日・1,220件集中型給与期間を追加する。Fake/Spy Repository で設定取得が対象暦月数以下、祝日取得が異なる版本数以下、曜日シフト読取が有界であること。`SalaryCalculatorTests` は変更なしで全通過。Phase 0 の5操作を再測定する。

### Phase 2: 入力候補クエリと索引採否の確定

- **目的:** 優先順位2位の PERF-03 を先行し、全履歴走査を必要な画面だけに限定するとともに、索引を測定に基づいて採否決定する。
- **対象:** PERF-03。
- **修正予定箇所:** `IWorkRecordUseCase` / Repository Ports、`WorkRecordUseCase.GetInputOptionsAsync`、Day/BasicShift の名称取得、候補索引の migration。
- **期待できる効果:** 勤務入力、日別詳細、基本シフト画面での全履歴 `GROUP BY` / sort の回数削減。索引を採用する場合も、読取改善と書込・容量コストの均衡を確認できる。
- **リスク:** 入力候補の「最終使用」「使用回数」順、migration、保存・更新・インポート性能、DB容量。
- **必要なテスト:** `WorkRecordUseCaseTests` の候補順、使用回数、最新行、無効設定、`CalendarWorkFlowViewModelTests` と `BasicShiftUseCaseTests` の名称表示を追加する。索引あり/なしの A/B で、使用回数 `GROUP BY`、最新行取得、勤務保存、更新、インポート、DB容量と `EXPLAIN QUERY PLAN` の実際の索引使用を測定する。DATA-LARGE と61日・1,220件集中型ケースで候補順・件数・計算結果が一致すること、Phase 0 の5操作を再測定する。

### Phase 3: 日別・カレンダー・勤務エディタの画面用読取統合

- **目的:** 1 画面内で同じ勤務・設定・祝日を複数 UseCase が読む構造を解消し、勤務表示・プレビュー・保存の明確な重複を除く。
- **対象:** PERF-02、PERF-04、PERF-06。
- **修正予定箇所:** Day/Calendar 用 Application DTO と UseCase、`DayViewModel.LoadCoreAsync`、`CalendarViewModel.LoadMonthCoreAsync/SelectDateCoreAsync`、`WorkRecordUseCase.PreviewAsync/SaveCoreAsync`、`ApplicationSupport.CalculateAsync`、`WorkEditorViewModel.LoadCoreAsync/PreviewCoreAsync/SaveAsync`、`BasicShiftUseCase` の純粋な候補構築部分。
- **期待できる効果:** 日別遷移、削除後更新、月切替、勤務入力での SQL/接続/オブジェクト生成を削減。編集対象の直接取得、祝日の二重取得、保存前の不要な再プレビューを除く。
- **リスク:** 日別給与行と保存済み勤務行の対応、基本シフト候補数、選択日3行サマリー、保存 operation ID の冪等性、保存時 stale 検証、入力正規化。
- **必要なテスト:** `WorkRecordUseCaseTests` の新規/編集/重複タップ/キャンセル/日付変更/無効設定、`CalendarWorkFlowViewModelTests` と `BasicShiftUseCaseTests` の初回表示、日付選択、月切替、削除、シフト反映、勤務なし/20件、候補済み ID 除外、editor 表示と戻りを追加する。旧 API と新画面 DTO の表示結果比較、Repository 呼出し回数テスト、61日・1,220件集中型ケースを含む Phase 0 の5操作再測定を行う。

### Phase 4: 画面再表示の無効化契約

- **目的:** データ変更がない画面遷移で共通ロードを再実行しない。
- **対象:** PERF-05、PERF-08。
- **修正予定箇所:** `IAppSessionState` または専用の in-process data-change generation、各変更 UseCase 完了通知、各 root/detail ViewModel の last-loaded generation、Settings menu/context、Import 後 root reset。
- **期待できる効果:** タブ往復、読取専用詳細からの復帰、設定画面間移動の待ち時間削減。追加報告の「画面遷移全般」に直接対応。
- **リスク:** 保存・削除・設定変更・手当変更・締め日変更・基本シフト反映後に古い表示を残すこと。キャンセルされたロードの世代扱い。
- **必要なテスト:** `HomeViewModelTests`、`CalendarWorkFlowViewModelTests`、`SettingsViewModelTests`、`NavigationAndStartupTests` で、初回は必ずロード、変更なし復帰は再ロードなし、各変更後は依存画面だけ再ロード、Import は全状態リセット、エラー/キャンセル後は再試行可能であることを確認する。手動 Reload は常に実行する。61日・1,220件集中型ケースを含む Phase 0 の5操作を再測定する。

### Phase 5: 過剰な画面固有処理の除去

- **目的:** 設定・起動の明確な不要処理を個別に除く。
- **対象:** PERF-09、PERF-10。
- **修正予定箇所:** `MonthlyAllowanceViewModel.LoadCoreAsync` と期間用 DTO、`SqliteDatabase.InitializeAsync/EnsureBootstrapAsync/BootstrapVersionOneAsync`、bootstrap 版 metadata / migration、`SqliteDataTransfer.cs` の candidate 作成・全置換コミット経路、`DataManagementViewModels.cs` の root reset 順序、DB仕様書とデータ形式規則。
- **期待できる効果:** 月額手当画面から給与全計算を除去。通常起動の書込トランザクションと祝日 INSERT 試行を除去。
- **リスク:** 締め日履歴による期間境界、アプリ更新時の新祝日版投入、古い schema-v1 DB の backfill、export に未参照の同梱祝日がない import candidate、Import 後の同一プロセス内 root reset。
- **必要なテスト:** `SettingsViewModelTests` の手当表示/期間移動/保存削除、`InfrastructureIntegrationTests` / `InfrastructureResilienceTests` の新規 DB、現行 DB 再起動、旧 DB、バンドル版更新、衝突拒否、Import 全置換を実施する。特に、(1) export に版マーカーが含まれないこと、(2) `bootstrapDefaults: false` candidate のマーカー初期値が未適用であること、(3) 参照中祝日版だけを含む export を import しても、コミット後かつ root reset 前に未適用同梱祝日を再投入すること、(4) 再投入失敗時は root reset せず、保持した live DB へ復元して Import 前のデータを維持すること、(5) 同一プロセスの Session/画面キャッシュ破棄後に再投入済み DB から再読込することを確認する。起動時 bootstrap 書込回数と Phase 0 の5操作を再測定する。

### Phase 6: 実測後の UI / SQLite 残余改善

- **目的:** Phase 1〜5 後にも残る遅延だけを実測に基づいて改善する。
- **対象:** PERF-11、PERF-12（計測のみ）、必要なら PERF-13。
- **修正予定箇所:** `CalculationDetailPage.xaml/CalculationDetailViewModel`、性能計測コード、必要時のみ `SqliteSettingSnapshotRepository.InsertSnapshotAsync`。PERF-12 で `SqliteDatabase.OpenUninitializedConnectionAsync` の PRAGMA 契約は変更しない。
- **期待できる効果:** 大量内訳のレイアウト負荷、接続初期化の残余コストの可視化、設定保存のコマンド生成の残余コスト削減。
- **リスク:** UI 表示/アクセシビリティ、SQLite durability、設定履歴の原子性。
- **必要なテスト:** `CalculationDetailViewModelTests` と MAUI UI テストに61日・1,220件集中型ケースを加え、PERF-001〜005 は Phase 0 の条件で、PERF-006〜007 は同じ代表 Android 実機で実施する。PERF-12 は各接続で `foreign_keys=ON`、`journal_mode=WAL`、`synchronous=FULL` が維持される耐障害テストと接続/PRAGMA 実測だけを行う。PERF-13 を実施する場合は、設定保存前後の DB 完全比較、書込時間、DB容量、既存 `InfrastructureResilienceTests` を必須とする。

## 6. 対応不要な最適化

次は一見最適化可能でも、現時点でユーザー体感への効果が小さい、またはリスクに見合わないため改善件数に含めない。

- `ServiceSettingsViewModel.LoadAsync` の `.Result` を機械的に `await` へ変える。`Task.WhenAll` 後なのでブロッキングではない。可読性変更は性能施策に数えない。
- 小規模な `OrderBy`、`ToList`、`ToArray`、`FirstOrDefault` を `for` に置き換える。まず DB 往復と大規模 UI を直す。
- 初期設定画面の `ObservableCollection` を別コレクションへ置換する。初期設定は限定項目で一度だけ行う操作であり、日常の全体遅延の説明にならない。
- カレンダーの 35〜42 セル配列や日別最大 20 行の ViewModel 生成を個別にマイクロ最適化する。件数が明確に有界で、DB N+1 の方が優先度が高い。
- Page/ViewModel を一律 Singleton にする。詳細画面の QueryProperty や未保存状態が漏れるリスクがある。
- すべての Repository 呼出しを `Task.WhenAll` へ追加する。SQLite 接続競合や必要順序を無視した並列化になり、重複処理も減らない。
- `synchronous=FULL` を下げる、保存時のトランザクション内再検証を削る。既存の耐久性・整合性要件に反する。
- 給与結果を正本として保存する、または未計測の永続キャッシュを追加する。設定履歴・祝日版・締め日履歴との無効化が複雑で、仕様上も実測後の選択肢である。
- Domain の小さな設定リスト走査を辞書化する。PERF-01/07 後に実測で CPU が支配的と判明するまで不要。
- `async void` の MAUI lifecycle / command event handler を性能理由だけで変更する。長い DB/集計は既に `await` 境界の下にあり、現状の主因を示す根拠はない。

## 7. 不確定事項

コードベースだけでは次を判断できない。これらは「実際のボトルネック」と断定せず、Phase ごとの検証対象とする。

1. 代表 Android 端末で各操作の wall-clock の何割が SQLite、Domain 計算、ViewModel 構築、XAML layout/render に使われるか。
2. `OnAppearing` が実利用の各タブ切替、OS 復帰、ダイアログ復帰で何回発火し、変更なし再ロードがどの程度を占めるか。
3. Microsoft.Data.Sqlite の connection pooling 状態、各 PRAGMA の端末別コスト、WAL file / lock の挙動。
4. `FindMostRecentAsync` と `GetServicePresetUsageCountsAsync` の実行計画、219,000 件での sort / group の実時間、提案索引の書込コスト。
5. 実データの設定項目数、割増条件数、1 日あたり勤務件数の分布。上限 20 件でも通常値は不明。
6. 給与詳細で実際に生成される VisualElement 数、GC allocation、layout pass、スクロール jank。非仮想化構造はリスクだが影響量は未測定。
7. 起動時 bootstrap の fsync 回数と実時間。不要処理であることは明確だが、ユーザー体感への寄与は端末依存。
8. DI resolution / XAML inflate の実時間。現コードでは DB/集計より弱い候補であり、計測前に lifetime は変更しない。
9. `HomeViewModel.QueueSummaryRequestAsync` の直列キューが実利用で操作待ちを生む頻度。最新リクエストだけを反映する安全策であり、実測なしに並列化・キャンセル方式を変えない。
10. Phase 1〜5 後に既存の 2 秒目標を満たすか。`docs/test_specification.md` の PERF-001〜007 に対応した代表実機での最終確認が必要。

## 結論

コード上の第一仮説は、特定画面固有の 1 箇所ではなく、**画面表示ごとの再ロードが、日単位 N+1・設定スナップショット全復元・入力候補全履歴走査を繰り返す複合問題**である。最初の実装単位は給与集計の範囲読取統合とし、次に日別/勤務入力の画面用読取、入力候補クエリ、変更通知ベースの再ロード制御へ進む。これにより既存仕様・計算結果・オフライン要件・履歴整合性・アーキテクチャを変えず、利用者が指摘する画面操作・遷移全般へ広く効く改善を優先できる。
