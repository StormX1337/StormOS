using StormOS.Core.Optimization;
using StormOS.Core.Power;

namespace StormOS.Optimization.Rules.Power;

/// <summary>Activates an existing Windows power plan. Nothing is forced permanently: the previous plan is restorable.</summary>
public sealed class PowerPlanRule(IPowerPlanService power) : IOptimizationRule
{
    /// <inheritdoc />
    public string Id => "power.plan";

    /// <inheritdoc />
    public string Name => "Power plan";

    /// <inheritdoc />
    public string Description => "Switches the active Windows power plan. High performance keeps CPU clocks up and avoids ramp-up delays at the cost of higher idle power.";

    /// <inheritdoc />
    public OptimizationCategory Category => OptimizationCategory.Power;

    /// <inheritdoc />
    public RiskLevel RiskLevel => RiskLevel.Low;

    /// <inheritdoc />
    public bool RequiresAdmin => false;

    /// <inheritdoc />
    public bool CanRollback => true;

    /// <inheritdoc />
    public bool RequiresRestart => false;

    /// <inheritdoc />
    public OsSupport SupportedOs => OsSupport.Windows10OrLater;

    /// <inheritdoc />
    public IReadOnlyList<RuleParameter> Parameters { get; } =
    [
        new("plan", "\"balanced\", \"high-performance\" or the GUID of an installed plan."),
    ];

    /// <summary>Resolves the plan parameter to a GUID.</summary>
    /// <param name="plan">Plan parameter.</param>
    /// <returns>The GUID or <see langword="null"/> when invalid.</returns>
    public static Guid? ResolvePlan(string plan) => plan switch
    {
        "balanced" => KnownPowerSchemes.Balanced,
        "high-performance" => KnownPowerSchemes.HighPerformance,
        _ => Guid.TryParse(plan, out var id) ? id : null,
    };

    /// <inheritdoc />
    public Task<RuleDetection> DetectAsync(OptimizationContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var plans = power.GetPlans();
        var active = plans.FirstOrDefault(p => p.IsActive);
        var targetId = ResolvePlan(context.Require("plan"));
        var target = plans.FirstOrDefault(p => p.Id == targetId);
        if (target is null)
        {
            return Task.FromResult(new RuleDetection(DetectionState.NotApplicable, active?.Name ?? "-", context.Require("plan"),
                "This power plan is not available on this PC. Modern Standby devices only offer Balanced; use the Windows power mode instead."));
        }

        return Task.FromResult(new RuleDetection(target.IsActive ? DetectionState.AlreadyApplied : DetectionState.Applicable, active?.Name ?? "-", target.Name, target.IsActive ? $"{target.Name} is already active." : $"Switch from {active?.Name} to {target.Name}."));
    }

    /// <inheritdoc />
    public Task<RuleSnapshot> CaptureAsync(OptimizationContext context, CancellationToken cancellationToken = default)
    {
        var active = power.GetActivePlanId();
        return Task.FromResult(new RuleSnapshot
        {
            RuleId = Id,
            CapturedAt = DateTimeOffset.UtcNow,
            Description = power.GetPlans().FirstOrDefault(p => p.Id == active)?.Name ?? active.ToString(),
            Values = new Dictionary<string, string?> { ["previous"] = active.ToString() },
        });
    }

    /// <inheritdoc />
    public Task ApplyAsync(OptimizationContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        power.SetActivePlan(ResolvePlan(context.Require("plan")) ?? throw new ArgumentException("Invalid plan."));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<RuleVerification> VerifyAsync(OptimizationContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var active = power.GetActivePlanId();
        return Task.FromResult(new RuleVerification(active == ResolvePlan(context.Require("plan")), NameOf(active)));
    }

    /// <inheritdoc />
    public Task<RuleVerification> RollbackAsync(OptimizationContext context, RuleSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var previous = Guid.Parse(snapshot.Get("previous") ?? throw new ArgumentException("Snapshot is missing the previous plan."));
        power.SetActivePlan(previous);
        var active = power.GetActivePlanId();
        return Task.FromResult(new RuleVerification(active == previous, NameOf(active)));
    }

    private string NameOf(Guid id) => power.GetPlans().FirstOrDefault(p => p.Id == id)?.Name ?? id.ToString();
}

/// <summary>Creates the "Ultimate Performance" plan from the built-in Windows template. Rollback deletes the created plan.</summary>
public sealed class UltimatePerformancePlanRule(IPowerPlanService power) : IOptimizationRule
{
    private static readonly string[] KnownNames = ["Ultimate Performance", "Ultimative Leistung", "Performances optimales", "Máximo rendimiento", "Prestazioni eccellenti"];

