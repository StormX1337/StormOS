using Microsoft.Data.Sqlite;

namespace StormOS.Infrastructure.Persistence;

/// <summary>Versioned schema migrations tracked with PRAGMA user_version.</summary>
public static class SchemaMigrations
{
    private static readonly (int Version, string Sql)[] Migrations =
    [
        (1, """
            CREATE TABLE IF NOT EXISTS settings (key TEXT PRIMARY KEY, value TEXT NOT NULL, updated_at INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS kv (key TEXT PRIMARY KEY, value TEXT NOT NULL, updated_at INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS secure_values (key TEXT PRIMARY KEY, ciphertext BLOB NOT NULL, updated_at INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS history_events (
                id TEXT PRIMARY KEY, ts INTEGER NOT NULL, category TEXT NOT NULL, action TEXT NOT NULL,
                result TEXT NOT NULL, details TEXT NOT NULL, rollback TEXT NOT NULL, related_id TEXT);
            CREATE INDEX IF NOT EXISTS ix_history_events_ts ON history_events (ts DESC);
            CREATE INDEX IF NOT EXISTS ix_history_events_category ON history_events (category, ts DESC);
            CREATE TABLE IF NOT EXISTS sessions (id TEXT PRIMARY KEY, game_id TEXT, started_at INTEGER NOT NULL, data TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS ix_sessions_started ON sessions (started_at DESC);
            CREATE INDEX IF NOT EXISTS ix_sessions_game ON sessions (game_id, started_at DESC);
            CREATE TABLE IF NOT EXISTS benchmarks (id TEXT PRIMARY KEY, type TEXT NOT NULL, game_id TEXT, started_at INTEGER NOT NULL, data TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS ix_benchmarks_type ON benchmarks (type, started_at DESC);
            CREATE TABLE IF NOT EXISTS network_reports (id TEXT PRIMARY KEY, ts INTEGER NOT NULL, data TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS metric_history (
                ts INTEGER PRIMARY KEY, cpu REAL, gpu REAL, ram REAL, vram REAL, fps REAL, frametime REAL,
                latency REAL, cpu_temp REAL, gpu_temp REAL) WITHOUT ROWID;
            CREATE TABLE IF NOT EXISTS optimization_records (
                id TEXT PRIMARY KEY, ts INTEGER NOT NULL, rule_id TEXT NOT NULL, outcome TEXT NOT NULL,
                rollback TEXT NOT NULL, data TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS ix_optimization_records_ts ON optimization_records (ts DESC);
            """),
    ];

    /// <summary>Gets the latest schema version.</summary>
    public static int LatestVersion => Migrations[^1].Version;

    /// <summary>Applies all pending migrations inside transactions.</summary>
    /// <param name="connection">An open connection.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the operation.</returns>
    public static async Task ApplyAsync(SqliteConnection connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var current = await GetVersionAsync(connection, cancellationToken).ConfigureAwait(false);
        foreach (var (version, sql) in Migrations)
        {
            if (version <= current)
            {
                continue;
            }

            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = sql + $"\nPRAGMA user_version = {version};";
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Reads the schema version.</summary>
    /// <param name="connection">An open connection.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The version.</returns>
    public static async Task<int> GetVersionAsync(SqliteConnection connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture);
    }
}
