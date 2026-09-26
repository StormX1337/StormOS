using Microsoft.Extensions.Logging;
using StormOS.Core.Common;
using StormOS.Core.Hardware;
using StormOS.Core.Processes;
using StormOS.Core.Telemetry;
using StormOS.Windows.Pdh;

namespace StormOS.Hardware.Gpu;

/// <summary>
/// GPU telemetry: utilization and VRAM from the "GPU Engine"/"GPU Adapter Memory" counters (all vendors),
/// temperature/fan/clocks from D3DKMT (all WDDM 2.4+ drivers) and, on NVIDIA, board power and clocks from NVML.
/// </summary>
public sealed class GpuMetricCollector : IGpuMetricCollector, IProcessGpuUsageProvider
{
    private readonly ILogger<GpuMetricCollector> _logger;
    private readonly List<AdapterState> _adapters = [];
    private PdhQuery? _query;
    private PdhCounter? _engine;
    private PdhCounter? _dedicated;
    private Nvml? _nvml;
    private bool _initialized;
    private IReadOnlyDictionary<int, double> _byProcess = new Dictionary<int, double>();

    /// <summary>Initializes a new instance of the <see cref="GpuMetricCollector"/> class.</summary>
    /// <param name="logger">Logger.</param>
    public GpuMetricCollector(ILogger<GpuMetricCollector> logger) => _logger = logger;

    /// <inheritdoc />
    public IReadOnlyList<GpuMetrics> Collect(SamplingMode mode)
    {
        EnsureInitialized();
        if (_adapters.Count == 0)
        {
            return [];
        }

        GpuEngineAggregator.Aggregate? engines = null;
        var dedicatedUsage = new Dictionary<long, double>();
        if (_query is not null && _query.Collect())
        {
            if (_engine is not null && _query.HasRateData)
            {
                engines = GpuEngineAggregator.Compute(_engine.GetInstances());
                Volatile.Write(ref _byProcess, engines.ByProcess);
            }

            if (_dedicated is not null)
            {
                foreach (var (instance, value) in _dedicated.GetInstances())
                {
                    if (GpuEngineInstance.TryParseAdapter(instance, out var luid))
                    {
                        dedicatedUsage[luid] = dedicatedUsage.GetValueOrDefault(luid) + value;
                    }
                }
            }
        }

        var result = new List<GpuMetrics>(_adapters.Count);
        foreach (var adapter in _adapters)
        {
            result.Add(Sample(adapter, engines, dedicatedUsage));
        }

        return result;
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<int, double> GetGpuUsageByProcess() => Volatile.Read(ref _byProcess);

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var adapter in _adapters)
        {
            adapter.Kmt?.Dispose();
        }

