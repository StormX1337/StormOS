using System.Net.NetworkInformation;
using System.Net.Sockets;
using StormOS.Core.Hardware;

namespace StormOS.Network;

/// <summary>Maps .NET network interfaces to <see cref="NetworkAdapterInfo"/>.</summary>
public static class NetworkAdapterMapper
{
    /// <summary>Maps an interface.</summary>
    /// <param name="nic">The interface.</param>
    /// <returns>The adapter information.</returns>
    public static NetworkAdapterInfo Map(NetworkInterface nic)
    {
        ArgumentNullException.ThrowIfNull(nic);
        var properties = nic.GetIPProperties();
        bool dhcp;
        try
        {
            dhcp = OperatingSystem.IsWindows() && (properties.GetIPv4Properties()?.IsDhcpEnabled ?? false);
        }
        catch (Exception ex) when (ex is NetworkInformationException or PlatformNotSupportedException)
        {
            dhcp = false;
        }

        return new NetworkAdapterInfo
        {
            Id = nic.Id,
            Name = nic.Name,
            Description = nic.Description,
            InterfaceType = nic.NetworkInterfaceType.ToString(),
            SpeedBitsPerSecond = nic.Speed,
            IsUp = nic.OperationalStatus == OperationalStatus.Up,
            IPv4Addresses = properties.UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork).Select(a => a.Address.ToString()).ToList(),
            IPv6Addresses = properties.UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetworkV6 && !a.Address.IsIPv6LinkLocal).Select(a => a.Address.ToString()).ToList(),
            Gateways = properties.GatewayAddresses.Select(g => g.Address.ToString()).Where(a => a != "0.0.0.0").ToList(),
            DnsServers = properties.DnsAddresses.Select(d => d.ToString()).ToList(),
            DhcpEnabled = dhcp,
        };
    }
}
