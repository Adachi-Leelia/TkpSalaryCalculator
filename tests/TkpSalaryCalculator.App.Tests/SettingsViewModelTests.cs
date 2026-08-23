using TkpSalaryCalculator.App.Navigation;
using TkpSalaryCalculator.App.Presentation.Common;
using TkpSalaryCalculator.App.Presentation.Features.Settings;
using TkpSalaryCalculator.App.Presentation.Services;
using TkpSalaryCalculator.Application.Contracts;
using TkpSalaryCalculator.Application.UseCases;
using TkpSalaryCalculator.Domain.Models;
using TkpSalaryCalculator.Domain.ValueObjects;

namespace TkpSalaryCalculator.App.Tests;

public sealed class SettingsViewModelTests
{
    private static readonly YearMonth August = new(2026, 8);
    private static readonly ServiceId Service = new(Guid.Parse("10000000-0000-0000-0000-000000000001"));
    private static readonly TimeCategoryId Category = new(Guid.Parse("20000000-0000-0000-0000-000000000001"));

    [Fact]
    public async Task PERF05_SettingsContextReusesSnapshotUntilSettingsGenerationChanges()
    {
        var settings = new MonthSettingsStub();
        settings.Values[August] = Snapshot(1_200);
        var session = new AppSessionState(new DateOnly(2026, 8, 22));
        var context = Context(settings, session);

        await context.RefreshAsync(default);
        await context.RefreshAsync(default);
        session.NotifyDataChanged(AppDataChangeKind.WorkRecords);
        await context.RefreshAsync(default);

        Assert.Single(settings.GetCalls);

        session.NotifyDataChanged(AppDataChangeKind.Settings);
        await context.RefreshAsync(default);

        Assert.Equal(2, settings.GetCalls.Count);
    }

    [Fact]
    public async Task PERF08_ChangingSettingsMonthUpdatesHeaderAndSessionWithoutLoadingSnapshot()
    {
        var settings = new MonthSettingsStub();
        settings.Values[August] = Snapshot(1_200);
        settings.Values[new YearMonth(2026, 9)] = Snapshot(1_500);
        var session = new AppSessionState(new DateOnly(2026, 8, 22));
        var context = Context(settings, session);
        var viewModel = new SettingsMenuViewModel(context, settings, new SettingsNavigatorStub(),
            new DialogStub(), new JapaneseDisplayFormatter(), new UserErrorPresenter());

        await viewModel.LoadAsync();
        await viewModel.MoveMonthAsync(1);

        Assert.Equal(new YearMonth(2026, 9), session.SettingsMonth);
        Assert.Equal("設定対象年月: 2026年9月", viewModel.MonthHeaderText);
        Assert.Null(context.Value);
        Assert.Empty(settings.GetCalls);
    }

    [Fact]
    public async Task HIST014_CopyPreviousMonthAlwaysPreviewsAndConfirmsBeforeCommit()
    {
        var events = new List<string>();
        var settings = new MonthSettingsStub { SharedEvents = events };
        settings.Values[August] = Snapshot(1_200);
        var session = new AppSessionState(new DateOnly(2026, 8, 22));
        var dialogs = new DialogStub { Result = true, SharedEvents = events };
        var viewModel = new SettingsMenuViewModel(Context(settings, session), settings,
            new SettingsNavigatorStub(), dialogs, new JapaneseDisplayFormatter(), new UserErrorPresenter());

        await viewModel.LoadAsync();
        events.Clear();
        await viewModel.CopyPreviousMonthAsync();

        Assert.Equal(["preview-copy", "dialog", "copy"], events);
        Assert.Contains("影響する勤務記録: 2件", dialogs.LastMessage);
        Assert.Contains("見込み差額: +300円", dialogs.LastMessage);
        Assert.Contains("他の年月は変更しません", dialogs.LastMessage);
        Assert.Equal(1, settings.CopyCalls);
    }

