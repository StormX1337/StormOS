using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using StormOS.App.Services;
using StormOS.Benchmark.Engine;
using StormOS.Core.Benchmark;
using StormOS.Core.Common;
using StormOS.Core.Games;
using StormOS.Core.History;
using StormOS.Core.Ipc;
using StormOS.Core.Scoring;
using StormOS.Services.Client;

namespace StormOS.App.ViewModels;

/// <summary>A benchmark type with availability.</summary>
public sealed record BenchmarkTypeItem(BenchmarkType Type, string Name, string? Unavailable)
{
    public bool IsAvailable => Unavailable is null;

    public string Status => Unavailable ?? "Ready";

    public override string ToString() => Name;
}

/// <summary>A formatted benchmark metric.</summary>
public sealed record MetricRow(string Name, string Value);

/// <summary>A stored benchmark result row.</summary>
public sealed record BenchmarkRow(BenchmarkResult Result)
{
    public string Title => $"{(Result.GameName ?? Result.Type.ToString())}{(string.IsNullOrWhiteSpace(Result.Label) ? string.Empty : " · " + Result.Label)}";

    public string When => Result.StartedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    public string Summary => !Result.Completed
        ? "Failed: " + (Result.Error ?? "unknown error")
        : Result.Frames is { FrameCount: > 0 } f
            ? $"{f.AverageFps:0} FPS avg · {f.OnePercentLowFps:0} 1% low"
            : string.Join(" · ", Result.Metrics.Take(2).Select(m => $"{m.Value.ToString("0.##", CultureInfo.CurrentCulture)} {m.Unit}"));

    public string ScoreText => Result.Score?.Score is { } s ? s.ToString("0", CultureInfo.CurrentCulture) : "—";

    public override string ToString() => $"{Title} — {When}";
}

/// <summary>A before/after delta row.</summary>
public sealed record DeltaRow(MetricDelta Delta)
{
    public string Name => Delta.Name;

    public string Before => Delta.Before.ToString("0.##", CultureInfo.CurrentCulture) + " " + Delta.Unit;

    public string After => Delta.After.ToString("0.##", CultureInfo.CurrentCulture) + " " + Delta.Unit;

    public string Change => Units.FormatDeltaPercent(Delta.DeltaPercent);

    public bool Improved => Delta.Improved;
}

/// <summary>Runs benchmarks, shows transparent scores and compares measured runs.</summary>
public sealed partial class BenchmarkViewModel(
    BenchmarkEngine engine,
    IStormServiceClient service,
    IRunningGameDetector detector,
    IHistoryStore history,
    NotificationService notifications,
    ILogger<BenchmarkViewModel> logger) : PageViewModel
{
    private CancellationTokenSource? _cts;

    public ObservableCollection<BenchmarkTypeItem> Types { get; } = [];

    public IReadOnlyList<string> Durations { get; } = ["10 seconds", "30 seconds", "60 seconds", "120 seconds"];

    public ObservableCollection<RunningGame> RunningGames { get; } = [];

    public ObservableCollection<MetricRow> LastMetrics { get; } = [];

    public ObservableCollection<BenchmarkRow> Results { get; } = [];

    public ObservableCollection<DeltaRow> Deltas { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGaming))]
    public partial BenchmarkTypeItem? SelectedType { get; set; }

    [ObservableProperty]
    public partial int SelectedDuration { get; set; }

    [ObservableProperty]
    public partial string Label { get; set; } = string.Empty;

    [ObservableProperty]
    public partial RunningGame? SelectedGame { get; set; }

    [ObservableProperty]
    public partial bool IsRunning { get; set; }

    [ObservableProperty]
    public partial double Progress { get; set; }

    [ObservableProperty]
    public partial string ProgressStage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial BenchmarkResult? LastResult { get; set; }

    [ObservableProperty]
    public partial ScoreBreakdown? LastScore { get; set; }

    [ObservableProperty]
    public partial string LastSummary { get; set; } = "No benchmark has run in this session yet.";

    [ObservableProperty]
    public partial BenchmarkRow? CompareBefore { get; set; }

    [ObservableProperty]
    public partial BenchmarkRow? CompareAfter { get; set; }

    [ObservableProperty]
    public partial string ComparisonNote { get; set; } = "Select two completed runs of the same type to compare.";

    public bool IsGaming => SelectedType?.Type == BenchmarkType.Gaming;

    public override void Activate(object? parameter)
    {
        Types.Clear();
        foreach (var (type, name, unavailable) in engine.List())
        {
            Types.Add(new BenchmarkTypeItem(type, name, unavailable));
        }

        var gamingUnavailable = service.State == ServiceConnectionState.Connected
            ? null
            : "Gaming benchmarks need the STORM OS service (frame capture runs there).";
        Types.Add(new BenchmarkTypeItem(BenchmarkType.Gaming, "Gaming (FPS)", gamingUnavailable));
        SelectedType = Types.FirstOrDefault(t => t.IsAvailable) ?? Types.FirstOrDefault();

        RefreshRunningGames();
        if (parameter is GameInfo game)
        {
            SelectedType = Types.First(t => t.Type == BenchmarkType.Gaming);
            SelectedGame = RunningGames.FirstOrDefault(r => r.Game?.GameId == game.GameId);
            SelectedDuration = 2;
            if (SelectedGame is null)
            {
                notifications.Info($"Start {game.Name} and play a repeatable scene, then run the gaming benchmark here.");
            }
        }

        _ = LoadResultsAsync();
    }

    public override void Deactivate() => _cts?.Cancel();

