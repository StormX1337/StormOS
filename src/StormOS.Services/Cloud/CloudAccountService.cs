using Microsoft.Extensions.Logging;
using StormOS.Core.Common;
using StormOS.Core.Hardware;
using StormOS.Core.Licensing;
using StormOS.Core.Settings;
using StormOS.Services.Licensing;

namespace StormOS.Services.Cloud;

/// <summary>
/// Keeps a signed-in PC connected to its STORM Cloud account: registers the PC once and refreshes the
/// device-bound license token. Does nothing unless the user enabled STORM Cloud and signed in.
/// </summary>
public sealed class CloudAccountService(
    CloudClient cloud,
    EntitlementService entitlements,
    ISettingsStore settings,
    IHardwareInventoryProvider inventory,
    ILogger<CloudAccountService> logger)
{
    /// <summary>Registers the PC if needed and refreshes the license.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The current entitlements, or the failure (the previous license stays in effect).</returns>
    public async Task<Result<Entitlements>> SyncAsync(CancellationToken cancellationToken = default)
    {
        var current = settings.Current.Cloud;
        if (!current.Enabled || current.AccountEmail is null)
        {
            return Result<Entitlements>.Ok(entitlements.Current);
        }

        if (current.DeviceId is null)
        {
            var registered = await cloud.RegisterDeviceAsync(DeviceName(), await HardwareSummaryAsync(cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
            if (!registered.IsSuccess)
            {
                logger.LogInformation("PC registration with STORM Cloud failed: {Reason}", registered.Error.Message);
                return Result<Entitlements>.Fail(registered.Error);
            }
        }

        var refreshed = await entitlements.RefreshAsync(cloud, cancellationToken).ConfigureAwait(false);
        if (!refreshed.IsSuccess)
        {
            logger.LogInformation("License refresh failed; keeping the stored license: {Reason}", refreshed.Error.Message);
        }

        return refreshed;
    }

    /// <summary>The PC name shown on the account page (at most 64 characters).</summary>
    /// <returns>The name.</returns>
    public static string DeviceName()
    {
        var name = Environment.MachineName.Trim();
        return name.Length is > 0 and <= 64 ? name : "Windows PC";
    }

    private async Task<string> HardwareSummaryAsync(CancellationToken cancellationToken)
    {
        try
        {
            var hardware = await inventory.GetInventoryAsync(cancellationToken).ConfigureAwait(false);
            var gpu = hardware.Gpus.Where(g => !g.IsSoftware).OrderByDescending(g => g.DedicatedMemoryBytes).FirstOrDefault()?.Name;
            var summary = string.Join(" · ", new[] { hardware.Cpu.Name, gpu, hardware.Memory.TotalBytes > 0 ? Units.FormatBytes(hardware.Memory.TotalBytes, 0) + " RAM" : null }.Where(s => !string.IsNullOrWhiteSpace(s)));
            return summary.Length <= 256 ? summary : summary[..256];
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or IOException)
        {
            logger.LogDebug(ex, "Hardware summary unavailable for device registration");
            return string.Empty;
        }
    }
}
