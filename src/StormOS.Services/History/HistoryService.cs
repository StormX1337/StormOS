using StormOS.Core.Benchmark;
using StormOS.Core.History;
using StormOS.Core.Optimization;
using StormOS.Services.Client;

namespace StormOS.Services.History;

/// <summary>Combines the local history with sessions and privileged changes recorded by the service.</summary>
public sealed class HistoryService(IHistoryStore local, IStormServiceClient service, IOptimizationJournal localJournal)
{
    /// <summary>Lists events, newest first, including service-side optimizations.</summary>
    /// <param name="category">Optional category.</param>
    /// <param name="limit">Maximum events.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Events.</returns>
    public async Task<IReadOnlyList<HistoryEvent>> ListEventsAsync(HistoryCategory? category = null, int limit = 300, CancellationToken cancellationToken = default)
    {
        var events = (await local.ListEventsAsync(category, limit, cancellationToken).ConfigureAwait(false)).ToList();
        if (category is null or HistoryCategory.Optimization)
        {
            var remote = await service.GetOptimizationHistoryAsync(cancellationToken).ConfigureAwait(false);
            if (remote.IsSuccess)
            {
                events.AddRange(remote.Value!.Where(r => r.Outcome != OptimizationOutcome.Skipped).Select(ToEvent));
            }
        }

        if (category is null or HistoryCategory.Session)
        {
            var sessions = await service.GetSessionsAsync(cancellationToken).ConfigureAwait(false);
            if (sessions.IsSuccess)
            {
                events.AddRange(sessions.Value!.Select(s => new HistoryEvent
                {
                    Id = s.Id,
                    Timestamp = s.EndedAt ?? s.StartedAt,
                    Category = HistoryCategory.Session,
                    Action = $"Played {s.GameName}",
                    Result = EventResult.Info,
                    Details = s.AverageFps is { } fps ? $"{Core.Common.Units.FormatDuration(s.Duration)} · {fps:0} FPS avg" : Core.Common.Units.FormatDuration(s.Duration),
                    Rollback = RollbackStatus.NotApplicable,
                    RelatedId = s.Id.ToString(),
                }));
            }
        }

        return events.GroupBy(e => e.Id).Select(g => g.First()).OrderByDescending(e => e.Timestamp).Take(limit).ToList();
    }

    /// <summary>Lists optimization records of both executors, newest first.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Records.</returns>
    public async Task<IReadOnlyList<OptimizationRecord>> ListChangesAsync(CancellationToken cancellationToken = default)
    {
        var records = (await localJournal.ListAsync(500, cancellationToken).ConfigureAwait(false)).ToList();
        var remote = await service.GetOptimizationHistoryAsync(cancellationToken).ConfigureAwait(false);
        if (remote.IsSuccess)
        {
            records.AddRange(remote.Value!);
        }

        return records.OrderByDescending(r => r.Timestamp).ToList();
    }

    /// <summary>Lists recorded sessions (from the service, which records them).</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Sessions.</returns>
    public async Task<IReadOnlyList<GameSession>> ListSessionsAsync(CancellationToken cancellationToken = default)
    {
        var remote = await service.GetSessionsAsync(cancellationToken).ConfigureAwait(false);
        return remote.IsSuccess ? remote.Value! : await local.ListSessionsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Lists benchmarks.</summary>
    /// <param name="type">Optional type.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Benchmarks.</returns>
    public Task<IReadOnlyList<BenchmarkResult>> ListBenchmarksAsync(BenchmarkType? type = null, CancellationToken cancellationToken = default) =>
        local.ListBenchmarksAsync(type, cancellationToken: cancellationToken);

    private static HistoryEvent ToEvent(OptimizationRecord record) => new()
    {
        Id = record.Id,
        Timestamp = record.RolledBackAt ?? record.Timestamp,
        Category = HistoryCategory.Optimization,
        Action = record.RuleName,
        Result = record.Outcome switch
        {
            OptimizationOutcome.Applied => EventResult.Success,
            OptimizationOutcome.FailedRolledBack => EventResult.Partial,
            _ => EventResult.Failed,
        },
        Details = $"{record.Before} → {record.After}. {record.Message}".Trim(),
        Rollback = record.Rollback,
        RelatedId = record.Id.ToString(),
    };
}
