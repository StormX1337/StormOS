namespace StormOS.Infrastructure.Configuration;

/// <summary>
/// Controls the development mock mode. Mock data is only possible in builds compiled with STORM_MOCK_MODE
/// (never Release, enforced by Directory.Build.targets) and must additionally be requested explicitly.
/// </summary>
public static class MockModeGuard
{
    /// <summary>Gets a value indicating whether this build can produce mock data at all.</summary>
    public static bool IsCompiledIn =>
#if STORM_MOCK_MODE
        true;
#else
        false;
#endif

    /// <summary>Determines whether mock mode is active for this process.</summary>
    /// <param name="requested">Whether configuration requested mock mode.</param>
    /// <returns><see langword="true"/> only when compiled in, requested and running in Development.</returns>
    public static bool IsActive(bool requested) => IsCompiledIn && requested && StormConfiguration.IsDevelopment;
}
