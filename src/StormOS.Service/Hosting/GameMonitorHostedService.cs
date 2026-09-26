using StormOS.Core.Ipc;
using StormOS.Core.Settings;
using StormOS.Games.Detection;
using StormOS.Infrastructure.Ipc;
using StormOS.Performance.Frames;
using StormOS.Performance.Sessions;

namespace StormOS.Service.Hosting;

/// <summary>Runs game detection, session recording and passive metric history, and broadcasts game events.</summary>
public sealed class GameMonitorHostedService(
    RunningGameDetector detector,
    SessionRecorder sessions,
    MetricHistoryRecorder history,
    FrameCaptureCoordinator frames,
    ISettingsStore settings,
    IpcServer server,
    ILogger<GameMonitorHostedService> logger) : IHostedService
{
    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await settings.LoadAsync(cancellationToken).ConfigureAwait(false);
        detector.GameStarted += (_, game) => Broadcast(IpcTopics.GameStarted, game);
        detector.GameStopped += (_, game) => Broadcast(IpcTopics.GameStopped, game);
        frames.StatusChanged += (_, status) => Broadcast(IpcTopics.FrameCaptureChanged, status);
        sessions.Start();
        history.Start();
        detector.Start();
        logger.LogInformation("Game monitoring started");
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        detector.Dispose();
        sessions.Dispose();
        await history.FlushAsync(cancellationToken).ConfigureAwait(false);
        history.Dispose();
        await frames.StopAsync().ConfigureAwait(false);
    }

    private void Broadcast(string topic, object payload)
    {
        foreach (var session in server.Sessions)
        {
            _ = session.SendEventAsync(topic, payload);
        }
    }
}
