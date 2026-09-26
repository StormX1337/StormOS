using Microsoft.Data.Sqlite;

namespace StormOS.Infrastructure.Persistence;

/// <summary>Owns the connection string and schema of a local SQLite database.</summary>
public sealed class SqliteDatabase : IDisposable
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;

    /// <summary>Initializes a new instance of the <see cref="SqliteDatabase"/> class.</summary>
    /// <param name="databasePath">Database file path, or ":memory:" style shared cache name for tests.</param>
    public SqliteDatabase(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        DatabasePath = databasePath;
        var directory = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = true,
            DefaultTimeout = 10,
        }.ToString();
    }

    /// <summary>Gets the database file path.</summary>
    public string DatabasePath { get; }

    /// <summary>Opens a connection, applying migrations on first use.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An open connection. The caller disposes it.</returns>
    public async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        if (!_initialized)
        {
            await InitializeAsync(cancellationToken).ConfigureAwait(false);
        }

        return await OpenRawAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Applies pending migrations.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the operation.</returns>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _initLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized)
            {
                return;
            }

            await using var connection = await OpenRawAsync(cancellationToken).ConfigureAwait(false);
            await using (var wal = connection.CreateCommand())
            {
                wal.CommandText = "PRAGMA journal_mode=WAL;";
                await wal.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await SchemaMigrations.ApplyAsync(connection, cancellationToken).ConfigureAwait(false);
            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _initLock.Dispose();
        SqliteConnection.ClearAllPools();
    }

    private async Task<SqliteConnection> OpenRawAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var pragma = connection.CreateCommand();
            pragma.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000; PRAGMA synchronous=NORMAL;";
            await pragma.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
