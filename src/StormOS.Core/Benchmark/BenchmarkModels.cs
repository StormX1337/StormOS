using StormOS.Core.Frames;
using StormOS.Core.Scoring;

namespace StormOS.Core.Benchmark;

/// <summary>Benchmark types.</summary>
public enum BenchmarkType
{
    /// <summary>Processor throughput.</summary>
    Cpu,

    /// <summary>GPU compute throughput.</summary>
    Gpu,

    /// <summary>Memory bandwidth and latency.</summary>
    Memory,

    /// <summary>Storage throughput and IOPS.</summary>
    Disk,

    /// <summary>Network latency, jitter, loss and throughput.</summary>
    Network,

    /// <summary>Frame capture of a running game.</summary>
    Gaming,
}

/// <summary>A single measured benchmark metric.</summary>
/// <param name="Key">Stable key, for example "cpu.multi.mops".</param>
/// <param name="Name">Display name.</param>
/// <param name="Value">Measured value.</param>
/// <param name="Unit">Unit.</param>
/// <param name="HigherIsBetter">Whether larger values are better.</param>
public sealed record BenchmarkMetric(string Key, string Name, double Value, string Unit, bool HigherIsBetter = true);

/// <summary>A completed benchmark run.</summary>
public sealed record BenchmarkResult
{
    /// <summary>Gets the run id.</summary>
    public Guid Id { get; init; }

    /// <summary>Gets the benchmark type.</summary>
    public BenchmarkType Type { get; init; }

    /// <summary>Gets the start time.</summary>
    public DateTimeOffset StartedAt { get; init; }

    /// <summary>Gets the run duration.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>Gets a hardware summary (CPU / GPU / RAM) at the time of the run.</summary>
    public string Hardware { get; init; } = string.Empty;

    /// <summary>Gets the game id for gaming benchmarks.</summary>
    public string? GameId { get; init; }

    /// <summary>Gets the game name for gaming benchmarks.</summary>
    public string? GameName { get; init; }

    /// <summary>Gets free-form settings notes, for example graphics preset.</summary>
    public string? Settings { get; init; }

    /// <summary>Gets an optional label such as "before" or "after".</summary>
    public string? Label { get; init; }

    /// <summary>Gets the measured metrics.</summary>
    public IReadOnlyList<BenchmarkMetric> Metrics { get; init; } = [];

    /// <summary>Gets frame statistics for gaming benchmarks.</summary>
    public FrameStatistics? Frames { get; init; }

    /// <summary>Gets the average CPU utilization during the run.</summary>
    public double? AverageCpuUsage { get; init; }

    /// <summary>Gets the average GPU utilization during the run.</summary>
    public double? AverageGpuUsage { get; init; }

    /// <summary>Gets the transparent score, when enough data exists.</summary>
    public ScoreBreakdown? Score { get; init; }

    /// <summary>Gets a value indicating whether the run completed.</summary>
    public bool Completed { get; init; }

    /// <summary>Gets an error message when the run did not complete.</summary>
    public string? Error { get; init; }

    /// <summary>Gets the STORM OS version that produced the result.</summary>
    public string AppVersion { get; init; } = string.Empty;

    /// <summary>Finds a metric by key.</summary>
    /// <param name="key">Metric key.</param>
    /// <returns>The metric or <see langword="null"/>.</returns>
    public BenchmarkMetric? Metric(string key) => Metrics.FirstOrDefault(m => string.Equals(m.Key, key, StringComparison.Ordinal));
}

/// <summary>Benchmark progress notification.</summary>
/// <param name="Percent">Progress 0–100.</param>
/// <param name="Stage">Current stage description.</param>
public sealed record BenchmarkProgress(double Percent, string Stage);

