using TkpSalaryCalculator.Domain.ValueObjects;

namespace TkpSalaryCalculator.Domain.Models;

/// <summary>Contains a normalized, persisted work record used by the pure calculator.</summary>
/// <param name="Id">The work record identifier.</param>
/// <param name="WorkDate">The local calendar date on which work began.</param>
/// <param name="ServiceId">The selected service.</param>
/// <param name="TimeCategoryId">The selected time category, or <see langword="null"/> for arbitrary-duration input.</param>
/// <param name="InputMode">The input mode used to normalize the interval.</param>
/// <param name="WorkMinutes">The normalized duration.</param>
/// <param name="StartTime">The local start time when required by the input mode or premiums.</param>
/// <param name="EndTime">The normalized local end time when required; an earlier value means the following day.</param>
public sealed record WorkRecord(
    WorkRecordId Id,
    DateOnly WorkDate,
    ServiceId ServiceId,
    TimeCategoryId? TimeCategoryId,
    WorkInputMode InputMode,
    WorkMinutes WorkMinutes,
    MinuteOfDay? StartTime,
    MinuteOfDay? EndTime);

/// <summary>Describes a service as it existed in one immutable snapshot.</summary>
/// <param name="Id">The stable logical identifier.</param>
/// <param name="DisplayName">The user-facing name.</param>
/// <param name="DisplayOrder">The display order.</param>
/// <param name="IsEnabled">Whether the service is offered for new input in the snapshot month.</param>
public sealed record SnapshotService(ServiceId Id, string DisplayName, DisplayOrder DisplayOrder, bool IsEnabled);

/// <summary>Describes a time category as it existed in one immutable snapshot.</summary>
/// <param name="Id">The stable logical identifier.</param>
/// <param name="ServiceId">The owning service.</param>
/// <param name="DisplayName">The user-facing name.</param>
/// <param name="StandardMinutes">The standard duration.</param>
/// <param name="DisplayOrder">The display order.</param>
/// <param name="IsEnabled">Whether the category is offered for new input in the snapshot month.</param>
public sealed record SnapshotTimeCategory(
    TimeCategoryId Id,
    ServiceId ServiceId,
    string DisplayName,
    WorkMinutes StandardMinutes,
    DisplayOrder DisplayOrder,
    bool IsEnabled);

/// <summary>Defines one base rate in an immutable snapshot.</summary>
/// <param name="ServiceId">The target service.</param>
/// <param name="TimeCategoryId">The target category, or <see langword="null"/> for a service-wide rate.</param>
/// <param name="RateType">The calculation method.</param>
/// <param name="Amount">The configured whole-yen amount.</param>
public sealed record SnapshotRate(
    ServiceId ServiceId,
    TimeCategoryId? TimeCategoryId,
    RateType RateType,
    YenAmount Amount);

/// <summary>Defines one premium rule in an immutable snapshot.</summary>
/// <param name="Id">The stable logical identifier.</param>
/// <param name="DisplayName">The user-facing name.</param>
/// <param name="CalculationType">The calculation method.</param>
/// <param name="Percentage">The percentage for percentage rules.</param>
/// <param name="Amount">The amount for fixed-amount rules.</param>
/// <param name="StartTime">The inclusive daily start time, when time-limited.</param>
/// <param name="EndTime">The exclusive daily end time, when time-limited.</param>
/// <param name="UsesNationalHolidays">Whether national holidays are a date condition.</param>
/// <param name="Weekdays">The matching weekdays; an empty set contributes no weekday condition.</param>
/// <param name="Dates">The matching individual dates; an empty set contributes no individual-date condition.</param>
/// <param name="ServiceIds">The target services; an empty set means all services.</param>
/// <param name="IsEnabled">Whether the rule applies in this snapshot.</param>
public sealed record SnapshotPremium(
    PremiumId Id,
    string DisplayName,
    PremiumCalculationType CalculationType,
    BasisPoints? Percentage,
    YenAmount? Amount,
    MinuteOfDay? StartTime,
    MinuteOfDay? EndTime,
    bool UsesNationalHolidays,
    IReadOnlySet<DayOfWeek> Weekdays,
    IReadOnlySet<DateOnly> Dates,
    IReadOnlySet<ServiceId> ServiceIds,
    bool IsEnabled);

