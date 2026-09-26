using System.Reflection;
using Microsoft.Extensions.Logging;
using StormOS.Core.Benchmark;
using StormOS.Core.Common;
using StormOS.Core.Games;
using StormOS.Core.Hardware;
using StormOS.Core.History;
using StormOS.Core.Ipc;
using StormOS.Core.Optimization;
using StormOS.Core.Processes;
using StormOS.Core.Telemetry;
using StormOS.Infrastructure.Ipc;

namespace StormOS.Services.Client;

/// <summary>
/// <see cref="IStormServiceClient"/> over the named pipe. Reconnects with exponential back-off and restores the
/// telemetry subscription after a service restart.
/// </summary>
public sealed class StormServiceClient : IStormServiceClient, IAsyncDisposable
{
    private readonly IpcClient _ipc;
    private readonly ILogger<StormServiceClient> _logger;
    private readonly string _clientName;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private int? _telemetryInterval;
    private Task? _reconnectLoop;
    private ServiceConnectionState _state;

    /// <summary>Initializes a new instance of the <see cref="StormServiceClient"/> class.</summary>
    /// <param name="ipc">IPC client.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="clientName">Client name reported in the handshake.</param>
    public StormServiceClient(IpcClient ipc, ILogger<StormServiceClient> logger, string clientName = "StormOS.App")
    {
        _ipc = ipc;
        _logger = logger;
        _clientName = clientName;
        _ipc.EventReceived += OnEvent;
        _ipc.Disconnected += (_, _) => SetState(ServiceConnectionState.Disconnected);
    }

    /// <inheritdoc />
    public event EventHandler<ServiceConnectionState>? StateChanged;

    /// <inheritdoc />
    public event EventHandler<MetricsSnapshot>? TelemetryReceived;

    /// <inheritdoc />
    public event EventHandler<RunningGame>? GameStarted;

    /// <inheritdoc />
    public event EventHandler<RunningGame>? GameStopped;

    /// <inheritdoc />
    public event EventHandler<OptimizationRecord>? OptimizationChanged;

    /// <inheritdoc />
    public event EventHandler<FrameCaptureStatus>? FrameCaptureChanged;

    /// <inheritdoc />
    public ServiceConnectionState State => _state;

    /// <inheritdoc />
    public HelloResponse? Hello { get; private set; }

    /// <inheritdoc />
    public async Task<Result> ConnectAsync(CancellationToken cancellationToken = default)
    {
        var result = await ConnectOnceAsync(cancellationToken).ConfigureAwait(false);
        _reconnectLoop ??= Task.Run(() => ReconnectLoopAsync(_lifetime.Token), CancellationToken.None);
        return result;
    }

    /// <inheritdoc />
    public Task<Result<HealthResponse>> GetHealthAsync(CancellationToken cancellationToken = default) =>
        _ipc.RequestAsync<HealthResponse>(IpcOperations.Health, cancellationToken: cancellationToken);

