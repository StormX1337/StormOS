using Microsoft.Extensions.Logging;
using StormOS.Core.Games;
using StormOS.Core.Settings;
using StormOS.Overlay;

namespace StormOS.App.Services;

/// <summary>Connects the overlay window to live telemetry and game detection.</summary>
public sealed class OverlayController(OverlayWindow window, TelemetryFeed feed, IRunningGameDetector detector, ISettingsStore settings, ILogger<OverlayController> logger) : IDisposable
{
    private bool _initialized;

    /// <summary>Gets a value indicating whether the overlay is running.</summary>
    public bool IsRunning => window.IsRunning;

    /// <summary>Wires events and starts the overlay if configured.</summary>
    /// <param name="overlay">Overlay settings.</param>
    public void Initialize(OverlaySettings overlay)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        feed.SnapshotReceived += (_, snapshot) =>
        {
            if (window.IsRunning)
            {
                window.Update(OverlayContent.Build(snapshot, settings.Current.Overlay));
            }
        };
        detector.GameStarted += (_, _) =>
        {
            if (settings.Current.Overlay is { Enabled: true, ShowWithGames: true })
            {
                Start();
            }
        };
        detector.GameStopped += (_, _) =>
        {
            if (settings.Current.Overlay.ShowWithGames && detector.Running.Count == 0)
            {
                Stop();
            }
        };
        settings.Changed += (_, updated) =>
        {
            if (window.IsRunning)
            {
                window.ApplySettings(updated.Overlay);
            }
        };

        if (overlay is { Enabled: true, ShowWithGames: false })
        {
            Start();
        }
    }

    /// <summary>Starts the overlay.</summary>
    public void Start()
    {
        window.Start(settings.Current.Overlay);
        window.Update(OverlayContent.Build(feed.Latest, settings.Current.Overlay));
        logger.LogInformation("Overlay started");
    }

    /// <summary>Stops the overlay.</summary>
    public void Stop()
    {
        if (window.IsRunning)
        {
            window.Stop();
            logger.LogInformation("Overlay stopped");
        }
    }

    /// <inheritdoc />
    public void Dispose() => Stop();
}
