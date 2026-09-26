using Microsoft.Win32;

namespace StormOS.App.Services;

/// <summary>
/// Visible, user-controlled autostart through the standard per-user Run key (shown in Task Manager › Startup apps).
/// Only written when the user enables "Start with Windows".
/// </summary>
public static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "STORM OS";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
        return key?.GetValue(ValueName) is string;
    }

    public static void Set(bool enabled, bool minimized)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (enabled && Environment.ProcessPath is { } exe)
        {
            key.SetValue(ValueName, $"\"{exe}\"{(minimized ? " --minimized" : string.Empty)}", RegistryValueKind.String);
        }
        else if (key.GetValue(ValueName) is not null)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
