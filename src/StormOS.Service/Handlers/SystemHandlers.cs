using System.Reflection;
using StormOS.Core.Ipc;
using StormOS.Infrastructure.Ipc;
using StormOS.Performance.Frames;
using StormOS.Performance.Telemetry;

namespace StormOS.Service.Handlers;

/// <summary>Runtime information about the service used by health reporting.</summary>
public sealed class ServiceRuntime
{
    /// <summary>Gets the time the service started.</summary>
    public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;

    /// <summary>Gets the service version.</summary>
    public string Version { get; } = typeof(ServiceRuntime).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

    /// <summary>Gets or sets the IPC server (set once it is created).</summary>
    public IpcServer? Server { get; set; }

    /// <summary>Gets or sets a value indicating whether mock mode is active.</summary>
    public bool MockMode { get; set; }
}

/// <summary>system.hello.</summary>
public sealed class HelloHandler(ServiceRuntime runtime, FrameCaptureCoordinator frames) : IpcHandler<HelloRequest>
{
    /// <inheritdoc />
    public override string Operation => IpcOperations.Hello;

    /// <inheritdoc />
    protected override string? Validate(HelloRequest payload) =>
        string.IsNullOrWhiteSpace(payload.ClientName) || payload.ClientName.Length > 64 || payload.ClientVersion?.Length > 32 ? "Invalid client information." : null;

    /// <inheritdoc />
    protected override Task<object?> HandleAsync(HelloRequest payload, IIpcSession session, CancellationToken cancellationToken)
    {
        var capabilities = new List<string> { "telemetry", "optimization", "rollback", "sessions" };
        if (frames.GetStatus().Providers.Any(p => p.Available))
        {
            capabilities.Add("frames");
        }

        return Task.FromResult<object?>(new HelloResponse
        {
            ServiceVersion = runtime.Version,
            ProtocolVersion = IpcProtocol.Version,
            ClientTrust = session.Client.Trust.ToString(),
            Capabilities = capabilities,
            MockMode = runtime.MockMode,
        });
    }
}

/// <summary>system.health.</summary>
public sealed class HealthHandler(ServiceRuntime runtime, TelemetryHub telemetry, FrameCaptureCoordinator frames) : IpcHandler<NoPayload>
{
    /// <inheritdoc />
    public override string Operation => IpcOperations.Health;

    /// <inheritdoc />
    protected override Task<object?> HandleAsync(NoPayload payload, IIpcSession session, CancellationToken cancellationToken)
    {
        var status = frames.GetStatus();
        var notes = new List<string>();
        if (!status.Providers.Any(p => p.Available))
        {
            notes.Add("No frame capture method is available (install PresentMon).");
        }

        return Task.FromResult<object?>(new HealthResponse
        {
            Status = notes.Count == 0 ? "healthy" : "degraded",
            Version = runtime.Version,
            Uptime = DateTimeOffset.UtcNow - runtime.StartedAt,
            ConnectedClients = runtime.Server?.ConnectionCount ?? 0,
            TelemetrySubscribers = telemetry.SubscriberCount,
            SamplingMode = telemetry.Mode.ToString(),
            FrameCapture = status.Source,
            WorkingSetBytes = Environment.WorkingSet,
            Notes = notes,
        });
    }
}
