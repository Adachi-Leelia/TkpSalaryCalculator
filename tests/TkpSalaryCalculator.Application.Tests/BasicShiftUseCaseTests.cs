namespace TkpSalaryCalculator.Application.Tests;

public sealed class BasicShiftUseCaseTests
{
    private static BasicShiftDto Shift(BasicShiftId? id = null, int order = 0, bool enabled = true)
    {
        return new(
        id ?? new BasicShiftId(Guid.NewGuid()), DayOfWeek.Saturday, null, TestData.ServiceId,
        TestData.CategoryId, WorkInputMode.Duration, new WorkMinutes(60), null, null,
        new DisplayOrder(order), enabled);
    }


    [Fact]
    public async Task GetForWeekday_ReturnsDisplayOrder()
    {
        var context = new TestContext();
        context.Shifts.Values.AddRange([Shift(order: 2), Shift(order: 0), Shift(order: 1)]);

        var result = await context.ShiftUseCase().GetForWeekdayAsync(DayOfWeek.Saturday, default);

        Assert.Equal([0, 1, 2], result.Select(x => x.DisplayOrder.Value));
    }

    [Fact]
    public async Task Preview_DoesNotPersistAndWarnsSimilarManualRecord()
    {
        var context = new TestContext();
        var shift = Shift();
        context.Shifts.Values.Add(shift);
        context.Works.Values.Add(TestData.Work(new(2026, 8, 1)));

        var result = await context.ShiftUseCase().PreviewForDateAsync(new(2026, 8, 1), default);

        Assert.Single(result.Candidates);
        Assert.True(result.Candidates[0].CanApply);
        Assert.True(result.Candidates[0].HasSimilarManualRecord);
        Assert.Equal(0, context.Works.UpsertCalls);
        Assert.Equal(0, context.Transactions.Calls);
    }

    [Fact]
    public async Task Apply_PersistsEachSelectedShiftAsIndependentWork()
    {
        var context = new TestContext();
        context.Shifts.Values.AddRange([Shift(order: 0), Shift(order: 1)]);

        var result = await context.ShiftUseCase().ApplyAsync(new(new(2026, 8, 1), [.. context.Shifts.Values.Select(x => x.Id)]), default);

        Assert.Equal(2, result.Count);
        Assert.Equal(2, result.Select(x => x.WorkRecord.Id).Distinct().Count());
        Assert.All(result, x => Assert.NotNull(x.WorkRecord.SourceBasicShiftId));
        Assert.Equal(1, context.Transactions.Commits);
    }

    [Fact]
    public async Task Apply_AlreadyAppliedShift_IsRejected()
    {
        var context = new TestContext();
        var shift = Shift();
        context.Shifts.Values.Add(shift);
        context.Works.Values.Add(TestData.Work(new(2026, 8, 1), shiftId: shift.Id));

        var exception = await Assert.ThrowsAsync<ApplicationErrorException>(() =>
            context.ShiftUseCase().ApplyAsync(new(new(2026, 8, 1), [shift.Id]), default));

        Assert.Equal("SHIFT_ALREADY_APPLIED", exception.Code);
        Assert.Equal(0, context.Transactions.Commits);
        Assert.Single(context.Works.Values);
    }

    [Fact]
    public async Task Preview_DisabledOrUnavailableShift_IsNotApplicable()
    {
        var context = new TestContext();
        context.Settings.Fallback = TestData.Snapshot(serviceEnabled: false);
        context.Shifts.Values.Add(Shift(enabled: false));

        var result = await context.ShiftUseCase().PreviewForDateAsync(new(2026, 8, 1), default);

        Assert.False(result.Candidates[0].CanApply);
        Assert.Contains(result.Candidates[0].Issues, x => x.Code == "SHIFT_DISABLED");
        Assert.Contains(result.Candidates[0].Issues, x => x.Code == "WORK_SERVICE_UNAVAILABLE");
    }

