using System.Globalization;

namespace StormOS.Core.Common;

/// <summary>Formatting helpers for sizes, rates and durations.</summary>
public static class Units
{
    private static readonly string[] ByteUnits = ["B", "KB", "MB", "GB", "TB", "PB"];

    /// <summary>Formats a byte count using binary multiples (1 KB = 1024 B).</summary>
    /// <param name="bytes">Number of bytes.</param>
    /// <param name="decimals">Number of decimals.</param>
    /// <returns>Formatted value such as "15.8 GB".</returns>
    public static string FormatBytes(double bytes, int decimals = 1)
    {
        var unit = 0;
        var value = Math.Abs(bytes);
        while (value >= 1024 && unit < ByteUnits.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        var signed = bytes < 0 ? -value : value;
        return signed.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture) + " " + ByteUnits[unit];
    }

    /// <summary>Formats a throughput value in bytes per second.</summary>
    /// <param name="bytesPerSecond">Rate in bytes per second.</param>
    /// <returns>Formatted value such as "12.4 MB/s".</returns>
    public static string FormatRate(double bytesPerSecond) => FormatBytes(bytesPerSecond) + "/s";

    /// <summary>Formats a bit rate (network speeds use decimal multiples).</summary>
    /// <param name="bitsPerSecond">Rate in bits per second.</param>
    /// <returns>Formatted value such as "940 Mbit/s".</returns>
    public static string FormatBitRate(double bitsPerSecond)
    {
        string[] units = ["bit/s", "kbit/s", "Mbit/s", "Gbit/s", "Tbit/s"];
        var unit = 0;
        var value = bitsPerSecond;
        while (value >= 1000 && unit < units.Length - 1)
        {
            value /= 1000;
            unit++;
        }

        return string.Create(CultureInfo.InvariantCulture, $"{value:0.#} {units[unit]}");
    }

    /// <summary>Formats a duration compactly, for example "3d 04:12:09".</summary>
    /// <param name="duration">The duration.</param>
    /// <returns>Formatted duration.</returns>
    public static string FormatDuration(TimeSpan duration) =>
        duration.TotalDays >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)duration.TotalDays}d {duration:hh\\:mm\\:ss}")
            : duration.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);

    /// <summary>Formats a percentage delta with sign, for example "+4.8%".</summary>
    /// <param name="percent">Delta in percent.</param>
    /// <returns>Formatted delta.</returns>
    public static string FormatDeltaPercent(double percent) =>
        string.Create(CultureInfo.InvariantCulture, $"{(percent >= 0 ? "+" : string.Empty)}{percent:0.0}%");
}
