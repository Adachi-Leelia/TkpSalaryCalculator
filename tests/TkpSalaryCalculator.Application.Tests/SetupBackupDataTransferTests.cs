namespace TkpSalaryCalculator.Application.Tests;

public sealed class SetupBackupDataTransferTests
{
    [Fact]
    public async Task InitialSetup_ResumesAndCompletesOnlyWithRequiredSettings()
    {
        var context = new TestContext();
        var snapshot = context.Settings.Fallback;
        context.Metadata.Value = context.Metadata.Value with { InitialSnapshotId = snapshot.Id };
        context.Closing.Values.Add(new ClosingRule(new ClosingRuleId(Guid.NewGuid()), new(new(2020, 1)), 20));
        var useCase = new InitialSetupUseCase(context.Metadata, context.Settings, context.Closing, context.Transactions);

        await useCase.SaveProgressAsync("rates", default);
        var resumed = await useCase.GetStateAsync(default);
        var completed = await useCase.CompleteAsync(default);

        Assert.Equal(InitialSetupStatus.InProgress, resumed.Status);
        Assert.Equal("rates", resumed.Step);
        Assert.Equal(InitialSetupStatus.Completed, completed.Status);
        Assert.Null(completed.Step);
    }

    [Fact]
    public async Task InitialSetup_MissingClosingRule_DoesNotComplete()
    {
        var context = new TestContext();
        context.Metadata.Value = context.Metadata.Value with { InitialSnapshotId = context.Settings.Fallback.Id };
        var useCase = new InitialSetupUseCase(context.Metadata, context.Settings, context.Closing, context.Transactions);

        var result = await useCase.CompleteAsync(default);

        Assert.Equal(InitialSetupStatus.InProgress, result.Status);
        Assert.Contains(result.Issues, x => x.Code == "SETUP_CLOSING_RULE_REQUIRED");
    }

    [Fact]
    public async Task InitialSetup_EnabledServiceWithoutApplicableRate_DoesNotComplete()
    {
        var context = new TestContext();
        var original = context.Settings.Fallback;
        context.Settings.Fallback = new SettingSnapshot(new SettingSnapshotId(Guid.NewGuid()), null,
            original.HolidayCalendarVersionId, original.SchemaVersion, DateTimeOffset.UnixEpoch,
            original.Services, original.TimeCategories, [], [], []);
        context.Metadata.Value = context.Metadata.Value with { InitialSnapshotId = context.Settings.Fallback.Id };
        context.Closing.Values.Add(new ClosingRule(new ClosingRuleId(Guid.NewGuid()), new(new(2020, 1)), 20));
        var useCase = new InitialSetupUseCase(context.Metadata, context.Settings, context.Closing, context.Transactions);

        var result = await useCase.CompleteAsync(default);

        Assert.Equal(InitialSetupStatus.InProgress, result.Status);
        Assert.Contains(result.Issues, x => x.Code == "SETUP_CALCULATION_SETTINGS_REQUIRED");
    }