    [Fact]
    public async Task HIST014_CancelledPreviousMonthPreviewDoesNotCommit()
    {
        var settings = new MonthSettingsStub();
        settings.Values[August] = Snapshot(1_200);
        var viewModel = new SettingsMenuViewModel(Context(settings, new AppSessionState(new DateOnly(2026, 8, 22))),
            settings, new SettingsNavigatorStub(), new DialogStub { Result = false },
            new JapaneseDisplayFormatter(), new UserErrorPresenter());

        await viewModel.CopyPreviousMonthAsync();

        Assert.Equal(0, settings.CopyCalls);
    }

    [Fact]
    public async Task HIST013_CountBonusSaveUsesPreviewTokenAndChangesOnlyReplacementSnapshot()
    {
        var events = new List<string>();
        var settings = new MonthSettingsStub { SharedEvents = events };
        settings.Values[August] = Snapshot(1_200);
        var session = new AppSessionState(new DateOnly(2026, 8, 22));
        var settingsGeneration = session.GetDataGeneration(AppDataChangeKind.Settings);
        var navigator = new SettingsNavigatorStub();
        var viewModel = new CountBonusSettingsEditorViewModel(Context(settings, session), settings,
            new DialogStub { Result = true, SharedEvents = events }, new JapaneseDisplayFormatter(), navigator,
            new UserErrorPresenter());
        viewModel.Initialize(null);
        await viewModel.LoadAsync();
        viewModel.DisplayName = "訪問件数加算";
        viewModel.AmountText = "300";

        await viewModel.SaveAsync();

        Assert.Equal(["preview", "dialog", "replace"], events);
        Assert.NotNull(settings.LastReplacement);
        var bonus = Assert.Single(settings.LastReplacement!.CountBonuses);
        Assert.Equal(300, bonus.Amount.Value);
        Assert.Empty(bonus.ServiceIds);
        Assert.Equal("件数加算設定を保存しました。", navigator.SuccessMessage);
        Assert.False(viewModel.IsDirty);
        Assert.True(session.GetDataGeneration(AppDataChangeKind.Settings) > settingsGeneration);
    }

    [Fact]
    public async Task UX006_ServiceListSeparatesGlobalCandidatesFromSelectedMonthSettings()
    {
        var settings = new MonthSettingsStub();
        settings.Values[August] = Snapshot(1_200);
        var preset = new PresetStub
        {
            Values = [new ServicePresetDto(new ServicePresetId(Guid.NewGuid()), "いつもの身体1", Service, Category,
                new WorkMinutes(30), new DisplayOrder(0), true)],
        };
        var context = Context(settings, new AppSessionState(new DateOnly(2026, 8, 22)));
        var viewModel = new ServiceSettingsViewModel(context, preset, new SettingsNavigatorStub(),
            new JapaneseDisplayFormatter(), new UserErrorPresenter());

        await viewModel.LoadAsync();

        Assert.Equal("2026年8月の給与設定", viewModel.MonthlySectionTitle);
        Assert.Equal("いつもの身体1", Assert.Single(viewModel.InputCandidateRows).DisplayName);
        Assert.Equal(
            "時給 1,200円",
            Assert.Single(viewModel.MonthlyRows, value => value.EditorId == Category.Value).RateText);
        Assert.Contains(viewModel.MonthlyRows, value => value.EditorId == Service.Value && value.TimeCategoryName == "任意時間");
    }

    [Fact]
    public async Task ServiceEditor_CanAddServiceLevelRateForArbitraryDuration()
    {
        var settings = new MonthSettingsStub();
        settings.Values[August] = Snapshot(1_200);
        var viewModel = new ServiceSettingsEditorViewModel(Context(settings, new AppSessionState(new DateOnly(2026, 8, 22))),
            settings, new PresetStub(), new DialogStub { Result = true }, new JapaneseDisplayFormatter(),
            new SettingsNavigatorStub(), new UserErrorPresenter());
        viewModel.Initialize(null);
        await viewModel.LoadAsync();
        viewModel.UseTimeCategory = false;
        viewModel.ServiceName = "任意時間サービス";
        viewModel.AmountText = "1200";
        viewModel.CandidateName = "任意時間サービス";

        await viewModel.SaveAsync();

        var rate = Assert.Single(settings.LastReplacement!.Rates, value =>
            value.ServiceId != Service && value.TimeCategoryId is null);
        Assert.Equal(1_200, rate.Amount.Value);
    }

