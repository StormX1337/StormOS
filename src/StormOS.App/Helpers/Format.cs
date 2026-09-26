using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using StormOS.Core.Common;
using StormOS.Core.Optimization;
using StormOS.Core.Scan;

namespace StormOS.App.Helpers;

/// <summary>Formatting functions used from x:Bind. Unavailable values are shown as such, never as zero.</summary>
public static class Format
{
    private static readonly CultureInfo Culture = CultureInfo.CurrentCulture;

    public static string Reading(Reading reading, string format, string unit) =>
        reading.Value is { } value ? value.ToString(format, Culture) + unit : "Unavailable";

    public static string ReadingReason(Reading reading) => reading.UnavailableReason ?? string.Empty;

    public static string Number(double? value, string format, string unit) =>
        value is { } v && double.IsFinite(v) ? v.ToString(format, Culture) + unit : "—";

    public static string Percent(double? value) => Number(value, "0", " %");

    public static string Bytes(long? bytes) => bytes is { } b ? Units.FormatBytes(b) : "—";

    public static string BytesRate(double? bytesPerSecond) => bytesPerSecond is { } b ? Units.FormatRate(b) : "—";

    public static string BitRate(double? bitsPerSecond) => bitsPerSecond is { } b ? Units.FormatBitRate(b) : "—";

    public static string Duration(TimeSpan duration) => Units.FormatDuration(duration);

    public static string DateTime(DateTimeOffset? value) => value is { } v ? v.ToLocalTime().ToString("g", Culture) : "—";

    public static string Date(DateOnly? value) => value is { } v ? v.ToString("d", Culture) : "—";

    public static string Text(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value;

    public static string Upper(string? value) => (value ?? string.Empty).ToUpperInvariant();

    public static string Score(double? score) => score is { } s ? s.ToString("0", Culture) : "—";

    public static string Delta(double percent) => Units.FormatDeltaPercent(percent);

    public static double Clamp100(double? value) => value is { } v && double.IsFinite(v) ? Math.Clamp(v, 0, 100) : 0;

    public static double ReadingBar(Reading reading) => Clamp100(reading.Value);

    public static double Scale(double value, double max) => double.IsFinite(value) ? Math.Clamp(value / 100.0 * max, 1, max) : 1;

    public static Visibility Show(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility StepVisible(int step, int index) => step == index ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility ShowCount(int count) => count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility Hide(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    public static Visibility ShowIf(object? value) => value is null ? Visibility.Collapsed : Visibility.Visible;

    public static Visibility ShowText(string? value) => string.IsNullOrWhiteSpace(value) ? Visibility.Collapsed : Visibility.Visible;

    public static bool Not(bool value) => !value;

    public static string Choose(bool condition, string whenTrue, string whenFalse) => condition ? whenTrue : whenFalse;

    public static Brush OverlayBrush(bool accent) => new SolidColorBrush(accent ? global::Windows.UI.Color.FromArgb(255, 64, 196, 255) : Microsoft.UI.Colors.White);

    public static bool IsNotEmpty(string? value) => !string.IsNullOrWhiteSpace(value);

    public static string Caps(object? value) => value?.ToString()?.ToUpperInvariant() ?? string.Empty;

    public static Brush RiskBrush(RiskLevel risk) => Resource(risk switch
    {
        RiskLevel.High => "StormErrorBrush",
        RiskLevel.Medium => "StormWarningBrush",
        _ => "StormSuccessBrush",
    });

    public static Brush SeverityBrush(ScanSeverity severity) => Resource(severity switch
    {
        ScanSeverity.Attention => "StormErrorBrush",
        ScanSeverity.Warning => "StormWarningBrush",
        _ => "StormSuccessBrush",
    });

    public static Brush PassBrush(bool passed) => Resource(passed ? "StormSuccessBrush" : "StormWarningBrush");

    public static string PassGlyph(bool passed) => passed ? "" : "";

    public static Brush ScoreBrush(double? score) => Resource(score switch
    {
        null => "StormTextTertiaryBrush",
        >= 75 => "StormSuccessBrush",
        >= 50 => "StormWarningBrush",
        _ => "StormErrorBrush",
    });

    public static Brush DeltaBrush(bool improved) => Resource(improved ? "StormSuccessBrush" : "StormErrorBrush");

    private static Brush Resource(string key) =>
        Application.Current.Resources.TryGetValue(key, out var value) && value is Brush brush ? brush : new SolidColorBrush(Microsoft.UI.Colors.Gray);
}
