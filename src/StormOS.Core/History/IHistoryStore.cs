using StormOS.Core.Benchmark;
using StormOS.Core.Network;

namespace StormOS.Core.History;

/// <summary>Local persistence for history, sessions, benchmarks, network tests and metric history.</summary>
public interface IHistoryStore
{
    /// <summary>Adds an event.</summary>
    /// <param name="historyEvent">The event.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the operation.</returns>
    Task AddEventAsync(HistoryEvent historyEvent, CancellationToken cancellationToken = default);

    /// <summary>Lists events, newest first.</summary>
    /// <param name="category">Optional category filter.</param>
    /// <param name="limit">Maximum number of events.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Events.</returns>
    Task<IReadOnlyList<HistoryEvent>> ListEventsAsync(HistoryCategory? category = null, int limit = 200, CancellationToken cancellationToken = default);

    /// <summary>Saves a gaming session.</summary>
    /// <param name="session">The session.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the operation.</returns>
    Task SaveSessionAsync(GameSession session, CancellationToken cancellationToken = default);

    /// <summary>Lists sessions, newest first.</summary>
    /// <param name="gameId">Optional game filter.</param>
    /// <param name="limit">Maximum number of sessions.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Sessions.</returns>
    Task<IReadOnlyList<GameSession>> ListSessionsAsync(string? gameId = null, int limit = 100, CancellationToken cancellationToken = default);

    /// <summary>Saves a benchmark result.</summary>
    /// <param name="result">The result.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the operation.</returns>
    Task SaveBenchmarkAsync(BenchmarkResult result, CancellationToken cancellationToken = default);

    /// <summary>Lists benchmark results, newest first.</summary>
    /// <param name="type">Optional type filter.</param>
    /// <param name="gameId">Optional game filter.</param>
    /// <param name="limit">Maximum number of results.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Results.</returns>
    Task<IReadOnlyList<BenchmarkResult>> ListBenchmarksAsync(BenchmarkType? type = null, string? gameId = null, int limit = 100, CancellationToken cancellationToken = default);

    /// <summary>Saves a network report.</summary>
    /// <param name="report">The report.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the operation.</returns>
    Task SaveNetworkReportAsync(NetworkDiagnosticsReport report, CancellationToken cancellationToken = default);

    /// <summary>Lists network reports, newest first.</summary>
    /// <param name="limit">Maximum number of reports.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Reports.</returns>
    Task<IReadOnlyList<NetworkDiagnosticsReport>> ListNetworkReportsAsync(int limit = 50, CancellationToken cancellationToken = default);

    /// <summary>Appends downsampled metric points.</summary>
    /// <param name="points">Points.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the operation.</returns>
    Task AppendMetricHistoryAsync(IReadOnlyList<MetricHistoryPoint> points, CancellationToken cancellationToken = default);

    /// <summary>Reads metric history within a time range.</summary>
    /// <param name="rangeStart">Start (inclusive).</param>
    /// <param name="rangeEnd">End (inclusive).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Points ordered by time.</returns>
    Task<IReadOnlyList<MetricHistoryPoint>> ReadMetricHistoryAsync(DateTimeOffset rangeStart, DateTimeOffset rangeEnd, CancellationToken cancellationToken = default);

    /// <summary>Deletes data older than the retention period.</summary>
    /// <param name="olderThan">Cut-off time.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of rows deleted.</returns>
    Task<int> PruneAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default);
}
