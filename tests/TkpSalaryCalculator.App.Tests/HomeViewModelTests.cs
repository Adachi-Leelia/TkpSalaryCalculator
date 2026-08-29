namespace TkpSalaryCalculator.App.Tests;

public sealed class HomeViewModelTests
{
    [Fact]
    public async Task PERF05_ReappearingWithoutDependentChangeSkipsLoadAndManualReloadAlwaysRuns()
    {
        var fixture = new HomeFixture();
        fixture.Salary.Add(Summary(2026, 8, 10_000, 10_000, 0, 0, 0, 0));

        await fixture.ViewModel.LoadIfNeededAsync();
        await fixture.ViewModel.LoadIfNeededAsync();
        fixture.Session.NotifyDataChanged(AppDataChangeKind.BasicShifts);
        await fixture.ViewModel.LoadIfNeededAsync();

        Assert.Single(fixture.Salary.RequestedKeys);

        fixture.Session.NotifyDataChanged(AppDataChangeKind.WorkRecords);
        await fixture.ViewModel.LoadIfNeededAsync();
        await fixture.ViewModel.LoadAsync();

        Assert.Equal(3, fixture.Salary.RequestedKeys.Count);
    }

    [Fact]
    public async Task BackupReminder_IsReevaluatedAfterLocalDateChangesWithoutReloadingSummary()
    {
        var fixture = new HomeFixture();
        fixture.Salary.Add(Summary(2026, 8, 10_000, 10_000, 0, 0, 0, 0));

        await fixture.ViewModel.LoadIfNeededAsync();
        await fixture.ViewModel.LoadIfNeededAsync();
        fixture.LocalDates.Today = fixture.LocalDates.Today.AddDays(1);
        await fixture.ViewModel.LoadIfNeededAsync();

        Assert.Single(fixture.Salary.RequestedKeys);
        Assert.Equal(2, fixture.Backup.GetStateCalls);
        Assert.Equal(fixture.LocalDates.Today, fixture.Backup.LastRequestedDate);
    }

    [Fact]
    public async Task PERF05_FailedConditionalLoadIsRetriedOnNextAppearance()
    {
        var fixture = new HomeFixture();
        fixture.Salary.Add(Summary(2026, 8, 10_000, 10_000, 0, 0, 0, 0));
        fixture.Salary.FailingKey = Key(2026, 8);

        await fixture.ViewModel.LoadIfNeededAsync();
        fixture.Salary.FailingKey = null;
        await fixture.ViewModel.LoadIfNeededAsync();

        Assert.Equal(2, fixture.Salary.RequestedKeys.Count);
        Assert.Equal("10,000円", fixture.ViewModel.TotalText);
    }

