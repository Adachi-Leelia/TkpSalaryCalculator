using TkpSalaryCalculator.App.Presentation.Features.Setup;

namespace TkpSalaryCalculator.App.Tests;

public sealed class InitialSetupFlowViewModelTests
{
    [Theory]
    [InlineData(null, InitialSetupStep.Welcome)]
    [InlineData(InitialSetupStepIds.ClosingDay, InitialSetupStep.ClosingDay)]
    [InlineData(InitialSetupStepIds.Services, InitialSetupStep.Services)]
    [InlineData(InitialSetupStepIds.Additions, InitialSetupStep.Additions)]
    [InlineData(InitialSetupStepIds.Confirmation, InitialSetupStep.Confirmation)]
    [InlineData("future-step", InitialSetupStep.Welcome)]
    public void UI003_SavedStepUsesStableIdsAndUnknownValuesFailSafeToWelcome(
        string? savedStep,
        InitialSetupStep expected)
    {
        Assert.Equal(expected, InitialSetupStepIds.ToStep(savedStep));
        Assert.Equal(expected == InitialSetupStep.Welcome ? InitialSetupStepIds.Welcome : savedStep,
            InitialSetupStepIds.FromStep(expected));
    }

    [Theory]
    [InlineData(2026, 8, 20, 20, 2026, 8)]
    [InlineData(2026, 8, 21, 20, 2026, 9)]
    [InlineData(2026, 8, 31, null, 2026, 8)]
    public void ClosingDay_DefaultEffectivePeriodContainsTheCurrentDate(
        int year,
        int month,
        int day,
        int? closingDay,
        int expectedYear,
        int expectedMonth)
    {
        var result = InitialSetupFlowViewModel.GetCurrentPayrollPeriodKey(
            new DateOnly(year, month, day), closingDay);

        Assert.Equal(new PayrollPeriodKey(new YearMonth(expectedYear, expectedMonth)), result);
    }

    [Fact]
    public async Task UI003_InitializesAtSavedStepAndEveryMovePersistsDestination()
    {
        var fixture = new FlowFixture(new InitialSetupStateDto(
            InitialSetupStatus.InProgress,
            InitialSetupStepIds.Services,
            [new IssueDto("SETUP_CLOSING_RULE_REQUIRED", null, "給与の締め日を設定してください。")]));

        await fixture.ViewModel.InitializeAsync();

        Assert.Equal(InitialSetupStep.Services, fixture.ViewModel.CurrentStep);
        Assert.Contains("サービスと単価", fixture.ViewModel.ResumeMessage);
        Assert.Contains("締め日", fixture.ViewModel.MissingRequirements);

        await fixture.ViewModel.MoveBackAsync();

        Assert.Equal(InitialSetupStep.ClosingDay, fixture.ViewModel.CurrentStep);
        Assert.Equal([InitialSetupStepIds.ClosingDay], fixture.InitialSetup.SavedSteps);
        Assert.Equal(InitialSetupStepIds.ClosingDay, fixture.Session.InitialSetupState!.Step);
    }

    [Fact]
    public async Task ClosingDay_PreviewShowsFullPeriodWithoutSavingTheRule()
    {
        var fixture = new FlowFixture(new InitialSetupStateDto(
            InitialSetupStatus.InProgress,
            InitialSetupStepIds.ClosingDay,
            []));
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.ClosingDay.Select(20);

        await fixture.ViewModel.PreviewClosingDayAsync();

        Assert.Contains("給与算定開始日: 2026年8月21日", fixture.ViewModel.ClosingDay.PeriodPreview);
        Assert.Contains("給与算定終了日: 2026年9月20日", fixture.ViewModel.ClosingDay.PeriodPreview);
        Assert.Equal(1, fixture.PayrollPeriods.PreviewCalls);
        Assert.Equal(0, fixture.PayrollPeriods.ReplaceCalls);
    }

