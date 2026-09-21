using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using TkpSalaryCalculator.Application.Contracts;
using TkpSalaryCalculator.Application.Errors;
using TkpSalaryCalculator.Application.Ports;
using TkpSalaryCalculator.Application.UseCases;
using TkpSalaryCalculator.Domain.Services;
using TkpSalaryCalculator.Domain.ValueObjects;
using TkpSalaryCalculator.Infrastructure.Sqlite;

namespace TkpSalaryCalculator.Infrastructure.Tests;

public sealed partial class InfrastructureResilienceTests
{
    [Fact]
    public async Task DB027_DB028_SchemaSixMigrationPreservesSalaryAndReopeningPreservesOff()
    {
        await using var fixture = await TestDatabase.CreateCompleteAsync(1);
        var clock = new FixedClock(new DateTimeOffset(2026, 8, 16, 0, 0, 0, TimeSpan.Zero));
        var repository = new SqliteWorkRecordRepository(fixture.Database, clock);
        var original = (await repository.FindAsync(new WorkRecordId(TestDatabase.WorkId(0)), default))!;
        var before = await CountBonusSalaryProjectionAsync(fixture, clock);
        await using (var connection = await fixture.OpenAsync())
        {
            await ExecuteAsync(connection, """
                ALTER TABLE work_record DROP COLUMN is_count_bonus_enabled;
                UPDATE app_metadata SET export_format_version = 3;
                PRAGMA user_version = 6;
                """);
        }

        await fixture.ClearPooledConnectionPoolAsync();
        var migrated = new SqliteDatabase(fixture.DatabasePath, bootstrapDefaults: false);
        await migrated.InitializeAsync();
        repository = new SqliteWorkRecordRepository(migrated, clock);
        Assert.Equal(original, await repository.FindAsync(original.Id, default));
        Assert.Equal(before, await CountBonusSalaryProjectionAsync(fixture, clock));
        await using (var connection = await fixture.OpenAsync())
        {
            Assert.Equal(7L, await ScalarLongAsync(connection, "PRAGMA user_version;"));
            Assert.Equal(4L, await ScalarLongAsync(connection, "SELECT export_format_version FROM app_metadata;"));
            Assert.Equal(0L, await ScalarLongAsync(connection, "SELECT COUNT(*) FROM setting_snapshot WHERE schema_version <> 1;"));
            Assert.Equal(1L, await ScalarLongAsync(connection, """
                SELECT COUNT(*) FROM pragma_table_info('work_record')
                WHERE name = 'is_count_bonus_enabled' AND "notnull" = 1 AND dflt_value = '1';
                """));
        }

        var disabled = original with { IsCountBonusEnabled = false };
        await repository.UpsertAsync(disabled, default);
        var reopened = new SqliteDatabase(fixture.DatabasePath, bootstrapDefaults: false);
        await reopened.InitializeAsync();
        Assert.Equal(disabled, await new SqliteWorkRecordRepository(reopened, clock).FindAsync(original.Id, default));
    }

