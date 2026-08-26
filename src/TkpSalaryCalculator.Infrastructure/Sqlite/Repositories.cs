using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using TkpSalaryCalculator.Application.Contracts;
using TkpSalaryCalculator.Application.Ports;
using TkpSalaryCalculator.Domain.Models;
using TkpSalaryCalculator.Domain.ValueObjects;

namespace TkpSalaryCalculator.Infrastructure.Sqlite;

public sealed class SqliteAppMetadataRepository(SqliteDatabase database, IUtcClock clock) : IAppMetadataRepository
{
    private readonly SqliteDatabase database = database ?? throw new ArgumentNullException(nameof(database));
    private readonly IUtcClock clock = clock ?? throw new ArgumentNullException(nameof(clock));

    public Task<AppMetadata> GetAsync(CancellationToken cancellationToken) =>
        database.ReadAsync(async (connection, transaction, token) =>
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                SELECT initial_setup_status, initial_setup_step, initial_snapshot_id, export_format_version,
                       last_exported_at_utc, last_data_changed_at_utc, backup_reminder_deferred_until_date
                FROM app_metadata WHERE id = 1;
                """;
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (!await reader.ReadAsync(token).ConfigureAwait(false))
                throw new InvalidDataException("The required app_metadata row is missing.");
            return new AppMetadata(
                Enum.Parse<InitialSetupStatus>(reader.GetString("initial_setup_status"), false),
                reader.GetNullableString("initial_setup_step"),
                reader.GetNullableString("initial_snapshot_id") is { } snapshotId
                    ? new SettingSnapshotId(SqliteValue.Guid(snapshotId)) : null,
                reader.GetInt32("export_format_version"),
                reader.GetNullableString("last_exported_at_utc") is { } exported ? SqliteValue.Utc(exported) : null,
                reader.GetNullableString("last_data_changed_at_utc") is { } changed ? SqliteValue.Utc(changed) : null,
                reader.GetNullableString("backup_reminder_deferred_until_date") is { } deferred
                    ? SqliteValue.Date(deferred) : null);
        }, cancellationToken);

    public Task SetInitialSetupAsync(InitialSetupStatus status, string? step, SettingSnapshotId? initialSnapshotId,
        CancellationToken cancellationToken) => UpdateAsync("""
            UPDATE app_metadata
            SET initial_setup_status = $value, initial_setup_step = $step, initial_snapshot_id = $snapshot,
                updated_at_utc = $now
            WHERE id = 1;
            """, command =>
        {
            command.Parameters.AddValue("$value", status.ToString());
            command.Parameters.AddValue("$step", step);
            command.Parameters.AddValue("$snapshot", initialSnapshotId is { } id ? SqliteValue.Id(id.Value) : null);
        }, cancellationToken);

    public Task SetExportFormatVersionAsync(int exportFormatVersion, CancellationToken cancellationToken)
    {
        if (exportFormatVersion < 1) throw new ArgumentOutOfRangeException(nameof(exportFormatVersion));
        return UpdateAsync("""
            UPDATE app_metadata SET export_format_version = $value, updated_at_utc = $now WHERE id = 1;
            """, command => command.Parameters.AddValue("$value", exportFormatVersion), cancellationToken);
    }

    public Task SetLastDataChangedAtUtcAsync(DateTimeOffset changedAtUtc, CancellationToken cancellationToken) =>
        UpdateAsync("""
            UPDATE app_metadata SET last_data_changed_at_utc = $value, updated_at_utc = $now WHERE id = 1;
            """, command => command.Parameters.AddValue("$value", SqliteValue.Utc(changedAtUtc)), cancellationToken);

    public Task SetLastExportedAtUtcAsync(DateTimeOffset exportedAtUtc, CancellationToken cancellationToken) =>
        UpdateAsync("""
            UPDATE app_metadata SET last_exported_at_utc = $value, updated_at_utc = $now WHERE id = 1;
            """, command => command.Parameters.AddValue("$value", SqliteValue.Utc(exportedAtUtc)), cancellationToken);

    public Task SetBackupReminderDeferredUntilDateAsync(DateOnly? deferredUntilDate,
        CancellationToken cancellationToken) => UpdateAsync("""
            UPDATE app_metadata SET backup_reminder_deferred_until_date = $value, updated_at_utc = $now WHERE id = 1;
            """, command => command.Parameters.AddValue("$value",
            deferredUntilDate is { } date ? SqliteValue.Date(date) : null), cancellationToken);

    private Task UpdateAsync(string sql, Action<SqliteCommand> bind, CancellationToken cancellationToken) =>
        database.WriteAsync(async (connection, transaction, token) =>
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            bind(command);
            command.Parameters.AddValue("$now", SqliteValue.Utc(clock.UtcNow));
            if (await command.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1)
                throw new InvalidDataException("The required app_metadata row is missing.");
            return true;
        }, cancellationToken);
}

public sealed class SqliteServicePresetRepository(SqliteDatabase database, IUtcClock clock) : IServicePresetRepository
{
    private readonly SqliteDatabase database = database ?? throw new ArgumentNullException(nameof(database));
    private readonly IUtcClock clock = clock ?? throw new ArgumentNullException(nameof(clock));

    public Task<IReadOnlyList<ServicePresetDto>> GetAllAsync(CancellationToken cancellationToken) =>
        database.ReadAsync(async (connection, transaction, token) =>
        {
            var values = new List<ServicePresetDto>();
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                SELECT id, display_name, service_id, time_category_id, default_work_minutes, display_order, is_enabled
                FROM service_preset ORDER BY is_enabled DESC, display_order, id;
                """;
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false)) values.Add(Read(reader));
            return (IReadOnlyList<ServicePresetDto>)values;
        }, cancellationToken);

    public Task UpsertAsync(ServicePresetDto preset, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preset);
        return database.WriteAsync(async (connection, transaction, token) =>
        {
            var now = SqliteValue.Utc(clock.UtcNow);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO service_preset(id, display_name, service_id, time_category_id, default_work_minutes,
                    display_order, is_enabled, created_at_utc, updated_at_utc)
                VALUES($id, $name, $service, $category, $minutes, $order, $enabled, $now, $now)
                ON CONFLICT(id) DO UPDATE SET display_name = excluded.display_name,
                    service_id = excluded.service_id, time_category_id = excluded.time_category_id,
                    default_work_minutes = excluded.default_work_minutes, display_order = excluded.display_order,
                    is_enabled = excluded.is_enabled, updated_at_utc = excluded.updated_at_utc;
                """;
            Bind(command, preset);
            command.Parameters.AddValue("$now", now);
            await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            return true;
        }, cancellationToken);
    }

    public Task DeleteAsync(ServicePresetId id, CancellationToken cancellationToken) =>
        SqliteRepositoryCommand.DeleteByIdAsync(database, "service_preset", id.Value, cancellationToken);

    private static void Bind(SqliteCommand command, ServicePresetDto value)
    {
        command.Parameters.AddValue("$id", SqliteValue.Id(value.Id.Value));
        command.Parameters.AddValue("$name", value.DisplayName);
        command.Parameters.AddValue("$service", SqliteValue.Id(value.ServiceId.Value));
        command.Parameters.AddValue("$category", value.TimeCategoryId is { } id ? SqliteValue.Id(id.Value) : null);
        command.Parameters.AddValue("$minutes", value.DefaultWorkMinutes.Value);
        command.Parameters.AddValue("$order", value.DisplayOrder.Value);
        command.Parameters.AddValue("$enabled", value.IsEnabled ? 1 : 0);
    }

    private static ServicePresetDto Read(SqliteDataReader reader) => new(
        new ServicePresetId(SqliteValue.Guid(reader.GetString("id"))),
        reader.GetString("display_name"),
        new ServiceId(SqliteValue.Guid(reader.GetString("service_id"))),
        reader.GetNullableString("time_category_id") is { } category
            ? new TimeCategoryId(SqliteValue.Guid(category)) : null,
        new WorkMinutes(reader.GetInt32("default_work_minutes")),
        new DisplayOrder(reader.GetInt32("display_order")),
        reader.GetBoolean("is_enabled"));
}

public sealed class SqliteBasicShiftRepository(SqliteDatabase database, IUtcClock clock) : IBasicShiftRepository
{
    private readonly SqliteDatabase database = database ?? throw new ArgumentNullException(nameof(database));
    private readonly IUtcClock clock = clock ?? throw new ArgumentNullException(nameof(clock));

    public async Task<IReadOnlyList<BasicShiftDto>> GetForWeekdayAsync(DayOfWeek weekday,
        CancellationToken cancellationToken)
    {
        var result = await GetForWeekdaysAsync([weekday], cancellationToken).ConfigureAwait(false);
        return result[weekday];
    }

    public Task<IReadOnlyDictionary<DayOfWeek, IReadOnlyList<BasicShiftDto>>> GetForWeekdaysAsync(
        IReadOnlyCollection<DayOfWeek> weekdays,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(weekdays);
        cancellationToken.ThrowIfCancellationRequested();
        var requested = weekdays.Distinct().OrderBy(WeekdayToDatabase).ToArray();
        if (requested.Length == 0)
            return Task.FromResult<IReadOnlyDictionary<DayOfWeek, IReadOnlyList<BasicShiftDto>>>(
                new Dictionary<DayOfWeek, IReadOnlyList<BasicShiftDto>>());

        return database.ReadAsync(async (connection, transaction, token) =>
        {
            var values = requested.ToDictionary(x => x, _ => new List<BasicShiftDto>());
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            var parameterNames = new string[requested.Length];
            for (var index = 0; index < requested.Length; index++)
            {
                parameterNames[index] = $"$weekday{index}";
                command.Parameters.AddValue(parameterNames[index], WeekdayToDatabase(requested[index]));
            }
            command.CommandText = $"""
                SELECT id, weekday, service_preset_id, service_id, time_category_id, input_mode, work_minutes,
                       start_time_minutes, end_time_minutes, display_order, is_enabled
                FROM basic_shift WHERE weekday IN ({string.Join(", ", parameterNames)})
                ORDER BY weekday, is_enabled DESC, display_order, id;
                """;
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                var value = Read(reader);
                values[value.Weekday].Add(value);
            }
            return (IReadOnlyDictionary<DayOfWeek, IReadOnlyList<BasicShiftDto>>)
                values.ToDictionary(x => x.Key, x => (IReadOnlyList<BasicShiftDto>)x.Value);
        }, cancellationToken);
    }

    public Task<BasicShiftDto?> FindAsync(BasicShiftId id, CancellationToken cancellationToken) =>
        database.ReadAsync(async (connection, transaction, token) =>
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                SELECT id, weekday, service_preset_id, service_id, time_category_id, input_mode, work_minutes,
                       start_time_minutes, end_time_minutes, display_order, is_enabled
                FROM basic_shift WHERE id = $id;
                """;
            command.Parameters.AddValue("$id", SqliteValue.Id(id.Value));
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            return await reader.ReadAsync(token).ConfigureAwait(false) ? Read(reader) : null;
        }, cancellationToken);

    public Task UpsertAsync(BasicShiftDto basicShift, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(basicShift);
        _ = new WorkRecord(new WorkRecordId(Guid.NewGuid()), new DateOnly(2000, 1, 1), basicShift.ServiceId,
            basicShift.TimeCategoryId, basicShift.InputMode, basicShift.WorkMinutes, basicShift.StartTime,
            basicShift.EndTime);
        return database.WriteAsync(async (connection, transaction, token) =>
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO basic_shift(id, weekday, service_preset_id, service_id, time_category_id, input_mode,
                    work_minutes, start_time_minutes, end_time_minutes, display_order, is_enabled,
                    created_at_utc, updated_at_utc)
                VALUES($id, $weekday, $preset, $service, $category, $mode, $minutes, $start, $end,
                    $order, $enabled, $now, $now)
                ON CONFLICT(id) DO UPDATE SET weekday = excluded.weekday,
                    service_preset_id = excluded.service_preset_id, service_id = excluded.service_id,
                    time_category_id = excluded.time_category_id, input_mode = excluded.input_mode,
                    work_minutes = excluded.work_minutes, start_time_minutes = excluded.start_time_minutes,
                    end_time_minutes = excluded.end_time_minutes, display_order = excluded.display_order,
                    is_enabled = excluded.is_enabled, updated_at_utc = excluded.updated_at_utc;
                """;
            Bind(command, basicShift);
            command.Parameters.AddValue("$now", SqliteValue.Utc(clock.UtcNow));
            await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            return true;
        }, cancellationToken);
    }

    public Task DeleteAsync(BasicShiftId id, CancellationToken cancellationToken) =>
        SqliteRepositoryCommand.DeleteByIdAsync(database, "basic_shift", id.Value, cancellationToken);

    internal static int WeekdayToDatabase(DayOfWeek weekday)
    {
        if (!Enum.IsDefined(weekday)) throw new ArgumentOutOfRangeException(nameof(weekday));
        return weekday == DayOfWeek.Sunday ? 7 : (int)weekday;
    }

    internal static DayOfWeek WeekdayFromDatabase(int weekday) => weekday == 7
        ? DayOfWeek.Sunday
        : (DayOfWeek)weekday;

    private static void Bind(SqliteCommand command, BasicShiftDto value)
    {
        command.Parameters.AddValue("$id", SqliteValue.Id(value.Id.Value));
        command.Parameters.AddValue("$weekday", WeekdayToDatabase(value.Weekday));
        command.Parameters.AddValue("$preset", value.ServicePresetId is { } preset ? SqliteValue.Id(preset.Value) : null);
        command.Parameters.AddValue("$service", SqliteValue.Id(value.ServiceId.Value));
        command.Parameters.AddValue("$category", value.TimeCategoryId is { } category ? SqliteValue.Id(category.Value) : null);
        command.Parameters.AddValue("$mode", value.InputMode.ToString());
        command.Parameters.AddValue("$minutes", value.WorkMinutes.Value);
        command.Parameters.AddValue("$start", value.StartTime?.Value);
        command.Parameters.AddValue("$end", value.EndTime?.Value);
        command.Parameters.AddValue("$order", value.DisplayOrder.Value);
        command.Parameters.AddValue("$enabled", value.IsEnabled ? 1 : 0);
    }

    private static BasicShiftDto Read(SqliteDataReader reader) => new(
        new BasicShiftId(SqliteValue.Guid(reader.GetString("id"))),
        WeekdayFromDatabase(reader.GetInt32("weekday")),
        reader.GetNullableString("service_preset_id") is { } preset
            ? new ServicePresetId(SqliteValue.Guid(preset)) : null,
        new ServiceId(SqliteValue.Guid(reader.GetString("service_id"))),
        reader.GetNullableString("time_category_id") is { } category
            ? new TimeCategoryId(SqliteValue.Guid(category)) : null,
        Enum.Parse<WorkInputMode>(reader.GetString("input_mode"), false),
        new WorkMinutes(reader.GetInt32("work_minutes")),
        reader.GetNullableInt32("start_time_minutes") is { } start ? new MinuteOfDay(start) : null,
        reader.GetNullableInt32("end_time_minutes") is { } end ? new MinuteOfDay(end) : null,
        new DisplayOrder(reader.GetInt32("display_order")),
        reader.GetBoolean("is_enabled"));
}

public sealed class SqliteWorkRecordRepository(SqliteDatabase database, IUtcClock clock) : IWorkRecordRepository
{
    private readonly SqliteDatabase database = database ?? throw new ArgumentNullException(nameof(database));
    private readonly IUtcClock clock = clock ?? throw new ArgumentNullException(nameof(clock));

    public Task<bool> AnyAsync(CancellationToken cancellationToken) => database.ReadAsync(async (connection, transaction, token) =>
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM work_record LIMIT 1);";
        return Convert.ToInt64(await command.ExecuteScalarAsync(token).ConfigureAwait(false)) != 0;
    }, cancellationToken);

    public Task<WorkInputHistory> GetInputHistoryAsync(
        CancellationToken cancellationToken) => database.ReadAsync(async (connection, transaction, token) =>
    {
        var usageCounts = new Dictionary<ServicePresetId, long>();
        await using (var usageCommand = connection.CreateCommand())
        {
            usageCommand.Transaction = transaction;
            usageCommand.CommandText = """
                SELECT source_service_preset_id, COUNT(*) AS usage_count
                FROM work_record WHERE source_service_preset_id IS NOT NULL GROUP BY source_service_preset_id;
                """;
            await using var reader = await usageCommand.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
                usageCounts[new ServicePresetId(SqliteValue.Guid(reader.GetString("source_service_preset_id")))] =
                    reader.GetInt64("usage_count");
        }

        WorkRecordDto? mostRecent = null;
        await using (var recentCommand = connection.CreateCommand())
        {
            recentCommand.Transaction = transaction;
            recentCommand.CommandText = """
                SELECT id, work_date, service_id, time_category_id, input_mode, work_minutes, start_time_minutes,
                       end_time_minutes, source_service_preset_id, source_basic_shift_id, source_work_record_id
                FROM work_record ORDER BY updated_at_utc DESC, work_date DESC, id DESC LIMIT 1;
                """;
            await using var reader = await recentCommand.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (await reader.ReadAsync(token).ConfigureAwait(false)) mostRecent = Read(reader);
        }

        return new WorkInputHistory(usageCounts, mostRecent);
    }, cancellationToken);

    public Task<WorkRecordDto?> FindAsync(WorkRecordId id, CancellationToken cancellationToken) => FindBySqlAsync("""
        SELECT id, work_date, service_id, time_category_id, input_mode, work_minutes, start_time_minutes,
               end_time_minutes, source_service_preset_id, source_basic_shift_id, source_work_record_id
        FROM work_record WHERE id = $value;
        """, SqliteValue.Id(id.Value), cancellationToken);

    public Task<WorkRecordDto?> FindBySaveOperationIdAsync(Guid operationId, CancellationToken cancellationToken)
    {
        if (operationId == Guid.Empty) throw new ArgumentException("Operation id cannot be empty.", nameof(operationId));
        return FindBySqlAsync("""
            SELECT id, work_date, service_id, time_category_id, input_mode, work_minutes, start_time_minutes,
                   end_time_minutes, source_service_preset_id, source_basic_shift_id, source_work_record_id
            FROM work_record WHERE save_operation_id = $value;
            """, SqliteValue.Id(operationId), cancellationToken);
    }

    public async IAsyncEnumerable<WorkRecordDto> StreamRangeAsync(DateOnly startDate, DateOnly endDate,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (startDate > endDate) throw new ArgumentException("Start date must not follow end date.", nameof(startDate));
        var values = await database.ReadAsync(async (connection, transaction, token) =>
        {
            var result = new List<WorkRecordDto>();
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                SELECT id, work_date, service_id, time_category_id, input_mode, work_minutes, start_time_minutes,
                       end_time_minutes, source_service_preset_id, source_basic_shift_id, source_work_record_id
                FROM work_record WHERE work_date BETWEEN $start AND $end ORDER BY work_date, id;
                """;
            command.Parameters.AddValue("$start", SqliteValue.Date(startDate));
            command.Parameters.AddValue("$end", SqliteValue.Date(endDate));
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false)) result.Add(Read(reader));
            return result;
        }, cancellationToken).ConfigureAwait(false);

        foreach (var value in values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return value;
        }
    }

    public Task UpsertAsync(WorkRecordDto workRecord, CancellationToken cancellationToken)
    {
        Validate(workRecord);
        return WriteAsync(workRecord, null, upsert: true, cancellationToken);
    }

    public Task<bool> TryInsertAsync(WorkRecordDto workRecord, Guid operationId,
        CancellationToken cancellationToken)
    {
        Validate(workRecord);
        if (operationId == Guid.Empty) throw new ArgumentException("Operation id cannot be empty.", nameof(operationId));
        return database.WriteAsync(async (connection, transaction, token) =>
        {
            await using (var check = connection.CreateCommand())
            {
                check.Transaction = transaction;
                check.CommandText = "SELECT EXISTS(SELECT 1 FROM work_record WHERE save_operation_id = $operation);";
                check.Parameters.AddValue("$operation", SqliteValue.Id(operationId));
                if (Convert.ToInt64(await check.ExecuteScalarAsync(token).ConfigureAwait(false)) != 0) return false;
            }

            await InsertOrUpdateAsync(connection, transaction, workRecord, SqliteValue.Id(operationId), false,
                SqliteValue.Utc(clock.UtcNow), token).ConfigureAwait(false);
            return true;
        }, cancellationToken);
    }

    public Task DeleteAsync(WorkRecordId id, CancellationToken cancellationToken) =>
        SqliteRepositoryCommand.DeleteByIdAsync(database, "work_record", id.Value, cancellationToken);

    private Task<bool> WriteAsync(WorkRecordDto value, string? operationId, bool upsert,
        CancellationToken cancellationToken) => database.WriteAsync(async (connection, transaction, token) =>
    {
        await InsertOrUpdateAsync(connection, transaction, value, operationId, upsert,
            SqliteValue.Utc(clock.UtcNow), token).ConfigureAwait(false);
        return true;
    }, cancellationToken);

    private static async Task InsertOrUpdateAsync(SqliteConnection connection, SqliteTransaction transaction,
        WorkRecordDto value, string? operationId, bool upsert, string now, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO work_record(id, work_date, service_id, time_category_id, input_mode, work_minutes,
                start_time_minutes, end_time_minutes, source_service_preset_id, source_basic_shift_id,
                source_work_record_id, save_operation_id, created_at_utc, updated_at_utc)
            VALUES($id, $date, $service, $category, $mode, $minutes, $start, $end, $preset, $shift,
                $source, $operation, $now, $now)
            """ + (upsert ? """
            ON CONFLICT(id) DO UPDATE SET work_date = excluded.work_date, service_id = excluded.service_id,
                time_category_id = excluded.time_category_id, input_mode = excluded.input_mode,
                work_minutes = excluded.work_minutes, start_time_minutes = excluded.start_time_minutes,
                end_time_minutes = excluded.end_time_minutes,
                source_service_preset_id = excluded.source_service_preset_id,
                source_basic_shift_id = excluded.source_basic_shift_id,
                source_work_record_id = excluded.source_work_record_id, updated_at_utc = excluded.updated_at_utc;
            """ : ";");
        Bind(command, value, operationId, now);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private Task<WorkRecordDto?> FindBySqlAsync(string sql, string? value, CancellationToken cancellationToken) =>
        database.ReadAsync(async (connection, transaction, token) =>
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            if (value is not null) command.Parameters.AddValue("$value", value);
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            return await reader.ReadAsync(token).ConfigureAwait(false) ? Read(reader) : null;
        }, cancellationToken);

    private static void Validate(WorkRecordDto value)
    {
        ArgumentNullException.ThrowIfNull(value);
        _ = new WorkRecord(value.Id, value.WorkDate, value.ServiceId, value.TimeCategoryId, value.InputMode,
            value.WorkMinutes, value.StartTime, value.EndTime);
    }

    private static void Bind(SqliteCommand command, WorkRecordDto value, string? operationId, string now)
    {
        command.Parameters.AddValue("$id", SqliteValue.Id(value.Id.Value));
        command.Parameters.AddValue("$date", SqliteValue.Date(value.WorkDate));
        command.Parameters.AddValue("$service", SqliteValue.Id(value.ServiceId.Value));
        command.Parameters.AddValue("$category", value.TimeCategoryId is { } category ? SqliteValue.Id(category.Value) : null);
        command.Parameters.AddValue("$mode", value.InputMode.ToString());
        command.Parameters.AddValue("$minutes", value.WorkMinutes.Value);
        command.Parameters.AddValue("$start", value.StartTime?.Value);
        command.Parameters.AddValue("$end", value.EndTime?.Value);
        command.Parameters.AddValue("$preset", value.SourceServicePresetId is { } preset ? SqliteValue.Id(preset.Value) : null);
        command.Parameters.AddValue("$shift", value.SourceBasicShiftId is { } shift ? SqliteValue.Id(shift.Value) : null);
        command.Parameters.AddValue("$source", value.SourceWorkRecordId is { } source ? SqliteValue.Id(source.Value) : null);
        command.Parameters.AddValue("$operation", operationId);
        command.Parameters.AddValue("$now", now);
    }

    internal static WorkRecordDto Read(SqliteDataReader reader) => new(
        new WorkRecordId(SqliteValue.Guid(reader.GetString("id"))),
        SqliteValue.Date(reader.GetString("work_date")),
        new ServiceId(SqliteValue.Guid(reader.GetString("service_id"))),
        reader.GetNullableString("time_category_id") is { } category
            ? new TimeCategoryId(SqliteValue.Guid(category)) : null,
        Enum.Parse<WorkInputMode>(reader.GetString("input_mode"), false),
        new WorkMinutes(reader.GetInt32("work_minutes")),
        reader.GetNullableInt32("start_time_minutes") is { } start ? new MinuteOfDay(start) : null,
        reader.GetNullableInt32("end_time_minutes") is { } end ? new MinuteOfDay(end) : null,
        reader.GetNullableString("source_service_preset_id") is { } preset
            ? new ServicePresetId(SqliteValue.Guid(preset)) : null,
        reader.GetNullableString("source_basic_shift_id") is { } shift
            ? new BasicShiftId(SqliteValue.Guid(shift)) : null,
        reader.GetNullableString("source_work_record_id") is { } source
            ? new WorkRecordId(SqliteValue.Guid(source)) : null);
}

public sealed class SqliteMonthlyAllowanceRepository(SqliteDatabase database, IUtcClock clock)
    : IMonthlyAllowanceRepository
{
    private readonly SqliteDatabase database = database ?? throw new ArgumentNullException(nameof(database));
    private readonly IUtcClock clock = clock ?? throw new ArgumentNullException(nameof(clock));

    public Task<IReadOnlyList<MonthlyAllowance>> GetForPeriodAsync(PayrollPeriodKey payrollPeriodKey,
        CancellationToken cancellationToken) => database.ReadAsync(async (connection, transaction, token) =>
    {
        var values = new List<MonthlyAllowance>();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id, payroll_period_year_month, display_name, amount_yen FROM monthly_allowance
            WHERE payroll_period_year_month = $period ORDER BY created_at_utc, id;
            """;
        command.Parameters.AddValue("$period", SqliteValue.YearMonth(payrollPeriodKey.Value));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
            values.Add(new MonthlyAllowance(
                new MonthlyAllowanceId(SqliteValue.Guid(reader.GetString("id"))),
                new PayrollPeriodKey(SqliteValue.YearMonth(reader.GetInt64("payroll_period_year_month"))),
                reader.GetString("display_name"), new YenAmount(reader.GetInt64("amount_yen"))));
        return (IReadOnlyList<MonthlyAllowance>)values;
    }, cancellationToken);

    public Task UpsertAsync(MonthlyAllowance allowance, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(allowance);
        return database.WriteAsync(async (connection, transaction, token) =>
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO monthly_allowance(id, payroll_period_year_month, display_name, amount_yen,
                    created_at_utc, updated_at_utc)
                VALUES($id, $period, $name, $amount, $now, $now)
                ON CONFLICT(id) DO UPDATE SET payroll_period_year_month = excluded.payroll_period_year_month,
                    display_name = excluded.display_name, amount_yen = excluded.amount_yen,
                    updated_at_utc = excluded.updated_at_utc;
                """;
            command.Parameters.AddValue("$id", SqliteValue.Id(allowance.Id.Value));
            command.Parameters.AddValue("$period", SqliteValue.YearMonth(allowance.PayrollPeriodKey.Value));
            command.Parameters.AddValue("$name", allowance.DisplayName);
            command.Parameters.AddValue("$amount", allowance.Amount.Value);
            command.Parameters.AddValue("$now", SqliteValue.Utc(clock.UtcNow));
            await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            return true;
        }, cancellationToken);
    }

    public Task DeleteAsync(MonthlyAllowanceId id, CancellationToken cancellationToken) =>
        SqliteRepositoryCommand.DeleteByIdAsync(database, "monthly_allowance", id.Value, cancellationToken);
}

public sealed class SqliteClosingRuleRepository(SqliteDatabase database, IUtcClock clock) : IClosingRuleRepository
{
    private readonly SqliteDatabase database = database ?? throw new ArgumentNullException(nameof(database));
    private readonly IUtcClock clock = clock ?? throw new ArgumentNullException(nameof(clock));

    public async Task<ClosingRuleHistorySnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        var rules = await GetHistoryAsync(cancellationToken).ConfigureAwait(false);
        return new ClosingRuleHistorySnapshot(rules, Version(rules));
    }

    public Task<IReadOnlyList<ClosingRule>> GetHistoryAsync(CancellationToken cancellationToken) =>
        database.ReadAsync(async (connection, transaction, token) =>
        {
            var values = new List<ClosingRule>();
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                SELECT id, effective_from_year_month, closing_day FROM closing_rule_history
                ORDER BY effective_from_year_month, id;
                """;
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
                values.Add(new ClosingRule(
                    new ClosingRuleId(SqliteValue.Guid(reader.GetString("id"))),
                    new PayrollPeriodKey(SqliteValue.YearMonth(reader.GetInt64("effective_from_year_month"))),
                    reader.GetNullableInt32("closing_day")));
            return (IReadOnlyList<ClosingRule>)values;
        }, cancellationToken);

    public Task<bool> TryReplaceEffectiveRuleAsync(ClosingRule rule, ClosingRuleHistoryVersion expectedVersion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(expectedVersion);
        return database.WriteAsync(async (connection, transaction, token) =>
        {
            var current = await ReadHistoryAsync(connection, transaction, token).ConfigureAwait(false);
            if (!StringComparer.Ordinal.Equals(Version(current).Value, expectedVersion.Value)) return false;
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO closing_rule_history(id, effective_from_year_month, closing_day, is_end_of_month, created_at_utc)
                VALUES($id, $effective, $day, $eom, $now)
                ON CONFLICT(effective_from_year_month) DO UPDATE SET id = excluded.id,
                    closing_day = excluded.closing_day, is_end_of_month = excluded.is_end_of_month,
                    created_at_utc = excluded.created_at_utc;
                """;
            command.Parameters.AddValue("$id", SqliteValue.Id(rule.Id.Value));
            command.Parameters.AddValue("$effective", SqliteValue.YearMonth(rule.EffectiveFrom.Value));
            command.Parameters.AddValue("$day", rule.ClosingDay);
            command.Parameters.AddValue("$eom", rule.ClosingDay is null ? 1 : 0);
            command.Parameters.AddValue("$now", SqliteValue.Utc(clock.UtcNow));
            await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            return true;
        }, cancellationToken);
    }

    private static async Task<IReadOnlyList<ClosingRule>> ReadHistoryAsync(SqliteConnection connection,
        SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        var values = new List<ClosingRule>();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id, effective_from_year_month, closing_day FROM closing_rule_history
            ORDER BY effective_from_year_month, id;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            values.Add(new ClosingRule(
                new ClosingRuleId(SqliteValue.Guid(reader.GetString("id"))),
                new PayrollPeriodKey(SqliteValue.YearMonth(reader.GetInt64("effective_from_year_month"))),
                reader.GetNullableInt32("closing_day")));
        return values;
    }

    private static ClosingRuleHistoryVersion Version(IReadOnlyList<ClosingRule> rules)
    {
        var text = string.Join('|', rules.Select(rule =>
            $"{SqliteValue.Id(rule.Id.Value)}:{SqliteValue.YearMonth(rule.EffectiveFrom.Value)}:{rule.ClosingDay?.ToString() ?? "EOM"}"));
        return new ClosingRuleHistoryVersion(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant());
    }
}

public sealed class SqliteHolidayCalendarRepository(SqliteDatabase database) : IHolidayCalendarRepository
{
    private readonly SqliteDatabase database = database ?? throw new ArgumentNullException(nameof(database));

    public async Task<HolidayCalendar> GetAsync(HolidayCalendarVersionId versionId,
        CancellationToken cancellationToken)
    {
        var result = await GetManyAsync([versionId], cancellationToken).ConfigureAwait(false);
        return result[versionId];
    }

    public Task<IReadOnlyDictionary<HolidayCalendarVersionId, HolidayCalendar>> GetManyAsync(
        IReadOnlyCollection<HolidayCalendarVersionId> versionIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(versionIds);
        cancellationToken.ThrowIfCancellationRequested();
        var requested = versionIds.Distinct().OrderBy(x => x.Value).ToArray();
        if (requested.Length == 0)
            return Task.FromResult<IReadOnlyDictionary<HolidayCalendarVersionId, HolidayCalendar>>(
                new Dictionary<HolidayCalendarVersionId, HolidayCalendar>());

        return database.ReadAsync(async (connection, transaction, token) =>
        {
            var parameterNames = new string[requested.Length];
            var byDatabaseId = new Dictionary<string, HolidayCalendarVersionId>(StringComparer.Ordinal);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            for (var index = 0; index < requested.Length; index++)
            {
                parameterNames[index] = $"$version{index}";
                var id = SqliteValue.Id(requested[index].Value);
                command.Parameters.AddValue(parameterNames[index], id);
                byDatabaseId.Add(id, requested[index]);
            }

            command.CommandText = $"SELECT id FROM holiday_calendar_version WHERE id IN ({string.Join(", ", parameterNames)});";
            var found = new HashSet<string>(StringComparer.Ordinal);
            await using (var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(token).ConfigureAwait(false)) found.Add(reader.GetString("id"));
            }
            if (found.Count != requested.Length)
                throw new KeyNotFoundException("Holiday calendar version was not found.");

            var holidays = requested.ToDictionary(x => x, _ => new Dictionary<DateOnly, string>());
            command.CommandText = $"""
                SELECT holiday_calendar_version_id, holiday_date, display_name FROM holiday_date
                WHERE holiday_calendar_version_id IN ({string.Join(", ", parameterNames)})
                ORDER BY holiday_calendar_version_id, holiday_date;
                """;
            await using (var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(token).ConfigureAwait(false))
                {
                    var versionId = byDatabaseId[reader.GetString("holiday_calendar_version_id")];
                    holidays[versionId].Add(
                        SqliteValue.Date(reader.GetString("holiday_date")), reader.GetString("display_name"));
                }
            }
            return (IReadOnlyDictionary<HolidayCalendarVersionId, HolidayCalendar>)
                holidays.ToDictionary(x => x.Key, x => new HolidayCalendar(x.Key, x.Value));
        }, cancellationToken);
    }

    public Task<HolidayCalendarVersionId> GetLatestVerifiedVersionIdAsync(CancellationToken cancellationToken) =>
        database.ReadAsync(async (connection, transaction, token) =>
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                SELECT id FROM holiday_calendar_version ORDER BY source_reference_date DESC, created_at_utc DESC, id DESC LIMIT 1;
                """;
            var result = await command.ExecuteScalarAsync(token).ConfigureAwait(false) as string;
            return result is null
                ? throw new InvalidDataException("No verified holiday calendar is installed.")
                : new HolidayCalendarVersionId(SqliteValue.Guid(result));
        }, cancellationToken);
}

internal static class SqliteRepositoryCommand
{
    public static Task DeleteByIdAsync(SqliteDatabase database, string table, Guid id,
        CancellationToken cancellationToken)
    {
        var allowed = table is "service_preset" or "basic_shift" or "work_record" or "monthly_allowance";
        if (!allowed) throw new ArgumentOutOfRangeException(nameof(table));
        return database.WriteAsync(async (connection, transaction, token) =>
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"DELETE FROM {table} WHERE id = $id;";
            command.Parameters.AddValue("$id", SqliteValue.Id(id));
            await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            return true;
        }, cancellationToken);
    }
}
