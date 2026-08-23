using System.Data;
using System.Globalization;
using Microsoft.Data.Sqlite;
using TkpSalaryCalculator.Application.Ports;
using TkpSalaryCalculator.Domain.ValueObjects;

namespace TkpSalaryCalculator.Infrastructure.Sqlite;

/// <summary>SQLite Infrastructure のファイル配置を指定します。</summary>
public sealed record SqliteInfrastructureOptions(string DatabasePath, string ImportStagingDirectory)
{
    public string DatabasePath { get; } = Path.GetFullPath(
        string.IsNullOrWhiteSpace(DatabasePath)
            ? throw new ArgumentException("Database path is required.", nameof(DatabasePath))
            : DatabasePath);

    public string ImportStagingDirectory { get; } = Path.GetFullPath(
        string.IsNullOrWhiteSpace(ImportStagingDirectory)
            ? throw new ArgumentException("Import staging directory is required.", nameof(ImportStagingDirectory))
            : ImportStagingDirectory);
}

/// <summary>システムの UTC 時計です。</summary>
public sealed class SystemUtcClock : IUtcClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

/// <summary>指定された端末タイムゾーンで UTC 日時をローカル日付へ変換します。</summary>
public sealed class TimeZoneLocalDateConverter(TimeZoneInfo timeZone) : ILocalDateConverter
{
    private readonly TimeZoneInfo timeZone = timeZone ?? throw new ArgumentNullException(nameof(timeZone));

    public DateOnly ToLocalDate(DateTimeOffset utcDateTime)
    {
        var local = TimeZoneInfo.ConvertTime(utcDateTime.ToUniversalTime(), timeZone);
        return DateOnly.FromDateTime(local.DateTime);
    }
}

/// <summary>接続設定、スキーマ更新および Ambient トランザクションを所有します。</summary>
public sealed class SqliteDatabase
{
    public const int CurrentSchemaVersion = 2;
    public const int CurrentSettingSnapshotSchemaVersion = 1;
    public const int CurrentExportFormatVersion = 1;

    private readonly string connectionString;
    private readonly bool bootstrapDefaults;
    private readonly SemaphoreSlim initializationGate = new(1, 1);
    private readonly AsyncLocal<TransactionContext?> ambientTransaction = new();
    private bool initialized;

