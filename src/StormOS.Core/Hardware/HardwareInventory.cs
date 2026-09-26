namespace StormOS.Core.Hardware;

/// <summary>A point-in-time description of the machine's hardware and operating system.</summary>
public sealed record HardwareInventory
{
    /// <summary>Gets the time the inventory was collected.</summary>
    public DateTimeOffset CollectedAt { get; init; }

    /// <summary>Gets the operating system description.</summary>
    public OsInfo OperatingSystem { get; init; } = new();

    /// <summary>Gets the processor description.</summary>
    public CpuInfo Cpu { get; init; } = new();

    /// <summary>Gets the graphics adapters.</summary>
    public IReadOnlyList<GpuInfo> Gpus { get; init; } = [];

    /// <summary>Gets the installed memory.</summary>
    public MemoryInfo Memory { get; init; } = new();

    /// <summary>Gets the motherboard description.</summary>
    public MotherboardInfo Motherboard { get; init; } = new();

    /// <summary>Gets the firmware description.</summary>
    public BiosInfo Bios { get; init; } = new();

    /// <summary>Gets the physical storage devices.</summary>
    public IReadOnlyList<StorageDeviceInfo> Storage { get; init; } = [];

    /// <summary>Gets the logical volumes.</summary>
    public IReadOnlyList<VolumeInfo> Volumes { get; init; } = [];

    /// <summary>Gets the network adapters.</summary>
    public IReadOnlyList<NetworkAdapterInfo> NetworkAdapters { get; init; } = [];

    /// <summary>Gets the connected monitors.</summary>
    public IReadOnlyList<MonitorInfo> Monitors { get; init; } = [];

    /// <summary>Gets problems encountered while collecting the inventory (missing data is reported, not invented).</summary>
    public IReadOnlyList<InventoryIssue> Issues { get; init; } = [];
}

/// <summary>Describes a component that could not be fully detected.</summary>
/// <param name="Component">Component name, for example "GPU".</param>
/// <param name="Message">What could not be read and why.</param>
public sealed record InventoryIssue(string Component, string Message);

/// <summary>Operating system information.</summary>
public sealed record OsInfo
{
    /// <summary>Gets the product name, for example "Windows 11 Pro".</summary>
    public string ProductName { get; init; } = string.Empty;

    /// <summary>Gets the marketing version, for example "24H2".</summary>
    public string DisplayVersion { get; init; } = string.Empty;

    /// <summary>Gets the OS build number.</summary>
    public int BuildNumber { get; init; }

    /// <summary>Gets the update build revision.</summary>
    public int Revision { get; init; }

    /// <summary>Gets the OS architecture, for example "x64".</summary>
    public string Architecture { get; init; } = string.Empty;

    /// <summary>Gets the machine host name.</summary>
    public string HostName { get; init; } = string.Empty;

    /// <summary>Gets the last boot time.</summary>
    public DateTimeOffset BootTime { get; init; }

    /// <summary>Gets a value indicating whether this is Windows 11 (build 22000 or later).</summary>
    public bool IsWindows11 => BuildNumber >= 22000;

    /// <summary>Gets the full version string, for example "10.0.26100.2605".</summary>
    public string VersionString => $"10.0.{BuildNumber}.{Revision}";
}

/// <summary>Processor information.</summary>
public sealed record CpuInfo
{
    /// <summary>Gets the processor model name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Gets the manufacturer, for example "AuthenticAMD".</summary>
    public string Manufacturer { get; init; } = string.Empty;

    /// <summary>Gets the number of physical cores.</summary>
    public int Cores { get; init; }

    /// <summary>Gets the number of logical processors.</summary>
    public int LogicalProcessors { get; init; }

    /// <summary>Gets the base clock in MHz, when reported.</summary>
    public int? BaseClockMhz { get; init; }

    /// <summary>Gets the maximum clock in MHz reported by firmware, when reported.</summary>
    public int? MaxClockMhz { get; init; }

    /// <summary>Gets the L2 cache size in KB, when reported.</summary>
    public int? L2CacheKb { get; init; }

    /// <summary>Gets the L3 cache size in KB, when reported.</summary>
    public int? L3CacheKb { get; init; }

