using System.Text.Json;
using Microsoft.Data.Sqlite;
using StormOS.Core.Benchmark;
using StormOS.Core.Common;
using StormOS.Core.History;
using StormOS.Core.Network;
using StormOS.Core.Optimization;

namespace StormOS.Infrastructure.Persistence;

/// <summary>SQLite implementation of <see cref="IHistoryStore"/>.</summary>
public sealed class SqliteHistoryStore : IHistoryStore
{
    private readonly SqliteDatabase _database;

    /// <summary>Initializes a new instance of the <see cref="SqliteHistoryStore"/> class.</summary>
    /// <param name="database">The database.</param>
    public SqliteHistoryStore(SqliteDatabase database) => _database = database;

    /// <inheritdoc />
    public async Task AddEventAsync(HistoryEvent historyEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(historyEvent);
        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.Command(
            "INSERT OR REPLACE INTO history_events (id, ts, category, action, result, details, rollback, related_id) VALUES ($id, $ts, $category, $action, $result, $details, $rollback, $related)",
            ("$id", historyEvent.Id.ToString()),
            ("$ts", historyEvent.Timestamp.ToUnixMs()),
            ("$category", historyEvent.Category.ToString()),
            ("$action", historyEvent.Action),
            ("$result", historyEvent.Result.ToString()),
            ("$details", historyEvent.Details),
            ("$rollback", historyEvent.Rollback.ToString()),
            ("$related", historyEvent.RelatedId));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<HistoryEvent>> ListEventsAsync(HistoryCategory? category = null, int limit = 200, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = category is null
            ? connection.Command("SELECT id, ts, category, action, result, details, rollback, related_id FROM history_events ORDER BY ts DESC LIMIT $limit", ("$limit", Math.Clamp(limit, 1, 5000)))
            : connection.Command("SELECT id, ts, category, action, result, details, rollback, related_id FROM history_events WHERE category = $category ORDER BY ts DESC LIMIT $limit", ("$category", category.Value.ToString()), ("$limit", Math.Clamp(limit, 1, 5000)));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var list = new List<HistoryEvent>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(new HistoryEvent
            {
                Id = Guid.Parse(reader.GetString(0)),
                Timestamp = SqliteExtensions.FromUnixMs(reader.GetInt64(1)),
                Category = Enum.Parse<HistoryCategory>(reader.GetString(2)),
                Action = reader.GetString(3),
                Result = Enum.Parse<EventResult>(reader.GetString(4)),
                Details = reader.GetString(5),
                Rollback = Enum.Parse<RollbackStatus>(reader.GetString(6)),
                RelatedId = reader.IsDBNull(7) ? null : reader.GetString(7),
            });
        }

        return list;
    }