    [Fact]
    public async Task Preview_MissingRate_IsNotApplicable()
    {
        var context = new TestContext();
        var original = context.Settings.Fallback;
        context.Settings.Fallback = new SettingSnapshot(new SettingSnapshotId(Guid.NewGuid()), null,
            original.HolidayCalendarVersionId, original.SchemaVersion, DateTimeOffset.UnixEpoch,
            original.Services, original.TimeCategories, [], [], []);
        context.Shifts.Values.Add(Shift());

        var result = await context.ShiftUseCase().PreviewForDateAsync(new(2026, 8, 1), default);

        Assert.False(result.Candidates[0].CanApply);
        Assert.Contains(result.Candidates[0].Issues, x => x.Code == "SHIFT_CALCULATION_SETTINGS_REQUIRED");
    }

    [Fact]
    public async Task Apply_FailureInBatch_DoesNotCommit()
    {
        var context = new TestContext();
        context.Shifts.Values.AddRange([Shift(order: 0), Shift(order: 1)]);
        context.Works.UpsertFailure = new InvalidOperationException("db");

        await Assert.ThrowsAsync<InvalidOperationException>(() => context.ShiftUseCase().ApplyAsync(
            new(new(2026, 8, 1), [.. context.Shifts.Values.Select(x => x.Id)]), default));

        Assert.Equal(0, context.Transactions.Commits);
        Assert.Null(context.Metadata.Value.LastDataChangedAtUtc);
    }

    [Fact]
    public async Task Apply_SecondInsertFailure_RollsBackAllRecords()
    {
        var context = new TestContext();
        context.Shifts.Values.AddRange([Shift(order: 0), Shift(order: 1)]);
        context.Works.UpsertFailure = new InvalidOperationException("second insert");
        context.Works.UpsertFailureAtCall = 2;

        await Assert.ThrowsAsync<InvalidOperationException>(() => context.ShiftUseCase().ApplyAsync(
            new(new(2026, 8, 1), [.. context.Shifts.Values.Select(x => x.Id)]), default));

        Assert.Equal(2, context.Works.UpsertCalls);
        Assert.Empty(context.Works.Values);
        Assert.Equal(0, context.Transactions.Commits);
        Assert.Null(context.Metadata.Value.LastDataChangedAtUtc);
    }

    [Fact]
    public async Task Save_CreatesAndUpdatesShift_AndRejectsInvalidWeekday()
    {
        var context = new TestContext();
        var command = new SaveBasicShiftCommand(null, DayOfWeek.Saturday, null, TestData.ServiceId,
            TestData.CategoryId, WorkInputMode.Duration, new WorkMinutes(60), null, null, new DisplayOrder(0), true);

        var created = await context.ShiftUseCase().SaveAsync(command, default);
        var updated = await context.ShiftUseCase().SaveAsync(command with
        {
            Id = created.Id,
            WorkMinutes = new WorkMinutes(90),
            Weekday = DayOfWeek.Sunday
        }, default);

        Assert.Single(context.Shifts.Values);
        Assert.Equal(90, updated.WorkMinutes.Value);
        Assert.Equal(DayOfWeek.Sunday, updated.Weekday);
        await Assert.ThrowsAsync<ArgumentNullException>(() => context.ShiftUseCase().SaveAsync(null!, default));
        await Assert.ThrowsAsync<ApplicationErrorException>(() => context.ShiftUseCase().SaveAsync(
            command with { Weekday = (DayOfWeek)99 }, default));
    }

    [Fact]
    public async Task UpdatingOrDeletingShift_DoesNotModifyExistingWork()
    {
        var context = new TestContext();
        var shift = Shift();
        context.Shifts.Values.Add(shift);
        var work = TestData.Work(new(2026, 8, 1), shiftId: shift.Id);
        context.Works.Values.Add(work);
        await context.ShiftUseCase().DeleteAsync(shift.Id, default);

        Assert.Equal(work, context.Works.Values[0]);
    }
}
