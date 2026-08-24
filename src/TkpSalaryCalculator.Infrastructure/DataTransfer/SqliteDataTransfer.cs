using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TkpSalaryCalculator.Application.Contracts;
using TkpSalaryCalculator.Application.Ports;
using TkpSalaryCalculator.Domain.Models;
using TkpSalaryCalculator.Domain.ValueObjects;
using TkpSalaryCalculator.Infrastructure.Sqlite;

namespace TkpSalaryCalculator.Infrastructure.DataTransfer;

/// <summary>WAL 読み取りトランザクションから再現に必要な正本行だけを逐次出力します。</summary>
public sealed class SqliteExportDataSource(SqliteDatabase database) : IExportDataSource
{
    private readonly SqliteDatabase database = database ?? throw new ArgumentNullException(nameof(database));

    public async Task<IExportReadSession> OpenReadSessionAsync(CancellationToken cancellationToken)
    {
        var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var transaction = connection.BeginTransaction(deferred: true);
            await using (var establishSnapshot = connection.CreateCommand())
            {
                establishSnapshot.Transaction = transaction;
                establishSnapshot.CommandText = "SELECT id FROM app_metadata WHERE id = 1;";
                _ = await establishSnapshot.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            }
            return new ReadSession(connection, transaction);
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private sealed class ReadSession(SqliteConnection connection, SqliteTransaction transaction) : IExportReadSession
    {
        private bool disposed;

        public async IAsyncEnumerable<DataTransferRecord> StreamAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var nextSequence = new Dictionary<DataTransferSection, long>();
            foreach (var query in ExportQueries)
            {
                if (!nextSequence.TryGetValue(query.Section, out var sequence)) sequence = query.FirstSequence;
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = query.Sql;
                await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    var values = new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["type"] = query.Type,
                    };
                    for (var index = 0; index < reader.FieldCount; index++)
                        values[reader.GetName(index)] = reader.IsDBNull(index) ? null : reader.GetValue(index);
                    var element = JsonSerializer.SerializeToElement(values);
                    yield return new DataTransferRecord<JsonElement>(query.Section, sequence++, element);
                }
                nextSequence[query.Section] = sequence;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (disposed) return;
            disposed = true;
            try
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // Read-only disposal is best effort.
            }
            await transaction.DisposeAsync().ConfigureAwait(false);
            await connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    private const string SnapshotSet = """
        WITH exported_snapshot(id) AS (
            SELECT initial_snapshot_id FROM app_metadata WHERE id = 1 AND initial_snapshot_id IS NOT NULL
            UNION SELECT snapshot_id FROM setting_month
        )
        """;

