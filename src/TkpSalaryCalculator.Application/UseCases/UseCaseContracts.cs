using TkpSalaryCalculator.Application.Contracts;
using TkpSalaryCalculator.Domain.Models;
using TkpSalaryCalculator.Domain.ValueObjects;

namespace TkpSalaryCalculator.Application.UseCases;

/// <summary>再開可能な最小限の初期設定をプレゼンテーション層へ公開します。</summary>
public interface IInitialSetupUseCase
{
    /// <summary>現在の初期設定状態と、不足している最低限の要件を取得します。</summary>
    Task<InitialSetupStateDto> GetStateAsync(CancellationToken cancellationToken);

    /// <summary>初期設定を完了扱いにせず、再開に使用する安定したステップを保存します。</summary>
    Task SaveProgressAsync(string step, CancellationToken cancellationToken);

    /// <summary>最低限の設定を検証し、有効な場合にだけ初期設定を完了扱いにします。</summary>
    Task<InitialSetupStateDto> CompleteAsync(CancellationToken cancellationToken);
}

/// <summary>勤務入力の補助にのみ使用する現在のサービスプリセットを公開します。</summary>
public interface IServicePresetUseCase
{
    /// <summary>プリセットを候補の表示順で取得します。</summary>
    Task<IReadOnlyList<ServicePresetDto>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>既存勤務記録を変更せず、現在のプリセットを作成または置換します。</summary>
    Task<ServicePresetDto> SaveAsync(SaveServicePresetCommand command, CancellationToken cancellationToken);

    /// <summary>プリセットから作成済みの勤務記録を変更せず、現在のプリセットを削除します。</summary>
    Task DeleteAsync(ServicePresetId id, CancellationToken cancellationToken);
}

/// <summary>勤務記録のコマンドとクエリをプレゼンテーション層へ公開します。</summary>
public interface IWorkRecordUseCase
{
    /// <summary>勤務入力画面の候補、編集対象および祝日を一度に取得します。</summary>
    Task<WorkEditorScreenDto> GetEditorScreenAsync(
        DateOnly workDate,
        WorkRecordId? workRecordId,
        CancellationToken cancellationToken);

    /// <summary>候補ランキングを作成せず、指定日の名称表示と入力検証に必要な月設定だけを取得します。</summary>
    Task<MonthSettingsDto> GetSettingsForDateAsync(
        DateOnly workDate,
        CancellationToken cancellationToken);

    /// <summary>1 勤務日分の有効な設定、最終表示順のプリセット候補、および編集可能な推奨値を取得します。</summary>
    Task<WorkInputOptionsDto> GetInputOptionsAsync(
        DateOnly workDate,
        CancellationToken cancellationToken);

    /// <summary>現地日付 1 日分の保存済み勤務記録を取得します。</summary>
    Task<IReadOnlyList<WorkRecordDto>> GetForDateAsync(DateOnly workDate, CancellationToken cancellationToken);

    /// <summary>アプリケーションデータを保存または変更せず、入力を検証、正規化、および計算します。</summary>
    Task<WorkRecordPreviewDto> PreviewAsync(
        SaveWorkRecordCommand command,
        CancellationToken cancellationToken);

    /// <summary>画面ロード済みの設定と祝日を再利用して勤務内容をプレビューします。</summary>
    Task<WorkRecordPreviewDto> PreviewForEditorAsync(
        SaveWorkRecordCommand command,
        WorkEditorScreenDto screen,
        CancellationToken cancellationToken);

    /// <summary>1 件の勤務記録を検証、正規化、保存、および計算します。</summary>
    Task<SaveWorkRecordResultDto> SaveAsync(SaveWorkRecordCommand command, CancellationToken cancellationToken);

    /// <summary>プレゼンテーション層で利用者の確認を得た後、勤務記録を 1 件削除します。</summary>
    Task DeleteAsync(WorkRecordId id, CancellationToken cancellationToken);

    /// <summary>日単位の複製に使用する未保存の確認データを構築します。</summary>
    Task<CopyDayPreviewDto> PreviewCopyDayAsync(
        DateOnly sourceDate,
        DateOnly targetDate,
        CancellationToken cancellationToken);

    /// <summary>指定日の全記録を、別の日付の独立した記録として複製します。</summary>
    Task<IReadOnlyList<SaveWorkRecordResultDto>> CopyDayAsync(
        DateOnly sourceDate,
        DateOnly targetDate,
        CopyDayConfirmationToken confirmationToken,
        CancellationToken cancellationToken);
}

