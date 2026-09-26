using System.Globalization;
using System.Text.RegularExpressions;

namespace StormOS.Hardware.Gpu;

/// <summary>A parsed instance name of the "GPU Engine" performance counter set.</summary>
/// <param name="ProcessId">Owning process id.</param>
/// <param name="AdapterLuid">Adapter LUID.</param>
/// <param name="PhysicalIndex">Physical adapter index.</param>
/// <param name="EngineIndex">Engine index.</param>
/// <param name="EngineType">Engine type, for example "3D", "Copy", "VideoDecode".</param>
public sealed partial record GpuEngineInstance(int ProcessId, long AdapterLuid, int PhysicalIndex, int EngineIndex, string EngineType)
{
    /// <summary>Parses "pid_1234_luid_0x00000000_0x0000D1F1_phys_0_eng_0_engtype_3D".</summary>
    /// <param name="instance">Instance name.</param>
    /// <param name="result">The parsed instance.</param>
    /// <returns><see langword="true"/> when parsed.</returns>
    public static bool TryParse(string instance, out GpuEngineInstance? result)
    {
        result = null;
        var match = EngineRegex().Match(instance ?? string.Empty);
        if (!match.Success)
        {
            return false;
        }

        result = new GpuEngineInstance(
            int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
            ParseLuid(match.Groups[2].Value, match.Groups[3].Value),
            int.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture),
            int.Parse(match.Groups[5].Value, CultureInfo.InvariantCulture),
            match.Groups[6].Value);
        return true;
    }

    /// <summary>Parses an adapter instance "luid_0x00000000_0x0000D1F1_phys_0" into its LUID.</summary>
    /// <param name="instance">Instance name.</param>
    /// <param name="luid">The LUID.</param>
    /// <returns><see langword="true"/> when parsed.</returns>
    public static bool TryParseAdapter(string instance, out long luid)
    {
        luid = 0;
        var match = AdapterRegex().Match(instance ?? string.Empty);
        if (!match.Success)
        {
            return false;
        }

        luid = ParseLuid(match.Groups[1].Value, match.Groups[2].Value);
        return true;
    }

    private static long ParseLuid(string high, string low) =>
        ((long)uint.Parse(high, NumberStyles.HexNumber, CultureInfo.InvariantCulture) << 32) | uint.Parse(low, NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    [GeneratedRegex("^pid_(\\d+)_luid_0x([0-9A-Fa-f]{8})_0x([0-9A-Fa-f]{8})_phys_(\\d+)_eng_(\\d+)_engtype_(.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex EngineRegex();

    [GeneratedRegex("^luid_0x([0-9A-Fa-f]{8})_0x([0-9A-Fa-f]{8})_phys_\\d+", RegexOptions.CultureInvariant)]
    private static partial Regex AdapterRegex();
}

/// <summary>Aggregates "GPU Engine" utilization the same way Task Manager does.</summary>
public static class GpuEngineAggregator
{
    /// <summary>Aggregated utilization.</summary>
    /// <param name="ByAdapter">Busiest engine per adapter (sum over processes per engine, max over engines).</param>
    /// <param name="ByAdapter3D">Busiest 3D engine per adapter.</param>
    /// <param name="ByProcess">Busiest engine per process across adapters.</param>
    public sealed record Aggregate(IReadOnlyDictionary<long, double> ByAdapter, IReadOnlyDictionary<long, double> ByAdapter3D, IReadOnlyDictionary<int, double> ByProcess);

    /// <summary>Aggregates raw counter instances.</summary>
    /// <param name="instances">Instance name/value pairs.</param>
    /// <returns>The aggregate.</returns>
    public static Aggregate Compute(IEnumerable<KeyValuePair<string, double>> instances)
    {
        ArgumentNullException.ThrowIfNull(instances);
        var perEngine = new Dictionary<(long Luid, int Phys, int Eng), (double Sum, bool Is3D)>();
        var perProcess = new Dictionary<int, double>();
        foreach (var (name, value) in instances)
        {
            if (!GpuEngineInstance.TryParse(name, out var engine) || engine is null || value <= 0)
            {
                continue;
            }

            var key = (engine.AdapterLuid, engine.PhysicalIndex, engine.EngineIndex);
            perEngine.TryGetValue(key, out var current);
            perEngine[key] = (current.Sum + value, current.Is3D || engine.EngineType.Equals("3D", StringComparison.OrdinalIgnoreCase));
            perProcess[engine.ProcessId] = Math.Max(perProcess.GetValueOrDefault(engine.ProcessId), value);
        }

        var byAdapter = new Dictionary<long, double>();
        var byAdapter3D = new Dictionary<long, double>();
        foreach (var ((luid, _, _), (sum, is3D)) in perEngine)
        {
            var capped = Math.Min(100, sum);
            byAdapter[luid] = Math.Max(byAdapter.GetValueOrDefault(luid), capped);
            if (is3D)
            {
                byAdapter3D[luid] = Math.Max(byAdapter3D.GetValueOrDefault(luid), capped);
            }
        }

        return new Aggregate(byAdapter, byAdapter3D, perProcess.ToDictionary(p => p.Key, p => Math.Min(100, p.Value)));
    }
}
