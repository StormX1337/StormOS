namespace StormOS.Core.Frames;

/// <summary>A single presented frame as measured by a frame capture provider.</summary>
/// <param name="TimestampSeconds">Present time in seconds relative to capture start.</param>
/// <param name="FrameTimeMs">Time since the previous present in milliseconds (MsBetweenPresents).</param>
/// <param name="CpuBusyMs">CPU time spent producing the frame, when the provider reports it.</param>
/// <param name="GpuBusyMs">GPU busy time for the frame, when the provider reports it.</param>
/// <param name="Dropped">Whether the frame was never displayed, when the provider reports it.</param>
public readonly record struct FrameSample(
    double TimestampSeconds,
    double FrameTimeMs,
    double? CpuBusyMs = null,
    double? GpuBusyMs = null,
    bool? Dropped = null);
