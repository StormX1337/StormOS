using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StormOS.App.Services;
using StormOS.Core.Common;
using StormOS.Core.Games;
using StormOS.Core.Hardware;
using StormOS.Core.Settings;
using StormOS.Services.Client;
using StormOS.Services.Onboarding;

namespace StormOS.App.ViewModels;

/// <summary>First-run setup in eight steps, ending with the STORM OS System Check results applied to settings.</summary>
public sealed partial class OnboardingViewModel(
    SystemCheckService systemCheck,
    IHardwareInventoryProvider inventory,
    IGameRegistry games,
    IStormServiceClient service,
    ISettingsStore settings,
    NavigationService navigation) : PageViewModel
{
    public const int StepCount = 8;

    private static readonly string[] Titles =
    [
        "Welcome to STORM OS",
        "STORM OS System Check",
        "Your hardware",
        "Your games",
        "STORM OS service",
        "Privacy",
        "Overlay and notifications",
        "Ready",
    ];

    public ObservableCollection<SystemCheckItem> Checks { get; } = [];

    public ObservableCollection<string> Hardware { get; } = [];

    public ObservableCollection<string> Games { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(StepText), nameof(Progress), nameof(CanGoBack), nameof(NextLabel))]
    public partial int Step { get; set; }

    [ObservableProperty]
    public partial string ServiceText { get; set; } = "Checking…";

    [ObservableProperty]
    public partial bool ServiceConnected { get; set; }

    [ObservableProperty]
    public partial string GamesSummary { get; set; } = "Scanning launchers…";

    [ObservableProperty]
    public partial bool PerformanceHistory { get; set; } = true;

    [ObservableProperty]
    public partial bool CloudSync { get; set; }

    [ObservableProperty]
    public partial bool CrashReports { get; set; }

    [ObservableProperty]
    public partial bool OverlayEnabled { get; set; }

    [ObservableProperty]
    public partial bool DetectGames { get; set; } = true;

    [ObservableProperty]
    public partial bool RunScanAfterSetup { get; set; } = true;

    public string Title => Titles[Math.Clamp(Step, 0, StepCount - 1)];

    public string StepText => $"Step {Step + 1} of {StepCount}";

    public double Progress => (Step + 1) * 100.0 / StepCount;

    public bool CanGoBack => Step > 0;

    public string NextLabel => Step == StepCount - 1 ? "FINISH" : "NEXT";

    public override void Activate(object? parameter)
    {
        var current = settings.Current;
        PerformanceHistory = current.Privacy.PerformanceHistory;
        CloudSync = current.Privacy.CloudSync;
        CrashReports = current.Privacy.CrashReports;
        OverlayEnabled = current.Overlay.Enabled;
        DetectGames = current.Games.AutoDetect;
        Step = 0;
    }

    [RelayCommand]
    private void Back()
    {
        if (Step > 0)
        {
            Step--;
        }
    }

    [RelayCommand]
    private async Task NextAsync()
    {
        if (Step == StepCount - 1)
        {
            await FinishAsync();
            return;
        }

        Step++;
        switch (Step)
        {
            case 1 when Checks.Count == 0:
                await RunSystemCheckAsync();
                break;
            case 2 when Hardware.Count == 0:
                await LoadHardwareAsync();
                break;
            case 3 when Games.Count == 0:
                await LoadGamesAsync();
                break;
            case 4:
                ServiceConnected = service.State == ServiceConnectionState.Connected;
                ServiceText = ServiceConnected
                    ? $"Connected (service {service.Hello?.ServiceVersion ?? "?"}). Frame capture, session history and administrator optimizations are available."
                    : "The STORM OS service is not running. Monitoring, benchmarks and user-level optimizations still work. Frame capture, sessions and administrator changes need the service (installed by the STORM OS installer).";
                break;
        }
    }

    [RelayCommand]
    private async Task SkipAsync() => await FinishAsync();

    private async Task RunSystemCheckAsync()
    {
        IsBusy = true;
        try
        {
            foreach (var item in await systemCheck.RunAsync())
            {
                Checks.Add(item);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadHardwareAsync()
    {
        IsBusy = true;
        try
        {
            var inv = await Task.Run(() => inventory.GetInventoryAsync());
            Hardware.Add($"Windows: {inv.OperatingSystem.ProductName} {inv.OperatingSystem.DisplayVersion} (build {inv.OperatingSystem.BuildNumber})");
            Hardware.Add($"CPU: {inv.Cpu.Name} · {inv.Cpu.Cores} cores / {inv.Cpu.LogicalProcessors} threads");
            foreach (var gpu in inv.Gpus.Where(g => !g.IsSoftware))
            {
                Hardware.Add($"GPU: {gpu.Name}{(gpu.DedicatedMemoryBytes > 0 ? " · " + Units.FormatBytes(gpu.DedicatedMemoryBytes) : string.Empty)} · driver {gpu.DriverVersion ?? "unknown"}");
            }

            Hardware.Add($"Memory: {Units.FormatBytes(inv.Memory.TotalBytes)} in {inv.Memory.Modules.Count} module(s)");
            foreach (var disk in inv.Storage)
            {
                Hardware.Add($"Storage: {disk.Model} · {Units.FormatBytes(disk.SizeBytes)} · {disk.MediaType}");
            }

            foreach (var monitor in inv.Monitors)
            {
                Hardware.Add($"Display: {monitor.Width} × {monitor.Height} @ {monitor.RefreshRateHz:0} Hz");
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadGamesAsync()
    {
        IsBusy = true;
        try
        {
            var list = await games.GetGamesAsync(true);
            foreach (var game in list.OrderByDescending(g => g.ProfileId is not null).ThenBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).Take(40))
            {
                Games.Add($"{game.Name} · {game.Launcher}{(game.ProfileId is null ? string.Empty : " · profile available")}");
            }

            GamesSummary = list.Count == 0
                ? "No games were found in Steam, Epic, Xbox, Battle.net, EA, Ubisoft, GOG or Riot. Running games are still detected by their process."
                : $"{list.Count} games found · {list.Count(g => g.ProfileId is not null)} with a STORM OS profile.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task FinishAsync()
    {
        var current = settings.Current;
        await settings.SaveAsync(current with
        {
            OnboardingCompleted = true,
            Privacy = current.Privacy with { PerformanceHistory = PerformanceHistory, CloudSync = CloudSync, CrashReports = CrashReports },
            Overlay = current.Overlay with { Enabled = OverlayEnabled },
            Games = current.Games with { AutoDetect = DetectGames },
        });
        if (RunScanAfterSetup)
        {
            navigation.Navigate("optimizer", "scan");
        }
        else
        {
            navigation.Navigate("dashboard");
        }
    }
}
