using StormOS.Core.Optimization;
using StormOS.Windows.Services;

namespace StormOS.Optimization.Rules.Services;

/// <summary>Changes the start type of an allow-listed Windows service (see <see cref="ServiceKnowledgeBase"/>).</summary>
public sealed class ServiceStartTypeRule(IServiceConfigurator services, ServiceKnowledgeBase knowledge) : IOptimizationRule
{
    /// <inheritdoc />
    public string Id => "service.start-type";

    /// <inheritdoc />
    public string Name => "Windows service start type";

    /// <inheritdoc />
    public string Description => "Changes when a non-essential Windows service starts. Only services with a documented recommendation can be changed; security services are never touched.";

    /// <inheritdoc />
    public OptimizationCategory Category => OptimizationCategory.Services;

    /// <inheritdoc />
    public RiskLevel RiskLevel => RiskLevel.Medium;

    /// <inheritdoc />
    public bool RequiresAdmin => true;

    /// <inheritdoc />
    public bool CanRollback => true;

    /// <inheritdoc />
    public bool RequiresRestart => false;

    /// <inheritdoc />
    public OsSupport SupportedOs => OsSupport.Windows10OrLater;

    /// <inheritdoc />
    public IReadOnlyList<RuleParameter> Parameters { get; } =
    [
        new("service", "Service name from the STORM OS knowledge base."),
        new("startType", "Target start type.", AllowedValues: ["automatic", "automatic-delayed", "manual", "disabled"]),
    ];

    /// <summary>Parses a start type parameter.</summary>
    /// <param name="value">Parameter value.</param>
    /// <returns>The start kind.</returns>
    public static ServiceStartKind ParseStartType(string value) => value switch
    {
        "automatic" => ServiceStartKind.Automatic,
        "automatic-delayed" => ServiceStartKind.AutomaticDelayed,
        "manual" => ServiceStartKind.Manual,
        "disabled" => ServiceStartKind.Disabled,
        _ => throw new ArgumentException("Unknown start type.", nameof(value)),
    };

    /// <inheritdoc />
    public Task<RuleDetection> DetectAsync(OptimizationContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var name = context.Require("service");
        var startType = context.Require("startType");
        var entry = knowledge.Find(name);
        if (entry is null || !entry.AllowedStartTypes.Contains(startType, StringComparer.Ordinal))
        {
            return Task.FromResult(new RuleDetection(DetectionState.NotApplicable, "-", startType, entry?.Recommendation ?? "STORM OS does not change this service."));
        }

        var current = services.GetStartKind(name);
        if (current is null)
        {
            return Task.FromResult(new RuleDetection(DetectionState.NotApplicable, "Not installed", startType, "The service is not installed on this PC."));
        }

        var target = ParseStartType(startType);
        return Task.FromResult(new RuleDetection(current == target ? DetectionState.AlreadyApplied : DetectionState.Applicable, current.Value.ToString(), target.ToString(), entry.Recommendation));
    }

    /// <inheritdoc />
    public Task<RuleSnapshot> CaptureAsync(OptimizationContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var current = services.GetStartKind(context.Require("service")) ?? throw new InvalidOperationException("The service is not installed.");
        return Task.FromResult(new RuleSnapshot { RuleId = Id, CapturedAt = DateTimeOffset.UtcNow, Description = current.ToString(), Values = new Dictionary<string, string?> { ["startKind"] = current.ToString() } });
    }

    /// <inheritdoc />
    public Task ApplyAsync(OptimizationContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        services.SetStartKind(context.Require("service"), ParseStartType(context.Require("startType")));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<RuleVerification> VerifyAsync(OptimizationContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var current = services.GetStartKind(context.Require("service"));
        return Task.FromResult(new RuleVerification(current == ParseStartType(context.Require("startType")), current?.ToString() ?? "-"));
    }

    /// <inheritdoc />
    public Task<RuleVerification> RollbackAsync(OptimizationContext context, RuleSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(snapshot);
        var original = Enum.Parse<ServiceStartKind>(snapshot.Get("startKind") ?? throw new ArgumentException("Snapshot is incomplete."));
        var name = context.Require("service");
        services.SetStartKind(name, original);
        var current = services.GetStartKind(name);
        return Task.FromResult(new RuleVerification(current == original, current?.ToString() ?? "-"));
    }
}
