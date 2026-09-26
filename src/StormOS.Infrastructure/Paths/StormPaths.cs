namespace StormOS.Infrastructure.Paths;

/// <summary>Well known STORM OS directories.</summary>
public interface IStormPaths
{
    /// <summary>Gets the per-user data directory (%LOCALAPPDATA%\StormOS).</summary>
    string UserData { get; }

    /// <summary>Gets the machine-wide data directory used by the service (%ProgramData%\StormOS).</summary>
    string MachineData { get; }

    /// <summary>Gets the log directory of the current process.</summary>
    string Logs { get; }

    /// <summary>Gets the directory containing bundled game profiles.</summary>
    string BundledProfiles { get; }

    /// <summary>Gets the directory containing administrator managed profile overrides.</summary>
    string ProfileOverrides { get; }

    /// <summary>Gets the directory of the running executable.</summary>
    string ApplicationDirectory { get; }
}

/// <summary>Whether the current process is the per-user app or the machine-wide service.</summary>
public enum StormProcessKind
{
    /// <summary>The desktop app or CLI running as the interactive user.</summary>
    User,

    /// <summary>The Windows service running as LocalSystem.</summary>
    Service,
}

/// <summary>Default path layout.</summary>
public sealed class StormPaths : IStormPaths
{
    /// <summary>Initializes a new instance of the <see cref="StormPaths"/> class.</summary>
    /// <param name="kind">The process kind.</param>
    /// <param name="rootOverride">Optional root used by tests and portable mode instead of the system folders.</param>
    public StormPaths(StormProcessKind kind, string? rootOverride = null)
    {
        ApplicationDirectory = AppContext.BaseDirectory;
        if (!string.IsNullOrWhiteSpace(rootOverride))
        {
            UserData = Path.Combine(rootOverride, "user");
            MachineData = Path.Combine(rootOverride, "machine");
        }
        else
        {
            UserData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create), "StormOS");
            MachineData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData, Environment.SpecialFolderOption.Create), "StormOS");
        }

        Logs = Path.Combine(kind == StormProcessKind.Service ? MachineData : UserData, "logs");
        BundledProfiles = Path.Combine(ApplicationDirectory, "profiles");
        ProfileOverrides = Path.Combine(MachineData, "profiles");
    }

    /// <inheritdoc />
    public string UserData { get; }

    /// <inheritdoc />
    public string MachineData { get; }

    /// <inheritdoc />
    public string Logs { get; }

    /// <inheritdoc />
    public string BundledProfiles { get; }

    /// <inheritdoc />
    public string ProfileOverrides { get; }

    /// <inheritdoc />
    public string ApplicationDirectory { get; }

    /// <summary>Creates the directories required by the process kind.</summary>
    /// <param name="kind">The process kind.</param>
    public void EnsureCreated(StormProcessKind kind)
    {
        Directory.CreateDirectory(kind == StormProcessKind.Service ? MachineData : UserData);
        Directory.CreateDirectory(Logs);
    }
}
