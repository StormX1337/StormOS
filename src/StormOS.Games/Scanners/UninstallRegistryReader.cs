using StormOS.Windows.RegistryAccess;

namespace StormOS.Games.Scanners;

/// <summary>An entry of the Windows "Apps &amp; features" uninstall registry.</summary>
/// <param name="Key">Registry sub key name.</param>
/// <param name="DisplayName">Display name.</param>
/// <param name="Publisher">Publisher.</param>
/// <param name="InstallLocation">Install location.</param>
/// <param name="DisplayVersion">Version.</param>
/// <param name="DisplayIcon">Icon path, often the main executable.</param>
public sealed record UninstallEntry(string Key, string DisplayName, string? Publisher, string? InstallLocation, string? DisplayVersion, string? DisplayIcon);

/// <summary>Reads uninstall entries from HKLM (64/32-bit) and HKCU.</summary>
public static class UninstallRegistryReader
{
    private const string Uninstall = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    private const string Uninstall32 = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";

    /// <summary>Reads all entries with a display name.</summary>
    /// <param name="registry">Registry access.</param>
    /// <returns>Entries.</returns>
    public static IReadOnlyList<UninstallEntry> Read(IRegistryAccess registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        var entries = new List<UninstallEntry>();
        foreach (var (hive, root) in new[] { (RegistryHive.LocalMachine, Uninstall), (RegistryHive.LocalMachine, Uninstall32), (RegistryHive.CurrentUser, Uninstall) })
        {
            foreach (var key in registry.GetSubKeyNames(hive, root))
            {
                var path = root + "\\" + key;
                if (registry.GetValue(hive, path, "DisplayName")?.Value is not string name || string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                entries.Add(new UninstallEntry(
                    key,
                    name.Trim(),
                    registry.GetValue(hive, path, "Publisher")?.Value as string,
                    (registry.GetValue(hive, path, "InstallLocation")?.Value as string)?.Trim('"', ' '),
                    registry.GetValue(hive, path, "DisplayVersion")?.Value as string,
                    registry.GetValue(hive, path, "DisplayIcon")?.Value as string));
            }
        }

        return entries;
    }
}
