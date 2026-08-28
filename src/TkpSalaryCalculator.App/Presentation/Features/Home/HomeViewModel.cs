using System.Windows.Input;
using TkpSalaryCalculator.App.Navigation;
using TkpSalaryCalculator.App.Presentation.Common;
using TkpSalaryCalculator.Application.Contracts;
using TkpSalaryCalculator.Application.Ports;
using TkpSalaryCalculator.Application.UseCases;
using TkpSalaryCalculator.Domain.Models;
using TkpSalaryCalculator.Domain.ValueObjects;

namespace TkpSalaryCalculator.App.Presentation.Features.Home;

/// <summary>給与算定期間の完全表示と期間移動を担当します。</summary>
public sealed class PayrollPeriodHeaderViewModel : ObservableObject
{
    private readonly Func<int, Task> moveBy;
    private readonly Func<Task> moveToCurrent;
    private readonly JapaneseDisplayFormatter formatter;
    private PayrollPeriodKey? key;
    private string startDateText = "給与算定開始日: 読み込み中";
    private string endDateText = "給与算定終了日: 読み込み中";

    public PayrollPeriodHeaderViewModel(
        Func<int, Task> moveBy,
        Func<Task> moveToCurrent,
        JapaneseDisplayFormatter formatter,
        Action<Exception> onException)
    {
        this.moveBy = moveBy ?? throw new ArgumentNullException(nameof(moveBy));
        this.moveToCurrent = moveToCurrent ?? throw new ArgumentNullException(nameof(moveToCurrent));
        this.formatter = formatter ?? throw new ArgumentNullException(nameof(formatter));
        ArgumentNullException.ThrowIfNull(onException);

        PreviousCommand = new AsyncCommand(() => this.moveBy(-1), onException, () => CanMovePrevious);
        NextCommand = new AsyncCommand(() => this.moveBy(1), onException, () => CanMoveNext);
        CurrentCommand = new AsyncCommand(this.moveToCurrent, onException);
    }

    public string StartDateText
    {
        get => startDateText;
        private set => SetProperty(ref startDateText, value);
    }

    public string EndDateText
    {
        get => endDateText;
        private set => SetProperty(ref endDateText, value);
    }

    public ICommand PreviousCommand { get; }

    public ICommand NextCommand { get; }

    public ICommand CurrentCommand { get; }

    public bool CanMovePrevious => key is not null &&
        (key.Value.Value.Year != 1 || key.Value.Value.Month != 1);

    public bool CanMoveNext => key is not null &&
        (key.Value.Value.Year != 9999 || key.Value.Value.Month != 12);

    internal void SetPeriod(PayrollPeriod period)
    {
        ArgumentNullException.ThrowIfNull(period);
        key = period.Key;
        StartDateText = $"給与算定開始日: {formatter.Date(period.StartDate, false)}";
        EndDateText = $"給与算定終了日: {formatter.Date(period.EndDate, false)}";
        OnPropertyChanged(nameof(CanMovePrevious));
        OnPropertyChanged(nameof(CanMoveNext));
        ((AsyncCommand)PreviousCommand).NotifyCanExecuteChanged();
        ((AsyncCommand)NextCommand).NotifyCanExecuteChanged();
    }
}

/// <summary>ホームに表示するバックアップ案内と 7 日延期を担当します。</summary>
public sealed class BackupReminderViewModel : ObservableObject
{
    private readonly IBackupReminderUseCase backupReminder;
    private readonly Func<DateOnly> getLocalToday;
    private bool shouldShow;

    public BackupReminderViewModel(
        IBackupReminderUseCase backupReminder,
        Func<DateOnly> getLocalToday,
        Action<Exception> onException)
    {
        this.backupReminder = backupReminder ?? throw new ArgumentNullException(nameof(backupReminder));
        this.getLocalToday = getLocalToday ?? throw new ArgumentNullException(nameof(getLocalToday));
        ArgumentNullException.ThrowIfNull(onException);
        DeferCommand = new AsyncCommand(DeferAsync, onException, () => ShouldShow);
    }

    public bool ShouldShow
    {
        get => shouldShow;
        private set
        {
            if (!SetProperty(ref shouldShow, value)) return;
            ((AsyncCommand)DeferCommand).NotifyCanExecuteChanged();
        }
    }

    public string Message => "勤務データを端末外にも保存しておくと、端末変更や故障に備えられます。設定のデータ管理からエクスポートできます。";

    public ICommand DeferCommand { get; }

    internal async Task LoadAsync(CancellationToken cancellationToken)
    {
        var state = await backupReminder.GetStateAsync(getLocalToday(), cancellationToken);
        ShouldShow = state.ShouldShow;
    }

