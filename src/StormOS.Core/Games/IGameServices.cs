namespace StormOS.Core.Games;

/// <summary>Scans a single launcher's library.</summary>
public interface IGameLibraryScanner
{
    /// <summary>Gets the launcher handled by this scanner.</summary>
    LauncherKind Launcher { get; }

    /// <summary>Scans for installed games. Must not throw for a missing launcher; return an empty list instead.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Installed games.</returns>
    Task<IReadOnlyList<GameInfo>> ScanAsync(CancellationToken cancellationToken = default);
}

/// <summary>Aggregates games from all scanners and matches them to profiles.</summary>
public interface IGameRegistry
{
    /// <summary>Gets all known games.</summary>
    /// <param name="refresh">Whether to rescan launchers instead of returning the cached list.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Known games.</returns>
    Task<IReadOnlyList<GameInfo>> GetGamesAsync(bool refresh = false, CancellationToken cancellationToken = default);

    /// <summary>Finds a game by id.</summary>
    /// <param name="gameId">The game id.</param>
    /// <returns>The game or <see langword="null"/>.</returns>
    GameInfo? Find(string gameId);

    /// <summary>Attempts to match an executable path to a known game.</summary>
    /// <param name="executablePath">Full executable path.</param>
    /// <returns>The game or <see langword="null"/>.</returns>
    GameInfo? MatchExecutable(string executablePath);
}

/// <summary>Detects running games.</summary>
public interface IRunningGameDetector
{
    /// <summary>Raised when a game process starts.</summary>
    event EventHandler<RunningGame>? GameStarted;

    /// <summary>Raised when a tracked game process exits.</summary>
    event EventHandler<RunningGame>? GameStopped;

    /// <summary>Gets the currently running games.</summary>
    IReadOnlyList<RunningGame> Running { get; }

    /// <summary>Gets the foreground (or most recently started) running game.</summary>
    RunningGame? Primary { get; }

    /// <summary>Performs a detection pass immediately.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the operation.</returns>
    Task PollAsync(CancellationToken cancellationToken = default);
}
