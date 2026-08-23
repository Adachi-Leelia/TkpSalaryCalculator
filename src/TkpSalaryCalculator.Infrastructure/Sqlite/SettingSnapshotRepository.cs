using Microsoft.Data.Sqlite;
using TkpSalaryCalculator.Application.Contracts;
using TkpSalaryCalculator.Application.Ports;
using TkpSalaryCalculator.Domain.Models;
using TkpSalaryCalculator.Domain.ValueObjects;

namespace TkpSalaryCalculator.Infrastructure.Sqlite;

/// <summary>変更不可な設定スナップショットと年月参照を永続化します。</summary>
public sealed class SqliteSettingSnapshotRepository(SqliteDatabase database, IUtcClock clock)
    : ISettingSnapshotRepository
{
    private readonly SqliteDatabase database = database ?? throw new ArgumentNullException(nameof(database));
    private readonly IUtcClock clock = clock ?? throw new ArgumentNullException(nameof(clock));

    public Task<SettingSnapshot?> FindAsync(SettingSnapshotId id, CancellationToken cancellationToken) =>
        database.ReadAsync((connection, transaction, token) =>
            LoadSnapshotAsync(connection, transaction, SqliteValue.Id(id.Value), token), cancellationToken);

    public Task<SettingSnapshot?> FindForMonthAsync(YearMonth yearMonth, CancellationToken cancellationToken) =>
        database.ReadAsync(async (connection, transaction, token) =>
        {
            var snapshotId = await FindSnapshotIdForExactMonthAsync(connection, transaction, yearMonth, token)
                .ConfigureAwait(false);
            return snapshotId is null
                ? null
                : await LoadSnapshotAsync(connection, transaction, snapshotId, token).ConfigureAwait(false);
        }, cancellationToken);

    public Task<SettingSnapshot> GetEffectiveForMonthAsync(YearMonth yearMonth, CancellationToken cancellationToken) =>
        database.ReadAsync(async (connection, transaction, token) =>
        {
            var snapshotId = await FindEffectiveSnapshotIdAsync(connection, transaction, yearMonth, token)
                .ConfigureAwait(false);
            if (snapshotId is null) throw new InvalidOperationException("No initial or effective setting snapshot exists.");
            return await LoadSnapshotAsync(connection, transaction, snapshotId, token).ConfigureAwait(false)
                ?? throw new InvalidDataException("The effective setting snapshot is missing.");
        }, cancellationToken);

    public Task<IReadOnlyDictionary<YearMonth, SettingSnapshot>> GetEffectiveForMonthsAsync(
        IReadOnlyCollection<YearMonth> yearMonths,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(yearMonths);
        cancellationToken.ThrowIfCancellationRequested();
        var requested = yearMonths.Distinct().OrderBy(x => x).ToArray();
        if (requested.Length == 0)
            return Task.FromResult<IReadOnlyDictionary<YearMonth, SettingSnapshot>>(
                new Dictionary<YearMonth, SettingSnapshot>());

        return database.ReadAsync(async (connection, transaction, token) =>
        {
            var snapshotsById = new Dictionary<string, SettingSnapshot>(StringComparer.Ordinal);
            var result = new Dictionary<YearMonth, SettingSnapshot>(requested.Length);
            foreach (var yearMonth in requested)
            {
                var snapshotId = await FindEffectiveSnapshotIdAsync(connection, transaction, yearMonth, token)
                    .ConfigureAwait(false)
                    ?? throw new InvalidOperationException("No initial or effective setting snapshot exists.");
                if (!snapshotsById.TryGetValue(snapshotId, out var snapshot))
                {
                    snapshot = await LoadSnapshotAsync(connection, transaction, snapshotId, token).ConfigureAwait(false)
                        ?? throw new InvalidDataException("The effective setting snapshot is missing.");
                    snapshotsById.Add(snapshotId, snapshot);
                }
                result.Add(yearMonth, snapshot);
            }
            return (IReadOnlyDictionary<YearMonth, SettingSnapshot>)result;
        }, cancellationToken);
    }

    public Task<SettingSnapshot> EnsureForMonthAsync(YearMonth yearMonth, CancellationToken cancellationToken) =>
        database.WriteAsync(async (connection, transaction, token) =>
        {
            var existingId = await FindSnapshotIdForExactMonthAsync(connection, transaction, yearMonth, token)
                .ConfigureAwait(false);
            if (existingId is not null)
                return await LoadSnapshotAsync(connection, transaction, existingId, token).ConfigureAwait(false)
                    ?? throw new InvalidDataException("The setting month refers to a missing snapshot.");

            var inheritedId = await FindEffectiveSnapshotIdAsync(connection, transaction, yearMonth, token)
                .ConfigureAwait(false);
            if (inheritedId is null) throw new InvalidOperationException("Initial settings must be created first.");
            var inherited = await LoadSnapshotAsync(connection, transaction, inheritedId, token).ConfigureAwait(false)
                ?? throw new InvalidDataException("The inherited setting snapshot is missing.");
            var latestHolidayId = await FindLatestHolidayIdAsync(connection, transaction, token).ConfigureAwait(false);

            SettingSnapshot selected;
            if (inherited.HolidayCalendarVersionId == latestHolidayId)
            {
                selected = inherited;
            }
            else
            {
                selected = new SettingSnapshot(new SettingSnapshotId(Guid.NewGuid()), inherited.Id, latestHolidayId,
                    inherited.SchemaVersion, clock.UtcNow.ToUniversalTime(), inherited.Services,
                    inherited.TimeCategories, inherited.Rates, inherited.Premiums, inherited.CountBonuses);
                await InsertSnapshotAsync(connection, transaction, selected, token).ConfigureAwait(false);
            }

            var now = SqliteValue.Utc(clock.UtcNow);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO setting_month(year_month, snapshot_id, created_at_utc, updated_at_utc)
                VALUES($month, $snapshot, $now, $now);
                """;
            command.Parameters.AddValue("$month", SqliteValue.YearMonth(yearMonth));
            command.Parameters.AddValue("$snapshot", SqliteValue.Id(selected.Id.Value));
            command.Parameters.AddValue("$now", now);
            await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            return selected;
        }, cancellationToken);

    public Task<SettingSnapshot?> TryEnsureForMonthAsync(YearMonth yearMonth,
        SettingSnapshotId expectedEffectiveSnapshotId, HolidayCalendarVersionId expectedHolidayCalendarVersionId,
        CancellationToken cancellationToken) =>
        database.WriteAsync(async (connection, transaction, token) =>
        {
            var existingId = await FindSnapshotIdForExactMonthAsync(connection, transaction, yearMonth, token)
                .ConfigureAwait(false);
            if (existingId is not null)
            {
                if (!StringComparer.Ordinal.Equals(existingId, SqliteValue.Id(expectedEffectiveSnapshotId.Value))) return null;
                return await LoadSnapshotAsync(connection, transaction, existingId, token).ConfigureAwait(false)
                    ?? throw new InvalidDataException("The setting month refers to a missing snapshot.");
            }

            var inheritedId = await FindEffectiveSnapshotIdAsync(connection, transaction, yearMonth, token)
                .ConfigureAwait(false);
            if (!StringComparer.Ordinal.Equals(inheritedId, SqliteValue.Id(expectedEffectiveSnapshotId.Value))) return null;
            var inherited = await LoadSnapshotAsync(connection, transaction, inheritedId!, token).ConfigureAwait(false)
                ?? throw new InvalidDataException("The inherited setting snapshot is missing.");
            var latestHolidayId = await FindLatestHolidayIdAsync(connection, transaction, token).ConfigureAwait(false);
            if (latestHolidayId != expectedHolidayCalendarVersionId) return null;

            SettingSnapshot selected;
            if (inherited.HolidayCalendarVersionId == latestHolidayId)
            {
                selected = inherited;
            }
            else
            {
                selected = new SettingSnapshot(new SettingSnapshotId(Guid.NewGuid()), inherited.Id, latestHolidayId,
                    inherited.SchemaVersion, clock.UtcNow.ToUniversalTime(), inherited.Services,
                    inherited.TimeCategories, inherited.Rates, inherited.Premiums, inherited.CountBonuses);
                await InsertSnapshotAsync(connection, transaction, selected, token).ConfigureAwait(false);
            }

            var now = SqliteValue.Utc(clock.UtcNow);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO setting_month(year_month, snapshot_id, created_at_utc, updated_at_utc)
                VALUES($month, $snapshot, $now, $now);
                """;
            command.Parameters.AddValue("$month", SqliteValue.YearMonth(yearMonth));
            command.Parameters.AddValue("$snapshot", SqliteValue.Id(selected.Id.Value));
            command.Parameters.AddValue("$now", now);
            await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            return selected;
        }, cancellationToken);

    // HIST-001/HIST-004: clone + conditional month switch is one BEGIN IMMEDIATE transaction.
    public Task<SettingSnapshot?> TryCloneAndReplaceMonthSnapshotAsync(YearMonth yearMonth,
        SettingSnapshotId expectedCurrentSnapshotId, SettingSnapshotReplacementDto replacement,
        HolidayCalendarVersionId holidayCalendarVersionId, DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        return database.WriteAsync(async (connection, transaction, token) =>
        {
            var currentId = await FindSnapshotIdForExactMonthAsync(connection, transaction, yearMonth, token)
                .ConfigureAwait(false);
            var expectedId = SqliteValue.Id(expectedCurrentSnapshotId.Value);
            var monthAlreadyExists = currentId is not null;
            if (currentId is null)
                currentId = await FindEffectiveSnapshotIdAsync(connection, transaction, yearMonth, token)
                    .ConfigureAwait(false);
            if (!StringComparer.Ordinal.Equals(currentId, expectedId)) return null;

            var snapshot = new SettingSnapshot(
                new SettingSnapshotId(Guid.NewGuid()), expectedCurrentSnapshotId, holidayCalendarVersionId,
                new SchemaVersion(SqliteDatabase.CurrentSettingSnapshotSchemaVersion), createdAtUtc.ToUniversalTime(),
                replacement.Services, replacement.TimeCategories, replacement.Rates,
                replacement.Premiums, replacement.CountBonuses);
            await InsertSnapshotAsync(connection, transaction, snapshot, token).ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = monthAlreadyExists ? """
                    UPDATE setting_month SET snapshot_id = $replacement, updated_at_utc = $now
                    WHERE year_month = $month AND snapshot_id = $expected;
                    """ : """
                    INSERT INTO setting_month(year_month, snapshot_id, created_at_utc, updated_at_utc)
                    VALUES($month, $replacement, $now, $now);
                    """;
            command.Parameters.AddValue("$replacement", SqliteValue.Id(snapshot.Id.Value));
            command.Parameters.AddValue("$now", SqliteValue.Utc(createdAtUtc));
            command.Parameters.AddValue("$month", SqliteValue.YearMonth(yearMonth));
            if (monthAlreadyExists) command.Parameters.AddValue("$expected", expectedId);
            if (await command.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1)
                throw new InvalidOperationException("The setting month changed while replacing its snapshot.");

            // During the setup wizard the seeded snapshot is only a template (it intentionally has no rates).
            // Keep the initial pointer on the immutable replacement so CompleteAsync validates the values that
            // the user just entered. Once setup is complete this pointer must remain historical and immutable.
            await using var metadata = connection.CreateCommand();
            metadata.Transaction = transaction;
            metadata.CommandText = """
                UPDATE app_metadata SET initial_snapshot_id = $replacement, updated_at_utc = $now
                WHERE id = 1 AND initial_setup_status <> 'Completed' AND initial_snapshot_id = $expected;
                """;
            metadata.Parameters.AddValue("$replacement", SqliteValue.Id(snapshot.Id.Value));
            metadata.Parameters.AddValue("$now", SqliteValue.Utc(createdAtUtc));
            metadata.Parameters.AddValue("$expected", expectedId);
            await metadata.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            return snapshot;
        }, cancellationToken);
    }

    internal static async Task<SettingSnapshot?> LoadSnapshotAsync(SqliteConnection connection,
        SqliteTransaction? transaction, string snapshotId, CancellationToken cancellationToken)
    {
        SettingSnapshotId id;
        SettingSnapshotId? basedOn;
        HolidayCalendarVersionId holiday;
        SchemaVersion schema;
        DateTimeOffset created;
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT id, based_on_id, holiday_calendar_version_id, schema_version, created_at_utc
                FROM setting_snapshot WHERE id = $id;
                """;
            command.Parameters.AddValue("$id", snapshotId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
            id = new SettingSnapshotId(SqliteValue.Guid(reader.GetString("id")));
            basedOn = reader.GetNullableString("based_on_id") is { } baseId
                ? new SettingSnapshotId(SqliteValue.Guid(baseId)) : null;
            holiday = new HolidayCalendarVersionId(SqliteValue.Guid(reader.GetString("holiday_calendar_version_id")));
            schema = new SchemaVersion(reader.GetInt32("schema_version"));
            created = SqliteValue.Utc(reader.GetString("created_at_utc"));
        }

        var services = await ReadServicesAsync(connection, transaction, snapshotId, cancellationToken).ConfigureAwait(false);
        var categories = await ReadCategoriesAsync(connection, transaction, snapshotId, cancellationToken).ConfigureAwait(false);
        var rates = await ReadRatesAsync(connection, transaction, snapshotId, cancellationToken).ConfigureAwait(false);
        var premiums = await ReadPremiumsAsync(connection, transaction, snapshotId, cancellationToken).ConfigureAwait(false);
        var bonuses = await ReadBonusesAsync(connection, transaction, snapshotId, cancellationToken).ConfigureAwait(false);
        return new SettingSnapshot(id, basedOn, holiday, schema, created, services, categories, rates, premiums, bonuses);
    }

    internal static async Task InsertSnapshotAsync(SqliteConnection connection, SqliteTransaction transaction,
        SettingSnapshot snapshot, CancellationToken cancellationToken)
    {
        var snapshotId = SqliteValue.Id(snapshot.Id.Value);
        var created = SqliteValue.Utc(snapshot.CreatedAtUtc);
        await ExecuteAsync(connection, transaction, """
            INSERT INTO setting_snapshot(id, based_on_id, holiday_calendar_version_id, schema_version, created_at_utc)
            VALUES($id, $based, $holiday, $schema, $created);
            """, command =>
        {
            command.Parameters.AddValue("$id", snapshotId);
            command.Parameters.AddValue("$based", snapshot.BasedOnId is { } based ? SqliteValue.Id(based.Value) : null);
            command.Parameters.AddValue("$holiday", SqliteValue.Id(snapshot.HolidayCalendarVersionId.Value));
            command.Parameters.AddValue("$schema", snapshot.SchemaVersion.Value);
            command.Parameters.AddValue("$created", created);
        }, cancellationToken).ConfigureAwait(false);

        foreach (var service in snapshot.Services)
        {
            await InsertDefinitionAsync(connection, transaction, "service_definition", SqliteValue.Id(service.Id.Value),
                created, cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(connection, transaction, """
                INSERT INTO snapshot_service(snapshot_id, service_id, display_name, display_order, is_enabled)
                VALUES($snapshot, $id, $name, $order, $enabled);
                """, command =>
            {
                command.Parameters.AddValue("$snapshot", snapshotId);
                command.Parameters.AddValue("$id", SqliteValue.Id(service.Id.Value));
                command.Parameters.AddValue("$name", service.DisplayName);
                command.Parameters.AddValue("$order", service.DisplayOrder.Value);
                command.Parameters.AddValue("$enabled", service.IsEnabled ? 1 : 0);
            }, cancellationToken).ConfigureAwait(false);
        }

        foreach (var category in snapshot.TimeCategories)
        {
            await InsertDefinitionAsync(connection, transaction, "time_category_definition",
                SqliteValue.Id(category.Id.Value), created, cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(connection, transaction, """
                INSERT INTO snapshot_time_category(snapshot_id, time_category_id, service_id, display_name,
                    standard_minutes, display_order, is_enabled)
                VALUES($snapshot, $id, $service, $name, $minutes, $order, $enabled);
                """, command =>
            {
                command.Parameters.AddValue("$snapshot", snapshotId);
                command.Parameters.AddValue("$id", SqliteValue.Id(category.Id.Value));
                command.Parameters.AddValue("$service", SqliteValue.Id(category.ServiceId.Value));
                command.Parameters.AddValue("$name", category.DisplayName);
                command.Parameters.AddValue("$minutes", category.StandardMinutes.Value);
                command.Parameters.AddValue("$order", category.DisplayOrder.Value);
                command.Parameters.AddValue("$enabled", category.IsEnabled ? 1 : 0);
            }, cancellationToken).ConfigureAwait(false);
        }

        foreach (var rate in snapshot.Rates)
            await ExecuteAsync(connection, transaction, """
                INSERT INTO snapshot_rate(snapshot_id, service_id, time_category_id, rate_type, amount_yen)
                VALUES($snapshot, $service, $category, $type, $amount);
                """, command =>
            {
                command.Parameters.AddValue("$snapshot", snapshotId);
                command.Parameters.AddValue("$service", SqliteValue.Id(rate.ServiceId.Value));
                command.Parameters.AddValue("$category", rate.TimeCategoryId is { } category
                    ? SqliteValue.Id(category.Value) : null);
                command.Parameters.AddValue("$type", rate.RateType.ToString());
                command.Parameters.AddValue("$amount", rate.Amount.Value);
            }, cancellationToken).ConfigureAwait(false);

        foreach (var premium in snapshot.Premiums)
        {
            await InsertDefinitionAsync(connection, transaction, "premium_definition", SqliteValue.Id(premium.Id.Value),
                created, cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(connection, transaction, """
                INSERT INTO snapshot_premium(snapshot_id, premium_id, display_name, calculation_type,
                    percentage_basis_points, amount_yen, start_time_minutes, end_time_minutes,
                    uses_national_holidays, is_enabled)
                VALUES($snapshot, $id, $name, $type, $percentage, $amount, $start, $end, $holidays, $enabled);
                """, command =>
            {
                command.Parameters.AddValue("$snapshot", snapshotId);
                command.Parameters.AddValue("$id", SqliteValue.Id(premium.Id.Value));
                command.Parameters.AddValue("$name", premium.DisplayName);
                command.Parameters.AddValue("$type", premium.CalculationType.ToString());
                command.Parameters.AddValue("$percentage", premium.Percentage?.Value);
                command.Parameters.AddValue("$amount", premium.Amount?.Value);
                command.Parameters.AddValue("$start", premium.StartTime?.Value);
                command.Parameters.AddValue("$end", premium.EndTime?.Value);
                command.Parameters.AddValue("$holidays", premium.UsesNationalHolidays ? 1 : 0);
                command.Parameters.AddValue("$enabled", premium.IsEnabled ? 1 : 0);
            }, cancellationToken).ConfigureAwait(false);

            foreach (var weekday in premium.Weekdays)
                await ExecuteChildAsync(connection, transaction, "snapshot_premium_weekday", snapshotId,
                    SqliteValue.Id(premium.Id.Value), "weekday", SqliteBasicShiftRepository.WeekdayToDatabase(weekday),
                    cancellationToken).ConfigureAwait(false);
            foreach (var date in premium.Dates)
                await ExecuteChildAsync(connection, transaction, "snapshot_premium_date", snapshotId,
                    SqliteValue.Id(premium.Id.Value), "target_date", SqliteValue.Date(date), cancellationToken)
                    .ConfigureAwait(false);
            foreach (var serviceId in premium.ServiceIds)
                await ExecuteChildAsync(connection, transaction, "snapshot_premium_service", snapshotId,
                    SqliteValue.Id(premium.Id.Value), "service_id", SqliteValue.Id(serviceId.Value), cancellationToken)
                    .ConfigureAwait(false);
        }

        foreach (var bonus in snapshot.CountBonuses)
        {
            await InsertDefinitionAsync(connection, transaction, "count_bonus_definition", SqliteValue.Id(bonus.Id.Value),
                created, cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(connection, transaction, """
                INSERT INTO snapshot_count_bonus(snapshot_id, count_bonus_id, display_name, amount_yen, is_enabled)
                VALUES($snapshot, $id, $name, $amount, $enabled);
                """, command =>
            {
                command.Parameters.AddValue("$snapshot", snapshotId);
                command.Parameters.AddValue("$id", SqliteValue.Id(bonus.Id.Value));
                command.Parameters.AddValue("$name", bonus.DisplayName);
                command.Parameters.AddValue("$amount", bonus.Amount.Value);
                command.Parameters.AddValue("$enabled", bonus.IsEnabled ? 1 : 0);
            }, cancellationToken).ConfigureAwait(false);
            foreach (var serviceId in bonus.ServiceIds)
                await ExecuteChildAsync(connection, transaction, "snapshot_count_bonus_service", snapshotId,
                    SqliteValue.Id(bonus.Id.Value), "service_id", SqliteValue.Id(serviceId.Value), cancellationToken)
                    .ConfigureAwait(false);
        }
    }

    private static async Task<string?> FindSnapshotIdForExactMonthAsync(SqliteConnection connection,
        SqliteTransaction? transaction, YearMonth yearMonth, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT snapshot_id FROM setting_month WHERE year_month = $month;";
        command.Parameters.AddValue("$month", SqliteValue.YearMonth(yearMonth));
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
    }

    private static async Task<string?> FindEffectiveSnapshotIdAsync(SqliteConnection connection,
        SqliteTransaction? transaction, YearMonth yearMonth, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT snapshot_id FROM setting_month WHERE year_month <= $month
            ORDER BY year_month DESC LIMIT 1;
            """;
        command.Parameters.AddValue("$month", SqliteValue.YearMonth(yearMonth));
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        if (result is not null) return result;
        command.Parameters.Clear();
        command.CommandText = "SELECT initial_snapshot_id FROM app_metadata WHERE id = 1;";
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
    }

    private static async Task<HolidayCalendarVersionId> FindLatestHolidayIdAsync(SqliteConnection connection,
        SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id FROM holiday_calendar_version ORDER BY source_reference_date DESC, created_at_utc DESC, id DESC LIMIT 1;
            """;
        var id = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        return id is null
            ? throw new InvalidDataException("No verified holiday calendar is installed.")
            : new HolidayCalendarVersionId(SqliteValue.Guid(id));
    }

    private static async Task<IReadOnlyList<SnapshotService>> ReadServicesAsync(SqliteConnection connection,
        SqliteTransaction? transaction, string snapshotId, CancellationToken cancellationToken)
    {
        var result = new List<SnapshotService>();
        await using var command = ChildCommand(connection, transaction, """
            SELECT service_id, display_name, display_order, is_enabled FROM snapshot_service
            WHERE snapshot_id = $snapshot ORDER BY display_order, service_id;
            """, snapshotId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new SnapshotService(new ServiceId(SqliteValue.Guid(reader.GetString("service_id"))),
                reader.GetString("display_name"), new DisplayOrder(reader.GetInt32("display_order")),
                reader.GetBoolean("is_enabled")));
        return result;
    }

    private static async Task<IReadOnlyList<SnapshotTimeCategory>> ReadCategoriesAsync(SqliteConnection connection,
        SqliteTransaction? transaction, string snapshotId, CancellationToken cancellationToken)
    {
        var result = new List<SnapshotTimeCategory>();
        await using var command = ChildCommand(connection, transaction, """
            SELECT time_category_id, service_id, display_name, standard_minutes, display_order, is_enabled
            FROM snapshot_time_category WHERE snapshot_id = $snapshot ORDER BY display_order, time_category_id;
            """, snapshotId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new SnapshotTimeCategory(
                new TimeCategoryId(SqliteValue.Guid(reader.GetString("time_category_id"))),
                new ServiceId(SqliteValue.Guid(reader.GetString("service_id"))), reader.GetString("display_name"),
                new WorkMinutes(reader.GetInt32("standard_minutes")), new DisplayOrder(reader.GetInt32("display_order")),
                reader.GetBoolean("is_enabled")));
        return result;
    }

    private static async Task<IReadOnlyList<SnapshotRate>> ReadRatesAsync(SqliteConnection connection,
        SqliteTransaction? transaction, string snapshotId, CancellationToken cancellationToken)
    {
        var result = new List<SnapshotRate>();
        await using var command = ChildCommand(connection, transaction, """
            SELECT service_id, time_category_id, rate_type, amount_yen FROM snapshot_rate
            WHERE snapshot_id = $snapshot ORDER BY service_id, time_category_id;
            """, snapshotId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new SnapshotRate(new ServiceId(SqliteValue.Guid(reader.GetString("service_id"))),
                reader.GetNullableString("time_category_id") is { } category
                    ? new TimeCategoryId(SqliteValue.Guid(category)) : null,
                Enum.Parse<RateType>(reader.GetString("rate_type"), false), new YenAmount(reader.GetInt64("amount_yen"))));
        return result;
    }

    private static async Task<IReadOnlyList<SnapshotPremium>> ReadPremiumsAsync(SqliteConnection connection,
        SqliteTransaction? transaction, string snapshotId, CancellationToken cancellationToken)
    {
        var rows = new List<PremiumRow>();
        await using (var command = ChildCommand(connection, transaction, """
            SELECT premium_id, display_name, calculation_type, percentage_basis_points, amount_yen,
                   start_time_minutes, end_time_minutes, uses_national_holidays, is_enabled
            FROM snapshot_premium WHERE snapshot_id = $snapshot ORDER BY premium_id;
            """, snapshotId))
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                rows.Add(new PremiumRow(reader.GetString("premium_id"), reader.GetString("display_name"),
                    reader.GetString("calculation_type"), reader.GetNullableInt32("percentage_basis_points"),
                    reader.GetNullableInt64("amount_yen"), reader.GetNullableInt32("start_time_minutes"),
                    reader.GetNullableInt32("end_time_minutes"), reader.GetBoolean("uses_national_holidays"),
                    reader.GetBoolean("is_enabled")));
        }

        var weekdays = await ReadPremiumChildrenAsync<int>(connection, transaction, snapshotId,
            "snapshot_premium_weekday", "weekday", value => Convert.ToInt32(value), cancellationToken).ConfigureAwait(false);
        var dates = await ReadPremiumChildrenAsync<DateOnly>(connection, transaction, snapshotId,
            "snapshot_premium_date", "target_date", value => SqliteValue.Date((string)value), cancellationToken)
            .ConfigureAwait(false);
        var services = await ReadPremiumChildrenAsync<ServiceId>(connection, transaction, snapshotId,
            "snapshot_premium_service", "service_id", value => new ServiceId(SqliteValue.Guid((string)value)),
            cancellationToken).ConfigureAwait(false);

        return rows.Select(row => new SnapshotPremium(
            new PremiumId(SqliteValue.Guid(row.Id)), row.Name, Enum.Parse<PremiumCalculationType>(row.Type, false),
            row.Percentage is { } percentage ? new BasisPoints(percentage) : null,
            row.Amount is { } amount ? new YenAmount(amount) : null,
            row.Start is { } start ? new MinuteOfDay(start) : null,
            row.End is { } end ? new MinuteOfDay(end) : null, row.Holidays,
            weekdays.GetValueOrDefault(row.Id, []).Select(SqliteBasicShiftRepository.WeekdayFromDatabase).ToHashSet(),
            dates.GetValueOrDefault(row.Id, []).ToHashSet(), services.GetValueOrDefault(row.Id, []).ToHashSet(),
            row.Enabled)).ToArray();
    }

    private static async Task<IReadOnlyList<SnapshotCountBonus>> ReadBonusesAsync(SqliteConnection connection,
        SqliteTransaction? transaction, string snapshotId, CancellationToken cancellationToken)
    {
        var rows = new List<(string Id, string Name, long Amount, bool Enabled)>();
        await using (var command = ChildCommand(connection, transaction, """
            SELECT count_bonus_id, display_name, amount_yen, is_enabled FROM snapshot_count_bonus
            WHERE snapshot_id = $snapshot ORDER BY count_bonus_id;
            """, snapshotId))
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                rows.Add((reader.GetString("count_bonus_id"), reader.GetString("display_name"),
                    reader.GetInt64("amount_yen"), reader.GetBoolean("is_enabled")));
        }

        var services = await ReadPremiumChildrenAsync<ServiceId>(connection, transaction, snapshotId,
            "snapshot_count_bonus_service", "service_id",
            value => new ServiceId(SqliteValue.Guid((string)value)), cancellationToken, "count_bonus_id")
            .ConfigureAwait(false);
        return rows.Select(row => new SnapshotCountBonus(new CountBonusId(SqliteValue.Guid(row.Id)), row.Name,
            new YenAmount(row.Amount), services.GetValueOrDefault(row.Id, []).ToHashSet(), row.Enabled)).ToArray();
    }

    private static async Task<Dictionary<string, List<T>>> ReadPremiumChildrenAsync<T>(SqliteConnection connection,
        SqliteTransaction? transaction, string snapshotId, string table, string valueColumn, Func<object, T> convert,
        CancellationToken cancellationToken, string idColumn = "premium_id")
    {
        var result = new Dictionary<string, List<T>>(StringComparer.Ordinal);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT {idColumn}, {valueColumn} FROM {table} WHERE snapshot_id = $snapshot ORDER BY {idColumn}, {valueColumn};";
        command.Parameters.AddValue("$snapshot", snapshotId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var id = reader.GetString(0);
            if (!result.TryGetValue(id, out var list)) result.Add(id, list = []);
            list.Add(convert(reader.GetValue(1)));
        }

        return result;
    }

    private static SqliteCommand ChildCommand(SqliteConnection connection, SqliteTransaction? transaction,
        string sql, string snapshotId)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.Parameters.AddValue("$snapshot", snapshotId);
        return command;
    }

    private static Task InsertDefinitionAsync(SqliteConnection connection, SqliteTransaction transaction,
        string table, string id, string created, CancellationToken cancellationToken)
    {
        if (table is not ("service_definition" or "time_category_definition" or "premium_definition" or "count_bonus_definition"))
            throw new ArgumentOutOfRangeException(nameof(table));
        return ExecuteAsync(connection, transaction,
            $"INSERT OR IGNORE INTO {table}(id, created_at_utc) VALUES($id, $created);", command =>
            {
                command.Parameters.AddValue("$id", id);
                command.Parameters.AddValue("$created", created);
            }, cancellationToken);
    }

    private static Task ExecuteChildAsync(SqliteConnection connection, SqliteTransaction transaction, string table,
        string snapshotId, string itemId, string valueColumn, object value, CancellationToken cancellationToken)
    {
        var idColumn = table.StartsWith("snapshot_count_bonus", StringComparison.Ordinal)
            ? "count_bonus_id" : "premium_id";
        return ExecuteAsync(connection, transaction,
            $"INSERT INTO {table}(snapshot_id, {idColumn}, {valueColumn}) VALUES($snapshot, $id, $value);",
            command =>
            {
                command.Parameters.AddValue("$snapshot", snapshotId);
                command.Parameters.AddValue("$id", itemId);
                command.Parameters.AddValue("$value", value);
            }, cancellationToken);
    }

    private static async Task ExecuteAsync(SqliteConnection connection, SqliteTransaction transaction, string sql,
        Action<SqliteCommand> bind, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        bind(command);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private sealed record PremiumRow(string Id, string Name, string Type, int? Percentage, long? Amount,
        int? Start, int? End, bool Holidays, bool Enabled);
}