    public SqliteDatabase(string databasePath, bool bootstrapDefaults = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        DatabasePath = Path.GetFullPath(databasePath);
        this.bootstrapDefaults = bootstrapDefaults;
        connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true,
            DefaultTimeout = 5,
        }.ToString();
    }

    public string DatabasePath { get; }

    /// <summary>未作成 DB を作成し、古い版には順次マイグレーションを適用します。</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (initialized) return;

        await initializationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (initialized) return;
            var directory = Path.GetDirectoryName(DatabasePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            await using var connection = await OpenUninitializedConnectionAsync(cancellationToken).ConfigureAwait(false);
            var version = await ExecuteScalarIntAsync(connection, "PRAGMA user_version;", cancellationToken).ConfigureAwait(false);
            if (version > CurrentSchemaVersion)
            {
                throw new InvalidOperationException(
                    $"Database schema version {version} is newer than supported version {CurrentSchemaVersion}.");
            }

            var databaseHadSchema = version > 0;
            while (version < CurrentSchemaVersion)
            {
                version = await ApplyMigrationAsync(connection, version, bootstrapDefaults, cancellationToken)
                    .ConfigureAwait(false);
            }

            // Backfill existing databases created by an implementation which did not seed defaults.
            // Fresh databases are bootstrapped in the version-one migration transaction itself.
            if (bootstrapDefaults && databaseHadSchema)
                await EnsureBootstrapAsync(connection, cancellationToken).ConfigureAwait(false);

            initialized = true;
        }
        finally
        {
            initializationGate.Release();
        }
    }

    internal TransactionContext? AmbientTransaction => ambientTransaction.Value;

    internal async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        return await OpenUninitializedConnectionAsync(cancellationToken).ConfigureAwait(false);
    }

    internal async Task<T> ReadAsync<T>(
        Func<SqliteConnection, SqliteTransaction?, CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        cancellationToken.ThrowIfCancellationRequested();
        var ambient = ambientTransaction.Value;
        if (ambient is not null)
        {
            return await operation(ambient.Connection, ambient.Transaction, cancellationToken).ConfigureAwait(false);
        }

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await operation(connection, null, cancellationToken).ConfigureAwait(false);
    }

    internal async Task<T> WriteAsync<T>(
        Func<SqliteConnection, SqliteTransaction, CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var ambient = ambientTransaction.Value;
        if (ambient is not null)
        {
            return await operation(ambient.Connection, ambient.Transaction, cancellationToken).ConfigureAwait(false);
        }

        T? result = default;
        await RunTransactionAsync(async token =>
        {
            var context = ambientTransaction.Value!;
            result = await operation(context.Connection, context.Transaction, token).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
        return result!;
    }

    internal async Task RunTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        cancellationToken.ThrowIfCancellationRequested();
        if (ambientTransaction.Value is not null)
        {
            await operation(cancellationToken).ConfigureAwait(false);
            return;
        }

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = connection.BeginTransaction(deferred: false);
        var context = new TransactionContext(connection, transaction);
        ambientTransaction.Value = context;
        try
        {
            await operation(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            context.NotifyCommitted();
        }
        catch
        {
            try
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // Preserve the original failure. Closing the connection also rolls back an open transaction.
            }

            context.NotifyRolledBack();

            throw;
        }
        finally
        {
            ambientTransaction.Value = null;
        }
    }

    internal async Task<T> RunTransactionAsync<T>(Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        T? result = default;
        await RunTransactionAsync(async token =>
        {
            result = await operation(token).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
        return result!;
    }

    private async Task<SqliteConnection> OpenUninitializedConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await ExecuteNonQueryAsync(connection, null, "PRAGMA foreign_keys = ON;", cancellationToken)
                .ConfigureAwait(false);
            await ExecuteNonQueryAsync(connection, null, "PRAGMA journal_mode = WAL;", cancellationToken)
                .ConfigureAwait(false);
            await ExecuteNonQueryAsync(connection, null, "PRAGMA synchronous = FULL;", cancellationToken)
                .ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<int> ApplyMigrationAsync(SqliteConnection connection, int fromVersion,
        bool bootstrapDefaults,
        CancellationToken cancellationToken)
    {
        return fromVersion switch
        {
            0 => await MigrateFromZeroToOneAsync(connection, bootstrapDefaults, cancellationToken)
                .ConfigureAwait(false),
            1 => await MigrateFromOneToTwoAsync(connection, cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidOperationException($"No migration from schema version {fromVersion} is available."),
        };
    }

    private static async Task<int> MigrateFromOneToTwoAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var transaction = connection.BeginTransaction(deferred: false);
        try
        {
            await ExecuteNonQueryAsync(connection, transaction, """
                CREATE INDEX IF NOT EXISTS ix_work_record_source_preset
                    ON work_record(source_service_preset_id)
                    WHERE source_service_preset_id IS NOT NULL;
                PRAGMA user_version = 2;
                """, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return 2;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<int> MigrateFromZeroToOneAsync(SqliteConnection connection, bool bootstrapDefaults,
        CancellationToken cancellationToken)
    {
        await using var transaction = connection.BeginTransaction(deferred: false);
        try
        {
            await ExecuteNonQueryAsync(connection, transaction, SchemaVersionOneSql, cancellationToken)
                .ConfigureAwait(false);
            var now = SqliteValue.Utc(DateTimeOffset.UtcNow);
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT OR IGNORE INTO app_metadata(
                        id, initial_setup_status, initial_setup_step, initial_snapshot_id,
                        export_format_version, last_exported_at_utc, last_data_changed_at_utc,
                        backup_reminder_deferred_until_date, created_at_utc, updated_at_utc)
                    VALUES (1, 'NotStarted', NULL, NULL, $format, NULL, NULL, NULL, $now, $now);
                    """;
                command.Parameters.AddWithValue("$format", CurrentExportFormatVersion);
                command.Parameters.AddWithValue("$now", now);
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            if (bootstrapDefaults)
                await BootstrapVersionOneAsync(connection, transaction, now, cancellationToken).ConfigureAwait(false);

            await ExecuteNonQueryAsync(connection, transaction, "PRAGMA user_version = 1;", cancellationToken)
                .ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return 1;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private static async Task EnsureBootstrapAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var transaction = connection.BeginTransaction(deferred: false);
        try
        {
            await BootstrapVersionOneAsync(connection, transaction, SqliteValue.Utc(DateTimeOffset.UtcNow),
                cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    internal static async Task BootstrapVersionOneAsync(SqliteConnection connection, SqliteTransaction transaction,
        string now, CancellationToken cancellationToken)
    {
        await ValidateBundledHolidayCalendarCollisionAsync(connection, transaction, cancellationToken)
            .ConfigureAwait(false);

        // Bundled holiday calendars are immutable versions and may be added independently of user setup state.
        await using (var calendar = connection.CreateCommand())
        {
            calendar.Transaction = transaction;
            calendar.CommandText = """
                INSERT OR IGNORE INTO holiday_calendar_version(
                    id, version_name, source_name, source_reference_date, created_at_utc)
                VALUES($id, $version, $source, $reference, $now);
                """;
            calendar.Parameters.AddWithValue("$id", BundledBootstrapData.HolidayCalendarId);
            calendar.Parameters.AddWithValue("$version", BundledBootstrapData.HolidayVersionName);
            calendar.Parameters.AddWithValue("$source", BundledBootstrapData.HolidaySourceName);
            calendar.Parameters.AddWithValue("$reference", BundledBootstrapData.HolidaySourceReferenceDate);
            calendar.Parameters.AddWithValue("$now", now);
            await calendar.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var holiday in BundledBootstrapData.Holidays)
        {
            await using var date = connection.CreateCommand();
            date.Transaction = transaction;
            date.CommandText = """
                INSERT OR IGNORE INTO holiday_date(holiday_calendar_version_id, holiday_date, display_name)
                VALUES($version, $date, $name);
                """;
            date.Parameters.AddWithValue("$version", BundledBootstrapData.HolidayCalendarId);
            date.Parameters.AddWithValue("$date", holiday.Date);
            date.Parameters.AddWithValue("$name", holiday.Name);
            await date.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var metadata = connection.CreateCommand())
        {
            metadata.Transaction = transaction;
            metadata.CommandText = "SELECT initial_snapshot_id FROM app_metadata WHERE id = 1;";
            if (await metadata.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is string)
                return;
        }

        await using (var defaults = connection.CreateCommand())
        {
            defaults.Transaction = transaction;
            defaults.CommandText = """
                INSERT OR IGNORE INTO service_definition(id, created_at_utc) VALUES($physical, $now);
                INSERT OR IGNORE INTO service_definition(id, created_at_utc) VALUES($living, $now);
                INSERT OR IGNORE INTO time_category_definition(id, created_at_utc) VALUES($physical0, $now);
                INSERT OR IGNORE INTO time_category_definition(id, created_at_utc) VALUES($physical1, $now);
                INSERT OR IGNORE INTO time_category_definition(id, created_at_utc) VALUES($physical2, $now);
                INSERT OR IGNORE INTO time_category_definition(id, created_at_utc) VALUES($living2, $now);
                INSERT OR IGNORE INTO time_category_definition(id, created_at_utc) VALUES($living3, $now);
                INSERT OR IGNORE INTO premium_definition(id, created_at_utc) VALUES($holidayPremium, $now);

                INSERT OR IGNORE INTO setting_snapshot(
                    id, based_on_id, holiday_calendar_version_id, schema_version, created_at_utc)
                VALUES($snapshot, NULL, $holiday, 1, $now);

                INSERT OR IGNORE INTO snapshot_service(
                    snapshot_id, service_id, display_name, display_order, is_enabled)
                VALUES($snapshot, $physical, '身体介護', 0, 1);
                INSERT OR IGNORE INTO snapshot_service(
                    snapshot_id, service_id, display_name, display_order, is_enabled)
                VALUES($snapshot, $living, '生活援助', 1, 1);

                INSERT OR IGNORE INTO snapshot_time_category(
                    snapshot_id, time_category_id, service_id, display_name, standard_minutes, display_order, is_enabled)
                VALUES($snapshot, $physical0, $physical, '身体0', 20, 0, 1);
                INSERT OR IGNORE INTO snapshot_time_category(
                    snapshot_id, time_category_id, service_id, display_name, standard_minutes, display_order, is_enabled)
                VALUES($snapshot, $physical1, $physical, '身体1', 30, 1, 1);
                INSERT OR IGNORE INTO snapshot_time_category(
                    snapshot_id, time_category_id, service_id, display_name, standard_minutes, display_order, is_enabled)
                VALUES($snapshot, $physical2, $physical, '身体2', 60, 2, 1);
                INSERT OR IGNORE INTO snapshot_time_category(
                    snapshot_id, time_category_id, service_id, display_name, standard_minutes, display_order, is_enabled)
                VALUES($snapshot, $living2, $living, '生活2', 45, 0, 1);
                INSERT OR IGNORE INTO snapshot_time_category(
                    snapshot_id, time_category_id, service_id, display_name, standard_minutes, display_order, is_enabled)
                VALUES($snapshot, $living3, $living, '生活3', 60, 1, 1);

                INSERT OR IGNORE INTO snapshot_premium(
                    snapshot_id, premium_id, display_name, calculation_type, percentage_basis_points,
                    amount_yen, start_time_minutes, end_time_minutes, uses_national_holidays, is_enabled)
                VALUES($snapshot, $holidayPremium, '休日', 'FixedPerHour', NULL, 0, NULL, NULL, 1, 0);
                INSERT OR IGNORE INTO snapshot_premium_weekday(snapshot_id, premium_id, weekday)
                VALUES($snapshot, $holidayPremium, 6);
                INSERT OR IGNORE INTO snapshot_premium_weekday(snapshot_id, premium_id, weekday)
                VALUES($snapshot, $holidayPremium, 7);
                """;
            defaults.Parameters.AddWithValue("$physical", BundledBootstrapData.PhysicalCareServiceId);
            defaults.Parameters.AddWithValue("$living", BundledBootstrapData.LivingSupportServiceId);
            defaults.Parameters.AddWithValue("$physical0", BundledBootstrapData.PhysicalZeroCategoryId);
            defaults.Parameters.AddWithValue("$physical1", BundledBootstrapData.PhysicalOneCategoryId);
            defaults.Parameters.AddWithValue("$physical2", BundledBootstrapData.PhysicalTwoCategoryId);
            defaults.Parameters.AddWithValue("$living2", BundledBootstrapData.LivingTwoCategoryId);
            defaults.Parameters.AddWithValue("$living3", BundledBootstrapData.LivingThreeCategoryId);
            defaults.Parameters.AddWithValue("$holidayPremium", BundledBootstrapData.HolidayPremiumId);
            defaults.Parameters.AddWithValue("$snapshot", BundledBootstrapData.InitialSnapshotId);
            defaults.Parameters.AddWithValue("$holiday", BundledBootstrapData.HolidayCalendarId);
            defaults.Parameters.AddWithValue("$now", now);
            await defaults.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var preset in BundledBootstrapData.Presets)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT OR IGNORE INTO service_preset(
                    id, display_name, service_id, time_category_id, default_work_minutes,
                    display_order, is_enabled, created_at_utc, updated_at_utc)
                VALUES($id, $name, $service, $category, $minutes, $order, 1, $now, $now);
                """;
            command.Parameters.AddWithValue("$id", preset.Id);
            command.Parameters.AddWithValue("$name", preset.Name);
            command.Parameters.AddWithValue("$service", preset.ServiceId);
            command.Parameters.AddWithValue("$category", preset.CategoryId);
            command.Parameters.AddWithValue("$minutes", preset.Minutes);
            command.Parameters.AddWithValue("$order", preset.Order);
            command.Parameters.AddWithValue("$now", now);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var updateMetadata = connection.CreateCommand();
        updateMetadata.Transaction = transaction;
        updateMetadata.CommandText = """
            UPDATE app_metadata SET initial_snapshot_id = $snapshot, updated_at_utc = $now
            WHERE id = 1 AND initial_snapshot_id IS NULL;
            """;
        updateMetadata.Parameters.AddWithValue("$snapshot", BundledBootstrapData.InitialSnapshotId);
        updateMetadata.Parameters.AddWithValue("$now", now);
        await updateMetadata.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    internal static async Task ValidateBundledHolidayCalendarCollisionAsync(SqliteConnection connection,
        SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        await using (var version = connection.CreateCommand())
        {
            version.Transaction = transaction;
            version.CommandText = """
                SELECT version_name, source_name, source_reference_date
                FROM holiday_calendar_version WHERE id = $id;
                """;
            version.Parameters.AddWithValue("$id", BundledBootstrapData.HolidayCalendarId);
            await using var reader = await version.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return;
            if (!StringComparer.Ordinal.Equals(reader.GetString(0), BundledBootstrapData.HolidayVersionName) ||
                !StringComparer.Ordinal.Equals(reader.GetString(1), BundledBootstrapData.HolidaySourceName) ||
                !StringComparer.Ordinal.Equals(reader.GetString(2), BundledBootstrapData.HolidaySourceReferenceDate))
                throw new InvalidDataException("The reserved bundled holiday calendar ID has conflicting metadata.");
        }

        var index = 0;
        await using var dates = connection.CreateCommand();
        dates.Transaction = transaction;
        dates.CommandText = """
            SELECT holiday_date, display_name FROM holiday_date
            WHERE holiday_calendar_version_id = $id ORDER BY holiday_date;
            """;
        dates.Parameters.AddWithValue("$id", BundledBootstrapData.HolidayCalendarId);
        await using var dateReader = await dates.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await dateReader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (index >= BundledBootstrapData.Holidays.Length ||
                !StringComparer.Ordinal.Equals(dateReader.GetString(0), BundledBootstrapData.Holidays[index].Date) ||
                !StringComparer.Ordinal.Equals(dateReader.GetString(1), BundledBootstrapData.Holidays[index].Name))
                throw new InvalidDataException("The reserved bundled holiday calendar ID has conflicting dates.");
            index++;
        }

        if (index != BundledBootstrapData.Holidays.Length)
            throw new InvalidDataException("The reserved bundled holiday calendar ID has an incomplete date set.");
    }

    private static async Task<int> ExecuteScalarIntAsync(SqliteConnection connection, string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    internal static async Task<int> ExecuteNonQueryAsync(SqliteConnection connection, SqliteTransaction? transaction,
        string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    internal sealed class TransactionContext(SqliteConnection connection, SqliteTransaction transaction)
    {
        private readonly List<Action> committedCallbacks = [];
        private readonly List<Action> rolledBackCallbacks = [];

        internal SqliteConnection Connection { get; } = connection;
        internal SqliteTransaction Transaction { get; } = transaction;

        internal void RegisterCompletion(Action committed, Action rolledBack)
        {
            ArgumentNullException.ThrowIfNull(committed);
            ArgumentNullException.ThrowIfNull(rolledBack);
            committedCallbacks.Add(committed);
            rolledBackCallbacks.Add(rolledBack);
        }

        internal void NotifyCommitted()
        {
            foreach (var callback in committedCallbacks) callback();
        }

        internal void NotifyRolledBack()
        {
            foreach (var callback in rolledBackCallbacks) callback();
        }
    }

    // DB-001/DB-002/DB-003/DB-004/DB-005: schema, checks, foreign keys and indexes.
    private const string SchemaVersionOneSql = """
        CREATE TABLE IF NOT EXISTS holiday_calendar_version (
            id TEXT PRIMARY KEY CHECK(length(id) = 36 AND id = lower(id)),
            version_name TEXT NOT NULL UNIQUE CHECK(length(trim(version_name)) > 0),
            source_name TEXT NOT NULL CHECK(length(trim(source_name)) > 0),
            source_reference_date TEXT NOT NULL CHECK(length(source_reference_date) = 10),
            created_at_utc TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS holiday_date (
            holiday_calendar_version_id TEXT NOT NULL,
            holiday_date TEXT NOT NULL CHECK(length(holiday_date) = 10),
            display_name TEXT NOT NULL CHECK(length(trim(display_name)) > 0),
            PRIMARY KEY (holiday_calendar_version_id, holiday_date),
            FOREIGN KEY (holiday_calendar_version_id) REFERENCES holiday_calendar_version(id) ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS setting_snapshot (
            id TEXT PRIMARY KEY CHECK(length(id) = 36 AND id = lower(id)),
            based_on_id TEXT NULL,
            holiday_calendar_version_id TEXT NOT NULL,
            schema_version INTEGER NOT NULL CHECK(schema_version >= 1),
            created_at_utc TEXT NOT NULL,
            FOREIGN KEY (based_on_id) REFERENCES setting_snapshot(id) ON DELETE SET NULL,
            FOREIGN KEY (holiday_calendar_version_id) REFERENCES holiday_calendar_version(id) ON DELETE RESTRICT
        );

        CREATE TABLE IF NOT EXISTS service_definition (
            id TEXT PRIMARY KEY CHECK(length(id) = 36 AND id = lower(id)),
            created_at_utc TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS time_category_definition (
            id TEXT PRIMARY KEY CHECK(length(id) = 36 AND id = lower(id)),
            created_at_utc TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS premium_definition (
            id TEXT PRIMARY KEY CHECK(length(id) = 36 AND id = lower(id)),
            created_at_utc TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS count_bonus_definition (
            id TEXT PRIMARY KEY CHECK(length(id) = 36 AND id = lower(id)),
            created_at_utc TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS setting_month (
            year_month INTEGER PRIMARY KEY CHECK(
                year_month BETWEEN 100001 AND 999912 AND year_month % 100 BETWEEN 1 AND 12),
            snapshot_id TEXT NOT NULL,
            created_at_utc TEXT NOT NULL,
            updated_at_utc TEXT NOT NULL,
            FOREIGN KEY (snapshot_id) REFERENCES setting_snapshot(id) ON DELETE RESTRICT
        );

        CREATE TABLE IF NOT EXISTS snapshot_service (
            snapshot_id TEXT NOT NULL,
            service_id TEXT NOT NULL,
            display_name TEXT NOT NULL CHECK(length(trim(display_name)) > 0),
            display_order INTEGER NOT NULL CHECK(display_order >= 0),
            is_enabled INTEGER NOT NULL CHECK(is_enabled IN (0, 1)),
            PRIMARY KEY (snapshot_id, service_id),
            FOREIGN KEY (snapshot_id) REFERENCES setting_snapshot(id) ON DELETE CASCADE,
            FOREIGN KEY (service_id) REFERENCES service_definition(id) ON DELETE RESTRICT
        );

        CREATE TABLE IF NOT EXISTS snapshot_time_category (
            snapshot_id TEXT NOT NULL,
            time_category_id TEXT NOT NULL,
            service_id TEXT NOT NULL,
            display_name TEXT NOT NULL CHECK(length(trim(display_name)) > 0),
            standard_minutes INTEGER NOT NULL CHECK(standard_minutes BETWEEN 1 AND 1440),
            display_order INTEGER NOT NULL CHECK(display_order >= 0),
            is_enabled INTEGER NOT NULL CHECK(is_enabled IN (0, 1)),
            PRIMARY KEY (snapshot_id, time_category_id),
            UNIQUE (snapshot_id, time_category_id, service_id),
            FOREIGN KEY (snapshot_id) REFERENCES setting_snapshot(id) ON DELETE CASCADE,
            FOREIGN KEY (time_category_id) REFERENCES time_category_definition(id) ON DELETE RESTRICT,
            FOREIGN KEY (snapshot_id, service_id) REFERENCES snapshot_service(snapshot_id, service_id) ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS snapshot_rate (
            snapshot_id TEXT NOT NULL,
            service_id TEXT NOT NULL,
            time_category_id TEXT NULL,
            rate_type TEXT NOT NULL CHECK(rate_type IN ('Hourly', 'FixedPerRecord')),
            amount_yen INTEGER NOT NULL CHECK(amount_yen >= 0),
            FOREIGN KEY (snapshot_id, service_id) REFERENCES snapshot_service(snapshot_id, service_id) ON DELETE CASCADE,
            FOREIGN KEY (snapshot_id, time_category_id, service_id)
                REFERENCES snapshot_time_category(snapshot_id, time_category_id, service_id) ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS snapshot_premium (
            snapshot_id TEXT NOT NULL,
            premium_id TEXT NOT NULL,
            display_name TEXT NOT NULL CHECK(length(trim(display_name)) > 0),
            calculation_type TEXT NOT NULL CHECK(calculation_type IN ('Percentage', 'FixedPerHour', 'FixedPerRecord')),
            percentage_basis_points INTEGER NULL CHECK(percentage_basis_points >= 0),
            amount_yen INTEGER NULL CHECK(amount_yen >= 0),
            start_time_minutes INTEGER NULL CHECK(start_time_minutes BETWEEN 0 AND 1439),
            end_time_minutes INTEGER NULL CHECK(end_time_minutes BETWEEN 0 AND 1439),
            uses_national_holidays INTEGER NOT NULL CHECK(uses_national_holidays IN (0, 1)),
            is_enabled INTEGER NOT NULL CHECK(is_enabled IN (0, 1)),
            PRIMARY KEY (snapshot_id, premium_id),
            CHECK ((calculation_type = 'Percentage' AND percentage_basis_points IS NOT NULL AND amount_yen IS NULL)
                OR (calculation_type IN ('FixedPerHour', 'FixedPerRecord') AND percentage_basis_points IS NULL AND amount_yen IS NOT NULL)),
            CHECK ((start_time_minutes IS NULL AND end_time_minutes IS NULL)
                OR (start_time_minutes IS NOT NULL AND end_time_minutes IS NOT NULL AND start_time_minutes <> end_time_minutes)),
            FOREIGN KEY (snapshot_id) REFERENCES setting_snapshot(id) ON DELETE CASCADE,
            FOREIGN KEY (premium_id) REFERENCES premium_definition(id) ON DELETE RESTRICT
        );

        CREATE TABLE IF NOT EXISTS snapshot_premium_weekday (
            snapshot_id TEXT NOT NULL,
            premium_id TEXT NOT NULL,
            weekday INTEGER NOT NULL CHECK(weekday BETWEEN 1 AND 7),
            PRIMARY KEY (snapshot_id, premium_id, weekday),
            FOREIGN KEY (snapshot_id, premium_id) REFERENCES snapshot_premium(snapshot_id, premium_id) ON DELETE CASCADE
        );
        CREATE TABLE IF NOT EXISTS snapshot_premium_date (
            snapshot_id TEXT NOT NULL,
            premium_id TEXT NOT NULL,
            target_date TEXT NOT NULL CHECK(length(target_date) = 10),
            PRIMARY KEY (snapshot_id, premium_id, target_date),
            FOREIGN KEY (snapshot_id, premium_id) REFERENCES snapshot_premium(snapshot_id, premium_id) ON DELETE CASCADE
        );
        CREATE TABLE IF NOT EXISTS snapshot_premium_service (
            snapshot_id TEXT NOT NULL,
            premium_id TEXT NOT NULL,
            service_id TEXT NOT NULL,
            PRIMARY KEY (snapshot_id, premium_id, service_id),
            FOREIGN KEY (snapshot_id, premium_id) REFERENCES snapshot_premium(snapshot_id, premium_id) ON DELETE CASCADE,
            FOREIGN KEY (snapshot_id, service_id) REFERENCES snapshot_service(snapshot_id, service_id) ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS snapshot_count_bonus (
            snapshot_id TEXT NOT NULL,
            count_bonus_id TEXT NOT NULL,
            display_name TEXT NOT NULL CHECK(length(trim(display_name)) > 0),
            amount_yen INTEGER NOT NULL CHECK(amount_yen >= 0),
            is_enabled INTEGER NOT NULL CHECK(is_enabled IN (0, 1)),
            PRIMARY KEY (snapshot_id, count_bonus_id),
            FOREIGN KEY (snapshot_id) REFERENCES setting_snapshot(id) ON DELETE CASCADE,
            FOREIGN KEY (count_bonus_id) REFERENCES count_bonus_definition(id) ON DELETE RESTRICT
        );
        CREATE TABLE IF NOT EXISTS snapshot_count_bonus_service (
            snapshot_id TEXT NOT NULL,
            count_bonus_id TEXT NOT NULL,
            service_id TEXT NOT NULL,
            PRIMARY KEY (snapshot_id, count_bonus_id, service_id),
            FOREIGN KEY (snapshot_id, count_bonus_id) REFERENCES snapshot_count_bonus(snapshot_id, count_bonus_id) ON DELETE CASCADE,
            FOREIGN KEY (snapshot_id, service_id) REFERENCES snapshot_service(snapshot_id, service_id) ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS service_preset (
            id TEXT PRIMARY KEY CHECK(length(id) = 36 AND id = lower(id)),
            display_name TEXT NOT NULL CHECK(length(trim(display_name)) > 0),
            service_id TEXT NOT NULL,
            time_category_id TEXT NULL,
            default_work_minutes INTEGER NOT NULL CHECK(default_work_minutes BETWEEN 1 AND 1440),
            display_order INTEGER NOT NULL CHECK(display_order >= 0),
            is_enabled INTEGER NOT NULL CHECK(is_enabled IN (0, 1)),
            created_at_utc TEXT NOT NULL,
            updated_at_utc TEXT NOT NULL,
            FOREIGN KEY (service_id) REFERENCES service_definition(id) ON DELETE RESTRICT,
            FOREIGN KEY (time_category_id) REFERENCES time_category_definition(id) ON DELETE RESTRICT
        );

        CREATE TABLE IF NOT EXISTS basic_shift (
            id TEXT PRIMARY KEY CHECK(length(id) = 36 AND id = lower(id)),
            weekday INTEGER NOT NULL CHECK(weekday BETWEEN 1 AND 7),
            service_preset_id TEXT NULL,
            service_id TEXT NOT NULL,
            time_category_id TEXT NULL,
            input_mode TEXT NOT NULL CHECK(input_mode IN ('TimeRange', 'Duration')),
            work_minutes INTEGER NOT NULL CHECK(work_minutes BETWEEN 1 AND 1440),
            start_time_minutes INTEGER NULL CHECK(start_time_minutes BETWEEN 0 AND 1439),
            end_time_minutes INTEGER NULL CHECK(end_time_minutes BETWEEN 0 AND 1439),
            display_order INTEGER NOT NULL CHECK(display_order >= 0),
            is_enabled INTEGER NOT NULL CHECK(is_enabled IN (0, 1)),
            created_at_utc TEXT NOT NULL,
            updated_at_utc TEXT NOT NULL,
            CHECK ((input_mode = 'TimeRange' AND start_time_minutes IS NOT NULL AND end_time_minutes IS NOT NULL)
                OR (input_mode = 'Duration' AND ((start_time_minutes IS NULL AND end_time_minutes IS NULL)
                    OR (start_time_minutes IS NOT NULL AND end_time_minutes IS NOT NULL)))),
            FOREIGN KEY (service_preset_id) REFERENCES service_preset(id) ON DELETE SET NULL,
            FOREIGN KEY (service_id) REFERENCES service_definition(id) ON DELETE RESTRICT,
            FOREIGN KEY (time_category_id) REFERENCES time_category_definition(id) ON DELETE RESTRICT
        );

        CREATE TABLE IF NOT EXISTS work_record (
            id TEXT PRIMARY KEY CHECK(length(id) = 36 AND id = lower(id)),
            work_date TEXT NOT NULL CHECK(length(work_date) = 10),
            service_id TEXT NOT NULL,
            time_category_id TEXT NULL,
            input_mode TEXT NOT NULL CHECK(input_mode IN ('TimeRange', 'Duration')),
            work_minutes INTEGER NOT NULL CHECK(work_minutes BETWEEN 1 AND 1440),
            start_time_minutes INTEGER NULL CHECK(start_time_minutes BETWEEN 0 AND 1439),
            end_time_minutes INTEGER NULL CHECK(end_time_minutes BETWEEN 0 AND 1439),
            source_service_preset_id TEXT NULL,
            source_basic_shift_id TEXT NULL CHECK(source_basic_shift_id IS NULL OR length(source_basic_shift_id) = 36),
            source_work_record_id TEXT NULL CHECK(source_work_record_id IS NULL OR length(source_work_record_id) = 36),
            save_operation_id TEXT NULL CHECK(save_operation_id IS NULL OR length(save_operation_id) = 36),
            created_at_utc TEXT NOT NULL,
            updated_at_utc TEXT NOT NULL,
            CHECK ((input_mode = 'TimeRange' AND start_time_minutes IS NOT NULL AND end_time_minutes IS NOT NULL)
                OR (input_mode = 'Duration' AND ((start_time_minutes IS NULL AND end_time_minutes IS NULL)
                    OR (start_time_minutes IS NOT NULL AND end_time_minutes IS NOT NULL)))),
            FOREIGN KEY (service_id) REFERENCES service_definition(id) ON DELETE RESTRICT,
            FOREIGN KEY (time_category_id) REFERENCES time_category_definition(id) ON DELETE RESTRICT,
            FOREIGN KEY (source_service_preset_id) REFERENCES service_preset(id) ON DELETE SET NULL
        );

        CREATE TABLE IF NOT EXISTS closing_rule_history (
            id TEXT PRIMARY KEY CHECK(length(id) = 36 AND id = lower(id)),
            effective_from_year_month INTEGER NOT NULL CHECK(
                effective_from_year_month BETWEEN 100001 AND 999912
                AND effective_from_year_month % 100 BETWEEN 1 AND 12),
            closing_day INTEGER NULL CHECK(closing_day BETWEEN 1 AND 31),
            is_end_of_month INTEGER NOT NULL CHECK(is_end_of_month IN (0, 1)),
            created_at_utc TEXT NOT NULL,
            CHECK ((is_end_of_month = 1 AND closing_day IS NULL)
                OR (is_end_of_month = 0 AND closing_day IS NOT NULL))
        );

        CREATE TABLE IF NOT EXISTS monthly_allowance (
            id TEXT PRIMARY KEY CHECK(length(id) = 36 AND id = lower(id)),
            payroll_period_year_month INTEGER NOT NULL CHECK(
                payroll_period_year_month BETWEEN 100001 AND 999912
                AND payroll_period_year_month % 100 BETWEEN 1 AND 12),
            display_name TEXT NOT NULL CHECK(length(trim(display_name)) > 0),
            amount_yen INTEGER NOT NULL CHECK(amount_yen >= 0),
            created_at_utc TEXT NOT NULL,
            updated_at_utc TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS app_metadata (
            id INTEGER PRIMARY KEY CHECK(id = 1),
            initial_setup_status TEXT NOT NULL CHECK(initial_setup_status IN ('NotStarted', 'InProgress', 'Completed')),
            initial_setup_step TEXT NULL,
            initial_snapshot_id TEXT NULL,
            export_format_version INTEGER NOT NULL CHECK(export_format_version >= 1),
            last_exported_at_utc TEXT NULL,
            last_data_changed_at_utc TEXT NULL,
            backup_reminder_deferred_until_date TEXT NULL CHECK(
                backup_reminder_deferred_until_date IS NULL OR length(backup_reminder_deferred_until_date) = 10),
            created_at_utc TEXT NOT NULL,
            updated_at_utc TEXT NOT NULL,
            FOREIGN KEY (initial_snapshot_id) REFERENCES setting_snapshot(id) ON DELETE RESTRICT
        );

        CREATE INDEX IF NOT EXISTS ix_setting_month_snapshot ON setting_month(snapshot_id);
        CREATE INDEX IF NOT EXISTS ix_snapshot_service_order
            ON snapshot_service(snapshot_id, is_enabled, display_order);
        CREATE INDEX IF NOT EXISTS ix_snapshot_time_category_order
            ON snapshot_time_category(snapshot_id, service_id, is_enabled, display_order);
        CREATE UNIQUE INDEX IF NOT EXISTS ux_snapshot_rate_service
            ON snapshot_rate(snapshot_id, service_id) WHERE time_category_id IS NULL;
        CREATE UNIQUE INDEX IF NOT EXISTS ux_snapshot_rate_time_category
            ON snapshot_rate(snapshot_id, service_id, time_category_id) WHERE time_category_id IS NOT NULL;
        CREATE INDEX IF NOT EXISTS ix_snapshot_premium_snapshot ON snapshot_premium(snapshot_id, is_enabled);
        CREATE INDEX IF NOT EXISTS ix_snapshot_count_bonus_snapshot ON snapshot_count_bonus(snapshot_id, is_enabled);
        CREATE INDEX IF NOT EXISTS ix_service_preset_order ON service_preset(is_enabled, display_order);
        CREATE INDEX IF NOT EXISTS ix_basic_shift_weekday ON basic_shift(weekday, is_enabled, display_order);
        CREATE INDEX IF NOT EXISTS ix_work_record_date ON work_record(work_date);
        CREATE INDEX IF NOT EXISTS ix_work_record_service_date ON work_record(service_id, work_date);
        CREATE UNIQUE INDEX IF NOT EXISTS ux_work_record_shift_date
            ON work_record(source_basic_shift_id, work_date) WHERE source_basic_shift_id IS NOT NULL;
        CREATE UNIQUE INDEX IF NOT EXISTS ux_work_record_save_operation
            ON work_record(save_operation_id) WHERE save_operation_id IS NOT NULL;
        CREATE UNIQUE INDEX IF NOT EXISTS ux_closing_rule_effective_month
            ON closing_rule_history(effective_from_year_month);
        CREATE INDEX IF NOT EXISTS ix_monthly_allowance_period
            ON monthly_allowance(payroll_period_year_month);
        CREATE INDEX IF NOT EXISTS ix_holiday_date_lookup
            ON holiday_date(holiday_calendar_version_id, holiday_date);
        """;
}

/// <summary>Application のトランザクション境界を SQLite BEGIN IMMEDIATE へ対応付けます。</summary>
public sealed class SqliteTransactionRunner(SqliteDatabase database) : ITransactionRunner
{
    private readonly SqliteDatabase database = database ?? throw new ArgumentNullException(nameof(database));

    public Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken) =>
        database.RunTransactionAsync(operation, cancellationToken);

    public Task<TResult> ExecuteAsync<TResult>(Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken) => database.RunTransactionAsync(operation, cancellationToken);
}

internal static class SqliteValue
{
    public static string Id(Guid value) => value.ToString("D", CultureInfo.InvariantCulture).ToLowerInvariant();
    public static Guid Guid(string value) => System.Guid.ParseExact(value, "D");
    public static int YearMonth(YearMonth value) => checked(value.Year * 100 + value.Month);
    public static YearMonth YearMonth(long value) => new(checked((int)value / 100), checked((int)value % 100));
    public static string Date(DateOnly value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public static DateOnly Date(string value) => DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture);
    public static string Utc(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
    public static DateTimeOffset Utc(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
    public static object Db(string? value) => value is null ? DBNull.Value : value;
    public static object Db(long? value) => value.HasValue ? value.Value : DBNull.Value;
    public static object Db(int? value) => value.HasValue ? value.Value : DBNull.Value;
}

internal static class SqliteCommandExtensions
{
    public static SqliteParameter AddValue(this SqliteParameterCollection parameters, string name, object? value) =>
        parameters.AddWithValue(name, value ?? DBNull.Value);

    public static string GetString(this SqliteDataReader reader, string name) =>
        reader.GetString(reader.GetOrdinal(name));

    public static string? GetNullableString(this SqliteDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    public static int GetInt32(this SqliteDataReader reader, string name) =>
        reader.GetInt32(reader.GetOrdinal(name));

    public static long GetInt64(this SqliteDataReader reader, string name) =>
        reader.GetInt64(reader.GetOrdinal(name));

    public static int? GetNullableInt32(this SqliteDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }

    public static long? GetNullableInt64(this SqliteDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);
    }

    public static bool GetBoolean(this SqliteDataReader reader, string name) => GetInt64(reader, name) != 0;
}
