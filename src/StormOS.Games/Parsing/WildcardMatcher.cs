namespace StormOS.Games.Parsing;

/// <summary>Case-insensitive wildcard matching supporting '*' and '?'.</summary>
public static class WildcardMatcher
{
    /// <summary>Tests whether <paramref name="text"/> matches <paramref name="pattern"/>.</summary>
    /// <param name="pattern">Pattern.</param>
    /// <param name="text">Text.</param>
    /// <returns><see langword="true"/> when matching.</returns>
    public static bool IsMatch(string pattern, string text)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(text);
        int p = 0, t = 0, star = -1, mark = 0;
        while (t < text.Length)
        {
            if (p < pattern.Length && (pattern[p] == '?' || char.ToUpperInvariant(pattern[p]) == char.ToUpperInvariant(text[t])))
            {
                p++;
                t++;
            }
            else if (p < pattern.Length && pattern[p] == '*')
            {
                star = p++;
                mark = t;
            }
            else if (star >= 0)
            {
                p = star + 1;
                t = ++mark;
            }
            else
            {
                return false;
            }
        }

        while (p < pattern.Length && pattern[p] == '*')
        {
            p++;
        }

        return p == pattern.Length;
    }
}
