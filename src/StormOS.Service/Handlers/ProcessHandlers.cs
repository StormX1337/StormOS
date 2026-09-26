using StormOS.Core.History;
using StormOS.Core.Ipc;
using StormOS.Core.Optimization;
using StormOS.Core.Processes;
using StormOS.Infrastructure.Ipc;
using StormOS.Infrastructure.Logging;
using StormOS.Infrastructure.Paths;
using StormOS.Optimization.Rules.Services;
using StormOS.Security.Validation;
using StormOS.Windows.Services;

namespace StormOS.Service.Handlers;

/// <summary>process.setPriority for processes the user cannot change without elevation.</summary>
public sealed class ProcessSetPriorityHandler(IProcessController controller, IHistoryStore history) : IpcHandler<ProcessPriorityRequest>
{
    /// <inheritdoc />
    public override string Operation => IpcOperations.ProcessSetPriority;

    /// <inheritdoc />
    protected override string? Validate(ProcessPriorityRequest payload) =>
        !InputValidator.IsProcessId(payload.ProcessId) || !Enum.IsDefined(payload.Priority) ? "Invalid process or priority." : null;

    /// <inheritdoc />
    protected override async Task<object?> HandleAsync(ProcessPriorityRequest payload, IIpcSession session, CancellationToken cancellationToken)
    {
        var result = controller.SetPriority(payload.ProcessId, payload.Priority);
        await history.AddEventAsync(new HistoryEvent
        {
            Timestamp = DateTimeOffset.UtcNow,
            Category = HistoryCategory.SystemChange,
            Action = $"Priority set to {payload.Priority} (pid {payload.ProcessId})",
            Result = result.IsSuccess ? EventResult.Success : EventResult.Failed,
            Details = result.IsSuccess ? $"Requested by {session.Client.UserName}" : result.Error.Message,
            Rollback = RollbackStatus.NotApplicable,
        }, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? true : throw new IpcOperationException(result.Error.Code, result.Error.Message);
    }
}

/// <summary>process.terminate for processes the user cannot end without elevation (protected processes are refused).</summary>
public sealed class ProcessTerminateHandler(IProcessController controller, IHistoryStore history) : IpcHandler<ProcessTerminateRequest>
{
    /// <inheritdoc />
    public override string Operation => IpcOperations.ProcessTerminate;

    /// <inheritdoc />
    protected override string? Validate(ProcessTerminateRequest payload) =>
        !InputValidator.IsProcessId(payload.ProcessId) || string.IsNullOrWhiteSpace(payload.ExpectedName) || payload.ExpectedName.Length > 260 ? "Invalid process." : null;

    /// <inheritdoc />
    protected override async Task<object?> HandleAsync(ProcessTerminateRequest payload, IIpcSession session, CancellationToken cancellationToken)
    {
        var result = controller.Terminate(payload.ProcessId, payload.ExpectedName);
        await history.AddEventAsync(new HistoryEvent
        {
            Timestamp = DateTimeOffset.UtcNow,
            Category = HistoryCategory.SystemChange,
            Action = $"Ended {payload.ExpectedName}",
            Result = result.IsSuccess ? EventResult.Success : EventResult.Failed,
            Details = result.IsSuccess ? $"Requested by {session.Client.UserName}" : result.Error.Message,
            Rollback = RollbackStatus.Unavailable,
        }, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? true : throw new IpcOperationException(result.Error.Code, result.Error.Message);
    }
}

/// <summary>services.list with STORM OS assessments.</summary>
public sealed class ServicesListHandler(IServiceConfigurator services, ServiceKnowledgeBase knowledge) : IpcHandler<NoPayload>
{
    /// <inheritdoc />
    public override string Operation => IpcOperations.ServicesList;

    /// <inheritdoc />
    protected override Task<object?> HandleAsync(NoPayload payload, IIpcSession session, CancellationToken cancellationToken) =>
        Task.Run<object?>(() => services.List().Select(s => knowledge.Find(s.Name) is { } k ? s with { Assessment = k.Assessment, Recommendation = k.Recommendation } : s).ToList(), cancellationToken);
}

/// <summary>logs.tail: recent service log lines (secrets are redacted at write time).</summary>
public sealed class LogsTailHandler(IStormPaths paths) : IpcHandler<LogTailRequest>
{
    /// <inheritdoc />
    public override string Operation => IpcOperations.LogsTail;

    /// <inheritdoc />
    protected override string? Validate(LogTailRequest payload) => payload.Lines is < 1 or > 1000 ? "Lines must be between 1 and 1000." : null;

    /// <inheritdoc />
    protected override Task<object?> HandleAsync(LogTailRequest payload, IIpcSession session, CancellationToken cancellationToken)
    {
        var (file, lines) = StormLogging.Tail(paths.Logs, LogCategories.Service, payload.Lines);
        return Task.FromResult<object?>(new LogTailResponse(file, lines));
    }
}

/// <summary>sessions.list.</summary>
public sealed class SessionsListHandler(IHistoryStore history) : IpcHandler<NoPayload>
{
    /// <inheritdoc />
    public override string Operation => IpcOperations.SessionsList;

    /// <inheritdoc />
    protected override async Task<object?> HandleAsync(NoPayload payload, IIpcSession session, CancellationToken cancellationToken) =>
        await history.ListSessionsAsync(limit: 200, cancellationToken: cancellationToken).ConfigureAwait(false);
}

/// <summary>history.metrics: downsampled history (at most 2000 points).</summary>
public sealed class MetricHistoryHandler(IHistoryStore history) : IpcHandler<MetricHistoryRequest>
{
    /// <inheritdoc />
    public override string Operation => IpcOperations.MetricHistory;

    /// <inheritdoc />
    protected override string? Validate(MetricHistoryRequest payload) =>
        payload.To <= payload.From || payload.To - payload.From > TimeSpan.FromDays(7) ? "The range must be positive and at most seven days." : null;

    /// <inheritdoc />
    protected override async Task<object?> HandleAsync(MetricHistoryRequest payload, IIpcSession session, CancellationToken cancellationToken)
    {
        var points = await history.ReadMetricHistoryAsync(payload.From, payload.To, cancellationToken).ConfigureAwait(false);
        var step = Math.Max(1, points.Count / 2000);
        return step == 1 ? points : points.Where((_, i) => i % step == 0).ToList();
    }
}
