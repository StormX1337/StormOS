namespace StormOS.Optimization.Rules.WindowsSettings;

/// <summary>Reads and edits the "key=value;" list stored in DirectXUserGlobalSettings without touching other keys.</summary>
public static class DirectXGlobalSettings
{
    /// <summary>Reads a key.</summary>
    /// <param name="settings">Settings string.</param>
    /// <param name="key">Key.</param>
    /// <returns>The value or <see langword="null"/>.</returns>
    public static string? Get(string? settings, string key)
    {
        foreach (var (k, v) in Parse(settings))
        {
            if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
            {
                return v;
            }
        }

        return null;
    }

    /// <summary>Sets a key, preserving all other keys and their order.</summary>
    /// <param name="settings">Settings string.</param>
    /// <param name="key">Key.</param>
    /// <param name="value">Value.</param>
    /// <returns>The new settings string.</returns>
    public static string Set(string? settings, string key, string value)
    {
        var pairs = Parse(settings).ToList();
        var index = pairs.FindIndex(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            pairs[index] = (pairs[index].Key, value);
        }
        else
        {
            pairs.Add((key, value));
        }

        return string.Concat(pairs.Select(p => $"{p.Key}={p.Value};"));
    }

    private static IEnumerable<(string Key, string Value)> Parse(string? settings)
    {
        if (string.IsNullOrWhiteSpace(settings))
        {
            yield break;
        }

        foreach (var part in settings.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = part.IndexOf('=', StringComparison.Ordinal);
            if (eq > 0)
            {
                yield return (part[..eq], part[(eq + 1)..]);
            }
        }
    }
}
