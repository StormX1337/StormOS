using Microsoft.Extensions.Logging;
using StormOS.Core.Common;
using StormOS.Core.Telemetry;
using StormOS.Windows.Pdh;

namespace StormOS.Hardware.Storage;

/// <summary>Physical disk throughput and active time from PDH.</summary>
public sealed class DiskMetricCollector : IDiskMetricCollector
{
    private readonly ILogger<DiskMetricCollector> _logger;
    private PdhQuery? _query;
    private PdhCounter? _read;
    private PdhCounter? _write;
    private PdhCounter? _idle;
    private bool _initialized;
    private int _tick;
    private IReadOnlyList<DiskMetrics> _last = [];

    /// <summary>Initializes a new instance of the <see cref="DiskMetricCollector"/> class.</summary>
    /// <param name="logger">Logger.</param>
    public DiskMetricCollector(ILogger<DiskMetricCollector> logger) => _logger = logger;

    /// <inheritdoc />
    public IReadOnlyList<DiskMetrics> Collect(SamplingMode mode)
    {
        // In performance mode disks are sampled every fifth tick to keep overhead minimal while gaming.
        if (mode == SamplingMode.Performance && _tick++ % 5 != 0)
        {
            return _last;
        }

        EnsureInitialized();
        if (_query is null || !_query.Collect() || !_query.HasRateData)
        {
            return _last;
        }

        var reads = _read?.GetInstances().ToDictionary(p => p.Key, p => p.Value) ?? [];
        var writes = _write?.GetInstances().ToDictionary(p => p.Key, p => p.Value) ?? [];
        var idle = _idle?.GetInstances().ToDictionary(p => p.Key, p => p.Value) ?? [];
        _last = reads.Keys.Where(k => k != "_Total").Select(name => new DiskMetrics
        {
            Name = name,
            ReadBytesPerSecond = reads.GetValueOrDefault(name),
            WriteBytesPerSecond = writes.GetValueOrDefault(name),
            ActiveTimePercent = idle.TryGetValue(name, out var i) ? Reading.Of(Math.Clamp(100 - i, 0, 100), "PDH") : Reading.Unavailable("Disk idle time is not available.", "PDH"),
        }).OrderBy(d => d.Name, StringComparer.Ordinal).ToList();
        return _last;
    }

    /// <inheritdoc />
    public void Dispose() => _query?.Dispose();

    private void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        try
        {
            _query = new PdhQuery();
            _read = _query.TryAddCounter(@"\PhysicalDisk(*)\Disk Read Bytes/sec");
            _write = _query.TryAddCounter(@"\PhysicalDisk(*)\Disk Write Bytes/sec");
            _idle = _query.TryAddCounter(@"\PhysicalDisk(*)\% Idle Time");
            _query.Collect();
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Disk performance counters could not be opened");
            _query?.Dispose();
            _query = null;
        }
    }
}