    private static readonly ExportQuery[] ExportQueries =
    [
        new(DataTransferSection.Metadata, 1, "app_metadata", """
            SELECT initial_setup_status, initial_setup_step, initial_snapshot_id, export_format_version,
                   last_exported_at_utc, last_data_changed_at_utc, created_at_utc, updated_at_utc
            FROM app_metadata WHERE id = 1;
            """),
        new(DataTransferSection.SettingMonths, 0, "setting_month", """
            SELECT year_month, snapshot_id, created_at_utc, updated_at_utc
            FROM setting_month ORDER BY year_month;
            """),
        new(DataTransferSection.SettingSnapshots, 0, "setting_snapshot", SnapshotSet + """
            SELECT s.id,
                   CASE WHEN s.based_on_id IN (SELECT id FROM exported_snapshot) THEN s.based_on_id ELSE NULL END AS based_on_id,
                   s.holiday_calendar_version_id, s.schema_version, s.created_at_utc
            FROM setting_snapshot s JOIN exported_snapshot e ON e.id = s.id ORDER BY s.id;
            """),
        new(DataTransferSection.SettingSnapshots, 0, "snapshot_service", SnapshotSet + """
            SELECT c.snapshot_id, c.service_id, c.display_name, c.display_order, c.is_enabled
            FROM snapshot_service c JOIN exported_snapshot e ON e.id = c.snapshot_id
            ORDER BY c.snapshot_id, c.service_id;
            """),
        new(DataTransferSection.SettingSnapshots, 0, "snapshot_time_category", SnapshotSet + """
            SELECT c.snapshot_id, c.time_category_id, c.service_id, c.display_name, c.standard_minutes,
                   c.display_order, c.is_enabled
            FROM snapshot_time_category c JOIN exported_snapshot e ON e.id = c.snapshot_id
            ORDER BY c.snapshot_id, c.time_category_id;
            """),
        new(DataTransferSection.SettingSnapshots, 0, "snapshot_rate", SnapshotSet + """
            SELECT c.snapshot_id, c.service_id, c.time_category_id, c.rate_type, c.amount_yen
            FROM snapshot_rate c JOIN exported_snapshot e ON e.id = c.snapshot_id
            ORDER BY c.snapshot_id, c.service_id, c.time_category_id;
            """),
        new(DataTransferSection.SettingSnapshots, 0, "snapshot_premium", SnapshotSet + """
            SELECT c.snapshot_id, c.premium_id, c.display_name, c.calculation_type,
                   c.percentage_basis_points, c.amount_yen, c.start_time_minutes, c.end_time_minutes,
                   c.uses_national_holidays, c.is_enabled
            FROM snapshot_premium c JOIN exported_snapshot e ON e.id = c.snapshot_id
            ORDER BY c.snapshot_id, c.premium_id;
            """),
        new(DataTransferSection.SettingSnapshots, 0, "snapshot_premium_weekday", SnapshotSet + """
            SELECT c.snapshot_id, c.premium_id, c.weekday FROM snapshot_premium_weekday c
            JOIN exported_snapshot e ON e.id = c.snapshot_id ORDER BY c.snapshot_id, c.premium_id, c.weekday;
            """),
        new(DataTransferSection.SettingSnapshots, 0, "snapshot_premium_date", SnapshotSet + """
            SELECT c.snapshot_id, c.premium_id, c.target_date FROM snapshot_premium_date c
            JOIN exported_snapshot e ON e.id = c.snapshot_id ORDER BY c.snapshot_id, c.premium_id, c.target_date;
            """),
        new(DataTransferSection.SettingSnapshots, 0, "snapshot_premium_service", SnapshotSet + """
            SELECT c.snapshot_id, c.premium_id, c.service_id FROM snapshot_premium_service c
            JOIN exported_snapshot e ON e.id = c.snapshot_id ORDER BY c.snapshot_id, c.premium_id, c.service_id;
            """),
        new(DataTransferSection.SettingSnapshots, 0, "snapshot_count_bonus", SnapshotSet + """
            SELECT c.snapshot_id, c.count_bonus_id, c.display_name, c.amount_yen, c.is_enabled
            FROM snapshot_count_bonus c JOIN exported_snapshot e ON e.id = c.snapshot_id
            ORDER BY c.snapshot_id, c.count_bonus_id;
            """),
        new(DataTransferSection.SettingSnapshots, 0, "snapshot_count_bonus_service", SnapshotSet + """
            SELECT c.snapshot_id, c.count_bonus_id, c.service_id FROM snapshot_count_bonus_service c
            JOIN exported_snapshot e ON e.id = c.snapshot_id ORDER BY c.snapshot_id, c.count_bonus_id, c.service_id;
            """),
        new(DataTransferSection.ClosingRules, 0, "closing_rule_history", """
            SELECT id, effective_from_year_month, closing_day, is_end_of_month, created_at_utc
            FROM closing_rule_history ORDER BY effective_from_year_month;
            """),
        new(DataTransferSection.MonthlyAllowances, 0, "monthly_allowance", """
            SELECT id, payroll_period_year_month, display_name, amount_yen, created_at_utc, updated_at_utc
            FROM monthly_allowance ORDER BY payroll_period_year_month, id;
            """),
        new(DataTransferSection.Definitions, 0, "service_definition", SnapshotSet + """
            SELECT d.id, d.created_at_utc FROM service_definition d WHERE d.id IN (
                SELECT service_id FROM snapshot_service WHERE snapshot_id IN (SELECT id FROM exported_snapshot)
                UNION SELECT service_id FROM service_preset UNION SELECT service_id FROM basic_shift
                UNION SELECT service_id FROM work_record) ORDER BY d.id;
            """),
        new(DataTransferSection.Definitions, 0, "time_category_definition", SnapshotSet + """
            SELECT d.id, d.created_at_utc FROM time_category_definition d WHERE d.id IN (
                SELECT time_category_id FROM snapshot_time_category WHERE snapshot_id IN (SELECT id FROM exported_snapshot)
                UNION SELECT time_category_id FROM service_preset WHERE time_category_id IS NOT NULL
                UNION SELECT time_category_id FROM basic_shift WHERE time_category_id IS NOT NULL
                UNION SELECT time_category_id FROM work_record WHERE time_category_id IS NOT NULL) ORDER BY d.id;
            """),
        new(DataTransferSection.Definitions, 0, "premium_definition", SnapshotSet + """
            SELECT d.id, d.created_at_utc FROM premium_definition d WHERE d.id IN (
                SELECT premium_id FROM snapshot_premium WHERE snapshot_id IN (SELECT id FROM exported_snapshot)) ORDER BY d.id;
            """),
        new(DataTransferSection.Definitions, 0, "count_bonus_definition", SnapshotSet + """
            SELECT d.id, d.created_at_utc FROM count_bonus_definition d WHERE d.id IN (
                SELECT count_bonus_id FROM snapshot_count_bonus WHERE snapshot_id IN (SELECT id FROM exported_snapshot)) ORDER BY d.id;
            """),
        new(DataTransferSection.ServicePresets, 0, "service_preset", """
            SELECT id, display_name, service_id, time_category_id, default_work_minutes, display_order,
                   is_enabled, created_at_utc, updated_at_utc FROM service_preset ORDER BY id;
            """),
        new(DataTransferSection.BasicShifts, 0, "basic_shift", """
            SELECT id, weekday, service_preset_id, service_id, time_category_id, input_mode, work_minutes,
                   start_time_minutes, end_time_minutes, display_order, is_enabled, created_at_utc, updated_at_utc
            FROM basic_shift ORDER BY id;
            """),
        new(DataTransferSection.WorkRecords, 0, "work_record", """
            SELECT id, work_date, service_id, time_category_id, input_mode, work_minutes,
                   start_time_minutes, end_time_minutes, source_service_preset_id, source_basic_shift_id,
                   source_work_record_id, save_operation_id, created_at_utc, updated_at_utc
            FROM work_record ORDER BY work_date, id;
            """),
        new(DataTransferSection.Holidays, 0, "holiday_calendar_version", SnapshotSet + """
            SELECT h.id, h.version_name, h.source_name, h.source_reference_date, h.created_at_utc
            FROM holiday_calendar_version h WHERE h.id IN (
                SELECT holiday_calendar_version_id FROM setting_snapshot WHERE id IN (SELECT id FROM exported_snapshot))
            ORDER BY h.id;
            """),
        new(DataTransferSection.Holidays, 0, "holiday_date", SnapshotSet + """
            SELECT h.holiday_calendar_version_id, h.holiday_date, h.display_name FROM holiday_date h
            WHERE h.holiday_calendar_version_id IN (
                SELECT holiday_calendar_version_id FROM setting_snapshot WHERE id IN (SELECT id FROM exported_snapshot))
            ORDER BY h.holiday_calendar_version_id, h.holiday_date;
            """),
    ];

    private sealed record ExportQuery(DataTransferSection Section, long FirstSequence, string Type, string Sql);
}

/// <summary>インポートを一時 SQLite へ格納し、確認前の検証と確認後の全置換を行います。</summary>
public sealed class SqliteImportStagingRepository : IImportStagingRepository
{
    private const string FormatName = "tkp-salary-calculator";
    private const int FormatVersion = 1;
    private readonly SqliteDatabase liveDatabase;
    private readonly string stagingDirectory;
    private readonly IUtcClock clock;
    private readonly ConcurrentDictionary<Guid, PreparedImportState> active = new();

    public SqliteImportStagingRepository(SqliteDatabase liveDatabase, string stagingDirectory, IUtcClock clock)
    {
        this.liveDatabase = liveDatabase ?? throw new ArgumentNullException(nameof(liveDatabase));
        ArgumentException.ThrowIfNullOrWhiteSpace(stagingDirectory);
        this.stagingDirectory = Path.GetFullPath(stagingDirectory);
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<PreparedImportId> CreateAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(stagingDirectory);
        var id = new PreparedImportId(Guid.NewGuid());
        var path = StagePath(id);
        await using var connection = await OpenStageConnectionAsync(path, cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, null, """
            CREATE TABLE staged_record(
                section INTEGER NOT NULL,
                sequence INTEGER NOT NULL CHECK(sequence >= 0),
                record_type TEXT NOT NULL,
                json TEXT NOT NULL,
                PRIMARY KEY(section, sequence)
            );
            CREATE TABLE staged_state(
                id INTEGER PRIMARY KEY CHECK(id = 1),
                created_at_utc TEXT NOT NULL,
                is_validated INTEGER NOT NULL CHECK(is_validated IN (0, 1)),
                export_created_at_utc TEXT NULL,
                format_version INTEGER NULL
            );
            """, cancellationToken).ConfigureAwait(false);
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "INSERT INTO staged_state VALUES(1, $created, 0, NULL, NULL);";
            command.Parameters.AddWithValue("$created", SqliteValue.Utc(clock.UtcNow));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        active.TryAdd(id.Value, PreparedImportState.Preparing);
        return id;
    }

