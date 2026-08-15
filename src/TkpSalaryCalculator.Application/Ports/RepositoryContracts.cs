using TkpSalaryCalculator.Application.Contracts;
using TkpSalaryCalculator.Domain.Models;
using TkpSalaryCalculator.Domain.ValueObjects;

namespace TkpSalaryCalculator.Application.Ports;

/// <summary>Persists resumable application setup metadata.</summary>
public interface IAppMetadataRepository
{
    /// <summary>Gets the persisted single-row metadata.</summary>
    Task<AppMetadata> GetAsync(CancellationToken cancellationToken);

    /// <summary>Persists initial-setup progress and its initial snapshot reference in the current transaction.</summary>
    Task SetInitialSetupAsync(
        InitialSetupStatus status,
        string? step,
        SettingSnapshotId? initialSnapshotId,
        CancellationToken cancellationToken);

    /// <summary>Persists the independently versioned export format in the current transaction.</summary>
    Task SetExportFormatVersionAsync(int exportFormatVersion, CancellationToken cancellationToken);

    /// <summary>Records the last committed data change using a supplied UTC instant.</summary>
    Task SetLastDataChangedAtUtcAsync(DateTimeOffset changedAtUtc, CancellationToken cancellationToken);

    /// <summary>Records the last successful export using a supplied UTC instant.</summary>
    Task SetLastExportedAtUtcAsync(DateTimeOffset exportedAtUtc, CancellationToken cancellationToken);

    /// <summary>Persists the device-local date before which the backup reminder remains hidden.</summary>
    Task SetBackupReminderDeferredUntilDateAsync(
        DateOnly? deferredUntilDate,
        CancellationToken cancellationToken);
}

/// <summary>Persists current service presets used only for input assistance.</summary>
public interface IServicePresetRepository
{
    /// <summary>Gets presets in configured display order.</summary>
    Task<IReadOnlyList<ServicePresetDto>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>Persists a current preset in the current transaction.</summary>
    Task UpsertAsync(ServicePresetDto preset, CancellationToken cancellationToken);

    /// <summary>Deletes a current preset while allowing existing work records to retain only provenance.</summary>
    Task DeleteAsync(ServicePresetId id, CancellationToken cancellationToken);
}

/// <summary>Persists normalized work records without exposing storage technology.</summary>
public interface IWorkRecordRepository
{
    /// <summary>Determines whether at least one persisted work record exists.</summary>
    Task<bool> AnyAsync(CancellationToken cancellationToken);

    /// <summary>Finds the most recently confirmed work record without loading all records.</summary>
    Task<WorkRecordDto?> FindMostRecentAsync(CancellationToken cancellationToken);

    /// <summary>Gets persisted work-record usage counts grouped by source service preset without streaming all records to Application.</summary>
    Task<IReadOnlyDictionary<ServicePresetId, long>> GetServicePresetUsageCountsAsync(
        CancellationToken cancellationToken);

    /// <summary>Finds one work record by identifier.</summary>
    Task<WorkRecordDto?> FindAsync(WorkRecordId id, CancellationToken cancellationToken);

    /// <summary>Streams records in ascending date and stable identifier order.</summary>
    IAsyncEnumerable<WorkRecordDto> StreamRangeAsync(
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken);

    /// <summary>Persists a normalized record in the current transaction.</summary>
    Task UpsertAsync(WorkRecordDto workRecord, CancellationToken cancellationToken);

    /// <summary>Deletes a record in the current transaction.</summary>
    Task DeleteAsync(WorkRecordId id, CancellationToken cancellationToken);
}

/// <summary>Reads settings and performs the only supported mutation of immutable month snapshots.</summary>
public interface ISettingSnapshotRepository
{
    /// <summary>Gets the snapshot explicitly referenced by a month, if one has been created.</summary>
    Task<SettingSnapshot?> FindForMonthAsync(YearMonth yearMonth, CancellationToken cancellationToken);

    /// <summary>Gets the effective inherited snapshot without creating a month row.</summary>
    Task<SettingSnapshot> GetEffectiveForMonthAsync(YearMonth yearMonth, CancellationToken cancellationToken);

    /// <summary>Creates the month reference when first needed, carrying forward salary settings and selecting the latest verified holiday data as specified.</summary>
    Task<SettingSnapshot> EnsureForMonthAsync(YearMonth yearMonth, CancellationToken cancellationToken);

    /// <summary>Atomically clones the complete current snapshot, replaces its salary settings, and repoints only the target month.</summary>
    /// <remarks>This contract intentionally exposes no API that updates a referenced snapshot or any of its child rows directly.</remarks>
    Task<SettingSnapshot> CloneAndReplaceMonthSnapshotAsync(
        YearMonth yearMonth,
        SettingSnapshotReplacementDto replacement,
        HolidayCalendarVersionId holidayCalendarVersionId,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken);
}

/// <summary>Persists closing-rule history.</summary>
public interface IClosingRuleRepository
{
    /// <summary>Gets closing rules ordered by their effective payroll-period month.</summary>
    Task<IReadOnlyList<ClosingRule>> GetHistoryAsync(CancellationToken cancellationToken);

    /// <summary>Atomically replaces the rule at one effective month without modifying earlier history.</summary>
    Task ReplaceEffectiveRuleAsync(ClosingRule rule, CancellationToken cancellationToken);
}

/// <summary>Persists allowances that are applied directly to payroll periods.</summary>
public interface IMonthlyAllowanceRepository
{
    /// <summary>Gets allowances for one period.</summary>
    Task<IReadOnlyList<MonthlyAllowance>> GetForPeriodAsync(
        PayrollPeriodKey payrollPeriodKey,
        CancellationToken cancellationToken);

    /// <summary>Persists an allowance in the current transaction.</summary>
    Task UpsertAsync(MonthlyAllowance allowance, CancellationToken cancellationToken);

    /// <summary>Deletes an allowance in the current transaction.</summary>
    Task DeleteAsync(MonthlyAllowanceId id, CancellationToken cancellationToken);
}

/// <summary>Persists the current basic shifts.</summary>
public interface IBasicShiftRepository
{
    /// <summary>Gets shifts for a weekday in display order.</summary>
    Task<IReadOnlyList<BasicShiftDto>> GetForWeekdayAsync(
        DayOfWeek weekday,
        CancellationToken cancellationToken);

    /// <summary>Finds one current shift.</summary>
    Task<BasicShiftDto?> FindAsync(BasicShiftId id, CancellationToken cancellationToken);

    /// <summary>Persists a current shift in the current transaction.</summary>
    Task UpsertAsync(BasicShiftDto basicShift, CancellationToken cancellationToken);

    /// <summary>Deletes a current shift without deleting its identifiers from created work records.</summary>
    Task DeleteAsync(BasicShiftId id, CancellationToken cancellationToken);
}

/// <summary>Reads versioned holiday calendars.</summary>
public interface IHolidayCalendarRepository
{
    /// <summary>Gets one complete, immutable holiday calendar version.</summary>
    Task<HolidayCalendar> GetAsync(
        HolidayCalendarVersionId versionId,
        CancellationToken cancellationToken);

    /// <summary>Gets the latest verified holiday version by source reference date.</summary>
    Task<HolidayCalendarVersionId> GetLatestVerifiedVersionIdAsync(CancellationToken cancellationToken);
}
