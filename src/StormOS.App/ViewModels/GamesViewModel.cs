using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StormOS.App.Services;
using StormOS.Core.Benchmark;
using StormOS.Core.Games;
using StormOS.Core.History;
using StormOS.Core.Optimization;
using StormOS.Games;
using StormOS.Services.History;
using StormOS.Services.Optimization;
using StormOS.Windows.Platform;

namespace StormOS.App.ViewModels;

/// <summary>A game row.</summary>
public sealed record GameItem
{
    public required GameInfo Game { get; init; }

    public GameProfile? Profile { get; init; }

    public bool IsRunning { get; init; }

    public string? LastBenchmark { get; init; }

    public string Name => Game.Name;

    public string Launcher => Game.Launcher.ToString();

    public string Installed => Game.IsInstalled ? "Installed" : "Not installed";

    public string Version => string.IsNullOrWhiteSpace(Game.Version) ? "—" : Game.Version!;

    public string Executable => Game.Executables.Count > 0 ? Path.GetFileName(Game.Executables[0]) : Profile?.Detection.Executables is { Count: > 0 } names ? names[0].Name : "—";

    public string LastPlayed => Game.LastPlayed is { } played ? played.ToLocalTime().ToString("g", System.Globalization.CultureInfo.CurrentCulture) : "—";

    public string ProfileStatus => Profile is null ? "No profile" : $"Profile {Profile.Id} v{Profile.Version}";

    public bool HasProfile => Profile is not null;

    public string RunningText => IsRunning ? "RUNNING" : string.Empty;
}

