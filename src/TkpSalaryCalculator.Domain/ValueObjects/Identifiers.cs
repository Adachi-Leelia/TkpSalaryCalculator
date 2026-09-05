namespace TkpSalaryCalculator.Domain.ValueObjects;

/// <summary>保存済み勤務記録を識別します。</summary>
public readonly record struct WorkRecordId
{
    /// <summary>識別子を生成します。</summary>
    public WorkRecordId(Guid Value) { DomainIdGuard.NotEmpty(Value, nameof(Value)); this.Value = Value; }
    /// <summary>GUID値を取得します。</summary>
    public Guid Value { get; }
    /// <summary>GUID値へ分解します。</summary>
    public void Deconstruct(out Guid Value)
    {
        Value = this.Value;
    }

}

/// <summary>訪問内の勤務タスクを識別します。</summary>
public readonly record struct WorkTaskId
{
    /// <summary>識別子を生成します。</summary>
    public WorkTaskId(Guid Value) { DomainIdGuard.NotEmpty(Value, nameof(Value)); this.Value = Value; }
    /// <summary>GUID値を取得します。</summary>
    public Guid Value { get; }
    /// <summary>GUID値へ分解します。</summary>
    public void Deconstruct(out Guid Value)
    {
        Value = this.Value;
    }
}

/// <summary>設定月をまたいでサービス定義を識別します。</summary>
public readonly record struct ServiceId
{
    /// <summary>識別子を生成します。</summary>
    public ServiceId(Guid Value) { DomainIdGuard.NotEmpty(Value, nameof(Value)); this.Value = Value; }
    /// <summary>GUID値を取得します。</summary>
    public Guid Value { get; }
    /// <summary>GUID値へ分解します。</summary>
    public void Deconstruct(out Guid Value)
    {
        Value = this.Value;
    }

}

/// <summary>設定月をまたいで時間区分を識別します。</summary>
public readonly record struct TimeCategoryId
{
    /// <summary>識別子を生成します。</summary>
    public TimeCategoryId(Guid Value) { DomainIdGuard.NotEmpty(Value, nameof(Value)); this.Value = Value; }
    /// <summary>GUID値を取得します。</summary>
    public Guid Value { get; }
    /// <summary>GUID値へ分解します。</summary>
    public void Deconstruct(out Guid Value)
    {
        Value = this.Value;
    }

}

/// <summary>設定月をまたいで割増定義を識別します。</summary>
public readonly record struct PremiumId
{
    /// <summary>識別子を生成します。</summary>
    public PremiumId(Guid Value) { DomainIdGuard.NotEmpty(Value, nameof(Value)); this.Value = Value; }
    /// <summary>GUID値を取得します。</summary>
    public Guid Value { get; }
    /// <summary>GUID値へ分解します。</summary>
    public void Deconstruct(out Guid Value)
    {
        Value = this.Value;
    }

}

/// <summary>設定月をまたいで件数加算定義を識別します。</summary>
public readonly record struct CountBonusId
{
    /// <summary>識別子を生成します。</summary>
    public CountBonusId(Guid Value) { DomainIdGuard.NotEmpty(Value, nameof(Value)); this.Value = Value; }
    /// <summary>GUID値を取得します。</summary>
    public Guid Value { get; }
    /// <summary>GUID値へ分解します。</summary>
    public void Deconstruct(out Guid Value)
    {
        Value = this.Value;
    }

}

/// <summary>不変の設定スナップショットを識別します。</summary>
public readonly record struct SettingSnapshotId
{
    /// <summary>識別子を生成します。</summary>
    public SettingSnapshotId(Guid Value) { DomainIdGuard.NotEmpty(Value, nameof(Value)); this.Value = Value; }
    /// <summary>GUID値を取得します。</summary>
    public Guid Value { get; }
    /// <summary>GUID値へ分解します。</summary>
    public void Deconstruct(out Guid Value)
    {
        Value = this.Value;
    }

}

/// <summary>締め日ルールの履歴項目を識別します。</summary>
public readonly record struct ClosingRuleId
{
    /// <summary>識別子を生成します。</summary>
    public ClosingRuleId(Guid Value) { DomainIdGuard.NotEmpty(Value, nameof(Value)); this.Value = Value; }
    /// <summary>GUID値を取得します。</summary>
    public Guid Value { get; }
    /// <summary>GUID値へ分解します。</summary>
    public void Deconstruct(out Guid Value)
    {
        Value = this.Value;
    }

}

/// <summary>月額手当を識別します。</summary>
public readonly record struct MonthlyAllowanceId
{
    /// <summary>識別子を生成します。</summary>
    public MonthlyAllowanceId(Guid Value) { DomainIdGuard.NotEmpty(Value, nameof(Value)); this.Value = Value; }
    /// <summary>GUID値を取得します。</summary>
    public Guid Value { get; }
    /// <summary>GUID値へ分解します。</summary>
    public void Deconstruct(out Guid Value)
    {
        Value = this.Value;
    }

}

/// <summary>祝日カレンダーのバージョンを識別します。</summary>
public readonly record struct HolidayCalendarVersionId
{
    /// <summary>識別子を生成します。</summary>
    public HolidayCalendarVersionId(Guid Value) { DomainIdGuard.NotEmpty(Value, nameof(Value)); this.Value = Value; }
    /// <summary>GUID値を取得します。</summary>
    public Guid Value { get; }
    /// <summary>GUID値へ分解します。</summary>
    public void Deconstruct(out Guid Value)
    {
        Value = this.Value;
    }

}

/// <summary>再利用可能なサービスプリセットを識別します。</summary>
public readonly record struct ServicePresetId
{
    /// <summary>識別子を生成します。</summary>
    public ServicePresetId(Guid Value) { DomainIdGuard.NotEmpty(Value, nameof(Value)); this.Value = Value; }
    /// <summary>GUID値を取得します。</summary>
    public Guid Value { get; }
    /// <summary>GUID値へ分解します。</summary>
    public void Deconstruct(out Guid Value)
    {
        Value = this.Value;
    }

}

/// <summary>基本シフトを識別します。</summary>
public readonly record struct BasicShiftId
{
    /// <summary>識別子を生成します。</summary>
    public BasicShiftId(Guid Value) { DomainIdGuard.NotEmpty(Value, nameof(Value)); this.Value = Value; }
    /// <summary>GUID値を取得します。</summary>
    public Guid Value { get; }
    /// <summary>GUID値へ分解します。</summary>
    public void Deconstruct(out Guid Value)
    {
        Value = this.Value;
    }

}

/// <summary>基本シフト内の勤務タスクを識別します。</summary>
public readonly record struct BasicShiftTaskId
{
    /// <summary>識別子を生成します。</summary>
    public BasicShiftTaskId(Guid Value) { DomainIdGuard.NotEmpty(Value, nameof(Value)); this.Value = Value; }
    /// <summary>GUID値を取得します。</summary>
    public Guid Value { get; }
    /// <summary>GUID値へ分解します。</summary>
    public void Deconstruct(out Guid Value)
    {
        Value = this.Value;
    }
}

internal static class DomainIdGuard
{
    public static void NotEmpty(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("空の識別子は使用できません。", parameterName);
        }
    }
}
