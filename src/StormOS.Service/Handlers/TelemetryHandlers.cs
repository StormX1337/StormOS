using StormOS.Core.Hardware;
using StormOS.Core.Ipc;
using StormOS.Core.Telemetry;
using StormOS.Infrastructure.Ipc;
using StormOS.Performance.Telemetry;

namespace StormOS.Service.Handlers;

/// <summary>telemetry.subscribe: pushes snapshots to the connection until it unsubscribes or disconnects.</summary>
public sealed class TelemetrySubscribeHandler(TelemetryHub hub) : IpcHandler<TelemetrySubscribeRequest>
{
    private const string ResourceKey = "telemetry";

    /// <inheritdoc />
    public override string Operation => IpcOperations.TelemetrySubscribe;

    /// <inheritdoc />
    protected override string? Validate(TelemetrySubscribeRequest payload) =>
        payload.IntervalMs is < 250 or > 10_000 ? "The interval must be between 250 and 10000 ms." : null;

    /// <inheritdoc />
    protected override Task<object?> HandleAsync(TelemetrySubscribeRequest payload, IIpcSession session, CancellationToken cancellationToken)
    {
        if (session.Resources.ContainsKey(ResourceKey))
        {
            return Task.FromResult<object?>(true);
        }

        hub.SetInterval(payload.IntervalMs);
        var subscription = hub.Subscribe(snapshot => _ = session.SendEventAsync(IpcTopics.Telemetry, snapshot));
        if (!session.Resources.TryAdd(ResourceKey, subscription))
        {
            subscription.Dispose();
        }

        return Task.FromResult<object?>(true);
    }

    /// <summary>Removes the subscription of a session.</summary>
    /// <param name="session">The session.</param>
    internal static void Remove(IIpcSession session)
    {
        if (session.Resources.TryRemove(ResourceKey, out var subscription))
        {
            subscription.Dispose();
        }
    }
}

/// <summary>telemetry.unsubscribe.</summary>
public sealed class TelemetryUnsubscribeHandler : IpcHandler<NoPayload>
{
    /// <inheritdoc />
    public override string Operation => IpcOperations.TelemetryUnsubscribe;

    /// <inheritdoc />
    protected override Task<object?> HandleAsync(NoPayload payload, IIpcSession session, CancellationToken cancellationToken)
    {
        TelemetrySubscribeHandler.Remove(session);
        return Task.FromResult<object?>(true);
    }
}

/// <summary>telemetry.snapshot.</summary>
public sealed class TelemetrySnapshotHandler(ITelemetryHub hub) : IpcHandler<NoPayload>
{
    /// <inheritdoc />
    public override string Operation => IpcOperations.TelemetrySnapshot;

    /// <inheritdoc />
    protected override async Task<object?> HandleAsync(NoPayload payload, IIpcSession session, CancellationToken cancellationToken) =>
        hub.Latest is { } latest && DateTimeOffset.UtcNow - latest.Timestamp < TimeSpan.FromSeconds(3)
            ? latest
            : await hub.SampleOnceAsync(cancellationToken).ConfigureAwait(false);
}

/// <summary>hardware.inventory (cached for five minutes).</summary>
public sealed class HardwareInventoryHandler(IHardwareInventoryProvider inventory) : IpcHandler<NoPayload>, IDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private HardwareInventory? _cached;

    /// <inheritdoc />
    public override string Operation => IpcOperations.HardwareInventory;

    /// <inheritdoc />
    public void Dispose() => _lock.Dispose();

    /// <inheritdoc />
    protected override async Task<object?> HandleAsync(NoPayload payload, IIpcSession session, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cached is null || DateTimeOffset.UtcNow - _cached.CollectedAt > TimeSpan.FromMinutes(5))
            {
                _cached = await inventory.GetInventoryAsync(cancellationToken).ConfigureAwait(false);
            }

            return _cached;
        }
        finally
        {
            _lock.Release();
        }
    }
}