    [Fact]
    public async Task ServiceEditor_ChangesTimeCategoryEnabledStateWithoutDisablingService()
    {
        var settings = new MonthSettingsStub();
        settings.Values[August] = Snapshot(1_200);
        var viewModel = new ServiceSettingsEditorViewModel(Context(settings, new AppSessionState(new DateOnly(2026, 8, 22))),
            settings, new PresetStub(), new DialogStub { Result = true }, new JapaneseDisplayFormatter(),
            new SettingsNavigatorStub(), new UserErrorPresenter());
        viewModel.Initialize(Category.Value);
        await viewModel.LoadAsync();
        viewModel.TimeCategoryIsEnabled = false;

        await viewModel.SaveAsync();

        Assert.True(Assert.Single(settings.LastReplacement!.Services).IsEnabled);
        Assert.False(Assert.Single(settings.LastReplacement.TimeCategories).IsEnabled);
    }

    [Fact]
    public async Task PremiumEditor_ExplainsTimeRangeAsAdditionalToDateCondition()
    {
        var settings = new MonthSettingsStub();
        settings.Values[August] = Snapshot(1_200);
        var viewModel = new PremiumSettingsEditorViewModel(Context(settings, new AppSessionState(new DateOnly(2026, 8, 22))),
            settings, new DialogStub { Result = true }, new JapaneseDisplayFormatter(), new SettingsNavigatorStub(),
            new UserErrorPresenter());
        viewModel.Initialize(null);
        await viewModel.LoadAsync();
        viewModel.Sunday = true;
        viewModel.UsesTimeRange = true;

        Assert.Contains("さらにその時間帯", viewModel.ConditionExplanation);
    }

    [Fact]
    public async Task PERIOD006_ClosingDaySavePreviewsBothPeriodsBeforeReplacingHistory()
    {
        var events = new List<string>();
        var monthSettings = new MonthSettingsStub();
        monthSettings.Values[August] = Snapshot(1_200);
        var periodSettings = new PeriodSettingsStub { SharedEvents = events };
        var dialogs = new DialogStub { Result = true, SharedEvents = events };
        var navigator = new SettingsNavigatorStub();
        var viewModel = new PayrollPeriodSettingsViewModel(
            Context(monthSettings, new AppSessionState(new DateOnly(2026, 8, 22))), periodSettings,
            dialogs, new JapaneseDisplayFormatter(), navigator, new UserErrorPresenter());

        await viewModel.LoadAsync();
        viewModel.SelectedClosingDay = ClosingDayOption.All.Single(value => value.Value == 15);
        await viewModel.SaveAsync();

        Assert.Equal(["preview-period", "dialog", "replace-period"], events);
        Assert.Contains("変更前の最初の給与期間", dialogs.LastMessage);
        Assert.Contains("給与算定開始日: 2026年7月21日", dialogs.LastMessage);
        Assert.Contains("給与算定終了日: 2026年8月15日", dialogs.LastMessage);
        Assert.Contains("それより前の給与期間は変更しません", dialogs.LastMessage);
        Assert.Equal(15, periodSettings.LastCommand!.ClosingDay);
    }

    [Fact]
    public async Task PERF09_MonthlyAllowanceListLoadsPeriodScreenWithoutSalaryCalculationAndMovesPeriods()
    {
        var augustKey = new PayrollPeriodKey(August);
        var septemberKey = new PayrollPeriodKey(new YearMonth(2026, 9));
        var periodSettings = new PeriodSettingsStub();
        periodSettings.AllowancePeriods[augustKey] = new MonthlyAllowancePeriodDto(
            new PayrollPeriod(augustKey, new DateOnly(2026, 7, 21), new DateOnly(2026, 8, 20)),
            [new MonthlyAllowanceDto(new MonthlyAllowanceId(Guid.NewGuid()), "資格手当", new YenAmount(5_000))]);
        periodSettings.AllowancePeriods[septemberKey] = new MonthlyAllowancePeriodDto(
            new PayrollPeriod(septemberKey, new DateOnly(2026, 8, 21), new DateOnly(2026, 9, 20)), []);
        var session = new AppSessionState(new DateOnly(2026, 8, 22)) { PayrollPeriod = augustKey };
        var viewModel = new MonthlyAllowanceViewModel(periodSettings, new SettingsNavigatorStub(),
            new DialogStub(), session, new FixedClock(), new FixedLocalDate(),
            new JapaneseDisplayFormatter(), new UserErrorPresenter());

        await viewModel.LoadAsync();
        await viewModel.MoveAsync(1);

        Assert.Equal([augustKey, septemberKey], periodSettings.AllowancePeriodCalls);
        Assert.Equal("対象給与期間: 2026年9月分（2026年8月21日～2026年9月20日）", viewModel.PeriodText);
        Assert.Equal("手当合計: 0円", viewModel.TotalText);
        Assert.Empty(viewModel.Rows);
        Assert.Equal(septemberKey, session.PayrollPeriod);
    }

