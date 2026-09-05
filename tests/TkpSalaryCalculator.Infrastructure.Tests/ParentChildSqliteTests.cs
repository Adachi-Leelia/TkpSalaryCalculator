using System.Collections;
using Microsoft.Data.Sqlite;
using TkpSalaryCalculator.Application.Contracts;
using TkpSalaryCalculator.Domain.Contracts;
using TkpSalaryCalculator.Domain.Models;
using TkpSalaryCalculator.Domain.Services;
using TkpSalaryCalculator.Domain.ValueObjects;
using TkpSalaryCalculator.Infrastructure.Sqlite;

namespace TkpSalaryCalculator.Infrastructure.Tests;

public sealed class ParentChildSqliteTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task DB020_DB021_DB024_WorkRecordRepositoryRoundTripsReplacesAndDeletesEveryTaskAtomically()
    {
        await using var fixture = await ParentChildDatabase.CreateAsync();
        var services = await fixture.GetServiceIdsAsync();
        var repository = new SqliteWorkRecordRepository(fixture.Database, new FixedClock(Now));
        var operationId = Guid.NewGuid();
        var original = WorkRecord(
            new WorkRecordId(Guid.NewGuid()),
            new DateOnly(2026, 9, 1),
            WorkTask(services[0], 0, 30),
            WorkTask(services[1], 1, 45));

        Assert.True(await repository.TryInsertAsync(original, operationId, default));
        Assert.False(await repository.TryInsertAsync(
            WorkRecord(new WorkRecordId(Guid.NewGuid()), original.WorkDate, WorkTask(services[0], 0, 10)),
            operationId, default));
        Assert.Equal(original, await repository.FindAsync(original.Id, default));
        Assert.Equal(original, await repository.FindBySaveOperationIdAsync(operationId, default));
        Assert.Equal([original], await repository.StreamRangeAsync(
            original.WorkDate, original.WorkDate, default).ToListAsync());

        await using (var connection = await fixture.OpenAsync())
        {
            var retainedId = original.Tasks[1].Id.Value;
            Assert.Equal(SqliteUtc(Now), await ScalarStringAsync(connection,
                $"SELECT created_at_utc FROM work_task WHERE id = '{retainedId:D}';"));
        }

        var replacement = WorkRecord(
            original.Id,
            original.WorkDate.AddDays(1),
            original.Tasks[1] with { DisplayOrder = new DisplayOrder(0), WorkMinutes = new WorkMinutes(60) },
            WorkTask(services[0], 1, 90));
        repository = new SqliteWorkRecordRepository(fixture.Database, new FixedClock(Now.AddHours(1)));
        await repository.UpsertAsync(replacement, default);
        Assert.Equal(replacement, await repository.FindAsync(original.Id, default));
        Assert.Equal(replacement, await repository.FindBySaveOperationIdAsync(operationId, default));
        await using (var connection = await fixture.OpenAsync())
        {
            var retainedId = replacement.Tasks[0].Id.Value;
            var addedId = replacement.Tasks[1].Id.Value;
            Assert.Equal(SqliteUtc(Now), await ScalarStringAsync(connection,
                $"SELECT created_at_utc FROM work_task WHERE id = '{retainedId:D}';"));
            Assert.Equal(SqliteUtc(Now.AddHours(1)), await ScalarStringAsync(connection,
                $"SELECT updated_at_utc FROM work_task WHERE id = '{retainedId:D}';"));
            Assert.Equal(SqliteUtc(Now.AddHours(1)), await ScalarStringAsync(connection,
                $"SELECT created_at_utc FROM work_task WHERE id = '{addedId:D}';"));
            Assert.Equal(0L, await ScalarLongAsync(connection,
                $"SELECT COUNT(*) FROM work_task WHERE id = '{original.Tasks[0].Id.Value:D}';"));
        }

        var invalid = WorkRecord(
            original.Id,
            original.WorkDate.AddDays(2),
            WorkTask(new ServiceId(Guid.NewGuid()), 0, 120));
        await Assert.ThrowsAsync<SqliteException>(() => repository.UpsertAsync(invalid, default));
        Assert.Equal(replacement, await repository.FindAsync(original.Id, default));

        await repository.DeleteAsync(original.Id, default);
        Assert.Null(await repository.FindAsync(original.Id, default));
        await using var finalConnection = await fixture.OpenAsync();
        Assert.Equal(0L, await ScalarLongAsync(finalConnection,
            $"SELECT COUNT(*) FROM work_task WHERE work_record_id = '{original.Id.Value:D}';"));
    }

    [Fact]
    public async Task DB020_DB024_BasicShiftRepositoryBulkReadsReplacesAndDeletesEveryTask()
    {
        await using var fixture = await ParentChildDatabase.CreateAsync();
        var services = await fixture.GetServiceIdsAsync();
        var repository = new SqliteBasicShiftRepository(fixture.Database, new FixedClock(Now));
        var shiftId = new BasicShiftId(Guid.NewGuid());
        var original = new BasicShiftDto(
            shiftId,
            DayOfWeek.Monday,
            [
                BasicShiftTask(services[0], 0, 30),
                BasicShiftTask(services[1], 1, 45),
            ],
            new DisplayOrder(0),
            true);

        await repository.UpsertAsync(original, default);
        Assert.Equal(original, await repository.FindAsync(shiftId, default));
        Assert.Equal(original, Assert.Single(await repository.GetForWeekdayAsync(DayOfWeek.Monday, default)));
        var bulk = await repository.GetForWeekdaysAsync(
            [DayOfWeek.Monday, DayOfWeek.Tuesday], default);
        Assert.Equal(original, Assert.Single(bulk[DayOfWeek.Monday]));
        Assert.Empty(bulk[DayOfWeek.Tuesday]);

        await using (var connection = await fixture.OpenAsync())
        {
            Assert.Equal(SqliteUtc(Now), await ScalarStringAsync(connection,
                $"SELECT created_at_utc FROM basic_shift_task WHERE id = '{original.Tasks[1].Id.Value:D}';"));
        }

        var replacement = new BasicShiftDto(
            shiftId,
            DayOfWeek.Tuesday,
            [
                original.Tasks[1] with { DisplayOrder = new DisplayOrder(0), WorkMinutes = new WorkMinutes(90) },
                BasicShiftTask(services[0], 1, 30),
            ],
            new DisplayOrder(1),
            false);
        repository = new SqliteBasicShiftRepository(fixture.Database, new FixedClock(Now.AddHours(1)));
        await repository.UpsertAsync(replacement, default);
        Assert.Empty(await repository.GetForWeekdayAsync(DayOfWeek.Monday, default));
        Assert.Equal(replacement, Assert.Single(await repository.GetForWeekdayAsync(DayOfWeek.Tuesday, default)));
        await using (var connection = await fixture.OpenAsync())
        {
            Assert.Equal(SqliteUtc(Now), await ScalarStringAsync(connection,
                $"SELECT created_at_utc FROM basic_shift_task WHERE id = '{replacement.Tasks[0].Id.Value:D}';"));
            Assert.Equal(SqliteUtc(Now.AddHours(1)), await ScalarStringAsync(connection,
                $"SELECT updated_at_utc FROM basic_shift_task WHERE id = '{replacement.Tasks[0].Id.Value:D}';"));
            Assert.Equal(SqliteUtc(Now.AddHours(1)), await ScalarStringAsync(connection,
                $"SELECT created_at_utc FROM basic_shift_task WHERE id = '{replacement.Tasks[1].Id.Value:D}';"));
            Assert.Equal(0L, await ScalarLongAsync(connection,
                $"SELECT COUNT(*) FROM basic_shift_task WHERE id = '{original.Tasks[0].Id.Value:D}';"));
        }

        await repository.DeleteAsync(shiftId, default);
        Assert.Null(await repository.FindAsync(shiftId, default));
        await using var finalConnection = await fixture.OpenAsync();
        Assert.Equal(0L, await ScalarLongAsync(finalConnection,
            $"SELECT COUNT(*) FROM basic_shift_task WHERE basic_shift_id = '{shiftId.Value:D}';"));
    }

    [Fact]
    public async Task DB020_RepositoriesSnapshotMutableTaskCollectionsBeforeBackgroundPersistence()
    {
        await using var fixture = await ParentChildDatabase.CreateAsync();
        var serviceId = (await fixture.GetServiceIdsAsync())[0];
        var clock = new FixedClock(Now);
        var recordId = new WorkRecordId(Guid.NewGuid());
        var recordTask = WorkTask(serviceId, 0, 30);
        var record = new WorkRecordDto(recordId, new DateOnly(2026, 9, 1),
            new OneShotReadOnlyList<WorkTaskDto>(recordTask), null, null);

        await new SqliteWorkRecordRepository(fixture.Database, clock).UpsertAsync(record, default);

        var savedRecord = await new SqliteWorkRecordRepository(fixture.Database, clock)
            .FindAsync(recordId, default);
        Assert.Equal(recordTask, Assert.Single(savedRecord!.Tasks));

        var shiftId = new BasicShiftId(Guid.NewGuid());
        var shiftTask = BasicShiftTask(serviceId, 0, 45);
        var shift = new BasicShiftDto(shiftId, DayOfWeek.Monday,
            new OneShotReadOnlyList<BasicShiftTaskDto>(shiftTask), new DisplayOrder(0), true);

        await new SqliteBasicShiftRepository(fixture.Database, clock).UpsertAsync(shift, default);

        var savedShift = await new SqliteBasicShiftRepository(fixture.Database, clock)
            .FindAsync(shiftId, default);
        Assert.Equal(shiftTask, Assert.Single(savedShift!.Tasks));
    }

    [Fact]
    public async Task DB009_DB022_SchemaFiveMigratesWorkAndShiftToOneTaskAndPreservesSalary()
    {
        await using var fixture = await ParentChildDatabase.CreateAsync();
        var serviceId = (await fixture.GetServiceIdsAsync())[0];
        var recordId = Guid.Parse("71000000-0000-4000-8000-000000000001");
        var shiftId = Guid.Parse("72000000-0000-4000-8000-000000000001");
        await fixture.ReplaceParentsWithVersionFiveAsync(serviceId.Value, recordId, shiftId);

        var beforeRecord = new WorkRecord(
            new WorkRecordId(recordId),
            new DateOnly(2026, 8, 31),
            [
                new WorkTask(new WorkTaskId(recordId), serviceId, null,
                    WorkInputMode.Duration, new WorkMinutes(60), null, null,
                    new DisplayOrder(0)),
            ]);
        var beforeSalary = Calculate(beforeRecord, serviceId);

        var migrated = new SqliteDatabase(fixture.DatabasePath);
        await migrated.InitializeAsync();
        var record = await new SqliteWorkRecordRepository(migrated, new FixedClock(Now))
            .FindAsync(new WorkRecordId(recordId), default);
        var shift = await new SqliteBasicShiftRepository(migrated, new FixedClock(Now))
            .FindAsync(new BasicShiftId(shiftId), default);

        Assert.NotNull(record);
        Assert.Equal(new WorkTaskId(recordId), Assert.Single(record!.Tasks).Id);
        Assert.Equal(new BasicShiftTaskId(shiftId), Assert.Single(shift!.Tasks).Id);
        Assert.Equal(record, await new SqliteWorkRecordRepository(migrated, new FixedClock(Now))
            .FindBySaveOperationIdAsync(recordId, default));
        var afterSalary = Calculate(record.ToDomain(), serviceId);
        Assert.Equal(beforeSalary.Total, afterSalary.Total);
        Assert.Equal(beforeSalary.TaskCalculations[0].BasePay, afterSalary.TaskCalculations[0].BasePay);

        await using var connection = await fixture.OpenAsync();
        Assert.Equal(6L, await ScalarLongAsync(connection, "PRAGMA user_version;"));
        Assert.Equal("ok", await ScalarStringAsync(connection, "PRAGMA integrity_check;"));
        Assert.Equal(0L, await ScalarLongAsync(connection, """
            SELECT COUNT(*) FROM pragma_table_info('work_record')
            WHERE name IN ('service_id', 'work_minutes', 'source_service_preset_id');
            """));
        Assert.Equal(0L, await ScalarLongAsync(connection, """
            SELECT COUNT(*) FROM sqlite_master
            WHERE type = 'index' AND name = 'ix_work_record_service_date';
            """));
    }

    [Fact]
    public async Task DB023_SchemaFiveMigrationFailureRollsBackOldTablesAndVersion()
    {
        await using var fixture = await ParentChildDatabase.CreateAsync();
        var serviceId = (await fixture.GetServiceIdsAsync())[0];
        var firstId = Guid.Parse("73000000-0000-4000-8000-000000000001");
        var secondId = Guid.Parse("73000000-0000-4000-8000-000000000002");
        await fixture.ReplaceParentsWithVersionFiveAsync(serviceId.Value, firstId, Guid.NewGuid(), secondId);

        var migrated = new SqliteDatabase(fixture.DatabasePath);
        await Assert.ThrowsAsync<SqliteException>(() => migrated.InitializeAsync());

        await using var connection = await fixture.OpenAsync();
        Assert.Equal(5L, await ScalarLongAsync(connection, "PRAGMA user_version;"));
        Assert.Equal(2L, await ScalarLongAsync(connection, "SELECT COUNT(*) FROM work_record;"));
        Assert.Equal(1L, await ScalarLongAsync(connection, """
            SELECT COUNT(*) FROM pragma_table_info('work_record') WHERE name = 'service_id';
            """));
        Assert.Equal(0L, await ScalarLongAsync(connection, """
            SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'work_task';
            """));
    }

    [Theory]
    [InlineData("UPDATE work_record SET input_mode = 'TimeRange', start_time_minutes = 540, end_time_minutes = 620 WHERE id = $id;")]
    [InlineData("UPDATE work_record SET source_work_record_id = '' WHERE id = $id;")]
    public async Task DB023_SchemaFiveDomainViolationRollsBackBeforeReplacingParentTables(string corruptSql)
    {
        await using var fixture = await ParentChildDatabase.CreateAsync();
        var serviceId = (await fixture.GetServiceIdsAsync())[0];
        var recordId = Guid.Parse("74000000-0000-4000-8000-000000000001");
        await fixture.ReplaceParentsWithVersionFiveAsync(serviceId.Value, recordId, Guid.NewGuid());
        await using (var connection = await fixture.OpenAsync())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = corruptSql;
            command.Parameters.AddWithValue("$id", recordId.ToString("D"));
            await command.ExecuteNonQueryAsync();
        }

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new SqliteDatabase(fixture.DatabasePath).InitializeAsync());

        await using var verification = await fixture.OpenAsync();
        Assert.Equal(5L, await ScalarLongAsync(verification, "PRAGMA user_version;"));
        Assert.Equal(1L, await ScalarLongAsync(verification, """
            SELECT COUNT(*) FROM pragma_table_info('work_record') WHERE name = 'service_id';
            """));
        Assert.Equal(0L, await ScalarLongAsync(verification, """
            SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'work_task';
            """));
    }

    [Fact]
    public async Task DB019_DB025_ChildOrderIndexesServeParentRangeReadsWithoutRedundantParentOnlyIndexes()
    {
        await using var fixture = await ParentChildDatabase.CreateAsync();
        await using var connection = await fixture.OpenAsync();
        var plan = await QueryStringsAsync(connection, """
            EXPLAIN QUERY PLAN
            SELECT wr.id, wt.id
            FROM work_record AS wr
            JOIN work_task AS wt ON wt.work_record_id = wr.id
            WHERE wr.work_date BETWEEN '2026-08-01' AND '2026-08-31'
            ORDER BY wr.work_date, wr.id, wt.display_order;
            """);

        Assert.Contains(plan, value => value.Contains("ix_work_record_date", StringComparison.Ordinal));
        Assert.Contains(plan, value => value.Contains("ux_work_task_order", StringComparison.Ordinal));
        Assert.Equal(0L, await ScalarLongAsync(connection, """
            SELECT COUNT(*) FROM sqlite_master
            WHERE type = 'index' AND name IN ('ix_work_task_parent', 'ix_basic_shift_task_parent');
            """));
    }

    private static WorkRecordDto WorkRecord(WorkRecordId id, DateOnly date, params WorkTaskDto[] tasks) =>
        new(id, date, tasks, null, null);

    private static WorkTaskDto WorkTask(ServiceId serviceId, int order, int minutes) =>
        new(new WorkTaskId(Guid.NewGuid()), serviceId, null, WorkInputMode.Duration,
            new WorkMinutes(minutes), null, null, new DisplayOrder(order), null);

    private static BasicShiftTaskDto BasicShiftTask(ServiceId serviceId, int order, int minutes) =>
        new(new BasicShiftTaskId(Guid.NewGuid()), null, serviceId, null, WorkInputMode.Duration,
            new WorkMinutes(minutes), null, null, new DisplayOrder(order));

    private static WorkSalaryCalculation Calculate(WorkRecord record, ServiceId serviceId)
    {
        var holidayId = new HolidayCalendarVersionId(Guid.NewGuid());
        var snapshot = new SettingSnapshot(
            new SettingSnapshotId(Guid.NewGuid()),
            null,
            holidayId,
            new SchemaVersion(1),
            Now,
            [new SnapshotService(serviceId, "Service", new DisplayOrder(0), true)],
            [],
            [new SnapshotRate(serviceId, null, RateType.FixedPerRecord, new YenAmount(1000))],
            [],
            [new SnapshotCountBonus(new CountBonusId(Guid.NewGuid()), "Count", new YenAmount(150),
                new HashSet<ServiceId>(), true)]);
        return new SalaryCalculator().Calculate(new WorkSalaryCalculationRequest(
            record, snapshot, new HolidayCalendar(holidayId, new Dictionary<DateOnly, string>())));
    }

    private static async Task<long> ScalarLongAsync(SqliteConnection connection, string sql) =>
        Convert.ToInt64(await ScalarAsync(connection, sql));

    private static async Task<string> ScalarStringAsync(SqliteConnection connection, string sql) =>
        Convert.ToString(await ScalarAsync(connection, sql))!;

    private static string SqliteUtc(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'",
            System.Globalization.CultureInfo.InvariantCulture);

    private static async Task<object?> ScalarAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }

    private static async Task<IReadOnlyList<string>> QueryStringsAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var result = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) result.Add(reader.GetString(3));
        return result;
    }

    private sealed class OneShotReadOnlyList<T>(T item) : IReadOnlyList<T>
    {
        private int enumerated;

        public int Count => 1;
        public T this[int index] => index == 0 ? item : throw new ArgumentOutOfRangeException(nameof(index));

        public IEnumerator<T> GetEnumerator() =>
            Interlocked.Exchange(ref enumerated, 1) == 0
                ? ((IEnumerable<T>)[item]).GetEnumerator()
                : Enumerable.Empty<T>().GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class ParentChildDatabase : IAsyncDisposable
    {
        private readonly string root;

        private ParentChildDatabase(string root)
        {
            this.root = root;
            DatabasePath = Path.Combine(root, "test.db");
            Database = new SqliteDatabase(DatabasePath);
        }

        internal string DatabasePath { get; }
        internal SqliteDatabase Database { get; }

        internal static async Task<ParentChildDatabase> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), $"tkp-parent-child-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            var fixture = new ParentChildDatabase(root);
            await fixture.Database.InitializeAsync();
            return fixture;
        }

        internal async Task<SqliteConnection> OpenAsync()
        {
            var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = DatabasePath,
                Mode = SqliteOpenMode.ReadWrite,
                Pooling = false,
            }.ToString());
            await connection.OpenAsync();
            return connection;
        }

        internal async Task<IReadOnlyList<ServiceId>> GetServiceIdsAsync()
        {
            await using var connection = await OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT id FROM service_definition ORDER BY id LIMIT 2;";
            var result = new List<ServiceId>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) result.Add(new ServiceId(Guid.Parse(reader.GetString(0))));
            return result;
        }

        internal async Task ReplaceParentsWithVersionFiveAsync(
            Guid serviceId,
            Guid recordId,
            Guid shiftId,
            Guid? collisionRecordId = null)
        {
            SqliteConnection.ClearAllPools();
            await using var connection = await OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                PRAGMA foreign_keys = OFF;
                DROP TABLE work_task;
                DROP TABLE work_record;
                DROP TABLE basic_shift_task;
                DROP TABLE basic_shift;

                CREATE TABLE work_record (
                    id TEXT PRIMARY KEY,
                    work_date TEXT NOT NULL,
                    service_id TEXT NOT NULL,
                    time_category_id TEXT NULL,
                    input_mode TEXT NOT NULL,
                    work_minutes INTEGER NOT NULL,
                    start_time_minutes INTEGER NULL,
                    end_time_minutes INTEGER NULL,
                    source_service_preset_id TEXT NULL,
                    source_basic_shift_id TEXT NULL,
                    source_work_record_id TEXT NULL,
                    save_operation_id TEXT NULL,
                    created_at_utc TEXT NOT NULL,
                    updated_at_utc TEXT NOT NULL,
                    FOREIGN KEY (service_id) REFERENCES service_definition(id) ON DELETE RESTRICT,
                    FOREIGN KEY (time_category_id) REFERENCES time_category_definition(id) ON DELETE RESTRICT,
                    FOREIGN KEY (source_service_preset_id) REFERENCES service_preset(id) ON DELETE SET NULL
                );
                CREATE UNIQUE INDEX ux_work_record_save_operation
                    ON work_record(save_operation_id) WHERE save_operation_id IS NOT NULL;
                CREATE UNIQUE INDEX ux_work_record_shift_date
                    ON work_record(source_basic_shift_id, work_date) WHERE source_basic_shift_id IS NOT NULL;
                CREATE INDEX ix_work_record_date ON work_record(work_date);
                CREATE INDEX ix_work_record_service_date ON work_record(service_id, work_date);

                CREATE TABLE basic_shift (
                    id TEXT PRIMARY KEY,
                    weekday INTEGER NOT NULL,
                    service_preset_id TEXT NULL,
                    service_id TEXT NOT NULL,
                    time_category_id TEXT NULL,
                    input_mode TEXT NOT NULL,
                    work_minutes INTEGER NOT NULL,
                    start_time_minutes INTEGER NULL,
                    end_time_minutes INTEGER NULL,
                    display_order INTEGER NOT NULL,
                    is_enabled INTEGER NOT NULL,
                    created_at_utc TEXT NOT NULL,
                    updated_at_utc TEXT NOT NULL,
                    FOREIGN KEY (service_preset_id) REFERENCES service_preset(id) ON DELETE SET NULL,
                    FOREIGN KEY (service_id) REFERENCES service_definition(id) ON DELETE RESTRICT,
                    FOREIGN KEY (time_category_id) REFERENCES time_category_definition(id) ON DELETE RESTRICT
                );
                CREATE INDEX ix_basic_shift_weekday ON basic_shift(weekday, is_enabled, display_order);

                INSERT INTO work_record(
                    id, work_date, service_id, input_mode, work_minutes, save_operation_id,
                    created_at_utc, updated_at_utc)
                VALUES($record, '2026-08-31', $service, 'Duration', 60, $saveOperation, $now, $now);
                INSERT INTO basic_shift(
                    id, weekday, service_id, input_mode, work_minutes, start_time_minutes, end_time_minutes,
                    display_order, is_enabled,
                    created_at_utc, updated_at_utc)
                VALUES($shift, 1, $service, 'TimeRange', 60, 540, 600, 0, 1, $now, $now);
                """ + (collisionRecordId is null ? "" : """
                INSERT INTO work_record(
                    id, work_date, service_id, input_mode, work_minutes, save_operation_id,
                    created_at_utc, updated_at_utc)
                VALUES($collision, '2026-08-30', $service, 'Duration', 30, NULL, $now, $now);
                """) + """
                PRAGMA user_version = 5;
                PRAGMA foreign_keys = ON;
                """;
            command.Parameters.AddWithValue("$record", recordId.ToString("D"));
            command.Parameters.AddWithValue("$shift", shiftId.ToString("D"));
            command.Parameters.AddWithValue("$service", serviceId.ToString("D"));
            command.Parameters.AddWithValue("$saveOperation",
                collisionRecordId is { } collision ? collision.ToString("D") : DBNull.Value);
            command.Parameters.AddWithValue("$collision",
                collisionRecordId?.ToString("D") ?? Guid.NewGuid().ToString("D"));
            command.Parameters.AddWithValue("$now", "2026-08-31T00:00:00.0000000Z");
            await command.ExecuteNonQueryAsync();
        }

        public ValueTask DisposeAsync()
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : TkpSalaryCalculator.Application.Ports.IUtcClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }
}
