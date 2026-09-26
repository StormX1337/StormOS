using System.Text.RegularExpressions;

namespace StormOS.Security.Secrets;

/// <summary>Removes secrets from text before it is written to logs.</summary>
public static partial class SecretRedactor
{
    /// <summary>Replacement text for redacted values.</summary>
    public const string Mask = "***";

    private static readonly string[] SensitiveKeys =
    [
        "password", "passwd", "pwd", "secret", "token", "apikey", "api_key", "authorization", "cookie",
        "refreshtoken", "accesstoken", "clientsecret", "privatekey", "license",
    ];

    /// <summary>Determines whether a property name refers to a secret.</summary>
    /// <param name="name">Property name.</param>
    /// <returns><see langword="true"/> when the value must be masked.</returns>
    public static bool IsSensitiveName(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        var normalized = name.Replace("-", string.Empty, StringComparison.Ordinal).Replace("_", string.Empty, StringComparison.Ordinal);
        foreach (var key in SensitiveKeys)
        {
            if (normalized.Contains(key.Replace("_", string.Empty, StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Masks bearer tokens, JWTs and key=value secrets inside free text.</summary>
    /// <param name="text">Text to redact.</param>
    /// <returns>Redacted text.</returns>
    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        var result = BearerRegex().Replace(text, "Bearer " + Mask);
        result = JwtRegex().Replace(result, Mask);
        result = KeyValueRegex().Replace(result, m => m.Groups["key"].Value + m.Groups["sep"].Value + Mask);
        return result;
    }

    [GeneratedRegex(@"Bearer\s+[A-Za-z0-9\-._~+/]+=*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BearerRegex();

    [GeneratedRegex(@"eyJ[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]+", RegexOptions.CultureInvariant)]
    private static partial Regex JwtRegex();

    [GeneratedRegex(@"(?<key>(password|passwd|pwd|secret|token|api[_-]?key|client[_-]?secret)\w*)(?<sep>\s*[=:]\s*)(""[^""]*""|'[^']*'|[^\s,;&]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex KeyValueRegex();
}
