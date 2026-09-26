using StormOS.Core.Telemetry;

namespace StormOS.Core.Settings;

/// <summary>Theme choice.</summary>
public enum AppTheme
{
    /// <summary>Always dark.</summary>
    Dark,

    /// <summary>Always light.</summary>
    Light,

    /// <summary>Follow Windows.</summary>
    System,
}

/// <summary>Overlay anchor.</summary>
public enum OverlayPosition
{
    /// <summary>Top left.</summary>
    TopLeft,

    /// <summary>Top right.</summary>
    TopRight,

    /// <summary>Bottom left.</summary>
    BottomLeft,

    /// <summary>Bottom right.</summary>
    BottomRight,
}

/// <summary>Update channel.</summary>
public enum UpdateChannel
{
    /// <summary>Stable releases.</summary>
    Stable,

    /// <summary>Beta releases.</summary>
    Beta,
}

/// <summary>All user settings. Stored locally in the settings table; defaults are privacy preserving.</summary>
public sealed record StormSettings
{
    /// <summary>Gets general settings.</summary>
    public GeneralSettings General { get; init; } = new();

    /// <summary>Gets appearance settings.</summary>
    public AppearanceSettings Appearance { get; init; } = new();

    /// <summary>Gets performance settings.</summary>
    public PerformanceSettings Performance { get; init; } = new();

    /// <summary>Gets overlay settings.</summary>
    public OverlaySettings Overlay { get; init; } = new();

    /// <summary>Gets game settings.</summary>
    public GameSettings Games { get; init; } = new();

    /// <summary>Gets optimization settings.</summary>
    public OptimizationSettings Optimization { get; init; } = new();

    /// <summary>Gets network settings.</summary>
    public NetworkSettings Network { get; init; } = new();

    /// <summary>Gets notification settings.</summary>
    public NotificationSettings Notifications { get; init; } = new();

    /// <summary>Gets privacy settings.</summary>
    public PrivacySettings Privacy { get; init; } = new();

    /// <summary>Gets cloud settings.</summary>
    public CloudSettings Cloud { get; init; } = new();

    /// <summary>Gets update settings.</summary>
    public UpdateSettings Updates { get; init; } = new();

    /// <summary>Gets advanced settings.</summary>
    public AdvancedSettings Advanced { get; init; } = new();

    /// <summary>Gets a value indicating whether the first-run setup was completed.</summary>
    public bool OnboardingCompleted { get; init; }
}

/// <summary>General settings.</summary>
public sealed record GeneralSettings
{
    /// <summary>Gets a value indicating whether STORM OS starts with Windows.</summary>
    public bool StartWithWindows { get; init; }

    /// <summary>Gets a value indicating whether the window starts minimized to the tray.</summary>
    public bool StartMinimized { get; init; }

    /// <summary>Gets the UI language (BCP-47), or empty for the system language.</summary>
    public string Language { get; init; } = string.Empty;
}

/// <summary>Appearance settings.</summary>
public sealed record AppearanceSettings
{
    /// <summary>Gets the theme.</summary>
    public AppTheme Theme { get; init; } = AppTheme.Dark;

    /// <summary>Gets a value indicating whether animations are reduced.</summary>
    public bool ReduceMotion { get; init; }

    /// <summary>Gets a value indicating whether Mica backdrop is used.</summary>
    public bool UseMica { get; init; } = true;
}

/// <summary>Performance collection settings.</summary>
public sealed record PerformanceSettings
{
    /// <summary>Gets the dashboard sampling interval in milliseconds.</summary>
    public int SamplingIntervalMs { get; init; } = 1000;

    /// <summary>Gets the sampling interval while a game runs, in milliseconds.</summary>
    public int GameSamplingIntervalMs { get; init; } = 2000;

    /// <summary>Gets a value indicating whether per-core metrics are collected.</summary>
    public bool CollectPerCore { get; init; } = true;

    /// <summary>Gets a value indicating whether frame capture starts automatically when a game is detected.</summary>
    public bool AutoCaptureFrames { get; init; } = true;

    /// <summary>Gets the history retention in days.</summary>
    public int HistoryRetentionDays { get; init; } = 30;

    /// <summary>Gets the default sampling mode.</summary>
    public SamplingMode DefaultMode { get; init; } = SamplingMode.Monitoring;
}

/// <summary>Overlay settings.</summary>
public sealed record OverlaySettings
{
    /// <summary>Gets a value indicating whether the overlay is enabled.</summary>
    public bool Enabled { get; init; }

    /// <summary>Gets a value indicating whether the overlay is shown automatically when a game starts.</summary>
    public bool ShowWithGames { get; init; } = true;

    /// <summary>Gets the anchor position.</summary>
    public OverlayPosition Position { get; init; } = OverlayPosition.TopLeft;

    /// <summary>Gets the horizontal offset in pixels.</summary>
    public int OffsetX { get; init; } = 16;

    /// <summary>Gets the vertical offset in pixels.</summary>
    public int OffsetY { get; init; } = 16;

    /// <summary>Gets the font size in points.</summary>
    public int FontSize { get; init; } = 12;

    /// <summary>Gets the opacity 0.2–1.</summary>
    public double Opacity { get; init; } = 0.85;

