using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StormOS.App.Services;
using StormOS.Core.Common;
using StormOS.Core.Hardware;
using StormOS.Core.Rgb;

namespace StormOS.App.ViewModels;

/// <summary>A label/value pair.</summary>
public sealed record DeviceField(string Label, string Value);

/// <summary>A titled group of fields.</summary>
public sealed record DeviceSection(string Title, IReadOnlyList<DeviceField> Fields);

/// <summary>An RGB provider row.</summary>
public sealed record RgbProviderRow(string Name, string Status, string Devices);

/// <summary>Hardware inventory and plugin-based RGB providers.</summary>
public sealed partial class DevicesViewModel(
    IHardwareInventoryProvider inventory,
    IEnumerable<IRgbProvider> rgbProviders,
    NotificationService notifications) : PageViewModel
{
    private HardwareInventory? _inventory;

    public ObservableCollection<DeviceSection> Sections { get; } = [];

    public ObservableCollection<RgbProviderRow> RgbProviders { get; } = [];

    public ObservableCollection<string> Issues { get; } = [];

    [ObservableProperty]
    public partial bool HasRgbProviders { get; set; }

    public override void Activate(object? parameter) => _ = LoadAsync();

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsBusy = true;
        StatusMessage = "Reading hardware inventory…";
        try
        {
            _inventory = await Task.Run(() => inventory.GetInventoryAsync());
            Build(_inventory);
            await LoadRgbAsync();
        }
        finally
        {
            IsBusy = false;
            StatusMessage = null;
        }
    }

    [RelayCommand]
    private void CopyReport()
    {
        if (_inventory is null)
        {
            return;
        }

        var package = new global::Windows.ApplicationModel.DataTransfer.DataPackage();
        package.SetText(JsonSerializer.Serialize(_inventory, StormJson.Indented));
        global::Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
        notifications.Success("The hardware report was copied to the clipboard. It contains no serial numbers or account data.");
    }

    private static string Or(string? value) => string.IsNullOrWhiteSpace(value) ? "Unavailable" : value;

    private static string Mhz(int? value) => value is { } v ? v.ToString(CultureInfo.CurrentCulture) + " MHz" : "Unavailable";

    private void Build(HardwareInventory inv)
    {
        Sections.Clear();
        var os = inv.OperatingSystem;
        Sections.Add(new DeviceSection("OPERATING SYSTEM", [
            new("Edition", Or(os.ProductName)),
            new("Version", $"{Or(os.DisplayVersion)} (build {os.VersionString})"),
            new("Architecture", Or(os.Architecture)),
            new("Computer name", Or(os.HostName)),
            new("Last boot", os.BootTime == default ? "Unavailable" : os.BootTime.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)),
        ]));

        var cpu = inv.Cpu;
        Sections.Add(new DeviceSection("PROCESSOR", [
            new("Model", Or(cpu.Name)),
            new("Cores / threads", $"{cpu.Cores} / {cpu.LogicalProcessors}"),
            new("Base / max clock", $"{Mhz(cpu.BaseClockMhz)} / {Mhz(cpu.MaxClockMhz)}"),
            new("L2 / L3 cache", $"{(cpu.L2CacheKb is { } l2 ? Units.FormatBytes(l2 * 1024L) : "—")} / {(cpu.L3CacheKb is { } l3 ? Units.FormatBytes(l3 * 1024L) : "—")}"),
            new("Socket", Or(cpu.Socket)),
        ]));

        foreach (var gpu in inv.Gpus.Where(g => !g.IsSoftware))
        {
            Sections.Add(new DeviceSection(gpu.IsIntegrated ? "GRAPHICS (INTEGRATED)" : "GRAPHICS", [
                new("Model", Or(gpu.Name)),
                new("Vendor", gpu.Vendor.ToString()),
                new("Dedicated memory", gpu.DedicatedMemoryBytes > 0 ? Units.FormatBytes(gpu.DedicatedMemoryBytes) : "None"),
                new("Shared memory", Units.FormatBytes(gpu.SharedMemoryBytes)),
                new("Driver", $"{Or(gpu.DriverVersion)}{(gpu.DriverDate is { } date ? " (" + date.ToString("d", CultureInfo.CurrentCulture) + ")" : string.Empty)}"),
            ]));
        }

        var memory = new List<DeviceField> { new("Installed", Units.FormatBytes(inv.Memory.TotalBytes)), new("Modules", inv.Memory.Modules.Count.ToString(CultureInfo.CurrentCulture)) };
        memory.AddRange(inv.Memory.Modules.Select(m => new DeviceField(
            Or(m.Slot),
            $"{Units.FormatBytes(m.CapacityBytes)} {m.MemoryType} · {(m.ConfiguredSpeedMts is { } c ? c.ToString(CultureInfo.CurrentCulture) : "?")} MT/s configured / {(m.SpeedMts is { } s ? s.ToString(CultureInfo.CurrentCulture) : "?")} MT/s rated · {m.Manufacturer} {m.PartNumber}".Trim())));
        Sections.Add(new DeviceSection("MEMORY", memory));

        Sections.Add(new DeviceSection("MOTHERBOARD & BIOS", [
            new("Board", $"{Or(inv.Motherboard.Manufacturer)} {inv.Motherboard.Product}".Trim()),
            new("Revision", Or(inv.Motherboard.Version)),
            new("BIOS", $"{Or(inv.Bios.Vendor)} {inv.Bios.Version}".Trim()),
            new("BIOS date", inv.Bios.ReleaseDate is { } bios ? bios.ToString("d", CultureInfo.CurrentCulture) : "Unavailable"),
        ]));

        Sections.Add(new DeviceSection("STORAGE", inv.Storage.Select(d => new DeviceField(
            Or(d.Model),
            $"{Units.FormatBytes(d.SizeBytes)} · {d.MediaType} · {d.BusType} · health {Or(d.HealthStatus)} · firmware {Or(d.FirmwareVersion)}")).Concat(
            inv.Volumes.Select(v => new DeviceField(
                $"{v.RootPath} {v.Label}".Trim(),
                $"{Units.FormatBytes(v.FreeBytes)} free of {Units.FormatBytes(v.TotalBytes)} ({v.UsedPercent:0} % used) · {v.FileSystem}"))).ToList()));

        Sections.Add(new DeviceSection("DISPLAYS", inv.Monitors.Select(m => new DeviceField(
            Or(m.FriendlyName ?? m.DeviceName) + (m.IsPrimary ? " (primary)" : string.Empty),
            string.Create(CultureInfo.CurrentCulture, $"{m.Width} × {m.Height} @ {m.RefreshRateHz:0.##} Hz · {m.BitsPerPixel} bit"))).ToList()));

        Sections.Add(new DeviceSection("NETWORK ADAPTERS", inv.NetworkAdapters.Select(n => new DeviceField(
            Or(n.Name),
            $"{n.Description} · {n.InterfaceType} · {(n.IsUp ? "connected" : "disconnected")}{(n.SpeedBitsPerSecond > 0 ? " · " + Units.FormatBitRate(n.SpeedBitsPerSecond) : string.Empty)}")).ToList()));

        Issues.Clear();
        foreach (var issue in inv.Issues)
        {
            Issues.Add($"{issue.Component}: {issue.Message}");
        }
    }

    private async Task LoadRgbAsync()
    {
        RgbProviders.Clear();
        foreach (var provider in rgbProviders)
        {
            var unavailable = provider.CheckAvailability();
            var devices = unavailable is null ? await provider.GetDevicesAsync() : [];
            RgbProviders.Add(new RgbProviderRow(provider.Name, unavailable ?? "Available", devices.Count == 0 ? "No devices" : string.Join(", ", devices.Select(d => d.Name))));
        }

        HasRgbProviders = RgbProviders.Count > 0;
    }
}
