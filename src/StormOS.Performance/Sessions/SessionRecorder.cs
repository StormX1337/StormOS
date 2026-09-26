using Microsoft.Extensions.Logging;
using StormOS.Core.Games;
using StormOS.Core.History;
using StormOS.Core.Optimization;
using StormOS.Core.Settings;
using StormOS.Core.Telemetry;
using StormOS.Performance.Frames;

namespace StormOS.Performance.Sessions;

/// <summary>
/// Records gaming sessions: when a game starts, telemetry is sampled in performance mode and frames are captured
/// (if enabled); when it exits, averages and frame statistics are stored as a <see cref="GameSession"/>.
/// </summary>
public sealed class SessionRecorder : IDisposable
{
    private readonly IRunningGameDetector _detector;
    private readonly ITelemetryHub _hub;
    private readonly FrameCaptureCoordinator? _frames;
    private readonly IHistoryStore _history;
    private readonly ISettingsStore? _settings;
    private readonly ILogger<SessionRecorder> _logger;
    private readonly TimeProvider _time;
    private readonly Lock _gate = new();
    private ActiveSession? _active;

    /// <summary>Initializes a new instance of the <see cref="SessionRecorder"/> class.</summary>
    /// <param name="detector">Running game detector.</param>
    /// <param name="hub">Telemetry hub.</param>
    /// <param name="history">History store.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="frames">Frame capture coordinator.</param>
    /// <param name="settings">Settings store.</param>
    /// <param name="timeProvider">Time source.</param>
    public SessionRecorder(IRunningGameDetector detector, ITelemetryHub hub, IHistoryStore history, ILogger<SessionRecorder> logger, FrameCaptureCoordinator? frames = null, ISettingsStore? settings = null, TimeProvider? timeProvider = null)
    {
        _detector = detector;
        _hub = hub;
        _history = history;
        _logger = logger;
        _frames = frames;
        _settings = settings;
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Gets the running session, if any.</summary>
    public GameSession? Current
    {
        get
        {
            lock (_gate)
            {
                return _active?.ToSession(_time.GetUtcNow(), null);
            }
        }
    }

    /// <summary>Starts listening for game start and stop events.</summary>
    public void Start()
    {
        _detector.GameStarted += OnGameStarted;
        _detector.GameStopped += OnGameStopped;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _detector.GameStarted -= OnGameStarted;
        _detector.GameStopped -= OnGameStopped;
        lock (_gate)
        {
            _active?.Subscription.Dispose();
            _active = null;
        }
    }

    private void OnGameStarted(object? sender, RunningGame game)
    {
        lock (_gate)
        {
            if (_active is not null)
            {
                return;
            }

            var session = new ActiveSession(Guid.NewGuid(), game, _time.GetUtcNow());
            session.Subscription = _hub.Subscribe(session.Add);
            _active = session;
        }

        _logger.LogInformation("Session started: {Game} ({Pid})", game.DisplayName, game.ProcessId);
        if (_frames is not null && (_settings?.Current.Performance.AutoCaptureFrames ?? true))
        {
            _ = Task.Run(async () =>
            {
                var result = await _frames.StartAsync(game.ProcessId).ConfigureAwait(false);
                if (!result.IsSuccess)
                {
                    _logger.LogInformation("No frame capture for {Game}: {Reason}", game.DisplayName, result.Error.Message);
                }
            });
        }
    }

    private void OnGameStopped(object? sender, RunningGame game)
    {
        ActiveSession? finished;
        lock (_gate)
        {
            if (_active is null || _active.Game.ProcessId != game.ProcessId)
            {
                return;
            }

            finished = _active;
            _active = null;
        }

        finished.Subscription.Dispose();
        _ = Task.Run(() => CompleteAsync(finished));
    }

    private async Task CompleteAsync(ActiveSession finished)
    {
        try
        {
            var frameStats = _frames?.GetSessionStatistics();
            var source = _frames?.GetStatus().Source;
            if (_frames is not null)
            {
                await _frames.StopAsync().ConfigureAwait(false);
            }

            var session = finished.ToSession(_time.GetUtcNow(), frameStats) with { FrameSource = frameStats is null ? null : source };
            await _history.SaveSessionAsync(session).ConfigureAwait(false);
            await _history.AddEventAsync(new HistoryEvent
            {
                Timestamp = session.EndedAt ?? _time.GetUtcNow(),
                Category = HistoryCategory.Session,
                Action = $"Played {session.GameName}",
                Result = EventResult.Info,
                Details = session.AverageFps is { } fps
                    ? $"{Core.Common.Units.FormatDuration(session.Duration)} · {fps:0} FPS avg · {session.OnePercentLowFps:0} FPS 1% low"
                    : $"{Core.Common.Units.FormatDuration(session.Duration)} · frame timing not captured",
                Rollback = RollbackStatus.NotApplicable,
                RelatedId = session.Id.ToString(),
            }).ConfigureAwait(false);
            _logger.LogInformation("Session saved: {Game} {Duration}", session.GameName, session.Duration);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            _logger.LogError(ex, "Could not save the session for {Game}", finished.Game.DisplayName);
        }
    }

    private sealed class ActiveSession(Guid id, RunningGame game, DateTimeOffset startedAt)
    {
        private readonly Lock _gate = new();
        private double _cpuSum;
        private int _cpuCount;
        private double _gpuSum;
        private int _gpuCount;
        private double _latencySum;
        private int _latencyCount;
        private double? _maxCpuTemp;
        private double? _maxGpuTemp;
        private int _samples;

        public RunningGame Game { get; } = game;

        public IDisposable Subscription { get; set; } = null!;

        public void Add(MetricsSnapshot snapshot)
        {
            lock (_gate)
            {
                _samples++;
                if (snapshot.Cpu.Usage.Value is { } cpu)
                {
                    _cpuSum += cpu;
                    _cpuCount++;
                }

                if (snapshot.PrimaryGpu()?.Usage.Value is { } gpu)
                {
                    _gpuSum += gpu;
                    _gpuCount++;
                }

                if (snapshot.System.LatencyMs.Value is { } latency)
                {
                    _latencySum += latency;
                    _latencyCount++;
                }

                if (snapshot.Cpu.TemperatureCelsius.Value is { } ct)
                {
                    _maxCpuTemp = Math.Max(_maxCpuTemp ?? ct, ct);
                }

                if (snapshot.PrimaryGpu()?.TemperatureCelsius.Value is { } gt)
                {
                    _maxGpuTemp = Math.Max(_maxGpuTemp ?? gt, gt);
                }
            }
        }

        public GameSession ToSession(DateTimeOffset now, (double AverageFps, double OnePercentLow, double PointOnePercentLow, double AverageFrameTimeMs)? frames)
        {
            lock (_gate)
            {
                return new GameSession
                {
                    Id = id,
                    GameId = Game.Game?.GameId,
                    GameName = Game.DisplayName,
                    ProcessName = Game.ProcessName,
                    StartedAt = startedAt,
                    EndedAt = now,
                    AverageCpuUsage = _cpuCount > 0 ? _cpuSum / _cpuCount : null,
                    AverageGpuUsage = _gpuCount > 0 ? _gpuSum / _gpuCount : null,
                    AverageLatencyMs = _latencyCount > 0 ? _latencySum / _latencyCount : null,
                    MaxCpuTemperature = _maxCpuTemp,
                    MaxGpuTemperature = _maxGpuTemp,
                    AverageFps = frames?.AverageFps,
                    OnePercentLowFps = frames?.OnePercentLow,
                    PointOnePercentLowFps = frames?.PointOnePercentLow,
                    AverageFrameTimeMs = frames?.AverageFrameTimeMs,
                    SampleCount = _samples,
                };
            }
        }
    }
}
