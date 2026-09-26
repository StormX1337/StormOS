using System.Globalization;
using System.Management;
using StormOS.Windows.RegistryAccess;

namespace StormOS.Optimization.Rules.Network;

/// <summary>IPv4 DNS configuration of an interface.</summary>
/// <param name="Servers">Statically configured servers; empty when DHCP-assigned.</param>
/// <param name="IsStatic">Whether servers are statically configured.</param>
public sealed record DnsConfiguration(IReadOnlyList<string> Servers, bool IsStatic);

/// <summary>Reads and writes IPv4 DNS server configuration.</summary>
public interface IDnsConfigurator
{
    /// <summary>Reads the configuration of an interface.</summary>
    /// <param name="interfaceId">Interface GUID, for example "{8A1C…}".</param>
    /// <returns>The configuration, or <see langword="null"/> when the interface does not exist.</returns>
    DnsConfiguration? Read(string interfaceId);

    /// <summary>Sets static servers, or reverts to DHCP when <paramref name="servers"/> is empty.</summary>
    /// <param name="interfaceId">Interface GUID.</param>
    /// <param name="servers">Servers.</param>
    void Write(string interfaceId, IReadOnlyList<string> servers);
}

/// <summary>DNS configuration through the registry (read) and Win32_NetworkAdapterConfiguration (write).</summary>
public sealed class WindowsDnsConfigurator(IRegistryAccess registry) : IDnsConfigurator
{
    private const string InterfacesKey = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\";

    /// <summary>Normalizes an interface id to "{GUID}" upper-case form.</summary>
    /// <param name="interfaceId">Id with or without braces.</param>
    /// <returns>The normalized id, or <see langword="null"/> when not a GUID.</returns>
    public static string? Normalize(string interfaceId) =>
        Guid.TryParse(interfaceId, out var guid) ? guid.ToString("B").ToUpperInvariant() : null;

    /// <summary>Parses a NameServer registry value (comma or space separated).</summary>
    /// <param name="value">Registry value.</param>
    /// <returns>Servers.</returns>
    public static IReadOnlyList<string> ParseNameServer(string? value) =>
        string.IsNullOrWhiteSpace(value) ? [] : value.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <inheritdoc />
    public DnsConfiguration? Read(string interfaceId)
    {
        var id = Normalize(interfaceId) ?? throw new ArgumentException("Invalid interface id.", nameof(interfaceId));
        var key = InterfacesKey + id;
        if (registry.GetValueNames(RegistryHive.LocalMachine, key).Count == 0)
        {
            return null;
        }

        var servers = ParseNameServer(registry.GetValue(RegistryHive.LocalMachine, key, "NameServer")?.Value as string);
        return new DnsConfiguration(servers, servers.Count > 0);
    }

    /// <inheritdoc />
    public void Write(string interfaceId, IReadOnlyList<string> servers)
    {
        ArgumentNullException.ThrowIfNull(servers);
        var id = Normalize(interfaceId) ?? throw new ArgumentException("Invalid interface id.", nameof(interfaceId));
        using var searcher = new ManagementObjectSearcher(@"root\cimv2", $"SELECT * FROM Win32_NetworkAdapterConfiguration WHERE SettingID = '{id}'");
        using var results = searcher.Get();
        var adapter = results.Cast<ManagementObject>().FirstOrDefault() ?? throw new InvalidOperationException("The network adapter was not found.");
        using (adapter)
        {
            using var parameters = adapter.GetMethodParameters("SetDNSServerSearchOrder");
            parameters["DNSServerSearchOrder"] = servers.Count == 0 ? null : servers.ToArray();
            using var output = adapter.InvokeMethod("SetDNSServerSearchOrder", parameters, null);
            var code = Convert.ToUInt32(output?["ReturnValue"] ?? 0u, CultureInfo.InvariantCulture);
            if (code is not (0 or 1))
            {
                throw new InvalidOperationException($"SetDNSServerSearchOrder failed with code {code}.");
            }
        }
    }
}
