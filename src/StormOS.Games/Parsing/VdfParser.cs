using System.Text;

namespace StormOS.Games.Parsing;

/// <summary>A node of a Valve KeyValues (VDF/ACF) document.</summary>
public sealed class VdfNode
{
    private readonly Dictionary<string, object> _children = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets the child keys in document order.</summary>
    public IReadOnlyCollection<string> Keys => _children.Keys;

    /// <summary>Gets a string value.</summary>
    /// <param name="key">Key.</param>
    /// <returns>The value or <see langword="null"/>.</returns>
    public string? Value(string key) => _children.TryGetValue(key, out var value) ? value as string : null;

    /// <summary>Gets a child node.</summary>
    /// <param name="key">Key.</param>
    /// <returns>The node or <see langword="null"/>.</returns>
    public VdfNode? Node(string key) => _children.TryGetValue(key, out var value) ? value as VdfNode : null;

    /// <summary>Enumerates child nodes.</summary>
    /// <returns>Key/node pairs.</returns>
    public IEnumerable<KeyValuePair<string, VdfNode>> Nodes() =>
        _children.Where(c => c.Value is VdfNode).Select(c => new KeyValuePair<string, VdfNode>(c.Key, (VdfNode)c.Value));

    internal void Set(string key, object value) => _children[key] = value;
}

/// <summary>Parser for Valve's text KeyValues format used by libraryfolders.vdf and appmanifest_*.acf.</summary>
public static class VdfParser
{
    /// <summary>Maximum nesting depth accepted.</summary>
    public const int MaxDepth = 32;

    /// <summary>Parses a document.</summary>
    /// <param name="text">Document text.</param>
    /// <returns>The root node (whose children are the top-level keys).</returns>
    /// <exception cref="FormatException">The document is malformed.</exception>
    public static VdfNode Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var position = 0;
        var root = new VdfNode();
        ParseBody(text, ref position, root, depth: 0, expectClose: false);
        return root;
    }

    private static void ParseBody(string text, ref int position, VdfNode node, int depth, bool expectClose)
    {
        if (depth > MaxDepth)
        {
            throw new FormatException("VDF nesting is too deep.");
        }

        while (true)
        {
            var token = NextToken(text, ref position);
            if (token is null)
            {
                if (expectClose)
                {
                    throw new FormatException("Unexpected end of VDF document.");
                }

                return;
            }

            if (token.Value.Kind == TokenKind.Close)
            {
                if (!expectClose)
                {
                    throw new FormatException("Unexpected '}' in VDF document.");
                }

                return;
            }

            if (token.Value.Kind != TokenKind.String)
            {
                throw new FormatException("Expected a key in VDF document.");
            }

            var key = token.Value.Text;
            var value = NextToken(text, ref position) ?? throw new FormatException($"Missing value for '{key}'.");
            switch (value.Kind)
            {
                case TokenKind.String:
                    node.Set(key, value.Text);
                    break;
                case TokenKind.Open:
                    var child = new VdfNode();
                    ParseBody(text, ref position, child, depth + 1, expectClose: true);
                    node.Set(key, child);
                    break;
                default:
                    throw new FormatException($"Unexpected '}}' after key '{key}'.");
            }
        }
    }

    private static Token? NextToken(string text, ref int position)
    {
        while (position < text.Length)
        {
            var c = text[position];
            if (char.IsWhiteSpace(c))
            {
                position++;
                continue;
            }

            if (c == '/' && position + 1 < text.Length && text[position + 1] == '/')
            {
                while (position < text.Length && text[position] != '\n')
                {
                    position++;
                }

                continue;
            }

            if (c == '{')
            {
                position++;
                return new Token(TokenKind.Open, "{");
            }

            if (c == '}')
            {
                position++;
                return new Token(TokenKind.Close, "}");
            }

            if (c == '[')
            {
                // Conditional suffixes such as [$WIN32] are ignored.
                while (position < text.Length && text[position] != ']')
                {
                    position++;
                }

                position++;
                continue;
            }

            return new Token(TokenKind.String, c == '"' ? ReadQuoted(text, ref position) : ReadBare(text, ref position));
        }

        return null;
    }

    private static string ReadQuoted(string text, ref int position)
    {
        position++;
        var builder = new StringBuilder();
        while (position < text.Length)
        {
            var c = text[position++];
            if (c == '"')
            {
                return builder.ToString();
            }

            if (c == '\\' && position < text.Length)
            {
                var escaped = text[position++];
                builder.Append(escaped switch { 'n' => '\n', 't' => '\t', '\\' => '\\', '"' => '"', _ => escaped });
                continue;
            }

            builder.Append(c);
        }

        throw new FormatException("Unterminated string in VDF document.");
    }

    private static string ReadBare(string text, ref int position)
    {
        var start = position;
        while (position < text.Length && !char.IsWhiteSpace(text[position]) && text[position] is not ('{' or '}' or '"'))
        {
            position++;
        }

        return text[start..position];
    }

    private enum TokenKind
    {
        String,
        Open,
        Close,
    }

    private readonly record struct Token(TokenKind Kind, string Text);
}
