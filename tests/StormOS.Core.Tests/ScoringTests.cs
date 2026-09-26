using StormOS.Core.Benchmark;
using StormOS.Core.Frames;
using StormOS.Core.Network;
using StormOS.Core.Scoring;

namespace StormOS.Core.Tests;

public sealed class ScoringTests
{
    [Fact]
    public void WeightedScore_ExcludesUnmeasuredInputs()
    {
        ScoreInput[] inputs =
        [
            new("A", 1, "x", 80, 0.5, "a"),
            new("B", 1, "x", 40, 0.25, "b"),
            new("C", null, "x", null, 0.25, "c"),
        ];

        var score = WeightedScore.Compute("Test", inputs);

        Assert.Equal((80 * 0.5 + 40 * 0.25) / 0.75, score.Score!.Value, 1);
        Assert.Equal(0.75, score.Coverage, 6);
    }

    [Fact]
    public void WeightedScore_InsufficientCoverage_ProducesNoScore()
    {
        ScoreInput[] inputs = [new("A", 1, "x", 100, 0.3, "a"), new("B", null, "x", null, 0.7, "b")];

        var score = WeightedScore.Compute("Test", inputs);

        Assert.Null(score.Score);
        Assert.Equal("Insufficient data", score.Rating);
    }

    [Theory]
    [InlineData(10, 100)]
    [InlineData(150, 0)]
    [InlineData(80, 50)]
    [InlineData(5, 100)]
    [InlineData(500, 0)]
    public void LowerIsBetter_ClampsAndInterpolates(double value, double expected)
    {
        Assert.Equal(expected, WeightedScore.LowerIsBetter(value, 10, 150), 6);
    }

    [Fact]
    public void NetworkScore_PerfectConnection_Scores100()
    {
        var ping = new PingStatistics { Sent = 10, Received = 10, AverageMs = 5, JitterMs = 0.5 };
        DnsTestResult[] dns = [new() { Server = "1.1.1.1", Success = true, LatencyMs = 8, IsConfigured = true }];

        var score = StormScores.Network(ping, dns);

        Assert.Equal(100, score.Score);
        Assert.Equal(4, score.Inputs.Count);
    }

    [Fact]
    public void NetworkScore_Offline_UsesOnlyLoss()
    {
        var ping = new PingStatistics { Sent = 10, Received = 0 };

        var score = StormScores.Network(ping, []);

        Assert.Null(score.Score);
        Assert.Equal(100, score.Inputs.Single(i => i.Name == "Packet loss").RawValue);
    }

    [Fact]
    public void GamingScore_UsesRefreshRateAndConsistency()
    {
        var frames = new FrameStatistics { FrameCount = 1000, AverageFps = 144, OnePercentLowFps = 120, StutterCount = 0 };

        var score = StormScores.Gaming(frames, 144);

        Assert.Equal(100, score.Score);
    }

    [Fact]
    public void GamingScore_WithoutFrames_HasNoScore()
    {
        Assert.Null(StormScores.Gaming(null, 144).Score);
    }

    [Fact]
    public void PerformanceScore_OnlyMeasuredBenchmarks()
    {
        var cpu = new BenchmarkResult
        {
            Type = BenchmarkType.Cpu,
            Completed = true,
            Metrics = [new("cpu.single.mops", "Single", StormScores.CpuSingleReference, "MOPS"), new("cpu.multi.mops", "Multi", StormScores.CpuMultiReference / 2, "MOPS")],
        };
        var gpu = new BenchmarkResult { Type = BenchmarkType.Gpu, Completed = true, Metrics = [new("gpu.compute.gflops", "GPU", StormScores.GpuComputeReference, "GFLOPS")] };

        var score = StormScores.Performance(cpu, null, null, gpu);

        Assert.NotNull(score.Score);
        Assert.Equal((100 * 0.20 + 50 * 0.25 + 100 * 0.25) / 0.70, score.Score!.Value, 1);
    }

    [Fact]
    public void SystemHealth_ComputesFromMeasuredInputs()
    {
        var score = StormScores.SystemHealth(new SystemHealthInputs { MemoryUsagePercent = 40, SystemDriveFreePercent = 50, EnabledStartupEntries = 3, ProcessCount = 100 });

        Assert.Equal(100, score.Score);
        Assert.Equal(0.7, score.Coverage, 6);
    }
}
