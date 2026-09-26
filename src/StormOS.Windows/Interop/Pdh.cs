using System.Runtime.InteropServices;

namespace StormOS.Windows.Interop;

/// <summary>pdh.dll declarations.</summary>
internal static partial class PdhNative
{
    public const uint ErrorSuccess = 0;
    public const uint PdhMoreData = 0x800007D2;
    public const uint PdhCstatusValidData = 0;
    public const uint PdhCstatusNewData = 1;
    public const uint PdhFmtDouble = 0x00000200;
    public const uint PdhFmtNoCap100 = 0x00008000;

    [StructLayout(LayoutKind.Explicit)]
    public struct FmtCounterValue
    {
        [FieldOffset(0)]
        public uint CStatus;

        [FieldOffset(8)]
        public double DoubleValue;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FmtCounterValueItem
    {
        public IntPtr Name;
        public FmtCounterValue Value;
    }

    [LibraryImport("pdh.dll", EntryPoint = "PdhOpenQueryW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint PdhOpenQuery(string? dataSource, IntPtr userData, out IntPtr query);

    [LibraryImport("pdh.dll", EntryPoint = "PdhAddEnglishCounterW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint PdhAddEnglishCounter(IntPtr query, string counterPath, IntPtr userData, out IntPtr counter);

    [LibraryImport("pdh.dll")]
    public static partial uint PdhCollectQueryData(IntPtr query);

    [LibraryImport("pdh.dll")]
    public static partial uint PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type, out FmtCounterValue value);

    [LibraryImport("pdh.dll", EntryPoint = "PdhGetFormattedCounterArrayW")]
    public static partial uint PdhGetFormattedCounterArray(IntPtr counter, uint format, ref uint bufferSize, out uint itemCount, IntPtr itemBuffer);

    [LibraryImport("pdh.dll")]
    public static partial uint PdhCloseQuery(IntPtr query);
}
