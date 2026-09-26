using StormOS.Core.Benchmark;
using StormOS.Core.Network;
using StormOS.Network.Diagnostics;

namespace StormOS.Benchmark.Workloads;

/// <summary>Network benchmark: latency, jitter and packet loss to the internet target plus HTTPS download throughput.</summary>
public sealed class NetworkBenchmark(NetworkDiagnostics diagnostics) : IBenchmark
{
    /// <summary>Gets or sets the latency target.</summary>
    public string Target { get; set; } = "1.1.1.1";

    /// <summary>Gets or sets the throughput URL (HTTPS).</summary>
    public string ThroughputUrl { get; set; } = "https://speed.cloudflare.com/__down?bytes=25000000";

    /// <inheritdoc />
    public BenchmarkType Type => BenchmarkType.Network;

    /// <inheritdoc />
    public string Name => "Network";

    /// <inheritdoc />
    public string? CheckAvailability() => diagnostics.GetActiveInterface() is null ? "No active network connection." : null;

    /// <inheritdoc />
    public async Task<IReadOnlyList<BenchmarkMetric>> RunAsync(BenchmarkRunOptions options, IProgress<BenchmarkProgress>? progress, CancellationToken cancellationToken)
    {
        progress?.Report(new BenchmarkProgress(5, "Latency and jitter"));
        PingStatistics ping = await diagnostics.PingAsync(Target, 30, cancellationToken).ConfigureAwait(false);
        progress?.Report(new BenchmarkProgress(50, "Download"));
        var throughput = await diagnostics.MeasureDownloadAsync(ThroughputUrl, cancellationToken: cancellationToken).ConfigureAwait(false);
        progress?.Report(new BenchmarkProgress(100, "Done"));

        var metrics = new List<BenchmarkMetric>
        {
            new("net.loss.percent", "Packet loss", ping.LossPercent, "%", HigherIsBetter: false),
        };
        if (ping.AverageMs is { } avg)
        {
            metrics.Add(new("net.latency.ms", "Latency", avg, "ms", HigherIsBetter: false));
        }

        if (ping.JitterMs is { } jitter)
        {
            metrics.Add(new("net.jitter.ms", "Jitter", jitter, "ms", HigherIsBetter: false));
        }

        if (throughput.DownloadBitsPerSecond is { } down)
        {
            metrics.Add(new("net.download.mbps", "Download", down / 1e6, "Mbit/s"));
        }

        if (metrics.Count == 1 && ping.Received == 0)
        {
            throw new InvalidOperationException(ping.Error ?? "The network target did not respond.");
        }

        return metrics;
    }
}
