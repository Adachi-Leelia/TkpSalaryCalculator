namespace TkpSalaryCalculator.Application.Tests;

public sealed class SettingsAndSalaryUseCaseTests
{
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
        Assert.Equal(1000, day.CalculatedSubtotal.Value);
        Assert.Equal(31, month.Count);
        Assert.Equal(1, month[0].WorkRecordCount);
        Assert.Equal(1, month[0].BasicShiftCandidateCount);
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
        Assert.Equal(new YearMonth(1, 1), context.Closing.Values[0].EffectiveFrom.Value);
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
}
