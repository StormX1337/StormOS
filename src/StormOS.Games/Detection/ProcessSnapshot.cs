namespace StormOS.Games.Detection;

/// <summary>A lightweight view of a running process used by game detection.</summary>
/// <param name="ProcessId">Process id.</param>
/// <param name="Name">Process name without extension.</param>
/// <param name="Path">Executable path, when accessible.</param>
/// <param name="StartTime">Start time, used with the id to detect PID reuse.</param>
public sealed record ProcessSnapshot(int ProcessId, string Name, string? Path, DateTime StartTime);

/// <summary>Provides process snapshots and command lines (abstracted for testing).</summary>
public interface IProcessSource
{
    /// <summary>Lists running processes.</summary>
    /// <returns>Processes.</returns>
    IReadOnlyList<ProcessSnapshot> List();

    /// <summary>Reads the command line of a process.</summary>
    /// <param name="processId">Process id.</param>
    /// <returns>The command line or <see langword="null"/>.</returns>
    string? GetCommandLine(int processId);

    /// <summary>Gets the process id owning the foreground window.</summary>
    /// <returns>The process id or <see langword="null"/>.</returns>
    int? GetForegroundProcessId();
}