    public async Task DeferAsync()
    {
        var state = await backupReminder.DeferForSevenDaysAsync(getLocalToday(), CancellationToken.None);
        ShouldShow = state.ShouldShow;
    }
}

/// <summary>SCR-HOME-01 の給与期間サマリーと画面遷移を統括します。</summary>
public sealed class HomeViewModel : ViewModelBase
{
    private readonly ISalaryQueryUseCase salaryQuery;
    private readonly IPayrollPeriodSettingsUseCase payrollPeriods;
    private readonly IHomeNavigator navigator;
    private readonly IAppSessionState sessionState;
    private readonly IUtcClock clock;
    private readonly ILocalDateConverter localDates;
    private readonly JapaneseDisplayFormatter formatter;
    private readonly AsyncCommand calculationDetailsCommand;
    private readonly AsyncCommand monthlyAllowancesCommand;
    private readonly AsyncCommand uncalculatedDaysCommand;
    private readonly AsyncCommand reloadCommand;
    private readonly object summaryRequestLock = new();
    private PayrollPeriodSummaryDto? summary;
    private Task queuedSummaryRequest = Task.CompletedTask;
    private int latestSummaryRequestVersion;
    private bool reloadBackupAfterLatestSummaryRequest;
    private DateOnly? lastBackupEvaluationDate;
    private string totalText = "0円";
    private string basePayText = "0円";
    private string premiumText = "0円";
    private string countBonusText = "0円";
    private string allowanceText = "0円";
    private string uncalculatedCountText = "0件";
    private string annualRangeText = string.Empty;
    private string annualTotalText = "0円";
    private string annualUncalculatedText = string.Empty;
    private string annualAccessibilityText = string.Empty;
    private bool hasWorkRecords;
    private bool hasUncalculatedRecords;
    private bool hasAnnualUncalculatedRecords;

    public HomeViewModel(
        ISalaryQueryUseCase salaryQuery,
        IPayrollPeriodSettingsUseCase payrollPeriods,
        IBackupReminderUseCase backupReminder,
        IHomeNavigator navigator,
        IAppSessionState sessionState,
        IUtcClock clock,
        ILocalDateConverter localDates,
        JapaneseDisplayFormatter formatter,
        IUserErrorPresenter errorPresenter) : base(errorPresenter)
    {
        this.salaryQuery = salaryQuery ?? throw new ArgumentNullException(nameof(salaryQuery));
        this.payrollPeriods = payrollPeriods ?? throw new ArgumentNullException(nameof(payrollPeriods));
        this.navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
        this.sessionState = sessionState ?? throw new ArgumentNullException(nameof(sessionState));
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        this.localDates = localDates ?? throw new ArgumentNullException(nameof(localDates));
        this.formatter = formatter ?? throw new ArgumentNullException(nameof(formatter));
        TrackDataChanges(sessionState,
            AppDataChangeKind.WorkRecords | AppDataChangeKind.Settings | AppDataChangeKind.ClosingRules |
            AppDataChangeKind.MonthlyAllowances | AppDataChangeKind.BackupStatus);

        PeriodHeader = new PayrollPeriodHeaderViewModel(MoveByAsync, MoveToCurrentAsync, formatter, PresentError);
        BackupReminder = new BackupReminderViewModel(backupReminder, GetLocalToday, PresentError);
        CalendarCommand = new AsyncCommand(OpenCalendarAsync, PresentError);
        reloadCommand = new AsyncCommand(LoadAsync, PresentError);
        calculationDetailsCommand = new AsyncCommand(OpenCalculationDetailsAsync, PresentError, () => Summary is not null);
        monthlyAllowancesCommand = new AsyncCommand(OpenMonthlyAllowancesAsync, PresentError, () => Summary is not null);
        uncalculatedDaysCommand = new AsyncCommand(OpenUncalculatedDaysAsync, PresentError, () => HasUncalculatedRecords);
    }

    public PayrollPeriodHeaderViewModel PeriodHeader { get; }

    public BackupReminderViewModel BackupReminder { get; }

    public PayrollPeriodSummaryDto? Summary
    {
        get => summary;
        private set
        {
            if (!SetProperty(ref summary, value)) return;
            OnPropertyChanged(nameof(HasSummary));
            OnPropertyChanged(nameof(HasNoWorkRecords));
            calculationDetailsCommand.NotifyCanExecuteChanged();
            monthlyAllowancesCommand.NotifyCanExecuteChanged();
        }
    }

    public bool HasSummary => Summary is not null;