/// <summary>カレンダーと給与の読み取りモデルをプレゼンテーション層へ公開します。</summary>
public interface ISalaryQueryUseCase
{
    /// <summary>月間セルと初期選択日のサマリーを同じ範囲読取から取得します。</summary>
    Task<CalendarMonthScreenDto> GetCalendarMonthScreenAsync(
        YearMonth yearMonth,
        DateOnly selectedDate,
        CancellationToken cancellationToken);

    /// <summary>日別給与行と基本シフト候補を同じ読取コンテキストから取得します。</summary>
    Task<DayScreenDto> GetDayScreenAsync(
        DateOnly workDate,
        CancellationToken cancellationToken);

    /// <summary>指定された暦月の日別概要を取得します。</summary>
    Task<IReadOnlyList<CalendarDayDto>> GetCalendarMonthAsync(
        YearMonth yearMonth,
        CancellationToken cancellationToken);

    /// <summary>指定日 1 日分の詳細な給与結果を取得します。</summary>
    Task<DailySalaryDto> GetDayAsync(DateOnly workDate, CancellationToken cancellationToken);

    /// <summary>直接適用する月額手当を含む給与期間の概要を取得します。</summary>
    Task<PayrollPeriodSummaryDto> GetPayrollPeriodAsync(
        PayrollPeriodKey payrollPeriodKey,
        CancellationToken cancellationToken);
}

/// <summary>不変の月設定に対する操作をプレゼンテーション層へ公開します。</summary>
public interface IMonthSettingsUseCase
{
    /// <summary>表示だけを目的としたデータを作成せず、有効な設定を取得します。</summary>
    Task<MonthSettingsDto> GetAsync(YearMonth yearMonth, CancellationToken cancellationToken);

    /// <summary>完全な複製置換による影響を検証および計算します。</summary>
    Task<SettingReplacementPreviewDto> PreviewReplacementAsync(
        YearMonth yearMonth,
        SettingSnapshotReplacementDto replacement,
        CancellationToken cancellationToken);

    /// <summary>現在のスナップショットを原子的に複製して完全な置換を適用し、対象月だけの参照先を変更します。</summary>
    Task<MonthSettingsDto> CloneAndReplaceAsync(
        YearMonth yearMonth,
        SettingSnapshotReplacementDto replacement,
        SettingReplacementConfirmationToken confirmationToken,
        CancellationToken cancellationToken);

    /// <summary>
    /// 対象年月の設定スナップショット差替えと、全期間共通のサービス入力候補の変更を
    /// 1つのトランザクションで確定します。
    /// </summary>
    Task<MonthSettingsDto> CloneAndReplaceWithServicePresetAsync(
        YearMonth yearMonth,
        SettingSnapshotReplacementDto replacement,
        SettingReplacementConfirmationToken confirmationToken,
        ServicePresetChangeCommand presetChange,
        CancellationToken cancellationToken);

    /// <summary>選択月を前月の給与設定で置換した場合の結果をプレビューします。</summary>
    Task<SettingReplacementPreviewDto> PreviewCopyPreviousMonthAsync(
        YearMonth yearMonth,
        CancellationToken cancellationToken);

    /// <summary>対象月の新しい祝日バージョンを維持しながら、前月の給与設定を原子的に複製します。</summary>
    Task<MonthSettingsDto> CopyPreviousMonthAsync(
        YearMonth yearMonth,
        SettingReplacementConfirmationToken confirmationToken,
        CancellationToken cancellationToken);
}

/// <summary>給与期間ルールと直接適用する月額手当をプレゼンテーション層へ公開します。</summary>
public interface IPayrollPeriodSettingsUseCase
{
    /// <summary>指定した日付を含む給与算定期間を、締め日履歴から取得します。</summary>
    Task<PayrollPeriod> FindPeriodAsync(
        DateOnly localDate,
        CancellationToken cancellationToken);

    /// <summary>締め日変更後の最初の給与期間を、副作用なく現在の期間と比較します。</summary>
    Task<ClosingRuleReplacementPreviewDto> PreviewClosingRuleReplacementAsync(
        ReplaceClosingRuleCommand command,
        CancellationToken cancellationToken);

    /// <summary>指定した給与期間キーに対して有効な締め日ルールを取得します。</summary>
    /// <returns>有効なルール。初期設定で締め日ルールの履歴がまだ作成されていない場合は <see langword="null"/>。</returns>
    Task<EffectiveClosingRuleDto?> GetClosingRuleAsync(
        PayrollPeriodKey payrollPeriodKey,
        CancellationToken cancellationToken);

