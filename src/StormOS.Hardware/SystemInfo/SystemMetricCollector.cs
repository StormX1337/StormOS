using StormOS.Core.Common;
using StormOS.Core.Telemetry;
using StormOS.Windows.Platform;
using StormOS.Windows.Pdh;

namespace StormOS.Hardware.SystemInfo;

/// <summary>Process/thread/handle counts, context switches and uptime.</summary>
public sealed class SystemMetricCollector : ISystemMetricCollector
{
    private PdhQuery? _query;
    private PdhCounter? _contextSwitches;
    private bool _initialized;

    /// <inheritdoc />
    public SystemMetrics Collect(SamplingMode mode)
    {
        if (!_initialized)
        {
            _initialized = true;
            try
            {
                _query = new PdhQuery();
                _contextSwitches = _query.TryAddCounter(@"\System\Context Switches/sec");
                _query.Collect();
            }
            catch (InvalidOperationException)
            {
                _query = null;
            }
        }

        var switches = Reading.Unavailable("Context switch counters are not available.", "PDH");
        if (_query is not null && _query.Collect() && _query.HasRateData && _contextSwitches is not null && _contextSwitches.TryGetValue(out var value))
        {
            switches = Reading.Of(value, "PDH");
        }

        var info = SystemPerformance.Read();
        return new SystemMetrics
        {
            ProcessCount = info?.ProcessCount ?? 0,
            ThreadCount = info?.ThreadCount ?? 0,
            HandleCount = info?.HandleCount ?? 0,
            ContextSwitchesPerSecond = switches,
            Uptime = SystemPerformance.Uptime,
        };
    }

    /// <inheritdoc />
    public void Dispose() => _query?.Dispose();
}
