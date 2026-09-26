using System.Management;
using System.Net.NetworkInformation;
using Microsoft.Extensions.Logging;
using StormOS.Core.Hardware;
using StormOS.Hardware.Gpu;
using StormOS.Network;
using StormOS.Windows.Display;
using StormOS.Windows.Wmi;

namespace StormOS.Hardware.Inventory;

/// <summary>
/// Builds the hardware inventory from WMI/CIM, DXGI, the storage management provider and .NET networking.
/// Every component is read independently; failures become <see cref="InventoryIssue"/> entries instead of errors.
/// </summary>
public sealed class WindowsHardwareInventoryProvider : IHardwareInventoryProvider
{
    private const string Cimv2 = @"root\cimv2";
    private readonly IOperatingSystemInfoProvider _os;
    private readonly ILogger<WindowsHardwareInventoryProvider> _logger;
    private readonly TimeProvider _time;

    /// <summary>Initializes a new instance of the <see cref="WindowsHardwareInventoryProvider"/> class.</summary>
    /// <param name="os">Operating system information provider.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="timeProvider">Time source.</param>
    public WindowsHardwareInventoryProvider(IOperatingSystemInfoProvider os, ILogger<WindowsHardwareInventoryProvider> logger, TimeProvider? timeProvider = null)
    {
        _os = os;
        _logger = logger;
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public Task<HardwareInventory> GetInventoryAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => Collect(cancellationToken), cancellationToken);

    private HardwareInventory Collect(CancellationToken cancellationToken)
    {
        var issues = new List<InventoryIssue>();
        var inventory = new HardwareInventory
        {
            CollectedAt = _time.GetUtcNow(),
            OperatingSystem = Try("Operating system", issues, _os.GetOsInfo) ?? new OsInfo(),
            Cpu = Try("CPU", issues, ReadCpu) ?? new CpuInfo(),
            Gpus = Try("GPU", issues, ReadGpus) ?? [],
            Memory = Try("Memory", issues, ReadMemory) ?? new MemoryInfo(),
            Motherboard = Try("Motherboard", issues, ReadBoard) ?? new MotherboardInfo(),
            Bios = Try("BIOS", issues, ReadBios) ?? new BiosInfo(),
            Storage = Try("Storage", issues, ReadStorage) ?? [],
            Volumes = Try("Volumes", issues, ReadVolumes) ?? [],
            NetworkAdapters = Try("Network", issues, ReadNetwork) ?? [],
            Monitors = Try("Monitors", issues, DisplayEnumerator.GetMonitors) ?? [],
        };
        cancellationToken.ThrowIfCancellationRequested();
        return inventory with { Issues = issues };
    }

    private T? Try<T>(string component, List<InventoryIssue> issues, Func<T> read)
        where T : class
    {
        try
        {
            return read();
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or InvalidOperationException or IOException or NetworkInformationException or TimeoutException or SharpGen.Runtime.SharpGenException)
        {
            _logger.LogWarning(ex, "Could not read {Component} information", component);
            issues.Add(new InventoryIssue(component, $"Storm OS could not read {component.ToLowerInvariant()} information."));
            return null;
        }
    }

    private static CpuInfo ReadCpu()
    {
        var rows = WmiQuery.Query(Cimv2, "SELECT Name, Manufacturer, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed, L2CacheSize, L3CacheSize, SocketDesignation, Architecture FROM Win32_Processor");
        if (rows.Count == 0)
        {
            return new CpuInfo { Name = "Unknown processor", LogicalProcessors = Environment.ProcessorCount };
        }

        var first = rows[0];
        return new CpuInfo
        {
            Name = InventoryParsers.CleanName(first.Str("Name")),
            Manufacturer = first.Str("Manufacturer") ?? string.Empty,
            Cores = (int)rows.Sum(r => r.Int64Value("NumberOfCores") ?? 0),
            LogicalProcessors = (int)rows.Sum(r => r.Int64Value("NumberOfLogicalProcessors") ?? 0),
            BaseClockMhz = (int?)first.Int64Value("MaxClockSpeed"),
            L2CacheKb = (int?)first.Int64Value("L2CacheSize"),
            L3CacheKb = (int?)first.Int64Value("L3CacheSize"),
            Socket = first.Str("SocketDesignation"),
            Architecture = InventoryParsers.Architecture(first.Int64Value("Architecture")),
        };
    }

    private static List<GpuInfo> ReadGpus()
    {
        var adapters = DxgiAdapterEnumerator.GetAdapters().Where(a => !a.IsSoftware).ToList();
        var drivers = WmiQuery.Query(Cimv2, "SELECT Name, DriverVersion, DriverDate, PNPDeviceID FROM Win32_VideoController");
        return adapters.Select(adapter =>
        {
            var driver = drivers.FirstOrDefault(d => string.Equals(d.Str("Name"), adapter.Name, StringComparison.OrdinalIgnoreCase))
                ?? drivers.FirstOrDefault(d => (d.Str("PNPDeviceID") ?? string.Empty).Contains($"DEV_{adapter.DeviceId:X4}", StringComparison.OrdinalIgnoreCase));
            return adapter with
            {
                DriverVersion = driver?.Str("DriverVersion"),
                DriverDate = WmiQuery.CimDate(driver?.Str("DriverDate")),
            };
        }).ToList();
    }

