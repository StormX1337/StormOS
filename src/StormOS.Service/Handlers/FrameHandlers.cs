using StormOS.Core.Games;
using StormOS.Core.Ipc;
using StormOS.Infrastructure.Ipc;
using StormOS.Performance.Frames;
using StormOS.Security.Validation;

namespace StormOS.Service.Handlers;

/// <summary>frames.start: only running, detected games (or the foreground process) may be captured.</summary>
public sealed class FramesStartHandler(FrameCaptureCoordinator frames) : IpcHandler<FrameCaptureStartRequest>
{
    /// <inheritdoc />
    public override string Operation => IpcOperations.FramesStart;

    /// <inheritdoc />
    protected override string? Validate(FrameCaptureStartRequest payload) =>
        InputValidator.IsProcessId(payload.ProcessId) ? null : "Invalid process id.";

    /// <inheritdoc />
    protected override async Task<object?> HandleAsync(FrameCaptureStartRequest payload, IIpcSession session, CancellationToken cancellationToken)
    {
        var result = await frames.StartAsync(payload.ProcessId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? frames.GetStatus() : throw new IpcOperationException(result.Error.Code, result.Error.Message);
    }
}

/// <summary>frames.stop.</summary>
public sealed class FramesStopHandler(FrameCaptureCoordinator frames) : IpcHandler<NoPayload>
{
    /// <inheritdoc />
    public override string Operation => IpcOperations.FramesStop;

    /// <inheritdoc />
    protected override async Task<object?> HandleAsync(NoPayload payload, IIpcSession session, CancellationToken cancellationToken)
    {
        await frames.StopAsync().ConfigureAwait(false);
        return frames.GetStatus();
    }
}

/// <summary>frames.status.</summary>
public sealed class FramesStatusHandler(FrameCaptureCoordinator frames) : IpcHandler<NoPayload>
{
    /// <inheritdoc />
    public override string Operation => IpcOperations.FramesStatus;

    /// <inheritdoc />
    protected override Task<object?> HandleAsync(NoPayload payload, IIpcSession session, CancellationToken cancellationToken) =>
        Task.FromResult<object?>(frames.GetStatus());
}

/// <summary>games.running.</summary>
public sealed class GamesRunningHandler(IRunningGameDetector detector) : IpcHandler<NoPayload>
{
    /// <inheritdoc />
    public override string Operation => IpcOperations.GamesRunning;

    /// <inheritdoc />
    protected override Task<object?> HandleAsync(NoPayload payload, IIpcSession session, CancellationToken cancellationToken) =>
        Task.FromResult<object?>(new RunningGamesResponse(detector.Running));
}
