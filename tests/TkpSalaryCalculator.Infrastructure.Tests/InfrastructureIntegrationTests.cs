using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using TkpSalaryCalculator.Application.Contracts;
using TkpSalaryCalculator.Application.Errors;
using TkpSalaryCalculator.Application.Ports;
using TkpSalaryCalculator.Application.UseCases;
using TkpSalaryCalculator.Domain.Models;
using TkpSalaryCalculator.Domain.ValueObjects;
using TkpSalaryCalculator.Infrastructure.DataTransfer;
using TkpSalaryCalculator.Infrastructure.Sqlite;

namespace TkpSalaryCalculator.Infrastructure.Tests;

public sealed class InfrastructureIntegrationTests
{
    [Fact]
    public async Task DB001_NewFileCreatesEveryCurrentTableIndexAndMajorForeignKey()
    {
        await using var fixture = await DatabaseFixture.CreateAsync();
        await using var connection = await fixture.OpenRawAsync();

        Assert.Equal(SqliteDatabase.CurrentSchemaVersion, await ScalarLongAsync(connection, "PRAGMA user_version;"));
        Assert.Equal("wal", Assert.IsType<string>(await ScalarAsync(connection, "PRAGMA journal_mode;")),
            StringComparer.OrdinalIgnoreCase);
        Assert.Equal(1L, await ScalarLongAsync(connection, "SELECT COUNT(*) FROM app_metadata WHERE id = 1;"));
        Assert.Equal(SqliteDatabase.CurrentBundledBootstrapVersion, await ScalarLongAsync(connection,
            "SELECT bundled_bootstrap_version FROM app_metadata WHERE id = 1;"));

        var requiredTables = new[]
        {
            "app_metadata", "setting_month", "setting_snapshot", "snapshot_service", "snapshot_time_category",
            "snapshot_rate", "snapshot_premium", "snapshot_premium_weekday", "snapshot_premium_date",
            "snapshot_premium_service", "snapshot_count_bonus", "snapshot_count_bonus_service", "service_preset",
            "basic_shift", "work_record", "closing_rule_history", "monthly_allowance", "annual_summary_setting", "holiday_calendar_version",
            "holiday_date", "service_definition", "time_category_definition", "premium_definition",
            "count_bonus_definition",
        };
        foreach (var table in requiredTables)
            Assert.Equal(1L, await ScalarLongAsync(connection,
                $"SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = '{table}';"));

        var requiredIndexes = new[]
        {
            "ix_setting_month_snapshot", "ix_snapshot_service_order", "ix_snapshot_time_category_order",
            "ux_snapshot_rate_service", "ux_snapshot_rate_time_category", "ix_snapshot_premium_snapshot",
            "ix_snapshot_count_bonus_snapshot", "ix_service_preset_order", "ix_basic_shift_weekday",
            "ix_work_record_date", "ix_work_record_service_date", "ux_work_record_shift_date",
            "ux_work_record_save_operation", "ux_closing_rule_effective_month", "ix_monthly_allowance_period",
            "ix_holiday_date_lookup",
        };
        foreach (var index in requiredIndexes)
            Assert.Equal(1L, await ScalarLongAsync(connection,
                $"SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = '{index}';"));
        Assert.Equal(0L, await ScalarLongAsync(connection, """
            SELECT COUNT(*) FROM sqlite_master
            WHERE type = 'index' AND name = 'ix_work_record_source_preset';
            """));

        var requiredForeignKeys = new[]
        {
            ("holiday_date", "holiday_calendar_version_id", "holiday_calendar_version", "id", "CASCADE"),
            ("setting_snapshot", "holiday_calendar_version_id", "holiday_calendar_version", "id", "RESTRICT"),
            ("setting_month", "snapshot_id", "setting_snapshot", "id", "RESTRICT"),
            ("snapshot_service", "snapshot_id", "setting_snapshot", "id", "CASCADE"),
            ("snapshot_service", "service_id", "service_definition", "id", "RESTRICT"),
            ("snapshot_time_category", "time_category_id", "time_category_definition", "id", "RESTRICT"),
            ("snapshot_premium", "premium_id", "premium_definition", "id", "RESTRICT"),
            ("snapshot_count_bonus", "count_bonus_id", "count_bonus_definition", "id", "RESTRICT"),
            ("service_preset", "service_id", "service_definition", "id", "RESTRICT"),
            ("basic_shift", "service_preset_id", "service_preset", "id", "SET NULL"),
            ("work_record", "source_service_preset_id", "service_preset", "id", "SET NULL"),
            ("app_metadata", "initial_snapshot_id", "setting_snapshot", "id", "RESTRICT"),
        };
        foreach (var (table, from, referencedTable, to, onDelete) in requiredForeignKeys)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA foreign_key_list({table});";
            await using var reader = await command.ExecuteReaderAsync();
            var found = false;
            while (await reader.ReadAsync())
                found |= reader.GetString(3) == from && reader.GetString(2) == referencedTable &&
                         reader.GetString(4) == to && reader.GetString(6) == onDelete;
            Assert.True(found, $"Missing FK {table}.{from} -> {referencedTable}.{to} ON DELETE {onDelete}.");
        }
    }

    [Fact]
    public async Task DB001_NewFileBootstrapsVersionedDefaultsWithoutCompletingSetup()
    {
        await using var fixture = await DatabaseFixture.CreateAsync();
        var clock = new FixedClock(new DateTimeOffset(2026, 8, 16, 0, 0, 0, TimeSpan.Zero));
        var metadata = await new SqliteAppMetadataRepository(fixture.Database, clock).GetAsync(default);
        Assert.Equal(InitialSetupStatus.NotStarted, metadata.InitialSetupStatus);
        Assert.NotNull(metadata.InitialSnapshotId);

        var snapshot = await new SqliteSettingSnapshotRepository(fixture.Database, clock)
            .FindAsync(metadata.InitialSnapshotId!.Value, default);
        Assert.NotNull(snapshot);
        Assert.Equal(["身体介護", "生活援助"], snapshot!.Services.Select(value => value.DisplayName));
        Assert.Equal(["身体0", "身体1", "身体2", "生活2", "生活3"],
            snapshot.TimeCategories.OrderBy(value => value.ServiceId == snapshot.Services[0].Id ? 0 : 1)
                .ThenBy(value => value.DisplayOrder.Value).Select(value => value.DisplayName));
        Assert.Empty(snapshot.Rates);
        var holidayPremium = Assert.Single(snapshot.Premiums);
        Assert.Equal("休日", holidayPremium.DisplayName);
        Assert.False(holidayPremium.IsEnabled);
        Assert.True(holidayPremium.UsesNationalHolidays);
        Assert.Equal(new HashSet<DayOfWeek> { DayOfWeek.Saturday, DayOfWeek.Sunday }, holidayPremium.Weekdays);

        var presets = await new SqliteServicePresetRepository(fixture.Database, clock).GetAllAsync(default);
        Assert.Equal(["身体0", "身体1", "身体2", "生活2", "生活3"], presets.Select(value => value.DisplayName));
        var calendar = await new SqliteHolidayCalendarRepository(fixture.Database)
            .GetAsync(snapshot.HolidayCalendarVersionId, default);
        Assert.Equal(35, calendar.Holidays.Count);
        Assert.Equal("休日", calendar.Holidays[new DateOnly(2026, 5, 6)]);
        Assert.Equal("休日", calendar.Holidays[new DateOnly(2026, 9, 22)]);
        Assert.Equal("休日", calendar.Holidays[new DateOnly(2027, 3, 22)]);
        var calendars = await new SqliteHolidayCalendarRepository(fixture.Database)
            .GetManyAsync([snapshot.HolidayCalendarVersionId], default);
        Assert.Equal(calendar.Holidays, calendars[snapshot.HolidayCalendarVersionId].Holidays);
        Assert.Empty(await new SqliteClosingRuleRepository(fixture.Database, clock).GetHistoryAsync(default));
        var annualSummary = new SqliteAnnualSummarySettingRepository(fixture.Database, clock);
        Assert.Equal(12, (await annualSummary.GetClosingMonthAsync(default)).Value);
    }

    [Fact]
    public async Task AnnualSummarySettingPersistsValidMonthAndDatabaseRejectsInvalidMonth()
    {
        await using var fixture = await DatabaseFixture.CreateAsync();
        var clock = new FixedClock(new DateTimeOffset(2026, 8, 29, 4, 0, 0, TimeSpan.Zero));
        var repository = new SqliteAnnualSummarySettingRepository(fixture.Database, clock);

        await repository.SaveClosingMonthAsync(new AnnualClosingMonth(3), default);

        Assert.Equal(3, (await repository.GetClosingMonthAsync(default)).Value);
        await using var connection = await fixture.OpenRawAsync();
        var command = connection.CreateCommand();
        command.CommandText = "UPDATE annual_summary_setting SET closing_month = 13 WHERE id = 1;";
        await Assert.ThrowsAsync<SqliteException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal(3L, await ScalarLongAsync(connection,
            "SELECT closing_month FROM annual_summary_setting WHERE id = 1;"));
    }

    [Fact]
    public async Task ExistingVersionOneWithoutDefaultsIsBackfilledIdempotently()
    {
        var root = Path.Combine(Path.GetTempPath(), $"tkp-infrastructure-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "salary.db");
        try
        {
            await new SqliteDatabase(path, bootstrapDefaults: false).InitializeAsync();
            await using (var versionOne = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                await versionOne.OpenAsync();
                await ExecuteAsync(versionOne, """
                    ALTER TABLE app_metadata DROP COLUMN bundled_bootstrap_version;
                    PRAGMA user_version = 1;
                    """);
            }
            await new SqliteDatabase(path).InitializeAsync();
            await new SqliteDatabase(path).InitializeAsync();
            await using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
            await connection.OpenAsync();
            Assert.Equal(1L, await ScalarLongAsync(connection,
                "SELECT COUNT(*) FROM app_metadata WHERE initial_snapshot_id IS NOT NULL;"));
            Assert.Equal(5L, await ScalarLongAsync(connection, "SELECT COUNT(*) FROM service_preset;"));
            Assert.Equal(35L, await ScalarLongAsync(connection, "SELECT COUNT(*) FROM holiday_date;"));
            Assert.Equal(12L, await ScalarLongAsync(connection,
                "SELECT closing_month FROM annual_summary_setting WHERE id = 1;"));
            Assert.Equal(2L, await ScalarLongAsync(connection,
                "SELECT export_format_version FROM app_metadata WHERE id = 1;"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SeededDefaultsCanBeReplacedAndInitialSetupCompletedThroughPublicPorts()
    {
        await using var fixture = await DatabaseFixture.CreateAsync();
        var clock = new FixedClock(new DateTimeOffset(2026, 8, 16, 0, 30, 0, TimeSpan.Zero));
        var metadata = new SqliteAppMetadataRepository(fixture.Database, clock);
        var settings = new SqliteSettingSnapshotRepository(fixture.Database, clock);
        var closing = new SqliteClosingRuleRepository(fixture.Database, clock);
        var initialMetadata = await metadata.GetAsync(default);
        var initial = (await settings.FindAsync(initialMetadata.InitialSnapshotId!.Value, default))!;
        var rates = initial.Services.Where(value => value.IsEnabled)
            .Select(value => new SnapshotRate(value.Id, null, RateType.Hourly, new YenAmount(1000))).ToArray();
        var replacement = new SettingSnapshotReplacementDto(initial.Services, initial.TimeCategories, rates,
            initial.Premiums, initial.CountBonuses);

        var changed = await settings.TryCloneAndReplaceMonthSnapshotAsync(new YearMonth(2026, 8), initial.Id,
            replacement, initial.HolidayCalendarVersionId, clock.UtcNow, default);
        Assert.NotNull(changed);
        var closingSnapshot = await closing.GetSnapshotAsync(default);
        Assert.True(await closing.TryReplaceEffectiveRuleAsync(new ClosingRule(new ClosingRuleId(Guid.NewGuid()),
            new PayrollPeriodKey(new YearMonth(2026, 1)), 20), closingSnapshot.Version, default));

        var result = await new InitialSetupUseCase(metadata, settings, closing,
            new SqliteTransactionRunner(fixture.Database)).CompleteAsync(default);
        Assert.Equal(InitialSetupStatus.Completed, result.Status);
        Assert.Equal(changed!.Id, (await metadata.GetAsync(default)).InitialSnapshotId);
    }

    [Fact]
    public async Task DB002_DB004_DB005_ForeignKeysUniquenessAndDeleteRestrictionsRejectBrokenRows()
    {
        await using var fixture = await DatabaseFixture.CreateSeededAsync();
        await using var connection = await fixture.OpenRawAsync();
        await ExecuteAsync(connection, "PRAGMA foreign_keys = ON;");

        await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(connection, """
            INSERT INTO work_record(id, work_date, service_id, input_mode, work_minutes,
                created_at_utc, updated_at_utc)
            VALUES('11111111-1111-1111-1111-111111111111', '2026-08-01',
                '99999999-9999-9999-9999-999999999999', 'Duration', 30,
                '2026-08-01T00:00:00.0000000Z', '2026-08-01T00:00:00.0000000Z');
            """));
        await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(connection, $"""
            INSERT INTO snapshot_rate(snapshot_id, service_id, time_category_id, rate_type, amount_yen)
            VALUES('{fixture.SnapshotId:D}', '{fixture.ServiceId:D}', NULL, 'Hourly', 2000);
            """));
        await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(connection,
            $"DELETE FROM setting_snapshot WHERE id = '{fixture.SnapshotId:D}';"));
    }

    [Fact]
    public async Task DB006_DB011_TransactionFailureRollsBackDataAndChangedTimestamp()
    {
        await using var fixture = await DatabaseFixture.CreateAsync();
        var clock = new FixedClock(new DateTimeOffset(2026, 8, 16, 0, 0, 0, TimeSpan.Zero));
        var runner = new SqliteTransactionRunner(fixture.Database);
        var allowances = new SqliteMonthlyAllowanceRepository(fixture.Database, clock);
        var metadata = new SqliteAppMetadataRepository(fixture.Database, clock);
        var allowance = new MonthlyAllowance(new MonthlyAllowanceId(Guid.NewGuid()),
            new PayrollPeriodKey(new YearMonth(2026, 8)), "test", new YenAmount(1000));

        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.ExecuteAsync(async token =>
        {
            await allowances.UpsertAsync(allowance, token);
            await metadata.SetLastDataChangedAtUtcAsync(clock.UtcNow, token);
            throw new InvalidOperationException("simulated failure");
        }, default));

        Assert.Empty(await allowances.GetForPeriodAsync(allowance.PayrollPeriodKey, default));
        Assert.Null((await metadata.GetAsync(default)).LastDataChangedAtUtc);
    }

    [Fact]
    public async Task HIST001_ChangingOneMonthClonesAndDoesNotMutateOtherMonth()
    {
        await using var fixture = await DatabaseFixture.CreateSeededAsync();
        var repository = new SqliteSettingSnapshotRepository(fixture.Database,
            new FixedClock(new DateTimeOffset(2026, 8, 16, 1, 0, 0, TimeSpan.Zero)));
        var beforeJuly = await repository.FindForMonthAsync(new YearMonth(2026, 7), default);
        var beforeAugust = await repository.FindForMonthAsync(new YearMonth(2026, 8), default);
        Assert.Equal(beforeJuly!.Id, beforeAugust!.Id);

        var replacement = new SettingSnapshotReplacementDto(beforeAugust.Services, beforeAugust.TimeCategories,
            [new SnapshotRate(new ServiceId(fixture.ServiceId), null, RateType.Hourly, new YenAmount(2500))],
            beforeAugust.Premiums, beforeAugust.CountBonuses);
        var changed = await repository.TryCloneAndReplaceMonthSnapshotAsync(new YearMonth(2026, 8), beforeAugust.Id,
            replacement, beforeAugust.HolidayCalendarVersionId,
            new DateTimeOffset(2026, 8, 16, 1, 0, 0, TimeSpan.Zero), default);

        Assert.NotNull(changed);
        Assert.NotEqual(beforeAugust.Id, changed!.Id);
        Assert.Equal(1000, (await repository.FindForMonthAsync(new YearMonth(2026, 7), default))!.Rates.Single().Amount.Value);
        Assert.Equal(2500, (await repository.FindForMonthAsync(new YearMonth(2026, 8), default))!.Rates.Single().Amount.Value);
        var effective = await repository.GetEffectiveForMonthsAsync(
            [new YearMonth(2026, 7), new YearMonth(2026, 8)], default);
        Assert.Equal(1000, effective[new YearMonth(2026, 7)].Rates.Single().Amount.Value);
        Assert.Equal(2500, effective[new YearMonth(2026, 8)].Rates.Single().Amount.Value);
    }

    [Fact]
    public async Task HIST005_ReplacementCreatesPreviouslyUnmaterializedMonthFromEffectiveSnapshot()
    {
        await using var fixture = await DatabaseFixture.CreateSeededAsync(includeAugust: false);
        var repository = new SqliteSettingSnapshotRepository(fixture.Database,
            new FixedClock(new DateTimeOffset(2026, 8, 16, 2, 0, 0, TimeSpan.Zero)));
        var effective = await repository.GetEffectiveForMonthAsync(new YearMonth(2026, 8), default);
        var replacement = new SettingSnapshotReplacementDto(effective.Services, effective.TimeCategories,
            effective.Rates, effective.Premiums, effective.CountBonuses);

        var changed = await repository.TryCloneAndReplaceMonthSnapshotAsync(new YearMonth(2026, 8), effective.Id,
            replacement, effective.HolidayCalendarVersionId,
            new DateTimeOffset(2026, 8, 16, 2, 0, 0, TimeSpan.Zero), default);

        Assert.NotNull(changed);
        Assert.Equal(changed!.Id, (await repository.FindForMonthAsync(new YearMonth(2026, 8), default))!.Id);
    }

    [Fact]
    public async Task FixedPremiumAmountUsesSqliteInt64()
    {
        await using var fixture = await DatabaseFixture.CreateSeededAsync();
        var clock = new FixedClock(new DateTimeOffset(2026, 8, 16, 2, 30, 0, TimeSpan.Zero));
        var repository = new SqliteSettingSnapshotRepository(fixture.Database, clock);
        var current = (await repository.FindForMonthAsync(new YearMonth(2026, 8), default))!;
        var premium = new SnapshotPremium(new PremiumId(Guid.NewGuid()), "Large fixed premium",
            PremiumCalculationType.FixedPerHour, null, new YenAmount(3_000_000_000L), null, null, false,
            new HashSet<DayOfWeek> { DayOfWeek.Sunday }, new HashSet<DateOnly>(), new HashSet<ServiceId>(), true);
        var replacement = new SettingSnapshotReplacementDto(current.Services, current.TimeCategories, current.Rates,
            [premium], current.CountBonuses);

        var changed = await repository.TryCloneAndReplaceMonthSnapshotAsync(new YearMonth(2026, 8), current.Id,
            replacement, current.HolidayCalendarVersionId, clock.UtcNow, default);

        Assert.Equal(3_000_000_000L, Assert.Single(changed!.Premiums).Amount!.Value.Value);
        Assert.Equal(3_000_000_000L,
            Assert.Single((await repository.FindForMonthAsync(new YearMonth(2026, 8), default))!.Premiums)
                .Amount!.Value.Value);
    }

    [Fact]
    public async Task HIST009_HIST011_NewHolidayVersionIsUsedOnlyByNewlyMaterializedMonth()
    {
        await using var fixture = await DatabaseFixture.CreateSeededAsync();
        var newerHolidayId = Guid.NewGuid();
        await using (var connection = await fixture.OpenRawAsync())
            await ExecuteAsync(connection, $"""
                INSERT INTO holiday_calendar_version(id, version_name, source_name, source_reference_date, created_at_utc)
                VALUES('{newerHolidayId:D}', 'test-v2', 'test', '2026-09-01', '2026-09-01T00:00:00.0000000Z');
                """);
        var repository = new SqliteSettingSnapshotRepository(fixture.Database,
            new FixedClock(new DateTimeOffset(2026, 9, 1, 1, 0, 0, TimeSpan.Zero)));
        var august = await repository.FindForMonthAsync(new YearMonth(2026, 8), default);

        var september = await repository.EnsureForMonthAsync(new YearMonth(2026, 9), default);

        Assert.NotEqual(august!.Id, september.Id);
        Assert.NotEqual(august.HolidayCalendarVersionId, september.HolidayCalendarVersionId);
        Assert.Equal(newerHolidayId, september.HolidayCalendarVersionId.Value);
        Assert.Equal(august.HolidayCalendarVersionId,
            (await repository.FindForMonthAsync(new YearMonth(2026, 8), default))!.HolidayCalendarVersionId);
    }

    [Fact]
    public async Task CopyPreviewCommit_OnlyMaterializesMonthWhenExpectedSettingsStillMatch()
    {
        await using var fixture = await DatabaseFixture.CreateSeededAsync();
        var newerHolidayId = Guid.NewGuid();
        await using (var connection = await fixture.OpenRawAsync())
            await ExecuteAsync(connection, $"""
                INSERT INTO holiday_calendar_version(id, version_name, source_name, source_reference_date, created_at_utc)
                VALUES('{newerHolidayId:D}', 'test-v2', 'test', '2026-09-01', '2026-09-01T00:00:00.0000000Z');
                """);
        var repository = new SqliteSettingSnapshotRepository(fixture.Database,
            new FixedClock(new DateTimeOffset(2026, 9, 1, 1, 0, 0, TimeSpan.Zero)));
        var august = (await repository.FindForMonthAsync(new YearMonth(2026, 8), default))!;

        var september = await repository.TryEnsureForMonthAsync(
            new YearMonth(2026, 9), august.Id, new HolidayCalendarVersionId(newerHolidayId), default);
        var newestHolidayId = Guid.NewGuid();
        await using (var connection = await fixture.OpenRawAsync())
            await ExecuteAsync(connection, $"""
                INSERT INTO holiday_calendar_version(id, version_name, source_name, source_reference_date, created_at_utc)
                VALUES('{newestHolidayId:D}', 'test-v3', 'test', '2026-09-02', '2026-09-02T00:00:00.0000000Z');
                """);
        var staleOctober = await repository.TryEnsureForMonthAsync(
            new YearMonth(2026, 10), september!.Id, new HolidayCalendarVersionId(newerHolidayId), default);

        Assert.NotNull(september);
        Assert.Equal(newerHolidayId, september!.HolidayCalendarVersionId.Value);
        Assert.Equal(september.Id, (await repository.FindForMonthAsync(new YearMonth(2026, 9), default))!.Id);
        Assert.Null(staleOctober);
        Assert.Null(await repository.FindForMonthAsync(new YearMonth(2026, 10), default));
    }

    [Fact]
    public async Task WORK007_SaveOperationIdIsDurableAndIdempotentAcrossReopen()
    {
        await using var fixture = await DatabaseFixture.CreateSeededAsync();
        var clock = new FixedClock(new DateTimeOffset(2026, 8, 16, 3, 0, 0, TimeSpan.Zero));
        var operation = Guid.NewGuid();
        var record = new WorkRecordDto(new WorkRecordId(Guid.NewGuid()), new DateOnly(2026, 8, 10),
            new ServiceId(fixture.ServiceId), null, WorkInputMode.Duration, new WorkMinutes(30), null, null,
            null, null, null);
        var repository = new SqliteWorkRecordRepository(fixture.Database, clock);

        Assert.True(await repository.TryInsertAsync(record, operation, default));
        Assert.False(await repository.TryInsertAsync(record with { Id = new WorkRecordId(Guid.NewGuid()) }, operation, default));

        var reopened = new SqliteDatabase(fixture.DatabasePath);
        var reopenedRepository = new SqliteWorkRecordRepository(reopened, clock);
        Assert.Equal(record.Id, (await reopenedRepository.FindBySaveOperationIdAsync(operation, default))!.Id);
    }

    [Fact]
    public async Task DB007_VersionThreeToCurrentRemovesUnusedHistoryIndexAndPreservesWorkRecords()
    {
        await using var fixture = await DatabaseFixture.CreateSeededAsync();
        var clock = new FixedClock(new DateTimeOffset(2026, 8, 16, 3, 0, 0, TimeSpan.Zero));
        var preset = new ServicePresetDto(new ServicePresetId(Guid.NewGuid()), "Migration preset",
            new ServiceId(fixture.ServiceId), null, new WorkMinutes(60), new DisplayOrder(0), true);
        await new SqliteServicePresetRepository(fixture.Database, clock).UpsertAsync(preset, default);
        var record = new WorkRecordDto(new WorkRecordId(Guid.NewGuid()), new DateOnly(2026, 8, 10),
            preset.ServiceId, null, WorkInputMode.Duration, new WorkMinutes(60), null, null,
            preset.Id, null, null);
        await new SqliteWorkRecordRepository(fixture.Database, clock).UpsertAsync(record, default);
        await using (var versionThree = await fixture.OpenRawAsync())
            await ExecuteAsync(versionThree, """
                CREATE INDEX ix_work_record_source_preset
                    ON work_record(source_service_preset_id)
                    WHERE source_service_preset_id IS NOT NULL;
                PRAGMA user_version = 3;
                """);

        var migrated = new SqliteDatabase(fixture.DatabasePath);
        await migrated.InitializeAsync();

        await using var connection = await fixture.OpenRawAsync();
        Assert.Equal(SqliteDatabase.CurrentSchemaVersion, await ScalarLongAsync(connection, "PRAGMA user_version;"));
        Assert.Equal(SqliteDatabase.CurrentBundledBootstrapVersion, await ScalarLongAsync(connection,
            "SELECT bundled_bootstrap_version FROM app_metadata WHERE id = 1;"));
        Assert.Equal(0L, await ScalarLongAsync(connection, """
            SELECT COUNT(*) FROM sqlite_master
            WHERE type = 'index' AND name = 'ix_work_record_source_preset';
            """));
        Assert.Equal(record, await new SqliteWorkRecordRepository(migrated, clock).FindAsync(record.Id, default));
    }

    [Fact]
    public async Task RepositoryPortsPersistCurrentTemplatesRulesAndAllowances()
    {
        await using var fixture = await DatabaseFixture.CreateSeededAsync();
        var clock = new FixedClock(new DateTimeOffset(2026, 8, 16, 3, 30, 0, TimeSpan.Zero));
        var presets = new SqliteServicePresetRepository(fixture.Database, clock);
        var preset = new ServicePresetDto(new ServicePresetId(Guid.NewGuid()), "Preset",
            new ServiceId(fixture.ServiceId), null, new WorkMinutes(60), new DisplayOrder(2), true);
        await presets.UpsertAsync(preset, default);
        Assert.Contains(preset, await presets.GetAllAsync(default));

        var shifts = new SqliteBasicShiftRepository(fixture.Database, clock);
        var shift = new BasicShiftDto(new BasicShiftId(Guid.NewGuid()), DayOfWeek.Monday, preset.Id,
            preset.ServiceId, null, WorkInputMode.TimeRange, new WorkMinutes(60), new MinuteOfDay(540),
            new MinuteOfDay(600), new DisplayOrder(0), true);
        await shifts.UpsertAsync(shift, default);
        Assert.Equal(shift, Assert.Single(await shifts.GetForWeekdayAsync(DayOfWeek.Monday, default)));
        var shiftsByWeekday = await shifts.GetForWeekdaysAsync(
            [DayOfWeek.Monday, DayOfWeek.Tuesday], default);
        Assert.Equal(shift, Assert.Single(shiftsByWeekday[DayOfWeek.Monday]));
        Assert.Empty(shiftsByWeekday[DayOfWeek.Tuesday]);

        var closing = new SqliteClosingRuleRepository(fixture.Database, clock);
        var snapshot = await closing.GetSnapshotAsync(default);
        var replacement = new ClosingRule(new ClosingRuleId(Guid.NewGuid()),
            new PayrollPeriodKey(new YearMonth(2026, 8)), 15);
        Assert.True(await closing.TryReplaceEffectiveRuleAsync(replacement, snapshot.Version, default));
        Assert.False(await closing.TryReplaceEffectiveRuleAsync(replacement, snapshot.Version, default));

        var allowances = new SqliteMonthlyAllowanceRepository(fixture.Database, clock);
        var allowance = new MonthlyAllowance(new MonthlyAllowanceId(Guid.NewGuid()), replacement.EffectiveFrom,
            "Allowance", new YenAmount(5000));
        await allowances.UpsertAsync(allowance, default);
        Assert.Equal(allowance, Assert.Single(await allowances.GetForPeriodAsync(replacement.EffectiveFrom, default)));

        await presets.DeleteAsync(preset.Id, default);
        Assert.Null((await shifts.FindAsync(shift.Id, default))!.ServicePresetId);
        await shifts.DeleteAsync(shift.Id, default);
        await allowances.DeleteAsync(allowance.Id, default);
        Assert.Empty(await allowances.GetForPeriodAsync(replacement.EffectiveFrom, default));
    }

    [Fact]
    public async Task ANNUALINFRA001_MonthlyAllowancesAreReadForTheInclusivePeriodRangeInOneQuery()
    {
        await using var fixture = await DatabaseFixture.CreateAsync();
        var clock = new FixedClock(new DateTimeOffset(2026, 8, 16, 0, 0, 0, TimeSpan.Zero));
        var repository = new SqliteMonthlyAllowanceRepository(fixture.Database, clock);
        foreach (var (year, month, amount) in new[]
                 {
                     (2025, 12, 12L),
                     (2026, 1, 1L),
                     (2026, 8, 8L),
                     (2026, 9, 9L),
                 })
        {
            await repository.UpsertAsync(new MonthlyAllowance(
                new MonthlyAllowanceId(Guid.NewGuid()),
                new PayrollPeriodKey(new YearMonth(year, month)),
                $"{month}月手当",
                new YenAmount(amount)), default);
        }

        var values = await repository.GetForRangeAsync(
            new PayrollPeriodKey(new YearMonth(2026, 1)),
            new PayrollPeriodKey(new YearMonth(2026, 8)),
            default);

        Assert.Equal([new YearMonth(2026, 1), new YearMonth(2026, 8)],
            values.Select(static value => value.PayrollPeriodKey.Value));
        Assert.Equal([1L, 8L], values.Select(static value => value.Amount.Value));
        await Assert.ThrowsAsync<ArgumentException>(() => repository.GetForRangeAsync(
            new PayrollPeriodKey(new YearMonth(2026, 8)),
            new PayrollPeriodKey(new YearMonth(2026, 1)),
            default));
    }

    [Fact]
    public async Task DATA001_StreamingJsonRoundTripProducesHeaderThenRecords()
    {
        var value = JsonSerializer.SerializeToElement(new Dictionary<string, object?>
        {
            ["type"] = "work_record",
            ["id"] = "11111111-1111-1111-1111-111111111111",
        });
        var output = new MemoryStream();
        var writer = new StreamingJsonExportStream();
        await writer.WriteAsync(output, new ExportDocumentHeader("tkp-salary-calculator", 1,
            new DateTimeOffset(2026, 8, 16, 0, 0, 0, TimeSpan.Zero), "1.0.0"),
            One(new DataTransferRecord<JsonElement>(DataTransferSection.WorkRecords, 0, value)), default);
        Assert.True(output.Length > 0);
        output.Position = 0;

        var records = new List<DataTransferRecord>();
        await foreach (var record in new StreamingJsonImportStream().ReadAsync(output, default)) records.Add(record);
        Assert.Equal(2, records.Count);
        Assert.Equal(DataTransferSection.Metadata, records[0].Section);
        Assert.Equal("document_header", Assert.IsType<DataTransferRecord<JsonElement>>(records[0]).Value
            .GetProperty("type").GetString());
        Assert.Equal(DataTransferSection.WorkRecords, records[1].Section);
    }

    [Fact]
    public async Task StreamingJsonRoundTripSupportsTokensLargerThanPrevious128KiBLimit()
    {
        var largeText = new string('x', 256 * 1024);
        var value = JsonSerializer.SerializeToElement(new Dictionary<string, object?>
        {
            ["type"] = "service_preset",
            ["display_name"] = largeText,
        });
        var stream = new MemoryStream();
        await new StreamingJsonExportStream().WriteAsync(stream,
            new ExportDocumentHeader("tkp-salary-calculator", 1,
                new DateTimeOffset(2026, 8, 16, 0, 0, 0, TimeSpan.Zero), largeText),
            One(new DataTransferRecord<JsonElement>(DataTransferSection.ServicePresets, 0, value)), default);
        stream.Position = 0;

        var records = new List<DataTransferRecord<JsonElement>>();
        await foreach (var record in new StreamingJsonImportStream().ReadAsync(stream, default))
            records.Add(Assert.IsType<DataTransferRecord<JsonElement>>(record));
        Assert.Equal(largeText, records[0].Value.GetProperty("appVersion").GetString());
        Assert.Equal(largeText, records[1].Value.GetProperty("display_name").GetString());
    }

    [Fact]
    public async Task DATA009_DATA011_ExportImportReplacesAndReproducesOnlyReferencedSnapshotsAndHolidays()
    {
        await using var source = await DatabaseFixture.CreateSeededAsync();
        var clock = new FixedClock(new DateTimeOffset(2026, 8, 16, 4, 0, 0, TimeSpan.Zero));
        var unreferencedSnapshotId = Guid.NewGuid();
        await using (var sourceConnection = await source.OpenRawAsync())
        {
            await ExecuteAsync(sourceConnection, $"""
                INSERT INTO setting_snapshot(id, based_on_id, holiday_calendar_version_id, schema_version, created_at_utc)
                SELECT '{unreferencedSnapshotId:D}', NULL, id, 1, '2026-08-02T00:00:00.0000000Z'
                FROM holiday_calendar_version LIMIT 1;
                """);
        }
        var sourceRecords = new SqliteWorkRecordRepository(source.Database, clock);
        var saveOperationId = Guid.NewGuid();
        var work = new WorkRecordDto(new WorkRecordId(Guid.NewGuid()), new DateOnly(2026, 8, 12),
            new ServiceId(source.ServiceId), null, WorkInputMode.Duration, new WorkMinutes(45), null, null,
            null, null, null);
        Assert.True(await sourceRecords.TryInsertAsync(work, saveOperationId, default));

        var sourceTransfer = CreateTransferUseCase(source.Database, source.StagingPath, clock);
        var stream = new MemoryStream();
        await sourceTransfer.ExportAsync(stream, "1.0.0", default);

        await using var destination = await DatabaseFixture.CreateAsync();
        var destinationTransfer = CreateTransferUseCase(destination.Database, destination.StagingPath, clock);
        await using var input = new MemoryStream(stream.ToArray());
        var preview = await destinationTransfer.PrepareImportAsync(input, default);
        Assert.Equal(1, preview.WorkRecordCount);
        Assert.Equal(2, preview.SettingMonthCount);
        await destinationTransfer.CommitImportAsync(preview.Id, default);

        var importedRecords = new SqliteWorkRecordRepository(destination.Database, clock);
        Assert.Equal(work, await importedRecords.FindAsync(work.Id, default));
        Assert.Equal(work.Id, (await importedRecords.FindBySaveOperationIdAsync(saveOperationId, default))!.Id);
        var importedSettings = new SqliteSettingSnapshotRepository(destination.Database, clock);
        var importedSnapshot = await importedSettings.FindForMonthAsync(new YearMonth(2026, 8), default);
        Assert.Equal(source.SnapshotId, importedSnapshot!.Id.Value);
        Assert.Null(await importedSettings.FindAsync(new SettingSnapshotId(unreferencedSnapshotId), default));
        var importedHoliday = await new SqliteHolidayCalendarRepository(destination.Database)
            .GetAsync(importedSnapshot.HolidayCalendarVersionId, default);
        Assert.Equal("Holiday", importedHoliday.Holidays[new DateOnly(2026, 8, 11)]);
        Assert.Equal(InitialSetupStatus.Completed,
            (await new SqliteAppMetadataRepository(destination.Database, clock).GetAsync(default)).InitialSetupStatus);
    }

    [Fact]
    public async Task DATA011_ExactBundledHolidayReservationRoundTripsWithoutDateMixing()
    {
        await using var source = await DatabaseFixture.CreateSeededAsync();
        await using (var connection = await source.OpenRawAsync())
            await ExecuteAsync(connection, $"""
                UPDATE setting_snapshot
                SET holiday_calendar_version_id = '10000000-0000-4000-8000-000000000001'
                WHERE id = '{source.SnapshotId:D}';
                """);
        var clock = new FixedClock(new DateTimeOffset(2026, 8, 16, 4, 30, 0, TimeSpan.Zero));
        var exported = new MemoryStream();
        await CreateTransferUseCase(source.Database, source.StagingPath, clock).ExportAsync(exported, "1.0.0", default);

        await using var destination = await DatabaseFixture.CreateAsync();
        var useCase = CreateTransferUseCase(destination.Database, destination.StagingPath, clock);
        await using var input = new MemoryStream(exported.ToArray());
        var preview = await useCase.PrepareImportAsync(input, default);
        await useCase.CommitImportAsync(preview.Id, default);

        await using var imported = await destination.OpenRawAsync();
        Assert.Equal(35L, await ScalarLongAsync(imported, """
            SELECT COUNT(*) FROM holiday_date
            WHERE holiday_calendar_version_id = '10000000-0000-4000-8000-000000000001';
            """));
        Assert.Equal("休日", Assert.IsType<string>(await ScalarAsync(imported, """
            SELECT display_name FROM holiday_date
            WHERE holiday_calendar_version_id = '10000000-0000-4000-8000-000000000001'
                AND holiday_date = '2026-09-22';
            """)));
    }

    [Fact]
    public async Task DATA003_DATA004_InvalidOrUnsupportedDocumentDoesNotChangeLiveData()
    {
        await using var fixture = await DatabaseFixture.CreateSeededAsync();
        var clock = new FixedClock(new DateTimeOffset(2026, 8, 16, 5, 0, 0, TimeSpan.Zero));
        var records = new SqliteWorkRecordRepository(fixture.Database, clock);
        var existing = new WorkRecordDto(new WorkRecordId(Guid.NewGuid()), new DateOnly(2026, 8, 13),
            new ServiceId(fixture.ServiceId), null, WorkInputMode.Duration, new WorkMinutes(30), null, null,
            null, null, null);
        await records.UpsertAsync(existing, default);
        var useCase = CreateTransferUseCase(fixture.Database, fixture.StagingPath, clock);

        await Assert.ThrowsAnyAsync<Exception>(() =>
            useCase.PrepareImportAsync(new MemoryStream("{ invalid json"u8.ToArray()), default));
        Assert.NotNull(await records.FindAsync(existing.Id, default));

        var unsupported = new MemoryStream();
        await new StreamingJsonExportStream().WriteAsync(unsupported,
            new ExportDocumentHeader("tkp-salary-calculator", 99, clock.UtcNow, "99.0"),
            EmptyRecords(), default);
        unsupported.Position = 0;
        await Assert.ThrowsAnyAsync<Exception>(() => useCase.PrepareImportAsync(unsupported, default));
        Assert.NotNull(await records.FindAsync(existing.Id, default));
    }

    [Fact]
    public async Task LegacyVersionOneImportBackfillsDecemberAnnualClosingMonth()
    {
        await using var source = await DatabaseFixture.CreateSeededAsync();
        var clock = new FixedClock(new DateTimeOffset(2026, 8, 29, 5, 0, 0, TimeSpan.Zero));
        await new SqliteAnnualSummarySettingRepository(source.Database, clock)
            .SaveClosingMonthAsync(new AnnualClosingMonth(3), default);
        var exported = new MemoryStream();
        await CreateTransferUseCase(source.Database, source.StagingPath, clock)
            .ExportAsync(exported, "2.0.0", default);
        var root = JsonNode.Parse(exported.ToArray())!.AsObject();
        root["formatVersion"] = 1;
        var data = root["data"]!.AsArray();
        FindValue(data, "app_metadata")["export_format_version"] = 1;
        RemoveRecords(data, "annual_summary_setting");

        await using var destination = await DatabaseFixture.CreateSeededAsync();
        var transfer = CreateTransferUseCase(destination.Database, destination.StagingPath, clock);
        var preview = await transfer.PrepareImportAsync(
            new MemoryStream(Encoding.UTF8.GetBytes(root.ToJsonString())), default);
        await transfer.CommitImportAsync(preview.Id, default);

        Assert.Equal(1, preview.FormatVersion);
        Assert.Equal(12, (await new SqliteAnnualSummarySettingRepository(destination.Database, clock)
            .GetClosingMonthAsync(default)).Value);
        Assert.Equal(2, (await new SqliteAppMetadataRepository(destination.Database, clock)
            .GetAsync(default)).ExportFormatVersion);
    }

    [Fact]
    public async Task DATA004_DATA005_ImportRejectsIncompleteVersionsAndMalformedIdsWithoutChangingLiveData()
    {
        await using var source = await DatabaseFixture.CreateSeededAsync();
        var clock = new FixedClock(new DateTimeOffset(2026, 8, 16, 5, 30, 0, TimeSpan.Zero));
        var sourceRecord = new WorkRecordDto(new WorkRecordId(Guid.NewGuid()), new DateOnly(2026, 8, 14),
            new ServiceId(source.ServiceId), null, WorkInputMode.Duration, new WorkMinutes(30), null, null,
            null, null, new WorkRecordId(Guid.NewGuid()));
        await new SqliteWorkRecordRepository(source.Database, clock).UpsertAsync(sourceRecord, default);
        var exported = new MemoryStream();
        await CreateTransferUseCase(source.Database, source.StagingPath, clock).ExportAsync(exported, "1.0.0", default);
        var validBytes = exported.ToArray();

        await using var destination = await DatabaseFixture.CreateSeededAsync();
        var destinationRecords = new SqliteWorkRecordRepository(destination.Database, clock);
        var existing = new WorkRecordDto(new WorkRecordId(Guid.NewGuid()), new DateOnly(2026, 8, 15),
            new ServiceId(destination.ServiceId), null, WorkInputMode.Duration, new WorkMinutes(45), null, null,
            null, null, null);
        await destinationRecords.UpsertAsync(existing, default);
        var useCase = CreateTransferUseCase(destination.Database, destination.StagingPath, clock);
        var invalidId = "zzzzzzzz-zzzz-zzzz-zzzz-zzzzzzzzzzzz";

        var mutations = new Action<JsonArray>[]
        {
            // No closing rule means the forced Completed state would be inconsistent.
            data => RemoveRecords(data, "closing_rule_history"),
            data => RemoveRecords(data, "annual_summary_setting"),
            data => FindValue(data, "annual_summary_setting")["closing_month"] = 13,
            data =>
            {
                var duplicate = data.Single(item =>
                    item!["value"]!["type"]!.GetValue<string>() == "annual_summary_setting")!.DeepClone();
                data.Add(duplicate);
            },
            // Initial snapshot has no applicable rate after this removal.
            data => RemoveRecords(data, "snapshot_rate"),
            data => FindValue(data, "app_metadata")["export_format_version"] = 3,
            data => FindValue(data, "setting_snapshot")["schema_version"] = 2,
            // Unreferenced holiday IDs must also be canonical UUIDs.
            data => data.Add(new JsonObject
            {
                ["section"] = DataTransferSection.Holidays.ToString(),
                ["sequence"] = 0,
                ["value"] = new JsonObject
                {
                    ["type"] = "holiday_calendar_version",
                    ["id"] = invalidId,
                    ["version_name"] = "invalid-id-version",
                    ["source_name"] = "test",
                    ["source_reference_date"] = "2026-08-16",
                    ["created_at_utc"] = "2026-08-16T00:00:00.0000000Z",
                },
            }),
            // Origin IDs have no foreign key, but are still UUID contracts.
            data => FindValue(data, "work_record")["source_work_record_id"] = invalidId,
            // The bundled calendar ID is reserved even when an imported snapshot does not reference it.
            data => AddHolidayVersion(data, "conflicting-version", "other source", "2026-08-15"),
            // Matching metadata is insufficient: the immutable date/name set must also be complete.
            data => AddHolidayVersion(data, "cao-jp-holidays-2026-2027-20260816-v1",
                "内閣府『国民の祝日について』公式CSV", "2026-08-16"),
        };

        foreach (var mutation in mutations)
        {
            var input = MutateExport(validBytes, mutation);
            await Assert.ThrowsAnyAsync<Exception>(() => useCase.PrepareImportAsync(input, default));
            Assert.Equal(existing, await destinationRecords.FindAsync(existing.Id, default));
        }
    }

    [Fact]
    public async Task DATA004_ImportRejectsEmptyGuidInGeneralIdValidationBeforeDomainConversion()
    {
        await using var source = await DatabaseFixture.CreateSeededAsync();
        var clock = new FixedClock(new DateTimeOffset(2026, 8, 16, 5, 45, 0, TimeSpan.Zero));
        var exported = new MemoryStream();
        await CreateTransferUseCase(source.Database, source.StagingPath, clock).ExportAsync(exported, "1.0.0", default);
        var input = MutateExport(exported.ToArray(),
            data => FindValue(data, "service_preset")["id"] = Guid.Empty.ToString("D"));

        await using var destination = await DatabaseFixture.CreateSeededAsync();
        var useCase = CreateTransferUseCase(destination.Database, destination.StagingPath, clock);

        var exception = await Assert.ThrowsAsync<ApplicationErrorException>(() =>
            useCase.PrepareImportAsync(input, default));
        Assert.Equal("IMPORT_INVALID", exception.Code);
        var cause = Assert.IsType<InvalidDataException>(exception.InnerException);
        Assert.Contains("service_preset.id", cause.Message, StringComparison.Ordinal);
        Assert.Contains("non-canonical UUID", cause.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DATA005_CommitRevalidatesReservedBundledHolidayIdAndRollsBackLiveData()
    {
        await using var source = await DatabaseFixture.CreateSeededAsync();
        var clock = new FixedClock(new DateTimeOffset(2026, 8, 16, 6, 0, 0, TimeSpan.Zero));
        var exported = new MemoryStream();
        await CreateTransferUseCase(source.Database, source.StagingPath, clock).ExportAsync(exported, "1.0.0", default);

        await using var destination = await DatabaseFixture.CreateSeededAsync();
        var liveRecords = new SqliteWorkRecordRepository(destination.Database, clock);
        var existing = new WorkRecordDto(new WorkRecordId(Guid.NewGuid()), new DateOnly(2026, 8, 15),
            new ServiceId(destination.ServiceId), null, WorkInputMode.Duration, new WorkMinutes(45), null, null,
            null, null, null);
        await liveRecords.UpsertAsync(existing, default);
        var useCase = CreateTransferUseCase(destination.Database, destination.StagingPath, clock);
        await using var input = new MemoryStream(exported.ToArray());
        var preview = await useCase.PrepareImportAsync(input, default);

        var candidatePath = Path.Combine(destination.StagingPath,
            $"tkp-import-{preview.Id.Value:N}.candidate.db");
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
        Assert.Equal(existing, await liveRecords.FindAsync(existing.Id, default));
        await useCase.DiscardImportAsync(preview.Id, default);
    }

    [Fact]
    public async Task DATA007_PreparedImportRejectsAmbientTransactionAndRetainsSingleConsumerState()
    {
        await using var source = await DatabaseFixture.CreateSeededAsync();
        var clock = new FixedClock(new DateTimeOffset(2026, 8, 16, 6, 30, 0, TimeSpan.Zero));
        var exported = new MemoryStream();
        await CreateTransferUseCase(source.Database, source.StagingPath, clock).ExportAsync(exported, "1.0.0", default);
        var bytes = exported.ToArray();

        await using var destination = await DatabaseFixture.CreateSeededAsync();
        var staging = new SqliteImportStagingRepository(destination.Database, destination.StagingPath, clock);
        var useCase = new DataTransferUseCase(new StreamingJsonExportStream(), new StreamingJsonImportStream(),
            new SqliteExportDataSource(destination.Database), staging,
            new SqliteAppMetadataRepository(destination.Database, clock),
            new SqliteTransactionRunner(destination.Database), clock);
        var preview = await useCase.PrepareImportAsync(new MemoryStream(bytes), default);

        await Assert.ThrowsAsync<InvalidOperationException>(() => new SqliteTransactionRunner(destination.Database)
            .ExecuteAsync(async token =>
            {
                Assert.True(await staging.TryConsumeAndReplaceLiveDataAsync(preview.Id, clock.UtcNow, token));
            }, default));

        var attempts = await Task.WhenAll(
            staging.TryConsumeAndReplaceLiveDataAsync(preview.Id, clock.UtcNow, default),
            staging.TryConsumeAndReplaceLiveDataAsync(preview.Id, clock.UtcNow, default));
        Assert.Single(attempts, value => value);
        Assert.False(await staging.TryConsumeAndReplaceLiveDataAsync(preview.Id, clock.UtcNow, default));

        await staging.DiscardAsync(preview.Id, default);
        Assert.Empty(Directory.EnumerateFiles(destination.StagingPath, "*", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public void LocalDateConverterUsesConfiguredDeviceTimeZone()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("test", TimeSpan.FromHours(9), "test", "test");
        var converter = new TimeZoneLocalDateConverter(zone);
        Assert.Equal(new DateOnly(2026, 8, 17),
            converter.ToLocalDate(new DateTimeOffset(2026, 8, 16, 15, 30, 0, TimeSpan.Zero)));
    }

    private static DataTransferUseCase CreateTransferUseCase(SqliteDatabase database, string stagingPath, IUtcClock clock) =>
        new(new StreamingJsonExportStream(), new StreamingJsonImportStream(), new SqliteExportDataSource(database),
            new SqliteImportStagingRepository(database, stagingPath, clock),
            new SqliteAppMetadataRepository(database, clock), new SqliteTransactionRunner(database), clock);

    private static async IAsyncEnumerable<DataTransferRecord> One(DataTransferRecord record)
    {
        await Task.Yield();
        yield return record;
    }

    private static async IAsyncEnumerable<DataTransferRecord> EmptyRecords()
    {
        await Task.CompletedTask;
        yield break;
    }

    private static MemoryStream MutateExport(byte[] bytes, Action<JsonArray> mutation)
    {
        var root = JsonNode.Parse(bytes)!.AsObject();
        var data = root["data"]!.AsArray();
        mutation(data);
        var nextSequence = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var item in data)
        {
            var envelope = item!.AsObject();
            var section = envelope["section"]!.GetValue<string>();
            if (!nextSequence.TryGetValue(section, out var sequence))
                sequence = section == DataTransferSection.Metadata.ToString() ? 1 : 0;
            envelope["sequence"] = sequence;
            nextSequence[section] = sequence + 1;
        }
        return new MemoryStream(Encoding.UTF8.GetBytes(root.ToJsonString()));
    }

    private static void RemoveRecords(JsonArray data, string type)
    {
        for (var index = data.Count - 1; index >= 0; index--)
            if (data[index]!["value"]!["type"]!.GetValue<string>() == type)
                data.RemoveAt(index);
    }

    private static JsonObject FindValue(JsonArray data, string type) => data
        .Select(item => item!["value"]!.AsObject())
        .First(value => value["type"]!.GetValue<string>() == type);

    private static void AddHolidayVersion(JsonArray data, string versionName, string sourceName,
        string referenceDate) => data.Add(new JsonObject
        {
            ["section"] = DataTransferSection.Holidays.ToString(),
            ["sequence"] = 0,
            ["value"] = new JsonObject
            {
                ["type"] = "holiday_calendar_version",
                ["id"] = "10000000-0000-4000-8000-000000000001",
                ["version_name"] = versionName,
                ["source_name"] = sourceName,
                ["source_reference_date"] = referenceDate,
                ["created_at_utc"] = "2026-08-16T00:00:00.0000000Z",
            },
        });

    private static async Task<object?> ScalarAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }

    private static async Task<long> ScalarLongAsync(SqliteConnection connection, string sql) =>
        Convert.ToInt64(await ScalarAsync(connection, sql), CultureInfo.InvariantCulture);

    private static async Task ExecuteAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private sealed class FixedClock(DateTimeOffset value) : IUtcClock
    {
        public DateTimeOffset UtcNow { get; } = value;
    }

    private sealed class DatabaseFixture : IAsyncDisposable
    {
        private DatabaseFixture(string root)
        {
            Root = root;
            DatabasePath = Path.Combine(root, "salary.db");
            StagingPath = Path.Combine(root, "staging");
            Database = new SqliteDatabase(DatabasePath);
        }

        public string Root { get; }
        public string DatabasePath { get; }
        public string StagingPath { get; }
        public SqliteDatabase Database { get; }
        public Guid ServiceId { get; private set; }
        public Guid SnapshotId { get; private set; }

        public static async Task<DatabaseFixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), $"tkp-infrastructure-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            var fixture = new DatabaseFixture(root);
            await fixture.Database.InitializeAsync();
            return fixture;
        }

        public static async Task<DatabaseFixture> CreateSeededAsync(bool includeAugust = true)
        {
            var fixture = await CreateAsync();
            fixture.ServiceId = Guid.NewGuid();
            fixture.SnapshotId = Guid.NewGuid();
            var holidayId = Guid.NewGuid();
            await using var connection = await fixture.OpenRawAsync();
            await ExecuteAsync(connection, "PRAGMA foreign_keys = ON;");
            await using var transaction = connection.BeginTransaction();
            try
            {
                var now = "2026-08-01T00:00:00.0000000Z";
                await ExecuteParameterizedAsync(connection, transaction, """
                    INSERT INTO holiday_calendar_version(id, version_name, source_name, source_reference_date, created_at_utc)
                    VALUES($holiday, 'test-v1', 'test', '2026-08-01', $now);
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
                    VALUES(202607, $snapshot, $now, $now);
                    INSERT INTO closing_rule_history(id, effective_from_year_month, closing_day, is_end_of_month, created_at_utc)
                    VALUES($closing, 202601, 20, 0, $now);
                    UPDATE app_metadata SET initial_setup_status = 'Completed', initial_snapshot_id = $snapshot,
                        updated_at_utc = $now WHERE id = 1;
                    """, new Dictionary<string, object?>
                {
                    ["$holiday"] = holidayId.ToString("D"),
                    ["$service"] = fixture.ServiceId.ToString("D"),
                    ["$snapshot"] = fixture.SnapshotId.ToString("D"),
                    ["$closing"] = Guid.NewGuid().ToString("D"),
                    ["$now"] = now,
                });
                if (includeAugust)
                    await ExecuteParameterizedAsync(connection, transaction, """
                        INSERT INTO setting_month(year_month, snapshot_id, created_at_utc, updated_at_utc)
                        VALUES(202608, $snapshot, $now, $now);
                        """, new Dictionary<string, object?>
                    {
                        ["$snapshot"] = fixture.SnapshotId.ToString("D"),
                        ["$now"] = now,
                    });
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
            return fixture;
        }

        public async Task<SqliteConnection> OpenRawAsync()
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

        public ValueTask DisposeAsync()
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
            return ValueTask.CompletedTask;
        }

        private static async Task ExecuteParameterizedAsync(SqliteConnection connection, SqliteTransaction transaction,
            string sql, IReadOnlyDictionary<string, object?> values)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            foreach (var pair in values) command.Parameters.AddWithValue(pair.Key, pair.Value ?? DBNull.Value);
            await command.ExecuteNonQueryAsync();
        }
    }
}
