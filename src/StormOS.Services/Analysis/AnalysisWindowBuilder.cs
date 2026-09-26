using StormOS.Core.Analysis;
using StormOS.Core.Frames;
using StormOS.Core.Telemetry;

namespace StormOS.Services.Analysis;

/// <summary>Aggregates telemetry snapshots into an <see cref="AnalysisWindow"/>.</summary>
public static class AnalysisWindowBuilder
{
    /// <summary>Builds a window.</summary>
    /// <param name="snapshots">Snapshots in time order.</param>
    /// <param name="frames">Frame statistics for the window, if captured.</param>
    /// <param name="powerPlan">Active power plan name.</param>
    /// <param name="refreshRateHz">Primary display refresh rate.</param>
    /// <param name="busyBackgroundProcesses">Number of busy background processes.</param>
    /// <returns>The window.</returns>
    public static AnalysisWindow Build(IReadOnlyList<MetricsSnapshot> snapshots, FrameStatistics? frames = null, string? powerPlan = null, double? refreshRateHz = null, int? busyBackgroundProcesses = null)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        if (snapshots.Count == 0)
        {
            return new AnalysisWindow { Frames = frames, PowerPlan = powerPlan, RefreshRateHz = refreshRateHz };
        }

        static double? Avg(IEnumerable<double?> values)
        {
            var list = values.Where(v => v.HasValue).Select(v => v!.Value).ToList();
            return list.Count > 0 ? list.Average() : null;
        }

        static double? Max(IEnumerable<double?> values)
        {
            var list = values.Where(v => v.HasValue).Select(v => v!.Value).ToList();
            return list.Count > 0 ? list.Max() : null;
        }

        return new AnalysisWindow
        {
            From = snapshots[0].Timestamp,
            To = snapshots[^1].Timestamp,
            SampleCount = snapshots.Count,
            AverageCpuUsage = Avg(snapshots.Select(s => s.Cpu.Usage.Value)),
            AverageMaxCoreUsage = Avg(snapshots.Select(s => s.Cpu.Cores.Count > 0 ? s.Cpu.Cores.Max(c => c.UsagePercent) : (double?)null)),
            AverageGpuUsage = Avg(snapshots.Select(s => s.PrimaryGpu()?.Usage.Value)),
            AverageRamUsage = Avg(snapshots.Select(s => s.Memory.TotalBytes > 0 ? s.Memory.UsagePercent : (double?)null)),
            AverageVramUsage = Avg(snapshots.Select(s => s.PrimaryGpu()?.MemoryUsagePercent)),
            AverageDiskActive = Avg(snapshots.Select(s => s.Disks.Count > 0 ? s.Disks.Max(d => d.ActiveTimePercent.Value ?? 0) : (double?)null)),
            AverageLatencyMs = Avg(snapshots.Select(s => s.System.LatencyMs.Value)),
            MaxCpuTemperature = Max(snapshots.Select(s => s.Cpu.TemperatureCelsius.Value)),
            MaxGpuTemperature = Max(snapshots.Select(s => s.PrimaryGpu()?.TemperatureCelsius.Value)),
            ProcessCount = snapshots[^1].System.ProcessCount,
            BusyBackgroundProcesses = busyBackgroundProcesses,
            GameName = snapshots.LastOrDefault(s => s.System.ActiveGame is not null)?.System.ActiveGame,
            Frames = frames ?? snapshots.LastOrDefault(s => s.Frames is not null)?.Frames?.Window,
            PowerPlan = powerPlan,
            RefreshRateHz = refreshRateHz,
        };
    }
}
