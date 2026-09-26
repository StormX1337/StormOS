using System.Globalization;
using StormOS.Core.Common;
using StormOS.Core.Hardware;
using StormOS.Core.Network;
using StormOS.Core.Optimization;
using StormOS.Core.Power;
using StormOS.Core.Processes;
using StormOS.Core.Scan;
using StormOS.Core.Startup;
using StormOS.Core.Telemetry;

namespace StormOS.Services.Scan;

/// <summary>Operating system checks.</summary>
public sealed class OperatingSystemScanCheck(IOperatingSystemInfoProvider os, TimeProvider? timeProvider = null) : ISystemScanCheck
{
    /// <inheritdoc />
    public string Area => "Operating system";

    /// <inheritdoc />
    public Task<(IReadOnlyList<ScanCheck> Checks, IReadOnlyList<ScanFinding> Findings)> RunAsync(CancellationToken cancellationToken = default)
    {
        var info = os.GetOsInfo();
        var checks = new List<ScanCheck> { new("Windows version", info.IsWindows11, $"{info.ProductName} {info.DisplayVersion} (build {info.VersionString})") };
        var findings = new List<ScanFinding>();
        if (!info.IsWindows11)
        {
            findings.Add(new ScanFinding
            {
                Id = "os.windows10",
                Area = Area,
                Severity = ScanSeverity.Warning,
                Title = "Windows 10 detected",
                Description = "STORM OS is designed for Windows 11. Some features (windowed game optimizations, newer scheduler improvements) are unavailable.",
                Evidence = [$"Build {info.BuildNumber}"],
                Impact = "Reduced feature set.",
                SuggestedAction = "Upgrade to Windows 11 when your hardware supports it.",
                Risk = RiskLevel.None,
            });
        }

        var uptime = (timeProvider ?? TimeProvider.System).GetUtcNow() - info.BootTime;
        checks.Add(new ScanCheck("Uptime", uptime < TimeSpan.FromDays(14), Units.FormatDuration(uptime)));
        if (uptime > TimeSpan.FromDays(14))
        {
            findings.Add(new ScanFinding
            {
                Id = "os.uptime",
                Area = Area,
                Severity = ScanSeverity.Warning,
                Title = "Windows has not been restarted for a long time",
                Description = "Long uptimes accumulate pending updates and memory held by drivers.",
                Evidence = [$"Uptime: {Units.FormatDuration(uptime)}"],
                Impact = "Pending updates are not installed and some memory is not reclaimed.",
                SuggestedAction = "Restart Windows when convenient. Note that Fast Startup does not reset uptime; use Restart, not Shut down.",
                Risk = RiskLevel.None,
            });
        }

        return Task.FromResult<(IReadOnlyList<ScanCheck>, IReadOnlyList<ScanFinding>)>((checks, findings));
    }
}

/// <summary>Hardware and driver checks.</summary>
public sealed class HardwareScanCheck(IHardwareInventoryProvider inventory, TimeProvider? timeProvider = null) : ISystemScanCheck
{
    /// <inheritdoc />
    public string Area => "Hardware";

