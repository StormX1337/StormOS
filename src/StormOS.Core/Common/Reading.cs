namespace StormOS.Core.Common;

/// <summary>
/// A single measured value that may be unavailable on the current system.
/// STORM OS never fabricates values: when a metric cannot be measured, <see cref="Value"/> is
/// <see langword="null"/> and <see cref="UnavailableReason"/> explains why.
/// </summary>
/// <param name="Value">The measured value, or <see langword="null"/> when unavailable.</param>
/// <param name="UnavailableReason">Human readable explanation when the value is unavailable.</param>
/// <param name="Source">The measurement source, for example "PDH" or "NVML".</param>
public readonly record struct Reading(double? Value, string? UnavailableReason = null, string? Source = null)
{
    /// <summary>Gets a value indicating whether a measured value is present.</summary>
    public bool IsAvailable => Value.HasValue;

    /// <summary>Creates an available reading.</summary>
    /// <param name="value">Measured value.</param>
    /// <param name="source">Measurement source.</param>
    /// <returns>An available reading.</returns>
    public static Reading Of(double value, string? source = null) =>
        double.IsFinite(value) ? new Reading(value, null, source) : Unavailable("The sensor returned a non-finite value.", source);

    /// <summary>Creates an unavailable reading with an explanation.</summary>
    /// <param name="reason">Why the metric is unavailable.</param>
    /// <param name="source">Measurement source that was attempted.</param>
    /// <returns>An unavailable reading.</returns>
    public static Reading Unavailable(string reason, string? source = null) => new(null, reason, source);

    /// <summary>Returns the value or a fallback when unavailable.</summary>
    /// <param name="fallback">Fallback value.</param>
    /// <returns>The measured value or <paramref name="fallback"/>.</returns>
    public double ValueOr(double fallback) => Value ?? fallback;
}
