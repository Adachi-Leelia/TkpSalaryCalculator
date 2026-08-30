# データベース仕様書

## 1. 文書情報

| 項目 | 内容 |
| --- | --- |
| 文書名 | データベース仕様書 |
| ステータス | 初期リリース向け物理設計方針 |
| データベース | Androidアプリ領域内のSQLite |
| 作成日 | 2026-08-15 |
| 最終更新日 | 2026-08-30 |

## 2. 目的と関連文書

本書は、[要件定義書](requirements.md)、[給与計算仕様書](salary_calculation_specification.md)および[設定履歴データモデル](setting_history_data_model.md)をSQLiteへ実装するためのテーブル、制約、索引、トランザクションおよびマイグレーション方針を定義する。

本書の対象は端末内データベースである。画面状態の一時データ、再生成可能な計算キャッシュ、永続ログおよび給与計算に不要な個人情報は保存しない。

## 3. 物理設計の共通規則

### 3.1 命名

- テーブル名と列名は小文字の`snake_case`とする。
- 主キーは原則として`id`とする。
- 外部キーは`参照先の単数形_id`とする。
- UTC日時は`*_at_utc`、ローカル日付は`*_date`、年月は`*_year_month`とする。
- 真偽値は`INTEGER`の`0`または`1`で保持する。

### 3.2 データ型

| 値 | SQLite表現 | 規則 |
| --- | --- | --- |
| 論理ID | `TEXT` | UUIDの小文字ハイフン付き文字列 |
| 年月 | `INTEGER` | `YYYYMM`。月部分は1～12 |
| ローカル日付 | `TEXT` | ISO 8601の`YYYY-MM-DD` |
| UTC日時 | `TEXT` | ISO 8601のUTC日時 |
| 時刻 | `INTEGER` | 0時からの分。0～1439 |
| 時間量 | `INTEGER` | 整数分 |
| 金額 | `INTEGER` | 0以上の整数円 |
| 割合 | `INTEGER` | basis point。25%は2500 |
| 表示順 | `INTEGER` | 0以上 |

金額、勤務時間および割合に`REAL`を使用しない。アプリケーション層では年月、日付、金額、分数および割合を値オブジェクトとして扱う。

### 3.3 SQLite接続設定

各接続で少なくとも次を有効にする。

```sql
PRAGMA foreign_keys = ON;
PRAGMA journal_mode = WAL;
PRAGMA synchronous = FULL;
```

データベースファイルはアプリ専用領域へ保存する。共有ストレージへ直接配置せず、OSまたはフレームワークによる自動クラウドバックアップの対象外とする。

### 3.4 スキーマとエクスポート形式のバージョン

- SQLiteスキーマバージョンは`PRAGMA user_version`で管理する。
- エクスポート形式のバージョンは`app_metadata.export_format_version`とエクスポートファイルのヘッダーで管理する。
- アプリ同梱データの適用版は`app_metadata.bundled_bootstrap_version`で管理し、端末ローカル状態としてエクスポートしない。
- スキーマバージョンとエクスポート形式バージョンは独立して増加させる。
- 複数タスク対応のSQLiteスキーマバージョンは6、エクスポート形式バージョンは3とする。

## 4. テーブル一覧

### 4.1 アプリ管理

| テーブル | 役割 |
| --- | --- |
| `app_metadata` | 初期設定状態とデータ形式情報 |

### 4.2 設定履歴

| テーブル | 役割 |
| --- | --- |
| `setting_month` | 年月から設定スナップショットへの参照 |
| `setting_snapshot` | 変更不可の設定一式のヘッダー |
| `service_definition` | サービス種類の論理ID |
| `time_category_definition` | 時間区分の論理ID |
| `premium_definition` | 割増の論理ID |
| `count_bonus_definition` | 件数加算の論理ID |
| `snapshot_service` | スナップショット内のサービス種類 |
| `snapshot_time_category` | スナップショット内の時間区分 |
| `snapshot_rate` | スナップショット内の基本単価 |
| `snapshot_premium` | スナップショット内の割増本体 |
| `snapshot_premium_weekday` | 割増対象曜日 |
| `snapshot_premium_date` | 割増対象個別日 |
| `snapshot_premium_service` | 割増対象サービス |
| `snapshot_count_bonus` | スナップショット内の件数加算本体 |
| `snapshot_count_bonus_service` | 件数加算対象サービス |

### 4.3 勤務・入力補助

| テーブル | 役割 |
| --- | --- |
| `service_preset` | サービス種類と時間を組み合わせた入力補助 |
| `basic_shift` | 曜日ごとの基本シフト親 |
| `basic_shift_task` | 基本シフト内のタスク |
| `work_record` | 訪問を表す確定済み勤務記録親 |
| `work_task` | 訪問内のタスク |

### 4.4 給与期間・休日

| テーブル | 役割 |
| --- | --- |
| `closing_rule_history` | 締め日の適用開始月履歴 |
| `monthly_allowance` | 給与期間単位の月額手当 |
| `annual_summary_setting` | 年間給与見込み累計の現在の締め月 |
| `holiday_calendar_version` | 国民の祝日データの版 |
| `holiday_date` | 版ごとの祝日 |

## 5. テーブル定義

### 5.1 `app_metadata`

1行だけを保持する。

