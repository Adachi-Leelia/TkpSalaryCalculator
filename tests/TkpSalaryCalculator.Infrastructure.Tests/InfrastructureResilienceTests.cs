using System.Globalization;
using Microsoft.Data.Sqlite;
using TkpSalaryCalculator.Application.Contracts;
using TkpSalaryCalculator.Application.Ports;
using TkpSalaryCalculator.Application.UseCases;
using TkpSalaryCalculator.Domain.Models;
using TkpSalaryCalculator.Domain.ValueObjects;
using TkpSalaryCalculator.Infrastructure.DataTransfer;
using TkpSalaryCalculator.Infrastructure.Sqlite;

namespace TkpSalaryCalculator.Infrastructure.Tests;

public sealed partial class InfrastructureResilienceTests
{
    [Fact]
    public async Task DB007_CurrentReleaseMigratesEmptyVersionZeroDatabaseThroughVersionTwoAndSeedsIt()
    {
        // There is no earlier formally released database fixture for the initial v1 release. This test therefore
        // covers the real version-0 (empty SQLite file) bootstrap only; future release migration tests must use
        // archived databases produced by each released application version.
        await using var fixture = await TestDatabase.CreateUninitializedAsync();
        await using (var before = await fixture.OpenAsync(SqliteOpenMode.ReadWriteCreate))
        {
            await ExecuteAsync(before, """
                CREATE TABLE pre_migration_marker(value TEXT NOT NULL);
                INSERT INTO pre_migration_marker VALUES('preserve-me');
                PRAGMA user_version = 0;
                """);
        }

        await fixture.Database.InitializeAsync();

        await using var after = await fixture.OpenAsync();
        Assert.Equal(SqliteDatabase.CurrentSchemaVersion, await ScalarLongAsync(after, "PRAGMA user_version;"));
        Assert.Equal("preserve-me", await ScalarStringAsync(after, "SELECT value FROM pre_migration_marker;"));
        Assert.Equal(1L, await ScalarLongAsync(after,
            "SELECT COUNT(*) FROM app_metadata WHERE id = 1 AND initial_snapshot_id IS NOT NULL;"));
        Assert.Equal(5L, await ScalarLongAsync(after, "SELECT COUNT(*) FROM service_preset;"));
        Assert.Equal(35L, await ScalarLongAsync(after, "SELECT COUNT(*) FROM holiday_date;"));
    }

