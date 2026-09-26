using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using StormOS.Core.Hardware;
using StormOS.Core.Network;
using StormOS.Core.Scoring;
using StormOS.Network.Dns;
using StormOS.Security.Validation;

namespace StormOS.Network.Diagnostics;

/// <summary>
/// Network diagnostics built from real measurements only: ICMP echo (latency, loss, jitter), UDP DNS queries against
/// specific servers, TCP connect timing, TTL-limited echo for route discovery and an HTTPS download for throughput.
/// </summary>
public sealed class NetworkDiagnostics : INetworkDiagnostics
{
    private static readonly TimeSpan PingTimeout = TimeSpan.FromSeconds(2);
    private readonly IHttpClientFactory? _httpClientFactory;
    private readonly ILogger<NetworkDiagnostics> _logger;
    private readonly TimeProvider _time;

    /// <summary>Initializes a new instance of the <see cref="NetworkDiagnostics"/> class.</summary>
    /// <param name="logger">Logger.</param>
    /// <param name="httpClientFactory">HTTP client factory for throughput tests.</param>
    /// <param name="timeProvider">Time source.</param>
    public NetworkDiagnostics(ILogger<NetworkDiagnostics> logger, IHttpClientFactory? httpClientFactory = null, TimeProvider? timeProvider = null)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public NetworkAdapterInfo? GetActiveInterface()
    {
        var candidates = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
            .Select(NetworkAdapterMapper.Map)
            .Where(n => n.Gateways.Count > 0)
            .ToList();
        return candidates.FirstOrDefault(n => n.InterfaceType.Contains("Ethernet", StringComparison.OrdinalIgnoreCase))
            ?? candidates.FirstOrDefault();
    }

