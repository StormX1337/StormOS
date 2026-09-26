using System.Text.Json;
using StormOS.Core.Common;
using StormOS.Core.Optimization;

namespace StormOS.Optimization.Rules.Services;

/// <summary>A curated assessment of a Windows service.</summary>
public sealed record ServiceKnowledge
{
    /// <summary>Gets the service name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Gets what the service does.</summary>
    public string Assessment { get; init; } = string.Empty;

    /// <summary>Gets the recommendation.</summary>
    public string Recommendation { get; init; } = string.Empty;

    /// <summary>Gets the risk of changing the service.</summary>
    public RiskLevel Risk { get; init; }

    /// <summary>Gets the start types STORM OS may set; empty means the service is never changed.</summary>
    public IReadOnlyList<string> AllowedStartTypes { get; init; } = [];
}

/// <summary>
/// The allow-list of services STORM OS may reconfigure. Services not listed here are shown read-only; STORM OS
/// never disables services wholesale.
/// </summary>
public sealed class ServiceKnowledgeBase
{
    private readonly Dictionary<string, ServiceKnowledge> _entries;

    private ServiceKnowledgeBase(IEnumerable<ServiceKnowledge> entries) =>
        _entries = entries.ToDictionary(e => e.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets the embedded knowledge base.</summary>
    public static ServiceKnowledgeBase Default { get; } = LoadEmbedded();

    /// <summary>Gets all entries.</summary>
    public IReadOnlyCollection<ServiceKnowledge> Entries => _entries.Values;

    /// <summary>Looks up a service.</summary>
    /// <param name="serviceName">Service name.</param>
    /// <returns>The entry or <see langword="null"/>.</returns>
    public ServiceKnowledge? Find(string serviceName) => _entries.GetValueOrDefault(serviceName);

    /// <summary>Parses a knowledge base document.</summary>
    /// <param name="json">JSON.</param>
    /// <returns>The knowledge base.</returns>
    public static ServiceKnowledgeBase Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var entries = document.RootElement.GetProperty("services").Deserialize<List<ServiceKnowledge>>(StormJson.Lenient) ?? [];
        return new ServiceKnowledgeBase(entries);
    }

    private static ServiceKnowledgeBase LoadEmbedded()
    {
        using var stream = typeof(ServiceKnowledgeBase).Assembly.GetManifestResourceStream("StormOS.Optimization.services.json")
            ?? throw new InvalidOperationException("The embedded service knowledge base is missing.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }
}
