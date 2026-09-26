using System.Management;

namespace StormOS.Windows.Wmi;

/// <summary>Bounded WMI/CIM queries returning plain dictionaries.</summary>
public static class WmiQuery
{
    /// <summary>Default query timeout.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Runs a WQL query.</summary>
    /// <param name="scope">Namespace, for example "root\cimv2".</param>
    /// <param name="wql">The query.</param>
    /// <param name="timeout">Timeout.</param>
    /// <returns>Rows as property dictionaries.</returns>
    public static IReadOnlyList<IReadOnlyDictionary<string, object?>> Query(string scope, string wql, TimeSpan? timeout = null)
    {
        var options = new System.Management.EnumerationOptions { Timeout = timeout ?? DefaultTimeout, ReturnImmediately = true, Rewindable = false };
        using var searcher = new ManagementObjectSearcher(new ManagementScope(scope), new ObjectQuery(wql), options);
        using var results = searcher.Get();
        var rows = new List<IReadOnlyDictionary<string, object?>>();
        foreach (var item in results)
        {
            using (item)
            {
                var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (var property in item.Properties)
                {
                    row[property.Name] = property.Value;
                }

                rows.Add(row);
            }
        }

        return rows;
    }

    /// <summary>Returns the first row, if any.</summary>
    /// <param name="rows">Rows.</param>
    /// <returns>The first row or <see langword="null"/>.</returns>
    public static IReadOnlyDictionary<string, object?>? FirstRow(this IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return rows.Count > 0 ? rows[0] : null;
    }

    /// <summary>Reads a string property.</summary>
    /// <param name="row">Row.</param>
    /// <param name="name">Property.</param>
    /// <returns>The trimmed value or <see langword="null"/>.</returns>
    public static string? Str(this IReadOnlyDictionary<string, object?> row, string name) =>
        row.TryGetValue(name, out var value) && value is not null && value.ToString() is { Length: > 0 } s ? s.Trim() : null;

    /// <summary>Reads an integer property.</summary>
    /// <param name="row">Row.</param>
    /// <param name="name">Property.</param>
    /// <returns>The value or <see langword="null"/>.</returns>
    public static long? Int64Value(this IReadOnlyDictionary<string, object?> row, string name) =>
        row.TryGetValue(name, out var value) && value is not null ? Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture) : null;

    /// <summary>Parses a CIM datetime (yyyymmddHHMMSS.mmmmmmsUUU) into a date.</summary>
    /// <param name="value">CIM datetime.</param>
    /// <returns>The date or <see langword="null"/>.</returns>
    public static DateOnly? CimDate(string? value) =>
        value is { Length: >= 8 } && DateOnly.TryParseExact(value[..8], "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var date) ? date : null;
}
