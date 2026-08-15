using TkpSalaryCalculator.Domain.ValueObjects;

namespace TkpSalaryCalculator.Application.Contracts;

/// <summary>Describes a reusable basic shift.</summary>
/// <param name="Id">The shift identifier.</param>
/// <param name="Weekday">The target weekday.</param>
/// <param name="ServicePresetId">The source preset, when any.</param>
/// <param name="ServiceId">The concrete service copied into a work record.</param>
/// <param name="TimeCategoryId">The concrete time category, when any.</param>
/// <param name="InputMode">The input mode.</param>
/// <param name="WorkMinutes">The normalized duration.</param>
/// <param name="StartTime">The start time when present.</param>
/// <param name="EndTime">The end time when present.</param>
/// <param name="DisplayOrder">The order within the weekday.</param>
/// <param name="IsEnabled">Whether the shift is available for new application.</param>
public sealed record BasicShiftDto(
    BasicShiftId Id,
    DayOfWeek Weekday,
    ServicePresetId? ServicePresetId,
    ServiceId ServiceId,
    TimeCategoryId? TimeCategoryId,
    WorkInputMode InputMode,
    WorkMinutes WorkMinutes,
    MinuteOfDay? StartTime,
    MinuteOfDay? EndTime,
    DisplayOrder DisplayOrder,
    bool IsEnabled);

/// <summary>Contains input for creating or replacing a basic shift.</summary>
/// <param name="Id">The existing identifier, or <see langword="null"/> for a new shift.</param>
/// <param name="Weekday">The target weekday.</param>
/// <param name="ServicePresetId">The source preset, when any.</param>
/// <param name="ServiceId">The concrete service.</param>
/// <param name="TimeCategoryId">The concrete time category, when any.</param>
/// <param name="InputMode">The input mode.</param>
/// <param name="WorkMinutes">The entered duration for <see cref="WorkInputMode.Duration"/>; <see langword="null"/> for <see cref="WorkInputMode.TimeRange"/>, where Application derives it from start and end times.</param>
/// <param name="StartTime">The start time when present.</param>
/// <param name="EndTime">The end time when present.</param>
/// <param name="DisplayOrder">The order within the weekday.</param>
/// <param name="IsEnabled">Whether the shift is available for application.</param>
public sealed record SaveBasicShiftCommand(
    BasicShiftId? Id,
    DayOfWeek Weekday,
    ServicePresetId? ServicePresetId,
    ServiceId ServiceId,
    TimeCategoryId? TimeCategoryId,
    WorkInputMode InputMode,
    WorkMinutes? WorkMinutes,
    MinuteOfDay? StartTime,
    MinuteOfDay? EndTime,
    DisplayOrder DisplayOrder,
    bool IsEnabled);

/// <summary>Describes one shift candidate and any reason it cannot be applied.</summary>
/// <param name="Shift">The source shift.</param>
/// <param name="CanApply">Whether it is valid for the selected date.</param>
/// <param name="IsAlreadyApplied">Whether this shift was already applied to the date.</param>
/// <param name="HasSimilarManualRecord">Whether a potentially duplicate manual record exists.</param>
/// <param name="Issues">Blocking or warning issues.</param>
public sealed record BasicShiftCandidateDto(
    BasicShiftDto Shift,
    bool CanApply,
    bool IsAlreadyApplied,
    bool HasSimilarManualRecord,
    IReadOnlyList<IssueDto> Issues);

/// <summary>Contains the unpersisted shift-application preview for one date.</summary>
/// <param name="WorkDate">The selected date.</param>
/// <param name="Candidates">The candidates in display order.</param>
/// <param name="ExistingWorkRecordCount">The current number of work records for the date.</param>
public sealed record BasicShiftPreviewDto(
    DateOnly WorkDate,
    IReadOnlyList<BasicShiftCandidateDto> Candidates,
    int ExistingWorkRecordCount);

/// <summary>Commits selected shift candidates as independent work records.</summary>
/// <param name="WorkDate">The selected date.</param>
/// <param name="BasicShiftIds">The selected source shifts.</param>
public sealed record ApplyBasicShiftsCommand(DateOnly WorkDate, IReadOnlyList<BasicShiftId> BasicShiftIds);