    [Fact]
    public void ServiceStep_RequiresRateOnlyForEnabledRowsAndBuildsCalculableReplacement()
    {
        var snapshot = CreateSnapshot(includeRate: false);
        var step = new ServiceRatesStepViewModel();
        step.Load(snapshot, []);
        var service = Assert.Single(step.Services);
        var enabled = service.Rates[0];
        var disabled = service.Rates[1];
        enabled.AmountText = "1250";
        disabled.IsEnabled = false;
        disabled.AmountText = string.Empty;

        var replacement = step.BuildReplacement(snapshot);

        Assert.True(Assert.Single(replacement.Services).IsEnabled);
        Assert.True(replacement.TimeCategories[0].IsEnabled);
        Assert.False(replacement.TimeCategories[1].IsEnabled);
        var rate = Assert.Single(replacement.Rates);
        Assert.Equal(1250, rate.Amount.Value);
        Assert.Equal(RateType.Hourly, rate.RateType);
    }

    [Fact]
    public void ServiceStep_LeavesInputIntactAndReportsMissingEnabledRate()
    {
        var snapshot = CreateSnapshot(includeRate: false);
        var step = new ServiceRatesStepViewModel();
        step.Load(snapshot, []);

        var exception = Assert.Throws<ApplicationErrorException>(() => step.BuildReplacement(snapshot));

        Assert.Equal("SETUP_RATE_REQUIRED", exception.Code);
        Assert.Equal("身体0", step.Services[0].Rates[0].DisplayName);
    }

    [Fact]
    public void UX004_SkipAdditionsNeedsNoAmountOrTimeAndDisablesEveryOptionalRule()
    {
        var snapshot = CreateSnapshot(includeRate: true);
        var step = new AdditionsStepViewModel();
        step.Load(snapshot);

        step.DisableAll();
        var replacement = step.BuildReplacement(snapshot);

        Assert.All(replacement.Premiums, value => Assert.False(value.IsEnabled));
        Assert.All(replacement.CountBonuses, value => Assert.False(value.IsEnabled));
        Assert.Contains(replacement.Premiums, value => value.DisplayName == "休日");
        Assert.Contains(replacement.Premiums, value => value.DisplayName == "夜間");
        Assert.Contains(replacement.CountBonuses, value => value.DisplayName == "件数加算");
    }

    [Fact]
    public async Task UI002_ConfirmationEnablesCompletionFromDtoAndNavigatesHomeOnlyAfterComplete()
    {
        var fixture = new FlowFixture(new InitialSetupStateDto(
            InitialSetupStatus.InProgress,
            InitialSetupStepIds.Confirmation,
            []));

        await fixture.ViewModel.InitializeAsync();

        Assert.True(fixture.ViewModel.CanComplete);
        Assert.True(fixture.ViewModel.NextCommand.CanExecute(null));
        Assert.Contains("使用可能なサービス設定: 2件", fixture.ViewModel.ServiceSummary);
        Assert.Null(fixture.Navigator.Request);

        await fixture.ViewModel.MoveNextAsync();

        Assert.Equal(1, fixture.InitialSetup.CompleteCalls);
        Assert.Equal(AppRootKind.Main, fixture.Navigator.Request!.RootKind);
        Assert.Equal(NavigationRoutes.Home, fixture.Session.SelectedRootRoute);
        Assert.Equal(new PayrollPeriodKey(new YearMonth(2026, 9)), fixture.Session.PayrollPeriod);
    }

    [Fact]
    public async Task UI002_ConfirmationDisablesCompletionWhenDtoContainsIssues()
    {
        var fixture = new FlowFixture(new InitialSetupStateDto(
            InitialSetupStatus.InProgress,
            InitialSetupStepIds.Confirmation,
            [new IssueDto("SETUP_CALCULATION_SETTINGS_REQUIRED", null, "サービスと単価を設定してください。")]));

        await fixture.ViewModel.InitializeAsync();

        Assert.False(fixture.ViewModel.CanComplete);
        Assert.False(fixture.ViewModel.NextCommand.CanExecute(null));
        Assert.Contains("サービスと単価", fixture.ViewModel.Confirmation.MissingRequirements);
        Assert.Null(fixture.Navigator.Request);
    }

