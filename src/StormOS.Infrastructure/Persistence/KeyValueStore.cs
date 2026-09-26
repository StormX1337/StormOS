using System.Text;
using System.Text.Json;
using StormOS.Core.Common;
using StormOS.Security.Secrets;

namespace StormOS.Infrastructure.Persistence;

/// <summary>Generic JSON key/value storage for small documents (device info, caches).</summary>
public sealed class KeyValueStore
{
    private readonly SqliteDatabase _database;
    private readonly TimeProvider _time;

    /// <summary>Initializes a new instance of the <see cref="KeyValueStore"/> class.</summary>
    /// <param name="database">The database.</param>
    /// <param name="timeProvider">Time source.</param>
    public KeyValueStore(SqliteDatabase database, TimeProvider? timeProvider = null)
    {
        _database = database;
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Reads a value.</summary>
    /// <typeparam name="T">Value type.</typeparam>
    /// <param name="key">Key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The value or default.</returns>
    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.Command("SELECT value FROM kv WHERE key = $key", ("$key", key));
        var json = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        return json is null ? default : JsonSerializer.Deserialize<T>(json, StormJson.Options);
    }

    /// <summary>Writes a value.</summary>
    /// <typeparam name="T">Value type.</typeparam>
    /// <param name="key">Key.</param>
    /// <param name="value">Value.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the operation.</returns>
    public async Task SetAsync<T>(string key, T value, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.Command(
            "INSERT OR REPLACE INTO kv (key, value, updated_at) VALUES ($key, $value, $ts)",
            ("$key", key),
            ("$value", JsonSerializer.Serialize(value, StormJson.Options)),
            ("$ts", _time.GetUtcNow().ToUnixMs()));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Stores secrets encrypted with <see cref="ISecretProtector"/> (DPAPI on Windows).</summary>
public sealed class SecureValueStore
{
    private readonly SqliteDatabase _database;
    private readonly ISecretProtector _protector;
    private readonly TimeProvider _time;

    /// <summary>Initializes a new instance of the <see cref="SecureValueStore"/> class.</summary>
    /// <param name="database">The database.</param>
    /// <param name="protector">Secret protector.</param>
    /// <param name="timeProvider">Time source.</param>
    public SecureValueStore(SqliteDatabase database, ISecretProtector protector, TimeProvider? timeProvider = null)
    {
        _database = database;
        _protector = protector;
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Reads and decrypts a secret.</summary>
    /// <param name="key">Key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The secret or <see langword="null"/>.</returns>
    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.Command("SELECT ciphertext FROM secure_values WHERE key = $key", ("$key", key));
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is byte[] cipher
            ? Encoding.UTF8.GetString(_protector.Unprotect(cipher))
            : null;
    }

    /// <summary>Encrypts and stores a secret.</summary>
    /// <param name="key">Key.</param>
    /// <param name="value">Secret value.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the operation.</returns>
    public async Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        var cipher = _protector.Protect(Encoding.UTF8.GetBytes(value));
        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.Command(
            "INSERT OR REPLACE INTO secure_values (key, ciphertext, updated_at) VALUES ($key, $cipher, $ts)",
            ("$key", key),
            ("$cipher", cipher),
            ("$ts", _time.GetUtcNow().ToUnixMs()));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Deletes a secret.</summary>
    /// <param name="key">Key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the operation.</returns>
    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.Command("DELETE FROM secure_values WHERE key = $key", ("$key", key));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