    /// <inheritdoc />
    public async Task SaveSessionAsync(GameSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.Command(
            "INSERT OR REPLACE INTO sessions (id, game_id, started_at, data) VALUES ($id, $game, $started, $data)",
            ("$id", session.Id.ToString()),
            ("$game", session.GameId),
            ("$started", session.StartedAt.ToUnixMs()),
            ("$data", JsonSerializer.Serialize(session, StormJson.Options)));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GameSession>> ListSessionsAsync(string? gameId = null, int limit = 100, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = gameId is null
            ? connection.Command("SELECT data FROM sessions ORDER BY started_at DESC LIMIT $limit", ("$limit", Math.Clamp(limit, 1, 5000)))
            : connection.Command("SELECT data FROM sessions WHERE game_id = $game ORDER BY started_at DESC LIMIT $limit", ("$game", gameId), ("$limit", Math.Clamp(limit, 1, 5000)));
        return await ReadJsonAsync<GameSession>(command, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SaveBenchmarkAsync(BenchmarkResult result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.Command(
            "INSERT OR REPLACE INTO benchmarks (id, type, game_id, started_at, data) VALUES ($id, $type, $game, $started, $data)",
            ("$id", result.Id.ToString()),
            ("$type", result.Type.ToString()),
            ("$game", result.GameId),
            ("$started", result.StartedAt.ToUnixMs()),
            ("$data", JsonSerializer.Serialize(result, StormJson.Options)));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BenchmarkResult>> ListBenchmarksAsync(BenchmarkType? type = null, string? gameId = null, int limit = 100, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.Command(
            "SELECT data FROM benchmarks WHERE ($type IS NULL OR type = $type) AND ($game IS NULL OR game_id = $game) ORDER BY started_at DESC LIMIT $limit",
            ("$type", type?.ToString()),
            ("$game", gameId),
            ("$limit", Math.Clamp(limit, 1, 5000)));
        return await ReadJsonAsync<BenchmarkResult>(command, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SaveNetworkReportAsync(NetworkDiagnosticsReport report, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.Command(
            "INSERT OR REPLACE INTO network_reports (id, ts, data) VALUES ($id, $ts, $data)",
            ("$id", report.Id.ToString()),
            ("$ts", report.Timestamp.ToUnixMs()),
            ("$data", JsonSerializer.Serialize(report, StormJson.Options)));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<NetworkDiagnosticsReport>> ListNetworkReportsAsync(int limit = 50, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.Command("SELECT data FROM network_reports ORDER BY ts DESC LIMIT $limit", ("$limit", Math.Clamp(limit, 1, 1000)));
        return await ReadJsonAsync<NetworkDiagnosticsReport>(command, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task AppendMetricHistoryAsync(IReadOnlyList<MetricHistoryPoint> points, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count == 0)
        {
            return;
        }

        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.Command(
            "INSERT OR REPLACE INTO metric_history (ts, cpu, gpu, ram, vram, fps, frametime, latency, cpu_temp, gpu_temp) VALUES ($ts, $cpu, $gpu, $ram, $vram, $fps, $ft, $lat, $ct, $gt)");
        command.Transaction = transaction;
        string[] names = ["$ts", "$cpu", "$gpu", "$ram", "$vram", "$fps", "$ft", "$lat", "$ct", "$gt"];
        foreach (var name in names)
        {
            command.Parameters.Add(new SqliteParameter { ParameterName = name });
        }

        foreach (var point in points)
        {
            object?[] values = [point.Timestamp.ToUnixMs(), point.Cpu, point.Gpu, point.Ram, point.Vram, point.Fps, point.FrameTimeMs, point.LatencyMs, point.CpuTemp, point.GpuTemp];
            for (var i = 0; i < values.Length; i++)
            {
                command.Parameters[i].Value = values[i] ?? DBNull.Value;
            }

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MetricHistoryPoint>> ReadMetricHistoryAsync(DateTimeOffset rangeStart, DateTimeOffset rangeEnd, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.Command(
            "SELECT ts, cpu, gpu, ram, vram, fps, frametime, latency, cpu_temp, gpu_temp FROM metric_history WHERE ts BETWEEN $from AND $to ORDER BY ts",
            ("$from", rangeStart.ToUnixMs()),
            ("$to", rangeEnd.ToUnixMs()));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var list = new List<MetricHistoryPoint>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(new MetricHistoryPoint
            {
                Timestamp = SqliteExtensions.FromUnixMs(reader.GetInt64(0)),
                Cpu = reader.GetNullableDouble(1),
                Gpu = reader.GetNullableDouble(2),
                Ram = reader.GetNullableDouble(3),
                Vram = reader.GetNullableDouble(4),
                Fps = reader.GetNullableDouble(5),
                FrameTimeMs = reader.GetNullableDouble(6),
                LatencyMs = reader.GetNullableDouble(7),
                CpuTemp = reader.GetNullableDouble(8),
                GpuTemp = reader.GetNullableDouble(9),
            });
        }

        return list;
    }

    /// <inheritdoc />
    public async Task<int> PruneAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.Command(
            "DELETE FROM metric_history WHERE ts < $cut; DELETE FROM history_events WHERE ts < $cut AND category <> 'Optimization'; DELETE FROM network_reports WHERE ts < $cut;",
            ("$cut", olderThan.ToUnixMs()));
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<T>> ReadJsonAsync<T>(SqliteCommand command, CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var list = new List<T>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (JsonSerializer.Deserialize<T>(reader.GetString(0), StormJson.Options) is { } item)
            {
                list.Add(item);
            }
        }

        return list;
    }
}
