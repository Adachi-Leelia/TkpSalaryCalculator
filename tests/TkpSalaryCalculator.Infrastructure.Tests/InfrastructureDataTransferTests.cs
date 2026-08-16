using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using TkpSalaryCalculator.Application.Contracts;
using TkpSalaryCalculator.Application.Ports;
using TkpSalaryCalculator.Application.UseCases;
using TkpSalaryCalculator.Domain.Models;
using TkpSalaryCalculator.Domain.Services;
using TkpSalaryCalculator.Domain.ValueObjects;
using TkpSalaryCalculator.Infrastructure.DataTransfer;
using TkpSalaryCalculator.Infrastructure.Sqlite;

namespace TkpSalaryCalculator.Infrastructure.Tests;

public sealed partial class InfrastructureResilienceTests
{
    private static readonly string[] AllTransferTableTypes =
    [
        "app_metadata", "setting_month", "setting_snapshot", "snapshot_service", "snapshot_time_category",
        "snapshot_rate", "snapshot_premium", "snapshot_premium_weekday", "snapshot_premium_date",
        "snapshot_premium_service", "snapshot_count_bonus", "snapshot_count_bonus_service", "service_preset",
        "basic_shift", "work_record", "closing_rule_history", "monthly_allowance", "holiday_calendar_version",
        "holiday_date", "service_definition", "time_category_definition", "premium_definition",
        "count_bonus_definition",
    ];

    [Fact]
    public async Task DATA002_AllTableDataAndRepresentativeSalaryBreakdownAreIdenticalAfterImportIntoAnotherDatabase()
    {
        await using var source = await TestDatabase.CreateCompleteAsync(1);
        var clock = new FixedClock(new DateTimeOffset(2026, 8, 16, 3, 0, 0, TimeSpan.Zero));
        var beforeSalary = await ReadSalaryProjectionAsync(source, clock);
        Assert.Equal(1200, beforeSalary.BasePay);
        Assert.Equal(300, beforeSalary.Premium);
        Assert.Equal(150, beforeSalary.CountBonus);
        Assert.Equal(1000, beforeSalary.Allowance);
        Assert.Equal(2650, beforeSalary.Total);

        var sourceJson = await ExportJsonAsync(source, clock);
        var sourceTypes = sourceJson["data"]!.AsArray()
            .Select(node => node!["value"]!["type"]!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(AllTransferTableTypes.Order(), sourceTypes.Order());
        AssertAllOriginIdsArePresent(sourceJson);

        await using var destination = await TestDatabase.CreateAsync();
        var destinationUseCase = CreateTransferUseCase(destination, clock);
        var input = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(sourceJson));
        var preview = await destinationUseCase.PrepareImportAsync(input, default);
        Assert.Equal(1, preview.WorkRecordCount);
        Assert.Equal(1, preview.SettingMonthCount);
        await destinationUseCase.CommitImportAsync(preview.Id, default);

        var destinationJson = await ExportJsonAsync(destination, clock);
        NormalizeTransferDocument(sourceJson);
        NormalizeTransferDocument(destinationJson);
        Assert.True(JsonNode.DeepEquals(sourceJson, destinationJson),
            $"Deep transfer mismatch.\nSource: {sourceJson}\nDestination: {destinationJson}");
        Assert.Equal(InitialSetupStatus.Completed,
            (await new SqliteAppMetadataRepository(destination.Database, clock).GetAsync(default)).InitialSetupStatus);

        var afterSalary = await ReadSalaryProjectionAsync(destination, clock);
        Assert.Equal(beforeSalary, afterSalary);
    }

    [Fact]
    public Task DATA010_DefaultCiStreamingRoundTripPreserves4096RecordsWithoutPartialReplacement() =>
        VerifyLargeStreamingRoundTripAsync(4_096);

    [Fact]
    [Trait("Category", "LongRunning")]
    [Trait("Specification", "DATA-010;PERF-006;PERF-007")]
    public Task DATA010_PERF006_PERF007_StreamingRoundTripPreserves219000RecordsWithoutPartialReplacement() =>
        VerifyLargeStreamingRoundTripAsync(219_000);

