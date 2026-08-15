using TkpSalaryCalculator.Domain.Models;
using TkpSalaryCalculator.Domain.ValueObjects;

namespace TkpSalaryCalculator.Application.Contracts;

/// <summary>Contains the settings presented for one calendar month.</summary>
/// <param name="YearMonth">The target calendar month.</param>
/// <param name="Snapshot">The immutable snapshot currently referenced by the month.</param>
public sealed record MonthSettingsDto(YearMonth YearMonth, SettingSnapshot Snapshot);

/// <summary>Contains the complete replacement content for a cloned setting snapshot.</summary>
/// <param name="Services">The replacement service rows.</param>
/// <param name="TimeCategories">The replacement time-category rows.</param>
/// <param name="Rates">The replacement rate rows.</param>
/// <param name="Premiums">The replacement premium rows.</param>
/// <param name="CountBonuses">The replacement count-bonus rows.</param>
public sealed record SettingSnapshotReplacementDto(
    IReadOnlyList<SnapshotService> Services,
    IReadOnlyList<SnapshotTimeCategory> TimeCategories,
    IReadOnlyList<SnapshotRate> Rates,
    IReadOnlyList<SnapshotPremium> Premiums,
    IReadOnlyList<SnapshotCountBonus> CountBonuses);

/// <summary>Contains the predicted effect of replacing one month's snapshot.</summary>
/// <param name="TargetMonth">The month whose reference would change.</param>
/// <param name="AffectedWorkRecordCount">The number of existing records whose result would change.</param>
/// <param name="CurrentCalculatedSubtotal">The current calculated subtotal.</param>
/// <param name="ReplacementCalculatedSubtotal">The predicted calculated subtotal.</param>
/// <param name="ResultingUncalculatedCount">The predicted number of uncalculated records.</param>
/// <param name="Issues">Validation issues that must be resolved before commit.</param>
public sealed record SettingReplacementPreviewDto(
    YearMonth TargetMonth,
    int AffectedWorkRecordCount,
    YenAmount CurrentCalculatedSubtotal,
    YenAmount ReplacementCalculatedSubtotal,
    int ResultingUncalculatedCount,
    IReadOnlyList<IssueDto> Issues);

/// <summary>Contains a closing-rule replacement effective from one payroll-period month.</summary>
/// <param name="EffectiveFrom">The first affected payroll period.</param>
/// <param name="ClosingDay">A day from 1 through 31, or <see langword="null"/> for month-end closing.</param>
public sealed record ReplaceClosingRuleCommand(PayrollPeriodKey EffectiveFrom, int? ClosingDay);

/// <summary>Describes the closing rule effective for a requested payroll period.</summary>
/// <param name="PayrollPeriodKey">The requested payroll period.</param>
/// <param name="RuleId">The effective history entry.</param>
/// <param name="EffectiveFrom">The first period to which the history entry applies.</param>
/// <param name="ClosingDay">The configured day from 1 through 31, or <see langword="null"/> for month end.</param>
public sealed record EffectiveClosingRuleDto(
    PayrollPeriodKey PayrollPeriodKey,
    ClosingRuleId RuleId,
    PayrollPeriodKey EffectiveFrom,
    int? ClosingDay);

/// <summary>Contains input for a monthly allowance.</summary>
/// <param name="Id">The existing identifier, or <see langword="null"/> for a new allowance.</param>
/// <param name="PayrollPeriodKey">The target payroll period.</param>
/// <param name="DisplayName">The user-facing name.</param>
/// <param name="Amount">The whole-yen amount.</param>
public sealed record SaveMonthlyAllowanceCommand(
    MonthlyAllowanceId? Id,
    PayrollPeriodKey PayrollPeriodKey,
    string DisplayName,
    YenAmount Amount);
