using Microsoft.Extensions.Logging;
using StormOS.Core.Optimization;
using StormOS.Services.Optimization;
using StormOS.Windows.Platform;

namespace StormOS.App.Services;

/// <summary>
/// The single path through which the UI changes the system: detect → explain and ask for consent → apply (with
/// snapshot, verification and automatic rollback in the engine) → report the verified outcome.
/// </summary>
public sealed class ChangeService(OptimizationCoordinator coordinator, DialogService dialogs, NotificationService notifications, ILogger<ChangeService> logger)
{
    private IReadOnlyList<OptimizationItem>? _catalog;
    private string? _catalogNote;
    private DateTimeOffset _catalogLoaded;

    /// <summary>Gets the rule catalog (cached briefly).</summary>
    /// <param name="refresh">Forces a reload.</param>
    /// <returns>Items and an optional note about unavailable service rules.</returns>
    public async Task<(IReadOnlyList<OptimizationItem> Items, string? Note)> CatalogAsync(bool refresh = false)
    {
        if (refresh || _catalog is null || DateTimeOffset.UtcNow - _catalogLoaded > TimeSpan.FromSeconds(30))
        {
            (_catalog, _catalogNote) = await coordinator.ListAsync();
            _catalogLoaded = DateTimeOffset.UtcNow;
        }

        return (_catalog, _catalogNote);
    }

    /// <summary>Applies a rule after showing what will change and receiving explicit consent.</summary>
    /// <param name="ruleId">Rule id.</param>
    /// <param name="parameters">Parameters.</param>
    /// <param name="reason">Why the change is suggested (shown in the dialog).</param>
    /// <param name="requestedBy">Journal attribution.</param>
    /// <returns>The record, or <see langword="null"/> when nothing was applied.</returns>
    public async Task<OptimizationRecord?> ApplyWithConsentAsync(string ruleId, IReadOnlyDictionary<string, string>? parameters, string? reason = null, string? requestedBy = null)
    {
        var (items, note) = await CatalogAsync();
        var item = items.FirstOrDefault(i => i.Descriptor.Id == ruleId);
        if (item is null)
        {
            notifications.Warning(note ?? $"The change '{ruleId}' is not available on this system.", "Not available");
            return null;
        }

        var detection = await coordinator.DetectAsync(item.Executor, ruleId, parameters);
        if (!detection.IsSuccess)
        {
            notifications.Warning(detection.Error.Message, item.Descriptor.Name);
            return null;
        }

        var state = detection.Value!;
        switch (state.State)
        {
            case DetectionState.AlreadyApplied:
                notifications.Info($"Already in place: {state.CurrentValue}.", item.Descriptor.Name);
                return null;
            case DetectionState.NotApplicable or DetectionState.Unsupported or DetectionState.Error:
                notifications.Info(state.Explanation, item.Descriptor.Name);
                return null;
        }

        var description = item.Descriptor.Description;
        if (!string.IsNullOrWhiteSpace(reason))
        {
            description += Environment.NewLine + Environment.NewLine + "Why: " + reason;
        }

        if (item.Descriptor.RequiresAdmin)
        {
            description += Environment.NewLine + Environment.NewLine + "This change is performed by the STORM OS service with administrator rights.";
        }

        if (!await dialogs.ConfirmChangeAsync(item.Descriptor.Name, description, item.Descriptor.RiskLevel, item.Descriptor.CanRollback, item.Descriptor.RequiresRestart, state.CurrentValue, state.TargetValue))
        {
            return null;
        }

        return await ApplyConfirmedAsync(item, parameters, requestedBy);
    }

    /// <summary>Applies a rule the user has already confirmed (for example as part of a reviewed plan).</summary>
    /// <param name="item">Catalog item.</param>
    /// <param name="parameters">Parameters.</param>
    /// <param name="requestedBy">Journal attribution.</param>
    /// <returns>The record, or <see langword="null"/> on transport failure.</returns>
    public async Task<OptimizationRecord?> ApplyConfirmedAsync(OptimizationItem item, IReadOnlyDictionary<string, string>? parameters, string? requestedBy = null)
    {
        ArgumentNullException.ThrowIfNull(item);
        var result = await coordinator.ApplyAsync(item.Executor, item.Descriptor.Id, parameters, requestedBy ?? Elevation.CurrentUserName);
        if (!result.IsSuccess)
        {
            notifications.Error(result.Error.Message, item.Descriptor.Name);
            return null;
        }

        var record = result.Value!;
        logger.LogInformation("Change {Rule} finished with {Outcome}", record.RuleId, record.Outcome);
        switch (record.Outcome)
        {
            case OptimizationOutcome.Applied:
                notifications.Success($"{record.Before} → {record.After}. Verified.{(record.RequiresRestart ? " Restart Windows to complete the change." : string.Empty)}", item.Descriptor.Name);
                break;
            case OptimizationOutcome.Skipped:
                notifications.Info(record.Message, item.Descriptor.Name);
                break;
            case OptimizationOutcome.FailedRolledBack:
                notifications.Warning($"{record.Message} The previous state was restored automatically.", item.Descriptor.Name);
                break;
            default:
                notifications.Error(record.Message, item.Descriptor.Name);
                break;
        }

        return record;
    }

    /// <summary>Restores one change after confirmation.</summary>
    /// <param name="record">The change.</param>
    /// <returns><see langword="true"/> when restored.</returns>
    public async Task<bool> RestoreWithConsentAsync(OptimizationRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (!await dialogs.ConfirmAsync("Restore", $"Restore \"{record.RuleName}\" to its previous state ({record.Before})?", "Restore"))
        {
            return false;
        }

        var result = await coordinator.RestoreAsync(record, Elevation.CurrentUserName);
        if (!result.IsSuccess)
        {
            notifications.Error(result.Error.Message, "Restore");
            return false;
        }

        if (result.Value!.Rollback == RollbackStatus.RolledBack)
        {
            notifications.Success($"{record.RuleName} restored to {record.Before}.", "Restore");
            return true;
        }

        notifications.Warning(result.Value.Message, "Restore");
        return false;
    }

    /// <summary>Restores every active change after confirmation.</summary>
    /// <returns>The number of restored changes.</returns>
    public async Task<int> RestoreAllWithConsentAsync()
    {
        if (!await dialogs.ConfirmAsync("Restore all", "Restore every active STORM OS change to the state captured before it was applied?", "Restore all"))
        {
            return 0;
        }

        var (records, note) = await coordinator.RestoreAllAsync(Elevation.CurrentUserName);
        var restored = records.Count(r => r.Rollback == RollbackStatus.RolledBack);
        var failed = records.Count(r => r.Rollback == RollbackStatus.Failed);
        var message = records.Count == 0 ? "There were no active changes." : $"{restored} change(s) restored{(failed > 0 ? $", {failed} could not be restored (see History)" : string.Empty)}.";
        if (note is not null)
        {
            message += " " + note;
        }

        if (failed > 0 || note is not null)
        {
            notifications.Warning(message, "Restore all");
        }
        else
        {
            notifications.Success(message, "Restore all");
        }

        _catalog = null;
        return restored;
    }

    /// <summary>Invalidates the cached catalog after changes.</summary>
    public void Invalidate() => _catalog = null;
}
