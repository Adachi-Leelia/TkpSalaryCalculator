using System.Collections.Concurrent;
using TkpSalaryCalculator.Application.Contracts;
using TkpSalaryCalculator.Infrastructure.DataTransfer;
using TkpSalaryCalculator.Infrastructure.Sqlite;

namespace TkpSalaryCalculator.Infrastructure.Tests;

public sealed class InfrastructureBackgroundExecutionTests
{
    [Fact]
    public async Task ARCH005_SqliteOperationsLeaveTheCallingSynchronizationContext()
    {
        var root = Path.Combine(Path.GetTempPath(), $"tkp-background-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var callingContext = new SynchronizationContext();
        var observations = new ConcurrentQueue<SynchronizationContext?>();
        var database = new SqliteDatabase(Path.Combine(root, "salary.db"), bootstrapDefaults: false,
            () => observations.Enqueue(SynchronizationContext.Current));

        try
        {
            await StartFromContextAsync(callingContext, () => database.InitializeAsync());
            observations.Clear();

            var repository = new SqliteAppMetadataRepository(database, new SystemUtcClock());
            var metadata = await StartFromContextAsync(callingContext, () => repository.GetAsync(default));

            Assert.NotNull(metadata);
            Assert.Single(observations);
            Assert.All(observations, observed => Assert.NotSame(callingContext, observed));

            observations.Clear();
            var workRecords = new SqliteWorkRecordRepository(database, new SystemUtcClock());
            await StartFromContextAsync(callingContext, () => EnumerateRangeAsync(workRecords));
            Assert.Single(observations);
            Assert.All(observations, observed => Assert.NotSame(callingContext, observed));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ARCH005_JsonExportFileIoLeavesTheCallingSynchronizationContext()
    {
        var callingContext = new SynchronizationContext();
        await using var destination = new ContextTrackingStream();

        await StartFromContextAsync(callingContext, () => new StreamingJsonExportStream().WriteAsync(
            destination, new ExportDocumentHeader("test", 1, DateTimeOffset.UnixEpoch, "1.0"),
            EmptyRecords(), default));

        Assert.NotEmpty(destination.WriteContexts);
        Assert.All(destination.WriteContexts, observed => Assert.NotSame(callingContext, observed));
    }

    [Fact]
    public async Task ARCH005_JsonImportFileIoLeavesTheCallingSynchronizationContext()
    {
        var callingContext = new SynchronizationContext();
        await using var source = new ContextTrackingReadStream(System.Text.Encoding.UTF8.GetBytes("""
            {"format":"test","formatVersion":1,"createdAtUtc":"1970-01-01T00:00:00Z","appVersion":"1.0","data":[]}
            """));

        var records = await StartFromContextAsync(callingContext, () => ReadAllAsync(source));

        Assert.Single(records);
        Assert.NotEmpty(source.ReadContexts);
        Assert.All(source.ReadContexts, observed => Assert.NotSame(callingContext, observed));
    }

    [Fact]
    public async Task ARCH005_AmbientTransactionUsesOneBackgroundBoundaryForNestedRepositories()
    {
        var root = Path.Combine(Path.GetTempPath(), $"tkp-background-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var workerEntries = 0;
        var database = new SqliteDatabase(Path.Combine(root, "salary.db"), bootstrapDefaults: false,
            () => Interlocked.Increment(ref workerEntries));

        try
        {
            await database.InitializeAsync();
            workerEntries = 0;
            var repository = new SqliteAppMetadataRepository(database, new SystemUtcClock());
            var runner = new SqliteTransactionRunner(database);

            await runner.ExecuteAsync(async token =>
            {
                await repository.SetExportFormatVersionAsync(2, token);
                Assert.Equal(2, (await repository.GetAsync(token)).ExportFormatVersion);
            }, default);

            Assert.Equal(1, workerEntries);
            Assert.Equal(2, (await repository.GetAsync(default)).ExportFormatVersion);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task StartFromContextAsync(SynchronizationContext context, Func<Task> operation)
    {
        var previous = SynchronizationContext.Current;
        Task task;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            task = operation();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
        await task.ConfigureAwait(false);
    }

    private static async Task<T> StartFromContextAsync<T>(SynchronizationContext context, Func<Task<T>> operation)
    {
        var previous = SynchronizationContext.Current;
        Task<T> task;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            task = operation();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
        return await task.ConfigureAwait(false);
    }

    private static async Task EnumerateRangeAsync(SqliteWorkRecordRepository repository)
    {
        await foreach (var _ in repository.StreamRangeAsync(
                           new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), default))
        {
        }
    }

    private static async IAsyncEnumerable<DataTransferRecord> EmptyRecords()
    {
        await Task.CompletedTask;
        yield break;
    }

    private static async Task<IReadOnlyList<DataTransferRecord>> ReadAllAsync(Stream source)
    {
        var records = new List<DataTransferRecord>();
        await foreach (var record in new StreamingJsonImportStream().ReadAsync(source, default))
            records.Add(record);
        return records;
    }

    private sealed class ContextTrackingStream : MemoryStream
    {
        public ConcurrentQueue<SynchronizationContext?> WriteContexts { get; } = new();

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            WriteContexts.Enqueue(SynchronizationContext.Current);
            base.Write(buffer);
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            WriteContexts.Enqueue(SynchronizationContext.Current);
            return base.WriteAsync(buffer, cancellationToken);
        }
    }

    private sealed class ContextTrackingReadStream(byte[] buffer) : MemoryStream(buffer)
    {
        public ConcurrentQueue<SynchronizationContext?> ReadContexts { get; } = new();

        public override ValueTask<int> ReadAsync(Memory<byte> destination,
            CancellationToken cancellationToken = default)
        {
            ReadContexts.Enqueue(SynchronizationContext.Current);
            return base.ReadAsync(destination, cancellationToken);
        }
    }
}