    /// <summary>指定した給与期間の月から有効な締め日ルールを原子的に置換します。</summary>
    Task ReplaceClosingRuleAsync(
        ReplaceClosingRuleCommand command,
        ClosingRuleReplacementConfirmationToken confirmationToken,
        CancellationToken cancellationToken);

    /// <summary>1 給与期間分の手当をすべて取得します。</summary>
    Task<IReadOnlyList<MonthlyAllowanceDto>> GetAllowancesAsync(
        PayrollPeriodKey payrollPeriodKey,
        CancellationToken cancellationToken);

    /// <summary>給与期間の手当を 1 件作成または置換します。</summary>
    Task<MonthlyAllowanceDto> SaveAllowanceAsync(
        SaveMonthlyAllowanceCommand command,
        CancellationToken cancellationToken);

    /// <summary>給与期間の手当を 1 件削除します。</summary>
    Task DeleteAllowanceAsync(MonthlyAllowanceId id, CancellationToken cancellationToken);
}

/// <summary>バックアップ通知の表示状態と延期操作をプレゼンテーション層へ公開します。</summary>
public interface IBackupReminderUseCase
{
    /// <summary>指定された端末現地日付に通知を表示する必要があるかどうかを判定します。</summary>
    Task<BackupReminderStateDto> GetStateAsync(
        DateOnly localToday,
        CancellationToken cancellationToken);

    /// <summary>指定された端末現地日付を基準にアプリケーション層で計算し、通知を 7 日間延期します。</summary>
    Task<BackupReminderStateDto> DeferForSevenDaysAsync(
        DateOnly localToday,
        CancellationToken cancellationToken);
}

/// <summary>現在の基本シフト管理と確認済みの反映操作をプレゼンテーション層へ公開します。</summary>
public interface IBasicShiftUseCase
{
    /// <summary>指定曜日の現在のシフトを表示順で取得します。</summary>
    Task<IReadOnlyList<BasicShiftDto>> GetForWeekdayAsync(
        DayOfWeek weekday,
        CancellationToken cancellationToken);

    /// <summary>現在の基本シフトを作成または置換します。</summary>
    Task<BasicShiftDto> SaveAsync(SaveBasicShiftCommand command, CancellationToken cancellationToken);

    /// <summary>基本シフトから作成済みの勤務記録を変更せず、現在の基本シフトを削除します。</summary>
    Task DeleteAsync(BasicShiftId id, CancellationToken cancellationToken);

    /// <summary>指定日 1 日分の未保存の反映プレビューを構築します。</summary>
    Task<BasicShiftPreviewDto> PreviewForDateAsync(DateOnly workDate, CancellationToken cancellationToken);

    /// <summary>選択した候補を独立した勤務記録として原子的に保存します。</summary>
    Task<IReadOnlyList<SaveWorkRecordResultDto>> ApplyAsync(
        ApplyBasicShiftsCommand command,
        CancellationToken cancellationToken);
}

/// <summary>単一ファイルへのストリーミングエクスポートと、確認済みの全データ置換インポートを公開します。</summary>
public interface IDataTransferUseCase
{
    /// <summary>現在のデータ転送形式識別子と、独立してバージョン管理される形式番号を取得します。</summary>
    Task<DataTransferFormatDto> GetFormatAsync(CancellationToken cancellationToken);

    /// <summary>呼び出し元が所有するストリームへエクスポート文書を逐次書き込みします。</summary>
    /// <param name="destination">呼び出し元が所有する書き込み可能なストリーム。このメソッドでは破棄または閉じてはいけません。</param>
    /// <param name="appVersion">エクスポートヘッダーへ書き込むアプリケーションバージョン。</param>
    /// <param name="cancellationToken">非同期入出力を中止します。</param>
    Task ExportAsync(Stream destination, string appVersion, CancellationToken cancellationToken);

    /// <summary>本番データを変更せず、インポートを逐次読み取り、検証して準備します。</summary>
    /// <param name="source">呼び出し元が所有する読み取り可能なストリーム。このメソッドでは破棄または閉じてはならず、シークを要求してもいけません。</param>
    /// <param name="cancellationToken">非同期入出力を中止します。</param>
    /// <returns>利用者の確認後に確定できるプレビュー用トークン。</returns>
    Task<ImportPreviewDto> PrepareImportAsync(Stream source, CancellationToken cancellationToken);

    /// <summary>事前に準備および検証したインポートで本番データをすべて原子的に置換します。</summary>
    Task CommitImportAsync(PreparedImportId preparedImportId, CancellationToken cancellationToken);

    /// <summary>準備済みインポートとその一時データを破棄します。</summary>
    Task DiscardImportAsync(PreparedImportId preparedImportId, CancellationToken cancellationToken);
}
