using StormOS.Core.Common;
using StormOS.Core.Ipc;
using StormOS.Core.Optimization;
using StormOS.Infrastructure.Ipc;
using StormOS.Security.Validation;

namespace StormOS.Service.Handlers;

/// <summary>Shared validation for rule requests.</summary>
internal static class RuleRequestValidation
{
    public static string? Validate(RuleRequest payload)
    {
        if (!InputValidator.IsIdentifier(payload.RuleId))
        {
            return "Invalid rule id.";
        }

        if (payload.Parameters is { Count: > 16 })
        {
            return "Too many parameters.";
        }

        return payload.Parameters?.Any(p => !InputValidator.IsParameterName(p.Key) || !InputValidator.IsSafeParameterValue(p.Value)) == true
            ? "Invalid rule parameters."
            : null;
    }
}

/// <summary>optimization.rules: lists service-side rules with their current detection.</summary>
public sealed class OptimizationRulesHandler(IOptimizationEngine engine) : IpcHandler<NoPayload>
{
    /// <inheritdoc />
    public override string Operation => IpcOperations.OptimizationRules;

    /// <inheritdoc />
    protected override async Task<object?> HandleAsync(NoPayload payload, IIpcSession session, CancellationToken cancellationToken)
    {
        var list = new List<RuleDescriptor>();
        foreach (var rule in engine.Rules)
        {
            var detection = rule.Parameters.Any(p => p.Required) ? null : await engine.DetectAsync(rule.Id, null, cancellationToken).ConfigureAwait(false);
            list.Add(RuleDescriptor.From(rule, detection));
        }

        return list;
    }
}

/// <summary>optimization.detect.</summary>
public sealed class OptimizationDetectHandler(IOptimizationEngine engine) : IpcHandler<RuleRequest>
{
    /// <inheritdoc />
    public override string Operation => IpcOperations.OptimizationDetect;

    /// <inheritdoc />
    protected override string? Validate(RuleRequest payload) => RuleRequestValidation.Validate(payload);

    /// <inheritdoc />
    protected override async Task<object?> HandleAsync(RuleRequest payload, IIpcSession session, CancellationToken cancellationToken) =>
        await engine.DetectAsync(payload.RuleId, payload.Parameters, cancellationToken).ConfigureAwait(false);
}

/// <summary>optimization.apply: runs the full rule lifecycle on behalf of the verified client user.</summary>
public sealed class OptimizationApplyHandler(IOptimizationEngine engine) : IpcHandler<RuleRequest>
{
    /// <inheritdoc />
    public override string Operation => IpcOperations.OptimizationApply;

    /// <inheritdoc />
    protected override string? Validate(RuleRequest payload) => RuleRequestValidation.Validate(payload);

    /// <inheritdoc />
    protected override async Task<object?> HandleAsync(RuleRequest payload, IIpcSession session, CancellationToken cancellationToken)
    {
        var record = await engine.ApplyAsync(payload.RuleId, payload.Parameters, session.Client.UserName ?? "unknown", cancellationToken).ConfigureAwait(false);
        await session.SendEventAsync(IpcTopics.OptimizationChanged, record).ConfigureAwait(false);
        return record;
    }
}

/// <summary>optimization.history.</summary>
public sealed class OptimizationHistoryHandler(IOptimizationJournal journal) : IpcHandler<NoPayload>
{
    /// <inheritdoc />
    public override string Operation => IpcOperations.OptimizationHistory;

    /// <inheritdoc />
    protected override async Task<object?> HandleAsync(NoPayload payload, IIpcSession session, CancellationToken cancellationToken) =>
        await journal.ListAsync(500, cancellationToken).ConfigureAwait(false);
}

/// <summary>optimization.restore.</summary>
public sealed class OptimizationRestoreHandler(IOptimizationEngine engine) : IpcHandler<RestoreRequest>
{
    /// <inheritdoc />
    public override string Operation => IpcOperations.OptimizationRestore;

    /// <inheritdoc />
    protected override string? Validate(RestoreRequest payload) => payload.ChangeId == Guid.Empty ? "Invalid change id." : null;

    /// <inheritdoc />
    protected override async Task<object?> HandleAsync(RestoreRequest payload, IIpcSession session, CancellationToken cancellationToken)
    {
        try
        {
            var record = await engine.RollbackAsync(payload.ChangeId, session.Client.UserName ?? "unknown", cancellationToken).ConfigureAwait(false);
            await session.SendEventAsync(IpcTopics.OptimizationChanged, record).ConfigureAwait(false);
            return record;
        }
        catch (KeyNotFoundException)
        {
            throw new IpcOperationException(StormErrorCodes.NotFound, "The change was not found.");
        }
    }
}

/// <summary>optimization.restoreAll.</summary>
public sealed class OptimizationRestoreAllHandler(IOptimizationEngine engine) : IpcHandler<NoPayload>
{
    /// <inheritdoc />
    public override string Operation => IpcOperations.OptimizationRestoreAll;

    /// <inheritdoc />
    protected override async Task<object?> HandleAsync(NoPayload payload, IIpcSession session, CancellationToken cancellationToken) =>
        await engine.RollbackAllAsync(session.Client.UserName ?? "unknown", cancellationToken).ConfigureAwait(false);
}