    /// <inheritdoc />
    public async Task<(IReadOnlyList<ScanCheck> Checks, IReadOnlyList<ScanFinding> Findings)> RunAsync(CancellationToken cancellationToken = default)
    {
        var data = await inventory.GetInventoryAsync(cancellationToken).ConfigureAwait(false);
        var checks = new List<ScanCheck>
        {
            new("CPU detected", !string.IsNullOrEmpty(data.Cpu.Name), data.Cpu.Name),
            new("GPU detected", data.Gpus.Count > 0, data.Gpus.Count > 0 ? string.Join(", ", data.Gpus.Select(g => g.Name)) : "No hardware GPU found"),
            new("RAM detected", data.Memory.TotalBytes > 0, Units.FormatBytes(data.Memory.TotalBytes, 0)),
        };
        var findings = new List<ScanFinding>();
        var modules = data.Memory.Modules;
        if (modules.Count == 1)
        {
            findings.Add(new ScanFinding
            {
                Id = "hw.single-channel",
                Area = Area,
                Severity = ScanSeverity.Warning,
                Title = "Memory runs in single-channel mode",
                Description = "Only one memory module is installed, so the memory controller uses one channel.",
                Evidence = [$"Modules: 1 × {Units.FormatBytes(modules[0].CapacityBytes, 0)} in {modules[0].Slot}"],
                Impact = "Up to half the memory bandwidth, which lowers 1% lows in CPU-bound games and integrated GPU performance.",
                SuggestedAction = "Add a matching module so both channels are populated.",
                Risk = RiskLevel.None,
            });
        }

        var slow = modules.Where(m => m.SpeedMts is > 0 && m.ConfiguredSpeedMts is > 0 && m.ConfiguredSpeedMts < m.SpeedMts * 0.95).ToList();
        if (slow.Count > 0)
        {
            findings.Add(new ScanFinding
            {
                Id = "hw.memory-speed",
                Area = Area,
                Severity = ScanSeverity.Warning,
                Title = "Memory runs below its rated speed",
                Description = "The memory modules report a higher rated speed than the configured speed. This usually means the XMP/EXPO profile is not enabled in the BIOS.",
                Evidence = slow.Select(m => $"{m.Slot}: rated {m.SpeedMts} MT/s, running {m.ConfiguredSpeedMts} MT/s").ToList(),
                Impact = "Lower memory bandwidth and higher latency.",
                SuggestedAction = "Enable the XMP (Intel) or EXPO (AMD) profile in the BIOS. STORM OS never changes firmware settings.",
                Risk = RiskLevel.Medium,
            });
        }

        var now = (timeProvider ?? TimeProvider.System).GetUtcNow();
        foreach (var gpu in data.Gpus.Where(g => g.DriverDate is { } date && now.Date - date.ToDateTime(TimeOnly.MinValue) > TimeSpan.FromDays(365)))
        {
            findings.Add(new ScanFinding
            {
                Id = "hw.gpu-driver-old",
                Area = "Drivers",
                Severity = ScanSeverity.Warning,
                Title = $"{gpu.Name}: graphics driver is more than a year old",
                Description = "New drivers regularly include performance fixes for recent games.",
                Evidence = [$"Driver {gpu.DriverVersion} from {gpu.DriverDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}"],
                Impact = "Missing game-specific optimizations and fixes.",
                SuggestedAction = "Update the driver from the GPU vendor's website or app.",
                Risk = RiskLevel.Low,
            });
        }

        checks.Add(new ScanCheck("GPU driver present", data.Gpus.All(g => !string.IsNullOrEmpty(g.DriverVersion)), string.Join(", ", data.Gpus.Select(g => g.DriverVersion ?? "unknown"))));
        return (checks, findings);
    }
}

/// <summary>Storage checks.</summary>
public sealed class StorageScanCheck(IHardwareInventoryProvider inventory) : ISystemScanCheck
{
    /// <inheritdoc />
    public string Area => "Storage";

