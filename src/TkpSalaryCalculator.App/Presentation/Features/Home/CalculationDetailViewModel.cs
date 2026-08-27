using TkpSalaryCalculator.App.Navigation;
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
    private IReadOnlyList<CalculationDetailRowViewModel> rows = [];

    public CalculationDetailViewModel(
        ISalaryQueryUseCase salaryQuery,
        IPayrollPeriodSettingsUseCase payrollPeriods,
        JapaneseDisplayFormatter formatter,
        IUserErrorPresenter errorPresenter,
        IAppSessionState sessionState) : base(errorPresenter)
    {
        this.salaryQuery = salaryQuery ?? throw new ArgumentNullException(nameof(salaryQuery));
        this.payrollPeriods = payrollPeriods ?? throw new ArgumentNullException(nameof(payrollPeriods));
        this.formatter = formatter ?? throw new ArgumentNullException(nameof(formatter));
        TrackDataChanges(sessionState ?? throw new ArgumentNullException(nameof(sessionState)),
            AppDataChangeKind.WorkRecords | AppDataChangeKind.Settings | AppDataChangeKind.ClosingRules |
            AppDataChangeKind.MonthlyAllowances);
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
    /// <summary>
    /// 日、勤務記録、割増、件数加算を単一の仮想化リストで表示するためのフラットな行です。
    /// </summary>
    public IReadOnlyList<CalculationDetailRowViewModel> Rows
    {
        get => rows;
        private set
        {
            if (!SetProperty(ref rows, value)) return;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }
    public bool IsEmpty => !Rows.Any(row => row is CalculationWorkRecordRowViewModel);
    public AsyncCommand ReloadCommand { get; }

    public void SetPayrollPeriod(PayrollPeriodKey value)
    {
        payrollPeriodKey = value;
        selectedDate = null;
        selectedWorkRecordId = null;
        InvalidateTrackedLoad();
        OnDetailScopeChanged();
    }

    public void SetWorkRecord(DateOnly date, WorkRecordId workRecordId)
    {
        selectedDate = date;
        selectedWorkRecordId = workRecordId;
        payrollPeriodKey = null;
        InvalidateTrackedLoad();
        OnDetailScopeChanged();
    }

    public Task LoadAsync() => LoadTrackedAsync(LoadCoreAsync, force: true);

    public Task LoadIfNeededAsync() => LoadTrackedAsync(LoadCoreAsync, force: false);

    private async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        var scope = new CalculationDetailScope(selectedDate, selectedWorkRecordId);
        var key = payrollPeriodKey;
        if (key is null && scope.SelectedDate is { } date)
            key = (await payrollPeriods.FindPeriodAsync(date, cancellationToken)).Key;
        if (key is null) throw new InvalidOperationException("対象給与期間が指定されていません。");

        var summary = await salaryQuery.GetPayrollPeriodAsync(key.Value, cancellationToken);
        var presentation = await Task.Run(
            () => CreatePresentation(summary, scope, cancellationToken),
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        StartDateText = presentation.StartDateText;
        EndDateText = presentation.EndDateText;
        TotalText = presentation.TotalText;
        BasePayText = presentation.BasePayText;
        PremiumText = presentation.PremiumText;
        CountBonusText = presentation.CountBonusText;
        AllowanceText = presentation.AllowanceText;
        UncalculatedText = presentation.UncalculatedText;
        Allowances = presentation.Allowances;
        PremiumTotals = presentation.PremiumTotals;
        Rows = presentation.Rows;
    }

    private CalculationDetailPresentation CreatePresentation(
        PayrollPeriodSummaryDto summary,
        CalculationDetailScope scope,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var allowances = summary.Allowances
            .Select(x => new CalculationAllowanceRowViewModel(x.DisplayName, formatter.Money(x.Amount)))
            .ToArray();
        var premiumTotals = summary.Days
            .SelectMany(day => day.Records)
            .SelectMany(record => record.Calculation.Premiums)
            .GroupBy(premium => new { premium.Rule.Id, premium.Rule.DisplayName })
            .Select(group => new CalculationPremiumTotalRowViewModel(
                group.Key.DisplayName,
                formatter.Money(new YenAmount(group.Sum(x => x.Amount.Value)))))
            .ToArray();
        cancellationToken.ThrowIfCancellationRequested();

        var uncalculatedText = summary.UncalculatedCount == 0
            ? string.Empty
            : $"未計算 {summary.UncalculatedCount}件。下記の勤務記録で不足している設定を確認してください。";
        return new CalculationDetailPresentation(
            $"給与算定開始日: {formatter.Date(summary.Period.StartDate, false)}",
            $"給与算定終了日: {formatter.Date(summary.Period.EndDate, false)}",
            formatter.Money(summary.CalculatedSubtotal),
            formatter.Money(summary.BasePaySubtotal),
            formatter.Money(summary.PremiumSubtotal),
            formatter.Money(summary.CountBonusSubtotal),
            formatter.Money(summary.AllowanceSubtotal),
            uncalculatedText,
            allowances,
            premiumTotals,
            CreateRows(summary, scope, premiumTotals, allowances, cancellationToken));
    }

    private IReadOnlyList<CalculationDetailRowViewModel> CreateRows(
        PayrollPeriodSummaryDto summary,
        CalculationDetailScope scope,
        IReadOnlyList<CalculationPremiumTotalRowViewModel> premiumTotals,
        IReadOnlyList<CalculationAllowanceRowViewModel> allowances,
        CancellationToken cancellationToken)
    {
        var result = new List<CalculationDetailRowViewModel>();
        if (scope.ShowsPayrollPeriodBreakdown && premiumTotals.Count != 0)
        {
            result.Add(new CalculationSectionHeaderRowViewModel("割増種別ごとの合計"));
            result.AddRange(premiumTotals);
        }

        if (scope.ShowsPayrollPeriodBreakdown && allowances.Count != 0)
        {
            result.Add(new CalculationSectionHeaderRowViewModel("月額手当"));
            result.AddRange(allowances);
        }

        var visibleDays = scope.SelectedDate is { } selected
            ? summary.Days.Where(day => day.Date == selected)
            : summary.Days;
        foreach (var day in visibleDays)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var visibleRecords = (scope.SelectedWorkRecordId is { } selectedId
                ? day.Records.Where(record => record.WorkRecord.Id == selectedId)
                : day.Records).ToArray();
            if (visibleRecords.Length == 0) continue;

            var uncalculatedCount = scope.ShowsPayrollPeriodBreakdown
                ? day.UncalculatedCount
                : visibleRecords.Count(record => record.Calculation.Status == SalaryCalculationStatus.Uncalculated);
            result.Add(new CalculationDayRowViewModel(
                formatter.Date(day.Date),
                formatter.Money(day.BasePaySubtotal),
                formatter.Money(day.PremiumSubtotal),
                formatter.Money(day.CountBonusSubtotal),
                formatter.Money(day.CalculatedSubtotal),
                uncalculatedCount == 0 ? string.Empty : $"未計算 {uncalculatedCount}件",
                scope.ShowsPayrollPeriodBreakdown));

            foreach (var record in visibleRecords)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AddRecordRows(result, record);
            }
        }

        return result;
    }

    private void OnDetailScopeChanged()
    {
        OnPropertyChanged(nameof(ShowsPayrollPeriodBreakdown));
        OnPropertyChanged(nameof(HasPeriodUncalculated));
        OnPropertyChanged(nameof(HasPremiumTotals));
        OnPropertyChanged(nameof(HasAllowances));
    }

    private void AddRecordRows(List<CalculationDetailRowViewModel> rows, WorkRecordSalaryDto value)
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
        rows.Add(new CalculationWorkRecordRowViewModel(
            title,
            time,
            rate,
            calculation.BasePay is { } basePay ? formatter.Money(basePay) : "未計算"));
        rows.AddRange(calculation.Premiums.Select(premium => new CalculationPremiumRowViewModel(
            premium.Rule.DisplayName,
            PremiumRuleText(premium.Rule),
            formatter.Duration(premium.ApplicableMinutes),
            formatter.Money(premium.Amount))));
        rows.AddRange(calculation.CountBonuses.Select(bonus => new CalculationCountBonusRowViewModel(
            bonus.DisplayName,
            formatter.Money(bonus.Amount))));
        rows.Add(new CalculationWorkRecordTotalRowViewModel(
            calculation.Total is { } total ? formatter.Money(total) : "未計算",
            formatter.SettingsMonth(value.SettingMonth ?? new YearMonth(record.WorkDate.Year, record.WorkDate.Month)),
            missing.Length == 0 ? string.Empty : string.Join("\n", missing)));
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

    private readonly record struct CalculationDetailScope(
        DateOnly? SelectedDate,
        WorkRecordId? SelectedWorkRecordId)
    {
        public bool ShowsPayrollPeriodBreakdown => SelectedWorkRecordId is null;
    }

    private sealed record CalculationDetailPresentation(
        string StartDateText,
        string EndDateText,
        string TotalText,
        string BasePayText,
        string PremiumText,
        string CountBonusText,
        string AllowanceText,
        string UncalculatedText,
        IReadOnlyList<CalculationAllowanceRowViewModel> Allowances,
        IReadOnlyList<CalculationPremiumTotalRowViewModel> PremiumTotals,
        IReadOnlyList<CalculationDetailRowViewModel> Rows);
}

