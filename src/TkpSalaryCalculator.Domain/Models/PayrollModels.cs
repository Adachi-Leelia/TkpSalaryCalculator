using System.Collections.Frozen;
using System.Collections.ObjectModel;
using TkpSalaryCalculator.Domain.ValueObjects;

namespace TkpSalaryCalculator.Domain.Models;

/// <summary>給与計算へ渡す正規化済みの勤務記録を表します。</summary>
public sealed record WorkRecord
{
    /// <summary>勤務記録を生成します。</summary>
    public WorkRecord(
        WorkRecordId Id,
        DateOnly WorkDate,
        ServiceId ServiceId,
        TimeCategoryId? TimeCategoryId,
        WorkInputMode InputMode,
        WorkMinutes WorkMinutes,
        MinuteOfDay? StartTime,
        MinuteOfDay? EndTime)
    {
        DomainIdGuard.NotEmpty(Id.Value, nameof(Id));
        DomainIdGuard.NotEmpty(ServiceId.Value, nameof(ServiceId));
        if (TimeCategoryId is { } timeCategoryId)
        {
            DomainIdGuard.NotEmpty(timeCategoryId.Value, nameof(TimeCategoryId));
        }

        if (!Enum.IsDefined(InputMode))
        {
            throw new ArgumentOutOfRangeException(nameof(InputMode));
        }

        DomainValueGuard.ValidWorkMinutes(WorkMinutes, nameof(WorkMinutes));
        if (StartTime is { } startTime)
        {
            DomainValueGuard.ValidMinuteOfDay(startTime, nameof(StartTime));
        }

        if (EndTime is { } endTime)
        {
            DomainValueGuard.ValidMinuteOfDay(endTime, nameof(EndTime));
        }

        var normalizedEnd = ValidateAndNormalizeInterval(InputMode, WorkMinutes, StartTime, EndTime);

        this.Id = Id;
        this.WorkDate = WorkDate;
        this.ServiceId = ServiceId;
        this.TimeCategoryId = TimeCategoryId;
        this.InputMode = InputMode;
        this.WorkMinutes = WorkMinutes;
        this.StartTime = StartTime;
        this.EndTime = normalizedEnd;
    }

    /// <summary>勤務記録識別子を取得します。</summary>
    public WorkRecordId Id { get; }

    /// <summary>勤務開始日のローカル日付を取得します。</summary>
    public DateOnly WorkDate { get; }

    /// <summary>サービス識別子を取得します。</summary>
    public ServiceId ServiceId { get; }

    /// <summary>時間区分識別子を取得します。</summary>
    public TimeCategoryId? TimeCategoryId { get; }

    /// <summary>入力方式を取得します。</summary>
    public WorkInputMode InputMode { get; }

    /// <summary>勤務分数を取得します。</summary>
    public WorkMinutes WorkMinutes { get; }

    /// <summary>開始時刻を取得します。</summary>
    public MinuteOfDay? StartTime { get; }

    /// <summary>正規化済みの終了時刻を取得します。</summary>
    public MinuteOfDay? EndTime { get; }

    /// <summary>勤務記録の各値へ分解します。</summary>
    public void Deconstruct(
        out WorkRecordId Id,
        out DateOnly WorkDate,
        out ServiceId ServiceId,
        out TimeCategoryId? TimeCategoryId,
        out WorkInputMode InputMode,
        out WorkMinutes WorkMinutes,
        out MinuteOfDay? StartTime,
        out MinuteOfDay? EndTime)
    {
        (Id, WorkDate, ServiceId, TimeCategoryId, InputMode, WorkMinutes, StartTime, EndTime) =
            (this.Id, this.WorkDate, this.ServiceId, this.TimeCategoryId, this.InputMode, this.WorkMinutes, this.StartTime, this.EndTime);
    }