    /// <inheritdoc />
    public async Task<(IReadOnlyList<ScanCheck> Checks, IReadOnlyList<ScanFinding> Findings)> RunAsync(CancellationToken cancellationToken = default)
    {
        var data = await inventory.GetInventoryAsync(cancellationToken).ConfigureAwait(false);
        var checks = new List<ScanCheck>();
        var findings = new List<ScanFinding>();
        var systemRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
        foreach (var volume in data.Volumes)
        {
            var free = 100 - volume.UsedPercent;
            checks.Add(new ScanCheck($"Free space {volume.RootPath}", free >= 15, $"{Units.FormatBytes(volume.FreeBytes)} free ({free:0} %)"));
            var isSystem = string.Equals(volume.RootPath, systemRoot, StringComparison.OrdinalIgnoreCase);
            if (free < (isSystem ? 15 : 5))
            {
                findings.Add(new ScanFinding
                {
                    Id = "storage.free-" + volume.RootPath[..1].ToLowerInvariant(),
                    Area = Area,
                    Severity = free < 5 ? ScanSeverity.Attention : ScanSeverity.Warning,
                    Title = $"Drive {volume.RootPath} is almost full",
                    Description = isSystem ? "Windows needs free space for updates, the page file and shader caches." : "Games need free space for updates and shader caches.",
                    Evidence = [$"{Units.FormatBytes(volume.FreeBytes)} free of {Units.FormatBytes(volume.TotalBytes)}"],
                    Impact = "Failed updates and possible stutter when caches cannot grow.",
                    SuggestedAction = "Uninstall unused games or run Windows Disk Cleanup. STORM OS can remove old temporary files (not reversible).",
                    Risk = RiskLevel.Low,
                    RuleId = isSystem ? "storage.temp-cleanup" : null,
                });
            }
        }

        foreach (var disk in data.Storage)
        {
            checks.Add(new ScanCheck($"Disk health {disk.Model}", disk.HealthStatus is null or "Healthy", disk.HealthStatus ?? "Not reported"));
            if (disk.HealthStatus is "Warning" or "Unhealthy")
            {
                findings.Add(new ScanFinding
                {
                    Id = "storage.health-" + disk.Index.ToString(CultureInfo.InvariantCulture),
                    Area = Area,
                    Severity = ScanSeverity.Attention,
                    Title = $"{disk.Model} reports a health problem",
                    Description = "Windows Storage Management reports that the drive is degraded.",
                    Evidence = [$"Health status: {disk.HealthStatus}"],
                    Impact = "Risk of data loss.",
                    SuggestedAction = "Back up important data now and check the drive with the manufacturer's tool.",
                    Risk = RiskLevel.None,
                });
            }
        }

        if (data.Storage.Count > 0 && data.Storage.All(d => d.MediaType == StorageMediaType.Hdd))
        {
            findings.Add(new ScanFinding
            {
                Id = "storage.hdd-only",
                Area = Area,
                Severity = ScanSeverity.Warning,
                Title = "No SSD detected",
                Description = "All drives are hard disks.",
                Evidence = data.Storage.Select(d => $"{d.Model}: {d.MediaType}").ToList(),
                Impact = "Long loading times and texture streaming stutter in modern games.",
                SuggestedAction = "Install games on an SSD.",
                Risk = RiskLevel.None,
            });
        }

        return (checks, findings);
    }
}

/// <summary>Power plan check.</summary>
public sealed class PowerScanCheck(IPowerPlanService power) : ISystemScanCheck
{
    /// <inheritdoc />
    public string Area => "Power";

    /// <inheritdoc />
    public Task<(IReadOnlyList<ScanCheck> Checks, IReadOnlyList<ScanFinding> Findings)> RunAsync(CancellationToken cancellationToken = default)
    {
        var plans = power.GetPlans();
        var active = plans.FirstOrDefault(p => p.IsActive);
        var checks = new List<ScanCheck> { new("Active power plan", active?.Id != KnownPowerSchemes.PowerSaver, active?.Name ?? "Unknown") };
        var findings = new List<ScanFinding>();
        if (active?.Id == KnownPowerSchemes.PowerSaver)
        {
            findings.Add(new ScanFinding
            {
                Id = "power.saver",
                Area = Area,
                Severity = ScanSeverity.Attention,
                Title = "Power saver is active",
                Description = "The Power saver plan caps processor performance.",
                Evidence = [$"Active plan: {active.Name}"],
                Impact = "Significantly lower CPU performance in games.",
                SuggestedAction = "Switch to Balanced (or High performance while gaming).",
                Risk = RiskLevel.Low,
                RuleId = "power.plan",
                RuleParameters = new Dictionary<string, string> { ["plan"] = "balanced" },
            });
        }

        return Task.FromResult<(IReadOnlyList<ScanCheck>, IReadOnlyList<ScanFinding>)>((checks, findings));
    }
}

/// <summary>Startup program check.</summary>
public sealed class StartupScanCheck(IStartupManager startup) : ISystemScanCheck
{
    /// <inheritdoc />
    public string Area => "Startup";