    private static MemoryInfo ReadMemory()
    {
        var total = WmiQuery.Query(Cimv2, "SELECT TotalPhysicalMemory FROM Win32_ComputerSystem").FirstRow()?.Int64Value("TotalPhysicalMemory") ?? 0;
        var modules = WmiQuery.Query(Cimv2, "SELECT DeviceLocator, Manufacturer, PartNumber, Capacity, Speed, ConfiguredClockSpeed, SMBIOSMemoryType FROM Win32_PhysicalMemory")
            .Select(row => new MemoryModuleInfo
            {
                Slot = row.Str("DeviceLocator") ?? string.Empty,
                Manufacturer = InventoryParsers.CleanName(row.Str("Manufacturer")) is { Length: > 0 } m ? m : null,
                PartNumber = row.Str("PartNumber"),
                CapacityBytes = row.Int64Value("Capacity") ?? 0,
                SpeedMts = (int?)row.Int64Value("Speed"),
                ConfiguredSpeedMts = (int?)row.Int64Value("ConfiguredClockSpeed"),
                MemoryType = InventoryParsers.MemoryType(row.Int64Value("SMBIOSMemoryType")),
            }).ToList();
        return new MemoryInfo { TotalBytes = total, Modules = modules };
    }

    private static MotherboardInfo ReadBoard()
    {
        var row = WmiQuery.Query(Cimv2, "SELECT Manufacturer, Product, Version FROM Win32_BaseBoard").FirstRow();
        return new MotherboardInfo
        {
            Manufacturer = InventoryParsers.CleanName(row?.Str("Manufacturer")),
            Product = InventoryParsers.CleanName(row?.Str("Product")),
            Version = InventoryParsers.CleanName(row?.Str("Version")) is { Length: > 0 } v ? v : null,
        };
    }

    private static BiosInfo ReadBios()
    {
        var row = WmiQuery.Query(Cimv2, "SELECT Manufacturer, SMBIOSBIOSVersion, ReleaseDate, SMBIOSMajorVersion, SMBIOSMinorVersion FROM Win32_BIOS").FirstRow();
        return new BiosInfo
        {
            Vendor = row?.Str("Manufacturer") ?? string.Empty,
            Version = row?.Str("SMBIOSBIOSVersion") ?? string.Empty,
            ReleaseDate = WmiQuery.CimDate(row?.Str("ReleaseDate")),
            SmbiosVersion = row?.Int64Value("SMBIOSMajorVersion") is { } major ? $"{major}.{row.Int64Value("SMBIOSMinorVersion") ?? 0}" : null,
        };
    }

    private static List<StorageDeviceInfo> ReadStorage()
    {
        try
        {
            return WmiQuery.Query(@"root\Microsoft\Windows\Storage", "SELECT DeviceId, FriendlyName, MediaType, BusType, Size, FirmwareVersion, HealthStatus FROM MSFT_PhysicalDisk")
                .Select(row => new StorageDeviceInfo
                {
                    Index = int.TryParse(row.Str("DeviceId"), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var id) ? id : -1,
                    Model = InventoryParsers.CleanName(row.Str("FriendlyName")),
                    MediaType = InventoryParsers.MediaType(row.Int64Value("MediaType")),
                    BusType = InventoryParsers.BusType(row.Int64Value("BusType")),
                    SizeBytes = row.Int64Value("Size") ?? 0,
                    FirmwareVersion = row.Str("FirmwareVersion"),
                    HealthStatus = InventoryParsers.Health(row.Int64Value("HealthStatus")),
                })
                .OrderBy(d => d.Index)
                .ToList();
        }
        catch (ManagementException)
        {
            // Older or trimmed systems without the Storage Management provider: fall back to Win32_DiskDrive.
            return WmiQuery.Query(Cimv2, "SELECT Index, Model, InterfaceType, Size, FirmwareRevision FROM Win32_DiskDrive")
                .Select(row => new StorageDeviceInfo
                {
                    Index = (int)(row.Int64Value("Index") ?? -1),
                    Model = InventoryParsers.CleanName(row.Str("Model")),
                    BusType = row.Str("InterfaceType") ?? "Unknown",
                    SizeBytes = row.Int64Value("Size") ?? 0,
                    FirmwareVersion = row.Str("FirmwareRevision"),
                })
                .ToList();
        }
    }

    private static List<VolumeInfo> ReadVolumes() =>
        DriveInfo.GetDrives()
            .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
            .Select(d => new VolumeInfo
            {
                RootPath = d.RootDirectory.FullName,
                Label = string.IsNullOrWhiteSpace(d.VolumeLabel) ? null : d.VolumeLabel,
                FileSystem = d.DriveFormat,
                TotalBytes = d.TotalSize,
                FreeBytes = d.AvailableFreeSpace,
            })
            .ToList();

    private static List<NetworkAdapterInfo> ReadNetwork() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
            .Select(NetworkAdapterMapper.Map)
            .OrderByDescending(n => n.IsUp)
            .ThenBy(n => n.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
}