    [Fact]
    public async Task DB029_SchemaSevenFailureRollsBackColumnMetadataAndVersionAndCanBeRetried()
    {
        await using var fixture = await TestDatabase.CreateCompleteAsync(1);
        SqliteConnection.ClearAllPools();
        await using (var connection = await fixture.OpenAsync())
        {
            await ExecuteAsync(connection, """
                ALTER TABLE work_record DROP COLUMN is_count_bonus_enabled;
                UPDATE app_metadata SET export_format_version = 3;
                PRAGMA user_version = 6;
                CREATE TRIGGER fail_count_bonus_migration BEFORE UPDATE OF export_format_version ON app_metadata
                BEGIN SELECT RAISE(ABORT, 'migration failure'); END;
                """);
            Assert.Equal(1L, await ScalarLongAsync(connection,
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'trigger' AND name = 'fail_count_bonus_migration';"));
        }
        await fixture.ClearPooledConnectionPoolAsync();
        var migrated = new SqliteDatabase(fixture.DatabasePath, bootstrapDefaults: false);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            await Assert.ThrowsAsync<SqliteException>(() => migrated.InitializeAsync());
            await using var connection = await fixture.OpenAsync();
            Assert.Equal(6L, await ScalarLongAsync(connection, "PRAGMA user_version;"));
            Assert.Equal(3L, await ScalarLongAsync(connection, "SELECT export_format_version FROM app_metadata;"));
            Assert.Equal(0L, await ScalarLongAsync(connection,
                "SELECT COUNT(*) FROM pragma_table_info('work_record') WHERE name = 'is_count_bonus_enabled';"));
            Assert.Equal(1L, await ScalarLongAsync(connection, "SELECT COUNT(*) FROM work_record;"));
            Assert.Equal(1L, await ScalarLongAsync(connection, "SELECT COUNT(*) FROM work_task;"));
        }
        await using (var connection = await fixture.OpenAsync())
            await ExecuteAsync(connection, "DROP TRIGGER fail_count_bonus_migration;");
        await fixture.ClearPooledConnectionPoolAsync();
        await migrated.InitializeAsync();
        await using var after = await fixture.OpenAsync();
        Assert.Equal(1L, await ScalarLongAsync(after, "SELECT is_count_bonus_enabled FROM work_record;"));
        Assert.Equal(7L, await ScalarLongAsync(after, "PRAGMA user_version;"));
    }

    [Theory]
    [InlineData("NULL")]
    [InlineData("-1")]
    [InlineData("2")]
    public async Task DB030_DatabaseRejectsInvalidCountBonusValues(string invalidValue)
    {
        await using var fixture = await TestDatabase.CreateCompleteAsync(1);
        await using var connection = await fixture.OpenAsync();
        Assert.Equal(1L, await ScalarLongAsync(connection, "SELECT is_count_bonus_enabled FROM work_record;"));
        await ExecuteAsync(connection, "UPDATE work_record SET is_count_bonus_enabled = 0;");
        await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(connection,
            $"UPDATE work_record SET is_count_bonus_enabled = {invalidValue};"));
        Assert.Equal(0L, await ScalarLongAsync(connection, "SELECT is_count_bonus_enabled FROM work_record;"));
        await ExecuteAsync(connection, "UPDATE work_record SET is_count_bonus_enabled = 1;");
    }

