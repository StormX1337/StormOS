using StormOS.Core.Benchmark;
using StormOS.Core.Frames;

namespace StormOS.Core.Tests;

public sealed class BenchmarkComparerTests
{
    private static BenchmarkResult Gaming(double avg, double low) => new()
    {
        Type = BenchmarkType.Gaming,
        Completed = true,
        Frames = new FrameStatistics { FrameCount = 1000, AverageFps = avg, OnePercentLowFps = low, PointOnePercentLowFps = low * 0.8, AverageFrameTimeMs = 1000 / avg },
    };

    [Fact]
    public void Compare_ComputesPercentDelta()
    {
        var comparison = BenchmarkComparer.Compare(Gaming(312, 184), Gaming(327, 201));

        Assert.NotNull(comparison);
        var avg = comparison!.Deltas.Single(d => d.Key == "fps.avg");
        Assert.Equal(4.8, avg.DeltaPercent, 1);
        Assert.True(avg.Improved);
        var frametime = comparison.Deltas.Single(d => d.Key == "frametime.avg");
        Assert.True(frametime.Improved);
        Assert.True(frametime.DeltaPercent < 0);
    }

    [Fact]
    public void Compare_DifferentTypes_ReturnsNull()
    {
        var cpu = new BenchmarkResult { Type = BenchmarkType.Cpu, Completed = true };

        Assert.Null(BenchmarkComparer.Compare(cpu, Gaming(1, 1)));
    }

    [Fact]
    public void Compare_IncompleteRun_ReturnsNull()
    {
        Assert.Null(BenchmarkComparer.Compare(Gaming(100, 80) with { Completed = false }, Gaming(110, 90)));
    }

    [Fact]
    public void Compare_OnlyMetricsPresentInBoth()
    {
        var before = new BenchmarkResult { Type = BenchmarkType.Cpu, Completed = true, Metrics = [new("a", "A", 10, "x"), new("b", "B", 10, "x")] };
        var after = new BenchmarkResult { Type = BenchmarkType.Cpu, Completed = true, Metrics = [new("a", "A", 12, "x")] };

        var comparison = BenchmarkComparer.Compare(before, after)!;

        Assert.Single(comparison.Deltas);
        Assert.Equal(20, comparison.Deltas[0].DeltaPercent, 6);
    }
}
