using Microsoft.Extensions.Configuration;

namespace StormOS.Infrastructure.Configuration;

/// <summary>Builds configuration from appsettings files and STORMOS_ environment variables.</summary>
public static class StormConfiguration
{
    /// <summary>Prefix for environment variables, for example STORMOS_Cloud__ApiBaseUrl.</summary>
    public const string EnvironmentPrefix = "STORMOS_";

    /// <summary>Gets the current environment name ("Production" unless STORMOS_ENVIRONMENT is set).</summary>
    public static string EnvironmentName =>
        Environment.GetEnvironmentVariable(EnvironmentPrefix + "ENVIRONMENT") is { Length: > 0 } env ? env : "Production";

    /// <summary>Gets a value indicating whether the current environment is Development.</summary>
    public static bool IsDevelopment => string.Equals(EnvironmentName, "Development", StringComparison.OrdinalIgnoreCase);

    /// <summary>Adds the STORM OS configuration sources to a builder.</summary>
    /// <param name="builder">The configuration builder.</param>
    /// <param name="basePath">Directory containing the appsettings files.</param>
    /// <returns>The builder.</returns>
    public static IConfigurationBuilder AddStormConfiguration(this IConfigurationBuilder builder, string basePath)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddJsonFile($"appsettings.{EnvironmentName}.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables(EnvironmentPrefix);
    }

    /// <summary>Builds a standalone configuration root.</summary>
    /// <param name="basePath">Directory containing the appsettings files.</param>
    /// <returns>The configuration.</returns>
    public static IConfigurationRoot Build(string basePath) =>
        new ConfigurationBuilder().AddStormConfiguration(basePath).Build();
}
