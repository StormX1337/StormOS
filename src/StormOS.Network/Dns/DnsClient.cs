using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using StormOS.Core.Network;

namespace StormOS.Network.Dns;

/// <summary>Sends a single UDP DNS query to a chosen server and measures the response time.</summary>
public static class DnsClient
{
    /// <summary>Resolves a host through a specific server.</summary>
    /// <param name="server">DNS server address.</param>
    /// <param name="host">Host name.</param>
    /// <param name="timeout">Timeout.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The test result.</returns>
    public static async Task<DnsTestResult> QueryAsync(IPAddress server, string host, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(server);
        var id = (ushort)RandomNumberGenerator.GetInt32(ushort.MaxValue);
        var query = DnsMessage.BuildQuery(id, host);
        using var socket = new Socket(server.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        var endpoint = new IPEndPoint(server, 53);
        var buffer = new byte[1232];
        try
        {
            var stopwatch = Stopwatch.StartNew();
            await socket.SendToAsync(query, SocketFlags.None, endpoint, timeoutCts.Token).ConfigureAwait(false);
            while (true)
            {
                var received = await socket.ReceiveFromAsync(buffer, SocketFlags.None, new IPEndPoint(server.AddressFamily == AddressFamily.InterNetwork ? IPAddress.Any : IPAddress.IPv6Any, 0), timeoutCts.Token).ConfigureAwait(false);
                var elapsed = stopwatch.Elapsed.TotalMilliseconds;
                if (!((IPEndPoint)received.RemoteEndPoint).Address.Equals(server))
                {
                    continue;
                }

                var response = DnsMessage.Parse(buffer.AsSpan(0, received.ReceivedBytes));
                if (response.Id != id)
                {
                    continue;
                }

                return new DnsTestResult
                {
                    Server = server.ToString(),
                    Query = host,
                    Success = response.ResponseCode == 0 && response.Addresses.Count > 0,
                    LatencyMs = elapsed,
                    Addresses = response.Addresses.Select(a => a.ToString()).ToList(),
                    Error = response.ResponseCode == 0 ? (response.Addresses.Count == 0 ? "No address records returned." : null) : DnsMessage.DescribeResponseCode(response.ResponseCode),
                };
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new DnsTestResult { Server = server.ToString(), Query = host, Success = false, Error = $"No response within {timeout.TotalSeconds:0} s." };
        }
        catch (Exception ex) when (ex is SocketException or FormatException)
        {
            return new DnsTestResult { Server = server.ToString(), Query = host, Success = false, Error = ex is SocketException s ? $"Network error ({s.SocketErrorCode})." : "Malformed response." };
        }
    }
}