| 列 | 型 | NULL | 制約・内容 |
| --- | --- | --- | --- |
| `id` | `INTEGER` | 不可 | 主キー。常に1 |
| `initial_setup_status` | `TEXT` | 不可 | `NotStarted`、`InProgress`、`Completed` |
| `initial_setup_step` | `TEXT` | 可 | 初期設定の再開位置 |
| `initial_snapshot_id` | `TEXT` | 可 | 初期設定スナップショット |
| `export_format_version` | `INTEGER` | 不可 | 1以上 |
| `last_exported_at_utc` | `TEXT` | 可 | 最後に正常終了したエクスポート日時 |
| `last_data_changed_at_utc` | `TEXT` | 可 | 設定または勤務データを最後に確定変更した日時 |
| `backup_reminder_deferred_until_date` | `TEXT` | 可 | バックアップ案内を再表示しないローカル日付 |
| `created_at_utc` | `TEXT` | 不可 | 作成日時 |
| `updated_at_utc` | `TEXT` | 不可 | 更新日時 |
| `bundled_bootstrap_version` | `INTEGER` | 不可 | この端末DBへ適用済みの同梱データ版。0は未適用 |

`CHECK(id = 1)`を設ける。`initial_setup_status = 'Completed'`の場合、初期スナップショット、締め日および計算に必要な設定が存在することはアプリケーション層でも検証する。

`last_data_changed_at_utc`は設定、年間締め月、基本シフト、月額手当または勤務記録を確定変更するトランザクション内で更新する。エクスポート成功と案内延期だけでは更新しない。バックアップ案内の状態と`bundled_bootstrap_version`は給与計算の再現に不要な端末設定であるため、エクスポート対象外とする。新規DBおよび`bootstrapDefaults: false`で作るインポート候補DBでは版を0（未適用）で初期化し、同梱データの投入成功と同じトランザクションで現行版へ更新する。

### 5.2 `setting_snapshot`

| 列 | 型 | NULL | 制約・内容 |
| --- | --- | --- | --- |
| `id` | `TEXT` | 不可 | 主キー |
| `based_on_id` | `TEXT` | 可 | 複製元。自己外部キー、削除時はNULL |
| `holiday_calendar_version_id` | `TEXT` | 不可 | 使用する祝日データ版 |
| `schema_version` | `INTEGER` | 不可 | 1以上 |
| `created_at_utc` | `TEXT` | 不可 | 作成日時 |

作成完了後は更新しない。`based_on_id`は系譜確認用であり、給与計算時に設定を継承する用途では使用しない。

### 5.3 `setting_month`

| 列 | 型 | NULL | 制約・内容 |
| --- | --- | --- | --- |
| `year_month` | `INTEGER` | 不可 | 主キー。年月形式をCHECK |
| `snapshot_id` | `TEXT` | 不可 | `setting_snapshot.id`、削除制限 |
| `created_at_utc` | `TEXT` | 不可 | 初回確定日時 |
| `updated_at_utc` | `TEXT` | 不可 | 参照先の最終変更日時 |

年月形式には次と同等の制約を設ける。

```sql
CHECK (
    year_month BETWEEN 100001 AND 999912
    AND year_month % 100 BETWEEN 1 AND 12
)
```

### 5.4 論理IDテーブル

次の4テーブルは同じ基本構造とする。

- `service_definition`
- `time_category_definition`
- `premium_definition`
- `count_bonus_definition`

| 列 | 型 | NULL | 制約・内容 |
| --- | --- | --- | --- |
| `id` | `TEXT` | 不可 | 主キー |
| `created_at_utc` | `TEXT` | 不可 | 作成日時 |

表示名や有効状態は保持しない。論理IDが勤務記録またはスナップショットから参照されている場合は物理削除しない。

### 5.5 `snapshot_service`

| 列 | 型 | NULL | 制約・内容 |
| --- | --- | --- | --- |
| `snapshot_id` | `TEXT` | 不可 | `setting_snapshot.id` |
| `service_id` | `TEXT` | 不可 | `service_definition.id` |
| `display_name` | `TEXT` | 不可 | 空文字不可 |
| `display_order` | `INTEGER` | 不可 | 0以上 |
| `is_enabled` | `INTEGER` | 不可 | 0または1 |

主キーは`snapshot_id, service_id`とする。同じスナップショット内で、前後の空白を除去した表示名が重複しないようアプリケーション層で検証する。

`is_enabled`は新規入力候補への表示可否を表す。既存勤務が参照するサービス行は、無効でも単価選択と計算に使用できる。

### 5.6 `snapshot_time_category`

| 列 | 型 | NULL | 制約・内容 |
| --- | --- | --- | --- |
| `snapshot_id` | `TEXT` | 不可 | `setting_snapshot.id` |
| `time_category_id` | `TEXT` | 不可 | `time_category_definition.id` |
| `service_id` | `TEXT` | 不可 | 同一スナップショット内のサービス種類 |
| `display_name` | `TEXT` | 不可 | 空文字不可 |
| `standard_minutes` | `INTEGER` | 不可 | 1以上 |
| `display_order` | `INTEGER` | 不可 | 0以上 |
| `is_enabled` | `INTEGER` | 不可 | 0または1 |

主キーは`snapshot_id, time_category_id`とする。`snapshot_id, service_id`から`snapshot_service`への複合外部キーを設ける。

`is_enabled`の意味は`snapshot_service`と同じとし、無効な時間区分も既存勤務の計算根拠として使用できる。

### 5.7 `snapshot_rate`

