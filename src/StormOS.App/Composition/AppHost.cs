using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Serilog;
using StormOS.App.Services;
using StormOS.App.ViewModels;
using StormOS.Benchmark;
using StormOS.Core.History;
using StormOS.Core.Optimization;
using StormOS.Core.Settings;
using StormOS.Games;
using StormOS.Hardware;
using StormOS.Infrastructure.Configuration;
using StormOS.Infrastructure.Ipc;
using StormOS.Infrastructure.Logging;
using StormOS.Infrastructure.Paths;
using StormOS.Infrastructure.Persistence;
using StormOS.Infrastructure.Settings;
using StormOS.Network;
using StormOS.Optimization;
using StormOS.Optimization.Engine;
using StormOS.Overlay;
using StormOS.Performance;
using StormOS.Services;
using StormOS.Services.Licensing;
using StormOS.Windows;
using StormOS.Windows.Platform;
using StormOS.Windows.Security;

namespace StormOS.App.Composition;

/// <summary>Composition root of the desktop app (user context, non-elevated).</summary>
internal static class AppHost
{
    public static ServiceProvider Build(DispatcherQueue dispatcher)
    {
        var paths = new StormPaths(StormProcessKind.User, Environment.GetEnvironmentVariable("STORMOS_DATA_ROOT"));
        paths.EnsureCreated(StormProcessKind.User);
        var configuration = StormConfiguration.Build(AppContext.BaseDirectory);
        var logger = StormLogging.Create(configuration, paths.Logs, LogCategories.Application, developerLogging: false);

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging(builder => builder.ClearProviders().AddSerilog(logger, dispose: true));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IStormPaths>(paths);
        services.AddSingleton(new SqliteDatabase(Path.Combine(paths.UserData, "storm.db")));
        services.AddSingleton<IHistoryStore, SqliteHistoryStore>();
        services.AddSingleton<IOptimizationJournal, SqliteOptimizationJournal>();
        services.AddSingleton<ISettingsStore, SqliteSettingsStore>();
        services.AddSingleton<SecureValueStore>();
        services.AddSingleton<KeyValueStore>();
        services.AddSingleton<IPipeServerVerifier>(new WindowsPipeServerVerifier(allowUnverified: StormConfiguration.IsDevelopment));

        services.AddStormWindows();
        services.AddStormHardware();
        services.AddStormNetwork();
        services.AddStormPerformance(configuration, includeFrameCapture: false);
        services.AddStormGames([paths.BundledProfiles, paths.ProfileOverrides]);
        services.AddStormOptimization(new OptimizationExecutor(OptimizationExecutor.App, Elevation.IsElevated, WindowsVersion.GetBuildNumber()));
        services.AddStormBenchmarks(includeGaming: false);
        services.AddStormApplicationServices(configuration, "StormOS.App");

        services.AddSingleton(new UiDispatcher(dispatcher));
        services.AddSingleton<NotificationService>();
        services.AddSingleton<DialogService>();
        services.AddSingleton<ChangeService>();
        services.AddSingleton<NavigationService>();
        services.AddSingleton<TelemetryFeed>();
        services.AddSingleton<OverlayWindow>();
        services.AddSingleton<OverlayController>();
        services.AddSingleton<ThemeService>();

        services.AddSingleton<ShellViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<GamesViewModel>();
        services.AddTransient<PerformanceViewModel>();
        services.AddTransient<BenchmarkViewModel>();
        services.AddTransient<OptimizerViewModel>();
        services.AddTransient<NetworkViewModel>();
        services.AddTransient<ProcessesViewModel>();
        services.AddTransient<StartupViewModel>();
        services.AddTransient<PowerViewModel>();
        services.AddTransient<OverlayViewModel>();
        services.AddTransient<DevicesViewModel>();
        services.AddTransient<HistoryViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<OnboardingViewModel>();
        services.AddSingleton<MainWindow>();
        return services.BuildServiceProvider();
    }

    public static async Task InitializeAsync(IServiceProvider services)
    {
        var logger = services.GetRequiredService<ILogger<App>>();
        await services.GetRequiredService<SqliteDatabase>().InitializeAsync();
        var settings = await services.GetRequiredService<ISettingsStore>().LoadAsync();
        await services.GetRequiredService<EntitlementService>().LoadAsync();
        services.GetRequiredService<TelemetryFeed>().Start();
        if (settings.Games.AutoDetect)
        {
            services.GetRequiredService<StormOS.Games.Detection.RunningGameDetector>().Start();
            services.GetRequiredService<StormOS.Optimization.Sessions.ProfileSessionOptimizer>().Start();
        }

        services.GetRequiredService<OverlayController>().Initialize(settings.Overlay);
        logger.LogInformation("STORM OS started (mock mode compiled in: {Mock})", MockModeGuard.IsCompiledIn);
    }
}
