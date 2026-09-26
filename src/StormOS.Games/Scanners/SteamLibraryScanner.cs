using System.Globalization;
using Microsoft.Extensions.Logging;
using StormOS.Core.Games;
using StormOS.Games.Parsing;
using StormOS.Windows.RegistryAccess;

namespace StormOS.Games.Scanners;

/// <summary>Scans Steam libraries (libraryfolders.vdf and appmanifest_*.acf).</summary>
public sealed class SteamLibraryScanner : IGameLibraryScanner
{
    private static readonly HashSet<string> NonGameAppIds = ["228980", "250820", "1070560", "1391110", "1628350", "1493710", "2180100"];
    private readonly IRegistryAccess _registry;
    private readonly ILogger<SteamLibraryScanner> _logger;
    private readonly string? _steamRootOverride;

    /// <summary>Initializes a new instance of the <see cref="SteamLibraryScanner"/> class.</summary>
    /// <param name="registry">Registry access.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="steamRootOverride">Optional Steam root used instead of the registry (tests).</param>
    public SteamLibraryScanner(IRegistryAccess registry, ILogger<SteamLibraryScanner> logger, string? steamRootOverride = null)
    {
        _registry = registry;
        _logger = logger;
        _steamRootOverride = steamRootOverride;
    }

    /// <inheritdoc />
    public LauncherKind Launcher => LauncherKind.Steam;

    /// <inheritdoc />
    public Task<IReadOnlyList<GameInfo>> ScanAsync(CancellationToken cancellationToken = default)
    {
        var root = _steamRootOverride ?? FindSteamRoot();
        if (root is null || !Directory.Exists(root))
        {
            return Task.FromResult<IReadOnlyList<GameInfo>>([]);
        }

        var games = new Dictionary<string, GameInfo>(StringComparer.Ordinal);
        foreach (var library in GetLibraries(root))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var steamApps = Path.Combine(library, "steamapps");
            if (!Directory.Exists(steamApps))
            {
                continue;
            }

            foreach (var manifest in Directory.EnumerateFiles(steamApps, "appmanifest_*.acf"))
            {
                if (TryReadManifest(manifest, steamApps) is { } game)
                {
                    games.TryAdd(game.GameId, game);
                }
            }
        }

        return Task.FromResult<IReadOnlyList<GameInfo>>(games.Values.ToList());
    }

    /// <summary>Reads library paths from libraryfolders.vdf, always including the Steam root.</summary>
    /// <param name="steamRoot">Steam installation directory.</param>
    /// <returns>Library directories.</returns>
    public static IReadOnlyList<string> GetLibraries(string steamRoot)
    {
        var libraries = new List<string> { steamRoot };
        var file = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(file))
        {
            return libraries;
        }

        var root = VdfParser.Parse(File.ReadAllText(file)).Node("libraryfolders");
        if (root is null)
        {
            return libraries;
        }

        foreach (var (_, node) in root.Nodes())
        {
            if (node.Value("path") is { Length: > 0 } path && !libraries.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                libraries.Add(path);
            }
        }

        return libraries;
    }

    /// <summary>Converts an appmanifest to a game.</summary>
    /// <param name="manifest">Parsed manifest root.</param>
    /// <param name="steamApps">The steamapps directory containing it.</param>
    /// <returns>The game or <see langword="null"/> when it is not an installed game.</returns>
    public static GameInfo? FromManifest(VdfNode manifest, string steamApps)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var state = manifest.Node("AppState");
        var appId = state?.Value("appid");
        var name = state?.Value("name");
        var installDir = state?.Value("installdir");
        if (state is null || string.IsNullOrEmpty(appId) || string.IsNullOrEmpty(name) || string.IsNullOrEmpty(installDir) || NonGameAppIds.Contains(appId))
        {
            return null;
        }

        var flags = long.TryParse(state.Value("StateFlags"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var f) ? f : 0;
        DateTimeOffset? lastPlayed = long.TryParse(state.Value("LastPlayed"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var lp) && lp > 0
            ? DateTimeOffset.FromUnixTimeSeconds(lp)
            : null;
        return new GameInfo
        {
            GameId = GameInfo.CreateId(LauncherKind.Steam, appId),
            Name = name,
            Launcher = LauncherKind.Steam,
            LauncherGameId = appId,
            InstallPath = Path.Combine(steamApps, "common", installDir),
            Version = state.Value("buildid"),
            SizeBytes = long.TryParse(state.Value("SizeOnDisk"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var size) ? size : null,
            LaunchUri = $"steam://rungameid/{appId}",
            IsInstalled = (flags & 4) != 0,
            LastPlayed = lastPlayed,
        };
    }

    private GameInfo? TryReadManifest(string manifest, string steamApps)
    {
        try
        {
            return FromManifest(VdfParser.Parse(File.ReadAllText(manifest)), steamApps);
        }
        catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Skipping unreadable Steam manifest {Manifest}", manifest);
            return null;
        }
    }

    private string? FindSteamRoot() =>
        (_registry.GetValue(RegistryHive.CurrentUser, @"Software\Valve\Steam", "SteamPath")?.Value as string)?.Replace('/', '\\')
        ?? _registry.GetValue(RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath")?.Value as string;
}
