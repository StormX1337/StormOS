namespace StormOS.Core.Processes;

/// <summary>Windows process priority classes that STORM OS may set. Realtime is intentionally excluded.</summary>
public enum ProcessPriority
{
    /// <summary>IDLE_PRIORITY_CLASS.</summary>
    Idle,

    /// <summary>BELOW_NORMAL_PRIORITY_CLASS.</summary>
    BelowNormal,

    /// <summary>NORMAL_PRIORITY_CLASS.</summary>
    Normal,

    /// <summary>ABOVE_NORMAL_PRIORITY_CLASS.</summary>
    AboveNormal,

    /// <summary>HIGH_PRIORITY_CLASS.</summary>
    High,
}

/// <summary>Authenticode signature status of an executable.</summary>
public enum SignatureStatus
{
    /// <summary>Not checked or not accessible.</summary>
    Unknown,

    /// <summary>Signed and trusted.</summary>
    Valid,

    /// <summary>Not signed.</summary>
    NotSigned,

    /// <summary>Signed but the signature is invalid or untrusted.</summary>
    Invalid,
}

/// <summary>A running process.</summary>
public sealed record ProcessEntry
{
    /// <summary>Gets the process id.</summary>
    public int ProcessId { get; init; }

    /// <summary>Gets the process name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Gets the executable path, when accessible.</summary>
    public string? Path { get; init; }

    /// <summary>Gets the CPU usage in percent of total system capacity since the previous sample.</summary>
    public double CpuPercent { get; init; }

    /// <summary>Gets the private working set in bytes.</summary>
    public long WorkingSetBytes { get; init; }

    /// <summary>Gets the GPU usage in percent, when measurable.</summary>
    public double? GpuPercent { get; init; }

    /// <summary>Gets the priority class, when accessible.</summary>
    public ProcessPriority? Priority { get; init; }

    /// <summary>Gets the file publisher (company name), when available.</summary>
    public string? Publisher { get; init; }

    /// <summary>Gets the signature status.</summary>
    public SignatureStatus Signature { get; init; }

    /// <summary>Gets the session id.</summary>
    public int SessionId { get; init; }

    /// <summary>Gets the thread count.</summary>
    public int ThreadCount { get; init; }

    /// <summary>Gets the handle count.</summary>
    public int HandleCount { get; init; }

    /// <summary>Gets a value indicating whether the process is protected from termination by STORM OS.</summary>
    public bool IsCritical { get; init; }
}

/// <summary>Enumerates running processes.</summary>
public interface IProcessInspector
{
    /// <summary>Takes a snapshot of running processes. CPU usage is computed against the previous snapshot.</summary>
    /// <returns>The processes.</returns>
    IReadOnlyList<ProcessEntry> Snapshot();

    /// <summary>Verifies the Authenticode signature of an executable (cached per path).</summary>
    /// <param name="path">Executable path.</param>
    /// <returns>The signature status.</returns>
    SignatureStatus VerifySignature(string path);

    /// <summary>Determines whether STORM OS must never modify or end a process with this name.</summary>
    /// <param name="processName">Process name without extension.</param>
    /// <returns><see langword="true"/> when protected.</returns>
    bool IsProtected(string processName);
}

/// <summary>Changes process scheduling or ends processes, with safety checks.</summary>
public interface IProcessController
{
    /// <summary>Gets the priority of a process.</summary>
    /// <param name="processId">Process id.</param>
    /// <returns>The priority or <see langword="null"/> when inaccessible.</returns>
    ProcessPriority? GetPriority(int processId);

    /// <summary>Sets the priority of a process.</summary>
    /// <param name="processId">Process id.</param>
    /// <param name="priority">Priority.</param>
    /// <returns>Success or a friendly error.</returns>
    Common.Result SetPriority(int processId, ProcessPriority priority);

    /// <summary>Ends a process after checking it is not protected and the name still matches.</summary>
    /// <param name="processId">Process id.</param>
    /// <param name="expectedName">Expected process name.</param>
    /// <returns>Success or a friendly error.</returns>
    Common.Result Terminate(int processId, string expectedName);
}

/// <summary>Provides per-process GPU utilization, when measurable.</summary>
public interface IProcessGpuUsageProvider
{
    /// <summary>Gets the most recent GPU utilization per process id (busiest engine, percent).</summary>
    /// <returns>Utilization by process id.</returns>
    IReadOnlyDictionary<int, double> GetGpuUsageByProcess();
}
