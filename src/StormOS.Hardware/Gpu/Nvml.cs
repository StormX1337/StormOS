using System.Runtime.InteropServices;
using System.Text;

namespace StormOS.Hardware.Gpu;

/// <summary>
/// Minimal binding to the NVIDIA Management Library (nvml.dll, installed with the NVIDIA driver).
/// Loaded dynamically; absent on systems without NVIDIA drivers.
/// </summary>
internal sealed unsafe class Nvml : IDisposable
{
    public const int Success = 0;
    public const uint TemperatureGpu = 0;
    public const uint ClockGraphics = 0;
    public const uint ClockMemory = 2;

    private readonly IntPtr _library;
    private readonly delegate* unmanaged<int> _shutdown;
    private readonly delegate* unmanaged<uint*, int> _getCount;
    private readonly delegate* unmanaged<uint, IntPtr*, int> _getHandle;
    private readonly delegate* unmanaged<IntPtr, byte*, uint, int> _getName;
    private readonly delegate* unmanaged<IntPtr, PciInfo*, int> _getPciInfo;
    private readonly delegate* unmanaged<IntPtr, uint, uint*, int> _getTemperature;
    private readonly delegate* unmanaged<IntPtr, uint, uint*, int> _getClock;
    private readonly delegate* unmanaged<IntPtr, uint*, int> _getPower;
    private readonly delegate* unmanaged<IntPtr, uint*, int> _getFan;
    private readonly delegate* unmanaged<IntPtr, UtilizationRates*, int> _getUtilization;
    private readonly delegate* unmanaged<IntPtr, MemoryInfo*, int> _getMemory;

    private Nvml(IntPtr library)
    {
        _library = library;
        _shutdown = (delegate* unmanaged<int>)NativeLibrary.GetExport(library, "nvmlShutdown");
        _getCount = (delegate* unmanaged<uint*, int>)NativeLibrary.GetExport(library, "nvmlDeviceGetCount_v2");
        _getHandle = (delegate* unmanaged<uint, IntPtr*, int>)NativeLibrary.GetExport(library, "nvmlDeviceGetHandleByIndex_v2");
        _getName = (delegate* unmanaged<IntPtr, byte*, uint, int>)NativeLibrary.GetExport(library, "nvmlDeviceGetName");
        _getPciInfo = (delegate* unmanaged<IntPtr, PciInfo*, int>)NativeLibrary.GetExport(library, "nvmlDeviceGetPciInfo_v3");
        _getTemperature = (delegate* unmanaged<IntPtr, uint, uint*, int>)NativeLibrary.GetExport(library, "nvmlDeviceGetTemperature");
        _getClock = (delegate* unmanaged<IntPtr, uint, uint*, int>)NativeLibrary.GetExport(library, "nvmlDeviceGetClockInfo");
        _getPower = (delegate* unmanaged<IntPtr, uint*, int>)NativeLibrary.GetExport(library, "nvmlDeviceGetPowerUsage");
        _getFan = (delegate* unmanaged<IntPtr, uint*, int>)NativeLibrary.GetExport(library, "nvmlDeviceGetFanSpeed");
        _getUtilization = (delegate* unmanaged<IntPtr, UtilizationRates*, int>)NativeLibrary.GetExport(library, "nvmlDeviceGetUtilizationRates");
        _getMemory = (delegate* unmanaged<IntPtr, MemoryInfo*, int>)NativeLibrary.GetExport(library, "nvmlDeviceGetMemoryInfo");
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PciInfo
    {
        public fixed byte BusIdLegacy[16];
        public uint Domain;
        public uint Bus;
        public uint Device;
        public uint PciDeviceId;
        public uint PciSubSystemId;
        public fixed byte BusId[32];
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct UtilizationRates
    {
        public uint Gpu;
        public uint Memory;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MemoryInfo
    {
        public ulong Total;
        public ulong Free;
        public ulong Used;
    }

    /// <summary>Loads and initializes NVML.</summary>
    /// <returns>The library, or <see langword="null"/> when NVML is not installed or fails to initialize.</returns>
    public static Nvml? TryLoad()
    {
        string[] candidates =
        [
            Path.Combine(Environment.SystemDirectory, "nvml.dll"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "NVSMI", "nvml.dll"),
        ];

        foreach (var path in candidates)
        {
            if (!File.Exists(path) || !NativeLibrary.TryLoad(path, out var library))
            {
                continue;
            }

            try
            {
                var init = (delegate* unmanaged<int>)NativeLibrary.GetExport(library, "nvmlInit_v2");
                if (init() == Success)
                {
                    return new Nvml(library);
                }
            }
            catch (EntryPointNotFoundException)
            {
                // Unsupported NVML version.
            }

            NativeLibrary.Free(library);
        }

        return null;
    }

    public IReadOnlyList<(IntPtr Handle, string Name, uint PciDeviceId)> GetDevices()
    {
        uint count;
        if (_getCount(&count) != Success)
        {
            return [];
        }

        var devices = new List<(IntPtr, string, uint)>();
        var nameBuffer = stackalloc byte[96];
        for (uint i = 0; i < count; i++)
        {
            IntPtr handle;
            if (_getHandle(i, &handle) != Success)
            {
                continue;
            }

            new Span<byte>(nameBuffer, 96).Clear();
            var name = _getName(handle, nameBuffer, 96) == Success ? Encoding.ASCII.GetString(nameBuffer, IndexOfZero(nameBuffer, 96)) : "NVIDIA GPU";
            PciInfo pci;
            var pciId = _getPciInfo(handle, &pci) == Success ? pci.PciDeviceId : 0u;
            devices.Add((handle, name, pciId));
        }

        return devices;
    }

    public uint? Temperature(IntPtr device)
    {
        uint value;
        return _getTemperature(device, TemperatureGpu, &value) == Success ? value : null;
    }

    public uint? Clock(IntPtr device, uint clock)
    {
        uint value;
        return _getClock(device, clock, &value) == Success ? value : null;
    }

    public uint? PowerMilliwatts(IntPtr device)
    {
        uint value;
        return _getPower(device, &value) == Success ? value : null;
    }

    public uint? FanPercent(IntPtr device)
    {
        uint value;
        return _getFan(device, &value) == Success ? value : null;
    }

    public UtilizationRates? Utilization(IntPtr device)
    {
        UtilizationRates value;
        return _getUtilization(device, &value) == Success ? value : null;
    }

    public MemoryInfo? Memory(IntPtr device)
    {
        MemoryInfo value;
        return _getMemory(device, &value) == Success ? value : null;
    }

    public void Dispose()
    {
        _ = _shutdown();
        NativeLibrary.Free(_library);
    }

    private static int IndexOfZero(byte* buffer, int length)
    {
        for (var i = 0; i < length; i++)
        {
            if (buffer[i] == 0)
            {
                return i;
            }
        }

        return length;
    }
}
