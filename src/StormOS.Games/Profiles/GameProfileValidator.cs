using System.Text.RegularExpressions;
using StormOS.Core.Games;
using StormOS.Security.Validation;

namespace StormOS.Games.Profiles;

/// <summary>Validates game profiles before they are used. Invalid profiles are rejected, never partially applied.</summary>
public static partial class GameProfileValidator
{
    /// <summary>Validates a profile.</summary>
    /// <param name="profile">The profile.</param>
    /// <returns>Validation errors; empty when valid.</returns>
    public static IReadOnlyList<string> Validate(GameProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var errors = new List<string>();
        if (profile.SchemaVersion != GameProfile.CurrentSchemaVersion)
        {
            errors.Add($"Unsupported schemaVersion {profile.SchemaVersion}; this build understands {GameProfile.CurrentSchemaVersion}.");
        }

        if (!InputValidator.IsIdentifier(profile.Id))
        {
            errors.Add("id must be a lowercase identifier (a-z, 0-9, '.', '-', '_').");
        }

        if (!SemVerRegex().IsMatch(profile.Version ?? string.Empty))
        {
            errors.Add("version must be a semantic version such as 1.2.0.");
        }

        if (string.IsNullOrWhiteSpace(profile.Name) || profile.Name.Length > 128)
        {
            errors.Add("name is required (max 128 characters).");
        }

        if (profile.Detection.Executables.Count == 0 && profile.Detection.Launchers.Count == 0)
        {
            errors.Add("detection needs at least one executable or launcher match.");
        }

        foreach (var exe in profile.Detection.Executables)
        {
            if (string.IsNullOrWhiteSpace(exe.Name) || exe.Name.Length > 128 || exe.Name.IndexOfAny(['\\', '/', ':', '"', '<', '>', '|']) >= 0)
            {
                errors.Add($"Invalid executable pattern '{exe.Name}'.");
            }
        }

        foreach (var reference in profile.SystemRecommendations.Concat(profile.OptimizationRules))
        {
            if (!InputValidator.IsIdentifier(reference.RuleId))
            {
                errors.Add($"Invalid rule id '{reference.RuleId}'.");
            }

            foreach (var (key, value) in reference.Parameters)
            {
                if (!InputValidator.IsParameterName(key) || !InputValidator.IsSafeParameterValue(value))
                {
                    errors.Add($"Invalid parameter '{key}' for rule '{reference.RuleId}'.");
                }
            }
        }

        if (profile.Launch?.Uri is { } uri && !GameLauncher.IsAllowedUri(uri))
        {
            errors.Add("launch.uri uses a scheme that is not allowed.");
        }

        if (profile.Benchmark is { } benchmark && (benchmark.DurationSeconds is < 10 or > 600 || benchmark.WarmupSeconds is < 0 or > 120))
        {
            errors.Add("benchmark durations are out of range (10–600 s, warm-up 0–120 s).");
        }

        return errors;
    }

    [GeneratedRegex("^\\d+\\.\\d+\\.\\d+(-[0-9A-Za-z.-]+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex SemVerRegex();
}
