using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StormOS.Core.Licensing;
using StormOS.Core.Scan;
using StormOS.Infrastructure.Ipc;
using StormOS.Services.Analysis;
using StormOS.Services.Client;
using StormOS.Services.Cloud;
using StormOS.Services.History;
using StormOS.Services.Licensing;
using StormOS.Services.Onboarding;
using StormOS.Services.Optimization;
using StormOS.Services.Scan;
using StormOS.Services.Updates;

namespace StormOS.Services;

/// <summary>Dependency injection registration for application services.</summary>
public static class ServicesServiceCollectionExtensions
{
    /// <summary>Registers the service client, coordinators, scan, analysis, history, cloud, licensing and updates.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Configuration.</param>
    /// <param name="clientName">Client name for the IPC handshake.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddStormApplicationServices(this IServiceCollection services, IConfiguration configuration, string clientName)
    {
        services.TryAddSingleton(new IpcClientOptions());
        services.TryAddSingleton<IpcClient>();
        services.TryAddSingleton(sp => ActivatorUtilities.CreateInstance<StormServiceClient>(sp, clientName));
        services.TryAddSingleton<IStormServiceClient>(sp => sp.GetRequiredService<StormServiceClient>());
        services.TryAddSingleton<OptimizationCoordinator>();

        services.AddSingleton<ISystemScanCheck, OperatingSystemScanCheck>();
        services.AddSingleton<ISystemScanCheck, HardwareScanCheck>();
        services.AddSingleton<ISystemScanCheck, StorageScanCheck>();
        services.AddSingleton<ISystemScanCheck, PowerScanCheck>();
        services.AddSingleton<ISystemScanCheck, StartupScanCheck>();
        services.AddSingleton<ISystemScanCheck, PerformanceScanCheck>();
        services.AddSingleton<ISystemScanCheck, NetworkScanCheck>();
        services.AddSingleton<ISystemScanCheck, GamingSettingsScanCheck>();
        services.TryAddSingleton<SystemScanService>();
        services.TryAddSingleton<SystemCheckService>();

        services.TryAddSingleton<RuleBasedAnalysisEngine>();
        services.TryAddSingleton<AnalysisService>();
        services.TryAddSingleton<HistoryService>();

        services.AddHttpClient(CloudClient.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(30));
        services.TryAddSingleton<CloudClient>();
        services.AddOptions<LicensingOptions>().Bind(configuration.GetSection(LicensingOptions.SectionName));
        services.TryAddSingleton<EntitlementService>();
        services.TryAddSingleton<IEntitlementService>(sp => sp.GetRequiredService<EntitlementService>());
        services.TryAddSingleton<UpdateService>();
        return services;
    }
}
