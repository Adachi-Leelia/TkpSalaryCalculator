namespace TkpSalaryCalculator.Domain.ValueObjects;

/// <summary>タイムゾーンを持たない暦年と月を表します。</summary>
/// <param name="Year">1 から 9999 までの年。</param>
/// <param name="Month">1 から 12 までの月。</param>
public readonly record struct YearMonth(int Year, int Month);

/// <summary>終了日を含む年月によって給与期間を識別します。</summary>
/// <param name="Value">給与期間の年月。</param>
public readonly record struct PayrollPeriodKey(YearMonth Value);

/// <summary>0 以上の円単位の金額を表します。</summary>
/// <param name="Value">円単位の金額。</param>
public readonly record struct YenAmount(long Value);

/// <summary>1 分から 1,440 分までの分単位の時間を表します。</summary>
/// <param name="Value">分単位の時間。</param>
public readonly record struct WorkMinutes(int Value);

/// <summary>現地時刻を午前 0 時からの経過分数で表します。</summary>
/// <param name="Value">0 から 1,439 までの値。</param>
public readonly record struct MinuteOfDay(int Value);

/// <summary>10,000 を 100% とする、0 以上のベーシスポイント値を表します。</summary>
/// <param name="Value">ベーシスポイント単位の割合。</param>
public readonly record struct BasisPoints(int Value);

/// <summary>0 以上の表示順を表します。</summary>
/// <param name="Value">表示順の値。</param>
public readonly record struct DisplayOrder(int Value);

/// <summary>1 以上のスキーマバージョンを表します。</summary>
/// <param name="Value">バージョン番号。</param>
public readonly record struct SchemaVersion(int Value);

/// <summary>勤務時間の入力方法を定義します。</summary>
public enum WorkInputMode
{
    /// <summary>開始時刻と終了時刻から勤務時間を算出します。</summary>
    TimeRange,

    /// <summary>勤務時間を直接入力します。</summary>
    Duration,
}

/// <summary>基本給の計算方法を定義します。</summary>
public enum RateType
{
    /// <summary>設定金額を時給として扱います。</summary>
    Hourly,

    /// <summary>勤務記録 1 件につき設定金額を 1 回支給します。</summary>
    FixedPerRecord,
}

/// <summary>割増額の計算方法を定義します。</summary>
public enum PremiumCalculationType
{
    /// <summary>対象となる基本給部分に対する割合で計算します。</summary>
    Percentage,

    /// <summary>対象時間 1 時間ごとの固定額で計算します。</summary>
    FixedPerHour,

    /// <summary>該当する勤務記録 1 件につき固定額を 1 回支給します。</summary>
    FixedPerRecord,
}
