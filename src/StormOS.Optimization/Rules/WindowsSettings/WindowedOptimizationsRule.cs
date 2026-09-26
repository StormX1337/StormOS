using StormOS.Core.Optimization;
using StormOS.Windows.RegistryAccess;

namespace StormOS.Optimization.Rules.WindowsSettings;

/// <summary>Enables "Optimizations for windowed games" (flip-model presentation for DX10/11 windowed games, Windows 11 22H2+).</summary>
public sealed class WindowedOptimizationsRule(IRegistryAccess registry) : IOptimizationRule
{
    private const string Key = @"Software\Microsoft\DirectX\UserGpuPreferences";
    private const string ValueName = "DirectXUserGlobalSettings";
    private const string Setting = "SwapEffectUpgradeEnable";

    /// <inheritdoc />
    public string Id => "windows.windowed-optimizations";

    /// <inheritdoc />
    public string Name => "Optimizations for windowed games";

    /// <inheritdoc />
    public string Description => "Upgrades DirectX 10/11 games running in windowed or borderless mode to the flip presentation model, reducing latency and enabling VRR and Auto HDR. Games with their own exclusive fullscreen are unaffected.";

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
    public OsSupport SupportedOs => new(22621);

    /// <inheritdoc />
    public IReadOnlyList<RuleParameter> Parameters => [];

    /// <inheritdoc />
    public Task<RuleDetection> DetectAsync(OptimizationContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var enabled = IsEnabled(context.WindowsBuild);
        return Task.FromResult(new RuleDetection(enabled ? DetectionState.AlreadyApplied : DetectionState.Applicable, enabled ? "On" : "Off", "On", enabled ? "Already enabled." : Description));
    }

    /// <inheritdoc />
    public Task<RuleSnapshot> CaptureAsync(OptimizationContext context, CancellationToken cancellationToken = default)
    {
        var current = registry.GetValue(RegistryHive.CurrentUser, Key, ValueName)?.Value as string;
        return Task.FromResult(new RuleSnapshot
        {
            RuleId = Id,
            CapturedAt = DateTimeOffset.UtcNow,
            Description = current ?? "(not set)",
            Values = new Dictionary<string, string?> { ["exists"] = current is null ? "false" : "true", ["value"] = current },
        });
    }

    /// <inheritdoc />
    public Task ApplyAsync(OptimizationContext context, CancellationToken cancellationToken = default)
    {
        var current = registry.GetValue(RegistryHive.CurrentUser, Key, ValueName)?.Value as string;
        registry.SetValue(RegistryHive.CurrentUser, Key, ValueName, new RegistryValue(RegistryKind.Sz, DirectXGlobalSettings.Set(current, Setting, "1")));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<RuleVerification> VerifyAsync(OptimizationContext context, CancellationToken cancellationToken = default)
    {
        var value = DirectXGlobalSettings.Get(registry.GetValue(RegistryHive.CurrentUser, Key, ValueName)?.Value as string, Setting);
        return Task.FromResult(new RuleVerification(value == "1", value == "1" ? "On" : "Off"));
    }

    /// <inheritdoc />
    public Task<RuleVerification> RollbackAsync(OptimizationContext context, RuleSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Get("exists") == "true" && snapshot.Get("value") is { } original)
        {
            registry.SetValue(RegistryHive.CurrentUser, Key, ValueName, new RegistryValue(RegistryKind.Sz, original));
        }
        else
        {
            registry.DeleteValue(RegistryHive.CurrentUser, Key, ValueName);
        }

        var restored = registry.GetValue(RegistryHive.CurrentUser, Key, ValueName)?.Value as string;
        return Task.FromResult(new RuleVerification(restored == snapshot.Get("value"), restored ?? "(not set)"));
    }

    private bool IsEnabled(int build)
    {
        var value = DirectXGlobalSettings.Get(registry.GetValue(RegistryHive.CurrentUser, Key, ValueName)?.Value as string, Setting);
        // Windows 11 24H2 (build 26100) turns the feature on by default when the key is absent.
        return value is null ? build >= 26100 : value == "1";
    }
}
