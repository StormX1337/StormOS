using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Extensions.Logging;
using StormOS.Core.Games;
using StormOS.Core.Optimization;
using StormOS.Core.Processes;
using StormOS.Core.Settings;

namespace StormOS.Optimization.Sessions;

/// <summary>
/// Applies a profile's session-scoped rules when its game starts and rolls them back when the game exits.
/// Disabled unless the user opted in (Settings → Games → "Apply profile session settings").
/// </summary>
public sealed class ProfileSessionOptimizer : IDisposable
{
    private readonly IRunningGameDetector _detector;
    private readonly IGameProfileRepository _profiles;
    private readonly IOptimizationEngine _engine;
    private readonly ISettingsStore _settings;
    private readonly ILogger<ProfileSessionOptimizer> _logger;
    private readonly ConcurrentDictionary<int, List<Guid>> _changes = new();

    /// <summary>Initializes a new instance of the <see cref="ProfileSessionOptimizer"/> class.</summary>
    /// <param name="detector">Running game detector.</param>
    /// <param name="profiles">Profiles.</param>
    /// <param name="engine">User-level optimization engine.</param>
    /// <param name="settings">Settings.</param>
    /// <param name="logger">Logger.</param>
    public ProfileSessionOptimizer(IRunningGameDetector detector, IGameProfileRepository profiles, IOptimizationEngine engine, ISettingsStore settings, ILogger<ProfileSessionOptimizer> logger)
    {
        _detector = detector;
        _profiles = profiles;
        _engine = engine;
        _settings = settings;
        _logger = logger;
    }

    /// <summary>Starts listening for games.</summary>
    public void Start()
    {
        _detector.GameStarted += OnStarted;
        _detector.GameStopped += OnStopped;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _detector.GameStarted -= OnStarted;
        _detector.GameStopped -= OnStopped;
    }

    private void OnStarted(object? sender, RunningGame game)
    {
        if (!_settings.Current.Games.ApplyProfileSessionSettings || game.ProfileId is null || _profiles.Find(game.ProfileId) is not { } profile)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            var applied = new List<Guid>();
            foreach (var reference in profile.OptimizationRules.Where(r => r.SessionScoped))
            {
                if (_engine.Rules.All(r => r.Id != reference.RuleId))
                {
                    continue;
                }

                var record = await _engine.ApplyAsync(reference.RuleId, reference.Parameters, $"profile:{profile.Id}").ConfigureAwait(false);
                if (record.Outcome == OptimizationOutcome.Applied)
                {
                    applied.Add(record.Id);
                }
            }

            if (profile.Process?.Priority is { } priority and not ProcessPriority.Normal)
            {
                var parameters = new Dictionary<string, string>
                {
                    ["processId"] = game.ProcessId.ToString(CultureInfo.InvariantCulture),
                    ["priority"] = priority == ProcessPriority.High ? "high" : "aboveNormal",
                };
                var record = await _engine.ApplyAsync("process.game-priority", parameters, $"profile:{profile.Id}").ConfigureAwait(false);
                if (record.Outcome == OptimizationOutcome.Applied)
                {
                    applied.Add(record.Id);
                }
            }

            _changes[game.ProcessId] = applied;
            _logger.LogInformation("Applied {Count} session settings for {Game}", applied.Count, profile.Name);
        });
    }

    private void OnStopped(object? sender, RunningGame game)
    {
        if (!_changes.TryRemove(game.ProcessId, out var applied) || applied.Count == 0)
        {
            return;
        }

        var profile = game.ProfileId is null ? null : _profiles.Find(game.ProfileId);
        if (!_settings.Current.Optimization.RestoreOnGameExit || profile?.Rollback.RestoreOnExit == false)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            foreach (var id in Enumerable.Reverse(applied))
            {
                await _engine.RollbackAsync(id, $"profile:{profile?.Id}").ConfigureAwait(false);
            }

            _logger.LogInformation("Restored {Count} session settings after {Game} exited", applied.Count, game.DisplayName);
        });
    }
}
