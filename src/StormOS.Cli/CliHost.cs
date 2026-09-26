using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
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
using StormOS.Performance;
using StormOS.Services;
using StormOS.Windows;
using StormOS.Windows.Platform;
using StormOS.Windows.Security;

namespace StormOS.Cli;

/// <summary>Builds the CLI's service provider (user context, same local database as the desktop app).</summary>
internal static class CliHost
{
    public static ServiceProvider Build()
    {
        var paths = new StormPaths(StormProcessKind.User, Environment.GetEnvironmentVariable("STORMOS_DATA_ROOT"));
        paths.EnsureCreated(StormProcessKind.User);
        var configuration = StormConfiguration.Build(AppContext.BaseDirectory);
        var logger = StormLogging.Create(configuration, paths.Logs, LogCategories.Application, developerLogging: false);

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging(builder => builder.ClearProviders().AddSerilog(logger, dispose: true));
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
        StormOS.Benchmark.BenchmarkServiceCollectionExtensions.AddStormBenchmarks(services, includeGaming: false);
        services.AddStormApplicationServices(configuration, "storm-cli");
        return services.BuildServiceProvider();
    }
}