    private static MinuteOfDay? ValidateAndNormalizeInterval(
        WorkInputMode inputMode,
        WorkMinutes workMinutes,
        MinuteOfDay? startTime,
        MinuteOfDay? endTime)
    {
        if (inputMode == WorkInputMode.TimeRange)
        {
            if (startTime is null || endTime is null)
            {
                throw new ArgumentException("開始・終了時刻入力では両方の時刻が必要です。");
            }

            var difference = endTime.Value.Value - startTime.Value.Value;
            if (difference <= 0)
            {
                difference += 1440;
            }

            if (difference != workMinutes.Value)
            {
                throw new ArgumentException("勤務分数と開始・終了時刻から求めた勤務区間が一致しません。", nameof(workMinutes));
            }

            return endTime;
        }

        if (startTime is null)
        {
            if (endTime is not null)
            {
                throw new ArgumentException("終了時刻だけを指定することはできません。", nameof(endTime));
            }

            return null;
        }

        var derivedEnd = new MinuteOfDay((startTime.Value.Value + workMinutes.Value) % 1440);
        if (endTime is not null && endTime != derivedEnd)
        {
            throw new ArgumentException("勤務時間入力の終了時刻は開始時刻と勤務分数から導出した値と一致する必要があります。", nameof(endTime));
        }

        return derivedEnd;
    }
}

/// <summary>設定スナップショット内のサービスを表します。</summary>
public sealed record SnapshotService
{
    /// <summary>サービスを生成します。</summary>
    public SnapshotService(ServiceId Id, string DisplayName, DisplayOrder DisplayOrder, bool IsEnabled)
    {
        DomainIdGuard.NotEmpty(Id.Value, nameof(Id));
        DomainModelGuard.NotBlank(DisplayName, nameof(DisplayName));
        DomainValueGuard.NonNegative(DisplayOrder, nameof(DisplayOrder));
        this.Id = Id;
        this.DisplayName = DisplayName;
        this.DisplayOrder = DisplayOrder;
        this.IsEnabled = IsEnabled;
    }

    /// <summary>識別子を取得します。</summary>
    public ServiceId Id { get; }

    /// <summary>表示名を取得します。</summary>
    public string DisplayName { get; }

    /// <summary>表示順を取得します。</summary>
    public DisplayOrder DisplayOrder { get; }

    /// <summary>新規入力候補で有効かを取得します。</summary>
    public bool IsEnabled { get; }

    /// <summary>サービスの各値へ分解します。</summary>
    public void Deconstruct(out ServiceId Id, out string DisplayName, out DisplayOrder DisplayOrder, out bool IsEnabled) =>
        (Id, DisplayName, DisplayOrder, IsEnabled) = (this.Id, this.DisplayName, this.DisplayOrder, this.IsEnabled);
}

/// <summary>設定スナップショット内の時間区分を表します。</summary>
public sealed record SnapshotTimeCategory
{
    /// <summary>時間区分を生成します。</summary>
    public SnapshotTimeCategory(
        TimeCategoryId Id,
        ServiceId ServiceId,
        string DisplayName,
        WorkMinutes StandardMinutes,
        DisplayOrder DisplayOrder,
        bool IsEnabled)
    {
        DomainIdGuard.NotEmpty(Id.Value, nameof(Id));
        DomainIdGuard.NotEmpty(ServiceId.Value, nameof(ServiceId));
        DomainModelGuard.NotBlank(DisplayName, nameof(DisplayName));
        DomainValueGuard.ValidWorkMinutes(StandardMinutes, nameof(StandardMinutes));
        DomainValueGuard.NonNegative(DisplayOrder, nameof(DisplayOrder));
        this.Id = Id;
        this.ServiceId = ServiceId;
        this.DisplayName = DisplayName;
        this.StandardMinutes = StandardMinutes;
        this.DisplayOrder = DisplayOrder;
        this.IsEnabled = IsEnabled;
    }

    /// <summary>識別子を取得します。</summary>
    public TimeCategoryId Id { get; }

    /// <summary>所属サービスを取得します。</summary>
    public ServiceId ServiceId { get; }

    /// <summary>表示名を取得します。</summary>
    public string DisplayName { get; }

    /// <summary>標準勤務分数を取得します。</summary>
    public WorkMinutes StandardMinutes { get; }

    /// <summary>表示順を取得します。</summary>
    public DisplayOrder DisplayOrder { get; }

    /// <summary>新規入力候補で有効かを取得します。</summary>
    public bool IsEnabled { get; }