    /// <inheritdoc />
    public async Task<(IReadOnlyList<ScanCheck> Checks, IReadOnlyList<ScanFinding> Findings)> RunAsync(CancellationToken cancellationToken = default)
    {
        var entries = await startup.ListAsync(cancellationToken).ConfigureAwait(false);
        var enabled = entries.Where(e => e.IsEnabled).ToList();
        var checks = new List<ScanCheck> { new("Startup programs", enabled.Count <= 15, $"{enabled.Count} enabled of {entries.Count}") };
        var findings = new List<ScanFinding>();
        if (enabled.Count > 15)
        {
            findings.Add(new ScanFinding
            {
                Id = "startup.many",
                Area = Area,
                Severity = ScanSeverity.Warning,
                Title = "Many programs start with Windows",
                Description = "Each startup program costs boot time and usually keeps running in the background.",
                Evidence = enabled.OrderByDescending(e => e.RuntimeCpuSeconds ?? 0).Take(8).Select(e => $"{e.Name} ({e.Impact})").ToList(),
                Impact = "Longer boot time, more background CPU and memory use.",
                SuggestedAction = "Review the Startup page and disable programs you do not need at login. Nothing is deleted.",
                Risk = RiskLevel.Low,
            });
        }

        return (checks, findings);
    }
}

/// <summary>Live resource checks (memory pressure, temperatures, busy background processes).</summary>
public sealed class PerformanceScanCheck(ITelemetryHub telemetry, IProcessInspector processes) : ISystemScanCheck
{
    /// <inheritdoc />
    public string Area => "Performance";

    /// <inheritdoc />
    public async Task<(IReadOnlyList<ScanCheck> Checks, IReadOnlyList<ScanFinding> Findings)> RunAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await telemetry.SampleOnceAsync(cancellationToken).ConfigureAwait(false);
        processes.Snapshot();
        await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
        var busy = processes.Snapshot().Where(p => p.CpuPercent > 5 && !p.IsCritical && p.SessionId != 0).OrderByDescending(p => p.CpuPercent).ToList();

        var checks = new List<ScanCheck>
        {
            new("Memory usage", snapshot.Memory.UsagePercent < 85, $"{snapshot.Memory.UsagePercent:0} %"),
            new("CPU temperature", snapshot.Cpu.TemperatureCelsius.Value is null or < 90, snapshot.Cpu.TemperatureCelsius.Value is { } t ? $"{t:0} °C" : snapshot.Cpu.TemperatureCelsius.UnavailableReason ?? "Unavailable"),
            new("Busy background processes", busy.Count <= 2, busy.Count.ToString(CultureInfo.InvariantCulture)),
        };
        var findings = new List<ScanFinding>();
        if (snapshot.Memory.UsagePercent >= 85)
        {
            findings.Add(new ScanFinding
            {
                Id = "perf.memory",
                Area = Area,
                Severity = snapshot.Memory.UsagePercent >= 93 ? ScanSeverity.Attention : ScanSeverity.Warning,
                Title = "High memory usage at rest",
                Description = "Most of the physical memory is in use before a game is started.",
                Evidence = [$"{Units.FormatBytes(snapshot.Memory.UsedBytes)} of {Units.FormatBytes(snapshot.Memory.TotalBytes)} in use"],
                Impact = "Games may page to disk, causing stutter.",
                SuggestedAction = "Close unneeded applications and review startup programs.",
                Risk = RiskLevel.Low,
            });
        }

        if (busy.Count > 2)
        {
            findings.Add(new ScanFinding
            {
                Id = "perf.background",
                Area = "Processes",
                Severity = ScanSeverity.Warning,
                Title = "Background programs are using CPU",
                Description = "Several programs use noticeable CPU time while you are not gaming.",
                Evidence = busy.Take(6).Select(p => $"{p.Name}: {p.CpuPercent:0.0} % CPU").ToList(),
                Impact = "Less CPU time for games.",
                SuggestedAction = "Close them, or lower their priority from the Processes page while playing.",
                Risk = RiskLevel.Low,
            });
        }

        if (snapshot.PrimaryGpu()?.TemperatureCelsius.Value is > 80 and var gpuTemp)
        {
            findings.Add(new ScanFinding
            {
                Id = "perf.gpu-temp",
                Area = Area,
                Severity = ScanSeverity.Warning,
                Title = "GPU is hot while idle",
                Description = "The GPU temperature is high without a game running.",
                Evidence = [$"{gpuTemp:0} °C"],
                Impact = "Reduced boost clocks under load.",
                SuggestedAction = "Check GPU fans and case airflow; close GPU-heavy background apps.",
                Risk = RiskLevel.None,
            });
        }

        return (checks, findings);
    }
}

/// <summary>Quick network check.</summary>
public sealed class NetworkScanCheck(INetworkDiagnostics network) : ISystemScanCheck
{
    /// <inheritdoc />
    public string Area => "Network";

