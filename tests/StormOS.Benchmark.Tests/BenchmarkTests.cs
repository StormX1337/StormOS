using Microsoft.Extensions.Logging.Abstractions;
using StormOS.Benchmark.Engine;
using StormOS.Benchmark.Workloads;
using StormOS.Core.Benchmark;
using StormOS.Core.Hardware;
using StormOS.Core.History;
using StormOS.Core.Network;

namespace StormOS.Benchmark.Tests;

public class WorkloadMathTests
{
    [Fact]
    public void CpuKernelIsDeterministicAndSeedDependent()
    {
        Assert.Equal(CpuBenchmark.Kernel(42, 10_000), CpuBenchmark.Kernel(42, 10_000));
        Assert.NotEqual(CpuBenchmark.Kernel(42, 10_000), CpuBenchmark.Kernel(1001, 10_000));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(17)]
    [InlineData(4096)]
    public void BuildCycleVisitsEveryElementExactlyOnce(int length)
    {
        var next = MemoryBenchmark.BuildCycle(length);
        var visited = new bool[length];
        var index = 0;
        for (var step = 0; step < length; step++)
        {
            Assert.False(visited[index]);
            visited[index] = true;
            index = next[index];
        }

        Assert.Equal(0, index);
        Assert.All(visited, Assert.True);
    }

    [Fact]
    public void GflopsHandlesZeroTime()
    {
        Assert.Equal(0, GpuBenchmark.Gflops(1024, 100, 0));
        Assert.True(GpuBenchmark.Gflops(1_048_576, 1024, 0.01) > 0);
    }
}

public class BenchmarkEngineTests
{
    [Fact]
    public async Task CompletedRunIsScoredAndStored()
    {
        var history = new RecordingHistory();
        using var engine = Create(history, new FakeBenchmark(BenchmarkType.Cpu, [new("cpu.single.mops", "Single", 1200, "MOPS"), new("cpu.multi.mops", "Multi", 8000, "MOPS")]));

        var result = await engine.RunAsync(BenchmarkType.Cpu, new BenchmarkRunOptions { Label = "before" }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Completed);
        Assert.Equal("before", result.Label);
        Assert.Contains("Test CPU", result.Hardware, StringComparison.Ordinal);
        Assert.NotNull(result.Score);
        Assert.Equal((100 * 0.45) + (50 * 0.55), result.Score.Score!.Value, 1);
        Assert.Single(history.Benchmarks);
        Assert.Single(history.Events);
    }

