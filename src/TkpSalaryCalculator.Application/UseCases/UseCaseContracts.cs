using TkpSalaryCalculator.Application.Contracts;
using TkpSalaryCalculator.Domain.ValueObjects;

namespace TkpSalaryCalculator.Application.UseCases;

/// <summary>Exposes resumable minimum initial setup to Presentation.</summary>
public interface IInitialSetupUseCase
{
    /// <summary>Gets the current setup state and missing minimum requirements.</summary>
    Task<InitialSetupStateDto> GetStateAsync(CancellationToken cancellationToken);

    /// <summary>Saves a stable resume step without marking setup complete.</summary>
    Task SaveProgressAsync(string step, CancellationToken cancellationToken);

    /// <summary>Validates the minimum settings and marks setup complete only when valid.</summary>
    Task<InitialSetupStateDto> CompleteAsync(CancellationToken cancellationToken);
}

/// <summary>Exposes current service presets used only as work-input assistance.</summary>
public interface IServicePresetUseCase
{
    /// <summary>Gets presets in candidate display order.</summary>
    Task<IReadOnlyList<ServicePresetDto>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>Creates or replaces a current preset without changing existing work records.</summary>
    Task<ServicePresetDto> SaveAsync(SaveServicePresetCommand command, CancellationToken cancellationToken);

    /// <summary>Deletes a current preset without changing work records created from it.</summary>
    Task DeleteAsync(ServicePresetId id, CancellationToken cancellationToken);
}

/// <summary>Exposes work-record commands and queries to Presentation.</summary>
public interface IWorkRecordUseCase
{
    /// <summary>Gets effective settings, final ordered preset candidates, and editable suggested values for one work date.</summary>
    Task<WorkInputOptionsDto> GetInputOptionsAsync(
        DateOnly workDate,
        CancellationToken cancellationToken);

    /// <summary>Gets the saved work records for one local date.</summary>
    Task<IReadOnlyList<WorkRecordDto>> GetForDateAsync(DateOnly workDate, CancellationToken cancellationToken);

    /// <summary>Validates, normalizes, and calculates input without persisting or otherwise changing application data.</summary>
    Task<WorkRecordPreviewDto> PreviewAsync(
        SaveWorkRecordCommand command,
        CancellationToken cancellationToken);

    /// <summary>Validates, normalizes, persists, and calculates one work record.</summary>
    Task<SaveWorkRecordResultDto> SaveAsync(SaveWorkRecordCommand command, CancellationToken cancellationToken);

    /// <summary>Deletes one work record after Presentation has obtained user confirmation.</summary>
    Task DeleteAsync(WorkRecordId id, CancellationToken cancellationToken);

    /// <summary>Builds the unpersisted confirmation data for a day copy.</summary>
    Task<CopyDayPreviewDto> PreviewCopyDayAsync(
        DateOnly sourceDate,
        DateOnly targetDate,
        CancellationToken cancellationToken);

    /// <summary>Copies every record from one date into independent records on another date.</summary>
    Task<IReadOnlyList<SaveWorkRecordResultDto>> CopyDayAsync(
        DateOnly sourceDate,
        DateOnly targetDate,
        CancellationToken cancellationToken);
}

/// <summary>Exposes calendar and salary read models to Presentation.</summary>
public interface ISalaryQueryUseCase
{
    /// <summary>Gets day summaries for the requested calendar month.</summary>
    Task<IReadOnlyList<CalendarDayDto>> GetCalendarMonthAsync(
        YearMonth yearMonth,
        CancellationToken cancellationToken);

    /// <summary>Gets a detailed salary result for one date.</summary>
    Task<DailySalaryDto> GetDayAsync(DateOnly workDate, CancellationToken cancellationToken);

    /// <summary>Gets a payroll-period summary including direct monthly allowances.</summary>
    Task<PayrollPeriodSummaryDto> GetPayrollPeriodAsync(
        PayrollPeriodKey payrollPeriodKey,
        CancellationToken cancellationToken);
}

/// <summary>Exposes immutable month-setting operations to Presentation.</summary>
public interface IMonthSettingsUseCase
{
    /// <summary>Gets the effective settings without creating data merely for display.</summary>
    Task<MonthSettingsDto> GetAsync(YearMonth yearMonth, CancellationToken cancellationToken);

    /// <summary>Validates and calculates the impact of a complete cloned replacement.</summary>
    Task<SettingReplacementPreviewDto> PreviewReplacementAsync(
        YearMonth yearMonth,
        SettingSnapshotReplacementDto replacement,
        CancellationToken cancellationToken);

    /// <summary>Atomically clones the current snapshot, applies the complete replacement, and repoints only the target month.</summary>
    Task<MonthSettingsDto> CloneAndReplaceAsync(
        YearMonth yearMonth,
        SettingSnapshotReplacementDto replacement,
        CancellationToken cancellationToken);

    /// <summary>Previews replacement of the selected month with the previous calendar month's salary settings.</summary>
    Task<SettingReplacementPreviewDto> PreviewCopyPreviousMonthAsync(
        YearMonth yearMonth,
        CancellationToken cancellationToken);

