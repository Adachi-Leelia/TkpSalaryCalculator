using TkpSalaryCalculator.App.Presentation.Common;
using TkpSalaryCalculator.Application.Contracts;
using TkpSalaryCalculator.Application.UseCases;
using TkpSalaryCalculator.Domain.Contracts;
using TkpSalaryCalculator.Domain.Models;
using TkpSalaryCalculator.Domain.Services;
using TkpSalaryCalculator.Domain.ValueObjects;

namespace TkpSalaryCalculator.App.Presentation.Features.Home;

/// <summary>SCR-CALC-01 の給与期間、日別、勤務記録別の計算内訳を構築します。</summary>
public sealed class CalculationDetailViewModel : ViewModelBase
{
    private readonly ISalaryQueryUseCase salaryQuery;
    private readonly IPayrollPeriodSettingsUseCase payrollPeriods;
    private readonly JapaneseDisplayFormatter formatter;
    private PayrollPeriodKey? payrollPeriodKey;
    private DateOnly? selectedDate;
    private WorkRecordId? selectedWorkRecordId;
    private string startDateText = string.Empty;
    private string endDateText = string.Empty;
    private string totalText = "0円";
    private string basePayText = "0円";
    private string premiumText = "0円";
    private string countBonusText = "0円";
    private string allowanceText = "0円";
    private string uncalculatedText = string.Empty;
    private IReadOnlyList<CalculationPremiumTotalRowViewModel> premiumTotals = [];
    private IReadOnlyList<CalculationAllowanceRowViewModel> allowances = [];
    private IReadOnlyList<CalculationDayRowViewModel> days = [];

    public CalculationDetailViewModel(
        ISalaryQueryUseCase salaryQuery,
        IPayrollPeriodSettingsUseCase payrollPeriods,
        JapaneseDisplayFormatter formatter,
        IUserErrorPresenter errorPresenter) : base(errorPresenter)
    {
        this.salaryQuery = salaryQuery ?? throw new ArgumentNullException(nameof(salaryQuery));
        this.payrollPeriods = payrollPeriods ?? throw new ArgumentNullException(nameof(payrollPeriods));
        this.formatter = formatter ?? throw new ArgumentNullException(nameof(formatter));
        ReloadCommand = new AsyncCommand(LoadAsync, PresentError);
    }

