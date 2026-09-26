using System.Runtime.InteropServices;

namespace StormOS.Hardware.Gpu;

/// <summary>
/// D3DKMT thunks exported by gdi32.dll (documented in d3dkmthk.h). They expose the same vendor neutral adapter
/// telemetry Task Manager uses: temperature, fan speed, memory and engine clocks (WDDM 2.4+ drivers).
/// </summary>
internal static partial class D3dkmt
{
    public const int QueryNodePerfData = 61;
    public const int QueryAdapterPerfData = 62;
    public const int QueryAdapterPerfDataCaps = 63;

    [StructLayout(LayoutKind.Sequential)]
    public struct Luid
    {
        public uint LowPart;
        public int HighPart;

        public readonly long Value => ((long)HighPart << 32) | LowPart;

        public static Luid FromValue(long value) => new() { LowPart = (uint)(value & 0xFFFFFFFF), HighPart = (int)(value >> 32) };
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct OpenAdapterFromLuid
    {
        public Luid AdapterLuid;
        public uint Adapter;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CloseAdapter
    {
        public uint Adapter;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct QueryAdapterInfo
    {
        public uint Adapter;
        public int Type;
        public IntPtr PrivateDriverData;
        public uint PrivateDriverDataSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct AdapterPerfData
    {
        public uint PhysicalAdapterIndex;
        public ulong MemoryFrequency;
        public ulong MaxMemoryFrequency;
        public ulong MaxMemoryFrequencyOC;
        public ulong MemoryBandwidth;
        public ulong PcieBandwidth;
        public uint FanRpm;
        public uint Power;
        public uint Temperature;
        public byte PowerStateOverride;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct AdapterPerfDataCaps
    {
        public uint PhysicalAdapterIndex;
        public ulong MaxMemoryBandwidth;
        public ulong MaxPcieBandwidth;
        public uint MaxFanRpm;
        public uint TemperatureMax;
        public uint TemperatureWarning;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NodePerfData
    {
        public uint NodeOrdinal;
        public uint PhysicalAdapterIndex;
        public ulong Frequency;
        public ulong MaxFrequency;
        public ulong MaxFrequencyOC;
        public uint Voltage;
        public uint VoltageMax;
        public uint VoltageMaxOC;
        public ulong MaxTransitionLatency;
    }

    [LibraryImport("gdi32.dll", EntryPoint = "D3DKMTOpenAdapterFromLuid")]
    public static partial int OpenAdapter(ref OpenAdapterFromLuid data);

    [LibraryImport("gdi32.dll", EntryPoint = "D3DKMTCloseAdapter")]
    public static partial int Close(ref CloseAdapter data);

    [LibraryImport("gdi32.dll", EntryPoint = "D3DKMTQueryAdapterInfo")]
    public static partial int Query(ref QueryAdapterInfo data);

    public static bool TryQuery<T>(uint adapter, int type, ref T value)
        where T : unmanaged
    {
        var size = Marshal.SizeOf<T>();
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(value, buffer, false);
            var info = new QueryAdapterInfo { Adapter = adapter, Type = type, PrivateDriverData = buffer, PrivateDriverDataSize = (uint)size };
            if (Query(ref info) != 0)
            {
                return false;
            }

            value = Marshal.PtrToStructure<T>(buffer);
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
