namespace StormOS.Core.Frames;

/// <summary>
/// Aggregated frame statistics. Percentile lows follow the common definition used by PresentMon based
/// tools: the "1% low" is the frame rate equivalent of the 99th percentile frame time.
/// </summary>
public sealed record FrameStatistics
{
    /// <summary>Gets the number of frames analysed.</summary>
    public int FrameCount { get; init; }

    /// <summary>Gets the capture duration in seconds (sum of frame times).</summary>
    public double DurationSeconds { get; init; }

    /// <summary>Gets the average FPS (frames divided by elapsed time).</summary>
    public double AverageFps { get; init; }

    /// <summary>Gets the minimum instantaneous FPS (slowest frame).</summary>
    public double MinFps { get; init; }

    /// <summary>Gets the maximum instantaneous FPS (fastest frame).</summary>
    public double MaxFps { get; init; }

    /// <summary>Gets the 1% low FPS (99th percentile frame time).</summary>
    public double OnePercentLowFps { get; init; }

    /// <summary>Gets the 0.1% low FPS (99.9th percentile frame time).</summary>
    public double PointOnePercentLowFps { get; init; }

    /// <summary>Gets the average frame time in milliseconds.</summary>
    public double AverageFrameTimeMs { get; init; }

    /// <summary>Gets the median frame time in milliseconds.</summary>
    public double MedianFrameTimeMs { get; init; }

    /// <summary>Gets the 99th percentile frame time in milliseconds.</summary>
    public double P99FrameTimeMs { get; init; }

    /// <summary>Gets the 99.9th percentile frame time in milliseconds.</summary>
    public double P999FrameTimeMs { get; init; }

    /// <summary>Gets the frame time standard deviation in milliseconds.</summary>
    public double FrameTimeStdDevMs { get; init; }

    /// <summary>Gets the number of stutter frames (frame time more than twice the rolling median).</summary>
    public int StutterCount { get; init; }

    /// <summary>Gets the average CPU busy time per frame, when reported by the provider.</summary>
    public double? AverageCpuBusyMs { get; init; }

    /// <summary>Gets the average GPU busy time per frame, when reported by the provider.</summary>
    public double? AverageGpuBusyMs { get; init; }

    /// <summary>Gets the number of dropped (never displayed) frames, when reported by the provider.</summary>
    public int? DroppedFrames { get; init; }

    /// <summary>Gets an empty statistics instance.</summary>
    public static FrameStatistics Empty { get; } = new();
}

/// <summary>Live frame metrics published with telemetry snapshots.</summary>
public sealed record FrameMetrics
{
    /// <summary>Gets the captured process id.</summary>
    public int ProcessId { get; init; }

    /// <summary>Gets the captured process name.</summary>
    public string ProcessName { get; init; } = string.Empty;

    /// <summary>Gets the capture provider name, for example "PresentMon" or "ETW (DXGI)".</summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>Gets the instantaneous FPS over the last second.</summary>
    public double Fps { get; init; }

    /// <summary>Gets the latest frame time in milliseconds.</summary>
    public double FrameTimeMs { get; init; }

    /// <summary>Gets statistics over the whole capture window.</summary>
    public FrameStatistics Window { get; init; } = FrameStatistics.Empty;
}
