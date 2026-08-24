namespace TkpSalaryCalculator.App.Tests;

public sealed class NavigationAndStartupTests
{
    [Theory]
    [InlineData("//setup-root/initial-setup/initial-setup-content")]
    [InlineData("/initial-setup/initial-setup-content?step=closing-day")]
    [InlineData("initial-setup-content#form")]
    public void InitialSetupRoute_AllowsOnlySetupHierarchy(string location)
    {
        Assert.True(NavigationRoutes.IsInitialSetupLocation(location));
    }

    [Theory]
    [InlineData("//setup-root/home")]
    [InlineData("//setup-root/future-normal-screen")]
    [InlineData("//main/settings")]
    [InlineData("")]
    public void InitialSetupRoute_RejectsAnythingOutsideSetupHierarchy(string location)
    {
        Assert.False(NavigationRoutes.IsInitialSetupLocation(location));
    }

    [Theory]
    [InlineData("//main/home/home-content", NavigationRoutes.Home)]
    [InlineData("//main/calendar/calendar-content?date=2026-08-21", NavigationRoutes.Calendar)]
    [InlineData("//main/settings/settings-content", NavigationRoutes.Settings)]
    public void MainTab_IsReadFromExactRouteSegment(string location, string expected)
    {
        Assert.Equal(expected, NavigationRoutes.GetMainTab(location));
    }

    [Theory]
    [InlineData("//main/home-detail")]
    [InlineData("//main/recalendar/calendar-content")]
    [InlineData("//main/app-settings")]
    [InlineData("//main/future-tab")]
    public void MainTab_RejectsSimilarOrUnknownRouteSegments(string location)
    {
        Assert.Null(NavigationRoutes.GetMainTab(location));
    }

    [Fact]
    public void SessionState_PreservesSelectionsAndRejectsUnknownRootRoute()
    {
        var state = new AppSessionState(new DateOnly(2026, 8, 21));
        var payroll = new PayrollPeriodKey(new YearMonth(2026, 9));

        state.SelectedRootRoute = NavigationRoutes.Calendar;
        state.CalendarMonth = new YearMonth(2026, 7);
        state.SelectedCalendarDate = new DateOnly(2026, 7, 14);
        state.SettingsMonth = new YearMonth(2026, 6);
        state.PayrollPeriod = payroll;

        Assert.Equal(NavigationRoutes.Calendar, state.SelectedRootRoute);
        Assert.Equal(new YearMonth(2026, 7), state.CalendarMonth);
        Assert.Equal(new DateOnly(2026, 7, 14), state.SelectedCalendarDate);
        Assert.Equal(new YearMonth(2026, 6), state.SettingsMonth);
        Assert.Equal(payroll, state.PayrollPeriod);

        state.SelectedRootRoute = "unknown";
        Assert.Equal(NavigationRoutes.Home, state.SelectedRootRoute);
    }

    [Fact]
    public void SessionState_TracksIndependentDataGenerationsAndCanInvalidateAll()
    {
        var state = new AppSessionState(new DateOnly(2026, 8, 21));
        var workBefore = state.GetDataGeneration(AppDataChangeKind.WorkRecords);
        var settingsBefore = state.GetDataGeneration(AppDataChangeKind.Settings);

        state.NotifyDataChanged(AppDataChangeKind.WorkRecords | AppDataChangeKind.BackupStatus);

        Assert.True(state.GetDataGeneration(AppDataChangeKind.WorkRecords) > workBefore);
        Assert.Equal(settingsBefore, state.GetDataGeneration(AppDataChangeKind.Settings));

        var workAfterChange = state.GetDataGeneration(AppDataChangeKind.WorkRecords);
        var settingsAfterChange = state.GetDataGeneration(AppDataChangeKind.Settings);
        state.ResetDataGenerations();

        Assert.True(state.GetDataGeneration(AppDataChangeKind.WorkRecords) > workAfterChange);
        Assert.True(state.GetDataGeneration(AppDataChangeKind.Settings) > settingsAfterChange);
    }

    [Theory]
    [InlineData(InitialSetupStatus.NotStarted, null)]
    [InlineData(InitialSetupStatus.InProgress, "closing-day")]
    public async Task Startup_IncompleteSetupPublishesOnlySetupRoot(
        InitialSetupStatus status,
        string? step)
    {
        var log = new List<string>();
        var state = new AppSessionState(new DateOnly(2026, 8, 21));
        var payroll = new PayrollPeriodStub(log, PeriodForAugustTwentyFirst());
        var navigator = new RootNavigatorStub(log);
        var coordinator = new AppStartupCoordinator(
            new DatabaseInitializerStub(log),
            new ImportStagingStub(log),
            new InitialSetupStub(log, new InitialSetupStateDto(status, step, [])),
            payroll,
            new ClockStub(new DateTimeOffset(2026, 8, 21, 1, 0, 0, TimeSpan.Zero)),
            new LocalDateConverterStub(new DateOnly(2026, 8, 21)),
            state,
            navigator);

        await coordinator.StartAsync(default);

        Assert.Equal(["database", "import-cleanup", "initial-setup", "navigate"], log);
        Assert.Equal(AppRootKind.InitialSetup, navigator.Request!.RootKind);
        Assert.Equal(step, navigator.Request.SetupStep);
        Assert.Equal(0, payroll.CallCount);
        Assert.Null(state.PayrollPeriod);
    }

