using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using StormOS.Core.Benchmark;

namespace StormOS.Benchmark.Workloads;

/// <summary>
/// CPU benchmark: a deterministic mixed integer/floating point kernel measured single- and multi-threaded
/// (reported in million kernel iterations per second, MOPS) plus SHA-256 hashing throughput.
/// </summary>
public sealed class CpuBenchmark : IBenchmark
{
    /// <inheritdoc />
    public BenchmarkType Type => BenchmarkType.Cpu;

    /// <inheritdoc />
    public string Name => "CPU";

    /// <inheritdoc />
    public string? CheckAvailability() => null;

    /// <summary>Runs one kernel batch and returns a checksum so the JIT cannot eliminate the work.</summary>
    /// <param name="seed">Seed.</param>
    /// <param name="iterations">Iterations.</param>
    /// <returns>Checksum.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static ulong Kernel(ulong seed, int iterations)
    {
        var x = seed | 1;
        var acc = 0.0;
        for (var i = 0; i < iterations; i++)
        {
            x ^= x << 13;
            x ^= x >> 7;
            x ^= x << 17;
            var f = (x & 0xFFFF) * (1.0 / 65536.0);
            acc = Math.FusedMultiplyAdd(acc, 0.999, f);
            x += (ulong)System.Numerics.BitOperations.PopCount(x) * 0x9E3779B97F4A7C15UL;
        }

        return x ^ (ulong)BitConverter.DoubleToInt64Bits(acc);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<BenchmarkMetric>> RunAsync(BenchmarkRunOptions options, IProgress<BenchmarkProgress>? progress, CancellationToken cancellationToken) =>
        Task.Factory.StartNew(() => Run(options, progress, cancellationToken), cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    private static IReadOnlyList<BenchmarkMetric> Run(BenchmarkRunOptions options, IProgress<BenchmarkProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        var phase = TimeSpan.FromTicks(Math.Max(TimeSpan.FromSeconds(2).Ticks, options.Duration.Ticks / 3));

        progress?.Report(new BenchmarkProgress(5, "Single-thread"));
        Kernel(1, 1_000_000);
        var single = Measure(1, phase, cancellationToken);

        progress?.Report(new BenchmarkProgress(40, "Multi-thread"));
        var threads = Environment.ProcessorCount;
        var multi = Measure(threads, phase, cancellationToken);

        progress?.Report(new BenchmarkProgress(75, "SHA-256"));
        var sha = MeasureSha256(phase, cancellationToken);
        progress?.Report(new BenchmarkProgress(100, "Done"));

        return
        [
            new("cpu.single.mops", "Single-thread", single, "MOPS"),
            new("cpu.multi.mops", "Multi-thread", multi, "MOPS"),
            new("cpu.scaling", "Multi-thread scaling", single > 0 ? multi / single : 0, "×"),
            new("cpu.threads", "Threads", threads, "threads"),
            new("cpu.sha256.mbps", "SHA-256 (1 thread)", sha, "MB/s"),
        ];
    }

    private static double Measure(int threads, TimeSpan duration, CancellationToken cancellationToken)
    {
        const int Batch = 200_000;
        var counts = new long[threads];
        using var start = new ManualResetEventSlim();
        var stopAt = 0L;
        var workers = Enumerable.Range(0, threads).Select(t => new Thread(() =>
        {
            start.Wait(cancellationToken);
            ulong sink = 0;
            long done = 0;
            while (Stopwatch.GetTimestamp() < Volatile.Read(ref stopAt) && !cancellationToken.IsCancellationRequested)
            {
                sink += Kernel((ulong)(t + done), Batch);
                done += Batch;
            }

            counts[t] = done;
            GC.KeepAlive(sink);
        }) { IsBackground = true, Priority = ThreadPriority.Normal }).ToList();

        foreach (var worker in workers)
        {
            worker.Start();
        }

        var began = Stopwatch.GetTimestamp();
        Volatile.Write(ref stopAt, began + (long)(duration.TotalSeconds * Stopwatch.Frequency));
        start.Set();
        foreach (var worker in workers)
        {
            worker.Join();
        }

        cancellationToken.ThrowIfCancellationRequested();
        var seconds = Stopwatch.GetElapsedTime(began).TotalSeconds;
        return counts.Sum() / seconds / 1_000_000.0;
    }

    private static double MeasureSha256(TimeSpan duration, CancellationToken cancellationToken)
    {
        var data = new byte[1024 * 1024];
        new Random(42).NextBytes(data);
        Span<byte> hash = stackalloc byte[32];
        long bytes = 0;
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < duration)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SHA256.HashData(data, hash);
            bytes += data.Length;
        }

        return bytes / stopwatch.Elapsed.TotalSeconds / (1024 * 1024);
    }
}
