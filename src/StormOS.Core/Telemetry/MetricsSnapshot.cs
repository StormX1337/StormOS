using StormOS.Core.Common;
using StormOS.Core.Frames;

namespace StormOS.Core.Telemetry;

/// <summary>A consistent set of live metrics captured in a single sampling tick.</summary>
public sealed record MetricsSnapshot
{
    /// <summary>Gets the sample timestamp.</summary>
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>Gets a monotonically increasing sequence number.</summary>
    public long Sequence { get; init; }

    /// <summary>Gets processor metrics.</summary>
    public CpuMetrics Cpu { get; init; } = new();

    /// <summary>Gets per-adapter GPU metrics.</summary>
    public IReadOnlyList<GpuMetrics> Gpus { get; init; } = [];

    /// <summary>Gets memory metrics.</summary>
    public MemoryMetrics Memory { get; init; } = new();

    /// <summary>Gets per-disk metrics.</summary>
    public IReadOnlyList<DiskMetrics> Disks { get; init; } = [];

    /// <summary>Gets per-interface network metrics.</summary>
    public IReadOnlyList<NetworkInterfaceMetrics> Network { get; init; } = [];

    /// <summary>Gets system-wide metrics.</summary>
    public SystemMetrics System { get; init; } = new();

    /// <summary>Gets live frame statistics when a frame capture is active.</summary>
    public FrameMetrics? Frames { get; init; }

    /// <summary>Gets a value indicating whether this snapshot was produced by the development mock source.</summary>
    public bool IsMock { get; init; }

    /// <summary>Returns the primary (most utilized, non-software) GPU metrics, if any.</summary>
    /// <returns>The primary GPU metrics or <see langword="null"/>.</returns>
    public GpuMetrics? PrimaryGpu()
    {
        GpuMetrics? best = null;
        foreach (var gpu in Gpus)
        {
            if (best is null || (gpu.MemoryTotalBytes ?? 0) > (best.MemoryTotalBytes ?? 0))
            {
                best = gpu;
            }
        }

        return best;
    }
}

/// <summary>Processor metrics.</summary>
public sealed record CpuMetrics
{
    /// <summary>Gets total CPU utility in percent.</summary>
    public Reading Usage { get; init; }

    /// <summary>Gets the effective average frequency in MHz.</summary>
    public Reading FrequencyMhz { get; init; }

    /// <summary>Gets the temperature in °C.</summary>
    public Reading TemperatureCelsius { get; init; }

    /// <summary>Gets the package power in watts.</summary>
    public Reading PackagePowerWatts { get; init; }

    /// <summary>Gets per logical processor metrics.</summary>
    public IReadOnlyList<CoreMetrics> Cores { get; init; } = [];
}

/// <summary>Per logical processor metrics.</summary>
/// <param name="Index">Logical processor index.</param>
/// <param name="UsagePercent">Utility in percent.</param>
/// <param name="FrequencyMhz">Effective frequency in MHz, when available.</param>
public sealed record CoreMetrics(int Index, double UsagePercent, double? FrequencyMhz);

/// <summary>Graphics adapter metrics.</summary>
public sealed record GpuMetrics
{
    /// <summary>Gets the adapter LUID.</summary>
    public long AdapterLuid { get; init; }

    /// <summary>Gets the adapter name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Gets the utilization of the busiest engine in percent (matches Task Manager).</summary>
    public Reading Usage { get; init; }

    /// <summary>Gets the 3D engine utilization in percent.</summary>
    public Reading Usage3D { get; init; }

    /// <summary>Gets the temperature in °C.</summary>
    public Reading TemperatureCelsius { get; init; }

    /// <summary>Gets the core clock in MHz.</summary>
    public Reading CoreClockMhz { get; init; }

    /// <summary>Gets the memory clock in MHz.</summary>
    public Reading MemoryClockMhz { get; init; }

    /// <summary>Gets the dedicated memory in use in bytes.</summary>
    public long? MemoryUsedBytes { get; init; }

    /// <summary>Gets the total dedicated memory in bytes.</summary>
    public long? MemoryTotalBytes { get; init; }

    /// <summary>Gets the board power in watts.</summary>
    public Reading PowerWatts { get; init; }

    /// <summary>Gets the fan speed in RPM.</summary>
    public Reading FanRpm { get; init; }

    /// <summary>Gets the fan speed in percent.</summary>
    public Reading FanPercent { get; init; }

    /// <summary>Gets VRAM usage in percent, when both values are known.</summary>
    public double? MemoryUsagePercent =>
        MemoryUsedBytes is { } used && MemoryTotalBytes is > 0 and var total ? 100.0 * used / total : null;
}

/// <summary>Memory metrics.</summary>
public sealed record MemoryMetrics
{
    /// <summary>Gets total physical memory in bytes.</summary>
    public long TotalBytes { get; init; }

    /// <summary>Gets available physical memory in bytes.</summary>
    public long AvailableBytes { get; init; }

    /// <summary>Gets committed memory in bytes.</summary>
    public long CommittedBytes { get; init; }

    /// <summary>Gets the commit limit in bytes.</summary>
    public long CommitLimitBytes { get; init; }

    /// <summary>Gets the system cache in bytes.</summary>
    public long CachedBytes { get; init; }

    /// <summary>Gets used physical memory in bytes.</summary>
    public long UsedBytes => Math.Max(0, TotalBytes - AvailableBytes);

    /// <summary>Gets physical memory usage in percent.</summary>
    public double UsagePercent => TotalBytes > 0 ? 100.0 * UsedBytes / TotalBytes : 0;
}

/// <summary>Physical disk metrics.</summary>
public sealed record DiskMetrics
{
    /// <summary>Gets the disk instance name, for example "0 C:".</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Gets read throughput in bytes per second.</summary>
    public double ReadBytesPerSecond { get; init; }

    /// <summary>Gets write throughput in bytes per second.</summary>
    public double WriteBytesPerSecond { get; init; }

    /// <summary>Gets active time in percent.</summary>
    public Reading ActiveTimePercent { get; init; }
}

/// <summary>Network interface metrics.</summary>
public sealed record NetworkInterfaceMetrics
{
    /// <summary>Gets the interface id.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Gets the interface name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Gets received bytes per second.</summary>
    public double ReceivedBytesPerSecond { get; init; }

    /// <summary>Gets sent bytes per second.</summary>
    public double SentBytesPerSecond { get; init; }

    /// <summary>Gets the link speed in bits per second.</summary>
    public long LinkSpeedBitsPerSecond { get; init; }
}

/// <summary>System-wide metrics.</summary>
public sealed record SystemMetrics
{
    /// <summary>Gets the number of processes.</summary>
    public int ProcessCount { get; init; }

    /// <summary>Gets the number of threads.</summary>
    public int ThreadCount { get; init; }

    /// <summary>Gets the number of handles.</summary>
    public int HandleCount { get; init; }

    /// <summary>Gets context switches per second.</summary>
    public Reading ContextSwitchesPerSecond { get; init; }

    /// <summary>Gets the system uptime.</summary>
    public TimeSpan Uptime { get; init; }

    /// <summary>Gets the latest measured internet latency in milliseconds.</summary>
    public Reading LatencyMs { get; init; }

    /// <summary>Gets the name of the active game, when one is detected.</summary>
    public string? ActiveGame { get; init; }

    /// <summary>Gets the process id of the active game, when one is detected.</summary>
    public int? ActiveGameProcessId { get; init; }
}