    [Fact]
    public async Task Startup_CompletedSetupResolvesCurrentPayrollPeriodBeforeMainRoot()
    {
        var log = new List<string>();
        var state = new AppSessionState(new DateOnly(2026, 8, 21));
        var payroll = new PayrollPeriodStub(log, PeriodForAugustTwentyFirst());
        var navigator = new RootNavigatorStub(log);
        var coordinator = new AppStartupCoordinator(
            new DatabaseInitializerStub(log),
            new ImportStagingStub(log),
            new InitialSetupStub(log, new InitialSetupStateDto(InitialSetupStatus.Completed, null, [])),
            payroll,
            new ClockStub(new DateTimeOffset(2026, 8, 20, 16, 0, 0, TimeSpan.Zero)),
            new LocalDateConverterStub(new DateOnly(2026, 8, 21)),
            state,
            navigator);

        await coordinator.StartAsync(default);

        Assert.Equal(["database", "import-cleanup", "initial-setup", "payroll-period", "navigate"], log);
        Assert.Equal(new DateOnly(2026, 8, 21), payroll.RequestedDate);
        Assert.Equal(new PayrollPeriodKey(new YearMonth(2026, 9)), state.PayrollPeriod);
        Assert.Equal(AppRootKind.Main, navigator.Request!.RootKind);
    }

    [Fact]
    public async Task Startup_RecreationDoesNotResetExistingSessionSelections()
    {
        var log = new List<string>();
        var state = new AppSessionState(new DateOnly(2026, 8, 21))
        {
            SelectedRootRoute = NavigationRoutes.Settings,
            CalendarMonth = new YearMonth(2026, 3),
            SelectedCalendarDate = new DateOnly(2026, 3, 5),
            SettingsMonth = new YearMonth(2026, 4),
            PayrollPeriod = new PayrollPeriodKey(new YearMonth(2026, 5)),
        };
        var payroll = new PayrollPeriodStub(log, PeriodForAugustTwentyFirst());
        var coordinator = new AppStartupCoordinator(
            new DatabaseInitializerStub(log),
            new ImportStagingStub(log),
            new InitialSetupStub(log, new InitialSetupStateDto(InitialSetupStatus.Completed, null, [])),
            payroll,
            new ClockStub(DateTimeOffset.UnixEpoch),
            new LocalDateConverterStub(new DateOnly(2026, 8, 21)),
            state,
            new RootNavigatorStub(log));

        await coordinator.StartAsync(default);
        await coordinator.StartAsync(default);

        Assert.Equal(0, payroll.CallCount);
        Assert.Equal(2, log.Count(entry => entry == "navigate"));
        Assert.Equal(NavigationRoutes.Settings, state.SelectedRootRoute);
        Assert.Equal(new YearMonth(2026, 3), state.CalendarMonth);
        Assert.Equal(new DateOnly(2026, 3, 5), state.SelectedCalendarDate);
        Assert.Equal(new YearMonth(2026, 4), state.SettingsMonth);
        Assert.Equal(new PayrollPeriodKey(new YearMonth(2026, 5)), state.PayrollPeriod);
    }

    private static PayrollPeriod PeriodForAugustTwentyFirst() => new(
        new PayrollPeriodKey(new YearMonth(2026, 9)),
        new DateOnly(2026, 8, 21),
        new DateOnly(2026, 9, 20));

    private sealed class DatabaseInitializerStub(List<string> log) : IApplicationDatabaseInitializer
    {
        public Task InitializeAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            log.Add("database");
            return Task.CompletedTask;
        }
    }

    private sealed class ImportStagingStub(List<string> log) : IImportStagingRepository
    {
        public Task<PreparedImportId> CreateAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task AppendBatchAsync(PreparedImportId preparedImportId, IReadOnlyList<DataTransferRecord> records,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ImportPreviewDto> ValidateAsync(PreparedImportId preparedImportId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> TryConsumeAndReplaceLiveDataAsync(PreparedImportId preparedImportId,
            DateTimeOffset importedAtUtc, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task DiscardAsync(PreparedImportId preparedImportId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DiscardAbandonedAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            log.Add("import-cleanup");
            return Task.CompletedTask;
        }
    }

    private sealed class InitialSetupStub(List<string> log, InitialSetupStateDto state) : IInitialSetupUseCase
    {
        public Task<InitialSetupStateDto> GetStateAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            log.Add("initial-setup");
            return Task.FromResult(state);
        }

        public Task SaveProgressAsync(string step, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<InitialSetupStateDto> CompleteAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class PayrollPeriodStub(List<string> log, PayrollPeriod period) : IPayrollPeriodSettingsUseCase
    {
        public int CallCount { get; private set; }
        public DateOnly? RequestedDate { get; private set; }

        public Task<PayrollPeriod> FindPeriodAsync(DateOnly localDate, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            RequestedDate = localDate;
            log.Add("payroll-period");
            return Task.FromResult(period);
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

    private sealed class RootNavigatorStub(List<string> log) : IAppRootNavigator
    {
        public AppRootNavigationRequest? Request { get; private set; }

        public Task SetRootAsync(AppRootNavigationRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Request = request;
            log.Add("navigate");
            return Task.CompletedTask;
        }
    }

    private sealed class ClockStub(DateTimeOffset utcNow) : IUtcClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class LocalDateConverterStub(DateOnly result) : ILocalDateConverter
    {
        public DateOnly ToLocalDate(DateTimeOffset utcDateTime) => result;
    }
}