    /// <summary>時間区分の各値へ分解します。</summary>
    public void Deconstruct(
        out TimeCategoryId Id,
        out ServiceId ServiceId,
        out string DisplayName,
        out WorkMinutes StandardMinutes,
        out DisplayOrder DisplayOrder,
        out bool IsEnabled) =>
        (Id, ServiceId, DisplayName, StandardMinutes, DisplayOrder, IsEnabled) =
            (this.Id, this.ServiceId, this.DisplayName, this.StandardMinutes, this.DisplayOrder, this.IsEnabled);
}

/// <summary>設定スナップショット内の基本単価を表します。</summary>
public sealed record SnapshotRate
{
    /// <summary>基本単価を生成します。</summary>
    public SnapshotRate(
        ServiceId ServiceId,
        TimeCategoryId? TimeCategoryId,
        RateType RateType,
        YenAmount Amount)
    {
        DomainIdGuard.NotEmpty(ServiceId.Value, nameof(ServiceId));
        if (TimeCategoryId is { } timeCategoryId)
        {
            DomainIdGuard.NotEmpty(timeCategoryId.Value, nameof(TimeCategoryId));
        }

        if (!Enum.IsDefined(RateType))
        {
            throw new ArgumentOutOfRangeException(nameof(RateType));
        }

        DomainValueGuard.NonNegative(Amount, nameof(Amount));
        this.ServiceId = ServiceId;
        this.TimeCategoryId = TimeCategoryId;
        this.RateType = RateType;
        this.Amount = Amount;
    }

    /// <summary>対象サービスを取得します。</summary>
    public ServiceId ServiceId { get; }

    /// <summary>対象時間区分を取得します。サービス単位ではnullです。</summary>
    public TimeCategoryId? TimeCategoryId { get; }

    /// <summary>単価方式を取得します。</summary>
    public RateType RateType { get; }

    /// <summary>単価額を取得します。</summary>
    public YenAmount Amount { get; }

    /// <summary>単価の各値へ分解します。</summary>
    public void Deconstruct(
        out ServiceId ServiceId,
        out TimeCategoryId? TimeCategoryId,
        out RateType RateType,
        out YenAmount Amount) =>
        (ServiceId, TimeCategoryId, RateType, Amount) =
            (this.ServiceId, this.TimeCategoryId, this.RateType, this.Amount);
}

/// <summary>設定スナップショット内の割増ルールを表します。</summary>
public sealed record SnapshotPremium
{
    /// <summary>割増ルールを生成します。</summary>
    public SnapshotPremium(
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
        bool IsEnabled)
    {
        DomainIdGuard.NotEmpty(Id.Value, nameof(Id));
        DomainModelGuard.NotBlank(DisplayName, nameof(DisplayName));
        if (!Enum.IsDefined(CalculationType))
        {
            throw new ArgumentOutOfRangeException(nameof(CalculationType));
        }

        ValidateAmount(CalculationType, Percentage, Amount);
        ValidateTimeRange(StartTime, EndTime);

        ArgumentNullException.ThrowIfNull(Weekdays);
        ArgumentNullException.ThrowIfNull(Dates);
        ArgumentNullException.ThrowIfNull(ServiceIds);
        foreach (var weekday in Weekdays)
        {
            if (!Enum.IsDefined(weekday))
            {
                throw new ArgumentException("曜日に範囲外の値が含まれています。", nameof(Weekdays));
            }
        }

        foreach (var serviceId in ServiceIds)
        {
            DomainIdGuard.NotEmpty(serviceId.Value, nameof(ServiceIds));
        }

        this.Id = Id;
        this.DisplayName = DisplayName;
        this.CalculationType = CalculationType;
        this.Percentage = Percentage;
        this.Amount = Amount;
        this.StartTime = StartTime;
        this.EndTime = EndTime;
        this.UsesNationalHolidays = UsesNationalHolidays;
        this.Weekdays = Weekdays.ToFrozenSet();
        this.Dates = Dates.ToFrozenSet();
        this.ServiceIds = ServiceIds.ToFrozenSet();
        this.IsEnabled = IsEnabled;
    }

