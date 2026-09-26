using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using StormOS.App.Services;
using StormOS.Benchmark.Engine;
using StormOS.Core.Benchmark;
using StormOS.Core.Games;
using StormOS.Core.Hardware;
using StormOS.Core.History;
using StormOS.Core.Scoring;
using StormOS.Core.Startup;
using StormOS.Core.Telemetry;
using StormOS.Games;

namespace StormOS.App.ViewModels;

/// <summary>Live dashboard: real telemetry, system information, quick actions and transparent scores.</summary>
public sealed partial class DashboardViewModel : PageViewModel
{
    private readonly TelemetryFeed _feed;
    private readonly UiDispatcher _ui;
    private readonly IHardwareInventoryProvider _inventory;
    private readonly IOperatingSystemInfoProvider _os;
    private readonly IHistoryStore _history;
    private readonly IStartupManager _startup;
    private readonly IGameRegistry _games;
    private readonly BenchmarkEngine _benchmarks;
    private readonly NavigationService _navigation;
    private readonly NotificationService _notifications;
    private readonly OverlayController _overlay;
    private readonly ILogger<DashboardViewModel> _logger;

    public DashboardViewModel(TelemetryFeed feed, UiDispatcher ui, IHardwareInventoryProvider inventory, IOperatingSystemInfoProvider os, IHistoryStore history, IStartupManager startup, IGameRegistry games, BenchmarkEngine benchmarks, NavigationService navigation, NotificationService notifications, OverlayController overlay, ILogger<DashboardViewModel> logger)
    {
        _feed = feed;
        _ui = ui;
        _inventory = inventory;
        _os = os;
        _history = history;
        _startup = startup;
        _games = games;
        _benchmarks = benchmarks;
        _navigation = navigation;
        _notifications = notifications;
        _overlay = overlay;
        _logger = logger;
    }

    [ObservableProperty]
    public partial MetricsSnapshot? Snapshot { get; set; }

    [ObservableProperty]
    public partial GpuMetrics? Gpu { get; set; }

    [ObservableProperty]
    public partial NetworkInterfaceMetrics? PrimaryNetwork { get; set; }

    [ObservableProperty]
    public partial HardwareInventory? Inventory { get; set; }

    [ObservableProperty]
    public partial OsInfo? Os { get; set; }

    [ObservableProperty]
    public partial VolumeInfo? SystemDrive { get; set; }

    [ObservableProperty]
    public partial string CpuName { get; set; } = "Detecting…";

    [ObservableProperty]
    public partial string GpuName { get; set; } = "Detecting…";

