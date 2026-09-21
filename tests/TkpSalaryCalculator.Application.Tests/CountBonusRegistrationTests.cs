namespace TkpSalaryCalculator.Application.Tests;

public sealed class CountBonusRegistrationTests
{
    private static readonly DateOnly Date = new(2026, 8, 1);

    private static SettingSnapshot Snapshot(int bonus = 150) => TestData.Snapshot(bonuses:
        [new SnapshotCountBonus(new CountBonusId(Guid.NewGuid()), "件数手当", new YenAmount(bonus), new HashSet<ServiceId>(), true)]);

    private static SaveWorkRecordCommand Command(bool enabled) => TestData.SaveCommand(Date) with
    {
        IsCountBonusEnabled = enabled,
    };

    [Theory]
    [InlineData(true, 1150)]
    [InlineData(false, 1000)]
    public async Task Work_PreviewSaveReopenAndNewVisit_PreserveSelection(bool enabled, int total)
    {
        var context = new TestContext();
        context.Settings.Fallback = Snapshot();
        var command = Command(enabled);
        var preview = await context.WorkUseCase().PreviewAsync(command, default);
        Assert.Empty(context.Works.Values);
        var saved = await context.WorkUseCase().SaveAsync(command, default);
        var reopened = await context.WorkUseCase().GetEditorScreenAsync(Date, saved.WorkRecord.Id, default);

        Assert.Equal(enabled, saved.WorkRecord.IsCountBonusEnabled);
        Assert.Equal(enabled, saved.Calculation.IsCountBonusEnabled);
        Assert.Equal(total, preview.Calculation!.Total!.Value.Value);
        Assert.Equal(preview.Calculation.Total, saved.Calculation.Total);
        Assert.Equal(saved.WorkRecord, reopened.ExistingRecord);
        var next = await context.WorkUseCase().SaveAsync(TestData.SaveCommand(Date), default);
        Assert.True(next.WorkRecord.IsCountBonusEnabled);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Work_EditDateAndTasks_PreviewMatchesSavedSelection(bool enabled)
    {
        var context = new TestContext();
        context.Settings.Fallback = Snapshot();
        var command = Command(false);
        var saved = await context.WorkUseCase().SaveAsync(command, default);
        var target = Date.AddMonths(1);
        context.Settings.Months[new(2026, 9)] = Snapshot(250);
        var edited = command with
        {
            Id = saved.WorkRecord.Id, WorkDate = target, IsCountBonusEnabled = enabled,
            Tasks = [command.Tasks[0] with { TimeCategoryId = null, WorkMinutes = new WorkMinutes(40) }],
        };
        var preview = await context.WorkUseCase().PreviewAsync(edited, default);
        var result = await context.WorkUseCase().SaveAsync(edited, default);

        Assert.Equal(enabled ? 1050 : 800, result.Calculation.Total!.Value.Value);
        Assert.Equal(preview.Calculation!.Total, result.Calculation.Total);
        Assert.Equal(enabled, Assert.Single(context.Works.Values).IsCountBonusEnabled);
        Assert.Equal(saved.WorkRecord.Id, result.WorkRecord.Id);
    }

    [Fact]
    public async Task Work_OffWithMissingRate_RemainsUncalculatedAndCanSave()
    {
        var context = new TestContext();
        var settings = Snapshot();
        context.Settings.Fallback = new SettingSnapshot(settings.Id, null, settings.HolidayCalendarVersionId,
            settings.SchemaVersion, settings.CreatedAtUtc, settings.Services, settings.TimeCategories, [], [], settings.CountBonuses);
        var preview = await context.WorkUseCase().PreviewAsync(Command(false), default);
        var saved = await context.WorkUseCase().SaveAsync(Command(false), default);

        Assert.True(preview.CanSave);
        Assert.Equal(SalaryCalculationStatus.Uncalculated, saved.Calculation.Status);
        Assert.Null(saved.Calculation.Total);
        Assert.NotEmpty(preview.Issues);
        Assert.False(saved.WorkRecord.IsCountBonusEnabled);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Work_RetryIsIdempotentAndChangedSelectionConflicts(bool enabled)
    {
        var context = new TestContext();
        var command = Command(enabled);
        var saved = await context.WorkUseCase().SaveAsync(command, default);
        var retry = await context.WorkUseCase().SaveAsync(command, default);
        var exception = await Assert.ThrowsAsync<ApplicationErrorException>(() => context.WorkUseCase().SaveAsync(
            command with { IsCountBonusEnabled = !enabled }, default));

        Assert.Equal("WORK_OPERATION_CONFLICT", exception.Code);
        Assert.Equal(saved.WorkRecord, retry.WorkRecord);
        Assert.Equal(saved.WorkRecord, Assert.Single(context.Works.Values));
        Assert.Equal(1, context.Works.UpsertCalls);
    }

    [Fact]
    public async Task Work_ConcurrentSelectionChangeConflicts()
    {
        var context = new TestContext();
        context.Works.UpsertGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var useCase = context.WorkUseCase();
        var command = Command(false);
        var pending = useCase.SaveAsync(command, default);
        try
        {
            var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => useCase.SaveAsync(
                command with { IsCountBonusEnabled = true }, default));
            Assert.Equal("WORK_OPERATION_CONFLICT", error.Code);
        }
        finally { context.Works.UpsertGate.SetResult(); }
        Assert.False((await pending).WorkRecord.IsCountBonusEnabled);
        Assert.Single(context.Works.Values);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Work_FailureRollsBackSelectionAndTasks_ThenRetrySucceeds(bool edit)
    {
        var context = new TestContext();
        var command = Command(true);
        if (edit)
        {
            var original = await context.WorkUseCase().SaveAsync(command, default);
            command = command with { Id = original.WorkRecord.Id };
        }
        var before = context.Works.Values.ToArray();
        var metadata = context.Metadata.Value;
        command = command with { IsCountBonusEnabled = false, Tasks = [command.Tasks[0] with { WorkMinutes = new WorkMinutes(90) }] };
        context.Metadata.SetLastDataChangedFailure = new InvalidOperationException("commit failure");
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.WorkUseCase().SaveAsync(command, default));
        Assert.Equal(before, context.Works.Values);
        Assert.Equal(metadata, context.Metadata.Value);

        context.Metadata.SetLastDataChangedFailure = null;
        var saved = await context.WorkUseCase().SaveAsync(command, default);
        Assert.False(saved.WorkRecord.IsCountBonusEnabled);
        Assert.Equal(90, saved.WorkRecord.Tasks[0].WorkMinutes.Value);
        Assert.Single(context.Works.Values);
    }

    [Fact]
    public async Task Copy_MixedSelections_UsesTargetSettingsAndNewIds()
    {
        var context = new TestContext();
        var sourceDate = Date.AddDays(-1);
        context.Settings.Months[new(2026, 7)] = Snapshot();
        context.Settings.Months[new(2026, 8)] = Snapshot(250);
        var source = new[] { TestData.Work(sourceDate), TestData.Work(sourceDate) with { IsCountBonusEnabled = false } };
        context.Works.Values.AddRange(source);
        var recording = new RecordingSalaryCalculator();
        var useCase = new WorkRecordUseCase(context.Works, context.Settings, context.Presets, context.Holidays,
            recording, context.Transactions, context.Metadata, context.Clock);
        var preview = await useCase.PreviewCopyDayAsync(sourceDate, Date, default);
        Assert.Equal([false, true], recording.Requests.Select(request => request.WorkRecord.IsCountBonusEnabled).Order());
        var copied = await useCase.CopyDayAsync(sourceDate, Date, preview.ConfirmationToken, default);

        Assert.Equal([1000, 1250], copied.Select(result => result.Calculation.Total!.Value.Value).Order());
        Assert.All(copied, result =>
        {
            var original = source.Single(work => work.Id == result.WorkRecord.SourceWorkRecordId);
            Assert.Equal(original.IsCountBonusEnabled, result.WorkRecord.IsCountBonusEnabled);
            Assert.NotEqual(original.Id, result.WorkRecord.Id);
            Assert.NotEqual(original.Tasks[0].Id, result.WorkRecord.Tasks[0].Id);
        });
    }

    [Theory]
    [InlineData("selection")]
    [InlineData("task")]
    [InlineData("added")]
    public async Task Copy_SourceChangesInvalidateConfirmation(string change)
    {
        var context = new TestContext();
        var sourceDate = Date.AddDays(-1);
        var source = TestData.Work(sourceDate);
        context.Works.Values.Add(source);
        var preview = await context.WorkUseCase().PreviewCopyDayAsync(sourceDate, Date, default);
        if (change == "selection") context.Works.Values[0] = source with { IsCountBonusEnabled = false };
        else if (change == "task") context.Works.Values[0] = source with { Tasks = [source.Tasks[0] with { WorkMinutes = new WorkMinutes(90) }] };
        else context.Works.Values.Add(TestData.Work(sourceDate));
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => context.WorkUseCase().CopyDayAsync(
            sourceDate, Date, preview.ConfirmationToken, default));
        Assert.Equal("COPY_DAY_PREVIEW_STALE", error.Code);
        Assert.DoesNotContain(context.Works.Values, work => work.WorkDate == Date);
        var refreshed = await context.WorkUseCase().PreviewCopyDayAsync(sourceDate, Date, default);
        var copied = await context.WorkUseCase().CopyDayAsync(sourceDate, Date, refreshed.ConfirmationToken, default);
        Assert.Equal(context.Works.Values.Where(work => work.WorkDate == sourceDate).Select(work => work.IsCountBonusEnabled).Order(),
            copied.Select(result => result.WorkRecord.IsCountBonusEnabled).Order());
    }