    /// <summary>識別子を取得します。</summary>
    public PremiumId Id { get; }

    /// <summary>表示名を取得します。</summary>
    public string DisplayName { get; }

    /// <summary>計算方式を取得します。</summary>
    public PremiumCalculationType CalculationType { get; }

    /// <summary>割合方式のbasis point値を取得します。</summary>
    public BasisPoints? Percentage { get; }

    /// <summary>固定額方式の金額を取得します。</summary>
    public YenAmount? Amount { get; }

    /// <summary>時間帯の開始時刻を取得します。</summary>
    public MinuteOfDay? StartTime { get; }

    /// <summary>時間帯の終了時刻を取得します。</summary>
    public MinuteOfDay? EndTime { get; }

    /// <summary>国民の祝日を日付条件に含めるかを取得します。</summary>
    public bool UsesNationalHolidays { get; }

    /// <summary>曜日条件を取得します。</summary>
    public IReadOnlySet<DayOfWeek> Weekdays { get; }

    /// <summary>個別日付条件を取得します。</summary>
    public IReadOnlySet<DateOnly> Dates { get; }

    /// <summary>対象サービスを取得します。空なら全サービスです。</summary>
    public IReadOnlySet<ServiceId> ServiceIds { get; }

    /// <summary>計算へ適用するかを取得します。</summary>
    public bool IsEnabled { get; }

    /// <summary>割増ルールの各値へ分解します。</summary>
    public void Deconstruct(
        out PremiumId Id,
        out string DisplayName,
        out PremiumCalculationType CalculationType,
        out BasisPoints? Percentage,
        out YenAmount? Amount,
        out MinuteOfDay? StartTime,
        out MinuteOfDay? EndTime,
        out bool UsesNationalHolidays,
        out IReadOnlySet<DayOfWeek> Weekdays,
        out IReadOnlySet<DateOnly> Dates,
        out IReadOnlySet<ServiceId> ServiceIds,
        out bool IsEnabled)
    {
        (Id, DisplayName, CalculationType, Percentage, Amount, StartTime, EndTime, UsesNationalHolidays,
            Weekdays, Dates, ServiceIds, IsEnabled) =
            (this.Id, this.DisplayName, this.CalculationType, this.Percentage, this.Amount, this.StartTime,
                this.EndTime, this.UsesNationalHolidays, this.Weekdays, this.Dates, this.ServiceIds, this.IsEnabled);
    }

    private static void ValidateAmount(
        PremiumCalculationType calculationType,
        BasisPoints? percentage,
        YenAmount? amount)
    {
        if (calculationType == PremiumCalculationType.Percentage)
        {
            if (percentage is null || amount is not null)
            {
                throw new ArgumentException("割合方式では割合だけを指定してください。");
            }

            DomainValueGuard.NonNegative(percentage.Value, nameof(percentage));
            return;
        }

        if (percentage is not null || amount is null)
        {
            throw new ArgumentException("固定額方式では金額だけを指定してください。");
        }

        DomainValueGuard.NonNegative(amount.Value, nameof(amount));
    }

    private static void ValidateTimeRange(MinuteOfDay? startTime, MinuteOfDay? endTime)
    {
        if (startTime.HasValue != endTime.HasValue)
        {
            throw new ArgumentException("割増時間帯は開始時刻と終了時刻を両方指定してください。");
        }

        if (startTime is null)
        {
            return;
        }

        DomainValueGuard.ValidMinuteOfDay(startTime.Value, nameof(startTime));
        DomainValueGuard.ValidMinuteOfDay(endTime!.Value, nameof(endTime));
        if (startTime == endTime)
        {
            throw new ArgumentException("割増時間帯の開始時刻と終了時刻は異なる値を指定してください。");
        }
    }
}

