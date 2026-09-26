using System.Globalization;
using StormOS.Core.Benchmark;
using StormOS.Core.Frames;
using StormOS.Core.Network;

namespace StormOS.Core.Scoring;

/// <summary>Inputs for the system health score.</summary>
public sealed record SystemHealthInputs
{
    /// <summary>Gets memory usage in percent.</summary>
    public double? MemoryUsagePercent { get; init; }

    /// <summary>Gets free space on the system drive in percent.</summary>
    public double? SystemDriveFreePercent { get; init; }

    /// <summary>Gets the number of enabled startup entries.</summary>
    public int? EnabledStartupEntries { get; init; }

    /// <summary>Gets the number of running processes.</summary>
    public int? ProcessCount { get; init; }

    /// <summary>Gets the CPU temperature in °C.</summary>
    public double? CpuTemperatureCelsius { get; init; }

    /// <summary>Gets the GPU temperature in °C.</summary>
    public double? GpuTemperatureCelsius { get; init; }
}

/// <summary>The four transparent STORM OS scores. All thresholds are documented in docs/BENCHMARK.md.</summary>
public static class StormScores
{
    /// <summary>Reference single-thread CPU throughput (MOPS) mapped to 100 points.</summary>
    public const double CpuSingleReference = 1200;

    /// <summary>Reference multi-thread CPU throughput (MOPS) mapped to 100 points.</summary>
    public const double CpuMultiReference = 16000;

    /// <summary>Reference memory copy bandwidth (GB/s) mapped to 100 points.</summary>
    public const double MemoryBandwidthReference = 60;

    /// <summary>Reference memory latency (ns) mapped to 100 points (lower is better).</summary>
    public const double MemoryLatencyReference = 60;

    /// <summary>Reference sequential read (MB/s) mapped to 100 points.</summary>
    public const double DiskSequentialReference = 6000;

    /// <summary>Reference 4K random read IOPS mapped to 100 points.</summary>
    public const double DiskRandomReference = 80000;

    /// <summary>Reference GPU compute throughput (GFLOPS) mapped to 100 points.</summary>
    public const double GpuComputeReference = 40000;

    /// <summary>Computes the STORM network score from measured values only.</summary>
    /// <param name="internet">Internet ping statistics.</param>
    /// <param name="dns">DNS test results for configured servers.</param>
    /// <returns>The score breakdown.</returns>
    public static ScoreBreakdown Network(PingStatistics? internet, IReadOnlyList<DnsTestResult>? dns)
    {
        var reachable = internet is { Received: > 0 };
        var configuredDns = dns?.Where(d => d.IsConfigured && d.Success && d.LatencyMs.HasValue).Select(d => d.LatencyMs!.Value).ToList();
        double? dnsLatency = configuredDns is { Count: > 0 } ? configuredDns.Average() : null;

        var inputs = new List<ScoreInput>
        {
            Input("Latency", reachable ? internet!.AverageMs : null, "ms", v => WeightedScore.LowerIsBetter(v, 10, 150), 0.35, "10 ms or less = 100, 150 ms or more = 0"),
            Input("Jitter", reachable ? internet!.JitterMs : null, "ms", v => WeightedScore.LowerIsBetter(v, 1, 30), 0.20, "1 ms or less = 100, 30 ms or more = 0"),
            Input("Packet loss", internet is { Sent: > 0 } ? internet.LossPercent : null, "%", v => WeightedScore.LowerIsBetter(v, 0, 5), 0.30, "0 % = 100, 5 % or more = 0"),
            Input("DNS latency", dnsLatency, "ms", v => WeightedScore.LowerIsBetter(v, 10, 200), 0.15, "10 ms or less = 100, 200 ms or more = 0"),
        };
        return WeightedScore.Compute("Network Score", inputs);
    }

    /// <summary>Computes the system health score.</summary>
    /// <param name="inputs">Measured inputs.</param>
    /// <returns>The score breakdown.</returns>
    public static ScoreBreakdown SystemHealth(SystemHealthInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        var list = new List<ScoreInput>
        {
            Input("Memory usage", inputs.MemoryUsagePercent, "%", v => WeightedScore.LowerIsBetter(v, 50, 95), 0.20, "50 % or less = 100, 95 % or more = 0"),
            Input("System drive free space", inputs.SystemDriveFreePercent, "%", v => WeightedScore.HigherIsBetter(v, 5, 25), 0.20, "25 % or more = 100, 5 % or less = 0"),
            Input("Enabled startup entries", inputs.EnabledStartupEntries, "entries", v => WeightedScore.LowerIsBetter(v, 5, 25), 0.15, "5 or fewer = 100, 25 or more = 0"),
            Input("Running processes", inputs.ProcessCount, "processes", v => WeightedScore.LowerIsBetter(v, 120, 320), 0.15, "120 or fewer = 100, 320 or more = 0"),
            Input("CPU temperature", inputs.CpuTemperatureCelsius, "°C", v => WeightedScore.LowerIsBetter(v, 60, 95), 0.15, "60 °C or less = 100, 95 °C or more = 0"),
            Input("GPU temperature", inputs.GpuTemperatureCelsius, "°C", v => WeightedScore.LowerIsBetter(v, 65, 90), 0.15, "65 °C or less = 100, 90 °C or more = 0"),
        };
        return WeightedScore.Compute("System Health Score", list);
    }

