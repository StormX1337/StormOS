using System.Globalization;
using StormOS.Core.Optimization;
using StormOS.Windows.Platform;

namespace StormOS.Optimization.Rules.WindowsSettings;

/// <summary>Turns off "Enhance pointer precision" (mouse acceleration) for consistent aiming.</summary>
public sealed class MousePrecisionRule(IMouseSettings mouse) : IOptimizationRule
{
    /// <inheritdoc />
    public string Id => "input.mouse-precision";

    /// <inheritdoc />
    public string Name => "Enhance pointer precision (mouse acceleration)";

    /// <inheritdoc />
    public string Description => "Turns off Windows mouse acceleration so the cursor moves exactly with your hand. Most competitive games use raw input in-game; this affects the desktop and games that use the Windows cursor.";

    /// <inheritdoc />
    public OptimizationCategory Category => OptimizationCategory.WindowsSettings;

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
    public IReadOnlyList<RuleParameter> Parameters => [];

    /// <inheritdoc />
    public Task<RuleDetection> DetectAsync(OptimizationContext context, CancellationToken cancellationToken = default)
    {
        var current = mouse.Read();
        return Task.FromResult(new RuleDetection(current.EnhancePointerPrecision ? DetectionState.Applicable : DetectionState.AlreadyApplied, Describe(current), "Off", current.EnhancePointerPrecision ? Description : "Mouse acceleration is already off."));
    }

    /// <inheritdoc />
    public Task<RuleSnapshot> CaptureAsync(OptimizationContext context, CancellationToken cancellationToken = default)
    {
        var current = mouse.Read();
        return Task.FromResult(new RuleSnapshot
        {
            RuleId = Id,
            CapturedAt = DateTimeOffset.UtcNow,
            Description = Describe(current),
            Values = new Dictionary<string, string?>
            {
                ["threshold1"] = current.Threshold1.ToString(CultureInfo.InvariantCulture),
                ["threshold2"] = current.Threshold2.ToString(CultureInfo.InvariantCulture),
                ["acceleration"] = current.Acceleration.ToString(CultureInfo.InvariantCulture),
            },
        });
    }

    /// <inheritdoc />
    public Task ApplyAsync(OptimizationContext context, CancellationToken cancellationToken = default)
    {
        mouse.Write(new MouseParameters(0, 0, 0));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<RuleVerification> VerifyAsync(OptimizationContext context, CancellationToken cancellationToken = default)
    {
        var current = mouse.Read();
        return Task.FromResult(new RuleVerification(!current.EnhancePointerPrecision, Describe(current)));
    }

    /// <inheritdoc />
    public Task<RuleVerification> RollbackAsync(OptimizationContext context, RuleSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var original = new MouseParameters(
            int.Parse(snapshot.Get("threshold1") ?? "6", CultureInfo.InvariantCulture),
            int.Parse(snapshot.Get("threshold2") ?? "10", CultureInfo.InvariantCulture),
            int.Parse(snapshot.Get("acceleration") ?? "1", CultureInfo.InvariantCulture));
        mouse.Write(original);
        var current = mouse.Read();
        return Task.FromResult(new RuleVerification(current == original, Describe(current)));
    }

    private static string Describe(MouseParameters parameters) => parameters.EnhancePointerPrecision ? "On" : "Off";
}
