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
    public async Task UI008_RepeatedValidationRequestsFocusForTheSameFirstInvalidFieldEachTime()
    {
        var settings = new MonthSettingsStub();
        settings.Values[August] = Snapshot(1_200);
        var viewModel = new CountBonusSettingsEditorViewModel(
            Context(settings, new AppSessionState(new DateOnly(2026, 8, 22))), settings,
            new DialogStub(), new JapaneseDisplayFormatter(), new SettingsNavigatorStub(), new UserErrorPresenter());
        viewModel.Initialize(null);
        await viewModel.LoadAsync();
        var focusRequests = 0;
        viewModel.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(CountBonusSettingsEditorViewModel.FirstInvalidField) &&
                viewModel.FirstInvalidField == nameof(CountBonusSettingsEditorViewModel.DisplayName))
                focusRequests++;
        };

        await viewModel.SaveAsync();
        await viewModel.SaveAsync();

        Assert.Equal(2, focusRequests);
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
        viewModel.Initialize(ServiceSettingsEditorMode.AddService, null);
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
        viewModel.Initialize(ServiceSettingsEditorMode.EditMonthlySetting, Category.Value);
        await viewModel.LoadAsync();
        viewModel.TimeCategoryIsEnabled = false;

        await viewModel.SaveAsync();

        Assert.True(Assert.Single(settings.LastReplacement!.Services).IsEnabled);
        Assert.False(Assert.Single(settings.LastReplacement.TimeCategories).IsEnabled);
    }

    [Fact]
    public async Task ServiceEditor_AddsTimeCategoryToExistingServiceWithoutCreatingAnotherService()
    {
        var settings = new MonthSettingsStub();
        settings.Values[August] = Snapshot(1_200);
        var viewModel = new ServiceSettingsEditorViewModel(Context(settings, new AppSessionState(new DateOnly(2026, 8, 22))),
            settings, new PresetStub(), new DialogStub { Result = true }, new JapaneseDisplayFormatter(),
            new SettingsNavigatorStub(), new UserErrorPresenter());
        viewModel.Initialize(ServiceSettingsEditorMode.AddTimeCategory, Service.Value);
        await viewModel.LoadAsync();
        viewModel.CategoryName = "身体2";
        viewModel.CategoryStandardMinutesText = "60";
        viewModel.CategoryDisplayOrderText = "1";
        viewModel.AmountText = "1800";
        viewModel.SaveInputCandidate = false;

        await viewModel.SaveAsync();

        var replacement = Assert.IsType<SettingSnapshotReplacementDto>(settings.LastReplacement);
        Assert.Equal(Service, Assert.Single(replacement.Services).Id);
        Assert.Equal(2, replacement.TimeCategories.Count);
        var added = Assert.Single(replacement.TimeCategories, value => value.Id != Category);
        Assert.Equal(Service, added.ServiceId);
        Assert.Equal("身体2", added.DisplayName);
        Assert.Equal(60, added.StandardMinutes.Value);
        Assert.Equal(1, added.DisplayOrder.Value);
    }

    [Fact]
    public async Task ServiceEditor_PreservesInputCandidateDefaultMinutesIndependentFromCategoryStandardTime()
    {
        var presetId = new ServicePresetId(Guid.NewGuid());
        var settings = new MonthSettingsStub();
        settings.Values[August] = Snapshot(1_200);
        var presets = new PresetStub
        {
            Values = [new ServicePresetDto(presetId, "45分候補", Service, Category,
                new WorkMinutes(45), new DisplayOrder(0), true)],
        };
        var viewModel = new ServiceSettingsEditorViewModel(Context(settings, new AppSessionState(new DateOnly(2026, 8, 22))),
            settings, presets, new DialogStub { Result = true }, new JapaneseDisplayFormatter(),
            new SettingsNavigatorStub(), new UserErrorPresenter());
        viewModel.Initialize(ServiceSettingsEditorMode.EditInputCandidate, presetId.Value);

        await viewModel.LoadAsync();

        Assert.Equal("30", viewModel.CategoryStandardMinutesText);
        Assert.Equal("45", viewModel.CandidateDefaultMinutesText);
        viewModel.CandidateName = "更新した候補";
        await viewModel.SaveAsync();

        Assert.Equal(30, Assert.Single(settings.LastReplacement!.TimeCategories).StandardMinutes.Value);
        Assert.Equal(45, settings.LastPresetChange!.Save!.DefaultWorkMinutes.Value);
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
    public async Task PayrollPeriodEditor_MovesEffectivePeriodWithoutChangingSettingsTargetMonth()
    {
        var monthSettings = new MonthSettingsStub();
        monthSettings.Values[August] = Snapshot(1_200);
        var session = new AppSessionState(new DateOnly(2026, 8, 22));
        var viewModel = new PayrollPeriodSettingsViewModel(Context(monthSettings, session), new PeriodSettingsStub(),
            new DialogStub { Result = true }, new JapaneseDisplayFormatter(), new SettingsNavigatorStub(),
            new UserErrorPresenter());
        await viewModel.LoadAsync();

        await viewModel.MoveMonthAsync(1);

        Assert.Equal(August, session.SettingsMonth);
        Assert.Equal("適用開始給与期間年月: 2026年9月", viewModel.EffectiveMonthText);
    }

    [Fact]
    public async Task AnnualSummaryEditorLoadsExampleAndNotifiesOnlyAnnualAndBackupChangesAfterSave()
    {
        var settings = new AnnualSummarySettingsStub();
        var navigator = new SettingsNavigatorStub();
        var session = new AppSessionState(new DateOnly(2026, 8, 29));
        var unrelatedBefore = session.GetDataGeneration(
            AppDataChangeKind.WorkRecords | AppDataChangeKind.Settings | AppDataChangeKind.ClosingRules |
            AppDataChangeKind.MonthlyAllowances | AppDataChangeKind.BasicShifts);
        var annualBefore = session.GetDataGeneration(AppDataChangeKind.AnnualSummarySettings);
        var backupBefore = session.GetDataGeneration(AppDataChangeKind.BackupStatus);
        var viewModel = new AnnualSummarySettingsViewModel(
            settings, navigator, session, new DialogStub(), new UserErrorPresenter());

        await viewModel.LoadAsync();
        Assert.Equal(12, viewModel.SelectedClosingMonth.Value);
        Assert.Equal("年間区分の例: 1月分～12月分", viewModel.AnnualPeriodExample);
        Assert.False(viewModel.IsDirty);

        viewModel.SelectedClosingMonth = AnnualClosingMonthOption.All.Single(option => option.Value == 3);
        Assert.Equal("年間区分の例: 前年4月分～当年3月分", viewModel.AnnualPeriodExample);
        Assert.True(viewModel.IsDirty);

        await viewModel.SaveAsync();

        Assert.Equal(3, settings.Value.ClosingMonth.Value);
        Assert.Equal("年間累計設定を保存しました。", navigator.SuccessMessage);
        Assert.False(viewModel.IsDirty);
        Assert.True(session.GetDataGeneration(AppDataChangeKind.AnnualSummarySettings) > annualBefore);
        Assert.True(session.GetDataGeneration(AppDataChangeKind.BackupStatus) > backupBefore);
        Assert.Equal(unrelatedBefore, session.GetDataGeneration(
            AppDataChangeKind.WorkRecords | AppDataChangeKind.Settings | AppDataChangeKind.ClosingRules |
            AppDataChangeKind.MonthlyAllowances | AppDataChangeKind.BasicShifts));
    }

    [Fact]
    public async Task UI017_AnnualSummaryEditorShowsEveryClosingMonthExampleAndGuardsUnsavedChanges()
    {
        var dialogs = new DialogStub { Result = false };
        var viewModel = new AnnualSummarySettingsViewModel(
            new AnnualSummarySettingsStub(),
            new SettingsNavigatorStub(),
            new AppSessionState(new DateOnly(2026, 8, 29)),
            dialogs,
            new UserErrorPresenter());
        await viewModel.LoadAsync();

        Assert.Equal(12, viewModel.ClosingMonths.Count);
        foreach (var option in viewModel.ClosingMonths)
        {
            viewModel.SelectedClosingMonth = option;
            var expected = option.Value == 12
                ? "年間区分の例: 1月分～12月分"
                : $"年間区分の例: 前年{option.Value + 1}月分～当年{option.Value}月分";
            Assert.Equal(expected, viewModel.AnnualPeriodExample);
        }

        Assert.True(viewModel.IsDirty);
        Assert.False(await viewModel.CanLeaveAsync());
        Assert.Equal(1, dialogs.DiscardCalls);
        dialogs.Result = true;
        Assert.True(await viewModel.CanLeaveAsync());
        Assert.Equal(2, dialogs.DiscardCalls);
    }

    [Fact]
    public async Task UI017_AnnualSummarySaveFailureKeepsEditsAndRetryNotifiesAfterSuccessOnly()
    {
        var settings = new AnnualSummarySettingsStub
        {
            SaveFailure = new InvalidOperationException("save failure"),
        };
        var navigator = new SettingsNavigatorStub();
        var session = new AppSessionState(new DateOnly(2026, 8, 29));
        var annualBefore = session.GetDataGeneration(AppDataChangeKind.AnnualSummarySettings);
        var backupBefore = session.GetDataGeneration(AppDataChangeKind.BackupStatus);
        var viewModel = new AnnualSummarySettingsViewModel(
            settings, navigator, session, new DialogStub(), new UserErrorPresenter());
        await viewModel.LoadAsync();
        viewModel.SelectedClosingMonth = AnnualClosingMonthOption.All.Single(option => option.Value == 3);

        await viewModel.SaveAsync();

        Assert.True(viewModel.HasError);
        Assert.True(viewModel.IsDirty);
        Assert.Null(navigator.SuccessMessage);
        Assert.Equal(annualBefore, session.GetDataGeneration(AppDataChangeKind.AnnualSummarySettings));
        Assert.Equal(backupBefore, session.GetDataGeneration(AppDataChangeKind.BackupStatus));

        settings.SaveFailure = null;
        await viewModel.SaveAsync();

        Assert.False(viewModel.HasError);
        Assert.False(viewModel.IsDirty);
        Assert.Equal(2, settings.SaveCalls);
        Assert.Equal(3, settings.Value.ClosingMonth.Value);
        Assert.Equal("年間累計設定を保存しました。", navigator.SuccessMessage);
        Assert.True(session.GetDataGeneration(AppDataChangeKind.AnnualSummarySettings) > annualBefore);
        Assert.True(session.GetDataGeneration(AppDataChangeKind.BackupStatus) > backupBefore);
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
        periodSettings.AllowancePeriods[key] = new MonthlyAllowancePeriodDto(
            new PayrollPeriod(key, new DateOnly(2026, 7, 21), new DateOnly(2026, 8, 20)), []);
        var navigator = new SettingsNavigatorStub();
        var session = new AppSessionState(new DateOnly(2026, 8, 22));
        var generationBefore = session.GetDataGeneration(AppDataChangeKind.MonthlyAllowances);
        var viewModel = new MonthlyAllowanceEditorViewModel(periodSettings, navigator,
            new UserErrorPresenter(), new IssuePresenter(), new DialogStub(), session,
            new JapaneseDisplayFormatter());
        viewModel.Initialize(key, null);
        await viewModel.LoadAsync();
        viewModel.DisplayName = "資格手当";
        viewModel.AmountText = "5000";

        await viewModel.SaveAsync();

        Assert.Equal("資格手当", periodSettings.LastAllowanceCommand!.DisplayName);
        Assert.Equal(5_000, periodSettings.LastAllowanceCommand.Amount.Value);
        Assert.Equal("対象給与期間: 2026年8月分（2026年7月21日～2026年8月20日）", viewModel.PeriodText);
        Assert.Equal("月額手当を保存しました。", navigator.SuccessMessage);
        Assert.True(session.GetDataGeneration(AppDataChangeKind.MonthlyAllowances) > generationBefore);
        Assert.False(viewModel.IsDirty);
    }

    [Fact]
    public async Task UI008_MonthlyAllowanceEditorShowsAndClearsFirstFieldError()
    {
        var key = new PayrollPeriodKey(August);
        var periodSettings = new PeriodSettingsStub();
        periodSettings.AllowancePeriods[key] = new MonthlyAllowancePeriodDto(
            new PayrollPeriod(key, new DateOnly(2026, 7, 21), new DateOnly(2026, 8, 20)), []);
        var viewModel = new MonthlyAllowanceEditorViewModel(
            periodSettings, new SettingsNavigatorStub(), new UserErrorPresenter(), new IssuePresenter(),
            new DialogStub(), new AppSessionState(new DateOnly(2026, 8, 22)), new JapaneseDisplayFormatter());
        viewModel.Initialize(key, null);
        await viewModel.LoadAsync();
        viewModel.DisplayName = "資格手当";
        viewModel.AmountText = "-1";

        await viewModel.SaveAsync();

        Assert.Equal("Amount", viewModel.FirstInvalidField);
        Assert.Equal("金額は0円以上の整数で入力してください。", viewModel.AmountError);
        Assert.Null(periodSettings.LastAllowanceCommand);

        viewModel.AmountText = "5000";
        Assert.Null(viewModel.FirstInvalidField);
        Assert.Empty(viewModel.AmountError);
    }

    [Fact]
    public async Task BasicShiftList_ResolvesNamesWithoutLoadingRankedInputCandidates()
    {
        var shift = new BasicShiftDto(new BasicShiftId(Guid.NewGuid()), DayOfWeek.Monday, [new BasicShiftTaskDto(new BasicShiftTaskId(Guid.NewGuid()), null, Service, Category, WorkInputMode.Duration, new WorkMinutes(60), null, null, new DisplayOrder(0))], new DisplayOrder(0), true);
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

    [Fact]
    public async Task BasicShiftDelete_NotifiesShiftAndBackupGenerations()
    {
        var shift = new BasicShiftDto(new BasicShiftId(Guid.NewGuid()), DayOfWeek.Monday, [new BasicShiftTaskDto(new BasicShiftTaskId(Guid.NewGuid()), null, Service, Category, WorkInputMode.Duration, new WorkMinutes(60), null, null, new DisplayOrder(0))], new DisplayOrder(0), true);
        var shifts = new BasicShiftStub(shift);
        var session = new AppSessionState(new DateOnly(2026, 8, 22));
        var viewModel = new BasicShiftViewModel(
            shifts, new WorkSettingsStub(new MonthSettingsDto(August, Snapshot(1_200))),
            new SettingsNavigatorStub(), new DialogStub { Result = true }, new FixedClock(),
            new FixedLocalDate(), new JapaneseDisplayFormatter(), new UserErrorPresenter(), session);
        await viewModel.LoadAsync();
        var shiftGeneration = session.GetDataGeneration(AppDataChangeKind.BasicShifts);
        var backupGeneration = session.GetDataGeneration(AppDataChangeKind.BackupStatus);

        await Assert.Single(Assert.Single(viewModel.Groups, group => group.HasRows).Rows).Delete();

        Assert.Equal(shift.Id, shifts.DeletedId);
        Assert.True(session.GetDataGeneration(AppDataChangeKind.BasicShifts) > shiftGeneration);
        Assert.True(session.GetDataGeneration(AppDataChangeKind.BackupStatus) > backupGeneration);
    }

    [Fact]
    public async Task BasicShiftSave_NotifiesShiftAndBackupGenerations()
    {
        var shift = new BasicShiftDto(new BasicShiftId(Guid.NewGuid()), DayOfWeek.Monday, [new BasicShiftTaskDto(new BasicShiftTaskId(Guid.NewGuid()), null, Service, Category, WorkInputMode.Duration, new WorkMinutes(60), null, null, new DisplayOrder(0))], new DisplayOrder(0), true);
        var shifts = new BasicShiftStub(shift);
        var session = new AppSessionState(new DateOnly(2026, 8, 22));
        var viewModel = new BasicShiftEditorViewModel(
            shifts, new WorkSettingsStub(new MonthSettingsDto(August, Snapshot(1_200))),
            new SettingsNavigatorStub(), new FixedClock(), new FixedLocalDate(), new UserErrorPresenter(),
            new IssuePresenter(), new DialogStub { Result = true }, session);
        viewModel.Initialize(null);
        await viewModel.LoadAsync();
        viewModel.Tasks[0].SelectedService = viewModel.Tasks[0].Services[0];
        viewModel.Tasks[0].WorkMinutesText = "60";
        var shiftGeneration = session.GetDataGeneration(AppDataChangeKind.BasicShifts);
        var backupGeneration = session.GetDataGeneration(AppDataChangeKind.BackupStatus);

        await viewModel.SaveAsync();

        Assert.NotNull(shifts.SavedCommand);
        Assert.True(session.GetDataGeneration(AppDataChangeKind.BasicShifts) > shiftGeneration);
        Assert.True(session.GetDataGeneration(AppDataChangeKind.BackupStatus) > backupGeneration);
    }

    [Fact]
    public async Task UI008_BasicShiftEditorIdentifiesDisplayOrderAsFirstInvalidField()
    {
        var shift = new BasicShiftDto(new BasicShiftId(Guid.NewGuid()), DayOfWeek.Monday, [new BasicShiftTaskDto(new BasicShiftTaskId(Guid.NewGuid()), null, Service, Category, WorkInputMode.Duration, new WorkMinutes(60), null, null, new DisplayOrder(0))], new DisplayOrder(0), true);
        var shifts = new BasicShiftStub(shift);
        var viewModel = new BasicShiftEditorViewModel(
            shifts, new WorkSettingsStub(new MonthSettingsDto(August, Snapshot(1_200))),
            new SettingsNavigatorStub(), new FixedClock(), new FixedLocalDate(), new UserErrorPresenter(),
            new IssuePresenter(), new DialogStub(), new AppSessionState(new DateOnly(2026, 8, 22)));
        viewModel.Initialize(null);
        await viewModel.LoadAsync();
        viewModel.DisplayOrderText = "-1";

        await viewModel.SaveAsync();

        Assert.Equal("DisplayOrder", viewModel.FirstInvalidField);
        Assert.Equal("表示順は0以上の整数で入力してください。", viewModel.DisplayOrderError);
        Assert.Null(shifts.SavedCommand);
    }

    [Fact]
    public async Task SHIFT011_NewShiftHasUnselectedTaskAndLastTaskCannotBeDeleted()
    {
        var (viewModel, shifts) = await ShiftEditorAsync(existing: false);
        var first = Assert.Single(viewModel.Tasks);
        Assert.Null(first.SelectedService);
        Assert.False(first.DeleteCommand.CanExecute(null));
        first.DeleteCommand.Execute(null);
        Assert.Single(viewModel.Tasks);
        await viewModel.SaveAsync();
        Assert.Null(shifts.SavedCommand);
        Assert.Same(first, viewModel.FirstInvalidTask);
        Assert.Equal("ServiceId", viewModel.FirstInvalidField);
        Assert.NotEmpty(first.ServiceError);
    }

    [Fact]
    public async Task SHIFT011_EditorAddsReordersDeletesAndSavesEveryTaskWithStableIds()
    {
        var (viewModel, shifts) = await ShiftEditorAsync(existing: true);
        var first = Assert.Single(viewModel.Tasks);
        await viewModel.AddTaskAsync();
        var second = viewModel.Tasks[1];
        second.SelectedService = second.Services[0];
        second.SelectedInputMode = WorkInputModeOption.TimeRange;
        second.StartTime = TimeSpan.FromHours(23);
        second.EndTime = TimeSpan.FromMinutes(30);
        second.MoveUpCommand.Execute(null);
        Assert.Equal([second.Id, first.Id], viewModel.Tasks.Select(task => task.Id));
        Assert.Equal([0, 1], viewModel.Tasks.Select(task => task.DisplayOrder));
        Assert.Equal(TimeSpan.FromHours(9), first.StartTime);
        await viewModel.AddTaskAsync();
        viewModel.Tasks[2].DeleteCommand.Execute(null);
        Assert.Equal(2, viewModel.Tasks.Count);

        await viewModel.SaveAsync();
        await viewModel.SaveAsync();

        Assert.Equal(1, shifts.SaveCalls);
        var saved = shifts.SavedCommand!;
        Assert.Equal([second.Id.Value, first.Id.Value], saved.Tasks.Select(task => task.Id.Value));
        Assert.Equal([0, 1], saved.Tasks.Select(task => task.DisplayOrder.Value));
        Assert.Equal(1380, saved.Tasks[0].StartTime!.Value.Value);
        Assert.Equal(30, saved.Tasks[0].EndTime!.Value.Value);
        Assert.Equal(60, saved.Tasks[1].WorkMinutes!.Value.Value);
    }

    [Fact]
    public async Task SHIFT011_EditorPointsValidationToSecondTaskAndClearsItAfterCorrection()
    {
        var (viewModel, shifts) = await ShiftEditorAsync(existing: true);
        await viewModel.AddTaskAsync();
        var second = viewModel.Tasks[1];
        second.SelectedService = second.Services[0];
        second.WorkMinutesText = "1441";

        await viewModel.SaveAsync();

        Assert.Null(shifts.SavedCommand);
        Assert.Same(second, viewModel.FirstInvalidTask);
        Assert.Equal("WorkMinutes", viewModel.FirstInvalidField);
        Assert.NotEmpty(second.WorkMinutesError);
        Assert.False(viewModel.Tasks[0].HasErrors);
        second.WorkMinutesText = "30";
        Assert.Null(viewModel.FirstInvalidField);
        Assert.False(second.HasErrors);
        await viewModel.SaveAsync();
        Assert.Equal(2, shifts.SavedCommand!.Tasks.Count);
    }

    [Fact]
    public async Task SHIFT011_EditorReloadsAllTasksAndListIncludesEveryTask()
    {
        var (editor, shifts) = await ShiftEditorAsync(existing: true);
        await editor.AddTaskAsync();
        var second = editor.Tasks[1];
        second.SelectedService = second.Services[0];
        second.WorkMinutesText = "45";
        await editor.SaveAsync();
        editor.Initialize(shifts.SavedCommand!.Id);
        await editor.LoadAsync();
        Assert.Equal([60, 45], editor.Tasks.Select(task => int.Parse(task.WorkMinutesText)));
        Assert.Equal(shifts.SavedCommand.Tasks.Select(task => task.Id.Value), editor.Tasks.Select(task => task.Id.Value));
        var list = new BasicShiftViewModel(shifts, new WorkSettingsStub(new MonthSettingsDto(August, Snapshot(1_200))),
            new SettingsNavigatorStub(), new DialogStub(), new FixedClock(), new FixedLocalDate(),
            new JapaneseDisplayFormatter(), new UserErrorPresenter(), new AppSessionState(new DateOnly(2026, 8, 22)));
        await list.LoadAsync();
        var row = Assert.Single(Assert.Single(list.Groups, group => group.HasRows).Rows);
        Assert.Contains("タスク 2件", row.WorkText);
        Assert.Contains("タスク 1", row.TimeText);
        Assert.Contains("タスク 2: 45分", row.TimeText);
    }

    private static async Task<(BasicShiftEditorViewModel Editor, BasicShiftStub Shifts)> ShiftEditorAsync(bool existing)
    {
        var shift = new BasicShiftDto(new BasicShiftId(Guid.NewGuid()), DayOfWeek.Monday,
            [new BasicShiftTaskDto(new BasicShiftTaskId(Guid.NewGuid()), null, Service, Category,
                WorkInputMode.Duration, new WorkMinutes(60), null, null, new DisplayOrder(0))], new DisplayOrder(0), true);
        var shifts = new BasicShiftStub(shift);
        var editor = new BasicShiftEditorViewModel(shifts, new WorkSettingsStub(new MonthSettingsDto(August, Snapshot(1_200))),
            new SettingsNavigatorStub(), new FixedClock(), new FixedLocalDate(), new UserErrorPresenter(),
            new IssuePresenter(), new DialogStub(), new AppSessionState(new DateOnly(2026, 8, 22)));
        editor.Initialize(existing ? shift.Id : null);
        await editor.LoadAsync();
        return (editor, shifts);
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
        public ServicePresetChangeCommand? LastPresetChange { get; private set; }

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
            LastPresetChange = presetChange;
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
        public int SaveCalls { get; private set; }
        public SaveBasicShiftCommand? SavedCommand { get; private set; }
        public BasicShiftId? DeletedId { get; private set; }

        public Task<IReadOnlyList<BasicShiftDto>> GetForWeekdayAsync(
            DayOfWeek weekday, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BasicShiftDto>>(weekday == shift.Weekday ? [shift] : []);
        public Task<BasicShiftDto> SaveAsync(SaveBasicShiftCommand command, CancellationToken cancellationToken)
        {
            SavedCommand = command;
            SaveCalls++;
            shift = new BasicShiftDto(command.Id ?? new BasicShiftId(Guid.NewGuid()), command.Weekday,
                command.Tasks.Select(task => new BasicShiftTaskDto(task.Id, task.ServicePresetId, task.ServiceId,
                    task.TimeCategoryId, task.InputMode, task.WorkMinutes ?? new WorkMinutes(60),
                    task.StartTime, task.EndTime, task.DisplayOrder)).ToArray(), command.DisplayOrder, command.IsEnabled);
            return Task.FromResult(shift);
        }
        public Task DeleteAsync(BasicShiftId id, CancellationToken cancellationToken)
        {
            DeletedId = id;
            return Task.CompletedTask;
        }
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
        public Task OpenServiceEditorAsync(ServiceSettingsEditorMode mode, Guid? id, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task OpenPremiumsAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task OpenPremiumEditorAsync(Guid? premiumId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task OpenCountBonusesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task OpenCountBonusEditorAsync(Guid? countBonusId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task OpenPayrollPeriodAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task OpenAnnualSummarySettingsAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task OpenMonthlyAllowancesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task OpenMonthlyAllowanceEditorAsync(PayrollPeriodKey payrollPeriodKey, Guid? allowanceId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task OpenBasicShiftsAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task OpenBasicShiftEditorAsync(Guid? basicShiftId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task OpenDataManagementAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task OpenAppInformationAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class AnnualSummarySettingsStub : IAnnualSummarySettingsUseCase
    {
        public AnnualSummarySettingDto Value { get; private set; } =
            new(new AnnualClosingMonth(12));
        public Exception? SaveFailure { get; set; }
        public int SaveCalls { get; private set; }

        public Task<AnnualSummarySettingDto> GetAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Value);
        }

        public Task<AnnualSummarySettingDto> SaveAsync(
            SaveAnnualSummarySettingCommand command,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SaveCalls++;
            if (SaveFailure is { } failure) return Task.FromException<AnnualSummarySettingDto>(failure);
            Value = new AnnualSummarySettingDto(new AnnualClosingMonth(command.ClosingMonth));
            return Task.FromResult(Value);
        }
    }

    private sealed class DialogStub : IConfirmationDialogService
    {
        public bool Result { get; set; }
        public string? LastMessage { get; private set; }
        public List<string>? SharedEvents { get; init; }
        public int DiscardCalls { get; private set; }
        public Task<bool> ConfirmDiscardChangesAsync(CancellationToken cancellationToken = default)
        {
            DiscardCalls++;
            return Task.FromResult(Result);
        }
        public Task<bool> ConfirmAsync(string title, string message, string acceptText, string cancelText, CancellationToken cancellationToken = default)
        { SharedEvents?.Add("dialog"); LastMessage = message; return Task.FromResult(Result); }
    }
}
