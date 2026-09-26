namespace StormOS.Core.Hardware;

/// <summary>Collects a hardware inventory. Implementations must tolerate missing information.</summary>
public interface IHardwareInventoryProvider
{
    /// <summary>Collects the current hardware inventory.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The inventory including any issues encountered.</returns>
    Task<HardwareInventory> GetInventoryAsync(CancellationToken cancellationToken = default);
}

/// <summary>Provides the operating system description.</summary>
public interface IOperatingSystemInfoProvider
{
    /// <summary>Reads the current operating system information.</summary>
    /// <returns>Operating system information.</returns>
    OsInfo GetOsInfo();
}
