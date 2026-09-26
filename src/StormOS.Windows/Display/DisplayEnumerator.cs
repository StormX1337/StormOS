using System.Runtime.InteropServices;
using StormOS.Core.Hardware;
using StormOS.Windows.Interop;
using StormOS.Windows.Wmi;

namespace StormOS.Windows.Display;

/// <summary>Enumerates active displays with resolution, refresh rate and EDID names.</summary>
public static class DisplayEnumerator
{
    /// <summary>Lists displays attached to the desktop.</summary>
    /// <returns>The displays.</returns>
    public static IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var names = ReadEdidNames();
        var monitors = new List<MonitorInfo>();
        for (uint index = 0; ; index++)
        {
            var adapter = new User32.DisplayDevice { Cb = Marshal.SizeOf<User32.DisplayDevice>() };
            if (!User32.EnumDisplayDevices(null, index, ref adapter, 0))
            {
                break;
            }

            if ((adapter.StateFlags & User32.DisplayDeviceAttachedToDesktop) == 0)
            {
                continue;
            }

            var mode = new User32.DevMode { Size = (ushort)Marshal.SizeOf<User32.DevMode>() };
            if (!User32.EnumDisplaySettings(adapter.DeviceName, User32.EnumCurrentSettings, ref mode))
            {
                continue;
            }

            var monitor = new User32.DisplayDevice { Cb = Marshal.SizeOf<User32.DisplayDevice>() };
            string? friendly = null;
            string? manufacturer = null;
            if (User32.EnumDisplayDevices(adapter.DeviceName, 0, ref monitor, 0))
            {
                var code = HardwareCode(monitor.DeviceId);
                if (code is not null && names.TryGetValue(code, out var edid))
                {
                    friendly = edid.Name;
                    manufacturer = edid.Manufacturer;
                }

                friendly ??= string.IsNullOrWhiteSpace(monitor.DeviceString) ? null : monitor.DeviceString;
            }

            monitors.Add(new MonitorInfo
            {
                DeviceName = adapter.DeviceName,
                FriendlyName = friendly,
                Manufacturer = manufacturer,
                Width = (int)mode.PelsWidth,
                Height = (int)mode.PelsHeight,
                RefreshRateHz = mode.DisplayFrequency,
                BitsPerPixel = (int)mode.BitsPerPel,
                IsPrimary = (adapter.StateFlags & User32.DisplayDevicePrimaryDevice) != 0,
            });
        }

        return monitors;
    }

    /// <summary>Extracts the EDID hardware code (for example "GSM5B7F") from a device id.</summary>
    /// <param name="deviceId">Device id such as "MONITOR\GSM5B7F\{...}" or "DISPLAY\GSM5B7F\5&amp;...".</param>
    /// <returns>The code or <see langword="null"/>.</returns>
    public static string? HardwareCode(string? deviceId)
    {
        var parts = deviceId?.Split('\\');
        return parts is { Length: >= 2 } && parts[1].Length > 0 ? parts[1].ToUpperInvariant() : null;
    }

    /// <summary>Decodes a WmiMonitorID UTF-16 code array.</summary>
    /// <param name="value">WMI value (ushort[]).</param>
    /// <returns>The decoded string or <see langword="null"/>.</returns>
    public static string? DecodeWmiString(object? value) =>
        value is ushort[] codes ? new string(codes.TakeWhile(c => c != 0).Select(c => (char)c).ToArray()).Trim() is { Length: > 0 } s ? s : null : null;

    private static Dictionary<string, (string? Name, string? Manufacturer)> ReadEdidNames()
    {
        var result = new Dictionary<string, (string?, string?)>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var row in WmiQuery.Query(@"root\wmi", "SELECT InstanceName, UserFriendlyName, ManufacturerName FROM WmiMonitorID", TimeSpan.FromSeconds(3)))
            {
                var code = HardwareCode(row.Str("InstanceName"));
                if (code is not null)
                {
                    result[code] = (DecodeWmiString(row.GetValueOrDefault("UserFriendlyName")), DecodeWmiString(row.GetValueOrDefault("ManufacturerName")));
                }
            }
        }
        catch (Exception ex) when (ex is System.Management.ManagementException or UnauthorizedAccessException or COMException)
        {
            // EDID names are optional; resolution and refresh rate still come from the display settings.
        }

        return result;
    }
}
