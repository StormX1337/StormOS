namespace StormOS.Performance.Frames;

/// <summary>Frame capture configuration.</summary>
public sealed class FrameCaptureOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "FrameCapture";

    /// <summary>Gets or sets an explicit PresentMon executable path.</summary>
    public string? PresentMonPath { get; set; }

    /// <summary>Gets or sets the expected SHA-256 of an unsigned PresentMon binary (optional pin).</summary>
    public string? PresentMonSha256 { get; set; }

    /// <summary>Gets or sets a value indicating whether the ETW fallback may be used.</summary>
    public bool AllowEtwFallback { get; set; } = true;

    /// <summary>Gets or sets the live statistics window in seconds.</summary>
    public int LiveWindowSeconds { get; set; } = 60;

    /// <summary>Gets or sets the maximum number of frames kept in memory.</summary>
    public int MaxBufferedFrames { get; set; } = 120_000;
}
