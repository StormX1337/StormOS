using System.Runtime.InteropServices;
using StormOS.Windows.Interop;

namespace StormOS.Windows.Pdh;

/// <summary>
/// A PDH (Performance Data Helper) query. Counters are added with English names so they work on every
/// display language. Rate counters need two collections before they return data.
/// </summary>
public sealed class PdhQuery : IDisposable
{
    private IntPtr _query;
    private bool _collectedOnce;

    /// <summary>Initializes a new instance of the <see cref="PdhQuery"/> class.</summary>
    /// <exception cref="InvalidOperationException">The query could not be opened.</exception>
    public PdhQuery()
    {
        var status = PdhNative.PdhOpenQuery(null, IntPtr.Zero, out _query);
        if (status != PdhNative.ErrorSuccess)
        {
            throw new InvalidOperationException($"PdhOpenQuery failed with 0x{status:X8}.");
        }
    }

    /// <summary>Gets a value indicating whether at least two collections happened (rate counters are valid).</summary>
    public bool HasRateData { get; private set; }

    /// <summary>Adds a counter. Returns <see langword="null"/> when the counter does not exist on this system.</summary>
    /// <param name="englishPath">English counter path, for example "\Processor Information(_Total)\% Processor Utility".</param>
    /// <returns>The counter or <see langword="null"/>.</returns>
    public PdhCounter? TryAddCounter(string englishPath)
    {
        ObjectDisposedException.ThrowIf(_query == IntPtr.Zero, this);
        var status = PdhNative.PdhAddEnglishCounter(_query, englishPath, IntPtr.Zero, out var counter);
        return status == PdhNative.ErrorSuccess ? new PdhCounter(counter, englishPath) : null;
    }

    /// <summary>Collects a new sample for all counters.</summary>
    /// <returns><see langword="true"/> when collection succeeded.</returns>
    public bool Collect()
    {
        ObjectDisposedException.ThrowIf(_query == IntPtr.Zero, this);
        var ok = PdhNative.PdhCollectQueryData(_query) == PdhNative.ErrorSuccess;
        if (ok)
        {
            HasRateData = _collectedOnce;
            _collectedOnce = true;
        }

        return ok;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_query != IntPtr.Zero)
        {
            _ = PdhNative.PdhCloseQuery(_query);
            _query = IntPtr.Zero;
        }
    }
}

/// <summary>A counter belonging to a <see cref="PdhQuery"/>. Handles are released with the query.</summary>
public sealed class PdhCounter
{
    private readonly IntPtr _counter;

    internal PdhCounter(IntPtr counter, string path)
    {
        _counter = counter;
        Path = path;
    }

    /// <summary>Gets the counter path.</summary>
    public string Path { get; }

    /// <summary>Reads the formatted value of a single-instance counter.</summary>
    /// <param name="value">The value.</param>
    /// <returns><see langword="true"/> when valid data was available.</returns>
    public bool TryGetValue(out double value)
    {
        var status = PdhNative.PdhGetFormattedCounterValue(_counter, PdhNative.PdhFmtDouble | PdhNative.PdhFmtNoCap100, out _, out var formatted);
        if (status == PdhNative.ErrorSuccess && formatted.CStatus is PdhNative.PdhCstatusValidData or PdhNative.PdhCstatusNewData && double.IsFinite(formatted.DoubleValue))
        {
            value = formatted.DoubleValue;
            return true;
        }

        value = 0;
        return false;
    }

    /// <summary>Reads all instances of a wildcard counter.</summary>
    /// <returns>Instance name/value pairs with valid data.</returns>
    public IReadOnlyList<KeyValuePair<string, double>> GetInstances()
    {
        uint size = 0;
        var status = PdhNative.PdhGetFormattedCounterArray(_counter, PdhNative.PdhFmtDouble | PdhNative.PdhFmtNoCap100, ref size, out _, IntPtr.Zero);
        if (status != PdhNative.PdhMoreData || size == 0)
        {
            return [];
        }

        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            status = PdhNative.PdhGetFormattedCounterArray(_counter, PdhNative.PdhFmtDouble | PdhNative.PdhFmtNoCap100, ref size, out var count, buffer);
            if (status != PdhNative.ErrorSuccess)
            {
                return [];
            }

            var result = new List<KeyValuePair<string, double>>((int)count);
            var itemSize = Marshal.SizeOf<PdhNative.FmtCounterValueItem>();
            for (var i = 0; i < count; i++)
            {
                var item = Marshal.PtrToStructure<PdhNative.FmtCounterValueItem>(buffer + (i * itemSize));
                if (item.Value.CStatus is not (PdhNative.PdhCstatusValidData or PdhNative.PdhCstatusNewData) || !double.IsFinite(item.Value.DoubleValue))
                {
                    continue;
                }

                var name = Marshal.PtrToStringUni(item.Name) ?? string.Empty;
                result.Add(new KeyValuePair<string, double>(name, item.Value.DoubleValue));
            }

            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
