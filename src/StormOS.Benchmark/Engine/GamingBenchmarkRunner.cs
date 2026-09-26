using StormOS.Core.Benchmark;
using StormOS.Core.Frames;
using StormOS.Core.Scoring;
using StormOS.Core.Telemetry;
using StormOS.Performance.Frames;

namespace StormOS.Benchmark.Engine;

/// <summary>
/// Gaming benchmark: captures real frame timing of a running game for a fixed window after a warm-up and records
/// FPS, 1% / 0.1% lows, frame times and average CPU/GPU utilization.
/// </summary>
public sealed class GamingBenchmarkRunner(FrameCaptureCoordinator frames, ITelemetryHub telemetry, TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    /// <summary>Runs a gaming benchmark.</summary>
    /// <param name="options">Options (process id, warm-up, duration, labels).</param>
    /// <param name="refreshRateHz">Display refresh rate used for the gaming score, when known.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<BenchmarkResult> RunAsync(BenchmarkRunOptions options, double? refreshRateHz, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.ProcessId is not { } pid)
        {
            throw new ArgumentException("A game process id is required.", nameof(options));
        }

        var started = _time.GetUtcNow();
        double cpuSum = 0, gpuSum = 0;
        int cpuCount = 0, gpuCount = 0;
        var measuring = false;
        using var subscription = telemetry.Subscribe(snapshot =>
        {
            if (!Volatile.Read(ref measuring))
            {
                return;
            }

            if (snapshot.Cpu.Usage.Value is { } cpu)
            {
                cpuSum += cpu;
                cpuCount++;
            }

            if (snapshot.PrimaryGpu()?.Usage.Value is { } gpu)
            {
                gpuSum += gpu;
                gpuCount++;
            }
        });

        var measureTask = frames.CaptureWindowAsync(pid, options.Warmup, options.Duration, cancellationToken);
        await Task.Delay(options.Warmup, cancellationToken).ConfigureAwait(false);
        Volatile.Write(ref measuring, true);
        var captured = await measureTask.ConfigureAwait(false);
        Volatile.Write(ref measuring, false);

        if (!captured.IsSuccess)
        {
            return new BenchmarkResult
            {
                Id = Guid.NewGuid(),
                Type = BenchmarkType.Gaming,
                StartedAt = started,
                Duration = _time.GetUtcNow() - started,
                GameId = options.GameId,
                GameName = options.GameName,
                Label = options.Label,
                Completed = false,
                Error = captured.Error.Message,
            };
        }

        var statistics = FrameStatisticsCalculator.Compute(captured.Value!);
        return new BenchmarkResult
        {
            Id = Guid.NewGuid(),
            Type = BenchmarkType.Gaming,
            StartedAt = started,
            Duration = _time.GetUtcNow() - started,
            GameId = options.GameId,
            GameName = options.GameName,
            Label = options.Label,
            Settings = options.Settings,
            Frames = statistics,
            Metrics = [new("frames.count", "Frames captured", statistics.FrameCount, "frames"), new("frames.stutter", "Stutter frames", statistics.StutterCount, "frames", HigherIsBetter: false)],
            AverageCpuUsage = cpuCount > 0 ? cpuSum / cpuCount : null,
            AverageGpuUsage = gpuCount > 0 ? gpuSum / gpuCount : null,
            Score = StormScores.Gaming(statistics, refreshRateHz),
            Completed = statistics.FrameCount > 0,
            Error = statistics.FrameCount > 0 ? null : "No valid frames were captured.",
        };
    }
}
