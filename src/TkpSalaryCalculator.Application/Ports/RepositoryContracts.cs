using TkpSalaryCalculator.Application.Contracts;
using TkpSalaryCalculator.Domain.Models;
using TkpSalaryCalculator.Domain.ValueObjects;

namespace TkpSalaryCalculator.Application.Ports;

/// <summary>再開可能なアプリケーション初期設定のメタデータを保存します。</summary>
public interface IAppMetadataRepository
{
    /// <summary>単一行で保存されたメタデータを取得します。</summary>
    Task<AppMetadata> GetAsync(CancellationToken cancellationToken);

    /// <summary>現在のトランザクション内で、初期設定の進捗と初期スナップショット参照を保存します。</summary>
    Task SetInitialSetupAsync(
        InitialSetupStatus status,
        string? step,
        SettingSnapshotId? initialSnapshotId,
        CancellationToken cancellationToken);

    /// <summary>現在のトランザクション内で、独立してバージョン管理されるエクスポート形式を保存します。</summary>
    Task SetExportFormatVersionAsync(int exportFormatVersion, CancellationToken cancellationToken);

    /// <summary>指定された UTC 日時を使用して、直近で確定したデータ変更を記録します。</summary>
    Task SetLastDataChangedAtUtcAsync(DateTimeOffset changedAtUtc, CancellationToken cancellationToken);

    /// <summary>指定された UTC 日時を使用して、直近で成功したエクスポートを記録します。</summary>
    Task SetLastExportedAtUtcAsync(DateTimeOffset exportedAtUtc, CancellationToken cancellationToken);

    /// <summary>バックアップ通知を非表示にする期限を表す端末現地日付を保存します。</summary>
    Task SetBackupReminderDeferredUntilDateAsync(
        DateOnly? deferredUntilDate,
        CancellationToken cancellationToken);
}

/// <summary>入力補助にのみ使用する現在のサービスプリセットを保存します。</summary>
public interface IServicePresetRepository
{
    /// <summary>プリセットを設定済みの表示順で取得します。</summary>
    Task<IReadOnlyList<ServicePresetDto>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>現在のトランザクション内で、現在のプリセットを保存します。</summary>
    Task UpsertAsync(ServicePresetDto preset, CancellationToken cancellationToken);

    /// <summary>既存勤務記録には作成元情報だけを残し、現在のプリセットを削除します。</summary>
    Task DeleteAsync(ServicePresetId id, CancellationToken cancellationToken);
}

/// <summary>ストレージ技術を公開せずに、正規化済み勤務記録を保存します。</summary>
public interface IWorkRecordRepository
{
    /// <summary>保存済み勤務記録が 1 件以上存在するかどうかを判定します。</summary>
    Task<bool> AnyAsync(CancellationToken cancellationToken);

    /// <summary>識別子で勤務記録を 1 件検索します。</summary>
    Task<WorkRecordDto?> FindAsync(WorkRecordId id, CancellationToken cancellationToken);

    /// <summary>新規保存操作の識別子で、既に確定した勤務記録を検索します。</summary>
    Task<WorkRecordDto?> FindBySaveOperationIdAsync(Guid operationId, CancellationToken cancellationToken);

    /// <summary>記録を日付の昇順、次に安定した識別子順でストリーミングします。</summary>
    IAsyncEnumerable<WorkRecordDto> StreamRangeAsync(
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken);

    /// <summary>現在のトランザクション内で、正規化済み記録を保存します。</summary>
    Task UpsertAsync(WorkRecordDto workRecord, CancellationToken cancellationToken);

    /// <summary>操作識別子の永続一意制約を使用して、新規勤務を一度だけ保存します。</summary>
    /// <returns>この呼出しで挿入した場合は <see langword="true"/>、既に同じ操作が確定済みの場合は <see langword="false"/>。</returns>
    Task<bool> TryInsertAsync(WorkRecordDto workRecord, Guid operationId, CancellationToken cancellationToken);

    /// <summary>現在のトランザクション内で記録を削除します。</summary>
    Task DeleteAsync(WorkRecordId id, CancellationToken cancellationToken);
}

/// <summary>設定を読み取り、不変の月別スナップショットに対して唯一対応する変更操作を実行します。</summary>
public interface ISettingSnapshotRepository
{
    /// <summary>識別子で不変の設定スナップショットを取得します。</summary>
    Task<SettingSnapshot?> FindAsync(SettingSnapshotId id, CancellationToken cancellationToken);

    /// <summary>作成済みの場合、月から明示的に参照されているスナップショットを取得します。</summary>
    Task<SettingSnapshot?> FindForMonthAsync(YearMonth yearMonth, CancellationToken cancellationToken);

    /// <summary>月行を作成せず、有効な継承スナップショットを取得します。</summary>
    Task<SettingSnapshot> GetEffectiveForMonthAsync(YearMonth yearMonth, CancellationToken cancellationToken);

    /// <summary>月行を作成せず、指定された各月の有効な継承スナップショットを有界にまとめて取得します。</summary>
    Task<IReadOnlyDictionary<YearMonth, SettingSnapshot>> GetEffectiveForMonthsAsync(
        IReadOnlyCollection<YearMonth> yearMonths,
        CancellationToken cancellationToken);

