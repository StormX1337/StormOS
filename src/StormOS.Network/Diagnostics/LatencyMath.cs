using StormOS.Core.Network;

namespace StormOS.Network.Diagnostics;

/// <summary>Latency statistics helpers.</summary>
public static class LatencyMath
{
    /// <summary>
    /// Computes jitter as the mean absolute difference between consecutive successful round trips
    /// (the interarrival jitter estimate of RFC 3550 without exponential smoothing).
    /// </summary>
    /// <param name="samples">RTT samples; <see langword="null"/> entries are timeouts.</param>
    /// <returns>The jitter, or <see langword="null"/> with fewer than two consecutive replies.</returns>
    public static double? Jitter(IReadOnlyList<double?> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        double sum = 0;
        var pairs = 0;
        for (var i = 1; i < samples.Count; i++)
        {
            if (samples[i] is { } current && samples[i - 1] is { } previous)
            {
                sum += Math.Abs(current - previous);
                pairs++;
            }
        }

        return pairs > 0 ? sum / pairs : null;
    }

    /// <summary>Builds ping statistics from samples.</summary>
    /// <param name="target">Target.</param>
    /// <param name="samples">RTT samples; <see langword="null"/> entries are timeouts.</param>
    /// <returns>The statistics.</returns>
    public static PingStatistics Summarize(string target, IReadOnlyList<double?> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        var replies = samples.Where(s => s.HasValue).Select(s => s!.Value).ToList();
        return new PingStatistics
        {
            Target = target,
            Sent = samples.Count,
            Received = replies.Count,
            MinMs = replies.Count > 0 ? replies.Min() : null,
            AverageMs = replies.Count > 0 ? replies.Average() : null,
            MaxMs = replies.Count > 0 ? replies.Max() : null,
            JitterMs = Jitter(samples),
            Samples = samples,
        };
    }
}
