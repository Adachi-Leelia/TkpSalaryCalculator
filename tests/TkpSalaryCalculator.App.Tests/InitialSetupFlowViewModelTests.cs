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
    public void UI008_ServiceNameAndMinutesValidationExposeTheirExactFields()
    {
        var snapshot = CreateSnapshot(includeRate: true);
        var step = new ServiceRatesStepViewModel();
        step.Load(snapshot, []);
        var service = step.Services[0];
        service.DisplayName = " ";

        var nameError = Assert.Throws<ApplicationErrorException>(() => step.BuildReplacement(snapshot));
        Assert.Equal(service.DisplayNameFieldId, nameError.Field);

        service.DisplayName = "身体介護";
        service.Rates[0].StandardMinutesText = "0";
        var minutesError = Assert.Throws<ApplicationErrorException>(() => step.BuildReplacement(snapshot));
        Assert.Equal(service.Rates[0].StandardMinutesFieldId, minutesError.Field);
    }

    [Fact]
    public async Task ServiceStep_NewAndUnlinkedRowsKeepStablePresetIdsAcrossRepeatedSaves()
    {
        var step = new ServiceRatesStepViewModel();
        step.Load(CreateSnapshot(includeRate: true), []);
        step.AddServiceCommand.Execute(null);
        var presetUseCase = new PresetStub();

        await step.SavePresetsAsync(presetUseCase, CancellationToken.None);
        var firstIds = presetUseCase.SavedCommands.Select(command => command.Id).ToArray();
        var countAfterFirstSave = presetUseCase.StoredPresetCount;
        await step.SavePresetsAsync(presetUseCase, CancellationToken.None);
        var secondIds = presetUseCase.SavedCommands.Skip(firstIds.Length).Select(command => command.Id).ToArray();

        Assert.All(firstIds, id => Assert.NotNull(id));
        Assert.Equal(firstIds, secondIds);
        Assert.Equal(firstIds.Length, firstIds.Distinct().Count());
        Assert.Equal(countAfterFirstSave, presetUseCase.StoredPresetCount);
    }

    [Fact]
    public void ServiceStep_AddsRateToExistingServiceAndPersistsExplicitOrdering()
    {
        var snapshot = CreateSnapshot(includeRate: true);
        var step = new ServiceRatesStepViewModel();
        step.Load(snapshot, []);
        var service = Assert.Single(step.Services);
        var originalFirst = service.Rates[0];

        service.AddRateCommand.Execute(null);
        Assert.Equal(3, service.Rates.Count);
        service.Rates[0].MoveDownCommand.Execute(null);

        Assert.Same(originalFirst, service.Rates[1]);
        foreach (var rate in service.Rates) rate.AmountText = "1200";
        var replacement = step.BuildReplacement(snapshot);
        Assert.Equal(service.Rates.Select(rate => rate.Id),
            replacement.TimeCategories.OrderBy(category => category.DisplayOrder.Value).Select(category => category.Id));
    }

    [Fact]
    public void ServiceStep_ReordersServiceTypesAndWritesTheirDisplayOrder()
    {
        var snapshot = CreateSnapshot(includeRate: true);
        var step = new ServiceRatesStepViewModel();
        step.Load(snapshot, []);
        step.AddServiceCommand.Execute(null);
        var added = step.Services[1];
        added.Rates[0].AmountText = "900";

        added.MoveUpCommand.Execute(null);
        var replacement = step.BuildReplacement(snapshot);

        Assert.Same(added, step.Services[0]);
        Assert.Equal(added.Id, replacement.Services.Single(service => service.DisplayOrder.Value == 0).Id);
    }

    [Fact]
    public void ServiceStep_BulkDisablesUnusedCandidatesButKeepsACalculableRow()
    {
        var step = new ServiceRatesStepViewModel();
        step.Load(CreateSnapshot(includeRate: false), []);
        var service = Assert.Single(step.Services);
        service.Rates[0].AmountText = "1200";
        service.Rates[1].AmountText = string.Empty;

        step.DisableUnusedCommand.Execute(null);

        Assert.True(service.IsEnabled);
        Assert.True(service.IsExpanded);
        Assert.True(service.Rates[0].IsEnabled);
        Assert.False(service.Rates[1].IsEnabled);
        Assert.NotEmpty(step.BuildReplacement(CreateSnapshot(includeRate: false)).Rates);
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
    public void UI014_NightUsesNormalizedPickerTimesAndAllowsAnOvernightRange()
    {
        var snapshot = CreateSnapshot(includeRate: true);
        var step = new AdditionsStepViewModel();
        step.Load(snapshot);
        step.Night.IsEnabled = true;
        step.Night.ValueText = "300";
        step.Night.StartTime = new TimeSpan(22, 30, 0);
        step.Night.EndTime = new TimeSpan(5, 15, 0);

        var replacement = step.BuildReplacement(snapshot);
        var night = replacement.Premiums.Single(premium => premium.DisplayName == "夜間");

        Assert.Equal(22 * 60 + 30, night.StartTime!.Value.Value);
        Assert.Equal(5 * 60 + 15, night.EndTime!.Value.Value);
    }

    [Fact]
    public void UI008_EqualNightPickerTimesReportTheEndTimeField()
    {
        var step = new AdditionsStepViewModel();
        step.Load(CreateSnapshot(includeRate: true));
        step.Night.IsEnabled = true;
        step.Night.ValueText = "300";
        step.Night.StartTime = new TimeSpan(22, 0, 0);
        step.Night.EndTime = new TimeSpan(22, 0, 0);

        var exception = Assert.Throws<ApplicationErrorException>(() =>
            step.BuildReplacement(CreateSnapshot(includeRate: true)));

        Assert.Equal(SetupFieldIds.NightEndTime, exception.Field);
    }

    [Fact]
    public void UI008_AdditionValuesExposeHolidayAndCountBonusFields()
    {
        var snapshot = CreateSnapshot(includeRate: true);
        var step = new AdditionsStepViewModel();
        step.Load(snapshot);
        step.Holiday.IsEnabled = true;
        step.Holiday.ValueText = "invalid";

        var holidayError = Assert.Throws<ApplicationErrorException>(() => step.BuildReplacement(snapshot));
        Assert.Equal(SetupFieldIds.HolidayValue, holidayError.Field);

        step.Holiday.IsEnabled = false;
        step.CountBonus.IsEnabled = true;
        step.CountBonus.AmountText = "invalid";
        var countError = Assert.Throws<ApplicationErrorException>(() => step.BuildReplacement(snapshot));
        Assert.Equal(SetupFieldIds.CountBonusAmount, countError.Field);
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

    [Fact]
    public async Task UI008_ServiceValidationIdentifiesAndRequestsFocusForFirstInvalidField()
    {
        var fixture = new FlowFixture(new InitialSetupStateDto(
            InitialSetupStatus.InProgress, InitialSetupStepIds.Services, []));
        await fixture.ViewModel.InitializeAsync();
        var firstRate = fixture.ViewModel.Services.Services[0].Rates[0];
        firstRate.AmountText = "invalid";
        string? requestedField = null;
        fixture.ViewModel.ErrorFocusRequested += (_, field) => requestedField = field;

        await fixture.ViewModel.MoveNextAsync();

        Assert.Equal(firstRate.AmountFieldId, fixture.ViewModel.FirstErrorField);
        Assert.Equal(firstRate.AmountFieldId, requestedField);
        Assert.True(firstRate.HasValidationError);
        Assert.Equal(InitialSetupStep.Services, fixture.ViewModel.CurrentStep);
        Assert.Equal("invalid", firstRate.AmountText);
    }

    [Fact]
    public async Task UI008_ClosingDayValidationIdentifiesAndRequestsFocusForPicker()
    {
        var fixture = new FlowFixture(new InitialSetupStateDto(
            InitialSetupStatus.InProgress, InitialSetupStepIds.ClosingDay, []));
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.ClosingDay.SelectedOption = null;
        string? requestedField = null;
        fixture.ViewModel.ErrorFocusRequested += (_, field) => requestedField = field;

        await fixture.ViewModel.MoveNextAsync();

        Assert.Equal(SetupFieldIds.ClosingDay, requestedField);
        Assert.True(fixture.ViewModel.ClosingDay.HasValidationError);
        Assert.Equal(InitialSetupStep.ClosingDay, fixture.ViewModel.CurrentStep);
    }

    [Fact]
    public async Task UI002_ConfirmationIssuesExposeOneStepFixNavigationAndPersistDestination()
    {
        var fixture = new FlowFixture(new InitialSetupStateDto(
            InitialSetupStatus.InProgress,
            InitialSetupStepIds.Confirmation,
            [
                new IssueDto("SETUP_CLOSING_RULE_REQUIRED", null, "締め日を設定してください。"),
                new IssueDto("SETUP_CALCULATION_SETTINGS_REQUIRED", null, "サービスと単価を設定してください。"),
            ]));
        await fixture.ViewModel.InitializeAsync();

        Assert.True(fixture.ViewModel.Confirmation.HasClosingDayIssue);
        Assert.True(fixture.ViewModel.Confirmation.HasServicesIssue);

        await fixture.ViewModel.GoToClosingDayAsync();
        Assert.Equal(InitialSetupStep.ClosingDay, fixture.ViewModel.CurrentStep);
        Assert.Equal(InitialSetupStepIds.ClosingDay, fixture.InitialSetup.SavedSteps[^1]);

        await fixture.ViewModel.GoToServicesAsync();
        Assert.Equal(InitialSetupStep.Services, fixture.ViewModel.CurrentStep);
        Assert.Equal(InitialSetupStepIds.Services, fixture.InitialSetup.SavedSteps[^1]);
    }

    [Fact]
    public async Task UI002_ReturningToConfirmationReloadsIssuesAndCompletionState()
    {
        var fixture = new FlowFixture(new InitialSetupStateDto(
            InitialSetupStatus.InProgress,
            InitialSetupStepIds.Confirmation,
            [new IssueDto("SETUP_CALCULATION_SETTINGS_REQUIRED", null, "サービスと単価を設定してください。")]));
        await fixture.ViewModel.InitializeAsync();
        await fixture.ViewModel.GoToServicesAsync();
        fixture.InitialSetup.SetState(new InitialSetupStateDto(
            InitialSetupStatus.InProgress, InitialSetupStepIds.Services, []));
        var callsBeforeCorrection = fixture.InitialSetup.GetStateCalls;

        await fixture.ViewModel.MoveNextAsync();
        await fixture.ViewModel.MoveNextAsync();

        Assert.Equal(InitialSetupStep.Confirmation, fixture.ViewModel.CurrentStep);
        Assert.True(fixture.InitialSetup.GetStateCalls > callsBeforeCorrection);
        Assert.True(fixture.ViewModel.CanComplete);
        Assert.False(fixture.ViewModel.Confirmation.HasServicesIssue);
    }

    [Fact]
    public async Task ViewModelUpdatesAfterRealAsyncSuspensionArePostedToTheCapturedUiContext()
    {
        var fixture = new FlowFixture(new InitialSetupStateDto(
            InitialSetupStatus.InProgress, InitialSetupStepIds.Services, []), completeAsynchronously: true);
        var context = new TrackingSynchronizationContext();
        var notificationsOnContext = true;
        fixture.ViewModel.Services.Services.CollectionChanged += (_, _) =>
            notificationsOnContext &= ReferenceEquals(SynchronizationContext.Current, context);

        await Task.Run(async () =>
        {
            SynchronizationContext.SetSynchronizationContext(context);
            try
            {
                await fixture.ViewModel.InitializeAsync();
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(null);
            }
        });

        Assert.True(context.PostCount > 0);
        Assert.True(notificationsOnContext);
        Assert.NotEmpty(fixture.ViewModel.Services.Services);
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
        public FlowFixture(InitialSetupStateDto state, bool completeAsynchronously = false)
        {
            InitialSetup = new InitialSetupStub(state, completeAsynchronously);
            MonthSettings = new MonthSettingsStub(CreateSnapshot(includeRate: true));
            PayrollPeriods = new PayrollPeriodStub();
            Navigator = new NavigatorStub();
            Session = new AppSessionState(new DateOnly(2026, 8, 21)) { InitialSetupState = state };
            Presets = new PresetStub();
            ViewModel = new InitialSetupFlowViewModel(
                InitialSetup,
                MonthSettings,
                Presets,
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
        public PresetStub Presets { get; }
        public NavigatorStub Navigator { get; }
        public AppSessionState Session { get; }
    }

    private sealed class InitialSetupStub(InitialSetupStateDto state, bool completeAsynchronously = false) : IInitialSetupUseCase
    {
        private InitialSetupStateDto state = state;

        public List<string> SavedSteps { get; } = [];
        public int CompleteCalls { get; private set; }
        public int GetStateCalls { get; private set; }

        public async Task<InitialSetupStateDto> GetStateAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GetStateCalls++;
            if (completeAsynchronously) await Task.Delay(10, cancellationToken).ConfigureAwait(false);
            return state;
        }

        public void SetState(InitialSetupStateDto value) => state = value;

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
        private readonly Dictionary<ServicePresetId, ServicePresetDto> storedPresets = [];

        public List<SaveServicePresetCommand> SavedCommands { get; } = [];

        public int StoredPresetCount => storedPresets.Count;

        public Task<IReadOnlyList<ServicePresetDto>> GetAllAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<ServicePresetDto>>([]);
        }

        public Task<ServicePresetDto> SaveAsync(SaveServicePresetCommand command, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SavedCommands.Add(command);
            var value = new ServicePresetDto(command.Id ?? new ServicePresetId(Guid.NewGuid()),
                command.DisplayName, command.ServiceId, command.TimeCategoryId, command.DefaultWorkMinutes,
                command.DisplayOrder, command.IsEnabled);
            storedPresets[value.Id] = value;
            return Task.FromResult(value);
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

    private sealed class TrackingSynchronizationContext : SynchronizationContext
    {
        private int postCount;

        public int PostCount => Volatile.Read(ref postCount);

        public override void Post(SendOrPostCallback callback, object? state)
        {
            Interlocked.Increment(ref postCount);
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var previous = Current;
                SetSynchronizationContext(this);
                try
                {
                    callback(state);
                }
                finally
                {
                    SetSynchronizationContext(previous);
                }
            });
        }
    }
}
