namespace TkpSalaryCalculator.Domain.ValueObjects;

/// <summary>Identifies a persisted work record.</summary>
/// <param name="Value">The stable identifier.</param>
public readonly record struct WorkRecordId(Guid Value);

/// <summary>Identifies a service definition across setting months.</summary>
/// <param name="Value">The stable logical identifier.</param>
public readonly record struct ServiceId(Guid Value);

/// <summary>Identifies a time category across setting months.</summary>
/// <param name="Value">The stable logical identifier.</param>
public readonly record struct TimeCategoryId(Guid Value);

/// <summary>Identifies a premium definition across setting months.</summary>
/// <param name="Value">The stable logical identifier.</param>
public readonly record struct PremiumId(Guid Value);

/// <summary>Identifies a count-bonus definition across setting months.</summary>
/// <param name="Value">The stable logical identifier.</param>
public readonly record struct CountBonusId(Guid Value);

/// <summary>Identifies an immutable setting snapshot.</summary>
/// <param name="Value">The snapshot identifier.</param>
public readonly record struct SettingSnapshotId(Guid Value);

/// <summary>Identifies a closing-rule history entry.</summary>
/// <param name="Value">The history identifier.</param>
public readonly record struct ClosingRuleId(Guid Value);

/// <summary>Identifies a monthly allowance.</summary>
/// <param name="Value">The allowance identifier.</param>
public readonly record struct MonthlyAllowanceId(Guid Value);

/// <summary>Identifies a holiday calendar version.</summary>
/// <param name="Value">The version identifier.</param>
public readonly record struct HolidayCalendarVersionId(Guid Value);

/// <summary>Identifies a reusable service preset.</summary>
/// <param name="Value">The preset identifier.</param>
public readonly record struct ServicePresetId(Guid Value);

/// <summary>Identifies a basic shift.</summary>
/// <param name="Value">The shift identifier.</param>
public readonly record struct BasicShiftId(Guid Value);