    public string StartDateText { get => startDateText; private set => SetProperty(ref startDateText, value); }
    public string EndDateText { get => endDateText; private set => SetProperty(ref endDateText, value); }
    public string TotalText { get => totalText; private set => SetProperty(ref totalText, value); }
    public string TotalLabel => HasUncalculated ? "計算済み分の小計" : "給与期間合計";
    public string BasePayText { get => basePayText; private set => SetProperty(ref basePayText, value); }
    public string PremiumText { get => premiumText; private set => SetProperty(ref premiumText, value); }
    public string CountBonusText { get => countBonusText; private set => SetProperty(ref countBonusText, value); }
    public string AllowanceText { get => allowanceText; private set => SetProperty(ref allowanceText, value); }
    public string UncalculatedText
    {
        get => uncalculatedText;
        private set
        {
            if (!SetProperty(ref uncalculatedText, value)) return;
            OnPropertyChanged(nameof(HasUncalculated));
            OnPropertyChanged(nameof(HasPeriodUncalculated));
            OnPropertyChanged(nameof(TotalLabel));
        }
    }
    public bool HasUncalculated => !string.IsNullOrWhiteSpace(UncalculatedText);
    /// <summary>給与期間全体を開いた場合だけ、期間集計を表示します。</summary>
    public bool ShowsPayrollPeriodBreakdown => selectedWorkRecordId is null;
    public bool HasPeriodUncalculated => ShowsPayrollPeriodBreakdown && HasUncalculated;
    public IReadOnlyList<CalculationPremiumTotalRowViewModel> PremiumTotals
    {
        get => premiumTotals;
        private set
        {
            if (!SetProperty(ref premiumTotals, value)) return;
            OnPropertyChanged(nameof(HasPremiumTotals));
        }
    }
    public bool HasPremiumTotals => ShowsPayrollPeriodBreakdown && PremiumTotals.Count != 0;
    public IReadOnlyList<CalculationAllowanceRowViewModel> Allowances
    {
        get => allowances;
        private set
        {
            if (!SetProperty(ref allowances, value)) return;
            OnPropertyChanged(nameof(HasAllowances));
        }
    }
    public bool HasAllowances => ShowsPayrollPeriodBreakdown && Allowances.Count != 0;
    public IReadOnlyList<CalculationDayRowViewModel> Days
    {
        get => days;
        private set
        {
            if (!SetProperty(ref days, value)) return;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }
    public bool IsEmpty => Days.Count == 0;
    public AsyncCommand ReloadCommand { get; }

    public void SetPayrollPeriod(PayrollPeriodKey value)
    {
        payrollPeriodKey = value;
        selectedDate = null;
        selectedWorkRecordId = null;
        OnDetailScopeChanged();
    }

    public void SetWorkRecord(DateOnly date, WorkRecordId workRecordId)
    {
        selectedDate = date;
        selectedWorkRecordId = workRecordId;
        payrollPeriodKey = null;
        OnDetailScopeChanged();
    }

    public Task LoadAsync() => RunBusyAsync(LoadCoreAsync);

    private async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        var key = payrollPeriodKey;
        if (key is null && selectedDate is { } date)
            key = (await payrollPeriods.FindPeriodAsync(date, cancellationToken)).Key;
        if (key is null) throw new InvalidOperationException("対象給与期間が指定されていません。");

        var summary = await salaryQuery.GetPayrollPeriodAsync(key.Value, cancellationToken);
        StartDateText = $"給与算定開始日: {formatter.Date(summary.Period.StartDate, false)}";
        EndDateText = $"給与算定終了日: {formatter.Date(summary.Period.EndDate, false)}";
        TotalText = formatter.Money(summary.CalculatedSubtotal);
        BasePayText = formatter.Money(summary.BasePaySubtotal);
        PremiumText = formatter.Money(summary.PremiumSubtotal);
        CountBonusText = formatter.Money(summary.CountBonusSubtotal);
        AllowanceText = formatter.Money(summary.AllowanceSubtotal);
        UncalculatedText = summary.UncalculatedCount == 0
            ? string.Empty
            : $"未計算 {summary.UncalculatedCount}件。下記の勤務記録で不足している設定を確認してください。";
        Allowances = summary.Allowances
            .Select(x => new CalculationAllowanceRowViewModel(x.DisplayName, formatter.Money(x.Amount)))
            .ToArray();
        PremiumTotals = summary.Days
            .SelectMany(day => day.Records)
            .SelectMany(record => record.Calculation.Premiums)
            .GroupBy(premium => new { premium.Rule.Id, premium.Rule.DisplayName })
            .Select(group => new CalculationPremiumTotalRowViewModel(
                group.Key.DisplayName,
                formatter.Money(new YenAmount(group.Sum(x => x.Amount.Value)))))
            .ToArray();

        var visibleDays = selectedDate is { } selected
            ? summary.Days.Where(day => day.Date == selected)
            : summary.Days;
        Days = visibleDays.Select(CreateDay).Where(day => day.Records.Count != 0).ToArray();
    }

    private CalculationDayRowViewModel CreateDay(DailySalaryDto day)
    {
        var visibleRecords = (selectedWorkRecordId is { } selectedId
            ? day.Records.Where(record => record.WorkRecord.Id == selectedId)
            : day.Records).ToArray();
        var uncalculatedCount = ShowsPayrollPeriodBreakdown
            ? day.UncalculatedCount
            : visibleRecords.Count(record => record.Calculation.Status == SalaryCalculationStatus.Uncalculated);
        return new CalculationDayRowViewModel(
            formatter.Date(day.Date),
            formatter.Money(day.BasePaySubtotal),
            formatter.Money(day.PremiumSubtotal),
            formatter.Money(day.CountBonusSubtotal),
            formatter.Money(day.CalculatedSubtotal),
            uncalculatedCount == 0 ? string.Empty : $"未計算 {uncalculatedCount}件",
            visibleRecords.Select(CreateRecord).ToArray(),
            ShowsPayrollPeriodBreakdown);
    }

    private void OnDetailScopeChanged()
    {
        OnPropertyChanged(nameof(ShowsPayrollPeriodBreakdown));
        OnPropertyChanged(nameof(HasPeriodUncalculated));
        OnPropertyChanged(nameof(HasPremiumTotals));
        OnPropertyChanged(nameof(HasAllowances));
    }