/// <summary>Defines one per-record count bonus in an immutable snapshot.</summary>
/// <param name="Id">The stable logical identifier.</param>
/// <param name="DisplayName">The user-facing name.</param>
/// <param name="Amount">The whole-yen amount paid once per matching record.</param>
/// <param name="ServiceIds">The target services; an empty set means all services.</param>
/// <param name="IsEnabled">Whether the rule applies in this snapshot.</param>
public sealed record SnapshotCountBonus(
    CountBonusId Id,
    string DisplayName,
    YenAmount Amount,
    IReadOnlySet<ServiceId> ServiceIds,
    bool IsEnabled);

/// <summary>Contains all salary settings for one immutable snapshot.</summary>
/// <param name="Id">The snapshot identifier.</param>
/// <param name="BasedOnId">The lineage source, when any; it is not consulted during calculation.</param>
/// <param name="HolidayCalendarVersionId">The holiday data version fixed to this snapshot.</param>
/// <param name="SchemaVersion">The snapshot schema version.</param>
/// <param name="CreatedAtUtc">The UTC instant at which the snapshot was created.</param>
/// <param name="Services">The complete service set.</param>
/// <param name="TimeCategories">The complete time-category set.</param>
/// <param name="Rates">The complete rate set.</param>
/// <param name="Premiums">The complete premium set.</param>
/// <param name="CountBonuses">The complete count-bonus set.</param>
public sealed record SettingSnapshot(
    SettingSnapshotId Id,
    SettingSnapshotId? BasedOnId,
    HolidayCalendarVersionId HolidayCalendarVersionId,
    SchemaVersion SchemaVersion,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<SnapshotService> Services,
    IReadOnlyList<SnapshotTimeCategory> TimeCategories,
    IReadOnlyList<SnapshotRate> Rates,
    IReadOnlyList<SnapshotPremium> Premiums,
    IReadOnlyList<SnapshotCountBonus> CountBonuses);

/// <summary>Contains the fixed holiday dates used by a setting snapshot.</summary>
/// <param name="VersionId">The holiday calendar version.</param>
/// <param name="Holidays">The holidays and their display names.</param>
public sealed record HolidayCalendar(
    HolidayCalendarVersionId VersionId,
    IReadOnlyDictionary<DateOnly, string> Holidays);

/// <summary>Defines a closing rule beginning with a payroll-period month.</summary>
/// <param name="Id">The history identifier.</param>
/// <param name="EffectiveFrom">The first payroll period key to which this rule applies.</param>
/// <param name="ClosingDay">The requested closing day, or <see langword="null"/> for month end.</param>
public sealed record ClosingRule(ClosingRuleId Id, PayrollPeriodKey EffectiveFrom, int? ClosingDay);

/// <summary>Represents the inclusive dates of one payroll period.</summary>
/// <param name="Key">The period key based on its end month.</param>
/// <param name="StartDate">The inclusive start date.</param>
/// <param name="EndDate">The inclusive end date.</param>
public sealed record PayrollPeriod(PayrollPeriodKey Key, DateOnly StartDate, DateOnly EndDate);

/// <summary>Defines a monthly allowance applied directly to one payroll period.</summary>
/// <param name="Id">The allowance identifier.</param>
/// <param name="PayrollPeriodKey">The target payroll period.</param>
/// <param name="DisplayName">The user-facing name.</param>
/// <param name="Amount">The whole-yen amount.</param>
public sealed record MonthlyAllowance(
    MonthlyAllowanceId Id,
    PayrollPeriodKey PayrollPeriodKey,
    string DisplayName,
    YenAmount Amount);
