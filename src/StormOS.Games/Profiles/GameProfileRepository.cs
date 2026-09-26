using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StormOS.Core.Common;
using StormOS.Core.Games;
using StormOS.Games.Parsing;

namespace StormOS.Games.Profiles;

/// <summary>Profile loading options.</summary>
public sealed class GameProfileOptions
{
    /// <summary>Gets or sets profile directories in increasing precedence (later directories override earlier ones).</summary>
    public IList<string> Directories { get; set; } = [];
}

/// <summary>Loads, validates and matches data-driven game profiles from JSON files.</summary>
public sealed class GameProfileRepository : IGameProfileRepository
{
    private readonly GameProfileOptions _options;
    private readonly ILogger<GameProfileRepository> _logger;
    private readonly Lock _gate = new();
    private IReadOnlyList<GameProfile> _profiles = [];
    private IReadOnlyList<string> _errors = [];

    /// <summary>Initializes a new instance of the <see cref="GameProfileRepository"/> class.</summary>
    /// <param name="options">Options.</param>
    /// <param name="logger">Logger.</param>
    public GameProfileRepository(IOptions<GameProfileOptions> options, ILogger<GameProfileRepository> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _logger = logger;
        Reload();
    }

    /// <inheritdoc />
    public IReadOnlyList<GameProfile> Profiles
    {
        get
        {
            lock (_gate)
            {
                return _profiles;
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> LoadErrors
    {
        get
        {
            lock (_gate)
            {
                return _errors;
            }
        }
    }

    /// <summary>Parses and validates a profile document.</summary>
    /// <param name="json">JSON text.</param>
    /// <param name="profile">The profile.</param>
    /// <returns>Validation errors; empty on success.</returns>
    public static IReadOnlyList<string> TryParse(string json, out GameProfile? profile)
    {
        profile = null;
        try
        {
            profile = JsonSerializer.Deserialize<GameProfile>(json, StormJson.Lenient);
        }
        catch (JsonException ex)
        {
            return [$"Invalid JSON: {ex.Message}"];
        }

        return profile is null ? ["The document is empty."] : GameProfileValidator.Validate(profile);
    }

    /// <summary>Tests whether a process matches an executable matcher.</summary>
    /// <param name="match">Matcher.</param>
    /// <param name="executablePath">Full path or file name.</param>
    /// <param name="commandLine">Command line, when known.</param>
    /// <returns><see langword="true"/> when matching.</returns>
    public static bool Matches(ExecutableMatch match, string executablePath, string? commandLine)
    {
        ArgumentNullException.ThrowIfNull(match);
        var fileName = Path.GetFileName(executablePath.Replace('/', '\\').Split('\\')[^1]);
        if (!WildcardMatcher.IsMatch(match.Name, fileName))
        {
            return false;
        }

        if (match.PathContains is { Length: > 0 } fragment && !executablePath.Replace('/', '\\').Contains(fragment.Replace('/', '\\'), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return match.CommandLineContains is not { Length: > 0 } required || (commandLine?.Contains(required, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    /// <inheritdoc />
    public GameProfile? Find(string profileId) => Profiles.FirstOrDefault(p => string.Equals(p.Id, profileId, StringComparison.Ordinal));

    /// <inheritdoc />
    public GameProfile? MatchGame(GameInfo game)
    {
        ArgumentNullException.ThrowIfNull(game);
        foreach (var profile in Profiles)
        {
            if (profile.Detection.Launchers.Any(l => l.Launcher == game.Launcher && string.Equals(l.GameId, game.LauncherGameId, StringComparison.OrdinalIgnoreCase)))
            {
                return profile;
            }
        }

        foreach (var profile in Profiles)
        {
            if (game.Executables.Any(exe => profile.Detection.Executables.Any(m => m.CommandLineContains is null && Matches(m, exe, null))))
            {
                return profile;
            }
        }

        return null;
    }

    /// <inheritdoc />
    public GameProfile? MatchProcess(string executablePath, string? commandLine)
    {
        ArgumentException.ThrowIfNullOrEmpty(executablePath);
        return Profiles.FirstOrDefault(p => p.Detection.Executables.Any(m => Matches(m, executablePath, commandLine)));
    }

    /// <summary>Determines whether any profile needs the command line to match this executable.</summary>
    /// <param name="executablePath">Executable path or name.</param>
    /// <returns><see langword="true"/> when a command line check is needed.</returns>
    public bool NeedsCommandLine(string executablePath) =>
        Profiles.Any(p => p.Detection.Executables.Any(m => m.CommandLineContains is not null && WildcardMatcher.IsMatch(m.Name, Path.GetFileName(executablePath))));

    /// <inheritdoc />
    public void Reload()
    {
        var byId = new Dictionary<string, GameProfile>(StringComparer.Ordinal);
        var errors = new List<string>();
        foreach (var directory in _options.Directories)
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(directory, "*.json").OrderBy(f => f, StringComparer.Ordinal))
            {
                try
                {
                    var fileInfo = new FileInfo(file);
                    if (fileInfo.Length > 256 * 1024)
                    {
                        errors.Add($"{fileInfo.Name}: file is too large.");
                        continue;
                    }

                    var problems = TryParse(File.ReadAllText(file), out var profile);
                    if (problems.Count > 0 || profile is null)
                    {
                        errors.AddRange(problems.Select(p => $"{fileInfo.Name}: {p}"));
                        continue;
                    }

                    byId[profile.Id] = profile;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    errors.Add($"{Path.GetFileName(file)}: could not be read.");
                    _logger.LogWarning(ex, "Could not read profile {File}", file);
                }
            }
        }

        foreach (var error in errors)
        {
            _logger.LogWarning("Profile rejected: {Error}", error);
        }

        lock (_gate)
        {
            _profiles = byId.Values.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
            _errors = errors;
        }

        _logger.LogInformation("Loaded {Count} game profiles", byId.Count);
    }
}
