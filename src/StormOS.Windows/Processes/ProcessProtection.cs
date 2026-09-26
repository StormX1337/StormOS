namespace StormOS.Windows.Processes;

/// <summary>Processes STORM OS never ends or re-prioritizes: core Windows, security software and anti-cheat.</summary>
public static class ProcessProtection
{
    private static readonly HashSet<string> Protected = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "Registry", "Memory Compression", "Secure System", "smss", "csrss", "wininit", "winlogon",
        "services", "lsass", "lsaiso", "svchost", "fontdrvhost", "dwm", "sihost", "ctfmon", "explorer", "spoolsv",
        "WUDFHost", "audiodg", "conhost", "dllhost", "RuntimeBroker", "SearchIndexer", "SecurityHealthService",
        "SecurityHealthSystray", "MsMpEng", "NisSrv", "MpDefenderCoreService", "smartscreen", "SgrmBroker",
        "StormOS", "StormOS.Service", "storm",
        "vgc", "vgtray", "EasyAntiCheat", "EasyAntiCheat_EOS", "BEService", "BEService_x64", "FACEIT", "FACEITService",
        "EAAntiCheat.GameService", "Vanguard", "mhyprot", "GameGuard", "nProtect", "xigncode",
    };

    /// <summary>Determines whether a process is protected.</summary>
    /// <param name="processName">Process name without extension.</param>
    /// <returns><see langword="true"/> when protected.</returns>
    public static bool IsProtected(string processName) =>
        Protected.Contains(Path.GetFileNameWithoutExtension(processName ?? string.Empty));
}