    /// <inheritdoc />
    public async Task<(IReadOnlyList<ScanCheck> Checks, IReadOnlyList<ScanFinding> Findings)> RunAsync(CancellationToken cancellationToken = default)
    {
        var active = network.GetActiveInterface();
        var checks = new List<ScanCheck> { new("Network available", active is not null, active is null ? "No connection" : $"{active.Name} ({active.Description})") };
        var findings = new List<ScanFinding>();
        if (active is null)
        {
            return (checks, findings);
        }

        var ping = await network.PingAsync("1.1.1.1", 10, cancellationToken).ConfigureAwait(false);
        checks.Add(new ScanCheck("Internet latency", ping.Received > 0, ping.AverageMs is { } avg ? $"{avg:0} ms, jitter {ping.JitterMs ?? 0:0.0} ms, loss {ping.LossPercent:0} %" : "No reply"));
        if (active.InterfaceType.Contains("Wireless", StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(new ScanFinding
            {
                Id = "net.wifi",
                Area = Area,
                Severity = ScanSeverity.Warning,
                Title = "Gaming over Wi-Fi",
                Description = "Wireless connections add latency variation and occasional packet loss.",
                Evidence = [$"Active adapter: {active.Description}", ping.JitterMs is { } j ? $"Measured jitter: {j:0.0} ms" : "Jitter not measured"],
                Impact = "Inconsistent latency in online games.",
                SuggestedAction = "Use an Ethernet cable for competitive games, or a 5/6 GHz band close to the router.",
                Risk = RiskLevel.None,
            });
        }

        if (ping.Sent > 0 && ping.LossPercent >= 5)
        {
            findings.Add(new ScanFinding
            {
                Id = "net.loss",
                Area = Area,
                Severity = ScanSeverity.Attention,
                Title = "Packet loss detected",
                Description = "Some echo requests to the internet were lost.",
                Evidence = [$"{ping.LossPercent:0} % loss over {ping.Sent} requests"],
                Impact = "Rubber-banding and missed hits online.",
                SuggestedAction = "Run the full Network test to find where the loss happens.",
                Risk = RiskLevel.None,
            });
        }

        return (checks, findings);
    }
}

/// <summary>Gaming related Windows settings (via the user-level optimization engine).</summary>
public sealed class GamingSettingsScanCheck(IOptimizationEngine engine) : ISystemScanCheck
{
    private static readonly (string RuleId, string Title, string Impact)[] Rules =
    [
        ("windows.game-mode", "Game Mode is off", "Windows Update and notifications may interrupt games."),
        ("windows.game-dvr", "Background recording is on", "The GPU encoder and disk are used continuously."),
        ("windows.windowed-optimizations", "Optimizations for windowed games are off", "Borderless DirectX 10/11 games use the slower blt presentation model."),
    ];

    /// <inheritdoc />
    public string Area => "Windows gaming settings";

    /// <inheritdoc />
    public async Task<(IReadOnlyList<ScanCheck> Checks, IReadOnlyList<ScanFinding> Findings)> RunAsync(CancellationToken cancellationToken = default)
    {
        var checks = new List<ScanCheck>();
        var findings = new List<ScanFinding>();
        foreach (var (ruleId, title, impact) in Rules)
        {
            var rule = engine.Rules.FirstOrDefault(r => r.Id == ruleId);
            if (rule is null)
            {
                continue;
            }

            var detection = await engine.DetectAsync(ruleId, null, cancellationToken).ConfigureAwait(false);
            checks.Add(new ScanCheck(rule.Name, detection.State != DetectionState.Applicable, detection.CurrentValue));
            if (detection.State == DetectionState.Applicable)
            {
                findings.Add(new ScanFinding
                {
                    Id = "gaming." + ruleId,
                    Area = Area,
                    Severity = ScanSeverity.Warning,
                    Title = title,
                    Description = rule.Description,
                    Evidence = [$"Current: {detection.CurrentValue}"],
                    Impact = impact,
                    SuggestedAction = $"Apply '{rule.Name}' (reversible).",
                    Risk = rule.RiskLevel,
                    RuleId = ruleId,
                });
            }
        }

        return (checks, findings);
    }
}
