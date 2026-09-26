using System.Globalization;
using StormOS.Core.Settings;
using StormOS.Core.Telemetry;

namespace StormOS.Overlay;

/// <summary>A label/value row shown in the overlay.</summary>
/// <param name="Label">Label.</param>
/// <param name="Value">Value text.</param>
/// <param name="Accent">Whether the value uses the accent color.</param>
public readonly record struct OverlayLine(string Label, string Value, bool Accent = false);

/// <summary>Formats telemetry into overlay lines according to the selected metrics. Unavailable metrics show "n/a".</summary>
public static class OverlayContent
{
    /// <summary>All selectable metric keys.</summary>
    public static IReadOnlyList<(string Key, string Label)> AvailableMetrics { get; } =
    [
        ("fps", "FPS"), ("avgFps", "AVG FPS"), ("low1", "1% LOW"), ("low01", "0.1% LOW"), ("frametime", "FRAME TIME"),
        ("cpu", "CPU"), ("gpu", "GPU"), ("ram", "RAM"), ("vram", "VRAM"), ("cpuTemp", "CPU TEMP"), ("gpuTemp", "GPU TEMP"), ("ping", "PING"),
    ];

    /// <summary>Builds the overlay lines.</summary>
    /// <param name="snapshot">Latest snapshot.</param>
    /// <param name="settings">Overlay settings.</param>
    /// <returns>Lines to render.</returns>
    public static IReadOnlyList<OverlayLine> Build(MetricsSnapshot? snapshot, OverlaySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var lines = new List<OverlayLine>(settings.Metrics.Count);
        var frames = snapshot?.Frames;
        var gpu = snapshot?.PrimaryGpu();
        foreach (var key in settings.Metrics)
        {
            var label = AvailableMetrics.FirstOrDefault(m => m.Key == key).Label;
            if (label is null)
            {
                continue;
            }

            lines.Add(key switch
            {
                "fps" => new(label, frames is null ? "n/a" : F(frames.Fps, "0"), true),
                "avgFps" => new(label, frames is null ? "n/a" : F(frames.Window.AverageFps, "0")),
                "low1" => new(label, frames is null ? "n/a" : F(frames.Window.OnePercentLowFps, "0")),
                "low01" => new(label, frames is null ? "n/a" : F(frames.Window.PointOnePercentLowFps, "0")),
                "frametime" => new(label, frames is null ? "n/a" : F(frames.FrameTimeMs, "0.00") + " ms"),
                "cpu" => new(label, snapshot?.Cpu.Usage.Value is { } cpu ? F(cpu, "0") + " %" : "n/a"),
                "gpu" => new(label, gpu?.Usage.Value is { } g ? F(g, "0") + " %" : "n/a"),
                "ram" => new(label, snapshot is { Memory.TotalBytes: > 0 } ? F(snapshot.Memory.UsagePercent, "0") + " %" : "n/a"),
                "vram" => new(label, gpu?.MemoryUsagePercent is { } v ? F(v, "0") + " %" : "n/a"),
                "cpuTemp" => new(label, snapshot?.Cpu.TemperatureCelsius.Value is { } ct ? F(ct, "0") + " °C" : "n/a"),
                "gpuTemp" => new(label, gpu?.TemperatureCelsius.Value is { } gt ? F(gt, "0") + " °C" : "n/a"),
                "ping" => new(label, snapshot?.System.LatencyMs.Value is { } p ? F(p, "0") + " ms" : "n/a"),
                _ => new(label, "n/a"),
            });
        }

        return lines;
    }

    /// <summary>Parses a hotkey such as "Ctrl+Shift+F10" into Win32 modifier flags and a virtual key code.</summary>
    /// <param name="hotkey">Hotkey text.</param>
    /// <param name="modifiers">MOD_* flags.</param>
    /// <param name="virtualKey">Virtual key code.</param>
    /// <returns><see langword="true"/> when valid.</returns>
    public static bool TryParseHotkey(string? hotkey, out uint modifiers, out uint virtualKey)
    {
        modifiers = 0;
        virtualKey = 0;
        if (string.IsNullOrWhiteSpace(hotkey))
        {
            return false;
        }

        foreach (var part in hotkey.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (part.ToUpperInvariant())
            {
                case "CTRL" or "CONTROL":
                    modifiers |= 0x2;
                    break;
                case "SHIFT":
                    modifiers |= 0x4;
                    break;
                case "ALT":
                    modifiers |= 0x1;
                    break;
                case var key when key.Length >= 2 && key[0] == 'F' && int.TryParse(key.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var f) && f is >= 1 and <= 24:
                    virtualKey = (uint)(0x70 + f - 1);
                    break;
                case var key when key.Length == 1 && char.IsAsciiLetterOrDigit(key[0]):
                    virtualKey = key[0];
                    break;
                default:
                    return false;
            }
        }

        // MOD_NOREPEAT avoids repeated toggles while the key is held.
        modifiers |= 0x4000;
        return virtualKey != 0 && (modifiers & 0x7) != 0;
    }

    private static string F(double value, string format) => value.ToString(format, CultureInfo.InvariantCulture);
}
