namespace StormOS.Core.Frames;

/// <summary>Computes <see cref="FrameStatistics"/> from raw frame samples.</summary>
public static class FrameStatisticsCalculator
{
    /// <summary>Frames longer than this multiple of the rolling median are counted as stutter.</summary>
    public const double StutterThresholdFactor = 2.0;

    /// <summary>Window size of the rolling median used for stutter detection.</summary>
    public const int StutterWindow = 20;

    /// <summary>Frame times outside this range (ms) are discarded as capture artefacts.</summary>
    public const double MaxPlausibleFrameTimeMs = 5000;

    /// <summary>Computes statistics for the given samples.</summary>
    /// <param name="samples">Frame samples in presentation order.</param>
    /// <returns>Computed statistics, or <see cref="FrameStatistics.Empty"/> when there is no usable data.</returns>
    public static FrameStatistics Compute(IReadOnlyList<FrameSample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);

        var frameTimes = new List<double>(samples.Count);
        double cpuSum = 0, gpuSum = 0;
        int cpuCount = 0, gpuCount = 0, dropped = 0, droppedKnown = 0;
        foreach (var sample in samples)
        {
            if (sample.FrameTimeMs is <= 0 or > MaxPlausibleFrameTimeMs || !double.IsFinite(sample.FrameTimeMs))
            {
                continue;
            }

            frameTimes.Add(sample.FrameTimeMs);
            if (sample.CpuBusyMs is { } cpu && double.IsFinite(cpu))
            {
                cpuSum += cpu;
                cpuCount++;
            }

            if (sample.GpuBusyMs is { } gpu && double.IsFinite(gpu))
            {
                gpuSum += gpu;
                gpuCount++;
            }

            if (sample.Dropped is { } isDropped)
            {
                droppedKnown++;
                if (isDropped)
                {
                    dropped++;
                }
            }
        }

        if (frameTimes.Count == 0)
        {
            return FrameStatistics.Empty;
        }

        var totalMs = frameTimes.Sum();
        var mean = totalMs / frameTimes.Count;
        var variance = frameTimes.Sum(t => (t - mean) * (t - mean)) / frameTimes.Count;
        var sorted = frameTimes.ToArray();
        Array.Sort(sorted);

        var p99 = Percentile(sorted, 99);
        var p999 = Percentile(sorted, 99.9);

        return new FrameStatistics
        {
            FrameCount = frameTimes.Count,
            DurationSeconds = totalMs / 1000.0,
            AverageFps = 1000.0 * frameTimes.Count / totalMs,
            MinFps = 1000.0 / sorted[^1],
            MaxFps = 1000.0 / sorted[0],
            OnePercentLowFps = 1000.0 / p99,
            PointOnePercentLowFps = 1000.0 / p999,
            AverageFrameTimeMs = mean,
            MedianFrameTimeMs = Percentile(sorted, 50),
            P99FrameTimeMs = p99,
            P999FrameTimeMs = p999,
            FrameTimeStdDevMs = Math.Sqrt(variance),
            StutterCount = CountStutters(frameTimes),
            AverageCpuBusyMs = cpuCount > 0 ? cpuSum / cpuCount : null,
            AverageGpuBusyMs = gpuCount > 0 ? gpuSum / gpuCount : null,
            DroppedFrames = droppedKnown > 0 ? dropped : null,
        };
    }

    /// <summary>Computes a percentile from an ascending sorted array using linear interpolation.</summary>
    /// <param name="sortedAscending">Values sorted ascending.</param>
    /// <param name="percentile">Percentile in the range 0–100.</param>
    /// <returns>The interpolated percentile value.</returns>
    public static double Percentile(IReadOnlyList<double> sortedAscending, double percentile)
    {
        ArgumentNullException.ThrowIfNull(sortedAscending);
        if (sortedAscending.Count == 0)
        {
            throw new ArgumentException("At least one value is required.", nameof(sortedAscending));
        }

        percentile = Math.Clamp(percentile, 0, 100);
        var rank = percentile / 100.0 * (sortedAscending.Count - 1);
        var lower = (int)Math.Floor(rank);
        var upper = (int)Math.Ceiling(rank);
        if (lower == upper)
        {
            return sortedAscending[lower];
        }

        var fraction = rank - lower;
        return sortedAscending[lower] + ((sortedAscending[upper] - sortedAscending[lower]) * fraction);
    }

    /// <summary>Computes the FPS over the most recent <paramref name="windowSeconds"/> seconds.</summary>
    /// <param name="samples">Frame samples in presentation order.</param>
    /// <param name="windowSeconds">Window length in seconds.</param>
    /// <returns>Frames per second in the window, or 0 without data.</returns>
    public static double RecentFps(IReadOnlyList<FrameSample> samples, double windowSeconds = 1.0)
    {
        ArgumentNullException.ThrowIfNull(samples);
        double elapsedMs = 0;
        var frames = 0;
        for (var i = samples.Count - 1; i >= 0 && elapsedMs < windowSeconds * 1000; i--)
        {
            var ft = samples[i].FrameTimeMs;
            if (ft is <= 0 or > MaxPlausibleFrameTimeMs)
            {
                continue;
            }

            elapsedMs += ft;
            frames++;
        }

        return elapsedMs > 0 ? 1000.0 * frames / elapsedMs : 0;
    }

    private static int CountStutters(List<double> frameTimes)
    {
        if (frameTimes.Count < StutterWindow)
        {
            return 0;
        }

        var window = new double[StutterWindow];
        var stutters = 0;
        for (var i = StutterWindow; i < frameTimes.Count; i++)
        {
            frameTimes.CopyTo(i - StutterWindow, window, 0, StutterWindow);
            Array.Sort(window);
            var median = (window[(StutterWindow / 2) - 1] + window[StutterWindow / 2]) / 2;
            if (frameTimes[i] > median * StutterThresholdFactor)
            {
                stutters++;
            }
        }

        return stutters;
    }
}