| 列 | 型 | NULL | 制約・内容 |
| --- | --- | --- | --- |
| `snapshot_id` | `TEXT` | 不可 | 所属スナップショット |
| `service_id` | `TEXT` | 不可 | 対象サービス種類 |
| `time_category_id` | `TEXT` | 可 | 時間区分単位の場合の対象 |
| `rate_type` | `TEXT` | 不可 | `Hourly`または`FixedPerRecord` |
| `amount_yen` | `INTEGER` | 不可 | 0以上 |

サービス種類単位の単価では`time_category_id`をNULLとする。SQLiteではNULLを含む一意制約だけでは重複を防げないため、次の部分一意索引を設ける。

`time_category_id`が設定されている場合は、`snapshot_id, time_category_id, service_id`の組み合わせが同じスナップショット内の時間区分と一致することを、複合外部キーまたは保存前検証で保証する。

```sql
CREATE UNIQUE INDEX ux_snapshot_rate_service
ON snapshot_rate(snapshot_id, service_id)
WHERE time_category_id IS NULL;

CREATE UNIQUE INDEX ux_snapshot_rate_time_category
ON snapshot_rate(snapshot_id, service_id, time_category_id)
WHERE time_category_id IS NOT NULL;
```

時間区分単位の単価をサービス種類単位の単価より優先する。任意時間入力はサービス種類単位の単価を使用し、`rate_type`が`FixedPerRecord`でも適用できる。詳細は[給与計算仕様書](salary_calculation_specification.md)に従う。

`FixedPerRecord`は形式1・2およびスキーマ5との互換性のため名称を維持し、スキーマ6では単価と割増のどちらでもタスク1件当たりを意味する。

### 5.8 `snapshot_premium`

| 列 | 型 | NULL | 制約・内容 |
| --- | --- | --- | --- |
| `snapshot_id` | `TEXT` | 不可 | 所属スナップショット |
| `premium_id` | `TEXT` | 不可 | 割増の論理ID |
| `display_name` | `TEXT` | 不可 | 空文字不可 |
| `calculation_type` | `TEXT` | 不可 | `Percentage`、`FixedPerHour`、`FixedPerRecord` |
| `percentage_basis_points` | `INTEGER` | 可 | 割合方式だけで使用。0以上 |
| `amount_yen` | `INTEGER` | 可 | 固定額方式だけで使用。0以上 |
| `start_time_minutes` | `INTEGER` | 可 | 0～1439 |
| `end_time_minutes` | `INTEGER` | 可 | 0～1439 |
| `uses_national_holidays` | `INTEGER` | 不可 | 0または1 |
| `is_enabled` | `INTEGER` | 不可 | 0または1 |

主キーは`snapshot_id, premium_id`とする。加算方式に応じ、割合と金額のどちらか一方だけが設定されるCHECKを設ける。開始時刻と終了時刻は両方NULLまたは両方非NULLとし、両方非NULLの場合は同じ値を許可しない。

子テーブル：

| テーブル | 主キー | 列・制約 |
| --- | --- | --- |
| `snapshot_premium_weekday` | `snapshot_id, premium_id, weekday` | `weekday`は1（月）～7（日） |
| `snapshot_premium_date` | `snapshot_id, premium_id, target_date` | `target_date`はローカル日付 |
| `snapshot_premium_service` | `snapshot_id, premium_id, service_id` | 同一スナップショット内のサービスを参照 |

子テーブルは`snapshot_id, premium_id`から`snapshot_premium`へ削除連鎖する。ただし完成済みスナップショットを通常処理で削除しない。

### 5.9 `snapshot_count_bonus`

| 列 | 型 | NULL | 制約・内容 |
| --- | --- | --- | --- |
| `snapshot_id` | `TEXT` | 不可 | 所属スナップショット |
| `count_bonus_id` | `TEXT` | 不可 | 件数加算の論理ID |
| `display_name` | `TEXT` | 不可 | 空文字不可 |
| `amount_yen` | `INTEGER` | 不可 | 0以上 |
| `is_enabled` | `INTEGER` | 不可 | 0または1 |

主キーは`snapshot_id, count_bonus_id`とする。

`snapshot_count_bonus_service`は`snapshot_id, count_bonus_id, service_id`を主キーとする。対象サービス行が1件もない件数加算は全サービスへ適用する。

### 5.10 `service_preset`

サービス設定は、勤務入力を補助する現在値のテンプレートであり、過去給与の計算根拠には使用しない。

| 列 | 型 | NULL | 制約・内容 |
| --- | --- | --- | --- |
| `id` | `TEXT` | 不可 | 主キー |
| `display_name` | `TEXT` | 不可 | 例：身体1 |
| `service_id` | `TEXT` | 不可 | サービス種類の論理ID |
| `time_category_id` | `TEXT` | 可 | 使用する時間区分 |
| `default_work_minutes` | `INTEGER` | 不可 | 1～1440 |
| `display_order` | `INTEGER` | 不可 | 0以上 |
| `is_enabled` | `INTEGER` | 不可 | 0または1 |
| `created_at_utc` | `TEXT` | 不可 | 作成日時 |
| `updated_at_utc` | `TEXT` | 不可 | 更新日時 |

サービス設定を選択したタスクには具体的なサービスID、時間区分IDおよび勤務分数をコピーする。後からサービス設定を変更しても、作成済みタスクは変更しない。

### 5.11 `basic_shift`