    private CalculationWorkRecordRowViewModel CreateRecord(WorkRecordSalaryDto value)
    {
        var record = value.WorkRecord;
        var calculation = value.Calculation;
        var serviceName = value.ServiceDisplayName ?? "サービス設定が見つかりません";
        var title = string.IsNullOrWhiteSpace(value.TimeCategoryDisplayName)
            ? serviceName
            : $"{serviceName} / {value.TimeCategoryDisplayName}";
        var time = record.StartTime is { } start && record.EndTime is { } end
            ? $"{formatter.Time(start)}～{formatter.Time(end)} / {formatter.Duration(record.WorkMinutes)}"
            : formatter.Duration(record.WorkMinutes);
        var rate = calculation.AppliedRate is { } appliedRate
            ? $"{RateTypeText(appliedRate.RateType)} {formatter.Money(appliedRate.Amount)}"
            : "適用単価なし";
        var missing = calculation.MissingRequirements
            .Select(requirement => MissingReason(requirement.Code))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return new CalculationWorkRecordRowViewModel(
            title,
            time,
            rate,
            calculation.BasePay is { } basePay ? formatter.Money(basePay) : "未計算",
            calculation.Total is { } total ? formatter.Money(total) : "未計算",
            formatter.SettingsMonth(value.SettingMonth ?? new YearMonth(record.WorkDate.Year, record.WorkDate.Month)),
            missing.Length == 0 ? string.Empty : string.Join("\n", missing),
            calculation.Premiums.Select(premium => new CalculationPremiumRowViewModel(
                premium.Rule.DisplayName,
                PremiumRuleText(premium.Rule),
                formatter.Duration(premium.ApplicableMinutes),
                formatter.Money(premium.Amount))).ToArray(),
            calculation.CountBonuses.Select(bonus => new CalculationCountBonusRowViewModel(
                bonus.DisplayName,
                formatter.Money(bonus.Amount))).ToArray());
    }

    private string PremiumRuleText(SnapshotPremium rule)
    {
        var calculation = rule.CalculationType switch
        {
            PremiumCalculationType.Percentage => $"{rule.Percentage!.Value.Value / 100m:0.##}%",
            PremiumCalculationType.FixedPerHour => $"1時間あたり {formatter.Money(rule.Amount!.Value)}",
            PremiumCalculationType.FixedPerRecord => $"1件あたり {formatter.Money(rule.Amount!.Value)}",
            _ => "割増",
        };
        var conditions = new List<string>();
        if (rule.StartTime is { } start && rule.EndTime is { } end)
            conditions.Add($"{formatter.Time(start)}～{formatter.Time(end)}");
        if (rule.UsesNationalHolidays) conditions.Add("祝日");
        if (rule.Weekdays.Count != 0)
            conditions.Add(string.Join("・", rule.Weekdays.Order().Select(WeekdayText)));
        if (rule.Dates.Count != 0)
            conditions.Add(string.Join("・", rule.Dates.Order().Select(date => formatter.Date(date, false))));
        return conditions.Count == 0 ? calculation : $"{calculation}（{string.Join("、", conditions)}）";
    }

    private static string RateTypeText(RateType value) => value switch
    {
        RateType.Hourly => "時給",
        RateType.FixedPerRecord => "1件固定",
        _ => "基本単価",
    };

    private static string MissingReason(string code) => code switch
    {
        MissingCalculationRequirementCodes.Service => "サービス設定が不足しています。設定の「サービス・単価」を確認してください。",
        MissingCalculationRequirementCodes.TimeCategory => "時間区分設定が不足しています。設定の「サービス・単価」を確認してください。",
        MissingCalculationRequirementCodes.Rate or "RATE_REQUIRED" => "基本単価が不足しています。設定の「サービス・単価」を確認してください。",
        _ => $"計算設定が不足しています（{code}）。給与設定を確認してください。",
    };

    private static string WeekdayText(DayOfWeek value) => value switch
    {
        DayOfWeek.Sunday => "日曜",
        DayOfWeek.Monday => "月曜",
        DayOfWeek.Tuesday => "火曜",
        DayOfWeek.Wednesday => "水曜",
        DayOfWeek.Thursday => "木曜",
        DayOfWeek.Friday => "金曜",
        DayOfWeek.Saturday => "土曜",
        _ => value.ToString(),
    };
}

public sealed record CalculationPremiumTotalRowViewModel(string DisplayName, string AmountText);
public sealed record CalculationAllowanceRowViewModel(string DisplayName, string AmountText);

public sealed record CalculationDayRowViewModel(
    string DateText,
    string BasePayText,
    string PremiumText,
    string CountBonusText,
    string TotalText,
    string UncalculatedText,
    IReadOnlyList<CalculationWorkRecordRowViewModel> Records,
    bool HasDaySubtotal)
{
    public bool HasUncalculated => !string.IsNullOrWhiteSpace(UncalculatedText);
}

public sealed record CalculationWorkRecordRowViewModel(
    string DisplayName,
    string WorkTimeText,
    string AppliedRateText,
    string BasePayText,
    string TotalText,
    string SettingMonthText,
    string MissingReasonText,
    IReadOnlyList<CalculationPremiumRowViewModel> Premiums,
    IReadOnlyList<CalculationCountBonusRowViewModel> CountBonuses)
{
    public bool HasMissingReason => !string.IsNullOrWhiteSpace(MissingReasonText);
    public bool HasPremiums => Premiums.Count != 0;
    public bool HasCountBonuses => CountBonuses.Count != 0;
}

public sealed record CalculationPremiumRowViewModel(
    string DisplayName,
    string RuleText,
    string ApplicableTimeText,
    string AmountText);

public sealed record CalculationCountBonusRowViewModel(string DisplayName, string AmountText);
