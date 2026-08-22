namespace TkpSalaryCalculator.Application.Tests;

public sealed class WorkRecordUseCaseTests
{
    [Fact]
    public async Task Preview_TimeRange_NormalizesOverMidnight_WithoutWrites()
    {
        var context = new TestContext();
        var command = TestData.SaveCommand(new DateOnly(2026, 8, 1), mode: WorkInputMode.TimeRange,
            start: new MinuteOfDay(1410), end: new MinuteOfDay(90));

        var result = await context.WorkUseCase().PreviewAsync(command, default);

        Assert.True(result.CanSave);
        Assert.Equal(120, result.NormalizedWorkMinutes!.Value.Value);
        Assert.Empty(context.Works.Values);
        Assert.Equal(0, context.Settings.EnsureCalls);
        Assert.Equal(0, context.Transactions.Calls);
    }

    [Fact]
    public async Task Preview_DurationWithTimedPremium_RequiresStartAndDerivesEnd()
    {
        var premium = new SnapshotPremium(new PremiumId(Guid.NewGuid()), "夜間", PremiumCalculationType.FixedPerHour,
            null, new YenAmount(100), new MinuteOfDay(1320), new MinuteOfDay(300), false,
            new HashSet<DayOfWeek>(), new HashSet<DateOnly>(), new HashSet<ServiceId>(), true);
        var context = new TestContext { Settings = { Fallback = TestData.Snapshot(premiums: [premium]) } };
        var missing = await context.WorkUseCase().PreviewAsync(TestData.SaveCommand(new(2026, 8, 1)), default);
        var valid = await context.WorkUseCase().PreviewAsync(TestData.SaveCommand(new(2026, 8, 1), start: new MinuteOfDay(1410)), default);

        Assert.False(missing.CanSave);
        Assert.Contains(missing.Issues, x => x.Code == "WORK_START_REQUIRED_FOR_PREMIUM");
        Assert.True(valid.CanSave);
        Assert.Equal(30, valid.NormalizedEndTime!.Value.Value);
    }

    [Fact]
    public async Task Preview_TimedPremiumForOtherWeekday_DoesNotRequireStart()
    {
        var premium = new SnapshotPremium(new PremiumId(Guid.NewGuid()), "月曜夜間", PremiumCalculationType.FixedPerHour,
            null, new YenAmount(100), new MinuteOfDay(1320), new MinuteOfDay(300), false,
            new HashSet<DayOfWeek> { DayOfWeek.Monday }, new HashSet<DateOnly>(), new HashSet<ServiceId>(), true);
        var context = new TestContext { Settings = { Fallback = TestData.Snapshot(premiums: [premium]) } };

        var result = await context.WorkUseCase().PreviewAsync(TestData.SaveCommand(new(2026, 8, 1)), default);

        Assert.True(result.CanSave);
        Assert.DoesNotContain(result.Issues, x => x.Code == "WORK_START_REQUIRED_FOR_PREMIUM");
    }

    [Fact]
    public async Task PreviewCopyDay_DisabledTargetService_IsBlocking()
    {
        var context = new TestContext();
        context.Works.Values.Add(TestData.Work(new(2026, 7, 31)));
        context.Settings.Months[new(2026, 7)] = TestData.Snapshot();
        context.Settings.Months[new(2026, 8)] = TestData.Snapshot(serviceEnabled: false);

        var result = await context.WorkUseCase().PreviewCopyDayAsync(new(2026, 7, 31), new(2026, 8, 1), default);

        Assert.Contains(result.Issues, x => x.Code == "WORK_SERVICE_UNAVAILABLE");
        await Assert.ThrowsAsync<ApplicationErrorException>(() =>
            context.WorkUseCase().CopyDayAsync(new(2026, 7, 31), new(2026, 8, 1), result.ConfirmationToken, default));
    }