| 列 | 型 | NULL | 制約・内容 |
| --- | --- | --- | --- |
| `id` | `TEXT` | 不可 | 主キー |
| `weekday` | `INTEGER` | 不可 | 1（月）～7（日） |
| `display_order` | `INTEGER` | 不可 | 0以上 |
| `is_enabled` | `INTEGER` | 不可 | 0または1 |
| `created_at_utc` | `TEXT` | 不可 | 作成日時 |
| `updated_at_utc` | `TEXT` | 不可 | 更新日時 |

基本シフトは現在の親情報だけを保持する。変更履歴や適用開始日は保持しない。同一曜日の`display_order`は保存時に0始まりの連番へ正規化する。

#### `basic_shift_task`

| 列 | 型 | NULL | 制約・内容 |
| --- | --- | --- | --- |
| `id` | `TEXT` | 不可 | 主キー |
| `basic_shift_id` | `TEXT` | 不可 | `basic_shift.id`、削除連鎖 |
| `service_preset_id` | `TEXT` | 可 | 入力元サービス設定。削除時NULL |
| `service_id` | `TEXT` | 不可 | サービス種類の論理ID |
| `time_category_id` | `TEXT` | 可 | 時間区分の論理ID |
| `input_mode` | `TEXT` | 不可 | `TimeRange`または`Duration` |
| `work_minutes` | `INTEGER` | 不可 | 1～1440 |
| `start_time_minutes` | `INTEGER` | 可 | 0～1439 |
| `end_time_minutes` | `INTEGER` | 可 | 0～1439 |
| `display_order` | `INTEGER` | 不可 | 親内の0始まりの表示順 |
| `created_at_utc` | `TEXT` | 不可 | 作成日時 |
| `updated_at_utc` | `TEXT` | 不可 | 更新日時 |

`UNIQUE(basic_shift_id, display_order)`を設ける。タスクIDはUUIDとし、同じ基本シフトの再編集、並べ替えおよびエクスポート形式3の往復で維持する。基本シフトと全タスクは1トランザクションで保存し、親ごとに1件以上の子があること、子IDと表示順が重複しないことをコミット前に検証する。

### 5.12 `work_record`

`work_record`は件数加算を1回数える訪問の親であり、サービスと時間の列を直接持たない。

| 列 | 型 | NULL | 制約・内容 |
| --- | --- | --- | --- |
| `id` | `TEXT` | 不可 | 主キー |
| `work_date` | `TEXT` | 不可 | 訪問全体で共通のローカル日付 |
| `source_basic_shift_id` | `TEXT` | 可 | 反映元の基本シフト |
| `source_work_record_id` | `TEXT` | 可 | 日単位複製時の複製元 |
| `save_operation_id` | `TEXT` | 不可 | 冪等な保存操作を識別するUUID |
| `created_at_utc` | `TEXT` | 不可 | 作成日時 |
| `updated_at_utc` | `TEXT` | 不可 | 更新日時 |

勤務記録には単価、割増額、件数加算額および設定スナップショットIDを保存しない。

#### `work_task`

| 列 | 型 | NULL | 制約・内容 |
| --- | --- | --- | --- |
| `id` | `TEXT` | 不可 | 主キー。タスクを再編集時にも識別するUUID |
| `work_record_id` | `TEXT` | 不可 | `work_record.id`、削除連鎖 |
| `service_id` | `TEXT` | 不可 | サービス種類の論理ID |
| `time_category_id` | `TEXT` | 可 | 時間区分の論理ID |
| `input_mode` | `TEXT` | 不可 | `TimeRange`または`Duration` |
| `work_minutes` | `INTEGER` | 不可 | 1～1440 |
| `start_time_minutes` | `INTEGER` | 可 | 0～1439 |
| `end_time_minutes` | `INTEGER` | 可 | 0～1439 |
| `display_order` | `INTEGER` | 不可 | 親内の0始まりの表示順 |
| `source_service_preset_id` | `TEXT` | 可 | 入力補助として使用したサービス設定 |
| `created_at_utc` | `TEXT` | 不可 | 作成日時 |
| `updated_at_utc` | `TEXT` | 不可 | 更新日時 |

`UNIQUE(work_record_id, display_order)`を設ける。訪問と全タスクは1トランザクションで保存し、親ごとに1件以上の子があること、空または重複したタスクIDがないこと、および表示順が0始まりの連番であることをコミット前と読取後に検証する。`work_task`だけを単独保存または削除する公開リポジトリ操作は設けない。

`input_mode = 'TimeRange'`の場合はタスクの開始・終了時刻を必須とする。終了時刻が開始時刻より後の場合は同日、以前の場合は翌日とし、正規化した`work_minutes`と時刻差が一致することをアプリケーション層で検証する。開始・終了時刻が同じ場合は1440分とする。

`input_mode = 'Duration'`で、対象サービスに適用可能な時刻条件付き割増がある場合はタスクの開始時刻を必須とし、終了時刻を開始時刻と`work_minutes`から算出して保存する。利用者へ終了時刻を重複入力させない。時刻条件付き割増がない場合は開始・終了時刻をNULLにできる。

訪問内のタスク間について、時刻の重複、空き、連続性または入力順との一致を検証する制約は設けない。タスク数と訪問内の合計勤務分数にも製品上限を設けない。

同じ基本シフトを同じ日へ二重反映しないため、次の部分一意索引を設ける。

```sql
CREATE UNIQUE INDEX ux_work_record_shift_date
ON work_record(source_basic_shift_id, work_date)
WHERE source_basic_shift_id IS NOT NULL;
```

