using System.Globalization;
using Microsoft.Extensions.Logging;
using StormOS.Core.Common;
using StormOS.Core.Telemetry;
using StormOS.Windows.Pdh;

namespace StormOS.Hardware.Cpu;

/// <summary>
/// CPU telemetry through PDH: utility and effective frequency (as shown by Task Manager), ACPI thermal zone
/// temperature and, where the platform exposes an Energy Meter (Intel RAPL / AMD), package power.
/// </summary>
public sealed class CpuMetricCollector : ICpuMetricCollector
{
    /// <summary>Explanation used when no temperature sensor is exposed.</summary>
    public const string TemperatureUnavailable = "Windows exposes no CPU temperature sensor on this system (no ACPI thermal zone). STORM OS does not install kernel drivers to read sensors directly.";

    private readonly ILogger<CpuMetricCollector> _logger;
    private PdhQuery? _query;
    private PdhCounter? _utility;
    private PdhCounter? _performance;
    private PdhCounter? _frequency;
    private PdhCounter? _thermal;
    private bool _thermalIsHighPrecision;
    private PdhCounter? _energy;
    private bool _initialized;

    /// <summary>Initializes a new instance of the <see cref="CpuMetricCollector"/> class.</summary>
    /// <param name="logger">Logger.</param>
    public CpuMetricCollector(ILogger<CpuMetricCollector> logger) => _logger = logger;

    /// <inheritdoc />
    public CpuMetrics Collect(SamplingMode mode)
    {
        EnsureInitialized();
        if (_query is null || !_query.Collect())
        {
            var reason = Reading.Unavailable("CPU performance counters are not available.", "PDH");
            return new CpuMetrics { Usage = reason, FrequencyMhz = reason, TemperatureCelsius = reason, PackagePowerWatts = reason };
        }

        var utility = _utility?.GetInstances() ?? [];
        var performance = _performance?.GetInstances() ?? [];
        var frequency = _frequency?.GetInstances() ?? [];

        var total = Find(utility, "_Total");
        var totalPerf = Find(performance, "_Total");
        var baseFreq = Find(frequency, "_Total");

        return new CpuMetrics
        {
            Usage = _query.HasRateData && total is { } u ? Reading.Of(Math.Clamp(u, 0, 100), "PDH") : Reading.Unavailable("CPU counters are warming up.", "PDH"),
            FrequencyMhz = totalPerf is { } p && baseFreq is { } f && f > 0 ? Reading.Of(f * p / 100.0, "PDH") : Reading.Unavailable("Processor frequency counters are not available.", "PDH"),
            TemperatureCelsius = ReadTemperature(),
            PackagePowerWatts = ReadPower(),
            Cores = mode == SamplingMode.Performance ? [] : BuildCores(utility, performance, frequency),
        };
    }

    /// <summary>Converts a thermal zone value to Celsius.</summary>
    /// <param name="raw">Raw value.</param>
    /// <param name="highPrecision">Whether the value is in tenths of Kelvin.</param>
    /// <returns>Temperature in °C.</returns>
    public static double KelvinToCelsius(double raw, bool highPrecision) => (highPrecision ? raw / 10.0 : raw) - 273.15;

    /// <summary>Parses a per-core instance name "group,index".</summary>
    /// <param name="instance">Instance name.</param>
    /// <param name="group">Processor group.</param>
    /// <param name="index">Index within the group.</param>
    /// <returns><see langword="true"/> for per-core instances.</returns>
    public static bool TryParseCoreInstance(string instance, out int group, out int index)
    {
        group = index = 0;
        var comma = instance.IndexOf(',', StringComparison.Ordinal);
        return comma > 0
            && int.TryParse(instance.AsSpan(0, comma), NumberStyles.None, CultureInfo.InvariantCulture, out group)
            && int.TryParse(instance.AsSpan(comma + 1), NumberStyles.None, CultureInfo.InvariantCulture, out index);
    }

    /// <inheritdoc />
    public void Dispose() => _query?.Dispose();

    private static double? Find(IReadOnlyList<KeyValuePair<string, double>> values, string instance)
    {
        foreach (var (name, value) in values)
        {
            if (name == instance)
            {
                return value;
            }
        }

        return null;
    }

    private List<CoreMetrics> BuildCores(IReadOnlyList<KeyValuePair<string, double>> utility, IReadOnlyList<KeyValuePair<string, double>> performance, IReadOnlyList<KeyValuePair<string, double>> frequency)
    {
        if (!(_query?.HasRateData ?? false))
        {
            return [];
        }

        var perf = performance.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        var freq = frequency.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        var cores = new List<(int Group, int Index, double Usage, double? Mhz)>();
        foreach (var (name, value) in utility)
        {
            if (!TryParseCoreInstance(name, out var group, out var index))
            {
                continue;
            }

            double? mhz = perf.TryGetValue(name, out var p) && freq.TryGetValue(name, out var f) && f > 0 ? f * p / 100.0 : null;
            cores.Add((group, index, Math.Clamp(value, 0, 100), mhz));
        }

        return cores.OrderBy(c => c.Group).ThenBy(c => c.Index).Select((c, i) => new CoreMetrics(i, c.Usage, c.Mhz)).ToList();
    }

    private Reading ReadTemperature()
    {
        if (_thermal is null)
        {
            return Reading.Unavailable(TemperatureUnavailable, "ACPI thermal zone");
        }

        double? max = null;
        foreach (var (_, raw) in _thermal.GetInstances())
        {
            var celsius = KelvinToCelsius(raw, _thermalIsHighPrecision);
            if (celsius is > 5 and < 125)
            {
                max = Math.Max(max ?? double.MinValue, celsius);
            }
        }

        return max is { } value
            ? Reading.Of(value, "ACPI thermal zone")
            : Reading.Unavailable(TemperatureUnavailable, "ACPI thermal zone");
    }

    private Reading ReadPower()
    {
        if (_energy is null)
        {
            return Reading.Unavailable("This platform exposes no Energy Meter for the CPU package.", "Energy Meter");
        }

        foreach (var (name, milliwatts) in _energy.GetInstances())
        {
            if ((name.Contains("PKG", StringComparison.OrdinalIgnoreCase) || name.Contains("package", StringComparison.OrdinalIgnoreCase)) && milliwatts is > 100 and < 1_000_000)
            {
                return Reading.Of(milliwatts / 1000.0, "Energy Meter");
            }
        }

        return Reading.Unavailable("The Energy Meter reports no CPU package channel.", "Energy Meter");
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
            _query = new PdhQuery();
            _utility = _query.TryAddCounter(@"\Processor Information(*)\% Processor Utility")
                ?? _query.TryAddCounter(@"\Processor Information(*)\% Processor Time");
            _performance = _query.TryAddCounter(@"\Processor Information(*)\% Processor Performance");
            _frequency = _query.TryAddCounter(@"\Processor Information(*)\Processor Frequency");
            _thermal = _query.TryAddCounter(@"\Thermal Zone Information(*)\High Precision Temperature");
            _thermalIsHighPrecision = _thermal is not null;
            _thermal ??= _query.TryAddCounter(@"\Thermal Zone Information(*)\Temperature");
            _energy = _query.TryAddCounter(@"\Energy Meter(*)\Power");
            _query.Collect();
            _logger.LogInformation("CPU telemetry initialized: thermal zone {Thermal}, energy meter {Energy}", _thermal is null ? "absent" : "present", _energy is null ? "absent" : "present");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "CPU performance counters could not be opened");
            _query?.Dispose();
            _query = null;
        }
    }
}