    /// <inheritdoc />
    public async Task<PingStatistics> PingAsync(string host, int count, CancellationToken cancellationToken = default)
    {
        if (!InputValidator.IsHost(host))
        {
            return new PingStatistics { Target = host, Error = "Invalid host." };
        }

        count = Math.Clamp(count, 1, 100);
        var samples = new List<double?>(count);
        using var ping = new Ping();
        try
        {
            for (var i = 0; i < count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var reply = await ping.SendPingAsync(host, PingTimeout, cancellationToken: cancellationToken).ConfigureAwait(false);
                samples.Add(reply.Status == IPStatus.Success ? reply.RoundtripTime : null);
                if (i < count - 1)
                {
                    await Task.Delay(200, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (PingException ex)
        {
            _logger.LogInformation(ex, "Ping to {Host} failed", host);
            return LatencyMath.Summarize(host, samples) with { Error = $"{host} could not be reached." };
        }

        return LatencyMath.Summarize(host, samples);
    }

    /// <inheritdoc />
    public Task<DnsTestResult> TestDnsAsync(string server, string query, CancellationToken cancellationToken = default)
    {
        if (!InputValidator.TryParseDnsServer(server, out var address) || !InputValidator.IsHost(query))
        {
            return Task.FromResult(new DnsTestResult { Server = server, Query = query, Error = "Invalid DNS server or host name." });
        }

        return DnsClient.QueryAsync(address!, query, TimeSpan.FromSeconds(3), cancellationToken);
    }

    /// <summary>Measures TCP connection setup time.</summary>
    /// <param name="host">Host.</param>
    /// <param name="port">Port.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result.</returns>
    public static async Task<TcpTestResult> TestTcpAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        using var client = new TcpClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            var addresses = await System.Net.Dns.GetHostAddressesAsync(host, timeout.Token).ConfigureAwait(false);
            var stopwatch = Stopwatch.StartNew();
            await client.ConnectAsync(addresses, port, timeout.Token).ConfigureAwait(false);
            return new TcpTestResult(host, port, true, stopwatch.Elapsed.TotalMilliseconds, null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new TcpTestResult(host, port, false, null, "Timed out.");
        }
        catch (SocketException ex)
        {
            return new TcpTestResult(host, port, false, null, $"Connection failed ({ex.SocketErrorCode}).");
        }
    }

    /// <summary>Discovers the route to a host with TTL-limited ICMP echo requests.</summary>
    /// <param name="host">Target.</param>
    /// <param name="maxHops">Maximum hops.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The hops.</returns>
    public static async Task<IReadOnlyList<TraceHop>> TraceRouteAsync(string host, int maxHops = 20, CancellationToken cancellationToken = default)
    {
        var hops = new List<TraceHop>();
        using var ping = new Ping();
        IPAddress[] targets;
        try
        {
            targets = await System.Net.Dns.GetHostAddressesAsync(host, AddressFamily.InterNetwork, cancellationToken).ConfigureAwait(false);
        }
        catch (SocketException)
        {
            return hops;
        }

        if (targets.Length == 0)
        {
            return hops;
        }

        var buffer = new byte[32];
        for (var ttl = 1; ttl <= Math.Clamp(maxHops, 1, 30); ttl++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stopwatch = Stopwatch.StartNew();
            PingReply reply;
            try
            {
                reply = await ping.SendPingAsync(targets[0], TimeSpan.FromSeconds(2), buffer, new PingOptions(ttl, dontFragment: true), cancellationToken).ConfigureAwait(false);
            }
            catch (PingException)
            {
                hops.Add(new TraceHop(ttl, null, null, true));
                continue;
            }

            var responded = reply.Status is IPStatus.Success or IPStatus.TtlExpired;
            hops.Add(new TraceHop(ttl, responded ? reply.Address.ToString() : null, responded ? stopwatch.Elapsed.TotalMilliseconds : null, !responded));
            if (reply.Status == IPStatus.Success)
            {
                break;
            }
        }

        return hops;
    }

    /// <summary>Measures download throughput from an HTTPS endpoint.</summary>
    /// <param name="url">HTTPS URL.</param>
    /// <param name="maxBytes">Maximum bytes to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<ThroughputResult> MeasureDownloadAsync(string url, long maxBytes = 25_000_000, CancellationToken cancellationToken = default)
    {
        if (!InputValidator.IsHttpsUrl(url))
        {
            return new ThroughputResult { Endpoint = url, Error = "Only HTTPS endpoints are allowed." };
        }

        using var client = _httpClientFactory?.CreateClient("storm-throughput") ?? new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(30);
        try
        {
            using var response = await client.GetAsync(new Uri(url), HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var buffer = new byte[81920];
            long total = 0;
            var stopwatch = Stopwatch.StartNew();
            int read;
            while (total < maxBytes && (read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                total += read;
            }

            var seconds = stopwatch.Elapsed.TotalSeconds;
            return new ThroughputResult { Endpoint = new Uri(url).Host, BytesTransferred = total, DownloadBitsPerSecond = seconds > 0 ? total * 8 / seconds : null };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            _logger.LogInformation(ex, "Throughput test failed");
            return new ThroughputResult { Endpoint = url, Error = "The throughput test could not complete." };
        }
    }

    /// <inheritdoc />
    public async Task<NetworkDiagnosticsReport> RunAsync(NetworkDiagnosticsOptions options, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var issues = new List<string>();
        var active = GetActiveInterface();
        if (active is null)
        {
            issues.Add("No active network connection with a default gateway was found.");
        }

        progress?.Report("Measuring gateway latency");
        PingStatistics? gateway = null;
        if (active?.Gateways.FirstOrDefault(g => IPAddress.TryParse(g, out var a) && a.AddressFamily == AddressFamily.InterNetwork) is { } gatewayAddress)
        {
            gateway = await PingAsync(gatewayAddress, 10, cancellationToken).ConfigureAwait(false);
        }

        progress?.Report("Measuring internet latency, jitter and packet loss");
        var internet = await PingAsync(options.InternetTarget, options.PingCount, cancellationToken).ConfigureAwait(false);

        progress?.Report("Testing DNS servers");
        var dnsResults = new List<DnsTestResult>();
        var configured = active?.DnsServers.Where(s => IPAddress.TryParse(s, out var a) && a.AddressFamily == AddressFamily.InterNetwork).ToList() ?? [];
        foreach (var server in configured.Concat(options.AlternativeDnsServers).Distinct(StringComparer.OrdinalIgnoreCase).Take(8))
        {
            var result = await TestDnsAsync(server, options.DnsQuery, cancellationToken).ConfigureAwait(false);
            dnsResults.Add(result with { IsConfigured = configured.Contains(server, StringComparer.OrdinalIgnoreCase) });
        }

        progress?.Report("Testing TCP connectivity");
        var tcp = new List<TcpTestResult>();
        foreach (var endpoint in options.TcpEndpoints.Take(6))
        {
            if (InputValidator.TryParseEndpoint(endpoint, out var host, out var port))
            {
                tcp.Add(await TestTcpAsync(host, port, cancellationToken).ConfigureAwait(false));
            }
        }

        IReadOnlyList<TraceHop> route = [];
        if (options.IncludeRoute)
        {
            progress?.Report("Tracing the route");
            route = await TraceRouteAsync(options.InternetTarget, 20, cancellationToken).ConfigureAwait(false);
        }

        ThroughputResult? throughput = null;
        if (options.IncludeThroughput && options.ThroughputUrl is { } url)
        {
            progress?.Report("Measuring download speed");
            throughput = await MeasureDownloadAsync(url, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        AddIssues(issues, gateway, internet, dnsResults, tcp);
        var reachable = internet.Received > 0 || tcp.Any(t => t.Success);
        return new NetworkDiagnosticsReport
        {
            Id = Guid.NewGuid(),
            Timestamp = _time.GetUtcNow(),
            ActiveInterface = active,
            InternetReachable = reachable,
            Gateway = gateway,
            Internet = internet,
            Dns = dnsResults,
            Tcp = tcp,
            Route = route,
            Throughput = throughput,
            Score = StormScores.Network(internet, dnsResults),
            Issues = issues,
        };
    }

    private static void AddIssues(List<string> issues, PingStatistics? gateway, PingStatistics internet, List<DnsTestResult> dns, List<TcpTestResult> tcp)
    {
        if (gateway is { Sent: > 0 } && gateway.LossPercent > 0)
        {
            issues.Add($"{gateway.LossPercent:0}% packet loss to your router — check the cable or Wi-Fi signal.");
        }

        if (gateway?.AverageMs is > 10)
        {
            issues.Add($"Router latency is {gateway.AverageMs:0} ms; a wired connection is usually below 2 ms.");
        }

        if (internet.Sent > 0 && internet.Received == 0)
        {
            issues.Add("The internet latency target did not answer (ICMP may be blocked).");
        }
        else if (internet.LossPercent >= 2)
        {
            issues.Add($"{internet.LossPercent:0}% packet loss to the internet.");
        }

        if (internet.JitterMs is > 10)
        {
            issues.Add($"High jitter ({internet.JitterMs:0} ms) causes inconsistent hit registration.");
        }

        if (dns.Where(d => d.IsConfigured).Any(d => !d.Success))
        {
            issues.Add("A configured DNS server did not answer.");
        }

        if (tcp.Count > 0 && tcp.All(t => !t.Success))
        {
            issues.Add("No TCP connections could be established.");
        }
    }
}
