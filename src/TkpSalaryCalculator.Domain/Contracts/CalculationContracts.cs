using TkpSalaryCalculator.Domain.Models;
using TkpSalaryCalculator.Domain.ValueObjects;

namespace TkpSalaryCalculator.Domain.Contracts;

/// <summary>Calculates a work record from normalized, immutable inputs without side effects.</summary>
public interface ISalaryCalculator
{
    /// <summary>Calculates one work record using the supplied complete setting and holiday snapshots.</summary>
    /// <param name="request">The normalized calculation request.</param>
    /// <returns>A calculated breakdown or explicit missing-setting reasons.</returns>
    WorkSalaryCalculation Calculate(WorkSalaryCalculationRequest request);

    /// <summary>Aggregates calculated and uncalculated records for one local work date.</summary>
    /// <param name="workDate">The local work date.</param>
    /// <param name="records">The individual record results.</param>
    /// <returns>Deterministic base-pay, premium, count-bonus, and total subtotals plus the uncalculated count.</returns>
    DailySalaryCalculation AggregateDay(
        DateOnly workDate,
        IReadOnlyList<WorkSalaryCalculation> records);

    /// <summary>Aggregates daily results and direct allowances for one payroll period.</summary>
    /// <param name="period">The inclusive period.</param>
    /// <param name="days">The daily results within the period.</param>
    /// <param name="allowances">Allowances applied once to the period.</param>
    /// <returns>Deterministic base-pay, premium, count-bonus, allowance, and total subtotals plus the uncalculated count.</returns>
    PayrollPeriodSalaryCalculation AggregatePeriod(
        PayrollPeriod period,
        IReadOnlyList<DailySalaryCalculation> days,
        IReadOnlyList<MonthlyAllowance> allowances);
}

/// <summary>Calculates payroll-period boundaries without persistence or clock dependencies.</summary>
public interface IPayrollPeriodCalculator
{
    /// <summary>Calculates the inclusive period identified by the supplied key.</summary>
    /// <param name="key">The payroll period key.</param>
    /// <param name="closingRules">All closing rules needed to resolve the key and its preceding key.</param>
    /// <returns>The deterministic payroll period.</returns>
    PayrollPeriod GetPeriod(PayrollPeriodKey key, IReadOnlyList<ClosingRule> closingRules);

    /// <summary>Finds the payroll period containing a local work date.</summary>
    /// <param name="workDate">The local work date.</param>
    /// <param name="closingRules">The closing-rule history.</param>
    /// <returns>The unique period whose inclusive boundaries contain the date.</returns>
    PayrollPeriod FindPeriod(DateOnly workDate, IReadOnlyList<ClosingRule> closingRules);
}

/// <summary>Contains all pure inputs needed to calculate one work record.</summary>
/// <param name="WorkRecord">The normalized work record.</param>
/// <param name="SettingSnapshot">The snapshot selected from the work date's calendar month.</param>
/// <param name="HolidayCalendar">The holiday calendar referenced by the setting snapshot.</param>
public sealed record WorkSalaryCalculationRequest(
    WorkRecord WorkRecord,
    SettingSnapshot SettingSnapshot,
    HolidayCalendar HolidayCalendar);

/// <summary>Describes whether a salary result could be calculated.</summary>
public enum SalaryCalculationStatus
{
    /// <summary>All required settings were present and the result is complete.</summary>
    Calculated,

    /// <summary>The input was valid but required calculation settings were missing.</summary>
    Uncalculated,
}

/// <summary>Identifies a missing calculation requirement without guessing a monetary value.</summary>
/// <param name="Code">A stable machine-readable reason code.</param>
/// <param name="RelatedId">An optional related logical identifier.</param>
public sealed record MissingCalculationRequirement(string Code, Guid? RelatedId);