    [Fact]
    public async Task MonthlyAllowanceDeleteReloadsTheLightweightPeriodScreen()
    {
        var key = new PayrollPeriodKey(August);
        var allowance = new MonthlyAllowanceDto(new MonthlyAllowanceId(Guid.NewGuid()), "資格手当", new YenAmount(5_000));
        var periodSettings = new PeriodSettingsStub();
        periodSettings.AllowancePeriods[key] = new MonthlyAllowancePeriodDto(
            new PayrollPeriod(key, new DateOnly(2026, 7, 21), new DateOnly(2026, 8, 20)), [allowance]);
        var session = new AppSessionState(new DateOnly(2026, 8, 22)) { PayrollPeriod = key };
        var viewModel = new MonthlyAllowanceViewModel(periodSettings, new SettingsNavigatorStub(),
            new DialogStub { Result = true }, session, new FixedClock(), new FixedLocalDate(),
            new JapaneseDisplayFormatter(), new UserErrorPresenter());
        await viewModel.LoadAsync();

        await Assert.Single(viewModel.Rows).Delete();

        Assert.Equal(allowance.Id, periodSettings.DeletedAllowanceId);
        Assert.Equal(2, periodSettings.AllowancePeriodCalls.Count);
        Assert.Empty(viewModel.Rows);
        Assert.Equal("月額手当を削除しました。", viewModel.SuccessMessage);
    }

    [Fact]
    public async Task MonthlyAllowanceEditorSaveInvalidatesAllowanceDataAndReturnsToList()
    {
        var key = new PayrollPeriodKey(August);
        var periodSettings = new PeriodSettingsStub();
        var navigator = new SettingsNavigatorStub();
        var session = new AppSessionState(new DateOnly(2026, 8, 22));
        var generationBefore = session.GetDataGeneration(AppDataChangeKind.MonthlyAllowances);
        var viewModel = new MonthlyAllowanceEditorViewModel(periodSettings, navigator,
            new UserErrorPresenter(), new DialogStub(), session);
        viewModel.Initialize(key, null);
        await viewModel.LoadAsync();
        viewModel.DisplayName = "資格手当";
        viewModel.AmountText = "5000";

        await viewModel.SaveAsync();

        Assert.Equal("資格手当", periodSettings.LastAllowanceCommand!.DisplayName);
        Assert.Equal(5_000, periodSettings.LastAllowanceCommand.Amount.Value);
        Assert.Equal("月額手当を保存しました。", navigator.SuccessMessage);
        Assert.True(session.GetDataGeneration(AppDataChangeKind.MonthlyAllowances) > generationBefore);
        Assert.False(viewModel.IsDirty);
    }

    [Fact]
    public async Task BasicShiftList_ResolvesNamesWithoutLoadingRankedInputCandidates()
    {
        var shift = new BasicShiftDto(new BasicShiftId(Guid.NewGuid()), DayOfWeek.Monday, null,
            Service, Category, WorkInputMode.Duration, new WorkMinutes(60), null, null,
            new DisplayOrder(0), true);
        var shifts = new BasicShiftStub(shift);
        var work = new WorkSettingsStub(new MonthSettingsDto(August, Snapshot(1_200)));
        var viewModel = new BasicShiftViewModel(
            shifts, work, new SettingsNavigatorStub(), new DialogStub(),
            new FixedClock(), new FixedLocalDate(), new JapaneseDisplayFormatter(), new UserErrorPresenter(),
            new AppSessionState(new DateOnly(2026, 8, 22)));

        await viewModel.LoadAsync();

        var row = Assert.Single(Assert.Single(viewModel.Groups, group => group.HasRows).Rows);
        Assert.Equal("身体 / 身体1", row.WorkText);
        Assert.Equal(1, work.SettingsCalls);
        Assert.Equal(0, work.InputOptionsCalls);
    }

