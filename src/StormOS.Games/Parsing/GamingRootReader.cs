using System.Text;

namespace StormOS.Games.Parsing;

/// <summary>Reads the ".GamingRoot" marker the Xbox app writes to the root of drives that hold PC Game Pass games.</summary>
public static class GamingRootReader
{
    /// <summary>Parses the file content: "RGBX" magic, a 32-bit count and UTF-16 null-terminated relative paths.</summary>
    /// <param name="data">File content.</param>
    /// <returns>Relative folder paths.</returns>
    public static IReadOnlyList<string> Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 10 || data[0] != (byte)'R' || data[1] != (byte)'G' || data[2] != (byte)'B' || data[3] != (byte)'X')
        {
            return [];
        }

        var text = Encoding.Unicode.GetString(data[8..]);
        return text.Split('\0', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => p.Length is > 0 and < 260 && !p.Contains("..", StringComparison.Ordinal))
            .ToList();
    }
}
