using System.Diagnostics;
using StormOS.Core.Benchmark;

namespace StormOS.Benchmark.Workloads;

/// <summary>Memory benchmark: large block copy bandwidth and random-access latency (pointer chasing).</summary>
public sealed class MemoryBenchmark : IBenchmark
{
    private const int BandwidthBytes = 256 * 1024 * 1024;
    private const int LatencyElements = 16 * 1024 * 1024;

    /// <inheritdoc />
    public BenchmarkType Type => BenchmarkType.Memory;

    /// <inheritdoc />
    public string Name => "Memory";

    /// <inheritdoc />
    public string? CheckAvailability()
    {
        var available = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        return available < 2L * BandwidthBytes + (LatencyElements * 8L) ? "Not enough free memory to run the memory benchmark." : null;
    }

    /// <summary>Builds a single random cycle over <paramref name="length"/> elements (Sattolo's algorithm).</summary>
    /// <param name="length">Number of elements.</param>
    /// <param name="seed">Random seed.</param>
    /// <returns>next[i] gives the index visited after i.</returns>
    public static int[] BuildCycle(int length, int seed = 1234)
    {
        var next = new int[length];
        for (var i = 0; i < length; i++)
        {
            next[i] = i;
        }

        var random = new Random(seed);
        for (var i = length - 1; i > 0; i--)
        {
            var j = random.Next(i);
            (next[i], next[j]) = (next[j], next[i]);
        }

        return next;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<BenchmarkMetric>> RunAsync(BenchmarkRunOptions options, IProgress<BenchmarkProgress>? progress, CancellationToken cancellationToken) =>
        Task.Factory.StartNew(() => Run(options, progress, cancellationToken), cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    private static IReadOnlyList<BenchmarkMetric> Run(BenchmarkRunOptions options, IProgress<BenchmarkProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        var phase = TimeSpan.FromTicks(Math.Max(TimeSpan.FromSeconds(2).Ticks, options.Duration.Ticks / 2));

        progress?.Report(new BenchmarkProgress(5, "Bandwidth"));
        var source = GC.AllocateUninitializedArray<byte>(BandwidthBytes, pinned: true);
        var target = GC.AllocateUninitializedArray<byte>(BandwidthBytes, pinned: true);
        source.AsSpan().Fill(0x5A);
        source.AsSpan().CopyTo(target);
        long copied = 0;
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < phase)
        {
            cancellationToken.ThrowIfCancellationRequested();
            source.AsSpan().CopyTo(target);
            copied += BandwidthBytes;
        }

        var bandwidth = copied / stopwatch.Elapsed.TotalSeconds / 1e9;

        progress?.Report(new BenchmarkProgress(50, "Latency"));
        var next = BuildCycle(LatencyElements);
        var index = 0;
        long hops = 0;
        stopwatch.Restart();
        while (stopwatch.Elapsed < phase)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var i = 0; i < 1_000_000; i++)
            {
                index = next[index];
            }

            hops += 1_000_000;
        }

        var latency = stopwatch.Elapsed.TotalMilliseconds * 1_000_000 / hops;
        GC.KeepAlive(index);
        progress?.Report(new BenchmarkProgress(100, "Done"));

        return
        [
            new("memory.copy.gbps", "Copy bandwidth", bandwidth, "GB/s"),
            new("memory.latency.ns", "Random access latency", latency, "ns", HigherIsBetter: false),
        ];
    }
}