    private static SettingSnapshot CreateSnapshot(bool includeRate)
    {
        var serviceId = new ServiceId(Guid.Parse("10000000-0000-4000-8000-000000000010"));
        var firstCategory = new TimeCategoryId(Guid.Parse("10000000-0000-4000-8000-000000000020"));
        var secondCategory = new TimeCategoryId(Guid.Parse("10000000-0000-4000-8000-000000000021"));
        var rates = includeRate
            ? new[]
            {
                new SnapshotRate(serviceId, firstCategory, RateType.Hourly, new YenAmount(1200)),
                new SnapshotRate(serviceId, secondCategory, RateType.FixedPerRecord, new YenAmount(900)),
            }
            : [];
        return new SettingSnapshot(
            new SettingSnapshotId(Guid.Parse("10000000-0000-4000-8000-000000000002")),
            null,
            new HolidayCalendarVersionId(Guid.Parse("10000000-0000-4000-8000-000000000001")),
            new SchemaVersion(1),
            DateTimeOffset.UnixEpoch,
            [new SnapshotService(serviceId, "身体介護", new DisplayOrder(0), true)],
            [
                new SnapshotTimeCategory(firstCategory, serviceId, "身体0", new WorkMinutes(20), new DisplayOrder(0), true),
                new SnapshotTimeCategory(secondCategory, serviceId, "身体1", new WorkMinutes(30), new DisplayOrder(1), true),
            ],
            rates,
            [new SnapshotPremium(new PremiumId(Guid.Parse("10000000-0000-4000-8000-000000000030")),
                "休日", PremiumCalculationType.FixedPerHour, null, new YenAmount(0), null, null, true,
                new HashSet<DayOfWeek> { DayOfWeek.Saturday, DayOfWeek.Sunday }, new HashSet<DateOnly>(),
                new HashSet<ServiceId>(), false)],
            []);
    }

    private sealed class FlowFixture
    {
        public FlowFixture(InitialSetupStateDto state)
        {
            InitialSetup = new InitialSetupStub(state);
            MonthSettings = new MonthSettingsStub(CreateSnapshot(includeRate: true));
            PayrollPeriods = new PayrollPeriodStub();
            Navigator = new NavigatorStub();
            Session = new AppSessionState(new DateOnly(2026, 8, 21)) { InitialSetupState = state };
            ViewModel = new InitialSetupFlowViewModel(
                InitialSetup,
                MonthSettings,
                new PresetStub(),
                PayrollPeriods,
                Navigator,
                Session,
                new ClockStub(new DateTimeOffset(2026, 8, 20, 15, 0, 0, TimeSpan.Zero)),
                new LocalDateStub(new DateOnly(2026, 8, 21)),
                new JapaneseDisplayFormatter(),
                new UserErrorPresenter());
        }

        public InitialSetupFlowViewModel ViewModel { get; }
        public InitialSetupStub InitialSetup { get; }
        public MonthSettingsStub MonthSettings { get; }
        public PayrollPeriodStub PayrollPeriods { get; }
        public NavigatorStub Navigator { get; }
        public AppSessionState Session { get; }
    }

    private sealed class InitialSetupStub(InitialSetupStateDto state) : IInitialSetupUseCase
    {
        private InitialSetupStateDto state = state;

        public List<string> SavedSteps { get; } = [];
        public int CompleteCalls { get; private set; }

        public Task<InitialSetupStateDto> GetStateAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(state);
        }

        public Task SaveProgressAsync(string step, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SavedSteps.Add(step);
            state = state with { Status = InitialSetupStatus.InProgress, Step = step };
            return Task.CompletedTask;
        }

