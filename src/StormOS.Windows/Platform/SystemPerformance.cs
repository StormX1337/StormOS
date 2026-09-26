using System.Runtime.InteropServices;
using StormOS.Windows.Interop;

namespace StormOS.Windows.Platform;

/// <summary>System-wide memory and object counts.</summary>
/// <param name="TotalPhysical">Total physical memory in bytes.</param>
/// <param name="AvailablePhysical">Available physical memory in bytes.</param>
/// <param name="CommitTotal">Committed memory in bytes.</param>
/// <param name="CommitLimit">Commit limit in bytes.</param>
/// <param name="SystemCache">System cache in bytes.</param>
/// <param name="ProcessCount">Process count.</param>
/// <param name="ThreadCount">Thread count.</param>
/// <param name="HandleCount">Handle count.</param>
public readonly record struct SystemPerformanceInfo(long TotalPhysical, long AvailablePhysical, long CommitTotal, long CommitLimit, long SystemCache, int ProcessCount, int ThreadCount, int HandleCount);

/// <summary>Reads system performance information (GlobalMemoryStatusEx, GetPerformanceInfo, GetTickCount64).</summary>
public static class SystemPerformance
{
    /// <summary>Gets the system uptime.</summary>
    public static TimeSpan Uptime => TimeSpan.FromMilliseconds(Kernel32.GetTickCount64());

    /// <summary>Reads the current values.</summary>
    /// <returns>The values, or <see langword="null"/> when the APIs fail.</returns>
    public static SystemPerformanceInfo? Read()
    {
        var status = new Kernel32.MemoryStatusEx { Length = (uint)Marshal.SizeOf<Kernel32.MemoryStatusEx>() };
        if (!Kernel32.GlobalMemoryStatusEx(ref status))
        {
            return null;
        }

        if (!Kernel32.GetPerformanceInfo(out var perf, (uint)Marshal.SizeOf<Kernel32.PerformanceInformation>()))
        {
            return new SystemPerformanceInfo((long)status.TotalPhys, (long)status.AvailPhys, 0, 0, 0, 0, 0, 0);
        }

        var page = (long)perf.PageSize;
        return new SystemPerformanceInfo(
            (long)status.TotalPhys,
            (long)status.AvailPhys,
            (long)perf.CommitTotal * page,
            (long)perf.CommitLimit * page,
            (long)perf.SystemCache * page,
            (int)perf.ProcessCount,
            (int)perf.ThreadCount,
            (int)perf.HandleCount);
    }
}