    [Fact]
    public async Task Preview_ZeroMinutes_CannotSave()
    {
        var context = new TestContext();
        var command = TestData.SaveCommand(new(2026, 8, 1), default(WorkMinutes));

        var result = await context.WorkUseCase().PreviewAsync(command, default);

        Assert.False(result.CanSave);
        Assert.Contains(result.Issues, x => x.Code == "WORK_MINUTES_OUT_OF_RANGE");
    }

    [Fact]
    public async Task Save_MissingRate_IsPersistedAsUncalculated()
    {
        var context = new TestContext();
        context.Settings.Fallback = new SettingSnapshot(new SettingSnapshotId(Guid.NewGuid()), null, TestData.HolidayId,
            new SchemaVersion(1), DateTimeOffset.UnixEpoch,
            [new SnapshotService(TestData.ServiceId, "訪問", new DisplayOrder(0), true)],
            [new SnapshotTimeCategory(TestData.CategoryId, TestData.ServiceId, "60分", new WorkMinutes(60), new DisplayOrder(0), true)],
            [], [], []);

        var result = await context.WorkUseCase().SaveAsync(TestData.SaveCommand(new(2026, 8, 1)), default);

        Assert.Equal(SalaryCalculationStatus.Uncalculated, result.Calculation.Status);
        Assert.Equal(result.WorkRecord.Id, result.Calculation.WorkRecordId);
        Assert.Single(context.Works.Values);
        Assert.Equal(1, context.Transactions.Commits);
        Assert.Equal(context.Clock.UtcNow, context.Metadata.Value.LastDataChangedAtUtc);
    }

    [Fact]
    public async Task Save_ConcurrentSameCommand_PersistsOnce()
    {
        var context = new TestContext();
        context.Works.UpsertGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var useCase = context.WorkUseCase();
        var command = TestData.SaveCommand(new(2026, 8, 1));
        var first = useCase.SaveAsync(command, default);
        var second = useCase.SaveAsync(command, default);
        context.Works.UpsertGate.SetResult();

        var results = await Task.WhenAll(first, second);

        Assert.Equal(results[0].WorkRecord.Id, results[1].WorkRecord.Id);
        Assert.Equal(1, context.Works.UpsertCalls);
        Assert.Single(context.Works.Values);
    }

    [Fact]
    public async Task Save_CancellationOfOneWaiter_DoesNotCancelSharedOperation()
    {
        var context = new TestContext();
        context.Works.UpsertGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var useCase = context.WorkUseCase();
        var command = TestData.SaveCommand(new(2026, 8, 1));
        var first = useCase.SaveAsync(command, default);
        using var cancellation = new CancellationTokenSource();
        var cancelledWaiter = useCase.SaveAsync(command, cancellation.Token);

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelledWaiter);
        context.Works.UpsertGate.SetResult();
        var result = await first;

