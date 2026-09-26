using StormOS.Core.Optimization;

namespace StormOS.Core.Scan;

/// <summary>Severity of a scan finding.</summary>
public enum ScanSeverity
{
    /// <summary>No problem.</summary>
    Healthy,

    /// <summary>Worth looking at.</summary>
    Warning,

    /// <summary>Needs attention.</summary>
    Attention,
}

/// <summary>A finding of the STORM system scan.</summary>
public sealed record ScanFinding
{
    /// <summary>Gets the finding id.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Gets the area, for example "Power" or "Storage".</summary>
    public string Area { get; init; } = string.Empty;

    /// <summary>Gets the severity.</summary>
    public ScanSeverity Severity { get; init; }

    /// <summary>Gets the title.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Gets the description.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Gets the evidence (measured facts).</summary>
    public IReadOnlyList<string> Evidence { get; init; } = [];

    /// <summary>Gets the expected impact.</summary>
    public string Impact { get; init; } = string.Empty;

    /// <summary>Gets the suggested action.</summary>
    public string SuggestedAction { get; init; } = string.Empty;

    /// <summary>Gets the risk of the suggested action.</summary>
    public RiskLevel Risk { get; init; }

    /// <summary>Gets the optimization rule that implements the suggested action, if any.</summary>
    public string? RuleId { get; init; }

    /// <summary>Gets parameters for <see cref="RuleId"/>.</summary>
    public IReadOnlyDictionary<string, string>? RuleParameters { get; init; }
}

/// <summary>A performed check and whether it passed.</summary>
/// <param name="Name">Check name.</param>
/// <param name="Passed">Whether the check passed.</param>
/// <param name="Detail">Detail text.</param>
public sealed record ScanCheck(string Name, bool Passed, string Detail);

/// <summary>Result of a system scan.</summary>
public sealed record SystemScanResult
{
    /// <summary>Gets the scan time.</summary>
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>Gets the overall status.</summary>
    public ScanSeverity Overall => Findings.Count == 0 ? ScanSeverity.Healthy : Findings.Max(f => f.Severity);

    /// <summary>Gets the findings.</summary>
    public IReadOnlyList<ScanFinding> Findings { get; init; } = [];

    /// <summary>Gets the performed checks.</summary>
    public IReadOnlyList<ScanCheck> Checks { get; init; } = [];
}

/// <summary>A single area check of the system scan.</summary>
public interface ISystemScanCheck
{
    /// <summary>Gets the area name.</summary>
    string Area { get; }

    /// <summary>Runs the check.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Checks performed and findings.</returns>
    Task<(IReadOnlyList<ScanCheck> Checks, IReadOnlyList<ScanFinding> Findings)> RunAsync(CancellationToken cancellationToken = default);
}
