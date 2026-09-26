using StormOS.Core.Optimization;
using StormOS.Security.Validation;

namespace StormOS.Optimization.Rules.Network;

/// <summary>Sets custom IPv4 DNS servers on one interface (opt-in, reversible back to the previous servers or DHCP).</summary>
public sealed class DnsServersRule(IDnsConfigurator dns) : IOptimizationRule
{
    /// <inheritdoc />
    public string Id => "network.dns";

    /// <inheritdoc />
    public string Name => "DNS servers";

    /// <inheritdoc />
    public string Description => "Uses the selected DNS servers for this network adapter. Faster DNS shortens connection setup (store, launcher, matchmaking lookups); it does not change in-game ping.";

    /// <inheritdoc />
    public OptimizationCategory Category => OptimizationCategory.Network;

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
        new("interfaceId", "Network adapter GUID."),
        new("servers", "One or two IPv4 DNS server addresses, comma separated."),
    ];

    /// <summary>Parses and validates the servers parameter (1–2 unicast IPv4 addresses).</summary>
    /// <param name="value">Parameter value.</param>
    /// <returns>The servers.</returns>
    /// <exception cref="ArgumentException">The value is invalid.</exception>
    public static IReadOnlyList<string> ParseServers(string value)
    {
        var parts = (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length is < 1 or > 2)
        {
            throw new ArgumentException("Specify one or two DNS servers.");
        }

        var result = new List<string>();
        foreach (var part in parts)
        {
            if (!InputValidator.TryParseDnsServer(part, out var address) || address!.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            {
                throw new ArgumentException($"'{part}' is not a valid IPv4 DNS server.");
            }

            result.Add(address.ToString());
        }

        return result;
    }

    /// <inheritdoc />
    public Task<RuleDetection> DetectAsync(OptimizationContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var target = ParseServers(context.Require("servers"));
        var current = dns.Read(context.Require("interfaceId"));
        if (current is null)
        {
            return Task.FromResult(new RuleDetection(DetectionState.NotApplicable, "-", string.Join(", ", target), "The network adapter was not found."));
        }

        var applied = current.IsStatic && current.Servers.SequenceEqual(target);
        return Task.FromResult(new RuleDetection(applied ? DetectionState.AlreadyApplied : DetectionState.Applicable, Describe(current), string.Join(", ", target), Description));
    }

    /// <inheritdoc />
    public Task<RuleSnapshot> CaptureAsync(OptimizationContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var current = dns.Read(context.Require("interfaceId")) ?? throw new InvalidOperationException("The network adapter was not found.");
        return Task.FromResult(new RuleSnapshot { RuleId = Id, CapturedAt = DateTimeOffset.UtcNow, Description = Describe(current), Values = new Dictionary<string, string?> { ["servers"] = string.Join(',', current.Servers) } });
    }

    /// <inheritdoc />
    public Task ApplyAsync(OptimizationContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        dns.Write(context.Require("interfaceId"), ParseServers(context.Require("servers")));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<RuleVerification> VerifyAsync(OptimizationContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var current = dns.Read(context.Require("interfaceId"));
        var target = ParseServers(context.Require("servers"));
        return Task.FromResult(new RuleVerification(current is { IsStatic: true } && current.Servers.SequenceEqual(target), current is null ? "-" : Describe(current)));
    }

    /// <inheritdoc />
    public Task<RuleVerification> RollbackAsync(OptimizationContext context, RuleSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(snapshot);
        var interfaceId = context.Require("interfaceId");
        var original = (snapshot.Get("servers") ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries);
        dns.Write(interfaceId, original);
        var current = dns.Read(interfaceId);
        return Task.FromResult(new RuleVerification(current is not null && current.Servers.SequenceEqual(original), current is null ? "-" : Describe(current)));
    }

    private static string Describe(DnsConfiguration configuration) =>
        configuration.IsStatic ? string.Join(", ", configuration.Servers) : "Automatic (DHCP)";
}
