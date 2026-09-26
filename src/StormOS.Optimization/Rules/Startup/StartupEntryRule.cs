using StormOS.Core.Optimization;
using StormOS.Core.Startup;

namespace StormOS.Optimization.Rules.Startup;

/// <summary>
/// Enables or disables a startup entry via the StartupApproved mechanism (like Task Manager). Entries are never
/// deleted. Two instances exist: one for per-user entries (runs in the app) and one for machine-wide entries (service).
/// </summary>
public sealed class StartupEntryRule : IOptimizationRule
{
    private readonly IStartupManager _startup;
    private readonly bool _machine;

    /// <summary>Initializes a new instance of the <see cref="StartupEntryRule"/> class.</summary>
    /// <param name="startup">Startup manager.</param>
    /// <param name="machine">Whether this instance handles machine-wide entries.</param>
    public StartupEntryRule(IStartupManager startup, bool machine)
    {
        _startup = startup;
        _machine = machine;
    }

    /// <inheritdoc />
    public string Id => _machine ? "startup.entry-machine" : "startup.entry";

    /// <inheritdoc />
    public string Name => _machine ? "Startup program (all users)" : "Startup program";

    /// <inheritdoc />
    public string Description => "Enables or disables a program that starts with Windows. The entry is kept and can be re-enabled at any time.";

    /// <inheritdoc />
    public OptimizationCategory Category => OptimizationCategory.Startup;

    /// <inheritdoc />
    public RiskLevel RiskLevel => RiskLevel.Low;

    /// <inheritdoc />
    public bool RequiresAdmin => _machine;

    /// <inheritdoc />
    public bool CanRollback => true;

    /// <inheritdoc />
    public bool RequiresRestart => false;

    /// <inheritdoc />
    public OsSupport SupportedOs => OsSupport.Windows10OrLater;

    /// <inheritdoc />
    public IReadOnlyList<RuleParameter> Parameters { get; } =
    [
        new("entryId", "The startup entry id."),
        new("state", "Target state.", AllowedValues: ["enabled", "disabled"]),
    ];

    /// <summary>Determines whether an entry id belongs to machine-wide sources.</summary>
    /// <param name="entryId">Entry id.</param>
    /// <returns><see langword="true"/> for machine-wide entries.</returns>
    public static bool IsMachineEntry(string entryId) =>
        entryId.StartsWith(nameof(StartupSource.RegistryMachineRun), StringComparison.Ordinal) || entryId.StartsWith(nameof(StartupSource.CommonStartupFolder), StringComparison.Ordinal);

    /// <inheritdoc />
    public Task<RuleDetection> DetectAsync(OptimizationContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var entryId = context.Require("entryId");
        var target = context.Require("state") == "enabled";
        if (IsMachineEntry(entryId) != _machine)
        {
            return Task.FromResult(new RuleDetection(DetectionState.NotApplicable, "-", "-", "This entry is managed by the other STORM OS component."));
        }

        var current = _startup.IsEnabled(entryId);
        if (current is null)
        {
            return Task.FromResult(new RuleDetection(DetectionState.NotApplicable, "Not found", "-", "The startup entry no longer exists."));
        }

        return Task.FromResult(new RuleDetection(current == target ? DetectionState.AlreadyApplied : DetectionState.Applicable, State(current.Value), State(target), Description));
    }

    /// <inheritdoc />
    public Task<RuleSnapshot> CaptureAsync(OptimizationContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var current = _startup.IsEnabled(context.Require("entryId")) ?? throw new InvalidOperationException("The startup entry no longer exists.");
        return Task.FromResult(new RuleSnapshot { RuleId = Id, CapturedAt = DateTimeOffset.UtcNow, Description = State(current), Values = new Dictionary<string, string?> { ["enabled"] = current ? "true" : "false" } });
    }

    /// <inheritdoc />
    public Task ApplyAsync(OptimizationContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        _startup.SetEnabled(context.Require("entryId"), context.Require("state") == "enabled");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<RuleVerification> VerifyAsync(OptimizationContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var current = _startup.IsEnabled(context.Require("entryId"));
        return Task.FromResult(new RuleVerification(current == (context.Require("state") == "enabled"), current is null ? "Not found" : State(current.Value)));
    }

    /// <inheritdoc />
    public Task<RuleVerification> RollbackAsync(OptimizationContext context, RuleSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(snapshot);
        var entryId = context.Require("entryId");
        var original = snapshot.Get("enabled") == "true";
        _startup.SetEnabled(entryId, original);
        var current = _startup.IsEnabled(entryId);
        return Task.FromResult(new RuleVerification(current == original, current is null ? "Not found" : State(current.Value)));
    }

    private static string State(bool enabled) => enabled ? "Enabled" : "Disabled";
}
