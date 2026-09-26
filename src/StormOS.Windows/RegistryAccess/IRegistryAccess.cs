namespace StormOS.Windows.RegistryAccess;

/// <summary>Registry hives STORM OS may touch.</summary>
public enum RegistryHive
{
    /// <summary>HKEY_CURRENT_USER of the calling process.</summary>
    CurrentUser,

    /// <summary>HKEY_LOCAL_MACHINE.</summary>
    LocalMachine,
}

/// <summary>Registry value kinds supported by STORM OS rules.</summary>
public enum RegistryKind
{
    /// <summary>REG_SZ.</summary>
    Sz,

    /// <summary>REG_EXPAND_SZ.</summary>
    ExpandSz,

    /// <summary>REG_DWORD.</summary>
    DWord,

    /// <summary>REG_QWORD.</summary>
    QWord,

    /// <summary>REG_BINARY.</summary>
    Binary,

    /// <summary>REG_MULTI_SZ.</summary>
    MultiSz,
}

/// <summary>A registry value with its kind.</summary>
/// <param name="Kind">Value kind.</param>
/// <param name="Value">The value: string, int, long, byte[] or string[].</param>
public sealed record RegistryValue(RegistryKind Kind, object Value)
{
    /// <summary>Serializes the value to a portable string for snapshots.</summary>
    /// <returns>Serialized value.</returns>
    public string Serialize() => Kind switch
    {
        RegistryKind.DWord => ((int)Value).ToString(System.Globalization.CultureInfo.InvariantCulture),
        RegistryKind.QWord => ((long)Value).ToString(System.Globalization.CultureInfo.InvariantCulture),
        RegistryKind.Binary => Convert.ToBase64String((byte[])Value),
        RegistryKind.MultiSz => string.Join('\0', (string[])Value),
        _ => (string)Value,
    };

    /// <summary>Deserializes a value produced by <see cref="Serialize"/>.</summary>
    /// <param name="kind">Value kind.</param>
    /// <param name="data">Serialized data.</param>
    /// <returns>The value.</returns>
    public static RegistryValue Deserialize(RegistryKind kind, string data) => kind switch
    {
        RegistryKind.DWord => new(kind, int.Parse(data, System.Globalization.CultureInfo.InvariantCulture)),
        RegistryKind.QWord => new(kind, long.Parse(data, System.Globalization.CultureInfo.InvariantCulture)),
        RegistryKind.Binary => new(kind, Convert.FromBase64String(data)),
        RegistryKind.MultiSz => new(kind, data.Split('\0')),
        _ => new(kind, data),
    };

    /// <inheritdoc />
    public bool Equals(RegistryValue? other) =>
        other is not null && Kind == other.Kind && Serialize() == other.Serialize();

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Kind, Serialize());
}

/// <summary>Registry access abstraction, allowing rules to be tested without touching the real registry.</summary>
public interface IRegistryAccess
{
    /// <summary>Reads a value.</summary>
    /// <param name="hive">Hive.</param>
    /// <param name="keyPath">Key path.</param>
    /// <param name="valueName">Value name.</param>
    /// <returns>The value or <see langword="null"/> when it does not exist.</returns>
    RegistryValue? GetValue(RegistryHive hive, string keyPath, string valueName);

    /// <summary>Writes a value, creating the key when necessary.</summary>
    /// <param name="hive">Hive.</param>
    /// <param name="keyPath">Key path.</param>
    /// <param name="valueName">Value name.</param>
    /// <param name="value">Value.</param>
    void SetValue(RegistryHive hive, string keyPath, string valueName, RegistryValue value);

    /// <summary>Deletes a value if it exists.</summary>
    /// <param name="hive">Hive.</param>
    /// <param name="keyPath">Key path.</param>
    /// <param name="valueName">Value name.</param>
    void DeleteValue(RegistryHive hive, string keyPath, string valueName);

    /// <summary>Lists value names of a key.</summary>
    /// <param name="hive">Hive.</param>
    /// <param name="keyPath">Key path.</param>
    /// <returns>Value names, empty when the key does not exist.</returns>
    IReadOnlyList<string> GetValueNames(RegistryHive hive, string keyPath);

    /// <summary>Lists sub key names of a key.</summary>
    /// <param name="hive">Hive.</param>
    /// <param name="keyPath">Key path.</param>
    /// <returns>Sub key names, empty when the key does not exist.</returns>
    IReadOnlyList<string> GetSubKeyNames(RegistryHive hive, string keyPath);
}
