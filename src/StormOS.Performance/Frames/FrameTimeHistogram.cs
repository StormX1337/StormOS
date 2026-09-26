namespace StormOS.Performance.Frames;

/// <summary>
/// Constant-memory frame time histogram used for long sessions (0.1 ms resolution up to 250 ms).
/// Percentiles are accurate to the bin width.
/// </summary>
public sealed class FrameTimeHistogram
{
    /// <summary>Bin width in milliseconds.</summary>
    public const double BinWidthMs = 0.1;

    /// <summary>Upper bound of the regular bins in milliseconds.</summary>
    public const double MaxMs = 250;

    private readonly long[] _bins = new long[(int)(MaxMs / BinWidthMs) + 1];

    /// <summary>Gets the number of frames.</summary>
    public long Count { get; private set; }

    /// <summary>Gets the total frame time in milliseconds.</summary>
    public double TotalMs { get; private set; }

    /// <summary>Gets the average FPS, or <see langword="null"/> without frames.</summary>
    public double? AverageFps => Count > 0 && TotalMs > 0 ? 1000.0 * Count / TotalMs : null;

    /// <summary>Gets the average frame time in milliseconds.</summary>
    public double? AverageFrameTimeMs => Count > 0 ? TotalMs / Count : null;

    /// <summary>Adds a frame time.</summary>
    /// <param name="frameTimeMs">Frame time in milliseconds.</param>
    public void Add(double frameTimeMs)
    {
        if (frameTimeMs <= 0 || !double.IsFinite(frameTimeMs) || frameTimeMs > Core.Frames.FrameStatisticsCalculator.MaxPlausibleFrameTimeMs)
        {
            return;
        }

        var index = Math.Min(_bins.Length - 1, (int)(frameTimeMs / BinWidthMs));
        _bins[index]++;
        Count++;
        TotalMs += frameTimeMs;
    }

    /// <summary>Returns the frame time at a percentile (upper edge of the containing bin).</summary>
    /// <param name="percentile">Percentile 0–100.</param>
    /// <returns>Frame time in milliseconds, or <see langword="null"/> without frames.</returns>
    public double? PercentileMs(double percentile)
    {
        if (Count == 0)
        {
            return null;
        }

        var target = (long)Math.Ceiling(Math.Clamp(percentile, 0, 100) / 100.0 * Count);
        long cumulative = 0;
        for (var i = 0; i < _bins.Length; i++)
        {
            cumulative += _bins[i];
            if (cumulative >= Math.Max(1, target))
            {
                return (i + 1) * BinWidthMs;
            }
        }

        return MaxMs;
    }

    /// <summary>Resets the histogram.</summary>
    public void Clear()
    {
        Array.Clear(_bins);
        Count = 0;
        TotalMs = 0;
    }
}
