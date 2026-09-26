using Microsoft.Extensions.Logging;
using StormOS.Core.History;
using StormOS.Core.Optimization;

namespace StormOS.Optimization.Engine;

/// <summary>
/// Executes rules with a strict lifecycle: validate → detect → snapshot → apply → verify → record.
/// A failed apply or verification triggers an automatic rollback from the snapshot.
/// The engine only exposes rules matching its executor (user rules in the app, admin rules in the service).
/// </summary>
public sealed class OptimizationEngine : IOptimizationEngine, IDisposable
{
    private readonly Dictionary<string, IOptimizationRule> _rules;
    private readonly IOptimizationJournal _journal;
    private readonly IHistoryStore? _history;
    private readonly OptimizationExecutor _executor;
    private readonly ILogger<OptimizationEngine> _logger;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _mutex = new(1, 1);

    /// <summary>Initializes a new instance of the <see cref="OptimizationEngine"/> class.</summary>
    /// <param name="rules">All registered rules.</param>
    /// <param name="journal">Journal.</param>
    /// <param name="executor">Executor description.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="history">Optional history store.</param>
    /// <param name="timeProvider">Time source.</param>
    public OptimizationEngine(IEnumerable<IOptimizationRule> rules, IOptimizationJournal journal, OptimizationExecutor executor, ILogger<OptimizationEngine> logger, IHistoryStore? history = null, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(executor);
        _executor = executor;
        _rules = rules.Where(r => r.RequiresAdmin == executor.IsService).ToDictionary(r => r.Id, StringComparer.Ordinal);
        _journal = journal;
        _history = history;
        _logger = logger;
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public IReadOnlyList<IOptimizationRule> Rules => [.. _rules.Values];

    /// <inheritdoc />
    public async Task<RuleDetection> DetectAsync(string ruleId, IReadOnlyDictionary<string, string>? parameters, CancellationToken cancellationToken = default)
    {
        if (!_rules.TryGetValue(ruleId, out var rule))
        {
            return new RuleDetection(DetectionState.Unsupported, "-", "-", "This optimization is not available here.");
        }

        if (ParameterValidation.Validate(rule, parameters) is { } error)
        {
            return new RuleDetection(DetectionState.Error, "-", "-", error);
        }

        if (!rule.SupportedOs.Supports(_executor.WindowsBuild))
        {
            return new RuleDetection(DetectionState.Unsupported, "-", "-", $"Requires Windows build {rule.SupportedOs.MinBuild} or later.");
        }

        try
        {
            return await rule.DetectAsync(Context(parameters, "detect"), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            _logger.LogWarning(ex, "Detection of {Rule} failed", rule.Id);
            return new RuleDetection(DetectionState.Error, "-", "-", "The current state could not be read.");
        }
    }

    /// <inheritdoc />
    public async Task<OptimizationRecord> ApplyAsync(string ruleId, IReadOnlyDictionary<string, string>? parameters, string requestedBy, CancellationToken cancellationToken = default)
    {
        parameters ??= new Dictionary<string, string>();
        var record = new OptimizationRecord
        {
            Id = Guid.NewGuid(),
            Timestamp = _time.GetUtcNow(),
            RuleId = ruleId,
            RuleName = ruleId,
            Parameters = parameters,
            User = requestedBy,
            Executor = _executor.Name,
            Rollback = RollbackStatus.Unavailable,
        };

        if (!_rules.TryGetValue(ruleId, out var rule))
        {
            return await FinishAsync(record with { Outcome = OptimizationOutcome.Failed, Message = "This optimization is not available here." }, cancellationToken).ConfigureAwait(false);
        }

        record = record with { RuleName = rule.Name, RequiresRestart = rule.RequiresRestart };
        if (ParameterValidation.Validate(rule, parameters) is { } error)
        {
            return await FinishAsync(record with { Outcome = OptimizationOutcome.Failed, Message = error }, cancellationToken).ConfigureAwait(false);
        }

        if (!rule.SupportedOs.Supports(_executor.WindowsBuild))
        {
            return await FinishAsync(record with { Outcome = OptimizationOutcome.Failed, Message = $"Requires Windows build {rule.SupportedOs.MinBuild} or later." }, cancellationToken).ConfigureAwait(false);
        }

        if (rule.RequiresAdmin && !_executor.IsElevated)
        {
            return await FinishAsync(record with { Outcome = OptimizationOutcome.Failed, Message = "Administrative rights are required. Make sure the STORM OS service is running." }, cancellationToken).ConfigureAwait(false);
        }

        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ExecuteAsync(rule, record, parameters, requestedBy, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _mutex.Release();
        }
    }

    /// <inheritdoc />
    public async Task<OptimizationRecord> RollbackAsync(Guid changeId, string requestedBy, CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await RollbackCoreAsync(changeId, requestedBy, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _mutex.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OptimizationRecord>> RollbackAllAsync(string requestedBy, CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var results = new List<OptimizationRecord>();
            foreach (var active in await _journal.ListActiveAsync(cancellationToken).ConfigureAwait(false))
            {
                if (active.Executor == _executor.Name)
                {
                    results.Add(await RollbackCoreAsync(active.Id, requestedBy, cancellationToken).ConfigureAwait(false));
                }
            }

            return results;
        }
        finally
        {
            _mutex.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose() => _mutex.Dispose();

    private static bool IsExpected(Exception ex) =>
        ex is InvalidOperationException or UnauthorizedAccessException or IOException or ArgumentException or KeyNotFoundException
            or System.Security.SecurityException or System.ComponentModel.Win32Exception or System.Runtime.InteropServices.COMException or NotSupportedException;

    private async Task<OptimizationRecord> ExecuteAsync(IOptimizationRule rule, OptimizationRecord record, IReadOnlyDictionary<string, string> parameters, string requestedBy, CancellationToken cancellationToken)
    {
        var context = Context(parameters, requestedBy);
        RuleDetection detection;
        try
        {
            detection = await rule.DetectAsync(context, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            _logger.LogWarning(ex, "Pre-check of {Rule} failed", rule.Id);
            return await FinishAsync(record with { Outcome = OptimizationOutcome.Failed, Message = "The current state could not be read, nothing was changed." }, cancellationToken).ConfigureAwait(false);
        }

        record = record with { Before = detection.CurrentValue };
        switch (detection.State)
        {
            case DetectionState.AlreadyApplied:
                return await FinishAsync(record with { Outcome = OptimizationOutcome.Skipped, After = detection.CurrentValue, Message = detection.Explanation }, cancellationToken).ConfigureAwait(false);
            case DetectionState.NotApplicable or DetectionState.Unsupported:
                return await FinishAsync(record with { Outcome = OptimizationOutcome.Skipped, After = detection.CurrentValue, Message = detection.Explanation }, cancellationToken).ConfigureAwait(false);
            case DetectionState.Error:
                return await FinishAsync(record with { Outcome = OptimizationOutcome.Failed, Message = detection.Explanation }, cancellationToken).ConfigureAwait(false);
        }

        RuleSnapshot snapshot;
        try
        {
            snapshot = await rule.CaptureAsync(context, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            _logger.LogWarning(ex, "Snapshot of {Rule} failed", rule.Id);
            return await FinishAsync(record with { Outcome = OptimizationOutcome.Failed, Message = "A backup of the current state could not be created, so nothing was changed." }, cancellationToken).ConfigureAwait(false);
        }

        record = record with { Snapshot = snapshot };
        try
        {
            await rule.ApplyAsync(context, cancellationToken).ConfigureAwait(false);
            var verification = await rule.VerifyAsync(context, cancellationToken).ConfigureAwait(false);
            if (verification.Verified)
            {
                _logger.LogInformation("Applied {Rule} for {User}: {Before} -> {After}", rule.Id, requestedBy, detection.CurrentValue, verification.ObservedValue);
                return await FinishAsync(record with
                {
                    Outcome = OptimizationOutcome.Applied,
                    After = verification.ObservedValue,
                    Message = rule.RequiresRestart ? "Applied. Restart Windows for the change to take full effect." : "Applied and verified.",
                    Rollback = rule.CanRollback ? RollbackStatus.Available : RollbackStatus.Unavailable,
                }, cancellationToken).ConfigureAwait(false);
            }

            _logger.LogWarning("Verification of {Rule} failed: {Message}", rule.Id, verification.Message);
            return await AutoRollbackAsync(rule, context, snapshot, record with { After = verification.ObservedValue }, "The change could not be verified", cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            _logger.LogWarning(ex, "Applying {Rule} failed", rule.Id);
            return await AutoRollbackAsync(rule, context, snapshot, record, "The change could not be applied", cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<OptimizationRecord> AutoRollbackAsync(IOptimizationRule rule, OptimizationContext context, RuleSnapshot snapshot, OptimizationRecord record, string reason, CancellationToken cancellationToken)
    {
        if (!rule.CanRollback)
        {
            return await FinishAsync(record with { Outcome = OptimizationOutcome.Failed, Message = reason + "." }, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            var restored = await rule.RollbackAsync(context, snapshot, cancellationToken).ConfigureAwait(false);
            return await FinishAsync(record with
            {
                Outcome = restored.Verified ? OptimizationOutcome.FailedRolledBack : OptimizationOutcome.FailedRollbackFailed,
                Message = restored.Verified ? reason + "; the previous state was restored." : reason + " and the previous state could not be verified. Use Restore in History.",
                Rollback = restored.Verified ? RollbackStatus.RolledBack : RollbackStatus.Available,
                RolledBackAt = restored.Verified ? _time.GetUtcNow() : null,
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            _logger.LogError(ex, "Automatic rollback of {Rule} failed", rule.Id);
            return await FinishAsync(record with { Outcome = OptimizationOutcome.FailedRollbackFailed, Message = reason + " and the automatic rollback failed. Use Restore in History.", Rollback = RollbackStatus.Available }, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<OptimizationRecord> RollbackCoreAsync(Guid changeId, string requestedBy, CancellationToken cancellationToken)
    {
        var record = await _journal.GetAsync(changeId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("The change was not found.");
        if (record.Rollback != RollbackStatus.Available || record.Snapshot is null)
        {
            return record;
        }

        if (!_rules.TryGetValue(record.RuleId, out var rule))
        {
            return record with { Message = "This change is managed by another STORM OS component." };
        }

        RuleVerification result;
        try
        {
            result = await rule.RollbackAsync(Context(record.Parameters, requestedBy), record.Snapshot, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            _logger.LogError(ex, "Rollback of {Rule} ({Change}) failed", rule.Id, changeId);
            result = new RuleVerification(false, "-", "The previous state could not be restored.");
        }

        var updated = record with
        {
            Rollback = result.Verified ? RollbackStatus.RolledBack : RollbackStatus.Failed,
            RolledBackAt = result.Verified ? _time.GetUtcNow() : null,
            Message = result.Verified ? "Restored and verified." : result.Message ?? "The restore could not be verified.",
        };
        await _journal.SaveAsync(updated, cancellationToken).ConfigureAwait(false);
        if (_history is not null)
        {
            await _history.AddEventAsync(new HistoryEvent
            {
                Timestamp = _time.GetUtcNow(),
                Category = HistoryCategory.Optimization,
                Action = $"Restored: {record.RuleName}",
                Result = result.Verified ? EventResult.Success : EventResult.Failed,
                Details = $"{record.After} → {result.ObservedValue}",
                Rollback = updated.Rollback,
                RelatedId = record.Id.ToString(),
            }, cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation("Rollback of {Rule} ({Change}) by {User}: {Result}", rule.Id, changeId, requestedBy, updated.Rollback);
        return updated;
    }

    private async Task<OptimizationRecord> FinishAsync(OptimizationRecord record, CancellationToken cancellationToken)
    {
        await _journal.SaveAsync(record, cancellationToken).ConfigureAwait(false);
        if (_history is not null && record.Outcome != OptimizationOutcome.Skipped)
        {
            await _history.AddEventAsync(new HistoryEvent
            {
                Timestamp = record.Timestamp,
                Category = HistoryCategory.Optimization,
                Action = record.RuleName,
                Result = record.Outcome switch
                {
                    OptimizationOutcome.Applied => EventResult.Success,
                    OptimizationOutcome.FailedRolledBack => EventResult.Partial,
                    _ => EventResult.Failed,
                },
                Details = $"{record.Before} → {record.After}. {record.Message}".Trim(),
                Rollback = record.Rollback,
                RelatedId = record.Id.ToString(),
            }, cancellationToken).ConfigureAwait(false);
        }

        return record;
    }

    private OptimizationContext Context(IReadOnlyDictionary<string, string>? parameters, string requestedBy) =>
        new(parameters, requestedBy, _executor.WindowsBuild, _executor.IsElevated);
}
