namespace TkpSalaryCalculator.Application.Tests;

public sealed class BasicShiftUseCaseTests
{
    private static BasicShiftDto MultiTaskShift()
    {
        var shift = Shift();
        return shift with { Tasks = [shift.Tasks[0], shift.Tasks[0] with
        {
            Id = new BasicShiftTaskId(Guid.NewGuid()), TimeCategoryId = null,
            WorkMinutes = new WorkMinutes(45), DisplayOrder = new DisplayOrder(1),
        }] };
    }

    private static SaveBasicShiftCommand SaveCommand(BasicShiftDto shift) =>
        new(shift.Id, shift.Weekday, shift.Tasks.Select(task => new SaveBasicShiftTaskCommand(
            task.Id, task.ServicePresetId, task.ServiceId, task.TimeCategoryId, task.InputMode,
            task.WorkMinutes, task.StartTime, task.EndTime, task.DisplayOrder)).ToArray(), shift.DisplayOrder, shift.IsEnabled);

    [Fact]
    public async Task Save_MultipleTasks_NormalizesOrderAndIndependentTimes()
    {
        var context = new TestContext();
        var command = SaveCommand(MultiTaskShift());
        var first = command.Tasks[0] with { DisplayOrder = new DisplayOrder(8), StartTime = new MinuteOfDay(540) };
        var second = command.Tasks[1] with { DisplayOrder = new DisplayOrder(3), InputMode = WorkInputMode.TimeRange,
            WorkMinutes = null, StartTime = new MinuteOfDay(1380), EndTime = new MinuteOfDay(30) };

        var saved = await context.ShiftUseCase().SaveAsync(command with { Tasks = [first, second] }, default);

        Assert.Equal([second.Id, first.Id], saved.Tasks.Select(task => task.Id));
        Assert.Equal([0, 1], saved.Tasks.Select(task => task.DisplayOrder.Value));
        Assert.Equal([90, 60], saved.Tasks.Select(task => task.WorkMinutes.Value));
        Assert.Equal([1380, 540], saved.Tasks.Select(task => task.StartTime!.Value.Value));
        Assert.Equal([30, 600], saved.Tasks.Select(task => task.EndTime!.Value.Value));
    }