    [Fact]
    public async Task UnavailableBenchmarkIsReportedNotFaked()
    {
        var history = new RecordingHistory();
        using var engine = Create(history, new FakeBenchmark(BenchmarkType.Gpu, [], unavailable: "No Direct3D 11 GPU."));

        var result = await engine.RunAsync(BenchmarkType.Gpu, new BenchmarkRunOptions(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.Completed);
        Assert.Equal("No Direct3D 11 GPU.", result.Error);
        Assert.Empty(result.Metrics);
        Assert.Null(result.Score);
        Assert.Equal(EventResult.Failed, history.Events.Single().Result);
    }

    [Fact]
    public async Task FailingWorkloadProducesFailedResult()
    {
        using var engine = Create(new RecordingHistory(), new FakeBenchmark(BenchmarkType.Disk, [], throws: new IOException("disk full")));

        var result = await engine.RunAsync(BenchmarkType.Disk, new BenchmarkRunOptions(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.Completed);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task OnlyOneBenchmarkRunsAtATime()
    {
        var gate = new TaskCompletionSource();
        using var engine = Create(new RecordingHistory(), new FakeBenchmark(BenchmarkType.Cpu, [new("cpu.single.mops", "Single", 1, "MOPS")], wait: gate.Task));

        var first = engine.RunAsync(BenchmarkType.Cpu, new BenchmarkRunOptions(), cancellationToken: TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.RunAsync(BenchmarkType.Cpu, new BenchmarkRunOptions(), cancellationToken: TestContext.Current.CancellationToken));
        gate.SetResult();
        Assert.True((await first).Completed);
    }

    [Fact]
    public void ComparisonNeedsTwoCompletedRunsOfTheSameType()
    {
        var before = new BenchmarkResult { Type = BenchmarkType.Memory, Completed = true, Metrics = [new("memory.latency.ns", "Latency", 80, "ns", HigherIsBetter: false)] };
        var after = before with { Metrics = [new("memory.latency.ns", "Latency", 70, "ns", HigherIsBetter: false)] };

        var comparison = BenchmarkComparer.Compare(before, after);

        Assert.NotNull(comparison);
        var delta = Assert.Single(comparison.Deltas);
        Assert.True(delta.Improved);
        Assert.Equal(-12.5, delta.DeltaPercent, 3);
        Assert.Null(BenchmarkComparer.Compare(before, after with { Completed = false }));
        Assert.Null(BenchmarkComparer.Compare(before, after with { Type = BenchmarkType.Cpu }));
    }

    private static BenchmarkEngine Create(IHistoryStore history, params IBenchmark[] benchmarks) =>
        new(benchmarks, new FakeInventory(), NullLogger<BenchmarkEngine>.Instance, history: history);

    private sealed class FakeBenchmark(BenchmarkType type, IReadOnlyList<BenchmarkMetric> metrics, string? unavailable = null, Exception? throws = null, Task? wait = null) : IBenchmark
    {
        public BenchmarkType Type => type;

        public string Name => $"Fake {type}";

        public string? CheckAvailability() => unavailable;

        public async Task<IReadOnlyList<BenchmarkMetric>> RunAsync(BenchmarkRunOptions options, IProgress<BenchmarkProgress>? progress, CancellationToken cancellationToken)
        {
            if (wait is not null)
            {
                await wait.WaitAsync(cancellationToken);
            }

            return throws is not null ? throw throws : metrics;
        }
    }

    private sealed class FakeInventory : IHardwareInventoryProvider
    {
        public Task<HardwareInventory> GetInventoryAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new HardwareInventory { Cpu = new CpuInfo { Name = "Test CPU" }, Memory = new MemoryInfo { TotalBytes = 16L << 30 } });
    }
}

internal sealed class RecordingHistory : IHistoryStore
{
    public List<BenchmarkResult> Benchmarks { get; } = [];

    public List<HistoryEvent> Events { get; } = [];

    public Task AddEventAsync(HistoryEvent historyEvent, CancellationToken cancellationToken = default)
    {
        Events.Add(historyEvent);
        return Task.CompletedTask;
    }

    public Task SaveBenchmarkAsync(BenchmarkResult result, CancellationToken cancellationToken = default)
    {
        Benchmarks.Add(result);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<HistoryEvent>> ListEventsAsync(HistoryCategory? category = null, int limit = 200, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<HistoryEvent>>(Events);

    public Task SaveSessionAsync(GameSession session, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<IReadOnlyList<GameSession>> ListSessionsAsync(string? gameId = null, int limit = 100, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<GameSession>>([]);

    public Task<IReadOnlyList<BenchmarkResult>> ListBenchmarksAsync(BenchmarkType? type = null, string? gameId = null, int limit = 100, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<BenchmarkResult>>(Benchmarks);

    public Task SaveNetworkReportAsync(NetworkDiagnosticsReport report, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<IReadOnlyList<NetworkDiagnosticsReport>> ListNetworkReportsAsync(int limit = 50, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<NetworkDiagnosticsReport>>([]);

    public Task AppendMetricHistoryAsync(IReadOnlyList<MetricHistoryPoint> points, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<IReadOnlyList<MetricHistoryPoint>> ReadMetricHistoryAsync(DateTimeOffset rangeStart, DateTimeOffset rangeEnd, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<MetricHistoryPoint>>([]);

    public Task<int> PruneAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default) => Task.FromResult(0);
}
