using StormOS.Core.Telemetry;
using StormOS.Windows.Platform;

namespace StormOS.Hardware.Memory;

/// <summary>Memory telemetry from GlobalMemoryStatusEx and GetPerformanceInfo.</summary>
public sealed class MemoryMetricCollector : IMemoryMetricCollector
{
    /// <inheritdoc />
    public MemoryMetrics Collect(SamplingMode mode)
    {
        if (SystemPerformance.Read() is not { } info)
        {
            return new MemoryMetrics();
        }

        return new MemoryMetrics
        {
            TotalBytes = info.TotalPhysical,
            AvailableBytes = info.AvailablePhysical,
            CommittedBytes = info.CommitTotal,
            CommitLimitBytes = info.CommitLimit,
            CachedBytes = info.SystemCache,
        };
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }
}
