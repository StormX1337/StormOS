using StormOS.Core.Frames;
using StormOS.Core.Optimization;

namespace StormOS.Core.Analysis;

/// <summary>Aggregated, structured measurements handed to the analysis engine.</summary>
public sealed record AnalysisWindow
{
    /// <summary>Gets the window start.</summary>
    public DateTimeOffset From { get; init; }

    /// <summary>Gets the window end.</summary>
    public DateTimeOffset To { get; init; }

    /// <summary>Gets the number of telemetry samples.</summary>
    public int SampleCount { get; init; }

    /// <summary>Gets the average CPU usage in percent.</summary>
    public double? AverageCpuUsage { get; init; }

    /// <summary>Gets the average of the busiest logical processor in percent.</summary>
    public double? AverageMaxCoreUsage { get; init; }

    /// <summary>Gets the average GPU usage in percent.</summary>
    public double? AverageGpuUsage { get; init; }

    /// <summary>Gets the average RAM usage in percent.</summary>
    public double? AverageRamUsage { get; init; }

    /// <summary>Gets the average VRAM usage in percent.</summary>
    public double? AverageVramUsage { get; init; }

    /// <summary>Gets the average disk active time of the busiest disk in percent.</summary>
    public double? AverageDiskActive { get; init; }

    /// <summary>Gets the average latency in ms.</summary>
    public double? AverageLatencyMs { get; init; }

    /// <summary>Gets the maximum CPU temperature in °C.</summary>
    public double? MaxCpuTemperature { get; init; }

    /// <summary>Gets the maximum GPU temperature in °C.</summary>
    public double? MaxGpuTemperature { get; init; }

    /// <summary>Gets the process count.</summary>
    public int? ProcessCount { get; init; }

    /// <summary>Gets the number of background (non-game, non-system) processes using noticeable CPU.</summary>
    public int? BusyBackgroundProcesses { get; init; }

    /// <summary>Gets the active game name, if any.</summary>
    public string? GameName { get; init; }

    /// <summary>Gets frame statistics, when a capture was active.</summary>
    public FrameStatistics? Frames { get; init; }

    /// <summary>Gets the active power plan name.</summary>
    public string? PowerPlan { get; init; }

    /// <summary>Gets the display refresh rate in Hz.</summary>
    public double? RefreshRateHz { get; init; }
}

/// <summary>Confidence of a recommendation.</summary>
public enum Confidence
{
    /// <summary>Low confidence; treat as a hint.</summary>
    Low,

    /// <summary>Medium confidence.</summary>
    Medium,

    /// <summary>High confidence; strong evidence.</summary>
    High,
}

/// <summary>A suggested action that the user may confirm.</summary>
/// <param name="Label">Button label.</param>
/// <param name="RuleId">Optimization rule to apply.</param>
/// <param name="Parameters">Rule parameters.</param>
public sealed record RecommendedAction(string Label, string RuleId, IReadOnlyDictionary<string, string>? Parameters = null);

/// <summary>An explainable recommendation. Recommendations never execute on their own.</summary>
public sealed record Recommendation
{
    /// <summary>Gets the recommendation id.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Gets the title.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Gets why the recommendation was made.</summary>
    public string Why { get; init; } = string.Empty;

    /// <summary>Gets the measured evidence.</summary>
    public IReadOnlyList<string> Evidence { get; init; } = [];

    /// <summary>Gets the risk of acting on it.</summary>
    public RiskLevel Risk { get; init; }

    /// <summary>Gets the recommendation text.</summary>
    public string Advice { get; init; } = string.Empty;

    /// <summary>Gets an optional action requiring user confirmation.</summary>
    public RecommendedAction? Action { get; init; }

    /// <summary>Gets the confidence.</summary>
    public Confidence Confidence { get; init; }

    /// <summary>Gets the source: "rules" for the deterministic engine or "ai" for the optional language model.</summary>
    public string Source { get; init; } = "rules";
}

/// <summary>Produces explainable recommendations from structured measurements.</summary>
public interface IAnalysisEngine
{
    /// <summary>Gets the engine name.</summary>
    string Name { get; }

    /// <summary>Analyzes a window of measurements.</summary>
    /// <param name="window">The measurements.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Recommendations backed by evidence.</returns>
    Task<IReadOnlyList<Recommendation>> AnalyzeAsync(AnalysisWindow window, CancellationToken cancellationToken = default);
}
