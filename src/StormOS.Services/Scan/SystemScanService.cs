using Microsoft.Extensions.Logging;
using StormOS.Core.Scan;

namespace StormOS.Services.Scan;

/// <summary>Runs every registered <see cref="ISystemScanCheck"/> and combines the results.</summary>
public sealed class SystemScanService(IEnumerable<ISystemScanCheck> checks, ILogger<SystemScanService> logger, TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    /// <summary>Runs the scan.</summary>
    /// <param name="progress">Progress (area names).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<SystemScanResult> RunAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var performed = new List<ScanCheck>();
        var findings = new List<ScanFinding>();
        foreach (var check in checks)
        {
            progress?.Report(check.Area);
            try
            {
                var (checkList, found) = await check.RunAsync(cancellationToken).ConfigureAwait(false);
                performed.AddRange(checkList);
                findings.AddRange(found);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
#pragma warning disable CA1031 // One failing area must not abort the whole scan; it is reported instead.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                logger.LogWarning(ex, "Scan area {Area} failed", check.Area);
                performed.Add(new ScanCheck(check.Area, false, $"Storm OS could not check {check.Area.ToLowerInvariant()}."));
            }
        }

        return new SystemScanResult
        {
            Timestamp = _time.GetUtcNow(),
            Checks = performed,
            Findings = findings.OrderByDescending(f => f.Severity).ThenBy(f => f.Area, StringComparer.Ordinal).ToList(),
        };
    }
}
