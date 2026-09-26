using StormOS.Core.Hardware;
using StormOS.Core.Scoring;

namespace StormOS.Core.Network;

/// <summary>ICMP echo statistics.</summary>
public sealed record PingStatistics
{
    /// <summary>Gets the target host or address.</summary>
    public string Target { get; init; } = string.Empty;

    /// <summary>Gets the number of echo requests sent.</summary>
    public int Sent { get; init; }

    /// <summary>Gets the number of replies received.</summary>
    public int Received { get; init; }

    /// <summary>Gets the packet loss in percent.</summary>
    public double LossPercent => Sent == 0 ? 0 : 100.0 * (Sent - Received) / Sent;

    /// <summary>Gets the minimum round trip time in ms.</summary>
    public double? MinMs { get; init; }

    /// <summary>Gets the average round trip time in ms.</summary>
    public double? AverageMs { get; init; }

    /// <summary>Gets the maximum round trip time in ms.</summary>
    public double? MaxMs { get; init; }

    /// <summary>Gets the jitter (mean absolute difference of consecutive RTTs, RFC 3550 style) in ms.</summary>
    public double? JitterMs { get; init; }

    /// <summary>Gets individual RTTs in ms; <see langword="null"/> entries are timeouts.</summary>
    public IReadOnlyList<double?> Samples { get; init; } = [];

    /// <summary>Gets an error message when the test could not run.</summary>
    public string? Error { get; init; }
}

/// <summary>Result of a DNS resolution test against a specific server.</summary>
public sealed record DnsTestResult
{
    /// <summary>Gets the DNS server address.</summary>
    public string Server { get; init; } = string.Empty;

    /// <summary>Gets the queried host name.</summary>
    public string Query { get; init; } = string.Empty;

    /// <summary>Gets a value indicating whether the query succeeded.</summary>
    public bool Success { get; init; }

    /// <summary>Gets the response time in ms.</summary>
    public double? LatencyMs { get; init; }

    /// <summary>Gets the resolved addresses.</summary>
    public IReadOnlyList<string> Addresses { get; init; } = [];

    /// <summary>Gets the DNS response code or error.</summary>
    public string? Error { get; init; }

    /// <summary>Gets a value indicating whether this server is currently configured on the active interface.</summary>
    public bool IsConfigured { get; init; }
}

/// <summary>A single traceroute hop.</summary>
/// <param name="Hop">Hop number (TTL).</param>
/// <param name="Address">Responding address, or <see langword="null"/> on timeout.</param>
/// <param name="RttMs">Round trip time in ms.</param>
/// <param name="TimedOut">Whether the hop did not respond.</param>
public sealed record TraceHop(int Hop, string? Address, double? RttMs, bool TimedOut);

/// <summary>Result of a TCP connect test.</summary>
/// <param name="Host">Host name.</param>
/// <param name="Port">Port.</param>
/// <param name="Success">Whether the connection was established.</param>
/// <param name="ConnectMs">Connection time in ms.</param>
/// <param name="Error">Error message on failure.</param>
public sealed record TcpTestResult(string Host, int Port, bool Success, double? ConnectMs, string? Error);

/// <summary>Result of a throughput measurement.</summary>
public sealed record ThroughputResult
{
    /// <summary>Gets the endpoint used.</summary>
    public string Endpoint { get; init; } = string.Empty;

    /// <summary>Gets the measured download rate in bits per second.</summary>
    public double? DownloadBitsPerSecond { get; init; }

    /// <summary>Gets the measured upload rate in bits per second.</summary>
    public double? UploadBitsPerSecond { get; init; }

    /// <summary>Gets the bytes transferred.</summary>
    public long BytesTransferred { get; init; }

    /// <summary>Gets an error message when the test failed.</summary>
    public string? Error { get; init; }
}

/// <summary>A complete network diagnostics report.</summary>
public sealed record NetworkDiagnosticsReport
{
    /// <summary>Gets the report id.</summary>
    public Guid Id { get; init; }

    /// <summary>Gets the report time.</summary>
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>Gets the active interface.</summary>
    public NetworkAdapterInfo? ActiveInterface { get; init; }

    /// <summary>Gets a value indicating whether internet connectivity was confirmed.</summary>
    public bool InternetReachable { get; init; }

    /// <summary>Gets gateway ping statistics.</summary>
    public PingStatistics? Gateway { get; init; }

    /// <summary>Gets internet ping statistics.</summary>
    public PingStatistics? Internet { get; init; }

    /// <summary>Gets DNS test results.</summary>
    public IReadOnlyList<DnsTestResult> Dns { get; init; } = [];

    /// <summary>Gets TCP connectivity results.</summary>
    public IReadOnlyList<TcpTestResult> Tcp { get; init; } = [];

    /// <summary>Gets the route to the internet target.</summary>
    public IReadOnlyList<TraceHop> Route { get; init; } = [];

    /// <summary>Gets throughput results, when measured.</summary>
    public ThroughputResult? Throughput { get; init; }

    /// <summary>Gets the STORM network score.</summary>
    public ScoreBreakdown? Score { get; init; }

    /// <summary>Gets problems found.</summary>
    public IReadOnlyList<string> Issues { get; init; } = [];
}

/// <summary>Options for a diagnostics run.</summary>
public sealed record NetworkDiagnosticsOptions
{
    /// <summary>Gets the internet latency target.</summary>
    public string InternetTarget { get; init; } = "1.1.1.1";

    /// <summary>Gets the number of echo requests.</summary>
    public int PingCount { get; init; } = 20;

    /// <summary>Gets the host names used for DNS tests.</summary>
    public string DnsQuery { get; init; } = "www.microsoft.com";

    /// <summary>Gets additional DNS servers to compare against the configured ones.</summary>
    public IReadOnlyList<string> AlternativeDnsServers { get; init; } = [];

    /// <summary>Gets TCP endpoints to test.</summary>
    public IReadOnlyList<string> TcpEndpoints { get; init; } = ["www.microsoft.com:443", "store.steampowered.com:443"];

    /// <summary>Gets a value indicating whether a traceroute is performed.</summary>
    public bool IncludeRoute { get; init; } = true;

    /// <summary>Gets a value indicating whether a throughput test is performed.</summary>
    public bool IncludeThroughput { get; init; }

    /// <summary>Gets the throughput download URL.</summary>
    public string? ThroughputUrl { get; init; }
}

/// <summary>Runs network diagnostics.</summary>
public interface INetworkDiagnostics
{
    /// <summary>Runs a full diagnostics pass.</summary>
    /// <param name="options">Options.</param>
    /// <param name="progress">Progress receiver.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The report.</returns>
    Task<NetworkDiagnosticsReport> RunAsync(NetworkDiagnosticsOptions options, IProgress<string>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>Pings a host.</summary>
    /// <param name="host">Host or address.</param>
    /// <param name="count">Number of echo requests.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The statistics.</returns>
    Task<PingStatistics> PingAsync(string host, int count, CancellationToken cancellationToken = default);

    /// <summary>Tests DNS resolution against a server.</summary>
    /// <param name="server">DNS server address.</param>
    /// <param name="query">Host name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result.</returns>
    Task<DnsTestResult> TestDnsAsync(string server, string query, CancellationToken cancellationToken = default);

    /// <summary>Gets the active network interface.</summary>
    /// <returns>The interface or <see langword="null"/> when offline.</returns>
    NetworkAdapterInfo? GetActiveInterface();
}
