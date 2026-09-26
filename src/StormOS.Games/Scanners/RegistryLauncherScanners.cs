using Microsoft.Extensions.Logging;
using StormOS.Core.Games;
using StormOS.Windows.RegistryAccess;

namespace StormOS.Games.Scanners;

/// <summary>Detects Battle.net games through their uninstall entries (publisher "Blizzard Entertainment").</summary>
public sealed class BattleNetLibraryScanner(IRegistryAccess registry) : IGameLibraryScanner
{
    /// <inheritdoc />
    public LauncherKind Launcher => LauncherKind.BattleNet;

    /// <inheritdoc />
    public Task<IReadOnlyList<GameInfo>> ScanAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<GameInfo>>(UninstallRegistryReader.Read(registry)
            .Where(e => e.Publisher?.Contains("Blizzard", StringComparison.OrdinalIgnoreCase) == true && !e.DisplayName.Contains("Battle.net", StringComparison.OrdinalIgnoreCase))
            .Select(e => FromUninstall(e, LauncherKind.BattleNet))
            .ToList());

    /// <summary>Creates a game from an uninstall entry.</summary>
    /// <param name="entry">Entry.</param>
    /// <param name="launcher">Launcher.</param>
    /// <returns>The game.</returns>
    public static GameInfo FromUninstall(UninstallEntry entry, LauncherKind launcher)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var icon = entry.DisplayIcon?.Split(',')[0].Trim('"');
        return new GameInfo
        {
            GameId = GameInfo.CreateId(launcher, entry.Key),
            Name = entry.DisplayName,
            Publisher = entry.Publisher,
            Launcher = launcher,
            LauncherGameId = entry.Key,
            InstallPath = entry.InstallLocation,
            Version = entry.DisplayVersion,
            Executables = icon is not null && icon.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? [icon] : [],
        };
    }
}

/// <summary>Detects EA app games through their uninstall entries (publisher "Electronic Arts").</summary>
public sealed class EaLibraryScanner(IRegistryAccess registry) : IGameLibraryScanner
{
    /// <inheritdoc />
    public LauncherKind Launcher => LauncherKind.EA;

    /// <inheritdoc />
    public Task<IReadOnlyList<GameInfo>> ScanAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<GameInfo>>(UninstallRegistryReader.Read(registry)
            .Where(e => e.Publisher?.Contains("Electronic Arts", StringComparison.OrdinalIgnoreCase) == true
                && !e.DisplayName.StartsWith("EA app", StringComparison.OrdinalIgnoreCase)
                && !e.DisplayName.StartsWith("Origin", StringComparison.OrdinalIgnoreCase))
            .Select(e => BattleNetLibraryScanner.FromUninstall(e, LauncherKind.EA))
            .ToList());
}

/// <summary>Detects Ubisoft Connect games (HKLM\SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs).</summary>
public sealed class UbisoftLibraryScanner(IRegistryAccess registry) : IGameLibraryScanner
{
    private const string InstallsKey = @"SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs";

    /// <inheritdoc />
    public LauncherKind Launcher => LauncherKind.Ubisoft;

    /// <inheritdoc />
    public Task<IReadOnlyList<GameInfo>> ScanAsync(CancellationToken cancellationToken = default)
    {
        var games = new List<GameInfo>();
        foreach (var id in registry.GetSubKeyNames(RegistryHive.LocalMachine, InstallsKey))
        {
            if (registry.GetValue(RegistryHive.LocalMachine, InstallsKey + "\\" + id, "InstallDir")?.Value is not string dir || string.IsNullOrWhiteSpace(dir))
            {
                continue;
            }

            var path = dir.Replace('/', '\\').TrimEnd('\\');
            games.Add(new GameInfo
            {
                GameId = GameInfo.CreateId(LauncherKind.Ubisoft, id),
                Name = Path.GetFileName(path),
                Publisher = "Ubisoft",
                Launcher = LauncherKind.Ubisoft,
                LauncherGameId = id,
                InstallPath = path,
                LaunchUri = $"uplay://launch/{id}/0",
            });
        }

        return Task.FromResult<IReadOnlyList<GameInfo>>(games);
    }
}

/// <summary>Detects GOG games (HKLM\SOFTWARE\WOW6432Node\GOG.com\Games).</summary>
public sealed class GogLibraryScanner(IRegistryAccess registry) : IGameLibraryScanner
{
    private const string GamesKey = @"SOFTWARE\WOW6432Node\GOG.com\Games";

    /// <inheritdoc />
    public LauncherKind Launcher => LauncherKind.Gog;

