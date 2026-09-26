using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StormOS.Core.Common;
using StormOS.Core.Frames;
using StormOS.Core.Ipc;
using StormOS.Performance.Collections;

namespace StormOS.Performance.Frames;

/// <summary>
/// Owns the single active frame capture: selects the best available provider, buffers frames for live statistics
/// and benchmarks, and keeps a constant-memory histogram for whole-session statistics.
/// </summary>
public sealed class FrameCaptureCoordinator : IAsyncDisposable
{
    private readonly IReadOnlyList<IFrameCaptureProvider> _providers;
    private readonly FrameCaptureOptions _options;
    private readonly ILogger<FrameCaptureCoordinator> _logger;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly Lock _gate = new();
    private readonly RingBuffer<FrameSample> _buffer;
    private readonly FrameTimeHistogram _histogram = new();
    private IFrameCaptureSession? _session;
    private CancellationTokenSource? _pumpCts;
    private Task? _pump;
    private string? _processName;
    private string? _lastError;
    private long _framesReceived;

    /// <summary>Initializes a new instance of the <see cref="FrameCaptureCoordinator"/> class.</summary>
    /// <param name="providers">Registered providers.</param>
    /// <param name="options">Options.</param>
    /// <param name="logger">Logger.</param>
    public FrameCaptureCoordinator(IEnumerable<IFrameCaptureProvider> providers, IOptions<FrameCaptureOptions> options, ILogger<FrameCaptureCoordinator> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _providers = providers.OrderByDescending(p => p.Priority).ToList();
        _logger = logger;
        _buffer = new RingBuffer<FrameSample>(Math.Clamp(_options.MaxBufferedFrames, 1_000, 1_000_000));
    }

    /// <summary>Raised when the capture starts or stops.</summary>
    public event EventHandler<FrameCaptureStatus>? StatusChanged;

    /// <summary>Gets the total number of frames received by the active capture.</summary>
    public long FramesReceived => Interlocked.Read(ref _framesReceived);

    /// <summary>Gets the current status including provider availability.</summary>
    /// <returns>The status.</returns>
    public FrameCaptureStatus GetStatus()
    {
        var session = _session;
        return new FrameCaptureStatus
        {
            Active = session is not null,
            ProcessId = session?.ProcessId,
            ProcessName = _processName,
            Source = session?.Source,
            LastError = _lastError,
            Providers = _providers.Select(p =>
            {
                var availability = p.CheckAvailability();
                return new ProviderAvailability(p.Name, availability.IsAvailable, availability.Reason);
            }).ToList(),
        };
    }

    /// <summary>Starts capturing a process, replacing any active capture.</summary>
    /// <param name="processId">The process id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success or a friendly error.</returns>
    public async Task<Result> StartAsync(int processId, CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_session?.ProcessId == processId)
            {
                return Result.Success;
            }

            await StopCoreAsync().ConfigureAwait(false);
            string processName;
            try
            {
                using var process = Process.GetProcessById(processId);
                processName = process.ProcessName;
            }
            catch (ArgumentException)
            {
                return Result.Failure(StormErrorCodes.NotFound, "The game process is no longer running.");
            }