    private static async Task VerifyLargeStreamingRoundTripAsync(int recordCount)
    {
        await using var source = await TestDatabase.CreateCompleteAsync(recordCount);
        var clock = new FixedClock(new DateTimeOffset(2026, 8, 16, 4, 0, 0, TimeSpan.Zero));
        var transferPath = Path.Combine(source.Root, $"transfer-{recordCount}.json");
        var stopwatch = Stopwatch.StartNew();
        await using (var output = new FileStream(transferPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                         64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            await CreateTransferUseCase(source, clock).ExportAsync(output, "1.0.0", default);
        Assert.True(new FileInfo(transferPath).Length > recordCount);

        await using var destination = await TestDatabase.CreateSeededAsync();
        var marker = await AddLiveMarkerAsync(destination, clock);
        var useCase = CreateTransferUseCase(destination, clock);
        ImportPreviewDto preview;
        await using (var input = new FileStream(transferPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                         64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            preview = await useCase.PrepareImportAsync(input, default);

        Assert.Equal(recordCount, preview.WorkRecordCount);
        Assert.Equal(marker, await new SqliteWorkRecordRepository(destination.Database, clock)
            .FindAsync(marker.Id, default));
        await useCase.CommitImportAsync(preview.Id, default);
        stopwatch.Stop();

        Assert.Null(await new SqliteWorkRecordRepository(destination.Database, clock).FindAsync(marker.Id, default));
        await using var connection = await destination.OpenAsync();
        Assert.Equal(recordCount, await ScalarLongAsync(connection, "SELECT COUNT(*) FROM work_record;"));
        Assert.Equal("1997-01-01", await ScalarStringAsync(connection,
            "SELECT MIN(work_date) FROM work_record;"));
        Assert.Equal(new DateOnly(1997, 1, 1).AddDays((recordCount - 1) / 20)
                .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), await ScalarStringAsync(connection,
            "SELECT MAX(work_date) FROM work_record;"));
        Assert.Equal("1997-01-01", await ScalarStringAsync(connection,
            $"SELECT work_date FROM work_record WHERE id = '{TestDatabase.WorkId(0):D}';"));
        Assert.Equal(new DateOnly(1997, 1, 1).AddDays((recordCount - 1) / 20)
                .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), await ScalarStringAsync(connection,
            $"SELECT work_date FROM work_record WHERE id = '{TestDatabase.WorkId(recordCount - 1):D}';"));
        Assert.Equal(recordCount, await ScalarLongAsync(connection,
            $"SELECT COUNT(*) FROM work_record WHERE service_id = '{source.ServiceId:D}' " +
            $"AND time_category_id = '{source.CompleteTimeCategoryId:D}' AND work_minutes = 60;"));
        Assert.Equal("ok", await ScalarStringAsync(connection, "PRAGMA integrity_check;"));
        Assert.Equal(0L, await CountRowsAsync(connection, "PRAGMA foreign_key_check;"));
        Assert.True(stopwatch.Elapsed > TimeSpan.Zero);
    }

    private static async Task<JsonObject> ExportJsonAsync(TestDatabase database, IUtcClock clock)
    {
        var stream = new MemoryStream();
        await CreateTransferUseCase(database, clock).ExportAsync(stream, "1.0.0", default);
        return JsonNode.Parse(stream.ToArray())!.AsObject();
    }

    private static void NormalizeTransferDocument(JsonObject document)
    {
        foreach (var item in document["data"]!.AsArray())
        {
            var value = item!["value"]!.AsObject();
            if (value["type"]!.GetValue<string>() != "app_metadata") continue;
            value.Remove("last_exported_at_utc");
            value.Remove("last_data_changed_at_utc");
            value.Remove("updated_at_utc");
        }
    }

    private static void AssertAllOriginIdsArePresent(JsonObject document)
    {
        var work = document["data"]!.AsArray()
            .Select(node => node!["value"]!.AsObject())
            .Single(value => value["type"]!.GetValue<string>() == "work_record");
        foreach (var column in new[]
                 {
                     "source_service_preset_id", "source_basic_shift_id", "source_work_record_id",
                     "save_operation_id",
                 })
        {
            var text = work[column]!.GetValue<string>();
            Assert.True(Guid.TryParseExact(text, "D", out var id) && id != Guid.Empty,
                $"Expected canonical non-empty {column}.");
        }
    }

    private static async Task<SalaryProjection> ReadSalaryProjectionAsync(TestDatabase database, IUtcClock clock)
    {
        var query = new SalaryQueryUseCase(
            new SqliteWorkRecordRepository(database.Database, clock),
            new SqliteSettingSnapshotRepository(database.Database, clock),
            new SqliteHolidayCalendarRepository(database.Database),
            new SqliteClosingRuleRepository(database.Database, clock),
            new SqliteMonthlyAllowanceRepository(database.Database, clock),
            new SqliteBasicShiftRepository(database.Database, clock),
            new SalaryCalculator(), new PayrollPeriodCalculator());
        var summary = await query.GetPayrollPeriodAsync(new PayrollPeriodKey(new YearMonth(2026, 8)), default);
        var day = Assert.Single(summary.Days);
        var record = Assert.Single(day.Records).Calculation;
        return new SalaryProjection(summary.BasePaySubtotal.Value, summary.PremiumSubtotal.Value,
            summary.CountBonusSubtotal.Value, summary.AllowanceSubtotal.Value, summary.CalculatedSubtotal.Value,
            Assert.Single(record.Premiums).Amount.Value,
            Assert.Single(record.CountBonuses).Amount.Value, summary.UncalculatedCount);
    }

    private sealed record SalaryProjection(long BasePay, long Premium, long CountBonus, long Allowance, long Total,
        long PremiumItem, long CountBonusItem, int UncalculatedCount);

    private sealed partial class TestDatabase
    {
        private const string CompleteNow = "2026-08-01T00:00:00.0000000Z";

        public Guid CompleteSnapshotId { get; private set; }
        public Guid CompleteTimeCategoryId { get; private set; }

        public static async Task<TestDatabase> CreateCompleteAsync(int workRecordCount)
        {
            if (workRecordCount < 1) throw new ArgumentOutOfRangeException(nameof(workRecordCount));
            var fixture = CreateUninitialized(bootstrapDefaults: false);
            await fixture.Database.InitializeAsync();
            fixture.ServiceId = Guid.Parse("40000000-0000-4000-8000-000000000001");
            fixture.CompleteTimeCategoryId = Guid.Parse("40000000-0000-4000-8000-000000000002");
            fixture.CompleteSnapshotId = Guid.Parse("40000000-0000-4000-8000-000000000003");
            await fixture.InsertCompleteNonWorkRowsAsync();
            await fixture.InsertWorkRowsAsync(workRecordCount);
            return fixture;
        }

        private async Task InsertCompleteNonWorkRowsAsync()
        {
            await using var connection = await OpenAsync();
            await ExecuteAsync(connection, "PRAGMA foreign_keys = ON;");
            await using var transaction = connection.BeginTransaction(deferred: false);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO holiday_calendar_version VALUES($holiday, 'complete-v1', 'test fixture', '2026-08-16', $now);
                INSERT INTO holiday_date VALUES($holiday, '2026-08-11', 'Fixture Holiday');
                INSERT INTO service_definition VALUES($service, $now);
                INSERT INTO time_category_definition VALUES($category, $now);
                INSERT INTO premium_definition VALUES($premium, $now);
                INSERT INTO count_bonus_definition VALUES($bonus, $now);
                INSERT INTO setting_snapshot VALUES($snapshot, NULL, $holiday, 1, $now);
                INSERT INTO snapshot_service VALUES($snapshot, $service, 'Complete Service', 0, 1);
                INSERT INTO snapshot_time_category VALUES($snapshot, $category, $service, 'Complete Category', 60, 0, 1);
                INSERT INTO snapshot_rate VALUES($snapshot, $service, $category, 'Hourly', 1200);
                INSERT INTO snapshot_premium VALUES($snapshot, $premium, 'Holiday 25%', 'Percentage', 2500, NULL,
                    NULL, NULL, 1, 1);
                INSERT INTO snapshot_premium_weekday VALUES($snapshot, $premium, 2);
                INSERT INTO snapshot_premium_date VALUES($snapshot, $premium, '2026-08-11');
                INSERT INTO snapshot_premium_service VALUES($snapshot, $premium, $service);
                INSERT INTO snapshot_count_bonus VALUES($snapshot, $bonus, 'Per-record bonus', 150, 1);
                INSERT INTO snapshot_count_bonus_service VALUES($snapshot, $bonus, $service);
                INSERT INTO service_preset VALUES($preset, 'Complete Preset', $service, $category, 60, 0, 1, $now, $now);
                INSERT INTO basic_shift VALUES($shift, 2, $preset, $service, $category, 'TimeRange', 60, 540, 600,
                    0, 1, $now, $now);
                INSERT INTO closing_rule_history VALUES($closing, 199001, 20, 0, $now);
                INSERT INTO monthly_allowance VALUES($allowance, 202608, 'Complete Allowance', 1000, $now, $now);
                INSERT INTO setting_month VALUES(202608, $snapshot, $now, $now);
                UPDATE app_metadata SET initial_setup_status = 'Completed', initial_setup_step = NULL,
                    initial_snapshot_id = $snapshot, export_format_version = 1, created_at_utc = $now,
                    updated_at_utc = $now WHERE id = 1;
                """;
            Add(command, "$holiday", "40000000-0000-4000-8000-000000000004");
            Add(command, "$premium", "40000000-0000-4000-8000-000000000005");
            Add(command, "$bonus", "40000000-0000-4000-8000-000000000006");
            Add(command, "$preset", "40000000-0000-4000-8000-000000000007");
            Add(command, "$shift", "40000000-0000-4000-8000-000000000008");
            Add(command, "$closing", "40000000-0000-4000-8000-000000000009");
            Add(command, "$allowance", "40000000-0000-4000-8000-000000000010");
            Add(command, "$service", ServiceId.ToString("D"));
            Add(command, "$category", CompleteTimeCategoryId.ToString("D"));
            Add(command, "$snapshot", CompleteSnapshotId.ToString("D"));
            Add(command, "$now", CompleteNow);
            await command.ExecuteNonQueryAsync();
            await transaction.CommitAsync();
        }

        private async Task InsertWorkRowsAsync(int count)
        {
            await using var connection = await OpenAsync();
            await ExecuteAsync(connection, "PRAGMA foreign_keys = ON;");
            await using var transaction = connection.BeginTransaction(deferred: false);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO work_record(id, work_date, service_id, time_category_id, input_mode, work_minutes,
                    start_time_minutes, end_time_minutes, source_service_preset_id, source_basic_shift_id,
                    source_work_record_id, save_operation_id, created_at_utc, updated_at_utc)
                VALUES($id, $date, $service, $category, 'TimeRange', 60, 540, 600, $preset, $shift,
                    $sourceWork, $operation, $now, $now);
                """;
            var id = command.Parameters.Add("$id", SqliteType.Text);
            var date = command.Parameters.Add("$date", SqliteType.Text);
            Add(command, "$service", ServiceId.ToString("D"));
            Add(command, "$category", CompleteTimeCategoryId.ToString("D"));
            var preset = command.Parameters.Add("$preset", SqliteType.Text);
            var shift = command.Parameters.Add("$shift", SqliteType.Text);
            var sourceWork = command.Parameters.Add("$sourceWork", SqliteType.Text);
            var operation = command.Parameters.Add("$operation", SqliteType.Text);
            Add(command, "$now", CompleteNow);
            var start = new DateOnly(1997, 1, 1);
            for (var index = 0; index < count; index++)
            {
                id.Value = WorkId(index).ToString("D");
                date.Value = (count == 1 ? new DateOnly(2026, 8, 11) : start.AddDays(index / 20))
                    .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                preset.Value = index == 0 ? "40000000-0000-4000-8000-000000000007" : DBNull.Value;
                shift.Value = index == 0 ? "40000000-0000-4000-8000-000000000008" : DBNull.Value;
                sourceWork.Value = index == 0 ? "40000000-0000-4000-8000-000000000011" : DBNull.Value;
                operation.Value = index == 0 ? "40000000-0000-4000-8000-000000000012" : DBNull.Value;
                await command.ExecuteNonQueryAsync();
            }
            await transaction.CommitAsync();
        }

        public static Guid WorkId(int index)
        {
            Span<byte> bytes = stackalloc byte[16];
            BitConverter.TryWriteBytes(bytes, index + 1);
            bytes[7] = 0x40;
            bytes[8] = 0x80;
            bytes[15] = 0x50;
            return new Guid(bytes);
        }

        private static void Add(SqliteCommand command, string name, object value) =>
            command.Parameters.AddWithValue(name, value);
    }
}
