using System.Reflection;
using Microsoft.Extensions.Logging;
using StormOS.Core.Benchmark;
using StormOS.Core.Common;
using StormOS.Core.Hardware;
using StormOS.Core.History;
using StormOS.Core.Optimization;
using StormOS.Core.Scoring;
using StormOS.Core.Telemetry;

namespace StormOS.Benchmark.Engine;

/// <summary>
/// Runs benchmarks, records average CPU/GPU utilization during the run, attaches hardware metadata and a transparent
/// score, and stores the result in the local history.
/// </summary>
public sealed class BenchmarkEngine : IDisposable
{
    private readonly IReadOnlyDictionary<BenchmarkType, IBenchmark> _benchmarks;
    private readonly IHardwareInventoryProvider _inventory;
    private readonly ITelemetryHub? _telemetry;
    private readonly IHistoryStore? _history;
    private readonly ILogger<BenchmarkEngine> _logger;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _runLock = new(1, 1);

    /// <summary>Initializes a new instance of the <see cref="BenchmarkEngine"/> class.</summary>
    /// <param name="benchmarks">Available benchmarks.</param>
    /// <param name="inventory">Hardware inventory.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="telemetry">Telemetry hub for utilization sampling.</param>
    /// <param name="history">History store.</param>
    /// <param name="timeProvider">Time source.</param>
    public BenchmarkEngine(IEnumerable<IBenchmark> benchmarks, IHardwareInventoryProvider inventory, ILogger<BenchmarkEngine> logger, ITelemetryHub? telemetry = null, IHistoryStore? history = null, TimeProvider? timeProvider = null)
    {
        _benchmarks = benchmarks.ToDictionary(b => b.Type);
        _inventory = inventory;
        _logger = logger;
        _telemetry = telemetry;
        _history = history;
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Gets the available benchmark types with their availability.</summary>
    /// <returns>Type, name and unavailability reason.</returns>
    public IReadOnlyList<(BenchmarkType Type, string Name, string? Unavailable)> List() =>
        _benchmarks.Values.Select(b => (b.Type, b.Name, b.CheckAvailability())).ToList();

    /// <summary>Runs a benchmark.</summary>
    /// <param name="type">Benchmark type.</param>
    /// <param name="options">Options.</param>
    /// <param name="progress">Progress receiver.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The stored result (also for failed runs, with <see cref="BenchmarkResult.Completed"/> false).</returns>
    public async Task<BenchmarkResult> RunAsync(BenchmarkType type, BenchmarkRunOptions options, IProgress<BenchmarkProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!_benchmarks.TryGetValue(type, out var benchmark))
        {
            throw new ArgumentException($"No {type} benchmark is registered.", nameof(type));
        }

        if (!await _runLock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("Another benchmark is already running.");
        }

        try
        {
            var hardware = await DescribeHardwareAsync(cancellationToken).ConfigureAwait(false);
            var started = _time.GetUtcNow();
            var utilization = new UtilizationTracker();
            using var subscription = _telemetry?.Subscribe(utilization.Add);
            BenchmarkResult result;
            if (benchmark.CheckAvailability() is { } unavailable)
            {
                result = Failed(type, started, hardware, options, unavailable);
            }
            else
            {
                try
                {
                    var metrics = await benchmark.RunAsync(options, progress, cancellationToken).ConfigureAwait(false);
                    result = new BenchmarkResult
                    {
                        Id = Guid.NewGuid(),
                        Type = type,
                        StartedAt = started,
                        Duration = _time.GetUtcNow() - started,
                        Hardware = hardware,
                        Label = options.Label,
                        Settings = options.Settings,
                        GameId = options.GameId,
                        GameName = options.GameName,
                        Metrics = metrics,
                        AverageCpuUsage = utilization.AverageCpu,
                        AverageGpuUsage = utilization.AverageGpu,
                        Completed = true,
                        AppVersion = AppVersion,
                    };
                    result = result with { Score = Score(result) };
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or TimeoutException or System.Runtime.InteropServices.COMException or SharpGen.Runtime.SharpGenException or DllNotFoundException)
                {
                    _logger.LogWarning(ex, "{Benchmark} benchmark failed", benchmark.Name);
                    result = Failed(type, started, hardware, options, ex is InvalidOperationException ? ex.Message : $"The {benchmark.Name} benchmark could not run on this system.");
                }
            }

            await StoreAsync(result, cancellationToken).ConfigureAwait(false);
            return result;
        }
        finally
        {
            _runLock.Release();
        }
    }

