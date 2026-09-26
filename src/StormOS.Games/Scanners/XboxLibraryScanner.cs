using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using StormOS.Core.Games;
using StormOS.Games.Parsing;

namespace StormOS.Games.Scanners;

/// <summary>
/// Scans PC Game Pass / Microsoft Store games installed by the Xbox app. Library roots are found through the
/// ".GamingRoot" marker on each fixed drive; each game folder contains Content\MicrosoftGame.config.
/// </summary>
public sealed class XboxLibraryScanner : IGameLibraryScanner
{
    private readonly ILogger<XboxLibraryScanner> _logger;
    private readonly IReadOnlyList<string>? _rootsOverride;

    /// <summary>Initializes a new instance of the <see cref="XboxLibraryScanner"/> class.</summary>
    /// <param name="logger">Logger.</param>
    /// <param name="rootsOverride">Optional library roots (tests).</param>
    public XboxLibraryScanner(ILogger<XboxLibraryScanner> logger, IReadOnlyList<string>? rootsOverride = null)
    {
        _logger = logger;
        _rootsOverride = rootsOverride;
    }

    /// <inheritdoc />
    public LauncherKind Launcher => LauncherKind.Xbox;

    /// <inheritdoc />
    public Task<IReadOnlyList<GameInfo>> ScanAsync(CancellationToken cancellationToken = default)
    {
        var games = new List<GameInfo>();
        foreach (var root in _rootsOverride ?? FindRoots())
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var folder in Directory.EnumerateDirectories(root))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var config = Path.Combine(folder, "Content", "MicrosoftGame.config");
                if (!File.Exists(config))
                {
                    continue;
                }

                try
                {
                    if (FromConfig(XDocument.Load(config), folder) is { } game)
                    {
                        games.Add(game);
                    }
                }
                catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException)
                {
                    _logger.LogDebug(ex, "Skipping unreadable {Config}", config);
                }
            }
        }

        return Task.FromResult<IReadOnlyList<GameInfo>>(games);
    }

    /// <summary>Converts a MicrosoftGame.config document to a game.</summary>
    /// <param name="document">The document.</param>
    /// <param name="folder">The game folder.</param>
    /// <returns>The game or <see langword="null"/>.</returns>
    public static GameInfo? FromConfig(XDocument document, string folder)
    {
        ArgumentNullException.ThrowIfNull(document);
        var game = document.Root;
        if (game is null)
        {
            return null;
        }

        var identity = game.Element("Identity");
        var identityName = identity?.Attribute("Name")?.Value;
        if (string.IsNullOrEmpty(identityName))
        {
            return null;
        }

        var visuals = game.Element("ShellVisuals");
        var displayName = visuals?.Attribute("DefaultDisplayName")?.Value;
        if (string.IsNullOrWhiteSpace(displayName) || displayName.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase))
        {
            displayName = Path.GetFileName(folder);
        }

        var content = Path.Combine(folder, "Content");
        var executables = game.Element("ExecutableList")?.Elements("Executable")
            .Select(e => e.Attribute("Name")?.Value)
            .Where(n => !string.IsNullOrEmpty(n) && !n!.Contains("..", StringComparison.Ordinal))
            .Select(n => Path.Combine(content, n!))
            .ToList() ?? [];

        return new GameInfo
        {
            GameId = GameInfo.CreateId(LauncherKind.Xbox, identityName),
            Name = displayName,
            Publisher = visuals?.Attribute("PublisherDisplayName")?.Value is { } publisher && !publisher.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase) ? publisher : null,
            Launcher = LauncherKind.Xbox,
            LauncherGameId = identityName,
            InstallPath = content,
            Executables = executables,
            Version = identity?.Attribute("Version")?.Value,
        };
    }

    private IEnumerable<string> FindRoots()
    {
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
        {
            var marker = Path.Combine(drive.RootDirectory.FullName, ".GamingRoot");
            IReadOnlyList<string> relative;
            try
            {
                relative = File.Exists(marker) ? GamingRootReader.Parse(File.ReadAllBytes(marker)) : [];
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogDebug(ex, "Could not read {Marker}", marker);
                continue;
            }

            foreach (var path in relative)
            {
                yield return Path.Combine(drive.RootDirectory.FullName, path);
            }
        }
    }
}
