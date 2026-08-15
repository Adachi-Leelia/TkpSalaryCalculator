namespace TkpSalaryCalculator.Domain.ValueObjects;

/// <summary>タイムゾーンを持たない暦年と月を表します。</summary>
public readonly record struct YearMonth : IComparable<YearMonth>
{
    /// <summary>指定した年と月を生成します。</summary>
    public YearMonth(int Year, int Month)
    {
        if (Year is < 1 or > 9999)
        {
            throw new ArgumentOutOfRangeException(nameof(Year), Year, "年は1から9999の範囲で指定してください。");
        }

        if (Month is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(Month), Month, "月は1から12の範囲で指定してください。");
        }

        this.Year = Year;
        this.Month = Month;
    }

    /// <summary>年を取得します。</summary>
    public int Year { get; }

    /// <summary>月を取得します。</summary>
    public int Month { get; }

    /// <summary>指定した月数を加算します。</summary>
    public YearMonth AddMonths(int months)
    {
        var date = new DateOnly(Year, Month, 1).AddMonths(months);
        return new YearMonth(date.Year, date.Month);
    }

    /// <inheritdoc />
    public int CompareTo(YearMonth other)
    {
        var yearComparison = Year.CompareTo(other.Year);
        return yearComparison != 0 ? yearComparison : Month.CompareTo(other.Month);
    }

    /// <summary>年と月へ分解します。</summary>
    public void Deconstruct(out int year, out int month)
    {
        (year, month) = (Year, Month);
    }

}

/// <summary>終了月によって給与期間を識別します。</summary>
public readonly record struct PayrollPeriodKey
{
    /// <summary>給与期間キーを生成します。</summary>
    public PayrollPeriodKey(YearMonth Value)
    {
        DomainValueGuard.ValidYearMonth(Value, nameof(Value));
        this.Value = Value;
    }

    /// <summary>給与期間の終了日が属する年月を取得します。</summary>
    public YearMonth Value { get; }

    /// <summary>値へ分解します。</summary>
    public void Deconstruct(out YearMonth Value)
    {
        Value = this.Value;
    }

}

/// <summary>0以上の整数円を表します。</summary>
public readonly record struct YenAmount
{
    /// <summary>金額を生成します。</summary>
    public YenAmount(long Value)
    {
        if (Value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Value), Value, "金額は0円以上で指定してください。");
        }

        this.Value = Value;
    }

    /// <summary>整数円を取得します。</summary>
    public long Value { get; }

    /// <summary>値へ分解します。</summary>
    public void Deconstruct(out long Value)
    {
        Value = this.Value;
    }

}

/// <summary>1分から1,440分までの勤務時間を表します。</summary>
public readonly record struct WorkMinutes
{
    /// <summary>勤務分数を生成します。</summary>
    public WorkMinutes(int Value)
    {
        if (Value is < 1 or > 1440)
        {
            throw new ArgumentOutOfRangeException(nameof(Value), Value, "勤務時間は1分から1,440分の範囲で指定してください。");
        }

        this.Value = Value;
    }

    /// <summary>勤務分数を取得します。</summary>
    public int Value { get; }

    /// <summary>値へ分解します。</summary>
    public void Deconstruct(out int Value)
    {
        Value = this.Value;
    }

}

/// <summary>現地時刻を午前0時からの経過分数で表します。</summary>
public readonly record struct MinuteOfDay
{
    /// <summary>時刻を生成します。</summary>
    public MinuteOfDay(int Value)
    {
        if (Value is < 0 or > 1439)
        {
            throw new ArgumentOutOfRangeException(nameof(Value), Value, "時刻は0から1,439の範囲で指定してください。");
        }

        this.Value = Value;
    }

    /// <summary>午前0時からの経過分数を取得します。</summary>
    public int Value { get; }

    /// <summary>値へ分解します。</summary>
    public void Deconstruct(out int Value)
    {
        Value = this.Value;
    }

}

