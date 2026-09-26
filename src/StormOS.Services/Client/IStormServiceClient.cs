using StormOS.Core.Benchmark;
using StormOS.Core.Common;
using StormOS.Core.Games;
using StormOS.Core.Hardware;
using StormOS.Core.History;
using StormOS.Core.Ipc;
using StormOS.Core.Optimization;
using StormOS.Core.Processes;
using StormOS.Core.Telemetry;

namespace StormOS.Services.Client;

/// <summary>Connection state of the STORM OS service.</summary>
public enum ServiceConnectionState
{
    /// <summary>Not connected yet.</summary>
    Disconnected,

    /// <summary>Connecting.</summary>
    Connecting,

    /// <summary>Connected and handshake completed.</summary>
    Connected,

    /// <summary>The service is not installed or not running.</summary>
    Unavailable,
}

/// <summary>Typed client for the StormOSService IPC API.</summary>
public interface IStormServiceClient
{
    /// <summary>Raised when the connection state changes.</summary>
    event EventHandler<ServiceConnectionState>? StateChanged;

    /// <summary>Raised for each telemetry snapshot while subscribed.</summary>
    event EventHandler<MetricsSnapshot>? TelemetryReceived;

    /// <summary>Raised when the service detects a game start.</summary>
    event EventHandler<RunningGame>? GameStarted;

    /// <summary>Raised when the service detects a game exit.</summary>
    event EventHandler<RunningGame>? GameStopped;

    /// <summary>Raised when a service-side optimization changes.</summary>
    event EventHandler<OptimizationRecord>? OptimizationChanged;

    /// <summary>Raised when the frame capture status changes.</summary>
    event EventHandler<FrameCaptureStatus>? FrameCaptureChanged;

    /// <summary>Gets the current state.</summary>
    ServiceConnectionState State { get; }

    /// <summary>Gets the handshake result once connected.</summary>
    HelloResponse? Hello { get; }

    /// <summary>Connects (and keeps reconnecting in the background until disposed).</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success or the reason the service is unavailable.</returns>
    Task<Result> ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets service health.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Health.</returns>
    Task<Result<HealthResponse>> GetHealthAsync(CancellationToken cancellationToken = default);

    /// <summary>Subscribes to telemetry.</summary>
    /// <param name="intervalMs">Interval.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success or error.</returns>
    Task<Result> SubscribeTelemetryAsync(int intervalMs, CancellationToken cancellationToken = default);

    /// <summary>Unsubscribes from telemetry.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success or error.</returns>
    Task<Result> UnsubscribeTelemetryAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets one telemetry snapshot.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The snapshot.</returns>
    Task<Result<MetricsSnapshot>> GetSnapshotAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets the hardware inventory.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The inventory.</returns>
    Task<Result<HardwareInventory>> GetInventoryAsync(CancellationToken cancellationToken = default);

    /// <summary>Starts a frame capture.</summary>
    /// <param name="processId">Process id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Status.</returns>
    Task<Result<FrameCaptureStatus>> StartFrameCaptureAsync(int processId, CancellationToken cancellationToken = default);

    /// <summary>Stops the frame capture.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Status.</returns>
    Task<Result<FrameCaptureStatus>> StopFrameCaptureAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets the frame capture status.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Status.</returns>
    Task<Result<FrameCaptureStatus>> GetFrameCaptureStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets games running according to the service.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Running games.</returns>
    Task<Result<RunningGamesResponse>> GetRunningGamesAsync(CancellationToken cancellationToken = default);

    /// <summary>Lists service-side rules.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Rules.</returns>
    Task<Result<IReadOnlyList<RuleDescriptor>>> GetRulesAsync(CancellationToken cancellationToken = default);

    /// <summary>Detects a service-side rule.</summary>
    /// <param name="ruleId">Rule id.</param>
    /// <param name="parameters">Parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Detection.</returns>
    Task<Result<RuleDetection>> DetectAsync(string ruleId, IReadOnlyDictionary<string, string>? parameters, CancellationToken cancellationToken = default);

    /// <summary>Applies a service-side rule.</summary>
    /// <param name="ruleId">Rule id.</param>
    /// <param name="parameters">Parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The record.</returns>
    Task<Result<OptimizationRecord>> ApplyAsync(string ruleId, IReadOnlyDictionary<string, string>? parameters, CancellationToken cancellationToken = default);

    /// <summary>Lists service-side optimization records.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Records.</returns>
    Task<Result<IReadOnlyList<OptimizationRecord>>> GetOptimizationHistoryAsync(CancellationToken cancellationToken = default);

    /// <summary>Restores a service-side change.</summary>
    /// <param name="changeId">Change id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The record.</returns>
    Task<Result<OptimizationRecord>> RestoreAsync(Guid changeId, CancellationToken cancellationToken = default);

    /// <summary>Restores all service-side changes.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The records.</returns>
    Task<Result<IReadOnlyList<OptimizationRecord>>> RestoreAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Sets a process priority through the service.</summary>
    /// <param name="processId">Process id.</param>
    /// <param name="priority">Priority.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success or error.</returns>
    Task<Result> SetProcessPriorityAsync(int processId, ProcessPriority priority, CancellationToken cancellationToken = default);

    /// <summary>Ends a process through the service.</summary>
    /// <param name="processId">Process id.</param>
    /// <param name="expectedName">Expected name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success or error.</returns>
    Task<Result> TerminateProcessAsync(int processId, string expectedName, CancellationToken cancellationToken = default);

    /// <summary>Lists Windows services.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Services.</returns>
    Task<Result<IReadOnlyList<ServiceEntry>>> GetServicesAsync(CancellationToken cancellationToken = default);

    /// <summary>Reads recent service log lines.</summary>
    /// <param name="lines">Line count.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Log lines.</returns>
    Task<Result<LogTailResponse>> TailLogsAsync(int lines, CancellationToken cancellationToken = default);

    /// <summary>Lists game sessions recorded by the service.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Sessions.</returns>
    Task<Result<IReadOnlyList<GameSession>>> GetSessionsAsync(CancellationToken cancellationToken = default);

    /// <summary>Reads metric history recorded by the service.</summary>
    /// <param name="rangeStart">Range start.</param>
    /// <param name="rangeEnd">Range end.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Points.</returns>
    Task<Result<IReadOnlyList<MetricHistoryPoint>>> GetMetricHistoryAsync(DateTimeOffset rangeStart, DateTimeOffset rangeEnd, CancellationToken cancellationToken = default);

    /// <summary>Runs a gaming benchmark in the service.</summary>
    /// <param name="request">Request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result.</returns>
    Task<Result<BenchmarkResult>> RunGamingBenchmarkAsync(GamingBenchmarkRequest request, CancellationToken cancellationToken = default);
}
