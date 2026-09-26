using CommunityToolkit.Mvvm.ComponentModel;
using StormOS.App.Services;
using StormOS.Core.Games;
using StormOS.Services.Client;

namespace StormOS.App.ViewModels;

/// <summary>Shell state: service connection, mock mode, active game and notifications.</summary>
public sealed partial class ShellViewModel : ObservableObject
{
    public ShellViewModel(IStormServiceClient service, TelemetryFeed feed, NotificationService notifications, UiDispatcher ui, IRunningGameDetector detector)
    {
        Notifications = notifications;
        IsMock = feed.IsMock;
        Update(service.State);
        service.StateChanged += (_, state) => ui.Post(() => Update(state));
        detector.GameStarted += (_, game) => ui.Post(() => ActiveGame = game.DisplayName);
        detector.GameStopped += (_, _) => ui.Post(() => ActiveGame = detector.Primary?.DisplayName);
    }

    public NotificationService Notifications { get; }

    [ObservableProperty]
    public partial string ServiceStatus { get; set; } = "Connecting to the STORM OS service…";

    [ObservableProperty]
    public partial bool ServiceUnavailable { get; set; }

    [ObservableProperty]
    public partial bool IsMock { get; set; }

    [ObservableProperty]
    public partial string? ActiveGame { get; set; }

    private void Update(ServiceConnectionState state)
    {
        ServiceUnavailable = state == ServiceConnectionState.Unavailable;
        ServiceStatus = state switch
        {
            ServiceConnectionState.Connected => "Service connected",
            ServiceConnectionState.Connecting => "Connecting…",
            ServiceConnectionState.Unavailable => "Service not running",
            _ => "Service disconnected",
        };
    }
}
