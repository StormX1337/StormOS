using StormOS.Core.Common;

namespace StormOS.Core.Rgb;

/// <summary>An RGB color.</summary>
/// <param name="R">Red.</param>
/// <param name="G">Green.</param>
/// <param name="B">Blue.</param>
public readonly record struct RgbColor(byte R, byte G, byte B)
{
    /// <summary>Formats the color as #RRGGBB.</summary>
    /// <returns>The hex string.</returns>
    public override string ToString() => $"#{R:X2}{G:X2}{B:X2}";
}

/// <summary>Kind of RGB device.</summary>
public enum RgbDeviceKind
{
    /// <summary>Unknown.</summary>
    Unknown,

    /// <summary>Keyboard.</summary>
    Keyboard,

    /// <summary>Mouse.</summary>
    Mouse,

    /// <summary>Headset.</summary>
    Headset,

    /// <summary>Motherboard or controller.</summary>
    Motherboard,

    /// <summary>Memory module.</summary>
    Memory,

    /// <summary>Graphics card.</summary>
    Gpu,

    /// <summary>Fan or cooler.</summary>
    Cooler,

    /// <summary>LED strip.</summary>
    LedStrip,
}

/// <summary>An RGB device exposed by a provider.</summary>
public sealed record RgbDevice
{
    /// <summary>Gets the provider-unique id.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Gets the display name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Gets the manufacturer.</summary>
    public string? Manufacturer { get; init; }

    /// <summary>Gets the device kind.</summary>
    public RgbDeviceKind Kind { get; init; }

    /// <summary>Gets the number of addressable zones.</summary>
    public int ZoneCount { get; init; }
}

/// <summary>
/// Plugin contract for RGB lighting. Providers must use the vendor's documented user-mode SDK or the Windows
/// Dynamic Lighting API; kernel drivers, direct SMBus access and injection are not allowed. Providers are optional
/// and discovered through dependency injection; STORM OS works without any.
/// </summary>
public interface IRgbProvider
{
    /// <summary>Gets the stable provider id (for example "windows.dynamic-lighting").</summary>
    string Id { get; }

    /// <summary>Gets the display name.</summary>
    string Name { get; }

    /// <summary>Checks whether the provider can be used on this system.</summary>
    /// <returns><see langword="null"/> when available, otherwise the reason.</returns>
    string? CheckAvailability();

    /// <summary>Lists devices.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Devices.</returns>
    Task<IReadOnlyList<RgbDevice>> GetDevicesAsync(CancellationToken cancellationToken = default);

    /// <summary>Sets a static color on a device.</summary>
    /// <param name="deviceId">Device id.</param>
    /// <param name="color">Color.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result.</returns>
    Task<Result> SetColorAsync(string deviceId, RgbColor color, CancellationToken cancellationToken = default);

    /// <summary>Returns control of all devices to their default (vendor) lighting.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result.</returns>
    Task<Result> ReleaseAsync(CancellationToken cancellationToken = default);
}