    public string TotalText
    {
        get => totalText;
        private set
        {
            if (!SetProperty(ref totalText, value)) return;
            OnPropertyChanged(nameof(TotalAccessibilityText));
        }
    }

    public string TotalAccessibilityText => $"給与見込み合計: {TotalText}";

    public string BasePayText
    {
        get => basePayText;
        private set => SetProperty(ref basePayText, value);
    }

    public string PremiumText
    {
        get => premiumText;
        private set => SetProperty(ref premiumText, value);
    }

    public string CountBonusText
    {
        get => countBonusText;
        private set => SetProperty(ref countBonusText, value);
    }

    public string AllowanceText
    {
        get => allowanceText;
        private set => SetProperty(ref allowanceText, value);
    }

    public string UncalculatedCountText
    {
        get => uncalculatedCountText;
        private set => SetProperty(ref uncalculatedCountText, value);
    }

    public string AnnualRangeText
    {
        get => annualRangeText;
        private set => SetProperty(ref annualRangeText, value);
    }

    public string AnnualTotalText
    {
        get => annualTotalText;
        private set => SetProperty(ref annualTotalText, value);
    }

    public string AnnualUncalculatedText
    {
        get => annualUncalculatedText;
        private set => SetProperty(ref annualUncalculatedText, value);
    }

    public string AnnualAccessibilityText
    {
        get => annualAccessibilityText;
        private set => SetProperty(ref annualAccessibilityText, value);
    }

    public bool HasAnnualUncalculatedRecords
    {
        get => hasAnnualUncalculatedRecords;
        private set => SetProperty(ref hasAnnualUncalculatedRecords, value);
    }

    public bool HasWorkRecords
    {
        get => hasWorkRecords;
        private set
        {
            if (!SetProperty(ref hasWorkRecords, value)) return;
            OnPropertyChanged(nameof(HasNoWorkRecords));
        }
    }

    public bool HasNoWorkRecords => HasSummary && !HasWorkRecords;

    public bool HasUncalculatedRecords
    {
        get => hasUncalculatedRecords;
        private set
        {
            if (!SetProperty(ref hasUncalculatedRecords, value)) return;
            uncalculatedDaysCommand.NotifyCanExecuteChanged();
        }
    }

    public ICommand CalendarCommand { get; }

    public ICommand ReloadCommand => reloadCommand;

    public ICommand CalculationDetailsCommand => calculationDetailsCommand;

    public ICommand MonthlyAllowancesCommand => monthlyAllowancesCommand;

    public ICommand UncalculatedDaysCommand => uncalculatedDaysCommand;

    public Task LoadAsync() => QueueSummaryRequestAsync(async cancellationToken =>
        sessionState.PayrollPeriod ??
        (await payrollPeriods.FindPeriodAsync(GetLocalToday(), cancellationToken)).Key,
        reloadBackup: true);

    /// <summary>初回表示または依存データの変更後だけ最新状態を読み直します。</summary>
    public Task LoadIfNeededAsync()
    {
        if (!IsTrackedDataCurrent()) return LoadAsync();
        return lastBackupEvaluationDate == GetLocalToday()
            ? Task.CompletedTask
            : ReloadBackupAsync();
    }

    public Task MoveByAsync(int monthDelta)
    {
        if (monthDelta is not (-1 or 1)) throw new ArgumentOutOfRangeException(nameof(monthDelta));
        var current = Summary?.Period.Key ?? sessionState.PayrollPeriod;
        return current is null
            ? Task.CompletedTask
            : QueueSummaryRequestAsync(_ => Task.FromResult(
                new PayrollPeriodKey(current.Value.Value.AddMonths(monthDelta))));
    }

    public Task MoveToCurrentAsync() => QueueSummaryRequestAsync(async cancellationToken =>
        (await payrollPeriods.FindPeriodAsync(GetLocalToday(), cancellationToken)).Key);

    private void ApplySummary(PayrollPeriodSummaryDto value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Summary = value;
        sessionState.PayrollPeriod = value.Period.Key;
        PeriodHeader.SetPeriod(value.Period);
        TotalText = formatter.Money(value.CalculatedSubtotal);
        BasePayText = formatter.Money(value.BasePaySubtotal);
        PremiumText = formatter.Money(value.PremiumSubtotal);
        CountBonusText = formatter.Money(value.CountBonusSubtotal);
        AllowanceText = formatter.Money(value.AllowanceSubtotal);
        UncalculatedCountText = $"{value.UncalculatedCount}件";
        HasWorkRecords = value.Days.Any(day => day.Records.Count > 0);
        HasUncalculatedRecords = value.UncalculatedCount > 0;
    }

