using System.Text.Json;
using Microsoft.Extensions.Logging;
using StormOS.Core.Games;

namespace StormOS.Games.Scanners;

/// <summary>Scans Epic Games Launcher manifests (%ProgramData%\Epic\EpicGamesLauncher\Data\Manifests\*.item).</summary>
public sealed class EpicLibraryScanner : IGameLibraryScanner
{
    private readonly ILogger<EpicLibraryScanner> _logger;
    private readonly string _manifestDirectory;

    /// <summary>Initializes a new instance of the <see cref="EpicLibraryScanner"/> class.</summary>
    /// <param name="logger">Logger.</param>
    /// <param name="manifestDirectory">Optional manifest directory override (tests).</param>
    public EpicLibraryScanner(ILogger<EpicLibraryScanner> logger, string? manifestDirectory = null)
    {
        _logger = logger;
        _manifestDirectory = manifestDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests");
    }

    /// <inheritdoc />
    public LauncherKind Launcher => LauncherKind.Epic;

    /// <inheritdoc />
    public async Task<IReadOnlyList<GameInfo>> ScanAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_manifestDirectory))
        {
            return [];
        }

        var games = new List<GameInfo>();
        foreach (var file in Directory.EnumerateFiles(_manifestDirectory, "*.item"))
        {
            try
            {
                await using var stream = File.OpenRead(file);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
                if (FromManifest(document.RootElement) is { } game)
                {
                    games.Add(game);
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                _logger.LogDebug(ex, "Skipping unreadable Epic manifest {File}", file);
            }
        }

        return games;
    }

    /// <summary>Converts an Epic manifest to a game.</summary>
    /// <param name="root">Manifest JSON.</param>
    /// <returns>The game or <see langword="null"/>.</returns>
    public static GameInfo? FromManifest(JsonElement root)
    {
        static string? Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        var appName = Str(root, "AppName");
        var displayName = Str(root, "DisplayName");
        var location = Str(root, "InstallLocation");
        if (string.IsNullOrEmpty(appName) || string.IsNullOrEmpty(displayName) || string.IsNullOrEmpty(location))
        {
            return null;
        }

        if (root.TryGetProperty("bIsIncompleteInstall", out var incomplete) && incomplete.ValueKind == JsonValueKind.True)
        {
            return null;
        }

        var executable = Str(root, "LaunchExecutable");
        return new GameInfo
        {
            GameId = GameInfo.CreateId(LauncherKind.Epic, appName),
            Name = displayName,
            Launcher = LauncherKind.Epic,
            LauncherGameId = appName,
            InstallPath = location,
            Executables = string.IsNullOrEmpty(executable) ? [] : [Path.Combine(location, executable)],
            Version = Str(root, "AppVersionString"),
            SizeBytes = root.TryGetProperty("InstallSize", out var size) && size.TryGetInt64(out var bytes) ? bytes : null,
            LaunchUri = $"com.epicgames.launcher://apps/{Uri.EscapeDataString(appName)}?action=launch&silent=true",
        };
    }
}
