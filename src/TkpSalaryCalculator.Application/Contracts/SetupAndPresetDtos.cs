using TkpSalaryCalculator.Domain.ValueObjects;

namespace TkpSalaryCalculator.Application.Contracts;

/// <summary>Describes progress through the minimum required initial setup.</summary>
public enum InitialSetupStatus
{
    /// <summary>Initial setup has not started.</summary>
    NotStarted,
    /// <summary>Initial setup is partially saved and can be resumed.</summary>
    InProgress,
    /// <summary>All minimum salary settings are valid.</summary>
    Completed,
}

/// <summary>Contains the resumable initial-setup state.</summary>
/// <param name="Status">The overall setup status.</param>
/// <param name="Step">The stable current step identifier, when any.</param>
/// <param name="Issues">Missing minimum requirements.</param>
public sealed record InitialSetupStateDto(
    InitialSetupStatus Status,
    string? Step,
    IReadOnlyList<IssueDto> Issues);

/// <summary>Represents the persisted single-row application metadata contract.</summary>
/// <param name="InitialSetupStatus">The persisted initial-setup status.</param>
/// <param name="InitialSetupStep">The stable resume step, when setup is in progress.</param>
/// <param name="InitialSnapshotId">The initial setting snapshot, when one has been established.</param>
/// <param name="ExportFormatVersion">The current export format version.</param>
/// <param name="LastExportedAtUtc">The most recent successful export instant.</param>
/// <param name="LastDataChangedAtUtc">The most recent committed settings or work-data change.</param>
/// <param name="BackupReminderDeferredUntilDate">The device-local date before which the backup reminder remains hidden.</param>
public sealed record AppMetadata(
    InitialSetupStatus InitialSetupStatus,
    string? InitialSetupStep,
    SettingSnapshotId? InitialSnapshotId,
    int ExportFormatVersion,
    DateTimeOffset? LastExportedAtUtc,
    DateTimeOffset? LastDataChangedAtUtc,
    DateOnly? BackupReminderDeferredUntilDate);

/// <summary>Contains the backup-reminder state calculated for Presentation.</summary>
/// <param name="EvaluatedOnLocalDate">The device-local date used for date-based decisions.</param>
/// <param name="ShouldShow">Whether the reminder should currently be visible.</param>
/// <param name="HasWorkRecords">Whether any work data exists to back up.</param>
/// <param name="LastExportedAtUtc">The most recent successful export instant.</param>
/// <param name="LastDataChangedAtUtc">The most recent committed data-change instant.</param>
/// <param name="DeferredUntilDate">The device-local date before which the reminder is hidden.</param>
public sealed record BackupReminderStateDto(
    DateOnly EvaluatedOnLocalDate,
    bool ShouldShow,
    bool HasWorkRecords,
    DateTimeOffset? LastExportedAtUtc,
    DateTimeOffset? LastDataChangedAtUtc,
    DateOnly? DeferredUntilDate);

/// <summary>Describes a current service preset used only as input assistance.</summary>
/// <param name="Id">The preset identifier.</param>
/// <param name="DisplayName">The user-facing name.</param>
/// <param name="ServiceId">The concrete service copied into work input.</param>
/// <param name="TimeCategoryId">The concrete category copied into work input, when any.</param>
/// <param name="DefaultWorkMinutes">The default duration.</param>
/// <param name="DisplayOrder">The candidate order.</param>
/// <param name="IsEnabled">Whether the preset is offered for new input.</param>
public sealed record ServicePresetDto(
    ServicePresetId Id,
    string DisplayName,
    ServiceId ServiceId,
    TimeCategoryId? TimeCategoryId,
    WorkMinutes DefaultWorkMinutes,
    DisplayOrder DisplayOrder,
    bool IsEnabled);

/// <summary>Contains input for creating or replacing a service preset.</summary>
/// <param name="Id">The existing identifier, or <see langword="null"/> for a new preset.</param>
/// <param name="DisplayName">The user-facing name.</param>
/// <param name="ServiceId">The concrete service.</param>
/// <param name="TimeCategoryId">The concrete category, when any.</param>
/// <param name="DefaultWorkMinutes">The default duration.</param>
/// <param name="DisplayOrder">The candidate order.</param>
/// <param name="IsEnabled">Whether the preset is offered for input.</param>
public sealed record SaveServicePresetCommand(
    ServicePresetId? Id,
    string DisplayName,
    ServiceId ServiceId,
    TimeCategoryId? TimeCategoryId,
    WorkMinutes DefaultWorkMinutes,
    DisplayOrder DisplayOrder,
    bool IsEnabled);
