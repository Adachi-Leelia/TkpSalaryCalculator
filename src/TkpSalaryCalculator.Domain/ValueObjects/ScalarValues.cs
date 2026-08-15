namespace TkpSalaryCalculator.Domain.ValueObjects;

/// <summary>Represents a calendar year and month without a time zone.</summary>
/// <param name="Year">The year from 1 through 9999.</param>
/// <param name="Month">The month from 1 through 12.</param>
public readonly record struct YearMonth(int Year, int Month);

/// <summary>Identifies a payroll period by the year and month containing its end date.</summary>
/// <param name="Value">The payroll period year and month.</param>
public readonly record struct PayrollPeriodKey(YearMonth Value);

/// <summary>Represents a non-negative, whole-yen amount.</summary>
/// <param name="Value">The amount in yen.</param>
public readonly record struct YenAmount(long Value);

/// <summary>Represents a whole-minute duration from 1 through 1,440 minutes.</summary>
/// <param name="Value">The duration in minutes.</param>
public readonly record struct WorkMinutes(int Value);

/// <summary>Represents a local wall-clock time as minutes after midnight.</summary>
/// <param name="Value">The value from 0 through 1,439.</param>
public readonly record struct MinuteOfDay(int Value);

/// <summary>Represents a non-negative percentage in basis points, where 10,000 is 100%.</summary>
/// <param name="Value">The percentage in basis points.</param>
public readonly record struct BasisPoints(int Value);

/// <summary>Represents a non-negative display order.</summary>
/// <param name="Value">The order value.</param>
public readonly record struct DisplayOrder(int Value);

/// <summary>Represents a schema version greater than or equal to one.</summary>
/// <param name="Value">The version number.</param>
public readonly record struct SchemaVersion(int Value);

/// <summary>Defines how a work interval was entered.</summary>
public enum WorkInputMode
{
    /// <summary>The duration was derived from a start and end time.</summary>
    TimeRange,

    /// <summary>The duration was entered directly.</summary>
    Duration,
}

/// <summary>Defines how the base pay is calculated.</summary>
public enum RateType
{
    /// <summary>The configured amount is an hourly rate.</summary>
    Hourly,

    /// <summary>The configured amount is paid once for the record.</summary>
    FixedPerRecord,
}

/// <summary>Defines how a premium amount is calculated.</summary>
public enum PremiumCalculationType
{
    /// <summary>A percentage of the applicable base-pay portion.</summary>
    Percentage,

    /// <summary>A fixed amount per applicable hour.</summary>
    FixedPerHour,

    /// <summary>A fixed amount once per matching work record.</summary>
    FixedPerRecord,
}