    [Fact]
    public async Task Save_RejectsEmptyDuplicateOrInvalidTasksWithoutWriting()
    {
        var context = new TestContext();
        var command = SaveCommand(MultiTaskShift());
        foreach (var tasks in new IReadOnlyList<SaveBasicShiftTaskCommand>[]
        {
            [], [command.Tasks[0], command.Tasks[0]],
            [command.Tasks[0], command.Tasks[1] with { DisplayOrder = new DisplayOrder(0) }],
            [command.Tasks[0] with { Id = default }],
            [command.Tasks[0], command.Tasks[1] with { WorkMinutes = null }],
        })
            await Assert.ThrowsAsync<ApplicationErrorException>(() => context.ShiftUseCase().SaveAsync(command with { Tasks = tasks }, default));
        Assert.Empty(context.Shifts.Values);
        Assert.Null(context.Metadata.Value.LastDataChangedAtUtc);
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => context.ShiftUseCase().SaveAsync(
            command with { Tasks = [command.Tasks[0], command.Tasks[1] with { ServiceId = default }] }, default));
        Assert.Equal($"Tasks[{command.Tasks[1].Id.Value:D}].ServiceId", error.Field);
    }

    [Fact]
    public async Task Apply_MultipleTasks_CountsVisitOnceAndKeepsTasksAfterSourceChanges()
    {
        var context = new TestContext();
        context.Settings.Fallback = TestData.Snapshot(bonuses: [new SnapshotCountBonus(
            new CountBonusId(Guid.NewGuid()), "訪問加算", new YenAmount(150), new HashSet<ServiceId>(), true)]);
        var shift = MultiTaskShift();
        await context.ShiftUseCase().SaveAsync(SaveCommand(shift), default);
        var date = new DateOnly(2026, 8, 1);

        var result = Assert.Single(await context.ShiftUseCase().ApplyAsync(new(date, [shift.Id]), default));

        Assert.Equal(2050, result.Calculation.Total!.Value.Value);
        Assert.Single(result.Calculation.CountBonuses);
        Assert.Equal(2, result.WorkRecord.Tasks.Count);
        Assert.Equal([60, 45], result.WorkRecord.Tasks.Select(task => task.WorkMinutes.Value));
        Assert.Equal(2, result.WorkRecord.Tasks.Select(task => task.Id).Distinct().Count());
        Assert.All(result.WorkRecord.Tasks, task => Assert.DoesNotContain(shift.Tasks, source => source.Id.Value == task.Id.Value));
        var preview = await context.ShiftUseCase().PreviewForDateAsync(date, default);
        Assert.True(Assert.Single(preview.Candidates).IsAlreadyApplied);
        await Assert.ThrowsAsync<ApplicationErrorException>(() => context.ShiftUseCase().ApplyAsync(new(date, [shift.Id]), default));
        var changed = SaveCommand(shift);
        await context.ShiftUseCase().SaveAsync(changed with { Tasks = [changed.Tasks[1] with { WorkMinutes = new WorkMinutes(120) }] }, default);
        Assert.Equal(result.WorkRecord, Assert.Single(context.Works.Values));
        await context.ShiftUseCase().DeleteAsync(shift.Id, default);
        Assert.Equal(result.WorkRecord, Assert.Single(context.Works.Values));
    }

    [Theory]
    [InlineData("reordered", true)]
    [InlineData("duplicates", true)]
    [InlineData("partial", false)]
    [InlineData("multiplicity", false)]
    [InlineData("mode", false)]
    [InlineData("end", false)]
    public async Task Preview_SimilarityComparesCompleteTaskMultiset(string variation, bool expected)
    {
        var context = new TestContext();
        var shift = MultiTaskShift();
        if (variation == "duplicates") shift = shift with { Tasks = [shift.Tasks[0], shift.Tasks[0] with
        {
            Id = shift.Tasks[1].Id, DisplayOrder = new DisplayOrder(1),
        }] };
        context.Shifts.Values.Add(shift);
        var tasks = shift.Tasks.Reverse().Select((task, index) => new WorkTaskDto(new WorkTaskId(Guid.NewGuid()),
            task.ServiceId, task.TimeCategoryId, task.InputMode, task.WorkMinutes, task.StartTime,
            task.EndTime, new DisplayOrder(index), new ServicePresetId(Guid.NewGuid()))).ToArray();
        tasks = variation switch
        {
            "partial" => [tasks[0]],
            "multiplicity" => [tasks[0], tasks[0] with { Id = tasks[1].Id, DisplayOrder = new DisplayOrder(1) }],
            "mode" => [tasks[0], tasks[1] with { InputMode = WorkInputMode.TimeRange }],
            "end" => [tasks[0], tasks[1] with { EndTime = new MinuteOfDay(600) }],
            _ => tasks,
        };
        context.Works.Values.Add(new WorkRecordDto(new WorkRecordId(Guid.NewGuid()), new(2026, 8, 1), tasks, null, null));

        var candidate = Assert.Single((await context.ShiftUseCase().PreviewForDateAsync(new(2026, 8, 1), default)).Candidates);

        Assert.Equal(expected, candidate.HasSimilarManualRecord);
        Assert.True(candidate.CanApply);
        Assert.Equal(0, context.Works.UpsertCalls);
    }

    [Theory]
    [InlineData("service", "WORK_SERVICE_UNAVAILABLE", "ServiceId")]
    [InlineData("category", "WORK_TIME_CATEGORY_UNAVAILABLE", "TimeCategoryId")]
    [InlineData("rate", "SHIFT_CALCULATION_SETTINGS_REQUIRED", "Rate")]
    [InlineData("start", "SHIFT_START_REQUIRED_FOR_PREMIUM", "StartTime")]
    public async Task Preview_SecondTaskFailureBlocksVisitAndIdentifiesTask(string variation, string code, string field)
    {
        var context = new TestContext();
        var shift = MultiTaskShift();
        var second = shift.Tasks[1];
        if (variation == "service") second = second with { ServiceId = new ServiceId(Guid.NewGuid()) };
        if (variation == "category") second = second with { TimeCategoryId = new TimeCategoryId(Guid.NewGuid()) };
        var snapshot = context.Settings.Fallback;
        if (variation == "rate") context.Settings.Fallback = new SettingSnapshot(snapshot.Id, null,
            snapshot.HolidayCalendarVersionId, snapshot.SchemaVersion, snapshot.CreatedAtUtc,
            snapshot.Services, snapshot.TimeCategories, snapshot.Rates.Where(rate => rate.TimeCategoryId is not null).ToArray(), [], []);
        if (variation == "start")
        {
            context.Settings.Fallback = TestData.Snapshot(premiums: [new SnapshotPremium(new PremiumId(Guid.NewGuid()), "夜間",
                PremiumCalculationType.FixedPerHour, null, new YenAmount(100), new MinuteOfDay(1320), new MinuteOfDay(300),
                false, new HashSet<DayOfWeek>(), new HashSet<DateOnly>(), new HashSet<ServiceId>(), true)]);
            shift = shift with { Tasks = [shift.Tasks[0] with { StartTime = new MinuteOfDay(540), EndTime = new MinuteOfDay(600) }, second] };
        }
        else shift = shift with { Tasks = [shift.Tasks[0], second] };
        context.Shifts.Values.Add(shift);

        var candidate = Assert.Single((await context.ShiftUseCase().PreviewForDateAsync(new(2026, 8, 1), default)).Candidates);

        Assert.False(candidate.CanApply);
        Assert.Contains(candidate.Issues, issue => issue.Code == code && issue.Field == $"Tasks[{second.Id.Value:D}].{field}");
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => context.ShiftUseCase().ApplyAsync(new(new(2026, 8, 1), [shift.Id]), default));
        Assert.Equal(code, error.Code);
        Assert.Equal($"Tasks[{second.Id.Value:D}].{field}", error.Field);
        Assert.Empty(context.Works.Values);
    }

    private static BasicShiftDto Shift(BasicShiftId? id = null, int order = 0, bool enabled = true)
    {
        return new(
        id ?? new BasicShiftId(Guid.NewGuid()), DayOfWeek.Saturday,
        [new BasicShiftTaskDto(new BasicShiftTaskId(Guid.NewGuid()), null, TestData.ServiceId,
        TestData.CategoryId, WorkInputMode.Duration, new WorkMinutes(60), null, null, new DisplayOrder(0))],
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
    public async Task Preview_ReusesOneCalculationSnapshotForAllShiftsOnDate()
    {
        var context = new TestContext();
        var premium = new SnapshotPremium(new PremiumId(Guid.NewGuid()), "月曜夜間",
            PremiumCalculationType.FixedPerHour, null, new YenAmount(100),
            new MinuteOfDay(1320), new MinuteOfDay(300), false,
            new HashSet<DayOfWeek> { DayOfWeek.Monday }, new HashSet<DateOnly>(),
            new HashSet<ServiceId>(), true);
        context.Settings.Fallback = TestData.Snapshot(premiums: [premium]);
        for (var index = 0; index < 20; index++) context.Shifts.Values.Add(Shift(order: index));
        var calculator = new RecordingSalaryCalculator();
        var useCase = new BasicShiftUseCase(context.Shifts, context.Works, context.Settings,
            context.Holidays, calculator, context.Transactions, context.Metadata, context.Clock);

        var result = await useCase.PreviewForDateAsync(new DateOnly(2026, 8, 1), default);

        Assert.Equal(20, result.Candidates.Count);
        Assert.Equal(20, calculator.Requests.Count);
        Assert.All(calculator.Requests,
            request => Assert.Same(calculator.Requests[0].SettingSnapshot, request.SettingSnapshot));
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
        context.Shifts.Values.AddRange([MultiTaskShift(), MultiTaskShift()]);
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
        var command = new SaveBasicShiftCommand(null, DayOfWeek.Saturday, [new SaveBasicShiftTaskCommand(new BasicShiftTaskId(Guid.NewGuid()), null, TestData.ServiceId, TestData.CategoryId, WorkInputMode.Duration, new WorkMinutes(60), null, null, new DisplayOrder(0))], new DisplayOrder(0), true);

        var created = await context.ShiftUseCase().SaveAsync(command, default);
        var updated = await context.ShiftUseCase().SaveAsync(command with
        {
            Id = created.Id,
            Tasks = [command.Tasks[0] with { WorkMinutes = new WorkMinutes(90) }],
            Weekday = DayOfWeek.Sunday
        }, default);

        Assert.Single(context.Shifts.Values);
        Assert.Equal(90, updated.Tasks[0].WorkMinutes.Value);
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