    private void ApplyAnnualSummary(AnnualSalarySummaryDto value)
    {
        ArgumentNullException.ThrowIfNull(value);
        AnnualRangeText = FormatAnnualRange(value.PeriodStart.Value, value.AccumulationEnd.Value);
        AnnualTotalText = formatter.Money(value.CalculatedSubtotal);
        HasAnnualUncalculatedRecords = value.UncalculatedCount > 0;
        AnnualUncalculatedText = HasAnnualUncalculatedRecords
            ? $"未計算{value.UncalculatedCount}件を除く"
            : string.Empty;
        AnnualAccessibilityText = $"年間給与見込み累計。集計範囲: {AnnualRangeText}。金額: {AnnualTotalText}。" +
            (HasAnnualUncalculatedRecords ? $"{AnnualUncalculatedText}。" : string.Empty);
    }

    private static string FormatAnnualRange(YearMonth start, YearMonth end) => start.Year == end.Year
        ? $"{start.Year}年{start.Month}月分～{end.Month}月分"
        : $"{start.Year}年{start.Month}月分～{end.Year}年{end.Month}月分";

    private Task QueueSummaryRequestAsync(
        Func<CancellationToken, Task<PayrollPeriodKey>> getKey,
        bool reloadBackup = false)
    {
        ArgumentNullException.ThrowIfNull(getKey);
        var requestVersion = Interlocked.Increment(ref latestSummaryRequestVersion);
        var dataGeneration = CaptureTrackedDataGeneration();
        lock (summaryRequestLock)
        {
            reloadBackupAfterLatestSummaryRequest |= reloadBackup;
            var precedingRequest = queuedSummaryRequest;
            queuedSummaryRequest = LoadAfterPrecedingRequestAsync(precedingRequest, requestVersion, dataGeneration, getKey);
            return queuedSummaryRequest;
        }
    }

    private async Task LoadAfterPrecedingRequestAsync(
        Task precedingRequest,
        int requestVersion,
        long dataGeneration,
        Func<CancellationToken, Task<PayrollPeriodKey>> getKey)
    {
        await precedingRequest;
        if (!IsLatestSummaryRequest(requestVersion)) return;

        if (await TryRunBusyAsync(async cancellationToken =>
        {
            var key = await getKey(cancellationToken);
            var value = await salaryQuery.GetHomeSalarySummaryAsync(key, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsLatestSummaryRequest(requestVersion)) return;

            ApplySummary(value.MonthlySummary);
            ApplyAnnualSummary(value.AnnualSummary);
            if (ConsumeBackupReloadRequest(requestVersion))
            {
                await LoadBackupAsync(cancellationToken);
            }
        }))
            AcceptDataGeneration(dataGeneration);
    }

    private bool IsLatestSummaryRequest(int requestVersion) =>
        requestVersion == Volatile.Read(ref latestSummaryRequestVersion);

    private async Task ReloadBackupAsync()
    {
        await TryRunBusyAsync(LoadBackupAsync);
    }

    private async Task LoadBackupAsync(CancellationToken cancellationToken)
    {
        var localToday = GetLocalToday();
        await BackupReminder.LoadAsync(cancellationToken);
        lastBackupEvaluationDate = localToday;
    }

    private bool ConsumeBackupReloadRequest(int requestVersion)
    {
        lock (summaryRequestLock)
        {
            if (!IsLatestSummaryRequest(requestVersion) || !reloadBackupAfterLatestSummaryRequest) return false;
            reloadBackupAfterLatestSummaryRequest = false;
            return true;
        }
    }

    public Task OpenCalendarAsync()
    {
        var localToday = GetLocalToday();
        sessionState.SelectedCalendarDate = localToday;
        sessionState.CalendarMonth = new YearMonth(localToday.Year, localToday.Month);
        return navigator.OpenCalendarAsync(localToday, CancellationToken.None);
    }

    private DateOnly GetLocalToday() => localDates.ToLocalDate(clock.UtcNow);

    public Task OpenCalculationDetailsAsync() => Summary is null
        ? Task.CompletedTask
        : navigator.OpenCalculationDetailsAsync(Summary.Period.Key, CancellationToken.None);

    public Task OpenMonthlyAllowancesAsync() => Summary is null
        ? Task.CompletedTask
        : navigator.OpenMonthlyAllowancesAsync(Summary.Period.Key, CancellationToken.None);

    public Task OpenUncalculatedDaysAsync() => !HasUncalculatedRecords || Summary is null
        ? Task.CompletedTask
        : navigator.OpenUncalculatedDaysAsync(Summary.Period.Key, CancellationToken.None);
}
