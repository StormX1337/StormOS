using StormOS.Core.Hardware;
using Vortice.DXGI;

namespace StormOS.Hardware.Gpu;

/// <summary>Enumerates graphics adapters through DXGI (names, vendor, VRAM and LUIDs).</summary>
public static class DxgiAdapterEnumerator
{
    /// <summary>Lists hardware and software adapters.</summary>
    /// <returns>Adapters in DXGI order.</returns>
    public static IReadOnlyList<GpuInfo> GetAdapters()
    {
        var list = new List<GpuInfo>();
        if (DXGI.CreateDXGIFactory1(out IDXGIFactory1? factory).Failure || factory is null)
        {
            return list;
        }

        using (factory)
        {
            for (uint index = 0; factory.EnumAdapters1(index, out var adapter).Success; index++)
            {
                using (adapter)
                {
                    var description = adapter.Description1;
                    var software = (description.Flags & AdapterFlags.Software) != 0 || description.VendorId == 0x1414;
                    list.Add(new GpuInfo
                    {
                        Name = description.Description.Trim(),
                        VendorId = (int)description.VendorId,
                        DeviceId = (int)description.DeviceId,
                        Vendor = VendorFromId(description.VendorId),
                        DedicatedMemoryBytes = (long)description.DedicatedVideoMemory,
                        SharedMemoryBytes = (long)description.SharedSystemMemory,
                        AdapterLuid = ((long)description.Luid.HighPart << 32) | description.Luid.LowPart,
                        IsSoftware = software,
                        IsIntegrated = !software && (long)description.DedicatedVideoMemory < 512L * 1024 * 1024,
                    });
                }
            }
        }

        return list;
    }

    /// <summary>Maps a PCI vendor id to a vendor.</summary>
    /// <param name="vendorId">PCI vendor id.</param>
    /// <returns>The vendor.</returns>
    public static GpuVendor VendorFromId(uint vendorId) => vendorId switch
    {
        0x10DE => GpuVendor.Nvidia,
        0x1002 or 0x1022 => GpuVendor.Amd,
        0x8086 => GpuVendor.Intel,
        0x5143 or 0x4D4F4351 => GpuVendor.Qualcomm,
        0x1414 => GpuVendor.Microsoft,
        _ => GpuVendor.Unknown,
    };
}
