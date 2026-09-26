using StormOS.Core.Hardware;
using StormOS.Core.Network;
using StormOS.Core.Telemetry;
using StormOS.Services.Client;

namespace StormOS.Services.Onboarding;

/// <summary>One line of the "Storm OS System Check".</summary>
/// <param name="Name">Check name, for example "CPU detected".</param>
/// <param name="Passed">Whether the check passed.</param>
/// <param name="Detail">Measured detail or the reason it failed.</param>
public sealed record SystemCheckItem(string Name, bool Passed, string Detail);

/// <summary>Runs the first-start system check using real detection only.</summary>
public sealed class SystemCheckService(IHardwareInventoryProvider inventory, IOperatingSystemInfoProvider os, INetworkDiagnostics network, ITelemetryHub telemetry, IStormServiceClient service)
{
    /// <summary>Runs all checks.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Check items.</returns>
    public async Task<IReadOnlyList<SystemCheckItem>> RunAsync(CancellationToken cancellationToken = default)
    {
        var items = new List<SystemCheckItem>();
        var data = await inventory.GetInventoryAsync(cancellationToken).ConfigureAwait(false);
        items.Add(new("CPU detected", !string.IsNullOrEmpty(data.Cpu.Name), string.IsNullOrEmpty(data.Cpu.Name) ? "The processor could not be identified." : $"{data.Cpu.Name} ({data.Cpu.Cores} cores / {data.Cpu.LogicalProcessors} threads)"));
        var gpu = data.Gpus.OrderByDescending(g => g.DedicatedMemoryBytes).FirstOrDefault();
        items.Add(new("GPU detected", gpu is not null, gpu?.Name ?? "No hardware graphics adapter found."));
        items.Add(new("GPU driver present", gpu?.DriverVersion is not null, gpu?.DriverVersion is { } v ? $"Driver {v}" : "No driver version reported."));
        items.Add(new("RAM detected", data.Memory.TotalBytes > 0, Core.Common.Units.FormatBytes(data.Memory.TotalBytes, 0)));

        var osInfo = os.GetOsInfo();
        items.Add(new("Windows 11 supported", osInfo.IsWindows11, osInfo.IsWindows11 ? $"{osInfo.ProductName} {osInfo.DisplayVersion}" : $"{osInfo.ProductName} — supported with limitations"));

        var connected = await service.ConnectAsync(cancellationToken).ConfigureAwait(false);
        items.Add(new("STORM OS service running", connected.IsSuccess, connected.IsSuccess ? $"Version {service.Hello?.ServiceVersion}" : connected.Error.Message));
        if (connected.IsSuccess && (await service.GetFrameCaptureStatusAsync(cancellationToken).ConfigureAwait(false)) is { IsSuccess: true } frames)
        {
            var presentMon = frames.Value!.Providers.FirstOrDefault(p => p.Name == "PresentMon");
            items.Add(new("PresentMon available", presentMon?.Available == true, presentMon?.Available == true ? "Frame timing via PresentMon" : frames.Value.Providers.Any(p => p.Available) ? "PresentMon not installed; ETW fallback (Direct3D titles) available" : presentMon?.Reason ?? "Not available"));
        }
        else
        {
            items.Add(new("PresentMon available", false, "Frame capture needs the STORM OS service."));
        }

        var active = network.GetActiveInterface();
        items.Add(new("Network available", active is not null, active is null ? "No active connection." : $"{active.Name} · {active.Description}"));

        var snapshot = await telemetry.SampleOnceAsync(cancellationToken).ConfigureAwait(false);
        var sensors = new List<string>();
        if (snapshot.Cpu.TemperatureCelsius.IsAvailable)
        {
            sensors.Add("CPU temperature");
        }

        if (snapshot.PrimaryGpu()?.TemperatureCelsius.IsAvailable == true)
        {
            sensors.Add("GPU temperature");
        }

        if (snapshot.Cpu.PackagePowerWatts.IsAvailable)
        {
            sensors.Add("CPU power");
        }

        if (snapshot.PrimaryGpu()?.PowerWatts.IsAvailable == true)
        {
            sensors.Add("GPU power");
        }

        items.Add(new("Available sensors", sensors.Count > 0, sensors.Count > 0 ? string.Join(", ", sensors) : "Windows exposes no temperature or power sensors on this PC."));
        return items;
    }
}
