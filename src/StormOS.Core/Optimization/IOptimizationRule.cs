namespace StormOS.Core.Optimization;

/// <summary>
/// A reversible system optimization. Every rule follows the same lifecycle enforced by the engine:
/// detect → capture snapshot → apply → verify → record → (rollback).
/// </summary>
public interface IOptimizationRule
{
    /// <summary>Gets the stable rule id, for example "windows.game-mode".</summary>
    string Id { get; }

    /// <summary>Gets the display name.</summary>
    string Name { get; }

    /// <summary>Gets a description of what the rule changes and why.</summary>
    string Description { get; }

    /// <summary>Gets the category.</summary>
    OptimizationCategory Category { get; }

    /// <summary>Gets the risk level.</summary>
    RiskLevel RiskLevel { get; }

    /// <summary>Gets a value indicating whether applying the rule requires administrative rights (executed by the service).</summary>
    bool RequiresAdmin { get; }

    /// <summary>Gets a value indicating whether the change can be rolled back.</summary>
    bool CanRollback { get; }

    /// <summary>Gets a value indicating whether a restart or sign-out is needed for the change to take full effect.</summary>
    bool RequiresRestart { get; }

    /// <summary>Gets the supported operating system range.</summary>
    OsSupport SupportedOs { get; }

    /// <summary>Gets the parameters this rule accepts.</summary>
    IReadOnlyList<RuleParameter> Parameters { get; }

    /// <summary>Inspects the current state.</summary>
    /// <param name="context">Execution context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The detection result.</returns>
    Task<RuleDetection> DetectAsync(OptimizationContext context, CancellationToken cancellationToken = default);

    /// <summary>Captures everything required to restore the current state.</summary>
    /// <param name="context">Execution context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The snapshot.</returns>
    Task<RuleSnapshot> CaptureAsync(OptimizationContext context, CancellationToken cancellationToken = default);

    /// <summary>Applies the change.</summary>
    /// <param name="context">Execution context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the operation.</returns>
    Task ApplyAsync(OptimizationContext context, CancellationToken cancellationToken = default);

    /// <summary>Verifies that the change is in effect.</summary>
    /// <param name="context">Execution context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The verification result.</returns>
    Task<RuleVerification> VerifyAsync(OptimizationContext context, CancellationToken cancellationToken = default);

    /// <summary>Restores the state described by a snapshot and verifies the restoration.</summary>
    /// <param name="context">Execution context.</param>
    /// <param name="snapshot">The snapshot captured before the change.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The verification of the restored state.</returns>
    Task<RuleVerification> RollbackAsync(OptimizationContext context, RuleSnapshot snapshot, CancellationToken cancellationToken = default);
}

/// <summary>Supported OS range expressed as Windows build numbers.</summary>
/// <param name="MinBuild">Minimum Windows build (inclusive).</param>
/// <param name="MaxBuild">Maximum Windows build (inclusive), or <see langword="null"/> for no upper bound.</param>
public sealed record OsSupport(int MinBuild = 19041, int? MaxBuild = null)
{
    /// <summary>Gets support for Windows 10 2004 and later.</summary>
    public static OsSupport Windows10OrLater { get; } = new(19041);

    /// <summary>Gets support for Windows 11 and later.</summary>
    public static OsSupport Windows11OrLater { get; } = new(22000);

    /// <summary>Determines whether a build is supported.</summary>
    /// <param name="build">Windows build number.</param>
    /// <returns><see langword="true"/> when supported.</returns>
    public bool Supports(int build) => build >= MinBuild && (MaxBuild is null || build <= MaxBuild);
}

/// <summary>Describes a rule parameter.</summary>
/// <param name="Name">Parameter name.</param>
/// <param name="Description">Explanation.</param>
/// <param name="Required">Whether the parameter is required.</param>
/// <param name="AllowedValues">Allowed values, or empty for free text validated by the rule.</param>
public sealed record RuleParameter(string Name, string Description, bool Required = true, IReadOnlyList<string>? AllowedValues = null);

/// <summary>Execution context passed to rules.</summary>
public sealed class OptimizationContext
{
    /// <summary>Initializes a new instance of the <see cref="OptimizationContext"/> class.</summary>
    /// <param name="parameters">Rule parameters.</param>
    /// <param name="requestedBy">The user on whose behalf the change is made.</param>
    /// <param name="windowsBuild">The current Windows build number.</param>
    /// <param name="isElevated">Whether the executing process is elevated.</param>
    public OptimizationContext(IReadOnlyDictionary<string, string>? parameters, string requestedBy, int windowsBuild, bool isElevated)
    {
        Parameters = parameters ?? new Dictionary<string, string>();
        RequestedBy = requestedBy;
        WindowsBuild = windowsBuild;
        IsElevated = isElevated;
    }

    /// <summary>Gets rule parameters.</summary>
    public IReadOnlyDictionary<string, string> Parameters { get; }

    /// <summary>Gets the user on whose behalf the change is made.</summary>
    public string RequestedBy { get; }

    /// <summary>Gets the Windows build number.</summary>
    public int WindowsBuild { get; }

    /// <summary>Gets a value indicating whether the executing process is elevated.</summary>
    public bool IsElevated { get; }

    /// <summary>Gets a required parameter.</summary>
    /// <param name="name">Parameter name.</param>
    /// <returns>The value.</returns>
    /// <exception cref="ArgumentException">The parameter is missing.</exception>
    public string Require(string name) =>
        Parameters.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"Missing required parameter '{name}'.", nameof(name));

    /// <summary>Gets an optional parameter.</summary>
    /// <param name="name">Parameter name.</param>
    /// <returns>The value or <see langword="null"/>.</returns>
    public string? Optional(string name) => Parameters.TryGetValue(name, out var value) ? value : null;
}

/// <summary>Detection result.</summary>
/// <param name="State">Detected state.</param>
/// <param name="CurrentValue">Human readable current value.</param>
/// <param name="TargetValue">Human readable target value.</param>
/// <param name="Explanation">Explanation of the state.</param>
public sealed record RuleDetection(DetectionState State, string CurrentValue, string TargetValue, string Explanation);

/// <summary>Verification result.</summary>
/// <param name="Verified">Whether the expected state was observed.</param>
/// <param name="ObservedValue">Human readable observed value.</param>
/// <param name="Message">Explanation.</param>
public sealed record RuleVerification(bool Verified, string ObservedValue, string? Message = null);

/// <summary>A snapshot of state captured before a change.</summary>
public sealed record RuleSnapshot
{
    /// <summary>Gets the rule id.</summary>
    public string RuleId { get; init; } = string.Empty;

    /// <summary>Gets the capture time.</summary>
    public DateTimeOffset CapturedAt { get; init; }

    /// <summary>Gets a human readable description of the captured state.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Gets rule specific snapshot values.</summary>
    public IReadOnlyDictionary<string, string?> Values { get; init; } = new Dictionary<string, string?>();

    /// <summary>Reads a value.</summary>
    /// <param name="key">Key.</param>
    /// <returns>The value or <see langword="null"/>.</returns>
    public string? Get(string key) => Values.TryGetValue(key, out var value) ? value : null;
}