/// <summary>設定スナップショット内の勤務記録単位の件数加算を表します。</summary>
public sealed record SnapshotCountBonus
{
    /// <summary>件数加算を生成します。</summary>
    public SnapshotCountBonus(
        CountBonusId Id,
        string DisplayName,
        YenAmount Amount,
        IReadOnlySet<ServiceId> ServiceIds,
        bool IsEnabled)
    {
        DomainIdGuard.NotEmpty(Id.Value, nameof(Id));
        DomainModelGuard.NotBlank(DisplayName, nameof(DisplayName));
        DomainValueGuard.NonNegative(Amount, nameof(Amount));
        ArgumentNullException.ThrowIfNull(ServiceIds);
        foreach (var serviceId in ServiceIds)
        {
            DomainIdGuard.NotEmpty(serviceId.Value, nameof(ServiceIds));
        }

        this.Id = Id;
        this.DisplayName = DisplayName;
        this.Amount = Amount;
        this.ServiceIds = ServiceIds.ToFrozenSet();
        this.IsEnabled = IsEnabled;
    }

    /// <summary>識別子を取得します。</summary>
    public CountBonusId Id { get; }

    /// <summary>表示名を取得します。</summary>
    public string DisplayName { get; }

    /// <summary>1件当たりの金額を取得します。</summary>
    public YenAmount Amount { get; }

    /// <summary>対象サービスを取得します。空なら全サービスです。</summary>
    public IReadOnlySet<ServiceId> ServiceIds { get; }

    /// <summary>計算へ適用するかを取得します。</summary>
    public bool IsEnabled { get; }

    /// <summary>件数加算の各値へ分解します。</summary>
    public void Deconstruct(
        out CountBonusId Id,
        out string DisplayName,
        out YenAmount Amount,
        out IReadOnlySet<ServiceId> ServiceIds,
        out bool IsEnabled) =>
        (Id, DisplayName, Amount, ServiceIds, IsEnabled) =
            (this.Id, this.DisplayName, this.Amount, this.ServiceIds, this.IsEnabled);
}

/// <summary>1件の変更不可な設定スナップショットを表します。</summary>
public sealed record SettingSnapshot
{
    /// <summary>設定スナップショットを生成し、子要素の参照整合性を検証します。</summary>
    public SettingSnapshot(
        SettingSnapshotId Id,
        SettingSnapshotId? BasedOnId,
        HolidayCalendarVersionId HolidayCalendarVersionId,
        SchemaVersion SchemaVersion,
        DateTimeOffset CreatedAtUtc,
        IReadOnlyList<SnapshotService> Services,
        IReadOnlyList<SnapshotTimeCategory> TimeCategories,
        IReadOnlyList<SnapshotRate> Rates,
        IReadOnlyList<SnapshotPremium> Premiums,
        IReadOnlyList<SnapshotCountBonus> CountBonuses)
    {
        DomainIdGuard.NotEmpty(Id.Value, nameof(Id));
        if (BasedOnId is { } basedOnId)
        {
            DomainIdGuard.NotEmpty(basedOnId.Value, nameof(BasedOnId));
            if (basedOnId == Id)
            {
                throw new ArgumentException("スナップショットは自分自身を派生元にできません。", nameof(BasedOnId));
            }
        }

        DomainIdGuard.NotEmpty(HolidayCalendarVersionId.Value, nameof(HolidayCalendarVersionId));
        DomainValueGuard.Positive(SchemaVersion, nameof(SchemaVersion));
        if (CreatedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("作成日時はUTCで指定してください。", nameof(CreatedAtUtc));
        }

        var services = DomainModelGuard.Copy(Services, nameof(Services));
        var timeCategories = DomainModelGuard.Copy(TimeCategories, nameof(TimeCategories));
        var rates = DomainModelGuard.Copy(Rates, nameof(Rates));
        var premiums = DomainModelGuard.Copy(Premiums, nameof(Premiums));
        var countBonuses = DomainModelGuard.Copy(CountBonuses, nameof(CountBonuses));
        ValidateChildren(services, timeCategories, rates, premiums, countBonuses);

        this.Id = Id;
        this.BasedOnId = BasedOnId;
        this.HolidayCalendarVersionId = HolidayCalendarVersionId;
        this.SchemaVersion = SchemaVersion;
        this.CreatedAtUtc = CreatedAtUtc;
        this.Services = services;
        this.TimeCategories = timeCategories;
        this.Rates = rates;
        this.Premiums = premiums;
        this.CountBonuses = countBonuses;
    }