    private static SettingsMonthContext Context(MonthSettingsStub settings, IAppSessionState session) =>
        new(settings, session, new JapaneseDisplayFormatter());

    private static SettingSnapshot Snapshot(long rate) => new(
        new SettingSnapshotId(Guid.NewGuid()), null, new HolidayCalendarVersionId(Guid.NewGuid()), new SchemaVersion(1),
        DateTimeOffset.UnixEpoch,
        [new SnapshotService(Service, "身体", new DisplayOrder(0), true)],
        [new SnapshotTimeCategory(Category, Service, "身体1", new WorkMinutes(30), new DisplayOrder(0), true)],
        [new SnapshotRate(Service, Category, RateType.Hourly, new YenAmount(rate))], [], []);

    private sealed class MonthSettingsStub : IMonthSettingsUseCase
    {
        public Dictionary<YearMonth, SettingSnapshot> Values { get; } = [];
        public List<YearMonth> GetCalls { get; } = [];
        public List<string>? SharedEvents { get; init; }
        public int CopyCalls { get; private set; }
        public SettingSnapshotReplacementDto? LastReplacement { get; private set; }

        public Task<MonthSettingsDto> GetAsync(YearMonth yearMonth, CancellationToken cancellationToken)
        {
            GetCalls.Add(yearMonth);
            return Task.FromResult(new MonthSettingsDto(yearMonth, Values.GetValueOrDefault(yearMonth, Snapshot(1_200))));
        }

        public Task<SettingReplacementPreviewDto> PreviewReplacementAsync(YearMonth yearMonth, SettingSnapshotReplacementDto replacement, CancellationToken cancellationToken)
        {
            SharedEvents?.Add("preview");
            LastReplacement = replacement;
            return Task.FromResult(Preview(yearMonth, Values.GetValueOrDefault(yearMonth, Snapshot(1_200))));
        }

        public Task<MonthSettingsDto> CloneAndReplaceAsync(YearMonth yearMonth, SettingSnapshotReplacementDto replacement,
            SettingReplacementConfirmationToken confirmationToken, CancellationToken cancellationToken)
        {
            SharedEvents?.Add("replace");
            LastReplacement = replacement;
            var old = Values.GetValueOrDefault(yearMonth, Snapshot(1_200));
            var value = new SettingSnapshot(new SettingSnapshotId(Guid.NewGuid()), old.Id, old.HolidayCalendarVersionId,
                old.SchemaVersion, DateTimeOffset.UnixEpoch, replacement.Services, replacement.TimeCategories,
                replacement.Rates, replacement.Premiums, replacement.CountBonuses);
            Values[yearMonth] = value;
            return Task.FromResult(new MonthSettingsDto(yearMonth, value));
        }

        public Task<MonthSettingsDto> CloneAndReplaceWithServicePresetAsync(YearMonth yearMonth,
            SettingSnapshotReplacementDto replacement, SettingReplacementConfirmationToken confirmationToken,
            ServicePresetChangeCommand presetChange, CancellationToken cancellationToken)
        {
            SharedEvents?.Add("replace-with-preset");
            return CloneAndReplaceAsync(yearMonth, replacement, confirmationToken, cancellationToken);
        }

        public Task<SettingReplacementPreviewDto> PreviewCopyPreviousMonthAsync(YearMonth yearMonth, CancellationToken cancellationToken)
        {
            SharedEvents?.Add("preview-copy");
            return Task.FromResult(Preview(yearMonth, Values.GetValueOrDefault(yearMonth, Snapshot(1_200))));
        }

        public Task<MonthSettingsDto> CopyPreviousMonthAsync(YearMonth yearMonth, SettingReplacementConfirmationToken confirmationToken, CancellationToken cancellationToken)
        {
            SharedEvents?.Add("copy");
            CopyCalls++;
            return GetAsync(yearMonth, cancellationToken);
        }