類似する手入力記録との重複はDB制約で禁止せず、反映前プレビューで警告する。

### 5.13 `closing_rule_history`

| 列 | 型 | NULL | 制約・内容 |
| --- | --- | --- | --- |
| `id` | `TEXT` | 不可 | 主キー |
| `effective_from_year_month` | `INTEGER` | 不可 | 一意。給与期間年月 |
| `closing_day` | `INTEGER` | 可 | 1～31 |
| `is_end_of_month` | `INTEGER` | 不可 | 0または1 |
| `created_at_utc` | `TEXT` | 不可 | 作成日時 |

月末締めでは`closing_day`をNULLとし、それ以外では1～31を必須とするCHECKを設ける。給与期間年月以下で適用開始年月が最も新しい行を使用する。

給与期間自体はテーブルへ重複保存せず、締め日履歴から決定的に算出する。給与期間キーは終了日が属する年月とする。

### 5.14 `monthly_allowance`

| 列 | 型 | NULL | 制約・内容 |
| --- | --- | --- | --- |
| `id` | `TEXT` | 不可 | 主キー |
| `payroll_period_year_month` | `INTEGER` | 不可 | 対象給与期間キー |
| `display_name` | `TEXT` | 不可 | 空文字不可 |
| `amount_yen` | `INTEGER` | 不可 | 0以上 |
| `created_at_utc` | `TEXT` | 不可 | 作成日時 |
| `updated_at_utc` | `TEXT` | 不可 | 更新日時 |

同じ給与期間へ複数登録できる。勤務記録または日単位へ配賦しない。

### 5.15 `annual_summary_setting`

現在値を1行だけ保持し、行の欠落はデータ不整合として扱う。

| 列 | 型 | NULL | 制約・内容 |
| --- | --- | --- | --- |
| `id` | `INTEGER` | 不可 | 主キー。`CHECK(id = 1)` |
| `closing_month` | `INTEGER` | 不可 | 1～12。既定値12 |
| `created_at_utc` | `TEXT` | 不可 | 作成日時 |
| `updated_at_utc` | `TEXT` | 不可 | 更新日時 |

年間締め月の変更は設定スナップショットまたは締め日履歴を作成せず、この行だけを更新する。同じトランザクションで`app_metadata.last_data_changed_at_utc`を更新する。

### 5.16 祝日テーブル

#### `holiday_calendar_version`

| 列 | 型 | NULL | 制約・内容 |
| --- | --- | --- | --- |
| `id` | `TEXT` | 不可 | 主キー |
| `version_name` | `TEXT` | 不可 | データ版名。一意 |
| `source_name` | `TEXT` | 不可 | 出典名 |
| `source_reference_date` | `TEXT` | 不可 | 参照日 |
| `created_at_utc` | `TEXT` | 不可 | 取り込み日時 |

#### `holiday_date`

| 列 | 型 | NULL | 制約・内容 |
| --- | --- | --- | --- |
| `holiday_calendar_version_id` | `TEXT` | 不可 | 祝日データ版 |
| `holiday_date` | `TEXT` | 不可 | ローカル日付 |
| `display_name` | `TEXT` | 不可 | 祝日名 |

主キーは`holiday_calendar_version_id, holiday_date`とする。既存の版は更新せず、アプリ更新では新しい版を追加する。

## 6. 外部キー削除規則

| 参照 | 規則 | 理由 |
| --- | --- | --- |
| `setting_month` → `setting_snapshot` | `RESTRICT` | 使用中スナップショットの削除防止 |
| スナップショット子 → `setting_snapshot` | `CASCADE` | 未使用スナップショットの保守削除用 |
| スナップショット子 → 論理ID | `RESTRICT` | 過去設定の再現 |
| `work_task` → サービス・時間区分論理ID | `RESTRICT` | 勤務内容の消失防止 |
| `work_record` → `work_task` | `CASCADE` | 訪問と全タスクを一括削除 |
| `work_record.source_basic_shift_id` | 外部キーにせず由来IDを保持 | 削除後も二重反映判定を維持する |
| `work_task.source_service_preset_id` | `SET NULL` | 入力補助は計算根拠ではない |
| `work_record.source_work_record_id` | 外部キーにせず由来IDを保持 | 複製元削除後も由来を識別できる |
| `service_preset` → サービス・時間区分論理ID | `RESTRICT` | 使用中の入力候補の参照切れ防止 |
| `basic_shift` → `basic_shift_task` | `CASCADE` | 基本シフトと全タスクを一括削除 |
| `basic_shift_task` → サービス・時間区分論理ID | `RESTRICT` | 基本シフトの参照切れ防止 |
| `basic_shift_task.service_preset_id` | `SET NULL` | 具体的な勤務内容は基本シフトタスク側に保持する |
| 祝日データ版 → 祝日日付 | `CASCADE` | 未使用版の保守削除用 |

基本シフト削除後も二重反映判定を維持するため、`source_basic_shift_id`は論理的な由来IDとして残す。基本シフトの表示内容が必要な場合は、訪問の`work_task`が保持する具体的なサービス、時間区分、分数および時刻から表示する。

## 7. 索引

最低限、次を作成する。

