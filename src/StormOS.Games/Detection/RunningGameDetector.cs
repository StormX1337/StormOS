using Microsoft.Extensions.Logging;
using StormOS.Core.Games;
using StormOS.Core.Telemetry;
using StormOS.Games.Parsing;
using StormOS.Games.Profiles;

namespace StormOS.Games.Detection;

/// <summary>Detection options.</summary>
public sealed class GameDetectionOptions
{
    /// <summary>Gets or sets the polling interval.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);
}

/// <summary>
/// Detects running games by matching processes against profiles (executable patterns, optional path and
/// command-line fragments) and against install directories of games found by the launcher scanners.
/// </summary>
public sealed class RunningGameDetector : IRunningGameDetector, IActiveGameProvider, IDisposable
{
    private static readonly string[] HelperPatterns =
    [
        "*CrashHandler*", "*CrashReport*", "*ErrorReporter*", "*Setup*", "*Updater*", "*Installer*", "*Uninstall*",
        "unins0*", "*redist*", "dxsetup", "EasyAntiCheat*", "BEService*", "*Bootstrapper*", "QtWebEngineProcess",
        "cef*", "*WebHelper*", "*Launcher*", "*Helper", "*Overlay*",
    ];

    private readonly IGameRegistry _registry;
    private readonly GameProfileRepository _profiles;
    private readonly IProcessSource _processes;
    private readonly GameDetectionOptions _options;
    private readonly ILogger<RunningGameDetector> _logger;
    private readonly TimeProvider _time;
    private readonly Lock _gate = new();
    private readonly Dictionary<(int, DateTime), string?> _commandLines = [];
    private Dictionary<int, RunningGame> _running = [];
    private CancellationTokenSource? _loop;
    private int? _foreground;

    /// <summary>Initializes a new instance of the <see cref="RunningGameDetector"/> class.</summary>
    /// <param name="registry">Game registry.</param>
    /// <param name="profiles">Profile repository.</param>
    /// <param name="processes">Process source.</param>
    /// <param name="options">Options.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="timeProvider">Time source.</param>
    public RunningGameDetector(IGameRegistry registry, GameProfileRepository profiles, IProcessSource processes, GameDetectionOptions options, ILogger<RunningGameDetector> logger, TimeProvider? timeProvider = null)
    {
        _registry = registry;
        _profiles = profiles;
        _processes = processes;
        _options = options;
        _logger = logger;
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public event EventHandler<RunningGame>? GameStarted;

    /// <inheritdoc />
    public event EventHandler<RunningGame>? GameStopped;

    /// <inheritdoc />
    public IReadOnlyList<RunningGame> Running
    {
        get
        {
            lock (_gate)
            {
                return [.. _running.Values];
            }
        }
    }

    /// <inheritdoc />
    public RunningGame? Primary
    {
        get
        {
            lock (_gate)
            {
                if (_foreground is { } fg && _running.TryGetValue(fg, out var foreground))
                {
                    return foreground;
                }

                return _running.Values.MaxBy(g => g.DetectedAt);
            }
        }
    }

    /// <inheritdoc />
    public (string Name, int ProcessId)? ActiveGame => Primary is { } game ? (game.DisplayName, game.ProcessId) : null;

    /// <summary>Determines whether a process name is a known launcher/helper that must not be treated as a game.</summary>
    /// <param name="processName">Process name without extension.</param>
    /// <returns><see langword="true"/> for helpers.</returns>
    public static bool IsHelper(string processName) => HelperPatterns.Any(p => WildcardMatcher.IsMatch(p, processName));

    /// <summary>Starts background polling.</summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_loop is not null)
            {
                return;
            }

            _loop = new CancellationTokenSource();
            _ = Task.Run(() => LoopAsync(_loop.Token));
        }
    }

    /// <inheritdoc />
    public Task PollAsync(CancellationToken cancellationToken = default)
    {
        var processes = _processes.List();
        var detected = new Dictionary<int, RunningGame>();
        foreach (var process in processes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Identify(process) is { } game)
            {
                detected[process.ProcessId] = game;
            }
        }

        List<RunningGame> started;
        List<RunningGame> stopped;
        lock (_gate)
        {
            _foreground = _processes.GetForegroundProcessId();
            started = detected.Values.Where(g => !_running.ContainsKey(g.ProcessId)).ToList();
            stopped = _running.Values.Where(g => !detected.ContainsKey(g.ProcessId)).ToList();
            foreach (var (pid, game) in _running)
            {
                if (detected.ContainsKey(pid))
                {
                    detected[pid] = game;
                }
            }

            _running = detected;
            var alive = processes.Select(p => (p.ProcessId, p.StartTime)).ToHashSet();
            foreach (var key in _commandLines.Keys.Where(k => !alive.Contains(k)).ToList())
            {
                _commandLines.Remove(key);
            }
        }

        foreach (var game in stopped)
        {
            _logger.LogInformation("Game stopped: {Game} ({Pid})", game.DisplayName, game.ProcessId);
            GameStopped?.Invoke(this, game);
        }

        foreach (var game in started)
        {
            _logger.LogInformation("Game detected: {Game} ({Pid})", game.DisplayName, game.ProcessId);
            GameStarted?.Invoke(this, game);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            _loop?.Cancel();
            _loop?.Dispose();
            _loop = null;
        }
    }

    private RunningGame? Identify(ProcessSnapshot process)
    {
        var path = process.Path ?? process.Name + ".exe";
        string? commandLine = null;
        if (_profiles.NeedsCommandLine(path))
        {
            lock (_gate)
            {
                var key = (process.ProcessId, process.StartTime);
                if (!_commandLines.TryGetValue(key, out commandLine))
                {
                    commandLine = _processes.GetCommandLine(process.ProcessId);
                    _commandLines[key] = commandLine;
                }
            }
        }

        var profile = _profiles.MatchProcess(path, commandLine);
        var game = process.Path is null ? null : _registry.MatchExecutable(process.Path);
        if (profile is null && (game is null || IsHelper(process.Name)))
        {
            return null;
        }

        if (profile is not null && game is null)
        {
            game = new GameInfo
            {
                GameId = GameInfo.CreateId(LauncherKind.Process, profile.Id),
                Name = profile.Name,
                Publisher = profile.Publisher,
                Launcher = LauncherKind.Process,
                Executables = process.Path is null ? [] : [process.Path],
                ProfileId = profile.Id,
            };
        }

        return new RunningGame
        {
            ProcessId = process.ProcessId,
            ProcessName = process.Name,
            ExecutablePath = process.Path,
            Game = game,
            ProfileId = profile?.Id ?? game?.ProfileId,
            DetectedAt = _time.GetUtcNow(),
        };
    }

    private async Task LoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _registry.GetGamesAsync(false, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Initial game scan failed; detection continues with profiles only");
        }

        using var timer = new PeriodicTimer(_options.PollInterval, _time);
        do
        {
            try
            {
                await PollAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
#pragma warning disable CA1031 // Detection must keep running after transient failures.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                _logger.LogWarning(ex, "Game detection pass failed");
            }
        }
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false));
    }
}