        public Task<InitialSetupStateDto> CompleteAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CompleteCalls++;
            if (state.Issues.Count == 0)
                state = new InitialSetupStateDto(InitialSetupStatus.Completed, null, []);
            return Task.FromResult(state);
        }
    }

    private sealed class MonthSettingsStub(SettingSnapshot snapshot) : IMonthSettingsUseCase
    {
        private SettingSnapshot snapshot = snapshot;

        public Task<MonthSettingsDto> GetAsync(YearMonth yearMonth, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new MonthSettingsDto(yearMonth, snapshot));
        }

        public Task<SettingReplacementPreviewDto> PreviewReplacementAsync(
            YearMonth yearMonth,
            SettingSnapshotReplacementDto replacement,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new SettingReplacementPreviewDto(yearMonth,
                new SettingReplacementConfirmationToken(yearMonth, snapshot.Id, null, "work", "settings",
                    snapshot.HolidayCalendarVersionId), 0, new YenAmount(0), new YenAmount(0), 0, []));
        }

        public Task<MonthSettingsDto> CloneAndReplaceAsync(
            YearMonth yearMonth,
            SettingSnapshotReplacementDto replacement,
            SettingReplacementConfirmationToken confirmationToken,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            snapshot = new SettingSnapshot(new SettingSnapshotId(Guid.NewGuid()), snapshot.Id,
                snapshot.HolidayCalendarVersionId, snapshot.SchemaVersion, DateTimeOffset.UnixEpoch,
                replacement.Services, replacement.TimeCategories, replacement.Rates,
                replacement.Premiums, replacement.CountBonuses);
            return Task.FromResult(new MonthSettingsDto(yearMonth, snapshot));
        }

        public Task<SettingReplacementPreviewDto> PreviewCopyPreviousMonthAsync(
            YearMonth yearMonth,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<MonthSettingsDto> CopyPreviousMonthAsync(
            YearMonth yearMonth,
            SettingReplacementConfirmationToken confirmationToken,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class PresetStub : IServicePresetUseCase
    {
        public Task<IReadOnlyList<ServicePresetDto>> GetAllAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<ServicePresetDto>>([]);
        }

        public Task<ServicePresetDto> SaveAsync(SaveServicePresetCommand command, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new ServicePresetDto(command.Id ?? new ServicePresetId(Guid.NewGuid()),
                command.DisplayName, command.ServiceId, command.TimeCategoryId, command.DefaultWorkMinutes,
                command.DisplayOrder, command.IsEnabled));
        }

        public Task DeleteAsync(ServicePresetId id, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class PayrollPeriodStub : IPayrollPeriodSettingsUseCase
    {
        private readonly PayrollPeriod period = new(new PayrollPeriodKey(new YearMonth(2026, 9)),
            new DateOnly(2026, 8, 21), new DateOnly(2026, 9, 20));

        public int PreviewCalls { get; private set; }
        public int ReplaceCalls { get; private set; }

        public Task<PayrollPeriod> FindPeriodAsync(DateOnly localDate, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(period);
        }

        public Task<ClosingRuleReplacementPreviewDto> PreviewClosingRuleReplacementAsync(
            ReplaceClosingRuleCommand command,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PreviewCalls++;
            return Task.FromResult(new ClosingRuleReplacementPreviewDto(command.EffectiveFrom, null, period,
                new ClosingRuleReplacementConfirmationToken(command.EffectiveFrom, command.ClosingDay,
                    new ClosingRuleHistoryVersion("v1"))));
        }

        public Task<EffectiveClosingRuleDto?> GetClosingRuleAsync(
            PayrollPeriodKey payrollPeriodKey,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<EffectiveClosingRuleDto?>(new EffectiveClosingRuleDto(payrollPeriodKey,
                new ClosingRuleId(Guid.Parse("20000000-0000-4000-8000-000000000001")),
                new PayrollPeriodKey(new YearMonth(1, 1)), 20));
        }

        public Task ReplaceClosingRuleAsync(
            ReplaceClosingRuleCommand command,
            ClosingRuleReplacementConfirmationToken confirmationToken,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReplaceCalls++;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<MonthlyAllowanceDto>> GetAllowancesAsync(
            PayrollPeriodKey payrollPeriodKey,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<MonthlyAllowanceDto> SaveAllowanceAsync(
            SaveMonthlyAllowanceCommand command,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task DeleteAllowanceAsync(MonthlyAllowanceId id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class NavigatorStub : IAppRootNavigator
    {
        public AppRootNavigationRequest? Request { get; private set; }

        public Task SetRootAsync(AppRootNavigationRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Request = request;
            return Task.CompletedTask;
        }
    }

    private sealed class ClockStub(DateTimeOffset utcNow) : IUtcClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class LocalDateStub(DateOnly localDate) : ILocalDateConverter
    {
        public DateOnly ToLocalDate(DateTimeOffset utcDateTime) => localDate;
    }
}
