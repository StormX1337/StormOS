using Microsoft.Extensions.Logging;
using StormOS.Core.History;
using StormOS.Core.Settings;
using StormOS.Core.Telemetry;
using StormOS.Performance.Telemetry;

namespace StormOS.Performance.Sessions;

/// <summary>
/// Passively downsamples telemetry into 10 second averages for historical charts. It never starts sampling on its
/// own and honours the "performance history" privacy setting and retention period.
/// </summary>
public sealed class MetricHistoryRecorder : IDisposable
{
    /// <summary>Bucket length.</summary>
    public static readonly TimeSpan BucketLength = TimeSpan.FromSeconds(10);

    private readonly TelemetryHub _hub;
    private readonly IHistoryStore _history;
    private readonly ISettingsStore? _settings;
    private readonly ILogger<MetricHistoryRecorder> _logger;
    private readonly TimeProvider _time;
    private readonly Lock _gate = new();
    private readonly List<MetricsSnapshot> _bucket = [];
    private readonly List<MetricHistoryPoint> _pending = [];
    private IDisposable? _observation;
    private DateTimeOffset _lastPrune;

    /// <summary>Initializes a new instance of the <see cref="MetricHistoryRecorder"/> class.</summary>
    /// <param name="hub">Telemetry hub.</param>
    /// <param name="history">History store.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="settings">Settings store.</param>
    /// <param name="timeProvider">Time source.</param>
    public MetricHistoryRecorder(TelemetryHub hub, IHistoryStore history, ILogger<MetricHistoryRecorder> logger, ISettingsStore? settings = null, TimeProvider? timeProvider = null)
    {
        _hub = hub;
        _history = history;
        _logger = logger;
        _settings = settings;
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Aggregates snapshots into one history point.</summary>
    /// <param name="snapshots">Snapshots of one bucket.</param>
    /// <returns>The averaged point.</returns>
    public static MetricHistoryPoint Aggregate(IReadOnlyList<MetricsSnapshot> snapshots)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        static double? Avg(IEnumerable<double?> values)
        {
            var list = values.Where(v => v.HasValue).Select(v => v!.Value).ToList();
            return list.Count > 0 ? list.Average() : null;
        }

        return new MetricHistoryPoint
        {
            Timestamp = snapshots[^1].Timestamp,
            Cpu = Avg(snapshots.Select(s => s.Cpu.Usage.Value)),
            Gpu = Avg(snapshots.Select(s => s.PrimaryGpu()?.Usage.Value)),
            Ram = Avg(snapshots.Select(s => (double?)s.Memory.UsagePercent)),
            Vram = Avg(snapshots.Select(s => s.PrimaryGpu()?.MemoryUsagePercent)),
            Fps = Avg(snapshots.Select(s => s.Frames?.Fps)),
            FrameTimeMs = Avg(snapshots.Select(s => s.Frames?.FrameTimeMs)),
            LatencyMs = Avg(snapshots.Select(s => s.System.LatencyMs.Value)),
            CpuTemp = Avg(snapshots.Select(s => s.Cpu.TemperatureCelsius.Value)),
            GpuTemp = Avg(snapshots.Select(s => s.PrimaryGpu()?.TemperatureCelsius.Value)),
        };
    }

    /// <summary>Starts observing telemetry.</summary>
    public void Start() => _observation ??= _hub.Observe(OnSnapshot);

    /// <summary>Writes pending points and prunes old data.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the operation.</returns>
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        List<MetricHistoryPoint> points;
        lock (_gate)
        {
            points = [.. _pending];
            _pending.Clear();
        }

        try
        {
            if (points.Count > 0)
            {
                await _history.AppendMetricHistoryAsync(points, cancellationToken).ConfigureAwait(false);
            }

            var now = _time.GetUtcNow();
            if (now - _lastPrune > TimeSpan.FromHours(6))
            {
                var retention = Math.Clamp(_settings?.Current.Performance.HistoryRetentionDays ?? 30, 1, 365);
                await _history.PruneAsync(now.AddDays(-retention), cancellationToken).ConfigureAwait(false);
                _lastPrune = now;
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            _logger.LogWarning(ex, "Could not write metric history");
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _observation?.Dispose();
        _observation = null;
    }

    private void OnSnapshot(MetricsSnapshot snapshot)
    {
        if (snapshot.IsMock || !(_settings?.Current.Privacy.PerformanceHistory ?? true))
        {
            return;
        }

        lock (_gate)
        {
            _bucket.Add(snapshot);
            if (snapshot.Timestamp - _bucket[0].Timestamp < BucketLength)
            {
                return;
            }

            _pending.Add(Aggregate(_bucket));
            _bucket.Clear();
            if (_pending.Count < 6)
            {
                return;
            }
        }

        _ = Task.Run(() => FlushAsync());
    }
}
