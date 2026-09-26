using System.ComponentModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using StormOS.App.Services;
using StormOS.Core.History;
using StormOS.Core.Settings;
using StormOS.Infrastructure.Configuration;
using StormOS.Infrastructure.Paths;
using StormOS.Security.Validation;
using StormOS.Services.Cloud;
using StormOS.Services.Licensing;
using StormOS.Services.Updates;

namespace StormOS.App.ViewModels;

/// <summary>All settings sections. Changes are validated and saved immediately.</summary>
public sealed partial class SettingsViewModel(
    ISettingsStore settings,
    ThemeService theme,
    CloudClient cloud,
    CloudAccountService account,
    EntitlementService entitlements,
    UpdateService updates,
    IHistoryStore history,
    IStormPaths paths,
    DialogService dialogs,
    NotificationService notifications,
    NavigationService navigation,
    ILogger<SettingsViewModel> logger) : PageViewModel
{
    private static readonly HashSet<string> Persisted = new(StringComparer.Ordinal)
    {
        nameof(StartWithWindows), nameof(StartMinimized), nameof(ThemeIndex), nameof(UseMica), nameof(ReduceMotion),
        nameof(SamplingIndex), nameof(GameSamplingIndex), nameof(CollectPerCore), nameof(AutoCaptureFrames), nameof(RetentionDays),
        nameof(AutoDetect), nameof(ScanOnStartup), nameof(ApplyProfileSessionSettings), nameof(ShowAdvancedRules), nameof(RestoreOnGameExit),
        nameof(NotifyGameDetected), nameof(NotifyOptimizationResults), nameof(NotifyHealthAlerts), nameof(NotifyUpdates),
        nameof(PrivacyTelemetry), nameof(PrivacyCrashReports), nameof(PrivacyCloudSync), nameof(PrivacyPerformanceHistory), nameof(PrivacyAnonymousDiagnostics),
        nameof(CloudEnabled), nameof(UpdateChannelIndex), nameof(CheckUpdatesAutomatically), nameof(DeveloperLogging), nameof(AllowEtwFallback),
    };

    private static readonly int[] SamplingSteps = [500, 1000, 2000, 5000];
    private bool _loading;
    private ReleaseInfo? _release;

    public IReadOnlyList<string> Themes { get; } = ["Dark", "Light", "Use Windows setting"];

    public IReadOnlyList<string> SamplingOptions { get; } = ["0.5 seconds", "1 second", "2 seconds", "5 seconds"];

    public IReadOnlyList<string> Channels { get; } = ["Stable", "Beta"];

    public string Version => UpdateService.CurrentVersion;

    public string DataFolder => paths.UserData;

    public string LogFolder => paths.Logs;

    public string BuildMode => MockModeGuard.IsCompiledIn ? "Development build (mock data available)" : "Release build";

    [ObservableProperty] public partial bool StartWithWindows { get; set; }
    [ObservableProperty] public partial bool StartMinimized { get; set; }
    [ObservableProperty] public partial int ThemeIndex { get; set; }
    [ObservableProperty] public partial bool UseMica { get; set; }
    [ObservableProperty] public partial bool ReduceMotion { get; set; }
    [ObservableProperty] public partial int SamplingIndex { get; set; }
    [ObservableProperty] public partial int GameSamplingIndex { get; set; }
    [ObservableProperty] public partial bool CollectPerCore { get; set; }
    [ObservableProperty] public partial bool AutoCaptureFrames { get; set; }
    [ObservableProperty] public partial double RetentionDays { get; set; }
    [ObservableProperty] public partial bool AutoDetect { get; set; }
    [ObservableProperty] public partial bool ScanOnStartup { get; set; }
    [ObservableProperty] public partial bool ApplyProfileSessionSettings { get; set; }
    [ObservableProperty] public partial bool ShowAdvancedRules { get; set; }
    [ObservableProperty] public partial bool RestoreOnGameExit { get; set; }
    [ObservableProperty] public partial string LatencyTarget { get; set; } = string.Empty;
    [ObservableProperty] public partial string AlternativeDns { get; set; } = string.Empty;
    [ObservableProperty] public partial string ThroughputUrl { get; set; } = string.Empty;
    [ObservableProperty] public partial string? NetworkError { get; set; }
    [ObservableProperty] public partial bool NotifyGameDetected { get; set; }
    [ObservableProperty] public partial bool NotifyOptimizationResults { get; set; }
    [ObservableProperty] public partial bool NotifyHealthAlerts { get; set; }
    [ObservableProperty] public partial bool NotifyUpdates { get; set; }
    [ObservableProperty] public partial bool PrivacyTelemetry { get; set; }
    [ObservableProperty] public partial bool PrivacyCrashReports { get; set; }
    [ObservableProperty] public partial bool PrivacyCloudSync { get; set; }
    [ObservableProperty] public partial bool PrivacyPerformanceHistory { get; set; }
    [ObservableProperty] public partial bool PrivacyAnonymousDiagnostics { get; set; }
    [ObservableProperty] public partial bool CloudEnabled { get; set; }
    [ObservableProperty] public partial string ApiBaseUrl { get; set; } = string.Empty;
    [ObservableProperty] public partial string? CloudError { get; set; }
    [ObservableProperty] public partial string Email { get; set; } = string.Empty;
    [ObservableProperty] public partial string Password { get; set; } = string.Empty;
    [ObservableProperty] public partial string AccountText { get; set; } = "Not signed in";
    [ObservableProperty] public partial bool IsSignedIn { get; set; }
    [ObservableProperty] public partial string LicenseText { get; set; } = string.Empty;
    [ObservableProperty] public partial int UpdateChannelIndex { get; set; }
    [ObservableProperty] public partial bool CheckUpdatesAutomatically { get; set; }
    [ObservableProperty] public partial string UpdateText { get; set; } = string.Empty;
    [ObservableProperty] public partial bool UpdateAvailable { get; set; }
    [ObservableProperty] public partial double DownloadProgress { get; set; }
    [ObservableProperty] public partial bool DeveloperLogging { get; set; }
    [ObservableProperty] public partial bool AllowEtwFallback { get; set; }
    [ObservableProperty] public partial string PresentMonPath { get; set; } = string.Empty;

    public override void Activate(object? parameter)
    {
        _loading = true;
        var s = settings.Current;
        StartWithWindows = s.General.StartWithWindows;
        StartMinimized = s.General.StartMinimized;
        ThemeIndex = (int)s.Appearance.Theme;
        UseMica = s.Appearance.UseMica;
        ReduceMotion = s.Appearance.ReduceMotion;
        SamplingIndex = IndexOf(s.Performance.SamplingIntervalMs);
        GameSamplingIndex = IndexOf(s.Performance.GameSamplingIntervalMs);
        CollectPerCore = s.Performance.CollectPerCore;
        AutoCaptureFrames = s.Performance.AutoCaptureFrames;
        RetentionDays = s.Performance.HistoryRetentionDays;
        AutoDetect = s.Games.AutoDetect;
        ScanOnStartup = s.Games.ScanOnStartup;
        ApplyProfileSessionSettings = s.Games.ApplyProfileSessionSettings;
        ShowAdvancedRules = s.Optimization.ShowAdvancedRules;
        RestoreOnGameExit = s.Optimization.RestoreOnGameExit;
        LatencyTarget = s.Network.LatencyTarget;
        AlternativeDns = string.Join(", ", s.Network.AlternativeDnsServers);
        ThroughputUrl = s.Network.ThroughputUrl;
        NotifyGameDetected = s.Notifications.GameDetected;
        NotifyOptimizationResults = s.Notifications.OptimizationResults;
        NotifyHealthAlerts = s.Notifications.HealthAlerts;
        NotifyUpdates = s.Notifications.Updates;
        PrivacyTelemetry = s.Privacy.Telemetry;
        PrivacyCrashReports = s.Privacy.CrashReports;
        PrivacyCloudSync = s.Privacy.CloudSync;
        PrivacyPerformanceHistory = s.Privacy.PerformanceHistory;
        PrivacyAnonymousDiagnostics = s.Privacy.AnonymousDiagnostics;
        CloudEnabled = s.Cloud.Enabled;
        ApiBaseUrl = s.Cloud.ApiBaseUrl;
        UpdateChannelIndex = (int)s.Updates.Channel;
        CheckUpdatesAutomatically = s.Updates.CheckAutomatically;
        DeveloperLogging = s.Advanced.DeveloperLogging;
        AllowEtwFallback = s.Advanced.AllowEtwFallback;
        PresentMonPath = s.Advanced.PresentMonPath ?? string.Empty;
        IsSignedIn = s.Cloud.AccountEmail is not null;
        AccountText = s.Cloud.AccountEmail is { } email ? $"Signed in as {email}" : "Not signed in";
        var e = entitlements.Current;
        LicenseText = $"{e.Tier} · {e.Source}{(e.ExpiresAt is { } exp ? " · valid until " + exp.ToLocalTime().ToString("d", System.Globalization.CultureInfo.CurrentCulture) : string.Empty)} · {e.Features.Count} features";
        UpdateText = $"Version {Version}";
        _loading = false;
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (!_loading && e.PropertyName is { } name && Persisted.Contains(name))
        {
            _ = SaveAsync();
        }
    }

    [RelayCommand]
    private async Task SaveNetworkAsync()
    {
        var servers = AlternativeDns.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries);
        if (!InputValidator.IsHost(LatencyTarget))
        {
            NetworkError = "The latency target must be a host name or IP address.";
            return;
        }

        if (servers.Length is 0 or > 6 || servers.Any(s => !InputValidator.TryParseDnsServer(s, out _)))
        {
            NetworkError = "Enter one to six public IPv4 DNS servers, separated by commas.";
            return;
        }

        if (!InputValidator.IsHttpsUrl(ThroughputUrl))
        {
            NetworkError = "The download test URL must use https.";
            return;
        }

        NetworkError = null;
        var s = settings.Current;
        await settings.SaveAsync(s with { Network = s.Network with { LatencyTarget = LatencyTarget.Trim(), AlternativeDnsServers = servers, ThroughputUrl = ThroughputUrl.Trim() } });
        notifications.Success("Network settings saved.");
    }

    [RelayCommand]
    private async Task SaveCloudUrlAsync()
    {
        if (!CloudClient.IsAcceptableBaseUrl(ApiBaseUrl))
        {
            CloudError = "The API address must use https (http is allowed for localhost only).";
            return;
        }

        CloudError = null;
        var s = settings.Current;
        await settings.SaveAsync(s with { Cloud = s.Cloud with { ApiBaseUrl = ApiBaseUrl.Trim() } });
        notifications.Success("Cloud address saved.");
    }

    [RelayCommand]
    private async Task SavePresentMonAsync()
    {
        var path = string.IsNullOrWhiteSpace(PresentMonPath) ? null : PresentMonPath.Trim();
        if (path is not null && (!Path.IsPathFullyQualified(path) || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
        {
            notifications.Warning("Enter the full path to PresentMon .exe. The service only runs it from Program Files or the STORM OS folder and verifies its signature.");
            return;
        }

        var s = settings.Current;
        await settings.SaveAsync(s with { Advanced = s.Advanced with { PresentMonPath = path } });
        notifications.Success("PresentMon path saved.");
    }

    [RelayCommand]
    private async Task SignInAsync()
    {
        if (!CloudEnabled)
        {
            CloudError = "Enable STORM Cloud first.";
            return;
        }

        IsBusy = true;
        try
        {
            var result = await cloud.LoginAsync(Email.Trim(), Password);
            Password = string.Empty;
            if (!result.IsSuccess)
            {
                CloudError = result.Error.Message;
                return;
            }

            CloudError = null;
            var s = settings.Current;
            await settings.SaveAsync(s with { Cloud = s.Cloud with { AccountEmail = Email.Trim() } });
            IsSignedIn = true;
            AccountText = $"Signed in as {Email.Trim()}";
            var license = await account.SyncAsync();
            LicenseText = license.IsSuccess ? $"{license.Value!.Tier} · {license.Value.Source}" : $"{entitlements.Current.Tier} · {license.Error.Message}";
            notifications.Success("Signed in to STORM Cloud. This PC is registered to your account.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SignOutAsync()
    {
        await cloud.LogoutAsync();
        var s = settings.Current;
        await settings.SaveAsync(s with { Cloud = s.Cloud with { AccountEmail = null } });
        IsSignedIn = false;
        AccountText = "Not signed in";
        notifications.Info("Signed out. Tokens were removed from this PC.");
    }

    [RelayCommand]
    private async Task CheckUpdatesAsync()
    {
        UpdateText = "Checking…";
        var result = await updates.CheckAsync();
        if (!result.IsSuccess)
        {
            UpdateText = $"Version {Version} · update check unavailable: {result.Error.Message}";
            return;
        }

        _release = result.Value!.Release;
        UpdateAvailable = _release is not null;
        UpdateText = _release is null
            ? $"Version {Version} is up to date."
            : $"Version {_release.Version} is available ({Core.Common.Units.FormatBytes(_release.SizeBytes)}). {_release.Notes}";
    }

    [RelayCommand]
    private async Task InstallUpdateAsync()
    {
        if (_release is null || !await dialogs.ConfirmAsync("Install update", $"Download STORM OS {_release.Version}, verify its checksum and signature, and start the installer?", "Download"))
        {
            return;
        }

        IsBusy = true;
        try
        {
            var progress = new Progress<double>(p => DownloadProgress = p);
            var result = await updates.DownloadAsync(_release, progress);
            if (!result.IsSuccess)
            {
                notifications.Error(result.Error.Message, "Update");
                return;
            }

            // The setup executable and the MSI both show progress only (/passive) and elevate themselves.
            var installer = result.Value!.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? new ProcessStartInfo(result.Value!) { ArgumentList = { "/passive" }, UseShellExecute = false }
                : new ProcessStartInfo("msiexec.exe") { ArgumentList = { "/i", result.Value!, "/passive" }, UseShellExecute = false };
            Process.Start(installer);
            Microsoft.UI.Xaml.Application.Current.Exit();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            logger.LogWarning(ex, "Installer could not start");
            notifications.Error("The installer could not be started.", "Update");
        }
        finally
        {
            IsBusy = false;
            DownloadProgress = 0;
        }
    }

    [RelayCommand]
    private async Task ClearHistoryAsync()
    {
        if (!await dialogs.ConfirmAsync("Clear history", "Delete all locally stored sessions, benchmarks, network tests and metric history? The optimization journal is kept so changes can still be restored.", "Delete"))
        {
            return;
        }

        var removed = await history.PruneAsync(DateTimeOffset.UtcNow.AddMinutes(1));
        notifications.Success($"{removed} records deleted.");
    }

    [RelayCommand]
    private void OpenLogs() => ShellLauncher.ShowInExplorer(paths.Logs + Path.DirectorySeparatorChar);

    [RelayCommand]
    private async Task RunOnboardingAsync()
    {
        var s = settings.Current;
        await settings.SaveAsync(s with { OnboardingCompleted = false });
        navigation.Navigate("onboarding");
    }

    private static int IndexOf(int intervalMs)
    {
        var index = Array.IndexOf(SamplingSteps, intervalMs);
        return index < 0 ? 1 : index;
    }

    private async Task SaveAsync()
    {
        var s = settings.Current;
        var updated = s with
        {
            General = s.General with { StartWithWindows = StartWithWindows, StartMinimized = StartMinimized },
            Appearance = s.Appearance with { Theme = (AppTheme)Math.Clamp(ThemeIndex, 0, 2), UseMica = UseMica, ReduceMotion = ReduceMotion },
            Performance = s.Performance with
            {
                SamplingIntervalMs = SamplingSteps[Math.Clamp(SamplingIndex, 0, SamplingSteps.Length - 1)],
                GameSamplingIntervalMs = SamplingSteps[Math.Clamp(GameSamplingIndex, 0, SamplingSteps.Length - 1)],
                CollectPerCore = CollectPerCore,
                AutoCaptureFrames = AutoCaptureFrames,
                HistoryRetentionDays = (int)Math.Clamp(double.IsFinite(RetentionDays) ? RetentionDays : 30, 1, 365),
            },
            Games = s.Games with { AutoDetect = AutoDetect, ScanOnStartup = ScanOnStartup, ApplyProfileSessionSettings = ApplyProfileSessionSettings },
            Optimization = s.Optimization with { ShowAdvancedRules = ShowAdvancedRules, RestoreOnGameExit = RestoreOnGameExit },
            Notifications = new NotificationSettings { GameDetected = NotifyGameDetected, OptimizationResults = NotifyOptimizationResults, HealthAlerts = NotifyHealthAlerts, Updates = NotifyUpdates },
            Privacy = new PrivacySettings { Telemetry = PrivacyTelemetry, CrashReports = PrivacyCrashReports, CloudSync = PrivacyCloudSync, PerformanceHistory = PrivacyPerformanceHistory, AnonymousDiagnostics = PrivacyAnonymousDiagnostics },
            Cloud = s.Cloud with { Enabled = CloudEnabled },
            Updates = new UpdateSettings { Channel = (UpdateChannel)Math.Clamp(UpdateChannelIndex, 0, 1), CheckAutomatically = CheckUpdatesAutomatically },
            Advanced = s.Advanced with { DeveloperLogging = DeveloperLogging, AllowEtwFallback = AllowEtwFallback },
        };
        await settings.SaveAsync(updated);
        theme.Apply(updated.Appearance.Theme);
        if (updated.General.StartWithWindows != s.General.StartWithWindows || updated.General.StartMinimized != s.General.StartMinimized)
        {
            try
            {
                AutoStart.Set(updated.General.StartWithWindows, updated.General.StartMinimized);
            }
            catch (UnauthorizedAccessException ex)
            {
                logger.LogWarning(ex, "Autostart could not be changed");
                notifications.Warning("Start with Windows could not be changed.");
            }
        }
    }
}
