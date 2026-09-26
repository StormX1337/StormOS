namespace StormOS.Core.Licensing;

/// <summary>Commercial tiers.</summary>
public enum LicenseTier
{
    /// <summary>Storm OS Free.</summary>
    Free,

    /// <summary>Storm OS Pro.</summary>
    Pro,

    /// <summary>Storm OS Ultimate.</summary>
    Ultimate,
}

/// <summary>Feature keys. The mapping from tier to features is defined server side (see cloud/apps/api).</summary>
public static class Features
{
    /// <summary>Live monitoring.</summary>
    public const string Monitoring = "monitoring";

    /// <summary>Basic game detection.</summary>
    public const string GameDetection = "game-detection";

    /// <summary>Basic profiles.</summary>
    public const string BasicProfiles = "profiles.basic";

    /// <summary>Advanced optimization.</summary>
    public const string AdvancedOptimization = "optimization.advanced";

    /// <summary>Benchmarks.</summary>
    public const string Benchmark = "benchmark";

    /// <summary>In-game overlay.</summary>
    public const string Overlay = "overlay";

    /// <summary>Network diagnostics.</summary>
    public const string NetworkDiagnostics = "network.diagnostics";

    /// <summary>AI analysis.</summary>
    public const string AiAnalysis = "ai.analysis";

    /// <summary>Cloud sync.</summary>
    public const string CloudSync = "cloud.sync";

    /// <summary>Advanced history.</summary>
    public const string AdvancedHistory = "history.advanced";

    /// <summary>Advanced profiles.</summary>
    public const string AdvancedProfiles = "profiles.advanced";

    /// <summary>Gets every known feature.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        Monitoring, GameDetection, BasicProfiles, AdvancedOptimization, Benchmark, Overlay,
        NetworkDiagnostics, AiAnalysis, CloudSync, AdvancedHistory, AdvancedProfiles,
    ];
}

/// <summary>How entitlements are resolved.</summary>
public enum LicensingMode
{
    /// <summary>All local features enabled (community/developer builds and offline use without a commercial license).</summary>
    Community,

    /// <summary>Features are gated by a signed entitlement token issued by STORM Cloud.</summary>
    Commercial,
}

/// <summary>The features the current installation is entitled to.</summary>
public sealed record Entitlements
{
    /// <summary>Gets the tier.</summary>
    public LicenseTier Tier { get; init; }

    /// <summary>Gets the enabled features.</summary>
    public IReadOnlySet<string> Features { get; init; } = new HashSet<string>();

    /// <summary>Gets the expiry time of the entitlement token.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>Gets where the entitlements came from.</summary>
    public string Source { get; init; } = "local";

    /// <summary>Gets a value indicating whether the feature is enabled.</summary>
    /// <param name="feature">Feature key.</param>
    /// <returns><see langword="true"/> when enabled.</returns>
    public bool Has(string feature) => Features.Contains(feature);

    /// <summary>Gets entitlements that unlock every local feature (community mode).</summary>
    public static Entitlements Community { get; } = new()
    {
        Tier = LicenseTier.Ultimate,
        Features = new HashSet<string>(Licensing.Features.All.Where(f => f != Licensing.Features.CloudSync), StringComparer.Ordinal),
        Source = "community",
    };

    /// <summary>Gets the offline free tier.</summary>
    public static Entitlements Free { get; } = new()
    {
        Tier = LicenseTier.Free,
        Features = new HashSet<string>([Licensing.Features.Monitoring, Licensing.Features.GameDetection, Licensing.Features.BasicProfiles], StringComparer.Ordinal),
        Source = "free",
    };
}

/// <summary>Resolves the current entitlements.</summary>
public interface IEntitlementService
{
    /// <summary>Gets the current entitlements.</summary>
    Entitlements Current { get; }

    /// <summary>Checks whether a feature is enabled.</summary>
    /// <param name="feature">Feature key.</param>
    /// <returns><see langword="true"/> when enabled.</returns>
    bool IsEnabled(string feature);
}