        private static SettingReplacementPreviewDto Preview(YearMonth month, SettingSnapshot snapshot) => new(
            month, new SettingReplacementConfirmationToken(month, snapshot.Id, null, "work", "replacement", snapshot.HolidayCalendarVersionId),
            2, new YenAmount(1_000), new YenAmount(1_300), 0, []);
    }

    private sealed class PresetStub : IServicePresetUseCase
    {
        public IReadOnlyList<ServicePresetDto> Values { get; init; } = [];
        public Task<IReadOnlyList<ServicePresetDto>> GetAllAsync(CancellationToken cancellationToken) => Task.FromResult(Values);
        public Task<ServicePresetDto> SaveAsync(SaveServicePresetCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteAsync(ServicePresetId id, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class BasicShiftStub(BasicShiftDto shift) : IBasicShiftUseCase
    {
        public Task<IReadOnlyList<BasicShiftDto>> GetForWeekdayAsync(
            DayOfWeek weekday, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BasicShiftDto>>(weekday == shift.Weekday ? [shift] : []);
        public Task<BasicShiftDto> SaveAsync(SaveBasicShiftCommand command, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task DeleteAsync(BasicShiftId id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<BasicShiftPreviewDto> PreviewForDateAsync(DateOnly workDate, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<SaveWorkRecordResultDto>> ApplyAsync(
            ApplyBasicShiftsCommand command, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class WorkSettingsStub(MonthSettingsDto settings) : IWorkRecordUseCase
    {
        public int SettingsCalls { get; private set; }
        public int InputOptionsCalls { get; private set; }

        public Task<WorkEditorScreenDto> GetEditorScreenAsync(
            DateOnly workDate, WorkRecordId? workRecordId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<MonthSettingsDto> GetSettingsForDateAsync(DateOnly workDate, CancellationToken cancellationToken)
        {
            SettingsCalls++;
            return Task.FromResult(settings);
        }
        public Task<WorkInputOptionsDto> GetInputOptionsAsync(DateOnly workDate, CancellationToken cancellationToken)
        {
            InputOptionsCalls++;
            throw new NotSupportedException();
        }
        public Task<IReadOnlyList<WorkRecordDto>> GetForDateAsync(DateOnly workDate, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<WorkRecordPreviewDto> PreviewAsync(SaveWorkRecordCommand command, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<WorkRecordPreviewDto> PreviewForEditorAsync(
            SaveWorkRecordCommand command, WorkEditorScreenDto screen, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<SaveWorkRecordResultDto> SaveAsync(SaveWorkRecordCommand command, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task DeleteAsync(WorkRecordId id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<CopyDayPreviewDto> PreviewCopyDayAsync(
            DateOnly sourceDate, DateOnly targetDate, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<SaveWorkRecordResultDto>> CopyDayAsync(
            DateOnly sourceDate, DateOnly targetDate, CopyDayConfirmationToken confirmationToken,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FixedClock : IUtcClock
    {
        public DateTimeOffset UtcNow => new(2026, 8, 22, 0, 0, 0, TimeSpan.Zero);
    }

    private sealed class FixedLocalDate : ILocalDateConverter
    {
        public DateOnly ToLocalDate(DateTimeOffset utcDateTime) => new(2026, 8, 22);
    }

    private sealed class PeriodSettingsStub : IPayrollPeriodSettingsUseCase
    {
        public List<string>? SharedEvents { get; init; }
        public Dictionary<PayrollPeriodKey, MonthlyAllowancePeriodDto> AllowancePeriods { get; } = [];
        public List<PayrollPeriodKey> AllowancePeriodCalls { get; } = [];
        public MonthlyAllowanceId? DeletedAllowanceId { get; private set; }
        public SaveMonthlyAllowanceCommand? LastAllowanceCommand { get; private set; }
        public ReplaceClosingRuleCommand? LastCommand { get; private set; }
        public Task<EffectiveClosingRuleDto?> GetClosingRuleAsync(PayrollPeriodKey key, CancellationToken token) =>
            Task.FromResult<EffectiveClosingRuleDto?>(new(key, new ClosingRuleId(Guid.NewGuid()), new PayrollPeriodKey(new YearMonth(2025, 1)), 20));
        public Task<ClosingRuleReplacementPreviewDto> PreviewClosingRuleReplacementAsync(ReplaceClosingRuleCommand command, CancellationToken token)
        {
            SharedEvents?.Add("preview-period");
            return Task.FromResult(new ClosingRuleReplacementPreviewDto(command.EffectiveFrom,
                new PayrollPeriod(command.EffectiveFrom, new DateOnly(2026, 7, 21), new DateOnly(2026, 8, 20)),
                new PayrollPeriod(command.EffectiveFrom, new DateOnly(2026, 7, 21), new DateOnly(2026, 8, 15)),
                new ClosingRuleReplacementConfirmationToken(command.EffectiveFrom, command.ClosingDay, new ClosingRuleHistoryVersion("v1"))));
        }
        public Task ReplaceClosingRuleAsync(ReplaceClosingRuleCommand command, ClosingRuleReplacementConfirmationToken confirmationToken, CancellationToken token) { SharedEvents?.Add("replace-period"); LastCommand = command; return Task.CompletedTask; }
        public Task<PayrollPeriod> FindPeriodAsync(DateOnly localDate, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<MonthlyAllowancePeriodDto> GetMonthlyAllowancePeriodAsync(PayrollPeriodKey payrollPeriodKey, CancellationToken cancellationToken)
        {
            AllowancePeriodCalls.Add(payrollPeriodKey);
            return Task.FromResult(AllowancePeriods[payrollPeriodKey]);
        }
        public Task<IReadOnlyList<MonthlyAllowanceDto>> GetAllowancesAsync(PayrollPeriodKey payrollPeriodKey, CancellationToken cancellationToken) =>
            Task.FromResult(AllowancePeriods.TryGetValue(payrollPeriodKey, out var value)
                ? value.Allowances
                : (IReadOnlyList<MonthlyAllowanceDto>)[]);
        public Task<MonthlyAllowanceDto> SaveAllowanceAsync(SaveMonthlyAllowanceCommand command, CancellationToken cancellationToken)
        {
            LastAllowanceCommand = command;
            return Task.FromResult(new MonthlyAllowanceDto(command.Id ?? new MonthlyAllowanceId(Guid.NewGuid()),
                command.DisplayName, command.Amount));
        }
        public Task DeleteAllowanceAsync(MonthlyAllowanceId id, CancellationToken cancellationToken)
        {
            DeletedAllowanceId = id;
            foreach (var pair in AllowancePeriods.ToArray())
                AllowancePeriods[pair.Key] = pair.Value with
                {
                    Allowances = pair.Value.Allowances.Where(x => x.Id != id).ToArray(),
                };
            return Task.CompletedTask;
        }
    }

    private sealed class SettingsNavigatorStub : ISettingsNavigator
    {
        public string? SuccessMessage { get; private set; }
        public Task GoBackAsync(string? successMessage, CancellationToken cancellationToken) { SuccessMessage = successMessage; return Task.CompletedTask; }
        public Task OpenServicesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task OpenServiceEditorAsync(Guid? serviceId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task OpenPremiumsAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task OpenPremiumEditorAsync(Guid? premiumId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task OpenCountBonusesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task OpenCountBonusEditorAsync(Guid? countBonusId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task OpenPayrollPeriodAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task OpenMonthlyAllowancesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task OpenMonthlyAllowanceEditorAsync(PayrollPeriodKey payrollPeriodKey, Guid? allowanceId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task OpenBasicShiftsAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task OpenBasicShiftEditorAsync(Guid? basicShiftId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task OpenDataManagementAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task OpenAppInformationAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class DialogStub : IConfirmationDialogService
    {
        public bool Result { get; init; }
        public string? LastMessage { get; private set; }
        public List<string>? SharedEvents { get; init; }
        public Task<bool> ConfirmDiscardChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(Result);
        public Task<bool> ConfirmAsync(string title, string message, string acceptText, string cancelText, CancellationToken cancellationToken = default)
        { SharedEvents?.Add("dialog"); LastMessage = message; return Task.FromResult(Result); }
    }
}
