namespace StormOS.Core.Startup;

/// <summary>Where a startup entry is registered.</summary>
public enum StartupSource
{
    /// <summary>HKCU\...\Run.</summary>
    RegistryUserRun,

    /// <summary>HKLM\...\Run.</summary>
    RegistryMachineRun,

    /// <summary>HKLM\...\WOW6432Node\...\Run.</summary>
    RegistryMachineRun32,

    /// <summary>The user's Startup folder.</summary>
    UserStartupFolder,

    /// <summary>The common Startup folder.</summary>
    CommonStartupFolder,

    /// <summary>A scheduled task triggered at logon (read-only in STORM OS).</summary>
    ScheduledTask,
}

/// <summary>A program that starts with Windows.</summary>
public sealed record StartupEntry
{
    /// <summary>Gets the stable entry id ("source|name").</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Gets the entry name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Gets the command line.</summary>
    public string Command { get; init; } = string.Empty;

    /// <summary>Gets the resolved executable path, when determinable.</summary>
    public string? ExecutablePath { get; init; }

    /// <summary>Gets the publisher from the file version info, when available.</summary>
    public string? Publisher { get; init; }

    /// <summary>Gets the source.</summary>
    public StartupSource Source { get; init; }

    /// <summary>Gets a value indicating whether the entry is enabled.</summary>
    public bool IsEnabled { get; init; }

    /// <summary>Gets a value indicating whether changing the entry requires administrative rights.</summary>
    public bool RequiresAdmin { get; init; }

    /// <summary>Gets a value indicating whether STORM OS can toggle this entry.</summary>
    public bool CanToggle { get; init; } = true;

    /// <summary>Gets the CPU time consumed since boot by the running process started from this entry, when running.</summary>
    public double? RuntimeCpuSeconds { get; init; }

    /// <summary>Gets the working set of the running process started from this entry, when running.</summary>
    public long? RuntimeMemoryBytes { get; init; }

    /// <summary>Gets the measured impact description.</summary>
    public string Impact => RuntimeCpuSeconds is { } cpu
        ? cpu switch { < 2 => "Low (measured)", < 15 => "Medium (measured)", _ => "High (measured)" }
        : "Not measured (process not running)";
}

/// <summary>Reads and toggles startup entries using the same StartupApproved mechanism as Task Manager.</summary>
public interface IStartupManager
{
    /// <summary>Lists startup entries.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Entries.</returns>
    Task<IReadOnlyList<StartupEntry>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets the enabled state of an entry.</summary>
    /// <param name="entryId">Entry id.</param>
    /// <returns>The state, or <see langword="null"/> when the entry does not exist.</returns>
    bool? IsEnabled(string entryId);

    /// <summary>Enables or disables an entry without deleting it.</summary>
    /// <param name="entryId">Entry id.</param>
    /// <param name="enabled">Target state.</param>
    void SetEnabled(string entryId, bool enabled);
}
