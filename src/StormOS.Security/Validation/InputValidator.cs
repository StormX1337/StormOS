using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace StormOS.Security.Validation;

/// <summary>Strict validators for values that cross a trust boundary (IPC payloads, profiles, CLI input).</summary>
public static partial class InputValidator
{
    /// <summary>Maximum length of free-form identifiers.</summary>
    public const int MaxIdentifierLength = 128;

    /// <summary>Validates a rule / profile / game identifier (lowercase letters, digits, '.', '-', '_', ':').</summary>
    /// <param name="value">The value.</param>
    /// <returns><see langword="true"/> when valid.</returns>
    public static bool IsIdentifier(string? value) =>
        !string.IsNullOrEmpty(value) && value.Length <= MaxIdentifierLength && IdentifierRegex().IsMatch(value);

    /// <summary>Validates a parameter key.</summary>
    /// <param name="value">The value.</param>
    /// <returns><see langword="true"/> when valid.</returns>
    public static bool IsParameterName(string? value) =>
        !string.IsNullOrEmpty(value) && value.Length <= 64 && ParameterNameRegex().IsMatch(value);

    /// <summary>Validates a parameter value: printable, bounded length, no control characters.</summary>
    /// <param name="value">The value.</param>
    /// <returns><see langword="true"/> when valid.</returns>
    public static bool IsSafeParameterValue(string? value) =>
        value is not null && value.Length <= 512 && !value.Any(char.IsControl);

    /// <summary>Validates a process id.</summary>
    /// <param name="processId">The process id.</param>
    /// <returns><see langword="true"/> when the id is in a plausible range (system idle / system excluded).</returns>
    public static bool IsProcessId(int processId) => processId > 4;

    /// <summary>Validates a unicast IPv4 or IPv6 address usable as a DNS server.</summary>
    /// <param name="value">The value.</param>
    /// <param name="address">The parsed address.</param>
    /// <returns><see langword="true"/> when valid.</returns>
    public static bool TryParseDnsServer(string? value, out IPAddress? address)
    {
        address = null;
        if (string.IsNullOrWhiteSpace(value) || value.Length > 64 || !IPAddress.TryParse(value.Trim(), out var parsed))
        {
            return false;
        }

        if (parsed.AddressFamily is not (AddressFamily.InterNetwork or AddressFamily.InterNetworkV6))
        {
            return false;
        }

        if (IPAddress.Any.Equals(parsed) || IPAddress.IPv6Any.Equals(parsed) || IPAddress.Broadcast.Equals(parsed) || parsed.IsIPv6Multicast)
        {
            return false;
        }

        var bytes = parsed.GetAddressBytes();
        if (parsed.AddressFamily == AddressFamily.InterNetwork && bytes[0] >= 224)
        {
            return false;
        }

        address = parsed;
        return true;
    }

    /// <summary>Validates a DNS host name or IP literal.</summary>
    /// <param name="value">The value.</param>
    /// <returns><see langword="true"/> when valid.</returns>
    public static bool IsHost(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 253)
        {
            return false;
        }

        var kind = Uri.CheckHostName(value);
        return kind is UriHostNameType.Dns or UriHostNameType.IPv4 or UriHostNameType.IPv6;
    }

    /// <summary>Validates a "host:port" endpoint.</summary>
    /// <param name="value">The value.</param>
    /// <param name="host">The host.</param>
    /// <param name="port">The port.</param>
    /// <returns><see langword="true"/> when valid.</returns>
    public static bool TryParseEndpoint(string? value, out string host, out int port)
    {
        host = string.Empty;
        port = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var index = value.LastIndexOf(':');
        if (index <= 0 || index == value.Length - 1)
        {
            return false;
        }

        var hostPart = value[..index].Trim('[', ']');
        if (!IsHost(hostPart) || !int.TryParse(value[(index + 1)..], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var parsedPort) || parsedPort is < 1 or > 65535)
        {
            return false;
        }

        host = hostPart;
        port = parsedPort;
        return true;
    }

    /// <summary>Validates an absolute HTTPS URL.</summary>
    /// <param name="value">The value.</param>
    /// <returns><see langword="true"/> when valid.</returns>
    public static bool IsHttpsUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo);

    /// <summary>
    /// Validates that <paramref name="path"/> is an absolute path located inside one of <paramref name="allowedRoots"/>
    /// after normalization (prevents traversal with "..").
    /// </summary>
    /// <param name="path">The path.</param>
    /// <param name="allowedRoots">Allowed root directories.</param>
    /// <returns><see langword="true"/> when the path is inside an allowed root.</returns>
    public static bool IsPathWithin(string? path, IEnumerable<string> allowedRoots)
    {
        ArgumentNullException.ThrowIfNull(allowedRoots);
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || path.Contains('\0', StringComparison.Ordinal))
        {
            return false;
        }

        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (ArgumentException)
        {
            return false;
        }

        foreach (var root in allowedRoots)
        {
            var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
            if (full.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Validates a Windows service name.</summary>
    /// <param name="value">The value.</param>
    /// <returns><see langword="true"/> when valid.</returns>
    public static bool IsServiceName(string? value) =>
        !string.IsNullOrEmpty(value) && value.Length <= 256 && ServiceNameRegex().IsMatch(value);

    [GeneratedRegex("^[a-z0-9][a-z0-9._:\\-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierRegex();

    [GeneratedRegex("^[a-zA-Z][a-zA-Z0-9_\\-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex ParameterNameRegex();

    [GeneratedRegex("^[A-Za-z0-9_.\\-$ ]+$", RegexOptions.CultureInvariant)]
    private static partial Regex ServiceNameRegex();
}
