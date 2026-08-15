using TkpSalaryCalculator.Domain.Contracts;
using TkpSalaryCalculator.Domain.Models;
using TkpSalaryCalculator.Domain.ValueObjects;

namespace TkpSalaryCalculator.Application.Contracts;

/// <summary>Describes a validation or business-rule issue for Presentation.</summary>
/// <param name="Code">A stable machine-readable code.</param>
/// <param name="Field">The related input field, when any.</param>
/// <param name="Message">A safe, user-facing Japanese message.</param>
public sealed record IssueDto(string Code, string? Field, string Message);

/// <summary>Represents a completed command with optional warnings.</summary>
/// <param name="Warnings">Non-blocking issues that Presentation should show.</param>
public sealed record CommandResultDto(IReadOnlyList<IssueDto> Warnings);

/// <summary>Describes a normalized work record for Presentation.</summary>
/// <param name="Id">The record identifier.</param>
/// <param name="WorkDate">The local date on which work began.</param>
/// <param name="ServiceId">The selected service.</param>
/// <param name="TimeCategoryId">The selected category, or <see langword="null"/> for arbitrary duration.</param>
/// <param name="InputMode">The input mode.</param>
/// <param name="WorkMinutes">The normalized duration.</param>
/// <param name="StartTime">The start time when present.</param>
/// <param name="EndTime">The normalized end time when present.</param>
/// <param name="SourceServicePresetId">The input-assistance preset used to create the record, when any.</param>
/// <param name="SourceBasicShiftId">The source shift identifier when the record was applied from a shift.</param>
/// <param name="SourceWorkRecordId">The source record identifier when the record was created by day copy.</param>
public sealed record WorkRecordDto(
    WorkRecordId Id,
    DateOnly WorkDate,
    ServiceId ServiceId,
    TimeCategoryId? TimeCategoryId,
    WorkInputMode InputMode,
    WorkMinutes WorkMinutes,
    MinuteOfDay? StartTime,
    MinuteOfDay? EndTime,
    ServicePresetId? SourceServicePresetId,
    BasicShiftId? SourceBasicShiftId,
    WorkRecordId? SourceWorkRecordId);

/// <summary>Contains Presentation input for creating or updating one work record.</summary>
/// <param name="Id">The existing identifier for an update, or <see langword="null"/> for a create.</param>
/// <param name="WorkDate">The local work start date.</param>
/// <param name="ServiceId">The selected service.</param>
/// <param name="TimeCategoryId">The selected time category, when any.</param>
/// <param name="InputMode">The selected input mode.</param>
/// <param name="WorkMinutes">The entered duration for duration mode; otherwise <see langword="null"/>.</param>
/// <param name="StartTime">The entered start time when required.</param>
/// <param name="EndTime">The entered end time for time-range mode.</param>
/// <param name="SourceServicePresetId">The preset used as input assistance, when any.</param>
public sealed record SaveWorkRecordCommand(
    WorkRecordId? Id,
    DateOnly WorkDate,
    ServiceId ServiceId,
    TimeCategoryId? TimeCategoryId,
    WorkInputMode InputMode,
    WorkMinutes? WorkMinutes,
    MinuteOfDay? StartTime,
    MinuteOfDay? EndTime,
    ServicePresetId? SourceServicePresetId);

/// <summary>Describes one service-preset candidate for work input.</summary>
/// <param name="Preset">The current input-assistance preset.</param>
/// <param name="IsAvailable">Whether the selected date's setting snapshot can use the preset without conversion.</param>
/// <param name="UsageCount">The persisted number of work records created from the preset.</param>
/// <param name="IsMostRecentlyUsed">Whether the preset was used by the most recently confirmed work record.</param>
/// <param name="Issues">Reasons an unavailable candidate cannot be used, or other non-blocking guidance.</param>
public sealed record ServicePresetCandidateDto(
    ServicePresetDto Preset,
    bool IsAvailable,
    long UsageCount,
    bool IsMostRecentlyUsed,
    IReadOnlyList<IssueDto> Issues);

/// <summary>Contains all settings and ordered candidates needed to open work input for one date.</summary>
/// <param name="WorkDate">The selected local work date.</param>
/// <param name="Settings">The effective month settings used for input and calculation.</param>
/// <param name="PresetCandidates">Candidates in their final Presentation order, with frequently and recently used available presets prioritized by Application.</param>
/// <param name="SuggestedValues">The most recently confirmed values adapted to the selected date as an editable initial candidate, when available.</param>
public sealed record WorkInputOptionsDto(
    DateOnly WorkDate,
    MonthSettingsDto Settings,
    IReadOnlyList<ServicePresetCandidateDto> PresetCandidates,
    SaveWorkRecordCommand? SuggestedValues);

/// <summary>Contains a non-persisted validation, normalization, and salary preview.</summary>
/// <param name="NormalizedWorkMinutes">The derived or validated work duration, when normalization succeeded.</param>
/// <param name="NormalizedStartTime">The normalized start time when required by input or an applicable time condition.</param>
/// <param name="NormalizedEndTime">The normalized end time when required; an earlier value denotes the following day.</param>
/// <param name="Calculation">The calculated or uncalculated salary result, or <see langword="null"/> when the input itself is invalid.</param>
/// <param name="CanSave">Whether persistence is allowed. Invalid input returns <see langword="false"/>; missing settings alone may return an uncalculated result with <see langword="true"/>.</param>
/// <param name="Issues">Blocking input issues or non-blocking missing-setting warnings and correction guidance.</param>
public sealed record WorkRecordPreviewDto(
    WorkMinutes? NormalizedWorkMinutes,
    MinuteOfDay? NormalizedStartTime,
    MinuteOfDay? NormalizedEndTime,
    WorkSalaryCalculation? Calculation,
    bool CanSave,
    IReadOnlyList<IssueDto> Issues);

