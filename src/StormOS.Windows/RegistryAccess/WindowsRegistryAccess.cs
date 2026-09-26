using Microsoft.Win32;

namespace StormOS.Windows.RegistryAccess;

/// <summary><see cref="IRegistryAccess"/> backed by the Windows registry (64-bit view).</summary>
public sealed class WindowsRegistryAccess : IRegistryAccess
{
    /// <inheritdoc />
    public RegistryValue? GetValue(RegistryHive hive, string keyPath, string valueName)
    {
        using var key = Open(hive, keyPath, writable: false);
        if (key is null)
        {
            return null;
        }

        var raw = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (raw is null)
        {
            return null;
        }

        return key.GetValueKind(valueName) switch
        {
            RegistryValueKind.DWord => new RegistryValue(RegistryKind.DWord, Convert.ToInt32(raw, System.Globalization.CultureInfo.InvariantCulture)),
            RegistryValueKind.QWord => new RegistryValue(RegistryKind.QWord, Convert.ToInt64(raw, System.Globalization.CultureInfo.InvariantCulture)),
            RegistryValueKind.Binary => new RegistryValue(RegistryKind.Binary, (byte[])raw),
            RegistryValueKind.MultiString => new RegistryValue(RegistryKind.MultiSz, (string[])raw),
            RegistryValueKind.ExpandString => new RegistryValue(RegistryKind.ExpandSz, (string)raw),
            _ => new RegistryValue(RegistryKind.Sz, raw.ToString() ?? string.Empty),
        };
    }

    /// <inheritdoc />
    public void SetValue(RegistryHive hive, string keyPath, string valueName, RegistryValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        using var baseKey = BaseKey(hive);
        using var key = baseKey.CreateSubKey(keyPath, writable: true) ?? throw new InvalidOperationException($"Could not open {hive}\\{keyPath}.");
        var kind = value.Kind switch
        {
            RegistryKind.DWord => RegistryValueKind.DWord,
            RegistryKind.QWord => RegistryValueKind.QWord,
            RegistryKind.Binary => RegistryValueKind.Binary,
            RegistryKind.MultiSz => RegistryValueKind.MultiString,
            RegistryKind.ExpandSz => RegistryValueKind.ExpandString,
            _ => RegistryValueKind.String,
        };
        key.SetValue(valueName, value.Value, kind);
    }

    /// <inheritdoc />
    public void DeleteValue(RegistryHive hive, string keyPath, string valueName)
    {
        using var key = Open(hive, keyPath, writable: true);
        key?.DeleteValue(valueName, throwOnMissingValue: false);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetValueNames(RegistryHive hive, string keyPath)
    {
        using var key = Open(hive, keyPath, writable: false);
        return key?.GetValueNames() ?? [];
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetSubKeyNames(RegistryHive hive, string keyPath)
    {
        using var key = Open(hive, keyPath, writable: false);
        return key?.GetSubKeyNames() ?? [];
    }

    private static RegistryKey? Open(RegistryHive hive, string keyPath, bool writable)
    {
        using var baseKey = BaseKey(hive);
        return baseKey.OpenSubKey(keyPath, writable);
    }

    private static RegistryKey BaseKey(RegistryHive hive) =>
        RegistryKey.OpenBaseKey(hive == RegistryHive.LocalMachine ? Microsoft.Win32.RegistryHive.LocalMachine : Microsoft.Win32.RegistryHive.CurrentUser, RegistryView.Registry64);
}
