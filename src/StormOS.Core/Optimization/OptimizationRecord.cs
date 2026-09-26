namespace StormOS.Core.Optimization;

/// <summary>An audit record of an optimization attempt.</summary>
public sealed record OptimizationRecord
{
    /// <summary>Gets the change id.</summary>
    public Guid Id { get; init; }

    /// <summary>Gets the time of the attempt.</summary>
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>Gets the rule id.</summary>
    public string RuleId { get; init; } = string.Empty;

    /// <summary>Gets the rule name.</summary>
    public string RuleName { get; init; } = string.Empty;

    /// <summary>Gets the rule parameters.</summary>
    public IReadOnlyDictionary<string, string> Parameters { get; init; } = new Dictionary<string, string>();

    /// <summary>Gets the state before the change.</summary>
    public string Before { get; init; } = string.Empty;

    /// <summary>Gets the state after the change.</summary>
    public string After { get; init; } = string.Empty;

    /// <summary>Gets the user that requested the change.</summary>
    public string User { get; init; } = string.Empty;

    /// <summary>Gets the executor ("app" or "service").</summary>
    public string Executor { get; init; } = string.Empty;

    /// <summary>Gets the outcome.</summary>
    public OptimizationOutcome Outcome { get; init; }

    /// <summary>Gets a message describing the outcome.</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>Gets the rollback status.</summary>
    public RollbackStatus Rollback { get; init; }

    /// <summary>Gets the time the change was rolled back.</summary>
    public DateTimeOffset? RolledBackAt { get; init; }

    /// <summary>Gets the snapshot captured before the change.</summary>
    public RuleSnapshot? Snapshot { get; init; }

    /// <summary>Gets a value indicating whether a restart is needed.</summary>
    public bool RequiresRestart { get; init; }
}

/// <summary>Persists optimization records and snapshots.</summary>
public interface IOptimizationJournal
{
    /// <summary>Stores or updates a record.</summary>
    /// <param name="record">The record.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the operation.</returns>
    Task SaveAsync(OptimizationRecord record, CancellationToken cancellationToken = default);

    /// <summary>Gets a record by id.</summary>
    /// <param name="id">Change id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The record or <see langword="null"/>.</returns>
    Task<OptimizationRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Lists records, newest first.</summary>
    /// <param name="limit">Maximum number of records.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Records.</returns>
    Task<IReadOnlyList<OptimizationRecord>> ListAsync(int limit = 200, CancellationToken cancellationToken = default);

    /// <summary>Lists changes that are still active and can be rolled back, newest first.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Active records.</returns>
    Task<IReadOnlyList<OptimizationRecord>> ListActiveAsync(CancellationToken cancellationToken = default);
}

/// <summary>A rule description with its current detection, used by the UI.</summary>
public sealed record RuleDescriptor
{
    /// <summary>Gets the rule id.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Gets the display name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Gets the description.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Gets the category.</summary>
    public OptimizationCategory Category { get; init; }

    /// <summary>Gets the risk level.</summary>
    public RiskLevel RiskLevel { get; init; }

    /// <summary>Gets a value indicating whether administrative rights are required.</summary>
    public bool RequiresAdmin { get; init; }

    /// <summary>Gets a value indicating whether the rule can be rolled back.</summary>
    public bool CanRollback { get; init; }

    /// <summary>Gets a value indicating whether a restart is required.</summary>
    public bool RequiresRestart { get; init; }

    /// <summary>Gets the minimum supported Windows build.</summary>
    public int MinWindowsBuild { get; init; }

    /// <summary>Gets the rule parameters.</summary>
    public IReadOnlyList<RuleParameter> Parameters { get; init; } = [];

    /// <summary>Gets the detection result for the default parameters, when detected.</summary>
    public RuleDetection? Detection { get; init; }

    /// <summary>Creates a descriptor from a rule.</summary>
    /// <param name="rule">The rule.</param>
    /// <param name="detection">Optional detection result.</param>
    /// <returns>The descriptor.</returns>
    public static RuleDescriptor From(IOptimizationRule rule, RuleDetection? detection = null)
    {
        ArgumentNullException.ThrowIfNull(rule);
        return new RuleDescriptor
        {
            Id = rule.Id,
            Name = rule.Name,
            Description = rule.Description,
            Category = rule.Category,
            RiskLevel = rule.RiskLevel,
            RequiresAdmin = rule.RequiresAdmin,
            CanRollback = rule.CanRollback,
            RequiresRestart = rule.RequiresRestart,
            MinWindowsBuild = rule.SupportedOs.MinBuild,
            Parameters = rule.Parameters,
            Detection = detection,
        };
    }
}

/// <summary>Applies, verifies and rolls back optimization rules.</summary>
public interface IOptimizationEngine
{
    /// <summary>Gets the rules available in this executor.</summary>
    IReadOnlyList<IOptimizationRule> Rules { get; }

    /// <summary>Detects the state of a rule.</summary>
    /// <param name="ruleId">Rule id.</param>
    /// <param name="parameters">Parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The detection.</returns>
    Task<RuleDetection> DetectAsync(string ruleId, IReadOnlyDictionary<string, string>? parameters, CancellationToken cancellationToken = default);

    /// <summary>Applies a rule following the full detect → snapshot → apply → verify → record lifecycle.</summary>
    /// <param name="ruleId">Rule id.</param>
    /// <param name="parameters">Parameters.</param>
    /// <param name="requestedBy">The requesting user.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The resulting record.</returns>
    Task<OptimizationRecord> ApplyAsync(string ruleId, IReadOnlyDictionary<string, string>? parameters, string requestedBy, CancellationToken cancellationToken = default);

    /// <summary>Rolls back a recorded change.</summary>
    /// <param name="changeId">The change id.</param>
    /// <param name="requestedBy">The requesting user.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated record.</returns>
    Task<OptimizationRecord> RollbackAsync(Guid changeId, string requestedBy, CancellationToken cancellationToken = default);

    /// <summary>Rolls back all active changes, newest first.</summary>
    /// <param name="requestedBy">The requesting user.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated records.</returns>
    Task<IReadOnlyList<OptimizationRecord>> RollbackAllAsync(string requestedBy, CancellationToken cancellationToken = default);
}
