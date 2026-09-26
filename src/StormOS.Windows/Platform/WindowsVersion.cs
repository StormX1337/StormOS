using Microsoft.Win32;
using StormOS.Core.Hardware;
using StormOS.Windows.Interop;

namespace StormOS.Windows.Platform;

/// <summary>Reads the operating system version using RtlGetVersion (not subject to manifest version lies).</summary>
public sealed class WindowsVersion : IOperatingSystemInfoProvider
{
    /// <summary>Minimum supported Windows build (Windows 10 2004, the Windows App SDK baseline used by STORM OS).</summary>
    public const int MinimumSupportedBuild = 19041;

    /// <summary>First Windows 11 build.</summary>
    public const int Windows11Build = 22000;

    /// <summary>Gets the current build number.</summary>
    /// <returns>The build number.</returns>
    public static int GetBuildNumber()
    {
        var info = new NtDll.OsVersionInfoEx { OsVersionInfoSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NtDll.OsVersionInfoEx>(), CsdVersion = string.Empty };
        return NtDll.RtlGetVersion(ref info) == 0 ? (int)info.BuildNumber : Environment.OSVersion.Version.Build;
    }

    /// <inheritdoc />
    public OsInfo GetOsInfo()
    {
        var build = GetBuildNumber();
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        var product = key?.GetValue("ProductName") as string ?? "Windows";
        // ProductName still reads "Windows 10" on Windows 11; the build number is authoritative.
        if (build >= Windows11Build)
        {
            product = product.Replace("Windows 10", "Windows 11", StringComparison.Ordinal);
        }

        var uptime = TimeSpan.FromMilliseconds(Kernel32.GetTickCount64());
        return new OsInfo
        {
            ProductName = product,
            DisplayVersion = key?.GetValue("DisplayVersion") as string ?? key?.GetValue("ReleaseId") as string ?? string.Empty,
            BuildNumber = build,
            Revision = key?.GetValue("UBR") is int ubr ? ubr : 0,
            Architecture = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant(),
            HostName = Environment.MachineName,
            BootTime = DateTimeOffset.Now - uptime,
        };
    }

    /// <summary>Gets the system uptime.</summary>
    /// <returns>The uptime.</returns>
    public static TimeSpan GetUptime() => TimeSpan.FromMilliseconds(Kernel32.GetTickCount64());
}

/// <summary>Result of the startup OS compatibility check.</summary>
/// <param name="Supported">Whether the OS is supported.</param>
/// <param name="FullySupported">Whether the OS is the primary target (Windows 11 x64/ARM64).</param>
/// <param name="Message">Explanation.</param>
public sealed record CompatibilityResult(bool Supported, bool FullySupported, string Message);

/// <summary>Checks OS compatibility at startup.</summary>
public static class CompatibilityCheck
{
    /// <summary>Evaluates compatibility of a build and architecture.</summary>
    /// <param name="build">Windows build number.</param>
    /// <param name="is64Bit">Whether the OS is 64-bit.</param>
    /// <returns>The result.</returns>
    public static CompatibilityResult Evaluate(int build, bool is64Bit)
    {
        if (!is64Bit)
        {
            return new CompatibilityResult(false, false, "STORM OS requires a 64-bit edition of Windows.");
        }

        if (build < WindowsVersion.MinimumSupportedBuild)
        {
            return new CompatibilityResult(false, false, $"STORM OS requires Windows 10 version 2004 (build {WindowsVersion.MinimumSupportedBuild}) or later. This PC runs build {build}.");
        }

        return build >= WindowsVersion.Windows11Build
            ? new CompatibilityResult(true, true, "Windows 11 is fully supported.")
            : new CompatibilityResult(true, false, "Windows 10 is supported with limitations. Windows 11 is recommended for the best experience.");
    }
}
