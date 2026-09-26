using StormOS.Core.Benchmark;
using StormOS.Core.History;
using StormOS.Core.Optimization;
using StormOS.Core.Settings;
using StormOS.Infrastructure.Persistence;
using StormOS.Infrastructure.Settings;

namespace StormOS.Infrastructure.Tests;

public sealed class SqliteStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "storm-tests-" + Guid.NewGuid().ToString("N"));
    private readonly SqliteDatabase _database;

    public SqliteStoreTests()
    {
        _database = new SqliteDatabase(Path.Combine(_directory, "storm.db"));
    }

    public void Dispose()
    {
        _database.Dispose();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task Migrations_ApplyOnce()
    {
        await _database.InitializeAsync(TestContext.Current.CancellationToken);
        await using var connection = await _database.OpenAsync(TestContext.Current.CancellationToken);
        Assert.Equal(SchemaMigrations.LatestVersion, await SchemaMigrations.GetVersionAsync(connection, TestContext.Current.CancellationToken));
        await SchemaMigrations.ApplyAsync(connection, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task HistoryEvents_FilterAndOrder()
    {
        var store = new SqliteHistoryStore(_database);
        var now = DateTimeOffset.UtcNow;
        await store.AddEventAsync(new HistoryEvent { Timestamp = now.AddMinutes(-2), Category = HistoryCategory.Benchmark, Action = "old", Result = EventResult.Success }, TestContext.Current.CancellationToken);
        await store.AddEventAsync(new HistoryEvent { Timestamp = now, Category = HistoryCategory.Benchmark, Action = "new", Result = EventResult.Success }, TestContext.Current.CancellationToken);
        await store.AddEventAsync(new HistoryEvent { Timestamp = now, Category = HistoryCategory.Session, Action = "session", Result = EventResult.Info }, TestContext.Current.CancellationToken);

        var benchmarks = await store.ListEventsAsync(HistoryCategory.Benchmark, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["new", "old"], benchmarks.Select(e => e.Action));
        Assert.Equal(3, (await store.ListEventsAsync(cancellationToken: TestContext.Current.CancellationToken)).Count);
    }

    [Fact]
    public async Task Benchmarks_RoundTrip()
    {
        var store = new SqliteHistoryStore(_database);
        var result = new BenchmarkResult { Id = Guid.NewGuid(), Type = BenchmarkType.Cpu, StartedAt = DateTimeOffset.UtcNow, Completed = true, Metrics = [new("cpu.multi.mops", "Multi", 1234.5, "MOPS")] };

        await store.SaveBenchmarkAsync(result, TestContext.Current.CancellationToken);
        var loaded = Assert.Single(await store.ListBenchmarksAsync(BenchmarkType.Cpu, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(1234.5, loaded.Metric("cpu.multi.mops")!.Value);
        Assert.Empty(await store.ListBenchmarksAsync(BenchmarkType.Gpu, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MetricHistory_RangeAndPrune()
    {
        var store = new SqliteHistoryStore(_database);
        var start = DateTimeOffset.UtcNow.AddHours(-1);
        var points = Enumerable.Range(0, 60).Select(i => new MetricHistoryPoint { Timestamp = start.AddMinutes(i), Cpu = i, Fps = i % 2 == 0 ? null : 100 }).ToList();

        await store.AppendMetricHistoryAsync(points, TestContext.Current.CancellationToken);
        var range = await store.ReadMetricHistoryAsync(start.AddMinutes(10), start.AddMinutes(19), TestContext.Current.CancellationToken);
        Assert.Equal(10, range.Count);
        Assert.Null(range[0].Fps);

        await store.PruneAsync(start.AddMinutes(30), TestContext.Current.CancellationToken);
        Assert.Equal(30, (await store.ReadMetricHistoryAsync(start, start.AddHours(2), TestContext.Current.CancellationToken)).Count);
    }

    [Fact]
    public async Task OptimizationJournal_ActiveRecords()
    {
        var journal = new SqliteOptimizationJournal(_database);
        var active = new OptimizationRecord { Id = Guid.NewGuid(), Timestamp = DateTimeOffset.UtcNow, RuleId = "a", Rollback = RollbackStatus.Available, Snapshot = new RuleSnapshot { RuleId = "a", Values = new Dictionary<string, string?> { ["v"] = "1" } } };
        var restored = active with { Id = Guid.NewGuid(), Rollback = RollbackStatus.RolledBack };

        await journal.SaveAsync(active, TestContext.Current.CancellationToken);
        await journal.SaveAsync(restored, TestContext.Current.CancellationToken);

        var list = await journal.ListActiveAsync(TestContext.Current.CancellationToken);
        Assert.Equal(active.Id, Assert.Single(list).Id);
        Assert.Equal("1", (await journal.GetAsync(active.Id, TestContext.Current.CancellationToken))!.Snapshot!.Get("v"));
    }

    [Fact]
    public async Task Settings_DefaultsAreDataMinimal_AndPersist()
    {
        var store = new SqliteSettingsStore(_database);
        var defaults = await store.LoadAsync(TestContext.Current.CancellationToken);
        Assert.False(defaults.Privacy.Telemetry);
        Assert.False(defaults.Privacy.CloudSync);
        Assert.False(defaults.Privacy.CrashReports);
        Assert.True(defaults.Privacy.PerformanceHistory);

        StormSettings? changed = null;
        store.Changed += (_, s) => changed = s;
        await store.SaveAsync(defaults with { Overlay = defaults.Overlay with { Opacity = 0.5 } }, TestContext.Current.CancellationToken);

        Assert.NotNull(changed);
        var reloaded = await new SqliteSettingsStore(_database).LoadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0.5, reloaded.Overlay.Opacity);
    }
}
