using StormOS.Core.Common;
using StormOS.Core.Optimization;
using StormOS.Services.Client;

namespace StormOS.Services.Optimization;

/// <summary>A rule as presented in the UI, including where it executes.</summary>
/// <param name="Descriptor">Rule description and detection.</param>
/// <param name="Executor">"app" or "service".</param>
public sealed record OptimizationItem(RuleDescriptor Descriptor, string Executor);

/// <summary>
/// Presents user-level rules (executed in the non-elevated app) and admin rules (executed by the service) as one
/// catalog, and routes apply/restore requests to the right executor.
/// </summary>
public sealed class OptimizationCoordinator(IOptimizationEngine local, IStormServiceClient service)
{
    /// <summary>Lists all rules with their current detection.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Rules; service rules are omitted (with a note) when the service is unavailable.</returns>
    public async Task<(IReadOnlyList<OptimizationItem> Items, string? ServiceNote)> ListAsync(CancellationToken cancellationToken = default)
    {
        var items = new List<OptimizationItem>();
        foreach (var rule in local.Rules)
        {
            var detection = rule.Parameters.Any(p => p.Required) ? null : await local.DetectAsync(rule.Id, null, cancellationToken).ConfigureAwait(false);
            items.Add(new OptimizationItem(RuleDescriptor.From(rule, detection), "app"));
        }

        string? note = null;
        var remote = await service.GetRulesAsync(cancellationToken).ConfigureAwait(false);
        if (remote.IsSuccess)
        {
            items.AddRange(remote.Value!.Select(d => new OptimizationItem(d, "service")));
        }
        else
        {
            note = "Optimizations that need administrator rights are unavailable because the STORM OS service is not running.";
        }

        return (items, note);
    }

    /// <summary>Detects a rule's state.</summary>
    /// <param name="executor">Executor.</param>
    /// <param name="ruleId">Rule id.</param>
    /// <param name="parameters">Parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The detection.</returns>
    public async Task<Result<RuleDetection>> DetectAsync(string executor, string ruleId, IReadOnlyDictionary<string, string>? parameters, CancellationToken cancellationToken = default) =>
        executor == "service"
            ? await service.DetectAsync(ruleId, parameters, cancellationToken).ConfigureAwait(false)
            : Result<RuleDetection>.Ok(await local.DetectAsync(ruleId, parameters, cancellationToken).ConfigureAwait(false));

    /// <summary>Applies a rule after the user confirmed it.</summary>
    /// <param name="executor">Executor.</param>
    /// <param name="ruleId">Rule id.</param>
    /// <param name="parameters">Parameters.</param>
    /// <param name="user">Requesting user.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The record.</returns>
    public async Task<Result<OptimizationRecord>> ApplyAsync(string executor, string ruleId, IReadOnlyDictionary<string, string>? parameters, string user, CancellationToken cancellationToken = default) =>
        executor == "service"
            ? await service.ApplyAsync(ruleId, parameters, cancellationToken).ConfigureAwait(false)
            : Result<OptimizationRecord>.Ok(await local.ApplyAsync(ruleId, parameters, user, cancellationToken).ConfigureAwait(false));

    /// <summary>Restores a change.</summary>
    /// <param name="record">The record to restore.</param>
    /// <param name="user">Requesting user.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated record.</returns>
    public async Task<Result<OptimizationRecord>> RestoreAsync(OptimizationRecord record, string user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        return record.Executor == "service"
            ? await service.RestoreAsync(record.Id, cancellationToken).ConfigureAwait(false)
            : Result<OptimizationRecord>.Ok(await local.RollbackAsync(record.Id, user, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>Restores all active changes of both executors (service first, newest first within each).</summary>
    /// <param name="user">Requesting user.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Restored records and an optional note.</returns>
    public async Task<(IReadOnlyList<OptimizationRecord> Records, string? Note)> RestoreAllAsync(string user, CancellationToken cancellationToken = default)
    {
        var records = new List<OptimizationRecord>();
        string? note = null;
        var remote = await service.RestoreAllAsync(cancellationToken).ConfigureAwait(false);
        if (remote.IsSuccess)
        {
            records.AddRange(remote.Value!);
        }
        else
        {
            note = "Service-side changes could not be restored because the STORM OS service is unavailable.";
        }

        records.AddRange(await local.RollbackAllAsync(user, cancellationToken).ConfigureAwait(false));
        return (records, note);
    }
}
