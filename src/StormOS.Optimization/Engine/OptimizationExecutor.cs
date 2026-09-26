namespace StormOS.Optimization.Engine;

/// <summary>Describes the process executing optimizations.</summary>
/// <param name="Name">"app" (user context) or "service" (LocalSystem).</param>
/// <param name="IsElevated">Whether the process has administrative rights.</param>
/// <param name="WindowsBuild">Windows build number.</param>
public sealed record OptimizationExecutor(string Name, bool IsElevated, int WindowsBuild)
{
    /// <summary>Executor name of the desktop app.</summary>
    public const string App = "app";

    /// <summary>Executor name of the Windows service.</summary>
    public const string Service = "service";

    /// <summary>Gets a value indicating whether this executor is the privileged service.</summary>
    public bool IsService => Name == Service;
}
