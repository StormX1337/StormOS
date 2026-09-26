using StormOS.Core.Common;

namespace StormOS.Hardware.Gpu;

/// <summary>Vendor neutral adapter readings from D3DKMT.</summary>
/// <param name="TemperatureCelsius">Temperature.</param>
/// <param name="FanRpm">Fan speed.</param>
/// <param name="MemoryClockMhz">Memory clock.</param>
/// <param name="CoreClockMhz">3D engine clock.</param>
/// <param name="PowerPercent">Power in percent of TDP.</param>
public sealed record D3dkmtReadings(Reading TemperatureCelsius, Reading FanRpm, Reading MemoryClockMhz, Reading CoreClockMhz, Reading PowerPercent);

/// <summary>Reads D3DKMT performance data for one adapter.</summary>
public sealed class D3dkmtAdapterTelemetry : IDisposable
{
    private const string Source = "D3DKMT";
    private readonly uint _adapter;
    private readonly D3dkmt.AdapterPerfDataCaps _caps;
    private readonly bool _hasCaps;
    private bool _disposed;

    private D3dkmtAdapterTelemetry(uint adapter, D3dkmt.AdapterPerfDataCaps caps, bool hasCaps)
    {
        _adapter = adapter;
        _caps = caps;
        _hasCaps = hasCaps;
    }

    /// <summary>Opens an adapter by LUID.</summary>
    /// <param name="luid">Adapter LUID.</param>
    /// <returns>The telemetry reader, or <see langword="null"/> when unavailable.</returns>
    public static D3dkmtAdapterTelemetry? TryOpen(long luid)
    {
        try
        {
            var open = new D3dkmt.OpenAdapterFromLuid { AdapterLuid = D3dkmt.Luid.FromValue(luid) };
            if (D3dkmt.OpenAdapter(ref open) != 0 || open.Adapter == 0)
            {
                return null;
            }

            var caps = default(D3dkmt.AdapterPerfDataCaps);
            var hasCaps = D3dkmt.TryQuery(open.Adapter, D3dkmt.QueryAdapterPerfDataCaps, ref caps);
            return new D3dkmtAdapterTelemetry(open.Adapter, caps, hasCaps);
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }
        catch (DllNotFoundException)
        {
            return null;
        }
    }

    /// <summary>Reads current values.</summary>
    /// <returns>The readings.</returns>
    public D3dkmtReadings Read()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var perf = default(D3dkmt.AdapterPerfData);
        if (!D3dkmt.TryQuery(_adapter, D3dkmt.QueryAdapterPerfData, ref perf))
        {
            var reason = Reading.Unavailable("The display driver does not report adapter performance data (WDDM 2.4 or newer is required).", Source);
            return new D3dkmtReadings(reason, reason, reason, reason, reason);
        }

        var node = default(D3dkmt.NodePerfData);
        var hasNode = D3dkmt.TryQuery(_adapter, D3dkmt.QueryNodePerfData, ref node);
        return Interpret(perf, _hasCaps ? _caps : null, hasNode ? node : null);
    }

    /// <summary>Converts raw D3DKMT structures into readings, rejecting values the driver reports as unsupported.</summary>
    /// <param name="perf">Adapter performance data.</param>
    /// <param name="caps">Capabilities, when available.</param>
    /// <param name="node">Node 0 (3D engine) performance data, when available.</param>
    /// <returns>The readings.</returns>
    internal static D3dkmtReadings Interpret(D3dkmt.AdapterPerfData perf, D3dkmt.AdapterPerfDataCaps? caps, D3dkmt.NodePerfData? node)
    {
        var temperatureSupported = caps is null || caps.Value.TemperatureMax > 0;
        var temperature = temperatureSupported && perf.Temperature is > 0 and < 1500
            ? Reading.Of(perf.Temperature / 10.0, Source)
            : Reading.Unavailable("The display driver does not report the GPU temperature.", Source);

        var fan = caps is { MaxFanRpm: > 0 }
            ? Reading.Of(perf.FanRpm, Source)
            : Reading.Unavailable("The display driver does not report fan speed (or the card is passively cooled).", Source);

        var memory = perf.MemoryFrequency > 0
            ? Reading.Of(perf.MemoryFrequency / 1_000_000.0, Source)
            : Reading.Unavailable("The display driver does not report the memory clock.", Source);

        var core = node is { Frequency: > 0 } n
            ? Reading.Of(n.Frequency / 1_000_000.0, Source)
            : Reading.Unavailable("The display driver does not report the engine clock.", Source);

        var power = perf.Power is > 0 and <= 2000
            ? Reading.Of(perf.Power / 10.0, Source)
            : Reading.Unavailable("The display driver does not report power.", Source);

        return new D3dkmtReadings(temperature, fan, memory, core, power);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        var close = new D3dkmt.CloseAdapter { Adapter = _adapter };
        _ = D3dkmt.Close(ref close);
    }
}
