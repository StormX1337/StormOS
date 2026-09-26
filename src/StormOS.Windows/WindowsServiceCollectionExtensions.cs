using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StormOS.Core.Hardware;
using StormOS.Core.Power;
using StormOS.Core.Processes;
using StormOS.Core.Startup;
using StormOS.Security.Ipc;
using StormOS.Security.Secrets;
using StormOS.Windows.Platform;
using StormOS.Windows.Power;
using StormOS.Windows.Processes;
using StormOS.Windows.RegistryAccess;
using StormOS.Windows.Security;
using StormOS.Windows.Services;
using StormOS.Windows.Startup;

namespace StormOS.Windows;

/// <summary>Dependency injection registration for Windows platform services.</summary>
public static class WindowsServiceCollectionExtensions
{
    /// <summary>Registers Windows platform services.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddStormWindows(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IRegistryAccess, WindowsRegistryAccess>();
        services.TryAddSingleton<IOperatingSystemInfoProvider, WindowsVersion>();
        services.TryAddSingleton<IPowerPlanService, PowerPlanService>();
        services.TryAddSingleton<IStartupManager, StartupManager>();
        services.TryAddSingleton<IServiceConfigurator, ServiceConfigurator>();
        services.TryAddSingleton<IProcessController, ProcessController>();
        services.TryAddSingleton<IProcessInspector>(sp => new ProcessInspector(sp.GetService<IProcessGpuUsageProvider>(), sp.GetRequiredService<TimeProvider>()));
        services.TryAddSingleton<IMouseSettings, MouseSettings>();
        services.TryAddSingleton<IAuthenticodeVerifier, AuthenticodeVerifier>();
        services.TryAddSingleton<ISecretProtector, DpapiSecretProtector>();
        return services;
    }
}
