using System.Text.Json;
using StormOS.Core.Common;
using StormOS.Core.Settings;
using StormOS.Infrastructure.Persistence;

namespace StormOS.Infrastructure.Settings;

/// <summary>Persists <see cref="StormSettings"/> as a JSON document in the local database.</summary>
public sealed class SqliteSettingsStore : ISettingsStore
{
    private const string Key = "settings";
    private readonly SqliteDatabase _database;
    private readonly TimeProvider _time;
    private StormSettings _current = new();

    /// <summary>Initializes a new instance of the <see cref="SqliteSettingsStore"/> class.</summary>
    /// <param name="database">The database.</param>
    /// <param name="timeProvider">Time source.</param>
    public SqliteSettingsStore(SqliteDatabase database, TimeProvider? timeProvider = null)
    {
        _database = database;
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public event EventHandler<StormSettings>? Changed;

    /// <inheritdoc />
    public StormSettings Current => Volatile.Read(ref _current);

    /// <inheritdoc />
    public async Task<StormSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.Command("SELECT value FROM settings WHERE key = $key", ("$key", Key));
        var json = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        StormSettings loaded;
        try
        {
            loaded = json is null ? new StormSettings() : JsonSerializer.Deserialize<StormSettings>(json, StormJson.Options) ?? new StormSettings();
        }
        catch (JsonException)
        {
            // A corrupted settings document must never prevent startup; defaults are safe and privacy preserving.
            loaded = new StormSettings();
        }

        Volatile.Write(ref _current, loaded);
        return loaded;
    }

    /// <inheritdoc />
    public async Task SaveAsync(StormSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.Command(
            "INSERT OR REPLACE INTO settings (key, value, updated_at) VALUES ($key, $value, $ts)",
            ("$key", Key),
            ("$value", JsonSerializer.Serialize(settings, StormJson.Options)),
            ("$ts", _time.GetUtcNow().ToUnixMs()));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        Volatile.Write(ref _current, settings);
        Changed?.Invoke(this, settings);
    }
}
