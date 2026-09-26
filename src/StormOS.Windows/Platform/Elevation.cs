using System.Security.Principal;

namespace StormOS.Windows.Platform;

/// <summary>Elevation helpers.</summary>
public static class Elevation
{
    /// <summary>Gets a value indicating whether the current process runs elevated or as LocalSystem.</summary>
    public static bool IsElevated
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return identity.IsSystem || new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    /// <summary>Gets the current user name (DOMAIN\user).</summary>
    public static string CurrentUserName
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return identity.Name;
        }
    }
}
