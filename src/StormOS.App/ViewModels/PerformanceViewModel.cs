using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StormOS.App.Services;
using StormOS.Core.Games;
using StormOS.Core.History;
using StormOS.Core.Telemetry;
using StormOS.Services.Client;

namespace StormOS.App.ViewModels;

/// <summary>Live and historical charts plus frame capture control.</summary>
public sealed partial class PerformanceViewModel(
    TelemetryFeed feed,
    UiDispatcher ui,
    IStormServiceClient service,
    IHistoryStore localHistory,
    IRunningGameDetector detector,
    NotificationService notifications) : PageViewModel
{
    private readonly DateTimeOffset _sessionStart = DateTimeOffset.UtcNow;

    public IReadOnlyList<string> Ranges { get; } = ["10 seconds", "1 minute", "5 minutes", "Session", "Historical (24 h)"];

    public ObservableCollection<RunningGame> RunningGames { get; } = [];

    [ObservableProperty]
    public partial int SelectedRange { get; set; } = 1;

    [ObservableProperty]
    public partial IReadOnlyList<double?> Cpu { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<double?> Gpu { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<double?> Ram { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<double?> Vram { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<double?> Fps { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<double?> FrameTime { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<double?> Latency { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<double?> Download { get; set; } = [];

    [ObservableProperty]
    public partial MetricsSnapshot? Snapshot { get; set; }

    [ObservableProperty]
    public partial RunningGame? SelectedGame { get; set; }

    [ObservableProperty]
    public partial string CaptureStatus { get; set; } = "No frame capture running.";

    [ObservableProperty]
    public partial string RangeSummary { get; set; } = string.Empty;

    public override void Activate(object? parameter)
    {
        feed.SnapshotReceived += OnSnapshot;
        detector.GameStarted += OnGamesChanged;
        detector.GameStopped += OnGamesChanged;
        RefreshGames();
        _ = RefreshCaptureStatusAsync();
        _ = RefreshAsync();
    }

    public override void Deactivate()
    {
        feed.SnapshotReceived -= OnSnapshot;
        detector.GameStarted -= OnGamesChanged;
        detector.GameStopped -= OnGamesChanged;
    }

    partial void OnSelectedRangeChanged(int value) => _ = RefreshAsync();

    [RelayCommand]
    private async Task StartCaptureAsync()
    {
        if (SelectedGame is not { } game)
        {
            notifications.Info("Select a running game first.");
            return;
        }

        var result = await service.StartFrameCaptureAsync(game.ProcessId);
        if (!result.IsSuccess)
        {
            notifications.Warning(result.Error.Message, "Frame capture");
        }

        await RefreshCaptureStatusAsync();
    }

    [RelayCommand]
    private async Task StopCaptureAsync()
    {
        await service.StopFrameCaptureAsync();
        await RefreshCaptureStatusAsync();
    }

    private void OnGamesChanged(object? sender, RunningGame e) => ui.Post(RefreshGames);

    private void RefreshGames()
    {
        RunningGames.Clear();
        foreach (var game in detector.Running)
        {
            RunningGames.Add(game);
        }

        SelectedGame ??= RunningGames.FirstOrDefault();
    }

    private async Task RefreshCaptureStatusAsync()
    {
        var status = await service.GetFrameCaptureStatusAsync();
        CaptureStatus = !status.IsSuccess
            ? "Frame capture requires the STORM OS service."
            : status.Value!.Active
                ? $"Capturing {status.Value.ProcessName} ({status.Value.ProcessId}) via {status.Value.Source}."
                : "No frame capture running. " + string.Join(" · ", status.Value.Providers.Select(p => $"{p.Name}: {(p.Available ? "available" : "unavailable")}"));
    }

    private void OnSnapshot(object? sender, MetricsSnapshot snapshot) => ui.Post(() =>
    {
        Snapshot = snapshot;
        if (SelectedRange <= 3)
        {
            ApplyLive();
        }
    });

    private async Task RefreshAsync()
    {
        if (SelectedRange <= 3)
        {
            ApplyLive();
            return;
        }

        IsBusy = true;
        try
        {
            var to = DateTimeOffset.UtcNow;
            var from = to.AddHours(-24);
            var remote = await service.GetMetricHistoryAsync(from, to);
            var points = remote.IsSuccess ? remote.Value! : await localHistory.ReadMetricHistoryAsync(from, to);
            Cpu = points.Select(p => p.Cpu).ToList();
            Gpu = points.Select(p => p.Gpu).ToList();
            Ram = points.Select(p => p.Ram).ToList();
            Vram = points.Select(p => p.Vram).ToList();
            Fps = points.Select(p => p.Fps).ToList();
            FrameTime = points.Select(p => p.FrameTimeMs).ToList();
            Latency = points.Select(p => p.LatencyMs).ToList();
            Download = [];
            RangeSummary = points.Count == 0
                ? "No history recorded yet. History is stored while telemetry runs (Settings → Privacy → Performance history)."
                : $"{points.Count} points (10-second averages) from {points[0].Timestamp.LocalDateTime:g} to {points[^1].Timestamp.LocalDateTime:g}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyLive()
    {
        var window = SelectedRange switch
        {
            0 => TimeSpan.FromSeconds(10),
            1 => TimeSpan.FromMinutes(1),
            2 => TimeSpan.FromMinutes(5),
            _ => DateTimeOffset.UtcNow - _sessionStart,
        };
        var cutoff = DateTimeOffset.UtcNow - window;
        var history = feed.History().Where(s => s.Timestamp >= cutoff).ToList();
        Cpu = history.Select(s => s.Cpu.Usage.Value).ToList();
        Gpu = history.Select(s => s.PrimaryGpu()?.Usage.Value).ToList();
        Ram = history.Select(s => s.Memory.TotalBytes > 0 ? s.Memory.UsagePercent : (double?)null).ToList();
        Vram = history.Select(s => s.PrimaryGpu()?.MemoryUsagePercent).ToList();
        Fps = history.Select(s => s.Frames?.Fps).ToList();
        FrameTime = history.Select(s => s.Frames?.FrameTimeMs).ToList();
        Latency = history.Select(s => s.System.LatencyMs.Value).ToList();
        Download = history.Select(s => (double?)(s.Network.Sum(n => n.ReceivedBytesPerSecond) * 8 / 1e6)).ToList();
        RangeSummary = history.Count == 0 ? "Waiting for data…" : $"{history.Count} samples · CPU avg {Average(Cpu)} · GPU avg {Average(Gpu)} · FPS avg {Average(Fps)}";
    }

    private static string Average(IReadOnlyList<double?> values)
    {
        var measured = values.Where(v => v.HasValue).Select(v => v!.Value).ToList();
        return measured.Count == 0 ? "—" : measured.Average().ToString("0", System.Globalization.CultureInfo.CurrentCulture);
    }
}