    /// <summary>Gets the socket designation.</summary>
    public string? Socket { get; init; }

    /// <summary>Gets the processor architecture.</summary>
    public string? Architecture { get; init; }
}

/// <summary>Known GPU vendors.</summary>
public enum GpuVendor
{
    /// <summary>Vendor could not be determined.</summary>
    Unknown,

    /// <summary>NVIDIA (PCI vendor 0x10DE).</summary>
    Nvidia,

    /// <summary>AMD (PCI vendor 0x1002).</summary>
    Amd,

    /// <summary>Intel (PCI vendor 0x8086).</summary>
    Intel,

    /// <summary>Qualcomm (PCI vendor 0x5143 / 0x4D4F4351).</summary>
    Qualcomm,

    /// <summary>Microsoft software adapters.</summary>
    Microsoft,
}

/// <summary>Graphics adapter information.</summary>
public sealed record GpuInfo
{
    /// <summary>Gets the adapter name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Gets the vendor.</summary>
    public GpuVendor Vendor { get; init; }

    /// <summary>Gets the PCI vendor id.</summary>
    public int VendorId { get; init; }

    /// <summary>Gets the PCI device id.</summary>
    public int DeviceId { get; init; }

    /// <summary>Gets the driver version.</summary>
    public string? DriverVersion { get; init; }

    /// <summary>Gets the driver date.</summary>
    public DateOnly? DriverDate { get; init; }

    /// <summary>Gets the dedicated video memory in bytes.</summary>
    public long DedicatedMemoryBytes { get; init; }

    /// <summary>Gets the shared system memory available to the GPU in bytes.</summary>
    public long SharedMemoryBytes { get; init; }

    /// <summary>Gets the adapter LUID used to correlate performance counters.</summary>
    public long AdapterLuid { get; init; }

    /// <summary>Gets a value indicating whether this is an integrated adapter.</summary>
    public bool IsIntegrated { get; init; }

    /// <summary>Gets a value indicating whether this is a software adapter.</summary>
    public bool IsSoftware { get; init; }
}

/// <summary>Installed memory information.</summary>
public sealed record MemoryInfo
{
    /// <summary>Gets the total physical memory visible to Windows in bytes.</summary>
    public long TotalBytes { get; init; }

    /// <summary>Gets the installed memory modules.</summary>
    public IReadOnlyList<MemoryModuleInfo> Modules { get; init; } = [];
}

/// <summary>A physical memory module.</summary>
public sealed record MemoryModuleInfo
{
    /// <summary>Gets the slot label.</summary>
    public string Slot { get; init; } = string.Empty;

    /// <summary>Gets the module manufacturer.</summary>
    public string? Manufacturer { get; init; }

    /// <summary>Gets the module part number.</summary>
    public string? PartNumber { get; init; }

    /// <summary>Gets the module capacity in bytes.</summary>
    public long CapacityBytes { get; init; }

    /// <summary>Gets the rated speed in MT/s.</summary>
    public int? SpeedMts { get; init; }

    /// <summary>Gets the configured speed in MT/s.</summary>
    public int? ConfiguredSpeedMts { get; init; }

    /// <summary>Gets the memory type (for example DDR5), when reported.</summary>
    public string? MemoryType { get; init; }
}

/// <summary>Motherboard information. Serial numbers are intentionally not collected.</summary>
public sealed record MotherboardInfo
{
    /// <summary>Gets the manufacturer.</summary>
    public string Manufacturer { get; init; } = string.Empty;

    /// <summary>Gets the product name.</summary>
    public string Product { get; init; } = string.Empty;

    /// <summary>Gets the board revision.</summary>
    public string? Version { get; init; }
}

/// <summary>Firmware information.</summary>
public sealed record BiosInfo
{
    /// <summary>Gets the firmware vendor.</summary>
    public string Vendor { get; init; } = string.Empty;

    /// <summary>Gets the firmware version.</summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>Gets the firmware release date.</summary>
    public DateOnly? ReleaseDate { get; init; }

    /// <summary>Gets the SMBIOS version.</summary>
    public string? SmbiosVersion { get; init; }
}