    [RelayCommand]
    private void RefreshRunningGames()
    {
        RunningGames.Clear();
        foreach (var game in detector.Running)
        {
            RunningGames.Add(game);
        }

        SelectedGame ??= RunningGames.FirstOrDefault();
    }

    [RelayCommand]
    private async Task RunAsync()
    {
        if (SelectedType is not { } type || IsRunning)
        {
            return;
        }

        if (!type.IsAvailable)
        {
            notifications.Warning(type.Unavailable!, type.Name);
            return;
        }

        var seconds = SelectedDuration switch { 1 => 30, 2 => 60, 3 => 120, _ => 10 };
        var label = string.IsNullOrWhiteSpace(Label) ? null : Label.Trim();
        _cts = new CancellationTokenSource();
        IsRunning = true;
        Progress = 0;
        ProgressStage = "Preparing…";
        try
        {
            BenchmarkResult result;
            if (type.Type == BenchmarkType.Gaming)
            {
                if (SelectedGame is not { } game)
                {
                    notifications.Warning("Start a game first. The gaming benchmark measures the frames of a running game.", "Gaming benchmark");
                    return;
                }

                ProgressStage = $"Capturing frames of {game.DisplayName} for {seconds} s (plus warm-up)… keep playing a repeatable scene.";
                var response = await service.RunGamingBenchmarkAsync(new GamingBenchmarkRequest(game.ProcessId, seconds, 5, label), _cts.Token);
                if (!response.IsSuccess)
                {
                    notifications.Warning(response.Error.Message, "Gaming benchmark");
                    return;
                }

                result = response.Value! with { GameId = response.Value!.GameId ?? game.Game?.GameId, GameName = response.Value.GameName ?? game.DisplayName };
                await engine.StoreAsync(result, _cts.Token);
            }
            else
            {
                var progress = new Progress<BenchmarkProgress>(p =>
                {
                    Progress = p.Percent;
                    ProgressStage = p.Stage;
                });
                var options = new BenchmarkRunOptions { Duration = TimeSpan.FromSeconds(seconds), Label = label };
                result = await Task.Run(() => engine.RunAsync(type.Type, options, progress, _cts.Token), _cts.Token);
            }

            Show(result);
            if (result.Completed)
            {
                notifications.Success($"{type.Name} benchmark finished.", "Benchmark");
            }
            else
            {
                notifications.Warning(result.Error ?? "The benchmark could not complete.", "Benchmark");
            }

            await LoadResultsAsync();
        }
        catch (OperationCanceledException)
        {
            notifications.Info("Benchmark cancelled. Partial results are not stored.");
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Benchmark could not start");
            notifications.Warning(ex.Message, "Benchmark");
        }
        finally
        {
            IsRunning = false;
            Progress = 0;
            ProgressStage = string.Empty;
            _cts.Dispose();
            _cts = null;
        }
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    [RelayCommand]
    private void ShowResult(BenchmarkRow? row)
    {
        if (row is not null)
        {
            Show(row.Result);
        }
    }

    partial void OnCompareBeforeChanged(BenchmarkRow? value) => Compare();

    partial void OnCompareAfterChanged(BenchmarkRow? value) => Compare();

    private void Show(BenchmarkResult result)
    {
        LastResult = result;
        LastScore = result.Score ?? BenchmarkEngine.Score(result);
        LastMetrics.Clear();
        foreach (var metric in BenchmarkComparer.AllMetrics(result))
        {
            LastMetrics.Add(new MetricRow(metric.Name, $"{metric.Value.ToString("0.##", CultureInfo.CurrentCulture)} {metric.Unit}"));
        }

        if (result.AverageCpuUsage is { } cpu)
        {
            LastMetrics.Add(new MetricRow("Average CPU load", $"{cpu:0} %"));
        }

        if (result.AverageGpuUsage is { } gpu)
        {
            LastMetrics.Add(new MetricRow("Average GPU load", $"{gpu:0} %"));
        }

        LastSummary = result.Completed
            ? $"{result.GameName ?? result.Type.ToString()} · {result.StartedAt.ToLocalTime():g} · {Units.FormatDuration(result.Duration)} · {result.Hardware}"
            : $"Not completed: {result.Error}";
    }

    private void Compare()
    {
        Deltas.Clear();
        if (CompareBefore is null || CompareAfter is null)
        {
            ComparisonNote = "Select two completed runs of the same type to compare.";
            return;
        }

        if (CompareBefore.Result.Id == CompareAfter.Result.Id)
        {
            ComparisonNote = "Select two different runs.";
            return;
        }

        var comparison = BenchmarkComparer.Compare(CompareBefore.Result, CompareAfter.Result);
        if (comparison is null)
        {
            ComparisonNote = "These runs cannot be compared: both must be completed and of the same benchmark type.";
            return;
        }

        foreach (var delta in comparison.Deltas)
        {
            Deltas.Add(new DeltaRow(delta));
        }

        ComparisonNote = comparison.Deltas.Count == 0
            ? "No metric was measured in both runs, so no difference is shown."
            : $"{comparison.Deltas.Count(d => d.Improved)} of {comparison.Deltas.Count} measured metrics improved. Only metrics measured in both runs are compared.";
    }

    private async Task LoadResultsAsync()
    {
        var results = await history.ListBenchmarksAsync(limit: 100);
        Results.Clear();
        foreach (var result in results)
        {
            Results.Add(new BenchmarkRow(result));
        }

        if (LastResult is null && results.FirstOrDefault() is { } latest)
        {
            Show(latest);
        }
    }
}