    /// <summary>最初に必要となった時点で月参照を作成し、給与設定を引き継いで、仕様に従い検証済みの最新祝日データを選択します。</summary>
    Task<SettingSnapshot> EnsureForMonthAsync(YearMonth yearMonth, CancellationToken cancellationToken);

    /// <summary>プレビュー時点の有効設定および祝日データ版が変わっていない場合だけ、対象年月を初めて使用するための月参照を作成します。</summary>
    /// <returns>プレビューと同じ設定を確定できた場合はそのスナップショット。設定が変わっている場合は <see langword="null"/>。</returns>
    Task<SettingSnapshot?> TryEnsureForMonthAsync(
        YearMonth yearMonth,
        SettingSnapshotId expectedEffectiveSnapshotId,
        HolidayCalendarVersionId expectedHolidayCalendarVersionId,
        CancellationToken cancellationToken);

    /// <summary>現在の完全なスナップショットを原子的に複製し、給与設定を置換して、対象月だけの参照先を変更します。</summary>
    /// <remarks>この契約では、参照済みスナップショットまたはその子行を直接更新する操作を意図的に公開しません。</remarks>
    Task<SettingSnapshot?> TryCloneAndReplaceMonthSnapshotAsync(
        YearMonth yearMonth,
        SettingSnapshotId expectedCurrentSnapshotId,
        SettingSnapshotReplacementDto replacement,
        HolidayCalendarVersionId holidayCalendarVersionId,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken);
}

/// <summary>締め日ルールの履歴を保存します。</summary>
public interface IClosingRuleRepository
{
    /// <summary>締め日履歴全体と、その同じ読取時点の不透明な版を取得します。</summary>
    Task<ClosingRuleHistorySnapshot> GetSnapshotAsync(CancellationToken cancellationToken);

    /// <summary>締め日ルールを有効な給与期間の月順に取得します。</summary>
    Task<IReadOnlyList<ClosingRule>> GetHistoryAsync(CancellationToken cancellationToken);

    /// <summary>過去の履歴を変更せず、指定した有効月のルールを原子的に置換します。</summary>
    Task<bool> TryReplaceEffectiveRuleAsync(
        ClosingRule rule,
        ClosingRuleHistoryVersion expectedVersion,
        CancellationToken cancellationToken);
}

/// <summary>給与期間に直接適用する手当を保存します。</summary>
public interface IMonthlyAllowanceRepository
{
    /// <summary>1 期間分の手当を取得します。</summary>
    Task<IReadOnlyList<MonthlyAllowance>> GetForPeriodAsync(
        PayrollPeriodKey payrollPeriodKey,
        CancellationToken cancellationToken);

    /// <summary>現在のトランザクション内で手当を保存します。</summary>
    Task UpsertAsync(MonthlyAllowance allowance, CancellationToken cancellationToken);

    /// <summary>現在のトランザクション内で手当を削除します。</summary>
    Task DeleteAsync(MonthlyAllowanceId id, CancellationToken cancellationToken);
}

/// <summary>現在の基本シフトを保存します。</summary>
public interface IBasicShiftRepository
{
    /// <summary>指定曜日のシフトを表示順で取得します。</summary>
    Task<IReadOnlyList<BasicShiftDto>> GetForWeekdayAsync(
        DayOfWeek weekday,
        CancellationToken cancellationToken);

    /// <summary>指定された各曜日のシフトを表示順で有界にまとめて取得します。</summary>
    Task<IReadOnlyDictionary<DayOfWeek, IReadOnlyList<BasicShiftDto>>> GetForWeekdaysAsync(
        IReadOnlyCollection<DayOfWeek> weekdays,
        CancellationToken cancellationToken);

    /// <summary>現在のシフトを 1 件検索します。</summary>
    Task<BasicShiftDto?> FindAsync(BasicShiftId id, CancellationToken cancellationToken);

    /// <summary>現在のトランザクション内で、現在のシフトを保存します。</summary>
    Task UpsertAsync(BasicShiftDto basicShift, CancellationToken cancellationToken);

    /// <summary>作成済み勤務記録から識別子を削除せず、現在のシフトを削除します。</summary>
    Task DeleteAsync(BasicShiftId id, CancellationToken cancellationToken);
}

/// <summary>バージョン管理された祝日カレンダーを読み取ります。</summary>
public interface IHolidayCalendarRepository
{
    /// <summary>完全で不変の祝日カレンダーバージョンを 1 件取得します。</summary>
    Task<HolidayCalendar> GetAsync(
        HolidayCalendarVersionId versionId,
        CancellationToken cancellationToken);

    /// <summary>完全で不変の祝日カレンダーバージョンを指定された版ごとに有界にまとめて取得します。</summary>
    Task<IReadOnlyDictionary<HolidayCalendarVersionId, HolidayCalendar>> GetManyAsync(
        IReadOnlyCollection<HolidayCalendarVersionId> versionIds,
        CancellationToken cancellationToken);

    /// <summary>情報源の基準日によって、検証済みの最新祝日バージョンを取得します。</summary>
    Task<HolidayCalendarVersionId> GetLatestVerifiedVersionIdAsync(CancellationToken cancellationToken);
}