public abstract record CalculationDetailRowViewModel;

public sealed record CalculationSectionHeaderRowViewModel(string Title) : CalculationDetailRowViewModel;

public sealed record CalculationPremiumTotalRowViewModel(string DisplayName, string AmountText)
    : CalculationDetailRowViewModel;

public sealed record CalculationAllowanceRowViewModel(string DisplayName, string AmountText)
    : CalculationDetailRowViewModel;

public sealed record CalculationDayRowViewModel(
    string DateText,
    string BasePayText,
    string PremiumText,
    string CountBonusText,
    string TotalText,
    string UncalculatedText,
    bool HasDaySubtotal) : CalculationDetailRowViewModel
{
    public bool HasUncalculated => !string.IsNullOrWhiteSpace(UncalculatedText);
}

public sealed record CalculationWorkRecordRowViewModel(
    string DisplayName,
    string WorkTimeText,
    string AppliedRateText,
    string BasePayText) : CalculationDetailRowViewModel;

public sealed record CalculationWorkRecordTotalRowViewModel(
    string TotalText,
    string SettingMonthText,
    string MissingReasonText) : CalculationDetailRowViewModel
{
    public bool HasMissingReason => !string.IsNullOrWhiteSpace(MissingReasonText);
}

public sealed record CalculationPremiumRowViewModel(
    string DisplayName,
    string RuleText,
    string ApplicableTimeText,
    string AmountText) : CalculationDetailRowViewModel;

public sealed record CalculationCountBonusRowViewModel(string DisplayName, string AmountText)
    : CalculationDetailRowViewModel;