    [Fact]
    public async Task DB008_PartialVersionZeroSchemaMigrationFailurePreservesExistingDataAndVersionAndOpenKeepsFailing()
    {
        // This is deliberately an inconsistent v0 database, not a claim of compatibility with an unreleased schema.
        await using var fixture = await TestDatabase.CreateUninitializedAsync();
        await using (var before = await fixture.OpenAsync(SqliteOpenMode.ReadWriteCreate))
        {
            await ExecuteAsync(before, """
                CREATE TABLE holiday_calendar_version(id TEXT PRIMARY KEY);
                INSERT INTO holiday_calendar_version VALUES('legacy-marker');
                PRAGMA user_version = 0;
                """);
        }

        await Assert.ThrowsAsync<SqliteException>(() => fixture.Database.InitializeAsync());

        await using (var afterFailure = await fixture.OpenAsync())
        {
            Assert.Equal(0L, await ScalarLongAsync(afterFailure, "PRAGMA user_version;"));
            Assert.Equal("legacy-marker",
                await ScalarStringAsync(afterFailure, "SELECT id FROM holiday_calendar_version;"));
            Assert.Equal(1L, await ScalarLongAsync(afterFailure,
                "SELECT COUNT(*) FROM pragma_table_info('holiday_calendar_version');"));
            Assert.Equal(0L, await ScalarLongAsync(afterFailure,
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'app_metadata';"));
        }

        var reopened = new SqliteDatabase(fixture.DatabasePath);
        await Assert.ThrowsAsync<SqliteException>(() => reopened.InitializeAsync());
        await using var final = await fixture.OpenAsync();
        Assert.Equal(0L, await ScalarLongAsync(final, "PRAGMA user_version;"));
        Assert.Equal("legacy-marker", await ScalarStringAsync(final, "SELECT id FROM holiday_calendar_version;"));
    }

    [Fact]
    public async Task DB006_WalReopenAfterUncommittedConnectionRetainsOnlyCommittedRowsAndPassesIntegrityChecks()
    {
        await using var fixture = await TestDatabase.CreateAsync();
        await using (var committedConnection = await fixture.OpenAsync())
        {
            await ExecuteAsync(committedConnection, """
                INSERT INTO monthly_allowance(id, payroll_period_year_month, display_name, amount_yen,
                    created_at_utc, updated_at_utc)
                VALUES('20000000-0000-4000-8000-000000000001', 202608, 'committed', 100,
                    '2026-08-16T00:00:00.0000000Z', '2026-08-16T00:00:00.0000000Z');
                """);
        }

        await using (var interruptedConnection = await fixture.OpenAsync())
        {
            await using var interrupted = interruptedConnection.BeginTransaction(deferred: false);
            await ExecuteAsync(interruptedConnection, """
                INSERT INTO monthly_allowance(id, payroll_period_year_month, display_name, amount_yen,
                    created_at_utc, updated_at_utc)
                VALUES('20000000-0000-4000-8000-000000000002', 202608, 'uncommitted', 200,
                    '2026-08-16T00:00:00.0000000Z', '2026-08-16T00:00:00.0000000Z');
                """, interrupted);
            // Disposing an open WAL transaction models the stable, deterministic part of a process interruption.
            // A child-process hard-kill test is intentionally not used because it is platform-sensitive in this suite.
        }

        await new SqliteDatabase(fixture.DatabasePath).InitializeAsync();
        await using var reopened = await fixture.OpenAsync();
        Assert.Equal(["committed"], await ReadStringsAsync(reopened,
            "SELECT display_name FROM monthly_allowance ORDER BY id;"));
        Assert.Equal("ok", await ScalarStringAsync(reopened, "PRAGMA integrity_check;"));
        Assert.Equal(0L, await CountRowsAsync(reopened, "PRAGMA foreign_key_check;"));
    }

    public static TheoryData<string, string> IndependentInvalidValues => new()
    {
        {
            "invalid year-month",
            """
            INSERT INTO monthly_allowance(id, payroll_period_year_month, display_name, amount_yen,
                created_at_utc, updated_at_utc)
            VALUES('30000000-0000-4000-8000-000000000001', 202613, 'valid name', 0,
                '2026-08-16T00:00:00.0000000Z', '2026-08-16T00:00:00.0000000Z');
            """
        },
        {
            "negative amount",
            """
            INSERT INTO monthly_allowance(id, payroll_period_year_month, display_name, amount_yen,
                created_at_utc, updated_at_utc)
            VALUES('30000000-0000-4000-8000-000000000002', 202608, 'valid name', -1,
                '2026-08-16T00:00:00.0000000Z', '2026-08-16T00:00:00.0000000Z');
            """
        },
        {
            "invalid date encoding",
            """
            INSERT INTO work_record(id, work_date, service_id, input_mode, work_minutes,
                created_at_utc, updated_at_utc)
            SELECT '30000000-0000-4000-8000-000000000003', '20260816', id, 'Duration', 30,
                '2026-08-16T00:00:00.0000000Z', '2026-08-16T00:00:00.0000000Z'
            FROM service_definition LIMIT 1;
            """
        },
        {
            "invalid minute-of-day",
            """
            INSERT INTO work_record(id, work_date, service_id, input_mode, work_minutes,
                start_time_minutes, end_time_minutes, created_at_utc, updated_at_utc)
            SELECT '30000000-0000-4000-8000-000000000004', '2026-08-16', id, 'TimeRange', 30,
                1440, 30, '2026-08-16T00:00:00.0000000Z', '2026-08-16T00:00:00.0000000Z'
            FROM service_definition LIMIT 1;
            """
        },
    };

    [Theory]
    [MemberData(nameof(IndependentInvalidValues))]
    public async Task DB003_EachInvalidPersistedValueIsRejectedWhileOtherColumnsRemainValid(
        string caseName, string sql)
    {
        await using var fixture = await TestDatabase.CreateAsync();
        await using var connection = await fixture.OpenAsync();
        await ExecuteAsync(connection, "PRAGMA foreign_keys = ON;");

        var exception = await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(connection, sql));

        Assert.Equal(19, exception.SqliteErrorCode);
        Assert.False(string.IsNullOrWhiteSpace(caseName));
    }

    [Fact]
    public async Task HIST004_OuterTransactionFailureRollsBackCloneMonthReferenceAndAllSnapshotRowsAcrossReopen()
    {
        await using var fixture = await TestDatabase.CreateSeededAsync();
        var clock = new FixedClock(new DateTimeOffset(2026, 8, 16, 1, 0, 0, TimeSpan.Zero));
        var repository = new SqliteSettingSnapshotRepository(fixture.Database, clock);
        var julyBefore = (await repository.FindForMonthAsync(new YearMonth(2026, 7), default))!;
        var augustBefore = (await repository.FindForMonthAsync(new YearMonth(2026, 8), default))!;
        await using var beforeConnection = await fixture.OpenAsync();
        var snapshotCountBefore = await ScalarLongAsync(beforeConnection, "SELECT COUNT(*) FROM setting_snapshot;");

        var replacement = new SettingSnapshotReplacementDto(augustBefore.Services, augustBefore.TimeCategories,
            [new SnapshotRate(new ServiceId(fixture.ServiceId), null, RateType.Hourly, new YenAmount(2750))],
            augustBefore.Premiums, augustBefore.CountBonuses);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new SqliteTransactionRunner(fixture.Database)
            .ExecuteAsync(async token =>
            {
                var clone = await repository.TryCloneAndReplaceMonthSnapshotAsync(new YearMonth(2026, 8),
                    augustBefore.Id, replacement, augustBefore.HolidayCalendarVersionId, clock.UtcNow, token);
                Assert.NotNull(clone);
                throw new InvalidOperationException("fail the outer operation after clone");
            }, default));

        var reopenedDatabase = new SqliteDatabase(fixture.DatabasePath);
        var reopened = new SqliteSettingSnapshotRepository(reopenedDatabase, clock);
        var julyAfter = (await reopened.FindForMonthAsync(new YearMonth(2026, 7), default))!;
        var augustAfter = (await reopened.FindForMonthAsync(new YearMonth(2026, 8), default))!;
        await using var afterConnection = await fixture.OpenAsync();
        Assert.Equal(julyBefore.Id, julyAfter.Id);
        Assert.Equal(augustBefore.Id, augustAfter.Id);
        Assert.Equal(snapshotCountBefore,
            await ScalarLongAsync(afterConnection, "SELECT COUNT(*) FROM setting_snapshot;"));
        Assert.Equal(1000, Assert.Single(julyAfter.Rates).Amount.Value);
        Assert.Equal(1000, Assert.Single(augustAfter.Rates).Amount.Value);
    }