/// <summary>Options for a benchmark run.</summary>
public sealed record BenchmarkRunOptions
{
    /// <summary>Gets the approximate run duration for time based tests.</summary>
    public TimeSpan Duration { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Gets the target process for gaming benchmarks.</summary>
    public int? ProcessId { get; init; }

    /// <summary>Gets the game id for gaming benchmarks.</summary>
    public string? GameId { get; init; }

    /// <summary>Gets the game name for gaming benchmarks.</summary>
    public string? GameName { get; init; }

    /// <summary>Gets the warm-up period excluded from results.</summary>
    public TimeSpan Warmup { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>Gets the directory used for disk tests.</summary>
    public string? DiskTestDirectory { get; init; }

    /// <summary>Gets the disk test file size in bytes.</summary>
    public long DiskTestFileBytes { get; init; } = 512L * 1024 * 1024;

    /// <summary>Gets a label such as "before" or "after".</summary>
    public string? Label { get; init; }

    /// <summary>Gets settings notes stored with the result.</summary>
    public string? Settings { get; init; }
}

/// <summary>A benchmark implementation.</summary>
public interface IBenchmark
{
    /// <summary>Gets the benchmark type.</summary>
    BenchmarkType Type { get; }

    /// <summary>Gets the display name.</summary>
    string Name { get; }

    /// <summary>Checks whether the benchmark can run on this system.</summary>
    /// <returns><see langword="null"/> when available, otherwise the reason it cannot run.</returns>
    string? CheckAvailability();

    /// <summary>Runs the benchmark.</summary>
    /// <param name="options">Run options.</param>
    /// <param name="progress">Progress receiver.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result metrics (the engine fills in metadata and score).</returns>
    Task<IReadOnlyList<BenchmarkMetric>> RunAsync(BenchmarkRunOptions options, IProgress<BenchmarkProgress>? progress, CancellationToken cancellationToken);
}

/// <summary>A per-metric difference between two runs.</summary>
/// <param name="Key">Metric key.</param>
/// <param name="Name">Display name.</param>
/// <param name="Unit">Unit.</param>
/// <param name="Before">Before value.</param>
/// <param name="After">After value.</param>
/// <param name="DeltaPercent">Relative change in percent.</param>
/// <param name="Improved">Whether the change is an improvement.</param>
public sealed record MetricDelta(string Key, string Name, string Unit, double Before, double After, double DeltaPercent, bool Improved);

/// <summary>Comparison of two runs of the same benchmark type.</summary>
/// <param name="Before">Earlier run.</param>
/// <param name="After">Later run.</param>
/// <param name="Deltas">Differences for metrics present in both runs.</param>
public sealed record BenchmarkComparison(BenchmarkResult Before, BenchmarkResult After, IReadOnlyList<MetricDelta> Deltas);

/// <summary>Compares benchmark runs. Differences are only computed for metrics measured in both runs.</summary>
public static class BenchmarkComparer
{
    /// <summary>Compares two runs.</summary>
    /// <param name="before">Earlier run.</param>
    /// <param name="after">Later run.</param>
    /// <returns>The comparison, or <see langword="null"/> when the runs are not comparable.</returns>
    public static BenchmarkComparison? Compare(BenchmarkResult before, BenchmarkResult after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        if (before.Type != after.Type || !before.Completed || !after.Completed)
        {
            return null;
        }

        var deltas = new List<MetricDelta>();
        foreach (var metric in AllMetrics(before))
        {
            var other = AllMetrics(after).FirstOrDefault(m => m.Key == metric.Key);
            if (other is null || metric.Value == 0 || !double.IsFinite(metric.Value) || !double.IsFinite(other.Value))
            {
                continue;
            }

            var delta = (other.Value - metric.Value) / Math.Abs(metric.Value) * 100.0;
            var improved = metric.HigherIsBetter ? other.Value > metric.Value : other.Value < metric.Value;
            deltas.Add(new MetricDelta(metric.Key, metric.Name, metric.Unit, metric.Value, other.Value, delta, improved));
        }

        return new BenchmarkComparison(before, after, deltas);
    }

    /// <summary>Returns explicit metrics plus metrics derived from frame statistics.</summary>
    /// <param name="result">The result.</param>
    /// <returns>All comparable metrics.</returns>
    public static IReadOnlyList<BenchmarkMetric> AllMetrics(BenchmarkResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Frames is not { FrameCount: > 0 } frames)
        {
            return result.Metrics;
        }

        var list = new List<BenchmarkMetric>(result.Metrics);
        AddIfMissing(list, new BenchmarkMetric("fps.avg", "Average FPS", frames.AverageFps, "FPS"));
        AddIfMissing(list, new BenchmarkMetric("fps.low1", "1% Low", frames.OnePercentLowFps, "FPS"));
        AddIfMissing(list, new BenchmarkMetric("fps.low01", "0.1% Low", frames.PointOnePercentLowFps, "FPS"));
        AddIfMissing(list, new BenchmarkMetric("frametime.avg", "Frame time", frames.AverageFrameTimeMs, "ms", HigherIsBetter: false));
        return list;
    }

    private static void AddIfMissing(List<BenchmarkMetric> list, BenchmarkMetric metric)
    {
        if (!list.Exists(m => m.Key == metric.Key))
        {
            list.Add(metric);
        }
    }
}