    /// <summary>識別子を取得します。</summary>
    public SettingSnapshotId Id { get; }

    /// <summary>派生元の識別子を取得します。</summary>
    public SettingSnapshotId? BasedOnId { get; }

    /// <summary>固定された祝日カレンダーバージョンを取得します。</summary>
    public HolidayCalendarVersionId HolidayCalendarVersionId { get; }

    /// <summary>スキーマバージョンを取得します。</summary>
    public SchemaVersion SchemaVersion { get; }

    /// <summary>作成日時を取得します。</summary>
    public DateTimeOffset CreatedAtUtc { get; }

    /// <summary>サービスを取得します。</summary>
    public IReadOnlyList<SnapshotService> Services { get; }

    /// <summary>時間区分を取得します。</summary>
    public IReadOnlyList<SnapshotTimeCategory> TimeCategories { get; }

    /// <summary>基本単価を取得します。</summary>
    public IReadOnlyList<SnapshotRate> Rates { get; }

    /// <summary>割増ルールを取得します。</summary>
    public IReadOnlyList<SnapshotPremium> Premiums { get; }

    /// <summary>件数加算を取得します。</summary>
    public IReadOnlyList<SnapshotCountBonus> CountBonuses { get; }

    /// <summary>設定スナップショットの各値へ分解します。</summary>
    public void Deconstruct(
        out SettingSnapshotId Id,
        out SettingSnapshotId? BasedOnId,
        out HolidayCalendarVersionId HolidayCalendarVersionId,
        out SchemaVersion SchemaVersion,
        out DateTimeOffset CreatedAtUtc,
        out IReadOnlyList<SnapshotService> Services,
        out IReadOnlyList<SnapshotTimeCategory> TimeCategories,
        out IReadOnlyList<SnapshotRate> Rates,
        out IReadOnlyList<SnapshotPremium> Premiums,
        out IReadOnlyList<SnapshotCountBonus> CountBonuses)
    {
        (Id, BasedOnId, HolidayCalendarVersionId, SchemaVersion, CreatedAtUtc, Services, TimeCategories,
            Rates, Premiums, CountBonuses) =
            (this.Id, this.BasedOnId, this.HolidayCalendarVersionId, this.SchemaVersion, this.CreatedAtUtc,
                this.Services, this.TimeCategories, this.Rates, this.Premiums, this.CountBonuses);
    }

    private static void ValidateChildren(
        IReadOnlyList<SnapshotService> services,
        IReadOnlyList<SnapshotTimeCategory> timeCategories,
        IReadOnlyList<SnapshotRate> rates,
        IReadOnlyList<SnapshotPremium> premiums,
        IReadOnlyList<SnapshotCountBonus> countBonuses)
    {
        DomainModelGuard.Unique(services, static item => item.Id, nameof(services));
        DomainModelGuard.UniqueNormalizedNames(services.Select(static item => item.DisplayName), nameof(services));
        DomainModelGuard.Unique(timeCategories, static item => item.Id, nameof(timeCategories));
        DomainModelGuard.Unique(premiums, static item => item.Id, nameof(premiums));
        DomainModelGuard.Unique(countBonuses, static item => item.Id, nameof(countBonuses));

        var serviceIds = services.Select(static item => item.Id).ToHashSet();
        var categoryById = timeCategories.ToDictionary(static item => item.Id);
        foreach (var category in timeCategories)
        {
            if (!serviceIds.Contains(category.ServiceId))
            {
                throw new ArgumentException("時間区分が同じスナップショットに存在しないサービスを参照しています。", nameof(timeCategories));
            }
        }

        var rateKeys = new HashSet<(ServiceId, TimeCategoryId?)>();
        foreach (var rate in rates)
        {
            if (!serviceIds.Contains(rate.ServiceId))
            {
                throw new ArgumentException("単価が同じスナップショットに存在しないサービスを参照しています。", nameof(rates));
            }

            if (rate.TimeCategoryId is { } categoryId &&
                (!categoryById.TryGetValue(categoryId, out var category) || category.ServiceId != rate.ServiceId))
            {
                throw new ArgumentException("単価の時間区分とサービスの組み合わせが不正です。", nameof(rates));
            }

            if (!rateKeys.Add((rate.ServiceId, rate.TimeCategoryId)))
            {
                throw new ArgumentException("同じ優先順位の単価が重複しています。", nameof(rates));
            }
        }

        foreach (var premium in premiums)
        {
            if (!premium.ServiceIds.IsSubsetOf(serviceIds))
            {
                throw new ArgumentException("割増ルールが同じスナップショットに存在しないサービスを参照しています。", nameof(premiums));
            }
        }

        foreach (var countBonus in countBonuses)
        {
            if (!countBonus.ServiceIds.IsSubsetOf(serviceIds))
            {
                throw new ArgumentException("件数加算が同じスナップショットに存在しないサービスを参照しています。", nameof(countBonuses));
            }
        }
    }
}