    public async Task AppendBatchAsync(PreparedImportId preparedImportId, IReadOnlyList<DataTransferRecord> records,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(records);
        if (!active.TryGetValue(preparedImportId.Value, out var state) ||
            state != PreparedImportState.Preparing)
            throw new KeyNotFoundException("Prepared import was not found or is no longer writable.");
        await using var connection = await OpenStageConnectionAsync(StagePath(preparedImportId), cancellationToken)
            .ConfigureAwait(false);
        await using var transaction = connection.BeginTransaction(deferred: false);
        try
        {
            foreach (var record in records)
            {
                ArgumentNullException.ThrowIfNull(record);
                if (record.Sequence < 0) throw new InvalidDataException("Record sequence cannot be negative.");
                var element = ToJsonElement(record);
                if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty("type", out var typeElement) ||
                    string.IsNullOrWhiteSpace(typeElement.GetString()))
                    throw new InvalidDataException("Every transfer value requires a type discriminator.");
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO staged_record(section, sequence, record_type, json)
                    VALUES($section, $sequence, $type, $json);
                    """;
                command.Parameters.AddWithValue("$section", (int)record.Section);
                command.Parameters.AddWithValue("$sequence", record.Sequence);
                command.Parameters.AddWithValue("$type", typeElement.GetString()!);
                command.Parameters.AddWithValue("$json", element.GetRawText());
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    // DATA-003/DATA-004/DATA-005: validation only touches staging and a candidate DB.
    public async Task<ImportPreviewDto> ValidateAsync(PreparedImportId preparedImportId,
        CancellationToken cancellationToken)
    {
        if (!active.TryGetValue(preparedImportId.Value, out var state) ||
            state != PreparedImportState.Preparing)
            throw new KeyNotFoundException("Prepared import was not found or is no longer validatable.");
        var stagePath = StagePath(preparedImportId);
        var candidatePath = CandidatePath(preparedImportId);
        DeleteDatabaseFiles(candidatePath);

        await using var stage = await OpenStageConnectionAsync(stagePath, cancellationToken).ConfigureAwait(false);
        await ValidateSequencesAndTypesAsync(stage, cancellationToken).ConfigureAwait(false);
        var header = await ReadSingleRecordAsync(stage, "document_header", cancellationToken).ConfigureAwait(false);
        if (RequiredString(header, "format") != FormatName)
            throw new InvalidDataException("The import format is not supported.");
        var version = RequiredInt32(header, "formatVersion");
        if (version != FormatVersion) throw new InvalidDataException($"Export format version {version} is not supported.");
        var exportCreated = SqliteValue.Utc(RequiredString(header, "createdAtUtc"));

        // The candidate must contain only imported rows. Default bootstrap data would otherwise
        // collide with stable seed IDs and contaminate the import preview.
        var candidate = new SqliteDatabase(candidatePath, bootstrapDefaults: false);
        await candidate.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await candidate.RunTransactionAsync(async token =>
        {
            var context = candidate.AmbientTransaction!;
            await SqliteDatabase.ExecuteNonQueryAsync(context.Connection, context.Transaction,
                "PRAGMA defer_foreign_keys = ON;", token).ConfigureAwait(false);
            foreach (var table in InsertOrder)
                await InsertStagedTableAsync(stage, context.Connection, context.Transaction, table, token)
                    .ConfigureAwait(false);
            await ApplyStagedMetadataAsync(stage, context.Connection, context.Transaction, token).ConfigureAwait(false);
            await ValidateCandidateAsync(context.Connection, context.Transaction, version, token).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);

        var preview = await BuildPreviewAsync(candidate, preparedImportId, version, exportCreated, cancellationToken)
            .ConfigureAwait(false);
        await using (var command = stage.CreateCommand())
        {
            command.CommandText = """
                UPDATE staged_state SET is_validated = 1, export_created_at_utc = $created,
                    format_version = $version WHERE id = 1;
                """;
            command.Parameters.AddWithValue("$created", SqliteValue.Utc(exportCreated));
            command.Parameters.AddWithValue("$version", version);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        if (!active.TryUpdate(preparedImportId.Value, PreparedImportState.Validated,
                PreparedImportState.Preparing))
            throw new InvalidOperationException("The prepared import changed state during validation.");
        return preview;
    }

    // DATA-002/DATA-007: replacement is committed before installation-local bootstrap is applied.
    // A temporary snapshot of the old live rows remains on the same connection until bootstrap succeeds,
    // so a bootstrap or final-validation failure can restore the pre-import database before returning.
    public async Task<bool> TryConsumeAndReplaceLiveDataAsync(PreparedImportId preparedImportId,
        DateTimeOffset importedAtUtc, CancellationToken cancellationToken)
    {
        if (!active.TryUpdate(preparedImportId.Value, PreparedImportState.Consuming,
                PreparedImportState.Validated))
            return false;

        var stagePath = StagePath(preparedImportId);
        var candidatePath = CandidatePath(preparedImportId);
        if (!File.Exists(stagePath) || !File.Exists(candidatePath))
        {
            active.TryUpdate(preparedImportId.Value, PreparedImportState.Validated,
                PreparedImportState.Consuming);
            return false;
        }

        try
        {
            if (liveDatabase.AmbientTransaction is not null)
                throw new InvalidOperationException("Import replacement must own its transaction boundary.");

            await using (var stage = await OpenStageConnectionAsync(stagePath, cancellationToken).ConfigureAwait(false))
            await using (var stagedState = stage.CreateCommand())
            {
                stagedState.CommandText = "SELECT is_validated FROM staged_state WHERE id = 1;";
                if (Convert.ToInt64(await stagedState.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
                        CultureInfo.InvariantCulture) != 1)
                {
                    active.TryUpdate(preparedImportId.Value, PreparedImportState.Validated,
                        PreparedImportState.Consuming);
                    return false;
                }
            }

            var candidate = new SqliteDatabase(candidatePath, bootstrapDefaults: false);
            await candidate.InitializeAsync(cancellationToken).ConfigureAwait(false);
            await using var source = await candidate.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var live = await liveDatabase.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            var backupSuffix = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
            try
            {
                await using (var replacement = live.BeginTransaction(deferred: false))
                {
                    try
                    {
                        await CreateLiveBackupAsync(live, replacement, backupSuffix, cancellationToken)
                            .ConfigureAwait(false);
                        await SqliteDatabase.ExecuteNonQueryAsync(live, replacement, "PRAGMA defer_foreign_keys = ON;",
                            cancellationToken).ConfigureAwait(false);
                        foreach (var table in DeleteOrder)
                            await SqliteDatabase.ExecuteNonQueryAsync(live, replacement, $"DELETE FROM {table};",
                                cancellationToken).ConfigureAwait(false);
                        foreach (var table in InsertOrder)
                            await CopyTableAsync(source, live, replacement, table, cancellationToken).ConfigureAwait(false);
                        await CopyImportedMetadataAsync(source, live, replacement, importedAtUtc, cancellationToken)
                            .ConfigureAwait(false);
                        await replacement.CommitAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch
                    {
                        await RollbackBestEffortAsync(replacement).ConfigureAwait(false);
                        throw;
                    }
                }

                try
                {
                    // Once replacement is committed, ignore caller cancellation until the database is either
                    // bootstrapped successfully or restored to the pre-import snapshot.
                    await using var bootstrap = live.BeginTransaction(deferred: false);
                    try
                    {
                        await SqliteDatabase.BootstrapVersionOneAsync(live, bootstrap, SqliteValue.Utc(importedAtUtc),
                            CancellationToken.None).ConfigureAwait(false);
                        await ValidateCandidateAsync(live, bootstrap, FormatVersion, CancellationToken.None)
                            .ConfigureAwait(false);
                        await bootstrap.CommitAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                    catch
                    {
                        await RollbackBestEffortAsync(bootstrap).ConfigureAwait(false);
                        throw;
                    }
                }
                catch (Exception bootstrapFailure)
                {
                    try
                    {
                        await RestoreLiveBackupAsync(live, backupSuffix, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception restoreFailure)
                    {
                        throw new AggregateException(
                            "Bundled bootstrap failed after import and the pre-import database could not be restored.",
                            bootstrapFailure, restoreFailure);
                    }
                    throw;
                }

                active.TryUpdate(preparedImportId.Value, PreparedImportState.Consumed,
                    PreparedImportState.Consuming);
                return true;
            }
            finally
            {
                await DropLiveBackupBestEffortAsync(live, backupSuffix).ConfigureAwait(false);
            }
        }
        catch
        {
            active.TryUpdate(preparedImportId.Value, PreparedImportState.Validated,
                PreparedImportState.Consuming);
            throw;
        }
    }

    private static async Task CreateLiveBackupAsync(SqliteConnection live, SqliteTransaction transaction,
        string backupSuffix, CancellationToken cancellationToken)
    {
        foreach (var table in InsertOrder)
        {
            await SqliteDatabase.ExecuteNonQueryAsync(live, transaction,
                $"CREATE TEMP TABLE {BackupTableName(backupSuffix, table.Name)} AS SELECT {string.Join(", ", table.Columns)} FROM {table.Name};",
                cancellationToken).ConfigureAwait(false);
        }
        await SqliteDatabase.ExecuteNonQueryAsync(live, transaction,
            $"CREATE TEMP TABLE {BackupTableName(backupSuffix, "app_metadata")} AS SELECT {string.Join(", ", MetadataColumns)} FROM app_metadata;",
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task RestoreLiveBackupAsync(SqliteConnection live, string backupSuffix,
        CancellationToken cancellationToken)
    {
        await using var transaction = live.BeginTransaction(deferred: false);
        try
        {
            await SqliteDatabase.ExecuteNonQueryAsync(live, transaction, "PRAGMA defer_foreign_keys = ON;",
                cancellationToken).ConfigureAwait(false);
            foreach (var table in DeleteOrder)
                await SqliteDatabase.ExecuteNonQueryAsync(live, transaction, $"DELETE FROM {table};",
                    cancellationToken).ConfigureAwait(false);
            await SqliteDatabase.ExecuteNonQueryAsync(live, transaction, "DELETE FROM app_metadata;",
                cancellationToken).ConfigureAwait(false);
            foreach (var table in InsertOrder)
            {
                var columns = string.Join(", ", table.Columns);
                await SqliteDatabase.ExecuteNonQueryAsync(live, transaction,
                    $"INSERT INTO {table.Name}({columns}) SELECT {columns} FROM {BackupTableName(backupSuffix, table.Name)};",
                    cancellationToken).ConfigureAwait(false);
            }
            var metadataColumns = string.Join(", ", MetadataColumns);
            await SqliteDatabase.ExecuteNonQueryAsync(live, transaction,
                $"INSERT INTO app_metadata({metadataColumns}) SELECT {metadataColumns} FROM {BackupTableName(backupSuffix, "app_metadata")};",
                cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await RollbackBestEffortAsync(transaction).ConfigureAwait(false);
            throw;
        }
    }

    private static async Task DropLiveBackupBestEffortAsync(SqliteConnection live, string backupSuffix)
    {
        foreach (var tableName in InsertOrder.Select(table => table.Name).Append("app_metadata"))
        {
            try
            {
                await SqliteDatabase.ExecuteNonQueryAsync(live, null,
                    $"DROP TABLE IF EXISTS {BackupTableName(backupSuffix, tableName)};", CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch
            {
                // Unique names prevent a cleanup failure from blocking a later import on a pooled connection.
            }
        }
    }

    private static string BackupTableName(string backupSuffix, string tableName) =>
        $"import_backup_{backupSuffix}_{tableName}";

    private static async Task RollbackBestEffortAsync(SqliteTransaction transaction)
    {
        try { await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false); }
        catch { /* Preserve the operation failure. Disposing the connection also rolls back an open transaction. */ }
    }

    public Task DiscardAsync(PreparedImportId preparedImportId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        active.TryRemove(preparedImportId.Value, out _);
        DeleteDatabaseFiles(StagePath(preparedImportId));
        DeleteDatabaseFiles(CandidatePath(preparedImportId));
        return Task.CompletedTask;
    }

    public Task DiscardAbandonedAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Directory.Exists(stagingDirectory)) return Task.CompletedTask;
        foreach (var path in Directory.EnumerateFiles(stagingDirectory, "tkp-import-*.db*", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = Path.GetFileName(path);
            if (active.Keys.Any(id => file.Contains(id.ToString("N"), StringComparison.OrdinalIgnoreCase))) continue;
            File.Delete(path);
        }
        return Task.CompletedTask;
    }

    private string StagePath(PreparedImportId id) => Path.Combine(stagingDirectory,
        $"tkp-import-{id.Value:N}.stage.db");
    private string CandidatePath(PreparedImportId id) => Path.Combine(stagingDirectory,
        $"tkp-import-{id.Value:N}.candidate.db");

    private static async Task<SqliteConnection> OpenStageConnectionAsync(string path,
        CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
            DefaultTimeout = 5,
        }.ToString());
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, null, "PRAGMA journal_mode = WAL; PRAGMA synchronous = FULL;",
            cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private static async Task ValidateSequencesAndTypesAsync(SqliteConnection stage,
        CancellationToken cancellationToken)
    {
        await using var command = stage.CreateCommand();
        command.CommandText = """
            SELECT section, MIN(sequence), MAX(sequence), COUNT(*) FROM staged_record GROUP BY section;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var section = (DataTransferSection)reader.GetInt32(0);
            if (!Enum.IsDefined(section)) throw new InvalidDataException("A record has an unknown section.");
            var min = reader.GetInt64(1);
            var max = reader.GetInt64(2);
            var count = reader.GetInt64(3);
            if (min != 0 || max != count - 1)
                throw new InvalidDataException($"Section {section} does not have contiguous sequence values.");
        }

        reader.Close();
        command.CommandText = "SELECT section, record_type FROM staged_record;";
        await using var typeReader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await typeReader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var section = (DataTransferSection)typeReader.GetInt32(0);
            var type = typeReader.GetString(1);
            if (!TypeToSection.TryGetValue(type, out var expected) || expected != section)
                throw new InvalidDataException($"Record type '{type}' is not valid in section {section}.");
        }
    }

    private static async Task<JsonElement> ReadSingleRecordAsync(SqliteConnection stage, string type,
        CancellationToken cancellationToken)
    {
        await using var command = stage.CreateCommand();
        command.CommandText = "SELECT json FROM staged_record WHERE record_type = $type;";
        command.Parameters.AddWithValue("$type", type);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            throw new InvalidDataException($"Required record '{type}' is missing.");
        using var document = JsonDocument.Parse(reader.GetString(0));
        var result = document.RootElement.Clone();
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            throw new InvalidDataException($"Record '{type}' must occur exactly once.");
        return result;
    }

    private static async Task InsertStagedTableAsync(SqliteConnection stage, SqliteConnection candidate,
        SqliteTransaction transaction, TableSpec table, CancellationToken cancellationToken)
    {
        await using var select = stage.CreateCommand();
        select.CommandText = "SELECT json FROM staged_record WHERE record_type = $type ORDER BY sequence;";
        select.Parameters.AddWithValue("$type", table.Name);
        await using var reader = await select.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            using var document = JsonDocument.Parse(reader.GetString(0));
            await InsertElementAsync(candidate, transaction, table, document.RootElement, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async Task InsertElementAsync(SqliteConnection connection, SqliteTransaction transaction,
        TableSpec table, JsonElement element, CancellationToken cancellationToken)
    {
        var names = string.Join(", ", table.Columns);
        var parameters = string.Join(", ", table.Columns.Select((_, index) => $"$p{index}"));
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"INSERT INTO {table.Name}({names}) VALUES({parameters});";
        for (var index = 0; index < table.Columns.Length; index++)
        {
            var column = table.Columns[index];
            object value = DBNull.Value;
            if (element.TryGetProperty(column, out var property) && property.ValueKind != JsonValueKind.Null)
            {
                value = property.ValueKind switch
                {
                    JsonValueKind.String => property.GetString()!,
                    JsonValueKind.Number when property.TryGetInt64(out var integer) => integer,
                    JsonValueKind.True => 1L,
                    JsonValueKind.False => 0L,
                    _ => throw new InvalidDataException($"Column {table.Name}.{column} has an invalid JSON value."),
                };
            }
            command.Parameters.AddWithValue($"$p{index}", value);
        }
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ApplyStagedMetadataAsync(SqliteConnection stage, SqliteConnection candidate,
        SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        var metadata = await ReadSingleRecordAsync(stage, "app_metadata", cancellationToken).ConfigureAwait(false);
        await using var command = candidate.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE app_metadata SET initial_setup_status = $status, initial_setup_step = $step,
                initial_snapshot_id = $snapshot, export_format_version = $version,
                last_exported_at_utc = $exported, last_data_changed_at_utc = $changed,
                backup_reminder_deferred_until_date = NULL, created_at_utc = $created, updated_at_utc = $updated
            WHERE id = 1;
            """;
        AddJson(command, "$status", metadata, "initial_setup_status");
        AddJson(command, "$step", metadata, "initial_setup_step");
        AddJson(command, "$snapshot", metadata, "initial_snapshot_id");
        AddJson(command, "$version", metadata, "export_format_version");
        AddJson(command, "$exported", metadata, "last_exported_at_utc");
        AddJson(command, "$changed", metadata, "last_data_changed_at_utc");
        AddJson(command, "$created", metadata, "created_at_utc");
        AddJson(command, "$updated", metadata, "updated_at_utc");
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ValidateCandidateAsync(SqliteConnection connection, SqliteTransaction transaction,
        int expectedFormatVersion,
        CancellationToken cancellationToken)
    {
        await using (var foreignKeys = connection.CreateCommand())
        {
            foreignKeys.Transaction = transaction;
            foreignKeys.CommandText = "PRAGMA foreign_key_check;";
            await using var reader = await foreignKeys.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                throw new InvalidDataException("The imported data has a broken reference.");
        }
        await using (var integrity = connection.CreateCommand())
        {
            integrity.Transaction = transaction;
            integrity.CommandText = "PRAGMA integrity_check;";
            if (!StringComparer.OrdinalIgnoreCase.Equals(
                    await integrity.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string, "ok"))
                throw new InvalidDataException("The imported database failed its integrity check.");
        }
        await ValidateAllIdsAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        await SqliteDatabase.ValidateBundledHolidayCalendarCollisionAsync(connection, transaction, cancellationToken)
            .ConfigureAwait(false);

        string initialSnapshotId;
        await using (var metadata = connection.CreateCommand())
        {
            metadata.Transaction = transaction;
            metadata.CommandText = "SELECT initial_snapshot_id, export_format_version FROM app_metadata WHERE id = 1;";
            await using var reader = await metadata.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false) || reader.IsDBNull(0))
                throw new InvalidDataException("The initial setting snapshot is missing.");
            initialSnapshotId = reader.GetString(0);
            var metadataVersion = reader.GetInt32(1);
            if (metadataVersion != expectedFormatVersion || metadataVersion != FormatVersion)
                throw new InvalidDataException(
                    $"Metadata export format version {metadataVersion} does not match supported header version {FormatVersion}.");
        }

        await using (var versions = connection.CreateCommand())
        {
            versions.Transaction = transaction;
            versions.CommandText = "SELECT COUNT(*) FROM setting_snapshot WHERE schema_version <> $supported;";
            versions.Parameters.AddWithValue("$supported", SqliteDatabase.CurrentSettingSnapshotSchemaVersion);
            if (Convert.ToInt64(await versions.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
                    CultureInfo.InvariantCulture) != 0)
                throw new InvalidDataException(
                    $"A setting snapshot uses an unsupported schema version. Supported version: {SqliteDatabase.CurrentSettingSnapshotSchemaVersion}.");
        }

        // Constructing domain objects catches cross-row business invariants beyond SQLite checks.
        var ids = new List<string>();
        await using (var snapshots = connection.CreateCommand())
        {
            snapshots.Transaction = transaction;
            snapshots.CommandText = "SELECT id FROM setting_snapshot ORDER BY id;";
            await using var reader = await snapshots.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) ids.Add(reader.GetString(0));
        }
        var loadedSnapshots = new Dictionary<string, SettingSnapshot>(StringComparer.Ordinal);
        foreach (var id in ids)
            loadedSnapshots.Add(id,
                await SqliteSettingSnapshotRepository.LoadSnapshotAsync(connection, transaction, id, cancellationToken)
                    .ConfigureAwait(false) ?? throw new InvalidDataException("A setting snapshot could not be loaded."));
        if (!loadedSnapshots.TryGetValue(initialSnapshotId, out var initialSnapshot))
            throw new InvalidDataException("The initial setting snapshot is missing.");
        if (!HasApplicableRatesForEnabledServices(initialSnapshot))
            throw new InvalidDataException("The initial setting snapshot does not contain calculation-complete rates.");

        await using (var closingCount = connection.CreateCommand())
        {
            closingCount.Transaction = transaction;
            closingCount.CommandText = "SELECT COUNT(*) FROM closing_rule_history;";
            if (Convert.ToInt64(await closingCount.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
                    CultureInfo.InvariantCulture) < 1)
                throw new InvalidDataException("At least one closing rule is required before import.");
        }

        await using var records = connection.CreateCommand();
        records.Transaction = transaction;
        records.CommandText = """
            SELECT id, work_date, service_id, time_category_id, input_mode, work_minutes, start_time_minutes,
                   end_time_minutes, source_service_preset_id, source_basic_shift_id, source_work_record_id
            FROM work_record ORDER BY id;
            """;
        await using var recordReader = await records.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await recordReader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var dto = SqliteWorkRecordRepository.Read(recordReader);
            _ = new WorkRecord(dto.Id, dto.WorkDate, dto.ServiceId, dto.TimeCategoryId, dto.InputMode,
                dto.WorkMinutes, dto.StartTime, dto.EndTime);
        }
        await recordReader.DisposeAsync().ConfigureAwait(false);

        await using (var shifts = connection.CreateCommand())
        {
            shifts.Transaction = transaction;
            shifts.CommandText = """
                SELECT service_id, time_category_id, input_mode, work_minutes, start_time_minutes, end_time_minutes
                FROM basic_shift;
                """;
            await using var reader = await shifts.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                _ = new WorkRecord(new WorkRecordId(Guid.NewGuid()), new DateOnly(2000, 1, 1),
                    new ServiceId(SqliteValue.Guid(reader.GetString(0))),
                    reader.IsDBNull(1) ? null : new TimeCategoryId(SqliteValue.Guid(reader.GetString(1))),
                    Enum.Parse<WorkInputMode>(reader.GetString(2), false), new WorkMinutes(reader.GetInt32(3)),
                    reader.IsDBNull(4) ? null : new MinuteOfDay(reader.GetInt32(4)),
                    reader.IsDBNull(5) ? null : new MinuteOfDay(reader.GetInt32(5)));
        }

        await using (var allowances = connection.CreateCommand())
        {
            allowances.Transaction = transaction;
            allowances.CommandText = "SELECT id, payroll_period_year_month, display_name, amount_yen FROM monthly_allowance;";
            await using var reader = await allowances.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                _ = new MonthlyAllowance(new MonthlyAllowanceId(SqliteValue.Guid(reader.GetString(0))),
                    new PayrollPeriodKey(SqliteValue.YearMonth(reader.GetInt64(1))), reader.GetString(2),
                    new YenAmount(reader.GetInt64(3)));
        }

        await using (var closingRules = connection.CreateCommand())
        {
            closingRules.Transaction = transaction;
            closingRules.CommandText = "SELECT id, effective_from_year_month, closing_day FROM closing_rule_history;";
            await using var reader = await closingRules.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                _ = new ClosingRule(new ClosingRuleId(SqliteValue.Guid(reader.GetString(0))),
                    new PayrollPeriodKey(SqliteValue.YearMonth(reader.GetInt64(1))),
                    reader.IsDBNull(2) ? null : reader.GetInt32(2));
        }

        foreach (var query in new[]
                 {
                     "SELECT source_reference_date FROM holiday_calendar_version;",
                     "SELECT holiday_date FROM holiday_date;",
                     "SELECT target_date FROM snapshot_premium_date;",
                 })
        {
            await using var dates = connection.CreateCommand();
            dates.Transaction = transaction;
            dates.CommandText = query;
            await using var reader = await dates.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) _ = SqliteValue.Date(reader.GetString(0));
        }
    }

    private static bool HasApplicableRatesForEnabledServices(SettingSnapshot snapshot)
    {
        var enabledServices = snapshot.Services.Where(value => value.IsEnabled).ToArray();
        if (enabledServices.Length == 0) return false;
        foreach (var service in enabledServices)
        {
            if (snapshot.Rates.Any(rate => rate.ServiceId == service.Id && rate.TimeCategoryId is null)) continue;
            var enabledCategories = snapshot.TimeCategories
                .Where(category => category.IsEnabled && category.ServiceId == service.Id).ToArray();
            if (enabledCategories.Length == 0 || enabledCategories.Any(category =>
                    !snapshot.Rates.Any(rate => rate.ServiceId == service.Id && rate.TimeCategoryId == category.Id)))
                return false;
        }

        return true;
    }

    private static async Task ValidateAllIdsAsync(SqliteConnection connection, SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        foreach (var (table, column) in ImportedIdColumns)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"SELECT {column} FROM {table} WHERE {column} IS NOT NULL;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var text = reader.GetString(0);
                if (!Guid.TryParseExact(text, "D", out var parsed) || parsed == Guid.Empty ||
                    !StringComparer.Ordinal.Equals(parsed.ToString("D"), text))
                    throw new InvalidDataException($"Column {table}.{column} contains a non-canonical UUID.");
            }
        }
    }

    private static Task<ImportPreviewDto> BuildPreviewAsync(SqliteDatabase candidate, PreparedImportId id,
        int formatVersion, DateTimeOffset exportCreated, CancellationToken cancellationToken) =>
        candidate.ReadAsync(async (connection, transaction, token) =>
        {
            var settingCount = await ScalarLongAsync(connection, transaction,
                "SELECT COUNT(*) FROM setting_month;", token).ConfigureAwait(false);
            var shiftCount = await ScalarLongAsync(connection, transaction,
                "SELECT COUNT(*) FROM basic_shift;", token).ConfigureAwait(false);
            var workCount = await ScalarLongAsync(connection, transaction,
                "SELECT COUNT(*) FROM work_record;", token).ConfigureAwait(false);
            var allowanceCount = await ScalarLongAsync(connection, transaction,
                "SELECT COUNT(*) FROM monthly_allowance;", token).ConfigureAwait(false);
            var oldestMonth = await ScalarNullableLongAsync(connection, transaction,
                "SELECT MIN(year_month) FROM setting_month;", token).ConfigureAwait(false);
            var latestMonth = await ScalarNullableLongAsync(connection, transaction,
                "SELECT MAX(year_month) FROM setting_month;", token).ConfigureAwait(false);
            var oldestDate = await ScalarNullableStringAsync(connection, transaction,
                "SELECT MIN(work_date) FROM work_record;", token).ConfigureAwait(false);
            var latestDate = await ScalarNullableStringAsync(connection, transaction,
                "SELECT MAX(work_date) FROM work_record;", token).ConfigureAwait(false);
            return new ImportPreviewDto(id, formatVersion, exportCreated, settingCount, shiftCount, workCount,
                allowanceCount, oldestMonth is { } oldMonth ? SqliteValue.YearMonth(oldMonth) : null,
                latestMonth is { } newMonth ? SqliteValue.YearMonth(newMonth) : null,
                oldestDate is { } oldDate ? SqliteValue.Date(oldDate) : null,
                latestDate is { } newDate ? SqliteValue.Date(newDate) : null, []);
        }, cancellationToken);

    private static async Task CopyTableAsync(SqliteConnection source, SqliteConnection destination,
        SqliteTransaction transaction, TableSpec table, CancellationToken cancellationToken)
    {
        await using var select = source.CreateCommand();
        select.CommandText = $"SELECT {string.Join(", ", table.Columns)} FROM {table.Name};";
        await using var reader = await select.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            await using var insert = destination.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = $"INSERT INTO {table.Name}({string.Join(", ", table.Columns)}) VALUES({string.Join(", ", table.Columns.Select((_, i) => $"$p{i}"))});";
            for (var i = 0; i < table.Columns.Length; i++)
                insert.Parameters.AddWithValue($"$p{i}", reader.IsDBNull(i) ? DBNull.Value : reader.GetValue(i));
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task CopyImportedMetadataAsync(SqliteConnection source, SqliteConnection destination,
        SqliteTransaction transaction, DateTimeOffset importedAtUtc, CancellationToken cancellationToken)
    {
        await using var read = source.CreateCommand();
        read.CommandText = "SELECT initial_snapshot_id, export_format_version, created_at_utc FROM app_metadata WHERE id = 1;";
        await using var reader = await read.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            throw new InvalidDataException("Imported metadata is missing.");
        var initial = reader.GetString(0);
        var version = reader.GetInt32(1);
        var created = reader.GetString(2);
        await reader.DisposeAsync().ConfigureAwait(false);
        await using var update = destination.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = """
            UPDATE app_metadata SET initial_setup_status = 'Completed', initial_setup_step = NULL,
                initial_snapshot_id = $initial, export_format_version = $version,
                last_exported_at_utc = $now, last_data_changed_at_utc = $now,
                backup_reminder_deferred_until_date = NULL, created_at_utc = $created, updated_at_utc = $now,
                bundled_bootstrap_version = 0
            WHERE id = 1;
            """;
        update.Parameters.AddWithValue("$initial", initial);
        update.Parameters.AddWithValue("$version", version);
        update.Parameters.AddWithValue("$now", SqliteValue.Utc(importedAtUtc));
        update.Parameters.AddWithValue("$created", created);
        await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static JsonElement ToJsonElement(DataTransferRecord record)
    {
        var value = StreamingJsonExportStream.GetRecordValue(record);
        return value is JsonElement element ? element.Clone() : JsonSerializer.SerializeToElement(value, value?.GetType() ?? typeof(object));
    }

    private static void AddJson(SqliteCommand command, string parameterName, JsonElement element, string propertyName)
    {
        object value = DBNull.Value;
        if (element.TryGetProperty(propertyName, out var property) && property.ValueKind != JsonValueKind.Null)
            value = property.ValueKind == JsonValueKind.Number ? property.GetInt64() : property.GetString()!;
        command.Parameters.AddWithValue(parameterName, value);
    }

    private static string RequiredString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(property.GetString())
            ? property.GetString()!
            : throw new InvalidDataException($"Required property '{propertyName}' is missing.");

    private static int RequiredInt32(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.TryGetInt32(out var value)
            ? value
            : throw new InvalidDataException($"Required property '{propertyName}' is missing.");

    private static async Task ExecuteAsync(SqliteConnection connection, SqliteTransaction? transaction, string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<long> ScalarLongAsync(SqliteConnection connection, SqliteTransaction? transaction,
        string sql, CancellationToken cancellationToken) => Convert.ToInt64(
        await ScalarAsync(connection, transaction, sql, cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);

    private static async Task<long?> ScalarNullableLongAsync(SqliteConnection connection, SqliteTransaction? transaction,
        string sql, CancellationToken cancellationToken)
    {
        var value = await ScalarAsync(connection, transaction, sql, cancellationToken).ConfigureAwait(false);
        return value is null or DBNull ? null : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    private static async Task<string?> ScalarNullableStringAsync(SqliteConnection connection,
        SqliteTransaction? transaction, string sql, CancellationToken cancellationToken)
    {
        var value = await ScalarAsync(connection, transaction, sql, cancellationToken).ConfigureAwait(false);
        return value is null or DBNull ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    private static async Task<object?> ScalarAsync(SqliteConnection connection, SqliteTransaction? transaction,
        string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void DeleteDatabaseFiles(string databasePath)
    {
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { databasePath, databasePath + "-wal", databasePath + "-shm" })
            if (File.Exists(path)) File.Delete(path);
    }

    private static readonly TableSpec[] InsertOrder =
    [
        new("holiday_calendar_version", "id", "version_name", "source_name", "source_reference_date", "created_at_utc"),
        new("holiday_date", "holiday_calendar_version_id", "holiday_date", "display_name"),
        new("service_definition", "id", "created_at_utc"),
        new("time_category_definition", "id", "created_at_utc"),
        new("premium_definition", "id", "created_at_utc"),
        new("count_bonus_definition", "id", "created_at_utc"),
        new("setting_snapshot", "id", "based_on_id", "holiday_calendar_version_id", "schema_version", "created_at_utc"),
        new("snapshot_service", "snapshot_id", "service_id", "display_name", "display_order", "is_enabled"),
        new("snapshot_time_category", "snapshot_id", "time_category_id", "service_id", "display_name", "standard_minutes", "display_order", "is_enabled"),
        new("snapshot_rate", "snapshot_id", "service_id", "time_category_id", "rate_type", "amount_yen"),
        new("snapshot_premium", "snapshot_id", "premium_id", "display_name", "calculation_type", "percentage_basis_points", "amount_yen", "start_time_minutes", "end_time_minutes", "uses_national_holidays", "is_enabled"),
        new("snapshot_premium_weekday", "snapshot_id", "premium_id", "weekday"),
        new("snapshot_premium_date", "snapshot_id", "premium_id", "target_date"),
        new("snapshot_premium_service", "snapshot_id", "premium_id", "service_id"),
        new("snapshot_count_bonus", "snapshot_id", "count_bonus_id", "display_name", "amount_yen", "is_enabled"),
        new("snapshot_count_bonus_service", "snapshot_id", "count_bonus_id", "service_id"),
        new("service_preset", "id", "display_name", "service_id", "time_category_id", "default_work_minutes", "display_order", "is_enabled", "created_at_utc", "updated_at_utc"),
        new("basic_shift", "id", "weekday", "service_preset_id", "service_id", "time_category_id", "input_mode", "work_minutes", "start_time_minutes", "end_time_minutes", "display_order", "is_enabled", "created_at_utc", "updated_at_utc"),
        new("work_record", "id", "work_date", "service_id", "time_category_id", "input_mode", "work_minutes", "start_time_minutes", "end_time_minutes", "source_service_preset_id", "source_basic_shift_id", "source_work_record_id", "save_operation_id", "created_at_utc", "updated_at_utc"),
        new("closing_rule_history", "id", "effective_from_year_month", "closing_day", "is_end_of_month", "created_at_utc"),
        new("monthly_allowance", "id", "payroll_period_year_month", "display_name", "amount_yen", "created_at_utc", "updated_at_utc"),
        new("setting_month", "year_month", "snapshot_id", "created_at_utc", "updated_at_utc"),
    ];

    private static readonly string[] MetadataColumns =
    [
        "id", "initial_setup_status", "initial_setup_step", "initial_snapshot_id", "export_format_version",
        "last_exported_at_utc", "last_data_changed_at_utc", "backup_reminder_deferred_until_date",
        "created_at_utc", "updated_at_utc", "bundled_bootstrap_version",
    ];

    private static readonly string[] DeleteOrder =
    [
        "work_record", "basic_shift", "service_preset", "monthly_allowance", "closing_rule_history",
        "setting_month", "snapshot_premium_weekday", "snapshot_premium_date", "snapshot_premium_service",
        "snapshot_count_bonus_service", "snapshot_rate", "snapshot_time_category", "snapshot_service",
        "snapshot_premium", "snapshot_count_bonus", "setting_snapshot", "holiday_date",
        "holiday_calendar_version", "time_category_definition", "service_definition", "premium_definition",
        "count_bonus_definition",
    ];

    // DATA-004: validate every logical/origin identifier, including nullable identifiers which
    // intentionally have no foreign key and holiday versions not referenced by a snapshot.
    private static readonly (string Table, string Column)[] ImportedIdColumns =
    [
        ("app_metadata", "initial_snapshot_id"),
        ("holiday_calendar_version", "id"),
        ("holiday_date", "holiday_calendar_version_id"),
        ("service_definition", "id"),
        ("time_category_definition", "id"),
        ("premium_definition", "id"),
        ("count_bonus_definition", "id"),
        ("setting_snapshot", "id"),
        ("setting_snapshot", "based_on_id"),
        ("setting_snapshot", "holiday_calendar_version_id"),
        ("setting_month", "snapshot_id"),
        ("snapshot_service", "snapshot_id"),
        ("snapshot_service", "service_id"),
        ("snapshot_time_category", "snapshot_id"),
        ("snapshot_time_category", "time_category_id"),
        ("snapshot_time_category", "service_id"),
        ("snapshot_rate", "snapshot_id"),
        ("snapshot_rate", "service_id"),
        ("snapshot_rate", "time_category_id"),
        ("snapshot_premium", "snapshot_id"),
        ("snapshot_premium", "premium_id"),
        ("snapshot_premium_weekday", "snapshot_id"),
        ("snapshot_premium_weekday", "premium_id"),
        ("snapshot_premium_date", "snapshot_id"),
        ("snapshot_premium_date", "premium_id"),
        ("snapshot_premium_service", "snapshot_id"),
        ("snapshot_premium_service", "premium_id"),
        ("snapshot_premium_service", "service_id"),
        ("snapshot_count_bonus", "snapshot_id"),
        ("snapshot_count_bonus", "count_bonus_id"),
        ("snapshot_count_bonus_service", "snapshot_id"),
        ("snapshot_count_bonus_service", "count_bonus_id"),
        ("snapshot_count_bonus_service", "service_id"),
        ("service_preset", "id"),
        ("service_preset", "service_id"),
        ("service_preset", "time_category_id"),
        ("basic_shift", "id"),
        ("basic_shift", "service_preset_id"),
        ("basic_shift", "service_id"),
        ("basic_shift", "time_category_id"),
        ("work_record", "id"),
        ("work_record", "service_id"),
        ("work_record", "time_category_id"),
        ("work_record", "source_service_preset_id"),
        ("work_record", "source_basic_shift_id"),
        ("work_record", "source_work_record_id"),
        ("work_record", "save_operation_id"),
        ("closing_rule_history", "id"),
        ("monthly_allowance", "id"),
    ];

    private static readonly IReadOnlyDictionary<string, DataTransferSection> TypeToSection =
        new Dictionary<string, DataTransferSection>(StringComparer.Ordinal)
        {
            ["document_header"] = DataTransferSection.Metadata,
            ["app_metadata"] = DataTransferSection.Metadata,
            ["setting_month"] = DataTransferSection.SettingMonths,
            ["setting_snapshot"] = DataTransferSection.SettingSnapshots,
            ["snapshot_service"] = DataTransferSection.SettingSnapshots,
            ["snapshot_time_category"] = DataTransferSection.SettingSnapshots,
            ["snapshot_rate"] = DataTransferSection.SettingSnapshots,
            ["snapshot_premium"] = DataTransferSection.SettingSnapshots,
            ["snapshot_premium_weekday"] = DataTransferSection.SettingSnapshots,
            ["snapshot_premium_date"] = DataTransferSection.SettingSnapshots,
            ["snapshot_premium_service"] = DataTransferSection.SettingSnapshots,
            ["snapshot_count_bonus"] = DataTransferSection.SettingSnapshots,
            ["snapshot_count_bonus_service"] = DataTransferSection.SettingSnapshots,
            ["closing_rule_history"] = DataTransferSection.ClosingRules,
            ["monthly_allowance"] = DataTransferSection.MonthlyAllowances,
            ["service_definition"] = DataTransferSection.Definitions,
            ["time_category_definition"] = DataTransferSection.Definitions,
            ["premium_definition"] = DataTransferSection.Definitions,
            ["count_bonus_definition"] = DataTransferSection.Definitions,
            ["service_preset"] = DataTransferSection.ServicePresets,
            ["basic_shift"] = DataTransferSection.BasicShifts,
            ["work_record"] = DataTransferSection.WorkRecords,
            ["holiday_calendar_version"] = DataTransferSection.Holidays,
            ["holiday_date"] = DataTransferSection.Holidays,
        };

    private sealed record TableSpec(string Name, params string[] Columns);

    private enum PreparedImportState
    {
        Preparing,
        Validated,
        Consuming,
        Consumed,
    }
}