        _adapters.Clear();
        _query?.Dispose();
        _nvml?.Dispose();
    }

    private GpuMetrics Sample(AdapterState adapter, GpuEngineAggregator.Aggregate? engines, Dictionary<long, double> dedicatedUsage)
    {
        var info = adapter.Info;
        var usage = engines is null
            ? Reading.Unavailable("GPU counters are warming up.", "PDH")
            : Reading.Of(engines.ByAdapter.GetValueOrDefault(info.AdapterLuid), "PDH");
        var usage3D = engines is null
            ? Reading.Unavailable("GPU counters are warming up.", "PDH")
            : Reading.Of(engines.ByAdapter3D.GetValueOrDefault(info.AdapterLuid), "PDH");

        var kmt = adapter.Kmt?.Read();
        var metrics = new GpuMetrics
        {
            AdapterLuid = info.AdapterLuid,
            Name = info.Name,
            Usage = usage,
            Usage3D = usage3D,
            MemoryTotalBytes = info.DedicatedMemoryBytes > 0 ? info.DedicatedMemoryBytes : null,
            MemoryUsedBytes = dedicatedUsage.TryGetValue(info.AdapterLuid, out var used) ? (long)used : null,
            TemperatureCelsius = kmt?.TemperatureCelsius ?? Reading.Unavailable("The display driver does not expose telemetry for this adapter.", "D3DKMT"),
            FanRpm = kmt?.FanRpm ?? Reading.Unavailable("Fan speed is not reported for this adapter.", "D3DKMT"),
            CoreClockMhz = kmt?.CoreClockMhz ?? Reading.Unavailable("The engine clock is not reported for this adapter.", "D3DKMT"),
            MemoryClockMhz = kmt?.MemoryClockMhz ?? Reading.Unavailable("The memory clock is not reported for this adapter.", "D3DKMT"),
            PowerWatts = Reading.Unavailable(info.Vendor == GpuVendor.Nvidia
                ? "NVML is not available; update the NVIDIA driver."
                : "Board power in watts is only exposed by vendor APIs; this driver reports none.", "D3DKMT"),
            FanPercent = Reading.Unavailable("Fan duty cycle is only exposed by vendor APIs.", "D3DKMT"),
        };

        if (adapter.NvmlDevice is { } device && _nvml is not null)
        {
            metrics = ApplyNvml(metrics, _nvml, device);
        }

        return metrics;
    }

    private static GpuMetrics ApplyNvml(GpuMetrics metrics, Nvml nvml, IntPtr device)
    {
        const string Source = "NVML";
        var memory = nvml.Memory(device);
        var temperature = nvml.Temperature(device);
        var core = nvml.Clock(device, Nvml.ClockGraphics);
        var memClock = nvml.Clock(device, Nvml.ClockMemory);
        var power = nvml.PowerMilliwatts(device);
        var fan = nvml.FanPercent(device);
        return metrics with
        {
            TemperatureCelsius = temperature is { } t ? Reading.Of(t, Source) : metrics.TemperatureCelsius,
            CoreClockMhz = core is { } c ? Reading.Of(c, Source) : metrics.CoreClockMhz,
            MemoryClockMhz = memClock is { } m ? Reading.Of(m, Source) : metrics.MemoryClockMhz,
            PowerWatts = power is { } p ? Reading.Of(p / 1000.0, Source) : Reading.Unavailable("This NVIDIA GPU does not report board power.", Source),
            FanPercent = fan is { } f ? Reading.Of(f, Source) : Reading.Unavailable("This NVIDIA GPU does not report fan speed.", Source),
            MemoryTotalBytes = memory is { } mem ? (long)mem.Total : metrics.MemoryTotalBytes,
            MemoryUsedBytes = memory is { } used ? (long)used.Used : metrics.MemoryUsedBytes,
        };
    }

    private void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        try
        {
            var adapters = DxgiAdapterEnumerator.GetAdapters().Where(a => !a.IsSoftware).ToList();
            _nvml = adapters.Any(a => a.Vendor == GpuVendor.Nvidia) ? Nvml.TryLoad() : null;
            var nvmlDevices = _nvml?.GetDevices().ToList() ?? [];
            foreach (var adapter in adapters)
            {
                IntPtr? nvmlDevice = null;
                if (adapter.Vendor == GpuVendor.Nvidia)
                {
                    var pciId = ((uint)adapter.DeviceId << 16) | (uint)adapter.VendorId;
                    var match = nvmlDevices.FindIndex(d => d.PciDeviceId == pciId);
                    if (match >= 0)
                    {
                        nvmlDevice = nvmlDevices[match].Handle;
                        nvmlDevices.RemoveAt(match);
                    }
                }

                _adapters.Add(new AdapterState(adapter, D3dkmtAdapterTelemetry.TryOpen(adapter.AdapterLuid), nvmlDevice));
            }

            _query = new PdhQuery();
            _engine = _query.TryAddCounter(@"\GPU Engine(*)\Utilization Percentage");
            _dedicated = _query.TryAddCounter(@"\GPU Adapter Memory(*)\Dedicated Usage");
            _query.Collect();
            _logger.LogInformation("GPU telemetry initialized for {Count} adapter(s); NVML {Nvml}; engine counters {Engine}", _adapters.Count, _nvml is null ? "unavailable" : "loaded", _engine is null ? "unavailable" : "available");
        }
        catch (Exception ex) when (ex is InvalidOperationException or DllNotFoundException or EntryPointNotFoundException or System.Runtime.InteropServices.COMException or SharpGen.Runtime.SharpGenException)
        {
            _logger.LogWarning(ex, "GPU telemetry could not be initialized");
        }
    }

    private sealed record AdapterState(GpuInfo Info, D3dkmtAdapterTelemetry? Kmt, IntPtr? NvmlDevice);
}
