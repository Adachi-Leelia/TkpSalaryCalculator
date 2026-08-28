namespace TkpSalaryCalculator.Application.Tests;

public sealed class SettingsAndSalaryUseCaseTests
{
    [Fact]
    public async Task CloneAndReplaceWithServicePreset_SavesSnapshotAndPresetTogether()
    {
        var context = new TestContext();
        var month = new YearMonth(2026, 8);
        var current = TestData.Snapshot();
        context.Settings.Months[month] = current;
        var replacement = new SettingSnapshotReplacementDto(current.Services, current.TimeCategories,
            current.Rates, current.Premiums, current.CountBonuses);
        var useCase = new MonthSettingsUseCase(context.Settings, context.Works, context.Holidays,
            context.Salary, context.Transactions, context.Metadata, context.Clock, context.Presets);
        var preview = await useCase.PreviewReplacementAsync(month, replacement, default);

        await useCase.CloneAndReplaceWithServicePresetAsync(month, replacement, preview.ConfirmationToken,
            new ServicePresetChangeCommand(new SaveServicePresetCommand(null, "任意時間", TestData.ServiceId, null,
                new WorkMinutes(60), new DisplayOrder(0), true), null), default);

        Assert.NotEqual(current.Id, context.Settings.Months[month].Id);
        var preset = Assert.Single(context.Presets.Values);
        Assert.Equal(TestData.ServiceId, preset.ServiceId);
        Assert.Null(preset.TimeCategoryId);
    }

    [Fact]
    public async Task CloneAndReplaceWithServicePreset_RollsBackSnapshotWhenPresetSaveFails()
    {
        var context = new TestContext();
        var month = new YearMonth(2026, 8);
        var current = TestData.Snapshot();
        context.Settings.Months[month] = current;
        var replacement = new SettingSnapshotReplacementDto(current.Services, current.TimeCategories,
            current.Rates, current.Premiums, current.CountBonuses);
        var useCase = new MonthSettingsUseCase(context.Settings, context.Works, context.Holidays,
            context.Salary, context.Transactions, context.Metadata, context.Clock, new FailingPresetRepository());
        var preview = await useCase.PreviewReplacementAsync(month, replacement, default);

        await Assert.ThrowsAsync<InvalidOperationException>(() => useCase.CloneAndReplaceWithServicePresetAsync(
            month, replacement, preview.ConfirmationToken,
            new ServicePresetChangeCommand(new SaveServicePresetCommand(null, "任意時間", TestData.ServiceId, null,
                new WorkMinutes(60), new DisplayOrder(0), true), null), default));

        Assert.Equal(current.Id, context.Settings.Months[month].Id);
    }

    [Fact]
    public async Task CloneAndReplace_ChangesOnlyTargetMonthAndMarksChanged()
    {
        var context = new TestContext();
        var july = new YearMonth(2026, 7);
        var august = new YearMonth(2026, 8);
        var shared = TestData.Snapshot();
        context.Settings.Months[july] = shared;
        context.Settings.Months[august] = shared;
        var replacement = new SettingSnapshotReplacementDto(shared.Services, shared.TimeCategories,
            [new SnapshotRate(TestData.ServiceId, TestData.CategoryId, RateType.FixedPerRecord, new YenAmount(2000))],
            shared.Premiums, shared.CountBonuses);
        var useCase = new MonthSettingsUseCase(context.Settings, context.Works, context.Holidays,
            context.Salary, context.Transactions, context.Metadata, context.Clock);

        var preview = await useCase.PreviewReplacementAsync(august, replacement, default);
        var result = await useCase.CloneAndReplaceAsync(august, replacement, preview.ConfirmationToken, default);

        Assert.Equal(shared.Id, context.Settings.Months[july].Id);
        Assert.NotEqual(shared.Id, result.Snapshot.Id);
        Assert.Equal(new YenAmount(2000), result.Snapshot.Rates[0].Amount);
        Assert.Equal(context.Clock.UtcNow, context.Metadata.Value.LastDataChangedAtUtc);
    }

    [Fact]
    public async Task PreviewReplacement_ReportsAffectedRecordsAndHasNoSideEffects()
    {
        var context = new TestContext();
        var month = new YearMonth(2026, 8);
        var bonusId = new CountBonusId(Guid.NewGuid());
        var currentBonus = new SnapshotCountBonus(bonusId, "加算", new YenAmount(100), new HashSet<ServiceId>(), true);
        var current = TestData.Snapshot(bonuses: [currentBonus]);
        context.Settings.Months[month] = current;
        context.Works.Values.Add(TestData.Work(new(2026, 8, 1)));
        var disabledBonus = new SnapshotCountBonus(bonusId, "加算", new YenAmount(100),
            new HashSet<ServiceId>(), false);
        var replacement = new SettingSnapshotReplacementDto(current.Services, current.TimeCategories,
            current.Rates, current.Premiums, [disabledBonus]);
        var useCase = new MonthSettingsUseCase(context.Settings, context.Works, context.Holidays,
            context.Salary, context.Transactions, context.Metadata, context.Clock);

        var result = await useCase.PreviewReplacementAsync(month, replacement, default);

        Assert.Equal(0, context.Settings.CloneCalls);
        Assert.Equal(0, context.Transactions.Calls);
        Assert.Equal(1, result.AffectedWorkRecordCount);
        Assert.Equal(1100, result.CurrentCalculatedSubtotal.Value);
        Assert.Equal(1000, result.ReplacementCalculatedSubtotal.Value);
    }

    [Fact]
    public async Task PreviewReplacement_ReusesCurrentAndCandidateSnapshotPerWorkDate()
    {
        var context = new TestContext();
        var month = new YearMonth(2026, 8);
        var date = new DateOnly(2026, 8, 1);
        var premium = new SnapshotPremium(new PremiumId(Guid.NewGuid()), "月曜夜間",
            PremiumCalculationType.FixedPerHour, null, new YenAmount(100),
            new MinuteOfDay(1320), new MinuteOfDay(300), false,
            new HashSet<DayOfWeek> { DayOfWeek.Monday }, new HashSet<DateOnly>(),
            new HashSet<ServiceId>(), true);
        var current = TestData.Snapshot(premiums: [premium]);
        context.Settings.Months[month] = current;
        for (var index = 0; index < 20; index++) context.Works.Values.Add(TestData.Work(date));
        var replacement = new SettingSnapshotReplacementDto(current.Services, current.TimeCategories,
            current.Rates, current.Premiums, current.CountBonuses);
        var calculator = new RecordingSalaryCalculator();
        var useCase = new MonthSettingsUseCase(context.Settings, context.Works, context.Holidays,
            calculator, context.Transactions, context.Metadata, context.Clock);

        var result = await useCase.PreviewReplacementAsync(month, replacement, default);

        Assert.Equal(result.CurrentCalculatedSubtotal, result.ReplacementCalculatedSubtotal);
        Assert.Equal(40, calculator.Requests.Count);
        Assert.Equal(2, calculator.Requests.Select(x => x.SettingSnapshot)
            .Distinct(ReferenceEqualityComparer.Instance).Count());
        Assert.Equal(1, context.Holidays.GetManyCalls);
        Assert.Single(context.Holidays.RequestedVersions.Distinct());
    }

