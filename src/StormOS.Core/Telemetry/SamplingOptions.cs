namespace StormOS.Core.Telemetry;

/// <summary>How aggressively telemetry is collected.</summary>
public enum SamplingMode
{
    /// <summary>Normal dashboard monitoring with all collectors enabled.</summary>
    Monitoring,

    /// <summary>A game is running: expensive collectors are reduced to keep overhead minimal.</summary>
    Performance,

    /// <summary>No consumers are attached; sampling is paused.</summary>
    Idle,
}

/// <summary>Configuration for the telemetry sampler.</summary>
public sealed class SamplingOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Telemetry";

    /// <summary>Gets or sets the monitoring interval in milliseconds (250–10000).</summary>
    public int IntervalMilliseconds { get; set; } = 1000;

    /// <summary>Gets or sets the interval used in performance mode, in milliseconds.</summary>
    public int PerformanceModeIntervalMilliseconds { get; set; } = 2000;

    /// <summary>Gets or sets a value indicating whether per-core metrics are collected.</summary>
    public bool CollectPerCore { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether disk metrics are collected.</summary>
    public bool CollectDisks { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether network throughput is collected.</summary>
    public bool CollectNetwork { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether a periodic latency probe is sent.</summary>
    public bool LatencyProbeEnabled { get; set; } = true;

    /// <summary>Gets or sets the latency probe target host.</summary>
    public string LatencyProbeHost { get; set; } = "1.1.1.1";

    /// <summary>Gets or sets the latency probe interval in seconds.</summary>
    public int LatencyProbeIntervalSeconds { get; set; } = 5;

    /// <summary>Clamps values into supported ranges.</summary>
    public void Normalize()
    {
        IntervalMilliseconds = Math.Clamp(IntervalMilliseconds, 250, 10_000);
        PerformanceModeIntervalMilliseconds = Math.Clamp(PerformanceModeIntervalMilliseconds, 500, 10_000);
        LatencyProbeIntervalSeconds = Math.Clamp(LatencyProbeIntervalSeconds, 1, 300);
    }
}
