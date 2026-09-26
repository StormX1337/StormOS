using Microsoft.Extensions.Logging;
using StormOS.Core.Games;

namespace StormOS.Games;

/// <summary>Aggregates all launcher scanners. No launcher is a hard dependency: a failing scanner is skipped.</summary>
public sealed class GameRegistry : IGameRegistry, IDisposable
{
    private readonly IReadOnlyList<IGameLibraryScanner> _scanners;
    private readonly IGameProfileRepository _profiles;
    private readonly ILogger<GameRegistry> _logger;
    private readonly SemaphoreSlim _scanLock = new(1, 1);
    private IReadOnlyList<GameInfo>? _games;

    /// <summary>Initializes a new instance of the <see cref="GameRegistry"/> class.</summary>
    /// <param name="scanners">Scanners.</param>
    /// <param name="profiles">Profile repository.</param>
    /// <param name="logger">Logger.</param>
    public GameRegistry(IEnumerable<IGameLibraryScanner> scanners, IGameProfileRepository profiles, ILogger<GameRegistry> logger)
    {
        _scanners = scanners.ToList();
        _profiles = profiles;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GameInfo>> GetGamesAsync(bool refresh = false, CancellationToken cancellationToken = default)
    {
        if (!refresh && _games is not null)
        {
            return _games;
        }

        await _scanLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!refresh && _games is not null)
            {
                return _games;
            }

            var results = await Task.WhenAll(_scanners.Select(s => ScanSafeAsync(s, cancellationToken))).ConfigureAwait(false);
            var merged = new Dictionary<string, GameInfo>(StringComparer.Ordinal);
            foreach (var game in results.SelectMany(r => r))
            {
                merged.TryAdd(game.GameId, game with { ProfileId = _profiles.MatchGame(game)?.Id });
            }

            _games = merged.Values.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            _logger.LogInformation("Game scan found {Count} games across {Launchers} launchers", _games.Count, results.Count(r => r.Count > 0));
            return _games;
        }
        finally
        {
            _scanLock.Release();
        }
    }

    /// <inheritdoc />
    public GameInfo? Find(string gameId) => _games?.FirstOrDefault(g => string.Equals(g.GameId, gameId, StringComparison.Ordinal));

    /// <inheritdoc />
    public GameInfo? MatchExecutable(string executablePath)
    {
        if (_games is null || string.IsNullOrEmpty(executablePath))
        {
            return null;
        }

        var exact = _games.FirstOrDefault(g => g.Executables.Any(e => string.Equals(e, executablePath, StringComparison.OrdinalIgnoreCase)));
        if (exact is not null)
        {
            return exact;
        }

        GameInfo? best = null;
        var bestLength = 0;
        foreach (var game in _games)
        {
            if (game.InstallPath is not { Length: > 3 } root)
            {
                continue;
            }

            var prefix = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
            if (executablePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && prefix.Length > bestLength)
            {
                best = game;
                bestLength = prefix.Length;
            }
        }

        return best;
    }

    /// <inheritdoc />
    public void Dispose() => _scanLock.Dispose();

    private async Task<IReadOnlyList<GameInfo>> ScanSafeAsync(IGameLibraryScanner scanner, CancellationToken cancellationToken)
    {
        try
        {
            return await Task.Run(() => scanner.ScanAsync(cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // A broken launcher installation must not prevent detection of other launchers.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _logger.LogWarning(ex, "The {Launcher} scanner failed", scanner.Launcher);
            return [];
        }
    }
}
