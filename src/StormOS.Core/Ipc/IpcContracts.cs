using StormOS.Core.Games;
using StormOS.Core.Processes;

namespace StormOS.Core.Ipc;

/// <summary>Handshake request.</summary>
/// <param name="ClientName">Client name, for example "StormOS.App".</param>
/// <param name="ClientVersion">Client version.</param>
public sealed record HelloRequest(string ClientName, string ClientVersion);

/// <summary>Handshake response.</summary>
public sealed record HelloResponse
{
    /// <summary>Gets the service version.</summary>
    public string ServiceVersion { get; init; } = string.Empty;

    /// <summary>Gets the protocol version.</summary>
    public int ProtocolVersion { get; init; }

    /// <summary>Gets the trust level assigned to the caller.</summary>
    public string ClientTrust { get; init; } = string.Empty;

    /// <summary>Gets the capabilities of the service on this machine.</summary>
    public IReadOnlyList<string> Capabilities { get; init; } = [];

    /// <summary>Gets a value indicating whether the service runs with mock data (development only).</summary>
    public bool MockMode { get; init; }
}

/// <summary>Service health.</summary>
public sealed record HealthResponse
{
    /// <summary>Gets the overall status: "healthy", "degraded" or "unhealthy".</summary>
    public string Status { get; init; } = "healthy";

    /// <summary>Gets the service version.</summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>Gets the service uptime.</summary>
    public TimeSpan Uptime { get; init; }

    /// <summary>Gets the number of connected clients.</summary>
    public int ConnectedClients { get; init; }

    /// <summary>Gets the number of telemetry subscribers.</summary>
    public int TelemetrySubscribers { get; init; }

    /// <summary>Gets the sampling mode.</summary>
    public string SamplingMode { get; init; } = string.Empty;

    /// <summary>Gets the active frame capture source, if any.</summary>
    public string? FrameCapture { get; init; }

    /// <summary>Gets the service process working set in bytes.</summary>
    public long WorkingSetBytes { get; init; }

    /// <summary>Gets component health notes.</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];
}

/// <summary>Telemetry subscription request.</summary>
/// <param name="IntervalMs">Requested interval in ms (clamped server side).</param>
public sealed record TelemetrySubscribeRequest(int IntervalMs = 1000);

/// <summary>Frame capture start request.</summary>
/// <param name="ProcessId">The process id to capture.</param>
public sealed record FrameCaptureStartRequest(int ProcessId);

/// <summary>Frame capture status.</summary>
public sealed record FrameCaptureStatus
{
    /// <summary>Gets a value indicating whether a capture is active.</summary>
    public bool Active { get; init; }

    /// <summary>Gets the captured process id.</summary>
    public int? ProcessId { get; init; }

    /// <summary>Gets the captured process name.</summary>
    public string? ProcessName { get; init; }

    /// <summary>Gets the provider in use.</summary>
    public string? Source { get; init; }

    /// <summary>Gets available providers and their state.</summary>
    public IReadOnlyList<ProviderAvailability> Providers { get; init; } = [];

    /// <summary>Gets the last error.</summary>
    public string? LastError { get; init; }
}

/// <summary>Availability of a provider.</summary>
/// <param name="Name">Provider name.</param>
/// <param name="Available">Whether it can be used.</param>
/// <param name="Reason">Reason when unavailable.</param>
public sealed record ProviderAvailability(string Name, bool Available, string? Reason);

/// <summary>Detect request for a rule.</summary>
/// <param name="RuleId">Rule id.</param>
/// <param name="Parameters">Rule parameters.</param>
public sealed record RuleRequest(string RuleId, IReadOnlyDictionary<string, string>? Parameters);

/// <summary>Restore request.</summary>
/// <param name="ChangeId">Change id.</param>
public sealed record RestoreRequest(Guid ChangeId);

/// <summary>Process priority request.</summary>
/// <param name="ProcessId">Process id.</param>
/// <param name="Priority">Target priority.</param>
public sealed record ProcessPriorityRequest(int ProcessId, ProcessPriority Priority);

/// <summary>Process termination request.</summary>
/// <param name="ProcessId">Process id.</param>
/// <param name="ExpectedName">Expected process name, guarding against PID reuse.</param>
public sealed record ProcessTerminateRequest(int ProcessId, string ExpectedName);

/// <summary>A Windows service entry.</summary>
public sealed record ServiceEntry
{
    /// <summary>Gets the service name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Gets the display name.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Gets the status.</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>Gets the start type.</summary>
    public string StartType { get; init; } = string.Empty;

    /// <summary>Gets the process id, when running.</summary>
    public int? ProcessId { get; init; }

    /// <summary>Gets the working set of the hosting process, when measurable.</summary>
    public long? WorkingSetBytes { get; init; }

    /// <summary>Gets STORM OS's assessment, for example "Required by Windows".</summary>
    public string? Assessment { get; init; }

    /// <summary>Gets the recommendation, if any.</summary>
    public string? Recommendation { get; init; }
}

/// <summary>Log tail request.</summary>
/// <param name="Lines">Number of lines (max 1000).</param>
public sealed record LogTailRequest(int Lines = 200);

/// <summary>Log tail response.</summary>
/// <param name="File">Log file name.</param>
/// <param name="Lines">Log lines.</param>
public sealed record LogTailResponse(string File, IReadOnlyList<string> Lines);

/// <summary>Running games response.</summary>
/// <param name="Games">Running games.</param>
public sealed record RunningGamesResponse(IReadOnlyList<RunningGame> Games);

/// <summary>Gaming benchmark request.</summary>
/// <param name="ProcessId">Game process id.</param>
/// <param name="DurationSeconds">Capture duration.</param>
/// <param name="WarmupSeconds">Warm-up period.</param>
/// <param name="Label">Label such as "before" or "after".</param>
public sealed record GamingBenchmarkRequest(int ProcessId, int DurationSeconds = 60, int WarmupSeconds = 5, string? Label = null);