            foreach (var provider in _providers)
            {
                if (provider is EtwPresentFrameCaptureProvider && !_options.AllowEtwFallback)
                {
                    continue;
                }

                var availability = provider.CheckAvailability();
                if (!availability.IsAvailable)
                {
                    continue;
                }

                try
                {
                    var session = await provider.StartAsync(processId, cancellationToken).ConfigureAwait(false);
                    lock (_gate)
                    {
                        _buffer.Clear();
                        _histogram.Clear();
                        Interlocked.Exchange(ref _framesReceived, 0);
                    }

                    _session = session;
                    _processName = processName;
                    _lastError = null;
                    _pumpCts = new CancellationTokenSource();
                    _pump = Task.Run(() => PumpAsync(session, _pumpCts.Token), CancellationToken.None);
                    _logger.LogInformation("Frame capture started for {Process} ({Pid}) using {Provider}", processName, processId, provider.Name);
                    StatusChanged?.Invoke(this, GetStatus());
                    return Result.Success;
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
                {
                    _logger.LogWarning(ex, "Frame capture provider {Provider} failed to start", provider.Name);
                    _lastError = $"{provider.Name} could not start.";
                }
            }

            _lastError ??= "No frame capture method is available. Install PresentMon or run the STORM OS service.";
            return Result.Failure(StormErrorCodes.NotSupported, _lastError);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    /// <summary>Stops the active capture.</summary>
    /// <returns>A task representing the operation.</returns>
    public async Task StopAsync()
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    /// <summary>Computes live frame metrics for telemetry snapshots.</summary>
    /// <returns>The metrics, or <see langword="null"/> when no capture is active or no frames arrived yet.</returns>
    public FrameMetrics? GetLiveMetrics()
    {
        var session = _session;
        if (session is null)
        {
            return null;
        }

        FrameSample[] window;
        lock (_gate)
        {
            if (_buffer.Count == 0)
            {
                return null;
            }

            window = _buffer.ToArray();
        }

        var windowSamples = TrimToWindow(window, _options.LiveWindowSeconds);
        return new FrameMetrics
        {
            ProcessId = session.ProcessId,
            ProcessName = _processName ?? string.Empty,
            Source = session.Source,
            Fps = FrameStatisticsCalculator.RecentFps(window),
            FrameTimeMs = window[^1].FrameTimeMs,
            Window = FrameStatisticsCalculator.Compute(windowSamples),
        };
    }

    /// <summary>Returns whole-capture statistics from the histogram (constant memory, bin accurate).</summary>
    /// <returns>Average FPS, 1% low, 0.1% low and average frame time, or <see langword="null"/> without frames.</returns>
    public (double AverageFps, double OnePercentLow, double PointOnePercentLow, double AverageFrameTimeMs)? GetSessionStatistics()
    {
        lock (_gate)
        {
            if (_histogram.AverageFps is not { } avg || _histogram.PercentileMs(99) is not { } p99 || _histogram.PercentileMs(99.9) is not { } p999)
            {
                return null;
            }

            return (avg, 1000.0 / p99, 1000.0 / p999, _histogram.AverageFrameTimeMs ?? 0);
        }
    }

    /// <summary>
    /// Captures a measurement window for a benchmark: waits for the warm-up, then records frames for the duration.
    /// </summary>
    /// <param name="processId">Game process id.</param>
    /// <param name="warmup">Warm-up excluded from results.</param>
    /// <param name="duration">Measurement duration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The measured frames, or an error.</returns>
    public async Task<Result<IReadOnlyList<FrameSample>>> CaptureWindowAsync(int processId, TimeSpan warmup, TimeSpan duration, CancellationToken cancellationToken = default)
    {
        var started = await StartAsync(processId, cancellationToken).ConfigureAwait(false);
        if (!started.IsSuccess)
        {
            return Result<IReadOnlyList<FrameSample>>.Fail(started.Error);
        }

        await Task.Delay(warmup, cancellationToken).ConfigureAwait(false);
        var startCount = FramesReceived;
        await Task.Delay(duration, cancellationToken).ConfigureAwait(false);
        var captured = FramesReceived - startCount;
        if (captured <= 0 || _session?.ProcessId != processId)
        {
            return Result<IReadOnlyList<FrameSample>>.Fail(StormErrorCodes.NotFound, "No frames were captured. Make sure the game is running in the foreground and rendering.");
        }

        lock (_gate)
        {
            return Result<IReadOnlyList<FrameSample>>.Ok(_buffer.TakeLast((int)Math.Min(captured, _buffer.Count)));
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _lifecycle.Dispose();
    }

    private static FrameSample[] TrimToWindow(FrameSample[] samples, int seconds)
    {
        double elapsed = 0;
        var start = samples.Length;
        while (start > 0 && elapsed < seconds * 1000.0)
        {
            start--;
            elapsed += Math.Max(0, samples[start].FrameTimeMs);
        }

        return samples[start..];
    }

    private async Task PumpAsync(IFrameCaptureSession session, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var sample in session.Frames.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                lock (_gate)
                {
                    _buffer.Add(sample);
                    _histogram.Add(sample.FrameTimeMs);
                }

                Interlocked.Increment(ref _framesReceived);
            }

            var reason = await session.Completion.ConfigureAwait(false);
            if (reason is not null)
            {
                _lastError = reason;
                _logger.LogWarning("Frame capture ended: {Reason}", reason);
            }
        }
        catch (OperationCanceledException)
        {
            // Stopped.
        }
    }

    private async Task StopCoreAsync()
    {
        var session = _session;
        if (session is null)
        {
            return;
        }

        _session = null;
        if (_pumpCts is not null)
        {
            await _pumpCts.CancelAsync().ConfigureAwait(false);
        }

        await session.DisposeAsync().ConfigureAwait(false);
        if (_pump is not null)
        {
            await _pump.ConfigureAwait(false);
        }

        _pumpCts?.Dispose();
        _pumpCts = null;
        _pump = null;
        _logger.LogInformation("Frame capture stopped for {Process}", _processName);
        StatusChanged?.Invoke(this, GetStatus());
    }
}
