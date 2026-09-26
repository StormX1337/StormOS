using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StormOS.Core.Settings;
using StormOS.Core.Telemetry;
using StormOS.Infrastructure.Configuration;
using StormOS.Performance.Collections;
using StormOS.Performance.Telemetry;
using StormOS.Services.Client;

namespace StormOS.App.Services;

/// <summary>
/// Single source of live telemetry for the UI. Uses the service stream (which includes frame timing) when the
/// service is connected and falls back to an in-process hub otherwise. Keeps ten minutes of history for charts.
/// </summary>
public sealed class TelemetryFeed : IDisposable
{
    private const int HistoryCapacity = 3600;
    private readonly IStormServiceClient _service;
    private readonly TelemetryHub _localHub;
    private readonly ISettingsStore _settings;
    private readonly ILogger<TelemetryFeed> _logger;
    private readonly bool _mock;
    private readonly Lock _gate = new();
    private readonly RingBuffer<MetricsSnapshot> _history = new(HistoryCapacity);
    private IDisposable? _localSubscription;
    private IDisposable? _mockSubscription;

    /// <summary>Initializes a new instance of the <see cref="TelemetryFeed"/> class.</summary>
    /// <param name="service">Service client.</param>
    /// <param name="localHub">In-process fallback hub.</param>
    /// <param name="settings">Settings.</param>
    /// <param name="configuration">Configuration.</param>
    /// <param name="logger">Logger.</param>
    public TelemetryFeed(IStormServiceClient service, TelemetryHub localHub, ISettingsStore settings, IConfiguration configuration, ILogger<TelemetryFeed> logger)
    {
        _service = service;
        _localHub = localHub;
        _settings = settings;
        _logger = logger;
        _mock = MockModeGuard.IsActive(configuration.GetValue("Development:MockMode", false));
    }

    /// <summary>Raised for every snapshot (on a background thread).</summary>
    public event EventHandler<MetricsSnapshot>? SnapshotReceived;

    /// <summary>Gets the latest snapshot.</summary>
    public MetricsSnapshot? Latest { get; private set; }

    /// <summary>Gets a value indicating whether data comes from the service.</summary>
    public bool UsingService { get; private set; }

    /// <summary>Gets a value indicating whether development mock data is shown.</summary>
    public bool IsMock => _mock;

    /// <summary>Returns buffered snapshots (oldest first).</summary>
    /// <param name="count">Maximum number of snapshots.</param>
    /// <returns>Snapshots.</returns>
    public IReadOnlyList<MetricsSnapshot> History(int count = HistoryCapacity)
    {
        lock (_gate)
        {
            return _history.TakeLast(count);
        }
    }

    /// <summary>Starts the feed.</summary>
    public void Start()
    {
        if (_mock)
        {
            _mockSubscription = MockTelemetry.Start(OnSnapshot);
            return;
        }

        _service.TelemetryReceived += (_, snapshot) => OnSnapshot(snapshot);
        _service.StateChanged += (_, state) => _ = SwitchAsync(state);
        _ = SwitchAsync(_service.State);
        _ = _service.ConnectAsync();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _localSubscription?.Dispose();
        _mockSubscription?.Dispose();
    }

    private async Task SwitchAsync(ServiceConnectionState state)
    {
        if (state == ServiceConnectionState.Connected)
        {
            var result = await _service.SubscribeTelemetryAsync(_settings.Current.Performance.SamplingIntervalMs).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                UsingService = true;
                lock (_gate)
                {
                    _localSubscription?.Dispose();
                    _localSubscription = null;
                }

                _logger.LogInformation("Telemetry: using the STORM OS service");
                return;
            }
        }

        if (state is ServiceConnectionState.Unavailable or ServiceConnectionState.Disconnected)
        {
            UsingService = false;
            lock (_gate)
            {
                _localSubscription ??= _localHub.Subscribe(OnSnapshot);
            }
        }
    }

    private void OnSnapshot(MetricsSnapshot snapshot)
    {
        lock (_gate)
        {
            _history.Add(snapshot);
        }

        Latest = snapshot;
        SnapshotReceived?.Invoke(this, snapshot);
    }
}