    [ObservableProperty]
    public partial IReadOnlyList<double?> CpuHistory { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<double?> GpuHistory { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<double?> FpsHistory { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<CoreMetrics> Cores { get; set; } = [];

    [ObservableProperty]
    public partial bool HasFrames { get; set; }

    [ObservableProperty]
    public partial string Uptime { get; set; } = "—";

    [ObservableProperty]
    public partial string DiskActivity { get; set; } = "—";

    [ObservableProperty]
    public partial ScoreBreakdown? PerformanceScore { get; set; }

    [ObservableProperty]
    public partial ScoreBreakdown? GamingScore { get; set; }

    [ObservableProperty]
    public partial ScoreBreakdown? HealthScore { get; set; }

    [ObservableProperty]
    public partial ScoreBreakdown? NetworkScore { get; set; }

    [ObservableProperty]
    public partial bool OverlayRunning { get; set; }

    public override void Activate(object? parameter)
    {
        _feed.SnapshotReceived += OnSnapshot;
        OverlayRunning = _overlay.IsRunning;
        if (_feed.Latest is { } latest)
        {
            Apply(latest);
        }

        _ = LoadAsync();
    }

    public override void Deactivate() => _feed.SnapshotReceived -= OnSnapshot;

    [RelayCommand]
    private void QuickOptimize() => _navigation.Navigate("optimizer", "quick");

    [RelayCommand]
    private void SystemScan() => _navigation.Navigate("optimizer", "scan");

    [RelayCommand]
    private void QuickNetworkTest() => _navigation.Navigate("network", "run");

    [RelayCommand]
    private void ToggleOverlay()
    {
        if (_overlay.IsRunning)
        {
            _overlay.Stop();
        }
        else
        {
            _overlay.Start();
        }

        OverlayRunning = _overlay.IsRunning;
    }

    [RelayCommand]
    private async Task QuickBenchmarkAsync()
    {
        IsBusy = true;
        StatusMessage = "Running a quick CPU benchmark…";
        try
        {
            var result = await _benchmarks.RunAsync(BenchmarkType.Cpu, new BenchmarkRunOptions { Duration = TimeSpan.FromSeconds(6), Label = "quick" });
            if (result.Completed)
            {
                var multi = result.Metric("cpu.multi.mops")?.Value ?? 0;
                _notifications.Success($"Multi-thread {multi:0} MOPS, single-thread {result.Metric("cpu.single.mops")?.Value:0} MOPS. Details are on the Benchmark page.", "Quick benchmark finished");
            }
            else
            {
                _notifications.Warning(result.Error ?? "The benchmark could not run.", "Quick benchmark");
            }

            await LoadScoresAsync();
        }
        finally
        {
            IsBusy = false;
            StatusMessage = null;
        }
    }

    [RelayCommand]
    private async Task OpenGameAsync()
    {
        var games = await _games.GetGamesAsync();
        var last = games.Where(g => g.LastPlayed is not null).MaxBy(g => g.LastPlayed) ?? games.FirstOrDefault(g => g.ProfileId is not null);
        if (last is null)
        {
            _navigation.Navigate("games");
            return;
        }

        var result = GameLauncher.Launch(last);
        if (result.IsSuccess)
        {
            _notifications.Info($"Starting {last.Name}…");
        }
        else
        {
            _notifications.Warning(result.Error.Message);
        }
    }

    private void OnSnapshot(object? sender, MetricsSnapshot snapshot) => _ui.Post(() => Apply(snapshot));

    private void Apply(MetricsSnapshot snapshot)
    {
        Snapshot = snapshot;
        Gpu = snapshot.PrimaryGpu();
        PrimaryNetwork = snapshot.Network.OrderByDescending(n => n.ReceivedBytesPerSecond + n.SentBytesPerSecond).FirstOrDefault();
        Cores = snapshot.Cpu.Cores;
        HasFrames = snapshot.Frames is not null;
        Uptime = StormOS.Core.Common.Units.FormatDuration(snapshot.System.Uptime);
        var read = snapshot.Disks.Sum(d => d.ReadBytesPerSecond);
        var write = snapshot.Disks.Sum(d => d.WriteBytesPerSecond);
        DiskActivity = snapshot.Disks.Count == 0 ? "—" : $"R {StormOS.Core.Common.Units.FormatRate(read)} · W {StormOS.Core.Common.Units.FormatRate(write)}";

        var history = _feed.History(60);
        CpuHistory = history.Select(s => s.Cpu.Usage.Value).ToList();
        GpuHistory = history.Select(s => s.PrimaryGpu()?.Usage.Value).ToList();
        FpsHistory = history.Select(s => s.Frames?.Fps).ToList();
    }

    private async Task LoadAsync()
    {
        try
        {
            Os = _os.GetOsInfo();
            var inventory = await _inventory.GetInventoryAsync();
            Inventory = inventory;
            CpuName = string.IsNullOrEmpty(inventory.Cpu.Name) ? "Unknown processor" : inventory.Cpu.Name;
            GpuName = inventory.Gpus.OrderByDescending(g => g.DedicatedMemoryBytes).FirstOrDefault()?.Name ?? "No GPU detected";
            var systemRoot = Path.GetPathRoot(Environment.SystemDirectory);
            SystemDrive = inventory.Volumes.FirstOrDefault(v => string.Equals(v.RootPath, systemRoot, StringComparison.OrdinalIgnoreCase));
            await LoadScoresAsync();
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Dashboard data could not be loaded");
            StatusMessage = "Storm OS could not read all hardware information.";
        }
    }

    private async Task LoadScoresAsync()
    {
        var benchmarks = await _history.ListBenchmarksAsync(limit: 200);
        BenchmarkResult? Latest(BenchmarkType type) => benchmarks.FirstOrDefault(b => b.Type == type && b.Completed);
        PerformanceScore = StormScores.Performance(Latest(BenchmarkType.Cpu), Latest(BenchmarkType.Memory), Latest(BenchmarkType.Disk), Latest(BenchmarkType.Gpu));
        var refresh = Inventory?.Monitors.FirstOrDefault(m => m.IsPrimary)?.RefreshRateHz;
        GamingScore = StormScores.Gaming(Latest(BenchmarkType.Gaming)?.Frames, refresh);
        var network = (await _history.ListNetworkReportsAsync(1)).FirstOrDefault();
        NetworkScore = network?.Score ?? StormScores.Network(null, null);

        var startup = await _startup.ListAsync();
        var snapshot = Snapshot ?? _feed.Latest;
        HealthScore = StormScores.SystemHealth(new SystemHealthInputs
        {
            MemoryUsagePercent = snapshot is { Memory.TotalBytes: > 0 } ? snapshot.Memory.UsagePercent : null,
            SystemDriveFreePercent = SystemDrive is { } drive ? 100 - drive.UsedPercent : null,
            EnabledStartupEntries = startup.Count(e => e.IsEnabled),
            ProcessCount = snapshot?.System.ProcessCount is > 0 and var count ? count : null,
            CpuTemperatureCelsius = snapshot?.Cpu.TemperatureCelsius.Value,
            GpuTemperatureCelsius = snapshot?.PrimaryGpu()?.TemperatureCelsius.Value,
        });
    }
}