| 索引 | 列 | 用途 |
| --- | --- | --- |
| `ix_setting_month_snapshot` | `setting_month(snapshot_id)` | 参照中スナップショット確認 |
| `ix_snapshot_service_order` | `snapshot_service(snapshot_id, is_enabled, display_order)` | サービス一覧 |
| `ix_snapshot_time_category_order` | `snapshot_time_category(snapshot_id, service_id, is_enabled, display_order)` | 時間区分一覧 |
| `ix_snapshot_premium_snapshot` | `snapshot_premium(snapshot_id, is_enabled)` | 割増取得 |
| `ix_snapshot_count_bonus_snapshot` | `snapshot_count_bonus(snapshot_id, is_enabled)` | 件数加算取得 |
| `ix_service_preset_order` | `service_preset(is_enabled, display_order)` | 入力候補 |
| `ix_basic_shift_weekday` | `basic_shift(weekday, is_enabled, display_order)` | 曜日別反映 |
| `ux_basic_shift_task_order` | `basic_shift_task(basic_shift_id, display_order)` | 基本シフトタスクの順序一意性と親別取得 |
| `ix_work_record_date` | `work_record(work_date)` | 日別・期間集計 |
| `ux_work_task_order` | `work_task(work_record_id, display_order)` | 訪問タスクの順序一意性と親別取得 |
| `ux_work_record_save_operation` | `work_record(save_operation_id)` | 保存再試行時の冪等性 |
| `ux_work_record_shift_date` | `work_record(source_basic_shift_id, work_date)` | 基本シフト二重反映防止 |
| `ux_closing_rule_effective_month` | `closing_rule_history(effective_from_year_month)` | 適用履歴の一意性 |
| `ix_monthly_allowance_period` | `monthly_allowance(payroll_period_year_month)` | 給与期間集計 |
| `ix_holiday_date_lookup` | `holiday_date(holiday_calendar_version_id, holiday_date)` | 祝日判定 |

`ux_basic_shift_task_order`と`ux_work_task_order`は親IDを左端に持つため、親IDだけの重複索引は追加しない。旧`ix_work_record_service_date`は親からサービス列を除くためスキーマ6で削除する。サービス起点の検索が実測で必要になった場合だけ、`work_task(service_id, work_record_id)`を候補として実行計画を確認する。約21.9万訪問とそのタスクの基準データで実行計画と応答時間を確認し、使用されない索引は追加しない。

## 8. 主要トランザクション

### 8.1 対象年月の設定変更

1. `BEGIN IMMEDIATE`で書き込みトランザクションを開始する。
2. 対象年月の`setting_month`を取得する。存在しない場合は直近月または初期設定を引き継ぎ、直近スナップショットの祝日データ版が古いときはスナップショットを複製して、端末内の検証済みデータのうち`source_reference_date`が最も新しい版へ更新してから作成する。
3. 参照中スナップショットとすべての子行を新しいIDへ複製する。
4. 変更を複製先へ反映する。
5. 単価の重複、外部キー、必須値および対象年月の計算可能性を検証する。
6. `setting_month.snapshot_id`を新しいスナップショットへ付け替える。
7. コミットする。

途中で失敗した場合はロールバックし、対象年月の参照先を変更しない。

### 8.2 勤務記録の保存

1. 訪問と全タスクの入力値を正規化し、勤務日、タスク件数、タスクID、表示順、分数、時刻および論理IDを検証する。
2. 勤務年月の設定スナップショットを取得する。
3. 全タスクの計算可能性を検証する。
4. `save_operation_id`で完了済み保存を確認し、未完了なら親を追加または更新する。
5. 子は親単位で差分反映し、削除・追加・更新後に1件以上、ID一意および表示順連番を再検証する。
6. キャッシュを採用している場合は影響範囲を無効化する。
7. コミットする。

親と子の全変更を1トランザクションで行い、`work_task`だけを保存または削除するリポジトリ操作は公開しない。入力自体が正しくても1件以上のタスクで計算設定だけが不足する場合は、警告付き保存を許可して訪問全体を未計算として表示する。日付、タスク件数、サービス、勤務分数または時刻など入力自体が不正な場合は保存しない。詳細は[給与計算仕様書](salary_calculation_specification.md)の保存可否に従う。

`FindAsync`、保存操作ID検索および日付範囲読取は、常に全タスクを含む完全な訪問を返す。範囲読取は親と全子を定数回のクエリで取得する。既存の`save_operation_id`が見つかった場合は、親だけでなくタスクID、表示順および全入力値を含む親子ペイロード全体を比較し、同じ再試行なら既存結果を返し、異なる内容なら競合として拒否する。

### 8.3 基本シフトの保存

1. 基本シフト親と全タスクの曜日、件数、ID、表示順、サービス、分数および時刻を正規化・検証する。
2. 親と全タスクを同じトランザクションで追加または更新する。
3. 子の差分反映後に1件以上、ID一意および表示順連番を再検証してコミットする。

`basic_shift_task`だけを保存または削除する公開リポジトリ操作は設けない。最後のタスク削除、親だけの保存または途中失敗では全変更をロールバックする。

### 8.4 基本シフトの反映

1. 反映対象と重複候補を読み取り専用で作成する。
2. 利用者の確定後に書き込みトランザクションを開始する。
3. 確定直前に同じ`source_basic_shift_id, work_date`が存在しないことを再確認する。
4. 選択された基本シフトごとに訪問親を1件作り、その基本シフトの全タスクを新しいタスクIDで追加する。
5. 1件でも予期しない失敗があった場合は一括でロールバックする。

### 8.5 インポート