    /// <inheritdoc />
    public async Task<Result> SubscribeTelemetryAsync(int intervalMs, CancellationToken cancellationToken = default)
    {
        _telemetryInterval = intervalMs;
        return await _ipc.RequestAsync<bool>(IpcOperations.TelemetrySubscribe, new TelemetrySubscribeRequest(intervalMs), cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result> UnsubscribeTelemetryAsync(CancellationToken cancellationToken = default)
    {
        _telemetryInterval = null;
        return await _ipc.RequestAsync<bool>(IpcOperations.TelemetryUnsubscribe, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<Result<MetricsSnapshot>> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
        _ipc.RequestAsync<MetricsSnapshot>(IpcOperations.TelemetrySnapshot, cancellationToken: cancellationToken);

    /// <inheritdoc />
    public Task<Result<HardwareInventory>> GetInventoryAsync(CancellationToken cancellationToken = default) =>
        _ipc.RequestAsync<HardwareInventory>(IpcOperations.HardwareInventory, timeout: TimeSpan.FromSeconds(60), cancellationToken: cancellationToken);

    /// <inheritdoc />
    public Task<Result<FrameCaptureStatus>> StartFrameCaptureAsync(int processId, CancellationToken cancellationToken = default) =>
        _ipc.RequestAsync<FrameCaptureStatus>(IpcOperations.FramesStart, new FrameCaptureStartRequest(processId), cancellationToken: cancellationToken);

    /// <inheritdoc />
    public Task<Result<FrameCaptureStatus>> StopFrameCaptureAsync(CancellationToken cancellationToken = default) =>
        _ipc.RequestAsync<FrameCaptureStatus>(IpcOperations.FramesStop, cancellationToken: cancellationToken);

    /// <inheritdoc />
    public Task<Result<FrameCaptureStatus>> GetFrameCaptureStatusAsync(CancellationToken cancellationToken = default) =>
        _ipc.RequestAsync<FrameCaptureStatus>(IpcOperations.FramesStatus, cancellationToken: cancellationToken);

    /// <inheritdoc />
    public Task<Result<RunningGamesResponse>> GetRunningGamesAsync(CancellationToken cancellationToken = default) =>
        _ipc.RequestAsync<RunningGamesResponse>(IpcOperations.GamesRunning, cancellationToken: cancellationToken);

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<RuleDescriptor>>> GetRulesAsync(CancellationToken cancellationToken = default) =>
        Cast<List<RuleDescriptor>, IReadOnlyList<RuleDescriptor>>(await _ipc.RequestAsync<List<RuleDescriptor>>(IpcOperations.OptimizationRules, cancellationToken: cancellationToken).ConfigureAwait(false));

    /// <inheritdoc />
    public Task<Result<RuleDetection>> DetectAsync(string ruleId, IReadOnlyDictionary<string, string>? parameters, CancellationToken cancellationToken = default) =>
        _ipc.RequestAsync<RuleDetection>(IpcOperations.OptimizationDetect, new RuleRequest(ruleId, parameters), cancellationToken: cancellationToken);

    /// <inheritdoc />
    public Task<Result<OptimizationRecord>> ApplyAsync(string ruleId, IReadOnlyDictionary<string, string>? parameters, CancellationToken cancellationToken = default) =>
        _ipc.RequestAsync<OptimizationRecord>(IpcOperations.OptimizationApply, new RuleRequest(ruleId, parameters), TimeSpan.FromMinutes(2), cancellationToken);

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<OptimizationRecord>>> GetOptimizationHistoryAsync(CancellationToken cancellationToken = default) =>
        Cast<List<OptimizationRecord>, IReadOnlyList<OptimizationRecord>>(await _ipc.RequestAsync<List<OptimizationRecord>>(IpcOperations.OptimizationHistory, cancellationToken: cancellationToken).ConfigureAwait(false));

    /// <inheritdoc />
    public Task<Result<OptimizationRecord>> RestoreAsync(Guid changeId, CancellationToken cancellationToken = default) =>
        _ipc.RequestAsync<OptimizationRecord>(IpcOperations.OptimizationRestore, new RestoreRequest(changeId), TimeSpan.FromMinutes(2), cancellationToken);

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<OptimizationRecord>>> RestoreAllAsync(CancellationToken cancellationToken = default) =>
        Cast<List<OptimizationRecord>, IReadOnlyList<OptimizationRecord>>(await _ipc.RequestAsync<List<OptimizationRecord>>(IpcOperations.OptimizationRestoreAll, timeout: TimeSpan.FromMinutes(5), cancellationToken: cancellationToken).ConfigureAwait(false));

    /// <inheritdoc />
    public async Task<Result> SetProcessPriorityAsync(int processId, ProcessPriority priority, CancellationToken cancellationToken = default) =>
        await _ipc.RequestAsync<bool>(IpcOperations.ProcessSetPriority, new ProcessPriorityRequest(processId, priority), cancellationToken: cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<Result> TerminateProcessAsync(int processId, string expectedName, CancellationToken cancellationToken = default) =>
        await _ipc.RequestAsync<bool>(IpcOperations.ProcessTerminate, new ProcessTerminateRequest(processId, expectedName), cancellationToken: cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<ServiceEntry>>> GetServicesAsync(CancellationToken cancellationToken = default) =>
        Cast<List<ServiceEntry>, IReadOnlyList<ServiceEntry>>(await _ipc.RequestAsync<List<ServiceEntry>>(IpcOperations.ServicesList, timeout: TimeSpan.FromSeconds(60), cancellationToken: cancellationToken).ConfigureAwait(false));

    /// <inheritdoc />
    public Task<Result<LogTailResponse>> TailLogsAsync(int lines, CancellationToken cancellationToken = default) =>
        _ipc.RequestAsync<LogTailResponse>(IpcOperations.LogsTail, new LogTailRequest(lines), cancellationToken: cancellationToken);

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<GameSession>>> GetSessionsAsync(CancellationToken cancellationToken = default) =>
        Cast<List<GameSession>, IReadOnlyList<GameSession>>(await _ipc.RequestAsync<List<GameSession>>(IpcOperations.SessionsList, cancellationToken: cancellationToken).ConfigureAwait(false));

    /// <inheritdoc />
    public Task<Result<BenchmarkResult>> RunGamingBenchmarkAsync(GamingBenchmarkRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _ipc.RequestAsync<BenchmarkResult>(IpcOperations.BenchmarkGaming, request, TimeSpan.FromSeconds(request.DurationSeconds + request.WarmupSeconds + 60), cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _lifetime.CancelAsync().ConfigureAwait(false);
        if (_reconnectLoop is not null)
        {
            try
            {
                await _reconnectLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Shutdown.
            }
        }

        await _ipc.DisposeAsync().ConfigureAwait(false);
        _lifetime.Dispose();
        _connectLock.Dispose();
    }

    private static Result<TOut> Cast<TIn, TOut>(Result<TIn> result)
        where TIn : TOut =>
        result.IsSuccess ? Result<TOut>.Ok(result.Value!) : Result<TOut>.Fail(result.Error);

    private async Task<Result> ConnectOnceAsync(CancellationToken cancellationToken)
    {
        await _connectLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_state == ServiceConnectionState.Connected && _ipc.IsConnected)
            {
                return Result.Success;
            }

            SetState(ServiceConnectionState.Connecting);
            var version = typeof(StormServiceClient).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
            var hello = await _ipc.RequestAsync<HelloResponse>(IpcOperations.Hello, new HelloRequest(_clientName, version[..Math.Min(version.Length, 32)]), cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!hello.IsSuccess)
            {
                SetState(ServiceConnectionState.Unavailable);
                return hello;
            }

            Hello = hello.Value;
            SetState(ServiceConnectionState.Connected);
            if (_telemetryInterval is { } interval)
            {
                await _ipc.RequestAsync<bool>(IpcOperations.TelemetrySubscribe, new TelemetrySubscribeRequest(interval), cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            return Result.Success;
        }
        finally
        {
            _connectLock.Release();
        }
    }

    private async Task ReconnectLoopAsync(CancellationToken cancellationToken)
    {
        var delay = TimeSpan.FromSeconds(2);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                if (_state == ServiceConnectionState.Connected && _ipc.IsConnected)
                {
                    delay = TimeSpan.FromSeconds(2);
                    continue;
                }

                var result = await ConnectOnceAsync(cancellationToken).ConfigureAwait(false);
                delay = result.IsSuccess ? TimeSpan.FromSeconds(2) : TimeSpan.FromSeconds(Math.Min(30, delay.TotalSeconds * 2));
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private void SetState(ServiceConnectionState state)
    {
        if (_state == state)
        {
            return;
        }

        _state = state;
        _logger.LogInformation("Service connection: {State}", state);
        StateChanged?.Invoke(this, state);
    }

    private void OnEvent(object? sender, IpcEvent evt)
    {
        try
        {
            switch (evt.Topic)
            {
                case IpcTopics.Telemetry when IpcPayload.To<MetricsSnapshot>(evt.Payload) is { } snapshot:
                    TelemetryReceived?.Invoke(this, snapshot);
                    break;
                case IpcTopics.GameStarted when IpcPayload.To<RunningGame>(evt.Payload) is { } game:
                    GameStarted?.Invoke(this, game);
                    break;
                case IpcTopics.GameStopped when IpcPayload.To<RunningGame>(evt.Payload) is { } game:
                    GameStopped?.Invoke(this, game);
                    break;
                case IpcTopics.OptimizationChanged when IpcPayload.To<OptimizationRecord>(evt.Payload) is { } record:
                    OptimizationChanged?.Invoke(this, record);
                    break;
                case IpcTopics.FrameCaptureChanged when IpcPayload.To<FrameCaptureStatus>(evt.Payload) is { } status:
                    FrameCaptureChanged?.Invoke(this, status);
                    break;
            }
        }
        catch (System.Text.Json.JsonException ex)
        {
            _logger.LogWarning(ex, "Ignoring malformed {Topic} event", evt.Topic);
        }
    }
}
