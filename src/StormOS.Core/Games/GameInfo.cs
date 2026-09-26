namespace StormOS.Core.Games;

/// <summary>Game launchers and sources STORM OS can detect games from.</summary>
public enum LauncherKind
{
    /// <summary>Unknown source.</summary>
    Unknown,

    /// <summary>Steam.</summary>
    Steam,

    /// <summary>Epic Games Launcher.</summary>
    Epic,

    /// <summary>Xbox app / Microsoft Store (PC Game Pass).</summary>
    Xbox,

    /// <summary>Battle.net.</summary>
    BattleNet,

    /// <summary>Riot Client.</summary>
    Riot,

    /// <summary>Ubisoft Connect.</summary>
    Ubisoft,

    /// <summary>EA app.</summary>
    EA,

    /// <summary>GOG Galaxy.</summary>
    Gog,

    /// <summary>Detected from a running process that matched a profile.</summary>
    Process,

    /// <summary>Added manually by the user.</summary>
    Manual,
}

/// <summary>An installed or running game.</summary>
public sealed record GameInfo
{
    /// <summary>Gets the stable game id, for example "steam:730".</summary>
    public string GameId { get; init; } = string.Empty;

    /// <summary>Gets the display name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Gets the publisher, when known.</summary>
    public string? Publisher { get; init; }

    /// <summary>Gets the launcher the game was detected through.</summary>
    public LauncherKind Launcher { get; init; }

    /// <summary>Gets the launcher specific id, for example the Steam app id.</summary>
    public string? LauncherGameId { get; init; }

    /// <summary>Gets the install directory.</summary>
    public string? InstallPath { get; init; }

    /// <summary>Gets known executable file names or paths of the game.</summary>
    public IReadOnlyList<string> Executables { get; init; } = [];

    /// <summary>Gets the installed version, when the launcher reports it.</summary>
    public string? Version { get; init; }

    /// <summary>Gets the install size in bytes, when the launcher reports it.</summary>
    public long? SizeBytes { get; init; }

    /// <summary>Gets a launch URI understood by the launcher, for example "steam://rungameid/730".</summary>
    public string? LaunchUri { get; init; }

    /// <summary>Gets the matching profile id, when a profile exists.</summary>
    public string? ProfileId { get; init; }

    /// <summary>Gets a value indicating whether the game is installed.</summary>
    public bool IsInstalled { get; init; } = true;

    /// <summary>Gets the last time the game was played, when known.</summary>
    public DateTimeOffset? LastPlayed { get; init; }

    /// <summary>Creates a stable id from a launcher and launcher specific id.</summary>
    /// <param name="launcher">The launcher.</param>
    /// <param name="launcherGameId">The launcher specific id.</param>
    /// <returns>A normalized id such as "steam:730".</returns>
    public static string CreateId(LauncherKind launcher, string launcherGameId) =>
        $"{launcher.ToString().ToLowerInvariant()}:{launcherGameId.Trim().ToLowerInvariant()}";
}

/// <summary>A game process currently running.</summary>
public sealed record RunningGame
{
    /// <summary>Gets the process id.</summary>
    public int ProcessId { get; init; }

    /// <summary>Gets the process name.</summary>
    public string ProcessName { get; init; } = string.Empty;

    /// <summary>Gets the executable path, when accessible.</summary>
    public string? ExecutablePath { get; init; }

    /// <summary>Gets the matched game, when known.</summary>
    public GameInfo? Game { get; init; }

    /// <summary>Gets the matched profile id, when known.</summary>
    public string? ProfileId { get; init; }

    /// <summary>Gets the time the process was first observed.</summary>
    public DateTimeOffset DetectedAt { get; init; }

    /// <summary>Gets the display name.</summary>
    public string DisplayName => Game?.Name ?? ProcessName;
}
