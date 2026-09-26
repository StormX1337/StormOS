using Microsoft.Extensions.DependencyInjection.Extensions;
using StormOS.Benchmark;
using StormOS.Core.History;
using StormOS.Core.Optimization;
using StormOS.Core.Settings;
using StormOS.Games;
using StormOS.Hardware;
using StormOS.Infrastructure.Ipc;
using StormOS.Infrastructure.Paths;
using StormOS.Infrastructure.Persistence;
using StormOS.Infrastructure.Settings;
using StormOS.Network;
using StormOS.Optimization;
using StormOS.Optimization.Engine;
using StormOS.Performance;
using StormOS.Security.Ipc;
using StormOS.Service.Handlers;
using StormOS.Windows;
using StormOS.Windows.Platform;
using StormOS.Windows.Security;

namespace StormOS.Service.Hosting;

/// <summary>Composes the service's dependency graph.</summary>
public static class ServiceComposition
{
    /// <summary>Executable names allowed to act as trusted clients.</summary>
    public static readonly string[] TrustedClientImages = ["StormOS.exe", "storm.exe"];

    /// <summary>Registers all service components.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Configuration.</param>
    /// <param name="paths">Paths.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddStormService(this IServiceCollection services, IConfiguration configuration, StormPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        services.AddSingleton<IStormPaths>(paths);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(new SqliteDatabase(Path.Combine(paths.MachineData, "service.db")));
        services.AddSingleton<IHistoryStore, SqliteHistoryStore>();
        services.AddSingleton<IOptimizationJournal, SqliteOptimizationJournal>();
        services.AddSingleton<ISettingsStore, SqliteSettingsStore>();

        services.AddStormWindows();
        services.AddStormHardware();
        services.AddStormNetwork();
        services.AddStormPerformance(configuration, includeFrameCapture: true);
        services.AddStormGames([paths.BundledProfiles, paths.ProfileOverrides]);
        services.AddStormOptimization(new OptimizationExecutor(OptimizationExecutor.Service, Elevation.IsElevated, WindowsVersion.GetBuildNumber()));
        services.AddStormBenchmarks(includeGaming: true);

        services.AddSingleton<ServiceRuntime>();
        services.AddSingleton(sp =>
        {
            var serviceExe = Path.Combine(AppContext.BaseDirectory, "StormOS.Service.exe");
            var verifier = sp.GetRequiredService<IAuthenticodeVerifier>();
            var signer = File.Exists(serviceExe) ? verifier.GetTrustedSignerThumbprint(serviceExe) : null;
            return new ClientTrustEvaluator(AppContext.BaseDirectory, TrustedClientImages, verifier, signer);
        });
        services.TryAddSingleton<IClientIdentityResolver, WindowsClientIdentityResolver>();
        services.TryAddSingleton<IPipeServerFactory, WindowsPipeServerFactory>();
        services.AddSingleton(configuration.GetSection("Ipc").Get<IpcServerOptions>() ?? new IpcServerOptions());

        foreach (var handler in typeof(ServiceComposition).Assembly.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IIpcOperationHandler).IsAssignableFrom(t)))
        {
            services.AddSingleton(typeof(IIpcOperationHandler), handler);
        }

        services.AddSingleton<IpcDispatcher>();
        services.AddSingleton<IpcServer>();
        services.AddHostedService<IpcHostedService>();
        services.AddHostedService<GameMonitorHostedService>();
        return services;
    }
}