/// <summary>10,000を100%とする0以上の割合を表します。</summary>
public readonly record struct BasisPoints
{
    /// <summary>basis point値を生成します。</summary>
    public BasisPoints(int Value)
    {
        if (Value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Value), Value, "割合は0以上で指定してください。");
        }

        this.Value = Value;
    }

    /// <summary>basis point値を取得します。</summary>
    public int Value { get; }

    /// <summary>値へ分解します。</summary>
    public void Deconstruct(out int Value)
    {
        Value = this.Value;
    }

}

/// <summary>0以上の表示順を表します。</summary>
public readonly record struct DisplayOrder
{
    /// <summary>表示順を生成します。</summary>
    public DisplayOrder(int Value)
    {
        if (Value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Value), Value, "表示順は0以上で指定してください。");
        }

        this.Value = Value;
    }

    /// <summary>表示順を取得します。</summary>
    public int Value { get; }

    /// <summary>値へ分解します。</summary>
    public void Deconstruct(out int Value)
    {
        Value = this.Value;
    }

}

/// <summary>1以上のスキーマバージョンを表します。</summary>
public readonly record struct SchemaVersion
{
    /// <summary>スキーマバージョンを生成します。</summary>
    public SchemaVersion(int Value)
    {
        if (Value < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(Value), Value, "スキーマバージョンは1以上で指定してください。");
        }

        this.Value = Value;
    }

    /// <summary>スキーマバージョンを取得します。</summary>
    public int Value { get; }

    /// <summary>値へ分解します。</summary>
    public void Deconstruct(out int Value)
    {
        Value = this.Value;
    }

}

/// <summary>勤務時間の入力方式を定義します。</summary>
public enum WorkInputMode
{
    /// <summary>開始時刻と終了時刻から勤務時間を計算します。</summary>
    TimeRange,

    /// <summary>勤務時間を直接入力します。</summary>
    Duration,
}

/// <summary>基本給与の計算方式を定義します。</summary>
public enum RateType
{
    /// <summary>設定金額を時給として扱います。</summary>
    Hourly,

    /// <summary>勤務記録1件につき設定金額を1回支給します。</summary>
    FixedPerRecord,
}

/// <summary>割増額の計算方式を定義します。</summary>
public enum PremiumCalculationType
{
    /// <summary>対象となる基本給与部分に割合を適用します。</summary>
    Percentage,

    /// <summary>対象時間1時間ごとの固定額で計算します。</summary>
    FixedPerHour,

    /// <summary>該当する勤務記録1件につき固定額を1回支給します。</summary>
    FixedPerRecord,
}

internal static class DomainValueGuard
{
    public static void ValidYearMonth(YearMonth value, string parameterName)
    {
        if (value.Year is < 1 or > 9999 || value.Month is < 1 or > 12)
        {
            throw new ArgumentException("有効な年月を指定してください。", parameterName);
        }
    }

    public static void ValidPayrollPeriodKey(PayrollPeriodKey value, string parameterName)
    {
        ValidYearMonth(value.Value, parameterName);
    }


    public static void NonNegative(YenAmount value, string parameterName)
    {
        if (value.Value < 0)
        {
            throw new ArgumentException("金額は0円以上で指定してください。", parameterName);
        }
    }

    public static void ValidWorkMinutes(WorkMinutes value, string parameterName)
    {
        if (value.Value is < 1 or > 1440)
        {
            throw new ArgumentException("勤務時間は1分から1,440分の範囲で指定してください。", parameterName);
        }
    }

    public static void ValidMinuteOfDay(MinuteOfDay value, string parameterName)
    {
        if (value.Value is < 0 or > 1439)
        {
            throw new ArgumentException("時刻は0から1,439の範囲で指定してください。", parameterName);
        }
    }

    public static void NonNegative(BasisPoints value, string parameterName)
    {
        if (value.Value < 0)
        {
            throw new ArgumentException("割合は0以上で指定してください。", parameterName);
        }
    }

    public static void NonNegative(DisplayOrder value, string parameterName)
    {
        if (value.Value < 0)
        {
            throw new ArgumentException("表示順は0以上で指定してください。", parameterName);
        }
    }

    public static void Positive(SchemaVersion value, string parameterName)
    {
        if (value.Value < 1)
        {
            throw new ArgumentException("スキーマバージョンは1以上で指定してください。", parameterName);
        }
    }
}
