using StormOS.Core.Frames;

namespace StormOS.Core.Tests;

public sealed class FrameStatisticsTests
{
    [Fact]
    public void Compute_ConstantFrameTimes_ProducesExactFps()
    {
        var samples = Enumerable.Range(0, 1000).Select(i => new FrameSample(i * 0.004, 4.0)).ToList();

        var stats = FrameStatisticsCalculator.Compute(samples);

        Assert.Equal(1000, stats.FrameCount);
        Assert.Equal(250, stats.AverageFps, 6);
        Assert.Equal(250, stats.OnePercentLowFps, 6);
        Assert.Equal(250, stats.PointOnePercentLowFps, 6);
        Assert.Equal(4.0, stats.AverageFrameTimeMs, 6);
        Assert.Equal(0, stats.StutterCount);
    }

    [Fact]
    public void Compute_WithSpikes_LowsReflectSlowFrames()
    {
        var samples = new List<FrameSample>();
        for (var i = 0; i < 1000; i++)
        {
            samples.Add(new FrameSample(i, i % 50 == 0 ? 20.0 : 5.0));
        }

        var stats = FrameStatisticsCalculator.Compute(samples);

        Assert.Equal(50, stats.OnePercentLowFps, 6);
        Assert.True(stats.OnePercentLowFps < stats.AverageFps);
        Assert.Equal(50, stats.MinFps, 6);
        Assert.Equal(200, stats.MaxFps, 6);
        Assert.True(stats.StutterCount >= 9);
    }

    [Fact]
    public void Compute_DiscardsImplausibleFrames()
    {
        FrameSample[] samples = [new(0, -1), new(0, 0), new(0, double.NaN), new(0, 10_000), new(0, 10)];

        var stats = FrameStatisticsCalculator.Compute(samples);

        Assert.Equal(1, stats.FrameCount);
        Assert.Equal(100, stats.AverageFps, 6);
    }

    [Fact]
    public void Compute_Empty_ReturnsEmpty()
    {
        Assert.Same(FrameStatistics.Empty, FrameStatisticsCalculator.Compute([]));
    }

    [Fact]
    public void Compute_AggregatesOptionalProviderMetrics()
    {
        FrameSample[] samples = [new(0, 10, 4, 6, false), new(0, 10, 6, 8, true), new(0, 10)];

        var stats = FrameStatisticsCalculator.Compute(samples);

        Assert.Equal(5, stats.AverageCpuBusyMs);
        Assert.Equal(7, stats.AverageGpuBusyMs);
        Assert.Equal(1, stats.DroppedFrames);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(50, 3)]
    [InlineData(100, 5)]
    [InlineData(25, 2)]
    public void Percentile_InterpolatesLinearly(double percentile, double expected)
    {
        Assert.Equal(expected, FrameStatisticsCalculator.Percentile([1, 2, 3, 4, 5], percentile), 6);
    }

    [Fact]
    public void RecentFps_UsesOnlyLatestWindow()
    {
        var samples = new List<FrameSample>();
        samples.AddRange(Enumerable.Repeat(new FrameSample(0, 20), 100));
        samples.AddRange(Enumerable.Repeat(new FrameSample(0, 5), 400));

        Assert.Equal(200, FrameStatisticsCalculator.RecentFps(samples), 6);
    }
}
