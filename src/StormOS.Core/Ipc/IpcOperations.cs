namespace StormOS.Core.Ipc;

/// <summary>Operation names understood by the service. Each has a server side validator and policy.</summary>
public static class IpcOperations
{
    /// <summary>Handshake: returns service version and capabilities.</summary>
    public const string Hello = "system.hello";

    /// <summary>Returns service health.</summary>
    public const string Health = "system.health";

    /// <summary>Subscribes the connection to telemetry snapshots.</summary>
    public const string TelemetrySubscribe = "telemetry.subscribe";

    /// <summary>Unsubscribes the connection from telemetry snapshots.</summary>
    public const string TelemetryUnsubscribe = "telemetry.unsubscribe";

    /// <summary>Returns a single telemetry snapshot.</summary>
    public const string TelemetrySnapshot = "telemetry.snapshot";

    /// <summary>Returns the hardware inventory.</summary>
    public const string HardwareInventory = "hardware.inventory";

    /// <summary>Starts a frame capture for a process.</summary>
    public const string FramesStart = "frames.start";

    /// <summary>Stops the active frame capture.</summary>
    public const string FramesStop = "frames.stop";

    /// <summary>Returns the frame capture status.</summary>
    public const string FramesStatus = "frames.status";

    /// <summary>Returns the running games.</summary>
    public const string GamesRunning = "games.running";

    /// <summary>Lists optimization rules executed by the service.</summary>
    public const string OptimizationRules = "optimization.rules";

    /// <summary>Detects the state of a service rule.</summary>
    public const string OptimizationDetect = "optimization.detect";

    /// <summary>Applies a service rule.</summary>
    public const string OptimizationApply = "optimization.apply";

    /// <summary>Lists service optimization records.</summary>
    public const string OptimizationHistory = "optimization.history";

    /// <summary>Rolls back a service change.</summary>
    public const string OptimizationRestore = "optimization.restore";

    /// <summary>Rolls back all active service changes.</summary>
    public const string OptimizationRestoreAll = "optimization.restoreAll";

    /// <summary>Sets the priority of a process (session scoped).</summary>
    public const string ProcessSetPriority = "process.setPriority";

    /// <summary>Terminates a process the user cannot end without elevation.</summary>
    public const string ProcessTerminate = "process.terminate";

    /// <summary>Lists Windows services with start types.</summary>
    public const string ServicesList = "services.list";

    /// <summary>Returns recent service log lines.</summary>
    public const string LogsTail = "logs.tail";

    /// <summary>Lists recorded game sessions.</summary>
    public const string SessionsList = "sessions.list";

    /// <summary>Runs a gaming benchmark via the service frame capture.</summary>
    public const string BenchmarkGaming = "benchmark.gaming";

    /// <summary>Reads downsampled metric history recorded by the service.</summary>
    public const string MetricHistory = "history.metrics";
}

/// <summary>Event topics pushed by the service.</summary>
public static class IpcTopics
{
    /// <summary>A telemetry snapshot.</summary>
    public const string Telemetry = "telemetry.snapshot";

    /// <summary>A game started.</summary>
    public const string GameStarted = "game.started";

    /// <summary>A game stopped.</summary>
    public const string GameStopped = "game.stopped";

    /// <summary>A service side optimization changed state.</summary>
    public const string OptimizationChanged = "optimization.changed";

    /// <summary>The frame capture state changed.</summary>
    public const string FrameCaptureChanged = "frames.changed";
}
