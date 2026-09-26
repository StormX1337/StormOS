using StormOS.Core.Optimization;
using StormOS.Security.Validation;

namespace StormOS.Optimization.Engine;

/// <summary>Validates rule parameters against the rule's declaration before any rule code runs.</summary>
public static class ParameterValidation
{
    /// <summary>Validates parameters.</summary>
    /// <param name="rule">The rule.</param>
    /// <param name="parameters">Supplied parameters.</param>
    /// <returns><see langword="null"/> when valid, otherwise a user-safe message.</returns>
    public static string? Validate(IOptimizationRule rule, IReadOnlyDictionary<string, string>? parameters)
    {
        ArgumentNullException.ThrowIfNull(rule);
        parameters ??= new Dictionary<string, string>();
        if (parameters.Count > 16)
        {
            return "Too many parameters.";
        }

        foreach (var (name, value) in parameters)
        {
            var declared = rule.Parameters.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal));
            if (declared is null)
            {
                return $"Unknown parameter '{name}' for {rule.Name}.";
            }

            if (!InputValidator.IsParameterName(name) || !InputValidator.IsSafeParameterValue(value))
            {
                return $"Invalid value for '{name}'.";
            }

            if (declared.AllowedValues is { Count: > 0 } allowed && !allowed.Contains(value, StringComparer.Ordinal))
            {
                return $"'{value}' is not an allowed value for '{name}'.";
            }
        }

        var missing = rule.Parameters.FirstOrDefault(p => p.Required && (!parameters.TryGetValue(p.Name, out var v) || string.IsNullOrWhiteSpace(v)));
        return missing is null ? null : $"Missing required parameter '{missing.Name}'.";
    }
}