1. ファイルをデータベース外で逐次読み取り、形式、版、訪問数、タスク数、値、初期スナップショットIDおよび参照整合性を検証する。形式3では親子ID、親ごとの子件数、タスクID、表示順および時間を検証する。大容量ファイルまたは1訪問分の全タスクをオブジェクトグラフとしてメモリへ展開しない。
2. 同梱初期データを投入しない候補DBを作り、`bundled_bootstrap_version`を0としてインポート内容だけを検証する。
3. 利用者の確認後、置換前のliveデータを復元可能な一時スナップショットとして保持して書き込みトランザクションを開始する。
4. インポート対象テーブルを親、子の外部キー順序に従って置換し、`bundled_bootstrap_version`を0へ戻す。全レコード取込後に親ごとの子件数と業務不変条件を検証し、ファイル内の出現順だけには依存しない。
5. エクスポートに含まれる初期スナップショットIDを`app_metadata.initial_snapshot_id`へ設定して初期設定状態を`Completed`とし、最終データ変更日時と最終エクスポート日時をインポート完了時刻へ設定する。バックアップ案内の延期状態は引き継がない。
6. 置換をコミットした後、root画面をリセットする前に、live DBへ未適用の同梱祝日版を投入する。予約IDの衝突検証、祝日投入および版マーカー更新は同じトランザクションで行う。
7. `PRAGMA foreign_key_check`と業務整合性検証を実行し、同梱データの投入をコミットする。
8. 置換後の同梱データ投入または最終検証に失敗した場合は、保持したスナップショットからlive DBを復元してインポート全体を失敗扱いとする。root画面とSessionキャッシュはリセットしない。
9. 全処理成功後にSessionと画面キャッシュを破棄し、同梱データ投入済みのDBからroot画面を再構築する。

Androidの選択元ストリームを再読込できない場合は、アプリ専用キャッシュへ一時コピーしてからストリーミング検証する。一時ファイルは自動バックアップ対象外とし、取消、成功、失敗および次回起動時の残存確認で削除する。確認前に既存データベースは変更しない。

## 9. 設定スナップショットの不変条件

- `setting_snapshot`と子行は作成中だけ変更できる。
- `setting_month`から参照された後は子行を直接更新しない。
- 設定編集用リポジトリは、更新APIではなく`CloneAndReplaceMonthSnapshot`相当のユースケースだけを公開する。
- 複数の年月が同じスナップショットを参照している状態を許可する。
- 1つの年月を変更するときは新しいスナップショットを作り、その年月だけ参照先を変更する。
- どの年月からも参照されないスナップショットは残してよい。削除は初期リリースの必須機能としない。

SQLiteトリガーによる不変化の強制は、マイグレーションとインポートを複雑にするため初期実装では採用しない。リポジトリAPI、トランザクションおよび統合テストで保証する。

## 10. 給与期間の算出

給与期間キー`YYYYMM`は、その給与期間の終了日が属する年月を表す。

```text
End(YYYYMM)
  = YYYYMMに適用される締め日

Start(YYYYMM)
  = End(前月の給与期間キー) + 1日
```

締め日が対象月に存在しない場合、`End`は対象月末日とする。月末締めでは対象月末日を使用する。

勤務日から給与期間を検索するときは、次を満たす期間キーを取得する。

```text
Start(period_key) <= work_date <= End(period_key)
```

給与期間は決定的に再計算できるためテーブルへ保存しない。画面表示や性能のためキャッシュする場合も、締め日履歴の変更時に再構築できるようにする。

## 11. エクスポート形式

データベースファイル自体はエクスポートせず、スキーマから独立したUTF-8のJSON文書へ変換する。ファイルは単一ファイルとし、アプリ固有の拡張子を使用する場合も内容形式は識別可能にする。

概念構造：

```json
{
  "format": "TkpSalaryCalculator.Export",
  "formatVersion": 3,
  "createdAtUtc": "2026-08-15T00:00:00Z",
  "appVersion": "1.0.0",
  "data": {
    "initialSnapshotId": "00000000-0000-0000-0000-000000000000",
    "settingMonths": [],
    "settingSnapshots": [],
    "closingRuleHistory": [],
    "monthlyAllowances": [],
    "annualSummarySetting": { "closingMonth": 12 },
    "serviceDefinitions": [],
    "timeCategoryDefinitions": [],
    "premiumDefinitions": [],
    "countBonusDefinitions": [],
    "servicePresets": [],
    "basicShifts": [],
    "basicShiftTasks": [],
    "workRecords": [],
    "workTasks": [],
    "holidayCalendarVersions": [],
    "holidayDates": []
  }
}
```

`settingSnapshots`の各要素には、サービス種類、時間区分、単価、割増とその条件、件数加算とその条件を子要素として含める。形式3の`basicShifts`、`basicShiftTasks`、`workRecords`および`workTasks`は、子が親IDを持つ別々のフラットレコード列とし、親レコード内へタスク配列を入れない。エクスポートには初期スナップショット、年月から参照中のスナップショット、参照される論理ID、年間締め月、およびそれらが参照する祝日データ版に属するすべての祝日日付を含める。内部のSQLite行番号、バックアップ案内状態、`bundled_bootstrap_version`および再生成可能なキャッシュは含めない。

形式バージョン3は年間締め月と親子レコードを必須とし、タスク0件、空または重複したID、親内の重複表示順、参照切れおよび不正な時間を確認前に拒否する。形式バージョン1と2は引き続き受け入れ、従来の基本シフトと勤務記録1件を、タスク1件を持つ親へ変換する。形式1では年間締め月へ12月を補う。形式バージョン4以降およびその他の非対応版は既存データを変更せず拒否する。