    [Fact]
    public async Task DATA024_FormatFourRoundTripPreservesMixedSelectionsTasksAndSalary()
    {
        await using var source = await TestDatabase.CreateCompleteAsync(1);
        var clock = new FixedClock(new DateTimeOffset(2026, 8, 16, 0, 0, 0, TimeSpan.Zero));
        var repository = new SqliteWorkRecordRepository(source.Database, clock);
        var original = (await repository.FindAsync(new WorkRecordId(TestDatabase.WorkId(0)), default))!;
        var disabled = original with
        {
            Id = new WorkRecordId(Guid.NewGuid()),
            IsCountBonusEnabled = false,
            SourceBasicShiftId = null,
            SourceWorkRecordId = original.Id,
            Tasks = [
                original.Tasks[0] with { Id = new WorkTaskId(Guid.NewGuid()) },
                original.Tasks[0] with { Id = new WorkTaskId(Guid.NewGuid()), DisplayOrder = new DisplayOrder(1) },
            ],
        };
        await repository.UpsertAsync(disabled, default);
        var before = await CountBonusSalaryProjectionAsync(source, clock);
        var json = await ExportJsonAsync(source, clock);
        Assert.Equal(4, json["formatVersion"]!.GetValue<int>());
        var parents = TransferWorkRecords(json).ToArray();
        Assert.Equal(2, parents.Length);
        Assert.Contains(parents, value => value["isCountBonusEnabled"]!.GetValue<bool>());
        Assert.Contains(parents, value => !value["isCountBonusEnabled"]!.GetValue<bool>());
        Assert.All(parents, value => Assert.False(value.ContainsKey("is_count_bonus_enabled")));

        await using var destination = await TestDatabase.CreateAsync();
        var transfer = CreateTransferUseCase(destination, clock);
        var preview = await transfer.PrepareImportAsync(new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(json)), default);
        Assert.Equal(2, preview.WorkRecordCount);
        Assert.Equal(3, preview.WorkTaskCount);
        await transfer.CommitImportAsync(preview.Id, default);
        var imported = new SqliteWorkRecordRepository(destination.Database, clock);
        Assert.Equal(original, await imported.FindAsync(original.Id, default));
        Assert.Equal(disabled, await imported.FindAsync(disabled.Id, default));
        Assert.Equal(before, await CountBonusSalaryProjectionAsync(destination, clock));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("null")]
    [InlineData("\"true\"")]
    [InlineData("\"false\"")]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("future")]
    public async Task DATA025_InvalidFormatFourSelectionOrFutureVersionKeepsLiveDatabase(string invalid)
    {
        await using var source = await TestDatabase.CreateCompleteAsync(1);
        var clock = new FixedClock(new DateTimeOffset(2026, 8, 16, 0, 0, 0, TimeSpan.Zero));
        var json = await ExportJsonAsync(source, clock);
        var parent = Assert.Single(TransferWorkRecords(json));
        if (invalid == "missing") parent.Remove("isCountBonusEnabled");
        else if (invalid == "future") json["formatVersion"] = 5;
        else parent["isCountBonusEnabled"] = JsonNode.Parse(invalid);
        await using var destination = await TestDatabase.CreateSeededAsync();
        var existing = await AddLiveMarkerAsync(destination, clock);
        var metadata = await new SqliteAppMetadataRepository(destination.Database, clock).GetAsync(default);

        var exception = await Assert.ThrowsAsync<ApplicationErrorException>(() =>
            CreateTransferUseCase(destination, clock).PrepareImportAsync(
                new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(json)), default));
        Assert.IsType<InvalidDataException>(exception.InnerException);
        Assert.Equal(existing, await new SqliteWorkRecordRepository(destination.Database, clock).FindAsync(existing.Id, default));
        Assert.Equal(metadata, await new SqliteAppMetadataRepository(destination.Database, clock).GetAsync(default));
        AssertStagingEmpty(destination);
    }

    private static IEnumerable<JsonObject> TransferWorkRecords(JsonObject document) =>
        document["data"]!.AsArray().Select(item => item!["value"]!.AsObject())
            .Where(value => value["type"]!.GetValue<string>() == "work_record");

    // Compare the complete calculated breakdown and day/period/annual totals, excluding no salary fields.
    private static async Task<string> CountBonusSalaryProjectionAsync(TestDatabase fixture, IUtcClock clock)
    {
        var query = new SalaryQueryUseCase(
            new SqliteWorkRecordRepository(fixture.Database, clock),
            new SqliteSettingSnapshotRepository(fixture.Database, clock),
            new SqliteHolidayCalendarRepository(fixture.Database),
            new SqliteClosingRuleRepository(fixture.Database, clock),
            new SqliteMonthlyAllowanceRepository(fixture.Database, clock),
            new SqliteBasicShiftRepository(fixture.Database, clock),
            new SalaryCalculator(), new PayrollPeriodCalculator(),
            new SqliteAnnualSummarySettingRepository(fixture.Database, clock));
        var period = new PayrollPeriodKey(new YearMonth(2026, 8));
        return JsonSerializer.Serialize(new
        {
            Day = await query.GetDayAsync(new DateOnly(2026, 8, 11), default),
            Period = await query.GetPayrollPeriodAsync(period, default),
            Home = await query.GetHomeSalarySummaryAsync(period, default),
        });
    }
}
