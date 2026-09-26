using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StormOS.Core.Hardware;
using StormOS.Core.Processes;
using StormOS.Core.Telemetry;
using StormOS.Hardware.Cpu;
using StormOS.Hardware.Gpu;
using StormOS.Hardware.Inventory;
using StormOS.Hardware.Memory;
using StormOS.Hardware.Networking;
using StormOS.Hardware.Storage;
using StormOS.Hardware.SystemInfo;

namespace StormOS.Hardware;

/// <summary>Dependency injection registration for hardware providers.</summary>
public static class HardwareServiceCollectionExtensions
{
    /// <summary>Registers hardware inventory and metric collectors.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddStormHardware(this IServiceCollection services)
    {
        services.TryAddSingleton<IHardwareInventoryProvider, WindowsHardwareInventoryProvider>();
        services.TryAddSingleton<ICpuMetricCollector, CpuMetricCollector>();
        services.TryAddSingleton<GpuMetricCollector>();
        services.TryAddSingleton<IGpuMetricCollector>(sp => sp.GetRequiredService<GpuMetricCollector>());
        services.TryAddSingleton<IProcessGpuUsageProvider>(sp => sp.GetRequiredService<GpuMetricCollector>());
        services.TryAddSingleton<IMemoryMetricCollector, MemoryMetricCollector>();
        services.TryAddSingleton<IDiskMetricCollector, DiskMetricCollector>();
        services.TryAddSingleton<INetworkMetricCollector, NetworkMetricCollector>();
        services.TryAddSingleton<ISystemMetricCollector, SystemMetricCollector>();
        return services;
    }
}