    /// <summary>Gets the metrics shown.</summary>
    public IReadOnlyList<string> Metrics { get; init; } = ["fps", "avgFps", "low1", "frametime", "cpu", "gpu", "gpuTemp", "ram", "ping"];

    /// <summary>Gets the toggle hotkey, for example "Ctrl+Shift+F10".</summary>
    public string Hotkey { get; init; } = "Ctrl+Shift+F10";
}

/// <summary>Game settings.</summary>
public sealed record GameSettings
{
    /// <summary>Gets a value indicating whether running games are detected automatically.</summary>
    public bool AutoDetect { get; init; } = true;

    /// <summary>Gets a value indicating whether launchers are scanned at startup.</summary>
    public bool ScanOnStartup { get; init; } = true;

    /// <summary>Gets additional library folders to scan.</summary>
    public IReadOnlyList<string> AdditionalLibraryPaths { get; init; } = [];

    /// <summary>Gets a value indicating whether session scoped profile settings are applied automatically.</summary>
    public bool ApplyProfileSessionSettings { get; init; }
}

/// <summary>Optimization settings.</summary>
public sealed record OptimizationSettings
{
    /// <summary>Gets a value indicating whether high risk rules are shown.</summary>
    public bool ShowAdvancedRules { get; init; }

    /// <summary>Gets a value indicating whether session scoped changes are restored when a game exits.</summary>
    public bool RestoreOnGameExit { get; init; } = true;
}

/// <summary>Network settings.</summary>
public sealed record NetworkSettings
{
    /// <summary>Gets the latency target host.</summary>
    public string LatencyTarget { get; init; } = "1.1.1.1";

    /// <summary>Gets the DNS servers offered for comparison tests.</summary>
    public IReadOnlyList<string> AlternativeDnsServers { get; init; } = ["1.1.1.1", "8.8.8.8", "9.9.9.9"];

    /// <summary>Gets the throughput test URL (must be HTTPS).</summary>
    public string ThroughputUrl { get; init; } = "https://speed.cloudflare.com/__down?bytes=25000000";
}

/// <summary>Notification settings.</summary>
public sealed record NotificationSettings
{
    /// <summary>Gets a value indicating whether a notification is shown when a game is detected.</summary>
    public bool GameDetected { get; init; } = true;

    /// <summary>Gets a value indicating whether optimization results are notified.</summary>
    public bool OptimizationResults { get; init; } = true;

    /// <summary>Gets a value indicating whether health alerts are notified.</summary>
    public bool HealthAlerts { get; init; } = true;

    /// <summary>Gets a value indicating whether update notifications are shown.</summary>
    public bool Updates { get; init; } = true;
}

/// <summary>Privacy settings. Everything that leaves the machine is opt-in.</summary>
public sealed record PrivacySettings
{
    /// <summary>Gets a value indicating whether usage telemetry is sent (default off).</summary>
    public bool Telemetry { get; init; }

    /// <summary>Gets a value indicating whether crash reports are sent (default off).</summary>
    public bool CrashReports { get; init; }

    /// <summary>Gets a value indicating whether data is synced to STORM Cloud (default off).</summary>
    public bool CloudSync { get; init; }

    /// <summary>Gets a value indicating whether performance history is stored locally (default on).</summary>
    public bool PerformanceHistory { get; init; } = true;

    /// <summary>Gets a value indicating whether anonymous diagnostics are sent (default off).</summary>
    public bool AnonymousDiagnostics { get; init; }
}

/// <summary>Cloud settings.</summary>
public sealed record CloudSettings
{
    /// <summary>Gets a value indicating whether the cloud integration is enabled.</summary>
    public bool Enabled { get; init; }

    /// <summary>Gets the API base URL.</summary>
    public string ApiBaseUrl { get; init; } = "https://api.stormos.app";

    /// <summary>Gets the registered device id.</summary>
    public string? DeviceId { get; init; }

    /// <summary>Gets the signed-in account e-mail.</summary>
    public string? AccountEmail { get; init; }
}

/// <summary>Update settings.</summary>
public sealed record UpdateSettings
{
    /// <summary>Gets the update channel.</summary>
    public UpdateChannel Channel { get; init; } = UpdateChannel.Stable;

    /// <summary>Gets a value indicating whether updates are checked automatically.</summary>
    public bool CheckAutomatically { get; init; } = true;
}

/// <summary>Advanced settings.</summary>
public sealed record AdvancedSettings
{
    /// <summary>Gets a value indicating whether verbose developer logging is enabled.</summary>
    public bool DeveloperLogging { get; init; }

    /// <summary>Gets an explicit PresentMon executable path.</summary>
    public string? PresentMonPath { get; init; }

    /// <summary>Gets a value indicating whether the ETW fallback frame capture may be used.</summary>
    public bool AllowEtwFallback { get; init; } = true;
}

/// <summary>Loads and saves settings.</summary>
public interface ISettingsStore
{
    /// <summary>Raised after settings were saved.</summary>
    event EventHandler<StormSettings>? Changed;

    /// <summary>Gets the current settings.</summary>
    StormSettings Current { get; }

    /// <summary>Loads settings from storage.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The settings.</returns>
    Task<StormSettings> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Saves settings.</summary>
    /// <param name="settings">The settings.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the operation.</returns>
    Task SaveAsync(StormSettings settings, CancellationToken cancellationToken = default);
}