/// <summary>Installed and running games with profile actions.</summary>
public sealed partial class GamesViewModel(
    IGameRegistry registry,
    IGameProfileRepository profiles,
    IRunningGameDetector detector,
    IHistoryStore history,
    HistoryService historyService,
    OptimizationCoordinator optimizer,
    DialogService dialogs,
    NotificationService notifications,
    NavigationService navigation) : PageViewModel
{
    public ObservableCollection<GameItem> Games { get; } = [];

    [ObservableProperty]
    public partial string Summary { get; set; } = string.Empty;

    public override void Activate(object? parameter) => _ = LoadAsync(false);

    [RelayCommand]
    private Task RefreshAsync() => LoadAsync(true);

    public void Launch(GameItem item)
    {
        var result = GameLauncher.Launch(item.Game);
        if (result.IsSuccess)
        {
            notifications.Info($"Starting {item.Name}…");
        }
        else
        {
            notifications.Warning(result.Error.Message);
        }
    }

    public void Benchmark(GameItem item) => navigation.Navigate("benchmark", item.Game);

    public async Task OpenProfileAsync(GameItem item)
    {
        if (item.Profile is not { } profile)
        {
            await dialogs.ShowAsync(item.Name, "No STORM OS profile exists for this game yet. Monitoring, sessions and benchmarks still work.");
            return;
        }

        var lines = new List<string> { $"Version {profile.Version} · anti-cheat: {profile.AntiCheat ?? "unknown"}", string.Empty };
        if (profile.Launch?.RecommendedArguments.Count > 0)
        {
            lines.Add("LAUNCH OPTIONS");
            lines.AddRange(profile.Launch.RecommendedArguments.Select(g => $"• {g.Title} — {g.Detail}"));
            lines.Add(string.Empty);
        }

        lines.Add("RECOMMENDED SETTINGS");
        lines.AddRange(profile.SystemRecommendations.Concat(profile.OptimizationRules).Select(r => $"• {r.RuleId}{(r.Parameters.Count > 0 ? " (" + string.Join(", ", r.Parameters.Select(p => $"{p.Key}={p.Value}")) + ")" : string.Empty)}{(r.SessionScoped ? " [while playing]" : string.Empty)} — {r.Reason}"));
        if (profile.Process?.Priority is { } priority)
        {
            lines.Add($"• Process priority {priority} while playing — {profile.Process.Reason}");
        }

        lines.Add(string.Empty);
        lines.Add("GRAPHICS");
        lines.AddRange(profile.GraphicsGuidance.Select(g => $"• {g.Title} — {g.Detail}"));
        if (profile.NetworkGuidance.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("NETWORK");
            lines.AddRange(profile.NetworkGuidance.Select(g => $"• {g.Title} — {g.Detail}"));
        }

        if (profile.Benchmark?.Scenario is { } scenario)
        {
            lines.Add(string.Empty);
            lines.Add("BENCHMARK SCENARIO");
            lines.Add(scenario);
        }

        await dialogs.ShowAsync(profile.Name, new Microsoft.UI.Xaml.Controls.ScrollViewer
        {
            MaxHeight = 520,
            Content = new Microsoft.UI.Xaml.Controls.TextBlock { Text = string.Join(Environment.NewLine, lines), TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap, IsTextSelectionEnabled = true },
        });
    }

    public async Task OptimizeAsync(GameItem item)
    {
        if (item.Profile is not { } profile)
        {
            notifications.Info("This game has no profile. Use the Optimizer page for general optimizations.");
            return;
        }

        var (catalog, note) = await optimizer.ListAsync();
        var references = profile.SystemRecommendations.Concat(profile.OptimizationRules.Where(r => !r.SessionScoped)).ToList();
        var plan = new List<(ProfileRuleReference Reference, OptimizationItem Item, RuleDetection Detection)>();
        foreach (var reference in references)
        {
            var item2 = catalog.FirstOrDefault(c => c.Descriptor.Id == reference.RuleId);
            if (item2 is null)
            {
                continue;
            }

            var detection = await optimizer.DetectAsync(item2.Executor, reference.RuleId, reference.Parameters);
            if (detection.IsSuccess && detection.Value!.State == DetectionState.Applicable)
            {
                plan.Add((reference, item2, detection.Value));
            }
        }

        if (plan.Count == 0)
        {
            notifications.Success($"{profile.Name}: all recommended settings are already in place.{(note is null ? string.Empty : " " + note)}");
            return;
        }

        var text = string.Join(Environment.NewLine, plan.Select(p => $"• {p.Item.Descriptor.Name}: {p.Detection.CurrentValue} → {p.Detection.TargetValue} (risk {p.Item.Descriptor.RiskLevel}{(p.Item.Descriptor.RequiresRestart ? ", restart required" : string.Empty)})\n   {p.Reference.Reason}"));
        if (!await dialogs.ConfirmAsync($"Optimize for {profile.Name}", text + "\n\nEvery change is backed up first and can be restored from History.", "Apply"))
        {
            return;
        }

        var applied = 0;
        foreach (var (reference, catalogItem, _) in plan)
        {
            var result = await optimizer.ApplyAsync(catalogItem.Executor, reference.RuleId, reference.Parameters, $"profile:{profile.Id}");
            if (result is { IsSuccess: true, Value.Outcome: OptimizationOutcome.Applied })
            {
                applied++;
            }
        }

        notifications.Success($"{applied} of {plan.Count} changes applied and verified for {profile.Name}.");
    }

    public async Task RestoreAsync(GameItem item)
    {
        if (item.Profile is not { } profile)
        {
            return;
        }

        var changes = (await historyService.ListChangesAsync())
            .Where(c => c.User == $"profile:{profile.Id}" && c.Rollback == RollbackStatus.Available)
            .ToList();
        if (changes.Count == 0)
        {
            notifications.Info($"There are no active changes made for {profile.Name}.");
            return;
        }

        if (!await dialogs.ConfirmAsync("Restore", $"Restore {changes.Count} change(s) made for {profile.Name}?", "Restore"))
        {
            return;
        }

        var restored = 0;
        foreach (var change in changes)
        {
            var result = await optimizer.RestoreAsync(change, Elevation.CurrentUserName);
            if (result is { IsSuccess: true, Value.Rollback: RollbackStatus.RolledBack })
            {
                restored++;
            }
        }

        notifications.Success($"{restored} of {changes.Count} changes restored.");
    }

    private async Task LoadAsync(bool refresh)
    {
        IsBusy = true;
        StatusMessage = refresh ? "Scanning launchers…" : null;
        try
        {
            var games = await registry.GetGamesAsync(refresh);
            var benchmarks = await history.ListBenchmarksAsync(BenchmarkType.Gaming, limit: 500);
            var running = detector.Running;
            var items = games.Select(g => new GameItem
            {
                Game = g,
                Profile = g.ProfileId is null ? null : profiles.Find(g.ProfileId),
                IsRunning = running.Any(r => r.Game?.GameId == g.GameId),
                LastBenchmark = benchmarks.FirstOrDefault(b => b.GameId == g.GameId && b.Completed) is { Frames: { } f } b ? $"{f.AverageFps:0} FPS avg · {b.StartedAt.LocalDateTime:d}" : "—",
            }).OrderByDescending(i => i.IsRunning).ThenByDescending(i => i.HasProfile).ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

            Games.Clear();
            foreach (var item in items)
            {
                Games.Add(item);
            }

            Summary = $"{items.Count} games · {items.Count(i => i.HasProfile)} with profiles · {items.Count(i => i.IsRunning)} running";
        }
        finally
        {
            IsBusy = false;
            StatusMessage = null;
        }
    }
}