    [Fact]
    public async Task DATA012_CancellationDuringPrepareDeletesStageAndCandidateAndKeepsLiveDatabase()
    {
        await using var source = await TestDatabase.CreateSeededAsync();
        var clock = new FixedClock(new DateTimeOffset(2026, 8, 16, 2, 0, 0, TimeSpan.Zero));
        var exported = await ExportAsync(source, clock);
        await using var destination = await TestDatabase.CreateSeededAsync();
        var existing = await AddLiveMarkerAsync(destination, clock);
        using var cancellation = new CancellationTokenSource();
        await using var input = new CancelAfterFirstReadStream(exported, cancellation);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateTransferUseCase(destination, clock).PrepareImportAsync(input, cancellation.Token));

        AssertStagingEmpty(destination);
        Assert.Equal(existing, await new SqliteWorkRecordRepository(destination.Database, clock)
            .FindAsync(existing.Id, default));
    }

    [Fact]
    public async Task DATA012_PrepareValidationFailureDeletesStageAndCandidateAndKeepsLiveDatabase()
    {
        await using var destination = await TestDatabase.CreateSeededAsync();
        var clock = new FixedClock(new DateTimeOffset(2026, 8, 16, 2, 15, 0, TimeSpan.Zero));
        var existing = await AddLiveMarkerAsync(destination, clock);

        await Assert.ThrowsAnyAsync<Exception>(() => CreateTransferUseCase(destination, clock)
            .PrepareImportAsync(new MemoryStream("{ invalid"u8.ToArray()), default));

        AssertStagingEmpty(destination);
        Assert.Equal(existing, await new SqliteWorkRecordRepository(destination.Database, clock)
            .FindAsync(existing.Id, default));
    }

    [Fact]
    public async Task DATA012_CommitFailureRetainsPreparedFilesForRetryUntilExplicitDiscardAndKeepsLiveDatabase()
    {
        await using var source = await TestDatabase.CreateSeededAsync();
        var clock = new FixedClock(new DateTimeOffset(2026, 8, 16, 2, 30, 0, TimeSpan.Zero));
        var exported = await ExportAsync(source, clock);
        await using var destination = await TestDatabase.CreateSeededAsync();
        var existing = await AddLiveMarkerAsync(destination, clock);
        var useCase = CreateTransferUseCase(destination, clock);
        var preview = await useCase.PrepareImportAsync(new MemoryStream(exported), default);
        var candidatePath = CandidatePath(destination, preview.Id);
        await using (var candidate = new SqliteConnection($"Data Source={candidatePath};Pooling=False"))
        {
            await candidate.OpenAsync();
            await ExecuteAsync(candidate, """
                INSERT INTO holiday_calendar_version(
                    id, version_name, source_name, source_reference_date, created_at_utc)
                VALUES('10000000-0000-4000-8000-000000000001', 'tampered', 'other', '2026-08-16',
                    '2026-08-16T00:00:00.0000000Z');
                """);
        }

        await Assert.ThrowsAnyAsync<Exception>(() => useCase.CommitImportAsync(preview.Id, default));

        Assert.True(File.Exists(StagePath(destination, preview.Id)));
        Assert.True(File.Exists(candidatePath));
        Assert.Equal(existing, await new SqliteWorkRecordRepository(destination.Database, clock)
            .FindAsync(existing.Id, default));
        await useCase.DiscardImportAsync(preview.Id, default);
        AssertStagingEmpty(destination);
    }

    [Fact]
    public async Task DATA012_NextRepositoryDiscardAbandonedDeletesPriorInstanceFilesAndKeepsLiveDatabase()
    {
        await using var source = await TestDatabase.CreateSeededAsync();
        var clock = new FixedClock(new DateTimeOffset(2026, 8, 16, 2, 45, 0, TimeSpan.Zero));
        var exported = await ExportAsync(source, clock);
        await using var destination = await TestDatabase.CreateSeededAsync();
        var existing = await AddLiveMarkerAsync(destination, clock);
        var firstRepository = new SqliteImportStagingRepository(destination.Database, destination.StagingPath, clock);
        var firstUseCase = CreateTransferUseCase(destination, clock, firstRepository);
        var preview = await firstUseCase.PrepareImportAsync(new MemoryStream(exported), default);
        Assert.True(File.Exists(StagePath(destination, preview.Id)));
        Assert.True(File.Exists(CandidatePath(destination, preview.Id)));

        // A new application process has no pooled handles from the prior repository instance.
        SqliteConnection.ClearAllPools();
        var nextRepository = new SqliteImportStagingRepository(destination.Database, destination.StagingPath, clock);
        await nextRepository.DiscardAbandonedAsync(default);

        AssertStagingEmpty(destination);
        Assert.Equal(existing, await new SqliteWorkRecordRepository(destination.Database, clock)
            .FindAsync(existing.Id, default));
    }

    private static async Task<byte[]> ExportAsync(TestDatabase database, IUtcClock clock)
    {
        var output = new MemoryStream();
        await CreateTransferUseCase(database, clock).ExportAsync(output, "1.0.0", default);
        return output.ToArray();
    }

    private static DataTransferUseCase CreateTransferUseCase(TestDatabase database, IUtcClock clock,
        SqliteImportStagingRepository? staging = null) =>
        new(new StreamingJsonExportStream(), new StreamingJsonImportStream(),
            new SqliteExportDataSource(database.Database),
            staging ?? new SqliteImportStagingRepository(database.Database, database.StagingPath, clock),
            new SqliteAppMetadataRepository(database.Database, clock),
            new SqliteTransactionRunner(database.Database), clock);

    private static async Task<WorkRecordDto> AddLiveMarkerAsync(TestDatabase database, IUtcClock clock)
    {
        var marker = new WorkRecordDto(new WorkRecordId(Guid.NewGuid()), new DateOnly(2026, 8, 15),
            new ServiceId(database.ServiceId), null, WorkInputMode.Duration, new WorkMinutes(45), null, null,
            null, null, null);
        await new SqliteWorkRecordRepository(database.Database, clock).UpsertAsync(marker, default);
        return marker;
    }

    private static string StagePath(TestDatabase fixture, PreparedImportId id) => Path.Combine(fixture.StagingPath,
        $"tkp-import-{id.Value:N}.stage.db");

    private static string CandidatePath(TestDatabase fixture, PreparedImportId id) => Path.Combine(fixture.StagingPath,
        $"tkp-import-{id.Value:N}.candidate.db");

    private static void AssertStagingEmpty(TestDatabase fixture) =>
        Assert.Empty(Directory.Exists(fixture.StagingPath)
            ? Directory.EnumerateFiles(fixture.StagingPath, "*", SearchOption.TopDirectoryOnly)
            : []);

    private static async Task ExecuteAsync(SqliteConnection connection, string sql,
        SqliteTransaction? transaction = null)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<object?> ScalarAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }

    private static async Task<long> ScalarLongAsync(SqliteConnection connection, string sql) =>
        Convert.ToInt64(await ScalarAsync(connection, sql), CultureInfo.InvariantCulture);

    private static async Task<string> ScalarStringAsync(SqliteConnection connection, string sql) =>
        Convert.ToString(await ScalarAsync(connection, sql), CultureInfo.InvariantCulture)!;

    private static async Task<long> CountRowsAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();
        long count = 0;
        while (await reader.ReadAsync()) count++;
        return count;
    }

    private static async Task<string[]> ReadStringsAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();
        var values = new List<string>();
        while (await reader.ReadAsync()) values.Add(reader.GetString(0));
        return [.. values];
    }

    private sealed class FixedClock(DateTimeOffset value) : IUtcClock
    {
        public DateTimeOffset UtcNow { get; } = value;
    }

    private sealed class CancelAfterFirstReadStream(byte[] bytes, CancellationTokenSource cancellation)
        : MemoryStream(bytes)
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            var count = await base.ReadAsync(buffer, cancellationToken);
            cancellation.Cancel();
            return count;
        }
    }

    private sealed partial class TestDatabase : IAsyncDisposable
    {
        private TestDatabase(string root, bool bootstrapDefaults = true)
        {
            Root = root;
            DatabasePath = Path.Combine(root, "salary.db");
            StagingPath = Path.Combine(root, "staging");
            Database = new SqliteDatabase(DatabasePath, bootstrapDefaults);
        }

        public string Root { get; }
        public string DatabasePath { get; }
        public string StagingPath { get; }
        public SqliteDatabase Database { get; }
        public Guid ServiceId { get; private set; }

        public static Task<TestDatabase> CreateUninitializedAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), $"tkp-infrastructure-resilience-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            return Task.FromResult(new TestDatabase(root));
        }

        private static TestDatabase CreateUninitialized(bool bootstrapDefaults)
        {
            var root = Path.Combine(Path.GetTempPath(), $"tkp-infrastructure-resilience-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            return new TestDatabase(root, bootstrapDefaults);
        }

        public static async Task<TestDatabase> CreateAsync()
        {
            var fixture = await CreateUninitializedAsync();
            await fixture.Database.InitializeAsync();
            return fixture;
        }

        public static async Task<TestDatabase> CreateSeededAsync()
        {
            var fixture = await CreateAsync();
            fixture.ServiceId = Guid.NewGuid();
            var snapshotId = Guid.NewGuid();
            var holidayId = Guid.NewGuid();
            await using var connection = await fixture.OpenAsync();
            await ExecuteAsync(connection, "PRAGMA foreign_keys = ON;");
            await using var transaction = connection.BeginTransaction(deferred: false);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO holiday_calendar_version(id, version_name, source_name, source_reference_date, created_at_utc)
                VALUES($holiday, $holidayName, 'test', '2026-08-01', $now);
                INSERT INTO holiday_date(holiday_calendar_version_id, holiday_date, display_name)
                VALUES($holiday, '2026-08-11', 'Holiday');
                INSERT INTO service_definition(id, created_at_utc) VALUES($service, $now);
                INSERT INTO setting_snapshot(id, based_on_id, holiday_calendar_version_id, schema_version, created_at_utc)
                VALUES($snapshot, NULL, $holiday, 1, $now);
                INSERT INTO snapshot_service(snapshot_id, service_id, display_name, display_order, is_enabled)
                VALUES($snapshot, $service, 'Service', 0, 1);
                INSERT INTO snapshot_rate(snapshot_id, service_id, time_category_id, rate_type, amount_yen)
                VALUES($snapshot, $service, NULL, 'Hourly', 1000);
                INSERT INTO setting_month(year_month, snapshot_id, created_at_utc, updated_at_utc)
                VALUES(202607, $snapshot, $now, $now), (202608, $snapshot, $now, $now);
                INSERT INTO closing_rule_history(id, effective_from_year_month, closing_day, is_end_of_month, created_at_utc)
                VALUES($closing, 202601, 20, 0, $now);
                UPDATE app_metadata SET initial_setup_status = 'Completed', initial_snapshot_id = $snapshot,
                    updated_at_utc = $now WHERE id = 1;
                """;
            command.Parameters.AddWithValue("$holiday", holidayId.ToString("D"));
            command.Parameters.AddWithValue("$holidayName", $"test-v1-{holidayId:D}");
            command.Parameters.AddWithValue("$service", fixture.ServiceId.ToString("D"));
            command.Parameters.AddWithValue("$snapshot", snapshotId.ToString("D"));
            command.Parameters.AddWithValue("$closing", Guid.NewGuid().ToString("D"));
            command.Parameters.AddWithValue("$now", "2026-08-01T00:00:00.0000000Z");
            await command.ExecuteNonQueryAsync();
            await transaction.CommitAsync();
            return fixture;
        }

        public async Task<SqliteConnection> OpenAsync(SqliteOpenMode mode = SqliteOpenMode.ReadWrite)
        {
            var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = DatabasePath,
                Mode = mode,
                Pooling = false,
            }.ToString());
            await connection.OpenAsync();
            return connection;
        }

        public ValueTask DisposeAsync()
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
            return ValueTask.CompletedTask;
        }
    }
}