    /// <summary>Stores an externally produced result (for example a gaming benchmark from the service).</summary>
    /// <param name="result">The result.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the operation.</returns>
    public Task StoreAsync(BenchmarkResult result, CancellationToken cancellationToken = default) => StoreCoreAsync(result, cancellationToken);

    /// <summary>Computes the score for a result, where a scoring model exists.</summary>
    /// <param name="result">The result.</param>
    /// <returns>The score or <see langword="null"/>.</returns>
    public static ScoreBreakdown? Score(BenchmarkResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.Completed ? StormScores.Component(result) : null;
    }

    /// <inheritdoc />
    public void Dispose() => _runLock.Dispose();

    private static string AppVersion => typeof(BenchmarkEngine).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

    private BenchmarkResult Failed(BenchmarkType type, DateTimeOffset started, string hardware, BenchmarkRunOptions options, string error) => new()
    {
        Id = Guid.NewGuid(),
        Type = type,
        StartedAt = started,
        Duration = _time.GetUtcNow() - started,
        Hardware = hardware,
        Label = options.Label,
        GameId = options.GameId,
        GameName = options.GameName,
        Completed = false,
        Error = error,
        AppVersion = AppVersion,
    };

    private async Task<string> DescribeHardwareAsync(CancellationToken cancellationToken)
    {
        try
        {
            var inventory = await _inventory.GetInventoryAsync(cancellationToken).ConfigureAwait(false);
            var gpu = inventory.Gpus.OrderByDescending(g => g.DedicatedMemoryBytes).FirstOrDefault()?.Name ?? "Unknown GPU";
            return $"{inventory.Cpu.Name} · {gpu} · {Units.FormatBytes(inventory.Memory.TotalBytes, 0)} RAM";
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Hardware summary unavailable");
            return "Unknown hardware";
        }
    }

    private async Task StoreCoreAsync(BenchmarkResult result, CancellationToken cancellationToken)
    {
        if (_history is null)
        {
            return;
        }

        await _history.SaveBenchmarkAsync(result, cancellationToken).ConfigureAwait(false);
        await _history.AddEventAsync(new HistoryEvent
        {
            Timestamp = result.StartedAt,
            Category = HistoryCategory.Benchmark,
            Action = $"{result.Type} benchmark" + (result.GameName is null ? string.Empty : $" · {result.GameName}"),
            Result = result.Completed ? EventResult.Success : EventResult.Failed,
            Details = result.Completed
                ? string.Join(" · ", BenchmarkComparer.AllMetrics(result).Take(3).Select(m => $"{m.Name}: {m.Value:0.##} {m.Unit}"))
                : result.Error ?? "Failed",
            Rollback = RollbackStatus.NotApplicable,
            RelatedId = result.Id.ToString(),
        }, cancellationToken).ConfigureAwait(false);
    }

    private sealed class UtilizationTracker
    {
        private readonly Lock _gate = new();
        private double _cpu;
        private int _cpuCount;
        private double _gpu;
        private int _gpuCount;

        public double? AverageCpu
        {
            get
            {
                lock (_gate)
                {
                    return _cpuCount > 0 ? _cpu / _cpuCount : null;
                }
            }
        }

        public double? AverageGpu
        {
            get
            {
                lock (_gate)
                {
                    return _gpuCount > 0 ? _gpu / _gpuCount : null;
                }
            }
        }

        public void Add(MetricsSnapshot snapshot)
        {
            lock (_gate)
            {
                if (snapshot.Cpu.Usage.Value is { } cpu)
                {
                    _cpu += cpu;
                    _cpuCount++;
                }

                if (snapshot.PrimaryGpu()?.Usage.Value is { } gpu)
                {
                    _gpu += gpu;
                    _gpuCount++;
                }
            }
        }
    }
}