/// <summary>Contains one applied premium line.</summary>
/// <param name="Rule">The complete immutable premium rule used for service, date, holiday, weekday, and time-condition evaluation.</param>
/// <param name="ApplicableMinutes">The number of applicable minutes.</param>
/// <param name="Amount">The rounded whole-yen premium amount.</param>
public sealed record AppliedPremium(
    SnapshotPremium Rule,
    WorkMinutes ApplicableMinutes,
    YenAmount Amount);

/// <summary>Contains one applied count-bonus line.</summary>
/// <param name="CountBonusId">The applied bonus identifier.</param>
/// <param name="DisplayName">The bonus name fixed in the snapshot.</param>
/// <param name="Amount">The whole-yen bonus amount.</param>
public sealed record AppliedCountBonus(CountBonusId CountBonusId, string DisplayName, YenAmount Amount);

/// <summary>Contains the deterministic result for one work record.</summary>
/// <param name="WorkRecordId">The calculated record.</param>
/// <param name="Status">Whether the result is complete.</param>
/// <param name="AppliedRate">The selected rate, or <see langword="null"/> when uncalculated.</param>
/// <param name="BasePay">The rounded base pay, or <see langword="null"/> when uncalculated.</param>
/// <param name="Premiums">The individual applied premiums.</param>
/// <param name="CountBonuses">The individual applied count bonuses.</param>
/// <param name="Total">The record total, or <see langword="null"/> when uncalculated.</param>
/// <param name="MissingRequirements">The explicit reasons why calculation was not possible.</param>
public sealed record WorkSalaryCalculation(
    WorkRecordId WorkRecordId,
    SalaryCalculationStatus Status,
    SnapshotRate? AppliedRate,
    YenAmount? BasePay,
    IReadOnlyList<AppliedPremium> Premiums,
    IReadOnlyList<AppliedCountBonus> CountBonuses,
    YenAmount? Total,
    IReadOnlyList<MissingCalculationRequirement> MissingRequirements);

/// <summary>Contains a pure daily aggregation result.</summary>
/// <param name="WorkDate">The local work date.</param>
/// <param name="Records">The individual record calculations.</param>
/// <param name="BasePaySubtotal">The base-pay subtotal of calculated records.</param>
/// <param name="PremiumSubtotal">The premium subtotal of calculated records.</param>
/// <param name="CountBonusSubtotal">The count-bonus subtotal of calculated records.</param>
/// <param name="CalculatedSubtotal">The subtotal of complete record calculations.</param>
/// <param name="UncalculatedCount">The number of incomplete records.</param>
public sealed record DailySalaryCalculation(
    DateOnly WorkDate,
    IReadOnlyList<WorkSalaryCalculation> Records,
    YenAmount BasePaySubtotal,
    YenAmount PremiumSubtotal,
    YenAmount CountBonusSubtotal,
    YenAmount CalculatedSubtotal,
    int UncalculatedCount);

/// <summary>Contains a pure payroll-period aggregation result.</summary>
/// <param name="Period">The inclusive payroll period.</param>
/// <param name="Days">The daily aggregation results.</param>
/// <param name="Allowances">The direct period allowances.</param>
/// <param name="BasePaySubtotal">The base-pay subtotal of calculated records.</param>
/// <param name="PremiumSubtotal">The premium subtotal of calculated records.</param>
/// <param name="CountBonusSubtotal">The count-bonus subtotal of calculated records.</param>
/// <param name="AllowanceSubtotal">The direct payroll-period allowance subtotal.</param>
/// <param name="CalculatedSubtotal">The calculated record subtotals plus allowances.</param>
/// <param name="UncalculatedCount">The number of incomplete work records.</param>
public sealed record PayrollPeriodSalaryCalculation(
    PayrollPeriod Period,
    IReadOnlyList<DailySalaryCalculation> Days,
    IReadOnlyList<MonthlyAllowance> Allowances,
    YenAmount BasePaySubtotal,
    YenAmount PremiumSubtotal,
    YenAmount CountBonusSubtotal,
    YenAmount AllowanceSubtotal,
    YenAmount CalculatedSubtotal,
    int UncalculatedCount);