/// <summary>Contains the saved record and its immediate salary status.</summary>
/// <param name="WorkRecord">The normalized saved record.</param>
/// <param name="Calculation">The deterministic calculation or missing-setting result.</param>
/// <param name="Warnings">Warnings that did not prevent persistence.</param>
public sealed record SaveWorkRecordResultDto(
    WorkRecordDto WorkRecord,
    WorkSalaryCalculation Calculation,
    IReadOnlyList<IssueDto> Warnings);

/// <summary>Pairs one persisted work record with its calculation basis and result.</summary>
/// <param name="WorkRecord">The persisted, normalized work content.</param>
/// <param name="Calculation">The calculation breakdown or explicit missing-setting result.</param>
public sealed record WorkRecordSalaryDto(
    WorkRecordDto WorkRecord,
    WorkSalaryCalculation Calculation);

/// <summary>Contains the calculation details for one day.</summary>
/// <param name="Date">The local date.</param>
/// <param name="Records">The persisted work content paired with each calculation result.</param>
/// <param name="BasePaySubtotal">The base-pay subtotal of calculated records.</param>
/// <param name="PremiumSubtotal">The premium subtotal of calculated records.</param>
/// <param name="CountBonusSubtotal">The count-bonus subtotal of calculated records.</param>
/// <param name="CalculatedSubtotal">The subtotal of successfully calculated records.</param>
/// <param name="UncalculatedCount">The number of records lacking calculation settings.</param>
public sealed record DailySalaryDto(
    DateOnly Date,
    IReadOnlyList<WorkRecordSalaryDto> Records,
    YenAmount BasePaySubtotal,
    YenAmount PremiumSubtotal,
    YenAmount CountBonusSubtotal,
    YenAmount CalculatedSubtotal,
    int UncalculatedCount);

/// <summary>Contains the unpersisted confirmation data for copying one day's work records.</summary>
/// <param name="SourceDate">The source local date.</param>
/// <param name="TargetDate">The target local date.</param>
/// <param name="SourceWorkRecordCount">The number of records that would be copied.</param>
/// <param name="TargetExistingWorkRecordCount">The number of records already saved on the target date.</param>
/// <param name="SourceSettingMonth">The calendar setting month used by source records.</param>
/// <param name="TargetSettingMonth">The calendar setting month that copied records will use.</param>
/// <param name="UsesDifferentSettingMonth">Whether copying causes recalculation under another month's snapshot.</param>
/// <param name="Issues">Blocking issues or duplicate warnings for Presentation.</param>
public sealed record CopyDayPreviewDto(
    DateOnly SourceDate,
    DateOnly TargetDate,
    int SourceWorkRecordCount,
    int TargetExistingWorkRecordCount,
    YearMonth SourceSettingMonth,
    YearMonth TargetSettingMonth,
    bool UsesDifferentSettingMonth,
    IReadOnlyList<IssueDto> Issues);

/// <summary>Contains one allowance line in a payroll-period summary.</summary>
/// <param name="Id">The allowance identifier.</param>
/// <param name="DisplayName">The display name.</param>
/// <param name="Amount">The whole-yen amount.</param>
public sealed record MonthlyAllowanceDto(MonthlyAllowanceId Id, string DisplayName, YenAmount Amount);

/// <summary>Contains a complete payroll-period read model for Presentation.</summary>
/// <param name="Period">The inclusive payroll period.</param>
/// <param name="Days">Calculated days within the period.</param>
/// <param name="Allowances">Allowances applied once to the period.</param>
/// <param name="BasePaySubtotal">The base-pay subtotal of calculated records.</param>
/// <param name="PremiumSubtotal">The premium subtotal of calculated records.</param>
/// <param name="CountBonusSubtotal">The count-bonus subtotal of calculated records.</param>
/// <param name="AllowanceSubtotal">The direct payroll-period allowance subtotal.</param>
/// <param name="CalculatedSubtotal">The calculated records plus allowances.</param>
/// <param name="UncalculatedCount">The number of uncalculated work records.</param>
public sealed record PayrollPeriodSummaryDto(
    PayrollPeriod Period,
    IReadOnlyList<DailySalaryDto> Days,
    IReadOnlyList<MonthlyAllowanceDto> Allowances,
    YenAmount BasePaySubtotal,
    YenAmount PremiumSubtotal,
    YenAmount CountBonusSubtotal,
    YenAmount AllowanceSubtotal,
    YenAmount CalculatedSubtotal,
    int UncalculatedCount);

/// <summary>Contains one calendar-day read model.</summary>
/// <param name="Date">The local date.</param>
/// <param name="WorkRecordCount">The number of saved records.</param>
/// <param name="CalculatedSubtotal">The calculated subtotal.</param>
/// <param name="UncalculatedCount">The number of uncalculated records.</param>
/// <param name="BasicShiftCandidateCount">The number of unapplied shift candidates.</param>
public sealed record CalendarDayDto(
    DateOnly Date,
    int WorkRecordCount,
    YenAmount CalculatedSubtotal,
    int UncalculatedCount,
    int BasicShiftCandidateCount);
