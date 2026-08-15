using TkpSalaryCalculator.Domain.ValueObjects;

namespace TkpSalaryCalculator.Domain.Models;

/// <summary>副作用のない計算処理で使用する、正規化された保存済み勤務記録を保持します。</summary>
/// <param name="Id">勤務記録識別子。</param>
/// <param name="WorkDate">勤務を開始した現地暦日。</param>
/// <param name="ServiceId">選択されたサービス。</param>
/// <param name="TimeCategoryId">選択された時間区分。任意時間入力の場合は <see langword="null"/>。</param>
/// <param name="InputMode">勤務時間の正規化に使用した入力モード。</param>
/// <param name="WorkMinutes">正規化済みの勤務時間。</param>
/// <param name="StartTime">入力モードまたは割増条件で必要となる現地開始時刻。</param>
/// <param name="EndTime">必要な場合の正規化済み現地終了時刻。開始時刻より前の値は翌日を表します。</param>
public sealed record WorkRecord(
    WorkRecordId Id,
    DateOnly WorkDate,
    ServiceId ServiceId,
    TimeCategoryId? TimeCategoryId,
    WorkInputMode InputMode,
    WorkMinutes WorkMinutes,
    MinuteOfDay? StartTime,
    MinuteOfDay? EndTime);

/// <summary>不変スナップショット内に存在するサービスを表します。</summary>
/// <param name="Id">安定した論理識別子。</param>
/// <param name="DisplayName">利用者向けの名称。</param>
/// <param name="DisplayOrder">表示順。</param>
/// <param name="IsEnabled">スナップショット対象月の新規入力でサービスを提示するかどうか。</param>
public sealed record SnapshotService(ServiceId Id, string DisplayName, DisplayOrder DisplayOrder, bool IsEnabled);

/// <summary>不変スナップショット内に存在する時間区分を表します。</summary>
/// <param name="Id">安定した論理識別子。</param>
/// <param name="ServiceId">所属するサービス。</param>
/// <param name="DisplayName">利用者向けの名称。</param>
/// <param name="StandardMinutes">標準勤務時間。</param>
/// <param name="DisplayOrder">表示順。</param>
/// <param name="IsEnabled">スナップショット対象月の新規入力で時間区分を提示するかどうか。</param>
public sealed record SnapshotTimeCategory(
    TimeCategoryId Id,
    ServiceId ServiceId,
    string DisplayName,
    WorkMinutes StandardMinutes,
    DisplayOrder DisplayOrder,
    bool IsEnabled);

/// <summary>不変スナップショット内の基本単価を定義します。</summary>
/// <param name="ServiceId">対象サービス。</param>
/// <param name="TimeCategoryId">対象時間区分。サービス全体の単価の場合は <see langword="null"/>。</param>
/// <param name="RateType">計算方法。</param>
/// <param name="Amount">設定された円単位の金額。</param>
public sealed record SnapshotRate(
    ServiceId ServiceId,
    TimeCategoryId? TimeCategoryId,
    RateType RateType,
    YenAmount Amount);

/// <summary>不変スナップショット内の割増ルールを定義します。</summary>
/// <param name="Id">安定した論理識別子。</param>
/// <param name="DisplayName">利用者向けの名称。</param>
/// <param name="CalculationType">計算方法。</param>
/// <param name="Percentage">割合ルールに使用する割合。</param>
/// <param name="Amount">固定額ルールに使用する金額。</param>
/// <param name="StartTime">時間制限がある場合の日単位の開始時刻。この時刻を含みます。</param>
/// <param name="EndTime">時間制限がある場合の日単位の終了時刻。この時刻を含みません。</param>
/// <param name="UsesNationalHolidays">国民の祝日を日付条件とするかどうか。</param>
/// <param name="Weekdays">一致対象の曜日。空の集合の場合、曜日条件を追加しません。</param>
/// <param name="Dates">一致対象の個別日付。空の集合の場合、個別日付条件を追加しません。</param>
/// <param name="ServiceIds">対象サービス。空の集合の場合は全サービスを表します。</param>
/// <param name="IsEnabled">このスナップショットでルールを適用するかどうか。</param>
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

/// <summary>不変スナップショット内の、記録単位の件数加算を定義します。</summary>
/// <param name="Id">安定した論理識別子。</param>
/// <param name="DisplayName">利用者向けの名称。</param>
/// <param name="Amount">該当する記録 1 件につき 1 回支給する円単位の金額。</param>
/// <param name="ServiceIds">対象サービス。空の集合の場合は全サービスを表します。</param>
/// <param name="IsEnabled">このスナップショットでルールを適用するかどうか。</param>
public sealed record SnapshotCountBonus(
    CountBonusId Id,
    string DisplayName,
    YenAmount Amount,
    IReadOnlySet<ServiceId> ServiceIds,
    bool IsEnabled);

/// <summary>1 件の不変スナップショットに含まれる給与設定をすべて保持します。</summary>
/// <param name="Id">スナップショット識別子。</param>
/// <param name="BasedOnId">派生元。存在しない場合があり、計算時には参照しません。</param>
/// <param name="HolidayCalendarVersionId">このスナップショットに固定された祝日データのバージョン。</param>
/// <param name="SchemaVersion">スナップショットのスキーマバージョン。</param>
/// <param name="CreatedAtUtc">スナップショットを作成した UTC 日時。</param>
/// <param name="Services">完全なサービス集合。</param>
/// <param name="TimeCategories">完全な時間区分集合。</param>
/// <param name="Rates">完全な単価集合。</param>
/// <param name="Premiums">完全な割増集合。</param>
/// <param name="CountBonuses">完全な件数加算集合。</param>
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

/// <summary>設定スナップショットで使用する固定済みの祝日を保持します。</summary>
/// <param name="VersionId">祝日カレンダーのバージョン。</param>
/// <param name="Holidays">祝日とその表示名。</param>
public sealed record HolidayCalendar(
    HolidayCalendarVersionId VersionId,
    IReadOnlyDictionary<DateOnly, string> Holidays);

/// <summary>指定した給与期間の月から始まる締め日ルールを定義します。</summary>
/// <param name="Id">履歴識別子。</param>
/// <param name="EffectiveFrom">このルールが適用される最初の給与期間キー。</param>
/// <param name="ClosingDay">指定された締め日。月末の場合は <see langword="null"/>。</param>
public sealed record ClosingRule(ClosingRuleId Id, PayrollPeriodKey EffectiveFrom, int? ClosingDay);

/// <summary>両端の日付を含む 1 給与期間を表します。</summary>
/// <param name="Key">終了月に基づく期間キー。</param>
/// <param name="StartDate">期間に含まれる開始日。</param>
/// <param name="EndDate">期間に含まれる終了日。</param>
public sealed record PayrollPeriod(PayrollPeriodKey Key, DateOnly StartDate, DateOnly EndDate);

/// <summary>1 給与期間に直接適用する月額手当を定義します。</summary>
/// <param name="Id">手当識別子。</param>
/// <param name="PayrollPeriodKey">対象の給与期間。</param>
/// <param name="DisplayName">利用者向けの名称。</param>
/// <param name="Amount">円単位の金額。</param>
public sealed record MonthlyAllowance(
    MonthlyAllowanceId Id,
    PayrollPeriodKey PayrollPeriodKey,
    string DisplayName,
    YenAmount Amount);