/// <summary>Storage media type.</summary>
public enum StorageMediaType
{
    /// <summary>Unknown media.</summary>
    Unknown,

    /// <summary>Rotational hard disk.</summary>
    Hdd,

    /// <summary>Solid state drive.</summary>
    Ssd,

    /// <summary>Storage class memory.</summary>
    Scm,
}

/// <summary>A physical storage device.</summary>
public sealed record StorageDeviceInfo
{
    /// <summary>Gets the device index (PhysicalDriveN).</summary>
    public int Index { get; init; }

    /// <summary>Gets the model name.</summary>
    public string Model { get; init; } = string.Empty;

    /// <summary>Gets the bus type, for example "NVMe" or "SATA".</summary>
    public string BusType { get; init; } = string.Empty;

    /// <summary>Gets the media type.</summary>
    public StorageMediaType MediaType { get; init; }

    /// <summary>Gets the size in bytes.</summary>
    public long SizeBytes { get; init; }

    /// <summary>Gets the firmware revision.</summary>
    public string? FirmwareVersion { get; init; }

    /// <summary>Gets the health status reported by Windows Storage Management.</summary>
    public string? HealthStatus { get; init; }
}

/// <summary>A logical volume.</summary>
public sealed record VolumeInfo
{
    /// <summary>Gets the root path, for example "C:\".</summary>
    public string RootPath { get; init; } = string.Empty;

    /// <summary>Gets the volume label.</summary>
    public string? Label { get; init; }

    /// <summary>Gets the file system.</summary>
    public string? FileSystem { get; init; }

    /// <summary>Gets the total size in bytes.</summary>
    public long TotalBytes { get; init; }

    /// <summary>Gets the free space in bytes.</summary>
    public long FreeBytes { get; init; }

    /// <summary>Gets the used percentage.</summary>
    public double UsedPercent => TotalBytes > 0 ? 100.0 * (TotalBytes - FreeBytes) / TotalBytes : 0;
}

/// <summary>A network adapter.</summary>
public sealed record NetworkAdapterInfo
{
    /// <summary>Gets the adapter id (interface GUID).</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Gets the friendly connection name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Gets the adapter description (hardware model).</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Gets the interface type, for example "Ethernet" or "Wireless80211".</summary>
    public string InterfaceType { get; init; } = string.Empty;

    /// <summary>Gets the link speed in bits per second.</summary>
    public long SpeedBitsPerSecond { get; init; }

    /// <summary>Gets a value indicating whether the adapter is operational.</summary>
    public bool IsUp { get; init; }

    /// <summary>Gets the IPv4 addresses.</summary>
    public IReadOnlyList<string> IPv4Addresses { get; init; } = [];

    /// <summary>Gets the IPv6 addresses.</summary>
    public IReadOnlyList<string> IPv6Addresses { get; init; } = [];

    /// <summary>Gets the default gateways.</summary>
    public IReadOnlyList<string> Gateways { get; init; } = [];

    /// <summary>Gets the configured DNS servers.</summary>
    public IReadOnlyList<string> DnsServers { get; init; } = [];

    /// <summary>Gets a value indicating whether DHCP is enabled for IPv4.</summary>
    public bool DhcpEnabled { get; init; }
}

/// <summary>A connected display.</summary>
public sealed record MonitorInfo
{
    /// <summary>Gets the device name, for example "\\.\DISPLAY1".</summary>
    public string DeviceName { get; init; } = string.Empty;

    /// <summary>Gets the friendly monitor name from EDID when available.</summary>
    public string? FriendlyName { get; init; }

    /// <summary>Gets the manufacturer code from EDID when available.</summary>
    public string? Manufacturer { get; init; }

    /// <summary>Gets the horizontal resolution in pixels.</summary>
    public int Width { get; init; }

    /// <summary>Gets the vertical resolution in pixels.</summary>
    public int Height { get; init; }

    /// <summary>Gets the refresh rate in Hz.</summary>
    public double RefreshRateHz { get; init; }

    /// <summary>Gets the bits per pixel.</summary>
    public int BitsPerPixel { get; init; }

    /// <summary>Gets a value indicating whether this is the primary display.</summary>
    public bool IsPrimary { get; init; }
}
