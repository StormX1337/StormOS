using StormOS.Core.Common;
using StormOS.Core.Frames;
using StormOS.Core.Telemetry;
using StormOS.Performance.Collections;
using StormOS.Performance.Frames;
using StormOS.Performance.Sessions;

namespace StormOS.Performance.Tests;

public class PresentMonCsvParserTests
{
    [Fact]
    public void ParsesPresentMon1Rows()
    {
        var parser = new PresentMonCsvParser();
        Assert.True(parser.TryParseHeader("Application,ProcessID,SwapChainAddress,Runtime,SyncInterval,PresentFlags,Dropped,TimeInSeconds,MsBetweenPresents,MsUntilDisplayed"));

        Assert.True(parser.TryParseRow("cs2.exe,4242,0x1,DXGI,0,0,0,1.5,6.944,10.1", out var sample, out var pid));
        Assert.Equal(4242, pid);
        Assert.Equal(6.944, sample.FrameTimeMs, 3);
        Assert.Equal(1.5, sample.TimestampSeconds);
        Assert.False(sample.Dropped);

        Assert.True(parser.TryParseRow("cs2.exe,4242,0x1,DXGI,0,0,1,1.51,7.0,NA", out sample, out _));
        Assert.True(sample.Dropped);
    }

    [Fact]
    public void ParsesPresentMon2RowsFromCpuStartTimeDifferences()
    {
        var parser = new PresentMonCsvParser();
        Assert.True(parser.TryParseHeader("Application,ProcessID,SwapChainAddress,PresentRuntime,CPUStartTime,MsCPUBusy,MsCPUWait,MsGPUBusy,MsUntilDisplayed"));

        Assert.True(parser.TryParseRow("game.exe,10,0x1,DXGI,1000.0,4.0,2.0,5.5,9.0", out var first, out _));
        Assert.Equal(6.0, first.FrameTimeMs);
        Assert.Equal(5.5, first.GpuBusyMs);

        Assert.True(parser.TryParseRow("game.exe,10,0x1,DXGI,1008.0,5.0,2.0,5.5,9.0", out var second, out _));
        Assert.Equal(8.0, second.FrameTimeMs, 6);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not,a,valid,row")]
    [InlineData("game.exe,abc,0x1,DXGI,0,0,0,1.5,6.9,1")]
    [InlineData("game.exe,10,0x1,DXGI,0,0,0,1.5,-3,1")]
    public void RejectsInvalidRows(string row)
    {
        var parser = new PresentMonCsvParser();
        parser.TryParseHeader("Application,ProcessID,SwapChainAddress,Runtime,SyncInterval,PresentFlags,Dropped,TimeInSeconds,MsBetweenPresents,MsUntilDisplayed");

        Assert.False(parser.TryParseRow(row, out _, out _));
    }

    [Fact]
    public void RejectsHeaderWithoutTimingColumns()
    {
        var parser = new PresentMonCsvParser();
        Assert.False(parser.TryParseHeader("Application,ProcessID,Runtime"));
        Assert.False(parser.TryParseRow("x,1,DXGI", out _, out _));
    }
}

public class FrameTimeHistogramTests
{
    [Fact]
    public void ComputesAverageAndPercentilesInConstantMemory()
    {
        var histogram = new FrameTimeHistogram();
        for (var i = 0; i < 990; i++)
        {
            histogram.Add(10.05);
        }

        for (var i = 0; i < 10; i++)
        {
            histogram.Add(40.05);
        }

        Assert.Equal(1000, histogram.Count);
        Assert.Equal(1000.0 * 1000 / ((990 * 10.05) + (10 * 40.05)), histogram.AverageFps!.Value, 6);
        Assert.Equal(10.1, histogram.PercentileMs(50)!.Value, 6);
        Assert.Equal(10.1, histogram.PercentileMs(99)!.Value, 6);
        Assert.Equal(40.1, histogram.PercentileMs(99.9)!.Value, 6);
    }

    [Fact]
    public void IgnoresImplausibleValuesAndReportsEmpty()
    {
        var histogram = new FrameTimeHistogram();
        histogram.Add(0);
        histogram.Add(-5);
        histogram.Add(double.NaN);

        Assert.Equal(0, histogram.Count);
        Assert.Null(histogram.AverageFps);
        Assert.Null(histogram.PercentileMs(99));
    }
}

public class SwapChainSelectorTests
{
    [Fact]
    public void ReportsFrameTimesOnlyForDominantSwapChain()
    {
        var selector = new SwapChainSelector();
        var results = new List<double>();
        for (var i = 0; i < 300; i++)
        {
            var t = i * 10.0;
            if (selector.OnPresent(1, t) is { } frame)
            {
                results.Add(frame);
            }

            if (i % 10 == 0)
            {
                Assert.Null(selector.OnPresent(2, t + 1));
            }
        }

        Assert.NotEmpty(results);
        Assert.All(results, r => Assert.Equal(10, r, 6));
    }
}

public class RingBufferTests
{
    [Fact]
    public void KeepsNewestItemsInOrder()
    {
        var buffer = new RingBuffer<int>(3);
        foreach (var i in Enumerable.Range(1, 5))
        {
            buffer.Add(i);
        }

        Assert.Equal(3, buffer.Count);
        Assert.Equal([3, 4, 5], buffer.ToArray());
        Assert.Equal([4, 5], buffer.TakeLast(2));
        Assert.Equal([3, 4, 5], buffer.TakeLast(10));

        buffer.Clear();
        Assert.Empty(buffer.ToArray());
    }

    [Fact]
    public void RejectsInvalidCapacity() => Assert.Throws<ArgumentOutOfRangeException>(() => new RingBuffer<int>(0));
}

public class MetricHistoryAggregateTests
{
    [Fact]
    public void AveragesMeasuredValuesAndKeepsMissingOnesMissing()
    {
        var start = DateTimeOffset.UnixEpoch;
        MetricsSnapshot Snapshot(int second, double cpu, Reading temp) => new()
        {
            Timestamp = start.AddSeconds(second),
            Cpu = new CpuMetrics { Usage = Reading.Of(cpu), TemperatureCelsius = temp },
            Memory = new MemoryMetrics { TotalBytes = 100, AvailableBytes = 40 },
            System = new SystemMetrics { LatencyMs = Reading.Unavailable("no network") },
        };

        var point = MetricHistoryRecorder.Aggregate(
        [
            Snapshot(1, 20, Reading.Of(60)),
            Snapshot(2, 40, Reading.Unavailable("no sensor")),
        ]);

        Assert.Equal(start.AddSeconds(2), point.Timestamp);
        Assert.Equal(30, point.Cpu);
        Assert.Equal(60, point.CpuTemp);
        Assert.Equal(60, point.Ram);
        Assert.Null(point.Gpu);
        Assert.Null(point.Fps);
        Assert.Null(point.LatencyMs);
    }

    [Fact]
    public void FrameStatisticsCalculatorAgreesWithHistogramAverage()
    {
        var samples = Enumerable.Range(0, 500).Select(i => new FrameSample(i * 0.01, i % 2 == 0 ? 8 : 12)).ToList();
        var stats = FrameStatisticsCalculator.Compute(samples);
        var histogram = new FrameTimeHistogram();
        samples.ForEach(s => histogram.Add(s.FrameTimeMs));

        Assert.Equal(stats.AverageFps, histogram.AverageFps!.Value, 6);
    }
}
