using StormOS.Core.Processes;

namespace StormOS.Core.Games;

/// <summary>
/// A data-driven, versioned game profile loaded from JSON (see docs/PROFILES.md).
/// Profiles only reference registered optimization rules; they cannot contain executable code.
/// </summary>
public sealed record GameProfile
{
    /// <summary>The profile schema version understood by this build.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>Gets the schema version of the document.</summary>
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>Gets the profile id, for example "cs2".</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Gets the semantic version of the profile content.</summary>
    public string Version { get; init; } = "1.0.0";

    /// <summary>Gets the game display name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Gets the publisher.</summary>
    public string? Publisher { get; init; }

    /// <summary>Gets the anti-cheat used by the game, for information only.</summary>
    public string? AntiCheat { get; init; }

    /// <summary>Gets the detection rules.</summary>
    public ProfileDetection Detection { get; init; } = new();

    /// <summary>Gets launch information.</summary>
    public ProfileLaunch? Launch { get; init; }

    /// <summary>Gets process settings applied while the game runs.</summary>
    public ProfileProcessSettings? Process { get; init; }

    /// <summary>Gets recommended system level rules.</summary>
    public IReadOnlyList<ProfileRuleReference> SystemRecommendations { get; init; } = [];

    /// <summary>Gets optimization rules offered for this game.</summary>
    public IReadOnlyList<ProfileRuleReference> OptimizationRules { get; init; } = [];

    /// <summary>Gets graphics settings guidance.</summary>
    public IReadOnlyList<GuidanceItem> GraphicsGuidance { get; init; } = [];

    /// <summary>Gets network guidance.</summary>
    public IReadOnlyList<GuidanceItem> NetworkGuidance { get; init; } = [];

    /// <summary>Gets rollback behaviour.</summary>
    public ProfileRollback Rollback { get; init; } = new();

    /// <summary>Gets benchmark configuration.</summary>
    public ProfileBenchmark? Benchmark { get; init; }

    /// <summary>Gets the minimum supported Windows build.</summary>
    public int MinWindowsBuild { get; init; } = 19041;

    /// <summary>Gets the supported game versions, "*" for any.</summary>
    public string SupportedGameVersions { get; init; } = "*";
}

/// <summary>How a profile recognises its game.</summary>
public sealed record ProfileDetection
{
    /// <summary>Gets executable matchers.</summary>
    public IReadOnlyList<ExecutableMatch> Executables { get; init; } = [];

    /// <summary>Gets launcher id matchers.</summary>
    public IReadOnlyList<LauncherMatch> Launchers { get; init; } = [];
}

/// <summary>Matches a process executable.</summary>
/// <param name="Name">Executable file name; supports '*' and '?' wildcards.</param>
/// <param name="PathContains">Optional path fragment that must be part of the full path.</param>
/// <param name="CommandLineContains">Optional command line fragment (for example for Java based games).</param>
public sealed record ExecutableMatch(string Name, string? PathContains = null, string? CommandLineContains = null);

/// <summary>Matches a launcher library entry.</summary>
/// <param name="Launcher">The launcher.</param>
/// <param name="GameId">The launcher specific id.</param>
public sealed record LauncherMatch(LauncherKind Launcher, string GameId);

/// <summary>Launch information.</summary>
public sealed record ProfileLaunch
{
    /// <summary>Gets the preferred launch URI.</summary>
    public string? Uri { get; init; }

    /// <summary>Gets recommended launch arguments with explanations. They are guidance, never applied silently.</summary>
    public IReadOnlyList<GuidanceItem> RecommendedArguments { get; init; } = [];
}

/// <summary>Process settings applied while the game runs (session scoped, restored on exit).</summary>
public sealed record ProfileProcessSettings
{
    /// <summary>Gets the recommended priority class.</summary>
    public ProcessPriority? Priority { get; init; }

    /// <summary>Gets an explanation for the setting.</summary>
    public string? Reason { get; init; }
}

/// <summary>A reference to a registered optimization rule with parameters.</summary>
public sealed record ProfileRuleReference
{
    /// <summary>Gets the rule id.</summary>
    public string RuleId { get; init; } = string.Empty;

    /// <summary>Gets rule parameters.</summary>
    public IReadOnlyDictionary<string, string> Parameters { get; init; } = new Dictionary<string, string>();

    /// <summary>Gets why the rule is suggested for this game.</summary>
    public string? Reason { get; init; }

    /// <summary>Gets a value indicating whether the rule is optional.</summary>
    public bool Optional { get; init; } = true;

    /// <summary>Gets a value indicating whether the change is reverted when the game exits.</summary>
    public bool SessionScoped { get; init; }
}

/// <summary>A piece of guidance with a title and explanation.</summary>
/// <param name="Title">Short title or value.</param>
/// <param name="Detail">Explanation.</param>
public sealed record GuidanceItem(string Title, string Detail);

/// <summary>Rollback behaviour of a profile.</summary>
public sealed record ProfileRollback
{
    /// <summary>Gets a value indicating whether session scoped changes are restored when the game exits.</summary>
    public bool RestoreOnExit { get; init; } = true;
}

/// <summary>Benchmark configuration for a game.</summary>
public sealed record ProfileBenchmark
{
    /// <summary>Gets the capture duration in seconds.</summary>
    public int DurationSeconds { get; init; } = 60;

    /// <summary>Gets the warm-up period in seconds excluded from results.</summary>
    public int WarmupSeconds { get; init; } = 5;

    /// <summary>Gets instructions for a reproducible scenario.</summary>
    public string? Scenario { get; init; }
}

/// <summary>Provides access to game profiles.</summary>
public interface IGameProfileRepository
{
    /// <summary>Gets all valid profiles.</summary>
    IReadOnlyList<GameProfile> Profiles { get; }

    /// <summary>Gets validation problems found while loading profiles.</summary>
    IReadOnlyList<string> LoadErrors { get; }

    /// <summary>Finds a profile by id.</summary>
    /// <param name="profileId">Profile id.</param>
    /// <returns>The profile or <see langword="null"/>.</returns>
    GameProfile? Find(string profileId);

    /// <summary>Finds the profile matching a game.</summary>
    /// <param name="game">The game.</param>
    /// <returns>The profile or <see langword="null"/>.</returns>
    GameProfile? MatchGame(GameInfo game);

    /// <summary>Finds the profile matching a process.</summary>
    /// <param name="executablePath">Full executable path or file name.</param>
    /// <param name="commandLine">Command line, when available.</param>
    /// <returns>The profile or <see langword="null"/>.</returns>
    GameProfile? MatchProcess(string executablePath, string? commandLine);

    /// <summary>Reloads profiles from disk.</summary>
    void Reload();
}