/// <summary>設定スナップショットが参照する固定済みの祝日を表します。</summary>
public sealed record HolidayCalendar
{
    /// <summary>祝日カレンダーを生成します。</summary>
    public HolidayCalendar(HolidayCalendarVersionId VersionId, IReadOnlyDictionary<DateOnly, string> Holidays)
    {
        DomainIdGuard.NotEmpty(VersionId.Value, nameof(VersionId));
        ArgumentNullException.ThrowIfNull(Holidays);
        foreach (var holiday in Holidays)
        {
            DomainModelGuard.NotBlank(holiday.Value, nameof(Holidays));
        }

        this.VersionId = VersionId;
        this.Holidays = Holidays.ToFrozenDictionary();
    }

    /// <summary>バージョン識別子を取得します。</summary>
    public HolidayCalendarVersionId VersionId { get; }

    /// <summary>祝日と表示名を取得します。</summary>
    public IReadOnlyDictionary<DateOnly, string> Holidays { get; }

    /// <summary>祝日カレンダーの各値へ分解します。</summary>
    public void Deconstruct(
        out HolidayCalendarVersionId VersionId,
        out IReadOnlyDictionary<DateOnly, string> Holidays) =>
        (VersionId, Holidays) = (this.VersionId, this.Holidays);
}

/// <summary>指定した給与期間月から適用される締め日ルールを表します。</summary>
public sealed record ClosingRule
{
    /// <summary>締め日ルールを生成します。月末締めではClosingDayをnullにします。</summary>
    public ClosingRule(ClosingRuleId Id, PayrollPeriodKey EffectiveFrom, int? ClosingDay)
    {
        DomainIdGuard.NotEmpty(Id.Value, nameof(Id));
        DomainValueGuard.ValidPayrollPeriodKey(EffectiveFrom, nameof(EffectiveFrom));
        if (ClosingDay is < 1 or > 31)
        {
            throw new ArgumentOutOfRangeException(nameof(ClosingDay), ClosingDay, "締め日は1から31、または月末締めを表すnullで指定してください。");
        }

        this.Id = Id;
        this.EffectiveFrom = EffectiveFrom;
        this.ClosingDay = ClosingDay;
    }

    /// <summary>履歴識別子を取得します。</summary>
    public ClosingRuleId Id { get; }

    /// <summary>適用開始給与期間月を取得します。</summary>
    public PayrollPeriodKey EffectiveFrom { get; }

    /// <summary>締め日を取得します。nullは月末締めです。</summary>
    public int? ClosingDay { get; }

    /// <summary>締め日ルールの各値へ分解します。</summary>
    public void Deconstruct(out ClosingRuleId Id, out PayrollPeriodKey EffectiveFrom, out int? ClosingDay) =>
        (Id, EffectiveFrom, ClosingDay) = (this.Id, this.EffectiveFrom, this.ClosingDay);
}