        Assert.Equal(result.WorkRecord.Id, context.Works.Values.Single().Id);
        Assert.Equal(1, context.Works.UpsertCalls);
    }

    [Fact]
    public async Task Save_SameOperationAcrossUseCaseInstances_ReturnsPersistedResult()
    {
        var context = new TestContext();
        var command = TestData.SaveCommand(new(2026, 8, 1));

        var first = await context.WorkUseCase().SaveAsync(command, default);
        var second = await context.WorkUseCase().SaveAsync(command, default);

        Assert.Equal(first.WorkRecord.Id, second.WorkRecord.Id);
        Assert.Equal(1, context.Works.UpsertCalls);
        Assert.Single(context.Works.Values);
    }

    [Fact]
    public async Task Save_RepositoryFailure_DoesNotCommitOrMarkChanged()
    {
        var context = new TestContext();
        context.Works.UpsertFailure = new InvalidOperationException("db");

        await Assert.ThrowsAsync<InvalidOperationException>(() => context.WorkUseCase().SaveAsync(TestData.SaveCommand(new(2026, 8, 1)), default));

        Assert.Equal(0, context.Transactions.Commits);
        Assert.Null(context.Metadata.Value.LastDataChangedAtUtc);
    }

    [Fact]
    public async Task CopyDay_SecondInsertFailure_RollsBackEntireUnitOfWork()
    {
        var context = new TestContext();
        var sourceDate = new DateOnly(2026, 8, 1);
        context.Works.Values.AddRange([TestData.Work(sourceDate), TestData.Work(sourceDate)]);
        var before = context.Works.Values.ToArray();
        context.Works.UpsertFailure = new InvalidOperationException("second insert");
        context.Works.UpsertFailureAtCall = 2;

        var targetDate = sourceDate.AddDays(1);
        var preview = await context.WorkUseCase().PreviewCopyDayAsync(sourceDate, targetDate, default);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            context.WorkUseCase().CopyDayAsync(sourceDate, targetDate, preview.ConfirmationToken, default));

        Assert.Equal(2, context.Works.UpsertCalls);
        Assert.Equal(before, context.Works.Values);
        Assert.Equal(0, context.Transactions.Commits);
        Assert.Null(context.Metadata.Value.LastDataChangedAtUtc);
    }

    [Fact]
    public async Task CopyDay_CreatesIndependentIdsAndUsesTargetMonth()
    {
        var context = new TestContext();
        var sourceDate = new DateOnly(2026, 7, 31);
        var targetDate = new DateOnly(2026, 8, 1);
        context.Works.Values.AddRange([TestData.Work(sourceDate), TestData.Work(sourceDate)]);

        var preview = await context.WorkUseCase().PreviewCopyDayAsync(sourceDate, targetDate, default);
        var result = await context.WorkUseCase().CopyDayAsync(sourceDate, targetDate, preview.ConfirmationToken, default);

        Assert.Equal(2, result.Count);
        Assert.Equal(2, result.Select(x => x.WorkRecord.Id).Distinct().Count());
        Assert.All(result, x => Assert.Equal(targetDate, x.WorkRecord.WorkDate));
        Assert.All(result, x => Assert.NotNull(x.WorkRecord.SourceWorkRecordId));
        Assert.Equal(0, context.Settings.EnsureCalls);
        Assert.Equal(1, context.Settings.TryEnsureCalls);
    }

    [Fact]
    public async Task PreviewCopyDay_UsesLatestHolidayForNewMonthWithoutCreatingIt()
    {
        var context = new TestContext();
        var sourceDate = new DateOnly(2026, 7, 31);
        var targetDate = new DateOnly(2026, 8, 1);
        var latestHoliday = new HolidayCalendarVersionId(Guid.NewGuid());
        var premium = new SnapshotPremium(new PremiumId(Guid.NewGuid()), "祝日夜間", PremiumCalculationType.FixedPerHour,
            null, new YenAmount(100), new MinuteOfDay(1320), new MinuteOfDay(300), true,
            new HashSet<DayOfWeek>(), new HashSet<DateOnly>(), new HashSet<ServiceId>(), true);
        context.Settings.Months[new YearMonth(2026, 7)] = TestData.Snapshot(premiums: [premium]);
        context.Holidays.Latest = latestHoliday;
        context.Holidays.Calendars[latestHoliday] = new Dictionary<DateOnly, string> { [targetDate] = "テスト祝日" };
        context.Works.Values.Add(TestData.Work(sourceDate));

        var preview = await context.WorkUseCase().PreviewCopyDayAsync(sourceDate, targetDate, default);

        Assert.Contains(preview.Issues, issue => issue.Code == "COPY_DAY_START_REQUIRED_FOR_PREMIUM");
        Assert.Equal(latestHoliday, preview.ConfirmationToken.ExpectedHolidayCalendarVersionId);
        Assert.False(context.Settings.Months.ContainsKey(new YearMonth(2026, 8)));
        Assert.Equal(0, context.Settings.EnsureCalls);

        var exception = await Assert.ThrowsAsync<ApplicationErrorException>(() =>
            context.WorkUseCase().CopyDayAsync(sourceDate, targetDate, preview.ConfirmationToken, default));

        Assert.Equal("COPY_DAY_START_REQUIRED_FOR_PREMIUM", exception.Code);
        Assert.Equal(1, context.Settings.TryEnsureCalls);
        Assert.DoesNotContain(context.Works.Values, work => work.WorkDate == targetDate);
    }

    [Fact]
    public async Task CopyDay_RejectsFutureSourceDateInPreviewAndOnCommit()
    {
        var context = new TestContext();
        var targetDate = new DateOnly(2026, 8, 1);
        var sourceDate = targetDate.AddDays(1);
        context.Works.Values.Add(TestData.Work(sourceDate));

        var preview = await context.WorkUseCase().PreviewCopyDayAsync(sourceDate, targetDate, default);

        Assert.Contains(preview.Issues, issue => issue.Code == "COPY_DAY_SOURCE_MUST_BE_PAST");
        var exception = await Assert.ThrowsAsync<ApplicationErrorException>(() =>
            context.WorkUseCase().CopyDayAsync(sourceDate, targetDate, preview.ConfirmationToken, default));
        Assert.Equal("COPY_DAY_SOURCE_MUST_BE_PAST", exception.Code);
        Assert.DoesNotContain(context.Works.Values, work => work.WorkDate == targetDate);
    }

    [Fact]
    public async Task CopyDay_RejectsPreviewWhenTargetSettingsChange()
    {
        var context = new TestContext();
        var sourceDate = new DateOnly(2026, 7, 31);
        var targetDate = new DateOnly(2026, 8, 1);
        context.Works.Values.Add(TestData.Work(sourceDate));

        var preview = await context.WorkUseCase().PreviewCopyDayAsync(sourceDate, targetDate, default);
        context.Settings.Months[new YearMonth(2026, 8)] = TestData.Snapshot();

        var exception = await Assert.ThrowsAsync<ApplicationErrorException>(() =>
            context.WorkUseCase().CopyDayAsync(sourceDate, targetDate, preview.ConfirmationToken, default));

        Assert.Equal("COPY_DAY_PREVIEW_STALE", exception.Code);
        Assert.DoesNotContain(context.Works.Values, work => work.WorkDate == targetDate);
    }

    [Fact]
    public async Task CopyDay_RejectsPreviewWhenTargetWorkRecordsChange()
    {
        var context = new TestContext();
        var sourceDate = new DateOnly(2026, 7, 31);
        var targetDate = new DateOnly(2026, 8, 1);
        context.Works.Values.Add(TestData.Work(sourceDate));

        var preview = await context.WorkUseCase().PreviewCopyDayAsync(sourceDate, targetDate, default);
        context.Works.Values.Add(TestData.Work(targetDate));

        var exception = await Assert.ThrowsAsync<ApplicationErrorException>(() =>
            context.WorkUseCase().CopyDayAsync(sourceDate, targetDate, preview.ConfirmationToken, default));

        Assert.Equal("COPY_DAY_PREVIEW_STALE", exception.Code);
        Assert.Single(context.Works.Values, work => work.WorkDate == targetDate);
    }

    [Fact]
    public async Task Save_EditPreservesShiftAndCopyProvenance()
    {
        var context = new TestContext();
        var shiftId = new BasicShiftId(Guid.NewGuid());
        var sourceId = new WorkRecordId(Guid.NewGuid());
        var existing = TestData.Work(new(2026, 7, 31), shiftId: shiftId) with { SourceWorkRecordId = sourceId };
        context.Works.Values.Add(existing);
        var command = TestData.SaveCommand(new(2026, 8, 1)) with { Id = existing.Id };

        var result = await context.WorkUseCase().SaveAsync(command, default);

        Assert.Equal(shiftId, result.WorkRecord.SourceBasicShiftId);
        Assert.Equal(sourceId, result.WorkRecord.SourceWorkRecordId);
        Assert.Equal(new DateOnly(2026, 8, 1), result.WorkRecord.WorkDate);
    }

    [Fact]
    public async Task Save_ReusedOperationIdWithDifferentInput_IsRejectedPendingAndPersisted()
    {
        var context = new TestContext();
        context.Works.UpsertGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var useCase = context.WorkUseCase();
        var original = TestData.SaveCommand(new(2026, 8, 1));
        var changed = original with { WorkDate = new DateOnly(2026, 8, 2) };
        var pending = useCase.SaveAsync(original, default);

        var pendingConflict = await Assert.ThrowsAsync<ApplicationErrorException>(() => useCase.SaveAsync(changed, default));
        context.Works.UpsertGate.SetResult();
        await pending;
        var persistedConflict = await Assert.ThrowsAsync<ApplicationErrorException>(() =>
            context.WorkUseCase().SaveAsync(changed, default));

        Assert.Equal("WORK_OPERATION_CONFLICT", pendingConflict.Code);
        Assert.Equal("WORK_OPERATION_CONFLICT", persistedConflict.Code);
        Assert.Single(context.Works.Values);
        Assert.Equal(1, context.Works.UpsertCalls);
    }

    [Fact]
    public async Task GetForDateAndDelete_ReflectStoredRecords()
    {
        var context = new TestContext();
        var date = new DateOnly(2026, 8, 1);
        var work = TestData.Work(date);
        context.Works.Values.Add(work);

        Assert.Single(await context.WorkUseCase().GetForDateAsync(date, default));
        await context.WorkUseCase().DeleteAsync(work.Id, default);

        Assert.Empty(context.Works.Values);
        Assert.Equal(1, context.Transactions.Commits);
        Assert.NotNull(context.Metadata.Value.LastDataChangedAtUtc);
    }

    [Fact]
    public async Task Preview_RejectsInvalidModeAndOverTwentyFourHours_AndHonorsCancellation()
    {
        var context = new TestContext();
        var invalidMode = TestData.SaveCommand(new(2026, 8, 1)) with { InputMode = (WorkInputMode)999 };
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.False((await context.WorkUseCase().PreviewAsync(invalidMode, default)).CanSave);
        await Assert.ThrowsAsync<ArgumentNullException>(() => context.WorkUseCase().PreviewAsync(null!, default));
        Assert.True((await context.WorkUseCase().PreviewAsync(
            TestData.SaveCommand(new(2026, 8, 1), new WorkMinutes(1440)), default)).CanSave);
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorkMinutes(1441));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            context.WorkUseCase().GetForDateAsync(new(2026, 8, 1), cancellation.Token));
    }

    [Fact]
    public async Task GetInputOptions_OrdersRecentThenFrequentAndCopiesPresetValues()
    {
        var context = new TestContext();
        var frequent = new ServicePresetDto(new ServicePresetId(Guid.NewGuid()), "頻繁", TestData.ServiceId,
            TestData.CategoryId, new WorkMinutes(60), new DisplayOrder(1), true);
        var recent = frequent with { Id = new ServicePresetId(Guid.NewGuid()), DisplayName = "直近", DisplayOrder = new DisplayOrder(2) };
        context.Presets.Values.AddRange([frequent, recent]);
        context.Works.Values.AddRange([
            TestData.Work(new(2026, 7, 1)) with { SourceServicePresetId = frequent.Id },
            TestData.Work(new(2026, 7, 2)) with { SourceServicePresetId = frequent.Id },
            TestData.Work(new(2026, 7, 3)) with { SourceServicePresetId = recent.Id }]);

        var result = await context.WorkUseCase().GetInputOptionsAsync(new(2026, 8, 1), default);

        Assert.Equal(recent.Id, result.PresetCandidates[0].Preset.Id);
        Assert.NotNull(result.SuggestedValues);
        Assert.Equal(new DateOnly(2026, 8, 1), result.SuggestedValues!.WorkDate);
    }
}
