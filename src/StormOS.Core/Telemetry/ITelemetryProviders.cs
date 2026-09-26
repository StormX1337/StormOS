namespace StormOS.Core.Telemetry;

/// <summary>A metric collector that participates in each sampling tick.</summary>
/// <typeparam name="T">The metric type produced.</typeparam>
public interface IMetricCollector<out T> : IDisposable
{
    /// <summary>Collects the current value. Called once per sampling tick from a single thread.</summary>
    /// <param name="mode">The current sampling mode.</param>
    /// <returns>The collected metrics.</returns>
    T Collect(SamplingMode mode);
}

/// <summary>Collects processor metrics.</summary>
public interface ICpuMetricCollector : IMetricCollector<CpuMetrics>;

/// <summary>Collects GPU metrics.</summary>
public interface IGpuMetricCollector : IMetricCollector<IReadOnlyList<GpuMetrics>>;

/// <summary>Collects memory metrics.</summary>
public interface IMemoryMetricCollector : IMetricCollector<MemoryMetrics>;

/// <summary>Collects disk metrics.</summary>
public interface IDiskMetricCollector : IMetricCollector<IReadOnlyList<DiskMetrics>>;

/// <summary>Collects network throughput metrics.</summary>
public interface INetworkMetricCollector : IMetricCollector<IReadOnlyList<NetworkInterfaceMetrics>>;

/// <summary>Collects system-wide metrics.</summary>
public interface ISystemMetricCollector : IMetricCollector<SystemMetrics>;

/// <summary>Produces live telemetry snapshots for any number of consumers.</summary>
public interface ITelemetryHub
{
    /// <summary>Gets the most recent snapshot, if any.</summary>
    MetricsSnapshot? Latest { get; }

    /// <summary>Gets the current sampling mode.</summary>
    SamplingMode Mode { get; }

    /// <summary>Subscribes to snapshots. Sampling runs only while at least one subscription is active.</summary>
    /// <param name="handler">Callback invoked for each snapshot.</param>
    /// <returns>A handle that ends the subscription when disposed.</returns>
    IDisposable Subscribe(Action<MetricsSnapshot> handler);

    /// <summary>Takes a single snapshot immediately, regardless of subscriptions.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The snapshot.</returns>
    Task<MetricsSnapshot> SampleOnceAsync(CancellationToken cancellationToken = default);

    /// <summary>Switches the sampling mode.</summary>
    /// <param name="mode">The new mode.</param>
    void SetMode(SamplingMode mode);
}

/// <summary>Supplies the currently active game, if any, to the telemetry pipeline.</summary>
public interface IActiveGameProvider
{
    /// <summary>Gets the active game name and process id.</summary>
    (string Name, int ProcessId)? ActiveGame { get; }
}