    /// <summary>Atomically copies the previous month's salary settings while preserving the target month's newer holiday version.</summary>
    Task<MonthSettingsDto> CopyPreviousMonthAsync(
        YearMonth yearMonth,
        CancellationToken cancellationToken);
}

/// <summary>Exposes payroll-period rules and direct monthly allowances to Presentation.</summary>
public interface IPayrollPeriodSettingsUseCase
{
    /// <summary>Gets the closing rule effective for the specified payroll-period key.</summary>
    /// <returns>The effective rule, or <see langword="null"/> when initial setup has not created closing-rule history.</returns>
    Task<EffectiveClosingRuleDto?> GetClosingRuleAsync(
        PayrollPeriodKey payrollPeriodKey,
        CancellationToken cancellationToken);

    /// <summary>Atomically replaces the closing rule effective from the specified payroll-period month.</summary>
    Task ReplaceClosingRuleAsync(ReplaceClosingRuleCommand command, CancellationToken cancellationToken);

    /// <summary>Gets all allowances for one payroll period.</summary>
    Task<IReadOnlyList<MonthlyAllowanceDto>> GetAllowancesAsync(
        PayrollPeriodKey payrollPeriodKey,
        CancellationToken cancellationToken);

    /// <summary>Creates or replaces one payroll-period allowance.</summary>
    Task<MonthlyAllowanceDto> SaveAllowanceAsync(
        SaveMonthlyAllowanceCommand command,
        CancellationToken cancellationToken);

    /// <summary>Deletes one payroll-period allowance.</summary>
    Task DeleteAllowanceAsync(MonthlyAllowanceId id, CancellationToken cancellationToken);
}

/// <summary>Exposes backup-reminder visibility and deferral to Presentation.</summary>
public interface IBackupReminderUseCase
{
    /// <summary>Evaluates whether the reminder should be shown on the supplied device-local date.</summary>
    Task<BackupReminderStateDto> GetStateAsync(
        DateOnly localToday,
        CancellationToken cancellationToken);

    /// <summary>Defers the reminder for seven days calculated by Application from the supplied device-local date.</summary>
    Task<BackupReminderStateDto> DeferForSevenDaysAsync(
        DateOnly localToday,
        CancellationToken cancellationToken);
}

/// <summary>Exposes current basic-shift management and confirmed application to Presentation.</summary>
public interface IBasicShiftUseCase
{
    /// <summary>Gets current shifts for one weekday in display order.</summary>
    Task<IReadOnlyList<BasicShiftDto>> GetForWeekdayAsync(
        DayOfWeek weekday,
        CancellationToken cancellationToken);

    /// <summary>Creates or replaces a current basic shift.</summary>
    Task<BasicShiftDto> SaveAsync(SaveBasicShiftCommand command, CancellationToken cancellationToken);

    /// <summary>Deletes a current basic shift without changing work records already created from it.</summary>
    Task DeleteAsync(BasicShiftId id, CancellationToken cancellationToken);

    /// <summary>Builds an unpersisted application preview for a single date.</summary>
    Task<BasicShiftPreviewDto> PreviewForDateAsync(DateOnly workDate, CancellationToken cancellationToken);

    /// <summary>Atomically persists selected candidates as independent work records.</summary>
    Task<IReadOnlyList<SaveWorkRecordResultDto>> ApplyAsync(
        ApplyBasicShiftsCommand command,
        CancellationToken cancellationToken);
}

/// <summary>Exposes single-file streaming export and confirmed whole-data replacement import.</summary>
public interface IDataTransferUseCase
{
    /// <summary>Gets the current data-transfer format identifier and independently versioned format number.</summary>
    Task<DataTransferFormatDto> GetFormatAsync(CancellationToken cancellationToken);

    /// <summary>Writes an export document incrementally to a caller-owned stream.</summary>
    /// <param name="destination">A writable stream owned by the caller. The method must not dispose or close it.</param>
    /// <param name="appVersion">The application version written to the export header.</param>
    /// <param name="cancellationToken">Stops the asynchronous I/O.</param>
    Task ExportAsync(Stream destination, string appVersion, CancellationToken cancellationToken);

    /// <summary>Reads, validates, and stages an import incrementally without changing live data.</summary>
    /// <param name="source">A readable stream owned by the caller. The method must not dispose or close it and must not require seeking.</param>
    /// <param name="cancellationToken">Stops the asynchronous I/O.</param>
    /// <returns>A preview token that can later be committed after user confirmation.</returns>
    Task<ImportPreviewDto> PrepareImportAsync(Stream source, CancellationToken cancellationToken);

    /// <summary>Atomically replaces all live data with a previously prepared and validated import.</summary>
    Task CommitImportAsync(PreparedImportId preparedImportId, CancellationToken cancellationToken);

    /// <summary>Discards a prepared import and its temporary data.</summary>
    Task DiscardImportAsync(PreparedImportId preparedImportId, CancellationToken cancellationToken);
}
