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
    private readonly JapaneseDisplayFormatter formatter;
    private PayrollPeriodKey? payrollPeriodKey;
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
        JapaneseDisplayFormatter formatter,
        IUserErrorPresenter errorPresenter,
        IAppSessionState sessionState) : base(errorPresenter)
    {
        this.salaryQuery = salaryQuery ?? throw new ArgumentNullException(nameof(salaryQuery));
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
    public bool IsEmpty => !Rows.Any(row => row is CalculationVisitRowViewModel);
    public AsyncCommand ReloadCommand { get; }

    public void SetPayrollPeriod(PayrollPeriodKey value)
    {
        payrollPeriodKey = value;
        selectedWorkRecordId = null;
        InvalidateTrackedLoad();
        OnDetailScopeChanged();
    }

    public void SetWorkRecord(DateOnly _, WorkRecordId workRecordId)
    {
        selectedWorkRecordId = workRecordId;
        payrollPeriodKey = null;
        InvalidateTrackedLoad();
        OnDetailScopeChanged();
    }

    public Task LoadAsync() => LoadTrackedAsync(LoadCoreAsync, force: true);

    public Task LoadIfNeededAsync() => LoadTrackedAsync(LoadCoreAsync, force: false);

    private async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        CalculationDetailPresentation presentation;
        if (selectedWorkRecordId is { } workRecordId)
        {
            var detail = await salaryQuery.GetWorkRecordCalculationAsync(workRecordId, cancellationToken);
            presentation = await Task.Run(
                () => CreateRecordPresentation(detail, cancellationToken),
                cancellationToken);
        }
        else
        {
            if (payrollPeriodKey is not { } key)
                throw new InvalidOperationException("対象給与期間が指定されていません。");
            var summary = await salaryQuery.GetPayrollPeriodAsync(key, cancellationToken);
            presentation = await Task.Run(
                () => CreatePresentation(summary, cancellationToken),
                cancellationToken);
        }
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

    private CalculationDetailPresentation CreateRecordPresentation(
        WorkRecordCalculationDto detail,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var record = detail.Record;
        var calculation = record.Calculation;
        var basePay = calculation.BasePay ?? new YenAmount(0);
        var premium = new YenAmount(calculation.Premiums.Sum(x => x.Amount.Value));
        var countBonus = new YenAmount(calculation.CountBonuses.Sum(x => x.Amount.Value));
        var total = calculation.Total ?? new YenAmount(0);
        var isUncalculated = calculation.Status == SalaryCalculationStatus.Uncalculated;
        var rows = new List<CalculationDetailRowViewModel>
        {
            new CalculationDayRowViewModel(
                formatter.Date(record.WorkRecord.WorkDate),
                formatter.Money(basePay),
                formatter.Money(premium),
                formatter.Money(countBonus),
                formatter.Money(total),
                isUncalculated ? "未計算 1件" : string.Empty,
                false),
        };
        AddRecordRows(rows, record);

        return new CalculationDetailPresentation(
            $"給与算定開始日: {formatter.Date(detail.Period.StartDate, false)}",
            $"給与算定終了日: {formatter.Date(detail.Period.EndDate, false)}",
            formatter.Money(total),
            formatter.Money(basePay),
            formatter.Money(premium),
            formatter.Money(countBonus),
            formatter.Money(new YenAmount(0)),
            isUncalculated ? "未計算 1件。下記の勤務記録で不足している設定を確認してください。" : string.Empty,
            [],
            [],
            rows);
    }

    private CalculationDetailPresentation CreatePresentation(
        PayrollPeriodSummaryDto summary,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var allowances = summary.Allowances
            .Select(x => new CalculationAllowanceRowViewModel(x.DisplayName, formatter.Money(x.Amount)))
            .ToArray();
        var premiumTotals = summary.Days
            .SelectMany(day => day.Records)
            .Where(record => record.Calculation.Status == SalaryCalculationStatus.Calculated)
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
            CreateRows(summary, premiumTotals, allowances, cancellationToken));
    }

    private IReadOnlyList<CalculationDetailRowViewModel> CreateRows(
        PayrollPeriodSummaryDto summary,
        IReadOnlyList<CalculationPremiumTotalRowViewModel> premiumTotals,
        IReadOnlyList<CalculationAllowanceRowViewModel> allowances,
        CancellationToken cancellationToken)
    {
        var result = new List<CalculationDetailRowViewModel>();
        if (premiumTotals.Count != 0)
        {
            result.Add(new CalculationSectionHeaderRowViewModel("割増種別ごとの合計"));
            result.AddRange(premiumTotals);
        }

        if (allowances.Count != 0)
        {
            result.Add(new CalculationSectionHeaderRowViewModel("月額手当"));
            result.AddRange(allowances);
        }

        foreach (var day in summary.Days)
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.Add(new CalculationDayRowViewModel(
                formatter.Date(day.Date),
                formatter.Money(day.BasePaySubtotal),
                formatter.Money(day.PremiumSubtotal),
                formatter.Money(day.CountBonusSubtotal),
                formatter.Money(day.CalculatedSubtotal),
                day.UncalculatedCount == 0 ? string.Empty : $"未計算 {day.UncalculatedCount}件",
                true));

            foreach (var record in day.Records)
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
        var calculationByTaskId = calculation.TaskCalculations.ToDictionary(task => task.WorkTaskId);
        var suppliedTaskDetails = value.Tasks?.ToDictionary(task => task.WorkTask.Id);
        var taskDetails = record.Tasks.OrderBy(task => task.DisplayOrder.Value).Select((task, index) =>
        {
            if (suppliedTaskDetails?.GetValueOrDefault(task.Id) is { } detail) return detail;
            var taskCalculation = calculationByTaskId.GetValueOrDefault(task.Id) ??
                calculation.TaskCalculations.ElementAtOrDefault(index) ??
                throw new InvalidOperationException("訪問のタスク計算内訳が不足しています。");
            return new WorkTaskSalaryDto(
                task,
                taskCalculation,
                index == 0 ? value.ServiceDisplayName : null,
                index == 0 ? value.TimeCategoryDisplayName : null);
        }).ToArray();

        var taskNames = taskDetails.Select(TaskDisplayName).ToArray();
        rows.Add(new CalculationVisitRowViewModel(
            formatter.Date(record.WorkDate),
            string.Join("、", taskNames),
            $"タスク {taskDetails.Length}件",
            calculation.Status == SalaryCalculationStatus.Calculated ? "計算済み" : "未計算"));

        for (var index = 0; index < taskDetails.Length; index++)
        {
            var detail = taskDetails[index];
            var task = detail.WorkTask;
            var taskCalculation = detail.Calculation;
            var time = task.StartTime is { } start && task.EndTime is { } end
                ? $"{formatter.Time(start)}～{formatter.Time(end)} / {formatter.Duration(task.WorkMinutes)}"
                : formatter.Duration(task.WorkMinutes);
            var rate = taskCalculation.AppliedRate is { } appliedRate
                ? $"{RateTypeText(appliedRate.RateType)} {formatter.Money(appliedRate.Amount)}"
                : "適用単価なし";
            var missing = taskCalculation.MissingRequirements
                .Select(requirement => MissingReason(requirement.Code))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            rows.Add(new CalculationWorkRecordRowViewModel(
                $"タスク {index + 1}",
                TaskDisplayName(detail),
                time,
                rate,
                taskCalculation.BasePay is { } basePay ? formatter.Money(basePay) : "未計算",
                taskCalculation.TaskSubtotal is { } subtotal ? formatter.Money(subtotal) : "未計算",
                string.Join("\n", missing)));
            rows.AddRange(taskCalculation.Premiums.Select(premium => new CalculationPremiumRowViewModel(
                premium.Rule.DisplayName,
                PremiumRuleText(premium.Rule),
                formatter.Duration(premium.ApplicableMinutes),
                formatter.Money(premium.Amount))));
        }

        if (calculation.Status == SalaryCalculationStatus.Calculated)
            rows.AddRange(calculation.CountBonuses.Select(bonus => new CalculationCountBonusRowViewModel(
                bonus.DisplayName,
                formatter.Money(bonus.Amount))));
        var visitMissing = calculation.Status == SalaryCalculationStatus.Uncalculated
            ? string.Join("\n", calculation.MissingRequirements
                .Select(requirement => MissingReason(requirement.Code))
                .Distinct(StringComparer.Ordinal)
                .Append("計算できないタスクがあります。訪問の件数加算と訪問合計は表示しません。"))
            : string.Empty;
        rows.Add(new CalculationWorkRecordTotalRowViewModel(
            calculation.Total is { } total ? formatter.Money(total) : "未計算",
            formatter.SettingsMonth(value.SettingMonth ?? new YearMonth(record.WorkDate.Year, record.WorkDate.Month)),
            visitMissing));
    }

    private static string TaskDisplayName(WorkTaskSalaryDto value)
    {
        var serviceName = value.ServiceDisplayName ?? "サービス設定が見つかりません";
        return string.IsNullOrWhiteSpace(value.TimeCategoryDisplayName)
            ? serviceName
            : $"{serviceName} / {value.TimeCategoryDisplayName}";
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

public sealed record CalculationVisitRowViewModel(
    string DateText,
    string TaskSummaryText,
    string TaskCountText,
    string StatusText) : CalculationDetailRowViewModel
{
    public string AccessibilityText => $"訪問、{DateText}、{TaskCountText}、{TaskSummaryText}、{StatusText}";
}

public sealed record CalculationWorkRecordRowViewModel(
    string TaskTitle,
    string DisplayName,
    string WorkTimeText,
    string AppliedRateText,
    string BasePayText,
    string TaskSubtotalText,
    string MissingReasonText) : CalculationDetailRowViewModel
{
    public bool HasMissingReason => !string.IsNullOrWhiteSpace(MissingReasonText);
    public string AccessibilityText => $"{TaskTitle}、{DisplayName}、{WorkTimeText}、タスク小計 {TaskSubtotalText}";
}

public sealed record CalculationWorkRecordTotalRowViewModel(
    string? TotalText,
    string SettingMonthText,
    string MissingReasonText) : CalculationDetailRowViewModel
{
    public bool HasMissingReason => !string.IsNullOrWhiteSpace(MissingReasonText);
    public bool HasTotal => !HasMissingReason && !string.IsNullOrWhiteSpace(TotalText);
}

public sealed record CalculationPremiumRowViewModel(
    string DisplayName,
    string RuleText,
    string ApplicableTimeText,
    string AmountText) : CalculationDetailRowViewModel;

public sealed record CalculationCountBonusRowViewModel(string DisplayName, string AmountText)
    : CalculationDetailRowViewModel;
