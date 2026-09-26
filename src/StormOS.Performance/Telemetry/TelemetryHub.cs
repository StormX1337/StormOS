using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StormOS.Core.Telemetry;
using StormOS.Performance.Frames;

namespace StormOS.Performance.Telemetry;

/// <summary>
/// Collects live telemetry. Sampling runs only while at least one subscriber is attached; with a game running
/// the hub switches to performance mode (longer interval, fewer collectors) to minimise its own overhead.
/// </summary>
public sealed class TelemetryHub : ITelemetryHub, IDisposable
{
    private readonly ICpuMetricCollector _cpu;
    private readonly IGpuMetricCollector _gpu;
    private readonly IMemoryMetricCollector _memory;
    private readonly IDiskMetricCollector _disk;
    private readonly INetworkMetricCollector _network;
    private readonly ISystemMetricCollector _system;
    private readonly LatencyProbe _latency;
    private readonly IActiveGameProvider? _activeGame;
    private readonly FrameCaptureCoordinator? _frames;
    private readonly SamplingOptions _options;
    private readonly ILogger<TelemetryHub> _logger;
    private readonly TimeProvider _time;
    private readonly Lock _gate = new();
    private readonly SemaphoreSlim _sampleLock = new(1, 1);
    private readonly List<Action<MetricsSnapshot>> _subscribers = [];
    private readonly List<Action<MetricsSnapshot>> _observers = [];
    private CancellationTokenSource? _loopCts;
    private MetricsSnapshot? _latest;
    private long _sequence;
    private SamplingMode? _forcedMode;

    /// <summary>Initializes a new instance of the <see cref="TelemetryHub"/> class.</summary>
    /// <param name="cpu">CPU collector.</param>
    /// <param name="gpu">GPU collector.</param>
    /// <param name="memory">Memory collector.</param>
    /// <param name="disk">Disk collector.</param>
    /// <param name="network">Network collector.</param>
    /// <param name="system">System collector.</param>
    /// <param name="latency">Latency probe.</param>
    /// <param name="options">Sampling options.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="activeGame">Active game provider.</param>
    /// <param name="frames">Frame capture coordinator.</param>
    /// <param name="timeProvider">Time source.</param>
    public TelemetryHub(
        ICpuMetricCollector cpu,
        IGpuMetricCollector gpu,
        IMemoryMetricCollector memory,
        IDiskMetricCollector disk,
        INetworkMetricCollector network,
        ISystemMetricCollector system,
        LatencyProbe latency,
        IOptions<SamplingOptions> options,
        ILogger<TelemetryHub> logger,
        IActiveGameProvider? activeGame = null,
        FrameCaptureCoordinator? frames = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _cpu = cpu;
        _gpu = gpu;
        _memory = memory;
        _disk = disk;
        _network = network;
        _system = system;
        _latency = latency;
        _options = options.Value;
        _options.Normalize();
        _logger = logger;
        _activeGame = activeGame;
        _frames = frames;
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public MetricsSnapshot? Latest => Volatile.Read(ref _latest);

    /// <inheritdoc />
    public SamplingMode Mode
    {
        get
        {
            lock (_gate)
            {
                return _subscribers.Count == 0 ? SamplingMode.Idle : _forcedMode ?? (_activeGame?.ActiveGame is not null ? SamplingMode.Performance : SamplingMode.Monitoring);
            }
        }
    }

    /// <summary>Gets the number of active subscribers.</summary>
    public int SubscriberCount
    {
        get
        {
            lock (_gate)
            {
                return _subscribers.Count;
            }
        }
    }

    /// <inheritdoc />
    public IDisposable Subscribe(Action<MetricsSnapshot> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        lock (_gate)
        {
            _subscribers.Add(handler);
            if (_loopCts is null)
            {
                _loopCts = new CancellationTokenSource();
                var token = _loopCts.Token;
                _ = Task.Factory.StartNew(() => LoopAsync(token), token, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
                if (_options.LatencyProbeEnabled)
                {
                    _latency.Start(_options.LatencyProbeHost, TimeSpan.FromSeconds(_options.LatencyProbeIntervalSeconds));
                }

                _logger.LogInformation("Telemetry sampling started");
            }
        }

        return new Subscription(this, handler);
    }

    /// <summary>
    /// Observes snapshots without keeping sampling alive: observers only receive data while at least one
    /// subscriber is attached (used for passive history recording).
    /// </summary>
    /// <param name="handler">Callback invoked for each snapshot.</param>
    /// <returns>A handle that ends the observation when disposed.</returns>
    public IDisposable Observe(Action<MetricsSnapshot> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        lock (_gate)
        {
            _observers.Add(handler);
        }

        return new Observation(this, handler);
    }

    /// <inheritdoc />
    public async Task<MetricsSnapshot> SampleOnceAsync(CancellationToken cancellationToken = default)
    {
        await _sampleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return Sample(Mode == SamplingMode.Idle ? SamplingMode.Monitoring : Mode);
        }
        finally
        {
            _sampleLock.Release();
        }
    }

    /// <inheritdoc />
    public void SetMode(SamplingMode mode)
    {
        lock (_gate)
        {
            _forcedMode = mode == SamplingMode.Idle ? null : mode;
        }
    }

    /// <summary>Changes the monitoring interval at runtime.</summary>
    /// <param name="interval">The new interval in milliseconds.</param>
    public void SetInterval(int interval)
    {
        _options.IntervalMilliseconds = interval;
        _options.Normalize();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            _loopCts?.Cancel();
            _loopCts?.Dispose();
            _loopCts = null;
            _subscribers.Clear();
        }

        _latency.Stop();
        _sampleLock.Dispose();
        _cpu.Dispose();
        _gpu.Dispose();
        _memory.Dispose();
        _disk.Dispose();
        _network.Dispose();
        _system.Dispose();
    }

