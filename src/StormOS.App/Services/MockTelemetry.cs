using StormOS.Core.Telemetry;
#if STORM_MOCK_MODE
using StormOS.Core.Common;
using StormOS.Core.Frames;
#endif

namespace StormOS.App.Services;

/// <summary>
/// Development-only synthetic telemetry for UI work without hardware access. Compiled only with
/// STORM_MOCK_MODE (never in Release) and every snapshot is flagged <see cref="MetricsSnapshot.IsMock"/>;
/// the shell shows a permanent "Development mock data" banner.
/// </summary>
internal static class MockTelemetry
{
    public static IDisposable Start(Action<MetricsSnapshot> sink)
    {
#if STORM_MOCK_MODE
        var cts = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            long sequence = 0;
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            while (await timer.WaitForNextTickAsync(cts.Token).ConfigureAwait(false))
            {
                var t = sequence++ / 10.0;
                sink(new MetricsSnapshot
                {
                    IsMock = true,
                    Sequence = sequence,
                    Timestamp = DateTimeOffset.UtcNow,
                    Cpu = new CpuMetrics { Usage = Reading.Of(35 + (20 * Math.Sin(t)), "MOCK"), FrequencyMhz = Reading.Of(4200, "MOCK"), TemperatureCelsius = Reading.Unavailable("Mock mode"), PackagePowerWatts = Reading.Unavailable("Mock mode") },
                    Memory = new MemoryMetrics { TotalBytes = 32L << 30, AvailableBytes = 18L << 30 },
                    Gpus = [new GpuMetrics { Name = "MOCK GPU", Usage = Reading.Of(60 + (30 * Math.Sin(t / 2)), "MOCK"), MemoryTotalBytes = 12L << 30, MemoryUsedBytes = 6L << 30 }],
                    Frames = new FrameMetrics { ProcessName = "mock", Source = "MOCK", Fps = 144 + (10 * Math.Sin(t)), FrameTimeMs = 6.9, Window = new FrameStatistics { FrameCount = 1000, AverageFps = 144, OnePercentLowFps = 110, PointOnePercentLowFps = 95 } },
                });
            }
        });
        return cts;
#else
        ArgumentNullException.ThrowIfNull(sink);
        throw new InvalidOperationException("Mock mode is not compiled into this build.");
#endif
    }
}