    [Fact]
    public async Task PreviewReplacement_NullChild_ReturnsSafeValidationIssue()
    {
        var context = new TestContext();
        var current = TestData.Snapshot();
        var replacement = new SettingSnapshotReplacementDto(
            [null!],
            current.TimeCategories,
            current.Rates,
            current.Premiums,
            current.CountBonuses);
        var useCase = new MonthSettingsUseCase(context.Settings, context.Works, context.Holidays,
            context.Salary, context.Transactions, context.Metadata, context.Clock);

        var result = await useCase.PreviewReplacementAsync(new(2026, 8), replacement, default);

        Assert.Contains(result.Issues, issue => issue.Code == "SETTINGS_REPLACEMENT_INVALID");
        Assert.Equal(string.Empty, result.ConfirmationToken.ReplacementFingerprint);
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            useCase.PreviewReplacementAsync(new(2026, 8), null!, default));
    }

    [Fact]
    public async Task CopyPreviousMonth_UsesLatestHolidayAndKeepsOtherMonths()
    {
        var context = new TestContext();
        var july = new YearMonth(2026, 7);
        var august = new YearMonth(2026, 8);
        var september = new YearMonth(2026, 9);
        context.Settings.Months[july] = TestData.Snapshot();
        context.Settings.Months[august] = TestData.Snapshot();
        context.Settings.Months[september] = TestData.Snapshot();
        var untouched = context.Settings.Months[september].Id;
        context.Holidays.Latest = new HolidayCalendarVersionId(Guid.NewGuid());
        var useCase = new MonthSettingsUseCase(context.Settings, context.Works, context.Holidays,
            context.Salary, context.Transactions, context.Metadata, context.Clock);

        var preview = await useCase.PreviewCopyPreviousMonthAsync(august, default);
        var result = await useCase.CopyPreviousMonthAsync(august, preview.ConfirmationToken, default);

        Assert.Equal(context.Holidays.Latest, result.Snapshot.HolidayCalendarVersionId);
        Assert.Equal(untouched, context.Settings.Months[september].Id);
    }

    [Fact]
    public async Task CloneFailure_DoesNotCommitOrMarkChanged()
    {
        var context = new TestContext();
        var current = TestData.Snapshot();
        context.Settings.CloneFailure = new InvalidOperationException("db");
        var useCase = new MonthSettingsUseCase(context.Settings, context.Works, context.Holidays,
            context.Salary, context.Transactions, context.Metadata, context.Clock);
        var replacement = new SettingSnapshotReplacementDto(current.Services, current.TimeCategories,
            current.Rates, current.Premiums, current.CountBonuses);

        var preview = await useCase.PreviewReplacementAsync(new(2026, 8), replacement, default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => useCase.CloneAndReplaceAsync(new(2026, 8), replacement, preview.ConfirmationToken, default));

        Assert.Equal(0, context.Transactions.Commits);
        Assert.Null(context.Metadata.Value.LastDataChangedAtUtc);
    }

    [Fact]
    public async Task Clone_MetadataFailure_RollsBackSnapshotReferenceAndMetadata()
    {
        var context = new TestContext();
        var month = new YearMonth(2026, 8);
        var current = TestData.Snapshot();
        context.Settings.Months[month] = current;
        var replacement = new SettingSnapshotReplacementDto(current.Services, current.TimeCategories,
            [new SnapshotRate(TestData.ServiceId, TestData.CategoryId, RateType.FixedPerRecord, new YenAmount(2000))],
            current.Premiums, current.CountBonuses);
        var useCase = new MonthSettingsUseCase(context.Settings, context.Works, context.Holidays,
            context.Salary, context.Transactions, context.Metadata, context.Clock);
        var preview = await useCase.PreviewReplacementAsync(month, replacement, default);
        context.Metadata.SetLastDataChangedFailure = new IOException("metadata");

        await Assert.ThrowsAsync<IOException>(() =>
            useCase.CloneAndReplaceAsync(month, replacement, preview.ConfirmationToken, default));

        Assert.Same(current, context.Settings.Months[month]);
        Assert.Equal(1, context.Settings.CloneCalls);
        Assert.Equal(0, context.Transactions.Commits);
        Assert.Null(context.Metadata.Value.LastDataChangedAtUtc);
    }

    [Fact]
    public async Task CloneAndReplace_RejectsStalePreviewAfterWorkChange()
    {
        var context = new TestContext();
        var month = new YearMonth(2026, 8);
        var current = TestData.Snapshot();
        context.Settings.Months[month] = current;
        var replacement = new SettingSnapshotReplacementDto(current.Services, current.TimeCategories,
            current.Rates, current.Premiums, current.CountBonuses);
        var useCase = new MonthSettingsUseCase(context.Settings, context.Works, context.Holidays,
            context.Salary, context.Transactions, context.Metadata, context.Clock);
        var preview = await useCase.PreviewReplacementAsync(month, replacement, default);
        context.Works.Values.Add(TestData.Work(new(2026, 8, 1)));

        var exception = await Assert.ThrowsAsync<ApplicationErrorException>(() =>
            useCase.CloneAndReplaceAsync(month, replacement, preview.ConfirmationToken, default));

        Assert.Equal("SETTINGS_PREVIEW_STALE", exception.Code);
        Assert.Equal(0, context.Settings.CloneCalls);
    }

    [Fact]
    public async Task CloneAndReplace_RepositoryCasFailure_LeavesSettingsAndMetadataUnchanged()
    {
        var context = new TestContext();
        var month = new YearMonth(2026, 8);
        var current = TestData.Snapshot();
        context.Settings.Months[month] = current;
        var replacement = new SettingSnapshotReplacementDto(current.Services, current.TimeCategories,
            current.Rates, current.Premiums, current.CountBonuses);
        var useCase = new MonthSettingsUseCase(context.Settings, context.Works, context.Holidays,
            context.Salary, context.Transactions, context.Metadata, context.Clock);
        var preview = await useCase.PreviewReplacementAsync(month, replacement, default);
        context.Settings.ForceCasFailure = true;

        var exception = await Assert.ThrowsAsync<ApplicationErrorException>(() =>
            useCase.CloneAndReplaceAsync(month, replacement, preview.ConfirmationToken, default));

        Assert.Equal("SETTINGS_PREVIEW_STALE", exception.Code);
        Assert.Same(current, context.Settings.Months[month]);
        Assert.Null(context.Metadata.Value.LastDataChangedAtUtc);
        Assert.Equal(1, context.Settings.CloneCalls);
        Assert.Equal(0, context.Transactions.Commits);
    }

    [Fact]
    public async Task CloneAndReplace_RejectsTokenReusedForDifferentReplacementOrMonth()
    {
        var context = new TestContext();
        var month = new YearMonth(2026, 8);
        var current = TestData.Snapshot();
        context.Settings.Months[month] = current;
        var replacement = new SettingSnapshotReplacementDto(current.Services, current.TimeCategories,
            current.Rates, current.Premiums, current.CountBonuses);
        var changed = replacement with
        {
            Rates = [new SnapshotRate(TestData.ServiceId, TestData.CategoryId, RateType.FixedPerRecord, new YenAmount(9999))]
        };
        var useCase = new MonthSettingsUseCase(context.Settings, context.Works, context.Holidays,
            context.Salary, context.Transactions, context.Metadata, context.Clock);
        var preview = await useCase.PreviewReplacementAsync(month, replacement, default);

        await Assert.ThrowsAsync<ApplicationErrorException>(() =>
            useCase.CloneAndReplaceAsync(month, changed, preview.ConfirmationToken, default));
        await Assert.ThrowsAsync<ApplicationErrorException>(() =>
            useCase.CloneAndReplaceAsync(new(2026, 9), replacement, preview.ConfirmationToken, default));
    }

    [Fact]
    public async Task SalaryQuery_UsesEachWorkDatesCalendarMonthAndAddsAllowanceOnce()
    {
        var context = new TestContext();
        var july = TestData.Snapshot();
        var august = new SettingSnapshot(new SettingSnapshotId(Guid.NewGuid()), null, TestData.HolidayId,
            new SchemaVersion(1), DateTimeOffset.UnixEpoch, july.Services, july.TimeCategories,
            [new SnapshotRate(TestData.ServiceId, TestData.CategoryId, RateType.FixedPerRecord, new YenAmount(2000))], [], []);
        context.Settings.Months[new(2026, 7)] = july;
        context.Settings.Months[new(2026, 8)] = august;
        context.Works.Values.AddRange([TestData.Work(new(2026, 7, 21)), TestData.Work(new(2026, 8, 1))]);
        var key = new PayrollPeriodKey(new YearMonth(2026, 8));
        context.Closing.Values.Add(new ClosingRule(new ClosingRuleId(Guid.NewGuid()), new(new(2020, 1)), 20));
        context.Allowances.Values.Add(new MonthlyAllowance(new MonthlyAllowanceId(Guid.NewGuid()), key, "手当", new YenAmount(5000)));
        var useCase = new SalaryQueryUseCase(context.Works, context.Settings, context.Holidays, context.Closing,
            context.Allowances, context.Shifts, context.Salary, context.Periods);

        var result = await useCase.GetPayrollPeriodAsync(key, default);

        Assert.Equal(2, result.Days.Count);
        Assert.Equal(8000, result.CalculatedSubtotal.Value);
        Assert.Equal(5000, result.AllowanceSubtotal.Value);
    }

    [Fact]
    public async Task ANNUALAPP001_HomeSummaryBatchesAnnualRangeAndBuildsMonthlyFromTheSameRead()
    {
        var context = new TestContext();
        var selected = new PayrollPeriodKey(new YearMonth(2026, 8));
        var future = new PayrollPeriodKey(new YearMonth(2026, 9));
        context.Closing.Values.Add(new ClosingRule(
            new ClosingRuleId(Guid.NewGuid()),
            new PayrollPeriodKey(new YearMonth(1, 1)),
            20));
        context.Works.Values.AddRange([
            TestData.Work(new DateOnly(2026, 1, 1)),
            TestData.Work(new DateOnly(2026, 2, 1)) with { ServiceId = new ServiceId(Guid.NewGuid()) },
            TestData.Work(new DateOnly(2026, 8, 1)),
            TestData.Work(new DateOnly(2026, 9, 1)),
        ]);
        context.Allowances.Values.AddRange([
            new MonthlyAllowance(new MonthlyAllowanceId(Guid.NewGuid()),
                new PayrollPeriodKey(new YearMonth(2026, 1)), "1月手当", new YenAmount(100)),
            new MonthlyAllowance(new MonthlyAllowanceId(Guid.NewGuid()),
                selected, "8月手当", new YenAmount(200)),
            new MonthlyAllowance(new MonthlyAllowanceId(Guid.NewGuid()),
                future, "9月手当", new YenAmount(500)),
        ]);
        var useCase = new SalaryQueryUseCase(
            context.Works,
            context.Settings,
            context.Holidays,
            context.Closing,
            context.Allowances,
            context.Shifts,
            context.Salary,
            context.Periods,
            new AnnualSalaryCalculator());

        var result = await useCase.GetHomeSalarySummaryAsync(selected, default);

        Assert.Equal(new YearMonth(2026, 1), result.AnnualSummary.PeriodStart.Value);
        Assert.Equal(new YearMonth(2026, 12), result.AnnualSummary.PeriodEnd.Value);
        Assert.Equal(new YearMonth(2026, 8), result.AnnualSummary.AccumulationEnd.Value);
        Assert.Equal(2_300, result.AnnualSummary.CalculatedSubtotal.Value);
        Assert.Equal(1, result.AnnualSummary.UncalculatedCount);
        Assert.Equal(1_200, result.MonthlySummary.CalculatedSubtotal.Value);
        Assert.Equal(200, result.MonthlySummary.AllowanceSubtotal.Value);
        Assert.Single(result.MonthlySummary.Days);
        Assert.Equal(1, context.Closing.GetHistoryCalls);
        Assert.Equal(1, context.Works.StreamRangeCalls);
        Assert.Equal((new DateOnly(2025, 12, 21), new DateOnly(2026, 8, 20)),
            Assert.Single(context.Works.StreamRanges));
        Assert.Equal(1, context.Settings.EffectiveMonthsBatchCalls);
        Assert.Equal(1, context.Holidays.GetManyCalls);
        Assert.Equal(1, context.Allowances.GetForRangeCalls);
        Assert.Equal(0, context.Allowances.GetForPeriodCalls);
    }

    [Fact]
    public async Task ANNUALAPP002_HomeSummaryIncludesAllowanceOnlyPeriodsAndReturnsZeroWithoutData()
    {
        var context = new TestContext();
        var selected = new PayrollPeriodKey(new YearMonth(2026, 8));
        context.Closing.Values.Add(new ClosingRule(
            new ClosingRuleId(Guid.NewGuid()),
            new PayrollPeriodKey(new YearMonth(1, 1)),
            null));
        context.Allowances.Values.Add(new MonthlyAllowance(
            new MonthlyAllowanceId(Guid.NewGuid()),
            new PayrollPeriodKey(new YearMonth(2026, 3)),
            "3月手当",
            new YenAmount(400)));
        var useCase = new SalaryQueryUseCase(
            context.Works,
            context.Settings,
            context.Holidays,
            context.Closing,
            context.Allowances,
            context.Shifts,
            context.Salary,
            context.Periods,
            new AnnualSalaryCalculator());

        var withAllowance = await useCase.GetHomeSalarySummaryAsync(selected, default);
        context.Allowances.Values.Clear();
        var withoutData = await useCase.GetHomeSalarySummaryAsync(selected, default);

        Assert.Equal(400, withAllowance.AnnualSummary.CalculatedSubtotal.Value);
        Assert.Equal(0, withAllowance.MonthlySummary.CalculatedSubtotal.Value);
        Assert.Equal(0, withoutData.AnnualSummary.CalculatedSubtotal.Value);
        Assert.Equal(0, withoutData.AnnualSummary.UncalculatedCount);
        Assert.Empty(withoutData.MonthlySummary.Days);
    }

    [Fact]
    public async Task SalaryQuery_UsesHolidayVersionSelectedByEachCalendarMonth()
    {
        var context = new TestContext();
        var julyHoliday = new HolidayCalendarVersionId(Guid.NewGuid());
        var augustHoliday = new HolidayCalendarVersionId(Guid.NewGuid());
        var holidayPremium = new SnapshotPremium(new PremiumId(Guid.NewGuid()), "祝日加算",
            PremiumCalculationType.FixedPerRecord, null, new YenAmount(100), null, null, true,
            new HashSet<DayOfWeek>(), new HashSet<DateOnly>(), new HashSet<ServiceId>(), true);
        context.Settings.Months[new YearMonth(2026, 7)] = TestData.Snapshot(
            premiums: [holidayPremium], holidayCalendarVersionId: julyHoliday);
        context.Settings.Months[new YearMonth(2026, 8)] = TestData.Snapshot(
            premiums: [holidayPremium], holidayCalendarVersionId: augustHoliday);
        context.Holidays.Calendars[julyHoliday] = new Dictionary<DateOnly, string>
        {
            [new DateOnly(2026, 7, 21)] = "テスト祝日",
        };
        context.Holidays.Calendars[augustHoliday] = new Dictionary<DateOnly, string>();
        context.Works.Values.AddRange([
            TestData.Work(new DateOnly(2026, 7, 21)),
            TestData.Work(new DateOnly(2026, 8, 1)),
        ]);
        var key = new PayrollPeriodKey(new YearMonth(2026, 8));
        context.Closing.Values.Add(new ClosingRule(new ClosingRuleId(Guid.NewGuid()),
            new PayrollPeriodKey(new YearMonth(1, 1)), 20));
        var useCase = new SalaryQueryUseCase(context.Works, context.Settings, context.Holidays, context.Closing,
            context.Allowances, context.Shifts, context.Salary, context.Periods);

        var result = await useCase.GetPayrollPeriodAsync(key, default);

        Assert.Equal(100, result.PremiumSubtotal.Value);
        Assert.Equal(2100, result.CalculatedSubtotal.Value);
        Assert.Equal(1, context.Holidays.GetManyCalls);
        Assert.Equal(2, context.Holidays.RequestedVersions.Distinct().Count());
    }

    [Fact]
    public async Task RSP004_WorkRecordCalculationLoadsOnlyTheRequestedRecordContext()
    {
        var context = new TestContext();
        var selected = TestData.Work(new DateOnly(2026, 8, 10));
        context.Works.Values.AddRange([
            TestData.Work(new DateOnly(2026, 7, 21)),
            selected,
            TestData.Work(new DateOnly(2026, 8, 20)),
        ]);
        context.Closing.Values.Add(new ClosingRule(
            new ClosingRuleId(Guid.NewGuid()),
            new PayrollPeriodKey(new YearMonth(1, 1)),
            20));
        context.Allowances.Values.Add(new MonthlyAllowance(
            new MonthlyAllowanceId(Guid.NewGuid()),
            new PayrollPeriodKey(new YearMonth(2026, 8)),
            "交通手当",
            new YenAmount(5_000)));
        var useCase = new SalaryQueryUseCase(context.Works, context.Settings, context.Holidays, context.Closing,
            context.Allowances, context.Shifts, context.Salary, context.Periods);

        var result = await useCase.GetWorkRecordCalculationAsync(selected.Id, default);

        Assert.Equal(selected.Id, result.Record.WorkRecord.Id);
        Assert.Equal(1_000, result.Record.Calculation.Total?.Value);
        Assert.Equal(new DateOnly(2026, 7, 21), result.Period.StartDate);
        Assert.Equal(new DateOnly(2026, 8, 20), result.Period.EndDate);
        Assert.Equal(1, context.Works.FindCalls);
        Assert.Equal(0, context.Works.StreamRangeCalls);
        Assert.Equal(1, context.Settings.EffectiveMonthCalls);
        Assert.Equal(0, context.Settings.EffectiveMonthsBatchCalls);
        Assert.Equal(1, context.Holidays.GetCalls);
        Assert.Equal(0, context.Holidays.GetManyCalls);
        Assert.Equal(1, context.Closing.GetHistoryCalls);
        Assert.Equal(0, context.Allowances.GetForPeriodCalls);
    }

    [Fact]
    public async Task CalendarDayAndMonthQueries_UseOrchestratedApplicationModels()
    {
        var context = new TestContext();
        var date = new DateOnly(2026, 8, 1);
        context.Works.Values.Add(TestData.Work(date));
        context.Shifts.Values.Add(new BasicShiftDto(new BasicShiftId(Guid.NewGuid()), date.DayOfWeek, null,
            TestData.ServiceId, TestData.CategoryId, WorkInputMode.Duration, new WorkMinutes(60), null, null,
            new DisplayOrder(0), true));
        var useCase = new SalaryQueryUseCase(context.Works, context.Settings, context.Holidays, context.Closing,
            context.Allowances, context.Shifts, context.Salary, context.Periods);

        var day = await useCase.GetDayAsync(date, default);
        var month = await useCase.GetCalendarMonthAsync(new(2026, 8), default);

        Assert.Single(day.Records);
        Assert.Equal("訪問", day.Records[0].ServiceDisplayName);
        Assert.Equal("60分", day.Records[0].TimeCategoryDisplayName);
        Assert.Equal(new YearMonth(2026, 8), day.Records[0].SettingMonth);
        Assert.Equal(1000, day.CalculatedSubtotal.Value);
        Assert.Equal(31, month.Count);
        Assert.Equal(1, month[0].WorkRecordCount);
        Assert.Equal(1, month[0].BasicShiftCandidateCount);
    }

    [Fact]
    public async Task CalendarMonthScreen_ReusesMonthRangeForSelectedDaySummary()
    {
        var context = new TestContext();
        var month = new YearMonth(2026, 8);
        var selectedDate = new DateOnly(2026, 8, 21);
        context.Works.Values.AddRange([
            TestData.Work(selectedDate),
            TestData.Work(selectedDate),
            TestData.Work(selectedDate.AddDays(1)),
        ]);
        var useCase = new SalaryQueryUseCase(context.Works, context.Settings, context.Holidays, context.Closing,
            context.Allowances, context.Shifts, context.Salary, context.Periods);

        var screen = await useCase.GetCalendarMonthScreenAsync(month, selectedDate, default);

        Assert.Equal(31, screen.Days.Count);
        Assert.Equal(selectedDate, screen.SelectedDay.Date);
        Assert.Equal(2, screen.SelectedDay.Records.Count);
        Assert.Equal(2_000, screen.SelectedDay.CalculatedSubtotal.Value);
        Assert.Equal(2, screen.Days.Single(x => x.Date == selectedDate).WorkRecordCount);
        Assert.Equal(1, context.Works.StreamRangeCalls);
        Assert.Equal(1, context.Settings.EffectiveMonthsBatchCalls);
        Assert.Equal(1, context.Holidays.GetManyCalls);
        Assert.Equal(1, context.Shifts.GetForWeekdaysCalls);

        var legacyMonth = await useCase.GetCalendarMonthAsync(month, default);
        var legacyDay = await useCase.GetDayAsync(selectedDate, default);
        Assert.Equal(legacyMonth, screen.Days);
        Assert.Equal(legacyDay.CalculatedSubtotal, screen.SelectedDay.CalculatedSubtotal);
        Assert.Equal(legacyDay.Records.Select(x => x.WorkRecord.Id),
            screen.SelectedDay.Records.Select(x => x.WorkRecord.Id));
    }

    [Fact]
    public async Task DayScreen_LoadsRecordsSettingsHolidayAndShiftsOnce()
    {
        var context = new TestContext();
        var date = new DateOnly(2026, 8, 1);
        for (var index = 0; index < 20; index++) context.Works.Values.Add(TestData.Work(date));
        var shift = new BasicShiftDto(new BasicShiftId(Guid.NewGuid()), date.DayOfWeek, null,
            TestData.ServiceId, TestData.CategoryId, WorkInputMode.Duration, new WorkMinutes(60), null, null,
            new DisplayOrder(0), true);
        context.Shifts.Values.Add(shift);
        var useCase = new SalaryQueryUseCase(context.Works, context.Settings, context.Holidays, context.Closing,
            context.Allowances, context.Shifts, context.Salary, context.Periods);

        var screen = await useCase.GetDayScreenAsync(date, default);

        Assert.Equal(20, screen.DailySalary.Records.Count);
        Assert.Equal(20_000, screen.DailySalary.CalculatedSubtotal.Value);
        Assert.Equal(20, screen.BasicShiftPreview.ExistingWorkRecordCount);
        Assert.Equal(shift.Id, Assert.Single(screen.BasicShiftPreview.Candidates).Shift.Id);
        Assert.Equal(1, context.Works.StreamRangeCalls);
        Assert.Equal(1, context.Settings.EffectiveMonthCalls);
        Assert.Equal(0, context.Settings.EffectiveMonthsBatchCalls);
        Assert.Equal(1, context.Holidays.GetCalls);
        Assert.Equal(0, context.Holidays.GetManyCalls);
        Assert.Equal(1, context.Shifts.GetForWeekdayCalls);
        Assert.Equal(0, context.Shifts.GetForWeekdaysCalls);

        var legacyDay = await useCase.GetDayAsync(date, default);
        Assert.Equal(legacyDay.CalculatedSubtotal, screen.DailySalary.CalculatedSubtotal);
        Assert.Equal(legacyDay.Records.Select(x => x.WorkRecord.Id),
            screen.DailySalary.Records.Select(x => x.WorkRecord.Id));
    }

    [Fact]
    public async Task SalaryQuery_EmptyCalendarSkipsCalculationDataAndBatchesWeekdayShifts()
    {
        var context = new TestContext();
        var useCase = new SalaryQueryUseCase(context.Works, context.Settings, context.Holidays, context.Closing,
            context.Allowances, context.Shifts, context.Salary, context.Periods);

        var month = await useCase.GetCalendarMonthAsync(new YearMonth(2026, 8), default);

        Assert.Equal(31, month.Count);
        Assert.All(month, day =>
        {
            Assert.Equal(0, day.WorkRecordCount);
            Assert.Equal(0, day.CalculatedSubtotal.Value);
            Assert.Equal(0, day.UncalculatedCount);
        });
        Assert.Equal(0, context.Settings.EffectiveMonthsBatchCalls);
        Assert.Equal(0, context.Holidays.GetManyCalls);
        Assert.Equal(1, context.Shifts.GetForWeekdaysCalls);
        Assert.Equal(7, context.Shifts.RequestedWeekdays.Distinct().Count());
        Assert.Equal(0, context.Shifts.GetForWeekdayCalls);
    }

    [Fact]
    public async Task ARCH005_SalaryCalculationLeavesTheCallingSynchronizationContext()
    {
        var context = new TestContext();
        var calculator = new RecordingSalaryCalculator();
        var useCase = new SalaryQueryUseCase(context.Works, context.Settings, context.Holidays, context.Closing,
            context.Allowances, context.Shifts, calculator, context.Periods);
        var callingContext = new SynchronizationContext();
        var previous = SynchronizationContext.Current;
        Task<IReadOnlyList<CalendarDayDto>> operation;
        try
        {
            SynchronizationContext.SetSynchronizationContext(callingContext);
            operation = useCase.GetCalendarMonthAsync(new YearMonth(2026, 8), default);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        await operation;

        Assert.NotEmpty(calculator.ExecutionContexts);
        Assert.All(calculator.ExecutionContexts, observed => Assert.NotSame(callingContext, observed));
    }

    [Fact]
    public async Task SalaryQuery_TwentyRecordsReuseOneDailyCalculationSnapshot()
    {
        var context = new TestContext();
        var date = new DateOnly(2026, 8, 1);
        var premium = new SnapshotPremium(new PremiumId(Guid.NewGuid()), "月曜夜間",
            PremiumCalculationType.FixedPerHour, null, new YenAmount(100),
            new MinuteOfDay(1320), new MinuteOfDay(300), false,
            new HashSet<DayOfWeek> { DayOfWeek.Monday }, new HashSet<DateOnly>(),
            new HashSet<ServiceId>(), true);
        context.Settings.Fallback = TestData.Snapshot(premiums: [premium]);
        for (var index = 0; index < 20; index++) context.Works.Values.Add(TestData.Work(date));
        var calculator = new RecordingSalaryCalculator();
        var useCase = new SalaryQueryUseCase(context.Works, context.Settings, context.Holidays, context.Closing,
            context.Allowances, context.Shifts, calculator, context.Periods);

        var month = await useCase.GetCalendarMonthAsync(new YearMonth(2026, 8), default);

        Assert.Equal(20_000, month[0].CalculatedSubtotal.Value);
        Assert.Equal(20, calculator.Requests.Count);
        Assert.All(calculator.Requests,
            request => Assert.Same(calculator.Requests[0].SettingSnapshot, request.SettingSnapshot));
        Assert.Equal(1, context.Settings.EffectiveMonthsBatchCalls);
        Assert.Equal([new YearMonth(2026, 8)], context.Settings.EffectiveMonthRequests.Distinct());
        Assert.Equal(1, context.Holidays.GetManyCalls);
        Assert.Equal([TestData.HolidayId], context.Holidays.RequestedVersions.Distinct());
        Assert.Equal(1, context.Shifts.GetForWeekdaysCalls);
    }

    [Fact]
    public async Task SalaryQuery_SixtyOneDayPeriodUsesTwoMonthsAndHolidayVersionsOnceEach()
    {
        var context = new TestContext();
        var julyMonth = new YearMonth(2026, 7);
        var augustMonth = new YearMonth(2026, 8);
        var julyHoliday = new HolidayCalendarVersionId(Guid.NewGuid());
        var augustHoliday = new HolidayCalendarVersionId(Guid.NewGuid());
        var july = TestData.Snapshot(holidayCalendarVersionId: julyHoliday);
        var augustBase = TestData.Snapshot(serviceEnabled: false, categoryEnabled: false,
            holidayCalendarVersionId: augustHoliday);
        var august = new SettingSnapshot(augustBase.Id, augustBase.BasedOnId,
            augustBase.HolidayCalendarVersionId, augustBase.SchemaVersion, augustBase.CreatedAtUtc,
            augustBase.Services, augustBase.TimeCategories,
            [new SnapshotRate(TestData.ServiceId, TestData.CategoryId,
                RateType.FixedPerRecord, new YenAmount(2000))],
            augustBase.Premiums, augustBase.CountBonuses);
        context.Settings.Months[julyMonth] = july;
        context.Settings.Months[augustMonth] = august;
        var key = new PayrollPeriodKey(augustMonth);
        context.Closing.Values.Add(new ClosingRule(new ClosingRuleId(Guid.NewGuid()),
            new PayrollPeriodKey(new YearMonth(1, 1)), 1));
        context.Closing.Values.Add(new ClosingRule(new ClosingRuleId(Guid.NewGuid()), key, 31));
        for (var date = new DateOnly(2026, 7, 2); date <= new DateOnly(2026, 8, 31); date = date.AddDays(1))
            for (var index = 0; index < 20; index++) context.Works.Values.Add(TestData.Work(date));
        var useCase = new SalaryQueryUseCase(context.Works, context.Settings, context.Holidays, context.Closing,
            context.Allowances, context.Shifts, context.Salary, context.Periods);

        var result = await useCase.GetPayrollPeriodAsync(key, default);

        Assert.Equal(new DateOnly(2026, 7, 2), result.Period.StartDate);
        Assert.Equal(new DateOnly(2026, 8, 31), result.Period.EndDate);
        Assert.Equal(61, result.Days.Count);
        Assert.Equal(1220, result.Days.Sum(x => x.Records.Count));
        Assert.Equal(1_840_000, result.CalculatedSubtotal.Value);
        Assert.Equal(0, result.UncalculatedCount);
        Assert.Equal(1, context.Settings.EffectiveMonthsBatchCalls);
        Assert.Equal([julyMonth, augustMonth], context.Settings.EffectiveMonthRequests.Distinct().OrderBy(x => x));
        Assert.Equal(1, context.Holidays.GetManyCalls);
        Assert.Equal(2, context.Holidays.RequestedVersions.Distinct().Count());
        Assert.Contains(julyHoliday, context.Holidays.RequestedVersions);
        Assert.Contains(augustHoliday, context.Holidays.RequestedVersions);
    }

    [Fact]
    public async Task SalaryQuery_MissingRateRemainsUncalculatedWithBatchedReads()
    {
        var context = new TestContext();
        var month = new YearMonth(2026, 8);
        var current = TestData.Snapshot();
        context.Settings.Months[month] = new SettingSnapshot(current.Id, current.BasedOnId,
            current.HolidayCalendarVersionId, current.SchemaVersion, current.CreatedAtUtc,
            current.Services, current.TimeCategories, [], current.Premiums, current.CountBonuses);
        context.Works.Values.Add(TestData.Work(new DateOnly(2026, 8, 1)));
        var useCase = new SalaryQueryUseCase(context.Works, context.Settings, context.Holidays, context.Closing,
            context.Allowances, context.Shifts, context.Salary, context.Periods);

        var result = await useCase.GetCalendarMonthAsync(month, default);

        Assert.Equal(1, result[0].UncalculatedCount);
        Assert.Equal(0, result[0].CalculatedSubtotal.Value);
        Assert.Equal(1, context.Settings.EffectiveMonthsBatchCalls);
        Assert.Equal(1, context.Holidays.GetManyCalls);
    }

    [Fact]
    public async Task ClosingRuleReplacement_PreservesHistoryAndAllowanceIsPeriodScoped()
    {
        var context = new TestContext();
        var oldKey = new PayrollPeriodKey(new YearMonth(2026, 7));
        var newKey = new PayrollPeriodKey(new YearMonth(2026, 8));
        context.Closing.Values.Add(new ClosingRule(new ClosingRuleId(Guid.NewGuid()), oldKey, 20));
        var useCase = new PayrollPeriodSettingsUseCase(context.Closing, context.Allowances,
            context.Transactions, context.Metadata, context.Clock, context.Periods);

        var preview = await useCase.PreviewClosingRuleReplacementAsync(new(newKey, 15), default);

        await useCase.ReplaceClosingRuleAsync(new(newKey, 15), preview.ConfirmationToken, default);
        await useCase.SaveAllowanceAsync(new(null, newKey, "資格手当", new YenAmount(5000)), default);

        Assert.Equal(2, context.Closing.Values.Count);
        Assert.Equal(20, context.Closing.Values.Single(x => x.EffectiveFrom == oldKey).ClosingDay);
        Assert.Equal(new DateOnly(2026, 8, 15), preview.ReplacementPeriod.EndDate);
        Assert.Single(await useCase.GetAllowancesAsync(newKey, default));
        Assert.Empty(await useCase.GetAllowancesAsync(oldKey, default));
    }

    [Fact]
    public async Task MonthlyAllowancePeriodQueryReadsOnlyBoundariesAndAllowancesOnce()
    {
        var context = new TestContext();
        var key = new PayrollPeriodKey(new YearMonth(2026, 8));
        context.Closing.Values.Add(new ClosingRule(new ClosingRuleId(Guid.NewGuid()),
            new PayrollPeriodKey(new YearMonth(1000, 1)), 20));
        var allowance = new MonthlyAllowance(new MonthlyAllowanceId(Guid.NewGuid()), key,
            "資格手当", new YenAmount(5_000));
        context.Allowances.Values.Add(allowance);
        var useCase = new PayrollPeriodSettingsUseCase(context.Closing, context.Allowances,
            context.Transactions, context.Metadata, context.Clock, context.Periods);

        var result = await useCase.GetMonthlyAllowancePeriodAsync(key, default);

        Assert.Equal(new DateOnly(2026, 7, 21), result.Period.StartDate);
        Assert.Equal(new DateOnly(2026, 8, 20), result.Period.EndDate);
        Assert.Equal(allowance.Id, Assert.Single(result.Allowances).Id);
        Assert.Equal(1, context.Closing.GetHistoryCalls);
        Assert.Equal(1, context.Allowances.GetForPeriodCalls);
        Assert.Empty(context.Works.Values);
    }

    [Fact]
    public async Task FirstClosingRule_IsPersistedAsBaselineAndUsedForPeriodCalculation()
    {
        var context = new TestContext();
        var key = new PayrollPeriodKey(new YearMonth(2026, 8));
        var settingsUseCase = new PayrollPeriodSettingsUseCase(context.Closing, context.Allowances,
            context.Transactions, context.Metadata, context.Clock, context.Periods);
        var preview = await settingsUseCase.PreviewClosingRuleReplacementAsync(new(key, 20), default);
        await settingsUseCase.ReplaceClosingRuleAsync(new(key, 20), preview.ConfirmationToken, default);
        var query = new SalaryQueryUseCase(context.Works, context.Settings, context.Holidays, context.Closing,
            context.Allowances, context.Shifts, context.Salary, context.Periods);

        var result = await query.GetPayrollPeriodAsync(key, default);

        Assert.Equal(new DateOnly(2026, 7, 21), result.Period.StartDate);
        Assert.Equal(new DateOnly(2026, 8, 20), result.Period.EndDate);
        Assert.Equal(new YearMonth(1000, 1), context.Closing.Values[0].EffectiveFrom.Value);
    }

    [Fact]
    public async Task FindPayrollPeriod_UsesClosingDayBoundaryForCurrentDate()
    {
        var context = new TestContext();
        context.Closing.Values.Add(new ClosingRule(
            new ClosingRuleId(Guid.NewGuid()),
            new PayrollPeriodKey(new YearMonth(1, 1)),
            20));
        var useCase = new PayrollPeriodSettingsUseCase(context.Closing, context.Allowances,
            context.Transactions, context.Metadata, context.Clock, context.Periods);

        var onClosingDay = await useCase.FindPeriodAsync(new DateOnly(2026, 8, 20), default);
        var afterClosingDay = await useCase.FindPeriodAsync(new DateOnly(2026, 8, 21), default);

        Assert.Equal(new PayrollPeriodKey(new YearMonth(2026, 8)), onClosingDay.Key);
        Assert.Equal(new PayrollPeriodKey(new YearMonth(2026, 9)), afterClosingDay.Key);
        Assert.Equal(new DateOnly(2026, 8, 21), afterClosingDay.StartDate);
        Assert.Equal(new DateOnly(2026, 9, 20), afterClosingDay.EndDate);
    }

    [Fact]
    public async Task FindPayrollPeriod_RequiresClosingRuleHistory()
    {
        var context = new TestContext();
        var useCase = new PayrollPeriodSettingsUseCase(context.Closing, context.Allowances,
            context.Transactions, context.Metadata, context.Clock, context.Periods);

        var exception = await Assert.ThrowsAsync<ApplicationErrorException>(() =>
            useCase.FindPeriodAsync(new DateOnly(2026, 8, 21), default));

        Assert.Equal("CLOSING_RULE_REQUIRED", exception.Code);
    }

    [Fact]
    public async Task ClosingRuleCommit_RejectsStaleHistoryVersion()
    {
        var context = new TestContext();
        var key = new PayrollPeriodKey(new YearMonth(2026, 8));
        context.Closing.Values.Add(new ClosingRule(
            new ClosingRuleId(Guid.NewGuid()),
            new PayrollPeriodKey(new YearMonth(1, 1)),
            20));
        var useCase = new PayrollPeriodSettingsUseCase(context.Closing, context.Allowances,
            context.Transactions, context.Metadata, context.Clock, context.Periods);
        var preview = await useCase.PreviewClosingRuleReplacementAsync(new(key, 15), default);
        var otherPreview = await useCase.PreviewClosingRuleReplacementAsync(new(key, 10), default);
        await useCase.ReplaceClosingRuleAsync(new(key, 10), otherPreview.ConfirmationToken, default);

        var exception = await Assert.ThrowsAsync<ApplicationErrorException>(() =>
            useCase.ReplaceClosingRuleAsync(new(key, 15), preview.ConfirmationToken, default));

        Assert.Equal("CLOSING_RULE_PREVIEW_STALE", exception.Code);
    }

    [Fact]
    public async Task ClosingRuleToken_CannotBeReusedForDifferentDayWithSameHistoryVersion()
    {
        var context = new TestContext();
        var key = new PayrollPeriodKey(new YearMonth(2026, 8));
        context.Closing.Values.Add(new ClosingRule(new ClosingRuleId(Guid.NewGuid()),
            new PayrollPeriodKey(new YearMonth(1, 1)), 20));
        var useCase = new PayrollPeriodSettingsUseCase(context.Closing, context.Allowances,
            context.Transactions, context.Metadata, context.Clock, context.Periods);
        var preview = await useCase.PreviewClosingRuleReplacementAsync(new(key, 15), default);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            useCase.PreviewClosingRuleReplacementAsync(null!, default));
        var invalidDay = await Assert.ThrowsAsync<ApplicationErrorException>(() =>
            useCase.PreviewClosingRuleReplacementAsync(new(key, 0), default));

        var exception = await Assert.ThrowsAsync<ApplicationErrorException>(() =>
            useCase.ReplaceClosingRuleAsync(new(key, 10), preview.ConfirmationToken, default));

        Assert.Equal("CLOSING_DAY_INVALID", invalidDay.Code);
        Assert.Equal("CLOSING_RULE_PREVIEW_STALE", exception.Code);
        Assert.Single(context.Closing.Values);
        Assert.Equal(0, context.Closing.ReplaceCalls);
        Assert.Equal(0, context.Transactions.Commits);
        Assert.Null(context.Metadata.Value.LastDataChangedAtUtc);
    }

    [Fact]
    public async Task ClosingRuleRepositoryCasFailure_DoesNotChangeState()
    {
        var context = new TestContext();
        var key = new PayrollPeriodKey(new YearMonth(2026, 8));
        var baseline = new ClosingRule(new ClosingRuleId(Guid.NewGuid()),
            new PayrollPeriodKey(new YearMonth(1, 1)), 20);
        context.Closing.Values.Add(baseline);
        var useCase = new PayrollPeriodSettingsUseCase(context.Closing, context.Allowances,
            context.Transactions, context.Metadata, context.Clock, context.Periods);
        var preview = await useCase.PreviewClosingRuleReplacementAsync(new(key, 15), default);
        context.Closing.ForceCasFailure = true;

        await Assert.ThrowsAsync<ApplicationErrorException>(() =>
            useCase.ReplaceClosingRuleAsync(new(key, 15), preview.ConfirmationToken, default));

        Assert.Equal([baseline], context.Closing.Values);
        Assert.Equal(1, context.Closing.ReplaceCalls);
        Assert.Equal(0, context.Transactions.Commits);
        Assert.Null(context.Metadata.Value.LastDataChangedAtUtc);
    }

    [Fact]
    public async Task ClosingRule_MetadataFailure_RollsBackHistoryAndVersion()
    {
        var context = new TestContext();
        var key = new PayrollPeriodKey(new YearMonth(2026, 8));
        var baseline = new ClosingRule(new ClosingRuleId(Guid.NewGuid()),
            new PayrollPeriodKey(new YearMonth(1, 1)), 20);
        context.Closing.Values.Add(baseline);
        var useCase = new PayrollPeriodSettingsUseCase(context.Closing, context.Allowances,
            context.Transactions, context.Metadata, context.Clock, context.Periods);
        var preview = await useCase.PreviewClosingRuleReplacementAsync(new(key, 15), default);
        context.Metadata.SetLastDataChangedFailure = new IOException("metadata");

        await Assert.ThrowsAsync<IOException>(() =>
            useCase.ReplaceClosingRuleAsync(new(key, 15), preview.ConfirmationToken, default));

        Assert.Equal([baseline], context.Closing.Values);
        Assert.Equal("0", (await context.Closing.GetSnapshotAsync(default)).Version.Value);
        Assert.Equal(0, context.Transactions.Commits);
        Assert.Null(context.Metadata.Value.LastDataChangedAtUtc);
    }

    [Fact]
    public async Task SettingsClosingAndAllowanceReadDelete_PublicOperationsWork()
    {
        var context = new TestContext();
        var month = new YearMonth(2026, 8);
        var key = new PayrollPeriodKey(month);
        context.Settings.Months[month] = TestData.Snapshot();
        context.Closing.Values.Add(new ClosingRule(new ClosingRuleId(Guid.NewGuid()),
            new PayrollPeriodKey(new YearMonth(1, 1)), 20));
        var allowance = new MonthlyAllowance(new MonthlyAllowanceId(Guid.NewGuid()), key, "transport", new YenAmount(500));
        context.Allowances.Values.Add(allowance);
        var settings = new MonthSettingsUseCase(context.Settings, context.Works, context.Holidays,
            context.Salary, context.Transactions, context.Metadata, context.Clock);
        var payroll = new PayrollPeriodSettingsUseCase(context.Closing, context.Allowances,
            context.Transactions, context.Metadata, context.Clock, context.Periods);

        Assert.Equal(context.Settings.Months[month].Id, (await settings.GetAsync(month, default)).Snapshot.Id);
        Assert.Equal(20, (await payroll.GetClosingRuleAsync(key, default))!.ClosingDay);
        await payroll.DeleteAllowanceAsync(allowance.Id, default);
        Assert.Empty(await payroll.GetAllowancesAsync(key, default));
    }
    private sealed class FailingPresetRepository : IServicePresetRepository
    {
        public Task<IReadOnlyList<ServicePresetDto>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ServicePresetDto>>([]);

        public Task UpsertAsync(ServicePresetDto preset, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("preset save failed");

        public Task DeleteAsync(ServicePresetId id, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("preset delete failed");
    }
}
