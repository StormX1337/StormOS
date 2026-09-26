using System.Text.Json;
using StormOS.Core.Common;
using StormOS.Core.Optimization;

namespace StormOS.Infrastructure.Persistence;

/// <summary>SQLite implementation of <see cref="IOptimizationJournal"/>. Records include their snapshots.</summary>
public sealed class SqliteOptimizationJournal : IOptimizationJournal
{
    private readonly SqliteDatabase _database;

    /// <summary>Initializes a new instance of the <see cref="SqliteOptimizationJournal"/> class.</summary>
    /// <param name="database">The database.</param>
    public SqliteOptimizationJournal(SqliteDatabase database) => _database = database;

    /// <inheritdoc />
    public async Task SaveAsync(OptimizationRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.Command(
            "INSERT OR REPLACE INTO optimization_records (id, ts, rule_id, outcome, rollback, data) VALUES ($id, $ts, $rule, $outcome, $rollback, $data)",
            ("$id", record.Id.ToString()),
            ("$ts", record.Timestamp.ToUnixMs()),
            ("$rule", record.RuleId),
            ("$outcome", record.Outcome.ToString()),
            ("$rollback", record.Rollback.ToString()),
            ("$data", JsonSerializer.Serialize(record, StormJson.Options)));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<OptimizationRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.Command("SELECT data FROM optimization_records WHERE id = $id", ("$id", id.ToString()));
        var data = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        return data is null ? null : JsonSerializer.Deserialize<OptimizationRecord>(data, StormJson.Options);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<OptimizationRecord>> ListAsync(int limit = 200, CancellationToken cancellationToken = default) =>
        QueryAsync("SELECT data FROM optimization_records ORDER BY ts DESC LIMIT $limit", Math.Clamp(limit, 1, 5000), cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<OptimizationRecord>> ListActiveAsync(CancellationToken cancellationToken = default) =>
        QueryAsync($"SELECT data FROM optimization_records WHERE rollback = '{nameof(RollbackStatus.Available)}' ORDER BY ts DESC LIMIT $limit", 5000, cancellationToken);

    private async Task<IReadOnlyList<OptimizationRecord>> QueryAsync(string sql, int limit, CancellationToken cancellationToken)
    {
        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.Command(sql, ("$limit", limit));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var list = new List<OptimizationRecord>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (JsonSerializer.Deserialize<OptimizationRecord>(reader.GetString(0), StormJson.Options) is { } record)
            {
                list.Add(record);
            }
        }

        return list;
    }
}