    /// <summary>Computes the performance score from the latest synthetic benchmark results.</summary>
    /// <param name="cpu">CPU result.</param>
    /// <param name="memory">Memory result.</param>
    /// <param name="disk">Disk result.</param>
    /// <param name="gpu">GPU result.</param>
    /// <returns>The score breakdown.</returns>
    public static ScoreBreakdown Performance(BenchmarkResult? cpu, BenchmarkResult? memory, BenchmarkResult? disk, BenchmarkResult? gpu)
    {
        var list = new List<ScoreInput>
        {
            Ratio("CPU single-thread", cpu?.Metric("cpu.single.mops")?.Value, "MOPS", CpuSingleReference, 0.20),
            Ratio("CPU multi-thread", cpu?.Metric("cpu.multi.mops")?.Value, "MOPS", CpuMultiReference, 0.25),
            Ratio("GPU compute", gpu?.Metric("gpu.compute.gflops")?.Value, "GFLOPS", GpuComputeReference, 0.25),
            Ratio("Memory bandwidth", memory?.Metric("memory.copy.gbps")?.Value, "GB/s", MemoryBandwidthReference, 0.10),
            Input("Memory latency", memory?.Metric("memory.latency.ns")?.Value, "ns", v => Math.Clamp(100.0 * MemoryLatencyReference / Math.Max(v, 1), 0, 100), 0.05, string.Create(CultureInfo.InvariantCulture, $"{MemoryLatencyReference} ns = 100, scaled inversely")),
            Ratio("Disk sequential read", disk?.Metric("disk.seqread.mbps")?.Value, "MB/s", DiskSequentialReference, 0.08),
            Ratio("Disk 4K random read", disk?.Metric("disk.rand4k.iops")?.Value, "IOPS", DiskRandomReference, 0.07),
        };
        return WeightedScore.Compute("Performance Score", list);
    }

    /// <summary>Computes the gaming score from frame statistics.</summary>
    /// <param name="frames">Frame statistics of a gaming benchmark or session.</param>
    /// <param name="refreshRateHz">Refresh rate of the display the game ran on.</param>
    /// <returns>The score breakdown.</returns>
    public static ScoreBreakdown Gaming(FrameStatistics? frames, double? refreshRateHz)
    {
        var hasFrames = frames is { FrameCount: > 0 };
        double? fpsRatio = hasFrames && refreshRateHz is > 0 ? frames!.AverageFps / refreshRateHz.Value * 100 : null;
        double? consistency = hasFrames && frames!.AverageFps > 0 ? frames.OnePercentLowFps / frames.AverageFps : null;
        double? stutterRate = hasFrames ? 1000.0 * frames!.StutterCount / frames.FrameCount : null;

        var list = new List<ScoreInput>
        {
            Input("Average FPS vs refresh rate", fpsRatio, "% of refresh", v => Math.Clamp(v, 0, 100), 0.40, "Average FPS at or above the display refresh rate = 100"),
            Input("1% low / average", consistency, "ratio", v => WeightedScore.HigherIsBetter(v, 0.3, 0.75), 0.35, "Ratio 0.75 or more = 100, 0.3 or less = 0"),
            Input("Stutter rate", stutterRate, "per 1000 frames", v => WeightedScore.LowerIsBetter(v, 0, 20), 0.25, "0 = 100, 20 or more per 1000 frames = 0"),
        };
        return WeightedScore.Compute("Gaming Score", list);
    }

    private static ScoreInput Ratio(string name, double? value, string unit, double reference, double weight) =>
        Input(name, value, unit, v => Math.Clamp(100.0 * v / reference, 0, 100), weight, string.Create(CultureInfo.InvariantCulture, $"{reference:N0} {unit} = 100, scaled linearly"));

    private static ScoreInput Input(string name, double? value, string unit, Func<double, double> normalize, double weight, string explanation) =>
        new(name, value, unit, value.HasValue && double.IsFinite(value.Value) ? Math.Round(normalize(value.Value), 1) : null, weight, explanation);
}
