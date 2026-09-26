using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StormOS.Core.Games;
using StormOS.Core.Telemetry;
using StormOS.Games.Detection;
using StormOS.Games.Profiles;
using StormOS.Games.Scanners;

namespace StormOS.Games;

/// <summary>Dependency injection registration for game detection and profiles.</summary>
public static class GamesServiceCollectionExtensions
{
    /// <summary>Registers scanners, the registry, profiles and the running game detector.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="profileDirectories">Profile directories in increasing precedence.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddStormGames(this IServiceCollection services, IEnumerable<string> profileDirectories)
    {
        var directories = profileDirectories.ToList();
        services.Configure<GameProfileOptions>(o => o.Directories = directories);
        services.TryAddSingleton<GameProfileRepository>();
        services.TryAddSingleton<IGameProfileRepository>(sp => sp.GetRequiredService<GameProfileRepository>());
        services.AddSingleton<IGameLibraryScanner, SteamLibraryScanner>(sp => ActivatorUtilities.CreateInstance<SteamLibraryScanner>(sp));
        services.AddSingleton<IGameLibraryScanner, EpicLibraryScanner>(sp => ActivatorUtilities.CreateInstance<EpicLibraryScanner>(sp));
        services.AddSingleton<IGameLibraryScanner, XboxLibraryScanner>(sp => ActivatorUtilities.CreateInstance<XboxLibraryScanner>(sp));
        services.AddSingleton<IGameLibraryScanner, RiotLibraryScanner>(sp => ActivatorUtilities.CreateInstance<RiotLibraryScanner>(sp));
        services.AddSingleton<IGameLibraryScanner, BattleNetLibraryScanner>();
        services.AddSingleton<IGameLibraryScanner, EaLibraryScanner>();
        services.AddSingleton<IGameLibraryScanner, UbisoftLibraryScanner>();
        services.AddSingleton<IGameLibraryScanner, GogLibraryScanner>();
        services.TryAddSingleton<IGameRegistry, GameRegistry>();
        services.TryAddSingleton<IProcessSource, WindowsProcessSource>();
        services.TryAddSingleton(new GameDetectionOptions());
        services.TryAddSingleton<RunningGameDetector>();
        services.TryAddSingleton<IRunningGameDetector>(sp => sp.GetRequiredService<RunningGameDetector>());
        services.TryAddSingleton<IActiveGameProvider>(sp => sp.GetRequiredService<RunningGameDetector>());
        return services;
    }
}
