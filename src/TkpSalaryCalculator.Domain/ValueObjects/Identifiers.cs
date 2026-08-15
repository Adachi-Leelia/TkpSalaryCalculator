namespace TkpSalaryCalculator.Domain.ValueObjects;

/// <summary>保存済み勤務記録を識別します。</summary>
/// <param name="Value">安定した識別子。</param>
public readonly record struct WorkRecordId(Guid Value);

/// <summary>設定月をまたいでサービス定義を識別します。</summary>
/// <param name="Value">安定した論理識別子。</param>
public readonly record struct ServiceId(Guid Value);

/// <summary>設定月をまたいで時間区分を識別します。</summary>
/// <param name="Value">安定した論理識別子。</param>
public readonly record struct TimeCategoryId(Guid Value);

/// <summary>設定月をまたいで割増定義を識別します。</summary>
/// <param name="Value">安定した論理識別子。</param>
public readonly record struct PremiumId(Guid Value);

/// <summary>設定月をまたいで件数加算定義を識別します。</summary>
/// <param name="Value">安定した論理識別子。</param>
public readonly record struct CountBonusId(Guid Value);

/// <summary>不変の設定スナップショットを識別します。</summary>
/// <param name="Value">スナップショット識別子。</param>
public readonly record struct SettingSnapshotId(Guid Value);

/// <summary>締め日ルールの履歴項目を識別します。</summary>
/// <param name="Value">履歴識別子。</param>
public readonly record struct ClosingRuleId(Guid Value);

/// <summary>月額手当を識別します。</summary>
/// <param name="Value">手当識別子。</param>
public readonly record struct MonthlyAllowanceId(Guid Value);

/// <summary>祝日カレンダーのバージョンを識別します。</summary>
/// <param name="Value">バージョン識別子。</param>
public readonly record struct HolidayCalendarVersionId(Guid Value);

/// <summary>再利用可能なサービスプリセットを識別します。</summary>
/// <param name="Value">プリセット識別子。</param>
public readonly record struct ServicePresetId(Guid Value);

/// <summary>基本シフトを識別します。</summary>
/// <param name="Value">シフト識別子。</param>
public readonly record struct BasicShiftId(Guid Value);