    private async Task LoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var mode = Mode;
            if (mode == SamplingMode.Idle)
            {
                break;
            }

            var started = _time.GetTimestamp();
            MetricsSnapshot snapshot;
            await _sampleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                snapshot = Sample(mode);
            }
            finally
            {
                _sampleLock.Release();
            }

            Action<MetricsSnapshot>[] handlers;
            lock (_gate)
            {
                handlers = [.. _subscribers, .. _observers];
            }

            foreach (var handler in handlers)
            {
                try
                {
                    handler(snapshot);
                }
#pragma warning disable CA1031 // A subscriber failure must not stop telemetry for everyone else.
                catch (Exception ex)
#pragma warning restore CA1031
                {
                    _logger.LogError(ex, "Telemetry subscriber failed");
                }
            }

            var interval = TimeSpan.FromMilliseconds(mode == SamplingMode.Performance ? _options.PerformanceModeIntervalMilliseconds : _options.IntervalMilliseconds);
            var remaining = interval - _time.GetElapsedTime(started);
            if (remaining > TimeSpan.Zero)
            {
                try
                {
                    await Task.Delay(remaining, _time, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private MetricsSnapshot Sample(SamplingMode mode)
    {
        var system = Safe(() => _system.Collect(mode), new SystemMetrics(), "system");
        var game = _activeGame?.ActiveGame;
        var snapshot = new MetricsSnapshot
        {
            Timestamp = _time.GetUtcNow(),
            Sequence = Interlocked.Increment(ref _sequence),
            Cpu = Safe(() => _cpu.Collect(mode), new CpuMetrics(), "CPU"),
            Gpus = Safe(() => _gpu.Collect(mode), [], "GPU"),
            Memory = Safe(() => _memory.Collect(mode), new MemoryMetrics(), "memory"),
            Disks = _options.CollectDisks ? Safe(() => _disk.Collect(mode), [], "disk") : [],
            Network = _options.CollectNetwork ? Safe(() => _network.Collect(mode), [], "network") : [],
            System = system with
            {
                LatencyMs = _options.LatencyProbeEnabled ? _latency.Latest : Core.Common.Reading.Unavailable("The latency probe is disabled in settings."),
                ActiveGame = game?.Name,
                ActiveGameProcessId = game?.ProcessId,
            },
            Frames = _frames?.GetLiveMetrics(),
        };
        Volatile.Write(ref _latest, snapshot);
        return snapshot;
    }

    private T Safe<T>(Func<T> collect, T fallback, string name)
    {
        try
        {
            return collect();
        }
#pragma warning disable CA1031 // One failing collector must not break the whole snapshot.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _logger.LogWarning(ex, "The {Collector} collector failed", name);
            return fallback;
        }
    }

    private void Unsubscribe(Action<MetricsSnapshot> handler)
    {
        lock (_gate)
        {
            _subscribers.Remove(handler);
            if (_subscribers.Count == 0 && _loopCts is not null)
            {
                _loopCts.Cancel();
                _loopCts.Dispose();
                _loopCts = null;
                _latency.Stop();
                _logger.LogInformation("Telemetry sampling paused (no subscribers)");
            }
        }
    }

    private sealed class Observation(TelemetryHub hub, Action<MetricsSnapshot> handler) : IDisposable
    {
        public void Dispose()
        {
            lock (hub._gate)
            {
                hub._observers.Remove(handler);
            }
        }
    }

    private sealed class Subscription(TelemetryHub hub, Action<MetricsSnapshot> handler) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                hub.Unsubscribe(handler);
            }
        }
    }
}