    private static BasicShiftDto Shift(int order = 0) => new(new BasicShiftId(Guid.NewGuid()), Date.DayOfWeek,
        [new BasicShiftTaskDto(new BasicShiftTaskId(Guid.NewGuid()), null, TestData.ServiceId, TestData.CategoryId,
            WorkInputMode.Duration, new WorkMinutes(60), null, null, new DisplayOrder(0))], new DisplayOrder(order), true);

    [Fact]
    public async Task Work_SavedMixedSelectionsFlowToDailyMonthlyAndAnnualSalary()
    {
        var context = new TestContext();
        context.Settings.Fallback = Snapshot();
        context.Closing.Values.Add(new ClosingRule(new ClosingRuleId(Guid.NewGuid()), new(new(2020, 1)), 20));
        var period = new PayrollPeriodKey(new(2026, 8));
        context.Allowances.Values.Add(new MonthlyAllowance(new MonthlyAllowanceId(Guid.NewGuid()), period, "手当", new YenAmount(5000)));
        await context.WorkUseCase().SaveAsync(Command(true), default);
        var off = Command(false);
        var savedOff = await context.WorkUseCase().SaveAsync(off, default);
        var salary = new SalaryQueryUseCase(context.Works, context.Settings, context.Holidays, context.Closing,
            context.Allowances, context.Shifts, context.Salary, context.Periods, context.AnnualSettings);
        var day = await salary.GetDayAsync(Date, default);
        var summary = await salary.GetHomeSalarySummaryAsync(period, default);
        Assert.Equal(2150, day.CalculatedSubtotal.Value);
        Assert.Equal(2, day.Records.Count);
        Assert.Equal(7150, summary.MonthlySummary.CalculatedSubtotal.Value);
        Assert.Equal(7150, summary.AnnualSummary.CalculatedSubtotal.Value);
        Assert.Equal(5000, summary.MonthlySummary.AllowanceSubtotal.Value);
        await context.WorkUseCase().SaveAsync(off with { Id = savedOff.WorkRecord.Id, IsCountBonusEnabled = true }, default);
        var updated = await salary.GetHomeSalarySummaryAsync(period, default);
        Assert.Equal(7300, updated.MonthlySummary.CalculatedSubtotal.Value);
        Assert.Equal(7300, updated.AnnualSummary.CalculatedSubtotal.Value);
    }