    [Fact]
    public async Task InitialSetup_RejectsBlankAndLongStep_AndHonorsCancellation()
    {
        var context = new TestContext();
        var useCase = new InitialSetupUseCase(context.Metadata, context.Settings, context.Closing, context.Transactions);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<ArgumentException>(() => useCase.SaveProgressAsync(" ", default));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => useCase.SaveProgressAsync(new string('x', 101), default));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => useCase.GetStateAsync(cancellation.Token));
        Assert.Equal(0, context.Transactions.Commits);
    }

    [Fact]
    public async Task ServicePreset_AllOperationsAndValidation_AreOrchestrated()
    {
        var context = new TestContext();
        var useCase = new ServicePresetUseCase(context.Presets, context.Transactions, context.Metadata, context.Clock);
        var command = new SaveServicePresetCommand(null, "  standard  ", TestData.ServiceId, TestData.CategoryId,
            new WorkMinutes(60), new DisplayOrder(1), true);

        var created = await useCase.SaveAsync(command, default);
        var updated = await useCase.SaveAsync(command with { Id = created.Id, DisplayName = "updated" }, default);
        Assert.Equal("updated", (await useCase.GetAllAsync(default)).Single().DisplayName);
        await useCase.DeleteAsync(updated.Id, default);

        Assert.Empty(await useCase.GetAllAsync(default));
        await Assert.ThrowsAsync<ArgumentNullException>(() => useCase.SaveAsync(null!, default));
        await Assert.ThrowsAsync<ApplicationErrorException>(() => useCase.SaveAsync(command with { DisplayName = " " }, default));
        await Assert.ThrowsAsync<ApplicationErrorException>(() => useCase.SaveAsync(command with { DefaultWorkMinutes = default }, default));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorkMinutes(1441));
    }

    [Fact]
    public async Task BackupReminder_ShowsForNeverExportedAndDefersSevenDays()
    {
        var context = new TestContext();
        context.Works.Values.Add(TestData.Work(new(2026, 8, 1)));
        var useCase = new BackupReminderUseCase(context.Metadata, context.Works, context.Transactions,
            new FakeLocalDateConverter(TimeSpan.FromHours(9)));

        var before = await useCase.GetStateAsync(new(2026, 8, 15), default);
        var deferred = await useCase.DeferForSevenDaysAsync(new(2026, 8, 15), default);
        var after = await useCase.GetStateAsync(new(2026, 8, 22), default);

        Assert.True(before.ShouldShow);
        Assert.False(deferred.ShouldShow);
        Assert.True(after.ShouldShow);
    }

    [Fact]
    public async Task BackupReminder_ChangedAfterExport_WaitsThirtyDays()
    {
        var context = new TestContext();
        context.Works.Values.Add(TestData.Work(new(2026, 8, 1)));
        context.Metadata.Value = context.Metadata.Value with
        {
            LastExportedAtUtc = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
            LastDataChangedAtUtc = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero)
        };
        var useCase = new BackupReminderUseCase(context.Metadata, context.Works, context.Transactions,
            new FakeLocalDateConverter(TimeSpan.FromHours(9)));

        Assert.False((await useCase.GetStateAsync(new(2026, 7, 30), default)).ShouldShow);
        Assert.True((await useCase.GetStateAsync(new(2026, 7, 31), default)).ShouldShow);
    }

    [Fact]
    public async Task BackupReminder_UsesLocalDateAtUtcDayBoundary()
    {
        var context = new TestContext();
        context.Works.Values.Add(TestData.Work(new(2026, 8, 1)));
        context.Metadata.Value = context.Metadata.Value with
        {
            LastExportedAtUtc = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
            LastDataChangedAtUtc = new DateTimeOffset(2026, 7, 1, 16, 0, 0, TimeSpan.Zero)
        };
        var useCase = new BackupReminderUseCase(context.Metadata, context.Works, context.Transactions,
            new FakeLocalDateConverter(TimeSpan.FromHours(9)));

        Assert.False((await useCase.GetStateAsync(new(2026, 7, 31), default)).ShouldShow);
        Assert.True((await useCase.GetStateAsync(new(2026, 8, 1), default)).ShouldShow);
    }

    [Fact]
    public async Task DataTransfer_PrepareCommit_UsesStagingAndAtomicReplacement()
    {
        var export = new FakeExportStream();
        var import = new FakeImportStream();
        import.Values.Add(new DataTransferRecord<string>(DataTransferSection.Metadata, 0, "metadata"));
        var source = new FakeExportDataSource();
        var staging = new FakeStagingRepository();
        var metadata = new FakeMetadataRepository();
        var transactions = new FakeTransactionRunner();
        transactions.Register(staging, metadata);
        var useCase = new DataTransferUseCase(export, import, source, staging, metadata, transactions,
            new FakeClock(new(2026, 8, 15, 0, 0, 0, TimeSpan.Zero)));

        var preview = await useCase.PrepareImportAsync(new MemoryStream([1]), default);
        await useCase.CommitImportAsync(preview.Id, default);

        Assert.Single(staging.LiveData);
        Assert.Equal(FakeStagingState.Discarded, staging.GetState(preview.Id));
        Assert.Equal(1, staging.ReplaceCalls);
        Assert.Equal(1, transactions.Commits);
        Assert.Equal(1, staging.DiscardCalls);
    }

    [Fact]
    public async Task DataTransfer_InvalidInput_DiscardsStagingAndDoesNotReplace()
    {
        var import = new FakeImportStream { Failure = new InvalidDataException("json") };
        var staging = new FakeStagingRepository();
        var useCase = new DataTransferUseCase(new FakeExportStream(), import, new FakeExportDataSource(), staging,
            new FakeMetadataRepository(), new FakeTransactionRunner(), new FakeClock(DateTimeOffset.UnixEpoch));

        var exception = await Assert.ThrowsAsync<ApplicationErrorException>(() => useCase.PrepareImportAsync(new MemoryStream([1]), default));

        Assert.Equal("IMPORT_INVALID", exception.Code);
        Assert.Equal(1, staging.DiscardCalls);
        Assert.Equal(0, staging.ReplaceCalls);
    }

    [Fact]
    public async Task DataTransfer_CleanupFailure_DoesNotReplaceOriginalFailureOrCommittedSuccess()
    {
        var invalidImport = new FakeImportStream { Failure = new InvalidDataException("original") };
        var failedStaging = new FakeStagingRepository { DiscardFailure = new IOException("cleanup") };
        var failedUseCase = new DataTransferUseCase(new FakeExportStream(), invalidImport, new FakeExportDataSource(), failedStaging,
            new FakeMetadataRepository(), new FakeTransactionRunner(), new FakeClock(DateTimeOffset.UnixEpoch));
        var failure = await Assert.ThrowsAsync<ApplicationErrorException>(() => failedUseCase.PrepareImportAsync(new MemoryStream([1]), default));
        Assert.IsType<InvalidDataException>(failure.InnerException);

        var validImport = new FakeImportStream();
        validImport.Values.Add(new DataTransferRecord<string>(DataTransferSection.Metadata, 0, "metadata"));
        var committedStaging = new FakeStagingRepository { DiscardFailure = new IOException("cleanup") };
        var committedUseCase = new DataTransferUseCase(new FakeExportStream(), validImport, new FakeExportDataSource(), committedStaging,
            new FakeMetadataRepository(), new FakeTransactionRunner(), new FakeClock(DateTimeOffset.UnixEpoch));
        var preview = await committedUseCase.PrepareImportAsync(new MemoryStream([1]), default);
        await committedUseCase.CommitImportAsync(preview.Id, default);
        Assert.Equal(1, committedStaging.ReplaceCalls);
        Assert.Equal(FakeStagingState.Consumed, committedStaging.GetState(preview.Id));
        Assert.Single(committedStaging.LiveData);
    }

    [Fact]
    public async Task DataTransfer_DiscardAndDoubleCommit_PreserveLiveDataAndEnforceTokenState()
    {
        var import = new FakeImportStream();
        import.Values.Add(new DataTransferRecord<string>(DataTransferSection.Metadata, 0, "new"));
        var staging = new FakeStagingRepository();
        staging.LiveData.Add(new DataTransferRecord<string>(DataTransferSection.Metadata, 0, "old"));
        var metadata = new FakeMetadataRepository();
        var transactions = new FakeTransactionRunner();
        transactions.Register(staging, metadata);
        var useCase = new DataTransferUseCase(new FakeExportStream(), import, new FakeExportDataSource(), staging,
            metadata, transactions, new FakeClock(DateTimeOffset.UnixEpoch));

        var discarded = await useCase.PrepareImportAsync(new MemoryStream([1]), default);
        Assert.Equal(FakeStagingState.Validated, staging.GetState(discarded.Id));
        await useCase.DiscardImportAsync(discarded.Id, default);
        Assert.Equal(FakeStagingState.Discarded, staging.GetState(discarded.Id));
        Assert.Equal("old", Assert.IsType<DataTransferRecord<string>>(staging.LiveData.Single()).Value);

        staging.Id = new PreparedImportId(Guid.NewGuid());
        var committed = await useCase.PrepareImportAsync(new MemoryStream([1]), default);
        await useCase.CommitImportAsync(committed.Id, default);
        var exception = await Assert.ThrowsAsync<ApplicationErrorException>(() => useCase.CommitImportAsync(committed.Id, default));
        Assert.Equal("IMPORT_NOT_PREPARED", exception.Code);
        Assert.Equal("new", Assert.IsType<DataTransferRecord<string>>(staging.LiveData.Single()).Value);
    }

    [Fact]
    public async Task DataTransfer_ReplacementFailure_RollsBackLiveDataAndKeepsValidatedStage()
    {
        var import = new FakeImportStream();
        import.Values.Add(new DataTransferRecord<string>(DataTransferSection.Metadata, 0, "new"));
        var staging = new FakeStagingRepository();
        staging.LiveData.Add(new DataTransferRecord<string>(DataTransferSection.Metadata, 0, "old"));
        var metadata = new FakeMetadataRepository();
        var transactions = new FakeTransactionRunner();
        transactions.Register(staging, metadata);
        var useCase = new DataTransferUseCase(new FakeExportStream(), import, new FakeExportDataSource(), staging,
            metadata, transactions, new FakeClock(DateTimeOffset.UnixEpoch));
        var preview = await useCase.PrepareImportAsync(new MemoryStream([1]), default);
        staging.ConsumeFailureAfterReplacement = new IOException("replace");

        await Assert.ThrowsAsync<IOException>(() => useCase.CommitImportAsync(preview.Id, default));

        Assert.Equal("old", Assert.IsType<DataTransferRecord<string>>(staging.LiveData.Single()).Value);
        Assert.Equal(FakeStagingState.Validated, staging.GetState(preview.Id));
        Assert.Equal(0, transactions.Commits);
        Assert.Equal(0, staging.DiscardCalls);
    }

    [Fact]
    public async Task DataTransfer_CancelledPrepareDeletesTemporaryData_AndNextPrepareCleansAbandoned()
    {
        var import = new FakeImportStream();
        import.Values.AddRange([
            new DataTransferRecord<string>(DataTransferSection.Metadata, 0, "one"),
            new DataTransferRecord<string>(DataTransferSection.Metadata, 1, "two")]);
        using var cancellation = new CancellationTokenSource();
        import.AfterFirstRecord = cancellation.Cancel;
        var staging = new FakeStagingRepository();
        var useCase = new DataTransferUseCase(new FakeExportStream(), import, new FakeExportDataSource(), staging,
            new FakeMetadataRepository(), new FakeTransactionRunner(), new FakeClock(DateTimeOffset.UnixEpoch));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            useCase.PrepareImportAsync(new MemoryStream([1]), cancellation.Token));
        Assert.Equal(FakeStagingState.Discarded, staging.GetState(staging.Id));
        Assert.Empty(staging.Values);

        var abandonedId = new PreparedImportId(Guid.NewGuid());
        staging.Id = abandonedId;
        await staging.CreateAsync(default);
        await staging.AppendBatchAsync(abandonedId,
            [new DataTransferRecord<string>(DataTransferSection.Metadata, 0, "abandoned")], default);
        staging.Id = new PreparedImportId(Guid.NewGuid());
        import.AfterFirstRecord = null;
        await useCase.PrepareImportAsync(new MemoryStream([1]), default);
        Assert.Equal(FakeStagingState.Discarded, staging.GetState(abandonedId));
        Assert.True(staging.AbandonedCalls >= 2);
    }

    [Fact]
    public async Task Export_StreamsRecordsAndOnlyUpdatesExportTimestamp()
    {
        var writer = new FakeExportStream();
        var source = new FakeExportDataSource();
        source.Values.Add(new DataTransferRecord<string>(DataTransferSection.Metadata, 0, "metadata"));
        var metadata = new FakeMetadataRepository();
        var clock = new FakeClock(new(2026, 8, 15, 0, 0, 0, TimeSpan.Zero));
        var useCase = new DataTransferUseCase(writer, new FakeImportStream(), source, new FakeStagingRepository(),
            metadata, new FakeTransactionRunner(), clock);

        await useCase.ExportAsync(new MemoryStream(), "1.0", default);

        Assert.Equal(1, writer.Count);
        Assert.Equal(clock.UtcNow, metadata.Value.LastExportedAtUtc);
        Assert.Null(metadata.Value.LastDataChangedAtUtc);
    }

    [Fact]
    public async Task DataTransfer_FormatAndPublicArgumentValidation_AreStable()
    {
        var useCase = new DataTransferUseCase(new FakeExportStream(), new FakeImportStream(),
            new FakeExportDataSource(), new FakeStagingRepository(), new FakeMetadataRepository(),
            new FakeTransactionRunner(), new FakeClock(DateTimeOffset.UnixEpoch));
        var format = await useCase.GetFormatAsync(default);
        using var readOnly = new MemoryStream([1], writable: false);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Equal(DataTransferUseCase.FormatName, format.Format);
        Assert.Equal(DataTransferUseCase.CurrentFormatVersion, format.FormatVersion);
        await Assert.ThrowsAsync<ArgumentNullException>(() => useCase.ExportAsync(null!, "1.0", default));
        await Assert.ThrowsAsync<ArgumentNullException>(() => useCase.PrepareImportAsync(null!, default));
        await Assert.ThrowsAsync<ArgumentException>(() => useCase.ExportAsync(readOnly, "1.0", default));
        await Assert.ThrowsAsync<ArgumentException>(() => useCase.ExportAsync(new MemoryStream(), " ", default));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => useCase.GetFormatAsync(cancellation.Token));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            useCase.CommitImportAsync(new PreparedImportId(Guid.Empty), default));
    }

    [Fact]
    public async Task Export_UsesFixedSnapshot_WritesNonSeekableStream_AndLeavesCallerStreamOpen()
    {
        var writer = new FakeExportStream();
        var source = new FakeExportDataSource();
        source.Values.AddRange([
            new DataTransferRecord<string>(DataTransferSection.Metadata, 0, "one"),
            new DataTransferRecord<string>(DataTransferSection.WorkRecords, 0, "two")]);
        source.AfterFirstRecord = () => source.Values.Add(
            new DataTransferRecord<string>(DataTransferSection.WorkRecords, 1, "concurrent"));
        var metadata = new FakeMetadataRepository();
        var useCase = new DataTransferUseCase(writer, new FakeImportStream(), source, new FakeStagingRepository(),
            metadata, new FakeTransactionRunner(), new FakeClock(DateTimeOffset.UnixEpoch));
        using var destination = new NonSeekableWriteStream();

        await useCase.ExportAsync(destination, "1.0", default);

        Assert.Equal(2, writer.Count);
        Assert.True(destination.CanWrite);
        Assert.True(destination.WrittenLength > 0);
        Assert.Equal(1, source.OpenCalls);
        Assert.Equal(1, source.DisposeCalls);
        Assert.NotNull(metadata.Value.LastExportedAtUtc);
    }

    [Fact]
    public async Task Export_FailureAndCancellation_DisposeSnapshotWithoutUpdatingTimestamp()
    {
        var failedWriter = new FakeExportStream { Failure = new IOException("write"), FailAfterRecord = 1 };
        var failedSource = new FakeExportDataSource();
        failedSource.Values.Add(new DataTransferRecord<string>(DataTransferSection.Metadata, 0, "one"));
        var failedMetadata = new FakeMetadataRepository();
        var failed = new DataTransferUseCase(failedWriter, new FakeImportStream(), failedSource, new FakeStagingRepository(),
            failedMetadata, new FakeTransactionRunner(), new FakeClock(DateTimeOffset.UnixEpoch));
        using var failedDestination = new NonSeekableWriteStream();
        await Assert.ThrowsAsync<IOException>(() => failed.ExportAsync(failedDestination, "1.0", default));
        Assert.Equal(1, failedSource.DisposeCalls);
        Assert.Null(failedMetadata.Value.LastExportedAtUtc);
        Assert.True(failedDestination.CanWrite);

        using var cancellation = new CancellationTokenSource();
        var cancelledSource = new FakeExportDataSource();
        cancelledSource.Values.AddRange([
            new DataTransferRecord<string>(DataTransferSection.Metadata, 0, "one"),
            new DataTransferRecord<string>(DataTransferSection.WorkRecords, 0, "two")]);
        cancelledSource.AfterFirstRecord = cancellation.Cancel;
        var cancelledMetadata = new FakeMetadataRepository();
        var cancelled = new DataTransferUseCase(new FakeExportStream(), new FakeImportStream(), cancelledSource,
            new FakeStagingRepository(), cancelledMetadata, new FakeTransactionRunner(), new FakeClock(DateTimeOffset.UnixEpoch));
        using var cancelledDestination = new NonSeekableWriteStream();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            cancelled.ExportAsync(cancelledDestination, "1.0", cancellation.Token));
        Assert.Equal(1, cancelledSource.DisposeCalls);
        Assert.Null(cancelledMetadata.Value.LastExportedAtUtc);
        Assert.True(cancelledDestination.CanWrite);
    }
}

internal sealed class NonSeekableWriteStream : Stream
{
    private readonly MemoryStream inner = new();
    public long WrittenLength => inner.Length;
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush()
    {
        inner.Flush();
    }


    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        return inner.FlushAsync(cancellationToken);
    }


    public override int Read(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }


    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }


    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }


    public override void Write(byte[] buffer, int offset, int count)
    {
        inner.Write(buffer, offset, count);
    }


    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        return inner.WriteAsync(buffer, cancellationToken);
    }

}
