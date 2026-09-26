namespace StormOS.Infrastructure.Logging;

/// <summary>Maps logger source contexts to the documented log categories.</summary>
public static class LogCategories
{
    /// <summary>Application log.</summary>
    public const string Application = "Application";

    /// <summary>Service log.</summary>
    public const string Service = "Service";

    /// <summary>Optimization log.</summary>
    public const string Optimization = "Optimization";

    /// <summary>Hardware and performance log.</summary>
    public const string Hardware = "Hardware";

    /// <summary>Network log.</summary>
    public const string Network = "Network";

    /// <summary>Benchmark log.</summary>
    public const string Benchmark = "Benchmark";

    /// <summary>Security and IPC audit log.</summary>
    public const string Security = "Security";

    /// <summary>Gets the dedicated categories that receive their own file in addition to the main log.</summary>
    public static IReadOnlyList<string> Dedicated { get; } = [Optimization, Hardware, Network, Benchmark, Security];

    /// <summary>Resolves the category of a source context (logger name).</summary>
    /// <param name="sourceContext">The logger name, usually a type name.</param>
    /// <param name="mainCategory">Category of the main log for this process.</param>
    /// <returns>The category.</returns>
    public static string Resolve(string? sourceContext, string mainCategory)
    {
        if (string.IsNullOrEmpty(sourceContext))
        {
            return mainCategory;
        }

        if (sourceContext.StartsWith("StormOS.Optimization", StringComparison.Ordinal))
        {
            return Optimization;
        }

        if (sourceContext.StartsWith("StormOS.Hardware", StringComparison.Ordinal) || sourceContext.StartsWith("StormOS.Performance", StringComparison.Ordinal))
        {
            return Hardware;
        }

        if (sourceContext.StartsWith("StormOS.Network", StringComparison.Ordinal))
        {
            return Network;
        }

        if (sourceContext.StartsWith("StormOS.Benchmark", StringComparison.Ordinal))
        {
            return Benchmark;
        }

        if (sourceContext.StartsWith("StormOS.Security", StringComparison.Ordinal) || sourceContext.Contains(".Audit", StringComparison.Ordinal))
        {
            return Security;
        }

        return mainCategory;
    }
}
