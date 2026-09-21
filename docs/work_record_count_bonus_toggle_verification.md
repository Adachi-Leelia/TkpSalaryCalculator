# 件数手当ON／OFF選択 タスク6検証記録

## 1. 判定と対象

- 実施日：2026-09-21。対象：`ea71508`を基点とする作業ツリーに本タスクの追加テストを適用した状態。
- 自動検証・仕様照合：完了。計574件成功、失敗0、スキップ0。製品コードの変更なし。
- Android操作：未実施。タスク6全体・機能受入・リリース受入は未完了。
- 不足していたCB-08の直接検証を8ケース追加した。自動検証の範囲では仕様との不一致を検出しなかった。

照合元は[タスク定義書7節](work_record_count_bonus_toggle_task_definition.md#7-受入条件検証)、[要件定義書](requirements.md)のFR-COUNT-04／FR-WORK-08／FR-DATA-08／AC-18、[給与計算仕様書](salary_calculation_specification.md)8～10節、[画面仕様書](screen_specification.md)7.4～7.5／10～11節、[DB仕様書](database_specification.md)5.12／8／11～12節、[設定履歴データモデル](setting_history_data_model.md)4.7／6～7節、[テスト仕様書](test_specification.md)20.2節。[ADR-0001](adr/0001-ui-framework.md)の層責務・MAUI採用は変更しない。

## 2. 実行環境・コマンド

Windowsホスト、.NET SDK 10.0.400、Debug構成で実行した。AppテストはMAUI非依存のPresentationコードを検証する.NETテストであり、Android UIを起動していない。

```powershell
dotnet test TkpSalaryCalculator.sln --no-restore --logger trx --results-directory artifacts/test-results/count-bonus-task6
# HIST-019追加後、変更したApplicationテストを全件再実行
dotnet test tests/TkpSalaryCalculator.Application.Tests --no-restore --logger trx --results-directory artifacts/test-results/count-bonus-task6-final
dotnet build src/TkpSalaryCalculator.App/TkpSalaryCalculator.App.csproj --no-restore
git diff --check
```

| 対象 | 成功 | 失敗／スキップ | 証跡 |
| --- | --- | --- | --- |
| Domain | 154 | 0／0 | `count-bonus-task6/*16_39_05_net10.0.trx` |
| Application（追加後） | 159 | 0／0 | `count-bonus-task6-final/*16_44_59_net10.0.trx` |
| Infrastructure | 86 | 0／0 | `count-bonus-task6/*16_39_24_net10.0.trx` |
| App | 175 | 0／0 | `count-bonus-task6/*16_39_23_net10.0.trx` |
| Android Debugビルド | 成功 | 警告0／エラー0 | `net10.0-android` |

TRXはローカルの`artifacts/test-results/`以下に保存（Git管理対象外）。最初の全件実行は566件。Applicationへ8ケース追加後の最終件数が574件であり、途中の再実行分を重複加算していない。

## 3. 受入条件との対応

以下の自動テストはすべて成功。クラス名・メソッド名の接頭辞で実装を特定する。Androidによる画面操作の証明とは区別する。

| 条件 | 実装テスト・確認内容 | Androidで残る確認 |
| --- | --- | --- |
| CB-01 | App `WORK021_ReopenRestoresOffAndNextNewVisitDefaultsOn`、Infrastructure `DB027_DB028_*`：新規ON、編集時OFF、DB再オープン後の保持 | 再起動後の画面復元 |
| CB-02 | Domain `Calc040_*`：1,950円／1,800円、基本給与と割増の一致。Appのプレビュー・内訳表示テスト | スイッチと金額表示 |
| CB-03 | Domain `Calc041_*`：同じルールは訪問に1回 | — |
| CB-04 | Domain `Calc042_*`：OFFですべてのルールを除外 | — |
| CB-05 | Domain `Calc043_*`、App `UI024_*`：適用なしでも選択を保持し、OFFと区別 | 表示の識別 |
| CB-06 | Domain `MixedSelectionsPreserveDailyPeriodAndAnnualTotals`：日計3,750円、2訪問、月額手当5,000円を1回加算して期間・年間8,750円。Application `Work_SavedMixedSelectionsFlowToDailyMonthlyAndAnnualSalary`：登録後の集計と編集差額 | 日別・カレンダー・期間・ホームの更新 |
| CB-07 | Application `Work_EditDateAndTasks_PreviewMatchesSavedSelection`、App `UI022_*`／`UI023_*`：対象日の設定、保存値、変更通知 | 画面間の金額一致 |
| CB-08 | 追加したApplication `HIST019_CountBonusSettingsChangesExcludeOffVisitsAndPreserveSelections`（8ケース） | — |
| CB-09 | Domain `Calc044_*`、Application `Work_OffWithMissingRate_*`、App `UI024_UI026_*`／`UI024_*`：未計算・警告付き保存、0円や合計の推測表示をしない | 警告と保存・内訳操作 |
| CB-10 | Application `Copy_MixedSelections_UsesTargetSettingsAndNewIds`／`Copy_SourceChangesInvalidateConfirmation` | 複製確認から内訳まで |
| CB-11 | Application `Shift_MixedSelections_*`／`Shift_ChangedConfirmationIsRejected`／`Shift_SimilarityIgnoresSelectionAndFailedBatchRollsBack`、App `SHIFT014_*`／`SHIFT016_*` | 候補ごとの切替・取消・確定 |
| CB-12 | App `UI022_*`／`UI023_*`：タスク操作・日付変更・未保存確認・古い非同期結果の排除 | 連続操作・戻る操作 |
| CB-13 | Application `Work_RetryIsIdempotentAndChangedSelectionConflicts`／`Work_ConcurrentSelectionChangeConflicts`／`Work_FailureRollsBackSelectionAndTasks_ThenRetrySucceeds`、Infrastructure `DB020_DB021_DB024_DB031_*` | — |
| CB-14 | Infrastructure `DB027_DB028_*`／`DB029_*`／`DB030_*`と既存連続移行テスト：給与集計維持、再移行なし、制約、ロールバック | — |
| CB-15 | Infrastructure `LegacyVersionOneImportBackfillsDecemberAnnualClosingMonth`／`DATA020_*`／`DATA019_DATA023_*`／`DATA024_*`：形式1～3のON補完、形式4で選択・構造・給与保持 | バックアップ復元後の画面 |
| CB-16 | Infrastructure `DATA025_*`／`DATA012_DATA026_*`：値欠落・不正型・非対応版、取消・失敗時の既存DB保持 | — |
| CB-17 | XAML上の48dp最小寸法、状態テキスト、読み上げ説明のバインドを確認。これは操作試験の代替ではない | `A11Y-010`の全操作 |

CB-08は金額変更・対象サービス変更・無効化・前月コピーの4操作を、OFFのみ／ON混在で確認する。OFFのみでは差額0円・影響0訪問、混在ではON訪問だけが影響対象となる。プレビューで保存しないこと、確定後の選択・給与額、前後月の設定不変も検証した。対応表は[Applicationテスト対応表](../tests/TkpSalaryCalculator.Application.Tests/SPEC_COVERAGE.md)に追加した。

## 4. 転送・集計性能

ON／OFF混在4,096件・219,000件の形式4逐次転送と、1訪問100タスクの編集・計算・保存・往復を通過した。219,000件の転送テスト全体は3分54.359秒（データ作成・検証を含む）であり、純粋な転送時間ではない。

約21.9万訪問の一時SQLiteで、ウォームアップ後3回の最悪値を記録した。索引利用・一括取得も既存テストで確認している。

| ホスト集計処理 | 1タスク／訪問 | 2タスク／訪問 |
| --- | --- | --- |
| PERF-001 日別再計算 | 12ms | 10ms |
| PERF-002 給与期間再計算 | 43ms | 53ms |
| PERF-003 カレンダー | 34ms | 52ms |
| PERF-004 期間切替 | 53ms | 56ms |
| PERF-005 期間集計 | 106ms | 48ms |
| PERF-005 内訳取得 | 4ms | 3ms |
| PERF-008 月次・年間同時集計 | 452ms | 434ms |

すべてホストの2秒基準以内。Androidの描画・入力応答性・端末メモリを測った結果ではなく、代表実機でのRelease計測は未実施。

## 5. Androidの未実施項目と再開手順

`adb devices -l`は接続端末なし。`ANDROID_HOME`のSDKに`emulator/emulator.exe`とシステムイメージがなく、ユーザーのAVDも見つからなかった。このためAndroid操作の結果・スクリーンショット・TalkBackの読み上げ確認は取得していない。

テスト仕様書5節のAPI 29、新しい対応OS、代表実機を用意し、端末・OS/API・APK版・文字倍率・TalkBack版・実施者・日時・結果を各ケースに記録する。専用の検証データを使用する。

前提データ：通常日に固定タスク給与1,000円＋800円、件数加算150円。不要な割増は設定しない。月末締め、月額手当5,000円、他の実績なし。別月には件数加算250円を用意する。

| 項目（すべて未実施） | 操作と合格条件 |
| --- | --- |
| 新規・編集・再起動 | 新規ONで1,950円、OFFで1,800円。保存・再編集・アプリ再起動でOFFを保持し、次の新規はON |
| UI-025 集計更新 | 同日ON／OFFを各1件保存。日計3,750円・2訪問、期間とホーム月次／年間8,750円。OFFをONにして保存すると日計3,900円、期間・月次／年間8,900円。内訳・カレンダーも一致 |
| 対象日・入力状態 | OFFでタスクの追加・編集・並べ替え・削除、サービス設定選択、日付変更後もOFFを維持。別月でONなら2,050円。未保存で戻ると確認し、取消でDBへ反映しない。連続切替後は最新状態の金額 |
| 複製・シフト | 混在日を別月に複製し、選択を保持して日計3,850円。基本シフト候補2件は初期ON、一方OFFで確定後も各選択を保持。取消時は実績・集計を変更しない。カレンダーと日別の両経路で確認 |
| 未計算・適用なし | 対象単価なしのOFF訪問は未計算を維持し、0円・訪問合計を推測表示しない。警告付き保存と内訳を操作可能。ONで適用ルールなしはOFFと文言で区別 |
| バックアップ復元 | ON／OFF混在データを形式4で出力し、検証用の空DBへ復元。再編集・内訳・各集計が一致 |
| A11Y-010 | 実績編集・カレンダーのシフト確認・日別候補でスイッチをタップ。標準／端末で設定可能な最大文字サイズでラベル、状態、保存、内訳が欠けず操作可能。TalkBackで対象訪問、加算有無、補足を認識し、切替後の状態を確認して保存・内訳まで操作可能 |
| 代表実機Release性能 | テスト仕様書18節のデータで、デバッガーなし・ウォームアップ後複数回実施し、描画を含むP0の最悪値2秒以内、転送中の応答性・復元・メモリを確認 |

Android操作を実施して証跡を追記し、不一致があれば修正・再検証した後にタスク6の完了状態を更新する。
