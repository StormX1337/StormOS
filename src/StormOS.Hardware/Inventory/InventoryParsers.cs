using StormOS.Core.Hardware;

namespace StormOS.Hardware.Inventory;

/// <summary>Pure parsing helpers for WMI/CIM hardware values (unit tested).</summary>
public static class InventoryParsers
{
    /// <summary>Maps MSFT_PhysicalDisk.MediaType.</summary>
    /// <param name="mediaType">CIM media type.</param>
    /// <returns>The media type.</returns>
    public static StorageMediaType MediaType(long? mediaType) => mediaType switch
    {
        3 => StorageMediaType.Hdd,
        4 => StorageMediaType.Ssd,
        5 => StorageMediaType.Scm,
        _ => StorageMediaType.Unknown,
    };

    /// <summary>Maps MSFT_PhysicalDisk.BusType.</summary>
    /// <param name="busType">CIM bus type.</param>
    /// <returns>A readable bus name.</returns>
    public static string BusType(long? busType) => busType switch
    {
        1 => "SCSI",
        2 => "ATAPI",
        3 => "ATA",
        4 => "IEEE 1394",
        5 => "SSA",
        6 => "Fibre Channel",
        7 => "USB",
        8 => "RAID",
        9 => "iSCSI",
        10 => "SAS",
        11 => "SATA",
        12 => "SD",
        13 => "MMC",
        15 => "File-backed virtual",
        16 => "Storage Spaces",
        17 => "NVMe",
        18 => "SCM",
        19 => "UFS",
        _ => "Unknown",
    };

    /// <summary>Maps MSFT_PhysicalDisk.HealthStatus.</summary>
    /// <param name="health">CIM health status.</param>
    /// <returns>A readable status.</returns>
    public static string? Health(long? health) => health switch
    {
        0 => "Healthy",
        1 => "Warning",
        2 => "Unhealthy",
        _ => null,
    };

    /// <summary>Maps Win32_PhysicalMemory.SMBIOSMemoryType.</summary>
    /// <param name="smbiosType">SMBIOS memory type.</param>
    /// <returns>A readable type or <see langword="null"/>.</returns>
    public static string? MemoryType(long? smbiosType) => smbiosType switch
    {
        20 => "DDR",
        21 => "DDR2",
        24 => "DDR3",
        26 => "DDR4",
        27 => "LPDDR",
        28 => "LPDDR2",
        29 => "LPDDR3",
        30 => "LPDDR4",
        34 => "DDR5",
        35 => "LPDDR5",
        _ => null,
    };

    /// <summary>Maps Win32_Processor.Architecture.</summary>
    /// <param name="architecture">WMI architecture code.</param>
    /// <returns>A readable architecture.</returns>
    public static string Architecture(long? architecture) => architecture switch
    {
        0 => "x86",
        5 => "ARM",
        9 => "x64",
        12 => "ARM64",
        _ => "Unknown",
    };

    /// <summary>Cleans up marketing noise and whitespace in WMI names.</summary>
    /// <param name="name">Raw name.</param>
    /// <returns>Normalized name.</returns>
    public static string CleanName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var cleaned = string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return cleaned is "To Be Filled By O.E.M." or "Default string" or "System Product Name" ? string.Empty : cleaned;
    }
}