エクスポートとインポートは1転送レコード分の有界メモリで逐次読み書きし、約21.9万訪問の全親子データまたは1訪問分のタスク配列をメモリへ保持しない。形式3の往復後は訪問ID、タスクID、訪問数、タスク数、タスク順、入力内容、由来IDおよび計算結果が一致することを検証する。

## 12. マイグレーション

- マイグレーションはスキーマバージョンごとの逐次処理とする。
- すべてのマイグレーションをトランザクション内で実行する。
- 既存列の意味を変更する場合は、新しい列またはテーブルへ変換してから旧構造を除去する。
- 更新前のデータで再現性を確認できる自動テストを用意する。
- アプリ更新後に`PRAGMA integrity_check`または同等の限定的な整合性確認を実施できる構造にする。
- マイグレーション失敗時はアプリの通常利用を開始せず、既存データを上書きしない。
- ダウングレードによる古いアプリでのDB利用は保証しない。

スキーマバージョン2では、入力候補の全履歴使用回数集計用に`ix_work_record_source_preset`を追加する。勤務記録の内容は変換しない。

スキーマバージョン3では、`app_metadata.bundled_bootstrap_version`を未適用の0で追加する。マイグレーション完了後に同梱データを検証・投入し、成功した場合だけ現行版へ更新する。既に同じ予約IDが異なる内容で使われている場合は通常利用を開始しない。

スキーマバージョン4では、画面で使用しない入力候補の全履歴使用回数集計を廃止したことに伴い、`ix_work_record_source_preset`を削除する。勤務記録の内容は変更しない。

スキーマバージョン5では、`annual_summary_setting`を追加して12月締めの行を投入し、`app_metadata.export_format_version`を2へ更新する。勤務記録、給与設定、締め日履歴および月額手当は変更しない。

スキーマバージョン6では、勤務記録と基本シフトを親子構造へ移行し、`app_metadata.export_format_version`を3へ更新する。移行は次を同じトランザクションで行う。

1. 新しい`work_record_new`、`work_task`、`basic_shift_new`および`basic_shift_task`を制約付きで作成する。
2. 旧`work_record`ごとに親を1件作成し、旧サービス、時間、入力方式および入力元サービス設定を同じIDの新規`work_task`1件へ移す。旧`source_basic_shift_id`と`source_work_record_id`は親へ移し、旧IDから決定的な`save_operation_id`を生成し、`display_order = 0`とする。
3. 旧`basic_shift`ごとに親を1件作成し、旧勤務内容を同じIDの新規`basic_shift_task`1件へ移して`display_order = 0`とする。
4. 全親が子を正確に1件持つこと、全件数、外部キー、時間および代表計算結果を検証する。
5. 旧テーブルを除去し、新テーブルを正式名へ変更して索引を作成する。

移行用の子IDは旧親IDから決定的に生成し、再試行しても同じ結果にする。移行前後で訪問数、基本給与、割増、件数加算、日別合計、給与期間合計および年間累計を変化させない。途中で失敗した場合はスキーマ5のDBを上書きせず通常利用を開始しない。

## 13. 性能方針

- カレンダーは表示月の日付範囲だけを`work_record.work_date`で検索し、必要な全タスクを`work_task(work_record_id, display_order)`でまとめて取得する。
- 給与期間集計は期間開始日と終了日の範囲検索を使用する。
- 年間給与見込み累計は、年間区分の開始給与期間の開始日から選択中給与期間の終了日までを`work_record.work_date`で1回範囲検索する。
- 年間範囲の月額手当は`monthly_allowance.payroll_period_year_month`の両端を含む範囲検索で1回取得し、`ix_monthly_allowance_period`を使用する。
- 訪問、全タスク、設定スナップショットおよび祝日データを必要な単位でまとめて読み込み、訪問ごとまたはタスクごとのN+1クエリを避ける。
- 初期リリースでは日別合計、給与期間合計および年間給与見込み累計を正本として保存しない。年間締め月だけを`annual_summary_setting`へ保存し、各ホーム要求の開始時に1回取得する。
- 約21.9万訪問とそのタスクを持つデータベースで、保存後再計算、カレンダー表示および給与期間集計を代表端末上で2秒以内に完了させる。
- 製品上限ではないストレス条件として、1訪問100タスクの編集、計算、親子一括保存および形式3の往復をメモリ不足なく完了させる。
- 性能条件を満たさないことを計測で確認した場合だけ、再構築可能な集計キャッシュを追加する。

## 14. セキュリティ・プライバシー

- 利用者氏名、訪問先氏名、住所、電話番号など給与計算に不要な個人情報を格納する列を設けない。
- 永続ログ用テーブルを設けない。
- データベースはアプリ専用領域に保存し、他アプリから直接参照させない。
- 自動クラウドバックアップを無効にする。
- エクスポートファイルは暗号化しないため、保存先選択前に注意を表示する。
- SQLエラーや内部パスを利用者向けメッセージへ直接表示しない。
- SQLはパラメーター化し、表示名などの入力値をSQL文字列へ連結しない。

## 15. 実装前に確定する事項

- 使用するSQLiteアクセスライブラリとトランザクションAPI
- アプリ固有エクスポートファイルの拡張子
- マイグレーション失敗時の利用者向け復旧導線