/// <summary>両端の日付を含む1給与期間を表します。</summary>
public sealed record PayrollPeriod
{
    /// <summary>給与期間を生成します。</summary>
    public PayrollPeriod(PayrollPeriodKey Key, DateOnly StartDate, DateOnly EndDate)
    {
        DomainValueGuard.ValidPayrollPeriodKey(Key, nameof(Key));
        if (StartDate > EndDate)
        {
            throw new ArgumentException("給与期間の開始日は終了日以前である必要があります。", nameof(StartDate));
        }

        if (EndDate.Year != Key.Value.Year || EndDate.Month != Key.Value.Month)
        {
            throw new ArgumentException("給与期間キーは終了日が属する年月と一致する必要があります。", nameof(Key));
        }

        this.Key = Key;
        this.StartDate = StartDate;
        this.EndDate = EndDate;
    }

    /// <summary>給与期間キーを取得します。</summary>
    public PayrollPeriodKey Key { get; }

    /// <summary>期間に含まれる開始日を取得します。</summary>
    public DateOnly StartDate { get; }

    /// <summary>期間に含まれる終了日を取得します。</summary>
    public DateOnly EndDate { get; }

    /// <summary>日付が両端を含む期間内かを返します。</summary>
    public bool Contains(DateOnly date) => StartDate <= date && date <= EndDate;

    /// <summary>給与期間の各値へ分解します。</summary>
    public void Deconstruct(out PayrollPeriodKey Key, out DateOnly StartDate, out DateOnly EndDate) =>
        (Key, StartDate, EndDate) = (this.Key, this.StartDate, this.EndDate);
}

/// <summary>1給与期間へ直接適用する月額手当を表します。</summary>
public sealed record MonthlyAllowance
{
    /// <summary>月額手当を生成します。</summary>
    public MonthlyAllowance(
        MonthlyAllowanceId Id,
        PayrollPeriodKey PayrollPeriodKey,
        string DisplayName,
        YenAmount Amount)
    {
        DomainIdGuard.NotEmpty(Id.Value, nameof(Id));
        DomainValueGuard.ValidPayrollPeriodKey(PayrollPeriodKey, nameof(PayrollPeriodKey));
        DomainModelGuard.NotBlank(DisplayName, nameof(DisplayName));
        DomainValueGuard.NonNegative(Amount, nameof(Amount));
        this.Id = Id;
        this.PayrollPeriodKey = PayrollPeriodKey;
        this.DisplayName = DisplayName;
        this.Amount = Amount;
    }

    /// <summary>識別子を取得します。</summary>
    public MonthlyAllowanceId Id { get; }

    /// <summary>対象給与期間を取得します。</summary>
    public PayrollPeriodKey PayrollPeriodKey { get; }

    /// <summary>表示名を取得します。</summary>
    public string DisplayName { get; }

    /// <summary>金額を取得します。</summary>
    public YenAmount Amount { get; }

    /// <summary>月額手当の各値へ分解します。</summary>
    public void Deconstruct(
        out MonthlyAllowanceId Id,
        out PayrollPeriodKey PayrollPeriodKey,
        out string DisplayName,
        out YenAmount Amount) =>
        (Id, PayrollPeriodKey, DisplayName, Amount) =
            (this.Id, this.PayrollPeriodKey, this.DisplayName, this.Amount);
}

internal static class DomainModelGuard
{
    public static void NotBlank(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("表示名は空白以外の文字を含む必要があります。", parameterName);
        }
    }

    public static IReadOnlyList<T> Copy<T>(IReadOnlyList<T> values, string parameterName)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);
        var copy = values.ToArray();
        if (copy.Any(static value => value is null))
        {
            throw new ArgumentException("コレクションにnullを含めることはできません。", parameterName);
        }

        return new ReadOnlyCollection<T>(copy);
    }

    public static void Unique<T, TKey>(IEnumerable<T> values, Func<T, TKey> keySelector, string parameterName)
        where TKey : notnull
    {
        var keys = new HashSet<TKey>();
        if (values.Any(value => !keys.Add(keySelector(value))))
        {
            throw new ArgumentException("同じ識別子を持つ要素が重複しています。", parameterName);
        }
    }

    public static void UniqueNormalizedNames(IEnumerable<string> names, string parameterName)
    {
        var normalizedNames = new HashSet<string>(StringComparer.Ordinal);
        if (names.Any(name => !normalizedNames.Add(name.Trim())))
        {
            throw new ArgumentException("前後の空白を除いた表示名が重複しています。", parameterName);
        }
    }
}