    /// <inheritdoc />
    public string Id => "power.ultimate-plan";

    /// <inheritdoc />
    public string Name => "Create Ultimate Performance plan";

    /// <inheritdoc />
    public string Description => "Adds Windows' built-in Ultimate Performance plan to the plan list (it is hidden by default). It is not activated automatically; select it afterwards. Uses more power at idle.";

    /// <inheritdoc />
    public OptimizationCategory Category => OptimizationCategory.Power;

    /// <inheritdoc />
    public RiskLevel RiskLevel => RiskLevel.Low;

    /// <inheritdoc />
    public bool RequiresAdmin => true;

    /// <inheritdoc />
    public bool CanRollback => true;

    /// <inheritdoc />
    public bool RequiresRestart => false;

    /// <inheritdoc />
    public OsSupport SupportedOs => OsSupport.Windows10OrLater;

    /// <inheritdoc />
    public IReadOnlyList<RuleParameter> Parameters => [];

    /// <inheritdoc />
    public Task<RuleDetection> DetectAsync(OptimizationContext context, CancellationToken cancellationToken = default)
    {
        var existing = FindExisting(power.GetPlans());
        return Task.FromResult(existing is null
            ? new RuleDetection(DetectionState.Applicable, "Not installed", "Installed", Description)
            : new RuleDetection(DetectionState.AlreadyApplied, "Installed", "Installed", $"'{existing.Name}' already exists."));
    }

    /// <inheritdoc />
    public Task<RuleSnapshot> CaptureAsync(OptimizationContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(new RuleSnapshot
        {
            RuleId = Id,
            CapturedAt = DateTimeOffset.UtcNow,
            Description = "Plans before creation",
            Values = new Dictionary<string, string?>
            {
                ["plans"] = string.Join(',', power.GetPlans().Select(p => p.Id)),
                ["active"] = power.GetActivePlanId().ToString(),
            },
        });

    /// <inheritdoc />
    public Task ApplyAsync(OptimizationContext context, CancellationToken cancellationToken = default)
    {
        _ = power.DuplicatePlan(KnownPowerSchemes.UltimatePerformance);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<RuleVerification> VerifyAsync(OptimizationContext context, CancellationToken cancellationToken = default)
    {
        var existing = FindExisting(power.GetPlans());
        return Task.FromResult(new RuleVerification(existing is not null, existing is null ? "Not installed" : "Installed"));
    }

    /// <inheritdoc />
    public Task<RuleVerification> RollbackAsync(OptimizationContext context, RuleSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var before = (snapshot.Get("plans") ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Guid.Parse).ToHashSet();
        var created = power.GetPlans().Where(p => !before.Contains(p.Id) && KnownNames.Contains(p.Name, StringComparer.OrdinalIgnoreCase)).ToList();
        if (created.Any(p => p.IsActive) && Guid.TryParse(snapshot.Get("active"), out var previous))
        {
            power.SetActivePlan(previous);
        }

        foreach (var plan in created)
        {
            power.DeletePlan(plan.Id);
        }

        var remaining = power.GetPlans().Count(p => created.Any(c => c.Id == p.Id));
        return Task.FromResult(new RuleVerification(remaining == 0, remaining == 0 ? "Removed" : "Still installed"));
    }

    private static PowerPlan? FindExisting(IReadOnlyList<PowerPlan> plans) =>
        plans.FirstOrDefault(p => p.Id == KnownPowerSchemes.UltimatePerformance || KnownNames.Contains(p.Name, StringComparer.OrdinalIgnoreCase));
}