    [Fact]
    public async Task CancelledCommands_DoNotPersistSelections()
    {
        var context = new TestContext();
        var shift = Shift();
        context.Shifts.Values.Add(shift);
        var cancellation = new CancellationToken(true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => context.WorkUseCase().SaveAsync(Command(false), cancellation));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => context.ShiftUseCase().ApplyAsync(
            new(Date, [shift.Id]) { CountBonusSelections = new Dictionary<BasicShiftId, bool> { [shift.Id] = false } }, cancellation));
        Assert.Empty(context.Works.Values);
        Assert.Empty(context.Settings.Months);
        Assert.Null(context.Metadata.Value.LastDataChangedAtUtc);
    }

    [Fact]
    public async Task Shift_NewMonthPreviewAndApplyUseSameHolidayVersion()
    {
        var context = new TestContext();
        var shift = Shift();
        context.Shifts.Values.Add(shift);
        context.Holidays.Latest = new HolidayCalendarVersionId(Guid.NewGuid());
        var preview = await context.ShiftUseCase().PreviewForDateAsync(Date, default);
        Assert.Equal(context.Holidays.Latest, context.Holidays.RequestedVersions.Last());
        var result = Assert.Single(await context.ShiftUseCase().ApplyAsync(
            new(Date, [shift.Id]) { ConfirmationToken = preview.ConfirmationToken }, default));
        Assert.Equal(preview.Candidates[0].Calculation!.Total, result.Calculation.Total);
        Assert.Equal(context.Holidays.Latest, context.Settings.Months[new(2026, 8)].HolidayCalendarVersionId);
    }

    [Fact]
    public async Task Shift_MixedSelections_PreviewIsReadOnlyAndApplyMatches()
    {
        var context = new TestContext();
        context.Settings.Fallback = Snapshot();
        var first = Shift();
        var second = Shift(1);
        context.Shifts.Values.AddRange([first, second]);
        var initial = await context.ShiftUseCase().PreviewForDateAsync(Date, default);
        Assert.All(initial.Candidates, candidate => Assert.True(candidate.IsCountBonusEnabled));
        var selections = new Dictionary<BasicShiftId, bool> { [second.Id] = false };
        var preview = await context.ShiftUseCase().PreviewForDateAsync(Date, default, selections);
        Assert.Equal([1150, 1000], preview.Candidates.Select(candidate => candidate.Calculation!.Total!.Value.Value));
        Assert.Empty(context.Works.Values);
        Assert.Empty(context.Settings.Months);
        Assert.Null(context.Metadata.Value.LastDataChangedAtUtc);
        var command = new ApplyBasicShiftsCommand(Date, [first.Id, second.Id])
        { CountBonusSelections = selections, ConfirmationToken = preview.ConfirmationToken };
        var saved = await context.ShiftUseCase().ApplyAsync(command, default);
        Assert.Equal(preview.Candidates.Select(candidate => candidate.Calculation!.Total), saved.Select(result => result.Calculation.Total));
        Assert.Equal([true, false], saved.Select(result => result.WorkRecord.IsCountBonusEnabled));
        Assert.All(saved, result => Assert.DoesNotContain(context.Shifts.Values.SelectMany(shift => shift.Tasks),
            task => task.Id.Value == result.WorkRecord.Tasks[0].Id.Value));
        await Assert.ThrowsAsync<ApplicationErrorException>(() => context.ShiftUseCase().ApplyAsync(command, default));
        Assert.Equal(2, context.Works.Values.Count);
        context.Shifts.Values.Clear();
        Assert.False(context.Works.Values[1].IsCountBonusEnabled);
    }

    [Theory]
    [InlineData("selection")]
    [InlineData("shift")]
    [InlineData("settings")]
    [InlineData("existing")]
    [InlineData("missing_token")]
    public async Task Shift_ChangedConfirmationIsRejected(string change)
    {
        var context = new TestContext();
        var shift = Shift();
        context.Shifts.Values.Add(shift);
        var selections = new Dictionary<BasicShiftId, bool> { [shift.Id] = true };
        var preview = await context.ShiftUseCase().PreviewForDateAsync(Date, default, selections);
        if (change == "selection") selections[shift.Id] = false;
        if (change == "shift") context.Shifts.Values[0] = shift with { Tasks = [shift.Tasks[0] with { WorkMinutes = new WorkMinutes(90) }] };
        if (change == "settings") context.Settings.Fallback = Snapshot(250);
        if (change == "existing") context.Works.Values.Add(TestData.Work(Date));
        var command = new ApplyBasicShiftsCommand(Date, [shift.Id])
        { CountBonusSelections = selections, ConfirmationToken = change == "missing_token" ? null : preview.ConfirmationToken };
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => context.ShiftUseCase().ApplyAsync(command, default));
        Assert.Equal("SHIFT_PREVIEW_STALE", error.Code);
        Assert.DoesNotContain(context.Works.Values, work => work.SourceBasicShiftId is not null);
        Assert.Empty(context.Settings.Months);
        Assert.Null(context.Metadata.Value.LastDataChangedAtUtc);
        var refreshed = await context.ShiftUseCase().PreviewForDateAsync(Date, default, selections);
        var saved = Assert.Single(await context.ShiftUseCase().ApplyAsync(command with { ConfirmationToken = refreshed.ConfirmationToken }, default));
        Assert.Equal(selections[shift.Id], saved.WorkRecord.IsCountBonusEnabled);
        Assert.Equal(refreshed.Candidates[0].Calculation!.Total, saved.Calculation.Total);
    }

    [Fact]
    public async Task Shift_SimilarityIgnoresSelectionAndFailedBatchRollsBack()
    {
        var context = new TestContext();
        var first = Shift();
        var second = Shift(1);
        context.Shifts.Values.AddRange([first, second]);
        context.Works.Values.Add(TestData.Work(Date));
        var selections = new Dictionary<BasicShiftId, bool> { [first.Id] = false };
        var preview = await context.ShiftUseCase().PreviewForDateAsync(Date, default, selections);
        Assert.All(preview.Candidates, candidate => Assert.True(candidate.HasSimilarManualRecord));
        var command = new ApplyBasicShiftsCommand(Date, [first.Id, second.Id])
        { CountBonusSelections = selections, ConfirmationToken = preview.ConfirmationToken };
        context.Works.UpsertFailure = new InvalidOperationException("second insert");
        context.Works.UpsertFailureAtCall = 2;
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.ShiftUseCase().ApplyAsync(command, default));
        Assert.Single(context.Works.Values);
        Assert.Empty(context.Settings.Months);
        Assert.Null(context.Metadata.Value.LastDataChangedAtUtc);
        context.Works.UpsertFailure = null;
        var saved = await context.ShiftUseCase().ApplyAsync(command, default);
        Assert.Equal([false, true], saved.Select(result => result.WorkRecord.IsCountBonusEnabled));
        Assert.Equal(3, context.Works.Values.Count);
    }
}
