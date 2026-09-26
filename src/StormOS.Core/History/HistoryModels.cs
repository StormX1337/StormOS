using StormOS.Core.Optimization;

namespace StormOS.Core.History;

/// <summary>History event categories.</summary>
public enum HistoryCategory
{
    /// <summary>Gaming sessions.</summary>
    Session,

    /// <summary>Optimizations.</summary>
    Optimization,

    /// <summary>Benchmarks.</summary>
    Benchmark,

    /// <summary>Network tests.</summary>
    NetworkTest,

    /// <summary>System changes such as power plan or startup changes.</summary>
    SystemChange,

    /// <summary>System scans.</summary>
    Scan,
}

/// <summary>Result of a history event.</summary>
public enum EventResult
{
    /// <summary>Informational.</summary>
    Info,

    /// <summary>Succeeded.</summary>
    Success,

    /// <summary>Partially succeeded.</summary>
    Partial,

    /// <summary>Failed.</summary>
    Failed,
}

/// <summary>An entry in the local history.</summary>
public sealed record HistoryEvent
{
    /// <summary>Gets the event id.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Gets the event time.</summary>
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>Gets the category.</summary>
    public HistoryCategory Category { get; init; }

    /// <summary>Gets the action performed.</summary>
    public string Action { get; init; } = string.Empty;

    /// <summary>Gets the result.</summary>
    public EventResult Result { get; init; }

    /// <summary>Gets details.</summary>
    public string Details { get; init; } = string.Empty;

    /// <summary>Gets the rollback status.</summary>
    public RollbackStatus Rollback { get; init; }

    /// <summary>Gets the related entity id (session, benchmark, change).</summary>
    public string? RelatedId { get; init; }
}

/// <summary>A recorded gaming session.</summary>
public sealed record GameSession
{
    /// <summary>Gets the session id.</summary>
    public Guid Id { get; init; }

    /// <summary>Gets the game id, when known.</summary>
    public string? GameId { get; init; }

    /// <summary>Gets the game name.</summary>
    public string GameName { get; init; } = string.Empty;

    /// <summary>Gets the process name.</summary>
    public string ProcessName { get; init; } = string.Empty;

    /// <summary>Gets the start time.</summary>
    public DateTimeOffset StartedAt { get; init; }

    /// <summary>Gets the end time.</summary>
    public DateTimeOffset? EndedAt { get; init; }

    /// <summary>Gets the duration.</summary>
    public TimeSpan Duration => (EndedAt ?? StartedAt) - StartedAt;

    /// <summary>Gets the average FPS, when frames were captured.</summary>
    public double? AverageFps { get; init; }

    /// <summary>Gets the 1% low FPS.</summary>
    public double? OnePercentLowFps { get; init; }

    /// <summary>Gets the 0.1% low FPS.</summary>
    public double? PointOnePercentLowFps { get; init; }

    /// <summary>Gets the average frame time in ms.</summary>
    public double? AverageFrameTimeMs { get; init; }

    /// <summary>Gets the average CPU usage in percent.</summary>
    public double? AverageCpuUsage { get; init; }

    /// <summary>Gets the average GPU usage in percent.</summary>
    public double? AverageGpuUsage { get; init; }

    /// <summary>Gets the maximum CPU temperature in °C.</summary>
    public double? MaxCpuTemperature { get; init; }

    /// <summary>Gets the maximum GPU temperature in °C.</summary>
    public double? MaxGpuTemperature { get; init; }

    /// <summary>Gets the average latency in ms.</summary>
    public double? AverageLatencyMs { get; init; }

    /// <summary>Gets the frame capture source.</summary>
    public string? FrameSource { get; init; }

    /// <summary>Gets the number of telemetry samples aggregated.</summary>
    public int SampleCount { get; init; }
}

/// <summary>A downsampled historical metric point used for long-range charts.</summary>
public sealed record MetricHistoryPoint
{
    /// <summary>Gets the timestamp.</summary>
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>Gets CPU usage in percent.</summary>
    public double? Cpu { get; init; }

    /// <summary>Gets GPU usage in percent.</summary>
    public double? Gpu { get; init; }

    /// <summary>Gets RAM usage in percent.</summary>
    public double? Ram { get; init; }

    /// <summary>Gets VRAM usage in percent.</summary>
    public double? Vram { get; init; }

    /// <summary>Gets FPS.</summary>
    public double? Fps { get; init; }

    /// <summary>Gets frame time in ms.</summary>
    public double? FrameTimeMs { get; init; }

    /// <summary>Gets latency in ms.</summary>
    public double? LatencyMs { get; init; }

    /// <summary>Gets CPU temperature in °C.</summary>
    public double? CpuTemp { get; init; }

    /// <summary>Gets GPU temperature in °C.</summary>
    public double? GpuTemp { get; init; }
}