    /// <inheritdoc />
    public Task<IReadOnlyList<GameInfo>> ScanAsync(CancellationToken cancellationToken = default)
    {
        var games = new List<GameInfo>();
        foreach (var id in registry.GetSubKeyNames(RegistryHive.LocalMachine, GamesKey))
        {
            var key = GamesKey + "\\" + id;
            if (registry.GetValue(RegistryHive.LocalMachine, key, "gameName")?.Value is not string name || registry.GetValue(RegistryHive.LocalMachine, key, "path")?.Value is not string path)
            {
                continue;
            }

            var exe = registry.GetValue(RegistryHive.LocalMachine, key, "exe")?.Value as string;
            games.Add(new GameInfo
            {
                GameId = GameInfo.CreateId(LauncherKind.Gog, id),
                Name = name,
                Launcher = LauncherKind.Gog,
                LauncherGameId = id,
                InstallPath = path,
                Executables = string.IsNullOrEmpty(exe) ? [] : [exe],
                Version = registry.GetValue(RegistryHive.LocalMachine, key, "ver")?.Value as string,
                LaunchUri = $"goggalaxy://openGameView/{id}",
            });
        }

        return Task.FromResult<IReadOnlyList<GameInfo>>(games);
    }
}

/// <summary>Detects Riot Client games from product_settings.yaml metadata files.</summary>
public sealed class RiotLibraryScanner : IGameLibraryScanner
{
    private static readonly Dictionary<string, (string Name, string[] Executables)> Products = new(StringComparer.OrdinalIgnoreCase)
    {
        ["valorant"] = ("VALORANT", ["ShooterGame\\Binaries\\Win64\\VALORANT-Win64-Shipping.exe"]),
        ["league_of_legends"] = ("League of Legends", ["Game\\League of Legends.exe"]),
        ["bacon"] = ("Legends of Runeterra", ["LoR.exe"]),
    };

    private readonly ILogger<RiotLibraryScanner> _logger;
    private readonly string _metadataDirectory;

    /// <summary>Initializes a new instance of the <see cref="RiotLibraryScanner"/> class.</summary>
    /// <param name="logger">Logger.</param>
    /// <param name="metadataDirectory">Optional metadata directory override (tests).</param>
    public RiotLibraryScanner(ILogger<RiotLibraryScanner> logger, string? metadataDirectory = null)
    {
        _logger = logger;
        _metadataDirectory = metadataDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Riot Games", "Metadata");
    }

    /// <inheritdoc />
    public LauncherKind Launcher => LauncherKind.Riot;

    /// <inheritdoc />
    public Task<IReadOnlyList<GameInfo>> ScanAsync(CancellationToken cancellationToken = default)
    {
        var games = new List<GameInfo>();
        if (!Directory.Exists(_metadataDirectory))
        {
            return Task.FromResult<IReadOnlyList<GameInfo>>(games);
        }

        foreach (var file in Directory.EnumerateFiles(_metadataDirectory, "*.product_settings.yaml", SearchOption.AllDirectories))
        {
            try
            {
                var product = Path.GetFileName(file).Split('.')[0];
                var path = ReadYamlValue(File.ReadLines(file), "product_install_full_path");
                if (path is null || !Products.TryGetValue(product, out var known))
                {
                    continue;
                }

                games.Add(new GameInfo
                {
                    GameId = GameInfo.CreateId(LauncherKind.Riot, product),
                    Name = known.Name,
                    Publisher = "Riot Games",
                    Launcher = LauncherKind.Riot,
                    LauncherGameId = product,
                    InstallPath = path.Replace('/', '\\'),
                    Executables = known.Executables.Select(e => Path.Combine(path.Replace('/', '\\'), e)).ToList(),
                });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogDebug(ex, "Skipping {File}", file);
            }
        }

        return Task.FromResult<IReadOnlyList<GameInfo>>(games);
    }

    /// <summary>Reads a top-level scalar from a simple YAML file.</summary>
    /// <param name="lines">Lines.</param>
    /// <param name="key">Key.</param>
    /// <returns>The value or <see langword="null"/>.</returns>
    public static string? ReadYamlValue(IEnumerable<string> lines, string key)
    {
        ArgumentNullException.ThrowIfNull(lines);
        foreach (var line in lines)
        {
            if (!line.StartsWith(key + ":", StringComparison.Ordinal))
            {
                continue;
            }

            var value = line[(key.Length + 1)..].Trim().Trim('"', '\'');
            return value.Length > 0 ? value : null;
        }

        return null;
    }
}
