using System.Globalization;
using StormOS.Core.Optimization;
using StormOS.Windows.RegistryAccess;

namespace StormOS.Optimization.Rules.Registry;

/// <summary>A registry value managed by a rule.</summary>
/// <param name="Hive">Hive.</param>
/// <param name="KeyPath">Key path.</param>
/// <param name="ValueName">Value name.</param>
/// <param name="Desired">Target value.</param>
/// <param name="DefaultWhenMissing">Effective Windows default when the value does not exist, if known.</param>
public sealed record RegistryTarget(RegistryHive Hive, string KeyPath, string ValueName, RegistryValue Desired, RegistryValue? DefaultWhenMissing = null);

/// <summary>
/// Base class for rules that set one or more registry values. Snapshots record whether each value existed and
/// its exact kind and data; rollback restores the value or deletes it if it did not exist before.
/// </summary>
public abstract class RegistryValueRule : IOptimizationRule
{
    private readonly IRegistryAccess _registry;

    /// <summary>Initializes a new instance of the <see cref="RegistryValueRule"/> class.</summary>
    /// <param name="registry">Registry access.</param>
    protected RegistryValueRule(IRegistryAccess registry) => _registry = registry;

    /// <inheritdoc />
    public abstract string Id { get; }

    /// <inheritdoc />
    public abstract string Name { get; }

    /// <inheritdoc />
    public abstract string Description { get; }

    /// <inheritdoc />
    public abstract OptimizationCategory Category { get; }

    /// <inheritdoc />
    public abstract RiskLevel RiskLevel { get; }

    /// <inheritdoc />
    public bool RequiresAdmin => Targets.Any(t => t.Hive == RegistryHive.LocalMachine);

    /// <inheritdoc />
    public bool CanRollback => true;

    /// <inheritdoc />
    public virtual bool RequiresRestart => false;

    /// <inheritdoc />
    public virtual OsSupport SupportedOs => OsSupport.Windows10OrLater;

    /// <inheritdoc />
    public IReadOnlyList<RuleParameter> Parameters => [];

    /// <summary>Gets the managed registry values.</summary>
    protected abstract IReadOnlyList<RegistryTarget> Targets { get; }

    /// <summary>Gets the human readable state when the rule is applied.</summary>
    protected abstract string AppliedState { get; }

    /// <summary>Gets the human readable state when the rule is not applied.</summary>
    protected abstract string NotAppliedState { get; }

    /// <inheritdoc />
    public Task<RuleDetection> DetectAsync(OptimizationContext context, CancellationToken cancellationToken = default)
    {
        var applied = IsApplied();
        return Task.FromResult(new RuleDetection(
            applied ? DetectionState.AlreadyApplied : DetectionState.Applicable,
            applied ? AppliedState : NotAppliedState,
            AppliedState,
            applied ? "Already configured." : Description));
    }

    /// <inheritdoc />
    public Task<RuleSnapshot> CaptureAsync(OptimizationContext context, CancellationToken cancellationToken = default)
    {
        var values = new Dictionary<string, string?>();
        for (var i = 0; i < Targets.Count; i++)
        {
            var target = Targets[i];
            var current = _registry.GetValue(target.Hive, target.KeyPath, target.ValueName);
            var prefix = "t" + i.ToString(CultureInfo.InvariantCulture);
            values[prefix + ".exists"] = current is null ? "false" : "true";
            values[prefix + ".kind"] = current?.Kind.ToString();
            values[prefix + ".data"] = current?.Serialize();
        }

        return Task.FromResult(new RuleSnapshot { RuleId = Id, CapturedAt = DateTimeOffset.UtcNow, Description = IsApplied() ? AppliedState : NotAppliedState, Values = values });
    }

    /// <inheritdoc />
    public Task ApplyAsync(OptimizationContext context, CancellationToken cancellationToken = default)
    {
        foreach (var target in Targets)
        {
            _registry.SetValue(target.Hive, target.KeyPath, target.ValueName, target.Desired);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<RuleVerification> VerifyAsync(OptimizationContext context, CancellationToken cancellationToken = default)
    {
        var verified = Targets.All(t => Equals(_registry.GetValue(t.Hive, t.KeyPath, t.ValueName), t.Desired));
        return Task.FromResult(new RuleVerification(verified, verified ? AppliedState : NotAppliedState, verified ? null : "The registry did not keep the new value."));
    }

    /// <inheritdoc />
    public Task<RuleVerification> RollbackAsync(OptimizationContext context, RuleSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var verified = true;
        for (var i = 0; i < Targets.Count; i++)
        {
            var target = Targets[i];
            var prefix = "t" + i.ToString(CultureInfo.InvariantCulture);
            if (snapshot.Get(prefix + ".exists") == "true" && Enum.TryParse<RegistryKind>(snapshot.Get(prefix + ".kind"), out var kind) && snapshot.Get(prefix + ".data") is { } data)
            {
                var original = RegistryValue.Deserialize(kind, data);
                _registry.SetValue(target.Hive, target.KeyPath, target.ValueName, original);
                verified &= Equals(_registry.GetValue(target.Hive, target.KeyPath, target.ValueName), original);
            }
            else
            {
                _registry.DeleteValue(target.Hive, target.KeyPath, target.ValueName);
                verified &= _registry.GetValue(target.Hive, target.KeyPath, target.ValueName) is null;
            }
        }

        return Task.FromResult(new RuleVerification(verified, IsApplied() ? AppliedState : NotAppliedState, verified ? null : "The original registry value could not be restored."));
    }

    private bool IsApplied() => Targets.All(t => Equals(_registry.GetValue(t.Hive, t.KeyPath, t.ValueName) ?? t.DefaultWhenMissing, t.Desired));

    /// <summary>Creates a DWORD value.</summary>
    /// <param name="value">Value.</param>
    /// <returns>The registry value.</returns>
    protected static RegistryValue DWord(int value) => new(RegistryKind.DWord, value);
}
