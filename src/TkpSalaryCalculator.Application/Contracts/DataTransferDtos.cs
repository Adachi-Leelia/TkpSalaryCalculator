using TkpSalaryCalculator.Domain.ValueObjects;

namespace TkpSalaryCalculator.Application.Contracts;

/// <summary>Identifies a prepared import staged outside the live data set.</summary>
/// <param name="Value">The opaque prepared-import identifier.</param>
public readonly record struct PreparedImportId(Guid Value);

/// <summary>Identifies the current data-transfer format shown by Presentation.</summary>
/// <param name="Format">The stable format identifier.</param>
/// <param name="FormatVersion">The independently versioned data-transfer format number.</param>
public sealed record DataTransferFormatDto(string Format, int FormatVersion);

/// <summary>Contains export document metadata.</summary>
/// <param name="Format">The stable document format identifier.</param>
/// <param name="FormatVersion">The independent export-format version.</param>
/// <param name="CreatedAtUtc">The UTC creation instant.</param>
/// <param name="AppVersion">The creating application version.</param>
public sealed record ExportDocumentHeader(
    string Format,
    int FormatVersion,
    DateTimeOffset CreatedAtUtc,
    string AppVersion);

/// <summary>Identifies one logical section of an export document.</summary>
public enum DataTransferSection
{
    /// <summary>Document metadata.</summary>
    Metadata,
    /// <summary>Setting-month references.</summary>
    SettingMonths,
    /// <summary>Immutable setting snapshots and child data.</summary>
    SettingSnapshots,
    /// <summary>Closing-rule history.</summary>
    ClosingRules,
    /// <summary>Monthly allowances.</summary>
    MonthlyAllowances,
    /// <summary>Logical definitions.</summary>
    Definitions,
    /// <summary>Service presets.</summary>
    ServicePresets,
    /// <summary>Basic shifts.</summary>
    BasicShifts,
    /// <summary>Work records.</summary>
    WorkRecords,
    /// <summary>Holiday calendar versions and dates.</summary>
    Holidays,
}

/// <summary>Provides the non-generic base for one logical streaming record.</summary>
/// <param name="Section">The containing logical section.</param>
/// <param name="Sequence">The zero-based order within that section.</param>
public abstract record DataTransferRecord(DataTransferSection Section, long Sequence);

/// <summary>Contains one strongly typed logical record in a streaming export or import.</summary>
/// <typeparam name="T">An immutable contract type supported by the format version.</typeparam>
/// <param name="Section">The containing logical section.</param>
/// <param name="Sequence">The zero-based order within that section.</param>
/// <param name="Value">The immutable record value.</param>
public sealed record DataTransferRecord<T>(DataTransferSection Section, long Sequence, T Value)
    : DataTransferRecord(Section, Sequence);

/// <summary>Summarizes a fully validated import staged without changing live data.</summary>
/// <param name="Id">The prepared import identifier.</param>
/// <param name="FormatVersion">The validated format version.</param>
/// <param name="ExportCreatedAtUtc">The creation instant recorded in the export document.</param>
/// <param name="SettingMonthCount">The staged setting-month count.</param>
/// <param name="BasicShiftCount">The staged basic-shift count.</param>
/// <param name="WorkRecordCount">The staged work-record count.</param>
/// <param name="MonthlyAllowanceCount">The staged monthly-allowance count.</param>
/// <param name="OldestSettingMonth">The oldest staged setting month, when present.</param>
/// <param name="LatestSettingMonth">The latest staged setting month, when present.</param>
/// <param name="OldestWorkDate">The oldest staged work date, when present.</param>
/// <param name="LatestWorkDate">The latest staged work date, when present.</param>
/// <param name="Warnings">Non-blocking validation warnings.</param>
public sealed record ImportPreviewDto(
    PreparedImportId Id,
    int FormatVersion,
    DateTimeOffset ExportCreatedAtUtc,
    long SettingMonthCount,
    long BasicShiftCount,
    long WorkRecordCount,
    long MonthlyAllowanceCount,
    YearMonth? OldestSettingMonth,
    YearMonth? LatestSettingMonth,
    DateOnly? OldestWorkDate,
    DateOnly? LatestWorkDate,
    IReadOnlyList<IssueDto> Warnings);
