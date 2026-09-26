using System.Net.NetworkInformation;
using StormOS.Core.Telemetry;

namespace StormOS.Hardware.Networking;

/// <summary>Per-interface throughput from interface byte counters.</summary>
public sealed class NetworkMetricCollector : INetworkMetricCollector
{
    private static readonly TimeSpan InterfaceRefresh = TimeSpan.FromSeconds(30);
    private readonly TimeProvider _time;
    private readonly Dictionary<string, (long Rx, long Tx)> _previous = new(StringComparer.Ordinal);
    private NetworkInterface[] _interfaces = [];
    private long _lastRefresh;
    private long _lastSample;

    /// <summary>Initializes a new instance of the <see cref="NetworkMetricCollector"/> class.</summary>
    /// <param name="timeProvider">Time source.</param>
    public NetworkMetricCollector(TimeProvider? timeProvider = null) => _time = timeProvider ?? TimeProvider.System;

    /// <inheritdoc />
    public IReadOnlyList<NetworkInterfaceMetrics> Collect(SamplingMode mode)
    {
        var now = _time.GetTimestamp();
        if (_lastRefresh == 0 || _time.GetElapsedTime(_lastRefresh, now) > InterfaceRefresh)
        {
            _interfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
                .ToArray();
            _lastRefresh = now;
        }

        var seconds = _lastSample == 0 ? 0 : _time.GetElapsedTime(_lastSample, now).TotalSeconds;
        _lastSample = now;
        var result = new List<NetworkInterfaceMetrics>(_interfaces.Length);
        foreach (var nic in _interfaces)
        {
            IPInterfaceStatistics stats;
            try
            {
                stats = nic.GetIPStatistics();
            }
            catch (NetworkInformationException)
            {
                continue;
            }

            var rx = stats.BytesReceived;
            var tx = stats.BytesSent;
            double rxRate = 0, txRate = 0;
            if (seconds > 0 && _previous.TryGetValue(nic.Id, out var prev) && rx >= prev.Rx && tx >= prev.Tx)
            {
                rxRate = (rx - prev.Rx) / seconds;
                txRate = (tx - prev.Tx) / seconds;
            }

            _previous[nic.Id] = (rx, tx);
            result.Add(new NetworkInterfaceMetrics { Id = nic.Id, Name = nic.Name, ReceivedBytesPerSecond = rxRate, SentBytesPerSecond = txRate, LinkSpeedBitsPerSecond = nic.Speed });
        }

        return result;
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }
}
