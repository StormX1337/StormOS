using System.Net.NetworkInformation;
using Microsoft.Extensions.Logging;
using StormOS.Core.Common;

namespace StormOS.Performance.Telemetry;

/// <summary>Sends a periodic ICMP echo to measure live latency while telemetry is active.</summary>
public sealed class LatencyProbe : IDisposable
{
    private readonly ILogger<LatencyProbe> _logger;
    private readonly Lock _gate = new();
    private CancellationTokenSource? _cts;
    private Reading _latest = Reading.Unavailable("Latency has not been measured yet.", "ICMP");

    /// <summary>Initializes a new instance of the <see cref="LatencyProbe"/> class.</summary>
    /// <param name="logger">Logger.</param>
    public LatencyProbe(ILogger<LatencyProbe> logger) => _logger = logger;

    /// <summary>Gets the latest measurement.</summary>
    public Reading Latest
    {
        get
        {
            lock (_gate)
            {
                return _latest;
            }
        }
    }

    /// <summary>Starts probing.</summary>
    /// <param name="host">Target host.</param>
    /// <param name="interval">Interval.</param>
    public void Start(string host, TimeSpan interval)
    {
        lock (_gate)
        {
            if (_cts is not null)
            {
                return;
            }

            _cts = new CancellationTokenSource();
            _ = Task.Run(() => RunAsync(host, interval, _cts.Token));
        }
    }

    /// <summary>Stops probing.</summary>
    public void Stop()
    {
        lock (_gate)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }
    }

    /// <inheritdoc />
    public void Dispose() => Stop();

    private async Task RunAsync(string host, TimeSpan interval, CancellationToken cancellationToken)
    {
        using var ping = new Ping();
        using var timer = new PeriodicTimer(interval);
        do
        {
            Reading reading;
            try
            {
                var reply = await ping.SendPingAsync(host, TimeSpan.FromSeconds(2), cancellationToken: cancellationToken).ConfigureAwait(false);
                reading = reply.Status == IPStatus.Success
                    ? Reading.Of(reply.RoundtripTime, "ICMP")
                    : Reading.Unavailable($"No reply from {host} ({reply.Status}).", "ICMP");
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (PingException ex)
            {
                _logger.LogDebug(ex, "Latency probe failed");
                reading = Reading.Unavailable($"{host} is not reachable.", "ICMP");
            }

            lock (_gate)
            {
                _latest = reading;
            }
        }
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false));
    }
}