    [Fact]
    public async Task PERF05_CancelledConditionalLoadIsRetriedOnNextAppearance()
    {
        var fixture = new HomeFixture();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = 0;
        fixture.Salary.GetPayrollPeriodAsyncOverride = async (_, cancellationToken) =>
        {
            if (Interlocked.Increment(ref requests) == 1)
            {
                started.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return Summary(2026, 8, 10_000, 10_000, 0, 0, 0, 0);
        };

        var cancelledLoad = fixture.ViewModel.LoadIfNeededAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        fixture.ViewModel.CancelPendingOperations();
        await cancelledLoad;
        await fixture.ViewModel.LoadIfNeededAsync();

        Assert.Equal(2, requests);
        Assert.Equal("10,000円", fixture.ViewModel.TotalText);
    }

    [Fact]
    public async Task SCRHOME01_LoadsSelectedPeriodSummaryAndBackupState()
    {
        var fixture = new HomeFixture();
        fixture.Backup.State = BackupState(shouldShow: true);
        fixture.Salary.Add(Summary(2026, 8, 10_000, 2_000, 300, 400, 500, 2));

        await fixture.ViewModel.LoadAsync();

        Assert.Equal([Key(2026, 8)], fixture.Salary.RequestedKeys);
        Assert.Equal("給与算定開始日: 2026年7月21日", fixture.ViewModel.PeriodHeader.StartDateText);
        Assert.Equal("給与算定終了日: 2026年8月20日", fixture.ViewModel.PeriodHeader.EndDateText);
        Assert.Equal("10,000円", fixture.ViewModel.TotalText);
        Assert.Equal("給与見込み合計: 10,000円", fixture.ViewModel.TotalAccessibilityText);
        Assert.Equal("2,000円", fixture.ViewModel.BasePayText);
        Assert.Equal("300円", fixture.ViewModel.PremiumText);
        Assert.Equal("400円", fixture.ViewModel.CountBonusText);
        Assert.Equal("500円", fixture.ViewModel.AllowanceText);
        Assert.Equal("2件", fixture.ViewModel.UncalculatedCountText);
        Assert.True(fixture.ViewModel.HasUncalculatedRecords);
        Assert.Equal("2026年1月分～8月分", fixture.ViewModel.AnnualRangeText);
        Assert.Equal("10,000円", fixture.ViewModel.AnnualTotalText);
        Assert.Equal("未計算2件を除く", fixture.ViewModel.AnnualUncalculatedText);
        Assert.True(fixture.ViewModel.HasAnnualUncalculatedRecords);
        Assert.Equal(
            "年間給与見込み累計。集計範囲: 2026年1月分～8月分。金額: 10,000円。未計算2件を除く。",
            fixture.ViewModel.AnnualAccessibilityText);
        Assert.True(fixture.ViewModel.BackupReminder.ShouldShow);
        Assert.Equal(Key(2026, 8), fixture.Session.PayrollPeriod);
    }

    [Fact]
    public async Task UI004_PeriodMovesRefreshDatesTotalsBreakdownAndUncalculatedCountTogether()
    {
        var fixture = new HomeFixture();
        fixture.Salary.Add(Summary(2026, 8, 1_000, 700, 100, 100, 100, 0));
        fixture.Salary.Add(Summary(2026, 9, 9_000, 5_000, 1_000, 2_000, 1_000, 4));
        await fixture.ViewModel.LoadAsync();
        Assert.Equal("0件", fixture.ViewModel.UncalculatedCountText);
        Assert.False(fixture.ViewModel.HasUncalculatedRecords);

        await fixture.ViewModel.MoveByAsync(1);

        Assert.Equal([Key(2026, 8), Key(2026, 9)], fixture.Salary.RequestedKeys);
        Assert.Equal("給与算定開始日: 2026年8月21日", fixture.ViewModel.PeriodHeader.StartDateText);
        Assert.Equal("給与算定終了日: 2026年9月20日", fixture.ViewModel.PeriodHeader.EndDateText);
        Assert.Equal("9,000円", fixture.ViewModel.TotalText);
        Assert.Equal("5,000円", fixture.ViewModel.BasePayText);
        Assert.Equal("1,000円", fixture.ViewModel.PremiumText);
        Assert.Equal("2,000円", fixture.ViewModel.CountBonusText);
        Assert.Equal("1,000円", fixture.ViewModel.AllowanceText);
        Assert.Equal("4件", fixture.ViewModel.UncalculatedCountText);
        Assert.Equal("2026年1月分～9月分", fixture.ViewModel.AnnualRangeText);
        Assert.Equal("9,000円", fixture.ViewModel.AnnualTotalText);
        Assert.Equal("未計算4件を除く", fixture.ViewModel.AnnualUncalculatedText);
        Assert.Equal(Key(2026, 9), fixture.Session.PayrollPeriod);
    }

    [Fact]
    public async Task UI004_PeriodMoveUpdatesUncalculatedStateFromRecordsToZero()
    {
        var fixture = new HomeFixture();
        fixture.Salary.Add(Summary(2026, 8, 1_000, 1_000, 0, 0, 0, 3));
        fixture.Salary.Add(Summary(2026, 9, 1_000, 1_000, 0, 0, 0, 0));
        await fixture.ViewModel.LoadAsync();
        Assert.Equal("3件", fixture.ViewModel.UncalculatedCountText);
        Assert.True(fixture.ViewModel.HasUncalculatedRecords);

        await fixture.ViewModel.MoveByAsync(1);

        Assert.Equal("0件", fixture.ViewModel.UncalculatedCountText);
        Assert.False(fixture.ViewModel.HasUncalculatedRecords);
        Assert.Equal(string.Empty, fixture.ViewModel.AnnualUncalculatedText);
        Assert.False(fixture.ViewModel.HasAnnualUncalculatedRecords);
        Assert.False(fixture.ViewModel.UncalculatedDaysCommand.CanExecute(null));
    }

    [Fact]
    public async Task UI017_AnnualSettingGenerationReloadsHomeWithTheNewAnnualPeriod()
    {
        var fixture = new HomeFixture();
        var summary = Summary(2026, 8, 10_000, 10_000, 0, 0, 0, 0);
        fixture.Salary.Add(summary, Annual(2026, 1, 2026, 12, 2026, 8, 10_000, 0));
        await fixture.ViewModel.LoadIfNeededAsync();
        Assert.Equal("2026年1月分～8月分", fixture.ViewModel.AnnualRangeText);

        fixture.Salary.SetAnnual(summary.Period.Key, Annual(2026, 4, 2027, 3, 2026, 8, 5_000, 0));
        fixture.Session.NotifyDataChanged(AppDataChangeKind.AnnualSummarySettings);
        await fixture.ViewModel.LoadIfNeededAsync();

        Assert.Equal(2, fixture.Salary.RequestedKeys.Count);
        Assert.Equal("2026年4月分～8月分", fixture.ViewModel.AnnualRangeText);
        Assert.Equal("5,000円", fixture.ViewModel.AnnualTotalText);
    }

    [Fact]
    public async Task LoadAsync_AfterCancellationQueuesTheLatestRequestAndIgnoresTheOldResult()
    {
        var fixture = new HomeFixture();
        var firstRequestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstRequest = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var obsoleteSummary = Summary(2026, 8, 1_000, 1_000, 0, 0, 0, 1);
        var latestSummary = Summary(2026, 8, 2_000, 2_000, 0, 0, 0, 0);
        var requests = 0;
        fixture.Salary.GetPayrollPeriodAsyncOverride = async (_, _) =>
        {
            if (Interlocked.Increment(ref requests) == 1)
            {
                firstRequestStarted.SetResult();
                await releaseFirstRequest.Task;
                return obsoleteSummary;
            }

            return latestSummary;
        };

        var initialLoad = fixture.ViewModel.LoadAsync();
        await firstRequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        fixture.ViewModel.CancelPendingOperations();
        var reappearingLoad = fixture.ViewModel.LoadAsync();
        releaseFirstRequest.SetResult();

        await Task.WhenAll(initialLoad, reappearingLoad);

        Assert.Equal(2, requests);
        Assert.Equal("2,000円", fixture.ViewModel.TotalText);
        Assert.Equal("0件", fixture.ViewModel.UncalculatedCountText);
        Assert.False(fixture.ViewModel.HasUncalculatedRecords);
    }

    [Fact]
    public async Task UI004_PeriodMoveDuringLoadQueuesTheSelectedPeriod()
    {
        var fixture = new HomeFixture();
        var firstRequestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstRequest = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Salary.GetPayrollPeriodAsyncOverride = async (key, _) =>
        {
            if (key == Key(2026, 8))
            {
                firstRequestStarted.TrySetResult();
                await releaseFirstRequest.Task;
                return Summary(2026, 8, 1_000, 1_000, 0, 0, 0, 0);
            }

            return Summary(2026, 9, 9_000, 5_000, 1_000, 2_000, 1_000, 4);
        };

        var initialLoad = fixture.ViewModel.LoadAsync();
        await firstRequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var periodMove = fixture.ViewModel.MoveByAsync(1);
        releaseFirstRequest.SetResult();

        await Task.WhenAll(initialLoad, periodMove);

        Assert.Equal([Key(2026, 8), Key(2026, 9)], fixture.Salary.RequestedKeys);
        Assert.Equal(Key(2026, 9), fixture.Session.PayrollPeriod);
        Assert.Equal("給与算定終了日: 2026年9月20日", fixture.ViewModel.PeriodHeader.EndDateText);
        Assert.Equal("9,000円", fixture.ViewModel.TotalText);
        Assert.Equal("4件", fixture.ViewModel.UncalculatedCountText);
    }

    [Fact]
    public async Task CurrentPeriod_UsesLocalTodayAndReloadsItsSummary()
    {
        var fixture = new HomeFixture(currentYear: 2026, currentMonth: 9);
        fixture.Session.PayrollPeriod = Key(2026, 6);
        fixture.Salary.Add(Summary(2026, 6, 600, 600, 0, 0, 0, 0));
        fixture.Salary.Add(Summary(2026, 9, 900, 900, 0, 0, 0, 0));
        await fixture.ViewModel.LoadAsync();

        await fixture.ViewModel.MoveToCurrentAsync();

        Assert.Equal(new DateOnly(2026, 8, 21), fixture.PayrollPeriods.RequestedDate);
        Assert.Equal(Key(2026, 9), fixture.Session.PayrollPeriod);
        Assert.Equal("900円", fixture.ViewModel.TotalText);
    }

    [Fact]
    public async Task FailedPeriodMove_PreservesPreviouslyDisplayedPeriodAndSummary()
    {
        var fixture = new HomeFixture();
        fixture.Salary.Add(Summary(2026, 8, 1_000, 1_000, 0, 0, 0, 0));
        fixture.Salary.FailingKey = Key(2026, 9);
        await fixture.ViewModel.LoadAsync();

        await fixture.ViewModel.MoveByAsync(1);

        Assert.Equal(Key(2026, 8), fixture.Session.PayrollPeriod);
        Assert.Equal("給与算定終了日: 2026年8月20日", fixture.ViewModel.PeriodHeader.EndDateText);
        Assert.Equal("1,000円", fixture.ViewModel.TotalText);
        Assert.True(fixture.ViewModel.HasError);
    }

    [Fact]
    public async Task UX007_DeferBackupReminderUsesLocalDateAndHidesItForSevenDays()
    {
        var fixture = new HomeFixture();
        fixture.Backup.State = BackupState(shouldShow: true);
        fixture.Salary.Add(Summary(2026, 8, 0, 0, 0, 0, 0, 0));
        await fixture.ViewModel.LoadAsync();

        await fixture.ViewModel.BackupReminder.DeferAsync();

        Assert.Equal(new DateOnly(2026, 8, 21), fixture.Backup.DeferredFromDate);
        Assert.Equal(new DateOnly(2026, 8, 28), fixture.Backup.State.DeferredUntilDate);
        Assert.False(fixture.ViewModel.BackupReminder.ShouldShow);
    }

    [Fact]
    public async Task HomeActionsPassCurrentDateOrSelectedPayrollPeriodToNavigator()
    {
        var fixture = new HomeFixture();
        fixture.Salary.Add(Summary(2026, 8, 1_000, 1_000, 0, 0, 0, 1));
        await fixture.ViewModel.LoadAsync();

        await fixture.ViewModel.OpenCalendarAsync();
        await fixture.ViewModel.OpenCalculationDetailsAsync();
        await fixture.ViewModel.OpenMonthlyAllowancesAsync();
        await fixture.ViewModel.OpenUncalculatedDaysAsync();

        Assert.Equal(new DateOnly(2026, 8, 21), fixture.Navigator.CalendarDate);
        Assert.Equal(new DateOnly(2026, 8, 21), fixture.Session.SelectedCalendarDate);
        Assert.Equal(new YearMonth(2026, 8), fixture.Session.CalendarMonth);
        Assert.Equal(Key(2026, 8), fixture.Navigator.CalculationDetailsKey);
        Assert.Equal(Key(2026, 8), fixture.Navigator.MonthlyAllowancesKey);
        Assert.Equal(Key(2026, 8), fixture.Navigator.UncalculatedDaysKey);
    }

    [Fact]
    public async Task UncalculatedNavigationIsDisabledAndIgnoredWhenCountIsZero()
    {
        var fixture = new HomeFixture();
        fixture.Salary.Add(Summary(2026, 8, 1_000, 1_000, 0, 0, 0, 0));
        await fixture.ViewModel.LoadAsync();

        Assert.False(fixture.ViewModel.UncalculatedDaysCommand.CanExecute(null));
        await fixture.ViewModel.OpenUncalculatedDaysAsync();

        Assert.Null(fixture.Navigator.UncalculatedDaysKey);
    }

    private static PayrollPeriodKey Key(int year, int month) => new(new YearMonth(year, month));

    private static AnnualSalarySummaryDto Annual(
        int startYear,
        int startMonth,
        int endYear,
        int endMonth,
        int accumulationEndYear,
        int accumulationEndMonth,
        long total,
        int uncalculated) => new(
            Key(startYear, startMonth),
            Key(endYear, endMonth),
            Key(accumulationEndYear, accumulationEndMonth),
            new YenAmount(total),
            uncalculated);

    private static PayrollPeriodSummaryDto Summary(
        int year,
        int month,
        long total,
        long basePay,
        long premium,
        long countBonus,
        long allowance,
        int uncalculated)
    {
        var end = new DateOnly(year, month, 20);
        var start = end.AddMonths(-1).AddDays(1);
        return new PayrollPeriodSummaryDto(
            new PayrollPeriod(Key(year, month), start, end),
            [],
            [],
            new YenAmount(basePay),
            new YenAmount(premium),
            new YenAmount(countBonus),
            new YenAmount(allowance),
            new YenAmount(total),
            uncalculated);
    }

    private static BackupReminderStateDto BackupState(bool shouldShow) => new(
        new DateOnly(2026, 8, 21),
        shouldShow,
        true,
        null,
        new DateTimeOffset(2026, 8, 21, 0, 0, 0, TimeSpan.Zero),
        null);

    private sealed class HomeFixture
    {
        public HomeFixture(int currentYear = 2026, int currentMonth = 8)
        {
            var currentKey = Key(currentYear, currentMonth);
            Session = new AppSessionState(new DateOnly(2026, 8, 21)) { PayrollPeriod = currentKey };
            var currentEnd = new DateOnly(currentYear, currentMonth, 20);
            PayrollPeriods = new PayrollPeriodStub(new PayrollPeriod(
                currentKey,
                currentEnd.AddMonths(-1).AddDays(1),
                currentEnd));
            ViewModel = new HomeViewModel(
                Salary,
                PayrollPeriods,
                Backup,
                Navigator,
                Session,
                new ClockStub(),
                LocalDates,
                new JapaneseDisplayFormatter(),
                new UserErrorPresenter());
        }

        public SalaryStub Salary { get; } = new();
        public PayrollPeriodStub PayrollPeriods { get; }
        public BackupStub Backup { get; } = new();
        public LocalDateConverterStub LocalDates { get; } = new();
        public HomeNavigatorStub Navigator { get; } = new();
        public AppSessionState Session { get; }
        public HomeViewModel ViewModel { get; }
    }

    private sealed class SalaryStub : ISalaryQueryUseCase
    {
        private readonly Dictionary<PayrollPeriodKey, PayrollPeriodSummaryDto> summaries = [];
        private readonly Dictionary<PayrollPeriodKey, AnnualSalarySummaryDto> annualSummaries = [];
        public List<PayrollPeriodKey> RequestedKeys { get; } = [];
        public PayrollPeriodKey? FailingKey { get; set; }
        public Func<PayrollPeriodKey, CancellationToken, Task<PayrollPeriodSummaryDto>>? GetPayrollPeriodAsyncOverride { get; set; }

        public Task<CalendarMonthScreenDto> GetCalendarMonthScreenAsync(
            YearMonth yearMonth, DateOnly selectedDate, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<DayScreenDto> GetDayScreenAsync(DateOnly workDate, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public void Add(PayrollPeriodSummaryDto summary, AnnualSalarySummaryDto? annual = null)
        {
            summaries.Add(summary.Period.Key, summary);
            annualSummaries.Add(summary.Period.Key, annual ?? Annual(
                summary.Period.Key.Value.Year,
                1,
                summary.Period.Key.Value.Year,
                12,
                summary.Period.Key.Value.Year,
                summary.Period.Key.Value.Month,
                summary.CalculatedSubtotal.Value,
                summary.UncalculatedCount));
        }

        public void SetAnnual(PayrollPeriodKey key, AnnualSalarySummaryDto annual) =>
            annualSummaries[key] = annual;

        public Task<PayrollPeriodSummaryDto> GetPayrollPeriodAsync(
            PayrollPeriodKey payrollPeriodKey,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestedKeys.Add(payrollPeriodKey);
            if (GetPayrollPeriodAsyncOverride is not null)
            {
                return GetPayrollPeriodAsyncOverride(payrollPeriodKey, cancellationToken);
            }
            if (FailingKey == payrollPeriodKey) throw new InvalidOperationException("test failure");
            return Task.FromResult(summaries[payrollPeriodKey]);
        }

        public async Task<HomeSalarySummaryDto> GetHomeSalarySummaryAsync(
            PayrollPeriodKey payrollPeriodKey,
            CancellationToken cancellationToken)
        {
            var monthly = await GetPayrollPeriodAsync(payrollPeriodKey, cancellationToken);
            var annual = annualSummaries.TryGetValue(payrollPeriodKey, out var configured)
                ? configured
                : Annual(
                    monthly.Period.Key.Value.Year,
                    1,
                    monthly.Period.Key.Value.Year,
                    12,
                    monthly.Period.Key.Value.Year,
                    monthly.Period.Key.Value.Month,
                    monthly.CalculatedSubtotal.Value,
                    monthly.UncalculatedCount);
            return new HomeSalarySummaryDto(monthly, annual);
        }

        public Task<IReadOnlyList<CalendarDayDto>> GetCalendarMonthAsync(
            YearMonth yearMonth,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<DailySalaryDto> GetDayAsync(DateOnly workDate, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<WorkRecordCalculationDto> GetWorkRecordCalculationAsync(
            WorkRecordId workRecordId,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class PayrollPeriodStub(PayrollPeriod current) : IPayrollPeriodSettingsUseCase
    {
        public DateOnly? RequestedDate { get; private set; }

        public Task<PayrollPeriod> FindPeriodAsync(DateOnly localDate, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestedDate = localDate;
            return Task.FromResult(current);
        }

        public Task<MonthlyAllowancePeriodDto> GetMonthlyAllowancePeriodAsync(PayrollPeriodKey payrollPeriodKey, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ClosingRuleReplacementPreviewDto> PreviewClosingRuleReplacementAsync(
            ReplaceClosingRuleCommand command,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<EffectiveClosingRuleDto?> GetClosingRuleAsync(
            PayrollPeriodKey payrollPeriodKey,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task ReplaceClosingRuleAsync(
            ReplaceClosingRuleCommand command,
            ClosingRuleReplacementConfirmationToken confirmationToken,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<MonthlyAllowanceDto>> GetAllowancesAsync(
            PayrollPeriodKey payrollPeriodKey,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<MonthlyAllowanceDto> SaveAllowanceAsync(
            SaveMonthlyAllowanceCommand command,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task DeleteAllowanceAsync(MonthlyAllowanceId id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class BackupStub : IBackupReminderUseCase
    {
        public BackupReminderStateDto State { get; set; } = BackupState(shouldShow: false);
        public DateOnly? DeferredFromDate { get; private set; }
        public DateOnly? LastRequestedDate { get; private set; }
        public int GetStateCalls { get; private set; }

        public Task<BackupReminderStateDto> GetStateAsync(DateOnly localToday, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GetStateCalls++;
            LastRequestedDate = localToday;
            return Task.FromResult(State);
        }

        public Task<BackupReminderStateDto> DeferForSevenDaysAsync(
            DateOnly localToday,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DeferredFromDate = localToday;
            State = State with { ShouldShow = false, DeferredUntilDate = localToday.AddDays(7) };
            return Task.FromResult(State);
        }
    }

    private sealed class HomeNavigatorStub : IHomeNavigator
    {
        public DateOnly? CalendarDate { get; private set; }
        public PayrollPeriodKey? CalculationDetailsKey { get; private set; }
        public PayrollPeriodKey? MonthlyAllowancesKey { get; private set; }
        public PayrollPeriodKey? UncalculatedDaysKey { get; private set; }

        public Task OpenCalendarAsync(DateOnly selectedDate, CancellationToken cancellationToken)
        {
            CalendarDate = selectedDate;
            return Task.CompletedTask;
        }

        public Task OpenCalculationDetailsAsync(PayrollPeriodKey payrollPeriodKey, CancellationToken cancellationToken)
        {
            CalculationDetailsKey = payrollPeriodKey;
            return Task.CompletedTask;
        }

        public Task OpenMonthlyAllowancesAsync(PayrollPeriodKey payrollPeriodKey, CancellationToken cancellationToken)
        {
            MonthlyAllowancesKey = payrollPeriodKey;
            return Task.CompletedTask;
        }

        public Task OpenUncalculatedDaysAsync(PayrollPeriodKey payrollPeriodKey, CancellationToken cancellationToken)
        {
            UncalculatedDaysKey = payrollPeriodKey;
            return Task.CompletedTask;
        }
    }

    private sealed class ClockStub : IUtcClock
    {
        public DateTimeOffset UtcNow => new(2026, 8, 20, 15, 0, 0, TimeSpan.Zero);
    }

    private sealed class LocalDateConverterStub : ILocalDateConverter
    {
        public DateOnly Today { get; set; } = new(2026, 8, 21);
        public DateOnly ToLocalDate(DateTimeOffset utcDateTime) => Today;
    }
}
